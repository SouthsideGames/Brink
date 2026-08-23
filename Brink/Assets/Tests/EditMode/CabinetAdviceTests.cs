using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The cabinet advises where the operator decides, and acts where it does
    /// not (GDD §7.2, §8, §28.1).
    ///
    /// The rule is symmetrical and it is the whole feature: an official is either
    /// running their pillar or advising on it, never both. Delegated, they act
    /// and the operator reads about it in the monthly briefing. Under Direct
    /// Control the operator decides, so the official advises instead — which
    /// finally gives Direct Control an upside beyond raw control, since it costs
    /// Command Points and the official's trust.
    /// </summary>
    public class CabinetAdviceTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3690);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static readonly Pillar[] AdvisedPillars =
        {
            Pillar.Economy, Pillar.Intelligence, Pillar.Diplomacy, Pillar.Government
        };

        void TakeDirectControl(Pillar pillar)
            => state.PlayerCountry.FindOfficial(pillar).mode = ControlMode.DirectControl;

        // ---------- the gate ----------

        [Test]
        public void EveryPillarAdvisesUnderDirectControl()
        {
            foreach (var pillar in AdvisedPillars)
            {
                TakeDirectControl(pillar);
                var advice = CabinetAdvice.For(state, pillar);

                Assert.IsNotNull(advice, $"{pillar} offered no counsel while the operator runs it.");
                Assert.IsNotEmpty(advice.action);
                Assert.IsNotEmpty(advice.rationale,
                    $"{pillar}'s recommendation has no reasoning behind it.");
            }
        }

        [Test]
        public void ADelegatedPillarGivesNoAdvice()
        {
            // They are acting. Recommending an action the official is already
            // taking is noise at best and a suggestion to interfere at worst.
            foreach (var pillar in AdvisedPillars)
            {
                var official = state.PlayerCountry.FindOfficial(pillar);

                official.mode = ControlMode.Autonomous;
                Assert.IsNull(CabinetAdvice.For(state, pillar),
                    $"{pillar} advised while running itself.");

                official.mode = ControlMode.Directed;
                Assert.IsNull(CabinetAdvice.For(state, pillar),
                    $"{pillar} advised while working to an instruction.");
            }
        }

        [Test]
        public void MilitaryFollowsTheSameRule()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            var minister = state.PlayerCountry.FindOfficial(Pillar.Military);

            minister.mode = ControlMode.Autonomous;
            Assert.IsNull(MilitaryAdvice.Recommend(state, confrontation),
                "The military advised while delegated — the rule has to be the same everywhere.");

            minister.mode = ControlMode.DirectControl;
            Assert.IsNotNull(MilitaryAdvice.Recommend(state, confrontation));
        }

        [Test]
        public void AVacantOfficeAdvisesNobody()
        {
            state.PlayerCountry.cabinet.Clear();
            foreach (var pillar in AdvisedPillars)
                Assert.IsNull(CabinetAdvice.For(state, pillar),
                    $"Counsel arrived from a vacant {pillar} office.");
        }

        // ---------- the advice is worth what the official is worth ----------

        [Test]
        public void ReliabilityFollowsCompetence()
        {
            TakeDirectControl(Pillar.Economy);
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);

            official.competence = 92f;
            Assert.Greater(CabinetAdvice.For(state, Pillar.Economy).reliability, 0.85f);

            official.competence = 12f;
            Assert.Less(CabinetAdvice.For(state, Pillar.Economy).reliability, 0.2f);
        }

        [Test]
        public void TheHeaderSaysHowFarToTrustThem()
        {
            TakeDirectControl(Pillar.Diplomacy);
            var official = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            official.competence = 20f;

            string header = CabinetAdvice.Header(state, CabinetAdvice.For(state, Pillar.Diplomacy));

            StringAssert.Contains("competence", header,
                "The operator cannot tell a confident desk from a competent one.");
            StringAssert.Contains(official.displayName, header);
            StringAssert.Contains("decision is yours", header,
                "Advice that does not say it is advice reads as an instruction.");
        }

        [Test]
        public void APoorDeskSometimesReachesForTheWrongInstrument()
        {
            // Not random noise — an incompetent official has real opinions, they
            // are simply not the ones the situation calls for.
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);
            official.mode = ControlMode.DirectControl;

            official.competence = 95f;
            string sound = CabinetAdvice.For(state, Pillar.Economy).action;

            bool everDiffered = false;
            for (int month = 0; month < 24 && !everDiffered; month++)
            {
                official.competence = 8f;
                if (CabinetAdvice.For(state, Pillar.Economy).action != sound) everDiffered = true;
                state.date = state.date.NextMonth();
            }

            Assert.IsTrue(everDiffered,
                "A near-incompetent minister gave identical advice to an expert every month, " +
                "so competence buys nothing where the operator can feel it.");
        }

        [Test]
        public void TheSameQuestionGetsTheSameAnswerTwice()
        {
            // A recommendation that changed on every refresh would be unusable.
            TakeDirectControl(Pillar.Government);
            state.PlayerCountry.FindOfficial(Pillar.Government).competence = 15f;

            string first = CabinetAdvice.For(state, Pillar.Government).action;
            string second = CabinetAdvice.For(state, Pillar.Government).action;

            Assert.AreEqual(first, second);
        }

        // ---------- the advice reads the world ----------

        [Test]
        public void AdviceRespondsToTheSituationRatherThanRepeatingItself()
        {
            TakeDirectControl(Pillar.Economy);
            var player = state.PlayerCountry;

            player.resources.energy = 20f;
            string shortOfEnergy = CabinetAdvice.For(state, Pillar.Economy).action;

            player.resources.energy = 90f;
            player.resources.strategicMaterials = 90f;
            player.economy.inflation = 2f;
            player.economy.growthRate = 4f;
            string comfortable = CabinetAdvice.For(state, Pillar.Economy).action;

            Assert.AreNotEqual(shortOfEnergy, comfortable,
                "The economy desk said the same thing in a shortage and in a boom.");
        }

        [Test]
        public void TheDiplomatNoticesWeAreFightingAlone()
        {
            TakeDirectControl(Pillar.Diplomacy);
            ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);

            var advice = CabinetAdvice.For(state, Pillar.Diplomacy);
            StringAssert.Contains("oalition", advice.action,
                $"At war and alone, the diplomat suggested '{advice.action}'.");
        }

        [Test]
        public void TheGovernmentDeskNoticesAThinChamber()
        {
            TakeDirectControl(Pillar.Government);
            state.PlayerCountry.government.legislativeSupport = 20f;
            state.PlayerCountry.FindOfficial(Pillar.Government).competence = 90f;

            var advice = CabinetAdvice.For(state, Pillar.Government);
            Assert.IsTrue(advice.action.Contains("chamber") || advice.action.Contains("elite"),
                $"With support at 20 the desk suggested '{advice.action}'.");
        }

        // ---------- delegation reports itself ----------

        [Test]
        public void DelegatedOfficialsReportWhatTheyDid()
        {
            // Delegation used to be silent: an official applied their effect and
            // the operator saw some numbers move.
            foreach (var official in state.PlayerCountry.cabinet)
                official.mode = ControlMode.Autonomous;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();

            Assert.Greater(state.cabinetReport.Count, 0,
                "A month passed with a fully delegated cabinet and nobody reported anything.");

            foreach (var line in state.cabinetReport)
            {
                Assert.IsNotEmpty(line.officialName);
                Assert.IsNotEmpty(line.summary);
            }
        }

        [Test]
        public void AnOfficialUnderDirectControlDoesNotReport()
        {
            // They took no action of their own to report on.
            foreach (var official in state.PlayerCountry.cabinet)
                official.mode = ControlMode.DirectControl;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();

            Assert.AreEqual(0, state.cabinetReport.Count,
                "A sidelined official reported work they did not do.");
        }

        [Test]
        public void TheReportDistinguishesOrdersFromJudgement()
        {
            // Two different things for the operator to read: their own decision
            // being carried out, versus somebody else's being made.
            var economy = state.PlayerCountry.FindOfficial(Pillar.Economy);
            var diplomacy = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            foreach (var official in state.PlayerCountry.cabinet)
                official.mode = ControlMode.Autonomous;

            economy.mode = ControlMode.Directed;
            economy.directiveId = "ECO_GROWTH";

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();

            bool sawInstruction = false, sawJudgement = false;
            foreach (var line in state.cabinetReport)
            {
                if (line.pillar == Pillar.Economy)
                {
                    sawInstruction = !line.ownJudgement;
                    StringAssert.Contains("instruction", line.summary);
                }
                if (line.pillar == Pillar.Diplomacy) sawJudgement = line.ownJudgement;
            }

            Assert.IsTrue(sawInstruction, "A directed official was not marked as following orders.");
            Assert.IsTrue(sawJudgement, "An autonomous official was not marked as choosing.");
        }

        [Test]
        public void TheReportIsAboutThisMonthAndNotAnArchive()
        {
            foreach (var official in state.PlayerCountry.cabinet)
                official.mode = ControlMode.Autonomous;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            turns.EndMonth();
            int afterOne = state.cabinetReport.Count;

            for (int month = 0; month < 5; month++) turns.EndMonth();

            Assert.AreEqual(afterOne, state.cabinetReport.Count,
                "The cabinet report accumulated across months. It describes the month just " +
                "resolved; the permanent record is the chronicle.");
        }

        [Test]
        public void ForeignCabinetsDoNotFileIntoOurBriefing()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();

            foreach (var line in state.cabinetReport)
            {
                bool ours = false;
                foreach (var official in state.PlayerCountry.cabinet)
                    if (official.displayName == line.officialName) ours = true;

                Assert.IsTrue(ours,
                    $"'{line.officialName}' is not in our cabinet but reported to our briefing.");
            }
        }

        // ---------- it stays reproducible ----------

        [Test]
        public void ADecadeStaysDeterministic()
        {
            string Fingerprint(int seed)
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);
                for (int month = 0; month < 120; month++) turns.EndMonth();

                var parts = new List<string>();
                foreach (var line in world.cabinetReport)
                    parts.Add($"{line.pillar}:{line.summary}");
                return string.Join("|", parts);
            }

            Assert.AreEqual(Fingerprint(3690), Fingerprint(3690));
        }
    }
}

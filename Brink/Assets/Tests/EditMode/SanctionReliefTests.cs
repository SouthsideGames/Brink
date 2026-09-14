using System.Linq;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Phase C2: a rivalry regime may outlive its war, but not forever.
    ///
    /// The relations arm of <see cref="EconomySystem.SanctionCauseStands"/> is
    /// self-fulfilling — a sanctioned pair settles at
    /// `strategicAlignment − SanctionChill`, so once alignment is below about 38
    /// the pair can never clear the 30 line and the regime runs for the rest of
    /// the save. Measured at Challenging before this change: 345 standing
    /// regimes at month 360, median age 232 months, 93% on pairs that could not
    /// mathematically clear the line, and only 1% on a pair actually at war.
    ///
    /// The repair is in the monthly review only. These tests pin both halves:
    /// an active or recent war still holds a regime in force, and nothing about
    /// what a regime costs, who imposes one, or the player's own measures moves.
    /// </summary>
    public class SanctionReliefTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4242);
            state.difficulty = Difficulty.Challenging;
        }

        [TearDown]
        public void TearDown() { GameLog.MirrorToUnityConsole = true; GameLog.Clear(); }

        /// <summary>A cold pair, so the relations arm of the cause always stands.</summary>
        void MakeCold(string a, string b)
        {
            var r = state.FindRelationship(a, b);
            Assert.IsNotNull(r);
            r.relations = 5f;
            r.strategicAlignment = 10f;   // below 38: can never clear the hostility line
        }

        Sanction Impose(string sender, string target, string cause, int age)
        {
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, sender, target,
                SanctionSeverity.Pressure, cause), "the fixture could not impose its regime");
            var s = state.FindSanction(sender, target);
            Assert.IsNotNull(s);
            s.monthsActive = age;
            return s;
        }

        Confrontation OpenWar(string a, string b)
        {
            var war = ConfrontationSystem.BeginBy(state, a, b,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(war, "the fixture could not open its war");
            war.escalation = EscalationState.LimitedConflict;
            return war;
        }

        /// <summary>A war between the pair, long enough ago to have been outlived.</summary>
        void OldWar(string a, string b)
        {
            var war = OpenWar(a, b);
            war.resolved = true;
            war.monthsActive = 6;
            war.startDate = Rewind(state.date, EconomySystem.SanctionReviewMonths + 120);
        }

        static bool Stands(GameState s, Sanction x)
            => EconomySystem.SanctionCauseStands(s, x.senderId, x.targetId);

        // ---------- 1. an active war holds the regime ----------

        [Test]
        public void ActiveWarRivalrySanctionDoesNotLapse()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 240);
            OpenWar("CHN", "IND");

            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "a regime on a pair currently at war must not become eligible to lapse");
            Assert.IsTrue(Stands(state, s), "the cause itself still stands");
        }

        // ---------- 2. a recent war holds it too ----------

        [Test]
        public void RecentlyEndedWarRivalrySanctionDoesNotLapsePrematurely()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 240);
            var war = OpenWar("CHN", "IND");
            // Ended last month: startDate is now, monthsActive 1, so it ended 0
            // months ago — well inside the cooling period.
            war.monthsActive = 1;
            war.resolved = true;

            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "a regime whose war ended last month must not be eligible yet");
        }

        [Test]
        public void AWarEndedJustInsideTheCoolingPeriodStillHoldsTheRegime()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 240);
            var war = OpenWar("CHN", "IND");
            war.resolved = true;
            war.monthsActive = 1;
            // Pull the start back so the war ended exactly one month short.
            war.startDate = Rewind(state.date, EconomySystem.SanctionReviewMonths);

            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "one month short of the cooling period must still hold the regime");
        }

        static GameDate Rewind(GameDate from, int months)
        {
            int total = from.year * 12 + (from.month - 1) - months;
            return new GameDate(total / 12, total % 12 + 1);
        }

        // ---------- 3. an old regime after sustained peace may lapse ----------

        [Test]
        public void OldRivalrySanctionAfterSustainedPeaceBecomesEligible()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 240);
            var war = OpenWar("CHN", "IND");
            war.resolved = true;
            war.monthsActive = 6;
            war.startDate = Rewind(state.date, EconomySystem.SanctionReviewMonths + 120);

            Assert.IsTrue(Stands(state, s),
                "the fixture is wrong: generic rivalry should still stand here");
            Assert.IsTrue(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "a rivalry regime long past its war must become eligible to lapse");
        }

        // ---------- 4. generic rivalry cannot make a regime immortal ----------

        /// <summary>
        /// A standing grievance with no war behind it is allowed to stand: it is
        /// part of what keeps a bloc's rivals cold, and lifting it let a
        /// befriend-everyone operator reach 15/15 warm friendships.
        /// </summary>
        [Test]
        public void APurelyColdPairWithNoWarBehindItKeepsItsRegime()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 300);

            Assert.IsTrue(Stands(state, s), "the pair is cold, so the cause stands");
            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "a regime with no war behind it has nothing to outlive");
        }

        [Test]
        public void AYoungRivalrySanctionIsNotEligibleHoweverColdThePair()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: EconomySystem.SanctionReviewMonths - 1);

            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "a regime younger than the review period must not be eligible");
        }

        // ---------- 5. repudiation is untouched ----------

        [Test]
        public void RepudiationRegimesAreNotAffected()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "REPUDIATION", age: 300);

            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                "C2 is scoped to rivalry regimes; repudiation must behave exactly as before");
        }

        [Test]
        public void CrisisAndUnknownCauseRegimesAreNotAffected()
        {
            MakeCold("CHN", "IND");
            var crisis = Impose("CHN", "IND", "CRISIS", age: 300);
            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, crisis));

            MakeCold("RUS", "DEU");
            var unknown = Impose("RUS", "DEU", "", age: 300);   // an old save
            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, unknown),
                "an unrecognised or empty cause must not be swept up by the rivalry rule");
        }

        // ---------- 6. player agency ----------

        [Test]
        public void ThePlayersOwnMeasuresAreNeverLiftedForThem()
        {
            var player = state.playerCountryId;
            MakeCold(player, "CHN");
            var s = Impose(player, "CHN", "PLAYER", age: 300);

            // The review skips player-sent regimes before any of this is reached.
            for (int m = 0; m < 120; m++) EconomySystem.MonthlyUpdate(state);

            Assert.IsNotNull(state.FindSanction(player, "CHN"),
                "the operator's own standing measure was lifted without them deciding to");
        }

        [Test]
        public void APlayerCauseRegimeIsNeverEligibleEvenFromAForeignSender()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "PLAYER", age: 300);
            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s));
        }

        // ---------- 7-10. contracts that must not move ----------

        [Test]
        public void SanctionPressureAndWeightAreUnchangedWhileTheRegimeStands()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 240);
            OldWar("CHN", "IND");
            float before = EconomySystem.SanctionPressureOn(state, "IND");
            Assert.Greater(before, 0f, "the fixture applied no pressure, so this proves nothing");

            // Eligibility is a lapse question, never a cost question.
            Assert.IsTrue(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s));
            Assert.AreEqual(before, EconomySystem.SanctionPressureOn(state, "IND"), 0f,
                "asking whether a regime may lapse changed what it costs");
        }

        [Test]
        public void EligibilityIsPureAndDrawsNoRandomNumbers()
        {
            MakeCold("CHN", "IND");
            var s = Impose("CHN", "IND", "RIVALRY", age: 240);
            OldWar("CHN", "IND");

            int sequence = state.actionSequence;
            string sender = s.senderId, target = s.targetId;
            int months = s.monthsActive, sanctions = state.sanctions.Count;

            for (int i = 0; i < 50; i++)
                Assert.IsTrue(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, s),
                    "the predicate is not deterministic");

            Assert.AreEqual(sequence, state.actionSequence, "eligibility consumed the action sequence");
            Assert.AreEqual(months, s.monthsActive, "eligibility mutated the regime");
            Assert.AreEqual(sanctions, state.sanctions.Count, "eligibility added or removed a regime");
            Assert.AreEqual(sender, s.senderId); Assert.AreEqual(target, s.targetId);
        }

        [Test]
        public void ImposingIsUnchanged_TheSharedPredicateWasNotWidened()
        {
            // `SanctionCauseStands` gates the AI's decision to impose and the
            // détente path. C2 must not have touched it.
            MakeCold("CHN", "IND");
            Assert.IsTrue(EconomySystem.SanctionCauseStands(state, "CHN", "IND"),
                "a cold pair must still read as a standing cause for imposing");

            var warm = state.FindRelationship("CHN", "JPN");
            if (warm != null) { warm.relations = 70f; }
            Assert.IsFalse(EconomySystem.SanctionCauseStands(state, "CHN", "JPN"),
                "a warm pair must still read as no cause");
        }

        [Test]
        public void NullsAreSafe()
        {
            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(null, null));
            Assert.IsFalse(EconomySystem.RivalryRegimeHasOutlivedItsWar(state, null));
        }

        // ---------- the rule actually fires in a real world ----------

        // Thirty years of the full pipeline. The horizon is the claim — a rule
        // about regimes outliving their wars cannot be shown in a short run — so
        // the timeout is raised rather than the run shortened, on the same
        // reasoning as the other long-run fixtures. Unity's editor session slows
        // as a partition fills, so the 180s default is not a useful bound here.
        [Test, Timeout(600000)]
        public void OverALongRunTheWorldNoLongerAccumulatesImmortalRivalryRegimes()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int m = 0; m < 360; m++) turns.EndMonth();

            int rivalry = state.sanctions.Count(s => s.cause == "RIVALRY");
            int repudiation = state.sanctions.Count(s => s.cause == "REPUDIATION");
            Assert.Greater(state.sanctions.Count, 0,
                "sanctions vanished from the simulation entirely — this is a recovery repair, not a nerf");
            Assert.Less(rivalry, 30,
                $"rivalry regimes are still accumulating without limit ({rivalry} standing)");
            Assert.Greater(repudiation, 0,
                "repudiation regimes disappeared, so the rule is not as narrow as intended");
        }
    }
}

using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The government pillar's verbs (GDD §13), and the restoring force under
    /// them (GDD §12).
    ///
    /// This pillar graded barely above doing nothing (+0.12 over passive) for two
    /// reasons, and both are structural rather than tuning. It had almost nothing
    /// to do month to month — 26 decisions in a decade against the economy's 344
    /// — and Political Capital had no sink above 7 against a cap of 20, so an
    /// operator who was not spending simply sat at the cap with nothing worth
    /// buying.
    /// </summary>
    public class GovernmentVerbTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5150);
            state.politicalCapital = 20f;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static GameState Run(int seed, int months, Action<GameState, int> eachMonth = null)
        {
            var world = WorldFactory.CreateDebugWorld(seed);
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);
            for (int month = 0; month < months; month++)
            {
                eachMonth?.Invoke(world, month);
                turns.EndMonth();
            }
            return world;
        }

        // ---------- the missing restoring force ----------

        [Test]
        public void StabilityAndUnityCanBeGovernedBackFromABadDecade()
        {
            // These were the only two political stats in the simulation with no
            // restoring force at all. A dozen flat drains pushed them down — war,
            // occupation, inflation, civil conflict, emergency powers — and they
            // recovered only through discrete events. National unity had exactly
            // one repeatable player source in the whole game.
            var world = WorldFactory.CreateDebugWorld(seed: 5150);
            var country = world.PlayerCountry;
            country.stability = 8f;
            country.nationalUnity = 8f;
            country.warExhaustion = 0f;

            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);
            for (int month = 0; month < 90; month++) turns.EndMonth();

            Assert.Greater(country.stability, 25f,
                $"A state left at peace for seven years recovered to {country.stability:F0} " +
                "stability. Nothing was restoring it.");
            Assert.Greater(country.nationalUnity, 25f,
                $"National unity recovered to {country.nationalUnity:F0} over seven peaceful " +
                "years. It could only ever fall.");
        }

        [Test]
        public void NeitherRunsAwayUpwardEither()
        {
            // The other half of a restoring force: it has to hold a level, not
            // simply point the ratchet the other way.
            var world = Run(5150, 240);
            foreach (var country in world.countries)
            {
                Assert.Less(country.stability, 99.5f,
                    $"{country.id} sat at maximum stability, so the target is not binding.");
                Assert.Less(country.nationalUnity, 99.5f,
                    $"{country.id} sat at maximum unity.");
            }
        }

        // ---------- political bargaining ----------

        [Test]
        public void BoughtSupportMovesTheTargetRatherThanTheValue()
        {
            // `legislativeSupport` and `eliteCohesion` both drift toward a
            // computed target every month, so a verb that added to them directly
            // would be erased before the player could feel it — the same trap
            // that made occupation's readiness cost dead code.
            var gov = state.PlayerCountry.government;
            float before = gov.legislativeSupport;

            Assert.IsTrue(GovernmentSystem.BuildPoliticalSupport(state));
            Assert.Greater(gov.brokeredSupport, 0f, "Nothing was bought.");

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 6; month++)
            {
                state.politicalCapital = 20f;
                GovernmentSystem.BuildPoliticalSupport(state);
                turns.EndMonth();
            }

            Assert.Greater(gov.legislativeSupport, before,
                "Six months of bargaining left the chamber exactly where it was. The verb " +
                "is writing into a value that drifts back every tick.");
        }

        [Test]
        public void SupportHasToBeMaintained()
        {
            var gov = state.PlayerCountry.government;
            for (int i = 0; i < 6; i++)
            {
                state.politicalCapital = 20f;
                GovernmentSystem.BuildPoliticalSupport(state);
            }
            float peak = gov.brokeredSupport;
            Assert.Greater(peak, 10f);

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 36; month++) turns.EndMonth();

            Assert.Less(gov.brokeredSupport, peak * 0.4f,
                "Bought support did not decay, so a government buys a compliant chamber once " +
                "and keeps it for the rest of the save.");
        }

        [Test]
        public void BuyingSupportGetsHarderTheMoreOfItYouBuy()
        {
            var gov = state.PlayerCountry.government;

            state.politicalCapital = 20f;
            GovernmentSystem.BuildPoliticalSupport(state);
            float firstGain = gov.brokeredSupport;

            for (int i = 0; i < 8; i++)
            {
                state.politicalCapital = 20f;
                GovernmentSystem.BuildPoliticalSupport(state);
            }
            float beforeLast = gov.brokeredSupport;
            state.politicalCapital = 20f;
            GovernmentSystem.BuildPoliticalSupport(state);
            float lastGain = gov.brokeredSupport - beforeLast;

            Assert.Less(lastGain, firstGain,
                "The tenth concession bought as much as the first. A government could simply " +
                "buy its way to a compliant chamber.");
        }

        [Test]
        public void PatronageBuysSupportWithMoneyAndCostsTheState()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 5000f;
            float institutionsBefore = player.pillars.government;
            float treasuryBefore = player.resources.treasury;

            Assert.IsTrue(GovernmentSystem.DistributePatronage(state));

            Assert.Less(player.resources.treasury, treasuryBefore, "It has to cost money.");
            Assert.Greater(player.government.brokeredSupport, 0f, "It has to buy support.");
            Assert.Less(player.pillars.government, institutionsBefore,
                "Governing this way has to hollow the state out, or it is strictly better " +
                "than bargaining and nobody would ever bargain.");
        }

        [Test]
        public void PatronageIsRefusedWhenTheTreasuryCannotCoverIt()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 0f;
            float pcBefore = state.politicalCapital;

            Assert.IsFalse(GovernmentSystem.DistributePatronage(state));
            Assert.AreEqual(pcBefore, state.politicalCapital, 0.001f,
                "A refused action must not still charge the operator.");
        }

        [Test]
        public void AnInquiryRaisesTheWeakestDeskAndCostsGoodwill()
        {
            var player = state.PlayerCountry;
            Official weakest = null;
            foreach (var official in player.cabinet)
                if (weakest == null || official.competence < weakest.competence) weakest = official;
            float competenceBefore = weakest.competence;

            player.government.brokeredSupport = 40f;
            float supportBefore = player.government.brokeredSupport;

            Assert.IsTrue(GovernmentSystem.LaunchInquiry(state));

            Assert.Greater(weakest.competence, competenceBefore,
                "The point is to raise the desk that is failing, not the one that is not.");
            Assert.Less(player.government.brokeredSupport, supportBefore,
                "Turning the state's scrutiny on itself has to cost the goodwill of whoever " +
                "benefited from the arrangements it disturbs.");
        }

        [Test]
        public void PatronageAndInquiryPullAgainstEachOther()
        {
            // They are the two directions of the same decision, and a pillar
            // whose verbs all point the same way has no decisions in it.
            var player = state.PlayerCountry;
            player.resources.treasury = 5000f;

            float institutions = player.pillars.government;
            GovernmentSystem.DistributePatronage(state);
            float afterPatronage = player.pillars.government;

            state.politicalCapital = 20f;
            GovernmentSystem.LaunchInquiry(state);
            float afterInquiry = player.pillars.government;

            Assert.Less(afterPatronage, institutions);
            Assert.Greater(afterInquiry, afterPatronage);
        }

        // ---------- civic posture ----------

        [Test]
        public void CivicPostureTradesOrderAgainstLegitimacy()
        {
            var gov = state.PlayerCountry.government;

            gov.civicPosture = CivicPosture.Restrictive;
            float restrictiveOrder = GovernmentSystem.StabilityShiftFor(gov);
            float restrictiveUnity = GovernmentSystem.UnityShiftFor(gov);
            float restrictivePlots = GovernmentSystem.ConspiracyRateFor(gov);

            gov.civicPosture = CivicPosture.Open;
            float openOrder = GovernmentSystem.StabilityShiftFor(gov);
            float openUnity = GovernmentSystem.UnityShiftFor(gov);
            float openPlots = GovernmentSystem.ConspiracyRateFor(gov);

            Assert.Greater(restrictiveOrder, openOrder, "Restrictive has to buy order.");
            Assert.Less(restrictiveUnity, openUnity,
                "A society held down coheres less, not more. The appearance of unanimity is " +
                "not the thing.");
            Assert.Less(restrictivePlots, openPlots, "Suppressing plots is the case for it.");
        }

        [Test]
        public void GoverningRestrictivelyCostsMoreInASystemThatVotes()
        {
            // GDD §13's requirement that the government *type* change how power
            // works, applied to the pillar's one standing choice.
            var elective = new GovernmentState
            { type = GovernmentType.PresidentialRepublic, civicPosture = CivicPosture.Restrictive };
            var notElective = new GovernmentState
            { type = GovernmentType.DominantPartyState, civicPosture = CivicPosture.Restrictive };

            Assert.Less(GovernmentSystem.ApprovalShiftFor(elective),
                GovernmentSystem.ApprovalShiftFor(notElective),
                "A system that faces the voters has to pay more for governing restrictively.");
        }

        [Test]
        public void ARestrictiveApparatusCostsSomethingEveryMonth()
        {
            var gov = state.PlayerCountry.government;
            Assert.IsTrue(GovernmentSystem.SetCivicPosture(state, CivicPosture.Restrictive));

            state.politicalCapital = 12f;
            float before = state.politicalCapital;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();

            // Income is added the same month, so compare against a control that
            // is otherwise identical but standing at Standard.
            var control = WorldFactory.CreateDebugWorld(seed: 5150);
            control.politicalCapital = 12f;
            var controlTurns = new TurnManager(control);
            SimulationPipeline.Wire(controlTurns, control);
            controlTurns.EndMonth();

            Assert.Less(state.politicalCapital, control.politicalCapital,
                "Holding a restrictive apparatus has to cost authority every month, or it is " +
                "free order and every government would do it forever.");
            Assert.AreEqual(CivicPosture.Restrictive, gov.civicPosture);
        }

        [Test]
        public void ClosingCivicSpaceActuallySuppressesPlots()
        {
            float ConspiracyAfter(CivicPosture posture)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 5150);
                var country = world.PlayerCountry;

                // Real grievance, so there is something for the posture to bite on.
                country.stability = 25f;
                country.governmentApproval = 25f;
                country.government.civicPosture = posture;

                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);
                for (int month = 0; month < 24; month++)
                {
                    country.stability = Math.Min(country.stability, 25f);
                    country.governmentApproval = Math.Min(country.governmentApproval, 25f);
                    turns.EndMonth();
                }
                return country.government.conspiracyLevel;
            }

            Assert.Less(ConspiracyAfter(CivicPosture.Restrictive), ConspiracyAfter(CivicPosture.Open),
                "If the posture does not change how fast a plot organises, there is no case " +
                "for paying its price.");
        }

        // ---------- succession ----------

        [Test]
        public void APreparedSuccessorArrivesKnowingTheFile()
        {
            var gov = state.PlayerCountry.government;
            for (int i = 0; i < 5; i++)
            {
                state.politicalCapital = 20f;
                GovernmentSystem.GroomSuccessor(state);
            }

            Assert.Greater(gov.successorReadiness, 80f);

            // Force the transition the preparation was for.
            gov.leader.age = 90f;
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            for (int month = 0; month < 240 && gov.successorReadiness > 0f; month++)
            {
                gov.leader.age = 90f;
                turns.EndMonth();
            }

            Assert.AreEqual(0f, gov.successorReadiness, 0.001f,
                "Readiness is consumed by the transition it was built for; it never fired.");
            Assert.Greater(gov.leader.competence, 60f,
                "A prepared succession has to raise the floor on who arrives, or the " +
                "preparation bought nothing.");
        }

        [Test]
        public void PreparationStopsAtFullyReady()
        {
            for (int i = 0; i < 10; i++)
            {
                state.politicalCapital = 20f;
                GovernmentSystem.GroomSuccessor(state);
            }
            state.politicalCapital = 20f;
            float pcBefore = state.politicalCapital;

            Assert.IsFalse(GovernmentSystem.GroomSuccessor(state));
            Assert.AreEqual(pcBefore, state.politicalCapital, 0.001f,
                "A refused action must not still charge the operator.");
        }

        // ---------- consolidating authority ----------

        [Test]
        public void ConsolidatingAuthorityIsPermanentAndCostsTheInstitutions()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            gov.legislativeSupport = 80f;
            state.politicalCapital = 20f;

            Pillar constrained = Pillar.Economy;
            Assert.AreNotEqual(AuthoritySystem.AuthorityLevel.Direct,
                AuthoritySystem.AuthorityOver(state, constrained),
                "Test premise: this pillar is not already ours to command.");

            float supportBefore = gov.legislativeSupport;
            Assert.IsTrue(GovernmentSystem.ConsolidateAuthority(state, constrained));

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.Direct,
                AuthoritySystem.AuthorityOver(state, constrained));
            Assert.Less(gov.legislativeSupport, supportBefore,
                "Reaching for permanent power has to cost standing with whoever gave it up.");

            // Permanence: unlike emergency powers it survives everything.
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 120; month++) turns.EndMonth();

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.Direct,
                AuthoritySystem.AuthorityOver(state, constrained),
                "It is a constitutional change, not a six-month suspension.");
        }

        [Test]
        public void AThinChamberWillNotAmendOurPowers()
        {
            var gov = state.PlayerCountry.government;
            gov.type = GovernmentType.PresidentialRepublic;
            gov.legislativeSupport = 20f;
            state.politicalCapital = 20f;
            float pcBefore = state.politicalCapital;

            Assert.IsFalse(GovernmentSystem.ConsolidateAuthority(state, Pillar.Economy));
            Assert.AreEqual(pcBefore, state.politicalCapital, 0.001f,
                "Refused before the charge, so bargaining first is the answer rather than " +
                "repeatedly paying to be told no.");
        }

        [Test]
        public void ItCannotBeBoughtTwiceForTheSamePillar()
        {
            var gov = state.PlayerCountry.government;
            gov.legislativeSupport = 80f;
            state.politicalCapital = 20f;
            GovernmentSystem.ConsolidateAuthority(state, Pillar.Economy);

            state.politicalCapital = 20f;
            float pcBefore = state.politicalCapital;
            Assert.IsFalse(GovernmentSystem.ConsolidateAuthority(state, Pillar.Economy));
            Assert.AreEqual(pcBefore, state.politicalCapital, 0.001f);
        }

        [Test]
        public void AuthorityIsOperatorCapabilityAndNeverNationalPower()
        {
            // The same rule the skill trees are held to. Consolidating authority
            // changes what the operator may order without asking; it must not
            // make the country stronger.
            var player = state.PlayerCountry;
            player.government.legislativeSupport = 80f;
            state.politicalCapital = 20f;

            float military = player.pillars.military;
            float economy = player.pillars.economy;
            float intelligence = player.pillars.intelligence;
            float diplomacy = player.pillars.diplomacy;
            float gdp = player.economy.gdp;
            float treasury = player.resources.treasury;

            GovernmentSystem.ConsolidateAuthority(state, Pillar.Economy);

            Assert.AreEqual(military, player.pillars.military, 0.001f);
            Assert.AreEqual(economy, player.pillars.economy, 0.001f);
            Assert.AreEqual(intelligence, player.pillars.intelligence, 0.001f);
            Assert.AreEqual(diplomacy, player.pillars.diplomacy, 0.001f);
            Assert.AreEqual(gdp, player.economy.gdp, 0.001f);
            Assert.AreEqual(treasury, player.resources.treasury, 0.001f);
        }

        // ---------- the world gets the same verbs ----------

        [Test]
        public void ForeignGovernmentsUseTheNewInstrumentsToo()
        {
            // The failure this codebase keeps repeating: a verb is added for the
            // player and the world is quietly locked out of it.
            //
            // Swept across several seeds deliberately. A single seed passed this
            // for a while and then broke when an unrelated change shifted the
            // world's trajectory — it was measuring one world's luck rather than
            // whether the capability exists at all.
            bool anyBoughtSupport = false;
            bool anyPrepared = false;
            bool anyPostureChange = false;

            // Sampled every month rather than read at the end. `brokeredSupport`
            // decays 6% a month and `successorReadiness` is consumed by the
            // transition it was built for, so a snapshot after fifteen years
            // measures "did this happen recently", not "can this happen at all".
            void Sample(GameState world, int _)
            {
                foreach (var country in world.countries)
                {
                    if (country.id == world.playerCountryId) continue;
                    if (country.government.brokeredSupport > 0.5f) anyBoughtSupport = true;
                    if (country.government.successorReadiness > 0.5f) anyPrepared = true;
                    if (country.government.civicPosture != CivicPosture.Standard) anyPostureChange = true;
                }
            }

            foreach (int seed in new[] { 7373, 4242, 9090 })
                Run(seed, 180, Sample);

            Assert.IsTrue(anyBoughtSupport,
                "No foreign government bargained for its own support across three fifteen-year runs.");
            Assert.IsTrue(anyPrepared || anyPostureChange,
                "No foreign government prepared a succession or changed how it holds its " +
                "society across three fifteen-year runs.");
        }

        [Test]
        public void ForeignGovernmentsPayForThemLikeAnyoneElse()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 7373);
            var ai = world.FindAI("MEX");
            var mex = world.FindCountry("MEX");
            mex.resources.treasury = 0f;
            ai.politicalCapital = 0f;

            float supportBefore = mex.government.brokeredSupport;

            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);
            for (int month = 0; month < 12; month++)
            {
                mex.resources.treasury = 0f;
                ai.politicalCapital = 0f;
                turns.EndMonth();
            }

            Assert.AreEqual(supportBefore, mex.government.brokeredSupport, 0.01f,
                "A government with no treasury and no political capital bought support anyway.");
        }

        // ---------- the pillar now has something to do ----------

        [Test]
        public void ThereIsAlwaysSomethingWorthSpendingPoliticalCapitalOn()
        {
            // The diagnosis behind all of this: income runs 1.5–3 a month against
            // a cap of 20, and nothing cost more than 7. An operator who was not
            // spending simply sat at the cap.
            Assert.Greater(GovernmentSystem.ConsolidateAuthorityCost,
                GovernmentSystem.EmergencyPowersCost * 1.4f,
                "The large purchase has to be larger than everything that came before it.");
            Assert.Less(GovernmentSystem.ConsolidateAuthorityCost, GameState.PoliticalCapitalCap,
                "And it has to be reachable without the cap making it impossible to save for.");

            // An operator who does nothing *should* sit at the cap — that is what
            // banking means, and a well-run government having political capital
            // to spare is correct rather than a fault. The claim is narrower and
            // more useful: an operator who *is* working always has something
            // worth doing, so the pillar is never idle for want of a verb.
            int idleMonths = 0;
            int actions = 0;

            Run(5150, 120, (w, month) =>
            {
                var gov = w.PlayerCountry.government;
                float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;

                if (w.politicalCapital >= GovernmentSystem.ConsolidateAuthorityCost
                    && GovernmentSystem.ConsolidateAuthority(w, Pillar.Economy)) { actions++; return; }
                if (backing < 75f && GovernmentSystem.BuildPoliticalSupport(w)) { actions++; return; }
                if (GovernmentSystem.GroomSuccessor(w)) { actions++; return; }
                if (GovernmentSystem.LaunchInquiry(w)) { actions++; return; }
                if (GovernmentSystem.PublicMessaging(w)) { actions++; return; }

                idleMonths++;
            });

            Assert.Greater(actions, 90,
                $"Only {actions} government actions were available across 120 months. Before " +
                "the verb list grew, this playstyle managed 26 decisions in a decade.");
            Assert.Less(idleMonths, 24,
                $"{idleMonths} months in a decade with nothing the operator could usefully do.");
        }
    }
}

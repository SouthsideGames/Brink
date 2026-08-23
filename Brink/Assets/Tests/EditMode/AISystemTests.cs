using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AISystemTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>A fully wired world: every system live, as the real game runs it.</summary>
        static TurnManager FullSimulation(GameState state)
        {
            var turns = new TurnManager(state);
            turns.ResolveMonth += CabinetSystem.MonthlyAct;
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += EconomySystem.AgeSanctions;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            return turns;
        }

        static void RunMonths(TurnManager turns, GameState state, int months)
        {
            for (int i = 0; i < months; i++)
            {
                // Answer any crisis so the unattended run never deadlocks.
                while (state.HasOpenCrisis)
                    CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }
        }

        [Test]
        public void Factory_SeedsAIForEveryNonPlayerState()
        {
            var state = WorldFactory.CreateDebugWorld(9001);
            Assert.AreEqual(WorldFactory.Profiles.Length - 1, state.aiStates.Count,
                "Every country except the player is AI-driven.");
            Assert.IsNull(state.FindAI("USA"), "The player is not driven by the AI.");
            Assert.NotNull(state.FindAI("CHN"));
            Assert.NotNull(state.FindAI("RUS"));
            Assert.NotNull(state.FindAI("IND"));
        }

        [Test]
        public void AIProfiles_DifferAcrossCountries()
        {
            var state = WorldFactory.CreateDebugWorld(9002);
            var profiles = new List<float>();
            foreach (var ai in state.aiStates) profiles.Add(ai.profile.aggression);

            bool allIdentical = true;
            for (int i = 1; i < profiles.Count; i++)
                if (System.Math.Abs(profiles[i] - profiles[0]) > 0.01f) allIdentical = false;

            Assert.IsFalse(allIdentical, "Governments must not be identical optimizers (GDD §24.1).");
        }

        [Test]
        public void AI_ActsIndependentlyAndDevelopsItsCountry()
        {
            var state = WorldFactory.CreateDebugWorld(9003);
            var turns = FullSimulation(state);

            var chn = state.FindCountry("CHN");
            float capabilityBefore = chn.pillars.military + chn.pillars.economy
                                     + chn.pillars.diplomacy + chn.pillars.government;

            RunMonths(turns, state, 36);

            float capabilityAfter = chn.pillars.military + chn.pillars.economy
                                    + chn.pillars.diplomacy + chn.pillars.government;

            Assert.Greater(capabilityAfter, capabilityBefore,
                "An AI government should develop its own country over three years.");
            Assert.Greater(state.FindAI("CHN").objectives.Count, 0, "The AI should hold objectives.");
        }

        [Test]
        public void AI_EstablishesItsOwnCollectionNetworks()
        {
            var state = WorldFactory.CreateDebugWorld(9004);
            var turns = FullSimulation(state);

            RunMonths(turns, state, 60);

            int aiNetworks = 0;
            foreach (var network in state.networks)
                if (network.ownerId != "USA") aiNetworks++;

            Assert.Greater(aiNetworks, 0, "AI states should run their own intelligence collection.");
        }

        [Test]
        public void AI_ReadsEstimatesNotTruth()
        {
            var state = WorldFactory.CreateDebugWorld(9005);
            var usa = state.PlayerCountry;
            usa.pillars.military = 90f;

            // No collection yet: the AI cannot see the real figure.
            float uninformed = AISystem.PerceivedStrength(state, "CHN", usa, IntelDomain.Military);
            Assert.That(uninformed, Is.Not.EqualTo(90f).Within(0.001f),
                "Without collection the AI must not read true foreign strength.");

            // With deep collection it converges toward the truth.
            state.networks.Add(new IntelNetwork
            {
                ownerId = "CHN",
                targetId = "USA",
                focus = IntelDomain.Military,
                penetration = 100f
            });
            usa.counterIntel.counterIntelligence = 0f;
            var turns = FullSimulation(state);
            RunMonths(turns, state, 4);

            float informed = AISystem.PerceivedStrength(state, "CHN", usa, IntelDomain.Military);
            Assert.Less(System.Math.Abs(informed - usa.pillars.military), 20f,
                "Good collection should bring the AI's estimate near the truth.");
        }

        [Test]
        public void AI_CanBeDeceivedByPlayerDeception()
        {
            float PerceivedUnderDeception(float deceptionStrength, float bias)
            {
                var state = WorldFactory.CreateDebugWorld(9006);
                var usa = state.PlayerCountry;
                usa.pillars.military = 50f;
                usa.counterIntel.deceptionStrength = deceptionStrength;
                usa.counterIntel.deceptionBias = bias;
                usa.counterIntel.deceptionDomain = IntelDomain.Military;

                // Enough access that the AI holds a usable estimate, but well
                // short of what it takes to see through a strong deception
                // program — the AI deepens this network on its own each month.
                usa.counterIntel.counterIntelligence = 5f;
                state.networks.Add(new IntelNetwork
                {
                    ownerId = "CHN",
                    targetId = "USA",
                    focus = IntelDomain.Military,
                    penetration = 30f
                });

                var turns = FullSimulation(state);
                RunMonths(turns, state, 2);
                return AISystem.PerceivedStrength(state, "CHN", usa, IntelDomain.Military);
            }

            float honest = PerceivedUnderDeception(0f, 0f);
            float inflated = PerceivedUnderDeception(95f, 1f);
            float understated = PerceivedUnderDeception(95f, -1f);

            Assert.Greater(inflated, honest,
                "A deception program should bend what the AI believes about us (GDD §24.1).");
            Assert.Less(understated, honest,
                "Understating capability should make the AI underestimate us.");
        }

        [Test]
        public void AI_BuildsBehavioralAssessmentOfThePlayer()
        {
            var state = WorldFactory.CreateDebugWorld(9007);
            var turns = FullSimulation(state);

            RunMonths(turns, state, 2);
            float peacefulRead = state.FindAI("CHN").playerAssessment.perceivedAggression;

            // The player behaves aggressively, visibly.
            EconomySystem.ImposeSanctionsBy(state, "USA", "CHN", SanctionSeverity.Severe);
            EconomySystem.ImposeSanctionsBy(state, "USA", "RUS", SanctionSeverity.Coercive);
            ConfrontationSystem.BeginBy(state, "USA", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            RunMonths(turns, state, 2);
            float aggressiveRead = state.FindAI("CHN").playerAssessment.perceivedAggression;

            Assert.Greater(aggressiveRead, peacefulRead,
                "Observed patterns should change how the AI reads the player (GDD §24.2).");
            Assert.AreEqual(2, state.FindAI("CHN").playerAssessment.observedEconomicCoercion);
        }

        [Test]
        public void Difficulty_ChangesReasoningQualityNotStats()
        {
            Assert.Less(AISystem.ActionBudget(Difficulty.Standard), AISystem.ActionBudget(Difficulty.Ruthless));
            Assert.Less(AISystem.PlanningHorizon(Difficulty.Standard), AISystem.PlanningHorizon(Difficulty.Ruthless));

            // Higher difficulty must not hand the AI free national power.
            var standard = WorldFactory.CreateDebugWorld(9008);
            standard.difficulty = Difficulty.Standard;
            var ruthless = WorldFactory.CreateDebugWorld(9008);
            ruthless.difficulty = Difficulty.Ruthless;

            Assert.AreEqual(standard.FindCountry("CHN").pillars.military,
                            ruthless.FindCountry("CHN").pillars.military,
                            "Difficulty must not apply hidden stat cheats (GDD §24.3).");
        }

        [Test]
        public void HigherDifficulty_ProducesMoreStrategicActivity()
        {
            int ActivityAt(Difficulty difficulty)
            {
                var state = WorldFactory.CreateDebugWorld(9009);
                state.difficulty = difficulty;
                var turns = FullSimulation(state);
                RunMonths(turns, state, 48);

                int activity = state.networks.Count + state.sanctions.Count
                               + state.treaties.Count + state.confrontations.Count;
                return activity;
            }

            Assert.GreaterOrEqual(ActivityAt(Difficulty.Ruthless), ActivityAt(Difficulty.Standard),
                "A better-reasoning AI should be more strategically active.");
        }

        [Test]
        public void AI_CanOpenAndConcludeItsOwnConfrontations()
        {
            var state = WorldFactory.CreateDebugWorld(9010);
            state.difficulty = Difficulty.Ruthless;

            // Make an AI pair genuinely hostile and mismatched.
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.relations = 5f;
            rusChn.trust = 5f;
            rusChn.threatPerceptionOfB = 90f;
            var rus = state.FindCountry("RUS");
            rus.pillars.military = 95f;
            rus.stability = 80f;
            state.FindAI("RUS").profile.aggression = 95f;
            state.FindAI("RUS").profile.opportunism = 95f;

            var turns = FullSimulation(state);
            RunMonths(turns, state, 120);

            bool aiOnlyConfrontation = false;
            foreach (var confrontation in state.confrontations)
                if (!confrontation.Involves("USA")) aiOnlyConfrontation = true;

            Assert.IsTrue(aiOnlyConfrontation,
                "AI states should be able to confront each other without the player (GDD §17).");
        }

        [Test]
        public void AI_DoesNotStartWarsWhileDomesticallyCollapsing()
        {
            var state = WorldFactory.CreateDebugWorld(9011);
            state.difficulty = Difficulty.Ruthless;

            var chn = state.FindCountry("CHN");
            chn.stability = 5f;
            chn.warExhaustion = 95f;
            var ai = state.FindAI("CHN");
            ai.profile.aggression = 100f;
            ai.profile.opportunism = 100f;

            var turns = FullSimulation(state);
            for (int i = 0; i < 24; i++)
            {
                chn.stability = 5f;        // hold the collapse in place
                chn.warExhaustion = 95f;
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            foreach (var confrontation in state.confrontations)
                Assert.AreNotEqual("CHN", confrontation.initiatorId,
                    "A state in domestic collapse should not launch foreign adventures.");
        }

        [Test]
        public void AIWars_ResolveRatherThanRunForever()
        {
            var state = WorldFactory.CreateDebugWorld(9012);
            state.difficulty = Difficulty.Challenging;

            var confrontation = ConfrontationSystem.BeginBy(state, "RUS", "IND",
                ConfrontationObjective.TerritorialConcession, "IND_IND", PrimaryStrategy.Military);
            Assert.NotNull(confrontation);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, "RUS");

            var turns = FullSimulation(state);
            RunMonths(turns, state, 180);

            Assert.IsTrue(confrontation.resolved,
                "A war between AI states must eventually reach a settlement (GDD §26).");
            Assert.IsNotEmpty(confrontation.outcomeSummary);
        }

        [Test]
        public void AIWar_AppliesRealCostsToBothSides()
        {
            var state = WorldFactory.CreateDebugWorld(9013);
            var rus = state.FindCountry("RUS");
            var ind = state.FindCountry("IND");
            float rusTreasury = rus.resources.treasury;
            float indTreasury = ind.resources.treasury;

            var confrontation = ConfrontationSystem.BeginBy(state, "RUS", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, "RUS");

            var turns = FullSimulation(state);
            RunMonths(turns, state, 12);

            Assert.Greater(confrontation.initiatorWarExhaustion, 0f);
            Assert.Greater(confrontation.defenderWarExhaustion, 0f);
            Assert.Less(rus.resources.treasury, rusTreasury);
            Assert.Less(ind.resources.treasury, indTreasury);
        }

        [Test]
        public void PlayerIsNotified_WhenAIMovesAgainstThem()
        {
            var state = WorldFactory.CreateDebugWorld(9014);
            ConfrontationSystem.BeginBy(state, "CHN", "USA",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            bool flashed = false;
            foreach (var notification in state.notifications)
                if (notification.priority == NotificationClass.Flash &&
                    notification.title.Contains("AGAINST US")) flashed = true;

            Assert.IsTrue(flashed, "An AI moving against the player is FLASH traffic.");
        }

        [Test]
        public void UnattendedWorld_RunsThirtyYearsAndStaysCoherent()
        {
            var state = WorldFactory.CreateDebugWorld(9015);
            state.difficulty = Difficulty.Challenging;
            var turns = FullSimulation(state);

            RunMonths(turns, state, 360); // 30 years, nobody at the terminal

            Assert.AreEqual(360, state.date.MonthsSince(state.startDate));

            foreach (var country in state.countries)
            {
                Assert.Greater(country.economy.gdp, 0f, $"{country.id} economy collapsed to nothing.");
                Assert.GreaterOrEqual(country.governmentApproval, 0f);
                Assert.LessOrEqual(country.governmentApproval, 100f);
                Assert.GreaterOrEqual(country.pillars.military, 0f);
                Assert.LessOrEqual(country.pillars.military, 100f);
                Assert.IsNotEmpty(country.government.leader.name);
                Assert.GreaterOrEqual(country.resources.energy, 0f);
                Assert.LessOrEqual(country.resources.energy, 100f);
            }

            // The world should have generated its own history.
            Assert.Greater(state.chronicle.Count, 20, "Thirty unattended years should produce a chronicle.");

            foreach (var location in state.locations)
                Assert.NotNull(state.FindCountry(location.ownerId), "Every location must have a real owner.");
        }

        [Test]
        public void UnattendedWorld_IsDeterministicPerSeed()
        {
            string Run(int seed)
            {
                var state = WorldFactory.CreateDebugWorld(seed);
                state.difficulty = Difficulty.Challenging;
                var turns = FullSimulation(state);
                RunMonths(turns, state, 120);
                return SaveSystem.ToJson(state);
            }

            Assert.AreEqual(Run(2468), Run(2468));
        }

        [Test]
        public void AIState_SurvivesSaveRoundTrip()
        {
            var state = WorldFactory.CreateDebugWorld(9016);
            state.difficulty = Difficulty.Ruthless;
            var turns = FullSimulation(state);
            RunMonths(turns, state, 24);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(Difficulty.Ruthless, loaded.difficulty);
            Assert.AreEqual(state.aiStates.Count, loaded.aiStates.Count);

            var original = state.FindAI("CHN");
            var restored = loaded.FindAI("CHN");
            Assert.AreEqual(original.profile.aggression, restored.profile.aggression);
            Assert.AreEqual(original.objectives.Count, restored.objectives.Count);
            Assert.AreEqual(original.playerAssessment.perceivedAggression,
                            restored.playerAssessment.perceivedAggression);
        }
    }
}

using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class ExerciseSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1515);
            turns = new TurnManager(state);
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            state.commandPoints.current = 40;
            WarmRelationsWith("IND");
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void WarmRelationsWith(string partnerId)
        {
            var relationship = state.FindRelationship(state.playerCountryId, partnerId);
            relationship.relations = 75f;
            relationship.trust = 70f;
            relationship.strategicAlignment = 70f;
            relationship.threatPerceptionOfA = 5f;
            relationship.threatPerceptionOfB = 5f;
        }

        [Test]
        public void Exercises_RequireACooperativePartner()
        {
            var cold = state.FindRelationship(state.playerCountryId, "CHN");
            cold.relations = 10f; cold.trust = 10f; cold.strategicAlignment = 10f;

            Assert.IsFalse(ExerciseSystem.CanExerciseWith(state, "CHN", out string reason));
            Assert.IsNotEmpty(reason);
            Assert.IsNull(ExerciseSystem.Conduct(state, turns, "CHN", ExerciseScale.Standard, ExerciseFocus.Combined));

            Assert.IsTrue(ExerciseSystem.CanExerciseWith(state, "IND", out _));
        }

        [Test]
        public void Exercises_CannotBeHeldWithAnActiveOpponent()
        {
            WarmRelationsWith("CHN");
            ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            Assert.IsFalse(ExerciseSystem.CanExerciseWith(state, "CHN", out string reason));
            StringAssert.Contains("confrontation", reason.ToLowerInvariant());
        }

        [Test]
        public void Exercise_CostsCommandPointsAndTreasury()
        {
            int cpBefore = state.commandPoints.current;
            float treasuryBefore = state.PlayerCountry.resources.treasury;

            var record = ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Full, ExerciseFocus.Naval);

            Assert.NotNull(record);
            Assert.AreEqual(cpBefore - ExerciseSystem.CostFor(ExerciseScale.Full), state.commandPoints.current);
            Assert.Less(state.PlayerCountry.resources.treasury, treasuryBefore);
        }

        [Test]
        public void Exercise_BuildsReadinessInteroperabilityAndTrust()
        {
            var player = state.PlayerCountry;
            player.military.ground.readiness = 50f;
            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            float relationsBefore = relationship.relations;
            float trustBefore = relationship.trust;

            var record = ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Standard, ExerciseFocus.Ground);

            Assert.Greater(player.military.ground.readiness, 50f);
            Assert.Greater(relationship.interoperability, 0f);
            Assert.Greater(relationship.relations, relationsBefore);
            Assert.Greater(relationship.trust, trustBefore);
            Assert.Greater(record.readinessGained, 0f);
        }

        [Test]
        public void LosingAnExerciseStillTeaches()
        {
            // Stack the deck so our side comes off worse.
            var player = state.PlayerCountry;
            player.military.naval.strength = 1f;
            player.military.naval.readiness = 1f;
            var partner = state.FindCountry("IND");
            partner.military.naval.strength = 100f;
            partner.military.naval.readiness = 100f;
            partner.military.naval.supply = 100f;

            float readinessBefore = player.military.naval.readiness;
            var record = ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Full, ExerciseFocus.Naval);

            Assert.IsFalse(record.weOutperformed, "Precondition: we should have lagged.");
            Assert.Greater(player.military.naval.readiness, readinessBefore,
                "A poor showing must still improve readiness (GDD §15.3).");
            Assert.Greater(record.readinessGained, 0f);
            StringAssert.Contains("lagged", record.lesson.ToLowerInvariant());
        }

        [Test]
        public void DeeperParticipation_TrainsMoreButExposesMore()
        {
            ExerciseRecord Run(ExerciseScale scale)
            {
                var sim = WorldFactory.CreateDebugWorld(1515);
                var simTurns = new TurnManager(sim);
                sim.commandPoints.current = 40;
                var relationship = sim.FindRelationship(sim.playerCountryId, "IND");
                relationship.relations = 75f; relationship.trust = 70f; relationship.strategicAlignment = 70f;
                relationship.threatPerceptionOfA = 5f; relationship.threatPerceptionOfB = 5f;
                return ExerciseSystem.Conduct(sim, simTurns, "IND", scale, ExerciseFocus.Combined);
            }

            var limited = Run(ExerciseScale.Limited);
            var full = Run(ExerciseScale.Full);

            Assert.Greater(full.interoperabilityGained, limited.interoperabilityGained);
            Assert.Greater(full.exposureIncurred, limited.exposureIncurred,
                "Depth is bought with exposure (GDD §15.3).");
        }

        [Test]
        public void Exercise_ImprovesPartnerCollectionAgainstUs()
        {
            Assert.IsNull(state.FindNetwork("IND", state.playerCountryId));

            ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Full, ExerciseFocus.Combined);

            var theirNetwork = state.FindNetwork("IND", state.playerCountryId);
            Assert.NotNull(theirNetwork, "A partner learns about us by exercising with us.");
            Assert.Greater(theirNetwork.penetration, 0f);
        }

        [Test]
        public void DoctrineKnowledge_OutlivesTheFriendship()
        {
            ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Full, ExerciseFocus.Combined);
            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            float familiarity = relationship.doctrineFamiliarity;
            Assert.Greater(familiarity, 0f);

            // The relationship collapses entirely.
            relationship.relations = 0f;
            relationship.trust = 0f;
            relationship.strategicAlignment = 0f;
            for (int i = 0; i < 24; i++) turns.EndMonth();

            Assert.Greater(relationship.doctrineFamiliarity, 0f,
                "Former partners retain knowledge of each other's doctrine (GDD §15.3).");
        }

        [Test]
        public void FamiliarityWithAFormerPartner_AidsOperationsAgainstThem()
        {
            float SuccessOddsProxy(float familiarity)
            {
                var sim = WorldFactory.CreateDebugWorld(2020);
                var simTurns = new TurnManager(sim);
                var relationship = sim.FindRelationship("USA", "IND");
                relationship.doctrineFamiliarity = familiarity;

                var confrontation = ConfrontationSystem.BeginBy(sim, "USA", "IND",
                    ConfrontationObjective.TerritorialConcession, "IND_IND", PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(sim, confrontation, EscalationState.LimitedConflict, "USA");

                var record = ConfrontationSystem.LaunchOperationBy(sim, confrontation, "USA", "IND_IND",
                    OperationType.Assault, new OperationDirective());
                return record.defenderLosses;
            }

            // Same seed, same forces: knowing their doctrine should bite harder.
            Assert.GreaterOrEqual(SuccessOddsProxy(90f), SuccessOddsProxy(0f));
        }

        [Test]
        public void Interoperability_StrengthensCoalitionContribution()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var coalition = new Coalition
            {
                id = "C1",
                leaderId = state.playerCountryId,
                confrontationId = confrontation.id,
                targetId = "CHN"
            };
            coalition.memberIds.Add(state.playerCountryId);
            coalition.memberIds.Add("IND");
            state.coalitions.Add(coalition);

            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            relationship.interoperability = 0f;
            float untrained = DiplomacySystem.CoalitionStrength(state, confrontation);

            relationship.interoperability = 100f;
            float trained = DiplomacySystem.CoalitionStrength(state, confrontation);

            Assert.Greater(trained, untrained,
                "Forces that have trained together contribute more than forces merely present.");
        }

        [Test]
        public void Exercises_HaveACooldownPerPartner()
        {
            Assert.NotNull(ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Standard, ExerciseFocus.Ground));

            Assert.IsFalse(ExerciseSystem.CanExerciseWith(state, "IND", out string reason),
                "A partner should not run back-to-back major exercises.");
            StringAssert.Contains("cycle", reason.ToLowerInvariant());
            Assert.Greater(ExerciseSystem.CooldownRemaining(state, "IND"), 0);

            state.commandPoints.current = 40;
            Assert.IsNull(ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Standard, ExerciseFocus.Ground));

            for (int i = 0; i < ExerciseSystem.CooldownMonths; i++)
            {
                state.commandPoints.current = 40;
                turns.EndMonth();
            }

            Assert.AreEqual(0, ExerciseSystem.CooldownRemaining(state, "IND"));
            Assert.IsTrue(ExerciseSystem.CanExerciseWith(state, "IND", out _),
                "The cycle should reopen after the cooldown.");
        }

        [Test]
        public void Cooldown_IsTrackedPerPartnerNotGlobally()
        {
            WarmRelationsWith("RUS");
            ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Limited, ExerciseFocus.Air);

            Assert.Greater(ExerciseSystem.CooldownRemaining(state, "IND"), 0);
            Assert.AreEqual(0, ExerciseSystem.CooldownRemaining(state, "RUS"),
                "Exercising with one partner should not block another.");
            Assert.IsTrue(ExerciseSystem.CanExerciseWith(state, "RUS", out _));
        }

        [Test]
        public void Exercises_SurviveSaveRoundTrip()
        {
            ExerciseSystem.Conduct(state, turns, "IND", ExerciseScale.Standard, ExerciseFocus.Air);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(state.exercises.Count, loaded.exercises.Count);
            Assert.AreEqual("IND", loaded.exercises[0].partnerId);
            Assert.AreEqual(ExerciseFocus.Air, loaded.exercises[0].focus);

            var original = state.FindRelationship("USA", "IND");
            var restored = loaded.FindRelationship("USA", "IND");
            Assert.AreEqual(original.interoperability, restored.interoperability);
            Assert.AreEqual(original.doctrineFamiliarity, restored.doctrineFamiliarity);
        }
    }
}

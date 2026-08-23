using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class MilitarySystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4040);
            turns = new TurnManager(state);
            // NARROW PIPELINE: upkeep and the confrontation tick only, here and
            // in the per-test worlds below — the AI, crisis, diplomacy and
            // government ticks are omitted because these cases set the garrison,
            // escalation, war support, momentum and treasury they need directly
            // and then assert MilitarySystem's and ConfrontationSystem's own
            // arithmetic, and a thinking world would move the very figures the
            // assertions pin (an AI settling the war would end the long-war
            // exhaustion case outright). Note the multi-year sustainment cases
            // top up treasury by hand precisely because the economy tick is not
            // here to refill it.
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Factory_BuildsForcesAndMap()
        {
            Assert.Greater(state.locations.Count, 30, "Every country needs meaningful locations.");
            foreach (var country in state.countries)
            {
                Assert.Greater(country.military.ground.strength, 0f);
                Assert.Greater(country.military.TotalPower, 0f);
            }
            var lane = state.FindLocation("CONTESTED_LANE");
            Assert.NotNull(lane);
            Assert.AreEqual(LocationType.Chokepoint, lane.type);
            Assert.IsFalse(lane.IsOccupied);
        }

        [Test]
        public void EffectivePower_ScalesWithReadinessAndSupply()
        {
            var force = new BranchForce { strength = 100, readiness = 100, supply = 100 };
            float best = force.EffectivePower;
            force.readiness = 0;
            float noReadiness = force.EffectivePower;
            force.readiness = 100; force.supply = 0;
            float noSupply = force.EffectivePower;

            Assert.Greater(best, noReadiness);
            Assert.Greater(best, noSupply);
            Assert.Greater(noReadiness, 0f, "Some capacity remains even unready.");
        }

        [Test]
        public void MonthlyUpkeep_AlertPostureRaisesReadinessAndCostsTreasury()
        {
            var player = state.PlayerCountry;
            player.military.alertPosture = true;
            player.military.ground.readiness = 40f;
            float treasuryBefore = player.resources.treasury;

            for (int i = 0; i < 6; i++) turns.EndMonth();

            Assert.Greater(player.military.ground.readiness, 40f);
            Assert.Less(player.resources.treasury, treasuryBefore);
        }

        [Test]
        public void Begin_CostsCPAndOpensAtTension()
        {
            int cpBefore = state.commandPoints.current;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);

            Assert.NotNull(confrontation);
            Assert.AreEqual(cpBefore - ConfrontationSystem.OpenCost, state.commandPoints.current);
            Assert.AreEqual(EscalationState.Tension, confrontation.escalation);
            Assert.AreSame(confrontation, state.ActiveConfrontation);
        }

        [Test]
        public void Begin_RefusesSecondConcurrentConfrontation()
        {
            ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            var second = ConfrontationSystem.Begin(state, turns, "USA", "RUS",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            Assert.IsNull(second);
            Assert.AreEqual(1, state.confrontations.Count);
        }

        [Test]
        public void Escalation_SkippingLevelsCostsPremium()
        {
            var confrontation = OpenPassConfrontation();

            // One step: Tension -> Crisis costs 1 CP.
            state.commandPoints.current = 10;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.Crisis);
            Assert.AreEqual(9, state.commandPoints.current);

            // Two steps at once: 2 CP + 1 premium = 3 CP, plus political cost.
            var player = state.PlayerCountry;
            float supportBefore = player.warSupport;
            state.commandPoints.current = 10;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.TotalWar);

            Assert.AreEqual(7, state.commandPoints.current);
            Assert.Less(player.warSupport, supportBefore, "Abrupt escalation should cost political support.");
            Assert.IsTrue(player.military.alertPosture);
        }

        [Test]
        public void DeEscalation_CanSkipLevelsWithoutPremium()
        {
            var confrontation = OpenPassConfrontation();
            state.commandPoints.current = 20;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.TotalWar);

            int cpBefore = state.commandPoints.current;
            Assert.IsTrue(ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.Peace));
            Assert.AreEqual(EscalationState.Peace, confrontation.escalation);
            Assert.AreEqual(cpBefore, state.commandPoints.current, "De-escalation should not cost CP.");
        }

        /// <summary>
        /// GDD §18.1 is explicit that escalation states "describe the situation;
        /// they do not hard-gate actions". This previously refused the order
        /// outright. The order is now allowed, priced for its abruptness, and the
        /// act itself carries the confrontation up to Limited Conflict.
        /// </summary>
        [Test]
        public void OperationsBelowLimitedConflict_AreDearerAndEscalateByThemselves()
        {
            var confrontation = OpenPassConfrontation();
            Assume.That(confrontation.escalation, Is.LessThan(EscalationState.LimitedConflict));

            state.commandPoints.current = 20;
            int cpBefore = state.commandPoints.current;
            var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                "CONTESTED_LANE", OperationType.Assault, new OperationDirective());

            Assert.IsNotNull(record, "Escalation must not gate the order.");
            Assert.Greater(cpBefore - state.commandPoints.current, 2,
                "Opening fire from below Limited Conflict costs more than a routine operation.");
            Assert.GreaterOrEqual(confrontation.escalation, EscalationState.LimitedConflict);
        }

        [Test]
        public void Assault_SuccessTransfersOwnershipAndRecordsHistory()
        {
            var confrontation = OpenPassConfrontation();
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.LimitedConflict);

            var pass = state.FindLocation("CONTESTED_LANE");
            pass.garrison = 1f;          // near-undefended
            pass.defenseValue = 0f;
            var korval = state.FindCountry("CHN");
            korval.military.ground.strength = 1f;
            korval.military.air.strength = 1f;
            korval.military.naval.strength = 1f;

            OperationRecord success = null;
            for (int i = 0; i < 6 && success == null; i++)
            {
                state.commandPoints.current = 40;
                var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                    "CONTESTED_LANE", OperationType.Assault, new OperationDirective());
                if (record != null && record.success) success = record;
            }

            Assert.NotNull(success, "Overwhelming odds should produce a capture within several attempts.");
            Assert.AreEqual("USA", pass.ownerId);
            Assert.IsTrue(pass.IsOccupied);
            Assert.Greater(confrontation.operations.Count, 0);
            Assert.Greater(confrontation.momentum, 0f);
        }

        [Test]
        public void Operations_CostForceTreasuryAndManpower()
        {
            var confrontation = OpenPassConfrontation();
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.LimitedConflict);

            var player = state.PlayerCountry;
            float treasuryBefore = player.resources.treasury;
            float manpowerBefore = player.resources.manpower;
            float strengthBefore = player.military.ground.strength;

            ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                "CONTESTED_LANE", OperationType.Assault, new OperationDirective());

            Assert.Less(player.resources.treasury, treasuryBefore);
            Assert.Less(player.resources.manpower, manpowerBefore);
            Assert.Less(player.military.ground.strength, strengthBefore);
            Assert.Greater(confrontation.initiatorWarExhaustion, 0f);
        }

        [Test]
        public void Directive_CivilianRiskLimitControlsCivilianHarm()
        {
            float HarmWithLimit(float limit)
            {
                var simState = WorldFactory.CreateDebugWorld(seed: 31);
                var simTurns = new TurnManager(simState);
                var confrontation = ConfrontationSystem.Begin(simState, simTurns, "USA", "CHN",
                    ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
                simState.commandPoints.current = 40;
                ConfrontationSystem.SetEscalation(simState, simTurns, confrontation, EscalationState.LimitedConflict);
                simState.commandPoints.current = 40;

                ConfrontationSystem.LaunchOperation(simState, simTurns, confrontation, "CONTESTED_LANE",
                    OperationType.Assault, new OperationDirective { civilianRiskLimit = limit, speedPriority = 80f });
                return confrontation.civilianHarmTotal;
            }

            Assert.Less(HarmWithLimit(0f), HarmWithLimit(100f));
        }

        [Test]
        public void Withdraw_CostsMomentumButNotForces()
        {
            var confrontation = OpenPassConfrontation();
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.LimitedConflict);
            state.commandPoints.current = 40;

            float strengthBefore = state.PlayerCountry.military.ground.strength;
            var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                "CONTESTED_LANE", OperationType.Withdraw, new OperationDirective());

            Assert.NotNull(record);
            Assert.IsTrue(record.success);
            Assert.AreEqual(strengthBefore, state.PlayerCountry.military.ground.strength, 0.001f);
            Assert.Less(confrontation.momentum, 0f);
        }

        [Test]
        public void Settlement_RejectedWhenOpponentStillPrefersResistance()
        {
            var confrontation = OpenPassConfrontation();
            var korval = state.FindCountry("CHN");
            korval.warSupport = 95f;
            korval.pillars.government = 90f;

            Assert.IsFalse(ConfrontationSystem.ProposeSettlement(state, confrontation));
            Assert.IsFalse(confrontation.resolved, "Rejected terms must not end the confrontation.");
        }

        [Test]
        public void Settlement_AcceptedWhenLeverageIsOverwhelming_TransfersObjective()
        {
            var confrontation = OpenPassConfrontation();
            var pass = state.FindLocation("CONTESTED_LANE");
            pass.ownerId = "USA"; // possession is leverage

            var korval = state.FindCountry("CHN");
            korval.warSupport = 0f;
            korval.pillars.government = 10f;
            confrontation.defenderWarExhaustion = 80f;
            confrontation.momentum = 50f;

            Assert.IsTrue(ConfrontationSystem.ProposeSettlement(state, confrontation));
            Assert.IsTrue(confrontation.resolved);
            Assert.AreEqual("USA", pass.ownerId);
            Assert.IsNull(state.ActiveConfrontation);
            Assert.IsFalse(state.PlayerCountry.military.alertPosture, "Settlement should stand the force down.");
        }

        [Test]
        public void Capital_ObjectivesAreResistedFarHarder()
        {
            var confrontation = OpenPassConfrontation();
            var korval = state.FindCountry("CHN");
            korval.warSupport = 0f;
            korval.pillars.government = 10f;
            confrontation.defenderWarExhaustion = 60f;
            confrontation.momentum = 40f;

            Assert.IsTrue(ConfrontationSystem.OpponentWouldAccept(state, confrontation),
                "Pass objective should be acceptable under this pressure.");

            confrontation.objectiveLocationId = "CHN_CAP";
            Assert.IsFalse(ConfrontationSystem.OpponentWouldAccept(state, confrontation),
                "Demanding the capital must be resisted far harder.");
        }

        [Test]
        public void Concede_EndsConfrontationWithPoliticalCost()
        {
            var confrontation = OpenPassConfrontation();
            var player = state.PlayerCountry;
            float approvalBefore = player.governmentApproval;

            Assert.IsTrue(ConfrontationSystem.ProposeSettlement(state, confrontation, concedeInstead: true));
            Assert.IsTrue(confrontation.resolved);
            Assert.Less(player.governmentApproval, approvalBefore);
            Assert.AreEqual("CHN", state.FindLocation("CONTESTED_LANE").ownerId, "Conceding must not award the objective.");
        }

        [Test]
        public void LongWar_AccumulatesExhaustionAndDrainsTreasury()
        {
            var confrontation = OpenPassConfrontation();
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.TotalWar);

            var player = state.PlayerCountry;
            float treasuryBefore = player.resources.treasury;
            float supportBefore = player.warSupport;

            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.Greater(confrontation.initiatorWarExhaustion, 10f);
            Assert.Less(player.resources.treasury, treasuryBefore);
            Assert.Less(player.warSupport, supportBefore, "Long wars erode public support.");
            Assert.AreEqual(12, confrontation.monthsActive);
        }

        [Test]
        public void Confrontation_SurvivesSaveRoundTrip()
        {
            var confrontation = OpenPassConfrontation();
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.LimitedConflict);
            ConfrontationSystem.LaunchOperation(state, turns, confrontation, "CONTESTED_LANE",
                OperationType.Raid, new OperationDirective());

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var loadedConfrontation = loaded.ActiveConfrontation;

            Assert.NotNull(loadedConfrontation);
            Assert.AreEqual(confrontation.escalation, loadedConfrontation.escalation);
            Assert.AreEqual(confrontation.operations.Count, loadedConfrontation.operations.Count);
            Assert.AreEqual(state.locations.Count, loaded.locations.Count);
            Assert.AreEqual(state.PlayerCountry.military.ground.strength, loaded.PlayerCountry.military.ground.strength);
        }

        [Test]
        public void Simulation_IsDeterministicPerSeed()
        {
            GameState Run(int seed)
            {
                var simState = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(simState);
                simTurns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
                simTurns.ResolveMonth += ConfrontationSystem.MonthlyTick;

                var confrontation = ConfrontationSystem.Begin(simState, simTurns, "USA", "CHN",
                    ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
                simState.commandPoints.current = 40;
                ConfrontationSystem.SetEscalation(simState, simTurns, confrontation, EscalationState.LimitedConflict);

                for (int i = 0; i < 10; i++)
                {
                    simState.commandPoints.current = 40;
                    ConfrontationSystem.LaunchOperation(simState, simTurns, confrontation, "CONTESTED_LANE",
                        OperationType.Assault, new OperationDirective());
                    simTurns.EndMonth();
                }
                return simState;
            }

            Assert.AreEqual(SaveSystem.ToJson(Run(88)), SaveSystem.ToJson(Run(88)));
        }

        /// <summary>
        /// Holding a posture must cost sustainment without destroying the force.
        /// An earlier build subtracted supply every month with no resupply path
        /// above Peacetime, so a state that simply stayed alert hollowed out its
        /// own army by standing still — and the military playstyle graded below
        /// doing nothing because of it.
        /// </summary>
        [Test]
        public void HoldingAPosture_SettlesTheForceLowerButDoesNotEmptyIt()
        {
            var player = state.PlayerCountry;
            state.commandPoints.current = 20;
            MilitarySystem.SetPosture(state, turns, MilitaryPosture.Alert);

            for (int i = 0; i < 60; i++) turns.EndMonth();
            float alertSupply = player.military.ground.supply;

            Assert.Greater(alertSupply, 25f,
                "Five years at alert must not leave the force with nothing to fight on.");

            state.commandPoints.current = 20;
            MilitarySystem.SetPosture(state, turns, MilitaryPosture.Peacetime);
            for (int i = 0; i < 60; i++) turns.EndMonth();

            Assert.Greater(player.military.ground.supply, alertSupply,
                "Standing down lets sustainment recover — that is the point of the choice.");
        }

        [Test]
        public void AForwardPosture_CostsMoreSustainmentThanAlert()
        {
            float SettledSupplyAt(MilitaryPosture posture)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 4040);
                var manager = new TurnManager(world);
                manager.ResolveMonth += MilitarySystem.MonthlyUpkeep;
                world.PlayerCountry.resources.treasury = 500000f;
                // Set directly: this measures the upkeep model, not the command
                // gate (Forward needs the ForwardBasing skill to authorize).
                world.PlayerCountry.military.posture = posture;
                for (int i = 0; i < 48; i++) manager.EndMonth();
                return world.PlayerCountry.military.ground.supply;
            }

            Assert.Less(SettledSupplyAt(MilitaryPosture.Forward), SettledSupplyAt(MilitaryPosture.Alert));
        }

        [Test]
        public void LogisticsInvestment_RaisesWhereTheForceSettles()
        {
            float SettledSupplyWithLogistics(float logistics)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 4040);
                var manager = new TurnManager(world);
                manager.ResolveMonth += MilitarySystem.MonthlyUpkeep;
                world.PlayerCountry.military.logistics = logistics;
                world.PlayerCountry.resources.treasury = 500000f;
                world.PlayerCountry.military.posture = MilitaryPosture.Alert;
                for (int i = 0; i < 24; i++) manager.EndMonth();
                return world.PlayerCountry.military.ground.supply;
            }

            Assert.Greater(SettledSupplyWithLogistics(90f), SettledSupplyWithLogistics(20f),
                "Logistics is what lets a country hold a posture without hollowing out (GDD §19).");
        }

        Confrontation OpenPassConfrontation()
        {
            return ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
        }
    }
}

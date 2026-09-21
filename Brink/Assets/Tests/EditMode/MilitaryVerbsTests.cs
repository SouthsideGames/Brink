using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using System.Linq;
using System.Text.RegularExpressions;
using Brink.UI;
using Brink.UI.Views;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    /// <summary>
    /// The peacetime military decisions (GDD §19): posture, procurement,
    /// logistics and doctrine. Added because validation showed a military
    /// operator had nothing to do between confrontations.
    /// </summary>
    public class MilitaryVerbsTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7700);
            turns = new TurnManager(state);
            // NARROW PIPELINE: upkeep plus the diplomatic tick that the
            // forward-posture threat case reads — the AI, economy, crisis and
            // confrontation ticks are omitted because every case here sets
            // treasury, logistics, posture and doctrine directly and asserts
            // the procurement, sustainment and readiness arithmetic that
            // follows over at most two years, none of which needs another
            // government to decide anything.
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            state.commandPoints.current = 60;

            GrantStrategicVerbs(state);
        }

        /// <summary>
        /// Forward posture and transformative programs are strategic verbs — a
        /// player must unlock them. Grant them so these tests can exercise the
        /// mechanics rather than the gate (the gate has its own tests).
        /// </summary>
        static void GrantStrategicVerbs(GameState target)
        {
            target.skillPoints = 99;
            foreach (var node in new[] { "MIL_1", "MIL_2", "MIL_BASING", "ECO_1", "ECO_2", "ECO_STRATEGIC" })
                ProgressionSystem.Unlock(target, node);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- posture ----------

        [Test]
        public void Posture_HoldsReadinessHigherAtHigherCost()
        {
            float ReadinessAfter(MilitaryPosture posture, out float treasurySpent)
            {
                var sim = WorldFactory.CreateDebugWorld(7700);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
                sim.commandPoints.current = 60;
                GrantStrategicVerbs(sim);

                var player = sim.PlayerCountry;
                player.military.ground.readiness = 50f;
                float treasuryBefore = player.resources.treasury;

                MilitarySystem.SetPosture(sim, simTurns, posture);
                for (int i = 0; i < 12; i++) simTurns.EndMonth();

                treasurySpent = treasuryBefore - player.resources.treasury;
                return player.military.ground.readiness;
            }

            float peacetime = ReadinessAfter(MilitaryPosture.Peacetime, out float peaceCost);
            float forward = ReadinessAfter(MilitaryPosture.Forward, out float forwardCost);

            Assert.Greater(forward, peacetime, "A forward posture should hold the force ready.");
            Assert.Greater(forwardCost, peaceCost, "And it should cost real money to do so.");
        }

        [Test]
        public void RaisingPosture_CostsCommandPointsButStandingDownIsFree()
        {
            int before = state.commandPoints.current;
            Assert.IsTrue(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Forward));
            Assert.AreEqual(before - MilitarySystem.PostureCost(MilitaryPosture.Forward),
                state.commandPoints.current);

            int afterRaise = state.commandPoints.current;
            Assert.IsTrue(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Peacetime));
            Assert.AreEqual(afterRaise, state.commandPoints.current, "Standing down is free.");

            Assert.IsFalse(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Peacetime),
                "Setting the posture we already hold is not a decision.");
        }

        [Test]
        public void ForwardPosture_MakesNeighborsNervous()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            float threatBefore = relationship.ThreatPerceivedBy("CHN");

            MilitarySystem.SetPosture(state, turns, MilitaryPosture.Forward);
            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.Greater(relationship.ThreatPerceivedBy("CHN"), threatBefore,
                "Standing forward is read abroad as a threat (GDD §15.1).");
        }

        [Test]
        public void AlertPostureCompatibility_StillWorksForConfrontations()
        {
            var player = state.PlayerCountry;
            Assert.IsFalse(player.military.alertPosture);

            player.military.alertPosture = true;
            Assert.AreEqual(MilitaryPosture.Alert, player.military.posture);

            player.military.posture = MilitaryPosture.Forward;
            Assert.IsTrue(player.military.alertPosture, "Forward is also a holding posture.");

            player.military.alertPosture = false;
            Assert.AreEqual(MilitaryPosture.Peacetime, player.military.posture);
        }

        // ---------- procurement ----------

        [TestCase(MilitarySystem.ProgramScale.Modest, 216)]
        [TestCase(MilitarySystem.ProgramScale.Major, 816)]
        [TestCase(MilitarySystem.ProgramScale.Transformative, 1980)]
        public void ProcurementReadoutUsesSavedCommitments(MilitarySystem.ProgramScale scale, int total)
        {
            var program = MilitarySystem.BuildProgram(ForceBranch.Naval, scale);
            state.PlayerCountry.military.programs.Add(program);
            string before = SaveSystem.ToJson(state);
            string text = MilitarySystem.ProcurementReadout(program);
            StringAssert.Contains(program.label, text);
            StringAssert.Contains($"commitment {total} at saved terms (not reserved)", text);
            StringAssert.Contains("during funded months", text);
            StringAssert.Contains("terminates", text);
            StringAssert.Contains("not refunded", text);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            program.monthsRemaining = 3; program.costPerMonth = 17;
            StringAssert.Contains("commitment 51 at saved terms", MilitarySystem.ProcurementReadout(program));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(MilitarySystem.ProcurementReadout(program),
                MilitarySystem.ProcurementReadout(loaded.PlayerCountry.military.programs.Last()));
        }

        [Test]
        public void ProcurementReadoutDoesNotBackfillMissingLegacyTerms()
        {
            var program = new ProcurementProgram { monthsRemaining = -2, costPerMonth = -5 };
            state.PlayerCountry.military.programs.Add(program);
            string before = SaveSystem.ToJson(state);
            string text = MilitarySystem.ProcurementReadout(program);
            StringAssert.Contains("Unnamed procurement programme", text);
            StringAssert.Contains("0 funded months remaining at 0/MO", text);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProcurementRecordsDistinguishCompletionFromFailedFunding(bool complete)
        {
            state.PlayerCountry.resources.treasury = 10000;
            int count = state.chronicle.Count;
            Assert.IsTrue(MilitarySystem.BeginProcurement(state, turns, ForceBranch.Ground, MilitarySystem.ProgramScale.Modest));
            Assert.AreEqual(count + 1, state.chronicle.Count);
            StringAssert.StartsWith("PROCUREMENT AUTHORIZED:", state.chronicle.Last().text);
            var program = state.PlayerCountry.military.programs.Single();
            program.monthsRemaining = 1;
            state.PlayerCountry.resources.treasury = complete ? 10000 : -10000;
            turns.EndMonth();
            Assert.IsEmpty(state.PlayerCountry.military.programs);
            string record = state.chronicle.Last(e => e.countryId == state.playerCountryId && e.text.StartsWith("PROCUREMENT ")).text;
            StringAssert.StartsWith(complete ? "PROCUREMENT COMPLETED:" : "PROCUREMENT TERMINATED:", record);
            StringAssert.Contains(complete ? "Benefits accrued during funded months" : "lack of funds", record);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(loaded.chronicle.Any(e => e.text == record));
        }

        [Test]
        public void ForeignProcurementKeepsItsRecordOwnership()
        {
            state.FindCountry("CHN").resources.treasury = 10000;
            int count = state.chronicle.Count;
            Assert.IsTrue(MilitarySystem.BeginProcurementBy(state, "CHN", ForceBranch.Air, MilitarySystem.ProgramScale.Major));
            Assert.AreEqual(count + 1, state.chronicle.Count);
            Assert.AreEqual("CHN", state.chronicle.Last().countryId);
            StringAssert.StartsWith("PROCUREMENT AUTHORIZED:", state.chronicle.Last().text);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void ProcurementPanelShowsOnlyOwnCommitmentsAndPaidBacklog(int columns)
        {
            var gc = GameController.Instance; var previous = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                var program = MilitarySystem.BuildProgram(ForceBranch.Ground, MilitarySystem.ProgramScale.Major);
                program.label = "A deliberately long saved procurement programme label";
                state.PlayerCountry.military.programs.Add(program);
                foreach (var branch in new[] { ForceBranch.Ground, ForceBranch.Air, ForceBranch.Naval })
                    state.PlayerCountry.military.Get(branch).inventory.stocks.Clear();
                var asset = AssetCatalog.For(AssetKind.Fighters);
                state.PlayerCountry.military.Get(asset.branch).inventory.Ensure(asset.kind).onOrder = 12.5f;
                for (int i = 0; i < 7; i++) state.AddChronicle(ChronicleCategory.Military, state.playerCountryId, "PROCUREMENT AUTHORIZED: marker " + i);
                state.AddChronicle(ChronicleCategory.Military, "CHN", "PROCUREMENT AUTHORIZED: FOREIGN HIDDEN");
                state.AddChronicle(ChronicleCategory.System, state.playerCountryId, "PROCUREMENT AUTHORIZED: WRONG CATEGORY");
                state.AddChronicle(ChronicleCategory.Military, state.playerCountryId, "Old generic military record");
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new MilitaryView(); view.Refresh();
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                var panels = view.Root.Query<Label>().ToList().Where(l => (l.text ?? "").Contains("RECENT PROCUREMENT RECORD")
                    || (l.text ?? "").Contains("PAID EQUIPMENT BACKLOG")).ToList();
                Assert.AreEqual(2, panels.Count);
                string text = Regex.Replace(string.Join(" ", panels.Select(l => l.text)), @"\s+", " ");
                StringAssert.Contains(program.label, text);
                StringAssert.Contains("commitment 816", text);
                StringAssert.Contains("marker 6", text); StringAssert.Contains("marker 2", text);
                StringAssert.DoesNotContain("marker 1", text); StringAssert.DoesNotContain("FOREIGN HIDDEN", text);
                StringAssert.DoesNotContain("WRONG CATEGORY", text); StringAssert.DoesNotContain("Old generic military record", text);
                Assert.Less(text.IndexOf("marker 6"), text.IndexOf("marker 2"));
                StringAssert.Contains(asset.displayName + ": 12.5 still to arrive", text);
                StringAssert.Contains("Already paid: no monthly purchase instalment", text);
                foreach (var panel in panels)
                    foreach (var line in panel.text.Split('\n')) Assert.LessOrEqual(line.Length, columns, line);
                Assert.AreEqual(before, SaveSystem.ToJson(state));
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, previous); TerminalMetrics.ResetForTests(); }
        }

        [Test]
        public void EquipmentReceiptReportsUpfrontPaymentAndIncrementalDelivery()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 10000;
            var profile = AssetCatalog.For(AssetKind.Fighters);
            var stock = player.military.Get(profile.branch).inventory.Ensure(profile.kind);
            stock.onOrder = 0;
            Assert.IsTrue(AcquisitionSystem.OrderBy(state, player.id, profile.kind, 10));
            Assert.AreEqual(10000 - AssetCatalog.CostOf(profile.kind, 10), player.resources.treasury);
            string receipt = state.notifications.Last(n => n.title == "ORDER PLACED").body;
            StringAssert.Contains("paid upfront", receipt);
            StringAssert.Contains("deliveries arrive incrementally", receipt);
            float treasury = player.resources.treasury;
            AcquisitionSystem.MonthlyDeliveries(state);
            Assert.Less(stock.onOrder, 10);
            Assert.AreEqual(treasury, player.resources.treasury);
        }

        [Test]
        public void Procurement_BuildsStrengthOverYearsAndChargesMonthly()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 5000f;
            float strengthBefore = player.military.naval.strength;
            float treasuryBefore = player.resources.treasury;

            Assert.IsTrue(MilitarySystem.BeginProcurement(state, turns,
                ForceBranch.Naval, MilitarySystem.ProgramScale.Major));
            Assert.AreEqual(1, player.military.programs.Count);

            // Nothing arrives immediately.
            Assert.AreEqual(strengthBefore, player.military.naval.strength, 0.001f);

            for (int i = 0; i < 24; i++) turns.EndMonth();

            Assert.Greater(player.military.naval.strength, strengthBefore, "The program delivered.");
            Assert.Less(player.resources.treasury, treasuryBefore, "And it was paid for every month.");
            Assert.AreEqual(0, player.military.programs.Count, "A finished program leaves the books.");
        }

        [Test]
        public void Procurement_IsLimitedByIndustrialAndFiscalCapacity()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 5000f;

            for (int i = 0; i < MilitarySystem.MaxPrograms; i++)
                Assert.IsTrue(MilitarySystem.BeginProcurement(state, turns,
                    ForceBranch.Ground, MilitarySystem.ProgramScale.Modest));

            Assert.IsFalse(MilitarySystem.BeginProcurement(state, turns,
                ForceBranch.Air, MilitarySystem.ProgramScale.Modest),
                "The industrial base cannot carry unlimited programs.");

            player.military.programs.Clear();
            player.resources.treasury = 10f;
            Assert.IsFalse(MilitarySystem.BeginProcurement(state, turns,
                ForceBranch.Air, MilitarySystem.ProgramScale.Transformative),
                "A program we cannot fund should not be authorized.");
        }

        [Test]
        public void UnfundedProgram_IsCancelledRatherThanRunningFree()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 500f;
            MilitarySystem.BeginProcurement(state, turns, ForceBranch.Ground, MilitarySystem.ProgramScale.Transformative);

            player.resources.treasury = 5f; // fiscal collapse
            turns.EndMonth();

            Assert.AreEqual(0, player.military.programs.Count, "An unfunded program terminates.");
            bool notified = false;
            foreach (var notification in state.notifications)
                if (notification.title == "PROGRAM CANCELLED") notified = true;
            Assert.IsTrue(notified);
        }

        [Test]
        public void LargerPrograms_DeliverMoreOverLonger()
        {
            var modest = MilitarySystem.BuildProgram(ForceBranch.Air, MilitarySystem.ProgramScale.Modest);
            var major = MilitarySystem.BuildProgram(ForceBranch.Air, MilitarySystem.ProgramScale.Major);
            var transformative = MilitarySystem.BuildProgram(ForceBranch.Air, MilitarySystem.ProgramScale.Transformative);

            Assert.Less(modest.monthsRemaining, major.monthsRemaining);
            Assert.Less(major.monthsRemaining, transformative.monthsRemaining);
            Assert.Less(modest.costPerMonth, transformative.costPerMonth);
            Assert.Less(modest.strengthPerMonth, transformative.strengthPerMonth);
        }

        // ---------- logistics ----------

        [Test]
        public void Logistics_RaisesSustainmentAndSoftensTheBurnOfHoldingPosture()
        {
            float SupplyAfterAYearForward(float logistics)
            {
                var sim = WorldFactory.CreateDebugWorld(7700);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
                sim.commandPoints.current = 60;
                GrantStrategicVerbs(sim);
                var player = sim.PlayerCountry;
                player.military.logistics = logistics;
                player.military.ground.supply = 80f;
                MilitarySystem.SetPosture(sim, simTurns, MilitaryPosture.Forward);
                for (int i = 0; i < 12; i++) simTurns.EndMonth();
                return player.military.ground.supply;
            }

            Assert.Greater(SupplyAfterAYearForward(95f), SupplyAfterAYearForward(10f),
                "Depots and transport are what let a force stand forward.");
        }

        [Test]
        public void LogisticsInvestment_CostsCommandPointsAndTreasury()
        {
            var player = state.PlayerCountry;
            float logisticsBefore = player.military.logistics;
            float treasuryBefore = player.resources.treasury;
            int cpBefore = state.commandPoints.current;

            Assert.IsTrue(MilitarySystem.InvestInLogistics(state, turns));

            Assert.Greater(player.military.logistics, logisticsBefore);
            Assert.Less(player.resources.treasury, treasuryBefore);
            Assert.AreEqual(cpBefore - MilitarySystem.LogisticsInvestmentCost, state.commandPoints.current);
        }

        // ---------- doctrine ----------

        [Test]
        public void Doctrine_ChangesHowOperationsResolve()
        {
            OperationRecord RunUnder(MilitaryDoctrine doctrine)
            {
                var sim = WorldFactory.CreateDebugWorld(7701);
                sim.PlayerCountry.military.doctrine = doctrine;

                var confrontation = ConfrontationSystem.BeginBy(sim, sim.playerCountryId, "CHN",
                    ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(sim, confrontation, EscalationState.LimitedConflict, sim.playerCountryId);

                return ConfrontationSystem.LaunchOperationBy(sim, confrontation, sim.playerCountryId,
                    "CONTESTED_LANE", OperationType.Assault,
                    new OperationDirective { speedPriority = 50f, casualtyTolerance = 50f, civilianRiskLimit = 50f });
            }

            var maneuver = RunUnder(MilitaryDoctrine.Maneuver);
            var attrition = RunUnder(MilitaryDoctrine.Attrition);
            var deterrence = RunUnder(MilitaryDoctrine.Deterrence);

            Assert.Less(maneuver.attackerLosses, attrition.attackerLosses,
                "Maneuver spends fewer of our own people than attrition.");
            Assert.Greater(attrition.defenderLosses, maneuver.defenderLosses,
                "Attrition is designed to hurt them more.");
            Assert.Less(deterrence.civilianHarm, maneuver.civilianHarm,
                "A deterrent force is more careful when it does fight.");
        }

        [Test]
        public void DeterrenceDoctrine_MakesOurTermsMoreCredible()
        {
            bool AcceptsUnder(MilitaryDoctrine doctrine, MilitaryPosture posture)
            {
                var sim = WorldFactory.CreateDebugWorld(7702);
                sim.PlayerCountry.military.doctrine = doctrine;
                sim.PlayerCountry.military.posture = posture;

                var confrontation = ConfrontationSystem.BeginBy(sim, sim.playerCountryId, "CHN",
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

                var target = sim.FindCountry("CHN");
                target.warSupport = 42f;
                target.pillars.government = 45f;
                confrontation.defenderWarExhaustion = 28f;
                confrontation.momentum = 8f;

                return ConfrontationSystem.OpponentWouldAccept(sim, confrontation);
            }

            Assert.IsFalse(AcceptsUnder(MilitaryDoctrine.Balanced, MilitaryPosture.Peacetime),
                "Precondition: they are still resisting.");
            Assert.IsTrue(AcceptsUnder(MilitaryDoctrine.Deterrence, MilitaryPosture.Forward),
                "A force built and postured to deter should bring them to terms sooner.");
        }

        [Test]
        public void SetDoctrine_CostsCommandPointsAndRejectsNoOp()
        {
            int before = state.commandPoints.current;
            Assert.IsFalse(MilitarySystem.SetDoctrine(state, turns, MilitaryDoctrine.Balanced),
                "Adopting the doctrine we already hold is not a decision.");
            Assert.AreEqual(before, state.commandPoints.current);

            Assert.IsTrue(MilitarySystem.SetDoctrine(state, turns, MilitaryDoctrine.Maneuver));
            Assert.AreEqual(before - MilitarySystem.DoctrineCost, state.commandPoints.current);
        }

        // ---------- integration ----------

        [Test]
        public void EveryStandingDecision_CountsAsInitiative()
        {
            state.initiativesThisYear = 0;
            state.PlayerCountry.resources.treasury = 5000f;

            MilitarySystem.SetPosture(state, turns, MilitaryPosture.Alert);
            MilitarySystem.SetDoctrine(state, turns, MilitaryDoctrine.Maneuver);
            MilitarySystem.InvestInLogistics(state, turns);
            MilitarySystem.BeginProcurement(state, turns, ForceBranch.Ground, MilitarySystem.ProgramScale.Modest);

            Assert.AreEqual(4, state.initiativesThisYear,
                "Peacetime military decisions must count toward the annual evaluation.");
        }

        [Test]
        public void MilitaryState_SurvivesSaveRoundTrip()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 5000f;
            MilitarySystem.SetPosture(state, turns, MilitaryPosture.Forward);
            MilitarySystem.SetDoctrine(state, turns, MilitaryDoctrine.Attrition);
            MilitarySystem.InvestInLogistics(state, turns);
            MilitarySystem.BeginProcurement(state, turns, ForceBranch.Air, MilitarySystem.ProgramScale.Major);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.PlayerCountry.military;

            Assert.AreEqual(MilitaryPosture.Forward, restored.posture);
            Assert.IsTrue(restored.alertPosture);
            Assert.AreEqual(MilitaryDoctrine.Attrition, restored.doctrine);
            Assert.AreEqual(player.military.logistics, restored.logistics);
            Assert.AreEqual(1, restored.programs.Count);
            Assert.AreEqual(ForceBranch.Air, restored.programs[0].branch);
            Assert.AreEqual(player.military.programs[0].monthsRemaining, restored.programs[0].monthsRemaining);
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using Brink.UI.Views;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    public class StrategyTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 712);
            MandateSystem.Assign(state);
            state.influence = GameState.InfluenceCap;
        }

        void ReadyForeignPolicy(string target, ForeignPolicyIntent intent)
        {
            state.authorizedPillarMask = 31;
            state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            Assert.IsTrue(ForeignPolicySystem.Set(state, target, intent));
            Assert.IsTrue(ForeignPolicySystem.SetDelegated(state, target, true));
        }

        [Test]
        public void StaffCompetenceChangesJudgmentWithoutChangingItsKnownCost()
        {
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);
            Assert.IsNotNull(official);
            var proposed = new Sanction { targetId = "CHN", severity = SanctionSeverity.Pressure };
            float cost = EconomySystem.SanctionBlowbackTerm(state, state.playerCountryId, proposed);
            bool different = false;
            for (int i = 0; i < 100 && !different; i++)
            {
                official.id = "forecast-fixture-" + i; official.competence = 100;
                string competent = StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Pressure, 200);
                official.competence = 0;
                string before = SaveSystem.ToJson(state);
                string poor = StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Pressure, 200);
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                Assert.AreEqual(cost, EconomySystem.SanctionBlowbackTerm(state, state.playerCountryId, proposed));
                different = competent != poor;
            }
            Assert.IsTrue(different, "Staff bands must not ignore the official's competence.");
        }

        [Test]
        public void SanctionsAssessmentDistinguishesExistingClosuresAndGeneralTrade()
        {
            var link = state.FindTrade(state.playerCountryId, "CHN");
            Assert.IsNotNull(link); link.focus = TradeFocus.Energy; link.embargoed = false;
            state.sanctions.Clear();
            StringAssert.Contains("closes in both", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Routine, 200));
            state.sanctions.Add(new Sanction { senderId = "CHN", targetId = state.playerCountryId });
            StringAssert.Contains("already closed", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Routine, 200));
            state.sanctions.Clear(); link.embargoed = true;
            StringAssert.Contains("already closed", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Routine, 200));
            link.focus = TradeFocus.General;
            StringAssert.Contains("no commodity supply", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Routine, 200));
            state.trade.Remove(link);
            StringAssert.Contains("no bilateral link", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Routine, 200));
        }

        [Test]
        public void AssessmentUsesDatedCollectedReportsAndKeepsRefusalsExplicit()
        {
            state.estimates.Clear();
            var estimate = new IntelEstimate { observerId = state.playerCountryId, targetId = "CHN",
                domain = IntelDomain.Military, reportedValue = 70, margin = 10,
                confidence = ConfidenceGrade.High, asOf = state.date, everCollected = false };
            state.estimates.Add(estimate);
            StringAssert.Contains("no collected military", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Pressure, 200));
            estimate.everCollected = true;
            string text = StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Pressure, 200);
            StringAssert.Contains(estimate.RangeText, text);
            StringAssert.Contains(estimate.asOf.DisplayString, text);
            StringAssert.Contains("not a probability", text);
            state.FindRelationship(state.playerCountryId, "CHN").sanctionsTruceMonths = 12;
            StringAssert.Contains("DÉTENTE", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Pressure, 200));
            state.sanctions.Add(new Sanction { senderId = state.playerCountryId, targetId = "CHN" });
            StringAssert.Contains("not replaced or stacked", StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Pressure, 200));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void EconomicDecisionPanelShowsSelectedAssessmentWithoutSaving(int columns)
        {
            TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
            try
            {
                var view = new EconomyView();
                typeof(EconomyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, "CHN");
                typeof(EconomyView).GetField("selectedSeverity", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, SanctionSeverity.Severe);
                string before = SaveSystem.ToJson(state);
                typeof(EconomyView).GetMethod("BuildEconomicWarfare", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { state });
                Label assessment = null;
                view.Root.Query<Label>().ForEach(label => { if ((label.text ?? "").Contains("CABINET ASSESSMENT")) assessment = label; });
                Assert.IsNotNull(assessment);
                StringAssert.Contains("SEVERE", assessment.text);
                foreach (var line in assessment.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                Assert.AreEqual(before, SaveSystem.ToJson(state));
            }
            finally { TerminalMetrics.Update(840f, 8f, 500f, Breakpoints.FromColumns(104)); }
        }

        [Test]
        public void ForecastsDoNotCreateMissingPlansOrReadHiddenForeignCapacity()
        {
            state.mandate.strategy = null;
            state.estimates.Clear();
            string before = SaveSystem.ToJson(state);
            StrategicForecastSystem.Render(state, StrategicDoctrine.Balanced, 12, 64);
            string first = StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Severe, 64);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            StringAssert.Contains("NO ASSESSMENT", first);
            var target = state.FindCountry("CHN");
            target.pillars.economy = 1; target.pillars.military = 99;
            target.resources.treasury = -10000;
            Assert.AreEqual(first, StrategicForecastSystem.SanctionsAssessment(state, "CHN", SanctionSeverity.Severe, 64));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void SanctionsAssessmentIsWrappedAndItsDomesticTermMatchesTheRealOrder(int width)
        {
            var proposed = new Sanction { senderId = state.playerCountryId, targetId = "CHN", severity = SanctionSeverity.Severe };
            float priced = EconomySystem.SanctionBlowbackTerm(state, state.playerCountryId, proposed);
            float beforeCost = EconomySystem.SanctionBlowbackFor(state, state.playerCountryId);
            string before = SaveSystem.ToJson(state);
            string text = StrategicForecastSystem.SanctionsAssessment(state, "CHN", proposed.severity, width);
            foreach (string line in text.Split('\n')) Assert.LessOrEqual(line.Length, width);
            Assert.AreEqual(text, StrategicForecastSystem.SanctionsAssessment(state, "CHN", proposed.severity, width));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", proposed.severity));
            Assert.AreEqual(priced, EconomySystem.SanctionBlowbackFor(state, state.playerCountryId) - beforeCost, 0.0001f);
        }

        [Test]
        public void ProgrammeEditsPauseAndLiveTargetConflictsCannotSpend()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 0, 12));
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Outreach, "IND", 90, 0, 24));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            Assert.IsTrue(StagedProgrammeSystem.MoveEarlier(state, 1));
            Assert.IsFalse(StagedProgrammeSystem.For(state).authorized);
            Assert.AreEqual("IND", StagedProgrammeSystem.Next(StagedProgrammeSystem.For(state)).targetId);
            Assert.IsTrue(StagedProgrammeSystem.Remove(state, 0));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Observe);
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsTrue(ForeignPolicySystem.SetDelegated(state, "CHN", false));
            state.countries.RemoveAll(c => c.id == "CHN");
            before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void DefaultLegacyProgrammeStartsItsScheduleAtTheFirstRealStage()
        {
            StrategySystem.Ensure(state).stagedProgramme = new StagedProgramme();
            string before = SaveSystem.ToJson(state);
            StagedProgrammeSystem.Observe(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Logistics, "", 70, 12, 24));
            Assert.AreEqual(state.date, StagedProgrammeSystem.For(state).adopted);
            Assert.IsFalse(StagedProgrammeSystem.For(state).authorized);
        }

        [Test]
        public void PausedProgrammeCannotUseItsPreviouslyAuthorizedBudget()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId && n.targetId == "CHN");
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 0, 12));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            Assert.AreEqual("", StagedProgrammeSystem.PendingReason(state));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, false, 0));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void StagedIntentWaitsForAuthorizationAndChargesTheOrdinaryCommand()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId && n.targetId == "CHN");
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 30, 0, 12));
            var turns = new TurnManager(state);
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, turns));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 2));
            var control = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(IntelligenceSystem.EstablishNetwork(control, new TurnManager(control), "CHN", IntelDomain.Military));
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, turns));
            Assert.AreEqual(control.commandPoints.current, state.commandPoints.current);
            Assert.AreEqual(control.initiativesThisYear, state.initiativesThisYear);
            Assert.AreEqual(control.FindNetwork(state.playerCountryId, "CHN").penetration,
                state.FindNetwork(state.playerCountryId, "CHN").penetration);
            Assert.AreEqual(2, StagedProgrammeSystem.Spent(StagedProgrammeSystem.For(state)));
            before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, turns));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            state.date = state.date.NextMonth();
            StringAssert.Contains("budget exhausted", StagedProgrammeSystem.PendingReason(state));
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, turns));
        }

        [Test]
        public void StagedPredecessorScheduleAndExplicitControlAreRealGates()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            state.PlayerCountry.military.logistics = 80;
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Logistics, "", 70, 1, 12));
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 1, 24));
            var plan = StagedProgrammeSystem.For(state);
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            StagedProgrammeSystem.Observe(state);
            Assert.IsFalse(plan.stages[0].completed);
            state.date = state.date.NextMonth();
            StagedProgrammeSystem.Observe(state);
            Assert.IsTrue(plan.stages[0].completed);
            Assert.IsFalse(plan.stages[1].completed);
            state.PlayerCountry.FindOfficial(Pillar.Intelligence).mode = ControlMode.DirectControl;
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            var loaded = SaveSystem.FromJson(before);
            Assert.IsTrue(StagedProgrammeSystem.For(loaded).stages[0].completed);
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, false, 0));
            Assert.IsTrue(StagedProgrammeSystem.Abandon(state));
            Assert.IsFalse(StagedProgrammeSystem.Authorize(state, true, 10));
        }

        [Test]
        public void StagedReadIsPureAndWrongTurnManagerCannotSpend()
        {
            Assert.IsNull(StagedProgrammeSystem.For(state));
            string before = SaveSystem.ToJson(state);
            Assert.IsNotEmpty(StagedProgrammeSystem.PendingReason(state));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsFalse(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", float.NaN, 0, 12));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 0, 12));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(SaveSystem.FromJson(before))));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Outreach, "IND", 70, 12, 24));
            Assert.IsFalse(StagedProgrammeSystem.For(state).authorized);
        }

        [Test]
        public void ProgrammeRestartRetainsSpentHistoryAndCannotBuyAnotherMonthlySlot()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 95, 0, 12));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            var old = StagedProgrammeSystem.For(state); int spent = StagedProgrammeSystem.Spent(old);
            Assert.Greater(spent, 0);
            Assert.IsFalse(StagedProgrammeSystem.StartNew(state));
            Assert.IsTrue(StagedProgrammeSystem.Abandon(state));
            Assert.IsTrue(StagedProgrammeSystem.StartNew(state));
            Assert.AreSame(old, state.mandate.strategy.programmeHistory[0]);
            Assert.AreEqual(spent, StagedProgrammeSystem.Spent(old));
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 95, 0, 12));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            state.date = state.date.NextMonth();
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
        }

        [Test]
        public void StagedProgrammeRunsThroughTheMonthlyPipelineOnlyWhenAuthorized()
        {
            state.authorizedPillarMask = 31;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 95, 0, 12));
            var turns = new TurnManager(state); SimulationPipeline.Wire(turns, state);
            turns.EndMonth();
            Assert.AreEqual(0, StagedProgrammeSystem.Spent(StagedProgrammeSystem.For(state)));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            turns.EndMonth();
            Assert.Greater(StagedProgrammeSystem.Spent(StagedProgrammeSystem.For(state)), 0);
            Assert.IsTrue(StagedProgrammeSystem.For(state).stages[0].started);
        }

        [Test]
        public void AssessmentStagePaysOnceAndWaitsForItsDeliveredProductAcrossLoad()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            state.intelProducts.Clear();
            var turns = new TurnManager(state);
            if (state.FindNetwork(state.playerCountryId, "CHN") == null)
                Assert.IsTrue(IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military));
            var old = IntelProductSystem.CommissionBy(state, state.playerCountryId, "CHN", EstimateQuestion.StrategicIntent);
            old.delivered = true;
            state.date = state.date.NextMonth();
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Assessment, "CHN", 1, 0, 12));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 2));
            StagedProgrammeSystem.Observe(state);
            Assert.IsFalse(StagedProgrammeSystem.For(state).stages[0].completed, "An old answer is not this commission.");
            var control = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsNotNull(IntelProductSystem.Commission(control, new TurnManager(control), "CHN", EstimateQuestion.StrategicIntent));
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, turns));
            Assert.AreEqual(control.commandPoints.current, state.commandPoints.current);
            Assert.AreEqual(control.initiativesThisYear, state.initiativesThisYear);
            state = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var stage = StagedProgrammeSystem.For(state).stages[0];
            for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
            {
                StagedProgrammeSystem.Observe(state);
                Assert.IsFalse(stage.completed);
                state.date = state.date.NextMonth();
                Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
                IntelProductSystem.MonthlyUpdate(state);
            }
            state.intelProducts[state.intelProducts.Count - 1].accurate = false;
            StagedProgrammeSystem.Observe(state);
            Assert.IsTrue(stage.completed, "Delivery, not secret correctness, satisfies the stage.");
            Assert.AreEqual(2, stage.commandPointsSpent);
            Assert.AreEqual(2, state.intelProducts.Count, "No duplicate order while waiting.");
        }

        [Test]
        public void ProgrammeTreasuryConditionBlocksWithoutSpendingAndOpensWhenRestored()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            Assert.IsFalse(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 0, 12,
                (ProgrammeCondition)999));
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 0, 12,
                ProgrammeCondition.NonnegativeTreasury));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            state.PlayerCountry.resources.treasury = -1;
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            state.PlayerCountry.resources.treasury = 0;
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
        }

        [Test]
        public void ProgrammePeaceConditionTracksOwnFrontsRatherThanForeignWars()
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            state.confrontations.Clear();
            var front = new Confrontation { initiatorId = state.playerCountryId, defenderId = "CHN" };
            state.confrontations.Add(front);
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.Collection, "CHN", 90, 0, 12,
                ProgrammeCondition.NoActiveFront));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            front.resolved = true;
            state.confrontations.Add(new Confrontation { initiatorId = "IND", defenderId = "CHN" });
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, new TurnManager(state)));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EnergyStageWaitsForRealCompletionAndDoesNotReorderALapsedProject(bool funded)
        {
            state.authorizedPillarMask = 31; state.commandPoints.current = 20;
            foreach (var official in state.PlayerCountry.cabinet) official.mode = ControlMode.Autonomous;
            var site = state.locations.Find(l => l.ownerId == state.playerCountryId && l.type == LocationType.EnergyRegion);
            Assert.IsNotNull(site); site.energyWorks = false;
            state.insurgencies.Clear(); state.PlayerCountry.economy.programmes.Clear();
            state.PlayerCountry.resources.treasury = funded ? 100000 : 0;
            Assert.IsTrue(StagedProgrammeSystem.Add(state, ProgrammeAction.EnergyWorks, site.id, 1, 0, 6));
            Assert.IsTrue(StagedProgrammeSystem.Authorize(state, true, 10));
            var turns = new TurnManager(state);
            Assert.IsTrue(StagedProgrammeSystem.Execute(state, turns));
            var stage = StagedProgrammeSystem.For(state).stages[0];
            StagedProgrammeSystem.Observe(state);
            Assert.IsFalse(stage.completed, "Ordering must not masquerade as delivery.");
            for (int month = 0; month < 12; month++)
            {
                IndustrialSystem.MonthlyUpdate(state); state.date = state.date.NextMonth();
                StagedProgrammeSystem.Observe(state);
            }
            Assert.AreEqual(funded, stage.completed);
            Assert.AreEqual(funded, site.energyWorks);
            Assert.AreEqual(2, stage.commandPointsSpent);
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(StagedProgrammeSystem.Execute(state, turns));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            if (!funded) StringAssert.Contains("DELAYED", StagedProgrammeSystem.Status(state));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void ProgrammePanelIsPureWrappedAndItsPauseButtonAutosaves(int columns)
        {
            var gc = GameController.Instance; var oldState = gc.State; var oldTurns = gc.Turns;
            string previousDirectory = SaveSystem.SaveDirectoryOverride;
            string directory = Path.Combine(Path.GetTempPath(), "brink-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SaveSystem.SaveDirectoryOverride = directory;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                typeof(GameController).GetProperty("Turns").SetValue(gc, new TurnManager(state));
                state.authorizedPillarMask = 31;
                Assert.IsTrue(gc.AddProgrammeStage(ProgrammeAction.Collection, "CHN", 70, 0, 12));
                Assert.IsTrue(gc.AuthorizeStagedProgramme(true, 10));
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new StrategistView();
                string before = SaveSystem.ToJson(state);
                typeof(StrategistView).GetMethod("BuildStagedProgramme", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(view, new object[] { state });
                view.Root.Query<Label>().ForEach(label => {
                    foreach (var line in (label.text ?? "").Split('\n')) Assert.LessOrEqual(line.Length, columns);
                });
                Button pause = null;
                view.Root.Query<Button>().ForEach(button => {
                    Assert.LessOrEqual(button.text.Length, columns);
                    if (button.text == "PAUSE") pause = button;
                });
                Assert.AreEqual(before, SaveSystem.ToJson(state)); Assert.IsNotNull(pause);
                var clickable = typeof(Button).GetProperty("clickable")?.GetValue(pause);
                if (clickable != null) clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clickable, new object[] { null });
                else typeof(Button).GetMethod("SendClick").Invoke(pause, null);
                Assert.IsFalse(StagedProgrammeSystem.For(state).authorized);
                Assert.IsFalse(StagedProgrammeSystem.For(SaveSystem.Load(0)).authorized);
            }
            finally
            {
                TerminalMetrics.ResetForTests(); SaveSystem.SaveDirectoryOverride = previousDirectory;
                typeof(GameController).GetProperty("State").SetValue(gc, oldState);
                typeof(GameController).GetProperty("Turns").SetValue(gc, oldTurns);
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void ForeignIntentIsPerTargetNotAnotherNationalPolicy()
        {
            int initiative = state.initiativesThisYear, influence = state.influence;
            float relations = state.FindRelationship("USA", "CHN").relations;
            Assert.IsTrue(ForeignPolicySystem.Set(state, "CHN", ForeignPolicyIntent.Contain));
            Assert.IsTrue(ForeignPolicySystem.Set(state, "IND", ForeignPolicyIntent.Cooperate));
            Assert.AreEqual(ForeignPolicyIntent.Contain, ForeignPolicySystem.IntentFor(state, "CHN"));
            Assert.AreEqual(ForeignPolicyIntent.Cooperate, ForeignPolicySystem.IntentFor(state, "IND"));
            Assert.AreEqual(ForeignPolicyIntent.Unset, ForeignPolicySystem.IntentFor(state, "RUS"));
            Assert.IsFalse(ForeignPolicySystem.IsDelegated(state, "CHN"));
            Assert.AreEqual(initiative, state.initiativesThisYear);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(relations, state.FindRelationship("USA", "CHN").relations);
        }

        [Test]
        public void ForeignRevisionCostsOnceAndRevokesOldAuthorization()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Observe);
            int before = state.influence;
            Assert.IsTrue(ForeignPolicySystem.Set(state, "CHN", ForeignPolicyIntent.Ignore));
            Assert.AreEqual(before - 1, state.influence);
            Assert.IsFalse(ForeignPolicySystem.IsDelegated(state, "CHN"));
            Assert.IsFalse(ForeignPolicySystem.SetDelegated(state, "CHN", true));
            Assert.IsTrue(ForeignPolicySystem.Set(state, "CHN", ForeignPolicyIntent.Ignore));
            Assert.AreEqual(before - 1, state.influence);
            Assert.IsTrue(ForeignPolicySystem.Set(state, "CHN", ForeignPolicyIntent.Unset));
            Assert.AreEqual(1, state.mandate.strategy.foreignPolicies.Count, "Clearing must not reset the first-adoption price.");
        }

        [Test]
        public void InvalidAndUnaffordableForeignIntentAreByteInert()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Observe);
            state.influence = 0;
            string before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Set(state, "USA", ForeignPolicyIntent.Ignore));
            Assert.IsFalse(ForeignPolicySystem.Set(state, "ABSENT", ForeignPolicyIntent.Ignore));
            Assert.IsFalse(ForeignPolicySystem.Set(state, "CHN", (ForeignPolicyIntent)999));
            Assert.IsFalse(ForeignPolicySystem.Set(state, "CHN", ForeignPolicyIntent.Isolate));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
        }

        [Test]
        public void ForeignPolicyPaysTheOrdinaryOutreachAndOnlyOncePerMonth()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Cooperate);
            var control = UnityEngine.JsonUtility.FromJson<GameState>(UnityEngine.JsonUtility.ToJson(state));
            Assert.IsTrue(DiplomacySystem.Outreach(control, new TurnManager(control), "CHN"));
            Assert.IsTrue(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(control.commandPoints.current, state.commandPoints.current);
            Assert.AreEqual(control.FindRelationship("USA", "CHN").relations, state.FindRelationship("USA", "CHN").relations);
            Assert.AreEqual(control.FindRelationship("USA", "CHN").trust, state.FindRelationship("USA", "CHN").trust);
            Assert.AreEqual(control.initiativesThisYear, state.initiativesThisYear);
            string after = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(after, UnityEngine.JsonUtility.ToJson(state));
        }

        [TestCase(ControlMode.Directed)]
        [TestCase(ControlMode.DirectControl)]
        public void ExplicitCabinetControlWinsOverForeignDelegation(ControlMode mode)
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Cooperate);
            state.PlayerCountry.FindOfficial(Pillar.Diplomacy).mode = mode;
            string before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
            StringAssert.Contains("Autonomous", ForeignPolicySystem.PendingReason(state, "CHN"));
        }

        [Test]
        public void ForeignSanctionsRespectAuthorityAndDetenteBeforeSpending()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Contain);
            state.PlayerCountry.government.type = GovernmentType.PresidentialRepublic;
            state.PlayerCountry.government.emergencyPowers = false;
            state.PlayerCountry.government.authorityUpgradeMask = 0;
            state.authorizedPillarMask = 0;
            Assert.IsFalse(AuthoritySystem.HoldsAuthority(state, Pillar.Economy));
            string before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
            state.authorizedPillarMask = 31;
            state.FindRelationship("USA", "CHN").sanctionsTruceMonths = 24;
            before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
        }

        [TestCase(ForeignPolicyIntent.Contain, SanctionSeverity.Pressure)]
        [TestCase(ForeignPolicyIntent.Isolate, SanctionSeverity.Severe)]
        public void ForeignContainmentUsesRealSanctionsWithoutRepeatedOrders(ForeignPolicyIntent intent, SanctionSeverity severity)
        {
            ReadyForeignPolicy("CHN", intent);
            Assert.IsNull(state.FindSanction("USA", "CHN"));
            Assert.IsTrue(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(severity, state.FindSanction("USA", "CHN").severity);
            Assert.Less(state.commandPoints.current, 20);
            state.date = state.date.NextMonth();
            string before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
        }

        [Test]
        public void ReconciliationLiftsOnlyOursAndDoesNotSpendOnOutreachThatMonth()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Reconcile);
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, "USA", "CHN", SanctionSeverity.Routine));
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, "CHN", "USA", SanctionSeverity.Routine));
            float before = state.FindRelationship("USA", "CHN").relations;
            Assert.IsTrue(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.IsNull(state.FindSanction("USA", "CHN"));
            Assert.NotNull(state.FindSanction("CHN", "USA"));
            Assert.AreEqual(before, state.FindRelationship("USA", "CHN").relations);
            Assert.AreEqual(19, state.commandPoints.current);
        }

        [Test]
        public void ObservationStopsAtItsBoundAndIgnoreDoesNotEraseANetwork()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Observe);
            state.networks.RemoveAll(n => n.ownerId == "USA" && n.targetId == "CHN");
            Assert.IsTrue(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            var network = state.FindNetwork("USA", "CHN");
            Assert.AreEqual(18f, network.penetration);
            Assert.AreEqual(18, state.commandPoints.current);
            state.date = state.date.NextMonth();
            network.penetration = 60f;
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.IsTrue(ForeignPolicySystem.Set(state, "CHN", ForeignPolicyIntent.Ignore));
            Assert.AreSame(network, state.FindNetwork("USA", "CHN"));
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
        }

        [Test]
        public void BlockedPolicyDoesNotStarveAnotherCountryAndReadsArePure()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Contain);
            ReadyForeignPolicy("IND", ForeignPolicyIntent.Cooperate);
            state.FindRelationship("USA", "CHN").sanctionsTruceMonths = 24;
            float before = state.FindRelationship("USA", "IND").relations;
            string json = UnityEngine.JsonUtility.ToJson(state);
            for (int i = 0; i < 10; i++) ForeignPolicySystem.PendingReason(state, "CHN");
            Assert.AreEqual(json, UnityEngine.JsonUtility.ToJson(state));
            Assert.IsTrue(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.Greater(state.FindRelationship("USA", "IND").relations, before);
            Assert.IsNull(state.FindSanction("USA", "CHN"));
        }

        [Test]
        public void ForeignIntentSurvivesSaveAndOldMissingLedgerStaysNeutral()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Cooperate);
            Assert.IsTrue(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            var loaded = UnityEngine.JsonUtility.FromJson<GameState>(UnityEngine.JsonUtility.ToJson(state));
            Assert.AreEqual(ForeignPolicyIntent.Cooperate, ForeignPolicySystem.IntentFor(loaded, "CHN"));
            Assert.IsTrue(ForeignPolicySystem.IsDelegated(loaded, "CHN"));
            Assert.IsFalse(ForeignPolicySystem.Execute(loaded, new TurnManager(loaded)), "Load must not allow a second action this month.");
            loaded.mandate.strategy.foreignPolicies = null;
            string before = UnityEngine.JsonUtility.ToJson(loaded);
            Assert.AreEqual(ForeignPolicyIntent.Unset, ForeignPolicySystem.IntentFor(loaded, "CHN"));
            Assert.IsFalse(ForeignPolicySystem.Execute(loaded, new TurnManager(loaded)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(loaded));
        }

        [Test]
        public void ForeignDelegationIsReachedByTheRealMonthlyPipeline()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Cooperate);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();
            Assert.AreEqual(state.date, state.mandate.strategy.lastForeignPolicyAction);
            Assert.IsTrue(state.notifications.Exists(n => n.title == "FOREIGN POLICY EXECUTED"));
        }

        [Test]
        public void ForeignReconciliationCannotLiftAChamberMandate()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Reconcile);
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, "USA", "CHN", SanctionSeverity.Pressure));
            if (state.council == null) state.council = new CouncilState();
            state.council.mandates.Add(new CouncilMandate { subjectId = "CHN", monthsRemaining = 12 });
            string before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            StringAssert.Contains("mandated", ForeignPolicySystem.PendingReason(state, "CHN"));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
        }

        [Test]
        public void ForeignPolicyDoesNotSpendWhenUnaffordableAndStopsWhenSatisfied()
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Observe);
            state.networks.RemoveAll(n => n.ownerId == "USA" && n.targetId == "CHN");
            state.commandPoints.current = 0;
            string before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Cooperate);
            var relationship = state.FindRelationship("USA", "CHN");
            relationship.relations = relationship.trust = 70;
            before = UnityEngine.JsonUtility.ToJson(state);
            Assert.IsFalse(ForeignPolicySystem.Execute(state, new TurnManager(state)));
            Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void ForeignPolicyPanelIsBoundedPureAndNamesTheActualTarget(int columns)
        {
            ReadyForeignPolicy("CHN", ForeignPolicyIntent.Observe);
            TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
            try
            {
                string before = UnityEngine.JsonUtility.ToJson(state);
                var view = new DiplomacyView();
                typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, "CHN");
                typeof(DiplomacyView).GetMethod("BuildForeignPolicyControls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { state });
                string text = "";
                view.Root.Query<Label>().ForEach(label =>
                {
                    text += label.text + "\n";
                    foreach (var line in (label.text ?? "").Split('\n')) Assert.LessOrEqual(AsciiChart.VisibleLength(line), columns);
                });
                view.Root.Query<Button>().ForEach(button => Assert.LessOrEqual(AsciiChart.VisibleLength(button.text), columns));
                StringAssert.Contains(state.FindCountry("CHN").displayName, text);
                StringAssert.Contains("Observe", text);
                Assert.AreEqual(before, UnityEngine.JsonUtility.ToJson(state));
            }
            finally { TerminalMetrics.ResetForTests(); }
        }

        [Test]
        public void ForeignPolicyControllerPersistsIntentAndSeparateDelegation()
        {
            var gc = GameController.Instance; var previous = gc.State; var previousTurns = gc.Turns;
            string oldDirectory = SaveSystem.SaveDirectoryOverride;
            string directory = Path.Combine(Path.GetTempPath(), "brink-foreign-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SaveSystem.SaveDirectoryOverride = directory;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                typeof(GameController).GetProperty("Turns").SetValue(gc, new TurnManager(state));
                Assert.IsTrue(gc.SetForeignPolicy("CHN", ForeignPolicyIntent.Cooperate));
                Assert.IsFalse(ForeignPolicySystem.IsDelegated(SaveSystem.Load(0), "CHN"));
                Assert.IsTrue(gc.DelegateForeignPolicy("CHN", true));
                Assert.IsTrue(ForeignPolicySystem.IsDelegated(SaveSystem.Load(0), "CHN"));
                Assert.IsTrue(gc.DelegateForeignPolicy("CHN", false));
                Assert.IsFalse(ForeignPolicySystem.IsDelegated(SaveSystem.Load(0), "CHN"));
                var view = new DiplomacyView();
                typeof(DiplomacyView).GetField("selectedTargetId", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(view, "CHN");
                typeof(DiplomacyView).GetMethod("BuildForeignPolicyControls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, new object[] { state });
                Button observe = null;
                view.Root.Query<Button>().ForEach(b => { if (b.text == "OBSERVE [1 INF]") observe = b; });
                Assert.NotNull(observe);
                var clickable = typeof(Button).GetProperty("clickable")?.GetValue(observe);
                if (clickable != null) clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clickable, new object[] { null });
                else typeof(Button).GetMethod("SendClick").Invoke(observe, null);
                Assert.AreEqual(ForeignPolicyIntent.Observe, ForeignPolicySystem.IntentFor(SaveSystem.Load(0), "CHN"));
                Assert.AreEqual(ForeignPolicyIntent.Unset, ForeignPolicySystem.IntentFor(state, "IND"));
            }
            finally
            {
                SaveSystem.SaveDirectoryOverride = oldDirectory;
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                typeof(GameController).GetProperty("Turns").SetValue(gc, previousTurns);
                Directory.Delete(directory, true);
            }
        }

        [Test]
        public void OldPostingLazilyGetsStrategicPlan()
        {
            state.mandate.strategy = null;
            var plan = StrategySystem.Ensure(state);
            Assert.NotNull(plan);
            Assert.AreEqual(StrategicDoctrine.Balanced, plan.doctrine);
            Assert.AreEqual(60, plan.horizonMonths);
        }

        [Test]
        public void FirstDoctrineIsFreeRevisionCostsInfluenceButNeverInitiative()
        {
            int before = state.influence;
            int initiative = state.initiativesThisYear;
            Assert.IsTrue(StrategySystem.SetDoctrine(state, StrategicDoctrine.Prosperity));
            Assert.AreEqual(before, state.influence);
            Assert.AreEqual(initiative, state.initiativesThisYear);
            Assert.IsTrue(StrategySystem.SetDoctrine(state, StrategicDoctrine.Deterrence));
            Assert.AreEqual(before - StrategySystem.DoctrineRevisionInfluence, state.influence);
            Assert.AreEqual(initiative, state.initiativesThisYear);
        }

        [Test]
        public void DoctrineSteersOnlyAutonomousOfficials()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Deterrence);
            var military = state.PlayerCountry.FindOfficial(Pillar.Military);
            var economy = state.PlayerCountry.FindOfficial(Pillar.Economy);
            var diplomacy = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            military.mode = ControlMode.Autonomous;
            economy.mode = ControlMode.Autonomous;
            diplomacy.mode = ControlMode.Directed;
            diplomacy.directiveId = "DIP_OUTREACH";

            StrategyCabinetBridge.Prepare(state);

            Assert.AreEqual(MilitaryAdvice.PrepareForWar, military.directiveId);
            Assert.AreEqual("ECO_AUSTERITY", economy.directiveId);
            Assert.AreEqual("DIP_OUTREACH", diplomacy.directiveId, "Standing strategy must not overwrite an explicit order.");
        }

        [Test]
        public void CountryPolicyIsActuallyCountrySpecificAndNotInitiative()
        {
            var policies = StrategySystem.AvailablePolicies(state);
            Assert.AreEqual(2, policies.Length);
            foreach (var policy in policies)
            {
                Assert.AreEqual("USA", policy.countryId);
                Assert.AreNotEqual(policy.favours, policy.strains);
            }
            Assert.IsFalse(StrategySystem.SetPolicy(state, "CHN_INDUSTRIAL_SECURITY"));
            int initiative = state.initiativesThisYear;
            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));
            Assert.AreEqual(initiative, state.initiativesThisYear);
        }

        [Test]
        public void EveryAuthoredCountryHasARealPolicyChoice()
        {
            foreach (var profile in WorldFactory.Profiles)
            {
                var posting = WorldFactory.CreateWorld(712, profile.id, WorldSize.Full);
                var policies = StrategySystem.AvailablePolicies(posting);

                Assert.AreEqual(2, policies.Length, profile.id);
                Assert.AreNotEqual(policies[0].id, policies[1].id, profile.id);
                Assert.AreEqual(policies[0].slotId, policies[1].slotId, profile.id);
                foreach (var policy in policies)
                {
                    Assert.AreEqual(profile.id, policy.countryId);
                    Assert.AreNotEqual(policy.favours, policy.strains, policy.id);
                }
            }
        }

        [Test]
        public void RevisingCountryPolicyCostsInfluenceAndChangesCabinetEmphasis()
        {
            var military = state.PlayerCountry.FindOfficial(Pillar.Military);
            var economy = state.PlayerCountry.FindOfficial(Pillar.Economy);
            var diplomacy = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            military.mode = economy.mode = diplomacy.mode = ControlMode.Autonomous;

            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));
            int beforeRevision = state.influence;
            StrategyCabinetBridge.Prepare(state);
            Assert.AreEqual("DIP_OUTREACH", diplomacy.directiveId);
            Assert.AreEqual("MIL_CONSERVE", military.directiveId);

            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_INDUSTRIAL_RENEWAL"));
            Assert.AreEqual(beforeRevision - StrategySystem.PolicyRevisionInfluence, state.influence);
            StrategyCabinetBridge.Prepare(state);
            Assert.AreEqual("ECO_GROWTH", economy.directiveId);
            Assert.AreEqual("DIP_PRESSURE", diplomacy.directiveId);
            Assert.AreEqual(1, StrategySystem.Ensure(state).policies.Count,
                "Alternatives replace the national-policy slot instead of stacking.");
        }

        [Test]
        public void PlanFrameIsPlanningContextNotNationalPower()
        {
            int initiative = state.initiativesThisYear;
            int influence = state.influence;
            float treasury = state.PlayerCountry.resources.treasury;
            float military = state.PlayerCountry.pillars.military;

            Assert.IsTrue(StrategySystem.SetPlanFrame(state, "Five Year Security Plan", 59));
            var plan = StrategySystem.Ensure(state);
            Assert.AreEqual("Five Year Security Plan", plan.planTitle);
            Assert.AreEqual(60, plan.horizonMonths);
            Assert.AreEqual(initiative, state.initiativesThisYear);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(treasury, state.PlayerCountry.resources.treasury);
            Assert.AreEqual(military, state.PlayerCountry.pillars.military);
        }

        [Test]
        public void ForecastIsDeterministicAndReadOnly()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Resilience);
            string before = UnityEngine.JsonUtility.ToJson(state);
            string a = StrategicForecastSystem.Render(state, StrategicDoctrine.Deterrence, 36, 72);
            string b = StrategicForecastSystem.Render(state, StrategicDoctrine.Deterrence, 36, 72);
            string after = UnityEngine.JsonUtility.ToJson(state);

            Assert.AreEqual(a, b);
            Assert.AreEqual(before, after, "A what-if must never become a hidden simulation step.");
            StringAssert.Contains("WHAT-IF: DETERRENCE", a);
            StringAssert.Contains("PREPARE FOR WAR", a);
            StringAssert.Contains("YES, BUT", a);
        }

        [Test]
        public void PlayerObjectivesAreBoundedStandingAndNotAnInitiativeFarm()
        {
            int xp = state.strategistXP;
            int initiative = state.initiativesThisYear;
            for (int i = 0; i < StrategySystem.MaxObjectives; i++)
                Assert.IsTrue(StrategySystem.AddObjective(state, "Goal " + i,
                    new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));
            Assert.IsFalse(StrategySystem.AddObjective(state, "Too many",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));

            StrategySystem.MonthlyUpdate(state);
            Assert.AreEqual(xp, state.strategistXP);
            Assert.AreEqual(initiative, state.initiativesThisYear);
            foreach (var objective in state.mandate.strategy.objectives)
            {
                Assert.IsTrue(objective.achieved);
                Assert.IsTrue(objective.everAchieved);
            }

            state.PlayerCountry.stability = 0f;
            StrategySystem.MonthlyUpdate(state);
            foreach (var objective in state.mandate.strategy.objectives)
            {
                Assert.IsFalse(objective.achieved, "Standing objectives must become unmet again when the world moves away from the target.");
                Assert.IsTrue(objective.everAchieved, "First attainment remains part of the record.");
            }
        }

        [Test]
        public void FreeformObjectiveIsSelfAssessedHistoricalAndUnrewarded()
        {
            int xp = state.strategistXP;
            int initiative = state.initiativesThisYear;
            int notifications = state.notifications.Count;
            int chronicle = state.chronicle.Count;

            Assert.IsTrue(StrategySystem.AddFreeformObjective(state,
                "  Keep the republic out of a continental war  "));
            var objective = StrategySystem.Ensure(state).objectives[0];
            Assert.IsTrue(objective.freeform);
            Assert.AreEqual("Keep the republic out of a continental war", objective.title);
            StringAssert.Contains("[OPEN]", StrategySystem.StatusText(state));

            StrategySystem.MonthlyUpdate(state);
            Assert.IsFalse(objective.achieved,
                "the simulation must not pretend it can evaluate arbitrary intent.");

            Assert.IsTrue(StrategySystem.SetFreeformObjectiveMet(state, objective.id, true));
            Assert.IsTrue(objective.achieved);
            Assert.IsTrue(objective.everAchieved);
            Assert.AreEqual(notifications + 1, state.notifications.Count);
            Assert.AreEqual(chronicle + 1, state.chronicle.Count);
            Assert.AreEqual(xp, state.strategistXP);
            Assert.AreEqual(initiative, state.initiativesThisYear);

            Assert.IsTrue(StrategySystem.SetFreeformObjectiveMet(state, objective.id, false));
            Assert.IsFalse(objective.achieved);
            Assert.IsTrue(objective.everAchieved);
            StringAssert.Contains("[PREVIOUSLY MET]", StrategySystem.StatusText(state));
            Assert.IsTrue(StrategySystem.SetFreeformObjectiveMet(state, objective.id, true));
            Assert.AreEqual(notifications + 1, state.notifications.Count,
                "reaching self-assessed intent again must not manufacture repeat traffic.");
            Assert.AreEqual(chronicle + 1, state.chronicle.Count);
        }

        [Test]
        public void FreeformAndMeasuredObjectivesShareOneLimitAndOneSaveList()
        {
            Assert.IsTrue(StrategySystem.AddFreeformObjective(state, "Preserve strategic room"));
            Assert.IsTrue(StrategySystem.AddObjective(state, "Hold stability",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=999f, text="Stability at 999." }));
            Assert.IsTrue(StrategySystem.AddFreeformObjective(state, "Avoid permanent dependence"));
            Assert.IsFalse(StrategySystem.AddFreeformObjective(state, "A fourth objective"));

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(StrategySystem.MaxObjectives, loaded.mandate.strategy.objectives.Count);
            Assert.IsTrue(loaded.mandate.strategy.objectives[0].freeform);
            Assert.NotNull(loaded.mandate.strategy.objectives[1].condition);
            Assert.IsFalse(loaded.mandate.strategy.objectives[1].freeform,
                "measured and old-save objectives default to simulation evaluation.");
            Assert.AreEqual("Avoid permanent dependence", loaded.mandate.strategy.objectives[2].title);

            int notifications = loaded.notifications.Count;
            int chronicle = loaded.chronicle.Count;
            StrategySystem.MonthlyUpdate(loaded);
            Assert.IsFalse(loaded.mandate.strategy.objectives[0].achieved,
                "a reloaded freeform objective was evaluated as a default numeric condition.");
            Assert.AreEqual(notifications, loaded.notifications.Count);
            Assert.AreEqual(chronicle, loaded.chronicle.Count);
            StringAssert.Contains("[OPEN]", StrategySystem.StatusText(loaded));
        }

        [Test]
        public void MeasuredObjectiveCannotBeMarkedMetByHand()
        {
            Assert.IsTrue(StrategySystem.AddObjective(state, "Hold stability",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=99f, text="Stability at 99." }));
            var objective = StrategySystem.Ensure(state).objectives[0];

            Assert.IsFalse(StrategySystem.SetFreeformObjectiveMet(state, objective.id, true));
            Assert.IsFalse(objective.achieved);
        }

        [Test]
        public void LongTermProgrammeSteersOnlyItsAutonomousDesk()
        {
            Assert.IsTrue(StrategySystem.AddObjective(state, "Steady the country",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=99f, text="Stability at 99." }));
            var objective = StrategySystem.Ensure(state).objectives[0];
            var government = state.PlayerCountry.FindOfficial(Pillar.Government);
            Assert.IsTrue(StrategySystem.SetProgramme(state, objective.id));
            Assert.AreEqual(objective.id, StrategySystem.Ensure(state).programmeObjectiveId);
            StrategyCabinetBridge.Prepare(state);
            Assert.AreEqual("GOV_STABILITY", government.directiveId);
            StringAssert.Contains("[PROGRAMME]", StrategySystem.StatusText(state));
            CabinetSystem.MonthlyAct(state);
            StrategySystem.ClarifyCabinetReport(state);
            var report = state.cabinetReport.Find(r => r.pillar == Pillar.Government);
            Assert.IsNotNull(report);
            StringAssert.Contains("long-term programme", report.summary);

            government.mode = ControlMode.Directed;
            government.directiveId = "GOV_APPROVAL";
            StrategyCabinetBridge.Prepare(state);
            Assert.AreEqual("GOV_APPROVAL", government.directiveId,
                "a strategic programme overrode an explicit Cabinet instruction");
        }

        [Test]
        public void ProgrammeCompletesWithItsMeasuredObjective()
        {
            Assert.IsTrue(StrategySystem.AddObjective(state, "Reach present stability",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));
            var objective = StrategySystem.Ensure(state).objectives[0];
            Assert.IsTrue(StrategySystem.SetProgramme(state, objective.id));
            StrategySystem.MonthlyUpdate(state);
            Assert.IsTrue(objective.achieved);
            Assert.IsEmpty(StrategySystem.Ensure(state).programmeObjectiveId);
            Assert.AreEqual(1, state.notifications.FindAll(n => n.title == "OBJECTIVE REACHED" || n.title == "PROGRAMME COMPLETE").Count,
                "one completed objective produced duplicate completion traffic");
        }

        [Test]
        public void ReplacingAProgrammeCostsInfluenceButCancellingDoesNotResetThePrice()
        {
            Assert.IsTrue(StrategySystem.AddObjective(state, "Stability",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=99f, text="Stability." }));
            Assert.IsTrue(StrategySystem.AddObjective(state, "Solvency",
                new MandateObjective { kind=MandateObjectiveKind.Solvent, text="Solvent." }));
            var plan = StrategySystem.Ensure(state);
            int before = state.influence;
            Assert.IsTrue(StrategySystem.SetProgramme(state, plan.objectives[0].id));
            Assert.AreEqual(before, state.influence);
            Assert.IsTrue(StrategySystem.CancelProgramme(state));
            Assert.IsTrue(StrategySystem.SetProgramme(state, plan.objectives[1].id));
            Assert.AreEqual(before - StrategySystem.ProgrammeRevisionInfluence, state.influence);
            Assert.AreEqual(plan.objectives[1].id, plan.programmeObjectiveId,
                "replacement retained the previous programme");
        }

        [Test]
        public void FreeformAndUnsupportedObjectivesCannotPretendToBeProgrammes()
        {
            Assert.IsTrue(StrategySystem.AddFreeformObjective(state, "Preserve room"));
            var freeform = StrategySystem.Ensure(state).objectives[0];
            Assert.IsFalse(StrategySystem.SetProgramme(state, freeform.id));
            Assert.IsTrue(StrategySystem.AddObjective(state, "Gain capabilities",
                new MandateObjective { kind=MandateObjectiveKind.CapabilitiesAtLeast, threshold=4f, text="Capabilities." }));
            Assert.IsFalse(StrategySystem.SetProgramme(state, StrategySystem.Ensure(state).objectives[1].id));

            foreach (var kind in new[] { MandateObjectiveKind.UnityAtLeast, MandateObjectiveKind.EnergyAtLeast,
                MandateObjectiveKind.FoodAtLeast, MandateObjectiveKind.MaterialsAtLeast,
                MandateObjectiveKind.IndustryAtLeast, MandateObjectiveKind.TreatiesAtLeast,
                MandateObjectiveKind.RelationsAtLeast, MandateObjectiveKind.RelationsAtMost })
                Assert.IsFalse(StrategyCabinetBridge.TryProgrammeInstruction(
                    new PlayerObjective { condition = new MandateObjective { kind=kind, param="CHN" } }, out _, out _), kind.ToString());
            Assert.IsFalse(StrategyCabinetBridge.TryProgrammeInstruction(
                new PlayerObjective { condition = new MandateObjective { kind=MandateObjectiveKind.PillarAtLeast, param="Government" } }, out _, out _));
            Assert.IsFalse(StrategyCabinetBridge.TryProgrammeInstruction(
                new PlayerObjective { condition = new MandateObjective { kind=MandateObjectiveKind.PillarAtLeast, param="Military" } }, out _, out _));
        }

        [Test]
        public void ProgrammeAuthorizationIsAdministrativeAndRemovalClearsIt()
        {
            Assert.IsTrue(StrategySystem.AddObjective(state, "Stability",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=99f, text="Stability." }));
            var plan = StrategySystem.Ensure(state);
            var objective = plan.objectives[0];
            string countryBefore = UnityEngine.JsonUtility.ToJson(state.PlayerCountry);
            int revisions = plan.revisionCount;
            Assert.IsTrue(StrategySystem.SetProgramme(state, objective.id));
            Assert.AreEqual(countryBefore, UnityEngine.JsonUtility.ToJson(state.PlayerCountry));
            Assert.AreEqual(revisions, plan.revisionCount,
                "authorizing delegated work was misfiled as a strategic reversal");
            Assert.IsTrue(StrategySystem.RemoveObjective(state, objective.id));
            Assert.IsEmpty(plan.programmeObjectiveId);
        }

        [Test]
        public void OperatorPanelOffersFreeformIntentAndManualStatus()
        {
            string source = ReadRuntimeSource(Path.Combine("UI", "Views", "StrategistView.cs"));
            int start = source.IndexOf("WRITE AN OBJECTIVE", StringComparison.Ordinal);
            int end = source.IndexOf("void BuildForecast", start, StringComparison.Ordinal);
            Assert.Greater(start, 0);
            Assert.Greater(end, start);
            string block = source.Substring(start, end - start);

            StringAssert.Contains("\"Freeform\"", block);
            StringAssert.Contains("AddFreeformObjective(state, title.value)", block);
            StringAssert.Contains("if (captured.freeform)", block);
            StringAssert.Contains("SetFreeformObjectiveMet", block);
            StringAssert.Contains("MARK MET", block);
            StringAssert.Contains("REOPEN", block);
        }

        [Test]
        public void StandingStrategyReportingDoesNotPretendItWasMinisterialJudgement()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Prosperity);
            StrategyCabinetBridge.Prepare(state);
            CabinetSystem.MonthlyAct(state);
            StrategySystem.ClarifyCabinetReport(state);

            bool found = false;
            foreach (var line in state.cabinetReport)
            {
                if (line.pillar != Pillar.Economy) continue;
                found = true;
                Assert.IsFalse(line.ownJudgement);
                StringAssert.Contains("standing strategy", line.summary);
            }
            Assert.IsTrue(found);
        }

        [Test]
        public void ObjectivesCannotUseBaselineDependentMandateConditions()
        {
            Assert.IsFalse(StrategySystem.AddObjective(state, "Exploit mandate baseline",
                new MandateObjective { kind=MandateObjectiveKind.GdpGrowthAtLeast, threshold=1f, text="Grow." }));
            Assert.IsFalse(StrategySystem.AddObjective(state, "Exploit original ground",
                new MandateObjective { kind=MandateObjectiveKind.HoldOriginalGround, text="Hold." }));
        }

        [Test]
        public void StrategyPersistsOnMandateReissue()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Influence);
            StrategySystem.SetPlanFrame(state, "Influence Plan", 120);
            var before = state.mandate.strategy;
            MandateSystem.Reissue(state, "test administration");
            Assert.AreSame(before, state.mandate.strategy);
            Assert.AreEqual(StrategicDoctrine.Influence, state.mandate.strategy.doctrine);
            Assert.AreEqual("Influence Plan", state.mandate.strategy.planTitle);
            Assert.AreEqual(120, state.mandate.strategy.horizonMonths);
        }

        // ---------- national-policy presentation and refusal ----------
        //
        // Every country gained a second alternative, which made the *replacement*
        // path reachable for the first time: with one policy per country,
        // re-adopting the same id returns true before any cost is charged. These
        // cover what that newly-live path has to say on screen.

        /// <summary>
        /// One definition of "which policy holds the slot". The panel prices the
        /// button from this and <see cref="StrategySystem.SetPolicy"/> charges
        /// from it, so the two cannot disagree about what a replacement is.
        /// </summary>
        [Test]
        public void TheOccupiedSlotHasASingleSharedDefinition()
        {
            var plan = StrategySystem.Ensure(state);
            Assert.IsNull(StrategySystem.PolicyInSlot(plan, "NATIONAL"),
                "nothing is adopted yet, so the national slot must read as empty.");

            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));

            var held = StrategySystem.PolicyInSlot(plan, "NATIONAL");
            Assert.IsNotNull(held, "an adopted policy no longer occupies its slot.");
            Assert.AreEqual("USA_ALLIANCE_FIRST", held.policyId);

            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_INDUSTRIAL_RENEWAL"));
            Assert.AreEqual("USA_INDUSTRIAL_RENEWAL",
                StrategySystem.PolicyInSlot(plan, "NATIONAL").policyId,
                "replacing left the old policy in the slot.");
            Assert.AreEqual(1, plan.policies.Count,
                "alternatives must replace in the NATIONAL slot, never stack.");
        }

        /// <summary>
        /// The adopted alternative is marked the way the doctrine row directly
        /// above it is marked. With one policy per country this was invisible;
        /// with two mutually exclusive ones, an unmarked pair is a guess.
        /// </summary>
        [Test]
        public void TheAdoptedPolicyIsMarkedLikeTheAdoptedDoctrine()
        {
            string block = PolicyBlockSource();

            StringAssert.Contains("► ", block,
                "the adopted policy is not marked with the same indicator the doctrine row uses.");
            StringAssert.Contains("primary", block,
                "the adopted policy does not carry the 'primary' class the adopted doctrine carries.");
            StringAssert.Contains("adopted", block,
                "nothing in the policy block distinguishes the standing policy from its alternative.");
        }

        /// <summary>
        /// An unaffordable replacement is refused *before* it is pressed. The
        /// button carries the cost tag, so the shell-wide gate reads it back,
        /// blocks it with its price, and prints the reason under the row — the
        /// convention that exists because a control which spends nothing and says
        /// nothing reads as a broken control.
        /// </summary>
        [Test]
        public void AnUnaffordableReplacementIsRefusedWithItsPriceOnScreen()
        {
            string block = PolicyBlockSource();
            StringAssert.Contains("INF]", block,
                "the replacement button carries no cost tag, so GateOnAffordability cannot refuse it.");
            StringAssert.Contains("PolicyRevisionInfluence", block,
                "the tag hardcodes a number instead of reading the cost the system actually charges.");

            state.influence = 0;

            var host = new UnityEngine.UIElements.VisualElement();
            var row = new UnityEngine.UIElements.VisualElement();
            row.AddToClassList("button-row");
            host.Add(row);

            var button = new UnityEngine.UIElements.Button
            {
                text = $"ADOPT [{StrategySystem.PolicyRevisionInfluence} INF]"
            };
            button.AddToClassList("cmd-button");
            row.Add(button);

            Brink.UI.Views.TerminalView.GateOnAffordability(row, state);
            Brink.UI.Views.TerminalView.ExplainBlockedCommands(host);

            Assert.IsFalse(button.enabledSelf,
                "a replacement the operator cannot pay for is still pressable.");
            StringAssert.Contains($"{StrategySystem.PolicyRevisionInfluence} INF",
                Brink.UI.Views.TerminalView.BlockedReason(button));

            string printed = "";
            foreach (var child in host.Children())
                if (child is UnityEngine.UIElements.Label label) printed += label.text;
            printed = System.Text.RegularExpressions.Regex.Replace(printed, @"\s+", " ");

            StringAssert.Contains("UNAVAILABLE", printed,
                "the refusal is not readable on screen; a tooltip is dead weight on a phone.");
        }

        /// <summary>
        /// And if a refusal ever reaches the callback anyway, the panel says so
        /// rather than swallowing it — the objective form's pattern, one section
        /// down in the same view.
        /// </summary>
        [Test]
        public void ARefusedAdoptionExplainsItselfInsteadOfFailingSilently()
        {
            string block = PolicyBlockSource();

            StringAssert.Contains("if (StrategySystem.SetPolicy(state, captured.id))", block,
                "the ADOPT callback still discards SetPolicy's answer, so a refusal is silent.");
            StringAssert.Contains("policyFeedback.text", block,
                "a refused adoption writes no explanation anywhere the operator can read it.");
        }

        /// <summary>
        /// A refusal must also be inert: nothing spent, nothing changed, the
        /// standing policy left exactly where it was.
        /// </summary>
        [Test]
        public void ARefusedReplacementChangesNothingAtAll()
        {
            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));
            var plan = StrategySystem.Ensure(state);
            state.influence = 0;
            int revisions = plan.revisionCount;

            Assert.IsFalse(StrategySystem.SetPolicy(state, "USA_INDUSTRIAL_RENEWAL"),
                "a replacement was accepted with no Influence to pay for it.");

            Assert.AreEqual(0, state.influence, "a refused replacement still moved the account.");
            Assert.AreEqual(revisions, plan.revisionCount, "a refused replacement counted as a revision.");
            Assert.AreEqual("USA_ALLIANCE_FIRST",
                StrategySystem.PolicyInSlot(plan, "NATIONAL").policyId,
                "a refused replacement unseated the standing policy.");
            Assert.AreEqual(1, plan.policies.Count);
        }

        /// <summary>
        /// The COMMAND INDEX answers "what can I do and what does it cost". It
        /// said "free" forever, while the second alternative costs Influence.
        /// </summary>
        [Test]
        public void TheActionIndexPricesAPolicyReplacementOnceOneIsHeld()
        {
            var before = FindPolicyEntry();
            StringAssert.Contains("Free first adoption", before.cost,
                "the first adoption is free and the index should say so.");

            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));

            var after = FindPolicyEntry();
            StringAssert.Contains($"{StrategySystem.PolicyRevisionInfluence} INF", after.cost,
                "with a policy already in the NATIONAL slot the index still advertises a free adoption.");
            StringAssert.Contains("revise", after.cost,
                "the revision cost is not presented the way the doctrine entry presents its own.");
        }

        ActionEntry FindPolicyEntry()
        {
            foreach (var entry in StrategyActionCatalog.All(state))
                if (entry.label == "Adopt national policy") return entry;
            Assert.Fail("the COMMAND INDEX no longer carries a national-policy entry.");
            return null;
        }

        /// <summary>
        /// The policy block of the real view, isolated from the rest of the file.
        /// The wiring is a line in a view that no headless assertion reaches, so
        /// it is read from source — the same approach SettlementFogTests takes to
        /// the oracle identifiers, and scoped to the block so a match elsewhere
        /// in the file cannot satisfy it.
        /// </summary>
        static string PolicyBlockSource()
        {
            string source = ReadRuntimeSource(Path.Combine("UI", "Views", "StrategistView.cs"));
            int start = source.IndexOf("AvailablePolicies(state)", StringComparison.Ordinal);
            Assert.Greater(start, 0, "STRATEGIST no longer offers national policy at all.");
            int end = source.IndexOf("WRITE AN OBJECTIVE", start, StringComparison.Ordinal);
            Assert.Greater(end, start, "the policy block's end marker moved; rescope this reader.");
            return source.Substring(start, end - start);
        }

        static string ReadRuntimeSource(string relative)
        {
            string root = Path.Combine(Directory.GetCurrentDirectory(), "Assets", "Scripts");
            for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
                 !Directory.Exists(root) && dir != null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName, "Brink", "Assets", "Scripts");
                if (Directory.Exists(candidate)) root = candidate;
            }
            string path = Path.Combine(root, relative);
            Assert.IsTrue(File.Exists(path), $"cannot find {relative} from {Directory.GetCurrentDirectory()}");
            return File.ReadAllText(path);
        }
    }
}

using System;
using System.Linq;
using System.Reflection;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using Brink.UI.Views;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    public class StrategicFreedomTests
    {
        GameState s, prior;
        TurnManager priorTurns;
        GameController gc;

        [SetUp]
        public void SetUp()
        {
            Assert.IsNotNull(SaveSystem.SaveDirectoryOverride, "Controller commands must never touch the real save.");
            gc = GameController.Instance; prior = gc.State; priorTurns = gc.Turns;
            s = WorldFactory.CreateDebugWorld(4747);
            s.PlayerCountry.government.authorityUpgradeMask = 31;
            s.commandPoints.current = 100; s.PlayerCountry.resources.treasury = 10000;
            Attach(s);
        }

        void Attach(GameState state)
        {
            s = state;
            typeof(GameController).GetMethod("Attach", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(gc, new object[] { state });
        }

        [TearDown]
        public void TearDown()
        {
            typeof(GameController).GetProperty("State").SetValue(gc, prior);
            typeof(GameController).GetProperty("Turns").SetValue(gc, priorTurns);
            TerminalMetrics.ResetForTests();
        }

        [TestCase(ForceBranch.Ground)] [TestCase(ForceBranch.Air)] [TestCase(ForceBranch.Naval)]
        public void RetirementIsRealSelectivePricedAndSaved(ForceBranch branch)
        {
            var c = s.PlayerCountry; var force = c.military.Get(branch);
            var kind = AssetCatalog.All.First(a => a.branch == branch).kind;
            force.inventory.Ensure(kind).onOrder = 50;
            c.military.programs.Add(new ProcurementProgram { branch = branch, monthsRemaining = 12, costPerMonth = 20, label = "Retire me" });
            var other = branch == ForceBranch.Ground ? ForceBranch.Air : ForceBranch.Ground;
            float otherStrength = c.military.Get(other).strength, treasury = c.resources.treasury, power = c.military.TotalPower;
            int cp = s.commandPoints.current;
            Assert.IsTrue(gc.StandDownService(branch));
            Assert.AreEqual(cp - AcquisitionSystem.StandDownCost, s.commandPoints.current);
            Assert.AreEqual(treasury, c.resources.treasury, "No refund or civilian dividend.");
            Assert.IsTrue(force.replacementSuspended); Assert.AreEqual(0, force.strength); Assert.AreEqual(0, force.experience);
            Assert.IsTrue(force.inventory.stocks.All(x => x.count == 0 && x.onOrder == 0));
            Assert.IsFalse(c.military.programs.Any(p => p.branch == branch));
            Assert.AreEqual(otherStrength, c.military.Get(other).strength); Assert.Less(c.military.TotalPower, power);
            Assert.IsTrue(SaveSystem.Load().PlayerCountry.military.Get(branch).replacementSuspended);
            string before = SaveSystem.ToJson(s);
            Assert.IsFalse(gc.StandDownService(branch)); Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [TestCase("USA")] [TestCase("CHN")]
        public void AllAutomaticBuyersRespectSuspension(string id)
        {
            var c = s.FindCountry(id);
            foreach (ForceBranch b in Enum.GetValues(typeof(ForceBranch))) Assert.IsTrue(AcquisitionSystem.StandDownBy(s, id, b));
            c.FindOfficial(Pillar.Military).mode = ControlMode.Directed;
            c.FindOfficial(Pillar.Military).directiveId = MilitaryAdvice.PrepareForWar;
            Assert.IsNull(AcquisitionSystem.WorstShortfall(c, out _));
            string before = SaveSystem.ToJson(s);
            Assert.IsFalse(AcquisitionSystem.RestockRoutine(s, id, 1)); Assert.AreEqual(before, SaveSystem.ToJson(s));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(s));
            var turns = new TurnManager(loaded); SimulationPipeline.Wire(turns, loaded);
            for (int i = 0; i < 12; i++) Assert.IsTrue(turns.EndMonth());
            foreach (ForceBranch b in Enum.GetValues(typeof(ForceBranch)))
            {
                var force = loaded.FindCountry(id).military.Get(b);
                Assert.AreEqual(0, force.strength); Assert.IsTrue(force.replacementSuspended);
                Assert.IsTrue(force.inventory.stocks.All(x => x.onOrder == 0 && x.count == 0));
            }
        }

        [Test]
        public void AutomaticProgrammeFallbackCannotRebuildRetiredServicesButExplicitProgrammeCan()
        {
            var c = s.FindCountry("CHN"); c.resources.treasury = 10000;
            c.military.logistics = 100; c.military.warFooting = true;
            foreach (ForceBranch b in Enum.GetValues(typeof(ForceBranch))) AcquisitionSystem.StandDownBy(s, c.id, b);
            var rebuild = typeof(AISystem).GetMethod("RebuildForces", BindingFlags.NonPublic | BindingFlags.Static);
            var rng = new Random(42);
            for (int i = 0; i < 32; i++) rebuild.Invoke(null, new object[] { s, c, rng });
            Assert.IsEmpty(c.military.programs);
            Assert.IsTrue(MilitarySystem.BeginProcurementBy(s, c.id, ForceBranch.Air, MilitarySystem.ProgramScale.Modest));
            Assert.IsTrue(c.military.air.replacementSuspended);
            Assert.IsTrue(c.military.programs.Any(p => p.branch == ForceBranch.Air));
        }

        [Test]
        public void ExplicitOrdersStillWorkAndResumptionRestoresPermissionNotEquipment()
        {
            Assert.IsTrue(gc.StandDownService(ForceBranch.Air));
            float treasury = s.PlayerCountry.resources.treasury;
            Assert.IsTrue(gc.OrderAssets(AssetKind.Fighters, 10));
            Assert.AreEqual(treasury - AssetCatalog.CostOf(AssetKind.Fighters, 10), s.PlayerCountry.resources.treasury);
            AcquisitionSystem.MonthlyDeliveries(s);
            Assert.Greater(s.PlayerCountry.military.air.strength, 0);
            Assert.IsTrue(s.PlayerCountry.military.air.replacementSuspended);
            Assert.IsTrue(gc.StandDownService(ForceBranch.Air));
            int cp = s.commandPoints.current;
            Assert.IsTrue(gc.ResumeServiceReplacement(ForceBranch.Air));
            Assert.AreEqual(cp - AcquisitionSystem.ResumeReplacementCost, s.commandPoints.current);
            Assert.IsFalse(s.PlayerCountry.military.air.replacementSuspended);
            Assert.AreEqual(0, s.PlayerCountry.military.air.strength);
            var worst = AcquisitionSystem.WorstShortfall(s.PlayerCountry, out _);
            Assert.IsNotNull(worst); Assert.AreEqual(ForceBranch.Air, worst.branch);
            float before = s.PlayerCountry.resources.treasury;
            Assert.IsTrue(AcquisitionSystem.RestockRoutine(s, "USA", 1));
            Assert.Less(s.PlayerCountry.resources.treasury, before);
            Assert.Greater(s.PlayerCountry.military.air.inventory.stocks.Sum(x => x.onOrder), 0);
            string json = SaveSystem.ToJson(s);
            Assert.IsFalse(gc.ResumeServiceReplacement(ForceBranch.Air)); Assert.AreEqual(json, SaveSystem.ToJson(s));
        }

        [Test]
        public void InvalidOrUnaffordableChangesCannotDestroyTheForce()
        {
            s.commandPoints.current = 0;
            string before = SaveSystem.ToJson(s);
            Assert.IsFalse(gc.StandDownService(ForceBranch.Ground));
            Assert.IsFalse(AcquisitionSystem.StandDownBy(s, "missing", ForceBranch.Air));
            Assert.IsFalse(AcquisitionSystem.StandDownBy(s, "USA", (ForceBranch)99));
            Assert.IsFalse(AcquisitionSystem.ResumeReplacementBy(s, "missing", ForceBranch.Ground));
            Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [Test]
        public void AuthorityRemainsARealConstraintOnBothExits()
        {
            s.PlayerCountry.government.authorityUpgradeMask = 0;
            s.PlayerCountry.government.type = GovernmentType.ParliamentaryRepublic;
            s.politicalCapital = 0;
            s.PlayerCountry.endgames.preparations.Add(new EndgamePreparation { type = EndgameType.SystemicCollapse, progress = 100 });
            float strength = s.PlayerCountry.military.ground.strength;
            int cp = s.commandPoints.current;
            Assert.IsFalse(gc.StandDownService(ForceBranch.Ground));
            Assert.IsFalse(gc.AbandonEndgame(EndgameType.SystemicCollapse));
            Assert.AreEqual(strength, s.PlayerCountry.military.ground.strength);
            Assert.AreEqual(100, s.PlayerCountry.endgames.ProgressFor(EndgameType.SystemicCollapse));
            Assert.AreEqual(cp, s.commandPoints.current);
        }

        [TestCase(EndgameType.StrategicDestruction)] [TestCase(EndgameType.SystemicCollapse)]
        [TestCase(EndgameType.StateDestabilization)] [TestCase(EndgameType.StrategicIsolation)] [TestCase(EndgameType.TotalMobilization)]
        public void PreparedWorkCanBeAbandonedWithoutErasingHistory(EndgameType type)
        {
            var e = s.PlayerCountry.endgames;
            e.preparations.Clear(); e.preparations.Add(new EndgamePreparation { type = type, progress = 100, everUsed = true });
            e.totalMobilization = true; e.mobilizationMonthsRemaining = 8;
            s.endgameRecords.Add(new EndgameRecord { actorId = "USA", type = type, severity = StrategicSeverity.Existential, summary = "Past use" });
            float treasury = s.PlayerCountry.resources.treasury, recidivism = EndgameSystem.Recidivism(s, "USA");
            int cp = s.commandPoints.current;
            Assert.IsTrue(gc.AbandonEndgame(type));
            Assert.AreEqual(cp - EndgameSystem.AbandonCost, s.commandPoints.current);
            Assert.AreEqual(0, e.ProgressFor(type)); Assert.IsTrue(e.Find(type).everUsed);
            Assert.AreEqual(treasury, s.PlayerCountry.resources.treasury);
            Assert.IsTrue(e.totalMobilization); Assert.AreEqual(8, e.mobilizationMonthsRemaining);
            Assert.AreEqual(recidivism, EndgameSystem.Recidivism(s, "USA")); Assert.AreEqual("Past use", s.endgameRecords.Last().summary);
            Assert.AreEqual(0, SaveSystem.Load().PlayerCountry.endgames.ProgressFor(type));
            string before = SaveSystem.ToJson(s);
            Assert.IsFalse(gc.AbandonEndgame(type)); Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [Test]
        public void AbandonmentRemovesCurrentAlarmNotResearchAndMustBeFundedAgain()
        {
            var c = s.PlayerCountry;
            c.endgames.preparations.Clear(); c.endgames.preparations.Add(new EndgamePreparation { type = EndgameType.StrategicDestruction, progress = 100 });
            Assert.AreEqual(100, EndgameSystem.KnownPreparation(s, "CHN", "USA", EndgameType.StrategicDestruction));
            s.commandPoints.current = 0; string before = SaveSystem.ToJson(s);
            Assert.IsFalse(gc.AbandonEndgame(EndgameType.StrategicDestruction)); Assert.AreEqual(before, SaveSystem.ToJson(s));
            s.commandPoints.current = 10; Assert.IsTrue(gc.AbandonEndgame(EndgameType.StrategicDestruction));
            Assert.LessOrEqual(EndgameSystem.KnownPreparation(s, "CHN", "USA", EndgameType.StrategicDestruction), 0);
            c.resources.treasury = 0;
            Assert.IsFalse(EndgameSystem.PrepareBy(s, "USA", EndgameType.StrategicDestruction));
            Assert.AreEqual(0, c.endgames.ProgressFor(EndgameType.StrategicDestruction));
        }

        [Test]
        public void MissingLegacyFlagKeepsOrdinaryReplacement()
        {
            s.PlayerCountry.military.air.replacementSuspended = true;
            string json = SaveSystem.ToJson(s).Replace("\"replacementSuspended\"", "\"unusedLegacyField\"");
            var legacy = SaveSystem.FromJson(json);
            Assert.IsFalse(legacy.PlayerCountry.military.air.replacementSuspended);
            Assert.IsTrue(SaveSystem.FromJson(SaveSystem.ToJson(s)).PlayerCountry.military.air.replacementSuspended);
        }

        [TestCase(34)] [TestCase(49)] [TestCase(64)] [TestCase(104)]
        public void BothPanelsExplainTheExitAndButtonsTargetTheirOwnService(int columns)
        {
            TerminalMetrics.Update((columns + 1) * 8f, 8f, 500, Breakpoints.FromColumns(columns));
            var view = new MilitaryView(); view.Refresh();
            var button = view.Root.Query<Button>().ToList().Single(b => b.text.StartsWith("RETIRE ALL AIR"));
            var clickable = typeof(Button).GetProperty("clickable")?.GetValue(button);
            if (clickable != null) clickable.GetType().GetMethod("Invoke", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(clickable, new object[] { null });
            else typeof(Button).GetMethod("SendClick").Invoke(button, null);
            Assert.IsTrue(s.PlayerCountry.military.air.replacementSuspended);
            Assert.IsFalse(s.PlayerCountry.military.ground.replacementSuspended);
            view.Refresh(); var endgame = new EndgameView(); endgame.Refresh();
            foreach (var root in new[] { view.Root, endgame.Root })
            {
                var text = root.Query<Label>().ToList().Where(l => (l.text ?? "").Contains("forfeit") || (l.text ?? "").Contains("Abandoning")).ToArray();
                Assert.IsNotEmpty(text);
                foreach (var label in text) foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                foreach (var b in root.Query<Button>().ToList().Where(b => b.text.StartsWith("RETIRE ALL") || b.text.StartsWith("RESUME ") || b.text.StartsWith("ABANDON PREPARATION")))
                    Assert.LessOrEqual(b.text.Length, columns);
            }
            string before = SaveSystem.ToJson(s); view.Refresh(); endgame.Refresh();
            Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [Test]
        public void ExistingTreatyAndTradeExitsPermitIsolationButKeepTheBill()
        {
            var r = s.FindRelationship("USA", "CHN"); r.relations = 80; r.trust = 80;
            s.treaties.Clear(); var treaty = new Treaty { countryA = "USA", countryB = "CHN", signedDate = s.date };
            treaty.commitments.Add(TreatyCommitment.MutualDefense); s.treaties.Add(treaty);
            Assert.IsTrue(gc.BreakTreaty("CHN")); Assert.IsTrue(treaty.broken); Assert.Less(r.trust, 80);
            foreach (var partner in s.trade.Where(l => l.Involves("USA")).Select(l => l.PartnerOf("USA")).Distinct().ToArray())
                Assert.IsTrue(gc.WithdrawFromTrade(partner));
            Assert.IsFalse(s.trade.Any(l => l.Involves("USA")));
            Assert.AreEqual(0, TradeSystem.Supply(s, "USA", TradeFocus.Energy));
        }
    }
}

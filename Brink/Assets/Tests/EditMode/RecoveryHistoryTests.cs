using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using System.Linq;
using System.Reflection;
using Brink.UI;
using Brink.UI.Views;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    public class RecoveryHistoryTests
    {
        static GameState World() => WorldFactory.CreateDebugWorld(998);
        static void Change(GameState s, CausalMetric metric, float delta, int age = 0, string owner = "USA")
        {
            int date = s.date.year * 12 + s.date.month - 1 - age;
            s.causal.records.Add(new CausalRecord { countryId = owner, metric = metric,
                year = date / 12, month = date % 12 + 1, delta = delta });
        }

        [TestCase(0, true)] [TestCase(11, true)] [TestCase(12, false)] [TestCase(-1, false)]
        public void OnlyActualObservationsInsideTheWindowCount(int age, bool included)
        {
            var s = World(); Change(s, CausalMetric.GovernmentApproval, 5, age);
            var reading = RecoveryHistorySystem.Read(s);
            Assert.AreEqual(included ? 1 : 0, reading.observedRecentMetrics);
            Assert.AreEqual(included ? 1 : 0, reading.improvingRecentMetrics);
        }

        [Test]
        public void EmptyOrForeignEvidenceDoesNotClaimRecoveryAfterTurnover()
        {
            var s = World(); s.administrationsServed = 4; s.PlayerCountry.warsLost = 3;
            Change(s, CausalMetric.GovernmentApproval, 8, owner: "CHN");
            Assert.AreEqual("NO RECENT EVIDENCE", RecoveryHistorySystem.Read(s).status);
            Assert.AreEqual(0, RecoveryHistorySystem.Read(s).observedRecentMetrics);
            s.causal.records = null;
            Assert.AreEqual("NO RECENT EVIDENCE", RecoveryHistorySystem.Read(s).status);
        }

        [Test]
        public void LatestPerMetricIsNotAWholeYearTrendAndLossesRemain()
        {
            var s = World(); s.PlayerCountry.warsLost = 2;
            Change(s, CausalMetric.Treasury, 100, 2);
            Change(s, CausalMetric.Treasury, -1, 1);
            Change(s, CausalMetric.SovereignDebt, -3, 1);
            Change(s, CausalMetric.SocialUnrest, -4, 1);
            var reading = RecoveryHistorySystem.Read(s);
            Assert.AreEqual(3, reading.observedRecentMetrics);
            Assert.AreEqual(2, reading.improvingRecentMetrics);
            Assert.AreEqual(1, reading.deterioratingRecentMetrics);
            Assert.AreEqual("RECOVERY AFTER SETBACK", reading.status);
            StringAssert.Contains("not a year-long trend", RecoveryHistorySystem.Render(s));
            Assert.AreEqual(2, s.PlayerCountry.warsLost);
        }

        [Test]
        public void OptionsReadCurrentOwnPressureWithoutRevealingForeignConditions()
        {
            var s = World(); s.sanctions.Clear(); s.treaties.Clear();
            s.PlayerCountry.fiscal.sovereignDebt = 500;
            s.PlayerCountry.resources.treasury = -1000;
            s.PlayerCountry.stability = 10;
            s.sanctions.Add(new Sanction { senderId = "CHN", targetId = "USA" });
            string before = SaveSystem.ToJson(s), text = RecoveryHistorySystem.Options(s);
            StringAssert.Contains("DOMESTIC BREAKDOWN", text);
            StringAssert.Contains("ECONOMIC SETBACK", text);
            StringAssert.Contains("1 sanction regimes", text);
            StringAssert.Contains("NO ACTIVE TREATY", text);
            StringAssert.Contains("not guarantee consent", text);
            Assert.AreEqual(before, SaveSystem.ToJson(s));
            s.FindCountry("CHN").resources.treasury = -99999;
            s.FindCountry("CHN").stability = 0;
            Assert.AreEqual(text, RecoveryHistorySystem.Options(s));
            s.sanctions[0].targetId = "RUS";
            Assert.IsFalse(RecoveryHistorySystem.Options(s).Contains("EXTERNAL PRESSURE"));
        }

        [Test]
        public void DormantAndExpiredPromisesDoNotMasqueradeAsActivePartners()
        {
            var s = World(); s.treaties.Clear();
            var treaty = new Treaty { countryA = "USA", countryB = "CHN", signedDate = s.date };
            treaty.commitments.Add(TreatyCommitment.Transit);
            treaty.clauses.Add(new TreatyClause { commitment = TreatyCommitment.Transit,
                trigger = TreatyClauseTrigger.ConflictWithCountry, triggerCountryId = "RUS" });
            s.treaties.Add(treaty);
            StringAssert.Contains("NO ACTIVE TREATY", RecoveryHistorySystem.Options(s));
            treaty.clauses.Clear();
            Assert.IsFalse(RecoveryHistorySystem.Options(s).Contains("NO ACTIVE TREATY"));
            treaty.clauses.Add(new TreatyClause { commitment = TreatyCommitment.Transit,
                durationMonths = 1, effectiveDate = new GameDate(1983, 1) });
            StringAssert.Contains("NO ACTIVE TREATY", RecoveryHistorySystem.Options(s));
            treaty.clauses.Clear(); treaty.broken = true;
            StringAssert.Contains("NO ACTIVE TREATY", RecoveryHistorySystem.Options(s));
        }

        [Test]
        public void ReunitedSuccessorDoesNotKeepAdvertisingAnUnfinishedSecession()
        {
            var s = World(); var c = s.PlayerCountry;
            c.government.inCivilConflict = true; c.government.civilConflictMonthsElapsed = 12;
            c.nationalUnity = 8; c.government.militaryLoyalty = 18;
            var child = SecessionSystem.Fracture(s, c, new System.Random(3)); Assert.IsNotNull(child);
            StringAssert.Contains("SECESSION", RecoveryHistorySystem.Options(s));
            ConquestSystem.Absorb(s, c, child);
            Assert.IsFalse(RecoveryHistorySystem.Options(s).Contains("SECESSION"));
            Assert.IsTrue(s.chronicle.Any(e => e.text.Contains("secedes")));
        }

        [Test]
        public void ActualReunificationCanPayItsRecoveryDividendOnlyOnce()
        {
            var s = World(); var c = s.PlayerCountry;
            c.government.inCivilConflict = true; c.government.civilConflictMonthsElapsed = 12;
            c.nationalUnity = 8; c.government.militaryLoyalty = 18;
            var child = SecessionSystem.Fracture(s, c, new System.Random(3)); Assert.IsNotNull(child);
            s.treaties.Clear(); s.confrontations.Clear();
            foreach (var relation in s.relationships)
            { relation.relations = 50; relation.trust = 50; relation.strategicAlignment = 50; }
            var pair = s.FindRelationship(c.id, child.id);
            pair.relations = 100; pair.trust = 100; pair.strategicAlignment = 100;
            s.date = new GameDate(1987, 1);
            Assert.IsTrue(SecessionSystem.CanReunify(s, c, child));
            float unity = c.nationalUnity;
            Assert.IsTrue(SecessionSystem.Reunify(s, c, child));
            Assert.AreEqual(unity + 10, c.nationalUnity);
            Assert.IsFalse(s.locations.Any(l => l.originalOwnerId == child.id));
            Assert.IsFalse(SecessionSystem.CanReunify(s, c, child), "An already reunited state must not pay a second recovery dividend.");
            string before = SaveSystem.ToJson(s);
            Assert.IsFalse(SecessionSystem.Reunify(s, c, child));
            Assert.AreEqual(before, SaveSystem.ToJson(s));
            s = SaveSystem.FromJson(SaveSystem.ToJson(s));
            for (int i = 0; i < 12; i++)
            {
                s.date = s.date.NextMonth(); before = SaveSystem.ToJson(s);
                SecessionSystem.MonthlyUpdate(s);
                Assert.AreEqual(before, SaveSystem.ToJson(s), "Monthly rechecking must not absorb or reward the same successor again after load.");
            }
        }

        static void Setback(GameState s, string kind)
        {
            var c = s.PlayerCountry;
            if (kind == "defeat" || kind == "conquest")
            {
                var prize = s.locations.First(l => l.originalOwnerId == "USA");
                var war = ConfrontationSystem.BeginBy(s, "CHN", "USA", ConfrontationObjective.TerritorialConcession,
                    prize.id, PrimaryStrategy.Military); Assert.IsNotNull(war);
                ConfrontationSystem.SetEscalationBy(s, war, EscalationState.LimitedConflict, "CHN");
                Assert.AreEqual(EscalationState.LimitedConflict, war.escalation);
                prize.ownerId = "CHN"; // The declared objective was actually lost, not merely conceded in prose.
                if (kind == "conquest")
                {
                    foreach (var site in s.locations.Where(l => l.originalOwnerId == "USA")) site.ownerId = "CHN";
                    ConquestSystem.CheckForTotalConquest(s, war);
                    Assert.IsFalse(s.locations.Any(l => l.originalOwnerId == "USA"));
                }
                else ConfrontationSystem.CloseWithSettlement(s, war, "CHN", "A costly defeat; the posting continues.");
                Assert.IsTrue(war.resolved); Assert.Greater(c.warsLost, 0);
            }
            if (kind == "coup")
            {
                c.stability = 10;
                typeof(RegimeSystem).GetMethod("SucceedingCoup", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { s, c, new System.Random(3) });
                Assert.AreEqual(1, c.government.coupsExperienced);
                Assert.IsTrue(c.government.inCivilConflict);
            }
            if (kind == "secession")
            {
                c.government.inCivilConflict = true; c.government.civilConflictMonthsElapsed = 12;
                c.nationalUnity = 8; c.government.militaryLoyalty = 18;
                Assert.IsNotNull(SecessionSystem.Fracture(s, c, new System.Random(3)));
            }
            if (kind == "crash")
            {
                c.fiscal.sovereignDebt = 4000; c.fiscal.creditStanding = 0;
                c.resources.treasury = -1000; c.economy.growthRate = -10;
                Assert.AreEqual(FiscalCondition.Crisis, FiscalSystem.ConditionOf(s, c));
            }
            if (kind == "isolation")
            {
                s.treaties.Clear(); s.trade.Clear(); s.sanctions.Clear();
                foreach (var other in s.countries.Where(x => x.id != c.id))
                    s.sanctions.Add(new Sanction { senderId = other.id, targetId = c.id, severity = SanctionSeverity.Severe });
            }
        }

        [Test]
        public void OccupationAndAnnexationHaveDifferentReadingsNotFreeRestoration()
        {
            var s = World(); Setback(s, "defeat");
            StringAssert.Contains(s.locations.Count(l => l.ownerId == "USA") + " controlled;", RecoveryHistorySystem.Options(s));
            StringAssert.Contains("1 titled sites held by others", RecoveryHistorySystem.Options(s));
            s = World(); Setback(s, "conquest");
            string before = SaveSystem.ToJson(s);
            StringAssert.Contains("0 controlled; 0 titled sites held by others", RecoveryHistorySystem.Options(s));
            StringAssert.Contains("NO CONTROLLED GROUND", RecoveryHistorySystem.Options(s));
            StringAssert.Contains("no automatic restoration", RecoveryHistorySystem.Options(s));
            Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [Test]
        public void WarAdviceClearsAfterSettlementButDefeatRemainsOnTheFile()
        {
            var s = World();
            var war = ConfrontationSystem.BeginBy(s, "CHN", "USA", ConfrontationObjective.Deterrence,
                null, PrimaryStrategy.Military);
            Assert.IsNotNull(war);
            ConfrontationSystem.SetEscalationBy(s, war, EscalationState.LimitedConflict, "CHN");
            StringAssert.Contains("WAR CONTINUES", RecoveryHistorySystem.Options(s));
            war.defenderWarExhaustion = 90;
            ConfrontationSystem.CloseWithSettlement(s, war, "CHN", "The war ended.");
            Assert.IsFalse(RecoveryHistorySystem.Options(s).Contains("WAR CONTINUES"));
            StringAssert.Contains("AFTER DEFEAT", RecoveryHistorySystem.Options(s));
            Assert.AreEqual(1, RecoveryHistorySystem.Read(s).warsLost);
        }

        [Test]
        public void ReadingEveryMonthCannotChangeARecoveringWorld()
        {
            var observed = World(); Setback(observed, "crash");
            var control = SaveSystem.FromJson(SaveSystem.ToJson(observed));
            // Round-trip both arms so native null materialization is symmetric.
            observed = SaveSystem.FromJson(SaveSystem.ToJson(observed));
            var a = new TurnManager(observed); SimulationPipeline.Wire(a, observed);
            var b = new TurnManager(control); SimulationPipeline.Wire(b, control);
            for (int i = 0; i < 12; i++)
            {
                string before = SaveSystem.ToJson(observed);
                RecoveryHistorySystem.Render(observed);
                Assert.AreEqual(before, SaveSystem.ToJson(observed));
                a.EndMonth(); b.EndMonth();
            }
            Assert.AreEqual(SaveSystem.ToJson(control), SaveSystem.ToJson(observed));
        }

        [TestCase("defeat")] [TestCase("conquest")] [TestCase("coup")]
        [TestCase("secession")] [TestCase("crash")] [TestCase("isolation")]
        public void CatastropheSurvivesSaveAndStillAdvancesTheRealWorld(string kind)
        {
            var s = World(); s.strategistXP = 500; s.skillPoints = 3;
            s.AddChronicle(ChronicleCategory.System, "USA", "Posting history before the setback.");
            Setback(s, kind);
            int losses = s.PlayerCountry.warsLost, countries = s.countries.Count;
            var history = s.chronicle.Select(e => e.text).ToArray();
            s = SaveSystem.FromJson(SaveSystem.ToJson(s));
            var date = s.date;
            var turns = new TurnManager(s); SimulationPipeline.Wire(turns, s);
            for (int i = 0; i < 3; i++) Assert.IsTrue(turns.EndMonth());
            Assert.AreEqual(3, s.date.MonthsSince(date));
            Assert.AreEqual("USA", s.playerCountryId); Assert.IsNotNull(s.PlayerCountry);
            Assert.GreaterOrEqual(s.strategistXP, 500); Assert.GreaterOrEqual(s.skillPoints, 3);
            Assert.GreaterOrEqual(s.PlayerCountry.warsLost, losses);
            Assert.GreaterOrEqual(s.countries.Count, countries);
            foreach (string entry in history) Assert.IsTrue(s.chronicle.Any(e => e.text == entry));
            Assert.Greater(s.commandPoints.current, 0);
            StringAssert.Contains("THIS OFFICE CONTINUES", RecoveryHistorySystem.Render(s));
        }

        [Test]
        public void RecoveryOrderPaysItsOrdinaryPriceAndDoesNotEraseTheFailure()
        {
            Assert.IsNotNull(SaveSystem.SaveDirectoryOverride, "Never invoke autosaving commands without assembly isolation.");
            var gc = GameController.Instance; var prior = gc.State; var priorTurns = gc.Turns; var s = World();
            try
            {
                typeof(GameController).GetMethod("Attach", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(gc, new object[] { s });
                s.PlayerCountry.warsLost = 2; s.PlayerCountry.fiscal.sovereignDebt = 1000;
                s.PlayerCountry.resources.treasury = -50; s.politicalCapital = 0;
                s.PlayerCountry.government.type = GovernmentType.CentralizedRepublic;
                Assert.IsFalse(gc.RestructureDebt()); Assert.AreEqual(1000, s.PlayerCountry.fiscal.sovereignDebt);
                s.politicalCapital = 100; float credit = s.PlayerCountry.fiscal.creditStanding;
                Assert.IsTrue(gc.RestructureDebt());
                Assert.AreEqual(100 - FiscalSystem.RestructureCost, s.politicalCapital);
                Assert.AreEqual(500, s.PlayerCountry.fiscal.sovereignDebt); Assert.AreEqual(0, s.PlayerCountry.resources.treasury);
                Assert.Less(s.PlayerCountry.fiscal.creditStanding, credit);
                Assert.AreEqual(2, s.PlayerCountry.warsLost);
                Assert.IsTrue(s.chronicle.Any(e => e.text.Contains("restructures")));
                Assert.IsTrue(gc.EndMonth()); Assert.IsTrue(gc.IsRunning);
                Assert.AreEqual(s.date, SaveSystem.Load().date);
            }
            finally
            {
                typeof(GameController).GetProperty("State").SetValue(gc, prior);
                typeof(GameController).GetProperty("Turns").SetValue(gc, priorTurns);
            }
        }

        [TestCase(34)] [TestCase(49)] [TestCase(64)] [TestCase(104)]
        public void RealRecoveryPanelIsWrappedHonestAndDoesNotIssueOrders(int columns)
        {
            var s = World(); Setback(s, "crash"); Setback(s, "isolation");
            StrategySystem.Ensure(s); // Existing view seeds the strategy, not this reader.
            var gc = GameController.Instance; var prior = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, s);
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500, Breakpoints.FromColumns(columns));
                string before = SaveSystem.ToJson(s);
                var view = new StrategistView(); view.Refresh();
                var label = view.Root.Query<Label>().ToList().Single(l => (l.text ?? "").Contains("RECOVERY FILE"));
                StringAssert.Contains("ECONOMIC SETBACK", label.text.Replace("\n", " "));
                StringAssert.Contains("EXTERNAL PRESSURE", label.text.Replace("\n", " "));
                foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                string first = label.text; view.Refresh();
                Assert.AreEqual(first, view.Root.Query<Label>().ToList().Single(l => (l.text ?? "").Contains("RECOVERY FILE")).text);
                Assert.AreEqual(before, SaveSystem.ToJson(s));
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, prior); TerminalMetrics.ResetForTests(); }
        }

        [Test]
        public void SetbackCanCoexistWithMeasuredRecovery()
        {
            var state = WorldFactory.CreateDebugWorld(998);
            state.PlayerCountry.warsLost = 1;
            state.causal.records.Add(new CausalRecord { metric = CausalMetric.GovernmentApproval, countryId = state.playerCountryId, year = state.date.year, month = state.date.month, previous = 30f, resulting = 35f, delta = 5f });
            state.causal.records.Add(new CausalRecord { metric = CausalMetric.SocialUnrest, countryId = state.playerCountryId, year = state.date.year, month = state.date.month, previous = 70f, resulting = 65f, delta = -5f });
            var reading = RecoveryHistorySystem.Read(state);
            Assert.AreEqual("RECOVERY AFTER SETBACK", reading.status);
            Assert.AreEqual(1, reading.warsLost);
        }

        [Test]
        public void RecoveryReaderIsReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(999);
            string before = SaveSystem.ToJson(state);
            RecoveryHistorySystem.Render(state);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }
    }
}

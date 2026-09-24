using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using System.Linq;
using Brink.UI;
using Brink.UI.Views;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    public class StrategicEraTests
    {
        static GameState Blank()
        {
            var s = WorldFactory.CreateDebugWorld(982);
            s.treaties.Clear(); s.sanctions.Clear(); s.networks.Clear(); s.chronicle.Clear();
            s.PlayerCountry.military.posture = MilitaryPosture.Peacetime;
            return s;
        }

        static void Months(GameState s, int count)
        {
            for (int i = 0; i < count; i++) { StrategicEraSystem.RecordMonth(s); s.date = s.date.NextMonth(); }
        }

        static void Pattern(GameState s, int index)
        {
            if (index == 0 || index == 4)
                s.treaties.Add(new Treaty { countryA = "USA", countryB = "CHN", signedDate = s.date,
                    commitments = new System.Collections.Generic.List<TreatyCommitment> { index == 0 ? TreatyCommitment.MutualDefense : TreatyCommitment.TradePreference } });
            if (index == 1) s.sanctions.Add(new Sanction { senderId = "USA", targetId = "CHN" });
            if (index == 2) s.PlayerCountry.military.posture = MilitaryPosture.Forward;
            if (index == 3) s.networks.Add(new IntelNetwork { ownerId = "USA", targetId = "CHN", penetration = 1 });
        }

        [Test]
        public void ChosenDoctrineAndCurrentPressureCannotAwardAnEarnedName()
        {
            var s = Blank(); var plan = StrategySystem.Ensure(s);
            plan.doctrineChosen = true; plan.doctrine = StrategicDoctrine.Deterrence; plan.doctrineAdopted = s.date;
            s.PlayerCountry.socialUnrest = 90;
            string before = SaveSystem.ToJson(s);
            StringAssert.Contains("No five-year", StrategicEraSystem.RenderEarned(s, "USA"));
            StringAssert.Contains("DECLARED", StrategicEraSystem.Render(s));
            Assert.AreEqual(before, SaveSystem.ToJson(s));
            Assert.IsNull(s.PlayerCountry.strategicConduct);
        }

        [TestCase(0, "Defence Partnership")]
        [TestCase(1, "Economic Pressure")]
        [TestCase(2, "Forward Presence")]
        [TestCase(3, "Collection Network")]
        [TestCase(4, "Commercial Partnership")]
        public void EachPatternNeedsObservedDurationNotOnePresentState(int index, string name)
        {
            var s = Blank(); Pattern(s, index); Months(s, 59);
            Assert.IsEmpty(s.chronicle);
            Assert.IsNull(s.PlayerCountry.strategicConduct.lastEarnedName);
            Months(s, 1);
            Assert.AreEqual(name, s.PlayerCountry.strategicConduct.lastEarnedName);
            var entry = s.chronicle.Single();
            Assert.AreEqual(HistoricalEvent.ConductReview, entry.historicalEvent);
            Assert.AreEqual(Publicity.Secret, entry.publicity);
            Assert.AreEqual("USA", entry.countryId);
            StringAssert.Contains(name + " Doctrine / " + name + " Era", entry.text);
            StringAssert.Contains("JAN 1984 to DEC 1988", entry.text);
            StringAssert.Contains("60/60", entry.text);
            Assert.AreEqual(0, s.PlayerCountry.strategicConduct.months);
        }

        [TestCase(35, false)] [TestCase(36, true)]
        public void SustainedThresholdIsPinnedFromBothSides(int held, bool named)
        {
            var s = Blank(); Pattern(s, 2); Months(s, held);
            s.PlayerCountry.military.posture = MilitaryPosture.Peacetime; Months(s, 60 - held);
            Assert.AreEqual(named, s.PlayerCountry.strategicConduct.lastEarnedName != null);
            StringAssert.Contains("Forward military posture " + held + "/60", s.chronicle.Single().text);
        }

        [Test]
        public void DurationRanksTheNameAndTiesUseStableOrderNotRandomness()
        {
            var s = Blank(); Pattern(s, 2); Months(s, 10); Pattern(s, 1); Months(s, 10);
            Pattern(s, 0); Pattern(s, 3); Months(s, 40);
            Assert.AreEqual("Forward Presence and Economic Pressure", s.PlayerCountry.strategicConduct.lastEarnedName);
            var tied = Blank(); for (int i = 0; i < 5; i++) Pattern(tied, i); Months(tied, 60);
            Assert.AreEqual("Defence Partnership and Economic Pressure", tied.PlayerCountry.strategicConduct.lastEarnedName);
            StringAssert.Contains("active trade-preference agreement 60/60", tied.chronicle.Single().text);
            Assert.AreEqual(0, tied.actionSequence);
        }

        [Test]
        public void DuplicateAndBackwardsCallsNeitherInflateNorRewriteHistory()
        {
            var s = Blank(); Pattern(s, 2); StrategicEraSystem.RecordMonth(s);
            string before = SaveSystem.ToJson(s);
            StrategicEraSystem.RecordMonth(s); Assert.AreEqual(before, SaveSystem.ToJson(s));
            s.date = s.date.NextMonth(); Months(s, 10); s.date = s.PlayerCountry.strategicConduct.lastObserved;
            before = SaveSystem.ToJson(s);
            StrategicEraSystem.RecordMonth(s); Assert.AreEqual(before, SaveSystem.ToJson(s), "A repeated close must not restart accumulated history.");
            s.date = new GameDate(1983, 12); before = SaveSystem.ToJson(s);
            StrategicEraSystem.RecordMonth(s); Assert.AreEqual(before, SaveSystem.ToJson(s));
        }

        [Test]
        public void MissingMonthsRestartTheWindowWithoutInventingObservations()
        {
            var s = Blank(); Pattern(s, 2); Months(s, 59); s.date = s.date.NextMonth();
            StrategicEraSystem.RecordMonth(s);
            Assert.AreEqual(1, s.PlayerCountry.strategicConduct.months);
            Assert.AreEqual(s.date, s.PlayerCountry.strategicConduct.since);
            Assert.AreEqual(1, s.PlayerCountry.strategicConduct.heldMonths[2]); Assert.IsEmpty(s.chronicle);
        }

        [Test]
        public void LegacyAndSuccessorRecordsStartNowNotAtTheParentEpoch()
        {
            var s = Blank(); s.date = new GameDate(2024, 1); Pattern(s, 2);
            // Unity writes a null serializable class as a default object, unlike
            // the author harness. Rename the key so the reader sees no known
            // field, without depending on either serializer's value spelling.
            string json = SaveSystem.ToJson(s).Replace("\"strategicConduct\"", "\"ignoredLegacyConduct\"");
            Assert.IsFalse(json.Contains("\"strategicConduct\""));
            s.PlayerCountry.strategicConduct = new StrategicConductRecord();
            foreach (string legacy in new[] { json, SaveSystem.ToJson(s) })
            {
                var loaded = SaveSystem.FromJson(legacy); Months(loaded, 1);
                var record = loaded.PlayerCountry.strategicConduct;
                Assert.AreEqual(1, record.months); Assert.IsEmpty(loaded.chronicle);
                Assert.AreEqual(new GameDate(2024, 1), record.since);
                CollectionAssert.AreEqual(new[] { 0, 0, 1, 0, 0 }, record.heldMonths);
                Assert.IsTrue(string.IsNullOrEmpty(record.lastEarnedName));
            }
            s.countries.Add(new CountryState { id = "NEW", displayName = "Successor", foundedDate = s.date });
            s.playerCountryId = "NEW"; Months(s, 1);
            Assert.AreEqual(1, s.PlayerCountry.strategicConduct.months);
            Assert.IsNull(s.PlayerCountry.strategicConduct.lastEarnedName);
        }

        [Test]
        public void OnlyLiveOwnPoliciesCountAndDuplicatesDoNotMultiplyMonths()
        {
            var s = Blank(); Pattern(s, 0); Pattern(s, 0); Pattern(s, 3); Pattern(s, 3);
            s.sanctions.Add(new Sanction { senderId = "CHN", targetId = "USA" });
            StrategicEraSystem.RecordMonth(s);
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 1, 0 }, s.PlayerCountry.strategicConduct.heldMonths);
            s.date = s.date.NextMonth(); s.treaties.ForEach(t => t.broken = true);
            s.networks.ForEach(n => n.compromised = true); StrategicEraSystem.RecordMonth(s);
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 1, 0 }, s.PlayerCountry.strategicConduct.heldMonths);
            Assert.IsNull(s.FindCountry("CHN").strategicConduct);
        }

        [Test]
        public void ExpiredAndConditionalAgreementsUseTheirRealActivationRule()
        {
            var s = Blank(); Pattern(s, 0); var treaty = s.treaties.Single();
            treaty.clauses.Add(new TreatyClause { commitment = TreatyCommitment.MutualDefense,
                durationMonths = 1, effectiveDate = s.date });
            Months(s, 2); Assert.AreEqual(1, s.PlayerCountry.strategicConduct.heldMonths[0]);
            treaty.clauses[0].durationMonths = 0;
            treaty.clauses[0].trigger = TreatyClauseTrigger.ConflictWithCountry; treaty.clauses[0].triggerCountryId = "RUS";
            Months(s, 1); Assert.AreEqual(1, s.PlayerCountry.strategicConduct.heldMonths[0]);
        }

        [Test]
        public void LaterHistoriansReferenceTheOriginalNameEvenAfterAReversal()
        {
            var s = Blank(); Pattern(s, 2); Months(s, 60); string first = s.chronicle.Single().text;
            s.PlayerCountry.military.posture = MilitaryPosture.Peacetime; Months(s, 60);
            Assert.AreEqual(2, s.chronicle.Count); Assert.AreEqual(first, s.chronicle[0].text);
            StringAssert.Contains("No settled doctrine", s.chronicle[1].text);
            StringAssert.Contains("Looking back to DEC 1988: Forward Presence Doctrine", s.chronicle[1].text);
            Pattern(s, 1); Months(s, 60);
            StringAssert.Contains("Economic Pressure Doctrine", s.chronicle[2].text);
            StringAssert.Contains("Forward Presence Doctrine remains in the archive", s.chronicle[2].text);
            Months(s, 60); StringAssert.Contains("this window sustains that reading", s.chronicle[3].text);
            Assert.AreEqual(new GameDate(1998, 12), s.PlayerCountry.strategicConduct.lastEarnedDate, "Continuing a named era must not move its original recognition date.");
        }

        [Test]
        public void ClassifierWritesOnlyOwnHistoryAndHasNoMechanicalReward()
        {
            var s = Blank(); Pattern(s, 2); Months(s, 59);
            var control = SaveSystem.FromJson(SaveSystem.ToJson(s));
            StrategicEraSystem.RecordMonth(s);
            s.PlayerCountry.strategicConduct = control.PlayerCountry.strategicConduct;
            s.chronicle.RemoveAll(e => e.historicalEvent == HistoricalEvent.ConductReview);
            Assert.AreEqual(SaveSystem.ToJson(control), SaveSystem.ToJson(s));
        }

        [Test]
        public void ForeignPortraitCannotReadClassifiedPostingEvidence()
        {
            var s = Blank(); Pattern(s, 3); Months(s, 60);
            s.playerCountryId = "CHN";
            Assert.AreEqual("", StrategicEraSystem.RenderEarned(s, "USA"));
            Assert.IsFalse(WorldWire.CanShow(s, s.chronicle.Single()));
            Assert.IsFalse(HistoricalIdentitySystem.Render(s, "USA").Contains("Collection Network"));
        }

        [Test]
        public void ActualPipelineArchivesAndResumesTheSameSixtyMonthWindow()
        {
            var a = Blank(); var b = SaveSystem.FromJson(SaveSystem.ToJson(a));
            var ta = new TurnManager(a); SimulationPipeline.Wire(ta, a);
            for (int i = 0; i < 60; i++) ta.EndMonth();
            var tb = new TurnManager(b); SimulationPipeline.Wire(tb, b);
            for (int i = 0; i < 30; i++) tb.EndMonth();
            b = SaveSystem.FromJson(SaveSystem.ToJson(b)); tb = new TurnManager(b); SimulationPipeline.Wire(tb, b);
            for (int i = 0; i < 30; i++) tb.EndMonth();
            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(b));
            var review = a.chronicle.Single(e => e.historicalEvent == HistoricalEvent.ConductReview);
            Assert.AreEqual(new GameDate(1988, 12), review.date);
            Assert.AreEqual(0, a.PlayerCountry.strategicConduct.months);
        }

        // Late in a native partition these paired worlds exceed the default
        // 180 seconds; retain the full 240-month comparison on both seeds.
        [TestCase(982)] [TestCase(4747)] [Timeout(900000)]
        public void TwoDecadesOfObservationDoNotChangeTheUnderlyingWorld(int seed)
        {
            var observed = WorldFactory.CreateDebugWorld(seed);
            var control = SaveSystem.FromJson(SaveSystem.ToJson(observed));
            var a = new TurnManager(observed); SimulationPipeline.Wire(a, observed);
            var b = new TurnManager(control); SimulationPipeline.Wire(b, control);
            b.MonthResolved -= StrategicEraSystem.RecordMonth;
            for (int i = 0; i < 240; i++) { a.EndMonth(); b.EndMonth(); }
            Assert.AreEqual(4, observed.chronicle.Count(e => e.historicalEvent == HistoricalEvent.ConductReview));
            observed.PlayerCountry.strategicConduct = null;
            observed.chronicle.RemoveAll(e => e.historicalEvent == HistoricalEvent.ConductReview);
            Assert.AreEqual(SaveSystem.ToJson(control), SaveSystem.ToJson(observed), "Only the recorded history may differ, not the world it describes.");
        }

        [TestCase(34)] [TestCase(49)] [TestCase(64)] [TestCase(104)]
        public void EarnedNamesAreWrappedInRealChronicleAndDossierWithoutWriting(int columns)
        {
            var s = Blank(); Pattern(s, 0); Pattern(s, 3); Months(s, 60);
            var gc = GameController.Instance; var prior = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, s);
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                string json = SaveSystem.ToJson(s);
                foreach (TerminalView view in new TerminalView[] { new ChronicleView(), new DossierView() })
                {
                    if (view is DossierView) typeof(DossierView).GetField("subjectId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(view, "USA");
                    view.Refresh();
                    var label = view.Root.Query<Label>().ToList().Single(l => (l.text ?? "").Contains("HISTORICAL IDENTITY"));
                    StringAssert.Contains("CLASSIFIED POSTING", label.text.Replace("\n", " "));
                    foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                }
                Assert.AreEqual(json, SaveSystem.ToJson(s));
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, prior); TerminalMetrics.ResetForTests(); }
        }

        [Test]
        public void DoctrineProducesNamedEra()
        {
            var state = WorldFactory.CreateDebugWorld(981);
            var strategy = StrategySystem.Ensure(state);
            strategy.doctrineChosen = true;
            strategy.doctrine = StrategicDoctrine.Deterrence;
            strategy.doctrineAdopted = state.date;

            var era = StrategicEraSystem.Current(state);
            Assert.IsNotNull(era);
            StringAssert.Contains("Shield Era", era.name);
            Assert.AreEqual("Deterrence", era.doctrine);
        }

        [Test]
        public void NationalPressureChangesEraLabelNotDoctrine()
        {
            var state = WorldFactory.CreateDebugWorld(982);
            var strategy = StrategySystem.Ensure(state);
            strategy.doctrineChosen = true;
            strategy.doctrine = StrategicDoctrine.Resilience;
            strategy.doctrineAdopted = state.date;
            state.PlayerCountry.socialUnrest = 80f;
            int sequence = state.actionSequence;

            var era = StrategicEraSystem.Current(state);
            StringAssert.StartsWith("Crisis ", era.name);
            Assert.AreEqual(StrategicDoctrine.Resilience, strategy.doctrine);
            Assert.AreEqual(sequence, state.actionSequence);
        }
    }
}

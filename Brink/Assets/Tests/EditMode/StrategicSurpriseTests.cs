using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;
using UnityEngine;

namespace Brink.Tests
{
    public class StrategicSurpriseTests
    {
        static IntelEstimate Picture(float value, int month)
            => new IntelEstimate
            {
                observerId = "USA", targetId = "CHN", domain = IntelDomain.Military,
                reportedValue = value, margin = 2f, confidence = ConfidenceGrade.High,
                everCollected = true, asOf = new GameDate(1984, month)
            };

        [TestCase(80f, 40f, "DOWNWARD")]
        [TestCase(40f, 80f, "UPWARD")]
        [TestCase(0f, 10f, "UPWARD")]
        public void MaterialRevisionRetainsBothDatedBandsNotAClaimOfTruth(float before, float after, string direction)
        {
            var previous = Picture(before, 1);
            var current = Picture(after, 2);
            string text = StrategicSurpriseSystem.RevisionText(previous, current);
            StringAssert.Contains(direction, text);
            StringAssert.Contains("JAN 1984: EST " + previous.RangeText + ", CONF HIGH", text);
            StringAssert.Contains("FEB 1984: EST " + current.RangeText + ", CONF HIGH", text);
            StringAssert.Contains("does not establish when or why reality changed", text);
            // Hidden correctness/deception cannot identify the cause for us.
            string saved = JsonUtility.ToJson(current);
            current.deceived = true;
            Assert.AreEqual(text, StrategicSurpriseSystem.RevisionText(previous, current));
            current.deceived = false;
            Assert.AreEqual(saved, JsonUtility.ToJson(current));
        }

        [TestCase("first")]
        [TestCase("uncollected")]
        [TestCase("low-before")]
        [TestCase("low-after")]
        [TestCase("same-month")]
        [TestCase("gap")]
        [TestCase("future")]
        [TestCase("overlap")]
        [TestCase("small")]
        [TestCase("relative")]
        [TestCase("other-target")]
        [TestCase("other-observer")]
        [TestCase("other-domain")]
        public void RevisionRequiresAComparableMaterialCollectedChange(string excluded)
        {
            var previous = Picture(80f, 1);
            var current = Picture(40f, 2);
            Assert.IsNotEmpty(StrategicSurpriseSystem.RevisionText(previous, current));
            switch (excluded)
            {
                case "first": previous = null; break;
                case "uncollected": current.everCollected = false; break;
                case "low-before": previous.confidence = ConfidenceGrade.Low; break;
                case "low-after": current.confidence = ConfidenceGrade.Low; break;
                case "same-month": current.asOf = previous.asOf; break;
                case "gap": current.asOf = new GameDate(1984, 3); break;
                case "future": previous.asOf = new GameDate(1984, 3); break;
                case "overlap": previous.margin = current.margin = 20f; break;
                case "small": previous.reportedValue = 10f; current.reportedValue = 19f; break;
                case "relative": current.reportedValue = 60f; break;
                case "other-target": current.targetId = "RUS"; break;
                case "other-observer": current.observerId = "RUS"; break;
                case "other-domain": current.domain = IntelDomain.Economic; break;
            }
            Assert.IsEmpty(StrategicSurpriseSystem.RevisionText(previous, current));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void RevisionUsesTheOrdinaryWrappedProseChannel(int columns)
        {
            string text = StrategicSurpriseSystem.RevisionText(Picture(80f, 1), Picture(40f, 2));
            foreach (var line in AsciiChart.WrapBlock(text, columns).Split('\n'))
                Assert.LessOrEqual(line.Length, columns);
        }

        static GameState RevisionWorld(string observer = "USA")
        {
            var state = WorldFactory.CreateDebugWorld(4747);
            state.networks.Clear();
            state.estimates.Clear();
            state.date = new GameDate(1984, 2);
            state.FindCountry("CHN").pillars.military = 20f;
            state.FindCountry("CHN").counterIntel.counterIntelligence = 0f;
            var previous = Picture(80f, 1);
            previous.observerId = observer;
            state.estimates.Add(previous);
            state.networks.Add(new IntelNetwork
            {
                ownerId = observer, targetId = "CHN", penetration = 100f,
                focus = IntelDomain.Military
            });
            return state;
        }

        [Test]
        public void RealCollectionRecordsOwnSecretRevisionOnceAndLoadDoesNotReplayIt()
        {
            var state = RevisionWorld();
            int sequence = state.actionSequence;
            IntelligenceSystem.MonthlyCollection(state);
            var notices = state.notifications.FindAll(n => n.title == "ASSESSMENT REVISED");
            Assert.AreEqual(1, notices.Count);
            Assert.AreEqual(ReportingDesk.Intelligence, notices[0].desk);
            Assert.AreEqual("CHN", notices[0].countryId);
            var records = state.chronicle.FindAll(e => e.text.Contains("assessment revised"));
            Assert.AreEqual(1, records.Count);
            Assert.AreEqual(state.playerCountryId, records[0].countryId);
            Assert.AreEqual(Publicity.Secret, records[0].publicity);
            Assert.AreEqual(sequence, state.actionSequence);
            var loaded = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(state));
            Assert.AreEqual(records[0].text, loaded.chronicle.Find(e => e.text.Contains("assessment revised")).text);
            IntelligenceSystem.MonthlyCollection(loaded);
            Assert.AreEqual(1, loaded.notifications.FindAll(n => n.title == "ASSESSMENT REVISED").Count);
            string saved = JsonUtility.ToJson(loaded);
            StrategicSurpriseSystem.Render(loaded);
            Assert.AreEqual(saved, JsonUtility.ToJson(loaded));
        }

        [Test]
        public void ForeignServicesRevisionDoesNotReachOurRecord()
        {
            var state = RevisionWorld("RUS");
            IntelligenceSystem.MonthlyCollection(state);
            Assert.Less(state.FindEstimate("RUS", "CHN", IntelDomain.Military).reportedValue, 30f);
            Assert.IsFalse(state.notifications.Exists(n => n.title == "ASSESSMENT REVISED"));
            Assert.IsFalse(state.chronicle.Exists(e => e.text.Contains("assessment revised")));
        }

        [Test]
        public void ReportingDoesNotChangeCollectionArithmeticOrOtherState()
        {
            var revised = RevisionWorld();
            var first = JsonUtility.FromJson<GameState>(JsonUtility.ToJson(revised));
            first.estimates.Clear();
            IntelligenceSystem.MonthlyCollection(revised);
            IntelligenceSystem.MonthlyCollection(first);
            Assert.AreEqual(1, revised.notifications.RemoveAll(n => n.title == "ASSESSMENT REVISED"));
            Assert.AreEqual(1, revised.chronicle.RemoveAll(e => e.text.Contains("assessment revised")));
            Assert.AreEqual(JsonUtility.ToJson(first), JsonUtility.ToJson(revised),
                "The existence of an earlier picture may add the report only, not change collection draws, effects or rewards.");
        }

        [Test]
        public void AuthoritativePipelineActuallyDeliversTheRevision()
        {
            var state = RevisionWorld();
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            Assert.IsTrue(turns.EndMonth());
            Assert.IsTrue(state.chronicle.Exists(e => e.text.Contains("assessment revised")));
        }

        [Test]
        public void MissingCollectionProducesBlindSpotWithoutForeignTruth()
        {
            var state = WorldFactory.CreateDebugWorld(951);
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId);

            var exposures = StrategicSurpriseSystem.Assess(state);
            var intel = exposures.Find(e => e.pillar == Pillar.Intelligence);
            Assert.IsNotNull(intel);
            StringAssert.Contains("no standing foreign collection", intel.known.ToLowerInvariant());
            StringAssert.Contains("missing collection", intel.unknown.ToLowerInvariant());
        }

        [Test]
        public void SurpriseAssessmentIsReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(952);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            int month = state.date.month;

            StrategicSurpriseSystem.Render(state);

            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(sequence, state.actionSequence);
            Assert.AreEqual(month, state.date.month);
        }

        [Test]
        public void AcuteDomesticPressureIsMaterialExposure()
        {
            var state = WorldFactory.CreateDebugWorld(953);
            state.PlayerCountry.governmentApproval = 20f;
            state.PlayerCountry.socialUnrest = 82f;

            var exposure = StrategicSurpriseSystem.Assess(state).Find(e => e.pillar == Pillar.Government);
            Assert.IsNotNull(exposure);
            Assert.AreEqual(4, exposure.severity);
        }
    }
}

using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Session recording and the review it produces (GDD §34.1).
    ///
    /// With one tester, an evening of real play is the only evidence available
    /// about questions the harness cannot answer: which of twenty-three military
    /// verbs a person reaches for, whether command capacity sits unspent, which
    /// crisis is always ignored. These tests check that the recorder captures
    /// that without anyone remembering to instrument each verb, and — the part
    /// that actually matters — that the analysis *catches planted faults* rather
    /// than producing a reassuring page of nothing.
    /// </summary>
    public class TelemetryTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            Telemetry.Clear();
            Telemetry.Enabled = true;

            state = WorldFactory.CreateDebugWorld(seed: 2468);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            Telemetry.BeginSession(state);
        }

        [TearDown]
        public void TearDown()
        {
            Telemetry.Enabled = false;
            Telemetry.Clear();
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void PlayMonths(int months)
        {
            for (int i = 0; i < months; i++) turns.EndMonth();
        }

        // ---------- it is off unless asked for ----------

        [Test]
        public void NothingIsRecordedWhenRecordingIsOff()
        {
            Telemetry.Clear();
            Telemetry.Enabled = false;

            Telemetry.BeginSession(state);
            turns.SpendCommandPoints(1, "Test action");
            PlayMonths(3);

            Assert.IsEmpty(Telemetry.Events,
                "The recorder wrote events while disabled. It has to be genuinely inert, not " +
                "merely unwritten to disk.");
        }

        // ---------- the hook catches verbs nobody instrumented ----------

        [Test]
        public void PlayerActionsAreCapturedWithoutInstrumentingEachVerb()
        {
            // The design claim: hooking the resource spends means a verb added
            // later is recorded without anybody remembering to touch it.
            state.commandPoints.current = 20;
            turns.SpendCommandPoints(2, "Assault at Norfolk Naval Complex");
            turns.SpendCommandPoints(2, "Assault at Port of Shanghai");
            turns.SpendCommandPoints(1, "Diplomatic outreach to China");

            var counts = Telemetry.CountsFor(TelemetryKind.PlayerAction, playerOnly: true);

            Assert.AreEqual(2, counts["ASSAULT"],
                "Two assaults at different places must aggregate as one verb, or every count " +
                "is 1 and the whole exercise measures nothing.");
            Assert.IsTrue(counts.ContainsKey("DIPLOMATIC OUTREACH"));
        }

        [Test]
        public void ReasonsAreReducedToStableBuckets()
        {
            // The identical trap that defeated XP diminishing returns: two call
            // sites interpolated a target into the reason and defeated the counter.
            Assert.AreEqual("ASSAULT", Telemetry.Bucket("Assault at Norfolk"));
            Assert.AreEqual("ASSAULT", Telemetry.Bucket("Assault at Beijing"));
            Assert.AreEqual("SET NATIONAL PRIORITY", Telemetry.Bucket("Set national priority: Security"));
            Assert.AreEqual("DIPLOMATIC OUTREACH", Telemetry.Bucket("Diplomatic outreach to India"));
            Assert.AreEqual("UNKNOWN", Telemetry.Bucket(""));
        }

        [Test]
        public void RefusalsAreRecordedToo()
        {
            // An action the operator keeps reaching for and keeps being denied is
            // mispriced or badly explained, and that is invisible if only the
            // successes are kept.
            state.commandPoints.current = 0;
            Assert.IsFalse(turns.SpendCommandPoints(3, "Assault at Norfolk"));

            bool sawRefusal = false;
            foreach (var e in Telemetry.Events)
                if (e.kind == TelemetryKind.PlayerAction && !e.success) sawRefusal = true;

            Assert.IsTrue(sawRefusal, "A refused action left no trace.");
        }

        [Test]
        public void TheWorldIsRecordedAsWellAsTheOperator()
        {
            PlayMonths(24);

            var aiActions = Telemetry.CountsFor(TelemetryKind.AiAction, playerOnly: false);
            Assert.Greater(aiActions.Count, 0,
                "Two years passed and no foreign government was recorded doing anything.");
        }

        [Test]
        public void EveryResolvedMonthLeavesASnapshot()
        {
            PlayMonths(18);

            Assert.AreEqual(18, Telemetry.Series("cp").Count,
                "The month series is what makes a ratchet visible; a gap in it is a blind spot.");
            Assert.AreEqual(18, Telemetry.Series("stab").Count);
        }

        // ---------- the analysis catches planted faults ----------

        [Test]
        public void APlantedRatchetIsCaught()
        {
            // The single most valuable thing this can do. A value that only ever
            // moves one way is the bug class this project has shipped five times,
            // and a live session is the cheapest place to catch the sixth.
            PlantMonthSeries(stability: month => 90f - month * 0.5f);

            var findings = TelemetryAnalysis.Findings(state);

            bool caught = false;
            foreach (var finding in findings)
                if (finding.severity == FindingSeverity.Suspect
                    && finding.headline.Contains("Stability")
                    && finding.headline.Contains("down")) caught = true;

            Assert.IsTrue(caught,
                "A value that fell every month for two years was not flagged. " +
                "Findings were: " + Describe(findings));
        }

        [Test]
        public void AHealthyValueIsNotFlagged()
        {
            // The other half: a review that cries wolf is a review nobody reads.
            PlantMonthSeries(stability: month => 60f + (month % 4 < 2 ? 3f : -3f));

            foreach (var finding in TelemetryAnalysis.Findings(state))
                Assert.IsFalse(finding.headline.Contains("Stability"),
                    "A value that moved both ways every few months was flagged as stuck.");
        }

        [Test]
        public void ResourcesWithNothingToBuyAreFlagged()
        {
            // Exactly how the Government pillar's problem was diagnosed, and it
            // was only found because somebody happened to look.
            PlantMonthSeries(politicalCapital: _ => 20f);

            bool caught = false;
            foreach (var finding in TelemetryAnalysis.Findings(state))
                if (finding.headline.Contains("Political Capital")) caught = true;

            Assert.IsTrue(caught, "Political Capital pinned at its cap for two years went unnoticed.");
        }

        [Test]
        public void ASilentWorldIsFlagged()
        {
            PlantMonthSeries();

            bool caught = false;
            foreach (var finding in TelemetryAnalysis.Findings(state))
                if (finding.headline.Contains("foreign government")) caught = true;

            Assert.IsTrue(caught,
                "A session in which no foreign state did anything at all was not flagged. " +
                "Either the world is inert or the recorder is not hooked in, and both matter.");
        }

        [Test]
        public void AShortSessionSaysSoRatherThanGuessing()
        {
            PlayMonths(3);

            var findings = TelemetryAnalysis.Findings(state);
            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual(FindingSeverity.Note, findings[0].severity);
            StringAssert.Contains("too short", findings[0].headline);
        }

        [Test]
        public void ACleanSessionReportsThatItIsClean()
        {
            // Recording is worthless if the review is always alarming.
            PlayMonths(36);

            var findings = TelemetryAnalysis.Findings(state);
            foreach (var finding in findings)
                Assert.AreNotEqual(FindingSeverity.Suspect, finding.severity,
                    "Thirty-six months of ordinary play produced a SUSPECT finding: " +
                    finding.headline + " — " + finding.detail);
        }

        // ---------- the report is readable ----------

        [Test]
        public void TheReportNamesTheRunItCameFrom()
        {
            PlayMonths(14);
            string report = TelemetryAnalysis.Report(state);

            StringAssert.Contains(Telemetry.SessionSeed.ToString(), report,
                "Without the seed the session cannot be reproduced, which is most of its value.");
            StringAssert.Contains(state.playerCountryId, report);
        }

        [Test]
        public void EveryFindingSaysWhatToDoAboutIt()
        {
            PlantMonthSeries(stability: month => 90f - month * 0.5f, politicalCapital: _ => 20f);

            foreach (var finding in TelemetryAnalysis.Findings(state))
            {
                Assert.IsNotEmpty(finding.headline);
                Assert.Greater(finding.detail.Length, 40,
                    $"'{finding.headline}' reports a problem without saying what to check. " +
                    "A finding nobody can act on is noise.");
            }
        }

        // ---------- helpers ----------

        /// <summary>
        /// Write a month series directly, so a fault can be planted without
        /// waiting for the simulation to produce one by accident.
        /// </summary>
        void PlantMonthSeries(System.Func<int, float> stability = null,
            System.Func<int, float> politicalCapital = null, int months = 24)
        {
            Telemetry.Clear();
            Telemetry.BeginSession(state);

            var player = state.PlayerCountry;
            for (int month = 0; month < months; month++)
            {
                if (stability != null) player.stability = stability(month);
                if (politicalCapital != null) state.politicalCapital = politicalCapital(month);
                Telemetry.RecordMonth(state);
            }
        }

        static string Describe(List<TelemetryFinding> findings)
        {
            var parts = new List<string>();
            foreach (var finding in findings) parts.Add(finding.headline);
            return parts.Count == 0 ? "(none)" : string.Join("; ", parts.ToArray());
        }
    }
}

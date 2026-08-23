using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The monthly system order lives in exactly one place — `SimulationPipeline`
    /// — and this test exists to keep it that way in the *tests* as well as in
    /// the game.
    ///
    /// This has already gone wrong twice, and both times it invalidated
    /// measurements that were then acted on:
    ///
    ///   1. `VerticalSliceValidationTests` hand-copied the list and drifted by
    ///      five systems. Every balance figure the harness ever produced had been
    ///      measured in a world with no coups, no research, no strategic
    ///      instruments, no value to holding ground and no cabinet turnover — and
    ///      reported as if it were the game.
    ///   2. `EndgameSystemTests.OverALongGame_SomeGovernmentBuildsAnInstrument`
    ///      wired three systems, so estimate confidence stayed pinned at its
    ///      floor, no AI rivalry could ever form, no AI pillar could move and AI
    ///      political capital was never credited. It concluded that no government
    ///      builds a strategic instrument in twenty years. What it had actually
    ///      built was a world where the AI could not think.
    ///
    /// A validation harness that does not run the thing it validates is worse
    /// than no harness, because it is trusted.
    ///
    /// **A narrow pipeline is still legitimate.** A test that asserts one
    /// month of economic arithmetic should not pay for sixteen AI governments to
    /// deliberate. What is not legitimate is doing it *by accident*, or doing it
    /// while making a claim about how the world behaves over decades. So the
    /// rule is not "never hand-wire" — it is "hand-wire on purpose, in writing".
    /// </summary>
    public class PipelineWiringTests
    {
        const string TestSourceDirectory = "Assets/Tests/EditMode";

        /// <summary>
        /// The marker a file must carry to hand-wire its own monthly systems.
        /// Deliberately verbose: it should be easier to call
        /// `SimulationPipeline.Wire` than to opt out of it.
        /// </summary>
        const string OptOutMarker = "NARROW PIPELINE:";

        /// <summary>
        /// Files that hand-wire and have not yet been reviewed.
        ///
        /// This list is **technical debt, written down**. It is not a permission
        /// list to grow: a file only belongs here because it predates the rule,
        /// and the correct response to seeing one is to check whether its claims
        /// actually survive the systems it omits, then either convert it to
        /// `SimulationPipeline.Wire` or give it a `NARROW PIPELINE:` line saying
        /// why its scope is deliberate.
        ///
        /// Nothing may be added. A new file that hand-wires fails this test, and
        /// that is the entire point — the bug is not that these 26 exist, it is
        /// that number 27 could be written without anybody noticing.
        /// </summary>
        static readonly HashSet<string> Grandfathered = new HashSet<string>
        {
            "AIDomesticTests.cs",
            "AISystemTests.cs",
            "AllianceSystemTests.cs",
            "AsciiWorldMapTests.cs",
            "AssessmentSystemTests.cs",
            "BugRegressionTests.cs",
            "CabinetSystemTests.cs",
            "ChronicleTests.cs",
            "CrisisSystemTests.cs",
            "DiplomacySystemTests.cs",
            "EconomySystemTests.cs",
            "EventCatalogTests.cs",
            "ExerciseSystemTests.cs",
            "GovernmentSystemTests.cs",
            "IntelligenceSystemTests.cs",
            "MilitarySystemTests.cs",
            "MilitaryVerbsTests.cs",
            "PeaceSystemTests.cs",
            "ProgressionSystemTests.cs",
            "RegimeSystemTests.cs",
            "ReportingSystemTests.cs",
            "SaveMigrationTests.cs",
            "TechnologySystemTests.cs",
            "TerritorySystemTests.cs",
            "TurnManagerTests.cs",
            "WorldInvariantTests.cs"
        };

        static string SourceDirectory
            => Path.Combine(Directory.GetCurrentDirectory(), TestSourceDirectory);

        [Test]
        public void NoNewTestFileHandWiresTheMonthlyPipeline()
        {
            Assert.IsTrue(Directory.Exists(SourceDirectory),
                $"Cannot find the test sources at {SourceDirectory}.");

            var offenders = new List<string>();

            foreach (string path in Directory.GetFiles(SourceDirectory, "*.cs"))
            {
                string name = Path.GetFileName(path);
                string source = File.ReadAllText(path);

                if (!source.Contains("ResolveMonth +=")) continue;
                if (source.Contains(OptOutMarker)) continue;
                if (Grandfathered.Contains(name)) continue;

                offenders.Add(name);
            }

            Assert.IsEmpty(offenders,
                $"{string.Join(", ", offenders)} hand-wires the monthly system list.\n\n" +
                "Call SimulationPipeline.Wire(turns, state) instead. A hand-copied list " +
                "drifts, and when it drifts the test keeps passing while measuring a " +
                "different game — this has already invalidated a full set of balance " +
                "figures once and a claim about the AI once.\n\n" +
                $"If the narrow scope is deliberate, write a '{OptOutMarker} <reason>' " +
                "comment in the file saying which systems are omitted and why the " +
                "assertions survive without them.");
        }

        [Test]
        public void TheGrandfatheredListDoesNotOutliveItsFiles()
        {
            // A stale entry would silently re-permit hand-wiring in a file that
            // had since been fixed or renamed — the debt list quietly becoming a
            // permission list, which is the failure mode of every such list.
            var missing = new List<string>();

            foreach (string name in Grandfathered)
            {
                string path = Path.Combine(SourceDirectory, name);
                if (!File.Exists(path)) { missing.Add($"{name} (no longer exists)"); continue; }
                if (!File.ReadAllText(path).Contains("ResolveMonth +="))
                    missing.Add($"{name} (no longer hand-wires)");
            }

            Assert.IsEmpty(missing,
                $"Remove from Grandfathered: {string.Join(", ", missing)}. " +
                "The list has to shrink as the debt is paid, or it stops meaning anything.");
        }
    }
}

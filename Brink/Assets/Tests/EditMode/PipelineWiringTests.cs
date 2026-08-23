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
        /// **Deliberately empty, and it must stay that way.** This began as 26
        /// entries of written-down debt. All 26 were then audited: eight were
        /// making long-run claims about emergent behaviour on a partial world and
        /// were converted to `SimulationPipeline.Wire`; the rest set their own
        /// preconditions and assert one system's arithmetic, and now carry a
        /// `NARROW PIPELINE:` line saying which systems they omit and why their
        /// assertions survive the omission.
        ///
        /// With the debt paid, the marker is the only mechanism left — which is
        /// the point. A grandfather list that outlives its debt stops being a
        /// record of what needs fixing and quietly becomes a list of permitted
        /// exceptions, and the next person to add an entry will be doing it to
        /// silence this test rather than to note a problem.
        ///
        /// If you are here because the build failed: do not add your file. Either
        /// call `SimulationPipeline.Wire`, or write the marker and say why.
        /// </summary>
        static readonly HashSet<string> Grandfathered = new HashSet<string>();

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

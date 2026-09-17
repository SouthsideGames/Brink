using System;
using System.Collections.Generic;
using System.IO;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using Brink.UI.Views;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Two Phase F/G readers were implemented, tested, certified and merged —
    /// and had **no UI call site at all**, so the player could never see either.
    /// That is this project's most-repeated bug family (written but never read)
    /// wearing its most expensive costume: a whole system that renders nothing.
    ///
    /// These tests guard the wiring rather than the readers, which have their own
    /// fixtures (`CabinetChoiceReadingTests`, `HistoricalIdentityTests`). What is
    /// asserted here is that the surfaces reach the production readers, that the
    /// views hold no second copy of the classification, that consultation is
    /// genuinely informational, and that neither addition overflows a phone.
    /// </summary>
    public class CabinetConsultationTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3131);
        }

        [TearDown]
        public void TearDown() => TerminalMetrics.ResetForTests();

        // ---------- 1. the player can get to it ----------

        [Test]
        public void CabinetConsultation_IsReachableFromAShippingPanel()
        {
            var panels = TerminalShellController.BuildPanels(false);
            Assert.IsTrue(panels.Exists(p => p.Id == "CABINET" && p.ShortCode == "CAB"),
                "CABINET is not a shipping panel, so the consultation cannot be reached at all.");

            Assert.IsNotEmpty(CabinetView.ConsultationChoices,
                "CABINET offers no directions to consult on — the panel would be a header with nothing under it.");
        }

        // ---------- 2. every ChoiceKind is offered and renders ----------

        [Test]
        public void EveryChoiceKind_IsOfferedAndRenders()
        {
            var offered = new List<CabinetChoiceReadingSystem.ChoiceKind>(CabinetView.ConsultationChoices);
            foreach (CabinetChoiceReadingSystem.ChoiceKind choice in
                     Enum.GetValues(typeof(CabinetChoiceReadingSystem.ChoiceKind)))
            {
                Assert.Contains(choice, offered,
                    $"{choice} exists in the reader's taxonomy but CABINET does not offer it. " +
                    "The panel enumerates the enum precisely so a direction added later cannot go unreachable.");

                string text = CabinetView.ConsultationText(state, choice);
                Assert.IsNotNull(text);
                Assert.IsNotEmpty(text, $"{choice} rendered nothing.");
                StringAssert.Contains(choice.ToString().ToUpperInvariant(), text,
                    $"the reading for {choice} does not name the direction it is about.");
            }
        }

        // ---------- 3. it is the real reading ----------

        [Test]
        public void TheConsultationIsTheProductionReading_NotAViewCopy()
        {
            foreach (CabinetChoiceReadingSystem.ChoiceKind choice in
                     Enum.GetValues(typeof(CabinetChoiceReadingSystem.ChoiceKind)))
                Assert.AreEqual(CabinetChoiceReadingSystem.Render(state, choice),
                    CabinetView.ConsultationText(state, choice),
                    $"CABINET prints something other than the production reading for {choice}.");
        }

        [Test]
        public void DifferentDirectionsReadDifferently()
        {
            // Non-vacuity: a pass-through that always returned the same block
            // would satisfy every assertion above and tell the operator nothing.
            string escalate = CabinetView.ConsultationText(
                state, CabinetChoiceReadingSystem.ChoiceKind.EscalateWar);
            string peace = CabinetView.ConsultationText(
                state, CabinetChoiceReadingSystem.ChoiceKind.SeekPeace);
            Assert.AreNotEqual(escalate, peace,
                "escalating a war and suing for peace read identically to the same cabinet.");
        }

        [Test]
        public void TheConsultationSaysItIsNeitherAVoteNorAVeto()
        {
            // The reading is advice. If that framing is ever lost the panel
            // starts to look like a permission check, which is exactly what
            // spec 32 forbids.
            string text = CabinetView.ConsultationText(
                state, CabinetChoiceReadingSystem.ChoiceKind.EscalateWar);
            StringAssert.Contains("NOT A VOTE", text);
            StringAssert.Contains("NOT A VETO", text);
        }

        // ---------- 4 + 9. informational only ----------

        [Test]
        public void ConsultingTheCabinet_ChangesNothingAtAll()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();   // a world with some history to read

            string before = SaveSystem.ToJson(state);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            var date = state.date;

            // Every direction, plus the identity reader, rendered repeatedly —
            // the whole player-facing surface of this milestone exercised.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (CabinetChoiceReadingSystem.ChoiceKind choice in
                         Enum.GetValues(typeof(CabinetChoiceReadingSystem.ChoiceKind)))
                    CabinetView.ConsultationText(state, choice);
                StrategistView.HistoricalIdentityText(state);
            }

            Assert.AreEqual(cp, state.commandPoints.current, "consulting the cabinet spent Command Points.");
            Assert.AreEqual(influence, state.influence, "consulting the cabinet spent Influence.");
            Assert.AreEqual(sequence, state.actionSequence,
                "consulting the cabinet consumed an action sequence number — it would move every later dice roll.");
            Assert.AreEqual(date.year, state.date.year, "consulting the cabinet advanced the clock.");
            Assert.AreEqual(date.month, state.date.month, "consulting the cabinet advanced the clock.");
            Assert.AreEqual(before, SaveSystem.ToJson(state),
                "reading the cabinet or the record mutated the save. Both are observational by specification: " +
                "they may not spend, advance, draw, execute or write.");
        }

        [Test]
        public void ConsultingDoesNotMoveCabinetDisposition()
        {
            var official = state.PlayerCountry.cabinet[0];
            float trust = official.trust, loyalty = official.loyalty, competence = official.competence;
            var mode = official.mode;

            foreach (CabinetChoiceReadingSystem.ChoiceKind choice in
                     Enum.GetValues(typeof(CabinetChoiceReadingSystem.ChoiceKind)))
                CabinetView.ConsultationText(state, choice);

            Assert.AreEqual(trust, official.trust, 0.0001f, "asking an official's opinion changed their trust.");
            Assert.AreEqual(loyalty, official.loyalty, 0.0001f, "asking an official's opinion changed their loyalty.");
            Assert.AreEqual(competence, official.competence, 0.0001f, "asking an official's opinion changed their competence.");
            Assert.AreEqual(mode, official.mode, "asking an official's opinion changed who controls their desk.");
        }

        // ---------- 5 + 6. historical identity in STRATEGIST ----------

        [Test]
        public void HistoricalIdentity_IsShownInStrategist()
        {
            var panels = TerminalShellController.BuildPanels(false);
            Assert.IsTrue(panels.Exists(p => p.Id == "OPERATOR"),
                "the operator's own record panel is not in the rail, so identity has nowhere to appear.");

            string text = StrategistView.HistoricalIdentityText(state);
            Assert.IsNotEmpty(text, "identity rendered nothing — even an unsettled record must say so.");
            StringAssert.Contains("HISTORICAL IDENTITY", text);
        }

        [Test]
        public void HistoricalIdentity_IsWiredIntoTheStrategicRecordSection()
        {
            // The readers were certified and merged with no call site; only the
            // wiring can regress now, and the wiring is a line in a view that no
            // headless assertion reaches. So read the source, as SettlementFogTests
            // does for the oracle identifiers.
            string source = ReadRuntimeSource(Path.Combine("UI", "Views", "StrategistView.cs"));
            StringAssert.Contains("HistoricalIdentityText(state)", source,
                "STRATEGIST no longer renders historical identity — the reader is orphaned again.");

            int section = source.IndexOf("BuildHistoricalCourse", StringComparison.Ordinal);
            int call = source.IndexOf("HistoricalIdentityText(state)", section, StringComparison.Ordinal);
            Assert.Greater(call, section,
                "identity is not rendered inside the STRATEGIC RECORD section, where era, precedent, " +
                "reversal, credibility and recovery already live.");
        }

        [Test]
        public void HistoricalIdentity_UsesTheProductionReader()
        {
            Assert.AreEqual(HistoricalIdentitySystem.Render(state),
                StrategistView.HistoricalIdentityText(state),
                "STRATEGIST prints something other than the production identity reading.");

            // And the view must not classify anything itself. Every label belongs
            // to HistoricalIdentitySystem; finding one in a view means the
            // taxonomy now exists twice and will drift.
            string source = ReadRuntimeSource(Path.Combine("UI", "Views", "StrategistView.cs"));
            foreach (string label in new[]
                     {
                         "SECURITY STATE", "COMMERCIAL POWER", "BROKER STATE", "CONTESTED ORDER",
                         "ARMED TRADITION", "INDUSTRIAL TRADITION", "POLITICS UNDER STRAIN"
                     })
                StringAssert.DoesNotContain(label, source,
                    $"StrategistView names \"{label}\" itself. Classification belongs to " +
                    "HistoricalIdentitySystem; a copy in the view is a second definition of one thing.");
        }

        // ---------- 7. both readable across the slice widths ----------

        [Test]
        public void BothAdditions_FitEveryVerticalSliceWidth()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            turns.EndMonth();

            // Spec 34's acceptance widths. These are prose blocks, so the shell
            // hard-wraps them through AsciiChart.WrapBlock after every refresh
            // (TerminalShellController.ApplyTextPolicy) — the check is that
            // wrapping actually brings every line inside the panel.
            foreach (int width in new[] { 41, 49, 60, 64, 79, 82, 100, 104 })
            {
                foreach (CabinetChoiceReadingSystem.ChoiceKind choice in
                         Enum.GetValues(typeof(CabinetChoiceReadingSystem.ChoiceKind)))
                    AssertFits(AsciiChart.WrapBlock(CabinetView.ConsultationText(state, choice), width),
                        width, $"CABINET CONSULTATION ({choice})");

                AssertFits(AsciiChart.WrapBlock(StrategistView.HistoricalIdentityText(state), width),
                    width, "HISTORICAL IDENTITY");
            }
        }

        [Test]
        public void ConsultationLabels_FitANarrowPanel()
        {
            // Eight buttons on a 41-column phone. `.button-row` wraps, so the
            // requirement is that no single label is itself wider than the panel.
            TerminalMetrics.Update(contentWidthPt: 287f, charWidthPt: 7f, heightPt: 400f,
                size: SizeClass.Compact);
            foreach (var choice in CabinetView.ConsultationChoices)
            {
                string label = CabinetView.ConsultationLabel(choice);
                Assert.LessOrEqual(label.Length, 24,
                    $"the compact label \"{label}\" is too wide for a phone button.");
                Assert.IsNotEmpty(label);
            }
        }

        // ---------- 8. the fog is intact ----------

        [Test]
        public void IdentityReadsOnlyOurOwnRecord()
        {
            string before = StrategistView.HistoricalIdentityText(state);

            var foreigner = state.countries.Find(c => !c.isPlayer);
            for (int i = 0; i < 12; i++)
            {
                state.AddChronicle(ChronicleCategory.Military, foreigner.id, $"foreign campaign {i}", Publicity.Public);
                state.AddChronicle(ChronicleCategory.Economic, foreigner.id, $"foreign programme {i}", Publicity.Public);
            }

            Assert.AreEqual(before, StrategistView.HistoricalIdentityText(state),
                "another country's record changed what WE remember ourselves as. Identity is ours alone " +
                "(spec 33), and reading a foreign chronicle into it would also be a fog breach.");
        }

        [Test]
        public void ConsultationProfilesOnlyOurOwnCabinet()
        {
            var voices = CabinetChoiceReadingSystem.Read(
                state, CabinetChoiceReadingSystem.ChoiceKind.EscalateWar);
            Assert.IsNotEmpty(voices, "the consultation returned no voices at all.");

            var ours = new HashSet<string>();
            foreach (var official in state.PlayerCountry.cabinet)
                if (official != null) ours.Add(official.displayName ?? official.office.ToString());

            Assert.AreEqual(state.PlayerCountry.cabinet.Count, voices.Count,
                "the consultation returned a different number of voices than we have officials.");
            foreach (var voice in voices)
                Assert.IsTrue(ours.Contains(voice.officialName),
                    $"\"{voice.officialName}\" is not one of our officials — the consultation is " +
                    "characterizing a foreign minister, which spec 32 forbids.");
        }

        // ---------- helpers ----------

        static void AssertFits(string block, int width, string what)
        {
            Assert.IsNotEmpty(block, $"{what} is empty at {width} columns.");
            foreach (var line in block.Split('\n'))
                Assert.LessOrEqual(line.TrimEnd('\r').Length, width,
                    $"{what} at {width} columns overflows: \"{line}\"");
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

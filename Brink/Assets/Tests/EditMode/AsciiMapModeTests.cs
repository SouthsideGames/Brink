using System;
using System.IO;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AsciiMapModeTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            state = WorldFactory.CreateDebugWorld(seed: 6120);
        }

        [Test]
        public void EveryModePreservesRequestedGrid()
        {
            foreach (WorldMapMode mode in System.Enum.GetValues(typeof(WorldMapMode)))
            {
                string map = AsciiMapModes.Render(state, state.playerCountryId, mode, 60, 14);
                var rows = map.Split('\n');
                Assert.AreEqual(14, rows.Length, mode.ToString());
                foreach (var row in rows) Assert.AreEqual(60, row.Length, mode.ToString());
            }
        }

        [Test]
        public void EveryPillarHasADistinctFixedHeightInstitutionalSignature()
        {
            var expectedHeadings = new[]
            {
                "FORCE STATUS", "NATIONAL MARKET", "SIGNAL PICTURE",
                "DIPLOMATIC NETWORK", "STATE HOUSE"
            };

            int index = 0;
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                string art = AsciiPillarArt.Render(state, pillar, 64);
                var rows = art.Split('\n');

                Assert.AreEqual(5, rows.Length, pillar.ToString());
                foreach (var row in rows) Assert.AreEqual(64, row.Length, pillar.ToString());
                StringAssert.Contains(expectedHeadings[index++], art, pillar.ToString());
            }
        }

        [Test]
        public void PillarSignatureClampsToItsResponsiveWidthContract()
        {
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                string narrow = AsciiPillarArt.Render(state, pillar, 12);
                string wide = AsciiPillarArt.Render(state, pillar, 140);

                Assert.AreEqual(5, narrow.Split('\n').Length, pillar.ToString());
                Assert.AreEqual(5, wide.Split('\n').Length, pillar.ToString());
                foreach (var row in narrow.Split('\n')) Assert.AreEqual(32, row.Length, pillar.ToString());
                foreach (var row in wide.Split('\n')) Assert.AreEqual(100, row.Length, pillar.ToString());
            }
        }

        /// <summary>
        /// The figure is only a feature if the dashboards actually draw it, and
        /// the call is a line in a view that no headless assertion reaches. Read
        /// it from source, as CabinetConsultationTests does for the strategic
        /// record: without this, deleting all five calls leaves the suite green.
        /// </summary>
        [Test]
        public void EveryPillarDashboardDrawsItsSignatureThroughTheSharedPath()
        {
            var views = new[]
            {
                ("MilitaryView.cs", Pillar.Military),
                ("EconomyView.cs", Pillar.Economy),
                ("IntelligenceView.cs", Pillar.Intelligence),
                ("DiplomacyView.cs", Pillar.Diplomacy),
                ("GovernmentView.cs", Pillar.Government),
            };

            foreach (var (file, pillar) in views)
            {
                string source = ReadRuntimeSource(Path.Combine("UI", "Views", file));
                StringAssert.Contains($"AddPillarArt(state, Pillar.{pillar})", source,
                    $"{file} no longer draws its institutional signature.");
            }

            // One shared path, figure-styled, rendering through the one renderer.
            string shell = ReadRuntimeSource(Path.Combine("UI", "Views", "TerminalView.cs"));
            int helper = shell.IndexOf("void AddPillarArt", StringComparison.Ordinal);
            Assert.Greater(helper, 0, "the shared AddPillarArt path is gone.");
            int end = shell.IndexOf("\n        }", helper, StringComparison.Ordinal);
            string body = shell.Substring(helper, end - helper);

            StringAssert.Contains("AddFigure", body,
                "pillar art stopped being figure-styled, so it would take paragraph spacing.");
            StringAssert.Contains("AsciiPillarArt.Render", body,
                "pillar art no longer routes through the shared renderer.");
            StringAssert.Contains("TerminalMetrics.Columns", body,
                "pillar art stopped being sized from the measured panel.");
        }

        /// <summary>
        /// Two failures flattened this skyline and only one was arithmetic. The
        /// heights were invariant mod 3; fixing that but scaling against the
        /// whole 0..100 range flattened it again, because a healthy economy
        /// occupies a narrow high band (the seeded world opens at 72..93). So
        /// this asserts the *seeded* world varies, not just a forced one.
        /// </summary>
        [Test]
        public void TheMarketSkylineVariesAcrossTheSectorsBehindIt()
        {
            Assert.Greater(state.PlayerCountry.economy.sectors.Count, 2,
                "the fixture has too few sectors to vary");

            Assert.Greater(DistinctColumnHeights(state), 1,
                "every column in the seeded world is the same height — the skyline "
                + "cannot express the sector spread the country actually has");
        }

        [Test]
        public void ALevelEconomyStillReadsLevel()
        {
            foreach (var sector in state.PlayerCountry.economy.sectors) sector.output = 70f;

            Assert.AreEqual(1, DistinctColumnHeights(state),
                "a genuinely balanced economy must not be shown as uneven");
        }

        /// <summary>How many different bar heights the market skyline draws.</summary>
        static int DistinctColumnHeights(GameState state)
        {
            var rows = AsciiPillarArt.Render(state, Pillar.Economy, 64).Split('\n');
            var heights = new System.Collections.Generic.HashSet<int>();
            for (int x = 1; x < 44; x += 3)
            {
                int h = 0;
                for (int y = 4; y >= 0; y--) { if (rows[y][x] != '█') break; h++; }
                if (h > 0) heights.Add(h);
            }
            Assert.Greater(heights.Count, 0, "the skyline drew no columns at all");
            return heights.Count;
        }

        [Test]
        public void PoliticalModeIsTheExistingPublicMap()
        {
            string expected = AsciiWorldMap.Render(state, "CHN", 64, 16);
            string actual = AsciiMapModes.Render(state, "CHN", WorldMapMode.Political, 64, 16);
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void IntelligenceModeDoesNotShowAnotherStatesNetwork()
        {
            state.networks.Clear();
            state.networks.Add(new IntelNetwork
            {
                ownerId = "CHN", targetId = "RUS", penetration = 90f, compromised = false
            });

            string cold = AsciiMapModes.Render(state, null, WorldMapMode.Intelligence, 78, 21);
            string baseMap = AsciiWorldMap.Render(state, null, 78, 21);
            Assert.AreEqual(baseMap, cold,
                "A foreign service's network is not information the player's map owns.");

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId, targetId = "RUS", penetration = 60f, compromised = false
            });
            string ours = AsciiMapModes.Render(state, null, WorldMapMode.Intelligence, 78, 21);
            Assert.AreNotEqual(baseMap, ours);
        }

        [Test]
        public void OverlayLegendsDoNotReusePoliticalOrChokepointGlyphs()
        {
            string military = AsciiMapModes.Legend(WorldMapMode.Military);
            string trade = AsciiMapModes.Legend(WorldMapMode.Trade);
            string intel = AsciiMapModes.Legend(WorldMapMode.Intelligence);

            StringAssert.Contains("O occupied", military);
            StringAssert.Contains("$ sanctions", trade);
            StringAssert.Contains("^ established", intel);
            StringAssert.Contains("@ deep", intel);
            StringAssert.DoesNotContain("! occupied", military);
            StringAssert.DoesNotContain("! sanctions", trade);
            StringAssert.DoesNotContain("+ established", intel);
            StringAssert.DoesNotContain("# deep", intel);
        }

        [Test]
        public void BlocAndTradeModesRespondToLiveState()
        {
            string blocBefore = AsciiMapModes.Render(state, null, WorldMapMode.Blocs, 78, 21);
            state.blocs.Add(new Bloc
            {
                id = "TEST", name = "TEST ALIGNMENT", leaderId = "USA",
                memberIds = { "USA", "CHN" }, founded = state.date
            });
            string blocAfter = AsciiMapModes.Render(state, null, WorldMapMode.Blocs, 78, 21);
            Assert.AreNotEqual(blocBefore, blocAfter);

            state.trade.Clear();
            string tradeBefore = AsciiMapModes.Render(state, null, WorldMapMode.Trade, 78, 21);
            state.trade.Add(new TradeRelation
            {
                countryA = state.playerCountryId, countryB = "CHN", volume = 45f
            });
            string tradeAfter = AsciiMapModes.Render(state, null, WorldMapMode.Trade, 78, 21);
            Assert.AreNotEqual(tradeBefore, tradeAfter);
        }

        [Test]
        public void ModeSummariesUseOnlyRelevantPlayerFacingCounts()
        {
            state.trade.Clear();
            state.sanctions.Clear();
            state.trade.Add(new TradeRelation { countryA = "USA", countryB = "CHN", volume = 25f });
            state.trade.Add(new TradeRelation { countryA = "CHN", countryB = "RUS", volume = 90f });
            string summary = AsciiMapModes.Summary(state, WorldMapMode.Trade);
            StringAssert.Contains("OUR TRADE LINKS 1", summary);
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

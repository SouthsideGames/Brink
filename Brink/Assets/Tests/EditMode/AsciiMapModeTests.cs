using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using Brink.Core;
using Brink.UI.Views;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UIElements;

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

        string PrepareDisplacement()
        {
            foreach (var country in state.countries)
            {
                country.displacement.displaced = country.displacement.hosted = 0f;
                country.displacement.bordersClosed = true;
            }
            var nearby = state.countries.Find(c => c.id != state.playerCountryId
                && GeographySystem.DistanceBetween(state, c.id, state.playerCountryId) <= DisplacementSystem.ReachableDistance);
            Assert.NotNull(nearby, "This fixture requires a real reachable host.");
            nearby.displacement.bordersClosed = false;
            state.PlayerCountry.displacement.displaced = 20f;
            return nearby.id;
        }

        [Test]
        public void DisplacementLinksFollowDirectionAndBorderPolicyWithoutErasingHostedPeople()
        {
            string host = PrepareDisplacement();
            var links = AsciiMapModes.DisplacementLinks(state);
            Assert.AreEqual(1, links.Count);
            Assert.AreEqual((state.playerCountryId, host), links[0]);
            string open = AsciiMapModes.Render(state, null, WorldMapMode.Displacement, 104, 23);
            state.FindCountry(host).displacement.bordersClosed = true;
            Assert.IsEmpty(AsciiMapModes.DisplacementLinks(state));
            Assert.AreNotEqual(open, AsciiMapModes.Render(state, null, WorldMapMode.Displacement, 104, 23));
            state.PlayerCountry.displacement.displaced = 0f;
            state.PlayerCountry.displacement.bordersClosed = false;
            state.FindCountry(host).displacement.displaced = 20f;
            Assert.AreEqual((host, state.playerCountryId), AsciiMapModes.DisplacementLinks(state)[0]);
            state.PlayerCountry.displacement.hosted = 9f;
            state.PlayerCountry.displacement.bordersClosed = true;
            Assert.IsEmpty(AsciiMapModes.DisplacementLinks(state));
            StringAssert.Contains("HOSTED 9.0", AsciiMapModes.Summary(state, WorldMapMode.Displacement));
            StringAssert.Contains("not people already hosted", AsciiMapModes.DisplacementReadout(state));
        }

        [Test]
        public void DisplacementDoesNotRevealThirdPartyConnectionsOrForeignAmounts()
        {
            string host = PrepareDisplacement();
            string before = AsciiMapModes.DisplacementReadout(state);
            string map = AsciiMapModes.Render(state, null, WorldMapMode.Displacement, 104, 23);
            foreach (var country in state.countries)
                if (country.id != state.playerCountryId)
                {
                    country.displacement.displaced = 87f;
                    country.displacement.hosted = 93f;
                }
            Assert.AreEqual(before, AsciiMapModes.DisplacementReadout(state));
            Assert.AreEqual(map, AsciiMapModes.Render(state, null, WorldMapMode.Displacement, 104, 23));
            Assert.AreEqual(1, AsciiMapModes.DisplacementLinks(state).Count);
        }

        [Test]
        public void DisplacementZeroAndUnknownPositionsDoNotInventConnections()
        {
            PrepareDisplacement();
            state.PlayerCountry.displacement.displaced = 0.99f;
            Assert.IsEmpty(AsciiMapModes.DisplacementLinks(state));
            var unknown = new CountryState { id = "UNMAPPED", displayName = "Unmapped state" };
            state.countries.Add(unknown);
            unknown.displacement.displaced = 40f;
            state.PlayerCountry.displacement.bordersClosed = false;
            Assert.IsEmpty(AsciiMapModes.DisplacementLinks(state));
            Assert.IsEmpty(DisplacementSystem.ReceivingWeights(state, unknown, out float total));
            Assert.AreEqual(0f, total);
        }

        [Test]
        public void TitledSuccessorRemainsListedWithoutInventingAMapCoordinate()
        {
            string host = PrepareDisplacement();
            state.FindCountry(host).displacement.bordersClosed = true;
            var successor = new CountryState { id = "NEW_HOST", displayName = "New receiving government" };
            state.countries.Add(successor);
            var ground = state.locations.Find(l => l.originalOwnerId == host);
            Assert.NotNull(ground);
            ground.originalOwnerId = successor.id;
            ground.ownerId = successor.id;
            Assert.IsNull(WorldFactory.FindProfile(successor.id));
            Assert.NotNull(GeographySystem.PositionFor(state, successor.id));
            var links = AsciiMapModes.DisplacementLinks(state);
            Assert.AreEqual(1, links.Count);
            Assert.AreEqual((state.playerCountryId, successor.id), links[0]);
            StringAssert.Contains(successor.displayName, AsciiMapModes.DisplacementReadout(state));
            StringAssert.Contains("Unmapped endpoints remain listed", AsciiMapModes.DisplacementReadout(state));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void DisplacementOverlayIsLiveBoundedPureAndPreservesCountryLabels(int columns)
        {
            PrepareDisplacement();
            string before = JsonUtility.ToJson(state);
            foreach (int rows in RealMapHeights)
            {
                var baseRows = AsciiWorldMap.Render(state, state.playerCountryId, columns, rows).Split('\n');
                string map = AsciiMapModes.Render(state, state.playerCountryId, WorldMapMode.Displacement, columns, rows);
                var lines = map.Split('\n');
                Assert.AreEqual(rows, lines.Length);
                Assert.AreNotEqual(string.Join("\n", baseRows), map, "The displacement overlay drew nothing.");
                for (int y = 0; y < rows; y++)
                {
                    Assert.AreEqual(columns, lines[y].Length);
                    for (int x = 0; x < columns; x++)
                        if (IsCountryLabel(baseRows[y][x])) Assert.AreEqual(baseRows[y][x], lines[y][x]);
                }
            }
            foreach (string text in new[] { AsciiMapModes.DisplacementReadout(state),
                AsciiMapModes.Legend(WorldMapMode.Displacement), AsciiMapModes.Summary(state, WorldMapMode.Displacement) })
                foreach (string line in AsciiChart.WrapBlock(text, columns).Split('\n'))
                    Assert.LessOrEqual(line.Length, columns);
            Assert.AreEqual(before, JsonUtility.ToJson(state));
        }

        [Test]
        public void SharedDisplacementWeightsMatchTheOriginalAllocationInOrder()
        {
            foreach (int seed in new[] { 4747, 6120, 982 })
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                for (int closed = 0; closed < world.countries.Count; closed++)
                {
                    world.countries[closed].displacement.bordersClosed = true;
                    foreach (var source in world.countries)
                    {
                        source.displacement.displaced = 20f;
                        var expected = new Dictionary<string, float>();
                        float expectedTotal = 0f;
                        foreach (var host in world.countries)
                        {
                            if (host.id == source.id || host.displacement.bordersClosed) continue;
                            float distance = GeographySystem.DistanceBetween(world, source.id, host.id);
                            if (distance > DisplacementSystem.ReachableDistance) continue;
                            float weight = 1f / Math.Max(4f, distance);
                            expected[host.id] = weight;
                            expectedTotal += weight;
                        }
                        var actual = DisplacementSystem.ReceivingWeights(world, source, out float total);
                        CollectionAssert.AreEqual(expected, actual);
                        Assert.AreEqual(expectedTotal, total);
                    }
                }
            }
        }

        [Test]
        public void MapOffersDisplacementModeAndTheActualButtonDisplaysItsReadout()
        {
            PrepareDisplacement();
            var gc = GameController.Instance;
            var previous = gc.State;
            string oldSave = SaveSystem.SaveDirectoryOverride;
            string directory = Path.Combine(Path.GetTempPath(), "brink-displacement-map-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SaveSystem.SaveDirectoryOverride = directory;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                var view = new WorldMapView();
                typeof(WorldMapView).GetMethod("Build", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                Button button = null;
                view.Root.Query<Button>().ForEach(b => { if (b.text == "DISPLACEMENT") button = b; });
                Assert.NotNull(button);
                var clickable = typeof(Button).GetProperty("clickable")?.GetValue(button);
                if (clickable != null) clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clickable, new object[] { null });
                else typeof(Button).GetMethod("SendClick").Invoke(button, null);
                bool found = false;
                view.Root.Query<Label>().ForEach(l => { if ((l.text ?? "").Contains("CURRENT HOSTING CONNECTIONS")) found = true; });
                Assert.IsTrue(found, "Selecting the mode must show its real readout, not only change a label.");
                Assert.IsEmpty(Directory.GetFiles(directory), "A map interaction must not save.");
            }
            finally
            {
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                SaveSystem.SaveDirectoryOverride = oldSave;
                Directory.Delete(directory, true);
            }
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
        /// Two separate faults flattened this skyline, and a test can easily
        /// guard only the first. The heights were invariant mod 3; fixing that
        /// but bucketing against the whole 0..100 range flattened it again,
        /// because a healthy economy occupies a narrow high band.
        ///
        /// The band is the whole point, so these outputs are set explicitly
        /// rather than taken from the fixture seed. An earlier version asserted
        /// against the seeded world and silently stopped guarding the
        /// calibration: on seed 6120 one sector sits at 59.7, which straddles
        /// the old 68-point bucket edge and yields two heights even under the
        /// broken mapping. Every value here is above that edge, so reverting to
        /// absolute 0-33 / 34-67 / 68-100 thresholds puts all seven in one
        /// bucket and fails this test.
        /// </summary>
        [Test]
        public void TheMarketSkylineVariesAcrossAHighBandOfSectorOutputs()
        {
            var sectors = state.PlayerCountry.economy.sectors;
            Assert.Greater(sectors.Count, 2, "the fixture has too few sectors to vary");

            // Ordinary healthy spread, all of it inside the old top bucket.
            float[] band = { 72f, 93f, 78f, 88f, 76f, 74f, 75f };
            for (int i = 0; i < sectors.Count; i++) sectors[i].output = band[i % band.Length];
            foreach (var sector in sectors)
                Assert.Greater(sector.output, 68f,
                    "a value below the old top bucket would let absolute thresholds pass");

            var heights = ColumnHeights(state);
            Assert.Greater(new System.Collections.Generic.HashSet<int>(heights).Count, 1,
                "every column is the same height — an ordinary high-band economy "
                + "cannot be told apart from a level one");

            // The tallest column is the strongest sector, not an arbitrary one.
            int strongest = 0, weakest = 0;
            for (int i = 0; i < sectors.Count; i++)
            {
                if (sectors[i].output > sectors[strongest].output) strongest = i;
                if (sectors[i].output < sectors[weakest].output) weakest = i;
            }
            Assert.Greater(heights[strongest], heights[weakest],
                "the tallest column does not belong to the strongest sector");

            // Presentation only, so the same state must draw the same picture.
            CollectionAssert.AreEqual(heights, ColumnHeights(state), "the skyline is not deterministic");

            var rows = AsciiPillarArt.Render(state, Pillar.Economy, 64).Split('\n');
            Assert.AreEqual(5, rows.Length);
            foreach (var row in rows) Assert.AreEqual(64, row.Length);
        }

        /// <summary>
        /// The state house used to stand on its own labels. At the 32-column
        /// floor the facade is centred across x=7..20 while STAB held x=1..7 and
        /// APP x=18..23, so row four read `STAB 64|_||_||_||APP 48`. Three-digit
        /// figures are the tightest case the readout can reach, so they are what
        /// this pins. Moving the labels back onto row four fails here twice
        /// over: they vanish from the heading row and the foundation breaks.
        /// </summary>
        [Test]
        public void TheStateHouseIsNeverOverwrittenByItsOwnReadout()
        {
            var player = state.PlayerCountry;
            player.stability = 100f;
            player.governmentApproval = 100f;

            var rows = AsciiPillarArt.Render(state, Pillar.Government, 32).Split('\n');

            Assert.AreEqual(5, rows.Length, "the signature is no longer five rows");
            foreach (var row in rows) Assert.AreEqual(32, row.Length, "the signature left its 32 columns");

            // The readout lives on the heading row, and only there.
            StringAssert.Contains("STAB 100", rows[0]);
            StringAssert.Contains("APP 100", rows[0]);
            for (int y = 1; y < 5; y++)
            {
                StringAssert.DoesNotContain("STAB", rows[y], $"STAB was drawn onto row {y}");
                StringAssert.DoesNotContain("APP", rows[y], $"APP was drawn onto row {y}");
            }

            // It does not run into the heading, and carries no part of the facade.
            StringAssert.Contains("STATE HOUSE ", rows[0], "the readout abuts the heading");
            foreach (char glyph in new[] { '/', '\\', '|' })
                Assert.IsFalse(rows[0].Contains(glyph.ToString()),
                    $"facade glyph '{glyph}' reached the readout row");

            // The building is whole: roof, upper facade, colonnade, foundation.
            StringAssert.Contains("/\\", rows[1], "the roof is broken");
            StringAssert.Contains("___/  \\___", rows[2], "the upper facade is broken");
            StringAssert.Contains("| || || || |", rows[3], "the colonnade is broken");
            StringAssert.Contains("_|_||_||_||_|_", rows[4],
                "the foundation is broken — a label was drawn across it");
        }

        [Test]
        public void ALevelEconomyStillReadsLevel()
        {
            foreach (var sector in state.PlayerCountry.economy.sectors) sector.output = 70f;

            Assert.AreEqual(1, DistinctColumnHeights(state),
                "a genuinely balanced economy must not be shown as uneven");
        }

        /// <summary>The height of each column the market skyline draws.</summary>
        static int[] ColumnHeights(GameState state)
        {
            var rows = AsciiPillarArt.Render(state, Pillar.Economy, 64).Split('\n');
            var heights = new System.Collections.Generic.List<int>();
            for (int x = 1; x < 44; x += 3)
            {
                int h = 0;
                for (int y = 4; y >= 0; y--) { if (rows[y][x] != '█') break; h++; }
                heights.Add(h);
            }
            Assert.Greater(heights.Count, 0, "the skyline drew no columns at all");
            return heights.ToArray();
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
        public void ActivityModeShowsOnlyPublicEventsFromLastMonth()
        {
            state.chronicle.Clear();
            state.date = new GameDate(2000, 2);
            string quiet = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(2000, 1), countryId = "CHN",
                category = ChronicleCategory.Political, publicity = Publicity.Public,
                text = "Public event"
            });
            string publicEvent = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);
            Assert.AreNotEqual(quiet, publicEvent);
            StringAssert.Contains("PUBLIC EVENTS LAST MONTH 1   ACTIVE STATES 1",
                AsciiMapModes.Summary(state, WorldMapMode.Activity));

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(2000, 1), countryId = "RUS",
                category = ChronicleCategory.Military, publicity = Publicity.Secret,
                text = "Hidden event"
            });
            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(2000, 1), countryId = "RUS",
                category = ChronicleCategory.Intelligence, publicity = Publicity.Public,
                text = "Misclassified intelligence event"
            });
            Assert.AreEqual(publicEvent,
                AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21),
                "The activity map bypassed the world wire and exposed a secret event.");

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(1999, 12), countryId = "RUS",
                category = ChronicleCategory.Military, publicity = Publicity.Public,
                text = "Old event"
            });
            Assert.AreEqual(publicEvent,
                AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21),
                "The activity map retained events older than the last month.");
        }

        [Test]
        public void ActivityModeDistinguishesOneEventFromSeveral()
        {
            state.chronicle.Clear();
            state.date = new GameDate(2000, 2);
            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(2000, 1), countryId = "CHN",
                category = ChronicleCategory.Political, publicity = Publicity.Public
            });
            string one = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(2000, 1), countryId = "CHN",
                category = ChronicleCategory.Economic, publicity = Publicity.Public
            });
            string several = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);

            Assert.AreNotEqual(one, several);
            StringAssert.Contains("PUBLIC EVENTS LAST MONTH 2   ACTIVE STATES 1",
                AsciiMapModes.Summary(state, WorldMapMode.Activity));
        }

        // ---------- overlay markers never eat the map's own labels ----------

        static readonly int[] RealMapHeights = { 11, 17, 23 };
        static readonly int[] PanelWidths = { 34, 41, 49, 64, 104 };

        static bool IsCountryLabel(char cell)
            => char.IsLetterOrDigit(cell)
               || cell == '[' || cell == ']' || cell == '<' || cell == '>';

        /// <summary>
        /// Give every state a public event so ACTIVITY marks the whole roster,
        /// which is the density that exposed the collision.
        /// </summary>
        void MarkEveryState()
        {
            state.chronicle.Clear();
            state.date = new GameDate(2000, 2);
            foreach (var country in state.countries)
                state.chronicle.Add(new ChronicleEntry
                {
                    date = new GameDate(2000, 1), countryId = country.id,
                    category = ChronicleCategory.Political, publicity = Publicity.Public,
                    text = "e"
                });
        }

        /// <summary>
        /// The overlays draw beside the base map, never through it. Each mode
        /// used to plot onto `y - 1` unconditionally, and once the map is
        /// squeezed into 11, 17 or 23 rows that cell is another country's code
        /// row for most of the roster — so a marker quietly ate a letter of
        /// someone else's name.
        /// </summary>
        [Test]
        public void OverlayMarkersNeverReplaceACountryLabel()
        {
            MarkEveryState();
            string selected = null;
            foreach (var country in state.countries)
                if (country.id != state.playerCountryId) { selected = country.id; break; }
            Assert.IsNotNull(selected, "the fixture has no foreign state to select");

            foreach (int rows in RealMapHeights)
            foreach (int columns in PanelWidths)
            {
                var baseline = AsciiMapModes.Render(state, selected, WorldMapMode.Political, columns, rows)
                    .Split('\n');

                foreach (WorldMapMode mode in System.Enum.GetValues(typeof(WorldMapMode)))
                {
                    if (mode == WorldMapMode.Political) continue;
                    var overlay = AsciiMapModes.Render(state, selected, mode, columns, rows).Split('\n');

                    for (int y = 0; y < rows; y++)
                    for (int x = 0; x < columns; x++)
                    {
                        if (!IsCountryLabel(baseline[y][x])) continue;
                        Assert.AreEqual(baseline[y][x], overlay[y][x],
                            $"{mode} at {columns}x{rows} overwrote the label '{baseline[y][x]}' "
                            + $"at ({x},{y}) with '{overlay[y][x]}'");
                    }
                }
            }
        }

        /// <summary>
        /// Displacing a marker must not silently discard it. With the whole
        /// roster active, ACTIVITY still has to draw something at every size.
        /// </summary>
        [Test]
        public void DisplacedActivityMarkersAreStillDrawn()
        {
            MarkEveryState();

            foreach (int rows in RealMapHeights)
            foreach (int columns in PanelWidths)
            {
                string map = AsciiMapModes.Render(state, null, WorldMapMode.Activity, columns, rows);
                int markers = 0;
                foreach (char cell in map) if (cell == '\u2022' || cell == '*') markers++;
                Assert.Greater(markers, 0, $"ACTIVITY drew nothing at {columns}x{rows}");
            }
        }

        /// <summary>
        /// Every state the summary counts is a state the map actually shows.
        ///
        /// Protecting country labels was not enough on its own. A marker is not
        /// a label, so the label test waved through a cell another state's
        /// marker already held, and `overwrite: true` did the rest: a later
        /// state silently erased an earlier one while the summary went on
        /// counting activity that was no longer drawn.
        ///
        /// **This has to be a FULL-roster world.** The fixture's Standard
        /// sixteen states never crowd each other — measured across five seeds
        /// and eighteen grids, every one places cleanly with or without the
        /// claim set, so a Standard-world version of this test passes just as
        /// happily with the guard removed. At twenty-four states the map loses
        /// exactly one marker at 34x11 and 49x11, on every seed tried. A
        /// regression test for a crowding bug has to be run in a crowd.
        /// </summary>
        [Test]
        public void EveryActiveStateTheSummaryCountsIsDrawnOnTheMap()
        {
            var crowded = WorldFactory.CreateWorld(6120, "USA", WorldSize.Full);
            crowded.chronicle.Clear();
            crowded.date = new GameDate(2000, 2);
            foreach (var country in crowded.countries)
                crowded.chronicle.Add(new ChronicleEntry
                {
                    date = new GameDate(2000, 1), countryId = country.id,
                    category = ChronicleCategory.Political, publicity = Publicity.Public,
                    text = "e"
                });

            var summary = AsciiMapModes.Summary(crowded, WorldMapMode.Activity);
            int active = int.Parse(summary.Substring(summary.IndexOf("ACTIVE STATES", StringComparison.Ordinal)
                + "ACTIVE STATES".Length).Trim());
            Assert.Greater(active, 16,
                "this needs the full roster; the standard sixteen never crowd each other");

            foreach (int rows in RealMapHeights)
            foreach (int columns in PanelWidths)
            {
                string map = AsciiMapModes.Render(crowded, null, WorldMapMode.Activity, columns, rows);
                int drawn = 0;
                foreach (char cell in map) if (cell == '\u2022' || cell == '*') drawn++;

                Assert.AreEqual(active, drawn,
                    $"at {columns}x{rows} the summary counts {active} active states but the map "
                    + $"draws {drawn} markers — one state's signal overwrote another's");
            }
        }

        [Test]
        public void ActivityPreservesTheGridAtRealMapHeights()
        {
            MarkEveryState();
            foreach (int rows in RealMapHeights)
            foreach (int columns in PanelWidths)
            {
                var lines = AsciiMapModes.Render(state, null, WorldMapMode.Activity, columns, rows)
                    .Split('\n');
                Assert.AreEqual(rows, lines.Length, $"{columns}x{rows}");
                foreach (var line in lines) Assert.AreEqual(columns, line.Length, $"{columns}x{rows}");
            }
        }

        // ---------- the month window ----------

        /// <summary>
        /// January's previous month is the December before it. Verified in the
        /// *including* direction: the existing test only proves a December entry
        /// is rejected from February, which a broken year rollover also does.
        /// </summary>
        [Test]
        public void JanuaryShowsThePreviousDecember()
        {
            state.chronicle.Clear();
            state.date = new GameDate(2000, 1);
            string quiet = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(1999, 12), countryId = "CHN",
                category = ChronicleCategory.Political, publicity = Publicity.Public, text = "e"
            });

            Assert.AreNotEqual(quiet, AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21),
                "a December event is invisible in January — the year rollover is broken");
            StringAssert.Contains("PUBLIC EVENTS LAST MONTH 1   ACTIVE STATES 1",
                AsciiMapModes.Summary(state, WorldMapMode.Activity));

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(1999, 11), countryId = "RUS",
                category = ChronicleCategory.Political, publicity = Publicity.Public, text = "e"
            });
            StringAssert.Contains("PUBLIC EVENTS LAST MONTH 1   ACTIVE STATES 1",
                AsciiMapModes.Summary(state, WorldMapMode.Activity));
        }

        [Test]
        public void ActivityIgnoresTheMonthStillBeingPlayed()
        {
            state.chronicle.Clear();
            state.date = new GameDate(2000, 2);
            string quiet = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);

            state.chronicle.Add(new ChronicleEntry
            {
                date = new GameDate(2000, 2), countryId = "CHN",
                category = ChronicleCategory.Political, publicity = Publicity.Public, text = "e"
            });

            Assert.AreEqual(quiet, AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21),
                "an event from the current month appeared on the map");
            StringAssert.Contains("PUBLIC EVENTS LAST MONTH 0   ACTIVE STATES 0",
                AsciiMapModes.Summary(state, WorldMapMode.Activity));
        }

        /// <summary>
        /// The wire carries global lines with no country, and a save may name a
        /// state this roster does not have. Neither has a point on the map, and
        /// neither may inflate the counts beside it.
        /// </summary>
        [Test]
        public void GlobalAndUnknownEntriesReachNeitherTheMapNorTheCount()
        {
            state.chronicle.Clear();
            state.date = new GameDate(2000, 2);
            string quiet = AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21);

            foreach (string id in new[] { "", null, "ZZZ", "XXX" })
                state.chronicle.Add(new ChronicleEntry
                {
                    date = new GameDate(2000, 1), countryId = id,
                    category = ChronicleCategory.Political, publicity = Publicity.Public, text = "e"
                });

            Assert.AreEqual(quiet, AsciiMapModes.Render(state, null, WorldMapMode.Activity, 78, 21),
                "a global or unknown entry drew a marker");
            StringAssert.Contains("PUBLIC EVENTS LAST MONTH 0   ACTIVE STATES 0",
                AsciiMapModes.Summary(state, WorldMapMode.Activity));
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

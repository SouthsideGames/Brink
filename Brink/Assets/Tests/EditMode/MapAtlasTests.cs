using System.Linq;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The real-geography charts (spec 09 §7): every authored state has an
    /// outline, the charts keep the terminal's exact-width rule, the world map's
    /// markers never collide, and the intelligence layer still decides what is
    /// drawn inside a foreign border.
    /// </summary>
    public class MapAtlasTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateWorld(4242, WorldFactory.PlayerCountryId, WorldSize.Full);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void Collect(string countryId, float penetration)
            => state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId, targetId = countryId,
                focus = IntelDomain.Military, penetration = penetration
            });

        [Test]
        public void EveryAuthoredCountryHasAnOutlineAndACapitalAnchor()
        {
            foreach (var profile in WorldFactory.Profiles)
            {
                if (state.FindCountry(profile.id) == null) continue;
                Assert.IsTrue(MapAtlas.TryGetChart(profile.id, out var chart), $"{profile.id} has no chart");
                Assert.IsTrue(MapAtlas.TryWorldAnchor(profile.id, out _, out _), $"{profile.id} has no world anchor");
                int land = chart.Regions.Sum(row => row.Count(c => c != ' '));
                Assert.Greater(land, 100, $"{profile.id}'s outline is almost empty");
            }
        }

        [Test]
        public void EveryAuthoredSiteHasARealPosition()
        {
            // CONTESTED_LANE is deliberately generic and has none.
            foreach (var location in state.locations)
            {
                if (location.id == "CONTESTED_LANE") continue;
                Assert.IsTrue(MapAtlas.TryWorldSite(location.id, out _, out _),
                    $"{location.id} ({location.displayName}) has no position on the world chart");
            }
        }

        [Test]
        public void CountryChartsAreExactlyTheirColumnsWide()
        {
            foreach (var country in state.countries)
            foreach (int columns in new[] { 34, 49, 64, 80, 104 })
            {
                string chart = AsciiCountryMap.Render(state, country.id, columns,
                    AsciiCountryMap.RowsFor(columns, 17));
                var rows = chart.Split('\n');
                Assert.LessOrEqual(rows.Length, AsciiCountryMap.RowsFor(columns, 17), country.id);
                foreach (var row in rows)
                    Assert.AreEqual(columns, row.Length, $"{country.id} at {columns} columns drew a ragged row");
            }
        }

        [Test]
        public void TheCapitalIsAlwaysPlotted()
        {
            foreach (var country in state.countries)
            {
                string code = WorldFactory.FindProfile(country.id).mapCode;
                string chart = AsciiCountryMap.Render(state, country.id, 64, 32);
                StringAssert.Contains("@", chart, $"{country.id}'s capital is missing");
                StringAssert.Contains("=" + code, chart, $"{country.id}'s capital is untagged");
            }
        }

        [Test]
        public void AnUncollectedCountryShowsItsBordersAndNothingInside()
        {
            string chart = AsciiCountryMap.Render(state, "CHN", 96, 34);
            // Border glyphs, state codes (lower case) and the capital tag only.
            foreach (char c in "PIEAMV#*^~")
                Assert.IsFalse(chart.Contains(c), $"'{c}' drawn inside a country we have not collected on");
            StringAssert.Contains("NOT SURVEYED", AsciiCountryMap.DescribeTerrain(state, "CHN"));
        }

        [Test]
        public void GoodCoverageChartsSitesAndTerrain()
        {
            Collect("CHN", 25f);
            string sites = AsciiCountryMap.Render(state, "CHN", 96, 34);
            StringAssert.Contains("P", sites, "partial coverage names the port");
            Assert.IsFalse(sites.Contains('^'), "partial coverage does not survey the ground");

            state.networks[state.networks.Count - 1].penetration = 80f;
            string detailed = AsciiCountryMap.Render(state, "CHN", 96, 34);
            StringAssert.Contains("^", detailed, "China's ranges should be charted with good coverage");
            string terrain = AsciiCountryMap.DescribeTerrain(state, "CHN");
            StringAssert.Contains("RANGES", terrain);
            StringAssert.Contains("DESERTS", terrain);
        }

        [Test]
        public void OurOwnChartShowsItsStates()
        {
            string chart = AsciiCountryMap.Render(state, "USA", 104, 34);
            StringAssert.Contains(" tx ", chart, "Texas should be labelled on a wide chart");
            StringAssert.Contains("^", chart, "our own ground is fully surveyed");
        }

        [Test]
        public void ChartsAreDeterministic()
        {
            Assert.AreEqual(AsciiCountryMap.Render(state, "RUS", 72, 30),
                            AsciiCountryMap.Render(state, "RUS", 72, 30));
            Assert.AreEqual(AsciiWorldMap.Render(state, "IND", 72, 17),
                            AsciiWorldMap.Render(state, "IND", 72, 17));
        }

        [Test]
        public void WorldMarkersNeverOverlapAndStayOnTheGrid()
        {
            foreach (var (columns, rows) in new[] { (34, 11), (40, 11), (49, 17), (64, 17), (78, 21), (104, 23) })
            {
                var slots = AsciiWorldMap.MarkerSlots(state, columns, rows);
                Assert.AreEqual(state.countries.Count(c => WorldFactory.FindProfile(c.id) != null), slots.Count,
                    $"a state lost its marker at {columns}x{rows}");
                var list = slots.ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i].Value;
                    Assert.IsTrue(a.x >= 0 && a.x + 4 <= columns && a.y >= 0 && a.y < rows,
                        $"{list[i].Key} off the grid at {columns}x{rows}");
                    for (int j = i + 1; j < list.Count; j++)
                    {
                        var b = list[j].Value;
                        Assert.IsFalse(a.y == b.y && a.x < b.x + 4 && b.x < a.x + 4,
                            $"{list[i].Key} and {list[j].Key} overlap at {columns}x{rows}");
                    }
                }
            }
        }

        [Test]
        public void TheWorldChartShowsRealLand()
        {
            string map = AsciiWorldMap.Render(state, null, 104, 23);
            int land = map.Count(c => c == AsciiWorldMap.Land || c == AsciiWorldMap.Highlight);
            Assert.Greater(land, 104 * 23 / 5, "the landmass is missing");
            Assert.Less(land, 104 * 23 * 3 / 5, "the oceans are missing");
            StringAssert.Contains(AsciiWorldMap.Highlight.ToString(), map, "our own ground is not shaded");
        }
    }
}

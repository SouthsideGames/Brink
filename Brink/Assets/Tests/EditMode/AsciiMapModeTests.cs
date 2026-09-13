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
    }
}
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The World Chronicle (GDD §31.3): a long save should become its own
    /// alternate history, archived by country and category as it happens.
    /// </summary>
    public class ChronicleTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static TurnManager FullSimulation(GameState state)
        {
            var turns = new TurnManager(state);
            turns.ResolveMonth += CabinetSystem.MonthlyAct;
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;
            turns.ResolveMonth += TechnologySystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;
            return turns;
        }

        [Test]
        public void ADecade_ProducesAReadableHistory()
        {
            var state = WorldFactory.CreateDebugWorld(1400);
            var turns = FullSimulation(state);

            for (int i = 0; i < 120; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            Assert.Greater(state.chronicle.Count, 40, "A decade should leave a substantial record.");

            foreach (var entry in state.chronicle)
            {
                Assert.IsNotEmpty(entry.text, "Every entry must say something.");
                Assert.Greater(entry.date.year, 0);
                if (string.IsNullOrEmpty(entry.countryId)) continue;
                Assert.NotNull(state.FindCountry(entry.countryId),
                    $"Entry attributed to unknown country '{entry.countryId}'.");
            }
        }

        [Test]
        public void History_IsAttributedAcrossCountriesAndCategories()
        {
            var state = WorldFactory.CreateDebugWorld(1401);
            var turns = FullSimulation(state);

            for (int i = 0; i < 240; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            var countries = new System.Collections.Generic.HashSet<string>();
            var categories = new System.Collections.Generic.HashSet<ChronicleCategory>();
            foreach (var entry in state.chronicle)
            {
                if (!string.IsNullOrEmpty(entry.countryId)) countries.Add(entry.countryId);
                categories.Add(entry.category);
            }

            Assert.Greater(countries.Count, 1, "History should not be about us alone (GDD §17).");
            Assert.Greater(categories.Count, 2, "And it should span more than one kind of event.");
        }

        [Test]
        public void MajorEvents_AreRecordedWhereAPlayerWouldLookForThem()
        {
            var state = WorldFactory.CreateDebugWorld(1402);
            state.commandPoints.current = 60;

            int before = state.chronicle.Count;

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", SanctionSeverity.Coercive);

            Assert.Greater(state.chronicle.Count, before,
                "Opening a confrontation and imposing sanctions are both history.");

            bool sawDiplomatic = false, sawEconomic = false;
            for (int i = before; i < state.chronicle.Count; i++)
            {
                if (state.chronicle[i].category == ChronicleCategory.Diplomatic) sawDiplomatic = true;
                if (state.chronicle[i].category == ChronicleCategory.Economic) sawEconomic = true;
            }
            Assert.IsTrue(sawDiplomatic && sawEconomic, "Filed under the category a player would expect.");
        }

        [Test]
        public void Chronicle_IsOrderedAndSurvivesSaveRoundTrip()
        {
            var state = WorldFactory.CreateDebugWorld(1403);
            var turns = FullSimulation(state);
            for (int i = 0; i < 60; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            for (int i = 1; i < state.chronicle.Count; i++)
                Assert.LessOrEqual(state.chronicle[i - 1].date.CompareTo(state.chronicle[i].date), 0,
                    "The record should read forward in time.");

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.chronicle.Count, loaded.chronicle.Count);
            Assert.AreEqual(state.chronicle[0].text, loaded.chronicle[0].text);
            Assert.AreEqual(state.chronicle[0].date, loaded.chronicle[0].date);
        }

        [Test]
        public void MonthCode_IsAvailableForTheRecord()
        {
            Assert.AreEqual("MAR", new GameDate(1984, 3).MonthCode());
            Assert.AreEqual("DEC", new GameDate(1984, 12).MonthCode());
        }
    }
}

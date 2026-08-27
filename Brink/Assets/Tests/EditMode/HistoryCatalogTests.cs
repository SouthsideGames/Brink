using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The world before the operator arrived (GDD §31.3).
    ///
    /// NARROW PIPELINE: no monthly systems at all. Every claim here is about what
    /// `WorldFactory.CreateWorld` leaves behind at month zero — the record, and
    /// what it deliberately does *not* touch. Running a month would only add
    /// noise from systems this is not about.
    /// </summary>
    public class HistoryCatalogTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 9014);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void ANewWorldHasAPast()
        {
            int before = 0;
            foreach (var entry in state.chronicle)
                if (entry.date.year < state.startDate.year) before++;

            Assert.Greater(before, 10,
                "A new save opened with an empty chronicle. A world with no history is a "
                + "world that began the moment the operator was posted to it.");
        }

        [Test]
        public void EveryCountryAppearsInTheRecord()
        {
            foreach (var country in state.countries)
            {
                bool found = false;
                foreach (var entry in state.chronicle)
                {
                    if (entry.date.year >= state.startDate.year) continue;
                    if (entry.countryId != country.id) continue;
                    found = true;
                    break;
                }

                Assert.IsTrue(found,
                    $"{country.displayName} has no past at all. A state with no record is "
                    + "the anonymity the authored-traits work already fixed once.");
            }
        }

        [Test]
        public void HistoryIsDatedBeforeTheSaveBegins()
        {
            foreach (var entry in state.chronicle)
            {
                if (entry.category == ChronicleCategory.System) continue;
                if (entry.date.year >= state.startDate.year) continue;

                Assert.GreaterOrEqual(entry.date.year, HistoryCatalog.FirstYear,
                    $"An entry is dated {entry.date.DisplayString}, before the record reaches.");
                Assert.LessOrEqual(entry.date.year, state.startDate.year,
                    "An entry is dated after the save begins but treated as history.");
                Assert.GreaterOrEqual(entry.date.month, 1);
                Assert.LessOrEqual(entry.date.month, 12);
            }
        }

        // ---------- the rule that keeps it backstory ----------

        [Test]
        public void HistoryMovesNoNationalStatistic()
        {
            // The same world, built twice: once as the factory builds it, once
            // with the history seeded a second time on top. If any line here
            // touched a statistic, doing it twice would show.
            var control = WorldFactory.CreateDebugWorld(seed: 9014);
            HistoryCatalog.Seed(control);

            for (int i = 0; i < state.countries.Count; i++)
            {
                var a = state.countries[i];
                var b = control.countries[i];

                Assert.AreEqual(a.pillars.economy, b.pillars.economy, 0.0001f, a.id);
                Assert.AreEqual(a.pillars.military, b.pillars.military, 0.0001f, a.id);
                Assert.AreEqual(a.stability, b.stability, 0.0001f, a.id);
                Assert.AreEqual(a.governmentApproval, b.governmentApproval, 0.0001f, a.id);
                Assert.AreEqual(a.publicGrievance, b.publicGrievance, 0.0001f, a.id);
                Assert.AreEqual(a.resources.treasury, b.resources.treasury, 0.0001f, a.id);
            }
        }

        [Test]
        public void SeededMemoriesCarryNoWeight()
        {
            // `memoryWeight` is read by treaty acceptance, sanctions relief and
            // alliance willingness. Seeding it would retune all three — and every
            // balance figure taken with them — before the first month was played.
            foreach (var relationship in state.relationships)
                Assert.AreEqual(0f, relationship.memoryWeight, 0.0001f,
                    $"{relationship.countryA}/{relationship.countryB} opened the save with "
                    + "weighted memory. History explains the starting position; it must not "
                    + "create it.");
        }

        [Test]
        public void ARecordedQuarrelMeansTheyActuallyAreCold()
        {
            // Derived, not authored: a grievance is written only between states
            // world generation already made cold. An authored 1974 quarrel
            // between opening allies would be worse than no history.
            foreach (var relationship in state.relationships)
            {
                bool quarrel = false;
                foreach (string line in relationship.memory)
                    if (line.Contains("quarrel")) quarrel = true;

                if (!quarrel) continue;

                Assert.LessOrEqual(relationship.relations, 40f,
                    $"{relationship.countryA} and {relationship.countryB} carry a recorded "
                    + "quarrel and open on warm terms. The record contradicts the numbers.");
            }
        }

        // ---------- determinism ----------

        [Test]
        public void TheSameSeedWritesTheSameHistory()
        {
            var again = WorldFactory.CreateDebugWorld(seed: 9014);

            Assert.AreEqual(state.chronicle.Count, again.chronicle.Count,
                "Two worlds from the same seed wrote different amounts of history.");

            for (int i = 0; i < state.chronicle.Count; i++)
            {
                Assert.AreEqual(state.chronicle[i].text, again.chronicle[i].text,
                    $"Entry {i} differs between two builds of the same seed.");
                Assert.AreEqual(state.chronicle[i].date.SortKey, again.chronicle[i].date.SortKey);
            }
        }

        [Test]
        public void HistorySurvivesASaveAndReload()
        {
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.chronicle.Count, loaded.chronicle.Count);
        }
    }
}

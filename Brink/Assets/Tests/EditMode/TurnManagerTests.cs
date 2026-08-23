using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class TurnManagerTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 12345);
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void EndMonth_AdvancesDate()
        {
            var before = state.date;
            turns.EndMonth();
            Assert.AreEqual(before.NextMonth(), state.date);
        }

        [Test]
        public void EndMonth_RefreshesCommandPoints()
        {
            turns.SpendCommandPoints(5, "test");
            Assert.AreEqual(0, state.commandPoints.current);
            turns.EndMonth();
            Assert.AreEqual(state.commandPoints.baselinePerMonth, state.commandPoints.current);
        }

        [Test]
        public void UnusedCP_CarriesIntoReserve_UpToCap()
        {
            // Full 5 CP unused; reserve cap is 2, so next month is baseline + 2.
            turns.EndMonth();
            Assert.AreEqual(state.commandPoints.baselinePerMonth + state.commandPoints.reserveCap,
                state.commandPoints.current);
        }

        [Test]
        public void SpendCommandPoints_FailsWhenInsufficient()
        {
            Assert.IsTrue(turns.SpendCommandPoints(5, "big intervention"));
            Assert.IsFalse(turns.SpendCommandPoints(1, "one too many"));
            Assert.AreEqual(0, state.commandPoints.current);
        }

        [Test]
        public void YearEnded_FiresOnlyWhenDecemberResolves()
        {
            int yearEndedCount = 0;
            int reportedYear = 0;
            turns.YearEnded += y => { yearEndedCount++; reportedYear = y; };

            for (int i = 0; i < 12; i++)
                turns.EndMonth();

            Assert.AreEqual(1, yearEndedCount);
            Assert.AreEqual(state.startDate.year, reportedYear);
            Assert.AreEqual(new GameDate(state.startDate.year + 1, 1), state.date);
        }

        [Test]
        public void ResolveMonth_FiresBeforeDateAdvances()
        {
            GameDate dateDuringResolve = default;
            var before = state.date;
            // NARROW PIPELINE: not a pipeline at all — a probe that records the date
            // the handler was called with. `SimulationPipeline` is omitted on
            // purpose throughout this file: these tests are about the turn manager
            // itself (date arithmetic, CP refresh and reserve, the year-end hook,
            // handler ordering), and every assertion is on TurnManager's own
            // bookkeeping, which no monthly system touches. The ten-year run below
            // asserts only that 120 months elapsed and the roster survived — it is
            // checking the loop, not the world; the world's long-run health is
            // WorldInvariantTests' job, on the real pipeline.
            turns.ResolveMonth += s => dateDuringResolve = s.date;
            turns.EndMonth();
            Assert.AreEqual(before, dateDuringResolve);
        }

        [Test]
        public void LongUnattendedRun_TenYears_StaysConsistent()
        {
            for (int i = 0; i < 120; i++)
                turns.EndMonth();

            Assert.AreEqual(120, state.date.MonthsSince(state.startDate));
            Assert.AreEqual(WorldFactory.Profiles.Length, state.countries.Count);
            Assert.NotNull(state.PlayerCountry);
        }
    }
}

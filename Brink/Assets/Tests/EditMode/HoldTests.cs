using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Standing back through a quiet stretch (GDD §6, §28.2).
    ///
    /// The claims: a hold ends the moment the game needs the operator, the months
    /// it consumes are genuinely forgone rather than banked, and it is never a way
    /// to have something decided for you while the clock runs.
    /// </summary>
    public class HoldTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4477);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void AQuietStretchRunsToCompletion()
        {
            var start = state.date;
            var result = HoldSystem.Hold(state, turns, 3);

            Assert.Greater(result.months, 0, "the hold advanced nothing: " + result.stopped);
            Assert.AreEqual(result.months, state.date.MonthsSince(start),
                "The hold reported a different number of months than it actually resolved.");
        }

        [Test]
        public void AHoldNeverRunsPastTheCap()
        {
            var result = HoldSystem.Hold(state, turns, 500);

            Assert.LessOrEqual(result.months, HoldSystem.MaxMonths,
                "A hold ran past its cap. Standing back from a quiet stretch is not a way "
                + "to skip to the end of the save.");
        }

        // ---------- it stops when it should ----------

        [Test]
        public void AnOpenCrisisRefusesTheHoldOutright()
        {
            state.activeCrises.Add(new ActiveCrisis
            {
                defId = "TEST",
                title = "TEST CRISIS",
                body = "Something requires a decision."
            });

            Assert.IsFalse(HoldSystem.CanHold(state, out string reason),
                "A hold began on top of an unanswered crisis, which would resolve it by "
                + "lapsing — a legitimate outcome of ending a month and an illegitimate "
                + "side effect of asking for quiet.");
            Assert.IsNotEmpty(reason);

            var result = HoldSystem.Hold(state, turns, 6);
            Assert.AreEqual(0, result.months);
            Assert.IsNotEmpty(result.stopped);
        }

        [Test]
        public void AVacantOfficeRefusesTheHold()
        {
            state.PlayerCountry.vacancies.Add(new CabinetVacancy { office = Pillar.Economy });

            Assert.IsFalse(HoldSystem.CanHold(state, out string reason),
                "A hold ran while a shortlist was waiting — the government would have "
                + "appointed for us.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void AWarRefusesTheHold()
        {
            var enemy = state.countries.Find(c => !c.isPlayer);
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId,
                enemy.id, ConfrontationObjective.PolicyReversal, "", PrimaryStrategy.Military);
            Assert.IsNotNull(confrontation, "the fixture could not open a confrontation");
            confrontation.escalation = EscalationState.LimitedConflict;

            Assert.IsFalse(HoldSystem.CanHold(state, out string reason),
                "A hold ran through a shooting war. Nothing about that month is quiet.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void AHoldStopsOnFlashTrafficRatherThanRunningThrough()
        {
            // A world where something is going to demand attention: FLASH is the
            // game's own definition of traffic that cannot be left alone, so it is
            // the right tripwire rather than a second list that would drift.
            bool everStopped = false;
            for (int attempt = 0; attempt < 40 && !everStopped; attempt++)
            {
                if (!HoldSystem.CanHold(state, out _))
                {
                    everStopped = true;   // refusing outright is the same guarantee
                    break;
                }

                var result = HoldSystem.Hold(state, turns, HoldSystem.MaxMonths);
                if (!result.RanToCompletion) everStopped = true;
            }

            Assert.IsTrue(everStopped,
                "Forty consecutive full-length holds ran to completion without the world "
                + "ever interrupting one. Either nothing in this game ever needs the "
                + "operator, or the tripwire is not connected.");
        }

        // ---------- and it costs ----------

        [Test]
        public void HeldMonthsAreForgoneRatherThanBanked()
        {
            state.commandPoints.current = 5;

            var result = HoldSystem.Hold(state, turns, 4);
            Assert.Greater(result.months, 1, "the hold did not advance far enough to measure");

            Assert.LessOrEqual(state.commandPoints.current,
                state.commandPoints.baselinePerMonth + state.commandPoints.reserveCap,
                "Holding banked the command capacity of every month it skipped. The whole "
                + "trade is that the months are cheaper in attention and more expensive in "
                + "everything else.");
        }

        [Test]
        public void HoldingRecordsNoInitiativeAndNoXP()
        {
            int xp = state.strategistXP;
            int initiative = state.initiativesThisYear;

            HoldSystem.Hold(state, turns, HoldSystem.MaxMonths);

            Assert.LessOrEqual(state.initiativesThisYear, initiative,
                "Standing back was recorded as an initiative, which would make doing "
                + "nothing grade like doing something.");
            Assert.LessOrEqual(state.strategistXP - xp, 200,
                "Holding paid XP beyond the passive monthly baseline.");
        }
    }
}

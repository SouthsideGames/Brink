using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// What the operator is told (GDD §28.1). The interesting assertions here
    /// are the *limits*: a filter on information is only a good mechanic while
    /// it cannot take away the player's ability to act, and while the player can
    /// see it coming.
    /// </summary>
    public class ReportingSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            ReportingSystem.Disabled = false;
            state = WorldFactory.CreateDebugWorld(seed: 5150);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
        }

        [TearDown]
        public void TearDown()
        {
            ReportingSystem.Disabled = false;
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        Official Desk(ReportingDesk desk) => ReportingSystem.OfficialFor(state, desk);

        void SetDesk(ReportingDesk desk, float competence, ControlMode mode)
        {
            var official = Desk(desk);
            official.competence = competence;
            official.trust = 70f;
            official.mode = mode;
        }

        /// <summary>
        /// Clear first and stay well under <c>GameState.MaxNotifications</c> —
        /// the list self-trims, which would silently invalidate the counts these
        /// tests assert on.
        /// </summary>
        int Fill(NotificationClass priority, ReportingDesk desk, int count)
        {
            state.notifications.Clear();
            for (int i = 0; i < count; i++)
                state.AddNotification(priority, $"ITEM {i}", "body", state.playerCountryId, desk);
            return 0;
        }

        // ---------- the two rules that keep it fair ----------

        [Test]
        public void ADecisionIsNeverWithheld()
        {
            SetDesk(ReportingDesk.Military, 0f, ControlMode.Autonomous);
            int start = Fill(NotificationClass.Flash, ReportingDesk.Military, 60);

            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(60, state.notifications.Count - start,
                "FLASH means an answer is required this month. Suppressing one strands the " +
                "player in front of a turn they cannot take — incompetence costs awareness, " +
                "never agency.");
        }

        [Test]
        public void CommandTrafficIsNeverFiltered()
        {
            foreach (var official in state.cabinet)
            {
                official.competence = 0f;
                official.mode = ControlMode.Autonomous;
            }
            int start = Fill(NotificationClass.Wire, ReportingDesk.Command, 60);

            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(60, state.notifications.Count - start,
                "The operator's own traffic has no intermediary to lose it.");
        }

        [Test]
        public void WhatYouRunYourselfYouSeeYourself()
        {
            SetDesk(ReportingDesk.Economy, 0f, ControlMode.DirectControl);
            int start = Fill(NotificationClass.Wire, ReportingDesk.Economy, 60);

            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(60, state.notifications.Count - start,
                "Direct Control removes the intermediary, so it removes the filter. " +
                "Its cost is CP and the official's trust, not blindness.");
        }

        [Test]
        public void ImportantNewsIsBuriedButNeverLost()
        {
            SetDesk(ReportingDesk.Diplomacy, 0f, ControlMode.Autonomous);
            int start = Fill(NotificationClass.Priority, ReportingDesk.Diplomacy, 60);

            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(60, state.notifications.Count - start,
                "PRIORITY traffic loses its urgency, not its existence.");

            bool anyDowngraded = false;
            for (int i = start; i < state.notifications.Count; i++)
                if (state.notifications[i].priority == NotificationClass.Advisory) anyDowngraded = true;

            Assert.IsTrue(anyDowngraded,
                "A failing desk should be burying some of its important traffic.");
        }

        // ---------- the filter actually does something ----------

        [Test]
        public void AFailingDeskLosesRoutineTraffic()
        {
            SetDesk(ReportingDesk.Intelligence, 10f, ControlMode.Autonomous);
            int start = Fill(NotificationClass.Wire, ReportingDesk.Intelligence, 150);

            ReportingSystem.FilterMonth(state, start);

            Assert.Less(state.notifications.Count - start, 150,
                "A barely-functioning desk should be losing routine items.");
        }

        [Test]
        public void AStrongDeskLosesNothing()
        {
            SetDesk(ReportingDesk.Intelligence, 100f, ControlMode.Autonomous);
            int start = Fill(NotificationClass.Wire, ReportingDesk.Intelligence, 150);

            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(150, state.notifications.Count - start,
                "A capable minister files everything they have.");
        }

        [Test]
        public void BetterAppointmentsMeanBetterReporting()
        {
            SetDesk(ReportingDesk.Military, 40f, ControlMode.Autonomous);
            float weak = ReportingSystem.MishandleChanceFor(state, ReportingDesk.Military);

            SetDesk(ReportingDesk.Military, 70f, ControlMode.Autonomous);
            float strong = ReportingSystem.MishandleChanceFor(state, ReportingDesk.Military);

            Assert.Less(strong, weak,
                "Competence has to be the thing that drives this, or appointing well buys nothing.");
        }

        [Test]
        public void DirectedReportsBetterThanAutonomous()
        {
            SetDesk(ReportingDesk.Economy, 45f, ControlMode.Autonomous);
            float autonomous = ReportingSystem.MishandleChanceFor(state, ReportingDesk.Economy);

            SetDesk(ReportingDesk.Economy, 45f, ControlMode.Directed);
            float directed = ReportingSystem.MishandleChanceFor(state, ReportingDesk.Economy);

            Assert.Less(directed, autonomous,
                "Stating a priority focuses a desk on what the operator asked about.");
        }

        [Test]
        public void ADistrustedDeskReportsWorse()
        {
            SetDesk(ReportingDesk.Government, 50f, ControlMode.Autonomous);
            float trusting = ReportingSystem.MishandleChanceFor(state, ReportingDesk.Government);

            Desk(ReportingDesk.Government).trust = 5f;
            float resentful = ReportingSystem.MishandleChanceFor(state, ReportingDesk.Government);

            Assert.Greater(resentful, trusting,
                "Trust is the willingness to bring the operator bad news.");
        }

        // ---------- honesty of the record ----------

        [Test]
        public void TheChronicleKnowsWhatTheBriefingDidNotSay()
        {
            SetDesk(ReportingDesk.Economy, 0f, ControlMode.Autonomous);
            state.AddChronicle(ChronicleCategory.Economic, state.playerCountryId, "It happened.");
            int chronicleBefore = state.chronicle.Count;

            int start = Fill(NotificationClass.Wire, ReportingDesk.Economy, 100);
            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(chronicleBefore, state.chronicle.Count,
                "The historical record must stay honest even when the briefing is not — " +
                "finding out later is the whole point.");
        }

        // ---------- predictability and determinism ----------

        [Test]
        public void ReportingQualityIsExposedForTheView()
        {
            SetDesk(ReportingDesk.Military, 100f, ControlMode.Autonomous);
            Assert.AreEqual(100f, ReportingSystem.ReportingQualityFor(state, ReportingDesk.Military), 0.5f);

            SetDesk(ReportingDesk.Military, 0f, ControlMode.Autonomous);
            Assert.Less(ReportingSystem.ReportingQualityFor(state, ReportingDesk.Military), 40f,
                "An invisible information penalty is indistinguishable from a bug.");
        }

        [Test]
        public void FilteringIsDeterministic()
        {
            int Run()
            {
                var world = WorldFactory.CreateDebugWorld(seed: 5150);
                foreach (var official in world.cabinet)
                {
                    official.competence = 30f;
                    official.mode = ControlMode.Autonomous;
                }
                world.notifications.Clear();
                int start = 0;
                for (int i = 0; i < 150; i++)
                    world.AddNotification(NotificationClass.Wire, $"ITEM {i}", "body",
                        world.playerCountryId, ReportingDesk.Military);
                ReportingSystem.FilterMonth(world, start);
                return world.notifications.Count - start;
            }

            Assert.AreEqual(Run(), Run(),
                "A reloaded save must be told exactly the same things.");
        }

        [Test]
        public void TheDebugSwitchRestoresEverything()
        {
            SetDesk(ReportingDesk.Military, 0f, ControlMode.Autonomous);
            ReportingSystem.Disabled = true;
            int start = Fill(NotificationClass.Wire, ReportingDesk.Military, 100);

            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(100, state.notifications.Count - start,
                "Every significant system exposes a debug control (GDD §34.1).");
        }

        [Test]
        public void AWeakMinisterDoesNotBuryTheOtherMinistersNews()
        {
            // The first pass filed every official's performance news under the
            // Government desk, so one weak minister suppressed reporting on all
            // five — the opposite of diagnosable. An official's news travels
            // through their own desk.
            SetDesk(ReportingDesk.Government, 0f, ControlMode.Autonomous);
            SetDesk(ReportingDesk.Military, 100f, ControlMode.Autonomous);

            int start = Fill(NotificationClass.Wire, ReportingDesk.Military, 100);
            ReportingSystem.FilterMonth(state, start);

            Assert.AreEqual(100, state.notifications.Count - start,
                "A failing Government desk must not silence the defence ministry.");
        }

        // ---------- it survives a real turn ----------

        [Test]
        public void APlayableGameStillArrivesWithTrafficEveryMonth()
        {
            foreach (var official in state.cabinet)
            {
                official.competence = 5f;
                official.mode = ControlMode.Autonomous;
            }

            var simulation = new TurnManager(state);
            simulation.ResolveMonth += s =>
            {
                CabinetSystem.MonthlyAct(s);
                EconomySystem.MonthlyUpdate(s);
                MilitarySystem.MonthlyUpkeep(s);
                GovernmentSystem.MonthlyUpdate(s);
            };

            for (int i = 0; i < 24; i++) simulation.EndMonth();

            Assert.IsNotEmpty(state.notifications,
                "Even the worst cabinet in the world must leave the operator a playable game.");
        }
    }
}

using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CrisisSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 555);
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Trigger_AddsCrisisFlashTrafficAndChronicle()
        {
            int chronicleBefore = state.chronicle.Count;
            var crisis = CrisisSystem.Trigger(state, "BORDER_INCIDENT");

            Assert.IsTrue(state.HasOpenCrisis);
            Assert.Contains(crisis, state.activeCrises);
            Assert.Greater(crisis.options.Count, 1);

            var last = state.notifications[state.notifications.Count - 1];
            Assert.AreEqual(NotificationClass.Flash, last.priority);
            Assert.AreEqual(state.chronicle.Count, chronicleBefore + 1);
        }

        [Test]
        public void EndMonth_IsNeverBlockedByACrisis()
        {
            CrisisSystem.Trigger(state, "MARKET_PANIC");
            var dateBefore = state.date;

            Assert.IsTrue(turns.EndMonth(),
                "The turn is never refused — the operator may fail to decide and live with it.");
            Assert.AreNotEqual(dateBefore, state.date);
            Assert.IsFalse(state.HasOpenCrisis, "An unanswered crisis lapses rather than persisting.");
        }

        [Test]
        public void Resolve_AppliesEffectsUnblocksAndRecords()
        {
            var player = state.PlayerCountry;
            float treasuryBefore = player.resources.treasury;
            var crisis = CrisisSystem.Trigger(state, "MARKET_PANIC");

            CrisisSystem.Resolve(state, crisis, 0); // emergency liquidity: -120 treasury

            Assert.IsFalse(state.HasOpenCrisis);
            Assert.AreEqual(treasuryBefore - 120f, player.resources.treasury, 0.001f);

            var last = state.notifications[state.notifications.Count - 1];
            Assert.AreEqual(NotificationClass.Priority, last.priority);
            StringAssert.Contains("RESOLVED", last.title);

            Assert.IsTrue(turns.EndMonth(), "End Month should proceed after resolution.");
        }

        [Test]
        public void Resolve_ClampsSocialValues()
        {
            var player = state.PlayerCountry;
            player.stability = 1f;
            var crisis = CrisisSystem.Trigger(state, "BORDER_INCIDENT");

            CrisisSystem.Resolve(state, crisis, 2); // suppress: stability -3

            Assert.AreEqual(0f, player.stability);
        }

        [Test]
        public void Resolve_InvalidInputsThrow()
        {
            var crisis = CrisisSystem.Trigger(state, "FOOD_SHORTAGE");
            Assert.Throws<System.ArgumentOutOfRangeException>(() => CrisisSystem.Resolve(state, crisis, 99));

            CrisisSystem.Resolve(state, crisis, 0);
            Assert.Throws<System.ArgumentException>(() => CrisisSystem.Resolve(state, crisis, 0));
        }

        [Test]
        public void Create_UnknownDefinitionThrows()
        {
            Assert.Throws<System.ArgumentException>(() => CrisisSystem.Create(state, "NO_SUCH_CRISIS"));
        }

        [Test]
        public void SystemicCheck_IsDeterministicPerSeed()
        {
            var monthsA = RunSimulation(seed: 900, months: 200);
            var monthsB = RunSimulation(seed: 900, months: 200);
            var monthsC = RunSimulation(seed: 901, months: 200);

            Assert.AreEqual(monthsA, monthsB, "Same seed must produce identical crisis months.");
            Assert.Greater(monthsA.Count, 0, "200 months at 8% should fire at least one crisis.");
            Assert.AreNotEqual(monthsA, monthsC, "Different seeds should diverge.");
        }

        static System.Collections.Generic.List<string> RunSimulation(int seed, int months)
        {
            var simState = WorldFactory.CreateDebugWorld(seed);
            var simTurns = new TurnManager(simState);
            simTurns.ResolveMonth += CrisisSystem.SystemicCheck;

            var crisisMonths = new System.Collections.Generic.List<string>();
            for (int i = 0; i < months; i++)
            {
                simTurns.EndMonth();

                // A crisis is detected by looking, not by the turn being
                // refused. This loop used to key off `EndMonth()` returning
                // false, which stopped detecting anything the moment the turn
                // became unblockable — and reported zero crises in 200 months
                // rather than failing honestly.
                while (simState.activeCrises.Count > 0)
                {
                    var crisis = simState.activeCrises[0];
                    crisisMonths.Add($"{simState.date.SortKey}:{crisis.defId}");
                    CrisisSystem.Resolve(simState, crisis, 0);
                }
            }
            return crisisMonths;
        }

        [Test]
        public void ActiveCrisis_SurvivesSaveRoundTrip()
        {
            CrisisSystem.Trigger(state, "BORDER_INCIDENT");
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.IsTrue(loaded.HasOpenCrisis);
            Assert.AreEqual("BORDER_INCIDENT", loaded.activeCrises[0].defId);
            Assert.AreEqual(3, loaded.activeCrises[0].options.Count);
            Assert.AreEqual(state.notifications.Count, loaded.notifications.Count);

            // The crisis survives the save as an open decision — and, like any
            // other, the operator can still choose to end the month past it.
            var loadedTurns = new TurnManager(loaded);
            Assert.IsTrue(loadedTurns.EndMonth());
            Assert.IsFalse(loaded.HasOpenCrisis,
                "A resumed save must handle an unanswered crisis the same way a live one does.");
        }
    }

    public class NotificationTests
    {
        [Test]
        public void PriorityOrder_FlashIsHighest()
        {
            Assert.Less((int)NotificationClass.Flash, (int)NotificationClass.Priority);
            Assert.Less((int)NotificationClass.Priority, (int)NotificationClass.Advisory);
            Assert.Less((int)NotificationClass.Advisory, (int)NotificationClass.Wire);
            Assert.Less((int)NotificationClass.Wire, (int)NotificationClass.Archive);
        }

        [Test]
        public void AddNotification_TrimsToCap()
        {
            var state = new GameState();
            for (int i = 0; i < GameState.MaxNotifications + 50; i++)
                state.AddNotification(NotificationClass.Wire, $"ITEM {i}", null);

            Assert.AreEqual(GameState.MaxNotifications, state.notifications.Count);
            Assert.AreEqual("ITEM 50", state.notifications[0].title, "Oldest items should be trimmed.");
        }

        [Test]
        public void MonthStart_EmitsAdvisoryTraffic()
        {
            GameLog.MirrorToUnityConsole = false;
            var state = WorldFactory.CreateDebugWorld(1);
            new TurnManager(state).EndMonth();
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();

            var last = state.notifications[state.notifications.Count - 1];
            Assert.AreEqual(NotificationClass.Advisory, last.priority);
            Assert.AreEqual("MONTH START", last.title);
            Assert.AreEqual(state.date, last.date);
        }
    }
}

using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class ActionFinderTests
    {
        [Test]
        public void EconomicOutcomePrefersEconomicCommands()
        {
            var state = WorldFactory.CreateDebugWorld(821);
            var items = ActionFinderSystem.ForMetric(state, CausalMetric.Treasury, 3);
            Assert.Greater(items.Count, 0);
            Assert.AreEqual(Pillar.Economy, FindPillar(state, items[0].label, items[0].viewId));
        }

        [Test]
        public void FinderDoesNotHideBlockedCommands()
        {
            var state = WorldFactory.CreateDebugWorld(822);
            state.commandPoints.current = 0;
            var items = ActionFinderSystem.ForMetric(state, CausalMetric.WarExhaustion, 12);
            Assert.Greater(items.Count, 0);
            bool sawBlocked = false;
            foreach (var item in items) if (!item.available) { sawBlocked = true; break; }
            Assert.IsTrue(sawBlocked, "Blocked but relevant commands should remain discoverable.");
        }

        [Test]
        public void FinderIsReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(823);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            ActionFinderSystem.Render(state, CausalMetric.GovernmentApproval);
            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(sequence, state.actionSequence);
        }

        static Pillar FindPillar(GameState state, string label, string viewId)
        {
            foreach (var entry in ActionCatalog.All(state))
                if (entry.label == label && entry.viewId == viewId) return entry.pillar;
            foreach (var entry in StrategyActionCatalog.All(state))
                if (entry.label == label && entry.viewId == viewId) return entry.pillar;
            Assert.Fail("Recommendation did not originate from an action catalog.");
            return Pillar.Government;
        }
    }
}

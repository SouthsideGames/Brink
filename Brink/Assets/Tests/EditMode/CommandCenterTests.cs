using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CommandCenterTests
    {
        [Test]
        public void CommandCenterIsReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(12001);
            string before = SaveSystem.ToJson(state);
            string rendered = CommandCenterSystem.Render(state);
            Assert.IsNotEmpty(rendered);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void CommandCenterPointsToRealOperatorDestinations()
        {
            var state = WorldFactory.CreateDebugWorld(12002);
            foreach (var section in CommandCenterSystem.Build(state))
                CollectionAssert.Contains(new[] { "BRIEFING", "CABINET", "INTELLIGENCE", "STRATEGIST" }, section.viewId);
        }

        [Test]
        public void CommandCenterOrdersHigherUrgencyFirst()
        {
            var state = WorldFactory.CreateDebugWorld(12003);
            var sections = CommandCenterSystem.Build(state);
            for (int i = 1; i < sections.Count; i++)
                Assert.GreaterOrEqual(sections[i - 1].urgency, sections[i].urgency,
                    "Command Center must remain an attention-ranked read model.");
        }
    }
}
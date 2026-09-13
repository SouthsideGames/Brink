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
        public void AcuteDomesticPressureSurfacesStrategicPressure()
        {
            var state = WorldFactory.CreateDebugWorld(12003);
            var player = state.PlayerCountry;
            player.governmentApproval = 15f;
            player.socialUnrest = 90f;

            // Resolve a month so the causal board has an authoritative interval.
            Causal.OpenMonth(state);
            Causal.Apply(state, CausalMetric.Approval, -10f, CausalReason.DomesticPressure);
            Causal.CloseMonth(state);

            var sections = CommandCenterSystem.Build(state);
            Assert.IsTrue(sections.Exists(s => s.title == "STRATEGIC PRESSURE"));
        }
    }
}

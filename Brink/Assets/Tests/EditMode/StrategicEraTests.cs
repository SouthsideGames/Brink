using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class StrategicEraTests
    {
        [Test]
        public void DoctrineProducesNamedEra()
        {
            var state = WorldFactory.CreateDebugWorld(981);
            var strategy = StrategySystem.Ensure(state);
            strategy.doctrineChosen = true;
            strategy.doctrine = StrategicDoctrine.Deterrence;
            strategy.doctrineAdopted = state.date;

            var era = StrategicEraSystem.Current(state);
            Assert.IsNotNull(era);
            StringAssert.Contains("Shield Era", era.name);
            Assert.AreEqual("Deterrence", era.doctrine);
        }

        [Test]
        public void NationalPressureChangesEraLabelNotDoctrine()
        {
            var state = WorldFactory.CreateDebugWorld(982);
            var strategy = StrategySystem.Ensure(state);
            strategy.doctrineChosen = true;
            strategy.doctrine = StrategicDoctrine.Resilience;
            strategy.doctrineAdopted = state.date;
            state.PlayerCountry.socialUnrest = 80f;
            int sequence = state.actionSequence;

            var era = StrategicEraSystem.Current(state);
            StringAssert.StartsWith("Crisis ", era.name);
            Assert.AreEqual(StrategicDoctrine.Resilience, strategy.doctrine);
            Assert.AreEqual(sequence, state.actionSequence);
        }
    }
}

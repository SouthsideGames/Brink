using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class StrategicReversalTests
    {
        [Test]
        public void RevisionsBecomeHistoricalReading()
        {
            var state = WorldFactory.CreateDebugWorld(994);
            var strategy = StrategySystem.Ensure(state);
            strategy.doctrineChosen = true;
            strategy.doctrine = StrategicDoctrine.Prosperity;
            strategy.doctrineAdopted = state.date;
            strategy.revisionCount = 2;
            var reading = StrategicReversalSystem.Read(state);
            Assert.IsNotNull(reading);
            Assert.AreEqual(2, reading.revisionCount);
            Assert.AreEqual("SECOND TURN", reading.headline);
        }

        [Test]
        public void NoRevisionMeansContinuity()
        {
            var state = WorldFactory.CreateDebugWorld(995);
            var strategy = StrategySystem.Ensure(state);
            strategy.doctrineChosen = true;
            strategy.revisionCount = 0;
            Assert.IsNull(StrategicReversalSystem.Read(state));
            StringAssert.Contains("NO DOCTRINAL REVERSAL", StrategicReversalSystem.Render(state));
        }
    }
}

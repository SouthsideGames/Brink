using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class StrategicSurpriseTests
    {
        [Test]
        public void MissingCollectionProducesBlindSpotWithoutForeignTruth()
        {
            var state = WorldFactory.CreateDebugWorld(951);
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId);

            var exposures = StrategicSurpriseSystem.Assess(state);
            var intel = exposures.Find(e => e.pillar == Pillar.Intelligence);
            Assert.IsNotNull(intel);
            StringAssert.Contains("no standing foreign collection", intel.known.ToLowerInvariant());
            StringAssert.Contains("missing collection", intel.unknown.ToLowerInvariant());
        }

        [Test]
        public void SurpriseAssessmentIsReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(952);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            int month = state.date.month;

            StrategicSurpriseSystem.Render(state);

            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(sequence, state.actionSequence);
            Assert.AreEqual(month, state.date.month);
        }

        [Test]
        public void AcuteDomesticPressureIsMaterialExposure()
        {
            var state = WorldFactory.CreateDebugWorld(953);
            state.PlayerCountry.governmentApproval = 20f;
            state.PlayerCountry.socialUnrest = 82f;

            var exposure = StrategicSurpriseSystem.Assess(state).Find(e => e.pillar == Pillar.Government);
            Assert.IsNotNull(exposure);
            Assert.AreEqual(4, exposure.severity);
        }
    }
}

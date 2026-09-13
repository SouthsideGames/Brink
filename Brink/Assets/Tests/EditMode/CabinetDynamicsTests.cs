using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CabinetDynamicsTests
    {
        [Test]
        public void SimilarOfficialsFormLegibleAlignment()
        {
            var state = WorldFactory.CreateDebugWorld(941);
            var military = state.PlayerCountry.FindOfficial(Pillar.Military);
            var intelligence = state.PlayerCountry.FindOfficial(Pillar.Intelligence);
            military.riskTolerance = 70f;
            intelligence.riskTolerance = 72f;
            military.trust = intelligence.trust = 70f;
            military.mode = intelligence.mode = ControlMode.Autonomous;

            var alignments = CabinetDynamicsSystem.Alignments(state);
            Assert.IsTrue(alignments.Exists(a =>
                (a.first == Pillar.Military && a.second == Pillar.Intelligence) ||
                (a.first == Pillar.Intelligence && a.second == Pillar.Military)));
        }

        [Test]
        public void InterventionReadingReflectsStrainedIndependentOfficial()
        {
            var state = WorldFactory.CreateDebugWorld(942);
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);
            official.trust = 20f;
            official.loyalty = 25f;
            official.competence = 90f;
            official.mode = ControlMode.DirectControl;

            var reading = CabinetDynamicsSystem.ReadIntervention(state, Pillar.Economy);
            Assert.IsNotNull(reading);
            Assert.GreaterOrEqual(reading.resistance, 4);
            StringAssert.Contains("bypass", reading.reaction);
        }

        [Test]
        public void DynamicsAreReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(943);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            float trust = state.PlayerCountry.FindOfficial(Pillar.Government).trust;

            CabinetDynamicsSystem.Render(state);
            CabinetDynamicsSystem.ReadIntervention(state, Pillar.Government);

            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(sequence, state.actionSequence);
            Assert.AreEqual(trust, state.PlayerCountry.FindOfficial(Pillar.Government).trust);
        }
    }
}

using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CabinetMeetingTests
    {
        [Test]
        public void MeetingIsReadOnlyAndContainsEverySeatedOffice()
        {
            var state = WorldFactory.CreateDebugWorld(831);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            int cabinetCount = state.PlayerCountry.cabinet.Count;

            var positions = CabinetMeetingSystem.Build(state);

            Assert.AreEqual(cabinetCount, positions.Count);
            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(sequence, state.actionSequence);
        }

        [Test]
        public void AcuteDomesticPressureMovesGovernmentVoiceUp()
        {
            var state = WorldFactory.CreateDebugWorld(832);
            state.PlayerCountry.governmentApproval = 20f;
            state.PlayerCountry.socialUnrest = 85f;

            var positions = CabinetMeetingSystem.Build(state);
            var government = positions.Find(p => p.pillar == Pillar.Government);

            Assert.IsNotNull(government);
            Assert.GreaterOrEqual(government.pressure, 3);
            Assert.IsTrue(government.concern.Contains("unrest"));
        }

        [Test]
        public void DirectedOfficialAcknowledgesOperatorInstruction()
        {
            var state = WorldFactory.CreateDebugWorld(833);
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);
            official.mode = ControlMode.Directed;
            official.directiveId = "ECO_AUSTERITY";

            var positions = CabinetMeetingSystem.Build(state);
            var economy = positions.Find(p => p.pillar == Pillar.Economy);

            Assert.IsTrue(economy.position.Contains("ECO_AUSTERITY"));
        }
    }
}

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
        public void DirectedOfficialAcknowledgesOperatorInstructionByHumanLabel()
        {
            var state = WorldFactory.CreateDebugWorld(833);
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);
            official.mode = ControlMode.Directed;
            official.directiveId = "ECO_AUSTERITY";

            var positions = CabinetMeetingSystem.Build(state);
            var economy = positions.Find(p => p.pillar == Pillar.Economy);

            Assert.IsTrue(economy.position.Contains("AUSTERITY"));
            Assert.IsFalse(economy.position.Contains("ECO_AUSTERITY"));
        }

        [Test]
        public void PersonalityIsStableAndReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(834);
            var official = state.PlayerCountry.FindOfficial(Pillar.Military);
            string before = SaveSystem.ToJson(state);

            var first = InstitutionalPersonalitySystem.ProfileFor(state, official);
            var second = InstitutionalPersonalitySystem.ProfileFor(state, official);

            Assert.IsNotNull(first);
            Assert.AreEqual(first.identity, second.identity);
            Assert.AreEqual(first.temperament, second.temperament);
            Assert.AreEqual(first.relationship, second.relationship);
            Assert.AreEqual(first.resistance, second.resistance);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void LowTrustAndRiskGapCreateVisibleCabinetFaultLine()
        {
            var state = WorldFactory.CreateDebugWorld(835);
            var military = state.PlayerCountry.FindOfficial(Pillar.Military);
            var economy = state.PlayerCountry.FindOfficial(Pillar.Economy);
            military.riskTolerance = 90f;
            military.trust = 25f;
            economy.riskTolerance = 20f;
            economy.trust = 70f;

            var frictions = InstitutionalPersonalitySystem.Frictions(state);
            var fault = frictions.Find(f =>
                (f.first == Pillar.Military && f.second == Pillar.Economy) ||
                (f.first == Pillar.Economy && f.second == Pillar.Military));

            Assert.IsNotNull(fault);
            Assert.GreaterOrEqual(fault.intensity, 3);
            Assert.IsTrue(CabinetMeetingSystem.Render(state).Contains("CABINET FAULT LINES"));
        }

        [Test]
        public void ForeignOfficialCannotBeProfiledThroughPlayerLayer()
        {
            var state = WorldFactory.CreateDebugWorld(836);
            Official foreign = null;
            foreach (var country in state.countries)
            {
                if (country.id == state.playerCountryId || country.cabinet.Count == 0) continue;
                foreign = country.cabinet[0];
                break;
            }

            Assert.IsNotNull(foreign);
            Assert.IsNull(InstitutionalPersonalitySystem.ProfileFor(state, foreign));
        }
    }
}

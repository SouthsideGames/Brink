using Brink.Core;
using Brink.Data;
using Brink.UI.Views;
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
            Assert.AreEqual(first.continuity, second.continuity);
            Assert.AreEqual(first.resistance, second.resistance);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void InstitutionalContinuityChangesAtLegibleTenureBoundaries()
        {
            var official = new Official();

            official.monthsInOffice = 0;
            StringAssert.Contains("newly appointed", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 12;
            StringAssert.Contains("settled in office", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 48;
            StringAssert.Contains("established command", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 96;
            StringAssert.Contains("entrenched command", InstitutionalPersonalitySystem.ContinuityFor(official));
        }

        [Test]
        public void LongTenureHardensOnlyAnAlreadyStrainedOffice()
        {
            var state = WorldFactory.CreateDebugWorld(837);
            var official = state.PlayerCountry.FindOfficial(Pillar.Economy);
            official.competence = 60f;
            official.loyalty = 60f;
            official.mode = ControlMode.Autonomous;
            official.trust = 45f;
            official.monthsInOffice = 95;
            int before = InstitutionalPersonalitySystem.ProfileFor(state, official).resistance;

            official.monthsInOffice = 96;
            int entrenched = InstitutionalPersonalitySystem.ProfileFor(state, official).resistance;
            Assert.AreEqual(before + 1, entrenched,
                "A long-serving strained office has no more institutional ground than a new one.");

            official.trust = 70f;
            Assert.AreEqual(0,
                InstitutionalPersonalitySystem.ProfileFor(state, official).resistance,
                "Tenure alone became hostility; continuity should deepen an existing dispute, not invent one.");
        }

        [Test]
        public void CabinetMeetingMakesContinuityVisibleWithoutMutatingTheSave()
        {
            var state = WorldFactory.CreateDebugWorld(838);
            var official = state.PlayerCountry.FindOfficial(Pillar.Government);
            official.monthsInOffice = 120;
            string before = SaveSystem.ToJson(state);

            var positions = CabinetMeetingSystem.Build(state);
            var government = positions.Find(p => p.pillar == Pillar.Government);
            string rendered = CabinetMeetingSystem.Render(state);

            StringAssert.Contains("entrenched command", government.continuity);
            StringAssert.Contains("CONTINUITY:", rendered);
            StringAssert.Contains("entrenched command", rendered);
            StringAssert.Contains("CONTINUITY:", CabinetView.ProfileText(
                InstitutionalPersonalitySystem.ProfileFor(state, official)));
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

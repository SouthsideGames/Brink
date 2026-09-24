using Brink.Core;
using Brink.Data;
using Brink.UI.Views;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CabinetMeetingTests
    {
        [Test]
        public void MissingMinistryHistoryIsNeutralAndReadsDoNotSeedIt()
        {
            var state = WorldFactory.CreateDebugWorld(840);
            var country = state.PlayerCountry;
            var official = country.FindOfficial(Pillar.Government);
            country.institutionalMemory = null;
            string before = SaveSystem.ToJson(state);
            Assert.AreEqual(official.riskTolerance,
                InstitutionalPersonalitySystem.EffectiveRisk(state, country, official));
            StringAssert.Contains("no recorded outcome history",
                InstitutionalPersonalitySystem.ProfileFor(state, official).continuity);
            CabinetMeetingSystem.Build(state);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [TestCase(true, 12f)]
        [TestCase(false, -12f)]
        public void MinistryExperienceIsBoundedAndFadesWithoutAWritingTick(bool success, float cap)
        {
            var state = WorldFactory.CreateDebugWorld(841);
            var country = state.PlayerCountry;
            var official = country.FindOfficial(Pillar.Government);
            official.riskTolerance = 50f;
            for (int i = 0; i < 20; i++)
                Assert.IsTrue(InstitutionalPersonalitySystem.RecordOutcome(state, country, official, success));
            Assert.AreEqual(cap, InstitutionalPersonalitySystem.RiskImprint(state, country, official.office));
            var memory = country.institutionalMemory[0];
            Assert.AreEqual(20, memory.successes + memory.setbacks);
            official.mode = ControlMode.DirectControl;
            for (int i = 0; i < 120; i++) state.date = state.date.NextMonth();
            string before = SaveSystem.ToJson(state);
            Assert.AreEqual(cap * (float)System.Math.Pow(0.99f, 120),
                InstitutionalPersonalitySystem.RiskImprint(state, country, official.office), 0.00001f);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            Assert.AreEqual(cap, memory.riskImprint, "Decay must be dated, not a render-time write.");
            Assert.IsTrue(InstitutionalPersonalitySystem.RecordOutcome(state, country, official, !success));
            Assert.AreEqual(cap * (float)System.Math.Pow(0.99f, 120) + (success ? -3f : 2f),
                memory.riskImprint, 0.00001f, "A new outcome must start from the decayed value.");
        }

        [Test]
        public void MinistryHistorySurvivesActualAppointmentButNotAsMinisterTraits()
        {
            var state = WorldFactory.CreateDebugWorld(842);
            var country = state.PlayerCountry;
            var old = country.FindOfficial(Pillar.Government);
            Assert.IsTrue(InstitutionalPersonalitySystem.RecordOutcome(state, country, old, false));
            country.cabinet.Remove(old);
            var vacancy = new CabinetVacancy { office = Pillar.Government };
            vacancy.candidates.Add(new OfficialCandidate { displayName = "Successor", competence = 60f,
                loyalty = 60f, riskTolerance = 50f, age = 50f });
            country.vacancies.Add(vacancy);
            Assert.IsTrue(CabinetLifecycle.Appoint(state, Pillar.Government, 0));
            var successor = country.FindOfficial(Pillar.Government);
            Assert.AreEqual(0, successor.monthsInOffice);
            Assert.AreEqual(60f, successor.competence);
            Assert.AreEqual(50f, successor.riskTolerance);
            Assert.AreEqual(47f, InstitutionalPersonalitySystem.EffectiveRisk(state, country, successor));
            StringAssert.Contains("cautioned by experience",
                InstitutionalPersonalitySystem.ProfileFor(state, successor).continuity);
            StringAssert.Contains("newly appointed", CabinetMeetingSystem.Render(state));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(InstitutionalPersonalitySystem.RecordOutcome(state, country, old, true));
            Assert.AreEqual(before, SaveSystem.ToJson(state), "Departed officials cannot rewrite their old office.");
            Assert.AreEqual(0f, InstitutionalPersonalitySystem.RiskImprint(state, country, Pillar.Economy));
        }

        [Test]
        public void MinistryHistoryRoundTripsAndAbsentLegacyFieldInventsNoExperience()
        {
            var state = WorldFactory.CreateDebugWorld(843);
            var official = state.PlayerCountry.FindOfficial(Pillar.Government);
            InstitutionalPersonalitySystem.RecordOutcome(state, state.PlayerCountry, official, true);
            string json = SaveSystem.ToJson(state);
            var loaded = SaveSystem.FromJson(json);
            Assert.AreEqual(2f, InstitutionalPersonalitySystem.RiskImprint(loaded, loaded.PlayerCountry, official.office));
            Assert.AreEqual(1, loaded.PlayerCountry.institutionalMemory[0].successes);
            var legacy = SaveSystem.FromJson(json.Replace("\"institutionalMemory\"", "\"omittedInstitutionalMemory\""));
            Assert.AreEqual(0f, InstitutionalPersonalitySystem.RiskImprint(legacy, legacy.PlayerCountry, official.office));
            legacy.PlayerCountry.institutionalMemory = new System.Collections.Generic.List<InstitutionalMemory>
                { new InstitutionalMemory { office = official.office } };
            Assert.AreEqual(0f, InstitutionalPersonalitySystem.RiskImprint(legacy, legacy.PlayerCountry, official.office));
        }

        [Test]
        public void ForeignMinistriesAccumulateTheirOwnHistoryWithoutProfileAccess()
        {
            var state = WorldFactory.CreateDebugWorld(844);
            var foreign = state.countries.Find(c => c.id != state.playerCountryId && c.cabinet.Count > 0);
            var official = foreign.cabinet[0];
            Assert.IsTrue(InstitutionalPersonalitySystem.RecordOutcome(state, foreign, official, false));
            Assert.AreEqual(-3f, InstitutionalPersonalitySystem.RiskImprint(state, foreign, official.office));
            Assert.AreEqual(0f, InstitutionalPersonalitySystem.RiskImprint(state, state.PlayerCountry, official.office));
            Assert.IsNull(InstitutionalPersonalitySystem.ProfileFor(state, official));
        }

        // Find a real deterministic Cabinet draw that isolates each consumer. The
        // fixture proves its premise before testing the production monthly path.
        [TestCase(false)]
        [TestCase(true)]
        public void MonthlyCabinetConsumesMinistryVarianceAndInitiativeFrequency(bool marginalEvent)
        {
            var state = WorldFactory.CreateDebugWorld(845);
            var country = state.PlayerCountry;
            var official = country.FindOfficial(Pillar.Government);
            foreach (var c in state.countries)
                foreach (var o in c.cabinet) o.mode = ControlMode.DirectControl;
            official.mode = ControlMode.Directed;
            official.directiveId = "GOV_APPROVAL";
            official.competence = 50f;
            official.riskTolerance = 50f;
            country.governmentApproval = 40f;
            for (int i = 0; i < 6; i++)
                InstitutionalPersonalitySystem.RecordOutcome(state, country, official, true);
            double varianceDraw = 0, eventDraw = 0, successDraw = 0;
            bool found = false;
            for (int seed = 1; seed <= 10000; seed++)
            {
                var rng = new System.Random(unchecked(seed * 92821 + (int)official.office + Hash.Of(country.id) * 7));
                varianceDraw = rng.NextDouble(); eventDraw = rng.NextDouble(); successDraw = rng.NextDouble();
                if (marginalEvent ? eventDraw >= 0.07 && eventDraw < 0.0772
                    : eventDraw > 0.1 && System.Math.Abs(varianceDraw - 0.5) > 0.3)
                { state.rngSeed = seed; found = true; break; }
            }
            Assert.IsTrue(found, "No separating draw found; fixture is invalid.");
            double performance = 0.5 + (varianceDraw * 2 - 1) * 0.62 * 0.3;
            float amount = (float)(0.05 + performance * 0.25)
                * GovernmentSystem.PriorityMultiplierFor(country.government.leader.priority, official.office);
            bool success = successDraw < performance * 0.7 + 0.15;
            float expected = 40f + amount;
            if (marginalEvent) expected += amount * (success ? 3f : -2f);
            CabinetSystem.MonthlyAct(state);
            Assert.AreEqual(expected, country.governmentApproval, 0.00001f);
            var memory = country.institutionalMemory[0];
            Assert.AreEqual(marginalEvent ? 7 : 6, memory.successes + memory.setbacks);
            Assert.AreEqual(marginalEvent ? (success ? 12f : 9f) : 12f, memory.riskImprint);
            Assert.AreEqual(50f, official.riskTolerance);
            Assert.AreEqual(50f, official.competence);
        }

        [Test]
        public void DirectControlNeitherUsesNorAddsMinistryExperience()
        {
            var state = WorldFactory.CreateDebugWorld(846);
            foreach (var country in state.countries)
                foreach (var official in country.cabinet) official.mode = ControlMode.DirectControl;
            var own = state.PlayerCountry;
            var minister = own.FindOfficial(Pillar.Government);
            InstitutionalPersonalitySystem.RecordOutcome(state, own, minister, false);
            float approval = own.governmentApproval;
            CabinetSystem.MonthlyAct(state);
            Assert.AreEqual(approval, own.governmentApproval);
            Assert.AreEqual(1, own.institutionalMemory[0].setbacks);
            Assert.AreEqual(-3f, own.institutionalMemory[0].riskImprint);
        }

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
            official.monthsInOffice = 11;
            StringAssert.Contains("newly appointed", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 12;
            StringAssert.Contains("settled in office", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 47;
            StringAssert.Contains("settled in office", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 48;
            StringAssert.Contains("established command", InstitutionalPersonalitySystem.ContinuityFor(official));
            official.monthsInOffice = 95;
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

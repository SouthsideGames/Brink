using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CabinetChoiceReadingTests
    {
        [Test]
        public void SpendingCreatesInstitutionalDisagreement()
        {
            var state = WorldFactory.CreateDebugWorld(961);
            var economy = state.PlayerCountry.FindOfficial(Pillar.Economy);
            var government = state.PlayerCountry.FindOfficial(Pillar.Government);
            economy.riskTolerance = 20f;
            government.riskTolerance = 75f;

            var voices = CabinetChoiceReadingSystem.Read(state, CabinetChoiceReadingSystem.ChoiceKind.ExpandSpending);
            var eco = voices.Find(v => v.pillar == Pillar.Economy);
            var gov = voices.Find(v => v.pillar == Pillar.Government);
            Assert.Less(eco.stance, 0);
            Assert.Greater(gov.stance, 0);
        }

        [Test]
        public void ChoiceReadingNeverVetoesOrMutates()
        {
            var state = WorldFactory.CreateDebugWorld(962);
            int cp = state.commandPoints.current;
            int influence = state.influence;
            int sequence = state.actionSequence;
            float trust = state.PlayerCountry.FindOfficial(Pillar.Military).trust;

            var text = CabinetChoiceReadingSystem.Render(state, CabinetChoiceReadingSystem.ChoiceKind.EscalateWar);

            StringAssert.Contains("NOT A VOTE AND NOT A VETO", text);
            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(influence, state.influence);
            Assert.AreEqual(sequence, state.actionSequence);
            Assert.AreEqual(trust, state.PlayerCountry.FindOfficial(Pillar.Military).trust);
        }

        [Test]
        public void ReadingUsesOnlyPlayerCabinet()
        {
            var state = WorldFactory.CreateDebugWorld(963);
            int expected = state.PlayerCountry.cabinet.Count;
            Assert.AreEqual(expected,
                CabinetChoiceReadingSystem.Read(state, CabinetChoiceReadingSystem.ChoiceKind.DiplomaticCompromise).Count);
        }
    }
}

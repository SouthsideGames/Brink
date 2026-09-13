using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class PrecedentTests
    {
        [Test]
        public void PlayerMilitaryHistoryBecomesPrecedent()
        {
            var state = WorldFactory.CreateDebugWorld(991);
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Military, countryId = state.playerCountryId, text = "Operation concluded.", publicity = Publicity.Public });
            var items = PrecedentSystem.Recent(state);
            Assert.IsTrue(items.Exists(p => p.category == ChronicleCategory.Military));
        }

        [Test]
        public void ForeignHistoryDoesNotBecomePlayerPrecedent()
        {
            var state = WorldFactory.CreateDebugWorld(992);
            var other = state.countries.Find(c => c.id != state.playerCountryId);
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Military, countryId = other.id, text = "Foreign operation.", publicity = Publicity.Public });
            Assert.IsFalse(PrecedentSystem.Recent(state).Exists(p => p.text == "Foreign operation."));
        }

        [Test]
        public void ReadingPrecedentDoesNotMutateState()
        {
            var state = WorldFactory.CreateDebugWorld(993);
            int sequence = state.actionSequence;
            int count = state.chronicle.Count;
            PrecedentSystem.Render(state);
            Assert.AreEqual(sequence, state.actionSequence);
            Assert.AreEqual(count, state.chronicle.Count);
        }
    }
}

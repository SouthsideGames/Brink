using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class HistoricalIdentityTests
    {
        [Test]
        public void RepeatedPublicRecordCreatesIdentityWithoutChangingState()
        {
            var state = WorldFactory.CreateDebugWorld(971);
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Diplomatic, countryId = state.playerCountryId, text = "Brokered settlement.", publicity = Publicity.Public });
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Diplomatic, countryId = state.playerCountryId, text = "Built coalition.", publicity = Publicity.Public });
            int sequence = state.actionSequence;

            var identities = HistoricalIdentitySystem.Build(state);

            Assert.IsTrue(identities.Exists(i => i.name == "BROKER STATE"));
            Assert.AreEqual(sequence, state.actionSequence);
        }

        [Test]
        public void IdentityDoesNotCountOtherCountriesRecord()
        {
            var state = WorldFactory.CreateDebugWorld(972);
            var other = state.countries.Find(c => c.id != state.playerCountryId);
            Assert.IsNotNull(other);
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Military, countryId = other.id, text = "Foreign war.", publicity = Publicity.Public });
            state.chronicle.Add(new ChronicleEntry { date = state.date, category = ChronicleCategory.Military, countryId = other.id, text = "Foreign war again.", publicity = Publicity.Public });

            var identities = HistoricalIdentitySystem.Build(state);
            Assert.IsFalse(identities.Exists(i => i.name == "SECURITY STATE" && i.evidence == 2));
        }
    }
}

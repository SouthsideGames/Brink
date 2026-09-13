using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CredibilityMemoryTests
    {
        [Test]
        public void ExistingRelationshipMemoryIsMadeLegibleWithoutNewScore()
        {
            var state = WorldFactory.CreateDebugWorld(996);
            var rel = state.relationships.Find(r => r.Involves(state.playerCountryId));
            Assert.IsNotNull(rel);
            float trust = rel.trust;
            float weight = rel.memoryWeight;
            rel.AddMemory(state.date, "Honored negotiated commitment", 3f);
            var items = CredibilityMemorySystem.Build(state);
            Assert.IsTrue(items.Exists(i => i.partnerId == rel.PartnerOf(state.playerCountryId) && i.memories > 0));
            CredibilityMemorySystem.Render(state);
            Assert.AreEqual(trust, rel.trust, "Reader must not alter the diplomacy trust score.");
            Assert.AreEqual(weight + 3f, rel.memoryWeight, 0.0001f, "Reader must not alter historical memory weight.");
        }

        [Test]
        public void BrokenTreatyRemembersWhichSideBrokeIt()
        {
            var state = WorldFactory.CreateDebugWorld(997);
            var rel = state.relationships.Find(r => r.Involves(state.playerCountryId));
            string partner = rel.PartnerOf(state.playerCountryId);
            state.treaties.Add(new Treaty { id = "TEST_MEMORY", countryA = state.playerCountryId, countryB = partner, broken = true, brokenBy = state.playerCountryId });
            var item = CredibilityMemorySystem.Build(state).Find(i => i.partnerId == partner);
            Assert.IsNotNull(item);
            Assert.AreEqual(1, item.brokenByUs);
            Assert.AreEqual(0, item.brokenByThem);
        }
    }
}

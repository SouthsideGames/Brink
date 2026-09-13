using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class RecoveryHistoryTests
    {
        [Test]
        public void SetbackCanCoexistWithMeasuredRecovery()
        {
            var state = WorldFactory.CreateDebugWorld(998);
            state.PlayerCountry.warsLost = 1;
            state.causal.records.Add(new CausalRecord { metric = CausalMetric.GovernmentApproval, countryId = state.playerCountryId, year = state.date.year, month = state.date.month, previous = 30f, resulting = 35f, delta = 5f });
            state.causal.records.Add(new CausalRecord { metric = CausalMetric.SocialUnrest, countryId = state.playerCountryId, year = state.date.year, month = state.date.month, previous = 70f, resulting = 65f, delta = -5f });
            var reading = RecoveryHistorySystem.Read(state);
            Assert.AreEqual("RECOVERY AFTER SETBACK", reading.status);
            Assert.AreEqual(1, reading.warsLost);
        }

        [Test]
        public void RecoveryReaderIsReadOnly()
        {
            var state = WorldFactory.CreateDebugWorld(999);
            string before = SaveSystem.ToJson(state);
            RecoveryHistorySystem.Render(state);
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }
    }
}

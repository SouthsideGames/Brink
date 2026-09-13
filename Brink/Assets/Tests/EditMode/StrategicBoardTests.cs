using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class StrategicBoardTests
    {
        [Test]
        public void BoardIsReadOnlyAndRanksAcuteDeteriorationFirst()
        {
            var state = WorldFactory.CreateDebugWorld(811);
            state.causal.records.Clear();
            state.causal.Add(Record(state, CausalMetric.GovernmentApproval, 32f, 24f,
                CausalReason.OrganisedUnrest));
            state.causal.Add(Record(state, CausalMetric.MarketIndex, 80f, 82f,
                CausalReason.MarketConfidence));

            int before = state.causal.records.Count;
            var board = StrategicBoardSystem.Build(state);

            Assert.AreEqual(before, state.causal.records.Count);
            Assert.AreEqual(CausalMetric.GovernmentApproval, board[0].metric);
            Assert.AreEqual("DETERIORATING", board[0].direction);
            Assert.AreEqual("SOCIAL UNREST", board[0].driver);
        }

        [Test]
        public void BoardUsesDisclosureGateRatherThanRawClassifiedCause()
        {
            var state = WorldFactory.CreateDebugWorld(812);
            state.causal.records.Clear();
            var record = Record(state, CausalMetric.GovernmentApproval, 50f, 44f,
                CausalReason.CovertAction);
            record.contributions[0].visibility = CausalVisibility.Classified;
            state.causal.Add(record);

            string text = StrategicBoardSystem.Render(state);
            Assert.IsFalse(text.Contains("COVERT ACTION"));
        }

        [Test]
        public void EmptyOldSaveProducesHonestNoAnalysisMessage()
        {
            var state = WorldFactory.CreateDebugWorld(813);
            state.causal.records.Clear();
            Assert.AreEqual("STRATEGIC BOARD — NO RESOLVED-MONTH ANALYSIS YET.",
                StrategicBoardSystem.Render(state));
        }

        static CausalRecord Record(GameState state, CausalMetric metric, float previous, float resulting,
            CausalReason reason)
        {
            float delta = resulting - previous;
            var record = new CausalRecord
            {
                metric = metric,
                countryId = state.playerCountryId,
                year = state.date.year,
                month = state.date.month,
                previous = previous,
                resulting = resulting,
                delta = delta,
                reconciliation = CausalReconciliation.Exact
            };
            record.contributions.Add(new CausalContribution(
                reason, delta, CausalCategory.Political, CausalKind.Direct, CausalVisibility.Known));
            return record;
        }
    }
}
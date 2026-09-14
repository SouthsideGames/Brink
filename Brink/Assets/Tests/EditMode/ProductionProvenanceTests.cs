using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Production-side provenance guards for operator decisions that feed the
    /// monthly debrief. These tests assert the causal record at its source rather
    /// than the presentation layer, so a renderer cannot manufacture authorship
    /// that the simulation never recorded.
    /// </summary>
    public class ProductionProvenanceTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            Causal.Enabled = true;
            Causal.RecordForeign = false;
            ReportingSystem.Disabled = true;

            state = WorldFactory.CreateDebugWorld(seed: 7731);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            Causal.Enabled = true;
            Causal.RecordForeign = false;
            ReportingSystem.Disabled = false;
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void CrisisLapseCarriesPlayerDecisionProvenance()
        {
            state.activeCrises.Add(CrisisSystem.Create(state, EventCatalog.Definitions[0].id));

            turns.EndMonth();

            var record = state.causal.Latest(state.playerCountryId, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record, "the lapse month recorded no approval movement");

            var lapsed = record.contributions.Find(c => c.reason == CausalReason.CrisisLapsed);
            Assert.IsNotNull(lapsed, "the lapse penalty was not recorded as a causal contribution");
            Assert.AreEqual(CausalCategory.PlayerDecision, lapsed.category,
                "an unanswered operator crisis must remain a player decision");
            Assert.AreEqual("CrisisLapsed", lapsed.sourceActionId,
                "the player decision has no production action provenance");
        }

        [Test]
        public void PlayerDecisionWithoutActionIdDoesNotInventProvenance()
        {
            var player = state.PlayerCountry;
            float before = player.governmentApproval;

            Causal.Note(state, player.id, CausalMetric.GovernmentApproval,
                CausalReason.CrisisLapsed, before, before - 1f,
                CausalCategory.PlayerDecision);

            var record = state.causal.Latest(player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            var contribution = record.contributions.Find(c => c.reason == CausalReason.CrisisLapsed);
            Assert.IsNotNull(contribution);
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category);
            Assert.AreEqual("", contribution.sourceActionId,
                "the causal layer must not infer operator provenance from category alone");
        }

        [Test]
        public void NonPlayerCategoryWithActionIdDoesNotBecomePlayerDecision()
        {
            var player = state.PlayerCountry;
            float before = player.fiscal.sovereignDebt;

            Causal.Note(state, player.id, CausalMetric.SovereignDebt,
                CausalReason.DebtRestructured, before, before + 1f,
                CausalCategory.Fiscal,
                sourceActionId: "RestructureDebt");

            var record = state.causal.Latest(player.id, CausalMetric.SovereignDebt);
            Assert.IsNotNull(record);
            var contribution = record.contributions.Find(c => c.reason == CausalReason.DebtRestructured);
            Assert.IsNotNull(contribution);
            Assert.AreEqual(CausalCategory.Fiscal, contribution.category,
                "an action id is metadata; it must never promote an autonomous/system cause to player authorship");
            Assert.AreEqual("RestructureDebt", contribution.sourceActionId,
                "the causal layer should preserve explicit metadata and leave authorship classification to the caller");
        }
    }
}

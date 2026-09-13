using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class MonthlyDebriefTests
    {
        [Test]
        public void DebriefUsesLatestResolvedMonthAndRanksMaterialChangeFirst()
        {
            var state = WorldFactory.CreateDebugWorld(941);
            state.causal.records.Clear();
            state.causal.Add(Record(state, CausalMetric.MarketIndex, 70f, 71f, CausalReason.MarketConfidence, 2040, 1));
            state.causal.Add(Record(state, CausalMetric.GovernmentApproval, 50f, 42f, CausalReason.OrganisedUnrest, 2040, 2));
            state.causal.Add(Record(state, CausalMetric.MarketIndex, 71f, 73f, CausalReason.MarketConfidence, 2040, 2));

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(2040, report.year);
            Assert.AreEqual(2, report.month);
            Assert.AreEqual(2, report.consequences.Count);
            Assert.AreEqual(CausalMetric.GovernmentApproval, report.consequences[0].metric);
            Assert.AreEqual("DETERIORATED", report.consequences[0].direction);
        }

        [Test]
        public void DebriefIsReadOnlyAndDoesNotExposeClassifiedCause()
        {
            var state = WorldFactory.CreateDebugWorld(942);
            state.causal.records.Clear();
            var record = Record(state, CausalMetric.GovernmentApproval, 50f, 44f, CausalReason.CovertAction, 2040, 3);
            record.contributions[0].visibility = CausalVisibility.Classified;
            state.causal.Add(record);
            int before = state.causal.records.Count;

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(before, state.causal.records.Count);
            Assert.AreEqual(1, report.consequences.Count);
            Assert.AreNotEqual("COVERT ACTION", report.consequences[0].driver);
            Assert.IsTrue(report.consequences[0].incomplete);
        }

        [Test]
        public void ClassifiedPlayerDecisionDoesNotLeakProvenanceOrInvolvement()
        {
            var state = WorldFactory.CreateDebugWorld(947);
            state.causal.records.Clear();
            var record = Record(state, CausalMetric.MarketIndex, 70f, 76f, CausalReason.FiscalStimulus, 2040, 3);
            record.contributions[0].category = CausalCategory.PlayerDecision;
            record.contributions[0].sourceActionId = "StimulusPackage";
            record.contributions[0].visibility = CausalVisibility.Classified;
            state.causal.Add(record);

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(1, report.consequences.Count);
            Assert.AreEqual(0, report.PlayerLinkedCount);
            Assert.AreEqual(0, report.PlayerDrivenCount);
            Assert.AreEqual(0, report.MixedCount);
            Assert.AreEqual(1, report.WorldDrivenCount);
            Assert.IsFalse(report.consequences[0].playerLinked);
            Assert.IsFalse(report.consequences[0].playerDominant);
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.World, report.consequences[0].involvement);
            Assert.IsTrue(string.IsNullOrEmpty(report.consequences[0].sourceActionId));
            Assert.IsTrue(report.consequences[0].incomplete);
        }

        [Test]
        public void DebriefCarriesKnownPlayerActionProvenance()
        {
            var state = WorldFactory.CreateDebugWorld(943);
            state.causal.records.Clear();
            var record = Record(state, CausalMetric.MarketIndex, 70f, 76f, CausalReason.MarketConfidence, 2040, 4);
            record.contributions[0].category = CausalCategory.PlayerDecision;
            record.contributions[0].sourceActionId = "StimulusPackage";
            state.causal.Add(record);

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(1, report.PlayerLinkedCount);
            Assert.AreEqual(1, report.PlayerDrivenCount);
            Assert.AreEqual(0, report.MixedCount);
            Assert.IsTrue(report.consequences[0].playerLinked);
            Assert.IsTrue(report.consequences[0].playerDominant);
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.PlayerDriven, report.consequences[0].involvement);
            Assert.AreEqual("StimulusPackage", report.consequences[0].sourceActionId);
        }

        [Test]
        public void PlayerProvenanceSurvivesWhenWorldCauseIsDominant()
        {
            var state = WorldFactory.CreateDebugWorld(945);
            state.causal.records.Clear();
            var record = Record(state, CausalMetric.MarketIndex, 70f, 76f, CausalReason.MarketConfidence, 2040, 5);
            record.contributions[0].value = 5f;
            record.contributions.Add(new CausalContribution(
                CausalReason.FiscalStimulus, 1f, CausalCategory.PlayerDecision, CausalKind.Direct, CausalVisibility.Known)
            { sourceActionId = "StimulusPackage" });
            state.causal.Add(record);

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(1, report.PlayerLinkedCount);
            Assert.AreEqual(0, report.PlayerDrivenCount);
            Assert.AreEqual(1, report.MixedCount);
            Assert.AreEqual("MARKET CONFIDENCE", report.consequences[0].driver);
            Assert.IsFalse(report.consequences[0].playerDominant);
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.Mixed, report.consequences[0].involvement);
            Assert.AreEqual("StimulusPackage", report.consequences[0].sourceActionId);
        }

        [Test]
        public void StrongestDisclosedPlayerContributionSuppliesProvenance()
        {
            var state = WorldFactory.CreateDebugWorld(948);
            state.causal.records.Clear();
            var record = Record(state, CausalMetric.MarketIndex, 70f, 78f, CausalReason.MarketConfidence, 2040, 7);
            record.contributions[0].value = 4f;
            record.contributions.Add(new CausalContribution(
                CausalReason.FiscalStimulus, 1f, CausalCategory.PlayerDecision, CausalKind.Direct, CausalVisibility.Known)
            { sourceActionId = "SmallStimulus" });
            record.contributions.Add(new CausalContribution(
                CausalReason.FiscalStimulus, 3f, CausalCategory.PlayerDecision, CausalKind.Direct, CausalVisibility.Known)
            { sourceActionId = "LargeStimulus" });
            state.causal.Add(record);

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(1, report.PlayerLinkedCount);
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.Mixed, report.consequences[0].involvement);
            Assert.AreEqual("LargeStimulus", report.consequences[0].sourceActionId);
        }

        [Test]
        public void AutonomousConsequenceIsClassifiedAsWorldDriven()
        {
            var state = WorldFactory.CreateDebugWorld(946);
            state.causal.records.Clear();
            state.causal.Add(Record(state, CausalMetric.SocialUnrest, 40f, 46f, CausalReason.OrganisedUnrest, 2040, 6));

            var report = MonthlyDebriefSystem.Build(state);

            Assert.AreEqual(0, report.PlayerLinkedCount);
            Assert.AreEqual(1, report.WorldDrivenCount);
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.World, report.consequences[0].involvement);
        }

        [Test]
        public void EmptyLedgerProducesEmptyHonestReport()
        {
            var state = WorldFactory.CreateDebugWorld(944);
            state.causal.records.Clear();
            var report = MonthlyDebriefSystem.Build(state);
            Assert.AreEqual(0, report.consequences.Count);
        }

        static CausalRecord Record(GameState state, CausalMetric metric, float previous, float resulting,
            CausalReason reason, int year, int month)
        {
            float delta = resulting - previous;
            var record = new CausalRecord
            {
                metric = metric,
                countryId = state.playerCountryId,
                year = year,
                month = month,
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

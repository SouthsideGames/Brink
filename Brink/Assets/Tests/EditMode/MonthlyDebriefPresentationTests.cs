using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class MonthlyDebriefPresentationTests
    {
        [Test]
        public void GroupsPlayerMixedAndWorldConsequencesWithoutChangingPriorityOrder()
        {
            var report = new MonthlyDebriefSystem.Report();
            report.consequences.Add(Item(CausalMetric.GovernmentApproval, MonthlyDebriefSystem.Involvement.World, 9));
            report.consequences.Add(Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, 8));
            report.consequences.Add(Item(CausalMetric.Treasury, MonthlyDebriefSystem.Involvement.PlayerDriven, 7));

            var sections = MonthlyDebriefPresentation.Build(report, 3, 3);

            Assert.AreEqual(3, sections.whatHappened.Count);
            Assert.AreEqual(CausalMetric.GovernmentApproval, sections.whatHappened[0].metric);
            Assert.AreEqual(2, sections.yourHand.Count);
            Assert.AreEqual(CausalMetric.MarketIndex, sections.yourHand[0].metric);
            Assert.AreEqual(CausalMetric.Treasury, sections.yourHand[1].metric);
            Assert.AreEqual(1, sections.worldMoved.Count);
            Assert.AreEqual(CausalMetric.GovernmentApproval, sections.worldMoved[0].metric);
        }

        [Test]
        public void CanonicalDensityPolicyAddsContextAsSpaceIncreases()
        {
            var compact = MonthlyDebriefPresentation.LimitsFor(MonthlyDebriefPresentation.Density.Compact);
            var medium = MonthlyDebriefPresentation.LimitsFor(MonthlyDebriefPresentation.Density.Medium);
            var large = MonthlyDebriefPresentation.LimitsFor(MonthlyDebriefPresentation.Density.Large);

            Assert.AreEqual(3, compact.consequenceLimit);
            Assert.AreEqual(1, compact.involvementLimit);
            Assert.AreEqual(4, medium.consequenceLimit);
            Assert.AreEqual(2, medium.involvementLimit);
            Assert.AreEqual(5, large.consequenceLimit);
            Assert.AreEqual(3, large.involvementLimit);
            Assert.Less(compact.consequenceLimit, medium.consequenceLimit);
            Assert.Less(medium.consequenceLimit, large.consequenceLimit);
            Assert.Less(compact.involvementLimit, medium.involvementLimit);
            Assert.Less(medium.involvementLimit, large.involvementLimit);
        }

        [Test]
        public void DensityBuildUsesCanonicalLimitsAndReportsHiddenConsequences()
        {
            var report = new MonthlyDebriefSystem.Report();
            report.consequences.Add(Item(CausalMetric.GovernmentApproval, MonthlyDebriefSystem.Involvement.World, 9));
            report.consequences.Add(Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, 8));
            report.consequences.Add(Item(CausalMetric.Treasury, MonthlyDebriefSystem.Involvement.PlayerDriven, 7));
            report.consequences.Add(Item(CausalMetric.SocialUnrest, MonthlyDebriefSystem.Involvement.World, 6));
            report.consequences.Add(Item(CausalMetric.WarExhaustion, MonthlyDebriefSystem.Involvement.World, 5));
            report.consequences.Add(Item(CausalMetric.LivingStandards, MonthlyDebriefSystem.Involvement.World, 4));

            var compact = MonthlyDebriefPresentation.Build(report, MonthlyDebriefPresentation.Density.Compact);
            var medium = MonthlyDebriefPresentation.Build(report, MonthlyDebriefPresentation.Density.Medium);
            var large = MonthlyDebriefPresentation.Build(report, MonthlyDebriefPresentation.Density.Large);

            Assert.AreEqual(3, compact.whatHappened.Count);
            Assert.AreEqual(3, MonthlyDebriefPresentation.HiddenConsequenceCount(report, compact));
            Assert.AreEqual(4, medium.whatHappened.Count);
            Assert.AreEqual(2, MonthlyDebriefPresentation.HiddenConsequenceCount(report, medium));
            Assert.AreEqual(5, large.whatHappened.Count);
            Assert.AreEqual(1, MonthlyDebriefPresentation.HiddenConsequenceCount(report, large));
        }

        [Test]
        public void DensityLimitsAreIndependentAndZeroIsHonoured()
        {
            var report = new MonthlyDebriefSystem.Report();
            report.consequences.Add(Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, 9));
            report.consequences.Add(Item(CausalMetric.Treasury, MonthlyDebriefSystem.Involvement.PlayerDriven, 8));
            report.consequences.Add(Item(CausalMetric.SocialUnrest, MonthlyDebriefSystem.Involvement.World, 7));
            report.consequences.Add(Item(CausalMetric.WarExhaustion, MonthlyDebriefSystem.Involvement.World, 6));

            var compact = MonthlyDebriefPresentation.Build(report, 2, 1);
            Assert.AreEqual(2, compact.whatHappened.Count);
            Assert.AreEqual(1, compact.yourHand.Count);
            Assert.AreEqual(1, compact.worldMoved.Count);

            var hidden = MonthlyDebriefPresentation.Build(report, 0, 0);
            Assert.AreEqual(0, hidden.whatHappened.Count);
            Assert.AreEqual(0, hidden.yourHand.Count);
            Assert.AreEqual(0, hidden.worldMoved.Count);
        }

        [Test]
        public void NegativeLimitsClampToZeroAndNullConsequencesAreSkipped()
        {
            var report = new MonthlyDebriefSystem.Report();
            report.consequences.Add(null);
            report.consequences.Add(Item(CausalMetric.Treasury, MonthlyDebriefSystem.Involvement.PlayerDriven, 7));

            var hidden = MonthlyDebriefPresentation.Build(report, -1, -2);
            Assert.AreEqual(0, hidden.whatHappened.Count);
            Assert.AreEqual(0, hidden.yourHand.Count);
            Assert.AreEqual(0, hidden.worldMoved.Count);

            var visible = MonthlyDebriefPresentation.Build(report, 1, 1);
            Assert.AreEqual(1, visible.whatHappened.Count);
            Assert.AreEqual(CausalMetric.Treasury, visible.whatHappened[0].metric);
        }

        [Test]
        public void InvolvementAndProvenanceLabelsDistinguishPlayerMixedAndWorld()
        {
            var driven = Item(CausalMetric.Treasury, MonthlyDebriefSystem.Involvement.PlayerDriven, 9);
            driven.sourceActionId = "StimulusPackage";
            var mixed = Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, 8);
            mixed.sourceActionId = "TariffOrder";
            var world = Item(CausalMetric.SocialUnrest, MonthlyDebriefSystem.Involvement.World, 7);

            Assert.AreEqual("DRIVEN BY YOUR ORDER", MonthlyDebriefPresentation.InvolvementLabel(driven));
            Assert.AreEqual("YOUR ORDER: STIMULUS PACKAGE", MonthlyDebriefPresentation.ProvenanceLabel(driven));
            Assert.AreEqual("YOUR ORDER CONTRIBUTED", MonthlyDebriefPresentation.InvolvementLabel(mixed));
            Assert.AreEqual("YOUR CONTRIBUTION: TARIFF ORDER", MonthlyDebriefPresentation.ProvenanceLabel(mixed));
            Assert.AreEqual("WORLD-DRIVEN", MonthlyDebriefPresentation.InvolvementLabel(world));
            Assert.AreEqual("", MonthlyDebriefPresentation.ProvenanceLabel(world));
        }

        [Test]
        public void HumanizesStableActionIdentifiersWithoutChangingStoredProvenance()
        {
            Assert.AreEqual("STIMULUS PACKAGE", MonthlyDebriefPresentation.HumanizeActionId("StimulusPackage"));
            Assert.AreEqual("SET TAX RATE", MonthlyDebriefPresentation.HumanizeActionId("set_tax_rate"));
            Assert.AreEqual("RUN COVERT OPERATION", MonthlyDebriefPresentation.HumanizeActionId("Run-CovertOperation"));
            Assert.AreEqual("GDP SHOCK", MonthlyDebriefPresentation.HumanizeActionId("GDPShock"));
            Assert.AreEqual("", MonthlyDebriefPresentation.HumanizeActionId(""));

            var consequence = Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.PlayerDriven, 9);
            consequence.sourceActionId = "StimulusPackage";
            MonthlyDebriefPresentation.ProvenanceLabel(consequence);
            Assert.AreEqual("StimulusPackage", consequence.sourceActionId);
        }

        [Test]
        public void NullReportAndNullConsequenceProduceEmptyPresentation()
        {
            var sections = MonthlyDebriefPresentation.Build(null, 5, 3);
            Assert.AreEqual(0, sections.whatHappened.Count);
            Assert.AreEqual(0, sections.yourHand.Count);
            Assert.AreEqual(0, sections.worldMoved.Count);
            Assert.AreEqual(0, MonthlyDebriefPresentation.HiddenConsequenceCount(null, sections));
            Assert.AreEqual("", MonthlyDebriefPresentation.InvolvementLabel(null));
            Assert.AreEqual("", MonthlyDebriefPresentation.ProvenanceLabel(null));
        }

        static MonthlyDebriefSystem.Consequence Item(CausalMetric metric, MonthlyDebriefSystem.Involvement involvement, int importance)
        {
            return new MonthlyDebriefSystem.Consequence
            {
                metric = metric,
                label = metric.ToString(),
                involvement = involvement,
                playerLinked = involvement != MonthlyDebriefSystem.Involvement.World,
                playerDominant = involvement == MonthlyDebriefSystem.Involvement.PlayerDriven,
                importance = importance
            };
        }
    }
}

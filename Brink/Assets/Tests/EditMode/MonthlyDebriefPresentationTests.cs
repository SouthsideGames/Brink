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
        public void NullReportProducesEmptySections()
        {
            var sections = MonthlyDebriefPresentation.Build(null, 5, 3);
            Assert.AreEqual(0, sections.whatHappened.Count);
            Assert.AreEqual(0, sections.yourHand.Count);
            Assert.AreEqual(0, sections.worldMoved.Count);
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

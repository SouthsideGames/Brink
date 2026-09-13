using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    public class MonthlyDebriefViewModelTests
    {
        [Test]
        public void BuildsOperatorFacingSectionsInRequiredOrder()
        {
            var report = new MonthlyDebriefSystem.Report { year = 2042, month = 6 };
            report.consequences.Add(Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, "TariffOrder", 8));
            report.consequences.Add(Item(CausalMetric.SocialUnrest, MonthlyDebriefSystem.Involvement.World, "", 7));

            var model = MonthlyDebriefViewModel.Build(report, MonthlyDebriefPresentation.Density.Large);

            Assert.AreEqual("JUN 2042", model.resolvedMonth.ToUpperInvariant());
            Assert.AreEqual(2, model.trackedCount);
            Assert.AreEqual(3, model.sections.Count);
            Assert.AreEqual("LAST MONTH — WHAT HAPPENED", model.sections[0].heading);
            Assert.AreEqual("YOUR HAND", model.sections[1].heading);
            Assert.AreEqual("THE WORLD MOVED", model.sections[2].heading);
        }

        [Test]
        public void MixedConsequenceKeepsWorldWhyAndMarksPlayerAsContributor()
        {
            var report = new MonthlyDebriefSystem.Report { year = 2042, month = 6 };
            var mixed = Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, "TariffOrder", 8);
            mixed.driver = "MARKET CONFIDENCE";
            report.consequences.Add(mixed);

            var model = MonthlyDebriefViewModel.Build(report, MonthlyDebriefPresentation.Density.Compact);
            var hand = model.sections[1];

            Assert.IsTrue(Contains(hand, "YOUR ORDER CONTRIBUTED"));
            Assert.IsTrue(Contains(hand, "WHY: MARKET CONFIDENCE"));
            Assert.IsTrue(Contains(hand, "YOUR CONTRIBUTION: TARIFF ORDER"));
            Assert.IsFalse(Contains(hand, "DRIVEN BY YOUR ORDER"));
        }

        [Test]
        public void WorldSectionNeverInventsPlayerProvenance()
        {
            var report = new MonthlyDebriefSystem.Report { year = 2042, month = 6 };
            report.consequences.Add(Item(CausalMetric.SocialUnrest, MonthlyDebriefSystem.Involvement.World, "", 7));

            var model = MonthlyDebriefViewModel.Build(report, MonthlyDebriefPresentation.Density.Compact);
            var world = model.sections[1];

            Assert.IsTrue(Contains(world, "WORLD-DRIVEN"));
            Assert.IsFalse(Contains(world, "YOUR ORDER:"));
            Assert.IsFalse(Contains(world, "YOUR CONTRIBUTION:"));
        }

        [Test]
        public void CompactModelReportsHiddenMainConsequencesWithoutDroppingCausalSections()
        {
            var report = new MonthlyDebriefSystem.Report { year = 2042, month = 6 };
            report.consequences.Add(Item(CausalMetric.GovernmentApproval, MonthlyDebriefSystem.Involvement.World, "", 9));
            report.consequences.Add(Item(CausalMetric.MarketIndex, MonthlyDebriefSystem.Involvement.Mixed, "TariffOrder", 8));
            report.consequences.Add(Item(CausalMetric.Treasury, MonthlyDebriefSystem.Involvement.PlayerDriven, "StimulusPackage", 7));
            report.consequences.Add(Item(CausalMetric.SocialUnrest, MonthlyDebriefSystem.Involvement.World, "", 6));

            var model = MonthlyDebriefViewModel.Build(report, MonthlyDebriefPresentation.Density.Compact);

            Assert.AreEqual(4, model.trackedCount);
            Assert.AreEqual(1, model.hiddenCount);
            Assert.AreEqual(3, model.sections.Count);
        }

        [Test]
        public void NullReportProducesEmptyReadOnlyModel()
        {
            var model = MonthlyDebriefViewModel.Build(null, MonthlyDebriefPresentation.Density.Large);
            Assert.AreEqual("", model.resolvedMonth);
            Assert.AreEqual(0, model.trackedCount);
            Assert.AreEqual(0, model.hiddenCount);
            Assert.AreEqual(0, model.sections.Count);
        }

        static bool Contains(MonthlyDebriefViewModel.Section section, string text)
        {
            foreach (var row in section.rows)
                if (row.text.Contains(text)) return true;
            return false;
        }

        static MonthlyDebriefSystem.Consequence Item(CausalMetric metric,
            MonthlyDebriefSystem.Involvement involvement, string sourceActionId, int importance)
        {
            return new MonthlyDebriefSystem.Consequence
            {
                metric = metric,
                label = metric.ToString(),
                resulting = 50f,
                delta = involvement == MonthlyDebriefSystem.Involvement.World ? -4f : 3f,
                direction = involvement == MonthlyDebriefSystem.Involvement.World ? "DETERIORATED" : "IMPROVED",
                driver = involvement == MonthlyDebriefSystem.Involvement.World ? "ORGANISED UNREST" : "MARKET CONFIDENCE",
                sourceActionId = sourceActionId,
                playerLinked = involvement != MonthlyDebriefSystem.Involvement.World,
                playerDominant = involvement == MonthlyDebriefSystem.Involvement.PlayerDriven,
                involvement = involvement,
                importance = importance
            };
        }
    }
}

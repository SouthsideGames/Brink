using System.Collections.Generic;

namespace Brink.Core
{
    /// <summary>
    /// Read-only presentation grouping for the rollover. Keeps the causal model
    /// separate from UI Toolkit so phone/foldable/large layouts can consume the
    /// same honest classification without re-deriving causality in a view.
    /// </summary>
    public static class MonthlyDebriefPresentation
    {
        public sealed class Sections
        {
            public readonly List<MonthlyDebriefSystem.Consequence> whatHappened = new List<MonthlyDebriefSystem.Consequence>();
            public readonly List<MonthlyDebriefSystem.Consequence> yourHand = new List<MonthlyDebriefSystem.Consequence>();
            public readonly List<MonthlyDebriefSystem.Consequence> worldMoved = new List<MonthlyDebriefSystem.Consequence>();
        }

        public static Sections Build(MonthlyDebriefSystem.Report report, int consequenceLimit, int involvementLimit)
        {
            var sections = new Sections();
            if (report == null) return sections;

            consequenceLimit = consequenceLimit < 0 ? 0 : consequenceLimit;
            involvementLimit = involvementLimit < 0 ? 0 : involvementLimit;

            for (int i = 0; i < report.consequences.Count && sections.whatHappened.Count < consequenceLimit; i++)
                sections.whatHappened.Add(report.consequences[i]);

            foreach (var consequence in report.consequences)
            {
                if (consequence == null) continue;
                if (consequence.involvement == MonthlyDebriefSystem.Involvement.PlayerDriven ||
                    consequence.involvement == MonthlyDebriefSystem.Involvement.Mixed)
                {
                    if (sections.yourHand.Count < involvementLimit) sections.yourHand.Add(consequence);
                }
                else if (sections.worldMoved.Count < involvementLimit)
                {
                    sections.worldMoved.Add(consequence);
                }

                if (sections.yourHand.Count >= involvementLimit && sections.worldMoved.Count >= involvementLimit)
                    break;
            }

            return sections;
        }
    }
}

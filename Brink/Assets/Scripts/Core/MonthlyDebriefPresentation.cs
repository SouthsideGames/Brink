using System.Collections.Generic;
using System.Text;

namespace Brink.Core
{
    /// <summary>
    /// Read-only presentation grouping for the rollover. Keeps the causal model
    /// separate from UI Toolkit so phone/foldable/large layouts can consume the
    /// same honest classification without re-deriving causality in a view.
    /// </summary>
    public static class MonthlyDebriefPresentation
    {
        public enum Density
        {
            Compact = 0,
            Medium,
            Large
        }

        public struct Limits
        {
            public int consequenceLimit;
            public int involvementLimit;

            public Limits(int consequenceLimit, int involvementLimit)
            {
                this.consequenceLimit = consequenceLimit;
                this.involvementLimit = involvementLimit;
            }
        }

        public sealed class Sections
        {
            public readonly List<MonthlyDebriefSystem.Consequence> whatHappened = new List<MonthlyDebriefSystem.Consequence>();
            public readonly List<MonthlyDebriefSystem.Consequence> yourHand = new List<MonthlyDebriefSystem.Consequence>();
            public readonly List<MonthlyDebriefSystem.Consequence> worldMoved = new List<MonthlyDebriefSystem.Consequence>();
        }

        /// <summary>
        /// Canonical rollover density. Small screens prioritize; larger screens
        /// add context without changing any underlying simulation or causality.
        /// </summary>
        public static Limits LimitsFor(Density density)
        {
            switch (density)
            {
                case Density.Compact: return new Limits(3, 1);
                case Density.Medium: return new Limits(4, 2);
                default: return new Limits(5, 3);
            }
        }

        public static Sections Build(MonthlyDebriefSystem.Report report, Density density)
        {
            var limits = LimitsFor(density);
            return Build(report, limits.consequenceLimit, limits.involvementLimit);
        }

        public static Sections Build(MonthlyDebriefSystem.Report report, int consequenceLimit, int involvementLimit)
        {
            var sections = new Sections();
            if (report == null) return sections;

            consequenceLimit = consequenceLimit < 0 ? 0 : consequenceLimit;
            involvementLimit = involvementLimit < 0 ? 0 : involvementLimit;

            for (int i = 0; i < report.consequences.Count && sections.whatHappened.Count < consequenceLimit; i++)
                if (report.consequences[i] != null) sections.whatHappened.Add(report.consequences[i]);

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

        public static int HiddenConsequenceCount(MonthlyDebriefSystem.Report report, Sections sections)
        {
            if (report == null) return 0;
            int shown = sections?.whatHappened?.Count ?? 0;
            int hidden = report.consequences.Count - shown;
            return hidden > 0 ? hidden : 0;
        }

        public static string InvolvementLabel(MonthlyDebriefSystem.Consequence consequence)
        {
            if (consequence == null) return "";
            switch (consequence.involvement)
            {
                case MonthlyDebriefSystem.Involvement.PlayerDriven: return "DRIVEN BY YOUR ORDER";
                case MonthlyDebriefSystem.Involvement.Mixed: return "YOUR ORDER CONTRIBUTED";
                default: return "WORLD-DRIVEN";
            }
        }

        public static string ProvenanceLabel(MonthlyDebriefSystem.Consequence consequence)
        {
            if (consequence == null || !consequence.playerLinked || string.IsNullOrEmpty(consequence.sourceActionId)) return "";
            string action = HumanizeActionId(consequence.sourceActionId);
            return consequence.involvement == MonthlyDebriefSystem.Involvement.PlayerDriven
                ? "YOUR ORDER: " + action
                : "YOUR CONTRIBUTION: " + action;
        }

        /// <summary>
        /// Causal provenance is stored as a stable action identifier. The rollover
        /// should not expose implementation-shaped PascalCase/snake_case tokens to
        /// the operator, so convert them to terminal copy without changing the
        /// authoritative identifier kept on the consequence itself.
        /// </summary>
        public static string HumanizeActionId(string actionId)
        {
            if (string.IsNullOrEmpty(actionId)) return "";
            var output = new StringBuilder(actionId.Length + 8);
            char previous = '\0';
            for (int i = 0; i < actionId.Length; i++)
            {
                char current = actionId[i];
                if (current == '_' || current == '-')
                {
                    if (output.Length > 0 && output[output.Length - 1] != ' ') output.Append(' ');
                    previous = current;
                    continue;
                }

                bool boundary = i > 0 && char.IsUpper(current) &&
                    (char.IsLower(previous) || char.IsDigit(previous) ||
                     (i + 1 < actionId.Length && char.IsLower(actionId[i + 1])));
                if (boundary && output.Length > 0 && output[output.Length - 1] != ' ') output.Append(' ');
                output.Append(current);
                previous = current;
            }
            return output.ToString().Trim().ToUpperInvariant();
        }
    }
}

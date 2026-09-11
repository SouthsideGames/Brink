using System;
using System.Collections.Generic;
using System.Text;
using Brink.Core;
using Brink.Data;

namespace Brink.UI
{
    /// <summary>
    /// Turns a disclosed explanation into terminal text (spec 26 §6).
    ///
    /// **Pure, and deliberately not a view.** It takes a width and returns a
    /// string, so every width rule this project has learned the hard way can be
    /// tested without a panel: no row is built from a hardcoded column count,
    /// the label column is derived from `TerminalMetrics` by the caller, and a
    /// long cause is truncated rather than allowed to run off the right edge.
    /// The END MONTH overflow was five separate defects of exactly that kind,
    /// one of which was a figure that grew one column per resolved month.
    ///
    /// **The presentation matches the mathematics.** Where the record reconciles
    /// — contributions that genuinely sum to the observed change — figures are
    /// printed and a NET line proves they add up. Where it does not, because a
    /// cause was withheld or the relationship is not additive, the same causes
    /// are printed as ranked bands and no total is claimed. Inventing additive
    /// precision would be the numeric version of the distortion spec 15 forbids.
    /// </summary>
    public static class CausalExplanation
    {
        /// <summary>Width of the right-hand figure column.</summary>
        const int ValueColumn = 9;

        /// <summary>
        /// The "WHY?" block for one month's movement. Never returns null; an
        /// absent or empty record produces an honest line saying so rather than
        /// nothing, because a blank panel reads as a broken feature.
        /// </summary>
        public static string Render(DisclosedExplanation view, int width)
        {
            width = Math.Max(24, width);
            var sb = new StringBuilder();

            if (view == null)
            {
                sb.Append(AsciiChart.WrapBlock(
                    "NO EXPLANATION ON RECORD YET. EXPLANATIONS BEGIN FROM "
                    + "THE NEXT RESOLVED MONTH.", width));
                return sb.ToString();
            }

            string heading = CausalReasons.MetricLabel(view.metric);
            sb.Append($"WHY DID {heading} CHANGE?").Append('\n');
            sb.Append(new GameDate(view.year, view.month).DisplayString).Append('\n').Append('\n');

            sb.Append(AsciiChart.Row("CURRENT", Figure(view.resulting), width)).Append('\n');
            sb.Append(AsciiChart.Row("THIS MONTH", Signed(view.delta), width)).Append('\n');

            var causes = Ranked(view);
            bool additive = view.reconciliation != CausalReconciliation.Qualitative;

            if (causes.Count == 0)
            {
                sb.Append('\n');
                sb.Append(view.withheld > 0
                    ? "NO REPORTING ON WHAT CAUSED THIS."
                    : "NO SINGLE FACTOR CARRIED THE MONTH.");
                return sb.ToString();
            }

            sb.Append('\n');
            sb.Append(additive ? "PRIMARY PRESSURES" : "PRIMARY DRIVERS").Append('\n');

            float largest = 0f;
            for (int i = 0; i < causes.Count; i++)
                largest = Math.Max(largest, Math.Abs(causes[i].value));

            for (int i = 0; i < causes.Count; i++)
            {
                var cause = causes[i];
                string label = LabelFor(cause);
                string value = !cause.sized ? "UNSIZED"
                    : additive ? Signed(cause.value)
                    : Band(cause.value, largest);
                sb.Append(Row(label, value, width)).Append('\n');
            }

            if (additive)
            {
                sb.Append(new string('-', width)).Append('\n');
                sb.Append(AsciiChart.Row("NET CHANGE", Signed(view.delta), width)).Append('\n');
            }

            if (view.withheld > 0)
            {
                sb.Append('\n');
                sb.Append(AsciiChart.WrapBlock(
                    view.withheld == 1
                        ? "ONE FURTHER FACTOR IS NOT REPORTED TO US."
                        : $"{view.withheld} FURTHER FACTORS ARE NOT REPORTED TO US.",
                    width));
                sb.Append('\n');
            }

            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Recent months, newest first (spec 26 §5). Bounded by the caller and
        /// by the ledger itself; this never walks more than it is handed.
        /// </summary>
        public static string RenderHistory(IList<DisclosedExplanation> views, int width, int perMonth = 3)
        {
            width = Math.Max(24, width);
            var sb = new StringBuilder();

            if (views == null || views.Count == 0)
                return "NO HISTORY ON RECORD YET.";

            sb.Append($"{CausalReasons.MetricLabel(views[0].metric)} HISTORY").Append('\n');

            for (int i = 0; i < views.Count; i++)
            {
                var view = views[i];
                sb.Append('\n');
                sb.Append(AsciiChart.Row(
                    new GameDate(view.year, view.month).DisplayString,
                    Signed(view.delta), width)).Append('\n');

                var causes = Ranked(view);
                bool additive = view.reconciliation != CausalReconciliation.Qualitative;
                float largest = 0f;
                for (int c = 0; c < causes.Count; c++)
                    largest = Math.Max(largest, Math.Abs(causes[c].value));

                int shown = Math.Min(perMonth, causes.Count);
                for (int c = 0; c < shown; c++)
                {
                    var cause = causes[c];
                    string value = !cause.sized ? "UNSIZED"
                        : additive ? Signed(cause.value)
                        : Band(cause.value, largest);
                    // Indented two, and the indent is taken out of the row width
                    // rather than added to it, or the deepest line is the one
                    // that overflows.
                    sb.Append("  ").Append(Row(LabelFor(cause), value, width - 2)).Append('\n');
                }
            }

            return sb.ToString().TrimEnd('\n');
        }

        /// <summary>
        /// Ranked for reading: largest effect first, bookkeeping last, and
        /// **deterministic** — ties break on the reason's own ordinal so two
        /// runs of the same save print the same order. Sorting a copy, never the
        /// record: the recorded order is the order the simulation applied things
        /// and is not the renderer's to rewrite.
        /// </summary>
        static List<DisclosedCause> Ranked(DisclosedExplanation view)
        {
            var causes = new List<DisclosedCause>();
            for (int i = 0; i < view.causes.Count; i++)
            {
                var cause = view.causes[i];

                // Bookkeeping is suppressed unless it is genuinely carrying the
                // month. An operator told that approval fell because it was
                // "settling toward level" has learned nothing.
                if (CausalReasons.IsStructural(cause.reason)
                    && Math.Abs(cause.value) < Math.Abs(view.delta) * 0.5f) continue;

                causes.Add(cause);
            }

            causes.Sort((a, b) =>
            {
                bool sa = CausalReasons.IsStructural(a.reason);
                bool sb2 = CausalReasons.IsStructural(b.reason);
                if (sa != sb2) return sa ? 1 : -1;

                int bySize = Math.Abs(b.value).CompareTo(Math.Abs(a.value));
                if (bySize != 0) return bySize;
                return ((int)a.reason).CompareTo((int)b.reason);
            });

            return causes;
        }

        static string LabelFor(DisclosedCause cause)
        {
            string label = CausalReasons.Label(cause.reason);

            // Name the state behind a cause only where disclosure allowed it.
            if (!string.IsNullOrEmpty(cause.sourceCountryId))
                label += " (" + cause.sourceCountryId + ")";

            if (cause.visibility == CausalVisibility.Suspected) label += " ?";
            return label;
        }

        /// <summary>
        /// A row whose label is truncated to fit rather than allowed to push the
        /// figure off the screen.
        /// </summary>
        static string Row(string label, string value, int width)
        {
            int room = Math.Max(6, width - Math.Max(ValueColumn, value.Length) - 3);
            return AsciiChart.Row(AsciiChart.Cell(label, room).TrimEnd(), value, width);
        }

        static string Figure(float value) => value.ToString("0.0");

        static string Signed(float value)
        {
            // Explicit sign on every figure: a column of bare numbers where some
            // are costs and some are relief is unreadable at a glance, and this
            // panel exists to be read at a glance.
            if (Math.Abs(value) < 0.05f) return "0.0";
            return value.ToString("+0.0;-0.0");
        }

        /// <summary>
        /// Rank and direction without a figure, for the cases where an additive
        /// total would be a fabrication.
        /// </summary>
        internal static string Band(float value, float largest)
        {
            string direction = value >= 0f ? "POSITIVE" : "NEGATIVE";
            if (largest <= 0f) return direction;

            float share = Math.Abs(value) / largest;
            if (share >= 0.6f) return "STRONG " + direction;
            if (share >= 0.3f) return "MODERATE " + direction;
            return "MINOR " + direction;
        }
    }
}

using System.Collections.Generic;
using Brink.Core;
using Brink.Data;

namespace Brink.UI
{
    /// <summary>
    /// Pure rollover UI model. Converts the read-only causal presentation into
    /// terminal-ready rows without depending on UI Toolkit or mutating GameState.
    /// The shell only needs to render these rows.
    /// </summary>
    public static class MonthlyDebriefViewModel
    {
        public sealed class Row
        {
            public string text;
            public string style;

            public Row(string text, string style = "terminal-text")
            {
                this.text = text ?? "";
                this.style = string.IsNullOrEmpty(style) ? "terminal-text" : style;
            }
        }

        public sealed class Section
        {
            public string heading;
            public readonly List<Row> rows = new List<Row>();

            public Section(string heading)
            {
                this.heading = heading ?? "";
            }
        }

        public sealed class Model
        {
            public string resolvedMonth = "";
            public int trackedCount;
            public int hiddenCount;
            public readonly List<Section> sections = new List<Section>();
        }

        public static Model Build(MonthlyDebriefSystem.Report report, MonthlyDebriefPresentation.Density density)
        {
            var model = new Model();
            if (report == null) return model;

            model.resolvedMonth = report.year > 0 && report.month > 0
                ? new GameDate(report.year, report.month).DisplayString
                : "";
            model.trackedCount = report.consequences.Count;

            var grouped = MonthlyDebriefPresentation.Build(report, density);
            model.hiddenCount = MonthlyDebriefPresentation.HiddenConsequenceCount(report, grouped);

            var happened = new Section("LAST MONTH — WHAT HAPPENED");
            foreach (var consequence in grouped.whatHappened)
                AddConsequence(happened, consequence, includeWhy: true, includeInvolvement: false);
            model.sections.Add(happened);

            if (grouped.yourHand.Count > 0)
            {
                var hand = new Section("YOUR HAND");
                foreach (var consequence in grouped.yourHand)
                    AddConsequence(hand, consequence, includeWhy: true, includeInvolvement: true);
                model.sections.Add(hand);
            }

            if (grouped.worldMoved.Count > 0)
            {
                var world = new Section("THE WORLD MOVED");
                foreach (var consequence in grouped.worldMoved)
                    AddConsequence(world, consequence, includeWhy: true, includeInvolvement: true);
                model.sections.Add(world);
            }

            return model;
        }

        static void AddConsequence(Section section, MonthlyDebriefSystem.Consequence consequence,
            bool includeWhy, bool includeInvolvement)
        {
            if (section == null || consequence == null) return;

            string marker = consequence.direction == "DETERIORATED" ? "!" :
                consequence.direction == "IMPROVED" ? "+" : ".";
            string style = consequence.direction == "DETERIORATED" ? "sig-hostile" : "terminal-text";
            section.rows.Add(new Row(
                $"{marker} {consequence.label.ToUpperInvariant()} {consequence.delta:+0.0;-0.0;0.0} — {consequence.direction}", style));

            if (includeInvolvement)
            {
                string involvement = MonthlyDebriefPresentation.InvolvementLabel(consequence);
                if (!string.IsNullOrEmpty(involvement)) section.rows.Add(new Row(involvement, "terminal-text-bright"));
            }

            if (includeWhy)
            {
                string why = "WHY: " + consequence.driver;
                if (consequence.incomplete) why += " [REPORTING INCOMPLETE]";
                section.rows.Add(new Row(why, "terminal-text-dim"));
            }

            string provenance = MonthlyDebriefPresentation.ProvenanceLabel(consequence);
            if (!string.IsNullOrEmpty(provenance)) section.rows.Add(new Row(provenance, "terminal-text-bright"));
        }
    }
}

using Brink.Core;
using UnityEngine.UIElements;

namespace Brink.UI
{
    /// <summary>
    /// UI Toolkit adapter for the pure MonthlyDebriefViewModel. Keeping rendering
    /// here makes TerminalShellController responsible only for supplying the report,
    /// density, and destination ScrollView.
    /// </summary>
    public static class MonthlyDebriefRenderer
    {
        public static MonthlyDebriefViewModel.Model Render(
            ScrollView body,
            MonthlyDebriefSystem.Report report,
            MonthlyDebriefPresentation.Density density)
        {
            var model = MonthlyDebriefViewModel.Build(report, density);
            if (body == null) return model;

            if (model.trackedCount == 0)
            {
                AddSection(body, "LAST MONTH — WHAT HAPPENED");
                AddLine(body, "  No resolved-month consequence record is available yet.", "terminal-text-dim");
                return model;
            }

            if (!string.IsNullOrEmpty(model.resolvedMonth))
            {
                AddLine(body,
                    $"  {model.resolvedMonth} — {model.trackedCount} tracked consequence{(model.trackedCount == 1 ? "" : "s")}.",
                    "terminal-text-dim");
            }

            for (int i = 0; i < model.sections.Count; i++)
            {
                var section = model.sections[i];
                AddSection(body, (i == 0 ? " " : "\n ") + section.heading);
                foreach (var row in section.rows)
                    AddLine(body, "  " + row.text, row.style);
            }

            if (model.hiddenCount > 0)
            {
                AddLine(body,
                    $"  + {model.hiddenCount} lower-priority consequence{(model.hiddenCount == 1 ? "" : "s")} {(model.hiddenCount == 1 ? "remains" : "remain")} on record.",
                    "terminal-text-dim");
            }

            return model;
        }

        static void AddSection(VisualElement body, string heading)
        {
            var label = new Label(heading);
            label.AddToClassList("terminal-text-bright");
            body.Add(label);
        }

        static void AddLine(VisualElement body, string text, string style)
        {
            var label = new Label(text ?? "");
            label.AddToClassList("terminal-text");
            if (!string.IsNullOrEmpty(style) && style != "terminal-text") label.AddToClassList(style);
            body.Add(label);
        }
    }
}

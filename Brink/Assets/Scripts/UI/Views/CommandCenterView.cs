using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Vertical-slice home surface. It does not create a new simulation layer;
    /// it composes the certified attention, strategic-board, Cabinet, surprise
    /// and standing-strategy readers into one operator-facing answer to:
    /// "what deserves my attention this month?"
    /// </summary>
    public sealed class CommandCenterView : TerminalView
    {
        public override string Id => "COMMAND CENTER";
        public override string ShortCode => "CMD";

        readonly Label header;
        readonly Label summary;
        readonly VisualElement priorityList;
        readonly Label course;
        readonly Label guidance;

        public CommandCenterView()
        {
            header = AddText("terminal-text-bright");
            summary = AddText();
            priorityList = new VisualElement();
            Root.Add(priorityList);
            course = AddText();
            guidance = AddText("terminal-text-dim");
        }

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            int w = TerminalMetrics.Columns;
            var report = CommandCenterSystem.Read(state);

            header.text = AsciiChart.BoxHeader($"COMMAND CENTER — {state.date.DisplayString}", w);

            var top = new StringBuilder();
            top.AppendLine($" {player.displayName.ToUpperInvariant()}   CP {state.commandPoints.current}");
            top.AppendLine($" {report.decisionCount} DECISION{(report.decisionCount == 1 ? "" : "S")} WAITING   {report.priorityCount} PRIORIT{(report.priorityCount == 1 ? "Y" : "IES")} SHOWN");
            top.AppendLine(report.decisionCount > 0
                ? " OPERATOR STATUS: ATTENTION REQUIRED"
                : " OPERATOR STATUS: NO REQUIRED DECISION — THE WORLD STILL MOVES");
            summary.text = top.ToString();

            priorityList.Clear();
            var heading = new Label(" PRIORITY BOARD");
            heading.AddToClassList("terminal-text-bright");
            priorityList.Add(heading);

            if (report.priorities.Count == 0)
            {
                AddLine("  NO MATERIAL PRESSURE IDENTIFIED.", "terminal-text-dim");
            }
            else
            {
                foreach (var item in report.priorities)
                {
                    string glyph = item.required ? "!" : ">";
                    AddLine($"  {glyph} {item.title.ToUpperInvariant()}   → {item.viewId}",
                        item.required ? "sig-hostile" : "terminal-text");
                    if (!string.IsNullOrEmpty(item.detail))
                        AddLine("    " + item.detail, "terminal-text-dim");
                }
            }

            var strategy = new StringBuilder();
            strategy.AppendLine(AsciiChart.BoxHeader("STANDING COURSE", w));
            strategy.AppendLine($" DOCTRINE: {report.doctrine.ToUpperInvariant()}");
            if (!string.IsNullOrEmpty(report.policy))
                strategy.AppendLine($" NATIONAL POLICY: {report.policy.ToUpperInvariant()}");
            if (!string.IsNullOrEmpty(report.plan))
                strategy.AppendLine($" PLAN: {report.plan.ToUpperInvariant()}");
            strategy.AppendLine($" ERA: {report.era.ToUpperInvariant()}");
            course.text = strategy.ToString();

            guidance.text = AsciiChart.WrapBlock(
                " COMMAND CENTER prioritizes; it does not decide. Open the named desk to investigate, direct, or intervene. END MONTH remains available when you choose to leave matters delegated or unresolved.", w);
        }

        void AddLine(string text, string cls)
        {
            var label = new Label(text);
            label.AddToClassList("terminal-text");
            label.AddToClassList(cls);
            priorityList.Add(label);
        }
    }
}

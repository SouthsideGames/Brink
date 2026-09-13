using System;
using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Vertical-slice home surface. It composes existing certified readers into
    /// one operator-facing answer to what changed, what needs attention, why,
    /// and where the operator can act. Read-only: no command is executed here.
    /// </summary>
    public sealed class CommandCenterView : TerminalView
    {
        public override string Id => "COMMAND CENTER";
        public override string ShortCode => "CMD";

        readonly Label header;
        readonly Label summary;
        readonly VisualElement priorityList;
        readonly VisualElement pressureBoard;
        readonly Label course;
        readonly Label guidance;

        public CommandCenterView()
        {
            header = AddText("terminal-text-bright");
            summary = AddText();
            priorityList = new VisualElement(); Root.Add(priorityList);
            pressureBoard = new VisualElement(); Root.Add(pressureBoard);
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
            top.AppendLine(report.decisionCount > 0 ? " OPERATOR STATUS: ATTENTION REQUIRED" : " OPERATOR STATUS: NO REQUIRED DECISION — THE WORLD STILL MOVES");
            summary.text = top.ToString();

            priorityList.Clear();
            AddLine(priorityList, " PRIORITY BOARD", "terminal-text-bright");
            if (report.priorities.Count == 0) AddLine(priorityList, "  NO MATERIAL PRESSURE IDENTIFIED.", "terminal-text-dim");
            else foreach (var item in report.priorities)
            {
                string glyph = item.required ? "!" : ">";
                AddLine(priorityList, $"  {glyph} {item.title.ToUpperInvariant()}   → {item.viewId}", item.required ? "sig-hostile" : "terminal-text");
                if (!string.IsNullOrEmpty(item.detail)) AddLine(priorityList, "    " + item.detail, "terminal-text-dim");
            }

            BuildPressureBoard(state);

            var strategy = new StringBuilder();
            strategy.AppendLine(AsciiChart.BoxHeader("STANDING COURSE", w));
            strategy.AppendLine($" DOCTRINE: {report.doctrine.ToUpperInvariant()}");
            if (!string.IsNullOrEmpty(report.policy)) strategy.AppendLine($" NATIONAL POLICY: {report.policy.ToUpperInvariant()}");
            if (!string.IsNullOrEmpty(report.plan)) strategy.AppendLine($" PLAN: {report.plan.ToUpperInvariant()}");
            strategy.AppendLine($" ERA: {report.era.ToUpperInvariant()}");
            course.text = strategy.ToString();

            guidance.text = AsciiChart.WrapBlock(" COMMAND CENTER prioritizes and explains; it does not decide. Suggested commands are places to investigate, not promises of outcome. END MONTH remains available when you choose to leave matters delegated or unresolved.", w);
        }

        void BuildPressureBoard(GameState state)
        {
            pressureBoard.Clear();
            AddLine(pressureBoard, AsciiChart.BoxHeader("WHAT CHANGED / WHAT CAN I DO?", TerminalMetrics.Columns), "terminal-text-bright");
            var board = StrategicBoardSystem.Build(state);
            if (board.Count == 0)
            {
                AddLine(pressureBoard, " NO RESOLVED-MONTH ANALYSIS YET. COMPLETE A MONTH TO ESTABLISH CAUSAL READOUTS.", "terminal-text-dim");
                return;
            }

            int issueLimit = TerminalMetrics.SizeClass == SizeClass.Compact ? 2 : TerminalMetrics.SizeClass == SizeClass.Medium ? 3 : 4;
            int optionLimit = TerminalMetrics.SizeClass == SizeClass.Compact ? 2 : 3;
            int shown = Math.Min(issueLimit, board.Count);
            for (int i = 0; i < shown; i++)
            {
                var item = board[i];
                string flag = item.urgency >= 3 ? "!!" : item.urgency == 2 ? "! " : "  ";
                string cls = item.urgency >= 3 ? "sig-hostile" : "terminal-text";
                AddLine(pressureBoard,
                    $" {flag} {item.label.ToUpperInvariant()}   {item.value:F1}   {(item.delta >= 0 ? "+" : "")}{item.delta:F1}   {item.direction}", cls);
                AddLine(pressureBoard, "    WHY: " + item.driver + (item.incomplete ? "  [REPORTING INCOMPLETE]" : ""), "terminal-text-dim");

                var options = ActionFinderSystem.ForMetric(state, item.metric, optionLimit);
                if (options.Count == 0)
                {
                    AddLine(pressureBoard, "    RESPONSE: NO RELEVANT COMMANDS INDEXED.", "terminal-text-dim");
                    continue;
                }

                AddLine(pressureBoard, "    POSSIBLE RESPONSES:", "terminal-text-dim");
                foreach (var option in options)
                {
                    string marker = option.available ? "▸" : "·";
                    string line = $"      {marker} {option.label.ToUpperInvariant()} [{option.cost}] → {option.viewId}";
                    if (!option.available && !string.IsNullOrEmpty(option.blockedReason)) line += " — " + option.blockedReason;
                    AddLine(pressureBoard, line, option.available ? "terminal-text" : "terminal-text-dim");
                }
            }
            AddLine(pressureBoard, " READOUT RANKS ATTENTION. OPTIONS ARE NAVIGATION, NOT GUARANTEED FIXES.", "terminal-text-dim");
        }

        static void AddLine(VisualElement parent, string text, string cls)
        {
            var label = new Label(text); label.AddToClassList("terminal-text"); label.AddToClassList(cls); parent.Add(label);
        }
    }
}
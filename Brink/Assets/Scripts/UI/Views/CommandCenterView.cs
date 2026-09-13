using System;
using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    public sealed class CommandCenterView : TerminalView
    {
        public override string Id => "COMMAND CENTER";
        public override string ShortCode => "CMD";

        readonly Label header;
        readonly Label summary;
        readonly VisualElement priorityList;
        readonly VisualElement pressureBoard;
        readonly OperationPlanningPanel operationPlanning;
        readonly Label course;
        readonly Label guidance;

        public CommandCenterView()
        {
            header = AddText("terminal-text-bright");
            summary = AddText();
            priorityList = new VisualElement(); Root.Add(priorityList);
            pressureBoard = new VisualElement(); Root.Add(pressureBoard);
            operationPlanning = new OperationPlanningPanel(Refresh); Root.Add(operationPlanning.Root);
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
            var report = CommandCenterSystem.Build(state);
            int decisionCount = AttentionSystem.DecisionCount(AttentionSystem.Collect(state));
            header.text = AsciiChart.BoxHeader($"COMMAND CENTER — {state.date.DisplayString}", w);

            var top = new StringBuilder();
            top.AppendLine($" {player.displayName.ToUpperInvariant()}   CP {state.commandPoints.current}");
            top.AppendLine($" {decisionCount} DECISION{(decisionCount == 1 ? "" : "S")} WAITING   {report.Count} PRIORIT{(report.Count == 1 ? "Y" : "IES")} SHOWN");
            top.AppendLine(decisionCount > 0 ? " OPERATOR STATUS: ATTENTION REQUIRED" : " OPERATOR STATUS: NO REQUIRED DECISION — THE WORLD STILL MOVES");
            summary.text = top.ToString();

            priorityList.Clear();
            AddLine(priorityList, " PRIORITY BOARD", "terminal-text-bright");
            if (report.Count == 0) AddLine(priorityList, "  NO MATERIAL PRESSURE IDENTIFIED.", "terminal-text-dim");
            else foreach (var item in report)
            {
                bool required = item.urgency >= 4;
                string glyph = required ? "!" : ">";
                AddLine(priorityList, $"  {glyph} {item.title.ToUpperInvariant()}   → {item.viewId}", required ? "sig-hostile" : "terminal-text");
                if (!string.IsNullOrEmpty(item.summary)) AddLine(priorityList, "    " + item.summary, "terminal-text-dim");
            }

            BuildPressureBoard(state);

            var front = state.ActiveConfrontation;
            operationPlanning.Root.style.display = front == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (front != null) operationPlanning.Build(state, front);

            var strategy = new StringBuilder();
            strategy.AppendLine(AsciiChart.BoxHeader("STANDING COURSE", w));
            var era = StrategicEraSystem.Current(state);
            if (era == null) strategy.AppendLine(" NO NAMED STRATEGIC ERA YET — SET OR REVISE THE COURSE FROM OPERATOR.");
            else
            {
                strategy.AppendLine($" ERA: {era.name.ToUpperInvariant()}");
                if (!string.IsNullOrEmpty(era.character)) strategy.AppendLine(" " + era.character);
            }
            course.text = strategy.ToString();

            guidance.text = AsciiChart.WrapBlock(" COMMAND CENTER prioritizes and explains; it does not decide. Campaign planning records intent only; actual military operations still execute from the MILITARY desk and pay normal CP. END MONTH remains available when you choose to leave matters delegated or unresolved.", w);
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

            int issueLimit = TerminalMetrics.Size == SizeClass.Compact ? 2 : TerminalMetrics.Size == SizeClass.Medium ? 3 : 4;
            int optionLimit = TerminalMetrics.Size == SizeClass.Compact ? 2 : 3;
            int shown = Math.Min(issueLimit, board.Count);
            for (int i = 0; i < shown; i++)
            {
                var item = board[i];
                string flag = item.urgency >= 3 ? "!!" : item.urgency == 2 ? "! " : "  ";
                string cls = item.urgency >= 3 ? "sig-hostile" : "terminal-text";
                AddLine(pressureBoard, $" {flag} {item.label.ToUpperInvariant()}   {item.value:F1}   {(item.delta >= 0 ? "+" : "")}{item.delta:F1}   {item.direction}", cls);
                AddLine(pressureBoard, "    WHY: " + item.driver + (item.incomplete ? "  [REPORTING INCOMPLETE]" : ""), "terminal-text-dim");

                var options = ActionFinderSystem.ForMetric(state, item.metric, optionLimit);
                if (options.Count == 0) { AddLine(pressureBoard, "    RESPONSE: NO RELEVANT COMMANDS INDEXED.", "terminal-text-dim"); continue; }
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
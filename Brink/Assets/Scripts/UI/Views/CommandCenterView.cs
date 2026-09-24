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
        readonly VisualElement waitList;
        readonly VisualElement pressureBoard;
        readonly OperationPlanningPanel operationPlanning;
        readonly Label course;
        readonly Label mandate;
        readonly Label guidance;

        public CommandCenterView()
        {
            header = AddText("terminal-text-bright");
            summary = AddText();
            priorityList = new VisualElement(); Root.Add(priorityList);
            waitList = new VisualElement(); Root.Add(waitList);
            pressureBoard = new VisualElement(); Root.Add(pressureBoard);
            operationPlanning = new OperationPlanningPanel(Refresh); Root.Add(operationPlanning.Root);
            course = AddText();
            mandate = AddText();
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
            top.AppendLine($" {decisionCount} DECISION{(decisionCount == 1 ? "" : "S")} WAITING   {report.Count} PRIORIT{(report.Count == 1 ? "Y" : "IES")} TRACKED");
            top.AppendLine(decisionCount > 0 ? " OPERATOR STATUS: ATTENTION REQUIRED" : " OPERATOR STATUS: NO REQUIRED DECISION — THE WORLD STILL MOVES");
            summary.text = top.ToString();

            BuildAttentionHierarchy(report);
            BuildPressureBoard(state);

            var front = state.ActiveConfrontation;
            operationPlanning.Root.style.display = front == null ? DisplayStyle.None : DisplayStyle.Flex;
            if (front != null) operationPlanning.Build(state, front);

            bool compact = TerminalMetrics.Size == SizeClass.Compact;
            bool large = TerminalMetrics.Size == SizeClass.Large;

            // Compact screens are an attention surface, not a shrunk desktop.
            // The standing course and mandate remain fully available from OPERATOR;
            // medium restores the course, while large uses the extra information
            // space to keep both strategic horizons visible at once.
            course.style.display = compact ? DisplayStyle.None : DisplayStyle.Flex;
            mandate.style.display = large ? DisplayStyle.Flex : DisplayStyle.None;

            if (!compact)
            {
                var strategy = new StringBuilder();
                strategy.AppendLine(AsciiChart.BoxHeader("STANDING COURSE", w));
                var era = StrategicEraSystem.Current(state);
                if (era == null) strategy.AppendLine(" NO DECLARED STRATEGIC ERA YET — SET OR REVISE THE COURSE FROM OPERATOR.");
                else
                {
                    strategy.AppendLine($" DECLARED ERA: {era.name.ToUpperInvariant()}");
                    if (!string.IsNullOrEmpty(era.character)) strategy.AppendLine(" " + era.character);
                }
                course.text = strategy.ToString();
            }

            if (large)
            {
                var mandateText = new StringBuilder();
                mandateText.AppendLine(AsciiChart.BoxHeader("MANDATE", w));
                if (state.mandate == null) mandateText.AppendLine(" NO MANDATE ON FILE.");
                else
                {
                    mandateText.AppendLine(" " + state.mandate.title.ToUpperInvariant());
                    if (state.mandateRecord != null)
                        mandateText.AppendLine($" VERDICT: {state.mandateRecord.verdict.ToString().ToUpperInvariant()}   {state.mandateRecord.met}/{state.mandateRecord.total}");
                    else
                    {
                        int remaining = Math.Max(0, state.mandate.reviewMonths - state.date.MonthsSince(state.startDate));
                        mandateText.AppendLine($" {MandateSystem.MetCount(state)}/{state.mandate.objectives.Count} CURRENTLY MET   REVIEW IN {remaining} MO");
                    }
                    mandateText.AppendLine(" FULL BRIEF AND STRATEGY → OPERATOR");
                }
                mandate.text = mandateText.ToString();
            }

            guidance.text = AsciiChart.WrapBlock(compact
                ? " COMMAND CENTER is attention-first on this display: what needs you, what changed, and campaign intent. Standing course and mandate detail remain in OPERATOR. END MONTH remains a valid choice."
                : large
                    ? " COMMAND CENTER uses the wider display for attention plus strategic context. It prioritizes and explains; it does not decide. Campaign plans remain intent unless you issue a standing order; every operation pays normal CP."
                    : " COMMAND CENTER shows attention plus the standing course on this display. Full mandate detail remains in OPERATOR. It prioritizes and explains; it does not decide. END MONTH remains available.", w);
        }

        void BuildAttentionHierarchy(System.Collections.Generic.List<CommandCenterSystem.Section> report)
        {
            priorityList.Clear();
            waitList.Clear();
            AddLine(priorityList, " WHAT NEEDS ME", "terminal-text-bright");

            int urgent = 0;
            foreach (var item in report)
            {
                if (item.urgency < 2) continue;
                urgent++;
                string glyph = item.urgency >= 4 ? "!" : ">";
                AddLine(priorityList, $"  {glyph} {item.title.ToUpperInvariant()}   → {item.viewId}", item.urgency >= 4 ? "sig-hostile" : "terminal-text");
                if (!string.IsNullOrEmpty(item.summary)) AddLine(priorityList, "    " + item.summary, "terminal-text-dim");
            }
            if (urgent == 0)
                AddLine(priorityList, "  NOTHING REQUIRES OPERATOR ATTENTION RIGHT NOW.", "terminal-text-dim");

            AddLine(waitList, " WHAT CAN WAIT", "terminal-text-bright");
            int deferred = 0;
            int deferredLimit = TerminalMetrics.Size == SizeClass.Compact ? 2 : TerminalMetrics.Size == SizeClass.Medium ? 4 : int.MaxValue;
            foreach (var item in report)
            {
                if (item.urgency >= 2) continue;
                deferred++;
                if (deferred > deferredLimit) continue;
                AddLine(waitList, $"  · {item.title.ToUpperInvariant()}   → {item.viewId}", "terminal-text-dim");
                if (!string.IsNullOrEmpty(item.summary)) AddLine(waitList, "    " + item.summary, "terminal-text-dim");
            }
            if (deferred == 0)
                AddLine(waitList, urgent == 0
                    ? "  NO EXCEPTIONAL PRESSURE. END MONTH IS A VALID CHOICE."
                    : "  EVERYTHING CURRENTLY TRACKED IS MATERIAL ENOUGH TO REVIEW.", "terminal-text-dim");
            else if (deferred > deferredLimit)
                AddLine(waitList, $"  + {deferred - deferredLimit} MORE LOWER-PRIORITY ITEM{(deferred - deferredLimit == 1 ? "" : "S")} IN BRIEFING.", "terminal-text-dim");
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
            if (board.Count > shown)
                AddLine(pressureBoard, $" + {board.Count - shown} MORE STRATEGIC PRESSURE{(board.Count - shown == 1 ? "" : "S")} IN BRIEFING / PILLAR DESKS.", "terminal-text-dim");
            AddLine(pressureBoard, " READOUT RANKS ATTENTION. OPTIONS ARE NAVIGATION, NOT GUARANTEED FIXES.", "terminal-text-dim");
        }

        static void AddLine(VisualElement parent, string text, string cls)
        {
            var label = new Label(text); label.AddToClassList("terminal-text"); label.AddToClassList(cls); parent.Add(label);
        }
    }
}

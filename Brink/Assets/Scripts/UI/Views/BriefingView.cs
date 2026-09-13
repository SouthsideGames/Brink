using System;
using System.Text;
using Brink.Core;
using Brink.Data;

namespace Brink.UI.Views
{
    /// <summary>
    /// Situation report: what is happening in the country and world. The Command
    /// Center owns prioritization; this screen owns evidence, traffic and context.
    /// </summary>
    public class BriefingView : TerminalView
    {
        public override string Id => "BRIEFING";
        public override string ShortCode => "BRF";
        static int W => TerminalMetrics.Columns;

        readonly UnityEngine.UIElements.Label header;
        readonly UnityEngine.UIElements.Label trafficHeading;
        readonly UnityEngine.UIElements.VisualElement trafficList;
        readonly UnityEngine.UIElements.Label body;
        readonly UnityEngine.UIElements.Label wire;
        readonly UnityEngine.UIElements.VisualElement attentionList;
        readonly UnityEngine.UIElements.VisualElement directiveList;
        readonly UnityEngine.UIElements.VisualElement holdPanel;

        public BriefingView()
        {
            header = AddText("terminal-text-bright");
            attentionList = new UnityEngine.UIElements.VisualElement(); Root.Add(attentionList);
            holdPanel = new UnityEngine.UIElements.VisualElement(); Root.Add(holdPanel);
            directiveList = new UnityEngine.UIElements.VisualElement(); Root.Add(directiveList);
            trafficHeading = AddText();
            trafficList = new UnityEngine.UIElements.VisualElement(); Root.Add(trafficList);
            body = AddText();
            wire = AddText("terminal-text-dim");
        }

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            header.text = AsciiChart.BoxHeader($"SITUATION BRIEF — {state.date.DisplayString}", W);
            BuildAttention(state);
            BuildHold(state);
            BuildDirectives(state);
            BuildTraffic(state);
            AttentionSystem.MarkBriefingSeen(state);

            bool compact = TerminalMetrics.Size == SizeClass.Compact;
            bool large = TerminalMetrics.Size == SizeClass.Large;
            var sb = new StringBuilder();
            sb.AppendLine($" NATION: {player.displayName.ToUpperInvariant()}   ({player.government.TypeText})");
            sb.AppendLine($" ELAPSED: {state.date.MonthsSince(state.startDate)} MO   COMMAND POINTS: {state.commandPoints.current}");

            if (!compact && state.assessment != null && state.assessment.traits.Count > 0)
            {
                var traitNames = new System.Collections.Generic.List<string>();
                foreach (var trait in state.assessment.traits) traitNames.Add(trait.name.ToUpperInvariant());
                sb.AppendLine($" NATIONAL CHARACTER: {string.Join(" / ", traitNames)}");
            }

            sb.AppendLine();
            sb.AppendLine(" NATIONAL CONDITION");
            if (compact)
            {
                sb.AppendLine("  " + AsciiChart.LabeledBar("ECONOMY", player.pillars.economy, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("MILITARY", player.pillars.military, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("APPROVAL", player.governmentApproval, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("STABILITY", player.stability, 100, 14, 20));
            }
            else
            {
                sb.AppendLine("  " + AsciiChart.LabeledBar("MILITARY", player.pillars.military, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("ECONOMY", player.pillars.economy, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("INTELLIGENCE", player.pillars.intelligence, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("DIPLOMACY", player.pillars.diplomacy, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("GOVERNMENT", player.pillars.government, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("APPROVAL", player.governmentApproval, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("STABILITY", player.stability, 100, 14, 20));
                sb.AppendLine("  " + AsciiChart.LabeledBar("UNITY", player.nationalUnity, 100, 14, 20));
            }

            sb.AppendLine();
            sb.AppendLine(" STRATEGIC RESOURCES");
            string trend = state.treasuryTrendSeeded && Math.Abs(state.treasuryTrend) >= 1f
                ? $"{player.resources.treasury:F0}  ({(state.treasuryTrend >= 0 ? "+" : "")}{state.treasuryTrend:F0}/MO)"
                : $"{player.resources.treasury:F0}";
            sb.AppendLine("  " + AsciiChart.Row("TREASURY", trend, W - 4));
            AppendTreasuryWarning(sb, state, player);

            if (compact)
            {
                sb.AppendLine("  " + AsciiChart.Row("ENERGY", $"{player.resources.energy:F1}", W - 4));
                sb.AppendLine("  " + AsciiChart.Row("INDUSTRY", $"{player.resources.industrialCapacity:F1}", W - 4));
                sb.AppendLine("  MORE NATIONAL DETAIL → PILLAR DESKS");
            }
            else
            {
                sb.AppendLine("  " + AsciiChart.Row("MANPOWER", $"{player.resources.manpower:F0}", W - 4));
                sb.AppendLine("  " + AsciiChart.Row("ENERGY", $"{player.resources.energy:F1}", W - 4));
                sb.AppendLine("  " + AsciiChart.Row("INDUSTRIAL CAPACITY", $"{player.resources.industrialCapacity:F1}", W - 4));
                if (large)
                {
                    sb.AppendLine("  " + AsciiChart.Row("STRATEGIC MATERIALS", $"{player.resources.strategicMaterials:F1}", W - 4));
                    sb.AppendLine("  " + AsciiChart.Row("FOOD SECURITY", $"{player.resources.foodSecurity:F1}", W - 4));
                }
            }
            body.text = sb.ToString();

            BuildWire(state, compact ? 4 : large ? 10 : 7);
        }

        static void AppendTreasuryWarning(StringBuilder sb, GameState state, CountryState player)
        {
            if (!state.treasuryTrendSeeded || state.treasuryTrend >= -4f) return;
            if (player.resources.treasury <= 0f)
            {
                sb.AppendLine("  ! THE TREASURY IS IN DEFICIT AND SINKING.");
                sb.AppendLine("    Programmes and research will stall.");
                return;
            }
            float monthsLeft = player.resources.treasury / -state.treasuryTrend;
            if (monthsLeft < 36f)
            {
                sb.AppendLine($"  ! SPENDING RUNS {-state.treasuryTrend:F0}/MONTH AHEAD OF INCOME.");
                sb.AppendLine($"    Reserves carry ~{monthsLeft:F0} months at this rate.");
            }
        }

        void BuildWire(GameState state, int limit)
        {
            var wireText = new StringBuilder();
            wireText.AppendLine(AsciiChart.BoxHeader("GLOBAL WIRE — RECENT", W));
            int shown = 0;
            for (int i = state.chronicle.Count - 1; i >= 0 && shown < limit; i--)
            {
                var entry = state.chronicle[i];
                if (!WorldWire.CanShow(state, entry)) continue;
                shown++;
                wireText.AppendLine($" {entry.date.SortKey}  [{Phrase.Caps(entry.category)}] {entry.text}");
            }
            if (shown == 0) wireText.AppendLine(" NO TRAFFIC.");
            wire.text = wireText.ToString();
        }

        void BuildAttention(GameState state)
        {
            attentionList.Clear();
            var items = AttentionSystem.Collect(state);
            int decisions = AttentionSystem.DecisionCount(items);
            if (decisions == 0) return;

            var heading = new UnityEngine.UIElements.Label(" UNRESOLVED DECISIONS");
            heading.AddToClassList("terminal-text-bright");
            attentionList.Add(heading);
            foreach (var item in items)
            {
                if (item.level != AttentionLevel.Decision) continue;
                var line = new UnityEngine.UIElements.Label($"  ! [{item.viewId}] {item.summary}");
                line.AddToClassList("terminal-text"); line.AddToClassList("sig-hostile"); attentionList.Add(line);
            }
            var note = new UnityEngine.UIElements.Label("  Prioritization and lower-pressure items → COMMAND CENTER. The month may still be ended; unanswered decisions are recorded as a failure to decide.");
            note.AddToClassList("terminal-text-dim"); attentionList.Add(note);
        }

        void BuildDirectives(GameState state)
        {
            directiveList.Clear();
            var directives = DirectiveSystem.Collect(state);
            if (directives.Count == 0) return;

            var heading = new UnityEngine.UIElements.Label(" CABINET RECOMMENDS");
            heading.AddToClassList("terminal-text-bright"); directiveList.Add(heading);
            int limit = TerminalMetrics.Size == SizeClass.Compact ? 1 : TerminalMetrics.Size == SizeClass.Medium ? 2 : directives.Count;
            int shown = 0;
            foreach (var directive in directives)
            {
                if (shown++ >= limit) break;
                var title = new UnityEngine.UIElements.Label($"  ▸ {directive.title.ToUpperInvariant()}   → {directive.viewId}");
                title.AddToClassList("terminal-text"); directiveList.Add(title);
                var why = new UnityEngine.UIElements.Label($"     {directive.rationale}");
                why.AddToClassList("terminal-text-dim"); directiveList.Add(why);
                if (TerminalMetrics.Size != SizeClass.Compact)
                {
                    var how = new UnityEngine.UIElements.Label($"     {directive.suggestion}");
                    how.AddToClassList("terminal-text-dim"); directiveList.Add(how);
                }
            }
            if (directives.Count > limit)
            {
                var more = new UnityEngine.UIElements.Label($"  + {directives.Count - limit} MORE CABINET RECOMMENDATION{(directives.Count - limit == 1 ? "" : "S")} → CABINET");
                more.AddToClassList("terminal-text-dim"); directiveList.Add(more);
            }
            var note = new UnityEngine.UIElements.Label("  Advice, not orders. Nothing here is tracked or scored.");
            note.AddToClassList("terminal-text-dim"); directiveList.Add(note);
        }

        void BuildTraffic(GameState state)
        {
            trafficHeading.text = " PRIORITY TRAFFIC";
            trafficList.Clear();
            var items = new System.Collections.Generic.List<Notification>();
            foreach (var n in state.notifications) if (n.date.Equals(state.date)) items.Add(n);
            items.Sort((a, b) => a.priority.CompareTo(b.priority));

            int limit = TerminalMetrics.Size == SizeClass.Compact ? 4 : TerminalMetrics.Size == SizeClass.Medium ? 7 : int.MaxValue;
            if (items.Count == 0)
            {
                var empty = new UnityEngine.UIElements.Label("  NO TRAFFIC THIS MONTH.");
                empty.AddToClassList("terminal-text"); empty.AddToClassList("terminal-text-dim"); trafficList.Add(empty); return;
            }

            int shown = 0;
            foreach (var n in items)
            {
                if (shown++ >= limit) break;
                string cls = n.priority.ToString().ToUpperInvariant();
                string line = $"  [{cls,-8}] {n.title}";
                if (!string.IsNullOrEmpty(n.body)) line += $" — {n.body}";
                var label = new UnityEngine.UIElements.Label(line);
                label.AddToClassList("terminal-text"); label.AddToClassList(TrafficClass(n.priority)); trafficList.Add(label);
            }
            if (items.Count > limit)
            {
                var more = new UnityEngine.UIElements.Label($"  + {items.Count - limit} MORE CURRENT-MONTH TRAFFIC ITEM{(items.Count - limit == 1 ? "" : "S")} IN CHRONICLE.");
                more.AddToClassList("terminal-text-dim"); trafficList.Add(more);
            }
        }

        static string TrafficClass(NotificationClass priority)
        {
            switch (priority)
            {
                case NotificationClass.Flash: return "sig-hostile";
                case NotificationClass.Priority: return "sig-rival";
                case NotificationClass.Advisory: return "terminal-text-bright";
                case NotificationClass.Wire: return "terminal-text";
                default: return "terminal-text-dim";
            }
        }

        void BuildHold(GameState state)
        {
            holdPanel.Clear();
            bool possible = HoldSystem.CanHold(state, out string blocked);
            var row = new UnityEngine.UIElements.VisualElement(); row.AddToClassList("button-row"); holdPanel.Add(row);
            var hold = AddButton(row, $"HOLD ({holdMonths} MO)", null, () => { lastHold = GameController.Instance.Hold(holdMonths); Refresh(); });
            if (!possible) Block(hold, blocked);
            AddButton(row, holdMonths <= 3 ? "LONGER ►" : "SHORTER ◄", null, () => { holdMonths = holdMonths <= 3 ? HoldSystem.MaxMonths : 3; Refresh(); });
            if (lastHold.months > 0)
            {
                var outcome = new UnityEngine.UIElements.Label(); outcome.AddToClassList("terminal-text"); outcome.AddToClassList("terminal-text-dim");
                outcome.text = $"   HELD {lastHold.months} MO — " + (lastHold.RanToCompletion ? "the stretch ran out quietly." : "STOPPED: " + lastHold.stopped);
                holdPanel.Add(outcome);
            }
            ExplainBlockedCommands(holdPanel);
        }

        static int holdMonths = 3;
        static HoldSystem.Result lastHold;
    }
}
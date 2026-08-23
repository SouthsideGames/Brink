using System.Text;
using Brink.Core;
using Brink.Data;

namespace Brink.UI.Views
{
    /// <summary>
    /// The Briefing layer (GDD §28.1): national situation at a glance plus the
    /// recent wire. Real prioritized items (FLASH/PRIORITY/...) arrive with the
    /// events system; Phase 1 presents state + chronicle.
    /// </summary>
    public class BriefingView : TerminalView
    {
        public override string Id => "BRIEFING";
        public override string ShortCode => "BRF";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        readonly UnityEngine.UIElements.Label header;
        readonly UnityEngine.UIElements.Label trafficHeading;

        /// <summary>
        /// Priority traffic is one label per entry rather than one rich-text
        /// block. Inline `&lt;color=…&gt;` tags were counted toward the line width by
        /// the wrapper, so a FLASH line wrapped 24 invisible characters early —
        /// and the hexes were green-theme values that did not follow the palette.
        /// A label per entry fixes both: the class carries the colour, and the
        /// wrapper sees only visible text.
        /// </summary>
        readonly UnityEngine.UIElements.VisualElement trafficList;

        readonly UnityEngine.UIElements.Label body;
        readonly UnityEngine.UIElements.Label wire;

        /// <summary>
        /// What is waiting, and on which panel. One label per line so the class
        /// carries the colour and the wrapper measures only visible text — the
        /// same reason priority traffic is built this way.
        /// </summary>
        readonly UnityEngine.UIElements.VisualElement attentionList;

        /// <summary>Standing directives — what the Cabinet thinks is worth doing.</summary>
        readonly UnityEngine.UIElements.VisualElement directiveList;

        public BriefingView()
        {
            header = AddText("terminal-text-bright");
            attentionList = new UnityEngine.UIElements.VisualElement();
            Root.Add(attentionList);
            directiveList = new UnityEngine.UIElements.VisualElement();
            Root.Add(directiveList);
            trafficHeading = AddText();
            trafficList = new UnityEngine.UIElements.VisualElement();
            Root.Add(trafficList);
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

            header.text = AsciiChart.BoxHeader($"STRATEGIC BRIEFING — {state.date.DisplayString}", W);

            BuildAttention(state);
            BuildDirectives(state);
            BuildTraffic(state);

            // Opening the briefing is what counts as having read it. Markers that
            // keep shouting after they have been seen become wallpaper, and then
            // a real one goes unnoticed.
            AttentionSystem.MarkBriefingSeen(state);

            var sb = new StringBuilder();
            sb.AppendLine($" NATION: {player.displayName.ToUpperInvariant()}   ({player.government.TypeText})");
            sb.AppendLine($" ELAPSED: {state.date.MonthsSince(state.startDate)} MO   COMMAND POINTS: {state.commandPoints.current}");

            if (state.assessment != null && state.assessment.traits.Count > 0)
            {
                var traitNames = new System.Collections.Generic.List<string>();
                foreach (var trait in state.assessment.traits) traitNames.Add(trait.name.ToUpperInvariant());
                sb.AppendLine($" NATIONAL CHARACTER: {string.Join(" / ", traitNames)}");
            }
            sb.AppendLine();
            sb.AppendLine(" HEADLINE PILLARS");
            sb.AppendLine("  " + AsciiChart.LabeledBar("MILITARY", player.pillars.military, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("ECONOMY", player.pillars.economy, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("INTELLIGENCE", player.pillars.intelligence, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("DIPLOMACY", player.pillars.diplomacy, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("GOVERNMENT", player.pillars.government, 100, 14, 20));
            sb.AppendLine();
            sb.AppendLine(" STRATEGIC RESOURCES");
            sb.AppendLine("  " + AsciiChart.Row("TREASURY", $"{player.resources.treasury:F0}", W - 4));
            sb.AppendLine("  " + AsciiChart.Row("MANPOWER", $"{player.resources.manpower:F0}", W - 4));
            sb.AppendLine("  " + AsciiChart.Row("ENERGY", $"{player.resources.energy:F1}", W - 4));
            sb.AppendLine("  " + AsciiChart.Row("INDUSTRIAL CAPACITY", $"{player.resources.industrialCapacity:F1}", W - 4));
            sb.AppendLine("  " + AsciiChart.Row("STRATEGIC MATERIALS", $"{player.resources.strategicMaterials:F1}", W - 4));
            sb.AppendLine("  " + AsciiChart.Row("FOOD SECURITY", $"{player.resources.foodSecurity:F1}", W - 4));
            sb.AppendLine();
            sb.AppendLine(" SOCIAL PRESSURE");
            sb.AppendLine("  " + AsciiChart.LabeledBar("APPROVAL", player.governmentApproval, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("STABILITY", player.stability, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("UNITY", player.nationalUnity, 100, 14, 20));
            body.text = sb.ToString();

            var wireText = new StringBuilder();
            wireText.AppendLine(AsciiChart.BoxHeader("GLOBAL WIRE — RECENT", W));
            int shown = 0;
            // Through the fog, like everything else. This printed the chronicle
            // raw — and the chronicle is the world's *true* record, including
            // every covert operation, research programme and sponsored coup in
            // it. A player with no collection at all was reading foreign secrets
            // off their own briefing screen.
            for (int i = state.chronicle.Count - 1; i >= 0 && shown < 10; i--)
            {
                var entry = state.chronicle[i];
                if (!WorldWire.CanShow(state, entry)) continue;
                shown++;
                wireText.AppendLine($" {entry.date.SortKey}  [{Phrase.Caps(entry.category)}] {entry.text}");
            }
            if (shown == 0) wireText.AppendLine(" NO TRAFFIC.");
            wire.text = wireText.ToString();
        }

        /// <summary>
        /// Priority traffic (GDD §28.2): current-month notifications sorted
        /// FLASH → PRIORITY → ADVISORY → WIRE → ARCHIVE, color-coded.
        /// </summary>
        /// <summary>
        /// The one place that answers "what should I be looking at?".
        ///
        /// Each line names the panel, so the operator is never left hunting for
        /// where a thing lives — which in a text game is most of the difficulty.
        /// Decisions are listed first and marked `!`; the glyph carries the
        /// meaning so the reading survives any palette and any colour vision.
        /// </summary>
        void BuildAttention(GameState state)
        {
            attentionList.Clear();

            var items = AttentionSystem.Collect(state);
            if (items.Count == 0) return;

            var heading = new UnityEngine.UIElements.Label(" REQUIRES ATTENTION");
            heading.AddToClassList("terminal-text-bright");
            attentionList.Add(heading);

            void Emit(AttentionLevel level, string glyph, string ussClass)
            {
                foreach (var item in items)
                {
                    if (item.level != level) continue;
                    var line = new UnityEngine.UIElements.Label(
                        $"  {glyph} [{item.viewId}] {item.summary}");
                    line.AddToClassList("terminal-text");
                    line.AddToClassList(ussClass);
                    attentionList.Add(line);
                }
            }

            Emit(AttentionLevel.Decision, "!", "sig-hostile");
            Emit(AttentionLevel.Information, ".", "terminal-text-dim");

            int decisions = AttentionSystem.DecisionCount(items);
            if (decisions > 0)
            {
                var note = new UnityEngine.UIElements.Label(
                    "  The month can still be ended. Anything left unanswered is recorded as this "
                    + "office having failed to decide.");
                note.AddToClassList("terminal-text-dim");
                attentionList.Add(note);
            }
        }

        /// <summary>
        /// What the Cabinet thinks is worth doing (GDD §29).
        ///
        /// The attention block says what is *waiting*; this says what is *worth
        /// doing*, which is the question a new operator actually has and the one
        /// nothing else in the game answered. Every entry names the panel and a
        /// concrete verb, because "improve the economy" is not advice.
        ///
        /// Advice, never obligation: nothing tracks these, nothing rewards
        /// clearing them, and they vanish when the condition that raised them
        /// does. The GDD is explicit that directives must never become chores.
        /// </summary>
        void BuildDirectives(GameState state)
        {
            directiveList.Clear();

            var directives = DirectiveSystem.Collect(state);
            if (directives.Count == 0) return;

            var heading = new UnityEngine.UIElements.Label(" CABINET RECOMMENDS");
            heading.AddToClassList("terminal-text-bright");
            directiveList.Add(heading);

            foreach (var directive in directives)
            {
                var title = new UnityEngine.UIElements.Label(
                    $"  ▸ {directive.title.ToUpperInvariant()}   → {directive.viewId}");
                title.AddToClassList("terminal-text");
                directiveList.Add(title);

                var why = new UnityEngine.UIElements.Label($"     {directive.rationale}");
                why.AddToClassList("terminal-text-dim");
                directiveList.Add(why);

                var how = new UnityEngine.UIElements.Label($"     {directive.suggestion}");
                how.AddToClassList("terminal-text-dim");
                directiveList.Add(how);
            }

            var note = new UnityEngine.UIElements.Label(
                "  Advice, not orders. Nothing here is tracked or scored.");
            note.AddToClassList("terminal-text-dim");
            directiveList.Add(note);
        }

        void BuildTraffic(GameState state)
        {
            trafficHeading.text = " PRIORITY TRAFFIC";
            trafficList.Clear();

            var items = new System.Collections.Generic.List<Notification>();
            foreach (var n in state.notifications)
                if (n.date.Equals(state.date)) items.Add(n);
            items.Sort((a, b) => a.priority.CompareTo(b.priority));

            if (items.Count == 0)
            {
                var empty = new UnityEngine.UIElements.Label("  NO TRAFFIC THIS MONTH.");
                empty.AddToClassList("terminal-text");
                empty.AddToClassList("terminal-text-dim");
                trafficList.Add(empty);
                return;
            }

            foreach (var n in items)
            {
                string cls = n.priority.ToString().ToUpperInvariant();
                string line = $"  [{cls,-8}] {n.title}";
                if (!string.IsNullOrEmpty(n.body)) line += $" — {n.body}";

                var label = new UnityEngine.UIElements.Label(line);
                label.AddToClassList("terminal-text");
                label.AddToClassList(TrafficClass(n.priority));
                trafficList.Add(label);
            }
        }

        /// <summary>
        /// Notification class as a USS class rather than an inline hex, so the
        /// colours follow whichever palette the operator is using (GDD §28.2).
        /// </summary>
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
    }
}

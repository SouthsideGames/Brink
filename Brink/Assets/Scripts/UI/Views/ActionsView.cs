using System.Text;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Everything the operator can do, and what is currently out of reach
    /// (GDD §28.1). Runtime-system verbs come from ActionCatalog; posting-level
    /// strategy verbs come from StrategyActionCatalog and are merged here into
    /// one permanent reference.
    /// </summary>
    public class ActionsView : TerminalView
    {
        public override string Id => "ACTIONS";
        public override string ShortCode => "ACT";
        static int W => TerminalMetrics.Columns;
        Pillar? filter;
        bool openOnly;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;

            Root.Clear();
            BuildHeader(state);
            BuildFilters();
            BuildEntries(state);
        }

        void BuildHeader(GameState state)
        {
            var all = AllEntries(state);
            int open = 0;
            foreach (var entry in all) if (entry.available) open++;

            var header = AddText("terminal-text-bright");
            header.text =
                AsciiChart.BoxHeader("COMMAND INDEX — WHAT CAN I DO?", W) + "\n" +
                $" CP {state.commandPoints.current}   INF {state.influence}   " +
                $"PC {state.politicalCapital:F0}   {open}/{all.Count} OPEN NOW";

            AddText("terminal-text-dim").text = openOnly
                ? " Showing commands open to you now. Switch to ALL to study blocked capabilities and what would unlock them."
                : " Open commands are listed before blocked capabilities. Blocked commands stay visible because knowing what would unlock them is part of the game.";
        }

        void BuildFilters()
        {
            var availability = new VisualElement();
            availability.AddToClassList("button-row");
            Root.Add(availability);
            AddModeButton(availability, "ALL COMMANDS", false);
            AddModeButton(availability, "OPEN NOW", true);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            AddFilterButton(row, "ALL", null);
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                AddFilterButton(row, pillar.ToString().ToUpperInvariant(), pillar);
        }

        void AddModeButton(VisualElement row, string label, bool value)
        {
            bool current = openOnly == value;
            var button = new Button(() => { openOnly = value; Refresh(); })
            { text = (current ? "► " : "") + label };
            button.AddToClassList("cmd-button");
            if (current) button.AddToClassList("primary");
            button.SetEnabled(!current);
            row.Add(button);
        }

        void AddFilterButton(VisualElement row, string label, Pillar? pillar)
        {
            bool current = filter.HasValue == pillar.HasValue
                           && (!filter.HasValue || filter.Value == pillar.Value);

            var button = new Button(() => { filter = pillar; Refresh(); })
            { text = (current ? "► " : "") + label };
            button.AddToClassList("cmd-button");
            if (current) button.AddToClassList("primary");
            button.SetEnabled(!current);
            row.Add(button);
        }

        void BuildEntries(GameState state)
        {
            var entries = AllEntries(state);
            var pillars = new List<Pillar>();
            foreach (var entry in entries)
                if ((!filter.HasValue || entry.pillar == filter.Value)
                    && (!openOnly || entry.available)
                    && !pillars.Contains(entry.pillar)) pillars.Add(entry.pillar);

            int shown = 0;
            foreach (var pillar in pillars)
            {
                AddText("terminal-text-bright").text = "\n " + pillar.ToString().ToUpperInvariant();
                shown += Emit(entries, pillar, true);
                if (!openOnly) shown += Emit(entries, pillar, false);
            }

            if (shown == 0)
                AddText("terminal-text-dim").text = openOnly
                    ? "\n NO COMMANDS IN THIS FILTER ARE OPEN RIGHT NOW. SWITCH TO ALL COMMANDS TO SEE WHY."
                    : "\n NO COMMANDS INDEXED FOR THIS FILTER.";
        }

        int Emit(List<ActionEntry> entries, Pillar pillar, bool available)
        {
            int count = 0;
            foreach (var entry in entries)
            {
                if (entry.pillar != pillar || entry.available != available) continue;
                if (filter.HasValue && entry.pillar != filter.Value) continue;
                if (openOnly && !entry.available) continue;

                var sb = new StringBuilder();
                sb.AppendLine($"  {(entry.available ? "▸" : "·")} {entry.label.ToUpperInvariant()}   " +
                              $"[{entry.cost}]   → {entry.viewId}");
                sb.Append($"     {entry.description}");
                if (!entry.available) sb.Append($"\n     UNAVAILABLE: {entry.blockedReason}");

                var label = AddText(entry.available ? "terminal-text" : "terminal-text-dim");
                label.text = sb.ToString();
                count++;
            }
            return count;
        }

        static List<ActionEntry> AllEntries(GameState state)
        {
            var entries = new List<ActionEntry>();
            entries.AddRange(ActionCatalog.All(state));
            entries.AddRange(StrategyActionCatalog.All(state));
            return entries;
        }
    }
}

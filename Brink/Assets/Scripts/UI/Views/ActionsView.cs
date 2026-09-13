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
            int open = ActionCatalog.AvailableCount(state) + StrategyActionCatalog.AvailableCount(state);
            var header = AddText("terminal-text-bright");
            header.text =
                AsciiChart.BoxHeader("COMMAND INDEX — EVERY AVAILABLE ACTION", W) + "\n" +
                $" CP {state.commandPoints.current}   INF {state.influence}   " +
                $"PC {state.politicalCapital:F0}   {open} action(s) open to you now";

            AddText("terminal-text-dim").text =
                " Greyed entries are real actions that something currently prevents. " +
                "The reason is given so the block reads as a rule rather than a missing feature.";
        }

        void BuildFilters()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            AddFilterButton(row, "ALL", null);
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                AddFilterButton(row, pillar.ToString().ToUpperInvariant(), pillar);
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
            var entries = new List<ActionEntry>();
            entries.AddRange(ActionCatalog.All(state));
            entries.AddRange(StrategyActionCatalog.All(state));

            Pillar? lastPillar = null;
            foreach (var entry in entries)
            {
                if (filter.HasValue && entry.pillar != filter.Value) continue;

                if (lastPillar != entry.pillar)
                {
                    lastPillar = entry.pillar;
                    AddText("terminal-text-bright").text =
                        "\n " + entry.pillar.ToString().ToUpperInvariant();
                }

                var sb = new StringBuilder();
                sb.AppendLine($"  {(entry.available ? "▸" : "·")} {entry.label.ToUpperInvariant()}   " +
                              $"[{entry.cost}]   → {entry.viewId}");
                sb.Append($"     {entry.description}");
                if (!entry.available) sb.Append($"\n     UNAVAILABLE: {entry.blockedReason}");

                var label = AddText(entry.available ? "terminal-text" : "terminal-text-dim");
                label.text = sb.ToString();
            }
        }
    }
}

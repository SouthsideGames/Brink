using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Everything the operator can do, and what is currently out of reach
    /// (GDD §28.1).
    ///
    /// This panel exists because the author of the game — who designed every one
    /// of these verbs — reported forgetting what was possible. That is a
    /// reference problem, not an onboarding one: onboarding fades, and this does
    /// not. So it is a permanent screen rather than a tutorial step, and it
    /// shows unavailable actions with the reason rather than hiding them,
    /// because a hidden verb teaches the player it does not exist.
    /// </summary>
    public class ActionsView : TerminalView
    {
        public override string Id => "ACTIONS";
        public override string ShortCode => "ACT";

        static int W => TerminalMetrics.Columns;

        Pillar? filter; // null = everything

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
            var header = AddText("terminal-text-bright");
            header.text =
                AsciiChart.BoxHeader("COMMAND INDEX — EVERY AVAILABLE ACTION", W) + "\n" +
                $" CP {state.commandPoints.current}   INF {state.influence}   " +
                $"PC {state.politicalCapital:F0}   " +
                $"{ActionCatalog.AvailableCount(state)} action(s) open to you now";

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
            var lastPillar = (Pillar?)null;

            foreach (var entry in ActionCatalog.All(state))
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

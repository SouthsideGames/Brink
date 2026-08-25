using System.Collections.Generic;
using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// The World Chronicle (GDD §31.3). Major events are archived by country and
    /// globally as they happen; a long save becomes its own alternate history.
    /// This is where the operator reads it back.
    /// </summary>
    public class ChronicleView : TerminalView
    {
        public override string Id => "CHRONICLE";
        public override string ShortCode => "CHR";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;
        const int PageSize = 24;

        string countryFilter;                    // null = all
        ChronicleCategory? categoryFilter;        // null = all
        int page;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;

            Root.Clear();
            var entries = Filter(state);

            BuildHeader(state, entries.Count);
            BuildFilters(state);
            BuildEntries(state, entries);
            BuildPager(entries.Count);
        }

        List<ChronicleEntry> Filter(GameState state)
        {
            var result = new List<ChronicleEntry>();
            foreach (var entry in state.chronicle)
            {
                // The record the operator can read is not the record the world
                // kept. This filtered by country and category and never looked at
                // publicity at all, so the history screen listed foreign covert
                // operations and research programmes to anyone who scrolled.
                if (!WorldWire.CanShow(state, entry)) continue;
                if (countryFilter != null && entry.countryId != countryFilter) continue;
                if (categoryFilter.HasValue && entry.category != categoryFilter.Value) continue;
                result.Add(entry);
            }
            return result;
        }

        void BuildHeader(GameState state, int matching)
        {
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("WORLD CHRONICLE", W));

            int years = state.date.MonthsSince(state.startDate) / 12;
            sb.AppendLine($" {state.startDate.DisplayString} — {state.date.DisplayString}   " +
                          $"{years} YEAR(S) ON RECORD   {state.chronicle.Count} ENTRIES");
            sb.AppendLine($" SHOWING: {matching}");

            // A quick read of what kind of history this save has been.
            var counts = new Dictionary<ChronicleCategory, int>();
            foreach (var entry in state.chronicle)
            {
                counts.TryGetValue(entry.category, out int existing);
                counts[entry.category] = existing + 1;
            }
            var parts = new List<string>();
            foreach (ChronicleCategory category in System.Enum.GetValues(typeof(ChronicleCategory)))
            {
                counts.TryGetValue(category, out int count);
                if (count > 0) parts.Add($"{category.ToString().ToUpperInvariant()} {count}");
            }
            if (parts.Count > 0) sb.AppendLine(" " + string.Join("   ", parts));

            text.text = sb.ToString();
        }

        void BuildFilters(GameState state)
        {
            AddText("terminal-text-dim").text = " FILTER BY STATE";
            var countryRow = MakeRow();

            AddFilterButton(countryRow, "ALL", countryFilter == null, () => { countryFilter = null; page = 0; });
            foreach (var country in state.countries)
            {
                var captured = country.id;
                var profile = WorldFactory.FindProfile(country.id);
                AddFilterButton(countryRow, profile?.mapCode ?? country.id,
                    countryFilter == country.id,
                    () => { countryFilter = captured; page = 0; });
            }

            AddText("terminal-text-dim").text = " FILTER BY CATEGORY";
            var categoryRow = MakeRow();
            AddFilterButton(categoryRow, "ALL", !categoryFilter.HasValue,
                () => { categoryFilter = null; page = 0; });
            foreach (ChronicleCategory category in System.Enum.GetValues(typeof(ChronicleCategory)))
            {
                var captured = category;
                AddFilterButton(categoryRow, category.ToString().ToUpperInvariant(),
                    categoryFilter == category,
                    () => { categoryFilter = captured; page = 0; });
            }
        }

        void BuildEntries(GameState state, List<ChronicleEntry> entries)
        {
            var text = AddText();
            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.Divider(W));

            if (entries.Count == 0)
            {
                sb.AppendLine(" NOTHING ON RECORD MATCHING THAT FILTER.");
                text.text = sb.ToString();
                return;
            }

            // Newest first — the operator wants recent history by default.
            int start = page * PageSize;
            int shown = 0;
            int lastYear = -1;

            for (int i = entries.Count - 1 - start; i >= 0 && shown < PageSize; i--, shown++)
            {
                var entry = entries[i];

                // Break the record into years so a long save reads as history.
                if (entry.date.year != lastYear)
                {
                    lastYear = entry.date.year;
                    sb.AppendLine();
                    sb.AppendLine($" ── {lastYear} ──");
                }

                var country = state.FindCountry(entry.countryId);
                string who = country != null
                    ? (WorldFactory.FindProfile(country.id)?.mapCode ?? country.id)
                    : "  ";

                sb.AppendLine($"  {entry.date.MonthCode()} {who}  " +
                              $"[{entry.category.ToString().ToUpperInvariant(),-12}] {entry.text}");
            }
            text.text = sb.ToString();
        }

        void BuildPager(int total)
        {
            int pages = (total + PageSize - 1) / PageSize;
            if (pages <= 1) return;

            var row = MakeRow();
            AddButton(row, "◄ NEWER", () => { if (page > 0) page--; Refresh(); });
            AddButton(row, "OLDER ►", () => { if (page < pages - 1) page++; Refresh(); });

            AddText("terminal-text-dim").text = $"  PAGE {page + 1} OF {pages}";
        }

        void AddFilterButton(VisualElement row, string label, bool current, System.Action onClick)
        {
            var button = new Button(() => { onClick(); Refresh(); })
            { text = (current ? "► " : "") + label };
            button.AddToClassList("cmd-button");
            if (current) button.AddToClassList("primary");
            row.Add(button);
        }

        void AddButton(VisualElement row, string label, System.Action onClick)
        {
            var button = new Button(onClick) { text = label };
            button.AddToClassList("cmd-button");
            row.Add(button);
        }
    }
}

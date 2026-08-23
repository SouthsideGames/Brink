using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// The strategic map (GDD §16). Two scales: the world chart, and — on
    /// selecting a nation — that country's own chart, showing as much of its
    /// interior as our collection supports. Foreign detail always comes through
    /// the intelligence layer, never raw.
    /// </summary>
    public class WorldMapView : TerminalView
    {
        public override string Id => "MAP";
        public override string ShortCode => "MAP";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string selectedCountryId;

        /// <summary>False = world chart, true = the selected country's own chart.</summary>
        bool zoomed;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;

            Root.Clear();
            if (selectedCountryId == null || state.FindCountry(selectedCountryId) == null)
                selectedCountryId = state.playerCountryId;

            if (zoomed) BuildCountryChart(state);
            else BuildWorldChart(state);
        }

        // ---------- world scale ----------

        void BuildWorldChart(GameState state)
        {
            AddText("terminal-text-bright").text =
                AsciiChart.BoxHeader($"STRATEGIC SITUATION — {state.date.DisplayString}", W);

            AddFigure().text = AsciiWorldMap.Render(state, selectedCountryId, W, TerminalMetrics.MapRows);
            AddText("terminal-text-dim").text = AsciiWorldMap.Legend;

            BuildSelector(state, zoomOnSelect: false);

            AddText().text = "\n" + AsciiChart.Divider(W) + "\n" +
                             AsciiWorldMap.Describe(state, selectedCountryId);

            var row = MakeRow();
            var open = new Button(() => { zoomed = true; Refresh(); })
            {
                text = $"OPEN {WorldFactory.FindProfile(selectedCountryId)?.mapCode ?? selectedCountryId} ►"
            };
            open.AddToClassList("cmd-button");
            open.AddToClassList("primary");
            row.Add(open);

            BuildChokepoints(state);
        }

        // ---------- country scale ----------

        void BuildCountryChart(GameState state)
        {
            var country = state.FindCountry(selectedCountryId);
            var level = AsciiCountryMap.LevelFor(state, selectedCountryId);

            AddText("terminal-text-bright").text =
                AsciiChart.BoxHeader(country.displayName.ToUpperInvariant(), W);

            var coverage = AddText(level == AsciiCountryMap.DetailLevel.Public
                ? "terminal-text-dim" : "terminal-text-bright");
            coverage.text = $" COVERAGE: {AsciiCountryMap.DescribeLevel(level)}";

            AddFigure().text = AsciiCountryMap.Render(state, selectedCountryId, W, TerminalMetrics.MapRows);
            AddText("terminal-text-dim").text = AsciiCountryMap.Legend;

            var back = MakeRow();
            var button = new Button(() => { zoomed = false; Refresh(); }) { text = "◄ WORLD MAP" };
            button.AddToClassList("cmd-button");
            button.AddToClassList("primary");
            back.Add(button);

            BuildSelector(state, zoomOnSelect: true);

            AddText().text = "\n" + AsciiChart.Divider(W) + "\n" +
                             AsciiWorldMap.Describe(state, selectedCountryId);

            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("INSTALLATIONS", W);
            AddText().text = AsciiCountryMap.DescribeSites(state, selectedCountryId);

            BuildForeignPresence(state);
        }

        /// <summary>
        /// Who else is operating from this country's soil. Worth its own panel:
        /// a partner's bases are the clearest single indicator of whose orbit a
        /// state is actually in, and it is only visible with real collection.
        /// </summary>
        void BuildForeignPresence(GameState state)
        {
            if (AsciiCountryMap.LevelFor(state, selectedCountryId) < AsciiCountryMap.DetailLevel.Detailed)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.BoxHeader("FOREIGN FORCES ON THIS SOIL", W));

            int found = 0;
            foreach (var location in state.locations)
            {
                if (location.ownerId != selectedCountryId) continue;
                if (!location.HasForeignBase) continue;

                found++;
                var op = state.FindCountry(location.foreignOperatorId);
                sb.AppendLine($"  {op?.displayName.ToUpperInvariant(),-18} operates from {location.displayName}");
            }

            if (found == 0)
                sb.AppendLine("  None. No foreign power operates from here by agreement.");

            AddText(found > 0 ? "terminal-text-bright" : "terminal-text-dim").text = sb.ToString();
        }

        // ---------- shared ----------

        void BuildSelector(GameState state, bool zoomOnSelect)
        {
            var row = MakeRow();

            foreach (var profile in WorldFactory.Profiles)
            {
                var country = state.FindCountry(profile.id);
                if (country == null) continue;

                var captured = profile.id;
                bool current = selectedCountryId == profile.id;
                var button = new Button(() =>
                {
                    selectedCountryId = captured;
                    if (zoomOnSelect) zoomed = true;
                    Refresh();
                })
                { text = (current ? "► " : "") + (profile.mapCode ?? profile.id) };
                button.AddToClassList("cmd-button");

                // Standing as colour, so sixteen two-letter codes stop being a
                // wall of identical text. The glyph legend still carries the
                // same reading for anyone the colour does not reach.
                button.AddToClassList(AsciiWorldMap.StandingClass(state, profile.id));
                if (current || country.isPlayer) button.AddToClassList("primary");
                row.Add(button);
            }
        }

        /// <summary>
        /// Chokepoints and contested ground — the geography that actually
        /// changes hands (GDD §16).
        /// </summary>
        void BuildChokepoints(GameState state)
        {
            var text = AddText("terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.BoxHeader("CHOKEPOINTS & CONTESTED GROUND", W));

            bool any = false;
            foreach (var location in state.locations)
            {
                bool contested = location.type == LocationType.Chokepoint
                                 || location.type == LocationType.MountainPass
                                 || location.IsOccupied;
                if (!contested) continue;

                any = true;
                var owner = state.FindCountry(location.ownerId);
                string note = location.IsOccupied
                    ? $"OCCUPIED — originally {state.FindCountry(location.originalOwnerId)?.displayName}"
                    : "";
                sb.AppendLine($"  [{location.TypeCode}] {AsciiChart.Cell(location.displayName, AsciiChart.NameWidth(W, 0.32f))} " +
                              $"{AsciiChart.Cell(owner?.displayName, AsciiChart.NameWidth(W, 0.20f))} {note}");
            }
            if (!any) sb.AppendLine("  NONE.");
            text.text = sb.ToString();
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
        }
    }
}

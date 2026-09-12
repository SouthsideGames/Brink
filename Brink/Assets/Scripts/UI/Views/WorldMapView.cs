using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Strategic world/country map. The world layer now has selectable map modes
    /// so the same geography can answer political, military, trade, intelligence
    /// and bloc questions without creating five separate screens.
    /// </summary>
    public class WorldMapView : TerminalView
    {
        public override string Id => "MAP";
        public override string ShortCode => "MAP";
        static int W => TerminalMetrics.Columns;

        string selectedCountryId;
        bool zoomed;
        WorldMapMode mapMode = WorldMapMode.Political;

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

        void BuildWorldChart(GameState state)
        {
            AddText("terminal-text-bright").text =
                AsciiChart.BoxHeader($"STRATEGIC SITUATION — {state.date.DisplayString}", W);

            BuildModeSelector();
            AddText("terminal-text-dim").text = " " + AsciiMapModes.Summary(state, mapMode);
            AddFigure().text = AsciiMapModes.Render(
                state, selectedCountryId, mapMode, W, TerminalMetrics.MapRows);
            AddText("terminal-text-dim").text = AsciiMapModes.Legend(mapMode);

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

        void BuildModeSelector()
        {
            var row = MakeRow();
            foreach (WorldMapMode mode in System.Enum.GetValues(typeof(WorldMapMode)))
            {
                var captured = mode;
                bool current = mapMode == mode;
                var button = new Button(() => { mapMode = captured; Refresh(); })
                {
                    text = (current ? "► " : "") + mode.ToString().ToUpperInvariant()
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }
        }

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

        void BuildForeignPresence(GameState state)
        {
            if (AsciiCountryMap.LevelFor(state, selectedCountryId) < AsciiCountryMap.DetailLevel.Detailed)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.BoxHeader("FOREIGN FORCES ON THIS SOIL", W));

            int found = 0;
            foreach (var location in state.locations)
            {
                if (location.ownerId != selectedCountryId || !location.HasForeignBase) continue;
                found++;
                var op = state.FindCountry(location.foreignOperatorId);
                sb.AppendLine($"  {op?.displayName.ToUpperInvariant(),-18} operates from {location.displayName}");
            }

            if (found == 0)
                sb.AppendLine("  None. No foreign power operates from here by agreement.");

            AddText(found > 0 ? "terminal-text-bright" : "terminal-text-dim").text = sb.ToString();
        }

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
                button.AddToClassList(AsciiWorldMap.StandingClass(state, profile.id));
                if (current || country.isPlayer) button.AddToClassList("primary");
                row.Add(button);
            }
        }

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
    }
}

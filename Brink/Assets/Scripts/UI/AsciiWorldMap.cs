using System;
using System.Collections.Generic;
using System.Text;
using Brink.Core;
using Brink.Data;

namespace Brink.UI
{
    /// <summary>
    /// The ASCII strategic world map (GDD §4, §16).
    ///
    /// Geography matters for access, projection and dependence, but this is not
    /// a province-painting board — the map shows *countries, routes and
    /// chokepoints*, not tiles. Foreign detail is rendered through the
    /// intelligence layer like everything else: a country you have never
    /// collected against shows its public identity and nothing more.
    /// </summary>
    public static class AsciiWorldMap
    {
        /// <summary>
        /// Taken from <see cref="GeographySystem.MapWidth"/>, never redeclared.
        /// The renderer and the distance model must agree on how wide the world
        /// is; as two independent literals they silently disagreed.
        /// </summary>
        public const int Width = GeographySystem.MapWidth;

        public const int Height = 21;

        // Landmass silhouette. Deliberately impressionistic: a briefing-room
        // chart, not a projection. Rows are Height tall, columns Width wide.
        static readonly string[] Landmass =
        {
            "                                                                              ",
            "      .-~~-.                    .-~~~~~-.        .-~~~~~~~-.                  ",
            "     /      \\_.-~~-.        .-~/         \\~~-..-~         \\.-~-.             ",
            "    |   .          `-.     /  |            |               |     \\            ",
            "    |               .-'   |   `.          .'                `.    |           ",
            "     \\            _/       \\    `-.____.-'                    \\  /            ",
            "      |          |          `-.                          .-~~-'  |            ",
            "      |          |             |                        |        |            ",
            "       \\        /              |         .-~~-.         |       /             ",
            "        |      |               `-.    .-'      `-.     /       |              ",
            "        |      |                  |  |            |   |        |              ",
            "         \\     |                  |  `.          .'   |       /               ",
            "          |    |                   \\   `-.____.-'    /       |                ",
            "          |   /                     |             .-'        |                ",
            "          |  |                      |            |          /                 ",
            "          \\  |                      `-.       .-'          |                  ",
            "           | |                         |     |         .-~~-.                 ",
            "           | |                         |     |        /      \\                ",
            "            \\|                          \\   /        |        |               ",
            "                                         `-'          `-....-'                ",
            "                                                                              "
        };

        /// <summary>
        /// Render the map. Countries appear at their authored coordinates with a
        /// two-letter code; the player's own nation and any selection are marked.
        /// </summary>
        public static string Render(GameState state, string selectedCountryId = null)
            => Render(state, selectedCountryId, TerminalMetrics.Columns, TerminalMetrics.MapRows);

        /// <summary>
        /// Render the map into an arbitrary grid.
        ///
        /// The chart is authored at 78×21 but the panel is whatever the device
        /// gives us, so the silhouette and every marker are sampled into the
        /// target grid rather than assuming a fixed width. On a folding phone's
        /// cover screen that is roughly half the columns and half the rows, and
        /// the map still reads.
        /// </summary>
        public static string Render(GameState state, string selectedCountryId, int columns, int rows)
        {
            columns = Math.Max(28, columns);
            rows = Math.Max(8, rows);

            float scaleX = (float)columns / Width;
            float scaleY = (float)rows / Height;

            var grid = new char[rows][];
            for (int y = 0; y < rows; y++)
            {
                grid[y] = new char[columns];
                int sourceY = Math.Min(Landmass.Length - 1, (int)(y / scaleY));
                string row = sourceY >= 0 ? Landmass[sourceY] : "";
                for (int x = 0; x < columns; x++)
                {
                    int sourceX = (int)(x / scaleX);
                    grid[y][x] = sourceX < row.Length ? row[sourceX] : ' ';
                }
            }

            // Chokepoints first, so a country marker always wins the cell.
            foreach (var location in state.locations)
            {
                if (location.type != LocationType.Chokepoint) continue;
                var owner = WorldFactory.FindProfile(GeographySystem.HostOf(location));
                if (owner == null) continue;
                Plot(grid, Scale(owner.mapX + 3, scaleX), Scale(owner.mapY + 1, scaleY), '#');
            }

            foreach (var profile in WorldFactory.Profiles)
            {
                var country = state.FindCountry(profile.id);
                if (country == null) continue;

                string code = profile.mapCode ?? profile.id.Substring(0, 2);
                bool isPlayer = country.isPlayer;
                bool isSelected = profile.id == selectedCountryId;

                // Marker carries the country's standing toward us at a glance.
                char left = isPlayer ? '[' : isSelected ? '>' : StatusMarker(state, profile.id);
                char right = isPlayer ? ']' : isSelected ? '<' : ' ';

                int x0 = Scale(profile.mapX, scaleX);
                int y0 = Scale(profile.mapY, scaleY);

                Plot(grid, x0, y0, left);
                Plot(grid, x0 + 1, y0, code.Length > 0 ? code[0] : '?');
                Plot(grid, x0 + 2, y0, code.Length > 1 ? code[1] : '?');
                Plot(grid, x0 + 3, y0, right);
            }

            var sb = new StringBuilder();
            for (int y = 0; y < rows; y++)
            {
                sb.Append(grid[y]);
                if (y < rows - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        static int Scale(int value, float scale) => (int)Math.Round(value * scale);

        /// <summary>
        /// A single character summarising how a state stands toward us. Derived
        /// from public diplomatic standing, which is observable (GDD §14).
        /// </summary>
        static char StatusMarker(GameState state, string countryId)
        {
            var status = DiplomacySystem.StatusOf(state, state.playerCountryId, countryId);
            switch (status)
            {
                case RelationshipStatus.Hostile: return '!';
                case RelationshipStatus.Rival: return '~';
                case RelationshipStatus.Ally:
                case RelationshipStatus.StrategicPartner: return '+';
                case RelationshipStatus.Friendly: return '-';
                default: return ' ';
            }
        }

        static void Plot(char[][] grid, int x, int y, char c)
        {
            if (grid == null || y < 0 || y >= grid.Length) return;
            if (x < 0 || x >= grid[y].Length) return;
            grid[y][x] = c;
        }

        public static string Legend =>
            "  [US] our post   +partner   -friendly   ~rival   !hostile   # chokepoint";

        /// <summary>
        /// The USS class carrying a country's standing as colour.
        ///
        /// Colour is a *second* channel here, never the only one — the map keeps
        /// its `! ~ - +` glyphs, so the reading survives any palette and any
        /// colour vision. The hues are orange/blue rather than red/green
        /// precisely because hostile-versus-allied is the one pair you must not
        /// encode red against green.
        /// </summary>
        public static string StandingClass(GameState state, string countryId)
        {
            if (countryId == state.playerCountryId) return "terminal-text-bright";

            switch (DiplomacySystem.StatusOf(state, state.playerCountryId, countryId))
            {
                case RelationshipStatus.Hostile: return "sig-hostile";
                case RelationshipStatus.Rival: return "sig-rival";
                case RelationshipStatus.Ally:
                case RelationshipStatus.StrategicPartner: return "sig-ally";
                case RelationshipStatus.Friendly: return "sig-friendly";
                default: return "sig-neutral";
            }
        }

        /// <summary>
        /// The detail panel for a selected country. Public facts are exact;
        /// capability is an estimate; occupied territory is visible to everyone.
        /// </summary>
        public static string Describe(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return "NO SELECTION.";

            var sb = new StringBuilder();
            sb.AppendLine($" {country.displayName.ToUpperInvariant()}");
            sb.AppendLine($"   GOVERNMENT: {country.government.TypeText}");
            sb.AppendLine($"   LEADER:     {country.government.leader.name} " +
                          $"({country.government.leader.priority.ToString().ToUpperInvariant()})");

            if (country.isPlayer)
            {
                sb.AppendLine($"   MILITARY:   {country.pillars.military,5:F1}   " +
                              $"ECONOMY: {country.pillars.economy,5:F1}");
                sb.AppendLine($"   POSTURE:    {country.military.posture.ToString().ToUpperInvariant()}");
            }
            else
            {
                var status = DiplomacySystem.StatusOf(state, state.playerCountryId, countryId);
                sb.AppendLine($"   STANDING:   {status.ToString().ToUpperInvariant()}");
                sb.AppendLine($"   MILITARY:   {IntelReadout.ForDomain(state, countryId, IntelDomain.Military)}");
                sb.AppendLine($"   ECONOMY:    {IntelReadout.ForDomain(state, countryId, IntelDomain.Economic)}");
            }

            sb.AppendLine($"   MARKET IDX: {country.economy.marketIndex,7:F1}");

            var held = new List<string>();
            var lost = new List<string>();
            foreach (var location in state.locations)
            {
                if (location.ownerId == countryId)
                    held.Add(location.IsOccupied
                        ? $"{location.displayName} (occupied)"
                        : location.displayName);
                else if (location.originalOwnerId == countryId)
                    lost.Add($"{location.displayName} (held by {state.FindCountry(location.ownerId)?.displayName})");
            }

            if (held.Count > 0) sb.AppendLine($"   HOLDS:      {string.Join(", ", held)}");
            if (lost.Count > 0) sb.AppendLine($"   LOST:       {string.Join(", ", lost)}");

            return sb.ToString();
        }
    }
}

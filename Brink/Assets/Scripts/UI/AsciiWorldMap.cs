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

        /// <summary>Glyph for land on the world chart. Overlays treat it as open ground.</summary>
        public const char Land = '░';

        /// <summary>Glyph for our own and the selected state's land.</summary>
        public const char Highlight = '▒';

        /// <summary>
        /// Render the map. Countries appear at their authored coordinates with a
        /// two-letter code; the player's own nation and any selection are marked.
        /// </summary>
        public static string Render(GameState state, string selectedCountryId = null)
            => Render(state, selectedCountryId, TerminalMetrics.Columns, TerminalMetrics.MapRows);

        /// <summary>
        /// Render the map into an arbitrary grid.
        ///
        /// The landmass is the real one (Natural Earth, baked by
        /// `Tools/mapgen`), stippled the way a briefing-room plot is, and resampled
        /// into whatever grid the panel measures. Our own ground and the current
        /// selection are shaded more heavily so the eye finds them first.
        ///
        /// Country markers sit on each capital. On a small grid several capitals
        /// share a cell — Europe is seven characters wide at 104 columns — so
        /// <see cref="MarkerSlots"/> moves a crowded marker to the nearest free
        /// slot in a fixed order. Overlays read the same slots, so a line drawn
        /// to a country always ends on its label.
        /// </summary>
        public static string Render(GameState state, string selectedCountryId, int columns, int rows)
        {
            columns = Math.Max(28, columns);
            rows = Math.Max(8, rows);

            var land = MapAtlas.Resample(MapAtlas.World, columns, rows, 0.25f);
            char own = MapAtlas.WorldLetter(state.playerCountryId);
            char selected = MapAtlas.WorldLetter(selectedCountryId);

            var grid = new char[rows][];
            for (int y = 0; y < rows; y++)
            {
                grid[y] = new char[columns];
                for (int x = 0; x < columns; x++)
                {
                    char cell = land[y][x];
                    grid[y][x] = cell == ' ' ? ' '
                        : (own != '\0' && cell == own) || (selected != '\0' && cell == selected) ? Highlight
                        : Land;
                }
            }

            // Chokepoints first, so a country marker always wins the cell.
            foreach (var location in state.locations)
            {
                if (location.type != LocationType.Chokepoint) continue;
                if (MapAtlas.TryWorldSite(location.id, out float fx, out float fy))
                {
                    Plot(grid, Cell(fx, columns), Cell(fy, rows), '#');
                    continue;
                }
                // A site with no real position (the deliberately generic
                // contested lane) sits just off its host's capital.
                if (MapAtlas.TryWorldAnchor(GeographySystem.HostOf(location), out fx, out fy))
                    Plot(grid, Cell(fx, columns) + 3, Cell(fy, rows) + 1, '#');
            }

            foreach (var pair in MarkerSlots(state, columns, rows))
            {
                var country = state.FindCountry(pair.Key);
                var profile = WorldFactory.FindProfile(pair.Key);
                if (country == null || profile == null) continue;

                string code = profile.mapCode ?? profile.id.Substring(0, 2);
                bool isPlayer = country.isPlayer;
                bool isSelected = profile.id == selectedCountryId;

                // Marker carries the country's standing toward us at a glance.
                char left = isPlayer ? '[' : isSelected ? '>' : StatusMarker(state, profile.id);
                char right = isPlayer ? ']' : isSelected ? '<' : ' ';

                int x0 = pair.Value.x, y0 = pair.Value.y;
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

        const int MarkerWidth = 4;

        /// <summary>
        /// Where each state's four-cell marker starts, for a grid of this size.
        ///
        /// Placed in roster order: a marker takes the slot centred on its capital
        /// if that slot is free, otherwise the nearest free slot, preferring the
        /// same row and nearby columns over moving rows (a row is twice as far
        /// on screen as a column). Deterministic and pure; it depends only on the
        /// grid size and which states exist.
        /// </summary>
        public static Dictionary<string, (int x, int y)> MarkerSlots(GameState state, int columns, int rows)
        {
            var slots = new Dictionary<string, (int x, int y)>();
            var used = new bool[rows, columns];
            int maxX = Math.Max(0, columns - MarkerWidth);

            foreach (var profile in WorldFactory.Profiles)
            {
                if (state.FindCountry(profile.id) == null) continue;

                int ax, ay;
                if (MapAtlas.TryWorldAnchor(profile.id, out float fx, out float fy))
                {
                    ax = Cell(fx, columns) - 1;
                    ay = Cell(fy, rows);
                }
                else
                {
                    ax = Scale(profile.mapX, (float)columns / Width);
                    ay = Scale(profile.mapY, (float)rows / Height);
                }
                ax = Math.Max(0, Math.Min(maxX, ax));
                ay = Math.Max(0, Math.Min(rows - 1, ay));

                bool placed = false;
                for (int radius = 0; radius <= columns + 2 * rows && !placed; radius++)
                for (int dy = 0; dy <= radius / 2 && !placed; dy++)
                {
                    int dx = radius - 2 * dy;
                    foreach (int sy in dy == 0 ? new[] { 0 } : new[] { dy, -dy })
                    foreach (int sx in dx == 0 ? new[] { 0 } : new[] { dx, -dx })
                    {
                        int x = ax + sx, y = ay + sy;
                        if (placed || x < 0 || x > maxX || y < 0 || y >= rows) continue;
                        bool free = true;
                        for (int i = 0; i < MarkerWidth && free; i++) if (used[y, x + i]) free = false;
                        if (!free) continue;
                        for (int i = 0; i < MarkerWidth; i++) used[y, x + i] = true;
                        slots[profile.id] = (x, y);
                        placed = true;
                    }
                }
            }
            return slots;
        }

        static int Scale(int value, float scale) => (int)Math.Round(value * scale);

        static int Cell(float fraction, int size)
            => Math.Max(0, Math.Min(size - 1, (int)(fraction * size)));

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
            "  [US] our post   +partner   -friendly   ~rival   !hostile   # chokepoint   ▒ our ground / selection";

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

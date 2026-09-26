using System;
using System.Collections.Generic;
using System.Text;
using Brink.Core;
using Brink.Data;

namespace Brink.UI
{
    /// <summary>
    /// The country-scale chart — what you see after selecting a nation on the
    /// world map (GDD §14, §16).
    ///
    /// This is the clearest expression of the intelligence layer anywhere in the
    /// game: the *same* country renders completely differently depending on what
    /// you have collected. With nothing, you get a border and a capital, because
    /// that much is public. With a deep network you get its industry, its energy,
    /// its garrisons, and — the thing worth paying for — which foreign powers are
    /// operating from its soil.
    ///
    /// The outline itself is real geography (<see cref="MapAtlas"/>): borders,
    /// coastline and state or provincial lines are public, like any atlas.
    /// Terrain — ranges, deserts, rivers — is drawn only with good coverage.
    ///
    /// Nothing here reads a true value the operator has not earned. Every gated
    /// figure goes through <see cref="IntelligenceSystem"/>.
    /// </summary>
    public static class AsciiCountryMap
    {
        /// <summary>How much of a country's interior our reporting supports showing.</summary>
        public enum DetailLevel
        {
            /// <summary>Public knowledge only: it exists, its capital, its government.</summary>
            Public,

            /// <summary>Major sites are known by name and type, but not their condition.</summary>
            Sites,

            /// <summary>Condition too: garrisons as bands, defences, foreign basing.</summary>
            Detailed,

            /// <summary>Our own country. Everything, exactly.</summary>
            Complete
        }

        /// <summary>
        /// What our collection against this country supports. Military reporting
        /// governs, because that is what tells you about installations — but any
        /// network at all lifts you off the public floor.
        /// </summary>
        public static DetailLevel LevelFor(GameState state, string countryId)
        {
            if (countryId == state.playerCountryId) return DetailLevel.Complete;

            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, countryId, IntelDomain.Military);
            var network = state.FindNetwork(state.playerCountryId, countryId);

            var grade = estimate?.confidence ?? ConfidenceGrade.None;
            float penetration = network != null && !network.compromised ? network.penetration : 0f;

            if (grade >= ConfidenceGrade.High || penetration >= 55f) return DetailLevel.Detailed;
            if (grade >= ConfidenceGrade.Low || penetration >= 20f) return DetailLevel.Sites;
            return DetailLevel.Public;
        }

        public static string DescribeLevel(DetailLevel level)
        {
            switch (level)
            {
                case DetailLevel.Complete: return "OWN TERRITORY — COMPLETE";
                case DetailLevel.Detailed: return "GOOD COVERAGE — SITES AND CONDITION";
                case DetailLevel.Sites: return "PARTIAL COVERAGE — MAJOR SITES ONLY";
                default: return "NO COVERAGE — PUBLIC INFORMATION ONLY";
            }
        }

        /// <summary>
        /// Rows a country chart may use. Taller than the world map's budget: the
        /// chart is the whole point of this screen and the view scrolls, while a
        /// tall country (Argentina, Japan, Vietnam) squeezed into the world map's
        /// 17 rows would be a dozen columns wide.
        /// </summary>
        public static int RowsFor(int columns, int mapRows)
            => Math.Max(mapRows, Math.Min(34, columns / 2));

        /// <summary>
        /// Draw the country's real outline, its state or provincial lines, and
        /// whatever our reporting supports placing inside it.
        ///
        /// The outline, the internal lines and the capital are public at every
        /// coverage level — anyone can buy an atlas. Installations need some
        /// collection; terrain (ranges, deserts, rivers) needs good collection,
        /// because a surveyed interior is what an operation would be planned on.
        /// A country without a baked chart (a secession successor) falls back to
        /// the schematic box, which still places its sites.
        ///
        /// Every row is exactly <paramref name="columns"/> wide; the chart is as
        /// tall as its shape needs within <paramref name="rows"/>.
        /// </summary>
        public static string Render(GameState state, string countryId, int columns, int rows)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return "NO SELECTION.";

            columns = Math.Max(30, Math.Min(columns, 104));
            rows = Math.Max(7, rows);

            if (!MapAtlas.TryGetChart(countryId, out var chart))
                return RenderSchematic(state, countryId, columns, rows);

            var level = LevelFor(state, countryId);

            // Fit the chart's own aspect into the grid, centred.
            float k = Math.Min((float)columns / chart.Width, (float)rows / chart.Height);
            int width = Math.Max(4, Math.Min(columns, (int)Math.Round(chart.Width * k)));
            int height = Math.Max(3, Math.Min(rows, (int)Math.Round(chart.Height * k)));
            int left = (columns - width) / 2;

            var regions = MapAtlas.Resample(chart.Regions, width, height, 0.3f);
            var grid = new char[height][];
            for (int y = 0; y < height; y++)
            {
                grid[y] = new char[columns];
                for (int x = 0; x < columns; x++) grid[y][x] = ' ';
            }

            DrawLines(grid, regions, left);

            if (level >= DetailLevel.Detailed)
            {
                var terrain = MapAtlas.ResampleTerrain(chart.Terrain, width, height);
                for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    if (regions[y][x] == ' ' || grid[y][left + x] != ' ') continue;
                    // Ranges and deserts are stippled on alternate cells so they
                    // read as texture beneath the sites; a river is a line and
                    // is drawn solid.
                    char kind = terrain[y][x];
                    if (kind != 'r' && (x + y) % 2 != 0) continue;
                    char glyph = TerrainGlyph(kind);
                    if (glyph != ' ') grid[y][left + x] = glyph;
                }
            }

            DrawLabels(grid, regions, left, chart);

            var taken = new HashSet<int>();
            foreach (var location in Sites(state, countryId))
            {
                if (level == DetailLevel.Public && location.type != LocationType.Capital) continue;
                if (!chart.TrySite(location.id, out float fx, out float fy)) continue;

                int x = left + Math.Min(width - 1, (int)(fx * width));
                int y = Math.Min(height - 1, (int)(fy * height));
                if (fx < 0f || fy < 0f || fx > 1f || fy > 1f) continue;

                char marker = MarkerFor(location.type);
                if (level >= DetailLevel.Detailed && location.HasForeignBase) marker = '*';
                if (location.IsOccupied) marker = '!';

                if (!FreeCellNear(grid, taken, ref x, ref y)) continue;
                Plot(grid, x, y, marker);
                taken.Add(y * columns + x);

                if (location.type == LocationType.Capital)
                {
                    string code = WorldFactory.FindProfile(countryId)?.mapCode ?? "";
                    string tag = "=" + code;
                    // The tag reads left-to-right from the marker when there is
                    // room, and from the other side on an east coast.
                    int start = x + tag.Length < columns ? x + 1 : x - tag.Length;
                    for (int i = 0; i < tag.Length; i++)
                    {
                        Plot(grid, start + i, y, tag[i]);
                        taken.Add(y * columns + start + i);
                    }
                }
            }

            var sb = new StringBuilder();
            for (int y = 0; y < height; y++)
            {
                sb.Append(grid[y]);
                if (y < height - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Coastline, land border and internal lines, drawn from the resampled
        /// region grid. A cell is on the outline when a neighbour is outside the
        /// country; its glyph follows which sides are open, so a coast running
        /// diagonally reads as a slope rather than a staircase. Internal lines are
        /// drawn on one side only — the cell west of, or above, a change of
        /// region — or every state line would be two characters thick.
        /// </summary>
        static void DrawLines(char[][] grid, char[][] regions, int left)
        {
            int height = regions.Length;
            int width = height > 0 ? regions[0].Length : 0;
            char At(int x, int y) => x < 0 || y < 0 || x >= width || y >= height ? ' ' : regions[y][x];

            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                char here = regions[y][x];
                if (here == ' ') continue;

                bool n = At(x, y - 1) == ' ', s = At(x, y + 1) == ' ';
                bool w = At(x - 1, y) == ' ', e = At(x + 1, y) == ' ';
                int open = (n ? 1 : 0) + (s ? 1 : 0) + (w ? 1 : 0) + (e ? 1 : 0);

                char glyph = ' ';
                if (open >= 3) glyph = 'o';
                else if (open == 2 && ((n && e) || (s && w))) glyph = '\\';
                else if (open == 2 && ((n && w) || (s && e))) glyph = '/';
                else if (w || e) glyph = '|';
                else if (n || s) glyph = '-';
                else
                {
                    char east = At(x + 1, y), south = At(x, y + 1);
                    if (east != here) glyph = '|';
                    else if (south != here) glyph = '_';
                }
                grid[y][left + x] = glyph;
            }
        }

        /// <summary>
        /// Two- or three-letter state codes, lower case so they can never be read
        /// as a site marker. Placed only where the region has a clear run of
        /// interior cells around the label — a crowded seaboard simply goes
        /// unlabelled rather than printing codes over each other's lines.
        /// </summary>
        static void DrawLabels(char[][] grid, char[][] regions, int left, MapAtlas.Chart chart)
        {
            int height = regions.Length;
            int width = height > 0 ? regions[0].Length : 0;
            var done = new HashSet<char>();

            for (int y = 1; y < height - 1; y++)
            for (int x = 1; x < width - 1; x++)
            {
                char region = regions[y][x];
                if (region == ' ' || done.Contains(region)) continue;
                string label = chart.LabelFor(region);
                if (label.Length == 0) continue;

                // Need the label plus one blank interior cell either side, all in
                // this region, and blank rows of the same region above and below
                // the label's own cells so it sits inside the shape.
                int span = label.Length + 2;
                if (x + span > width) continue;
                bool fits = true;
                for (int i = 0; i < span && fits; i++)
                {
                    int cx = x + i;
                    if (regions[y][cx] != region || grid[y][left + cx] != ' ') fits = false;
                    else if (i > 0 && i <= label.Length &&
                             (regions[y - 1][cx] != region || regions[y + 1][cx] != region)) fits = false;
                }
                if (!fits) continue;

                for (int i = 0; i < label.Length; i++) grid[y][left + x + 1 + i] = label[i];
                done.Add(region);
            }
        }

        static char TerrainGlyph(char terrain)
        {
            switch (terrain)
            {
                case 'm': return '^';
                case 'd': return '.';
                case 'r': return '~';
                default: return ' ';
            }
        }

        /// <summary>
        /// Two sites can resolve to one cell on a small chart (a capital and its
        /// port). The later one moves to the nearest cell no marker has claimed,
        /// in a fixed order, so the same world always draws the same chart.
        /// </summary>
        static bool FreeCellNear(char[][] grid, HashSet<int> taken, ref int x, ref int y)
        {
            int columns = grid[0].Length;
            var offsets = new[] { (0, 0), (1, 0), (-1, 0), (0, 1), (0, -1), (2, 0), (-2, 0),
                                  (1, 1), (-1, -1), (1, -1), (-1, 1), (0, 2), (0, -2) };
            foreach (var (dx, dy) in offsets)
            {
                int cx = x + dx, cy = y + dy;
                if (cy < 0 || cy >= grid.Length || cx < 0 || cx >= columns) continue;
                if (taken.Contains(cy * columns + cx)) continue;
                x = cx;
                y = cy;
                return true;
            }
            return false;
        }

        /// <summary>
        /// The pre-atlas chart: a box with sites placed by id hash. Kept for
        /// states with no authored geography — a secession successor is carved out
        /// of its parent at runtime and has no outline of its own to bake.
        /// </summary>
        static string RenderSchematic(GameState state, string countryId, int columns, int rows)
        {
            var level = LevelFor(state, countryId);
            var grid = new char[rows][];
            for (int y = 0; y < rows; y++)
            {
                grid[y] = new char[columns];
                for (int x = 0; x < columns; x++) grid[y][x] = ' ';
            }

            DrawBorder(grid, columns, rows);

            foreach (var location in Sites(state, countryId))
            {
                if (level == DetailLevel.Public && location.type != LocationType.Capital) continue;

                int x = 3 + Math.Abs(Hash.Of(location.id)) % Math.Max(1, columns - 8);
                int y = 1 + Math.Abs(Hash.Of(location.id + "y")) % Math.Max(1, rows - 2);

                char marker = MarkerFor(location.type);
                if (level >= DetailLevel.Detailed && location.HasForeignBase) marker = '*';
                if (location.IsOccupied) marker = '!';

                Plot(grid, x, y, marker);
                string code = WorldFactory.FindProfile(countryId)?.mapCode ?? "";
                if (location.type == LocationType.Capital)
                {
                    Plot(grid, x + 1, y, '=');
                    Plot(grid, x + 2, y, code.Length > 0 ? code[0] : '?');
                    Plot(grid, x + 3, y, code.Length > 1 ? code[1] : '?');
                }
            }

            var sb = new StringBuilder();
            for (int y = 0; y < rows; y++)
            {
                sb.Append(grid[y]);
                if (y < rows - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        static void DrawBorder(char[][] grid, int columns, int rows)
        {
            for (int x = 0; x < columns; x++)
            {
                Plot(grid, x, 0, '─');
                Plot(grid, x, rows - 1, '─');
            }
            for (int y = 0; y < rows; y++)
            {
                Plot(grid, 0, y, '│');
                Plot(grid, columns - 1, y, '│');
            }
            Plot(grid, 0, 0, '╭');
            Plot(grid, columns - 1, 0, '╮');
            Plot(grid, 0, rows - 1, '╰');
            Plot(grid, columns - 1, rows - 1, '╯');
        }

        static char MarkerFor(LocationType type)
        {
            switch (type)
            {
                case LocationType.Capital: return '@';
                case LocationType.Port: return 'P';
                case LocationType.Airbase: return 'A';
                case LocationType.IndustrialCenter: return 'I';
                case LocationType.EnergyRegion: return 'E';
                case LocationType.MaterialsRegion: return 'M';
                case LocationType.MountainPass: return 'V';
                case LocationType.Chokepoint: return '#';
                default: return '?';
            }
        }

        public static string Legend =>
            "  @capital  Pport  Aairbase  Iindustry  Eenergy  Mmaterials  Vpass  #chokepoint\n" +
            "  *foreign force present   !occupied   ^mountains  .desert  ~river/lake   tx state";

        /// <summary>
        /// What the chart can say about the ground itself: the share of the
        /// country that is mountain, desert and water, and the named ranges,
        /// deserts and rivers behind those figures. Gated like the terrain layer —
        /// an unsurveyed interior is reported as exactly that, with what would
        /// change it.
        /// </summary>
        public static string DescribeTerrain(GameState state, string countryId)
        {
            if (!MapAtlas.TryGetChart(countryId, out var chart))
                return "  TERRAIN: NOT CHARTED — this state's ground has no survey on file.";

            var level = LevelFor(state, countryId);
            if (level < DetailLevel.Detailed)
                return "  TERRAIN: NOT SURVEYED — good coverage would chart its mountains,\n" +
                       "  deserts and rivers.";

            int land = 0, mountain = 0, desert = 0, water = 0;
            var regions = chart.Regions;
            var terrain = chart.Terrain;
            for (int y = 0; y < chart.Height; y++)
            for (int x = 0; x < chart.Width; x++)
            {
                if (regions[y][x] == ' ') continue;
                land++;
                switch (terrain[y][x])
                {
                    case 'm': mountain++; break;
                    case 'd': desert++; break;
                    case 'r': water++; break;
                }
            }

            int Share(int n) => land == 0 ? 0 : (int)Math.Round(100f * n / land);
            var sb = new StringBuilder();
            sb.AppendLine($"  TERRAIN   ^ MOUNTAIN {Share(mountain)}%   . DESERT {Share(desert)}%   ~ WATER {Share(water)}%");
            if (chart.Mountains.Length > 0) sb.AppendLine("  RANGES:  " + string.Join(", ", chart.Mountains));
            if (chart.Deserts.Length > 0) sb.AppendLine("  DESERTS: " + string.Join(", ", chart.Deserts));
            if (chart.Rivers.Length > 0) sb.AppendLine("  RIVERS:  " + string.Join(", ", chart.Rivers));
            return sb.ToString();
        }

        static IEnumerable<StrategicLocation> Sites(GameState state, string countryId)
        {
            foreach (var location in state.locations)
            {
                // A country's sites are the ground that is *theirs* — including
                // anything currently taken from them, which is exactly the fact a
                // player most wants to see on this screen.
                if (location.originalOwnerId == countryId || location.ownerId == countryId)
                    yield return location;
            }
        }

        /// <summary>
        /// The written readout beneath the chart. Each line is gated: a site's
        /// name needs some collection, its garrison needs good collection, and a
        /// foreign force on its soil needs better still.
        /// </summary>
        public static string DescribeSites(GameState state, string countryId)
        {
            var level = LevelFor(state, countryId);
            var sb = new StringBuilder();

            if (level == DetailLevel.Public)
            {
                sb.AppendLine("  Our reporting does not extend inside this country. Establish a");
                sb.AppendLine("  network and collect on it to see what it actually contains.");
                return sb.ToString();
            }

            int shown = 0;
            foreach (var location in Sites(state, countryId))
            {
                if (level == DetailLevel.Public && location.type != LocationType.Capital) continue;
                shown++;

                sb.Append($"  {location.TypeCode} {location.displayName}");

                if (location.IsOccupied)
                {
                    var holder = state.FindCountry(location.ownerId);
                    sb.Append($"  — HELD BY {holder?.displayName.ToUpperInvariant()}");
                }

                sb.AppendLine();

                if (level < DetailLevel.Detailed) continue;

                if (IntelligenceSystem.TryEstimateGarrison(state, state.playerCountryId, location,
                        out float low, out float high, out var confidence))
                {
                    sb.AppendLine(level == DetailLevel.Complete
                        ? $"       GARRISON {low:F0}   DEFENCE {location.defenseValue:F0}"
                        : $"       GARRISON ~{low:F0}-{high:F0} ({confidence.ToString().ToUpperInvariant()})");
                }

                if (location.HasForeignBase)
                {
                    var operatorState = state.FindCountry(location.foreignOperatorId);
                    sb.AppendLine($"       FOREIGN FORCE PRESENT: {operatorState?.displayName.ToUpperInvariant()}");
                }
            }

            if (shown == 0) sb.AppendLine("  No installations of consequence reported.");

            if (level == DetailLevel.Sites)
            {
                sb.AppendLine();
                sb.AppendLine("  Sites are known; their condition is not. Deeper penetration would");
                sb.AppendLine("  report garrisons and any foreign forces operating from here.");
            }

            return sb.ToString();
        }

        static void Plot(char[][] grid, int x, int y, char c)
        {
            if (grid == null || y < 0 || y >= grid.Length) return;
            if (x < 0 || x >= grid[y].Length) return;
            grid[y][x] = c;
        }
    }
}

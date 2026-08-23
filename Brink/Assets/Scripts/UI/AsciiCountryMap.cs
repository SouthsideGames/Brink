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
        /// Draw the country as a bordered chart with its known installations
        /// placed inside it. Positions are derived from each location's id, so a
        /// site sits in the same place every time you look at it.
        /// </summary>
        public static string Render(GameState state, string countryId, int columns, int rows)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return "NO SELECTION.";

            columns = Math.Max(30, Math.Min(columns, 100));
            rows = Math.Max(7, rows);

            var level = LevelFor(state, countryId);
            var grid = new char[rows][];
            for (int y = 0; y < rows; y++)
            {
                grid[y] = new char[columns];
                for (int x = 0; x < columns; x++) grid[y][x] = ' ';
            }

            DrawBorder(grid, columns, rows);

            // Interior sites. At Public level the capital alone is plotted:
            // everyone knows where a country's seat of government is.
            foreach (var location in Sites(state, countryId))
            {
                if (level == DetailLevel.Public && location.type != LocationType.Capital) continue;

                int x = 3 + Math.Abs(Hash.Of(location.id)) % Math.Max(1, columns - 8);
                int y = 1 + Math.Abs(Hash.Of(location.id + "y")) % Math.Max(1, rows - 2);

                char marker = MarkerFor(location.type);

                // A foreign presence overrides the type marker: it is the single
                // most important thing about a site, once you can see it.
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
                case LocationType.Chokepoint: return '#';
                default: return '^';
            }
        }

        public static string Legend =>
            "  @capital  Pport  Aairbase  Iindustry  Eenergy  ^pass  #chokepoint\n" +
            "  *foreign force present   !occupied";

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

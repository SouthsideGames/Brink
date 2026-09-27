using System;
using System.Collections.Generic;

namespace Brink.UI
{
    /// <summary>
    /// Real geography for the terminal charts (spec 09 §7).
    ///
    /// The shapes are baked by `Tools/mapgen/generate_map_atlas.py` from Natural
    /// Earth into <c>MapAtlas.Generated.cs</c>: one character raster per country
    /// (a letter per state or province, space outside), a matching terrain
    /// raster (m mountain, d desert, r river or lake), and each authored site's
    /// position as a fraction of its chart. The world raster carries a letter per
    /// authored country and '.' for everyone else's land.
    ///
    /// **Presentation only.** Distance, reach and theatres still come from the
    /// authored `mapX`/`mapY` in <c>WorldFactory</c>; no simulation system reads
    /// this file, so redrawing a coastline can never move a balance figure.
    ///
    /// Every raster is stored at roughly twice the widest panel and resampled
    /// into whatever grid the panel measures, so a folded cover screen and an
    /// unfolded tablet draw the same country at different sizes rather than
    /// different countries.
    /// </summary>
    public static partial class MapAtlas
    {
        /// <summary>One country's baked chart.</summary>
        public sealed class Chart
        {
            public readonly int Width;
            public readonly int Height;
            public readonly string[] Labels;
            public readonly string[] RegionNames;
            public readonly string[] Mountains;
            public readonly string[] Deserts;
            public readonly string[] Rivers;
            readonly string[] regionRows;
            readonly string[] terrainRows;
            readonly Dictionary<string, (float x, float y)> sites;
            char[][] regions;
            char[][] terrain;

            public Chart(int width, int height, string[] regionRows, string[] terrainRows,
                string[] labels, string[] regionNames, string[] mountains, string[] deserts,
                string[] rivers, Dictionary<string, (float, float)> sites)
            {
                Width = width;
                Height = height;
                this.regionRows = regionRows;
                this.terrainRows = terrainRows;
                Labels = labels;
                RegionNames = regionNames;
                Mountains = mountains;
                Deserts = deserts;
                Rivers = rivers;
                this.sites = sites;
            }

            /// <summary>Region raster at native resolution: ' ' outside, else a region letter.</summary>
            public char[][] Regions => regions ?? (regions = Decode(regionRows, Width));

            /// <summary>Terrain raster at native resolution: ' ', 'm', 'd' or 'r'.</summary>
            public char[][] Terrain => terrain ?? (terrain = Decode(terrainRows, Width));

            /// <summary>Where a site sits on this chart, as fractions of width and height.</summary>
            public bool TrySite(string siteId, out float x, out float y)
            {
                x = y = 0f;
                if (siteId == null || !sites.TryGetValue(siteId, out var at)) return false;
                x = at.x;
                y = at.y;
                return true;
            }

            /// <summary>The label a region letter carries, or empty.</summary>
            public string LabelFor(char region)
            {
                int index = RegionAlphabet.IndexOf(region);
                return index >= 0 && index < Labels.Length ? Labels[index] : string.Empty;
            }
        }

        static char[][] world;

        /// <summary>The baked chart for an authored country, if there is one.</summary>
        public static bool TryGetChart(string countryId, out Chart chart)
        {
            chart = null;
            return countryId != null && Charts.TryGetValue(countryId, out chart);
        }

        /// <summary>
        /// The world raster at native resolution: ' ' sea, '.' land of a state
        /// outside the roster, otherwise the <see cref="WorldLetter"/> of an
        /// authored country.
        /// </summary>
        public static char[][] World => world ?? (world = Decode(WorldRows, WorldWidth));

        /// <summary>The letter an authored country's land carries on the world raster.</summary>
        public static char WorldLetter(string countryId)
        {
            int index = Array.IndexOf(CountryOrder, countryId);
            return index >= 0 && index < RegionAlphabet.Length ? RegionAlphabet[index] : '\0';
        }

        /// <summary>A country's capital on the world raster, as fractions of width and height.</summary>
        public static bool TryWorldAnchor(string countryId, out float x, out float y)
        {
            x = y = 0f;
            if (countryId == null || !WorldAnchors.TryGetValue(countryId, out var at)) return false;
            x = at.x;
            y = at.y;
            return true;
        }

        /// <summary>An authored site on the world raster, as fractions of width and height.</summary>
        public static bool TryWorldSite(string siteId, out float x, out float y)
        {
            x = y = 0f;
            if (siteId == null || !WorldSites.TryGetValue(siteId, out var at)) return false;
            x = at.x;
            y = at.y;
            return true;
        }

        /// <summary>
        /// Resample a raster into a target grid. Each target cell reads the source
        /// block it covers and keeps its most common non-blank value, provided
        /// non-blank cells cover at least <paramref name="threshold"/> of the
        /// block. Deterministic, and pure: the source is never modified.
        /// </summary>
        public static char[][] Resample(char[][] source, int width, int height, float threshold)
        {
            int sourceHeight = source.Length;
            int sourceWidth = sourceHeight > 0 ? source[0].Length : 0;
            var result = new char[height][];
            var counts = new Dictionary<char, int>();

            for (int y = 0; y < height; y++)
            {
                result[y] = new char[width];
                int y0 = y * sourceHeight / height;
                int y1 = Math.Max(y0 + 1, (y + 1) * sourceHeight / height);
                for (int x = 0; x < width; x++)
                {
                    int x0 = x * sourceWidth / width;
                    int x1 = Math.Max(x0 + 1, (x + 1) * sourceWidth / width);
                    counts.Clear();
                    int filled = 0, total = 0;
                    for (int sy = y0; sy < y1 && sy < sourceHeight; sy++)
                    for (int sx = x0; sx < x1 && sx < sourceWidth; sx++)
                    {
                        total++;
                        char c = source[sy][sx];
                        if (c == ' ') continue;
                        filled++;
                        counts.TryGetValue(c, out int n);
                        counts[c] = n + 1;
                    }

                    char pick = ' ';
                    if (total > 0 && filled >= threshold * total)
                    {
                        int best = -1;
                        foreach (var pair in counts)
                            if (pair.Value > best || (pair.Value == best && pair.Key < pick))
                            {
                                best = pair.Value;
                                pick = pair.Key;
                            }
                    }
                    result[y][x] = pick;
                }
            }
            return result;
        }

        /// <summary>
        /// Terrain resampling favours what is thin: a river covers a sliver of any
        /// block it crosses, so it wins at a lower share than a mountain range or a
        /// desert, which must cover a good part of the cell to be drawn at all.
        /// </summary>
        public static char[][] ResampleTerrain(char[][] source, int width, int height)
        {
            int sourceHeight = source.Length;
            int sourceWidth = sourceHeight > 0 ? source[0].Length : 0;
            var result = new char[height][];
            for (int y = 0; y < height; y++)
            {
                result[y] = new char[width];
                int y0 = y * sourceHeight / height;
                int y1 = Math.Max(y0 + 1, (y + 1) * sourceHeight / height);
                for (int x = 0; x < width; x++)
                {
                    int x0 = x * sourceWidth / width;
                    int x1 = Math.Max(x0 + 1, (x + 1) * sourceWidth / width);
                    int total = 0, river = 0, mountain = 0, desert = 0;
                    for (int sy = y0; sy < y1 && sy < sourceHeight; sy++)
                    for (int sx = x0; sx < x1 && sx < sourceWidth; sx++)
                    {
                        total++;
                        switch (source[sy][sx])
                        {
                            case 'r': river++; break;
                            case 'm': mountain++; break;
                            case 'd': desert++; break;
                        }
                    }

                    char pick = ' ';
                    if (total > 0)
                    {
                        if (river >= Math.Max(1f, 0.3f * total)) pick = 'r';
                        else if (mountain >= 0.45f * total) pick = 'm';
                        else if (desert >= 0.45f * total) pick = 'd';
                    }
                    result[y][x] = pick;
                }
            }
            return result;
        }

        /// <summary>Decode the generator's run-length rows: an optional decimal count, then the cell.</summary>
        static char[][] Decode(string[] rows, int width)
        {
            var result = new char[rows.Length][];
            for (int y = 0; y < rows.Length; y++)
            {
                var cells = new char[width];
                for (int i = 0; i < width; i++) cells[i] = ' ';
                int x = 0, count = 0;
                foreach (char c in rows[y])
                {
                    if (c >= '0' && c <= '9')
                    {
                        count = count * 10 + (c - '0');
                        continue;
                    }
                    int run = count == 0 ? 1 : count;
                    for (int i = 0; i < run && x < width; i++) cells[x++] = c;
                    count = 0;
                }
                result[y] = cells;
            }
            return result;
        }
    }
}

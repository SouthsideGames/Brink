using System;
using Brink.Data;

namespace Brink.UI
{
    /// <summary>
    /// Compact stateful art for the five command pillars. These are not charts in
    /// disguise: each pillar gets a distinct visual grammar, while the changing
    /// marks are derived only from the player's own state.
    /// </summary>
    public static class AsciiPillarArt
    {
        public static string Render(GameState state, Pillar pillar, int width)
        {
            var country = state?.PlayerCountry;
            if (country == null) return "";
            width = Math.Max(32, Math.Min(100, width));
            var c = new AsciiCanvas(width, 5);

            switch (pillar)
            {
                case Pillar.Military: Military(c, country); break;
                case Pillar.Economy: Economy(c, country); break;
                case Pillar.Intelligence: Intelligence(c, country); break;
                case Pillar.Diplomacy: Diplomacy(c, country); break;
                default: Government(c, country); break;
            }
            return c.ToString();
        }

        static void Military(AsciiCanvas c, CountryState country)
        {
            int mid = c.Width / 2;
            c.Text(1, 0, "FORCE STATUS");
            c.Line(1, 2, c.Width - 2, 2, '─');
            c.Text(Math.Max(1, mid - 6), 1, "<==[◆]==>");
            int readiness = (int)Math.Round(country.military.ground.readiness / 100f * Math.Max(4, c.Width - 24));
            c.Text(1, 3, "READY ");
            c.Text(7, 3, new string('█', Math.Max(0, readiness)));
            c.Text(Math.Max(1, c.Width - 18), 4,
                country.military.posture.ToString().ToUpperInvariant());
        }

        static void Economy(AsciiCanvas c, CountryState country)
        {
            c.Text(1, 0, "NATIONAL MARKET");
            int baseY = 4;
            int columns = Math.Max(8, c.Width - 20);

            // Each column is a sector, not noise. The previous height was
            // `1 + |x * 17 + marketIndex| % 3` sampled at x = 1, 4, 7, ...;
            // since every sampled x is congruent to 1 mod 3 and 17 * 3 is
            // divisible by 3, `x * 17 % 3` was invariant, so every bar always
            // shared one height and the skyline could never vary. Reading the
            // sectors the country actually has makes an uneven economy look
            // uneven, and is still derived only from our own state.
            var sectors = country.economy.sectors;
            float low = 0f, span = 0f;
            if (sectors != null && sectors.Count > 0)
            {
                low = sectors[0].output; float high = low;
                foreach (var sector in sectors)
                {
                    if (sector.output < low) low = sector.output;
                    if (sector.output > high) high = sector.output;
                }
                span = high - low;
            }

            for (int x = 1, i = 0; x < columns; x += 3, i++)
            {
                int h = sectors == null || sectors.Count == 0
                    ? 1
                    : SectorHeight(sectors[i % sectors.Count].output, low, span);
                for (int y = 0; y < h; y++) c.Plot(x, baseY - y, '█');
            }
            c.Text(Math.Max(1, c.Width - 18), 1, $"IDX {country.economy.marketIndex:F0}");
            c.Text(Math.Max(1, c.Width - 18), 2, $"GDP {country.economy.gdp:F0}");
            c.Text(Math.Max(1, c.Width - 18), 3,
                country.economy.growthRate >= 0 ? "TREND  /" : "TREND  \\");
        }

        /// <summary>
        /// A sector's capacity as a skyline height, scaled against the spread
        /// this country actually has rather than the whole 0..100 range. A
        /// healthy economy sits in a narrow high band — the seeded world opens
        /// at 72..93 — so absolute thresholds put every sector in one bucket
        /// and flattened the skyline just as thoroughly as the arithmetic bug
        /// did. A genuinely level economy still reads level.
        /// </summary>
        static int SectorHeight(float output, float low, float span)
        {
            if (span < 4f) return 2;
            int h = 1 + (int)Math.Round((output - low) / span * 3f);
            return Math.Max(1, Math.Min(4, h));
        }

        static void Intelligence(AsciiCanvas c, CountryState country)
        {
            int mid = c.Width / 2;
            c.Text(1, 0, "SIGNAL PICTURE");
            c.Text(Math.Max(1, mid - 8), 1, " .-----------. ");
            c.Text(Math.Max(1, mid - 8), 2, "(     <●>     )");
            c.Text(Math.Max(1, mid - 8), 3, " '-----------' ");
            int strength = (int)Math.Round(country.pillars.intelligence);
            c.Text(1, 4, $"SERVICE {strength:000}   COUNTERINTEL {country.counterIntel.counterIntelligence:000}");
        }

        static void Diplomacy(AsciiCanvas c, CountryState country)
        {
            int mid = c.Width / 2;
            c.Text(1, 0, "DIPLOMATIC NETWORK");
            c.Plot(mid, 2, '◆');
            int reach = Math.Max(5, Math.Min(mid - 3, (int)(country.pillars.diplomacy / 8f)));
            c.Line(mid - reach, 1, mid, 2, '·');
            c.Line(mid, 2, mid + reach, 1, '·');
            c.Line(mid - reach, 3, mid, 2, '·');
            c.Line(mid, 2, mid + reach, 3, '·');
            c.Plot(mid - reach, 1, '○'); c.Plot(mid + reach, 1, '○');
            c.Plot(mid - reach, 3, '○'); c.Plot(mid + reach, 3, '○');
            c.Text(1, 4, $"REACH {country.pillars.diplomacy:F0}/100");
        }

        static void Government(AsciiCanvas c, CountryState country)
        {
            c.Text(1, 0, "STATE HOUSE");
            int mid = c.Width / 2;
            c.Text(Math.Max(1, mid - 9), 1, "      /\\      ");
            c.Text(Math.Max(1, mid - 9), 2, "  ___/  \\___  ");
            c.Text(Math.Max(1, mid - 9), 3, " | || || || | ");
            c.Text(Math.Max(1, mid - 9), 4, "_|_||_||_||_|_");

            // The readout shares the title row rather than row 4. On a narrow
            // panel the facade is centred and its foundation reaches within a
            // few columns of both edges, so labels drawn onto row 4 overwrote
            // it and fused into `STAB 64|_||_||_||APP 48`. Row 0 carries only
            // the eleven-character heading, so there is room at every width the
            // 32-column floor allows.
            string readout = $"STAB {country.stability:F0}   APP {country.governmentApproval:F0}";
            c.Text(Math.Max(13, c.Width - 1 - readout.Length), 0, readout);
        }
    }
}
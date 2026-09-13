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
            for (int x = 1; x < columns; x += 3)
            {
                int h = 1 + (Math.Abs((x * 17 + (int)country.economy.marketIndex)) % 3);
                for (int y = 0; y < h; y++) c.Plot(x, baseY - y, '█');
            }
            c.Text(Math.Max(1, c.Width - 18), 1, $"IDX {country.economy.marketIndex:F0}");
            c.Text(Math.Max(1, c.Width - 18), 2, $"GDP {country.economy.gdp:F0}");
            c.Text(Math.Max(1, c.Width - 18), 3,
                country.economy.growthRate >= 0 ? "TREND  /" : "TREND  \\");
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
            c.Text(1, 4, $"STAB {country.stability:F0}");
            c.Text(Math.Max(1, c.Width - 14), 4, $"APP {country.governmentApproval:F0}");
        }
    }
}
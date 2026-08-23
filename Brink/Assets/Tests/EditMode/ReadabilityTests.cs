using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Readability guards for a game that is entirely text.
    ///
    /// These read the real stylesheet rather than a copy of its values, so a
    /// colour tweak that quietly drops a readout below a legible contrast fails
    /// here instead of on a phone in daylight.
    ///
    /// Thresholds are WCAG 2.1 contrast ratios. AA for body text is 4.5:1 and
    /// AAA is 7:1; non-text UI (borders, rules) needs 3:1.
    /// </summary>
    public class ReadabilityTests
    {
        const string StylesheetPath = "Assets/Resources/UI/TerminalShell.uss";

        static string Stylesheet()
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), StylesheetPath);
            Assert.IsTrue(File.Exists(path), $"Stylesheet not found at {path}");
            return File.ReadAllText(path);
        }

        // ---------- WCAG ----------

        static double Channel(double v)
        {
            v /= 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        static double Luminance(int r, int g, int b)
            => 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);

        static double Contrast((int r, int g, int b) fg, (int r, int g, int b) bg)
        {
            double a = Luminance(fg.r, fg.g, fg.b);
            double c = Luminance(bg.r, bg.g, bg.b);
            if (a < c) (a, c) = (c, a);
            return (a + 0.05) / (c + 0.05);
        }

        /// <summary>Read the colour of a named property from a named USS rule.</summary>
        static (int r, int g, int b) ColourIn(string uss, string selector, string property)
        {
            // Anchored on the left so ".terminal-root" cannot match inside
            // ".theme-green.terminal-root" and read the wrong palette's colours.
            var block = Regex.Match(uss,
                @"(?<![-a-zA-Z.])" + Regex.Escape(selector) + @"\s*\{(.*?)\}",
                RegexOptions.Singleline);
            Assert.IsTrue(block.Success, $"No rule '{selector}' in the stylesheet.");

            // The leading guard matters: without it, asking for `color` matches
            // inside `background-color`, so a rule is compared against its own
            // background and every contrast reads as a perfect 1.00:1. That bug
            // sat in this helper until the suite was first actually executed.
            var colour = Regex.Match(block.Groups[1].Value,
                @"(?<![-a-zA-Z])" + Regex.Escape(property)
                + @"\s*:\s*rgb\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(\d+)\s*\)");
            Assert.IsTrue(colour.Success, $"No '{property}' colour in rule '{selector}'.");

            return (int.Parse(colour.Groups[1].Value),
                    int.Parse(colour.Groups[2].Value),
                    int.Parse(colour.Groups[3].Value));
        }

        // ---------- contrast ----------

        [Test]
        public void EveryTextColour_IsComfortablyReadableOnTheTerminalBackground()
        {
            string uss = Stylesheet();
            var background = ColourIn(uss, ".terminal-root", "background-color");

            var textRules = new Dictionary<string, string>
            {
                { ".terminal-root", "color" },
                { ".terminal-text", "color" },
                { ".terminal-text-dim", "color" },
                { ".terminal-text-bright", "color" },
                { ".classification", "color" },
                { ".status-stat", "color" },
                { ".log-line-warning", "color" },
                { ".log-line-error", "color" }
            };

            foreach (var rule in textRules)
            {
                double ratio = Contrast(ColourIn(uss, rule.Key, rule.Value), background);
                Assert.GreaterOrEqual(ratio, 7.0,
                    $"{rule.Key} sits at {ratio:F2}:1 against the terminal background. "
                    + "This game is nothing but text — every readout should clear WCAG AAA (7:1). "
                    + "The dim colour was the offender at 4.74:1.");
            }
        }

        [Test]
        public void DimTextIsQuieter_ButStillFullyLegible()
        {
            string uss = Stylesheet();
            var background = ColourIn(uss, ".terminal-root", "background-color");

            double body = Contrast(ColourIn(uss, ".terminal-text", "color"), background);
            double dim = Contrast(ColourIn(uss, ".terminal-text-dim", "color"), background);
            double bright = Contrast(ColourIn(uss, ".terminal-text-bright", "color"), background);

            Assert.Less(dim, body, "Dim text must read as secondary.");
            Assert.Greater(bright, body, "Bright text must read as emphasis.");
            Assert.GreaterOrEqual(dim, 7.0, "…but secondary is not the same as hard to read.");
        }

        [Test]
        public void BordersAndRules_MeetTheNonTextMinimum()
        {
            string uss = Stylesheet();
            var background = ColourIn(uss, ".terminal-root", "background-color");

            double ratio = Contrast(ColourIn(uss, ".view-scroll", "border-color"), background);
            Assert.GreaterOrEqual(ratio, 3.0,
                $"Panel borders at {ratio:F2}:1 — WCAG requires 3:1 for non-text UI.");
        }

        [Test]
        public void NavigationLabels_AreReadableOnTheirOwnBackground()
        {
            string uss = Stylesheet();
            double ratio = Contrast(
                ColourIn(uss, ".nav-button", "color"),
                ColourIn(uss, ".nav-button", "background-color"));

            Assert.GreaterOrEqual(ratio, 4.5,
                $"Nav labels at {ratio:F2}:1 against their own button fill.");
        }

        // ---------- themes ----------

        /// <summary>
        /// A theme is a choice about comfort, never about legibility. Every text
        /// colour in every palette must clear AAA against that palette's own
        /// background — otherwise picking amber quietly costs the reader.
        /// </summary>
        [Test]
        public void EveryTheme_KeepsEveryTextColourAtAAA()
        {
            string uss = Stylesheet();

            foreach (string theme in new[] { "theme-green", "theme-amber", "theme-soft", "theme-signal" })
            {
                var background = ColourIn(uss, $".{theme}.terminal-root", "background-color");

                foreach (string rule in new[]
                         {
                             ".terminal-text", ".terminal-text-dim", ".terminal-text-bright",
                             ".classification", ".status-stat"
                         })
                {
                    double ratio = Contrast(ColourIn(uss, $".{theme} {rule}", "color"), background);
                    Assert.GreaterOrEqual(ratio, 7.0,
                        $"{theme} {rule} is {ratio:F2}:1 — below AAA. Choosing a palette must "
                        + "never cost legibility.");
                }

                double border = Contrast(ColourIn(uss, $".{theme} .view-scroll", "border-color"), background);
                Assert.GreaterOrEqual(border, 3.0, $"{theme} borders at {border:F2}:1.");
            }
        }

        [Test]
        public void TheLowGlareTheme_ActuallyReducesGlare()
        {
            string uss = Stylesheet();

            double green = Contrast(
                ColourIn(uss, ".theme-green .terminal-text", "color"),
                ColourIn(uss, ".theme-green.terminal-root", "background-color"));
            double soft = Contrast(
                ColourIn(uss, ".theme-soft .terminal-text", "color"),
                ColourIn(uss, ".theme-soft.terminal-root", "background-color"));

            Assert.Less(soft, green,
                "Low glare should narrow the luminance gap — that is the whole point — "
                + "while still clearing AAA, which the test above enforces.");
        }

        [Test]
        public void EveryThemeClass_IsDefinedInTheStylesheet()
        {
            string uss = Stylesheet();
            foreach (string themeClass in Brink.UI.DisplaySettings.AllThemeClasses)
                StringAssert.Contains($".{themeClass}", uss,
                    $"DisplaySettings offers '{themeClass}' but no rule defines it.");
        }

        /// <summary>
        /// Semantic colour has to survive every palette, or picking amber
        /// silently degrades the map's readability.
        /// </summary>
        [Test]
        public void SemanticColours_ClearAAAAgainstEveryPalette()
        {
            string uss = Stylesheet();
            // `sig-advice` is not a standing colour — it marks what our own
            // cabinet recommends — but it is drawn from the same shared set and
            // has to clear the same bar on every palette.
            string[] signals =
            {
                ".sig-hostile", ".sig-rival", ".sig-neutral", ".sig-friendly", ".sig-ally",
                ".sig-advice"
            };

            foreach (string theme in new[] { "theme-green", "theme-amber", "theme-soft", "theme-signal" })
            {
                var background = ColourIn(uss, $".{theme}.terminal-root", "background-color");

                foreach (string signal in signals)
                {
                    double ratio = Contrast(ColourIn(uss, signal, "color"), background);
                    Assert.GreaterOrEqual(ratio, 7.0,
                        $"{signal} is {ratio:F2}:1 on {theme}. One semantic set has to work "
                        + "on every base, or the palette choice costs the reader.");
                }
            }
        }

        /// <summary>
        /// Red-green deficiency affects roughly one man in twelve, and
        /// hostile-versus-allied is exactly the pair you must not encode that
        /// way. The safeguard is that colour is never the only channel — the map
        /// keeps its glyphs — but the hues should be safe regardless.
        /// </summary>
        [Test]
        public void HostileAndAllied_AreNotDistinguishedByRedAgainstGreen()
        {
            string uss = Stylesheet();
            var hostile = ColourIn(uss, ".sig-hostile", "color");
            var ally = ColourIn(uss, ".sig-ally", "color");

            // Beyond hue, the two must differ in luminance, which is the channel
            // that survives every form of colour blindness.
            double hostileLum = Luminance(hostile.r, hostile.g, hostile.b);
            double allyLum = Luminance(ally.r, ally.g, ally.b);
            double separation = Math.Abs(hostileLum - allyLum) / Math.Max(hostileLum, allyLum);

            Assert.Greater(separation, 0.15,
                "Hostile and allied must be told apart by brightness as well as hue.");

            Assert.Greater(ally.b, ally.r,
                "Allied should sit on the blue side, not the green — red/green is the "
                + "one pair to avoid for this distinction.");
        }

        [Test]
        public void EverySemanticClassTheMapAsksFor_Exists()
        {
            string uss = Stylesheet();
            foreach (string cls in new[]
                     { "sig-hostile", "sig-rival", "sig-neutral", "sig-friendly", "sig-ally" })
                StringAssert.Contains($".{cls}", uss,
                    $"AsciiWorldMap.StandingClass can return '{cls}' but no rule defines it.");
        }

        // ---------- preferences ----------

        [Test]
        public void LargerTextMeansFewerColumns_TheScreenDoesNotGrow()
        {
            float medium = Brink.UI.TerminalScale.ScaleFor(2400, 1080, 1f);
            float larger = Brink.UI.TerminalScale.ScaleFor(2400, 1080, 1.45f);

            Assert.Greater(larger, medium, "A larger text preference must actually enlarge text.");

            float mediumColumns = 2400 / (Brink.UI.TerminalScale.BaseCharPx * medium);
            float largerColumns = 2400 / (Brink.UI.TerminalScale.BaseCharPx * larger);
            Assert.Less(largerColumns, mediumColumns,
                "…and the columns have to give, because the screen does not grow.");
        }

        [Test]
        public void EveryTextSize_StaysWithinASaneScale()
        {
            foreach (Brink.UI.TextSize size in Enum.GetValues(typeof(Brink.UI.TextSize)))
            {
                float multiplier =
                    size == Brink.UI.TextSize.Small ? 0.82f :
                    size == Brink.UI.TextSize.Large ? 1.20f :
                    size == Brink.UI.TextSize.Larger ? 1.45f : 1f;

                foreach (var (w, h) in new[] { (2316, 904), (2176, 1812), (1280, 720), (2560, 1600) })
                {
                    float scale = Brink.UI.TerminalScale.ScaleFor(w, h, multiplier);
                    float columns = w / (Brink.UI.TerminalScale.BaseCharPx * scale);

                    Assert.GreaterOrEqual(columns, 30f,
                        $"{size} on {w}x{h} leaves only {columns:F0} columns — tables stop fitting.");
                    Assert.Greater(Brink.UI.TerminalScale.BaseFontPx * scale, 18f,
                        $"{size} on {w}x{h} renders text too small to read.");
                }
            }
        }

        [Test]
        public void CompactSpacing_RemovesLeadingAndComfortableRestoresIt()
        {
            var original = Brink.UI.DisplaySettings.Density;
            try
            {
                Brink.UI.DisplaySettings.Density = Brink.UI.TextDensity.Compact;
                Assert.AreEqual(0f, Brink.UI.DisplaySettings.ParagraphSpacing,
                    "Compact is the trade a short screen makes: rows instead of leading.");

                Brink.UI.DisplaySettings.Density = Brink.UI.TextDensity.Comfortable;
                Assert.Greater(Brink.UI.DisplaySettings.ParagraphSpacing, 0f);
            }
            finally
            {
                Brink.UI.DisplaySettings.Density = original;
            }
        }

        [Test]
        public void PreferencesPersist_AndSurviveAReset()
        {
            var originalSize = Brink.UI.DisplaySettings.Size;
            var originalTheme = Brink.UI.DisplaySettings.Theme;
            try
            {
                Brink.UI.DisplaySettings.Size = Brink.UI.TextSize.Larger;
                Brink.UI.DisplaySettings.Theme = Brink.UI.TerminalTheme.Amber;

                Assert.AreEqual(Brink.UI.TextSize.Larger, Brink.UI.DisplaySettings.Size);
                Assert.AreEqual("theme-amber", Brink.UI.DisplaySettings.ThemeClass);

                Brink.UI.DisplaySettings.ResetToDefaults();
                Assert.AreEqual(Brink.UI.TextSize.Small, Brink.UI.DisplaySettings.Size);
                Assert.AreEqual(Brink.UI.TerminalTheme.Amber, Brink.UI.DisplaySettings.Theme);
            }
            finally
            {
                Brink.UI.DisplaySettings.Size = originalSize;
                Brink.UI.DisplaySettings.Theme = originalTheme;
            }
        }

        // ---------- nothing may run off the edge ----------

        /// <summary>
        /// The bug reported from the device: the box rules fitted the panel, but
        /// the prose written inside them did not. Under `white-space: pre`
        /// nothing wraps, so a long notification body simply ran off the right.
        /// </summary>
        [Test]
        public void LongProse_IsWrappedToTheTerminalWidth()
        {
            const string line =
                "  [FLASH   ] CRISIS PENDING — An unresolved crisis demands a decision "
                + "before the month can end, and the staff are waiting on you.";

            foreach (int width in new[] { 40, 58, 70, 88 })
            {
                string wrapped = Brink.UI.AsciiChart.WrapBlock(line, width);
                foreach (var row in wrapped.Split('\n'))
                    Assert.LessOrEqual(row.Length, width,
                        $"A {row.Length}-character row survived wrapping to {width} — "
                        + "that is the text running off the screen.");
            }
        }

        [Test]
        public void Wrapping_KeepsEveryWordAndBreaksOnSpaces()
        {
            const string line = "Response force deployed to the frontier and the incident is contained.";
            string wrapped = Brink.UI.AsciiChart.WrapBlock(line, 30);

            // Nothing may be lost: a briefing that drops its second half is worse
            // than one that takes two lines.
            string rejoined = string.Join(" ", wrapped.Split('\n'));
            foreach (var word in line.Split(' '))
                StringAssert.Contains(word, rejoined, $"'{word}' was lost in wrapping.");

            Assert.Greater(wrapped.Split('\n').Length, 1, "That line has to wrap at 30 columns.");
        }

        [Test]
        public void Wrapping_LeavesShortLinesAndFiguresUntouched()
        {
            const string figure = "  ├──────────┤\n  │ 12  34   │\n  └──────────┘";
            Assert.AreEqual(figure, Brink.UI.AsciiChart.WrapBlock(figure, 60),
                "Anything already inside the width must pass through byte-identical, "
                + "or ASCII alignment drifts on every refresh.");
        }

        [Test]
        public void Wrapping_HandlesAWordLongerThanTheLine()
        {
            string wrapped = Brink.UI.AsciiChart.WrapBlock(new string('X', 120), 40);
            foreach (var row in wrapped.Split('\n'))
                Assert.LessOrEqual(row.Length, 40, "An unbreakable word must still be cut to fit.");
        }

        [Test]
        public void Wrapping_IsIdempotent()
        {
            const string line = "  A rather long advisory that will certainly need to be wrapped twice over.";
            string once = Brink.UI.AsciiChart.WrapBlock(line, 34);
            Assert.AreEqual(once, Brink.UI.AsciiChart.WrapBlock(once, 34),
                "Views re-apply this on every refresh; a second pass must change nothing.");
        }

        // ---------- defaults ----------

        [Test]
        public void TheDefaults_AreWhatWasChosenOnDevice()
        {
            Brink.UI.DisplaySettings.ResetToDefaults();

            Assert.AreEqual(Brink.UI.TerminalTheme.Amber, Brink.UI.DisplaySettings.Theme,
                "Amber was preferred on the Fold.");
            Assert.AreEqual(Brink.UI.TextSize.Small, Brink.UI.DisplaySettings.Size,
                "Small read as the natural size on device.");
        }

        [Test]
        public void ThereIsAStepBelowTheDefaultSize()
        {
            Assert.Less((int)Brink.UI.TextSize.Smallest, (int)Brink.UI.TextSize.Small,
                "With Small as the default, a reader who wants more density needs "
                + "somewhere to go.");
        }

        // ---------- typography ----------

        [Test]
        public void ReadoutTextHasLeading_AndFiguresDoNot()
        {
            string uss = Stylesheet();

            var textBlock = Regex.Match(uss, @"\.terminal-text\s*\{(.*?)\}", RegexOptions.Singleline);
            StringAssert.Contains("-unity-paragraph-spacing", textBlock.Groups[1].Value,
                "Dense monospace with no leading is the most tiring thing to read in a "
                + "text game. UI Toolkit has no line-height, but paragraph spacing "
                + "applies after every newline, which is what these blocks are made of.");

            var figureBlock = Regex.Match(uss, @"\.terminal-figure\s*\{(.*?)\}", RegexOptions.Singleline);
            Assert.IsTrue(figureBlock.Success, "ASCII figures need a rule that opts out of leading.");
            StringAssert.Contains("-unity-paragraph-spacing: 0", figureBlock.Groups[1].Value,
                "A gap between rows breaks the vertical strokes that make a map a map.");
        }

        [Test]
        public void NoTextIsSetSmallerThanTheShellCanScaleComfortably()
        {
            string uss = Stylesheet();

            foreach (Match match in Regex.Matches(uss, @"font-size:\s*(\d+)px"))
            {
                int size = int.Parse(match.Groups[1].Value);
                Assert.GreaterOrEqual(size, 11,
                    $"A {size}px rule exists. Base sizes below 11px leave too little room "
                    + "once a small screen scales them down.");
            }
        }

        [Test]
        public void TableCells_TruncateRatherThanOverflow()
        {
            Assert.AreEqual("SAUDI ARAB…", Brink.UI.AsciiChart.Cell("SAUDI ARABIA", 11),
                "A long name must be cut to fit, not pushed off the screen — fixed "
                + "paddings inside a correctly-sized box were still overflowing.");
            Assert.AreEqual("USA       ", Brink.UI.AsciiChart.Cell("USA", 10));
            Assert.AreEqual(10, Brink.UI.AsciiChart.Cell(null, 10).Length);

            foreach (int width in new[] { 40, 55, 70, 90 })
                Assert.AreEqual(width, Brink.UI.AsciiChart.Cell("A rather long installation name", width).Length,
                    "Cell must always return exactly the width it was asked for.");
        }

        [Test]
        public void NameColumns_ScaleWithTheTerminalAndStayBounded()
        {
            int narrow = Brink.UI.AsciiChart.NameWidth(42);
            int wide = Brink.UI.AsciiChart.NameWidth(90);

            Assert.Less(narrow, wide, "A wider terminal should give names more room.");
            Assert.GreaterOrEqual(narrow, 10, "…but a name column must never collapse to nothing.");
            Assert.LessOrEqual(wide, 30, "…nor sprawl across a tablet.");
        }

        /// <summary>
        /// The measure — characters per line. Typographic guidance puts
        /// comfortable prose at 45–75 characters; much beyond that and the eye
        /// loses its place returning to the next line.
        /// </summary>
        [Test]
        public void TheLineLength_StaysInAComfortableRange()
        {
            var screens = new (string name, int w, int h)[]
            {
                ("fold cover", 2316, 904),
                ("fold open", 2176, 1812),
                ("phone", 2400, 1080),
                ("small phone", 1280, 720),
                ("tablet", 2560, 1600)
            };

            foreach (var screen in screens)
            {
                int columns = Brink.UI.TerminalScale.TargetColumns(screen.w, screen.h);
                Assert.LessOrEqual(columns, 80,
                    $"{screen.name} targets {columns} characters per line — too wide to "
                    + "track comfortably back to the next line.");
                Assert.GreaterOrEqual(columns, 40,
                    $"{screen.name} targets only {columns} characters — the tables and "
                    + "bars stop fitting.");
            }
        }
    }
}

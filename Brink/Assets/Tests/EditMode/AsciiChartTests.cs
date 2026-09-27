using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AsciiChartTests
    {
        [Test]
        public void SpritesClipAndLayerWithoutErasingTransparentCells()
        {
            var c = new AsciiCanvas(4, 3, '.');
            c.Stamp(-1, -1, "ignored", "A BCD", " E F", null);
            Assert.AreEqual(".BCD\nE.F.\n....", c.ToString());
            c.Stamp(3, 2, "XY", "ZZ");
            Assert.AreEqual('X', c.At(3, 2));
            c.Stamp(0, 0, (string[])null);
            Assert.AreEqual(4, c.Width); Assert.AreEqual(3, c.Height);
        }

        [Test]
        public void Bar_FillsProportionally()
        {
            Assert.AreEqual("█████░░░░░", AsciiChart.Bar(50, 100, 10));
            Assert.AreEqual("██████████", AsciiChart.Bar(100, 100, 10));
            Assert.AreEqual("░░░░░░░░░░", AsciiChart.Bar(0, 100, 10));
        }

        [Test]
        public void Bar_ClampsOutOfRangeValues()
        {
            Assert.AreEqual("██████████", AsciiChart.Bar(250, 100, 10));
            Assert.AreEqual("░░░░░░░░░░", AsciiChart.Bar(-40, 100, 10));
        }

        [Test]
        public void Bar_AlwaysExactWidth()
        {
            for (int v = 0; v <= 100; v += 7)
                Assert.AreEqual(12, AsciiChart.Bar(v, 100, 12).Length, $"width mismatch at value {v}");
        }

        [Test]
        public void Bar_HandlesDegenerateInputs()
        {
            Assert.AreEqual(string.Empty, AsciiChart.Bar(50, 100, 0));
            Assert.AreEqual(10, AsciiChart.Bar(5, 0, 10).Length);
        }

        [Test]
        public void LabeledBar_PadsAndTruncatesLabel()
        {
            string line = AsciiChart.LabeledBar("MIL", 50, 100, 10, 10);
            StringAssert.StartsWith("MIL       ", line);
            string truncated = AsciiChart.LabeledBar("VERYLONGNAME", 50, 100, 6, 10);
            StringAssert.StartsWith("VERYLO ", truncated);
        }

        [Test]
        public void BoxHeader_HasExactWidthAndCorners()
        {
            string header = AsciiChart.BoxHeader("Test", 30);
            Assert.AreEqual(30, header.Length);
            StringAssert.StartsWith("┌─ TEST ", header);
            StringAssert.EndsWith("┐", header);
        }

        [Test]
        public void BoxHeader_LongTitleDoesNotUnderflow()
        {
            string header = AsciiChart.BoxHeader("An extremely long section title", 10);
            StringAssert.EndsWith("┐", header);
            StringAssert.Contains("AN EXTREMELY LONG", header);
        }

        [Test]
        public void Row_AlignsToWidth()
        {
            string row = AsciiChart.Row("TREASURY", "1240", 30);
            Assert.AreEqual(30, row.Length);
            StringAssert.StartsWith("TREASURY ", row);
            StringAssert.EndsWith(" 1240", row);
            StringAssert.Contains("...", row);
        }

        [Test]
        public void Sparkline_MapsRangeToLevels()
        {
            string line = AsciiChart.Sparkline(new float[] { 0, 50, 100 }, 100);
            Assert.AreEqual(3, line.Length);
            Assert.AreEqual(' ', line[0]);
            Assert.AreEqual('█', line[2]);
        }

        [Test]
        public void Canvas_ClipsAndKeepsExactDimensions()
        {
            var canvas = new AsciiCanvas(12, 5);
            canvas.Text(9, 2, "ABCDE");
            canvas.Plot(-1, 0, 'X');
            var rows = canvas.ToString().Split('\n');
            Assert.AreEqual(5, rows.Length);
            foreach (var row in rows) Assert.AreEqual(12, row.Length);
            StringAssert.EndsWith("ABC", rows[2]);
        }

        [Test]
        public void Canvas_LineCanRespectExistingArt()
        {
            var canvas = new AsciiCanvas(12, 5);
            canvas.Text(5, 2, "US");
            canvas.Line(0, 2, 11, 2, '·', overwrite: false);
            Assert.AreEqual('U', canvas.At(5, 2));
            Assert.AreEqual('S', canvas.At(6, 2));
            Assert.AreEqual('·', canvas.At(4, 2));
            Assert.AreEqual('·', canvas.At(7, 2));
        }

        [Test]
        public void Canvas_FromTextPreservesExistingFigure()
        {
            var canvas = AsciiCanvas.FromText("ABC\nD E");
            Assert.AreEqual(3, canvas.Width);
            Assert.AreEqual(2, canvas.Height);
            Assert.AreEqual('B', canvas.At(1, 0));
            Assert.AreEqual(' ', canvas.At(1, 1));
        }

        [Test]
        public void Canvas_OverlayLinesCrossTheWorldChartsLand()
        {
            var canvas = new AsciiCanvas(6, 1, AsciiWorldMap.Land);
            canvas.Plot(3, 0, AsciiWorldMap.Highlight);
            canvas.Line(0, 0, 5, 0, ':', overwrite: false);
            Assert.AreEqual("::::::", canvas.ToString(),
                "Stippled land is ground, not information: a line over a continent must stay visible.");
        }
    }

    public class BreakpointTests
    {
        [Test]
        public void FromColumns_MapsSizeClasses()
        {
            Assert.AreEqual(SizeClass.Compact, Breakpoints.FromColumns(40));
            Assert.AreEqual(SizeClass.Compact, Breakpoints.FromColumns(Breakpoints.MediumMinColumns - 1));
            Assert.AreEqual(SizeClass.Medium, Breakpoints.FromColumns(Breakpoints.MediumMinColumns));
            Assert.AreEqual(SizeClass.Medium, Breakpoints.FromColumns(Breakpoints.LargeMinColumns - 1));
            Assert.AreEqual(SizeClass.Large, Breakpoints.FromColumns(Breakpoints.LargeMinColumns));
            Assert.AreEqual(SizeClass.Large, Breakpoints.FromColumns(120));
        }

        /// <summary>
        /// The Z Fold report: the class used to be read from the content width,
        /// which the class itself changes by moving the rail, so a width near a
        /// threshold flipped between layouts and left text wrapped for the other
        /// one. Judged from the shared width, each class must leave itself enough
        /// columns, and the next class up must not have fitted.
        /// </summary>
        [Test]
        public void FromAvailableWidth_ChoosesAClassThatFitsItsOwnRail()
        {
            const float charWidth = 7.8f;
            foreach (bool shortScreen in new[] { false, true })
            for (float width = 250f; width <= 1400f; width += 3f)
            {
                var size = Breakpoints.FromAvailableWidth(width, charWidth, shortScreen);
                int Columns(SizeClass c) => (int)((width - Breakpoints.RailCost(c, shortScreen)) / charWidth) - 1;

                if (size != SizeClass.Compact)
                    Assert.GreaterOrEqual(Breakpoints.FromColumns(Columns(size)), size,
                        $"{size} at {width}pt leaves only {Columns(size)} columns once its own rail is placed");
                if (size != SizeClass.Large)
                {
                    var bigger = size + 1;
                    Assert.Less(Breakpoints.FromColumns(Columns(bigger)), bigger,
                        $"{bigger} would have fitted at {width}pt");
                }
            }
        }

        [Test]
        public void FromAvailableWidth_IsTheSameWhicheverLayoutMeasuredIt()
        {
            // Width shared by rail and content is what each layout's measurement
            // adds back up to, so the decision cannot depend on the current class.
            const float charWidth = 7.8f;
            for (float total = 400f; total <= 900f; total += 5f)
            {
                var answers = new System.Collections.Generic.HashSet<SizeClass>();
                foreach (SizeClass current in System.Enum.GetValues(typeof(SizeClass)))
                {
                    float content = total - Breakpoints.RailCost(current, false);
                    answers.Add(Breakpoints.FromAvailableWidth(content + Breakpoints.RailCost(current, false), charWidth, false));
                }
                Assert.AreEqual(1, answers.Count, $"the size class at {total}pt depends on the layout that measured it");
            }
        }

        [Test]
        public void RailCosts_MatchTheStylesheet()
        {
            string path = System.IO.Path.Combine(System.IO.Directory.GetCurrentDirectory(),
                "Assets/Resources/UI/TerminalShell.uss");
            Assert.IsTrue(System.IO.File.Exists(path), $"Stylesheet not found at {path}");
            string uss = System.IO.File.ReadAllText(path);

            float Px(string selector, string property)
            {
                int start = uss.IndexOf(selector + " {", System.StringComparison.Ordinal);
                Assert.GreaterOrEqual(start, 0, $"no rule for {selector}");
                int end = uss.IndexOf('}', start);
                var match = System.Text.RegularExpressions.Regex.Match(
                    uss.Substring(start, end - start), @"(?m)^\s*" + property + @":\s*([0-9.]+)px");
                Assert.IsTrue(match.Success, $"{selector} has no {property}");
                return float.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            }

            float baseMargin = Px(".nav-rail", "margin-right");
            Assert.AreEqual(Breakpoints.LargeRailPt, Px(".nav-rail", "width") + baseMargin);
            Assert.AreEqual(Breakpoints.MediumRailPt, Px(".bp-medium .nav-rail", "width") + baseMargin);
            Assert.AreEqual(Breakpoints.ShortRailPt,
                Px(".bp-short .nav-rail", "width") + Px(".bp-short .nav-rail", "margin-right"));
        }

        [Test]
        public void ToUssClass_IsStable()
        {
            Assert.AreEqual("bp-compact", Breakpoints.ToUssClass(SizeClass.Compact));
            Assert.AreEqual("bp-medium", Breakpoints.ToUssClass(SizeClass.Medium));
            Assert.AreEqual("bp-large", Breakpoints.ToUssClass(SizeClass.Large));
        }
    }
}

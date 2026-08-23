using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AsciiChartTests
    {
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
            Assert.AreEqual(10, AsciiChart.Bar(5, 0, 10).Length); // max<=0 treated as 1
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
    }

    public class BreakpointTests
    {
        /// <summary>
        /// Size classes are measured in characters across, not panel points —
        /// points became meaningless once the panel scale started being derived
        /// to hit a column target.
        /// </summary>
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

        [Test]
        public void ToUssClass_IsStable()
        {
            Assert.AreEqual("bp-compact", Breakpoints.ToUssClass(SizeClass.Compact));
            Assert.AreEqual("bp-medium", Breakpoints.ToUssClass(SizeClass.Medium));
            Assert.AreEqual("bp-large", Breakpoints.ToUssClass(SizeClass.Large));
        }
    }
}

using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class GameDateTests
    {
        [Test]
        public void NextMonth_AdvancesWithinYear()
        {
            var date = new GameDate(1984, 3).NextMonth();
            Assert.AreEqual(1984, date.year);
            Assert.AreEqual(4, date.month);
        }

        [Test]
        public void NextMonth_RollsOverYearEnd()
        {
            var date = new GameDate(1984, 12).NextMonth();
            Assert.AreEqual(1985, date.year);
            Assert.AreEqual(1, date.month);
        }

        [Test]
        public void IsYearEnd_OnlyInDecember()
        {
            Assert.IsTrue(new GameDate(1984, 12).IsYearEnd);
            Assert.IsFalse(new GameDate(1984, 11).IsYearEnd);
        }

        [Test]
        public void MonthsSince_CountsAcrossYears()
        {
            var start = new GameDate(1984, 1);
            var later = new GameDate(1986, 3);
            Assert.AreEqual(26, later.MonthsSince(start));
        }

        [Test]
        public void DisplayString_UsesTerminalFormat()
        {
            Assert.AreEqual("MAR 1984", new GameDate(1984, 3).DisplayString);
        }

        [Test]
        public void InvalidMonth_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GameDate(1984, 13));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new GameDate(1984, 0));
        }
    }
}

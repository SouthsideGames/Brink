using System;

namespace Brink.Data
{
    /// <summary>
    /// In-game calendar. One normal turn = one month (GDD §6).
    /// The world is fictional; the epoch year is arbitrary flavor.
    /// </summary>
    [Serializable]
    public struct GameDate : IEquatable<GameDate>, IComparable<GameDate>
    {
        public int year;
        public int month; // 1..12

        public static readonly string[] MonthCodes =
        {
            "JAN", "FEB", "MAR", "APR", "MAY", "JUN",
            "JUL", "AUG", "SEP", "OCT", "NOV", "DEC"
        };

        public GameDate(int year, int month)
        {
            if (month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), month, "Month must be 1..12.");
            this.year = year;
            this.month = month;
        }

        public GameDate NextMonth()
        {
            return month == 12 ? new GameDate(year + 1, 1) : new GameDate(year, month + 1);
        }

        /// <summary>Total months elapsed since another date. Positive when this is later.</summary>
        public int MonthsSince(GameDate other) => (year - other.year) * 12 + (month - other.month);

        public bool IsYearEnd => month == 12;

        /// <summary>Cold terminal display, e.g. "MAR 1984".</summary>
        public string DisplayString => $"{MonthCodes[month - 1]} {year}";

        /// <summary>Sortable key, e.g. "1984-03", for chronicle/archive ordering.</summary>
        public string SortKey => $"{year:D4}-{month:D2}";

        /// <summary>Three-letter month code, e.g. "MAR".</summary>
        public string MonthCode() => MonthCodes[month - 1];

        public override string ToString() => DisplayString;

        public bool Equals(GameDate other) => year == other.year && month == other.month;
        public override bool Equals(object obj) => obj is GameDate other && Equals(other);
        public override int GetHashCode() => year * 12 + month;
        public int CompareTo(GameDate other) => (year * 12 + month).CompareTo(other.year * 12 + other.month);
    }
}

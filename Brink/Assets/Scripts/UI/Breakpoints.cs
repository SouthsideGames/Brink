namespace Brink.UI
{
    /// <summary>Responsive size classes (GDD §2: phones, large phones, foldables, tablets).</summary>
    public enum SizeClass
    {
        Compact, // phones
        Medium,  // large phones / foldables
        Large    // tablets / desktop
    }

    /// <summary>
    /// Size classes, measured in **characters across** rather than panel points.
    ///
    /// Points were the wrong unit once the panel scale started being derived to
    /// hit a column target: panel width in points is then roughly constant
    /// (400–850) whatever the device, so `Large` at 1050pt was unreachable and
    /// `Medium` almost so. Both branches were dead, taking the full nav labels
    /// and the tall map with them.
    ///
    /// Columns are the honest measure of how much a screen can show, which is
    /// what GDD §4 actually asks about: a larger display shows more information,
    /// never larger UI.
    /// </summary>
    public static class Breakpoints
    {
        public const int MediumMinColumns = 64;
        public const int LargeMinColumns = 82;

        public static SizeClass FromColumns(int columns)
        {
            if (columns >= LargeMinColumns) return SizeClass.Large;
            if (columns >= MediumMinColumns) return SizeClass.Medium;
            return SizeClass.Compact;
        }

        public static string ToUssClass(SizeClass size)
        {
            switch (size)
            {
                case SizeClass.Large: return "bp-large";
                case SizeClass.Medium: return "bp-medium";
                default: return "bp-compact";
            }
        }
    }
}

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

        /// <summary>
        /// Points the navigation rail takes beside the content, per layout.
        /// Keep in step with `.nav-rail`, `.bp-medium .nav-rail` and
        /// `.bp-short .nav-rail` in TerminalShell.uss (width + margin-right);
        /// `BreakpointTests` parses the stylesheet and fails if they drift.
        /// Compact puts the rail above the content, so it costs no width.
        /// </summary>
        public const float LargeRailPt = 174f;
        public const float MediumRailPt = 78f;
        public const float ShortRailPt = 56f;

        public static float RailCost(SizeClass size, bool shortScreen)
        {
            if (shortScreen) return ShortRailPt;
            switch (size)
            {
                case SizeClass.Large: return LargeRailPt;
                case SizeClass.Medium: return MediumRailPt;
                default: return 0f;
            }
        }

        /// <summary>
        /// The size class for a screen, from the width shared by the rail and
        /// the content rather than from the content alone.
        ///
        /// **Deciding from content columns was a feedback loop.** The class
        /// decides where the rail goes, and the rail decides how wide the content
        /// is: an unfolded Z Fold measured about 70 columns with the rail on top
        /// (so Medium), Medium moved a 78pt rail to the side (so about 60 columns,
        /// so Compact), and the layout flipped between the two. Whichever
        /// measurement landed last won, so the screen could show the side rail
        /// with text wrapped for the full width — sentences cut off at the right
        /// edge until switching panels rebuilt it.
        ///
        /// Here each class is judged by the columns *it* would leave the content,
        /// so the answer does not depend on the answer and a width change settles
        /// in one step. The largest class that still meets its own threshold wins.
        /// </summary>
        public static SizeClass FromAvailableWidth(float availablePt, float charWidthPt, bool shortScreen)
        {
            if (charWidthPt <= 0.01f || availablePt <= 0f) return SizeClass.Compact;
            foreach (var size in new[] { SizeClass.Large, SizeClass.Medium })
            {
                int columns = (int)((availablePt - RailCost(size, shortScreen)) / charWidthPt) - 1;
                if (FromColumns(columns) >= size) return size;
            }
            return SizeClass.Compact;
        }

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

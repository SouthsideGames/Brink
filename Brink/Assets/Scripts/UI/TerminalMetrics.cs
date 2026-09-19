using System;

namespace Brink.UI
{
    /// <summary>
    /// How much terminal actually fits on this screen.
    ///
    /// The shell is a character grid: ASCII bars, box rules and the world map
    /// only line up under `white-space: pre`, which means nothing wraps and
    /// anything wider than the panel simply runs off the right edge. Views used
    /// to hardcode 64–78 columns, which fits a tablet and overflows a phone —
    /// and badly overflows a foldable's cover screen.
    ///
    /// So the column count is measured from the real panel width at runtime and
    /// every view builds its output to that width instead. Larger screens get
    /// more columns, which is exactly the GDD §4 promise that a bigger display
    /// shows *more*, never bigger.
    /// </summary>
    public static class TerminalMetrics
    {
        /// <summary>Narrowest layout worth attempting; below this, content is unreadable anyway.</summary>
        public const int MinColumns = 34;

        /// <summary>Beyond this, long rules look absurd and lines become hard to track.</summary>
        public const int MaxColumns = 104;

        /// <summary>Panel height below which vertical space is the scarce resource.</summary>
        public const float ShortScreenHeight = 320f;

        /// <summary>Characters that fit across the content area.</summary>
        public static int Columns { get; private set; } = 64;

        /// <summary>
        /// True on a short, wide panel — a folding phone's cover display, most
        /// notably. Width is abundant and height is not, so the shell trades one
        /// for the other.
        /// </summary>
        public static bool ShortScreen { get; private set; }

        /// <summary>
        /// Measured panel height in points. Exposed so an overlay can bound
        /// itself against the screen rather than against its own content — on a
        /// landscape phone an unbounded panel will happily push its own buttons
        /// below the fold, which is how the orientation panel became impossible
        /// to finish on an iPhone.
        /// </summary>
        public static float PanelHeight { get; private set; } = 640f;

        /// <summary>Measured width of one character, in points.</summary>
        public static float CharWidth { get; private set; } = 8f;

        /// <summary>Measured width of the content area, in points.</summary>
        public static float ContentWidth { get; private set; } = 512f;

        /// <summary>
        /// Columns available *inside a modal overlay* — the monthly briefing and
        /// the crisis panel.
        ///
        /// **These are not `Columns`.** `.crisis-panel` is `width: 80%` with 14px
        /// of padding and a 2px border on each side, and it is a sibling of the
        /// content host rather than a child, so a line wrapped to the full content
        /// width overflows it by roughly a fifth. That was the END MONTH bug: the
        /// briefing is the one thing that appears on that path, its wire lines
        /// name countries and run long, and a quiet month's lines happened to fit
        /// while a busy month's did not — which is exactly the reported
        /// "sometimes it looks fine".
        ///
        /// Derived rather than guessed, so it stays correct if the panel's width
        /// rule changes: keep the 0.8 in step with `.crisis-panel` in the USS.
        /// </summary>
        public static int OverlayColumns
        {
            get
            {
                if (CharWidth <= 0.01f) return MinColumns;

                const float PanelWidthFraction = 0.8f;   // must match .crisis-panel
                const float PaddingAndBorderPt = 32f;    // 14px padding + 2px border, both sides

                float usable = ContentWidth * PanelWidthFraction - PaddingAndBorderPt;
                int columns = (int)Math.Floor(usable / CharWidth) - 1;
                return Math.Max(MinColumns, Math.Min(MaxColumns, columns));
            }
        }

        public static SizeClass Size { get; private set; } = SizeClass.Compact;

        // Compatibility name used by vertical-slice views. Keep Size as the
        // authoritative stored value so there is still one responsive metric.
        public static SizeClass SizeClass => Size;

        /// <summary>Raised when the usable grid changes, so open views can rebuild.</summary>
        public static event Action Changed;

        /// <summary>
        /// Recompute from a measured content width and character width. Returns
        /// true when anything changed, so the caller can avoid a needless rebuild.
        /// </summary>
        public static bool Update(float contentWidthPt, float charWidthPt, float heightPt, SizeClass size)
        {
            int columns = Columns;
            if (charWidthPt > 0.01f && contentWidthPt > 0f)
            {
                // One column of headroom: rounding and the scroll gutter should
                // never be what pushes a rule past the edge.
                columns = (int)Math.Floor(contentWidthPt / charWidthPt) - 1;
                columns = Math.Max(MinColumns, Math.Min(MaxColumns, columns));
            }

            bool shortScreen = heightPt > 0f && heightPt < ShortScreenHeight;
            bool heightChanged = heightPt > 0f && Math.Abs(heightPt - PanelHeight) > 0.5f;
            if (heightPt > 0f) PanelHeight = heightPt;
            if (charWidthPt > 0.01f) CharWidth = charWidthPt;
            if (contentWidthPt > 0f) ContentWidth = contentWidthPt;

            if (columns == Columns && shortScreen == ShortScreen && size == Size && !heightChanged) return false;

            Columns = columns;
            ShortScreen = shortScreen;
            Size = size;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// A width for a panel that should not run the full terminal width —
        /// detail boxes and charts read better slightly inset on wide screens.
        /// </summary>
        public static int Inset(int margin = 2) => Math.Max(MinColumns, Columns - margin);

        /// <summary>Rows a full-height ASCII figure may occupy without pushing everything else off.</summary>
        public static int MapRows => ShortScreen ? 11 : Size == SizeClass.Large ? 23 : 17;

        /// <summary>Reset to defaults. Tests only.</summary>
        public static void ResetForTests()
        {
            Columns = 64;
            ShortScreen = false;
            Size = SizeClass.Compact;
            PanelHeight = 640f;
            CharWidth = 8f;
            ContentWidth = 512f;
        }
    }
}

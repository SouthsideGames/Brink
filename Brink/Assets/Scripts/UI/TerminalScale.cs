using System;

namespace Brink.UI
{
    /// <summary>
    /// How large a pixel of the terminal should be.
    ///
    /// The shell previously used `ConstantPhysicalSize` with a 96 DPI fallback,
    /// which makes the entire UI depend on the platform reporting an honest
    /// screen DPI. When it does not — the Device Simulator, and some real
    /// devices — the scale collapses to 1 and 13px text renders as 13 actual
    /// pixels on a 2300-pixel-wide display, which is unreadably small.
    ///
    /// So the scale is derived from pixel width instead, targeting a column
    /// count. No DPI, no platform trust, and the result is a pure function of
    /// the resolution — which means it can be tested without a device.
    ///
    /// The GDD §4 promise still holds: a bigger screen gets *more columns*, not
    /// bigger text.
    /// </summary>
    public static class TerminalScale
    {
        /// <summary>Font size in the stylesheet, before scaling.</summary>
        public const float BaseFontPx = 13f;

        /// <summary>JetBrains Mono advances 0.6em, so this is one character at scale 1.</summary>
        public const float BaseCharPx = BaseFontPx * 0.6f;

        public const float MinScale = 1.0f;
        public const float MaxScale = 6.0f;

        /// <summary>
        /// Columns we aim to fit across a screen of this pixel width. Bigger
        /// panels get more of them, which is the whole point.
        /// </summary>
        public static int TargetColumns(int screenWidthPx, int screenHeightPx)
        {
            // Tuned for legibility over density. This is a game made entirely of
            // text, so the character has to be comfortably readable at arm's
            // length before the screen is allowed to hold more of them. Typographic
            // guidance puts a comfortable measure at 45–75 characters; these
            // targets keep the prose columns inside that and let tables be wider.
            int columns =
                screenWidthPx <= 900 ? 42 :
                screenWidthPx <= 1400 ? 50 :
                screenWidthPx <= 2000 ? 58 :
                screenWidthPx <= 2600 ? 66 : 76;

            // A very wide, short panel — a folding phone's cover display — spends
            // a little of its abundant width buying vertical lines back. Kept
            // small: on a short screen it is tempting to shrink the text to fit
            // more rows, and that is exactly the trade that makes a text game
            // tiring to read.
            if (screenHeightPx > 0 && (float)screenWidthPx / screenHeightPx >= 2.2f)
                columns += 4;

            return columns;
        }

        /// <summary>
        /// Panel scale for this resolution: enough that the target column count
        /// spans the screen.
        /// </summary>
        public static float ScaleFor(int screenWidthPx, int screenHeightPx)
            => ScaleFor(screenWidthPx, screenHeightPx, 1f);

        /// <summary>
        /// Panel scale, with the operator's text-size preference applied. Larger
        /// text buys fewer columns: the screen does not grow, so something has to
        /// give, and which way that trade goes is the reader's call.
        /// </summary>
        public static float ScaleFor(int screenWidthPx, int screenHeightPx, float sizeMultiplier)
        {
            if (screenWidthPx <= 0) return 1f;
            if (sizeMultiplier <= 0f || float.IsNaN(sizeMultiplier)) sizeMultiplier = 1f;

            int columns = TargetColumns(screenWidthPx, screenHeightPx);
            float desiredPanelWidth = columns * BaseCharPx;
            float scale = screenWidthPx / desiredPanelWidth * sizeMultiplier;

            return Math.Max(MinScale, Math.Min(MaxScale, scale));
        }
    }
}

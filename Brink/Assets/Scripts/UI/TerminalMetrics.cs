using System;

namespace Brink.UI
{
    public static class TerminalMetrics
    {
        public const int MinColumns = 34;
        public const int MaxColumns = 104;
        public const float ShortScreenHeight = 320f;
        public static int Columns { get; private set; } = 64;
        public static bool ShortScreen { get; private set; }
        public static float PanelHeight { get; private set; } = 640f;
        public static float CharWidth { get; private set; } = 8f;
        public static float ContentWidth { get; private set; } = 512f;

        public static int OverlayColumns
        {
            get
            {
                if (CharWidth <= 0.01f) return MinColumns;
                const float PanelWidthFraction = 0.8f;
                const float PaddingAndBorderPt = 32f;
                float usable = ContentWidth * PanelWidthFraction - PaddingAndBorderPt;
                int columns = (int)Math.Floor(usable / CharWidth) - 1;
                return Math.Max(MinColumns, Math.Min(MaxColumns, columns));
            }
        }

        public static SizeClass Size { get; private set; } = SizeClass.Compact;

        // Compatibility name used by vertical-slice views. Keep Size as the
        // authoritative stored value so there is still one responsive metric.
        public static SizeClass SizeClass => Size;

        public static event Action Changed;

        public static bool Update(float contentWidthPt, float charWidthPt, float heightPt, SizeClass size)
        {
            int columns = Columns;
            if (charWidthPt > 0.01f && contentWidthPt > 0f)
            {
                columns = (int)Math.Floor(contentWidthPt / charWidthPt) - 1;
                columns = Math.Max(MinColumns, Math.Min(MaxColumns, columns));
            }

            bool shortScreen = heightPt > 0f && heightPt < ShortScreenHeight;
            if (heightPt > 0f) PanelHeight = heightPt;
            if (charWidthPt > 0.01f) CharWidth = charWidthPt;
            if (contentWidthPt > 0f) ContentWidth = contentWidthPt;

            if (columns == Columns && shortScreen == ShortScreen && size == Size) return false;
            Columns = columns;
            ShortScreen = shortScreen;
            Size = size;
            Changed?.Invoke();
            return true;
        }

        public static int Inset(int margin = 2) => Math.Max(MinColumns, Columns - margin);
        public static int MapRows => ShortScreen ? 11 : Size == SizeClass.Large ? 23 : 17;

        public static void ResetForTests()
        {
            Columns = 64;
            ShortScreen = false;
            Size = SizeClass.Compact;
        }
    }
}
using System;
using UnityEngine;

namespace Brink.UI
{
    /// <summary>How large the terminal's text should be, relative to the default.</summary>
    public enum TextSize
    {
        /// <summary>Denser than the default, for readers who want more on screen.</summary>
        Smallest,

        /// <summary>The default. Chosen on device over the larger settings.</summary>
        Small,
        Medium,
        Large,
        Larger
    }

    /// <summary>
    /// Display palette. All three clear WCAG AAA (7:1) for every text colour —
    /// a theme is a choice about comfort, never about legibility.
    /// </summary>
    public enum TerminalTheme
    {
        /// <summary>Phosphor green on near-black. The default terminal look.</summary>
        Green,

        /// <summary>Amber phosphor. Less blue light, easier for some at night.</summary>
        Amber,

        /// <summary>
        /// Lifted background and softened foreground. Lower luminance difference
        /// reduces the glow around bright glyphs on a dark field, which is what
        /// makes long sessions tiring for readers prone to it.
        /// </summary>
        Soft,

        /// <summary>
        /// Neutral bone on charcoal. Deliberately not pure white — white on
        /// near-black glares, and reads as a modern CLI rather than an old
        /// government terminal. Its purpose is to free the hue wheel so
        /// colour can carry standing instead of atmosphere.
        /// </summary>
        Signal
    }

    /// <summary>Line spacing. A real trade on a short screen: leading or lines.</summary>
    public enum TextDensity
    {
        /// <summary>Leading between rows. Easier to read, fewer rows visible.</summary>
        Comfortable,

        /// <summary>Rows touching. More fits on screen, harder on the eye.</summary>
        Compact
    }

    /// <summary>
    /// Per-device display preferences.
    ///
    /// Deliberately **not** part of `GameState`: how large you like your text is
    /// a property of the person and the handset, not of the save. It follows you
    /// across a full reset, and a save copied to another device does not drag
    /// one phone's ergonomics onto another.
    /// </summary>
    public static class DisplaySettings
    {
        const string TextSizeKey = "brink.display.textSize";
        const string ThemeKey = "brink.display.theme";
        const string DensityKey = "brink.display.density";
        const string MonthlyBriefingKey = "brink.display.monthlyBriefing";
        const string WorldWireKey = "brink.display.worldWire";

        static bool loaded;
        static TextSize textSize = TextSize.Small;
        static TerminalTheme theme = TerminalTheme.Amber;
        static TextDensity density = TextDensity.Comfortable;

        /// <summary>
        /// Whether the monthly briefing opens by itself after a month resolves.
        ///
        /// **On by default.** A strategy game this deep fails first at
        /// communication, not at simulation — the operator has to be told what
        /// happened. Anyone who finds it intrusive can switch it off and read the
        /// same material on the briefing panel whenever they like; nothing is
        /// lost, only interruption.
        /// </summary>
        static bool monthlyBriefing = true;

        /// <summary>
        /// Whether the briefing includes world news, or only our own affairs.
        ///
        /// Separate from the auto-open toggle on purpose: "do not interrupt me"
        /// and "I do not care what other countries are doing" are different
        /// preferences, and folding them into one switch would force a player
        /// who wants a quiet turn to also give up knowing the world exists.
        /// </summary>
        static bool worldWire = true;

        /// <summary>Raised when any preference changes, so the shell can re-apply.</summary>
        public static event Action Changed;

        public static TextSize Size
        {
            get { Load(); return textSize; }
            set { Load(); if (textSize == value) return; textSize = value; Save(); }
        }

        public static TerminalTheme Theme
        {
            get { Load(); return theme; }
            set { Load(); if (theme == value) return; theme = value; Save(); }
        }

        public static TextDensity Density
        {
            get { Load(); return density; }
            set { Load(); if (density == value) return; density = value; Save(); }
        }

        /// <summary>Does the monthly briefing open on its own after a turn?</summary>
        public static bool MonthlyBriefing
        {
            get { Load(); return monthlyBriefing; }
            set { Load(); if (monthlyBriefing == value) return; monthlyBriefing = value; Save(); }
        }

        /// <summary>Does that briefing carry world news, or only our own affairs?</summary>
        public static bool WorldWire
        {
            get { Load(); return worldWire; }
            set { Load(); if (worldWire == value) return; worldWire = value; Save(); }
        }

        /// <summary>
        /// Multiplier on the terminal scale. Larger text means fewer columns —
        /// the screen does not grow, so something has to give.
        /// </summary>
        public static float ScaleMultiplier
        {
            get
            {
                switch (Size)
                {
                    case TextSize.Smallest: return 0.70f;
                    case TextSize.Small: return 0.82f;
                    case TextSize.Large: return 1.20f;
                    case TextSize.Larger: return 1.45f;
                    default: return 1f;
                }
            }
        }

        /// <summary>Leading applied after each line of readout text, in unscaled px.</summary>
        public static float ParagraphSpacing => Density == TextDensity.Compact ? 0f : 4f;

        /// <summary>USS class carrying the palette.</summary>
        public static string ThemeClass
        {
            get
            {
                switch (Theme)
                {
                    case TerminalTheme.Amber: return "theme-amber";
                    case TerminalTheme.Soft: return "theme-soft";
                    case TerminalTheme.Signal: return "theme-signal";
                    default: return "theme-green";
                }
            }
        }

        public static string[] AllThemeClasses => new[] { "theme-green", "theme-amber", "theme-soft", "theme-signal" };

        public static string Describe(TextSize value)
        {
            switch (value)
            {
                case TextSize.Smallest: return "SMALLEST";
                case TextSize.Small: return "SMALL";
                case TextSize.Large: return "LARGE";
                case TextSize.Larger: return "LARGEST";
                default: return "MEDIUM";
            }
        }

        public static string Describe(TerminalTheme value)
        {
            switch (value)
            {
                case TerminalTheme.Amber: return "AMBER";
                case TerminalTheme.Soft: return "LOW GLARE";
                case TerminalTheme.Signal: return "SIGNAL";
                default: return "GREEN";
            }
        }

        public static string Describe(TextDensity value)
            => value == TextDensity.Compact ? "COMPACT" : "COMFORTABLE";

        static void Load()
        {
            if (loaded) return;
            loaded = true;

            textSize = (TextSize)PlayerPrefs.GetInt(TextSizeKey, (int)TextSize.Small);
            theme = (TerminalTheme)PlayerPrefs.GetInt(ThemeKey, (int)TerminalTheme.Amber);
            density = (TextDensity)PlayerPrefs.GetInt(DensityKey, (int)TextDensity.Comfortable);
            monthlyBriefing = PlayerPrefs.GetInt(MonthlyBriefingKey, 1) != 0;
            worldWire = PlayerPrefs.GetInt(WorldWireKey, 1) != 0;

            // A preference file written by a newer build must not brick the UI.
            if (!Enum.IsDefined(typeof(TextSize), textSize)) textSize = TextSize.Small;
            if (!Enum.IsDefined(typeof(TerminalTheme), theme)) theme = TerminalTheme.Amber;
            if (!Enum.IsDefined(typeof(TextDensity), density)) density = TextDensity.Comfortable;
        }

        static void Save()
        {
            PlayerPrefs.SetInt(TextSizeKey, (int)textSize);
            PlayerPrefs.SetInt(ThemeKey, (int)theme);
            PlayerPrefs.SetInt(DensityKey, (int)density);
            PlayerPrefs.SetInt(MonthlyBriefingKey, monthlyBriefing ? 1 : 0);
            PlayerPrefs.SetInt(WorldWireKey, worldWire ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Restore defaults. Tests, and the panel's RESET control.</summary>
        public static void ResetToDefaults()
        {
            Load();
            textSize = TextSize.Small;
            theme = TerminalTheme.Amber;
            density = TextDensity.Comfortable;
            Save();
        }
    }
}

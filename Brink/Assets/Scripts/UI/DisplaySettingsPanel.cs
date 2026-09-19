using System;
using Brink.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace Brink.UI
{
    /// <summary>
    /// Display preferences, reachable from the status bar in every build.
    ///
    /// This is not the debug console — it ships. For a game made entirely of
    /// text, being able to set the type size is an accessibility feature, not a
    /// nicety: no single default suits a folding phone's cover screen, a tablet,
    /// and every pair of eyes that will read it.
    /// </summary>
    public class DisplaySettingsPanel
    {
        public VisualElement Root { get; }

        readonly Action onChanged;
        readonly ScrollView reader;
        bool confirmingReset;

        public DisplaySettingsPanel(Action onChanged)
        {
            this.onChanged = onChanged;

            Root = new VisualElement();
            Root.AddToClassList("settings-panel");
            Root.style.display = DisplayStyle.None;

            // The panel's content scrolls; CLOSE stays pinned below it. The
            // panel was a plain VisualElement with `flex-shrink: 0` and no
            // height cap — the tutorial-panel bug, refiled under Settings: on a
            // phone with large text everything past the fold was unreachable,
            // and the *last* thing in the panel was FULL RESET. A shipped game
            // whose only new-game path is below the fold of a box that cannot
            // scroll has no new-game path.
            reader = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            Root.Add(reader);

            Rebuild();
        }

        public bool Visible => Root.style.display == DisplayStyle.Flex;

        public void Toggle()
        {
            Root.style.display = Visible ? DisplayStyle.None : DisplayStyle.Flex;
            if (Visible) Rebuild();
        }

        public void Hide()
        {
            confirmingReset = false;
            Root.style.display = DisplayStyle.None;
        }

        /// <summary>Keep the reader inside the newly measured screen.</summary>
        public void RefreshLayout()
        {
            reader.style.maxHeight = ReaderHeightFor(
                TerminalMetrics.PanelHeight, TerminalMetrics.ShortScreen);
        }

        public static float ReaderHeightFor(float panelHeight, bool shortScreen)
            => Mathf.Max(44f, panelHeight * (shortScreen ? 0.55f : 0.70f));

        void Rebuild()
        {
            reader.Clear();

            // Pinned children (title above the scroller, CLOSE below it) are
            // rebuilt too, so clear everything except the scroller itself.
            for (int i = Root.childCount - 1; i >= 0; i--)
                if (Root[i] != reader) Root.RemoveAt(i);

            // Settings may cover most of the screen — unlike the tutorial they
            // are not teaching the panel behind them — but never all of it, and
            // never more than fits: the content scrolls to whatever remains.
            RefreshLayout();

            var title = new Label("DISPLAY");
            title.AddToClassList("terminal-text-bright");
            Root.Insert(0, title);

            BuildRow("TEXT SIZE",
                (TextSize[])Enum.GetValues(typeof(TextSize)),
                value => DisplaySettings.Describe(value),
                value => DisplaySettings.Size == value,
                value => { DisplaySettings.Size = value; Refresh(); });

            BuildRow("PALETTE",
                (TerminalTheme[])Enum.GetValues(typeof(TerminalTheme)),
                value => DisplaySettings.Describe(value),
                value => DisplaySettings.Theme == value,
                value => { DisplaySettings.Theme = value; Refresh(); });

            BuildRow("LINE SPACING",
                (TextDensity[])Enum.GetValues(typeof(TextDensity)),
                value => DisplaySettings.Describe(value),
                value => DisplaySettings.Density == value,
                value => { DisplaySettings.Density = value; Refresh(); });

            BuildRow("MONTHLY BRIEFING",
                new[] { true, false },
                value => value ? "ON" : "OFF",
                value => DisplaySettings.MonthlyBriefing == value,
                value => { DisplaySettings.MonthlyBriefing = value; Refresh(); });

            BuildRow("WORLD NEWS",
                new[] { true, false },
                value => value ? "ON" : "OFF",
                value => DisplaySettings.WorldWire == value,
                value => { DisplaySettings.WorldWire = value; Refresh(); });

            BuildRow("SCANLINES",
                new[] { true, false },
                value => value ? "ON" : "OFF",
                value => DisplaySettings.Atmosphere == value,
                value => { DisplaySettings.Atmosphere = value; Refresh(); });

            var displayRow = new VisualElement();
            displayRow.AddToClassList("settings-row");
            reader.Add(displayRow);

            var reset = new Button(() => { DisplaySettings.ResetToDefaults(); Refresh(); })
            { text = "RESET DISPLAY" };
            reset.AddToClassList("cmd-button");
            displayRow.Add(reset);

            // ---- audio (2026-08 audit) ----
            //
            // `AudioPreferences` had a full persisted model of master / music /
            // SFX / ambience / mute and no screen that set any of it. Five steps
            // per channel rather than a slider: the terminal's controls are
            // buttons, and a slider is the one widget this shell never draws.
            var audioTitle = new Label("AUDIO");
            audioTitle.AddToClassList("terminal-text-bright");
            reader.Add(audioTitle);

            var levels = new[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
            string Level(float v) => v <= 0f ? "OFF" : $"{Mathf.RoundToInt(v * 100)}";
            bool Near(float a, float b) => Mathf.Abs(a - b) < 0.125f;

            BuildRow("MASTER", levels, Level,
                value => Near(Brink.Audio.AudioPreferences.Master, value),
                value => { Brink.Audio.AudioPreferences.Master = value; Refresh(); });
            BuildRow("MUSIC", levels, Level,
                value => Near(Brink.Audio.AudioPreferences.Music, value),
                value => { Brink.Audio.AudioPreferences.Music = value; Refresh(); });
            BuildRow("EFFECTS", levels, Level,
                value => Near(Brink.Audio.AudioPreferences.Sfx, value),
                value => { Brink.Audio.AudioPreferences.Sfx = value; Refresh(); });
            BuildRow("TERMINAL HUM", levels, Level,
                value => Near(Brink.Audio.AudioPreferences.Ambience, value),
                value => { Brink.Audio.AudioPreferences.Ambience = value; Refresh(); });
            BuildRow("MUTE ALL",
                new[] { false, true },
                value => value ? "ON" : "OFF",
                value => Brink.Audio.AudioPreferences.Muted == value,
                value => { Brink.Audio.AudioPreferences.Muted = value; Refresh(); });

            // ---- no save/load rows here, deliberately ----
            //
            // A 2026-08 phone-chrome pass added SAVE TO / LOAD FROM slots 1–3 to
            // this panel. Removed 2026-08-28 (user decision, reaffirming the
            // original): **the game autosaves and there is no way to reload a
            // month you disliked.** GDD §30's rule is that consequences stick,
            // and a load button in the always-shipping settings panel is exactly
            // the loop the rule exists to close — it would also make an operator
            // caught running covert action able to reload past the world
            // hardening against them, which is the one thing spec 06 §7b's
            // counter-play design refuses.
            //
            // `SaveSystem`'s slots stay in code (tests use them, and cloud sync
            // would need them), and `SaveToSlot` / `LoadFromSlot` remain on
            // `GameController`; they are named in
            // `ActionIndexTests.NotOperatorActions` as session lifecycle.
            // **Do not add a save/load screen without asking.**

            // The reset sits above the reference prose, not below it: on most
            // screens it is now visible without scrolling at all, which is what
            // "reachable from Settings" has to mean on a phone.
            BuildFullReset();

            var hint = new Label(
                "  Larger text means fewer characters per line — the screen does not\n" +
                "  grow. COMPACT spacing fits more rows on a short screen at the cost\n" +
                "  of some legibility. LOW GLARE softens the contrast for long sessions.\n" +
                "\n" +
                "  MONTHLY BRIEFING opens a summary after each turn. Switched off, the\n" +
                "  same material stays on the BRIEFING panel — you lose the interruption,\n" +
                "  not the information. WORLD NEWS controls whether that summary covers\n" +
                "  other countries; it reports only what is publicly known, so it never\n" +
                "  replaces intelligence work.");
            hint.AddToClassList("terminal-text-dim");
            reader.Add(hint);

            // CLOSE is pinned outside the scroller — the one control that must
            // never be below the fold, per the tutorial-panel rule.
            var closeRow = new VisualElement();
            closeRow.AddToClassList("settings-row");
            Root.Add(closeRow);

            var close = new Button(Hide) { text = "CLOSE" };
            close.AddToClassList("cmd-button");
            close.AddToClassList("primary");
            closeRow.Add(close);
        }

        /// <summary>
        /// Full reset (GDD §5.1), which the design requires to be reachable from
        /// Settings.
        ///
        /// It used to live only on the SYSTEM debug console — so gating that
        /// console out of release builds (GDD §30 forbids its save-scum loop)
        /// silently took the reset with it, leaving a shipped game with no way
        /// back to the assessment. It belongs here, where players can find it.
        ///
        /// Two steps, because it erases the nation, the world's history and all
        /// Strategist progression, and there is no undo.
        /// </summary>
        void BuildFullReset()
        {
            var warning = new Label(
                "\n  FULL RESET erases the nation, the world's history and all\n" +
                "  Strategist progression, and returns you to the assessment.\n" +
                "  It cannot be undone.");
            warning.AddToClassList("terminal-text");
            warning.AddToClassList("terminal-text-dim");
            reader.Add(warning);

            var row = new VisualElement();
            row.AddToClassList("settings-row");
            reader.Add(row);

            if (!confirmingReset)
            {
                var request = new Button(() => { confirmingReset = true; Rebuild(); })
                { text = "FULL RESET" };
                request.AddToClassList("cmd-button");
                request.AddToClassList("danger");
                row.Add(request);
                return;
            }

            var confirm = new Button(() =>
            {
                confirmingReset = false;
                Hide();
                GameController.Instance.ResetGame();
            })
            { text = "CONFIRM — ERASE EVERYTHING" };
            confirm.AddToClassList("cmd-button");
            confirm.AddToClassList("danger");
            row.Add(confirm);

            var cancel = new Button(() => { confirmingReset = false; Rebuild(); }) { text = "CANCEL" };
            cancel.AddToClassList("cmd-button");
            cancel.AddToClassList("primary");
            row.Add(cancel);
        }

        void BuildRow<T>(string label, T[] values, Func<T, string> describe,
            Func<T, bool> isCurrent, Action<T> select)
        {
            var row = new VisualElement();
            row.AddToClassList("settings-row");
            reader.Add(row);

            var name = new Label(label);
            name.AddToClassList("settings-label");
            name.AddToClassList("terminal-text-dim");
            row.Add(name);

            foreach (var value in values)
            {
                var captured = value;
                bool current = isCurrent(value);
                var button = new Button(() => select(captured))
                { text = (current ? "► " : "") + describe(value) };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }
        }

        void Refresh()
        {
            Rebuild();
            onChanged?.Invoke();
        }
    }
}

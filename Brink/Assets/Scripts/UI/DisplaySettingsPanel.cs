using System;
using Brink.Core;
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
        bool confirmingReset;

        public DisplaySettingsPanel(Action onChanged)
        {
            this.onChanged = onChanged;

            Root = new VisualElement();
            Root.AddToClassList("settings-panel");
            Root.style.display = DisplayStyle.None;

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

        void Rebuild()
        {
            Root.Clear();

            var title = new Label("DISPLAY");
            title.AddToClassList("terminal-text-bright");
            Root.Add(title);

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
            Root.Add(hint);

            var row = new VisualElement();
            row.AddToClassList("settings-row");
            Root.Add(row);

            var reset = new Button(() => { DisplaySettings.ResetToDefaults(); Refresh(); })
            { text = "RESET DISPLAY" };
            reset.AddToClassList("cmd-button");
            row.Add(reset);

            var close = new Button(Hide) { text = "CLOSE" };
            close.AddToClassList("cmd-button");
            close.AddToClassList("primary");
            row.Add(close);

            BuildFullReset();
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
            Root.Add(warning);

            var row = new VisualElement();
            row.AddToClassList("settings-row");
            Root.Add(row);

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
            Root.Add(row);

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

using System;
using Brink.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Brink.UI
{
    /// <summary>
    /// The Crisis Turn modal (GDD §6): the one overlay that stands between the
    /// operator and END MONTH, so it has to be finishable on every screen.
    ///
    /// **The decision scrolls; the header never does.** The panel used to be a
    /// plain column with no scroller and no height cap — the tutorial-panel bug
    /// in the worst possible place. A long body plus three or four options with
    /// hints clipped below the fold of a short screen with nothing scrollable,
    /// and the option that was cut off was as often as not the one the operator
    /// wanted. The body and the options now live in one scroller bounded
    /// against the measured panel height, so on a phone the reader sees a
    /// cut-off option (which invites a scroll) rather than an invisible one.
    /// The FLASH line and the title stay pinned so the reader always knows what
    /// they are looking at.
    ///
    /// A class rather than a block of the shell controller so it can be built
    /// and asserted on without a UIDocument — the settings panel precedent.
    /// </summary>
    public class CrisisPanel
    {
        public VisualElement Root { get; }

        readonly Label title;
        readonly Label body;
        readonly Label scene;
        readonly VisualElement options;
        readonly ScrollView reader;

        public CrisisPanel()
        {
            Root = new VisualElement();
            Root.AddToClassList("crisis-panel");

            var flash = new Label("■ FLASH ■ CRISIS TURN ■ IMMEDIATE DECISION REQUIRED");
            flash.AddToClassList("crisis-flash");
            Root.Add(flash);

            title = new Label();
            title.AddToClassList("crisis-title");
            Root.Add(title);

            reader = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            reader.AddToClassList("crisis-reader");
            Root.Add(reader);

            body = new Label();
            body.AddToClassList("crisis-body");
            reader.Add(body);

            scene = new Label();
            scene.AddToClassList("terminal-figure");
            scene.AddToClassList("terminal-text-dim");
            reader.Add(scene);

            options = new VisualElement();
            options.AddToClassList("crisis-options");
            reader.Add(options);
        }

        /// <summary>
        /// Put one crisis in front of the operator. `decide` receives the option
        /// index the moment a button is pressed.
        /// </summary>
        public void Show(ActiveCrisis crisis, Action<int> decide)
        {
            title.text = crisis.title;
            body.text = crisis.body;
            scene.text = AsciiPillarArt.Crisis(TerminalMetrics.OverlayColumns);

            options.Clear();
            for (int i = 0; i < crisis.options.Count; i++)
            {
                var option = crisis.options[i];
                int index = i;
                var button = new Button(() => decide?.Invoke(index))
                { text = $"{i + 1}. {option.label}" };
                button.AddToClassList("crisis-option-button");
                options.Add(button);

                var hint = new Label(option.description);
                hint.AddToClassList("crisis-option-hint");
                options.Add(hint);
            }

            // Bound the scrolling area against the *screen*, never the content.
            // A percentage in USS would need a definite parent height, which a
            // centred overlay does not have. Tighter where height is scarce, and
            // leaving room above for the pinned FLASH line and title and the
            // panel's own padding.
            reader.style.maxHeight = ReaderHeightFor(TerminalMetrics.PanelHeight, TerminalMetrics.ShortScreen);
            reader.scrollOffset = Vector2.zero;
        }

        /// <summary>Height budget for the scrolling part, in panel points.</summary>
        public static float ReaderHeightFor(float panelHeight, bool shortScreen)
            => Math.Max(80f, panelHeight * (shortScreen ? 0.58f : 0.68f));
    }
}

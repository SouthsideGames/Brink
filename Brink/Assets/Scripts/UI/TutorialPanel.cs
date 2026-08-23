using Brink.Core;
using UnityEngine.UIElements;

namespace Brink.UI
{
    /// <summary>
    /// The orientation panel (GDD §5). Sits above the content host during the
    /// first posting, states what the operator is being taught and what they
    /// need to do, and gets out of the way permanently once done. It never
    /// blocks an action and can be dismissed at any point.
    /// </summary>
    public class TutorialPanel
    {
        public VisualElement Root { get; }

        readonly Label title;
        readonly Label body;
        readonly Label instruction;
        readonly Button acknowledge;
        readonly ScrollView reader;
        readonly System.Action onChanged;

        public TutorialPanel(System.Action onChanged)
        {
            this.onChanged = onChanged;

            Root = new VisualElement();
            Root.AddToClassList("tutorial-panel");

            title = new Label();
            title.AddToClassList("tutorial-title");
            Root.Add(title);

            // **The prose scrolls; the controls never do.**
            //
            // This panel used to be a plain column with `flex-shrink: 0`, so on a
            // landscape phone a long step pushed ACKNOWLEDGED and DISMISS
            // ORIENTATION below the bottom of the screen with no way to reach
            // them — the tutorial could be started and not finished, and could not
            // be dismissed either. Height is the scarce resource on this device,
            // and an overlay has to bound itself against the *screen* rather than
            // against its own content.
            //
            // Scrolling the whole panel would have been the easy fix and the wrong
            // one: the buttons would still have been off-screen until the operator
            // discovered they could scroll a box that gives no sign of being
            // scrollable. Only the text moves.
            var reader = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            reader.AddToClassList("tutorial-reader");
            Root.Add(reader);

            body = new Label();
            body.AddToClassList("tutorial-body");
            reader.Add(body);

            instruction = new Label();
            instruction.AddToClassList("tutorial-instruction");
            reader.Add(instruction);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            this.reader = reader;

            acknowledge = new Button(() =>
            {
                GameController.Instance.AcknowledgeTutorialStep();
                onChanged?.Invoke();
            })
            { text = "ACKNOWLEDGED" };
            acknowledge.AddToClassList("cmd-button");
            acknowledge.AddToClassList("primary");
            row.Add(acknowledge);

            var skip = new Button(() =>
            {
                GameController.Instance.SkipTutorial();
                onChanged?.Invoke();
            })
            { text = "DISMISS ORIENTATION" };
            skip.AddToClassList("cmd-button");
            row.Add(skip);
        }

        /// <summary>Update the panel; hides itself when orientation is over.</summary>
        public void Refresh()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning)
            {
                Root.style.display = DisplayStyle.None;
                return;
            }

            gc.RefreshTutorial();
            var step = TutorialSystem.CurrentStep(gc.State);
            if (step == null)
            {
                Root.style.display = DisplayStyle.None;
                return;
            }

            Root.style.display = DisplayStyle.Flex;

            // Bound the readable area against the panel, not the content. A third
            // of the screen is enough for six lines of orientation and leaves the
            // terminal it is describing actually visible underneath — the panel
            // exists to teach the screen behind it, so covering that screen
            // defeats it. Tighter still where height is already scarce.
            reader.style.maxHeight =
                TerminalMetrics.PanelHeight * (TerminalMetrics.ShortScreen ? 0.22f : 0.33f);

            int number = gc.State.tutorial.stepIndex + 1;
            title.text = $"ORIENTATION {number}/{TutorialSystem.Steps.Count} — {step.title}";
            body.text = step.body;

            bool needsAction = step.isSatisfied != null && !step.isSatisfied(gc.State);
            instruction.text = needsAction ? $"► {step.instruction}" : "► Ready to continue.";

            // Read-only steps advance on acknowledgement; action steps advance
            // themselves the moment the operator actually does the thing.
            acknowledge.text = needsAction ? "WAITING" : "ACKNOWLEDGED";
            acknowledge.SetEnabled(!needsAction);
        }
    }
}

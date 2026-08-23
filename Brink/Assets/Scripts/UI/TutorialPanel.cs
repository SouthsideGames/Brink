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
        readonly System.Action onChanged;

        public TutorialPanel(System.Action onChanged)
        {
            this.onChanged = onChanged;

            Root = new VisualElement();
            Root.AddToClassList("tutorial-panel");

            title = new Label();
            title.AddToClassList("tutorial-title");
            Root.Add(title);

            body = new Label();
            body.AddToClassList("tutorial-body");
            Root.Add(body);

            instruction = new Label();
            instruction.AddToClassList("tutorial-instruction");
            Root.Add(instruction);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

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

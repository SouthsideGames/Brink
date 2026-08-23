using System.Collections.Generic;
using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI
{
    /// <summary>
    /// First-launch strategic assessment (GDD §5). Ten in-universe scenarios,
    /// then a single controlled intervention — the operator may accept the
    /// posting the assessment produced, or override it — before the simulation
    /// begins. Scoring is never shown as numbers.
    /// </summary>
    public class AssessmentScreen
    {
        public VisualElement Root { get; }

        readonly List<int> answers = new List<int>();
        readonly System.Action onComplete;

        int index;
        AssessmentResult pendingResult;
        int seed;

        public AssessmentScreen(System.Action onComplete)
        {
            this.onComplete = onComplete;
            Root = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            Root.AddToClassList("view-scroll");
            Restart();
        }

        public void Restart()
        {
            answers.Clear();
            index = 0;
            pendingResult = null;
            seed = System.Environment.TickCount;
            Render();
        }

        void Render()
        {
            Root.Clear();
            if (pendingResult != null) RenderIntervention();
            else if (index < AssessmentCatalog.Questions.Count) RenderQuestion();
        }

        void RenderQuestion()
        {
            var question = AssessmentCatalog.Questions[index];

            var header = AddLabel("terminal-text-bright");
            header.text =
                AsciiChart.BoxHeader("STRATEGIC APTITUDE ASSESSMENT", 72) + "\n" +
                $" SCENARIO {index + 1} OF {AssessmentCatalog.Questions.Count}\n" +
                $" {AsciiChart.Bar(index, AssessmentCatalog.Questions.Count, 40)}\n";

            var situation = AddLabel();
            situation.style.whiteSpace = WhiteSpace.Normal;
            situation.text = " " + question.situation + "\n\n " + question.prompt;

            for (int i = 0; i < question.options.Length; i++)
            {
                int choice = i;
                var button = new Button(() =>
                {
                    answers.Add(choice);
                    index++;
                    if (index >= AssessmentCatalog.Questions.Count)
                        pendingResult = AssessmentSystem.Evaluate(answers, seed);
                    Render();
                })
                { text = $"{(char)('A' + i)}.  {question.options[i].text}" };
                button.AddToClassList("crisis-option-button");
                Root.Add(button);
            }
        }

        /// <summary>
        /// The single controlled intervention before acceptance (GDD §5): the
        /// operator sees the profile in-universe and may take a different post.
        /// </summary>
        void RenderIntervention()
        {
            var assigned = WorldFactory.FindProfile(pendingResult.assignedCountryId);

            var header = AddLabel("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("ASSESSMENT COMPLETE", 72));
            sb.AppendLine($" {pendingResult.classificationText}");
            sb.AppendLine();
            sb.AppendLine(" ASSESSED DISPOSITION");
            sb.AppendLine(" " + pendingResult.doctrineText);
            sb.AppendLine();
            sb.AppendLine($" PROPOSED POSTING: {assigned?.displayName.ToUpperInvariant()}");
            sb.AppendLine();
            sb.AppendLine(" NATIONAL CHARACTER ON ASSUMPTION OF POST");
            foreach (var trait in pendingResult.traits)
            {
                sb.AppendLine($"   {trait.name.ToUpperInvariant()}");
                sb.AppendLine($"     {trait.description}");
            }
            header.text = sb.ToString();

            var note = AddLabel("terminal-text-dim");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.text = "\n One reassignment is permitted before your posting is entered into the record.\n";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            foreach (var profile in WorldFactory.Profiles)
            {
                var captured = profile;
                bool proposed = profile.id == pendingResult.assignedCountryId;
                var button = new Button(() =>
                {
                    pendingResult.assignedCountryId = captured.id;
                    Render();
                })
                { text = (proposed ? "► " : "") + profile.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (proposed) button.AddToClassList("primary");
                row.Add(button);
            }

            var acceptRow = new VisualElement();
            acceptRow.AddToClassList("button-row");
            Root.Add(acceptRow);

            var accept = new Button(() =>
            {
                GameController.Instance.NewGameFromAssessment(pendingResult, seed);
                onComplete?.Invoke();
            })
            { text = "ACCEPT POSTING AND BEGIN" };
            accept.AddToClassList("cmd-button");
            accept.AddToClassList("primary");
            acceptRow.Add(accept);

            var retake = new Button(Restart) { text = "RETAKE ASSESSMENT" };
            retake.AddToClassList("cmd-button");
            acceptRow.Add(retake);
        }

        Label AddLabel(string extraClass = null)
        {
            var label = new Label();
            label.AddToClassList("terminal-text");
            if (extraClass != null) label.AddToClassList(extraClass);
            Root.Add(label);
            return label;
        }
    }
}

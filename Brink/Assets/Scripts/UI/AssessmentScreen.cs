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
        WorldSize chosenSize = WorldSize.Standard;

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
            chosenSize = WorldSize.Standard;
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

            // Theatre scale (GDD §31.2 amendment): how much of the authored
            // world this save opens with. Chosen here, before acceptance,
            // because world composition is part of the posting decision — a
            // Regional world has fewer posts to be assigned to.
            var scaleNote = AddLabel("terminal-text-dim");
            scaleNote.style.whiteSpace = WhiteSpace.Normal;
            scaleNote.text = "\n THEATER OF OPERATIONS — how much of the world this posting opens onto.\n";

            var scaleRow = new VisualElement();
            scaleRow.AddToClassList("button-row");
            Root.Add(scaleRow);

            void AddScale(WorldSize size, string label)
            {
                bool current = chosenSize == size;
                var button = new Button(() =>
                {
                    chosenSize = size;

                    // A posting the chosen world does not contain is reassigned
                    // to the best fit inside it — never silently kept and then
                    // quietly replaced at world creation.
                    var roster = WorldFactory.RosterFor(chosenSize);
                    if (System.Array.IndexOf(roster, pendingResult.assignedCountryId) < 0)
                        pendingResult.assignedCountryId =
                            AssessmentSystem.AssignPosting(pendingResult.doctrine, roster);
                    Render();
                })
                { text = (current ? "► " : "") + label };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                scaleRow.Add(button);
            }

            AddScale(WorldSize.Regional,
                $"REGIONAL — {WorldFactory.RegionalRoster.Length} STATES");
            AddScale(WorldSize.Standard,
                $"STANDARD — {WorldFactory.StandardRoster.Length} STATES");
            AddScale(WorldSize.Full,
                $"FULL WORLD — {WorldFactory.Profiles.Length} STATES");

            var note = AddLabel("terminal-text-dim");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.text = "\n One reassignment is permitted before your posting is entered into the record.\n";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            var available = new System.Collections.Generic.HashSet<string>(
                WorldFactory.RosterFor(chosenSize));
            foreach (var profile in WorldFactory.Profiles)
            {
                if (!available.Contains(profile.id)) continue;

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
                GameController.Instance.NewGameFromAssessment(pendingResult, seed, chosenSize);
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

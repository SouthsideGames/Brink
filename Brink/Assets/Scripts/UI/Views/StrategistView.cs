using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Strategist progression (GDD Phase 10, §25): experience, the annual
    /// evaluation archive, and the five branching skill trees.
    /// </summary>
    public class StrategistView : TerminalView
    {
        // This panel is the operator's own file — their record, their
        // evaluations, their skills — and "operator" is what the game calls
        // them everywhere else. It was "STRATEGIST/STR", one letter away from
        // the strategic-instruments panel next to it in the rail.
        public override string Id => "OPERATOR";
        public override string ShortCode => "OPR";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        Pillar selectedTree = Pillar.Government;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;

            Root.Clear();
            BuildStanding(state);
            BuildMandate(state);
            BuildEvaluations(state);
            BuildTreeSelector(state);
            BuildTree(state);
        }

        /// <summary>
        /// What this posting is for (GDD §25 amendment). The brief the operator
        /// arrived with, each undertaking marked as it stands today, and the
        /// verdict once the ten-year review has been delivered.
        /// </summary>
        void BuildMandate(GameState state)
        {
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("MANDATE", W));
            if (state.mandate == null)
            {
                sb.AppendLine(" No mandate on file.");
            }
            else
            {
                sb.AppendLine(" " + state.mandate.brief);
                foreach (var line in MandateSystem.StatusText(state).Split('\n'))
                    sb.AppendLine(" " + line);
            }
            text.text = sb.ToString();
        }

        void BuildStanding(GameState state)
        {
            int currentFloor = ProgressionSystem.XPForLevel(state.strategistLevel);
            int nextLevel = ProgressionSystem.XPForLevel(state.strategistLevel + 1);
            int span = System.Math.Max(1, nextLevel - currentFloor);
            int into = state.strategistXP - currentFloor;

            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("OPERATOR RECORD", W));
            sb.AppendLine(" Your own file. What this office has learned, and what it may now do.");
            sb.AppendLine(" Nothing here is national power — for that, see the five pillar panels.");
            sb.AppendLine($" LEVEL {state.strategistLevel}   XP {state.strategistXP}   " +
                          $"SKILL POINTS AVAILABLE: {state.skillPoints}");
            sb.AppendLine("  " + AsciiChart.LabeledBar("NEXT LEVEL", into, span, 12, 24));
            sb.AppendLine($" ADMINISTRATIONS SERVED: {state.administrationsServed}   " +
                          $"YEARS EVALUATED: {state.evaluations.Count}");
            text.text = sb.ToString();
        }

        void BuildEvaluations(GameState state)
        {
            var text = AddText();
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("ANNUAL EVALUATIONS", W));

            if (state.evaluations.Count == 0)
            {
                sb.AppendLine(" No evaluation yet. Performance is assessed at the end of each year,");
                sb.AppendLine(" judged against the circumstances you actually governed under.");
            }
            else
            {
                int start = System.Math.Max(0, state.evaluations.Count - 8);
                for (int i = state.evaluations.Count - 1; i >= start; i--)
                {
                    var record = state.evaluations[i];
                    sb.AppendLine($" {record.year}   GRADE {record.grade}   SCORE {record.score,5:F1}   " +
                                  $"+{record.skillPointsAwarded} SP");
                    sb.AppendLine($"    {record.summary}");
                    sb.AppendLine($"    TRAJ {record.trajectoryScore,5:F0}  ECON {record.economyScore,5:F0}  " +
                                  $"STAB {record.stabilityScore,5:F0}  POS {record.positionScore,5:F0}  " +
                                  $"CRIS {record.crisisScore,5:F0}  INIT {record.initiativeScore,5:F0}");
                }
            }
            text.text = sb.ToString();
        }

        void BuildTreeSelector(GameState state)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("SKILL TREES", W);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                var captured = pillar;
                bool current = selectedTree == pillar;
                var button = new Button(() => { selectedTree = captured; Refresh(); })
                { text = (current ? "► " : "") + pillar.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }
        }

        void BuildTree(GameState state)
        {
            var nodes = SkillCatalog.ForPillar(selectedTree);
            nodes.Sort((a, b) => a.tier.CompareTo(b.tier));

            foreach (var node in nodes)
            {
                bool unlocked = state.HasSkill(node.id);
                bool available = ProgressionSystem.CanUnlock(state, node.id, out string reason);

                var text = AddText(unlocked ? "terminal-text-bright" : "terminal-text-dim");
                var sb = new StringBuilder();
                sb.AppendLine($" [{(unlocked ? "X" : " ")}] TIER {node.tier}  {node.name.ToUpperInvariant()}" +
                              (node.isHybrid ? "   ** HYBRID **" : ""));
                sb.AppendLine($"     {node.description}");
                if (node.prerequisites.Length > 0)
                {
                    var names = new System.Collections.Generic.List<string>();
                    foreach (var prerequisite in node.prerequisites)
                        names.Add(SkillCatalog.Find(prerequisite)?.name ?? prerequisite);
                    sb.AppendLine($"     REQUIRES: {string.Join(", ", names)}");
                }
                if (!unlocked && !available)
                    sb.AppendLine($"     LOCKED: {reason}");
                text.text = sb.ToString();

                if (unlocked || !available) continue;

                var row = new VisualElement();
                row.AddToClassList("button-row");
                Root.Add(row);
                var button = new Button(() => { GameController.Instance.UnlockSkill(node.id); Refresh(); })
                { text = $"UNLOCK [{node.cost} SP]" };
                button.AddToClassList("cmd-button");
                button.AddToClassList("primary");
                row.Add(button);
            }

            AddText("terminal-text-dim").text =
                "\n Skills expand what you can know, choose and afford as operator.\n" +
                " National power is still built through investment, officials and policy.";
        }
    }
}

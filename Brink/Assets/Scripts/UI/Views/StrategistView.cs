using System.Text;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    public class StrategistView : TerminalView
    {
        public override string Id => "OPERATOR";
        public override string ShortCode => "OPR";
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
            BuildStrategy(state);
            BuildDirectives(state);
            BuildCareer(state);
            BuildEvaluations(state);
            BuildTreeSelector(state);
            BuildTree(state);
        }

        void BuildMandate(GameState state)
        {
            var text = AddText("terminal-text-bright"); var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("MANDATE", W));
            if (state.mandate == null) sb.AppendLine(" No mandate on file.");
            else { sb.AppendLine(" " + state.mandate.brief); foreach (var line in MandateSystem.StatusText(state).Split('\n')) sb.AppendLine(" " + line); }
            text.text = sb.ToString();
        }

        void BuildStrategy(GameState state)
        {
            var plan = StrategySystem.Ensure(state);
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("STANDING STRATEGY", W));
            sb.AppendLine(" The mandate says what the posting owes. Strategy says what unattended government should favour.");
            foreach (var line in StrategySystem.StatusText(state).Split('\n')) sb.AppendLine(" " + line);
            text.text = sb.ToString();

            AddText("terminal-text-dim").text = " DOCTRINE — first adoption is free; revising it costs 2 Influence.";
            var doctrineRow = new VisualElement(); doctrineRow.AddToClassList("button-row"); Root.Add(doctrineRow);
            foreach (StrategicDoctrine doctrine in System.Enum.GetValues(typeof(StrategicDoctrine)))
            {
                var captured = doctrine;
                bool active = plan != null && plan.doctrineChosen && plan.doctrine == doctrine;
                var button = new Button(() => { StrategySystem.SetDoctrine(state, captured); Refresh(); })
                { text = (active ? "► " : "") + StrategySystem.DoctrineLabel(doctrine).ToUpperInvariant() };
                button.AddToClassList("cmd-button"); if (active) button.AddToClassList("primary"); doctrineRow.Add(button);
            }

            var policies = StrategySystem.AvailablePolicies(state);
            if (policies.Length > 0)
            {
                AddText("terminal-text-dim").text = " NATIONAL POLICY — country-shaped trade-off; replacement costs 1 Influence.";
                foreach (var policy in policies)
                {
                    var captured = policy;
                    var row = new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
                    AddText().text = $" {policy.label.ToUpperInvariant()} — {policy.description}";
                    var button = new Button(() => { StrategySystem.SetPolicy(state, captured.id); Refresh(); }) { text = "ADOPT" };
                    button.AddToClassList("cmd-button"); row.Add(button);
                }
            }

            AddText("terminal-text-dim").text = " WRITE AN OBJECTIVE — measurement only; no XP, no hidden bonus, maximum three.";
            var title = new TextField("OBJECTIVE") { value = "" }; title.AddToClassList("terminal-input"); Root.Add(title);
            var kinds = new List<string> { "Stability", "Approval", "Unity", "Energy", "Food", "Materials", "Industry", "Military", "Economy", "Intelligence", "Diplomacy", "Government", "Treaties", "Capabilities", "Relations ≥", "Relations ≤", "Solvent" };
            var kind = new DropdownField("MEASURE", kinds, 0); Root.Add(kind);
            var threshold = new FloatField("TARGET") { value = 60f }; Root.Add(threshold);
            var param = new TextField("COUNTRY ID (relations only)") { value = "" }; Root.Add(param);
            var add = new Button(() =>
            {
                var condition = ObjectiveCondition(kind.value, threshold.value, param.value);
                if (condition != null && StrategySystem.AddObjective(state, string.IsNullOrWhiteSpace(title.value) ? condition.text : title.value, condition)) Refresh();
            }) { text = "ADD OBJECTIVE" };
            add.AddToClassList("cmd-button"); add.AddToClassList("primary"); Root.Add(add);

            if (plan != null)
                foreach (var objective in plan.objectives)
                {
                    var captured = objective;
                    var row = new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
                    var remove = new Button(() => { StrategySystem.RemoveObjective(state, captured.id); Refresh(); }) { text = "REMOVE" };
                    remove.AddToClassList("cmd-button"); row.Add(remove);
                }
        }

        static MandateObjective ObjectiveCondition(string measure, float target, string param)
        {
            MandateObjectiveKind kind;
            string p = "";
            switch (measure)
            {
                case "Stability": kind=MandateObjectiveKind.StabilityAtLeast; break;
                case "Approval": kind=MandateObjectiveKind.ApprovalAtLeast; break;
                case "Unity": kind=MandateObjectiveKind.UnityAtLeast; break;
                case "Energy": kind=MandateObjectiveKind.EnergyAtLeast; break;
                case "Food": kind=MandateObjectiveKind.FoodAtLeast; break;
                case "Materials": kind=MandateObjectiveKind.MaterialsAtLeast; break;
                case "Industry": kind=MandateObjectiveKind.IndustryAtLeast; break;
                case "Treaties": kind=MandateObjectiveKind.TreatiesAtLeast; break;
                case "Capabilities": kind=MandateObjectiveKind.CapabilitiesAtLeast; break;
                case "Relations ≥": kind=MandateObjectiveKind.RelationsAtLeast; p=param?.Trim(); if(string.IsNullOrEmpty(p)) return null; break;
                case "Relations ≤": kind=MandateObjectiveKind.RelationsAtMost; p=param?.Trim(); if(string.IsNullOrEmpty(p)) return null; break;
                case "Solvent": return new MandateObjective { kind=MandateObjectiveKind.Solvent, text="Remain fiscally solvent." };
                default:
                    kind=MandateObjectiveKind.PillarAtLeast; p=measure; break;
            }
            return new MandateObjective { kind=kind, threshold=target, param=p, text=$"{measure} at {target:F0} or better." };
        }

        void BuildStanding(GameState state)
        {
            int currentFloor=ProgressionSystem.XPForLevel(state.strategistLevel), nextLevel=ProgressionSystem.XPForLevel(state.strategistLevel+1);
            int span=System.Math.Max(1,nextLevel-currentFloor), into=state.strategistXP-currentFloor;
            var text=AddText("terminal-text-bright"); var sb=new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("OPERATOR RECORD",W));
            sb.AppendLine(" Your own file. What this office has learned, and what it may now do.");
            sb.AppendLine(" Nothing here is national power — for that, see the five pillar panels.");
            sb.AppendLine($" LEVEL {state.strategistLevel}   XP {state.strategistXP}   SKILL POINTS AVAILABLE: {state.skillPoints}");
            sb.AppendLine("  "+AsciiChart.LabeledBar("NEXT LEVEL",into,span,12,24));
            sb.AppendLine($" ADMINISTRATIONS SERVED: {state.administrationsServed}   YEARS EVALUATED: {state.evaluations.Count}"); text.text=sb.ToString();
        }

        void BuildDirectives(GameState state)
        {
            var text=AddText(); var sb=new StringBuilder(); sb.AppendLine(AsciiChart.BoxHeader("STANDING DIRECTIVES",W));
            sb.AppendLine(" Suggested by the desks when circumstances warrant. Optional; nothing is owed.");
            foreach(var line in StandingDirectiveSystem.StatusText(state).Split('\n')) sb.AppendLine(" "+line); text.text=sb.ToString();
        }

        void BuildCareer(GameState state)
        {
            var text=AddText(); var sb=new StringBuilder(); sb.AppendLine(AsciiChart.BoxHeader("CAREER",W));
            foreach(var line in CareerRecord.StatusText(state).Split('\n')) sb.AppendLine(" "+line); text.text=sb.ToString();
        }

        void BuildEvaluations(GameState state)
        {
            var text=AddText(); var sb=new StringBuilder(); sb.AppendLine(AsciiChart.BoxHeader("ANNUAL EVALUATIONS",W));
            if(state.evaluations.Count==0){ sb.AppendLine(" No evaluation yet. Performance is assessed at the end of each year,"); sb.AppendLine(" judged against the circumstances you actually governed under."); }
            else { int start=System.Math.Max(0,state.evaluations.Count-8); for(int i=state.evaluations.Count-1;i>=start;i--){ var r=state.evaluations[i]; sb.AppendLine($" {r.year}   GRADE {r.grade}   SCORE {r.score,5:F1}   +{r.skillPointsAwarded} SP"); sb.AppendLine($"    {r.summary}"); sb.AppendLine($"    TRAJ {r.trajectoryScore,5:F0}  ECON {r.economyScore,5:F0}  STAB {r.stabilityScore,5:F0}  POS {r.positionScore,5:F0}  CRIS {r.crisisScore,5:F0}  INIT {r.initiativeScore,5:F0}"); }}
            text.text=sb.ToString();
        }

        void BuildTreeSelector(GameState state)
        {
            AddText("terminal-text-bright").text=AsciiChart.BoxHeader("SKILL TREES",W); var row=new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
            foreach(Pillar pillar in System.Enum.GetValues(typeof(Pillar))){ var captured=pillar; bool current=selectedTree==pillar; var button=new Button(()=>{selectedTree=captured;Refresh();}){text=(current?"► ":"")+pillar.ToString().ToUpperInvariant()}; button.AddToClassList("cmd-button"); if(current)button.AddToClassList("primary"); row.Add(button); }
        }

        void BuildTree(GameState state)
        {
            var nodes=SkillCatalog.ForPillar(selectedTree); nodes.Sort((a,b)=>a.tier.CompareTo(b.tier));
            foreach(var node in nodes){ bool unlocked=state.HasSkill(node.id); bool available=ProgressionSystem.CanUnlock(state,node.id,out string reason); var text=AddText(unlocked?"terminal-text-bright":"terminal-text-dim"); var sb=new StringBuilder(); sb.AppendLine($" [{(unlocked?"X":" ")}] TIER {node.tier}  {node.name.ToUpperInvariant()}"+(node.isHybrid?"   ** HYBRID **":"")); sb.AppendLine($"     {node.description}"); if(node.prerequisites.Length>0){var names=new List<string>();foreach(var prerequisite in node.prerequisites)names.Add(SkillCatalog.Find(prerequisite)?.name??prerequisite);sb.AppendLine($"     REQUIRES: {string.Join(", ",names)}");} if(!unlocked&&!available)sb.AppendLine($"     LOCKED: {reason}"); text.text=sb.ToString(); if(unlocked||!available)continue; var row=new VisualElement();row.AddToClassList("button-row");Root.Add(row);var button=new Button(()=>{GameController.Instance.UnlockSkill(node.id);Refresh();}){text=$"UNLOCK [{node.cost} SP]"};button.AddToClassList("cmd-button");button.AddToClassList("primary");row.Add(button);}
            AddText("terminal-text-dim").text="\n Skills expand what you can know, choose and afford as operator.\n National power is still built through investment, officials and policy.";
        }
    }
}
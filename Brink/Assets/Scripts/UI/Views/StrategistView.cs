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
        StrategicDoctrine previewDoctrine = StrategicDoctrine.Balanced;
        int previewHorizon;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            Root.Clear();
            BuildStanding(state);
            BuildMandate(state);
            BuildStrategy(state);
            BuildHistoricalCourse(state);
            BuildForecast(state);
            BuildDirectives(state);
            BuildCareer(state);
            BuildEvaluations(state);
            BuildTreeSelector(state);
            BuildTree(state);
        }

        void BuildHistoricalCourse(GameState state)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("STRATEGIC RECORD", W);
            AddText().text = StrategicEraSystem.Render(state);
            AddText("terminal-text-dim").text = StrategicReversalSystem.Render(state);

            var precedents = PrecedentSystem.Recent(state, TerminalMetrics.Size == SizeClass.Compact ? 2 : 4);
            AddText("terminal-text-bright").text = " PRECEDENT — WHAT LATER GOVERNMENTS CAN POINT TO";
            if (precedents.Count == 0) AddText("terminal-text-dim").text = "  No major player-country precedent is yet recorded.";
            else foreach (var item in precedents)
                AddText("terminal-text-dim").text = $"  {item.date.DisplayString}  {item.category.ToString().ToUpperInvariant()}\n   {item.text}\n   PRECEDENT: {item.reading}";

            var recovery = RecoveryHistorySystem.Read(state);
            if (recovery != null)
                AddText(recovery.deterioratingRecentMetrics > recovery.improvingRecentMetrics ? "sig-hostile" : "terminal-text-dim").text =
                    $" RECOVERY FILE — {recovery.status}\n  WARS WON/LOST {recovery.warsWon}/{recovery.warsLost}   ADMINISTRATIONS {recovery.administrationsServed}\n  RECENT IMPROVING/DETERIORATING {recovery.improvingRecentMetrics}/{recovery.deterioratingRecentMetrics} [12-MONTH CAUSAL WINDOW]";

            var memories = CredibilityMemorySystem.Build(state);
            if (memories.Count > 0)
            {
                AddText("terminal-text-bright").text = " CREDIBILITY MEMORY — PROMISES LEAVE A RECORD";
                int limit = TerminalMetrics.Size == SizeClass.Compact ? 2 : 4;
                for (int i = 0; i < memories.Count && i < limit; i++)
                {
                    var p = memories[i];
                    AddText("terminal-text-dim").text =
                        $"  {p.partnerName.ToUpperInvariant()}   TRUST {System.Math.Round(p.trust)}   MEMORY {(p.memoryWeight >= 0 ? "+" : "")}{System.Math.Round(p.memoryWeight, 1)}\n" +
                        $"   commitments {p.activeCommitments}   broken by us {p.brokenByUs}   broken by them {p.brokenByThem}" +
                        (string.IsNullOrEmpty(p.latestMemory) ? "" : $"\n   latest: {p.latestMemory}");
                }
                AddText("terminal-text-dim").text = "  This explains existing diplomatic trust and memory; it does not create a second credibility score.";
            }
        }

        void BuildMandate(GameState state)
        {
            var text = AddText("terminal-text-bright"); var sb = new StringBuilder(); sb.AppendLine(AsciiChart.BoxHeader("MANDATE", W));
            if (state.mandate == null) sb.AppendLine(" No mandate on file."); else { sb.AppendLine(" " + state.mandate.brief); foreach (var line in MandateSystem.StatusText(state).Split('\n')) sb.AppendLine(" " + line); } text.text = sb.ToString();
        }

        void BuildStrategy(GameState state)
        {
            var plan = StrategySystem.Ensure(state); var text = AddText("terminal-text-bright"); var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("STANDING STRATEGY", W)); sb.AppendLine(" The mandate says what the posting owes. Strategy says what unattended government should favour.");
            foreach (var line in StrategySystem.StatusText(state).Split('\n')) sb.AppendLine(" " + line); text.text = sb.ToString();
            if (plan != null)
            {
                AddText("terminal-text-dim").text = " LONG-TERM PLAN — a name and horizon for organising decisions; no score or timer.";
                var planName = new TextField("PLAN NAME") { value = plan.planTitle }; planName.AddToClassList("terminal-input"); Root.Add(planName);
                var horizons = new List<string> { "12 months", "36 months", "60 months", "120 months" }; int hIndex = plan.horizonMonths <= 12 ? 0 : plan.horizonMonths <= 36 ? 1 : plan.horizonMonths <= 60 ? 2 : 3;
                var horizon = new DropdownField("HORIZON", horizons, hIndex); horizon.AddToClassList("terminal-input"); Root.Add(horizon);
                var row = new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
                var save = new Button(() => { int months = horizon.index == 0 ? 12 : horizon.index == 1 ? 36 : horizon.index == 2 ? 60 : 120; if (StrategySystem.SetPlanFrame(state, planName.value, months)) Refresh(); }) { text = "SET PLAN FRAME" }; save.AddToClassList("cmd-button"); row.Add(save);
            }
            AddText("terminal-text-dim").text = " DOCTRINE — first adoption is free; revising it costs 2 Influence.";
            var doctrineRow = new VisualElement(); doctrineRow.AddToClassList("button-row"); Root.Add(doctrineRow);
            foreach (StrategicDoctrine d in System.Enum.GetValues(typeof(StrategicDoctrine)))
            {
                var captured = d; var b = new Button(() => { previewDoctrine = captured; previewHorizon = 0; Refresh(); }) { text = StrategySystem.DoctrineLabel(d) }; b.AddToClassList("cmd-button"); doctrineRow.Add(b);
            }
            BuildDoctrinePreview(state);
        }

        void BuildDoctrinePreview(GameState state)
        {
            var plan = StrategySystem.Ensure(state); if (plan == null) return;
            int horizon = previewHorizon <= 0 ? (plan.horizonMonths <= 0 ? 12 : plan.horizonMonths) : previewHorizon;
            AddText("terminal-text-bright").text = $" PREVIEW: {StrategySystem.DoctrineLabel(previewDoctrine).ToUpperInvariant()} — {horizon} MONTHS";
            AddText("terminal-text-dim").text = StrategicForecastSystem.Render(state, previewDoctrine, horizon);
            var horizons = new VisualElement(); horizons.AddToClassList("button-row"); Root.Add(horizons);
            foreach (int h in new[] { 12, 36, 60, 120 }) { int captured = h; var b = new Button(() => { previewHorizon = captured; Refresh(); }) { text = h + "M" }; b.AddToClassList("cmd-button"); horizons.Add(b); }
            var row = new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
            var adopt = new Button(() => { if (StrategySystem.SetDoctrine(state, previewDoctrine)) Refresh(); }) { text = plan.doctrineChosen ? "REVISE DOCTRINE" : "ADOPT DOCTRINE" }; adopt.AddToClassList("cmd-button"); adopt.AddToClassList("primary"); row.Add(adopt);
        }

        void BuildForecast(GameState state) { }
        void BuildStanding(GameState state) { }
        void BuildDirectives(GameState state) { }
        void BuildCareer(GameState state) { }
        void BuildEvaluations(GameState state) { }
        void BuildTreeSelector(GameState state) { }
        void BuildTree(GameState state) { }
    }
}
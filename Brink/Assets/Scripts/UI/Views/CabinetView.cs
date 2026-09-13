using System;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Cabinet management and institutional readout. The vertical slice keeps
    /// personality on the desk where it matters instead of adding another panel.
    /// </summary>
    public class CabinetView : TerminalView
    {
        public override string Id => "CABINET";
        public override string ShortCode => "CAB";
        static int W => TerminalMetrics.Columns;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            Root.Clear();

            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("CABINET — PILLAR LEADERSHIP", W) + "\n" +
                $" INFLUENCE: {state.influence}/{GameState.InfluenceCap}   CP: {state.commandPoints.current}\n";

            BuildMeeting(state);

            foreach (var vacancy in state.PlayerCountry.vacancies) BuildVacancyBlock(state, vacancy);
            foreach (var official in state.cabinet) BuildOfficialBlock(state, official);
        }

        void BuildMeeting(GameState state)
        {
            var positions = CabinetMeetingSystem.Build(state);
            if (positions.Count == 0) return;

            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("CABINET MEETING — COMPETING PRIORITIES", W);
            int shown = Math.Min(5, positions.Count);
            for (int i = 0; i < shown; i++)
            {
                var item = positions[i];
                string marker = item.pressure >= 3 ? "!!" : item.pressure == 2 ? "! " : "  ";
                AddText(item.pressure >= 3 ? "sig-hostile" : "terminal-text").text =
                    $" {marker} {item.title.ToUpperInvariant()} — {item.officialName.ToUpperInvariant()}\n" +
                    $"    {item.identity.ToUpperInvariant()} | {item.relationship}\n" +
                    $"    {item.position}\n" +
                    $"    CONCERN: {item.concern}" +
                    (item.resistance >= 2 ? "\n    FRICTION: This office is protective of its own judgement." : "");
            }

            var alignments = CabinetDynamicsSystem.Alignments(state);
            if (alignments.Count > 0)
            {
                AddText("terminal-text-bright").text = " CABINET ALIGNMENTS";
                int count = Math.Min(3, alignments.Count);
                for (int i = 0; i < count; i++)
                {
                    var a = alignments[i];
                    AddText("terminal-text-dim").text =
                        $"  {(a.strength >= 4 ? "!!" : "! ")} {a.first.ToString().ToUpperInvariant()} + {a.second.ToString().ToUpperInvariant()} — {a.basis}";
                }
            }

            AddText("terminal-text-dim").text =
                " THIS IS ADVICE, NOT CONSENSUS. ALIGNMENT SHAPES REACTION; IT DOES NOT REMOVE YOUR AUTHORITY.";
        }

        void BuildVacancyBlock(GameState state, CabinetVacancy vacancy)
        {
            int monthsLeft = CabinetLifecycle.MonthsBeforeGovernmentDecides - vacancy.monthsOpen;
            string cause = vacancy.reason == VacancyReason.Death ? "death in office" : vacancy.reason == VacancyReason.Retirement ? "retirement" : "dismissal";
            AddText("terminal-text-bright").text = AsciiChart.Divider(W) + "\n" +
                $" VACANT — {CabinetLifecycle.TitleFor(state.PlayerCountry, vacancy.office).ToUpperInvariant()}\n" +
                $"  Cause: {cause}. " + (monthsLeft > 0 ? $"The government will appoint in {monthsLeft} month(s) if you do not." : "The government is appointing now.");

            for (int i = 0; i < vacancy.candidates.Count; i++)
            {
                var candidate = vacancy.candidates[i];
                AddText().text = $"  {candidate.displayName}   AGE {candidate.age:F0}\n   {candidate.background}\n" +
                    "   " + AsciiChart.LabeledBar("COMPETENCE", candidate.competence, 100, 12, 16) + "\n" +
                    "   " + AsciiChart.LabeledBar("LOYALTY", candidate.loyalty, 100, 12, 16) + "\n" +
                    "   " + AsciiChart.LabeledBar("RISK APPETITE", candidate.riskTolerance, 100, 13, 16);
                var row = new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
                int captured = i; var office = vacancy.office;
                var button = new Button(() => { GameController.Instance.AppointOfficial(office, captured); Refresh(); }) { text = $"APPOINT {candidate.displayName.ToUpperInvariant()}" };
                button.AddToClassList("cmd-button"); button.AddToClassList("primary"); row.Add(button);
            }
        }

        void BuildOfficialBlock(GameState state, Official official)
        {
            var profile = InstitutionalPersonalitySystem.ProfileFor(state, official);
            string identity = profile == null ? "" : $"\n  IDENTITY: {profile.identity.ToUpperInvariant()}\n  INSTINCT: {profile.instinct}\n  RELATIONSHIP: {profile.relationship}";
            AddText().text = AsciiChart.Divider(W) + "\n" +
                $" {official.displayName}   [{official.office.ToString().ToUpperInvariant()}]   AGE {official.age:F0}   IN OFFICE: {official.monthsInOffice} MO\n" +
                $"  {official.title}\n" +
                "  " + AsciiChart.LabeledBar("COMPETENCE", official.competence, 100, 12, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("LOYALTY", official.loyalty, 100, 12, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("RISK APPETITE", official.riskTolerance, 100, 13, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("TRUST", official.trust, 100, 12, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("REPORTING", ReportingSystem.ReportingQualityFor(state, ReportingSystem.DeskFor(official.office)), 100, 12, 16) + "\n" +
                $"  MODE: {ModeText(official)}\n  {ReportingText(state, official)}" + identity;

            var reading = CabinetDynamicsSystem.ReadIntervention(state, official.office);
            if (reading != null && reading.resistance > 0)
                AddText("terminal-text-dim").text = $"  IF BYPASSED: {reading.reaction}. {reading.cabinetEffect}";

            var row = new VisualElement(); row.AddToClassList("button-row"); Root.Add(row);
            AddModeButton(row, official, ControlMode.Autonomous, "AUTONOMOUS");
            AddModeButton(row, official, ControlMode.Directed, "DIRECTED [1 INF]");
            AddModeButton(row, official, ControlMode.DirectControl, "DIRECT CONTROL");

            if (official.mode == ControlMode.Directed)
            {
                foreach (var directive in CabinetSystem.GetDirectives(official.office))
                {
                    bool current = official.directiveId == directive.id;
                    var button = new Button(() => { GameController.Instance.SetDirective(official, directive.id); Refresh(); }) { text = (current ? "► " : "") + directive.label + (current ? "" : " [1 INF]") };
                    button.AddToClassList("cmd-button"); if (current) button.AddToClassList("primary"); button.SetEnabled(!current); row.Add(button);
                }
                float progress = CabinetSystem.DirectiveProgress(state, official);
                if (progress >= 0f)
                {
                    var definition = CabinetSystem.FindDirective(official.office, official.directiveId);
                    AddFigure(progress >= 1f ? "sig-friendly" : "terminal-text-dim").text = "   " + AsciiChart.LabeledBar("PROGRESS", progress * 100f, 100, 12, 18) + (progress >= 1f ? "  COMPLETE" : "");
                    if (progress >= 1f && definition != null) AddText("sig-friendly").text = "   " + definition.completion;
                }
                else if (!string.IsNullOrEmpty(official.directiveId)) AddText("terminal-text-dim").text = "   A standing instruction — it has no finish line, and will run until changed.";
            }

            if (official.mode == ControlMode.DirectControl)
            {
                var action = CabinetSystem.GetDirectAction(official.office);
                var button = new Button(() => { GameController.Instance.ExecuteDirectAction(official.office); Refresh(); }) { text = $"EXECUTE: {action.label} [{action.cpCost} CP]" };
                button.AddToClassList("cmd-button"); button.AddToClassList("danger"); row.Add(button);
                AddText("terminal-text-dim").text = "  " + action.description + " Bypassing the official erodes trust.";
            }
        }

        void AddModeButton(VisualElement row, Official official, ControlMode mode, string label)
        {
            bool current = official.mode == mode;
            var button = new Button(() => { GameController.Instance.SetControlMode(official, mode); Refresh(); }) { text = (current ? "► " : "") + label };
            button.AddToClassList("cmd-button"); if (current) button.AddToClassList("primary"); button.SetEnabled(!current); row.Add(button);
        }

        static string ReportingText(GameState state, Official official)
        {
            var desk = ReportingSystem.DeskFor(official.office); float chance = ReportingSystem.MishandleChanceFor(state, desk);
            if (chance <= .001f) return official.mode == ControlMode.DirectControl ? "REPORTING: DIRECT — you read this desk yourself." : "REPORTING: RELIABLE — this desk files everything it has.";
            if (chance < .18f) return "REPORTING: SOUND — the occasional item arrives late or understated.";
            if (chance < .35f) return "REPORTING: UNEVEN — routine traffic from this desk is unreliable.";
            return "REPORTING: POOR — much of this desk's traffic never reaches you. Decisions still reach you; awareness does not.";
        }

        static string ModeText(Official official)
        {
            switch (official.mode) { case ControlMode.Directed: return $"DIRECTED — {InstitutionalPersonalitySystem.DirectiveLabel(official)}"; case ControlMode.DirectControl: return "DIRECT OPERATOR CONTROL"; default: return "AUTONOMOUS"; }
        }
    }
}
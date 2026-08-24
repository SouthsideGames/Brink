using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Cabinet management (GDD Phase 3): dossier per official with the three
    /// control modes — Autonomous (free), Directed (Influence), Direct Control
    /// (CP per action). Rebuilt on each refresh; five officials keep it cheap.
    /// </summary>
    public class CabinetView : TerminalView
    {
        public override string Id => "CABINET";
        public override string ShortCode => "CAB";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;

            Root.Clear();

            var headline = AddText("terminal-text-bright");
            headline.text = AsciiChart.BoxHeader("CABINET — PILLAR LEADERSHIP", W) + "\n" +
                            $" INFLUENCE: {state.influence}/{GameState.InfluenceCap}   " +
                            $"CP: {state.commandPoints.current}\n";

            // Vacancies first — an empty ministry is the most urgent thing on
            // this screen, and burying it under five filled dossiers would make
            // the operator scroll to find the only decision here.
            foreach (var vacancy in state.PlayerCountry.vacancies)
                BuildVacancyBlock(state, vacancy);

            foreach (var official in state.cabinet)
                BuildOfficialBlock(state, official);
        }

        void BuildVacancyBlock(GameState state, CabinetVacancy vacancy)
        {
            var header = AddText("terminal-text-bright");
            int monthsLeft = CabinetLifecycle.MonthsBeforeGovernmentDecides - vacancy.monthsOpen;
            string cause = vacancy.reason == VacancyReason.Death ? "death in office"
                : vacancy.reason == VacancyReason.Retirement ? "retirement" : "dismissal";

            header.text =
                AsciiChart.Divider(W) + "\n" +
                $" VACANT — {CabinetLifecycle.TitleFor(state.PlayerCountry, vacancy.office).ToUpperInvariant()}\n" +
                $"  Cause: {cause}. " +
                (monthsLeft > 0
                    ? $"The government will appoint in {monthsLeft} month(s) if you do not."
                    : "The government is appointing now.");

            for (int i = 0; i < vacancy.candidates.Count; i++)
            {
                var candidate = vacancy.candidates[i];
                var dossier = AddText();
                dossier.text =
                    $"  {candidate.displayName}   AGE {candidate.age:F0}\n" +
                    $"   {candidate.background}\n" +
                    "   " + AsciiChart.LabeledBar("COMPETENCE", candidate.competence, 100, 12, 16) + "\n" +
                    "   " + AsciiChart.LabeledBar("LOYALTY", candidate.loyalty, 100, 12, 16) + "\n" +
                    "   " + AsciiChart.LabeledBar("RISK APPETITE", candidate.riskTolerance, 100, 13, 16);

                var row = new VisualElement();
                row.AddToClassList("button-row");
                Root.Add(row);

                int captured = i;
                var office = vacancy.office;
                var button = new Button(() =>
                {
                    GameController.Instance.AppointOfficial(office, captured);
                    Refresh();
                })
                { text = $"APPOINT {candidate.displayName.ToUpperInvariant()}" };
                button.AddToClassList("cmd-button");
                button.AddToClassList("primary");
                row.Add(button);
            }
        }

        void BuildOfficialBlock(GameState state, Official official)
        {
            var dossier = AddText();
            dossier.text =
                AsciiChart.Divider(W) + "\n" +
                $" {official.displayName}   [{official.office.ToString().ToUpperInvariant()}]   " +
                $"AGE {official.age:F0}   IN OFFICE: {official.monthsInOffice} MO\n" +
                $"  {official.title}\n" +
                "  " + AsciiChart.LabeledBar("COMPETENCE", official.competence, 100, 12, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("LOYALTY", official.loyalty, 100, 12, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("RISK APPETITE", official.riskTolerance, 100, 13, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("TRUST", official.trust, 100, 12, 16) + "\n" +
                "  " + AsciiChart.LabeledBar("REPORTING",
                    ReportingSystem.ReportingQualityFor(state, ReportingSystem.DeskFor(official.office)),
                    100, 12, 16) + "\n" +
                $"  MODE: {ModeText(official)}\n" +
                $"  {ReportingText(state, official)}";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            AddModeButton(row, official, ControlMode.Autonomous, "AUTONOMOUS");
            AddModeButton(row, official, ControlMode.Directed, "DIRECTED [1 INF]");
            AddModeButton(row, official, ControlMode.DirectControl, "DIRECT CONTROL");

            if (official.mode == ControlMode.Directed)
            {
                var directives = CabinetSystem.GetDirectives(official.office);
                foreach (var directive in directives)
                {
                    bool current = official.directiveId == directive.id;
                    var button = new Button(() =>
                    {
                        GameController.Instance.SetDirective(official, directive.id);
                        Refresh();
                    })
                    { text = (current ? "► " : "") + directive.label + (current ? "" : " [1 INF]") };
                    button.AddToClassList("cmd-button");
                    if (current) button.AddToClassList("primary");
                    button.SetEnabled(!current);
                    row.Add(button);
                }

                // How far along the standing instruction is.
                //
                // An order used to be issued into silence: the operator told the
                // defence minister to prepare for war and had no way of knowing
                // when the force was ready, so they either stopped too early or
                // kept paying for an instruction that had already finished. A bar
                // here answers "how long?" and the briefing answers "it's done".
                float progress = CabinetSystem.DirectiveProgress(
                    GameController.Instance.State, official);

                if (progress >= 0f)
                {
                    var definition = CabinetSystem.FindDirective(official.office, official.directiveId);
                    AddFigure(progress >= 1f ? "sig-friendly" : "terminal-text-dim").text =
                        "   " + AsciiChart.LabeledBar("PROGRESS", progress * 100f, 100, 12, 18)
                        + (progress >= 1f ? "  COMPLETE" : "");

                    if (progress >= 1f && definition != null)
                        AddText("sig-friendly").text = "   " + definition.completion;
                }
                else if (!string.IsNullOrEmpty(official.directiveId))
                {
                    // Say so, rather than leaving a gap where a bar might belong.
                    AddText("terminal-text-dim").text =
                        "   A standing instruction — it has no finish line, and will run until changed.";
                }
            }

            if (official.mode == ControlMode.DirectControl)
            {
                var action = CabinetSystem.GetDirectAction(official.office);
                var button = new Button(() =>
                {
                    GameController.Instance.ExecuteDirectAction(official.office);
                    Refresh();
                })
                { text = $"EXECUTE: {action.label} [{action.cpCost} CP]" };
                button.AddToClassList("cmd-button");
                button.AddToClassList("danger");
                row.Add(button);

                var hint = new Label("  " + action.description + " Bypassing the official erodes trust.");
                hint.AddToClassList("terminal-text");
                hint.AddToClassList("terminal-text-dim");
                Root.Add(hint);
            }
        }

        void AddModeButton(VisualElement row, Official official, ControlMode mode, string label)
        {
            bool current = official.mode == mode;
            var button = new Button(() =>
            {
                GameController.Instance.SetControlMode(official, mode);
                Refresh();
            })
            { text = (current ? "► " : "") + label };
            button.AddToClassList("cmd-button");
            if (current) button.AddToClassList("primary");
            button.SetEnabled(!current);
            row.Add(button);
        }

        /// <summary>
        /// Say plainly what this desk's reporting is costing the operator
        /// (GDD §28.1). The filter has to be predictable: an information
        /// penalty the player cannot see coming reads as the game being broken,
        /// not as a consequence of who they appointed.
        /// </summary>
        static string ReportingText(GameState state, Official official)
        {
            var desk = ReportingSystem.DeskFor(official.office);
            float chance = ReportingSystem.MishandleChanceFor(state, desk);

            if (chance <= 0.001f)
                return official.mode == ControlMode.DirectControl
                    ? "REPORTING: DIRECT — you read this desk yourself."
                    : "REPORTING: RELIABLE — this desk files everything it has.";

            if (chance < 0.18f)
                return "REPORTING: SOUND — the occasional item arrives late or understated.";
            if (chance < 0.35f)
                return "REPORTING: UNEVEN — routine traffic from this desk is unreliable.";

            return "REPORTING: POOR — much of this desk's traffic never reaches you. "
                   + "Decisions still reach you; awareness does not.";
        }

        static string ModeText(Official official)
        {
            switch (official.mode)
            {
                case ControlMode.Directed: return $"DIRECTED — {official.directiveId}";
                case ControlMode.DirectControl: return "DIRECT OPERATOR CONTROL";
                default: return "AUTONOMOUS";
            }
        }
    }
}

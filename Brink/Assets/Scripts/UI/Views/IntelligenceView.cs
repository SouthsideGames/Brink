using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Intelligence command (GDD Phase 6, §14): the estimate board with
    /// confidence grades, collection networks, covert operations, and the
    /// defensive side — counterintelligence and deception.
    /// </summary>
    public class IntelligenceView : TerminalView
    {
        public override string Id => "INTELLIGENCE";
        public override string ShortCode => "INT";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string selectedTargetId;
        IntelDomain selectedFocus = IntelDomain.Military;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();
            AddAuthorityBadge(state, Pillar.Intelligence);
            AddCabinetAdvice(state, Pillar.Intelligence);
            BuildEstimateBoard(state);
            BuildDefensivePosture(state, player);
            BuildNetworkControls(state);
        }

        void BuildEstimateBoard(GameState state)
        {
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("ANALYTICAL ESTIMATE BOARD", W));
            sb.AppendLine(" Foreign capability is estimated, never observed. Ranges widen with");
            sb.AppendLine(" poor access; grades fall as reporting goes stale.");
            sb.AppendLine();

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var network = state.FindNetwork(state.playerCountryId, country.id);
                string networkStatus = network == null
                    ? "NO NETWORK"
                    : network.compromised
                        ? $"COMPROMISED (PEN {network.penetration:F0})"
                        : $"PEN {network.penetration:F0}  FOCUS {network.focus.ToString().ToUpperInvariant()}";

                sb.AppendLine($" {country.displayName.ToUpperInvariant()}   [{networkStatus}]");
                foreach (IntelDomain domain in System.Enum.GetValues(typeof(IntelDomain)))
                    sb.AppendLine($"   {domain.ToString().ToUpperInvariant(),-11} {IntelReadout.ForDomain(state, country.id, domain)}");
                sb.AppendLine();
            }
            text.text = sb.ToString();
        }

        void BuildDefensivePosture(GameState state, CountryState player)
        {
            var ci = player.counterIntel;
            var text = AddText();
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("DEFENSIVE POSTURE", W));
            sb.AppendLine("  " + AsciiChart.LabeledBar("COUNTERINTEL", ci.counterIntelligence, 100, 14, 20));
            sb.AppendLine("  " + AsciiChart.LabeledBar("DECEPTION", ci.deceptionStrength, 100, 14, 20));
            if (ci.deceptionStrength > 0f)
                sb.AppendLine($"   PROGRAM: {(ci.deceptionBias >= 0 ? "OVERSTATE" : "UNDERSTATE")} " +
                              $"{ci.deceptionDomain.ToString().ToUpperInvariant()} CAPABILITY");

            int hostile = 0;
            foreach (var network in state.networks)
                if (network.targetId == player.id && !network.compromised) hostile++;
            sb.AppendLine($"   KNOWN HOSTILE NETWORKS ACTIVE: {(hostile > 0 ? hostile.ToString() : "NONE DETECTED")}");
            text.text = sb.ToString();

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            AddButton(row, "COUNTERINTEL SWEEP [1 CP]", "primary", () =>
            {
                GameController.Instance.StrengthenCounterIntelligence();
                Refresh();
            });
            if (IntelligenceSystem.CanRunCovertOperation(state, null, CovertOperation.Deception,
                    out string deceptionBlocked))
            {
                AddButton(row, $"DECEPTION: OVERSTATE [{IntelligenceSystem.CovertOperationCost} CP]", null, () =>
                {
                    GameController.Instance.RunCovertOperation(null, CovertOperation.Deception, 1f);
                    Refresh();
                });
                AddButton(row, $"DECEPTION: UNDERSTATE [{IntelligenceSystem.CovertOperationCost} CP]", null, () =>
                {
                    GameController.Instance.RunCovertOperation(null, CovertOperation.Deception, -1f);
                    Refresh();
                });
            }
            else
            {
                AddText("terminal-text-dim").text = $"  DECEPTION UNAVAILABLE: {deceptionBlocked}";
            }
        }

        /// <summary>
        /// Who actually runs the target's ministries (GDD §8, §14).
        ///
        /// Every government has a cabinet now, and a foreign one is exactly the
        /// sort of thing collection is for — a rival's economy being run by
        /// someone incompetent is a real, exploitable fact about them. It is
        /// also the only place the player can ever see that foreign cabinets
        /// exist, and a system with no surface reads as no system.
        ///
        /// Strictly fog-gated, and gated in *stages*: names come first, because
        /// who holds an office is close to public; judging how good they are at
        /// it takes real penetration. Nothing here prints a foreign country's
        /// true value without collection behind it.
        /// </summary>
        void BuildForeignCabinetDossier(GameState state, IntelNetwork network)
        {
            var target = state.FindCountry(selectedTargetId);
            if (target == null || target.cabinet.Count == 0) return;

            float penetration = network != null && !network.compromised ? network.penetration : 0f;

            var header = AddText("terminal-text-bright");
            header.text = $"\n {target.displayName.ToUpperInvariant()} — GOVERNMENT PERSONNEL";

            if (penetration < 20f)
            {
                AddText("terminal-text-dim").text =
                    "   No reporting. Establish and deepen a network to identify their ministers.";
                return;
            }

            bool assessable = penetration >= 55f;
            int nameWidth = AsciiChart.NameWidth(W - 6, 0.45f);

            var sb = new System.Text.StringBuilder();
            foreach (var official in target.cabinet)
            {
                string office = official.office.ToString().ToUpperInvariant();
                string assessment = assessable
                    ? CompetenceBand(official.competence)
                    : "UNASSESSED";
                sb.AppendLine($"   {office,-13} {AsciiChart.Cell(official.displayName, nameWidth)}  {assessment}");
            }

            // A figure, not prose: the columns are built to an exact grid and
            // the shell's wrapper would corrupt them.
            AddFigure().text = sb.ToString().TrimEnd();

            if (!assessable)
                AddText("terminal-text-dim").text =
                    "   Identities only. Judging their competence needs deeper access.";
        }

        /// <summary>
        /// A band, never a number. An estimate that printed 63.4 would be
        /// claiming a precision collection does not have.
        /// </summary>
        static string CompetenceBand(float competence)
        {
            if (competence >= 70f) return "CAPABLE";
            if (competence >= 50f) return "ADEQUATE";
            if (competence >= 35f) return "WEAK";
            return "OUT OF DEPTH";
        }

        void BuildNetworkControls(GameState state)
        {
            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("COLLECTION & COVERT ACTION", W);

            if (selectedTargetId == null)
                foreach (var country in state.countries)
                    if (!country.isPlayer) { selectedTargetId = country.id; break; }

            var targetRow = new VisualElement();
            targetRow.AddToClassList("button-row");
            Root.Add(targetRow);
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var captured = country;
                bool current = selectedTargetId == country.id;
                var button = new Button(() => { selectedTargetId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + country.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                targetRow.Add(button);
            }

            var network = state.FindNetwork(state.playerCountryId, selectedTargetId);

            BuildForeignCabinetDossier(state, network);

            var focusRow = new VisualElement();
            focusRow.AddToClassList("button-row");
            Root.Add(focusRow);
            foreach (IntelDomain domain in System.Enum.GetValues(typeof(IntelDomain)))
            {
                var captured = domain;
                bool current = network != null ? network.focus == domain : selectedFocus == domain;
                var button = new Button(() =>
                {
                    selectedFocus = captured;
                    if (network != null) GameController.Instance.SetIntelFocus(selectedTargetId, captured);
                    Refresh();
                })
                { text = (current ? "► " : "") + domain.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                focusRow.Add(button);
            }

            var actionRow = new VisualElement();
            actionRow.AddToClassList("button-row");
            Root.Add(actionRow);

            if (network == null)
            {
                AddButton(actionRow, $"ESTABLISH NETWORK [{IntelligenceSystem.EstablishNetworkCost} CP]", "primary", () =>
                {
                    GameController.Instance.EstablishNetwork(selectedTargetId, selectedFocus);
                    Refresh();
                });
            }
            else
            {
                AddButton(actionRow, $"EXPAND NETWORK [{IntelligenceSystem.ExpandNetworkCost} CP]", null, () =>
                {
                    GameController.Instance.ExpandNetwork(selectedTargetId);
                    Refresh();
                });
                foreach (CovertOperation operation in System.Enum.GetValues(typeof(CovertOperation)))
                {
                    if (operation == CovertOperation.Deception) continue;
                    var captured = operation;
                    AddButton(actionRow, $"{Phrase.Caps(operation)} [{IntelligenceSystem.CovertOperationCost} CP]", "danger", () =>
                    {
                        GameController.Instance.RunCovertOperation(selectedTargetId, captured);
                        Refresh();
                    });
                }
            }

            var hint = AddText("terminal-text-dim");
            hint.text =
                "  Covert action can fail, and can be exposed even when it succeeds.\n" +
                "  Exposure costs diplomatic standing and hardens the target's services.";
        }

        void AddButton(VisualElement row, string text, string extraClass, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cmd-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            row.Add(button);
        }
    }
}

using System.Collections.Generic;
using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Diplomatic command (GDD Phase 7, §15): the six-dimension relationship
    /// board, treaties with explicit commitments, and coalition assembly.
    /// </summary>
    public class DiplomacyView : TerminalView
    {
        public override string Id => "DIPLOMACY";
        public override string ShortCode => "DIP";

        /// <summary>Orders here are Diplomacy pillar orders — see TerminalView.GateOnAuthority.</summary>
        protected override Pillar? CommandPillar => Pillar.Diplomacy;

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string selectedTargetId;
        readonly HashSet<TreatyCommitment> draftCommitments = new HashSet<TreatyCommitment>();

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            if (state.PlayerCountry == null) return;

            Root.Clear();
            AddAuthorityBadge(state, Pillar.Diplomacy);
            AddCabinetAdvice(state, Pillar.Diplomacy);
            BuildRelationshipBoard(state);
            BuildTargetSelector(state);
            BuildTreatyControls(state);
            BuildAccessionControls(state);
            BuildCoalitionControls(state);
        }

        /// <summary>
        /// Bringing a friend into the union (GDD §15.1).
        ///
        /// Deliberately on the DIPLOMACY panel and not INTELLIGENCE. It reads as
        /// a covert instrument, but everything it runs on — trust, dependence,
        /// years of being a good partner — is built here, and putting it here is
        /// the honest statement of what it costs. The operator should see the
        /// option sitting directly beneath the treaties that made it possible.
        /// </summary>
        void BuildAccessionControls(GameState state)
        {
            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("ACCESSION", W);

            var running = AccessionSystem.FindCampaign(state, state.playerCountryId, selectedTargetId);
            var target = state.FindCountry(selectedTargetId);

            if (running != null)
            {
                var status = AddText();
                status.text =
                    $"  EFFORT UNDER WAY IN {target?.displayName.ToUpperInvariant()}\n" +
                    "  " + AsciiChart.LabeledBar("CONSENT", running.progress, 100, 12, 20) + "\n" +
                    $"  {Phrase.Caps(running.route)} ROUTE   {running.monthsRunning} MO   " +
                    (running.exposed ? "EXPOSED" : "UNDETECTED");

                AddText("terminal-text-dim").text = running.exposed
                    ? "  They know. The argument is far harder to make now, and the goodwill it "
                      + "rests on is going."
                    : "  It holds only while they still trust us. Let the friendship lapse and "
                      + "the effort collapses with it.";

                var row = MakeRow();
                AddButton(row, "CALL IT OFF", "danger", () =>
                {
                    GameController.Instance.AbandonAccession(selectedTargetId);
                    Refresh();
                });
                return;
            }

            var routeRow = MakeRow();
            foreach (AccessionRoute route in System.Enum.GetValues(typeof(AccessionRoute)))
            {
                var captured = route;
                bool allowed = AccessionSystem.CanBegin(
                    state, state.playerCountryId, selectedTargetId, route, out string blocked);

                AddButton(routeRow,
                    $"{Phrase.Caps(route)} [{AccessionSystem.OpenCost} CP]",
                    allowed ? "primary" : null,
                    () =>
                    {
                        GameController.Instance.BeginAccession(selectedTargetId, captured);
                        Refresh();
                    });

                if (!allowed)
                    AddText("terminal-text-dim").text = $"   {Phrase.Caps(route)}: {blocked}";
            }

            AddText("terminal-text-dim").text =
                "  A country that trusts us, relies on us, and has stopped believing in itself " +
                "can be argued into our union rather than taken. It costs no soldiers.\n" +
                "  What it costs is every other friendship we have: absorbing a partner tells " +
                "every remaining partner exactly what our friendship is worth, and the closer " +
                "they were, the harder they will take it.";
        }

        void BuildRelationshipBoard(GameState state)
        {
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("RELATIONSHIP BOARD", W));

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var relationship = state.FindRelationship(state.playerCountryId, country.id);
                if (relationship == null) continue;

                var status = DiplomacySystem.StatusOf(state, state.playerCountryId, country.id);
                var treaty = state.FindTreaty(state.playerCountryId, country.id);

                sb.AppendLine($" {country.displayName.ToUpperInvariant()}   [{StatusText(status)}]");
                sb.AppendLine("   " + AsciiChart.LabeledBar("RELATIONS", relationship.relations, 100, 12, 16));
                sb.AppendLine("   " + AsciiChart.LabeledBar("TRUST", relationship.trust, 100, 12, 16));
                sb.AppendLine("   " + AsciiChart.LabeledBar("ALIGNMENT", relationship.strategicAlignment, 100, 12, 16));
                sb.AppendLine("   " + AsciiChart.LabeledBar("OUR DEPEND.", relationship.DependenceOf(state.playerCountryId), 100, 12, 16));
                sb.AppendLine("   " + AsciiChart.LabeledBar("THEIR DEPEND", relationship.DependenceOf(country.id), 100, 12, 16));
                sb.AppendLine("   " + AsciiChart.LabeledBar("THEY FEAR US", relationship.ThreatPerceivedBy(country.id), 100, 12, 16));

                if (treaty != null)
                {
                    var commitments = new List<string>();
                    foreach (var commitment in treaty.commitments)
                        commitments.Add(Phrase.Caps(commitment));
                    sb.AppendLine($"   TREATY: {string.Join(", ", commitments)}");
                }

                if (relationship.memory.Count > 0)
                {
                    sb.AppendLine("   HISTORICAL MEMORY:");
                    int start = System.Math.Max(0, relationship.memory.Count - 3);
                    for (int i = start; i < relationship.memory.Count; i++)
                        sb.AppendLine($"     {relationship.memory[i]}");
                }
                sb.AppendLine();
            }
            text.text = sb.ToString();
        }

        void BuildTargetSelector(GameState state)
        {
            if (selectedTargetId == null)
                foreach (var country in state.countries)
                    if (!country.isPlayer) { selectedTargetId = country.id; break; }

            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("DIPLOMATIC ACTION", W);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var captured = country;
                bool current = selectedTargetId == country.id;
                var button = new Button(() => { selectedTargetId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + country.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }

            var actionRow = new VisualElement();
            actionRow.AddToClassList("button-row");
            Root.Add(actionRow);
            AddButton(actionRow, $"OUTREACH [{DiplomacySystem.DiplomaticOutreachCost} CP]", "primary", () =>
            {
                GameController.Instance.DiplomaticOutreach(selectedTargetId);
                Refresh();
            });

            if (state.FindTreaty(state.playerCountryId, selectedTargetId) != null)
            {
                AddButton(actionRow, "BREAK TREATY", "danger", () =>
                {
                    GameController.Instance.BreakTreaty(selectedTargetId);
                    Refresh();
                });
            }
        }

        void BuildTreatyControls(GameState state)
        {
            if (state.FindTreaty(state.playerCountryId, selectedTargetId) != null) return;

            AddText().text = "\n DRAFT COMMITMENTS";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            foreach (TreatyCommitment commitment in System.Enum.GetValues(typeof(TreatyCommitment)))
            {
                var captured = commitment;
                bool selected = draftCommitments.Contains(commitment);
                var button = new Button(() =>
                {
                    if (!draftCommitments.Remove(captured)) draftCommitments.Add(captured);
                    Refresh();
                })
                { text = (selected ? "[X] " : "[ ] ") + Phrase.Caps(commitment) };
                button.AddToClassList("cmd-button");
                if (selected) button.AddToClassList("primary");
                row.Add(button);
            }

            var proposeRow = new VisualElement();
            proposeRow.AddToClassList("button-row");
            Root.Add(proposeRow);

            var draft = new List<TreatyCommitment>(draftCommitments);
            float willingness = draft.Count > 0
                ? DiplomacySystem.TreatyWillingness(state, selectedTargetId, draft)
                : 0f;

            var propose = new Button(() =>
            {
                GameController.Instance.ProposeTreaty(selectedTargetId, new List<TreatyCommitment>(draftCommitments));
                Refresh();
            })
            { text = $"PROPOSE TREATY [{DiplomacySystem.TreatyProposalCost} CP]" };
            propose.AddToClassList("cmd-button");
            propose.AddToClassList("primary");
            propose.SetEnabled(draft.Count > 0);
            proposeRow.Add(propose);

            var assessment = AddText("terminal-text-dim");
            assessment.text = draft.Count == 0
                ? "  Select commitments to draft a proposal."
                : $"  ESTIMATED RECEPTION: {ReceptionText(willingness)}\n" +
                  "  Heavier commitments require a warmer relationship. A state that\n" +
                  "  fears you, or that is close to your opponent, will refuse.";
        }

        void BuildCoalitionControls(GameState state)
        {
            var confrontation = state.ActiveConfrontation;
            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("COALITION", W);

            if (confrontation == null)
            {
                AddText("terminal-text-dim").text = "  No active confrontation. Coalitions form around a confrontation.";
                return;
            }

            var coalition = state.FindCoalition(confrontation.id);
            var sb = new StringBuilder();
            if (coalition == null)
            {
                sb.AppendLine($"  TARGET: {state.FindCountry(confrontation.OpponentOf(state.playerCountryId))?.displayName}");
                sb.AppendLine("  PROJECTED RESPONSES:");
                foreach (var country in state.countries)
                {
                    if (country.isPlayer || country.id == confrontation.OpponentOf(state.playerCountryId)) continue;
                    float willingness = DiplomacySystem.CoalitionWillingness(state,
                        state.playerCountryId, country.id,
                        confrontation.OpponentOf(state.playerCountryId));
                    sb.AppendLine($"   {country.displayName.ToUpperInvariant(),-18} {ReceptionText(willingness)}");
                }
                AddText().text = sb.ToString();

                var row = new VisualElement();
                row.AddToClassList("button-row");
                Root.Add(row);
                AddButton(row, $"REQUEST COALITION [{DiplomacySystem.CoalitionRequestCost} CP]", "primary", () =>
                {
                    GameController.Instance.RequestCoalition();
                    Refresh();
                });
            }
            else
            {
                sb.AppendLine($"  COALITION AGAINST {state.FindCountry(coalition.targetId)?.displayName.ToUpperInvariant()}");
                foreach (var memberId in coalition.memberIds)
                {
                    var member = state.FindCountry(memberId);
                    sb.AppendLine($"   {(memberId == coalition.leaderId ? "►" : " ")}{member?.displayName.ToUpperInvariant()}");
                }
                sb.AppendLine($"  ADDED STRENGTH: {DiplomacySystem.CoalitionStrength(state, confrontation):F1}");
                sb.AppendLine("  Partners may withdraw if the confrontation turns against their interests.");
                AddText().text = sb.ToString();
            }
        }

        static string StatusText(RelationshipStatus status)
        {
            switch (status)
            {
                case RelationshipStatus.StrategicPartner: return "STRATEGIC PARTNER";
                default: return Phrase.Caps(status);
            }
        }

        static string ReceptionText(float willingness)
        {
            if (willingness >= 70f) return "ENTHUSIASTIC";
            if (willingness >= 50f) return "RECEPTIVE";
            if (willingness >= 35f) return "HESITANT";
            if (willingness >= 15f) return "RELUCTANT";
            return "OPPOSED";
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
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

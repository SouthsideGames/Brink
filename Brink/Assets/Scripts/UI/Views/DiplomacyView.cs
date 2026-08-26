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

        /// <summary>The treaty being negotiated: what each side would carry.</summary>
        readonly List<TreatyClause> draftClauses = new List<TreatyClause>();

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

        /// <summary>
        /// Grow a standing treaty (spec 04 §5a). This panel used to go silent the
        /// moment a treaty existed, which made the first signature per pair the
        /// last — a friendship could never become an alliance.
        /// </summary>
        void BuildDeepeningControls(GameState state, Treaty standing)
        {
            AddText().text = "\n STANDING AGREEMENT — DEEPEN IT";

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            bool anyMissing = false;
            foreach (TreatyCommitment commitment in System.Enum.GetValues(typeof(TreatyCommitment)))
            {
                if (standing.Has(commitment)) continue;
                anyMissing = true;
                var captured = commitment;

                float reading = DiplomacySystem.TreatyWillingness(state, state.playerCountryId,
                    selectedTargetId, new List<TreatyCommitment> { captured }) + 8f;
                var button = new Button(() =>
                {
                    GameController.Instance.DeepenTreaty(selectedTargetId, captured);
                    Refresh();
                })
                { text = $"ADD {Phrase.Caps(captured)} [{DiplomacySystem.TreatyProposalCost} CP]" };
                button.AddToClassList("cmd-button");
                if (reading < 40f)
                    Block(button, "THE RELATIONSHIP IS NOT THERE YET FOR THIS COMMITMENT.");
                row.Add(button);
            }

            AddText("terminal-text-dim").text = anyMissing
                ? "  A partner with history signs what a stranger would not — and a pact "
                  + "added here answers to bloc politics like any pact."
                : "  Every commitment is already in force. This is as deep as treaties go.";
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

            // Their measures against us end at a table, not a countdown: the
            // automatic lapse needs relations the sanctions themselves suppress.
            if (state.FindSanction(selectedTargetId, state.playerCountryId) != null)
            {
                var relief = AddButton(actionRow, "SEEK SANCTIONS RELIEF [2 CP]", null, () =>
                {
                    GameController.Instance.SeekSanctionsRelief(selectedTargetId);
                    Refresh();
                });
                float reliefReading = EconomySystem.ReliefWillingness(
                    state, selectedTargetId, state.playerCountryId);
                if (reliefReading < 35f)
                    Block(relief, "THEY ARE NOT PERSUADABLE YET — THREAT AND WARMTH DECIDE THIS.");
            }

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
            var standing = state.FindTreaty(state.playerCountryId, selectedTargetId);
            if (standing != null)
            {
                BuildDeepeningControls(state, standing);
                return;
            }

            var player = state.PlayerCountry;

            AddText().text = "\n DRAFT COMMITMENTS";

            // What the foreign ministry thinks we should be asking for. The
            // diplomat is the one official whose job is knowing where the country
            // is exposed, and until now they had nothing to say about it.
            var suggested = DiplomacySystem.SuggestedCommitmentFor(player);
            AddText("sig-advice").text =
                $" ★ THE MINISTRY RECOMMENDS: {Phrase.Caps(suggested)}. "
                + DiplomacySystem.SuggestionReason(player, suggested);

            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            // **Each clause says who carries it.** A treaty used to be a flat list
            // both sides implicitly received, so every agreement was symmetrical by
            // construction and there was nothing to negotiate. Cycling a clause
            // through MUTUAL → THEY → WE → off is the whole back-and-forth: what
            // are we asking them to bear, and what are we prepared to bear.
            foreach (TreatyCommitment commitment in System.Enum.GetValues(typeof(TreatyCommitment)))
            {
                var captured = commitment;
                var existing = FindClause(captured);

                string marker = existing == null ? "[ ]"
                    : existing.side == ClauseSide.Mutual ? "[BOTH]"
                    : existing.side == ClauseSide.TheyProvide ? "[THEY]"
                    : "[WE]";

                var button = new Button(() => { CycleClause(captured); Refresh(); })
                {
                    text = $"{marker} {Phrase.Caps(captured)}"
                           + (captured == suggested ? " ★" : "")
                };
                button.AddToClassList("cmd-button");
                if (existing != null) button.AddToClassList("primary");
                row.Add(button);
            }

            var proposeRow = new VisualElement();
            proposeRow.AddToClassList("button-row");
            Root.Add(proposeRow);

            float willingness = draftClauses.Count > 0
                ? DiplomacySystem.TreatyWillingness(state, state.playerCountryId,
                    selectedTargetId, draftClauses)
                : 0f;
            float balance = DiplomacySystem.BalanceOf(draftClauses);

            var propose = new Button(() =>
            {
                GameController.Instance.ProposeNegotiatedTreaty(
                    selectedTargetId, new List<TreatyClause>(draftClauses));
                Refresh();
            })
            { text = $"PROPOSE TREATY [{DiplomacySystem.TreatyProposalCost} CP]" };
            propose.AddToClassList("cmd-button");
            propose.AddToClassList("primary");
            if (draftClauses.Count == 0)
                Block(propose, "NO TERMS DRAFTED.");
            proposeRow.Add(propose);

            if (draftClauses.Count == 0)
            {
                AddText("terminal-text-dim").text =
                    "  Cycle a commitment through BOTH / THEY / WE to draft terms.";
                return;
            }

            AddText(balance >= 6f ? "sig-hostile" : "terminal-text-dim").text =
                $"  BALANCE: {DiplomacySystem.DescribeBalance(balance)}.";
            AddText("terminal-text-dim").text =
                $"  ESTIMATED RECEPTION: {ReceptionText(willingness)}";

            // The trade-off, stated where the decision is made.
            AddText("terminal-text-dim").text =
                $"  OUR NAME AS A PARTNER: {player.reciprocity:F0}. A state that needs us will "
                + "sign terms a self-sufficient one would refuse — and everyone else will price "
                + "that in the next time we ask them for something.";
        }

        /// <summary>The clause for a commitment in the current draft, or null.</summary>
        TreatyClause FindClause(TreatyCommitment commitment)
        {
            foreach (var clause in draftClauses)
                if (clause.commitment == commitment) return clause;
            return null;
        }

        /// <summary>
        /// Cycle a commitment: off → both carry it → they carry it → we carry it
        /// → off. Four states on one control, because a negotiation screen with
        /// six commitments and three separate toggles each is unreadable on a
        /// phone.
        /// </summary>
        void CycleClause(TreatyCommitment commitment)
        {
            var existing = FindClause(commitment);
            if (existing == null)
            {
                draftClauses.Add(new TreatyClause { commitment = commitment, side = ClauseSide.Mutual });
                return;
            }

            switch (existing.side)
            {
                case ClauseSide.Mutual: existing.side = ClauseSide.TheyProvide; break;
                case ClauseSide.TheyProvide: existing.side = ClauseSide.WeProvide; break;
                default: draftClauses.Remove(existing); break;
            }
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
    }
}

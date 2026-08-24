using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Negotiated settlement (GDD §26).
    ///
    /// There is no surrender button. A peace is assembled from terms, and the
    /// other side prices each one separately against how much it currently wants
    /// the war to stop. Demands cost them; concessions buy goodwill. Ask for
    /// everything and you will get nothing — overreaching prolongs a war that
    /// could otherwise have ended.
    /// </summary>
    public static class PeaceSystem
    {
        /// <summary>
        /// Is this term something the proposer *extracted*, or something they
        /// gave up?
        ///
        /// The enum is already ordered demands-then-concessions, but that is a
        /// convention a reader has to notice rather than a rule the code states.
        /// Naming it here means the war-verdict logic and the pricing logic agree
        /// about which way a term points, instead of each deciding for itself.
        ///
        /// Exhaustive by design — a term added without a row here will not
        /// compile past the switch, which is the same guard `CrisisEffects` uses.
        /// </summary>
        public static bool IsDemand(PeaceTerm term)
        {
            switch (term)
            {
                case PeaceTerm.TerritorialCession:
                case PeaceTerm.Reparations:
                case PeaceTerm.Demilitarization:
                case PeaceTerm.ResourceAccess:
                case PeaceTerm.Recognition:
                case PeaceTerm.TreatyRevision:
                case PeaceTerm.PoliticalConcessions:
                    return true;

                case PeaceTerm.Withdrawal:
                case PeaceTerm.SanctionsRelief:
                case PeaceTerm.PrisonerExchange:
                case PeaceTerm.SecurityGuarantee:
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// What a term costs the side being asked to accept it. Negative values
        /// are concessions — things that make a settlement *more* attractive.
        /// </summary>
        public static float TermCost(GameState state, Confrontation confrontation,
            string proposerId, PeaceTerm term)
        {
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));

            switch (term)
            {
                case PeaceTerm.TerritorialCession:
                {
                    var location = state.FindLocation(confrontation.objectiveLocationId);
                    if (location == null) return 25f;

                    // Ceding the seat of state ends state continuity (GDD §22).
                    if (location.type == LocationType.Capital) return 200f;

                    float cost = 30f + location.strategicValue * 0.35f;
                    // Ground we already hold is far easier to sign away.
                    if (location.ownerId == proposerId) cost -= 28f;
                    return cost;
                }

                case PeaceTerm.Reparations:
                    // A ruined treasury cannot pay, and resents being asked.
                    return 28f + Math.Max(0f, 40f - opponent.economy.growthRate * 5f) * 0.2f;

                case PeaceTerm.Demilitarization:
                    // The more dangerous their neighbourhood, the less thinkable.
                    return 35f + opponent.pillars.military * 0.2f;

                case PeaceTerm.ResourceAccess:
                    return 18f;

                case PeaceTerm.Recognition:
                    return 14f;

                case PeaceTerm.TreatyRevision:
                    return 22f;

                case PeaceTerm.PoliticalConcessions:
                    // Priced on how much authority the government has left to
                    // spend. A popular, cohesive administration can survive
                    // being seen to change course; a fragile one cannot, and
                    // will fight on rather than sign its own humiliation.
                    return 16f + opponent.governmentApproval * 0.22f
                               + (100f - opponent.stability) * 0.12f;

                // ---- concessions ----
                case PeaceTerm.Withdrawal:
                {
                    // Only worth something if we actually hold ground of theirs.
                    float value = 0f;
                    foreach (var location in state.locations)
                        if (location.ownerId == proposerId && location.originalOwnerId == opponent.id)
                            value -= 24f + location.strategicValue * 0.2f;
                    return value;
                }

                case PeaceTerm.SanctionsRelief:
                    return state.FindSanction(proposerId, opponent.id) != null ? -26f : 0f;

                case PeaceTerm.PrisonerExchange:
                    return -8f;

                case PeaceTerm.SecurityGuarantee:
                    // Worth most to a state that feels genuinely threatened.
                    return -18f - opponent.warExhaustion * 0.15f;

                default:
                    return 0f;
            }
        }

        /// <summary>Net price of a whole proposal to the side being asked.</summary>
        public static float ProposalCost(GameState state, Confrontation confrontation,
            string proposerId, PeaceProposal proposal)
        {
            float total = 0f;
            foreach (var term in proposal.terms)
                total += TermCost(state, confrontation, proposerId, term);
            return total;
        }

        /// <summary>
        /// Whether the opponent would sign this exact proposal. Their willingness
        /// to stop comes from the confrontation; the price comes from the terms.
        /// </summary>
        public static bool WouldAccept(GameState state, Confrontation confrontation,
            string proposerId, PeaceProposal proposal)
        {
            float willingness = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, proposerId);
            float cost = ProposalCost(state, confrontation, proposerId, proposal);
            return willingness >= cost;
        }

        /// <summary>
        /// What our side *believes* about a draft proposal, at the precision our
        /// reporting on the opponent supports.
        ///
        /// `WouldAccept` is ground truth and must never reach the player directly:
        /// it is computed from the opponent's true war support, government pillar,
        /// growth and exhaustion. Printing it made the negotiating table a perfect
        /// oracle — a player could binary-search the exact maximal set of terms
        /// with no collection at all, and GDD §26's central tension (ask for too
        /// much and you prolong the war) became unreachable.
        ///
        /// Political collection is what buys precision here: read on their war
        /// support and their government's cohesion is exactly what an intelligence
        /// service is for.
        /// </summary>
        public static SettlementOutlook Assess(GameState state, Confrontation confrontation,
            string proposerId, PeaceProposal proposal)
        {
            if (proposal == null || proposal.terms.Count == 0) return SettlementOutlook.NoTerms;

            float willingness = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, proposerId);
            float margin = willingness - ProposalCost(state, confrontation, proposerId, proposal);

            string opponentId = confrontation.OpponentOf(proposerId);
            var estimate = IntelligenceSystem.GetEstimate(state, proposerId, opponentId, IntelDomain.Political);
            var grade = estimate?.confidence ?? ConfidenceGrade.None;

            switch (grade)
            {
                case ConfidenceGrade.Confirmed:
                case ConfidenceGrade.High:
                    // We can read the room.
                    return margin >= 0f ? SettlementOutlook.Likely : SettlementOutlook.Unlikely;

                case ConfidenceGrade.Moderate:
                case ConfidenceGrade.Low:
                    // We can tell a hopeless demand from a modest one, no more.
                    if (margin > 25f) return SettlementOutlook.Likely;
                    if (margin < -25f) return SettlementOutlook.Unlikely;
                    return SettlementOutlook.Uncertain;

                default:
                    // No reporting on their politics: we are guessing.
                    return SettlementOutlook.Unknown;
            }
        }

        /// <summary>
        /// Put terms to the other side. Returns true when they sign. A refusal
        /// costs nothing but time — and time is the thing wars consume.
        /// </summary>
        public static bool ProposeTerms(GameState state, Confrontation confrontation,
            string proposerId, PeaceProposal proposal)
        {
            if (confrontation == null || confrontation.resolved) return false;
            if (proposal == null || proposal.terms.Count == 0) return false;

            var proposer = state.FindCountry(proposerId);
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            if (proposer == null || opponent == null) return false;

            if (!WouldAccept(state, confrontation, proposerId, proposal))
            {
                var relationship = state.FindRelationship(proposerId, opponent.id);
                relationship?.AddMemory(state.date, "Rejected settlement terms", -0.5f);

                if (proposerId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "TERMS REJECTED",
                        $"{opponent.displayName} will not sign that. The war continues.", opponent.id,
                        desk: ReportingDesk.Military);
                GameLog.Info("PEACE", $"{opponent.id} rejected terms from {proposerId}.");
                return false;
            }

            ApplyTerms(state, confrontation, proposerId, opponent, proposal);
            return true;
        }

        static void ApplyTerms(GameState state, Confrontation confrontation,
            string proposerId, CountryState opponent, PeaceProposal proposal)
        {
            var proposer = state.FindCountry(proposerId);
            var relationship = state.FindRelationship(proposerId, opponent.id);
            var summary = new List<string>();

            foreach (var term in proposal.terms)
            {
                switch (term)
                {
                    case PeaceTerm.TerritorialCession:
                    {
                        var location = state.FindLocation(confrontation.objectiveLocationId);
                        if (location != null)
                        {
                            TerritorySystem.Cede(state, location, proposerId);
                            summary.Add($"{opponent.displayName} cedes {location.displayName}");
                            opponent.stability = Clamp(opponent.stability - location.strategicValue * 0.05f);
                        }
                        break;
                    }

                    case PeaceTerm.Reparations:
                    {
                        float amount = Math.Min(opponent.resources.treasury * 0.25f, 400f);
                        opponent.resources.treasury -= amount;
                        proposer.resources.treasury += amount;
                        opponent.governmentApproval = Clamp(opponent.governmentApproval - 5f);
                        summary.Add("reparations paid");
                        break;
                    }

                    case PeaceTerm.Demilitarization:
                        opponent.military.ground.readiness = Clamp(opponent.military.ground.readiness - 25f);
                        opponent.military.posture = MilitaryPosture.Peacetime;
                        opponent.pillars.military = Clamp(opponent.pillars.military - 6f);
                        summary.Add("forces stood down");
                        break;

                    case PeaceTerm.ResourceAccess:
                        proposer.resources.strategicMaterials = Clamp(proposer.resources.strategicMaterials + 12f);
                        proposer.resources.energy = Clamp(proposer.resources.energy + 8f);
                        summary.Add("resource access secured");
                        break;

                    case PeaceTerm.Recognition:
                        proposer.pillars.diplomacy = Clamp(proposer.pillars.diplomacy + 4f);
                        if (relationship != null)
                            relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 6f);
                        summary.Add("our position recognized");
                        break;

                    case PeaceTerm.PoliticalConcessions:
                        // A government forced to be seen changing course pays for
                        // it at home, and the change outlives the war: their
                        // strategic alignment moves toward ours because the
                        // people who argued for confronting us have just lost.
                        opponent.governmentApproval = Clamp(opponent.governmentApproval - 12f);
                        opponent.government.eliteCohesion = Clamp(opponent.government.eliteCohesion - 8f);
                        if (opponent.government.IsElective)
                            opponent.government.legislativeSupport =
                                Clamp(opponent.government.legislativeSupport - 10f);
                        if (relationship != null)
                            relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 10f);
                        summary.Add("policy concessions extracted");
                        break;

                    case PeaceTerm.TreatyRevision:
                    {
                        var treaty = state.FindTreaty(opponent.id, proposerId);
                        if (treaty != null) treaty.commitments.Remove(TreatyCommitment.MutualDefense);
                        opponent.pillars.government = Clamp(opponent.pillars.government - 4f);
                        summary.Add("commitments revised");
                        break;
                    }

                    case PeaceTerm.Withdrawal:
                        foreach (var location in state.locations)
                        {
                            if (location.ownerId != proposerId) continue;
                            if (location.originalOwnerId != opponent.id) continue;
                            location.ownerId = opponent.id;
                            summary.Add($"{location.displayName} returned");
                        }
                        if (relationship != null) relationship.AddMemory(state.date, "Returned occupied ground", 3f);
                        break;

                    case PeaceTerm.SanctionsRelief:
                    {
                        var sanction = state.FindSanction(proposerId, opponent.id);
                        if (sanction != null)
                        {
                            state.sanctions.Remove(sanction);
                            var link = state.FindTrade(proposerId, opponent.id);
                            if (link != null) link.embargoed = false;
                            summary.Add("sanctions lifted");
                        }
                        break;
                    }

                    case PeaceTerm.PrisonerExchange:
                        proposer.resources.manpower += 40f;
                        opponent.resources.manpower += 40f;
                        proposer.governmentApproval = Clamp(proposer.governmentApproval + 3f);
                        opponent.governmentApproval = Clamp(opponent.governmentApproval + 3f);
                        summary.Add("prisoners exchanged");
                        break;

                    case PeaceTerm.SecurityGuarantee:
                        if (relationship != null)
                        {
                            relationship.trust = Clamp(relationship.trust + 10f);
                            relationship.SetThreatPerceivedBy(opponent.id,
                                Clamp(relationship.ThreatPerceivedBy(opponent.id) - 12f));
                        }
                        summary.Add("security guarantee extended");
                        break;
                }
            }

            string text = summary.Count > 0
                ? $"Settlement with {opponent.displayName}: {string.Join(", ", summary)}."
                : $"Settlement concluded with {opponent.displayName}.";

            state.settlements.Add(new SettlementRecord
            {
                date = state.date,
                confrontationId = confrontation.id,
                proposerId = proposerId,
                accepterId = opponent.id,
                terms = new List<PeaceTerm>(proposal.terms),
                summary = text
            });

            // A signed peace is worth something to the relationship, whatever
            // was in it — the fighting has stopped.
            if (relationship != null)
            {
                relationship.AddMemory(state.date, "Concluded a settlement", 2f);
                relationship.relations = Clamp(relationship.relations + 6f);
            }

            proposer.governmentApproval = Clamp(proposer.governmentApproval + 6f);
            if (proposerId == state.playerCountryId)
                ProgressionSystem.AwardXP(state, 80, "Settlement concluded on our terms");

            ConfrontationSystem.CloseWithSettlement(state, confrontation, proposerId, text);
        }

        /// <summary>
        /// A reasonable opening offer for the player: the objective, plus the
        /// sweeteners that cost us least.
        /// </summary>
        /// <summary>
        /// The most favourable set of terms the other side would actually sign.
        ///
        /// This exists because the game had no **accept** verb. It would tell the
        /// operator "the other side is prepared to negotiate" and then offer only
        /// PUT TERMS TO THEM — leaving them to assemble a proposal from ten
        /// checkboxes and guess at what would be taken. The message named an
        /// opportunity the interface had no way to act on, which reads as a
        /// missing button rather than a deep negotiation system.
        ///
        /// Starts from the ordinary suggestion and gives ground, dropping the
        /// most expensive demand until the proposal is one they would accept.
        /// Concessions are never dropped — they are what makes a hard term
        /// signable, so removing them makes agreement *less* likely, not more.
        ///
        /// Returns null when nothing acceptable exists, in which case the honest
        /// answer is that there is no deal to be had this month.
        /// </summary>
        public static PeaceProposal BestAcceptableProposal(GameState state, Confrontation confrontation,
            string proposerId)
        {
            var proposal = SuggestProposal(state, confrontation, proposerId);

            // Give ground one demand at a time, most expensive first.
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (proposal.terms.Count > 0
                    && WouldAccept(state, confrontation, proposerId, proposal)) return proposal;

                PeaceTerm? worst = null;
                float worstCost = 0f;
                foreach (var term in proposal.terms)
                {
                    float cost = TermCost(state, confrontation, proposerId, term);
                    if (cost > worstCost) { worstCost = cost; worst = term; }
                }

                if (worst == null) break; // only concessions left; nothing more to give
                proposal.terms.Remove(worst.Value);
            }

            return proposal.terms.Count > 0
                   && WouldAccept(state, confrontation, proposerId, proposal)
                ? proposal
                : null;
        }

        public static PeaceProposal SuggestProposal(GameState state, Confrontation confrontation, string proposerId)
        {
            var proposal = new PeaceProposal();

            if (!string.IsNullOrEmpty(confrontation.objectiveLocationId))
                proposal.terms.Add(PeaceTerm.TerritorialCession);
            else
                proposal.terms.Add(PeaceTerm.Recognition);

            proposal.terms.Add(PeaceTerm.PrisonerExchange);
            if (state.FindSanction(proposerId, confrontation.OpponentOf(proposerId)) != null)
                proposal.terms.Add(PeaceTerm.SanctionsRelief);

            return proposal;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

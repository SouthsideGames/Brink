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
                case PeaceTerm.SanctionsLifted:
                    return true;

                case PeaceTerm.Withdrawal:
                case PeaceTerm.SanctionsRelief:
                case PeaceTerm.PrisonerExchange:
                case PeaceTerm.SecurityGuarantee:
                    return false;

                default:
                    // **Loud, not `return false`.** A silent default would make a
                    // newly added term count as a concession — so the war-verdict
                    // logic would quietly conclude that demanding it *cost* us
                    // something, and nothing would fail. Every other exhaustive
                    // switch in this codebase is guarded; this one was not.
                    // `EveryPeaceTermIsClassified` walks the enum, so this line
                    // should be unreachable.
                    GameLog.Error("PEACE",
                        $"{term} has no demand/concession classification. "
                        + "Add it to PeaceSystem.IsDemand.");
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

                case PeaceTerm.SanctionsLifted:
                {
                    // The mirror of SanctionsRelief, priced the same way: worth
                    // nothing if they have not sanctioned us, so the screen cannot
                    // offer a demand that would buy us nothing.
                    var theirs = state.FindSanction(opponent.id, proposerId);
                    if (theirs == null) return 0f;

                    // Priced on what they invested in it, not on what it costs us.
                    // An Existential embargo is a decade of foreign policy and the
                    // instrument they have been counting on; standing it down is
                    // conceding the campaign, and they price it accordingly.
                    // A routine measure is a gesture and goes cheaply.
                    return 10f + theirs.Weight * 11f + Math.Min(14f, theirs.monthsActive * 0.25f);
                }

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

        /// <summary>
        /// The operator-facing label for a term, phrased from *our* side of the
        /// table so "WE LIFT SANCTIONS" and "THEY LIFT SANCTIONS" cannot be
        /// confused for each other.
        ///
        /// **Lives here, next to `IsDemand`, because the version that lived
        /// privately in the view could not be tested.** Its `default:` branch
        /// caught both `SecurityGuarantee` and `PoliticalConcessions`, so the
        /// tenth term shipped as a second button reading "WE GUARANTEE THEM" —
        /// two different demands wearing one label, found by a player rather
        /// than by the suite. `EveryPeaceTermHasItsOwnLabel` walks the enum now.
        /// </summary>
        public static string Describe(PeaceTerm term)
        {
            switch (term)
            {
                // ---- demands ----
                case PeaceTerm.TerritorialCession: return "CEDE OBJECTIVE";
                case PeaceTerm.Reparations: return "REPARATIONS";
                case PeaceTerm.Demilitarization: return "DEMILITARIZE";
                case PeaceTerm.ResourceAccess: return "RESOURCE ACCESS";
                case PeaceTerm.Recognition: return "RECOGNITION";
                case PeaceTerm.TreatyRevision: return "REVISE TREATIES";
                case PeaceTerm.PoliticalConcessions: return "THEY CHANGE COURSE";
                case PeaceTerm.SanctionsLifted: return "THEY LIFT SANCTIONS";

                // ---- concessions ----
                case PeaceTerm.Withdrawal: return "WE WITHDRAW";
                case PeaceTerm.SanctionsRelief: return "WE LIFT SANCTIONS";
                case PeaceTerm.PrisonerExchange: return "PRISONER EXCHANGE";
                case PeaceTerm.SecurityGuarantee: return "WE GUARANTEE THEM";

                default:
                    GameLog.Error("PEACE",
                        $"{term} has no label. Add it to PeaceSystem.Describe.");
                    return Phrase.Caps(term);
            }
        }

        /// <summary>A term in an incoming offer, phrased from the recipient's side.</summary>
        public static string DescribeReceived(PeaceTerm term)
        {
            switch (term)
            {
                case PeaceTerm.TerritorialCession: return "WE CEDE THE OBJECTIVE";
                case PeaceTerm.Reparations: return "WE PAY REPARATIONS";
                case PeaceTerm.Demilitarization: return "WE DEMILITARIZE";
                case PeaceTerm.ResourceAccess: return "WE GRANT RESOURCE ACCESS";
                case PeaceTerm.Recognition: return "WE RECOGNIZE THEIR POSITION";
                case PeaceTerm.TreatyRevision: return "WE REVISE OUR TREATIES";
                case PeaceTerm.PoliticalConcessions: return "WE CHANGE COURSE";
                case PeaceTerm.SanctionsLifted: return "WE LIFT SANCTIONS";
                case PeaceTerm.Withdrawal: return "THEY WITHDRAW";
                case PeaceTerm.SanctionsRelief: return "THEY LIFT SANCTIONS";
                case PeaceTerm.PrisonerExchange: return "PRISONER EXCHANGE";
                case PeaceTerm.SecurityGuarantee: return "THEY GUARANTEE US";
                default:
                    GameLog.Error("PEACE",
                        $"{term} has no incoming-offer label. Add it to PeaceSystem.DescribeReceived.");
                    return Phrase.Caps(term);
            }
        }

        /// <summary>
        /// Why a term would achieve nothing if signed, or null if it is live.
        ///
        /// Three terms are conditional on world state: you cannot lift sanctions
        /// you never imposed, demand they lift sanctions they never imposed, or
        /// withdraw from ground you do not hold. All three price to exactly zero,
        /// which the settlement screen used to render as an ordinary button —
        /// costing nothing, buying nothing, and looking identical to a free
        /// concession. Following `OperationCatalog.CanOrder`: shown, disabled,
        /// and given a reason.
        /// </summary>
        public static string WhyInert(GameState state, Confrontation confrontation,
            string proposerId, PeaceTerm term)
        {
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            if (opponent == null) return null;

            switch (term)
            {
                case PeaceTerm.SanctionsRelief:
                    return state.FindSanction(proposerId, opponent.id) == null
                        ? "WE HAVE NO SANCTIONS ON THEM" : null;

                case PeaceTerm.SanctionsLifted:
                    return state.FindSanction(opponent.id, proposerId) == null
                        ? "THEY HAVE NO SANCTIONS ON US" : null;

                case PeaceTerm.Withdrawal:
                    foreach (var location in state.locations)
                        if (location.ownerId == proposerId && location.originalOwnerId == opponent.id)
                            return null;
                    return "WE HOLD NO GROUND OF THEIRS";

                case PeaceTerm.TerritorialCession:
                    return state.FindLocation(confrontation.objectiveLocationId) == null
                        ? "THIS WAR HAS NO TERRITORIAL OBJECTIVE" : null;

                default:
                    return null;
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

            // A dead-band around the line, narrowing with collection and never
            // closing entirely. Confirmed/High used to answer the exact sign of
            // the margin, which let a well-collected operator walk the term list
            // to the precise acceptance boundary — an oracle bought rather than
            // free, but still an oracle. Better reporting now narrows the band
            // of doubt; it never removes it.
            float deadBand = DeadBandFor(grade);
            if (grade == ConfidenceGrade.None) return SettlementOutlook.Unknown;
            if (margin > deadBand) return SettlementOutlook.Likely;
            if (margin < -deadBand) return SettlementOutlook.Unlikely;
            return SettlementOutlook.Uncertain;
        }

        /// <summary>
        /// Half-width of the margin our reporting cannot resolve, by grade.
        /// Precision is bought with collection; certainty is never sold.
        /// </summary>
        public static float DeadBandFor(ConfidenceGrade grade)
        {
            switch (grade)
            {
                case ConfidenceGrade.Confirmed: return 4f;
                case ConfidenceGrade.High: return 10f;
                case ConfidenceGrade.Moderate: return 25f;
                case ConfidenceGrade.Low: return 35f;
                default: return float.PositiveInfinity;
            }
        }

        /// <summary>
        /// How the other side is disposed toward ending the war at all, as far
        /// as our reporting on their politics supports (GDD §26). The truth is
        /// `ConfrontationSystem.SettlementWillingnessFor`; this is what a
        /// government at war actually knows about the other government's mood.
        ///
        /// Two properties carry the fog rule. **Identical reporting gives an
        /// identical read**: two hidden willingness values inside the same band
        /// are indistinguishable, so the read cannot be used to solve for the
        /// hidden figure. **Better collection buys resolution, not truth**: with
        /// Moderate or Low reporting the five bands collapse to three, and with
        /// none there is no read at all.
        /// </summary>
        public static SettlementDisposition AssessDisposition(GameState state, Confrontation confrontation,
            string proposerId)
        {
            if (confrontation == null || confrontation.resolved) return SettlementDisposition.Unknown;

            string opponentId = confrontation.OpponentOf(proposerId);
            var estimate = IntelligenceSystem.GetEstimate(state, proposerId, opponentId, IntelDomain.Political);
            var grade = estimate?.confidence ?? ConfidenceGrade.None;
            if (grade == ConfidenceGrade.None) return SettlementDisposition.Unknown;

            float willingness = ConfrontationSystem.SettlementWillingnessFor(state, confrontation, proposerId);

            SettlementDisposition fine;
            if (willingness >= 60f) fine = SettlementDisposition.LikelyReceptive;
            else if (willingness >= 35f) fine = SettlementDisposition.PotentiallyReceptive;
            else if (willingness >= 15f) fine = SettlementDisposition.Uncertain;
            else if (willingness >= -10f) fine = SettlementDisposition.Resistant;
            else fine = SettlementDisposition.HighlyResistant;

            if (grade == ConfidenceGrade.Confirmed || grade == ConfidenceGrade.High) return fine;

            // Coarse reporting: receptive, uncertain, or resistant — no more.
            switch (fine)
            {
                case SettlementDisposition.LikelyReceptive: return SettlementDisposition.PotentiallyReceptive;
                case SettlementDisposition.HighlyResistant: return SettlementDisposition.Resistant;
                default: return fine;
            }
        }

        /// <summary>
        /// The terms our own staff would recommend putting to them, derived from
        /// the **assessment**, never from the acceptance test.
        ///
        /// The settlement screen used to draft its one-press offer from
        /// `BestAcceptableProposal`, which walks `WouldAccept` — so it printed
        /// the exact maximal term list the enemy would sign, under the words
        /// "THEY WOULD SIGN THIS TODAY", with no collection at all. This walks
        /// the same suggestion, giving ground one demand at a time, but stops
        /// where our *reporting* says they would likely sign. With poor
        /// reporting that means giving more ground than strictly necessary;
        /// with none there is no recommendation, and the honest answer is that
        /// we would be guessing.
        ///
        /// Returns null with `outlook` = `Unknown` when nothing can be said, and
        /// the last draft reached with `outlook` = `Uncertain` when the read
        /// never firmed up — a draft that *might* be signed is still worth
        /// putting to them, and the label says so.
        /// </summary>
        public static PeaceProposal RecommendedProposal(GameState state, Confrontation confrontation,
            string proposerId, out SettlementOutlook outlook)
        {
            outlook = SettlementOutlook.Unknown;
            if (confrontation == null || confrontation.resolved) return null;

            var proposal = SuggestProposal(state, confrontation, proposerId);
            PeaceProposal uncertain = null;

            for (int attempt = 0; attempt < 8 && proposal.terms.Count > 0; attempt++)
            {
                var read = Assess(state, confrontation, proposerId, proposal);
                if (read == SettlementOutlook.Unknown) { outlook = read; return null; }
                if (read == SettlementOutlook.Likely) { outlook = read; return proposal; }
                if (read == SettlementOutlook.Uncertain && uncertain == null)
                {
                    uncertain = new PeaceProposal();
                    uncertain.terms.AddRange(proposal.terms);
                }

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

            if (uncertain != null) { outlook = SettlementOutlook.Uncertain; return uncertain; }
            outlook = SettlementOutlook.Unlikely;
            return null;
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

        /// <summary>
        /// Let a foreign government use the same constructed settlement grammar
        /// as the player. AI opponents get the best package they will actually
        /// accept; the player receives the opening package as a Crisis decision.
        /// </summary>
        public static bool ProposeConstructedSettlementBy(GameState state,
            Confrontation confrontation, string proposerId)
        {
            if (confrontation == null || confrontation.resolved) return false;
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            if (opponent == null) return false;

            var proposal = opponent.isPlayer
                ? SuggestProposal(state, confrontation, proposerId)
                : BestAcceptableProposal(state, confrontation, proposerId);
            if (proposal == null || proposal.terms.Count == 0) return false;

            return opponent.isPlayer
                ? ConfrontationSystem.OfferConstructedTermsToPlayer(
                    state, confrontation, proposerId, proposal)
                : ProposeTerms(state, confrontation, proposerId, proposal);
        }

        /// <summary>Apply a package the player explicitly accepted.</summary>
        public static bool AcceptOfferedTerms(GameState state, Confrontation confrontation,
            string proposerId, PeaceProposal proposal)
        {
            if (confrontation == null || confrontation.resolved
                || proposal == null || proposal.terms.Count == 0) return false;
            var opponent = state.FindCountry(confrontation.OpponentOf(proposerId));
            if (opponent == null) return false;
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

                    case PeaceTerm.SanctionsLifted:
                    {
                        // Symmetric with SanctionsRelief above, and deliberately
                        // so: signing both stands the whole economic war down,
                        // which is what a mutual de-escalation actually looks like.
                        var theirs = state.FindSanction(opponent.id, proposerId);
                        if (theirs != null)
                        {
                            state.sanctions.Remove(theirs);
                            var link = state.FindTrade(opponent.id, proposerId);
                            if (link != null) link.embargoed = false;
                            summary.Add($"{opponent.displayName} lifted sanctions");
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
        /// The most favourable set of terms the other side would actually sign.
        ///
        /// **Ground truth. Never for a view.** This walks `WouldAccept`, so
        /// handing its result to the player is handing them the enemy's exact
        /// acceptance boundary; the settlement screen did exactly that once and
        /// `SettlementFogTests` now scans player-facing code for it. The
        /// player-facing counterpart is `RecommendedProposal`, which walks the
        /// assessment instead.
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

        /// <summary>
        /// A reasonable opening offer for the player: the objective, plus the
        /// sweeteners that cost us least. Ours to know — what we would put on the
        /// table is not a fact about them.
        /// </summary>
        public static PeaceProposal SuggestProposal(GameState state, Confrontation confrontation, string proposerId)
        {
            var proposal = new PeaceProposal();

            if (proposerId == confrontation.initiatorId
                && !string.IsNullOrEmpty(confrontation.objectiveLocationId))
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

using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Relationships, treaties and coalitions (GDD Phase 7, §15).
    ///
    /// Relationship status is always derived from the full six-dimension state,
    /// never stored. Countries join coalitions for their own reasons — their
    /// rivalry with the target, their alignment with the leader, their
    /// dependence and their threat perception — not because the player asked.
    /// </summary>
    public static class DiplomacySystem
    {
        public const int TreatyProposalCost = 2;
        public const int CoalitionRequestCost = 2;
        public const int DiplomaticOutreachCost = 1;

        /// <summary>Create the relationship graph for a new world.</summary>
        public static void SeedRelationships(GameState state)
        {
            for (int i = 0; i < state.countries.Count; i++)
            for (int j = i + 1; j < state.countries.Count; j++)
            {
                var a = state.countries[i];
                var b = state.countries[j];
                var relationship = new Relationship { countryA = a.id, countryB = b.id };

                // Dependence begins from the authored trade network.
                var link = state.FindTrade(a.id, b.id);
                if (link != null)
                {
                    relationship.dependenceAOnB = Clamp(link.volume * 0.6f);
                    relationship.dependenceBOnA = Clamp(link.volume * 0.6f);
                }

                relationship.threatPerceptionOfA = Clamp(a.pillars.military * 0.45f);
                relationship.threatPerceptionOfB = Clamp(b.pillars.military * 0.45f);
                state.relationships.Add(relationship);
            }
        }

        /// <summary>
        /// Emergent status (GDD §15.1). Combines temperature, trust, alignment,
        /// threat and treaty commitments rather than reading one number.
        /// </summary>
        public static RelationshipStatus StatusOf(GameState state, string a, string b)
        {
            var relationship = state.FindRelationship(a, b);
            if (relationship == null) return RelationshipStatus.Neutral;

            var treaty = state.FindTreaty(a, b);
            float score = relationship.relations * 0.4f
                          + relationship.trust * 0.25f
                          + relationship.strategicAlignment * 0.25f
                          - (relationship.threatPerceptionOfA + relationship.threatPerceptionOfB) * 0.5f * 0.3f
                          + relationship.memoryWeight * 0.5f;

            if (treaty != null)
            {
                if (treaty.Has(TreatyCommitment.MutualDefense)) score += 18f;
                if (treaty.Has(TreatyCommitment.IntelligenceSharing)) score += 8f;
                if (treaty.Has(TreatyCommitment.NonAggression)) score += 5f;
            }

            // An active confrontation overrides warm paperwork.
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (!confrontation.Involves(a) || !confrontation.Involves(b)) continue;
                if (confrontation.escalation >= EscalationState.LimitedConflict) return RelationshipStatus.Hostile;
                if (confrontation.escalation >= EscalationState.Crisis) score -= 30f;
                else score -= 15f;
            }

            bool allied = treaty != null && treaty.Has(TreatyCommitment.MutualDefense);
            if (score >= 78f && allied) return RelationshipStatus.Ally;
            if (score >= 70f) return RelationshipStatus.StrategicPartner;
            if (score >= 60f) return RelationshipStatus.Friendly;
            if (score >= 50f) return RelationshipStatus.Cooperative;
            if (score >= 35f) return RelationshipStatus.Neutral;
            if (score >= 20f) return RelationshipStatus.Rival;
            return RelationshipStatus.Hostile;
        }

        // ---------- player commands ----------

        /// <summary>Diplomatic outreach: cheap, incremental warming.</summary>
        public static bool Outreach(GameState state, TurnManager turns, string targetId)
        {
            var relationship = state.FindRelationship(state.playerCountryId, targetId);
            if (relationship == null) return false;
            int cost = ProgressionSystem.DiscountedCost(state, DiplomaticOutreachCost,
                SkillEffect.OutreachEfficiency, minimum: 0);
            if (cost > 0 && !turns.SpendCommandPoints(cost, "Diplomatic outreach")) return false;

            var player = state.PlayerCountry;
            float effectiveness = 1f + player.pillars.diplomacy / 100f;

            // Envoys are received exactly as warmly as bloc politics allows. An
            // operator deeply aligned with this state's enemy finds the meetings
            // short and the communiqués thin — without this, monthly outreach
            // simply out-pumped rival gravity (+3/month beats any drag), and the
            // befriend-everyone line was measured achievable in full.
            effectiveness *= 1f - RivalGravity(state, state.playerCountryId, targetId) * 0.8f;

            // Diminishing returns: courtesy calls cannot manufacture an alliance.
            // The warmer things already are, the less another visit achieves.
            relationship.relations = Growth.Apply(relationship.relations, 3f * effectiveness);
            relationship.trust = Growth.Apply(relationship.trust, 1f * effectiveness);

            var target = state.FindCountry(targetId);
            state.AddChronicle(ChronicleCategory.Diplomatic, player.id,
                $"Diplomatic outreach to {target?.displayName}.", Publicity.Public);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 6, "Diplomatic outreach");
            return true;
        }

        /// <summary>
        /// Propose a treaty. The other state accepts on its own interests: how
        /// warm the relationship is, how much it trusts us, how exposed it feels,
        /// and how demanding the commitments are.
        /// </summary>
        public static bool ProposeTreaty(GameState state, TurnManager turns, string targetId,
            List<TreatyCommitment> commitments)
        {
            if (state.FindTreaty(state.playerCountryId, targetId) != null)
            {
                GameLog.Warn("DIPLO", "A treaty is already in force with that state.");
                return false;
            }
            var target = state.FindCountry(targetId);
            if (target == null || commitments == null || commitments.Count == 0) return false;
            if (!turns.SpendCommandPoints(TreatyProposalCost, $"Propose treaty to {target.displayName}")) return false;

            return ProposeTreatyBy(state, state.playerCountryId, targetId, commitments);
        }

        /// <summary>Treaty proposed by any state. AI diplomacy uses the same logic.</summary>
        public static bool ProposeTreatyBy(GameState state, string proposerId, string targetId,
            List<TreatyCommitment> commitments)
        {
            if (proposerId == targetId) return false;
            if (state.FindTreaty(proposerId, targetId) != null) return false;
            var relationship = state.FindRelationship(proposerId, targetId);
            var target = state.FindCountry(targetId);
            if (relationship == null || target == null || commitments == null || commitments.Count == 0) return false;

            float willingness = TreatyWillingness(state, proposerId, targetId, commitments);
            if (willingness < 50f)
            {
                relationship.AddMemory(state.date, "Rejected treaty proposal", -0.5f);
                if (proposerId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "TREATY REJECTED",
                        $"{target.displayName} declines the proposed commitments.", targetId,
                        desk: ReportingDesk.Diplomacy);
                GameLog.Info("DIPLO", $"{targetId} rejected a treaty proposal from {proposerId}.");
                return false;
            }

            var treaty = new Treaty
            {
                id = $"TRTY_{state.date.SortKey}_{state.treaties.Count}",
                countryA = proposerId,
                countryB = targetId,
                signedDate = state.date
            };
            treaty.commitments.AddRange(commitments);
            state.treaties.Add(treaty);

            relationship.relations = Clamp(relationship.relations + 8f);
            relationship.trust = Clamp(relationship.trust + 5f);
            relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 10f);
            relationship.AddMemory(state.date, "Signed treaty", 2f);

            bool playerInvolved = proposerId == state.playerCountryId || targetId == state.playerCountryId;
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "TREATY SIGNED",
                $"{state.FindCountry(proposerId)?.displayName} and {target.displayName} conclude an agreement: " +
                $"{string.Join(", ", commitments).ToLowerInvariant()}.",
                targetId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, proposerId,
                $"Treaty signed with {target.displayName}.", Publicity.Public);
            if (proposerId == state.playerCountryId)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 30, "Treaty concluded");
            }
            return true;
        }

        /// <summary>How willing a state is to sign commitments proposed by the player.</summary>
        /// <summary>
        /// What a commitment is worth to whoever receives it.
        ///
        /// A guarantee to fight for someone is the heaviest thing a state can
        /// promise; agreeing not to attack them is close to free. These weights
        /// are what make a treaty a *negotiation* — without them every clause is
        /// interchangeable and "balance" has no meaning.
        /// </summary>
        public static float ValueOf(TreatyCommitment commitment)
        {
            switch (commitment)
            {
                case TreatyCommitment.ArmsControl: return 4f;    // neither of us builds the thing
                case TreatyCommitment.MutualDefense: return 5f;   // we will die for you
                case TreatyCommitment.Transit: return 3.5f;       // our ground, your forces
                case TreatyCommitment.IntelligenceSharing: return 3f;
                case TreatyCommitment.TradePreference: return 2.5f;
                case TreatyCommitment.JointPlanning: return 2f;
                default: return 1.5f;                             // NonAggression
            }
        }

        /// <summary>
        /// How lopsided a set of clauses is, from the proposer's side. Positive
        /// means we are receiving more than we give.
        ///
        /// Mutual clauses net to zero — both sides carry and both sides receive —
        /// which is why an even treaty of five mutual commitments is balanced no
        /// matter how substantial it is. Scale and fairness are different axes.
        /// </summary>
        public static float BalanceOf(List<TreatyClause> clauses)
        {
            float balance = 0f;
            if (clauses == null) return 0f;

            foreach (var clause in clauses)
            {
                if (clause.side == ClauseSide.TheyProvide) balance += ValueOf(clause.commitment);
                else if (clause.side == ClauseSide.WeProvide) balance -= ValueOf(clause.commitment);
            }
            return balance;
        }

        /// <summary>
        /// Propose a treaty whose clauses say who carries what — the negotiated
        /// form (GDD §15.1 amendment).
        ///
        /// Signing a lopsided agreement is deliberately *allowed*. A state that
        /// depends on us will accept terms a self-sufficient one would laugh at,
        /// and taking that deal is a legitimate move. What it costs is our
        /// standing as a partner, which everyone else prices in the next time they
        /// are asked to sign something.
        /// </summary>
        public static bool ProposeNegotiatedTreatyBy(GameState state, string proposerId,
            string targetId, List<TreatyClause> clauses)
        {
            if (clauses == null || clauses.Count == 0) return false;

            var commitments = new List<TreatyCommitment>();
            foreach (var clause in clauses) commitments.Add(clause.commitment);

            float willingness = TreatyWillingness(state, proposerId, targetId, clauses);
            var target = state.FindCountry(targetId);
            var proposer = state.FindCountry(proposerId);
            var relationship = state.FindRelationship(proposerId, targetId);
            if (target == null || proposer == null || relationship == null) return false;

            if (willingness < 50f)
            {
                relationship.AddMemory(state.date, "Rejected treaty proposal", -0.5f);
                if (proposerId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "TREATY REJECTED",
                        $"{target.displayName} declines these terms. "
                        + $"{(BalanceOf(clauses) > 2.5f ? "They can see what is being asked of them." : "The relationship is not there yet.")}",
                        targetId, desk: ReportingDesk.Diplomacy);
                return false;
            }

            if (!ProposeTreatyBy(state, proposerId, targetId, commitments)) return false;

            // Record who carries what, relative to countryA.
            var treaty = state.FindTreaty(proposerId, targetId);
            if (treaty != null)
            {
                foreach (var clause in clauses)
                    treaty.clauses.Add(new TreatyClause
                    {
                        commitment = clause.commitment,
                        side = treaty.countryA == proposerId
                            ? clause.side
                            : Flip(clause.side)
                    });
            }

            ApplyReciprocity(state, proposer, target, BalanceOf(clauses));
            return true;
        }

        /// <summary>
        /// Deepen an existing treaty with new commitments (spec 04 §5a).
        ///
        /// Until this existed a treaty could never be amended, so a friendship
        /// signed early **permanently** locked that relationship out of ever
        /// becoming an alliance — played out in a campaign that ended with
        /// fourteen treaties and one defence pact, the pact possible only where
        /// the operator had deliberately refused to sign anything for years.
        /// Diplomacy dead-ended at the first signature per pair; a relationship
        /// is supposed to be a thing that grows.
        /// </summary>
        public static bool DeepenTreaty(GameState state, TurnManager turns, string partnerId,
            List<TreatyCommitment> addedCommitments)
        {
            var treaty = state.FindTreaty(state.playerCountryId, partnerId);
            if (treaty == null || treaty.broken)
            {
                GameLog.Warn("DIPLO", "There is no standing treaty to deepen.");
                return false;
            }
            var partner = state.FindCountry(partnerId);
            if (partner == null) return false;
            if (!turns.SpendCommandPoints(TreatyProposalCost, $"Deepen treaty with {partner.displayName}"))
                return false;

            bool deepened = DeepenTreatyBy(state, state.playerCountryId, partnerId, addedCommitments);
            if (deepened)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 20, "Treaty deepened");
            }
            return deepened;
        }

        /// <summary>Deepening by any state. AI blocs solidify through the same door.</summary>
        public static bool DeepenTreatyBy(GameState state, string proposerId, string targetId,
            List<TreatyCommitment> addedCommitments)
        {
            var treaty = state.FindTreaty(proposerId, targetId);
            var relationship = state.FindRelationship(proposerId, targetId);
            var target = state.FindCountry(targetId);
            if (treaty == null || treaty.broken || relationship == null || target == null) return false;
            if (addedCommitments == null) return false;

            // Only what the treaty does not already carry.
            var added = new List<TreatyCommitment>();
            foreach (var commitment in addedCommitments)
                if (!treaty.Has(commitment) && !added.Contains(commitment)) added.Add(commitment);
            if (added.Count == 0) return false;

            // Judged on the *added* burden by the same acceptance logic a new
            // treaty faces — the rival-tie and encirclement penalties included,
            // so deepening into a pact answers to bloc politics like any pact —
            // plus what a standing relationship is worth: a partner with history
            // signs what a stranger would not.
            float history = Math.Min(12f, state.date.MonthsSince(treaty.signedDate) * 0.1f) + 8f;
            float willingness = TreatyWillingness(state, proposerId, targetId, added) + history;

            if (willingness < 50f)
            {
                relationship.AddMemory(state.date, "Declined to deepen the treaty", -0.5f);
                if (proposerId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "DEEPENING DECLINED",
                        $"{target.displayName} values the treaty as it stands. Heavier commitments "
                        + "need more warmth, more trust, or fewer of our friends among their enemies.",
                        targetId, desk: ReportingDesk.Diplomacy);
                GameLog.Info("DIPLO", $"{targetId} declined to deepen the treaty with {proposerId}.");
                return false;
            }

            treaty.commitments.AddRange(added);

            relationship.relations = Clamp(relationship.relations + 4f);
            relationship.trust = Clamp(relationship.trust + 4f);
            relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 8f);
            relationship.AddMemory(state.date, "Deepened the treaty", 1.5f);

            bool playerInvolved = proposerId == state.playerCountryId || targetId == state.playerCountryId;
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "TREATY DEEPENED",
                $"{state.FindCountry(proposerId)?.displayName} and {target.displayName} extend their "
                + $"agreement: {DescribeCommitments(added)}.",
                targetId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, proposerId,
                $"Treaty with {target.displayName} deepened ({DescribeCommitments(added)}).", Publicity.Public);
            GameLog.Info("DIPLO", $"{proposerId} deepened treaty with {targetId}: {DescribeCommitments(added)}.");
            return true;
        }

        static string DescribeCommitments(List<TreatyCommitment> commitments)
        {
            var parts = new List<string>();
            foreach (var commitment in commitments) parts.Add(Phrase.Of(commitment).ToLowerInvariant());
            return string.Join(", ", parts);
        }

        static ClauseSide Flip(ClauseSide side)
            => side == ClauseSide.TheyProvide ? ClauseSide.WeProvide
             : side == ClauseSide.WeProvide ? ClauseSide.TheyProvide
             : ClauseSide.Mutual;

        /// <summary>
        /// What signing this did to our name.
        ///
        /// Extraction is public — the terms of an agreement are not a secret — so
        /// third parties adjust their view of us directly. A generous or even deal
        /// slowly rebuilds the assumption of good faith, which is why a reputation
        /// is recoverable but slow: cheap to spend, expensive to earn back.
        /// </summary>
        static void ApplyReciprocity(GameState state, CountryState proposer,
            CountryState target, float balance)
        {
            if (balance > 2.5f)
            {
                float severity = Math.Min(3f, (balance - 2.5f) / 3f);
                proposer.reciprocity = Clamp(proposer.reciprocity - 4f * severity);

                // Everyone who can see it thinks a little less of us. Smaller than
                // the direct cost, because it happened to somebody else — but it
                // is the mechanism that makes a habit of extraction expensive
                // rather than one deal.
                foreach (var other in state.relationships)
                {
                    if (!other.Involves(proposer.id)) continue;
                    if (other.Involves(target.id)) continue;
                    other.trust = Clamp(other.trust - 1.2f * severity);
                }

                state.AddChronicle(ChronicleCategory.Diplomatic, proposer.id,
                    $"{proposer.displayName} concludes markedly one-sided terms with "
                    + $"{target.displayName}.", Publicity.Public);

                if (proposer.isPlayer)
                    state.AddNotification(NotificationClass.Advisory, "TERMS NOTED ABROAD",
                        "The imbalance of that agreement has not gone unremarked. Governments "
                        + "that were minded to deal with us will ask for more next time.",
                        target.id, desk: ReportingDesk.Diplomacy);
            }
            else if (balance > -2.5f)
            {
                // An even bargain, honestly struck.
                proposer.reciprocity = Clamp(proposer.reciprocity + 1.2f);
            }
            else
            {
                // We gave more than we got, and it is noticed.
                proposer.reciprocity = Clamp(proposer.reciprocity + 2.2f);
            }
        }

        /// <summary>
        /// What the foreign ministry thinks we should be asking for, and of whom.
        ///
        /// The diplomat is the one official whose job is knowing where the country
        /// is exposed and who might close the gap — and they had nothing to say
        /// about it. An operator opening the treaty screen faced six commitment
        /// types and fifteen countries with no indication of which mattered.
        ///
        /// Reads our own condition, which is ours to know exactly, and picks the
        /// commitment that answers our worst gap. Deliberately does *not* pick the
        /// partner by their true statistics — who can actually supply it is a
        /// question about them, and that runs through what our reporting says.
        /// </summary>
        public static TreatyCommitment SuggestedCommitmentFor(CountryState us)
        {
            if (us == null) return TreatyCommitment.NonAggression;

            // Ranked by how badly each gap hurts, worst first.
            if (us.resources.energy < 45f || us.resources.strategicMaterials < 40f)
                return TreatyCommitment.TradePreference;
            if (us.pillars.military < 55f)
                return TreatyCommitment.MutualDefense;
            if (us.pillars.intelligence < 55f)
                return TreatyCommitment.IntelligenceSharing;
            if (us.pillars.diplomacy < 55f)
                return TreatyCommitment.NonAggression;

            return TreatyCommitment.Transit;   // comfortable: buy reach
        }

        /// <summary>One line on why, for the negotiation screen.</summary>
        public static string SuggestionReason(CountryState us, TreatyCommitment commitment)
        {
            switch (commitment)
            {
                case TreatyCommitment.TradePreference:
                    return $"We are short — energy {us.resources.energy:F0}, materials "
                         + $"{us.resources.strategicMaterials:F0}. Preferential terms would ease it.";
                case TreatyCommitment.MutualDefense:
                    return "Our own forces will not deter what is out there. A guarantee would.";
                case TreatyCommitment.IntelligenceSharing:
                    return "We are reading the world poorly. Somebody else's reporting would help.";
                case TreatyCommitment.NonAggression:
                    return "Our standing is thin. Fewer people wanting to fight us is worth having.";
                default:
                    return "We want for nothing pressing. Reach is what money buys when it is not needed elsewhere.";
            }
        }

        /// <summary>Plain reading of a balance figure, for the negotiation screen.</summary>
        public static string DescribeBalance(float balance)
        {
            if (balance >= 6f) return "HEAVILY IN OUR FAVOUR — this is extraction, and it will be seen as such";
            if (balance >= 2.5f) return "IN OUR FAVOUR — we receive more than we give";
            if (balance > -2.5f) return "EVEN — both sides carry it";
            if (balance > -6f) return "IN THEIR FAVOUR — we are paying for something";
            return "HEAVILY IN THEIR FAVOUR — we are buying this relationship";
        }

        public static float TreatyWillingness(GameState state, string targetId, List<TreatyCommitment> commitments)
            => TreatyWillingness(state, state.playerCountryId, targetId, commitments);

        /// <summary>
        /// What a commitment costs the side that carries it.
        ///
        /// Extracted from the willingness calculation so the clause overload can
        /// *redistribute* this burden rather than adding a second charge on top of
        /// it. Measured play caught that: a heavy three-clause demand was already
        /// costing 40 points of willingness for weight, and an imbalance penalty
        /// took another 26 — so a state 90% dependent on us refused terms it
        /// should have had no way to refuse, and the first version of the test
        /// reported that as a finding about the game.
        ///
        /// Non-aggression is negative because promising not to attack someone is a
        /// thing they *want*.
        /// </summary>
        public static float BurdenOf(TreatyCommitment commitment)
        {
            switch (commitment)
            {
                case TreatyCommitment.MutualDefense: return 22f;
                // Heavy: it binds what we may field and how far we may go. Real
                // agreements between rivals are hard, which is the point.
                case TreatyCommitment.ArmsControl: return 16f;
                case TreatyCommitment.JointPlanning: return 12f;
                case TreatyCommitment.IntelligenceSharing: return 10f;
                case TreatyCommitment.Transit: return 8f;
                case TreatyCommitment.TradePreference: return 2f;
                default: return -4f;   // NonAggression
            }
        }

        /// <summary>
        /// Willingness against a clause list, which is what a negotiation actually
        /// produces.
        ///
        /// The base figure charges every commitment's full burden, which assumes
        /// both sides carry it. Sides then adjust that assumption: a clause *they*
        /// carry alone is heavier than a shared one, and a clause *we* carry is
        /// something they receive rather than pay for. This is a redistribution of
        /// the burden already priced, not a second penalty — charging both was the
        /// bug that made even a desperate state refuse.
        /// </summary>
        public static float TreatyWillingness(GameState state, string proposerId, string targetId,
            List<TreatyClause> clauses)
        {
            var commitments = new List<TreatyCommitment>();
            foreach (var clause in clauses) commitments.Add(clause.commitment);

            float willingness = TreatyWillingness(state, proposerId, targetId, commitments);

            var relationship = state.FindRelationship(proposerId, targetId);
            if (relationship == null) return 0f;

            // **How much they can afford to refuse.** A state that depends on us
            // will swallow terms a self-sufficient one would not — which is what
            // makes exploiting a weak partner possible, and what makes the
            // reputation cost the only thing standing between the operator and
            // doing it every time.
            float leverage = relationship.DependenceOf(targetId) / 100f;

            foreach (var clause in clauses)
            {
                float burden = BurdenOf(clause.commitment);

                if (clause.side == ClauseSide.TheyProvide)
                    willingness -= burden * 0.6f * (1f - leverage * 0.6f);
                else if (clause.side == ClauseSide.WeProvide)
                    willingness += burden * 1.4f;
            }

            return willingness;
        }

        /// <summary>How willing <paramref name="targetId"/> is to sign with <paramref name="proposerId"/> (0..100).</summary>
        public static float TreatyWillingness(GameState state, string proposerId, string targetId,
            List<TreatyCommitment> commitments)
        {
            var relationship = state.FindRelationship(proposerId, targetId);
            if (relationship == null) return 0f;
            var player = state.FindCountry(proposerId);
            var target = state.FindCountry(targetId);
            if (player == null || target == null) return 0f;

            float willingness = relationship.relations * 0.45f
                                + relationship.trust * 0.3f
                                + relationship.strategicAlignment * 0.25f
                                + relationship.DependenceOf(targetId) * 0.15f
                                + player.pillars.diplomacy * 0.12f
                                + relationship.memoryWeight * 1.5f;

            // A state that already fears us wants fewer entanglements, not more.
            willingness -= relationship.ThreatPerceivedBy(targetId) * 0.25f;

            // **Nobody signs with a state they will not admit exists** (spec 04
            // §5b). A breakaway has to win recognition before it can win
            // treaties, which is what makes recognition the first thing it
            // needs and the thing worth spending standing on. Exactly zero for
            // any country that was there at world creation.
            willingness -= (1f - Legitimacy(state, player)) * 45f;

            // Somebody in the room who knows them (spec 04 §5e). Zero unless the
            // foreign minister is actually posted here.
            willingness += EnvoyWeight(state, proposerId, targetId) * 12f;

            // Speaking past a government to the people it governs (`CAP_BROADCAST`).
            // Being disliked abroad costs us less than it did.
            willingness += TechnologySystem.Effectiveness(player, "CAP_BROADCAST")
                           * Math.Max(0f, 45f - relationship.relations) * 0.25f;

            // Money that arrives as help and stays as leverage (`CAP_DEVAID`).
            willingness += TechnologySystem.Effectiveness(player, "CAP_DEVAID")
                           * relationship.DependenceOf(targetId) * 0.10f;

            // **Verification is what lets rivals believe each other** (spec 04
            // §5f) — which is precisely what `CAP_VERIFICATION`'s description
            // has always promised and what it had almost no read site for. An
            // arms-control clause is a heavy burden between states that do not
            // trust each other; monitoring is how it gets signed anyway.
            if (commitments != null && commitments.Contains(TreatyCommitment.ArmsControl))
            {
                // **A capability that unlocks a treaty class** (spec 13 §6).
                // Inspection protocols nobody has to take on trust are what make
                // a limitation signable at all; without the regime, proposing one
                // is a piece of paper and everybody knows it.
                if (!TechnologySystem.Has(player, "CAP_ARMSCONTROL")) return 0f;
                willingness += TechnologySystem.Effectiveness(player, "CAP_VERIFICATION") * 26f;
            }

            // **We will not pact with our enemy's ally.** The strongest case over
            // every third state of the proposer being deeply aligned with a
            // genuine rival of the target. Without this, an operator could sign
            // both sides of every rivalry on earth — measured: fifteen of
            // fifteen friendships in twenty years, unresisted.
            float rivalTie = 0f;
            foreach (var third in state.countries)
            {
                if (third.id == proposerId || third.id == targetId) continue;
                var proposerThird = state.FindRelationship(proposerId, third.id);
                var targetThird = state.FindRelationship(targetId, third.id);
                if (proposerThird == null || targetThird == null) continue;

                // Same thresholds as RivalGravity, so the door and the room agree.
                float warmth = Math.Max(0f, proposerThird.strategicAlignment - 68f) / 32f;
                float coldness = Math.Max(0f, 22f - targetThird.relations) / 22f;
                rivalTie = Math.Max(rivalTie, warmth * coldness);
            }
            willingness -= rivalTie * 40f;

            // **Encirclement anxiety.** A proposer already pacted across the
            // world is offering membership in a hegemony, and every signature
            // makes the next state warier — which is what finally puts a ceiling
            // on collecting the whole map.
            willingness -= PactAnxiety(state, proposerId) * 25f;

            // **What kind of partner we have been to everyone else.**
            //
            // A government about to sign with us looks at how we have treated the
            // states that could not refuse us. A record of even dealing opens
            // doors; a record of extraction closes them — which is the whole cost
            // of taking advantage of a weak neighbour, and the reason doing so is
            // a decision rather than free profit.
            willingness += (player.reciprocity - 55f) * 0.30f;

            // Operator negotiating craft (player proposals only).
            if (proposerId == state.playerCountryId)
                willingness += ProgressionSystem.EffectValue(state, SkillEffect.TreatyPersuasion);

            // Institutions others want to be inside (GDD §11).
            willingness += TechnologySystem.Effectiveness(player, "CAP_CONVENING") * 10f;

            // Heavier commitments demand a warmer relationship.
            foreach (var commitment in commitments)
                willingness -= BurdenOf(commitment);

            // Nobody allies with a state that is currently fighting their partner.
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (!confrontation.Involves(proposerId)) continue;
                string opponentId = confrontation.OpponentOf(proposerId);
                if (opponentId == targetId) { willingness -= 60f; continue; }

                var targetToOpponent = state.FindRelationship(targetId, opponentId);
                if (targetToOpponent != null)
                {
                    // Warm ties to our opponent make them reluctant; rivalry helps us.
                    var opponentStatus = StatusOf(state, targetId, opponentId);
                    if (opponentStatus >= RelationshipStatus.Friendly) willingness -= 25f;
                    else if (opponentStatus <= RelationshipStatus.Rival) willingness += 12f;
                }
            }

            return willingness;
        }

        /// <summary>
        /// Break a treaty. Always possible, but trustworthiness and future
        /// diplomacy suffer — and everyone watching remembers (GDD §15.2).
        /// </summary>
        public static bool BreakTreaty(GameState state, string partnerId)
            => BreakTreatyBy(state, state.playerCountryId, partnerId);

        /// <summary>
        /// Repudiate a treaty. **Actor-generic** — this was player-only, so a
        /// foreign government could never be seen to break its word through this
        /// path, and `AISystem`'s counter-play could not learn from something the
        /// world could not do. Arms control breaking on escalation needed it too.
        /// </summary>
        public static bool BreakTreatyBy(GameState state, string actorId, string partnerId)
        {
            var treaty = state.FindTreaty(actorId, partnerId);
            if (treaty == null || treaty.broken) return false;

            treaty.broken = true;
            treaty.brokenBy = actorId;

            var relationship = state.FindRelationship(actorId, partnerId);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations - 25f);
                relationship.trust = Clamp(relationship.trust - 35f);
                relationship.AddMemory(state.date, "Treaty broken against them", -6f);
            }

            // Third parties revise their view of our reliability.
            var player = state.FindCountry(actorId);
            player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 6f);
            foreach (var other in state.relationships)
            {
                if (!other.Involves(actorId)) continue;
                if (other.Involves(partnerId)) continue;
                other.trust = Clamp(other.trust - 8f);
                other.AddMemory(state.date, "Observed treaty violation", -1.5f);
            }

            var partner = state.FindCountry(partnerId);

            // Ours is a decision we took; anybody else's is news off the wire —
            // and only where we would see it (`WorldWire`). Before this was
            // actor-generic the notification fired unconditionally and the
            // chronicle was attributed to the player whoever broke the treaty.
            if (actorId == state.playerCountryId)
                state.AddNotification(NotificationClass.Priority, "TREATY BROKEN",
                    $"Commitments to {partner?.displayName} repudiated. Reputation damaged.",
                    partnerId, desk: ReportingDesk.Diplomacy);
            else if (WorldWire.Watches(state, actorId) || partnerId == state.playerCountryId)
                state.AddNotification(NotificationClass.Wire, "TREATY BROKEN",
                    $"{player?.displayName} has repudiated its commitments to "
                    + $"{partner?.displayName}.", actorId, desk: ReportingDesk.Diplomacy);

            state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                $"Treaty with {partner?.displayName} broken.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Ask other states to join the player's confrontation. Each decides on
        /// its own interests; the player can exploit an enemy's rocky
        /// relationships to recruit support (GDD §15.2).
        /// </summary>
        /// <summary>
        /// Actor-generic coalition request (2026-08). Any state at war assembles
        /// one around its own confrontation with the same recruitment test the
        /// player faces. `RequestCoalition` was player-only, so AI states could
        /// join a defender-led coalition through an obligation and could never
        /// build one — coalitions formed in 12 of 556 measured decades.
        /// </summary>
        public static Coalition RequestCoalitionBy(GameState state, string leaderId)
        {
            var confrontation = state.ActiveConfrontationFor(leaderId);
            if (confrontation == null || confrontation.resolved) return null;
            if (state.FindCoalitionLedBy(confrontation.id, leaderId) != null) return null;

            string targetId = confrontation.OpponentOf(leaderId);
            var coalition = new Coalition
            {
                id = $"COAL_{state.date.SortKey}_{leaderId}",
                leaderId = leaderId,
                confrontationId = confrontation.id,
                targetId = targetId
            };
            coalition.memberIds.Add(leaderId);

            foreach (var country in state.countries)
            {
                if (country.id == leaderId || country.id == targetId) continue;
                if (CoalitionWillingness(state, leaderId, country.id, targetId) < 50f) continue;
                coalition.memberIds.Add(country.id);
                state.FindRelationship(leaderId, country.id)?.AddMemory(state.date, "Joined our coalition", 3f);
                if (country.isPlayer)
                    state.AddNotification(NotificationClass.Priority, "COALITION JOINED",
                        $"We stand with {state.FindCountry(leaderId)?.displayName} against " +
                        $"{state.FindCountry(targetId)?.displayName}.", leaderId, desk: ReportingDesk.Diplomacy);
            }

            state.coalitions.Add(coalition);
            state.AddChronicle(ChronicleCategory.Diplomatic, leaderId,
                $"Coalition formed against {state.FindCountry(targetId)?.displayName} " +
                $"with {coalition.memberIds.Count - 1} partner(s).", Publicity.Public);
            if (targetId == state.playerCountryId)
                state.AddNotification(NotificationClass.Priority, "COALITION AGAINST US",
                    $"{state.FindCountry(leaderId)?.displayName} has assembled {coalition.memberIds.Count - 1} " +
                    "partner(s) against us.", leaderId, desk: ReportingDesk.Diplomacy);
            return coalition;
        }

        public static Coalition RequestCoalition(GameState state, TurnManager turns)
        {
            var confrontation = state.ActiveConfrontation;
            if (confrontation == null)
            {
                GameLog.Warn("DIPLO", "No active confrontation to build a coalition around.");
                return null;
            }
            if (state.FindCoalition(confrontation.id) != null)
            {
                GameLog.Warn("DIPLO", "A coalition already exists for this confrontation.");
                return null;
            }
            if (!turns.SpendCommandPoints(CoalitionRequestCost, "Coalition request")) return null;

            string targetId = confrontation.OpponentOf(state.playerCountryId);
            var coalition = new Coalition
            {
                id = $"COAL_{state.date.SortKey}",
                leaderId = state.playerCountryId,
                confrontationId = confrontation.id,
                targetId = targetId
            };
            coalition.memberIds.Add(state.playerCountryId);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 25, "Coalition assembled");

            foreach (var country in state.countries)
            {
                if (country.isPlayer || country.id == targetId) continue;
                if (CoalitionWillingness(state, state.playerCountryId, country.id, targetId) >= 50f)
                {
                    coalition.memberIds.Add(country.id);
                    var relationship = state.FindRelationship(state.playerCountryId, country.id);
                    relationship?.AddMemory(state.date, "Joined our coalition", 3f);

                    state.AddNotification(NotificationClass.Priority, "COALITION PARTNER",
                        $"{country.displayName} joins the coalition.", country.id,
                        desk: ReportingDesk.Diplomacy);
                }
                else
                {
                    state.AddNotification(NotificationClass.Advisory, "COALITION DECLINED",
                        $"{country.displayName} declines to participate.", country.id,
                        desk: ReportingDesk.Diplomacy);
                }
            }

            state.coalitions.Add(coalition);
            state.AddChronicle(ChronicleCategory.Diplomatic, state.playerCountryId,
                $"Coalition formed against {state.FindCountry(targetId)?.displayName} " +
                $"with {coalition.memberIds.Count - 1} partner(s).", Publicity.Public);
            return coalition;
        }

        /// <summary>
        /// Whether a third party will join <paramref name="leaderId"/>'s coalition
        /// against the target.
        ///
        /// The leader is explicit because both sides of a war can field one
        /// (`AllianceSystem.Honor` builds defender-led coalitions). This formerly
        /// hardcoded the player as leader, which meant an AI-led coalition
        /// cohered on its members' relations with the *player* — and the player,
        /// once a non-leader member of someone else's coalition, was scored on a
        /// relationship with themselves, which does not exist, and was evicted
        /// the month after honoring an alliance.
        /// </summary>
        public static float CoalitionWillingness(GameState state, string leaderId,
            string candidateId, string targetId)
        {
            if (candidateId == leaderId) return 100f; // the leader is not recruited

            var toLeader = state.FindRelationship(leaderId, candidateId);
            var toTarget = state.FindRelationship(candidateId, targetId);
            if (toLeader == null || toTarget == null) return 0f;

            float willingness = toLeader.relations * 0.35f
                                + toLeader.trust * 0.25f
                                + toLeader.strategicAlignment * 0.2f;

            // Hostility toward the target is the strongest recruiting factor.
            willingness += (50f - toTarget.relations) * 0.5f;
            willingness += toTarget.ThreatPerceivedBy(candidateId) * 0.35f;

            // But dependence on the target argues powerfully for staying out.
            willingness -= toTarget.DependenceOf(candidateId) * 0.6f;

            var treaty = state.FindTreaty(leaderId, candidateId);
            if (treaty != null)
            {
                if (treaty.Has(TreatyCommitment.MutualDefense)) willingness += 25f;
                if (treaty.Has(TreatyCommitment.JointPlanning)) willingness += 10f;
            }

            var targetTreaty = state.FindTreaty(candidateId, targetId);
            if (targetTreaty != null && targetTreaty.Has(TreatyCommitment.MutualDefense))
                willingness -= 70f; // they are committed to the other side

            // Operator training persuades on our behalf only. Applying it to every
            // coalition on the map made the player's own skills hold together the
            // alliances fielded against them.
            if (leaderId == state.playerCountryId)
                willingness += ProgressionSystem.EffectValue(state, SkillEffect.CoalitionPersuasion);

            return willingness;
        }

        // ---------- basing ----------

        /// <summary>
        /// Grant and revoke foreign basing (GDD §16, §19).
        ///
        /// A Transit commitment is permission to operate from a partner's soil.
        /// It is not occupation — the host keeps sovereignty and takes the rights
        /// back the moment the treaty lapses or the relationship sours. A base is
        /// how a country projects force somewhere it does not own, and knowing
        /// who operates from where is one of the more valuable things collection
        /// can tell you about a state.
        /// </summary>
        static void UpdateBasingRights(GameState state)
        {
            foreach (var location in state.locations)
            {
                // Occupied ground is held, not hosted; basing has no meaning there.
                if (!location.SupportsBasing || location.IsOccupied)
                {
                    location.foreignOperatorId = "";
                    continue;
                }

                string host = location.ownerId;

                // An existing arrangement survives only while the agreement and
                // the relationship behind it both hold.
                if (location.HasForeignBase)
                {
                    if (StillWelcome(state, host, location.foreignOperatorId)) continue;

                    string departing = location.foreignOperatorId;
                    location.foreignOperatorId = "";
                    if (departing == state.playerCountryId || host == state.playerCountryId)
                        state.AddNotification(NotificationClass.Advisory, "BASING RIGHTS ENDED",
                            $"{state.FindCountry(departing)?.displayName} no longer operates from " +
                            $"{location.displayName}.", host, desk: ReportingDesk.Diplomacy);
                    state.AddChronicle(ChronicleCategory.Diplomatic, host,
                        $"Basing rights at {location.displayName} withdrawn.", Publicity.Public);
                    continue;
                }

                // Otherwise the most capable partner holding transit rights takes it.
                string best = null;
                float bestReach = 0f;
                foreach (var treaty in state.treaties)
                {
                    if (treaty.broken || !treaty.Involves(host)) continue;
                    if (!treaty.Has(TreatyCommitment.Transit)) continue;

                    string partner = treaty.PartnerOf(host);
                    if (!StillWelcome(state, host, partner)) continue;

                    var partnerState = state.FindCountry(partner);
                    if (partnerState == null || partnerState.pillars.military <= bestReach) continue;

                    bestReach = partnerState.pillars.military;
                    best = partner;
                }

                if (best == null) continue;

                location.foreignOperatorId = best;
                if (best == state.playerCountryId || host == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "BASING RIGHTS GRANTED",
                        $"{state.FindCountry(best)?.displayName} now operates from " +
                        $"{location.displayName}.", host, desk: ReportingDesk.Diplomacy);
                state.AddChronicle(ChronicleCategory.Diplomatic, host,
                    $"{state.FindCountry(best)?.displayName} granted basing at {location.displayName}.",
                    Publicity.Public);
            }
        }

        /// <summary>Whether a host still tolerates a partner operating from its soil.</summary>
        static bool StillWelcome(GameState state, string hostId, string partnerId)
        {
            if (string.IsNullOrEmpty(partnerId) || partnerId == hostId) return false;

            var treaty = state.FindTreaty(hostId, partnerId);
            if (treaty == null || treaty.broken || !treaty.Has(TreatyCommitment.Transit)) return false;

            var relationship = state.FindRelationship(hostId, partnerId);
            if (relationship == null) return false;

            // Nobody hosts a force they have come to fear.
            return relationship.relations >= 35f
                   && relationship.ThreatPerceivedBy(hostId) <= 70f;
        }

        // ---------- monthly resolution ----------

        /// <summary>
        /// How hard being close to one side of a rivalry pulls against being
        /// close to the other, 0..1 (spec 04 §8a).
        ///
        /// Measured with the befriend-everyone bot: an operator could reach warm
        /// relations with **all fifteen** other states in twenty years, on every
        /// seed tried, with zero cost anywhere — the world offered friendship no
        /// structural resistance at all, and a diplomatic playthrough solved
        /// itself. Reported from play in exactly those terms.
        ///
        /// The fix is the oldest rule in alignment politics: *the friend of my
        /// enemy cannot also be my friend.* For the pair (a,b), gravity is the
        /// strongest case over every third state c of one side being **deeply
        /// aligned** with c while the other side is in **genuine rivalry** with
        /// c. Both thresholds are deliberately severe — ordinary warmth beside
        /// ordinary coolness produces nothing, so a neutral broker stays
        /// possible; it is committed alignment with somebody's enemy that a
        /// relationship cannot survive.
        /// </summary>
        public static float RivalGravity(GameState state, Relationship pair,
            Dictionary<string, Relationship> lookup)
        {
            // Thresholds are deliberately severe, and severity is load-bearing:
            // at warmth-over-60 / coldness-under-30 the first calibration froze
            // the whole planet — gravity spread coldness, coldness fed more
            // gravity, and thirty years later 93 of 120 pairs were hostile.
            // Only committed blocs (alignment past 68) radiate, and only real
            // enmity (relations under 22) attracts.
            float Warmth(Relationship r) => Math.Max(0f, r.strategicAlignment - 68f) / 32f;
            float Coldness(Relationship r) => Math.Max(0f, 22f - r.relations) / 22f;

            float worst = 0f;
            foreach (var third in state.countries)
            {
                if (third.id == pair.countryA || third.id == pair.countryB) continue;
                if (!lookup.TryGetValue(PairKey(pair.countryA, third.id), out var ac)) continue;
                if (!lookup.TryGetValue(PairKey(pair.countryB, third.id), out var bc)) continue;

                worst = Math.Max(worst, Warmth(ac) * Coldness(bc));
                worst = Math.Max(worst, Warmth(bc) * Coldness(ac));
            }
            return worst;
        }

        static string PairKey(string a, string b)
            => string.CompareOrdinal(a, b) < 0 ? a + "|" + b : b + "|" + a;

        /// <summary>Single-pair gravity, for action-time checks like Outreach.</summary>
        public static float RivalGravity(GameState state, string aId, string bId)
        {
            var pair = state.FindRelationship(aId, bId);
            if (pair == null) return 0f;

            var lookup = new Dictionary<string, Relationship>(state.relationships.Count);
            foreach (var r in state.relationships)
                lookup[PairKey(r.countryA, r.countryB)] = r;
            return RivalGravity(state, pair, lookup);
        }

        /// <summary>
        /// How anxious a state's alliance web makes everyone outside it, 0..1
        /// (spec 04 §8a). A power with defence pacts everywhere reads as
        /// encirclement to whoever is not inside the web — each pact past the
        /// fourth raises it. This is what makes hegemony a held position rather
        /// than a finish line.
        /// </summary>
        public static float PactAnxiety(GameState state, string countryId)
        {
            int pacts = 0;
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken) continue;
                if (treaty.countryA != countryId && treaty.countryB != countryId) continue;
                if (treaty.Has(TreatyCommitment.MutualDefense)) pacts++;
            }
            return Math.Min(1f, Math.Max(0f, pacts - 4) / 6f);
        }

        // ---------- recognition of successor states (spec 04 §5b) ----------

        public const int RecogniseCost = 1;   // CP

        /// <summary>
        /// A state founded after world creation — a `SecessionSystem` breakaway.
        ///
        /// `SecessionSystem` is the only thing in the game that constructs a
        /// country at runtime, and until now diplomacy had no verb about one:
        /// a state could come into existence and the world had no way to take a
        /// position on whether it existed.
        /// </summary>
        public static bool IsSuccessor(GameState state, CountryState country)
            => country != null && country.foundedDate.CompareTo(state.startDate) > 0;

        /// <summary>How many sovereign states have recognised this one.</summary>
        public static int RecognitionCount(GameState state, string successorId)
        {
            int count = 0;
            foreach (var relationship in state.relationships)
            {
                if (!relationship.recognised || !relationship.Involves(successorId)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// 0..1 legitimacy: the share of the world that accepts this state
        /// exists. Read by its stability target and by treaty acceptance, so
        /// recognition is worth something concrete to the state receiving it
        /// rather than being a line on a screen.
        /// </summary>
        public static float Legitimacy(GameState state, CountryState country)
        {
            if (!IsSuccessor(state, country)) return 1f;
            int others = Math.Max(1, state.countries.Count - 1);
            return Math.Min(1f, RecognitionCount(state, country.id) / (float)others);
        }

        public static bool CanRecognise(GameState state, string actorId, string successorId,
            out string reason)
        {
            reason = "";
            var successor = state.FindCountry(successorId);
            if (successor == null) { reason = "NO SUCH STATE."; return false; }
            if (actorId == successorId) { reason = "A STATE DOES NOT RECOGNISE ITSELF."; return false; }

            if (!IsSuccessor(state, successor))
            {
                reason = "THEY HAVE ALWAYS BEEN THERE. Recognition is for a state that has "
                         + "just declared itself.";
                return false;
            }

            var relationship = state.FindRelationship(actorId, successorId);
            if (relationship != null && relationship.recognised)
            {
                reason = "WE ALREADY RECOGNISE THEM.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Recognise a breakaway state. Actor-generic.
        ///
        /// **The cost lands on the parent, and that is the decision.** Nothing
        /// about this is free either way: recognising buys a grateful new state
        /// and an angry old one, and withholding is not neutrality — it is a
        /// position the successor notices for as long as it lasts.
        /// </summary>
        public static bool RecogniseBy(GameState state, string actorId, string successorId)
        {
            if (!CanRecognise(state, actorId, successorId, out _)) return false;

            var successor = state.FindCountry(successorId);
            var relationship = state.FindRelationship(actorId, successorId);
            if (relationship == null) return false;

            relationship.recognised = true;
            relationship.relations = Clamp(relationship.relations + 16f);
            relationship.trust = Clamp(relationship.trust + 12f);
            relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 10f);
            relationship.AddMemory(state.date, "Recognised us when it counted.", 0.9f);

            // The parent state takes it as a hostile act, because it is one.
            string parentId = ParentOf(state, successor);
            var parent = state.FindCountry(parentId);
            if (parent != null && parent.id != actorId)
            {
                var withParent = state.FindRelationship(actorId, parentId);
                if (withParent != null)
                {
                    withParent.relations = Clamp(withParent.relations - 14f);
                    withParent.trust = Clamp(withParent.trust - 10f);
                    withParent.AddMemory(state.date,
                        $"Recognised {successor.displayName} while we still called it ours.", 1f);
                }
            }

            state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                $"Recognises {successor.displayName}.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// The state a breakaway broke away from. Derived from the id convention
        /// `SecessionSystem` already uses (`PARENT_S`) rather than stored, so
        /// there is nothing to migrate and nothing that can disagree with it.
        /// </summary>
        public static string ParentOf(GameState state, CountryState successor)
        {
            if (successor == null) return null;
            int marker = successor.id.LastIndexOf("_S", StringComparison.Ordinal);
            return marker <= 0 ? null : successor.id.Substring(0, marker);
        }

        // ---------- mediating somebody else's war (spec 04 §5c) ----------

        public const int MediationCost = 2;   // CP

        /// <summary>
        /// Whether we can offer to mediate this confrontation, and why not.
        ///
        /// A mediator has to be outside the war and acceptable to both sides.
        /// The world now fights around three of its own wars every thirty years
        /// (spec 06) and the operator could only ever watch them — the pillar
        /// had no verb that acted on a conflict it was not party to.
        /// </summary>
        public static bool CanMediate(GameState state, string actorId,
            Confrontation confrontation, out string reason)
        {
            reason = "";
            if (confrontation == null || confrontation.resolved)
            {
                reason = "THAT SITUATION IS CLOSED.";
                return false;
            }
            if (confrontation.Involves(actorId))
            {
                reason = "WE ARE A PARTY TO IT. A belligerent is not a mediator.";
                return false;
            }
            if (confrontation.escalation < EscalationState.Crisis)
            {
                reason = "NOTHING TO MEDIATE YET.";
                return false;
            }

            // Both sides have to be willing to have us in the room.
            var withA = state.FindRelationship(actorId, confrontation.initiatorId);
            var withB = state.FindRelationship(actorId, confrontation.defenderId);
            if (withA == null || withB == null) { reason = "NO STANDING WITH THEM."; return false; }

            if (withA.relations < MediationFloor || withB.relations < MediationFloor)
            {
                reason = $"ONE SIDE WILL NOT HAVE US IN THE ROOM (needs {MediationFloor:F0} "
                         + "relations with both).";
                return false;
            }
            return true;
        }

        public const float MediationFloor = 35f;

        /// <summary>
        /// Offer to mediate. Actor-generic.
        ///
        /// **Failing has to cost**, or tabling an offer every month and seeing
        /// what sticks is the correct play — the same reasoning that prices a
        /// lost chamber motion. Success buys standing with both sides and a
        /// settlement neither could reach alone; failure spends a little of that
        /// standing with each of them, because we asked them to stop and they
        /// declined in public.
        /// </summary>
        public static bool OfferMediationBy(GameState state, string actorId,
            Confrontation confrontation)
        {
            if (!CanMediate(state, actorId, confrontation, out _)) return false;

            var mediator = state.FindCountry(actorId);
            var withA = state.FindRelationship(actorId, confrontation.initiatorId);
            var withB = state.FindRelationship(actorId, confrontation.defenderId);

            // What a mediator brings: standing with both sides, the diplomatic
            // pillar, and — the term that makes collection and treaties pay off
            // here — how tired of the war the belligerents already are.
            var initiator = state.FindCountry(confrontation.initiatorId);
            var defender = state.FindCountry(confrontation.defenderId);
            float exhaustion = ((initiator?.warExhaustion ?? 0f)
                                + (defender?.warExhaustion ?? 0f)) * 0.5f;

            float odds = (withA.relations + withB.relations) * 0.25f
                         + (mediator?.pillars.diplomacy ?? 0f) * 0.35f
                         + exhaustion * 0.45f
                         + TechnologySystem.Effectiveness(mediator, "CAP_VERIFICATION") * 18f
                         - confrontation.momentum * 0.30f;

            int monthIndex = state.date.MonthsSince(state.startDate);
            var rng = new Random(unchecked(
                state.rngSeed * 7919 + monthIndex * 313
                + Hash.Of(actorId) * 37 + state.NextActionSequence() * 104729));

            bool accepted = rng.NextDouble() * 100.0 < odds;

            if (accepted)
            {
                ConfrontationSystem.CloseWithSettlement(state, confrontation, actorId,
                    $"Mediated by {mediator?.displayName ?? actorId}.");

                foreach (var relationship in new[] { withA, withB })
                {
                    relationship.relations = Clamp(relationship.relations + 12f);
                    relationship.trust = Clamp(relationship.trust + 14f);
                    relationship.AddMemory(state.date, "Brought us out of a war.", 1f);
                }

                state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                    $"Mediates an end to the fighting between "
                    + $"{initiator?.displayName} and {defender?.displayName}.", Publicity.Public);
                return true;
            }

            // Refused, in public.
            foreach (var relationship in new[] { withA, withB })
            {
                relationship.relations = Clamp(relationship.relations - 5f);
                relationship.trust = Clamp(relationship.trust - 3f);
            }
            state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                $"Offer to mediate between {initiator?.displayName} and "
                + $"{defender?.displayName} is declined.", Publicity.Public);
            return false;
        }

        // ---------- normalisation after a war (spec 04 §5d) ----------

        public const int NormalisationCost = 2;   // CP

        /// <summary>
        /// Whether there is a war to put behind us.
        ///
        /// Requires a settlement truce standing between the pair — that is what
        /// marks two states as having *just stopped fighting*, which is the
        /// situation this verb is about. Without one there is nothing to
        /// normalise, only ordinary outreach.
        /// </summary>
        public static bool CanNormalise(GameState state, string actorId, string partnerId,
            out string reason)
        {
            reason = "";
            var relationship = state.FindRelationship(actorId, partnerId);
            if (relationship == null) { reason = "NO STANDING WITH THEM."; return false; }

            if (relationship.settlementTruceMonths <= 0)
            {
                reason = "NO RECENT WAR TO PUT BEHIND US.";
                return false;
            }
            if (state.IsAtWar(actorId) && ConfrontationSystem.ExistingBetween(state, actorId, partnerId) != null)
            {
                reason = "WE ARE STILL FIGHTING THEM.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Put a war behind us. Actor-generic.
        ///
        /// **The one verb that touches `memoryWeight` downward.** Historical
        /// memory is read by treaty acceptance, sanctions relief and alliance
        /// willingness, and until now it only ever accumulated — a pair who
        /// fought in 1986 carried it identically in 2020 whatever either did
        /// about it. That is the one-way-value family in the diplomatic model:
        /// the game had no way to say two countries had *got over* something.
        ///
        /// It is deliberately partial. A third of the weight, once, at the cost
        /// of standing at home — reconciling with an enemy is unpopular with the
        /// people who fought them, which is what stops it being free.
        /// </summary>
        public static bool BeginNormalisationBy(GameState state, string actorId, string partnerId)
        {
            if (!CanNormalise(state, actorId, partnerId, out _)) return false;

            var relationship = state.FindRelationship(actorId, partnerId);
            var actor = state.FindCountry(actorId);
            var partner = state.FindCountry(partnerId);

            relationship.memoryWeight = Math.Max(0f, relationship.memoryWeight * 0.66f);
            relationship.relations = Clamp(relationship.relations + 9f);
            relationship.trust = Clamp(relationship.trust + 7f);

            // The truce runs down faster once both sides are talking, so
            // normalising is also the route back to being able to sign anything.
            relationship.settlementTruceMonths =
                Math.Max(0, relationship.settlementTruceMonths - 8);

            // Unpopular with the people who did the fighting.
            if (actor != null)
            {
                actor.governmentApproval = Clamp(actor.governmentApproval - 4f);
                actor.warSupport = Clamp(actor.warSupport - 6f);
            }

            state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                $"Moves to normalise relations with {partner?.displayName ?? partnerId}.",
                Publicity.Public);
            return true;
        }

        // ---------- standing envoys (spec 04 §5e) ----------

        /// <summary>
        /// The foreign minister, posted to one capital. One at a time: an envoy
        /// who is everywhere is a modifier, not a decision.
        /// </summary>
        public static bool AssignEnvoyBy(GameState state, string actorId, string postingId)
        {
            var country = state.FindCountry(actorId);
            var official = country?.FindOfficial(Pillar.Diplomacy);
            if (official == null) return false;

            // An empty posting recalls them, which has to be possible or the
            // first choice is permanent.
            if (string.IsNullOrEmpty(postingId))
            {
                official.envoyToCountryId = "";
                return true;
            }

            if (postingId == actorId) return false;
            if (state.FindCountry(postingId) == null) return false;

            official.envoyToCountryId = postingId;
            return true;
        }

        /// <summary>
        /// What a posted envoy is worth on one relationship, 0..1.
        ///
        /// **Scaled by the official's competence**, which is the whole point:
        /// this is the first place the diplomatic minister's quality shows up in
        /// a *relationship* rather than in the pillar. A poor envoy is close to
        /// no envoy; a good one is a standing channel.
        ///
        /// Read by monthly relationship drift and by treaty willingness, so
        /// where the minister is posted is a real allocation of one scarce
        /// person.
        /// </summary>
        public static float EnvoyWeight(GameState state, string actorId, string partnerId)
        {
            var country = state.FindCountry(actorId);
            var official = country?.FindOfficial(Pillar.Diplomacy);
            if (official == null || official.envoyToCountryId != partnerId) return 0f;

            // Direct Control means the operator is running the pillar themselves
            // and the minister is executing, not representing us abroad.
            if (official.mode == ControlMode.DirectControl) return 0f;

            return Math.Max(0f, Math.Min(1f, official.competence / 100f));
        }

        // ---------- summits (spec 04 §5g) ----------

        public const int SummitCost = 3;          // CP
        public const int SummitPreparation = 4;   // months

        public static bool CanConveneSummit(GameState state, string actorId, string partnerId,
            out string reason)
        {
            reason = "";
            var relationship = state.FindRelationship(actorId, partnerId);
            if (relationship == null) { reason = "NO STANDING WITH THEM."; return false; }

            if (relationship.summitMonthsRemaining > 0)
            {
                reason = $"A SUMMIT IS ALREADY BEING PREPARED ({relationship.summitMonthsRemaining} "
                         + "MONTH(S)).";
                return false;
            }
            if (ConfrontationSystem.ExistingBetween(state, actorId, partnerId) != null)
            {
                reason = "WE ARE IN A CONFRONTATION WITH THEM. Settle it or mediate it first.";
                return false;
            }
            if (relationship.relations < SummitFloor)
            {
                reason = $"THEY WILL NOT SIT DOWN WITH US (needs {SummitFloor:F0} relations).";
                return false;
            }
            return true;
        }

        public const float SummitFloor = 30f;

        /// <summary>Announce a summit. Actor-generic. The work is the months.</summary>
        public static bool ConveneSummitBy(GameState state, string actorId, string partnerId)
        {
            if (!CanConveneSummit(state, actorId, partnerId, out _)) return false;

            var relationship = state.FindRelationship(actorId, partnerId);
            relationship.summitMonthsRemaining = SummitPreparation;

            var actor = state.FindCountry(actorId);
            var partner = state.FindCountry(partnerId);
            state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                $"{actor?.displayName} and {partner?.displayName} announce talks.",
                Publicity.Public);
            return true;
        }

        /// <summary>
        /// Advance every announced summit, and resolve the ones that arrive.
        ///
        /// **The world can move underneath it.** Whether the meeting is worth
        /// anything is judged on the relationship *as it stands the month it
        /// happens*, not as it stood when it was called — so a summit announced
        /// in a warm month and met in a cold one produces a communiqué and
        /// nothing else. That is the whole reason it takes four months.
        /// </summary>
        static void AdvanceSummits(GameState state)
        {
            foreach (var relationship in state.relationships)
            {
                if (relationship.summitMonthsRemaining <= 0) continue;

                // A war between the pair collapses the talks outright.
                if (ConfrontationSystem.ExistingBetween(
                        state, relationship.countryA, relationship.countryB) != null)
                {
                    relationship.summitMonthsRemaining = 0;
                    CollapseSummit(state, relationship, "overtaken by events");
                    continue;
                }

                relationship.summitMonthsRemaining--;
                if (relationship.summitMonthsRemaining > 0) continue;

                if (relationship.relations < SummitFloor)
                {
                    CollapseSummit(state, relationship, "the two sides had drifted too far apart");
                    continue;
                }

                // It met, and it was worth having.
                relationship.relations = Clamp(relationship.relations + 14f);
                relationship.trust = Clamp(relationship.trust + 16f);
                relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 9f);
                relationship.AddMemory(state.date, "Sat down with us and meant it.", 1.2f);

                var a = state.FindCountry(relationship.countryA);
                var b = state.FindCountry(relationship.countryB);
                state.AddChronicle(ChronicleCategory.Diplomatic, relationship.countryA,
                    $"Summit between {a?.displayName} and {b?.displayName} concludes.",
                    Publicity.Public);

                if (relationship.Involves(state.playerCountryId))
                {
                    string other = relationship.PartnerOf(state.playerCountryId);
                    state.AddNotification(NotificationClass.Priority, "SUMMIT CONCLUDES",
                        $"The meeting with {state.FindCountry(other)?.displayName} went well. "
                        + "Standing and trust have both moved.", other,
                        desk: ReportingDesk.Diplomacy);
                }
            }
        }

        static void CollapseSummit(GameState state, Relationship relationship, string why)
        {
            relationship.summitMonthsRemaining = 0;

            // A failed summit is worse than none: it was announced, and it did
            // not deliver. Small, because the announcement is the exposure and
            // the drift is what actually did the damage.
            relationship.relations = Clamp(relationship.relations - 4f);

            var a = state.FindCountry(relationship.countryA);
            var b = state.FindCountry(relationship.countryB);
            state.AddChronicle(ChronicleCategory.Diplomatic, relationship.countryA,
                $"Talks between {a?.displayName} and {b?.displayName} collapse — {why}.",
                Publicity.Public);

            if (!relationship.Involves(state.playerCountryId)) return;
            string other = relationship.PartnerOf(state.playerCountryId);
            state.AddNotification(NotificationClass.Priority, "SUMMIT COLLAPSES",
                $"The meeting with {state.FindCountry(other)?.displayName} did not happen — "
                + $"{why}. It was announced, and it did not deliver.", other,
                desk: ReportingDesk.Diplomacy);
        }

        public static void MonthlyUpdate(GameState state)
        {
            UpdateBasingRights(state);
            AdvanceSummits(state);

            // O(1) pair lookup for the gravity pass — FindRelationship scans the
            // whole list, and gravity reads two third-party pairs per country per
            // relationship, which is quadratic-times-linear without this.
            var lookup = new Dictionary<string, Relationship>(state.relationships.Count);
            foreach (var relationship in state.relationships)
                lookup[PairKey(relationship.countryA, relationship.countryB)] = relationship;

            foreach (var relationship in state.relationships)
            {
                var a = state.FindCountry(relationship.countryA);
                var b = state.FindCountry(relationship.countryB);
                if (a == null || b == null) continue;

                // A posted envoy keeps a relationship warm without the operator
                // spending a Command Point on it every month (spec 04 §5e).
                // Small on purpose: a standing channel is worth about a third of
                // an outreach a month, so it is a way to *hold* a relationship
                // rather than a cheaper way to build one — and it is scaled by
                // the minister's competence, so a weak appointment posted abroad
                // is close to nobody being there.
                float envoy = Math.Max(EnvoyWeight(state, a.id, b.id),
                                       EnvoyWeight(state, b.id, a.id));
                if (envoy > 0.01f)
                {
                    relationship.relations = Clamp(relationship.relations + envoy * 0.9f);
                    relationship.trust = Clamp(relationship.trust + envoy * 0.5f);
                }

                // The friend of my enemy: deep alignment with a state's genuine
                // rival caps how warm this relationship can be, and erodes what
                // is above the cap. **A ceiling, not merely a drag** — the first
                // version subtracted a fraction of a point per month, and the
                // befriend-everyone bot simply out-spammed it with outreach
                // (+3/month beats −0.9/month forever): fifteen of fifteen
                // friendships survived the mechanic built to prevent them.
                // Courtesy calls cannot outrun bloc politics.
                float gravity = RivalGravity(state, relationship, lookup);
                if (gravity > 0.01f)
                {
                    // 85, not 55: at the realistic gravity a committed bloc
                    // produces (~0.45), a factor of 55 capped relations at 75 —
                    // still comfortably a friendship, and the bot proved it by
                    // befriending all fifteen anyway. The cap has to cross the
                    // friendship line, or it decorates the thing it exists to
                    // prevent.
                    float ceiling = 100f - gravity * 85f;
                    if (relationship.relations > ceiling)
                        relationship.relations = Approach(relationship.relations, ceiling, 0.12f);
                    if (relationship.trust > ceiling)
                        relationship.trust = Approach(relationship.trust, ceiling, 0.08f);
                    relationship.strategicAlignment = Clamp(relationship.strategicAlignment - gravity * 0.3f);
                }

                // What you learned of a partner's doctrine goes stale.
                //
                // `doctrineFamiliarity` was written in exactly one place
                // (`ExerciseSystem`) and had **no decay path anywhere in the
                // codebase** — the one-way-value class again, running upward.
                // Knowledge of how an army fought in 1984 stayed perfectly current
                // fifty years later, and the test asserting it "outlives the
                // friendship" could not fail.
                //
                // Deliberately slow, and proportional so a deep familiarity fades
                // faster than a shallow one: the design point is that it *does*
                // outlive the friendship (a former partner is genuinely easier to
                // fight), so a decade of usefulness is right and permanence is not.
                if (relationship.doctrineFamiliarity > 0f)
                    relationship.doctrineFamiliarity = Clamp(
                        relationship.doctrineFamiliarity
                        - (0.05f + relationship.doctrineFamiliarity * 0.004f));

                // Threat perception tracks capability and posture — and the size
                // of a state's alliance web. An army is a fact about them; a web
                // of defence pacts is a fact about everyone else's room to move.
                float anxietyA = state.FindTreaty(a.id, b.id) == null ? PactAnxiety(state, a.id) : 0f;
                float anxietyB = state.FindTreaty(a.id, b.id) == null ? PactAnxiety(state, b.id) : 0f;
                relationship.threatPerceptionOfA = Approach(relationship.threatPerceptionOfA,
                    Clamp(a.pillars.military * 0.45f + (a.military.alertPosture ? 15f : 0f)
                          + anxietyA * 14f), 0.1f);
                relationship.threatPerceptionOfB = Approach(relationship.threatPerceptionOfB,
                    Clamp(b.pillars.military * 0.45f + (b.military.alertPosture ? 15f : 0f)
                          + anxietyB * 14f), 0.1f);

                // Dependence follows live trade.
                var link = state.FindTrade(a.id, b.id);
                float dependenceTarget = link != null && !link.embargoed ? Clamp(link.volume * 0.6f) : 0f;
                relationship.dependenceAOnB = Approach(relationship.dependenceAOnB, dependenceTarget, 0.08f);
                relationship.dependenceBOnA = Approach(relationship.dependenceBOnA, dependenceTarget, 0.08f);

                // Sanctions are read as hostility by the target.
                if (state.FindSanction(a.id, b.id) != null || state.FindSanction(b.id, a.id) != null)
                {
                    relationship.relations = Clamp(relationship.relations - 1.2f);
                    relationship.trust = Clamp(relationship.trust - 0.4f);
                }
                else
                {
                    // Relations drift slowly toward the alignment baseline.
                    relationship.relations = Approach(relationship.relations, relationship.strategicAlignment, 0.02f);
                }

                var treaty = state.FindTreaty(a.id, b.id);
                if (treaty != null)
                {
                    relationship.trust = Clamp(relationship.trust + 0.25f);
                    relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 0.15f);
                }

                // Historical memory fades but never fully disappears.
                relationship.memoryWeight *= 0.985f;

                if (relationship.sanctionsTruceMonths > 0) relationship.sanctionsTruceMonths--;
                if (relationship.settlementTruceMonths > 0) relationship.settlementTruceMonths--;
            }

            UpdateConfrontationEffects(state);
            UpdateCoalitions(state);
        }

        static void UpdateConfrontationEffects(GameState state)
        {
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                var relationship = state.FindRelationship(confrontation.initiatorId, confrontation.defenderId);
                if (relationship == null) continue;

                float damage = confrontation.escalation >= EscalationState.TotalWar ? 2.5f
                             : confrontation.escalation >= EscalationState.LimitedConflict ? 1.5f
                             : confrontation.escalation >= EscalationState.Crisis ? 0.8f : 0.3f;

                relationship.relations = Clamp(relationship.relations - damage);
                relationship.trust = Clamp(relationship.trust - damage * 0.5f);
                relationship.strategicAlignment = Clamp(relationship.strategicAlignment - damage * 0.4f);

                if (confrontation.escalation >= EscalationState.LimitedConflict && confrontation.monthsActive % 6 == 0)
                    relationship.AddMemory(state.date, "Armed conflict", -3f);
            }
        }

        /// <summary>
        /// Coalition participation can change mid-conflict as interests shift
        /// (GDD §15.2): members leave when the war sours or costs mount.
        /// </summary>
        static void UpdateCoalitions(GameState state)
        {
            foreach (var coalition in state.coalitions)
            {
                if (coalition.dissolved) continue;

                Confrontation confrontation = null;
                foreach (var c in state.confrontations)
                    if (c.id == coalition.confrontationId) { confrontation = c; break; }

                if (confrontation == null || confrontation.resolved)
                {
                    coalition.dissolved = true;
                    continue;
                }

                var leaving = new List<string>();
                foreach (var memberId in coalition.memberIds)
                {
                    if (memberId == coalition.leaderId) continue;
                    if (CoalitionWillingness(state, coalition.leaderId, memberId, coalition.targetId) < 30f)
                        leaving.Add(memberId);
                }

                foreach (var memberId in leaving)
                {
                    coalition.memberIds.Remove(memberId);
                    var country = state.FindCountry(memberId);
                    state.AddNotification(NotificationClass.Priority, "COALITION PARTNER WITHDRAWS",
                        $"{country?.displayName} leaves the coalition.", memberId,
                        desk: ReportingDesk.Diplomacy);
                    state.AddChronicle(ChronicleCategory.Diplomatic, memberId,
                        $"Withdrew from the coalition against {state.FindCountry(coalition.targetId)?.displayName}.",
                        Publicity.Public);
                }
            }
        }

        /// <summary>Coalition support adds weight to the player's confrontation momentum.</summary>
        public static float CoalitionStrength(GameState state, Confrontation confrontation)
            => CoalitionStrength(state, confrontation, state.playerCountryId);

        /// <summary>
        /// Combat weight a country's coalition partners add to its operations.
        ///
        /// Optionally for a specific kind of operation, in which case partners
        /// contribute through the same branch profile the leader does. Summing a
        /// partner's whole military into an air strike we fly with our air force
        /// alone would make force composition matter for us and not for them —
        /// and would quietly make a landlocked ally useful in a blockade.
        /// </summary>
        public static float CoalitionStrength(GameState state, Confrontation confrontation, string leaderId,
            OperationType? operationType = null)
        {
            var coalition = state.FindCoalitionLedBy(confrontation.id, leaderId);
            if (coalition == null) return 0f;

            float strength = 0f;
            foreach (var memberId in coalition.memberIds)
            {
                if (memberId == coalition.leaderId) continue;
                var member = state.FindCountry(memberId);
                if (member == null) continue;

                float contribution = operationType.HasValue
                    ? MilitarySystem.BranchPowerFor(member.military, operationType.Value)
                    : member.military.TotalPower;

                // Forces that have trained together contribute far more than
                // forces merely present (GDD §15.3).
                var relationship = state.FindRelationship(coalition.leaderId, memberId);
                float interoperability = relationship?.interoperability ?? 0f;
                strength += contribution * 0.3f * (1f + interoperability / 130f);
            }
            return strength;
        }

        static float Approach(float current, float target, float rate) => current + (target - current) * rate;
        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

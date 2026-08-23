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
                $"{state.FindCountry(proposerId)?.displayName} and {target.displayName} conclude an agreement.",
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
        public static float TreatyWillingness(GameState state, string targetId, List<TreatyCommitment> commitments)
            => TreatyWillingness(state, state.playerCountryId, targetId, commitments);

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

            // Operator negotiating craft (player proposals only).
            if (proposerId == state.playerCountryId)
                willingness += ProgressionSystem.EffectValue(state, SkillEffect.TreatyPersuasion);

            // Institutions others want to be inside (GDD §11).
            willingness += TechnologySystem.Effectiveness(player, "CAP_CONVENING") * 10f;

            // Heavier commitments demand a warmer relationship.
            foreach (var commitment in commitments)
            {
                switch (commitment)
                {
                    case TreatyCommitment.MutualDefense: willingness -= 22f; break;
                    case TreatyCommitment.JointPlanning: willingness -= 12f; break;
                    case TreatyCommitment.IntelligenceSharing: willingness -= 10f; break;
                    case TreatyCommitment.Transit: willingness -= 8f; break;
                    case TreatyCommitment.TradePreference: willingness -= 2f; break;
                    case TreatyCommitment.NonAggression: willingness += 4f; break;
                }
            }

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
        {
            var treaty = state.FindTreaty(state.playerCountryId, partnerId);
            if (treaty == null) return false;

            treaty.broken = true;
            treaty.brokenBy = state.playerCountryId;

            var relationship = state.FindRelationship(state.playerCountryId, partnerId);
            relationship.relations = Clamp(relationship.relations - 25f);
            relationship.trust = Clamp(relationship.trust - 35f);
            relationship.AddMemory(state.date, "Treaty broken against them", -6f);

            // Third parties revise their view of our reliability.
            var player = state.PlayerCountry;
            player.pillars.diplomacy = Clamp(player.pillars.diplomacy - 6f);
            foreach (var other in state.relationships)
            {
                if (!other.Involves(state.playerCountryId)) continue;
                if (other.Involves(partnerId)) continue;
                other.trust = Clamp(other.trust - 8f);
                other.AddMemory(state.date, "Observed treaty violation", -1.5f);
            }

            var partner = state.FindCountry(partnerId);
            state.AddNotification(NotificationClass.Priority, "TREATY BROKEN",
                $"Commitments to {partner?.displayName} repudiated. Reputation damaged.", partnerId);
            state.AddChronicle(ChronicleCategory.Diplomatic, state.playerCountryId,
                                $"Treaty with {partner?.displayName} broken.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Ask other states to join the player's confrontation. Each decides on
        /// its own interests; the player can exploit an enemy's rocky
        /// relationships to recruit support (GDD §15.2).
        /// </summary>
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

        public static void MonthlyUpdate(GameState state)
        {
            UpdateBasingRights(state);

            foreach (var relationship in state.relationships)
            {
                var a = state.FindCountry(relationship.countryA);
                var b = state.FindCountry(relationship.countryB);
                if (a == null || b == null) continue;

                // Threat perception tracks capability and posture.
                relationship.threatPerceptionOfA = Approach(relationship.threatPerceptionOfA,
                    Clamp(a.pillars.military * 0.45f + (a.military.alertPosture ? 15f : 0f)), 0.1f);
                relationship.threatPerceptionOfB = Approach(relationship.threatPerceptionOfB,
                    Clamp(b.pillars.military * 0.45f + (b.military.alertPosture ? 15f : 0f)), 0.1f);

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

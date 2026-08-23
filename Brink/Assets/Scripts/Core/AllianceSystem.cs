using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Alliance obligations (GDD §15.2).
    ///
    /// A mutual defense commitment is only meaningful if it is actually called
    /// upon. When a confrontation reaches Limited Conflict, every signatory to a
    /// defense treaty with the defender must decide: honor the commitment and
    /// join the war, or repudiate it and take the reputational damage. Nobody is
    /// forced — but "commitments may be broken, and trustworthiness and future
    /// diplomacy suffer."
    /// </summary>
    public static class AllianceSystem
    {
        /// <summary>Crisis definition id used when the player is the one called upon.</summary>
        public const string PlayerObligationCrisisId = "ALLIANCE_OBLIGATION";

        /// <summary>
        /// Called when a confrontation first reaches Limited Conflict. Invokes
        /// every unbroken mutual-defense commitment held by the defender.
        /// </summary>
        public static void InvokeObligations(GameState state, Confrontation confrontation)
        {
            if (confrontation.obligationsInvoked) return;
            confrontation.obligationsInvoked = true;

            string defenderId = confrontation.defenderId;
            string aggressorId = confrontation.initiatorId;

            // Copy first: honoring can add coalitions and modify state.
            var signatories = new List<string>();
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Has(TreatyCommitment.MutualDefense)) continue;
                if (!treaty.Involves(defenderId)) continue;
                string ally = treaty.PartnerOf(defenderId);
                if (ally == aggressorId) continue; // they are fighting each other
                signatories.Add(ally);
            }

            foreach (var allyId in signatories)
            {
                if (allyId == state.playerCountryId)
                    RaisePlayerObligation(state, confrontation);
                else
                    ResolveAiObligation(state, confrontation, allyId);
            }
        }

        // ---------- the player's decision ----------

        static void RaisePlayerObligation(GameState state, Confrontation confrontation)
        {
            var defender = state.FindCountry(confrontation.defenderId);
            var aggressor = state.FindCountry(confrontation.initiatorId);

            var crisis = new ActiveCrisis
            {
                defId = PlayerObligationCrisisId,
                title = "ALLIANCE OBLIGATION INVOKED",
                body = $"{aggressor?.displayName} has opened hostilities against " +
                       $"{defender?.displayName}. Our mutual defense commitment has been invoked. " +
                       "The treaty is explicit. The decision is not.",
                startDate = state.date,
                options = new List<CrisisOption>
                {
                    new CrisisOption
                    {
                        label = "HONOR THE COMMITMENT",
                        description = "Join the war on their side. Costly, but the alliance holds.",
                        resultText = $"We have entered the conflict alongside {defender?.displayName}.",
                        approvalDelta = -4, stabilityDelta = -2
                    },
                    new CrisisOption
                    {
                        label = "REPUDIATE THE COMMITMENT",
                        description = "Stay out. Every state watching will draw conclusions.",
                        resultText = $"We have declined to act. {defender?.displayName} stands alone.",
                        approvalDelta = 2
                    }
                }
            };

            state.activeCrises.Add(crisis);
            state.crisesFacedThisYear++;
            state.AddNotification(NotificationClass.Flash, crisis.title,
                "Immediate decision required. End Month is suspended.", confrontation.defenderId);
            state.AddChronicle(ChronicleCategory.Diplomatic, state.playerCountryId,
                $"Defense obligation to {defender?.displayName} invoked.");
            GameLog.Warn("ALLIANCE", "Player defense obligation invoked.");
        }

        /// <summary>
        /// Apply the player's answer. Called by <see cref="CrisisSystem.Resolve"/>
        /// when the crisis is an alliance obligation.
        /// </summary>
        public static void ApplyPlayerDecision(GameState state, bool honored)
        {
            var confrontation = FindObligationConfrontation(state);
            if (confrontation == null) return;

            if (honored) Honor(state, confrontation, state.playerCountryId);
            else Repudiate(state, confrontation, state.playerCountryId);
        }

        /// <summary>The war whose obligation the player is currently answering.</summary>
        static Confrontation FindObligationConfrontation(GameState state)
        {
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || !confrontation.obligationsInvoked) continue;
                var treaty = state.FindTreaty(state.playerCountryId, confrontation.defenderId);
                if (treaty != null && treaty.Has(TreatyCommitment.MutualDefense))
                    return confrontation;
            }
            return null;
        }

        // ---------- AI decisions ----------

        static void ResolveAiObligation(GameState state, Confrontation confrontation, string allyId)
        {
            if (HonorWillingness(state, confrontation, allyId) >= 50f)
                Honor(state, confrontation, allyId);
            else
                Repudiate(state, confrontation, allyId);
        }

        /// <summary>
        /// How willing a signatory is to actually fight. Warmth and shared threat
        /// argue for honoring; exhaustion, instability and dependence on the
        /// aggressor argue for finding a reason not to.
        /// </summary>
        public static float HonorWillingness(GameState state, Confrontation confrontation, string allyId)
        {
            var ally = state.FindCountry(allyId);
            if (ally == null) return 0f;

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            var toAggressor = state.FindRelationship(allyId, confrontation.initiatorId);
            if (toDefender == null || toAggressor == null) return 0f;

            float willingness = 30f
                                + toDefender.relations * 0.35f
                                + toDefender.trust * 0.25f
                                + toDefender.interoperability * 0.15f
                                + toAggressor.ThreatPerceivedBy(allyId) * 0.30f
                                - toAggressor.DependenceOf(allyId) * 0.45f
                                - ally.warExhaustion * 0.35f
                                - Math.Max(0f, 55f - ally.stability) * 0.5f
                                - Math.Max(0f, 45f - ally.warSupport) * 0.3f;

            // An honorable state's own record weighs on the decision.
            willingness += toDefender.memoryWeight * 1.2f;

            return willingness;
        }

        // ---------- outcomes ----------

        static void Honor(GameState state, Confrontation confrontation, string allyId)
        {
            var ally = state.FindCountry(allyId);
            var defender = state.FindCountry(confrontation.defenderId);

            // Join the defender's coalition, creating it if this is the first ally.
            var coalition = state.FindCoalitionLedBy(confrontation.id, confrontation.defenderId);
            if (coalition == null)
            {
                coalition = new Coalition
                {
                    id = $"COAL_DEF_{confrontation.id}",
                    leaderId = confrontation.defenderId,
                    confrontationId = confrontation.id,
                    targetId = confrontation.initiatorId
                };
                coalition.memberIds.Add(confrontation.defenderId);
                state.coalitions.Add(coalition);
            }
            if (!coalition.memberIds.Contains(allyId)) coalition.memberIds.Add(allyId);

            ally.military.alertPosture = true;
            ally.warSupport = Clamp(ally.warSupport - 5f);

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            toDefender.relations = Clamp(toDefender.relations + 12f);
            toDefender.trust = Clamp(toDefender.trust + 15f);
            toDefender.AddMemory(state.date, "Honored defense commitment", 6f);

            // And now they are at war with the aggressor.
            var toAggressor = state.FindRelationship(allyId, confrontation.initiatorId);
            toAggressor.relations = Clamp(toAggressor.relations - 30f);
            toAggressor.trust = Clamp(toAggressor.trust - 15f);
            toAggressor.AddMemory(state.date, "Entered war against us", -5f);

            bool playerInvolved = allyId == state.playerCountryId
                                  || confrontation.Involves(state.playerCountryId);
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "ALLIANCE HONORED",
                $"{ally.displayName} enters the conflict alongside {defender?.displayName}.", allyId,
                desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, allyId,
                $"Honored defense commitment to {defender?.displayName}.", Publicity.Public);
            GameLog.Info("ALLIANCE", $"{allyId} honored its commitment to {confrontation.defenderId}.");
        }

        static void Repudiate(GameState state, Confrontation confrontation, string allyId)
        {
            var ally = state.FindCountry(allyId);
            var defender = state.FindCountry(confrontation.defenderId);

            var treaty = state.FindTreaty(allyId, confrontation.defenderId);
            if (treaty != null)
            {
                treaty.broken = true;
                treaty.brokenBy = allyId;
            }

            var toDefender = state.FindRelationship(allyId, confrontation.defenderId);
            toDefender.relations = Clamp(toDefender.relations - 35f);
            toDefender.trust = Clamp(toDefender.trust - 45f);
            toDefender.AddMemory(state.date, "Abandoned us when the treaty was invoked", -10f);

            ally.pillars.diplomacy = Clamp(ally.pillars.diplomacy - 8f);

            // Everyone recalculates what this state's signature is worth.
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(allyId)) continue;
                if (relationship.Involves(confrontation.defenderId)) continue;
                relationship.trust = Clamp(relationship.trust - 12f);
                relationship.AddMemory(state.date, "Observed an abandoned defense commitment", -2.5f);
            }

            bool playerInvolved = allyId == state.playerCountryId
                                  || confrontation.Involves(state.playerCountryId);
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "ALLIANCE REPUDIATED",
                $"{ally.displayName} declines to honor its commitment to {defender?.displayName}. " +
                "Its guarantees are now discounted everywhere.", allyId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, allyId,
                $"Repudiated defense commitment to {defender?.displayName}.", Publicity.Public);
            GameLog.Warn("ALLIANCE", $"{allyId} repudiated its commitment to {confrontation.defenderId}.");
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

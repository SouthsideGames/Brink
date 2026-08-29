using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// What each government believes about everyone else, and what it expects them
    /// to do next (GDD §24.2).
    ///
    /// This is the answer to the specific failure where a player learns the AI in
    /// one playthrough, resets, and runs a known-winning opening. It cannot be
    /// fixed with randomness — random is not the same as unpredictable, and a
    /// world that behaves differently for no reason is worse than one that behaves
    /// the same for good reasons. The fix is that **AI behaviour is a function of
    /// what the player does**, not of the seed:
    ///
    ///   A player who always opens with covert action finds, by the third year,
    ///   a world of hardened security services. A player who always rushes
    ///   militarily finds fortified, allied neighbours waiting. Reset and repeat
    ///   the opening and the same counter emerges again — the world is not
    ///   surprising you, it is *answering* you. The way past it is to change what
    ///   you do, which is the thing the game is supposed to be about.
    ///
    /// Two rules keep it fair, and both are tested:
    ///
    /// 1. **Only what could be seen.** Observations come from public acts and from
    ///    covert acts that were exposed. A state with poor collection predicts
    ///    badly. This is what keeps intelligence worth buying, and it is why a
    ///    deception campaign can make a rival prepare for the wrong war.
    /// 2. **A prediction must change a decision.** The old `PlayerAssessment` was
    ///    computed every month and read by exactly one line of code, so a player
    ///    assessed as maximally dangerous changed nothing about how the world
    ///    behaved. Everything here feeds objective scoring.
    /// </summary>
    public static class AIPrediction
    {
        /// <summary>How fast a model moves toward what was observed this month.</summary>
        const float Adaptation = 0.09f;

        /// <summary>Months of observation before predictions are worth acting on.</summary>
        public const int SettledAfter = 8;

        /// <summary>
        /// Update this government's model of every other state, from what it could
        /// actually have observed.
        /// </summary>
        public static void Observe(GameState state, AIState ai)
        {
            var self = state.FindCountry(ai.countryId);
            if (self == null) return;

            foreach (var other in state.countries)
            {
                if (other.id == ai.countryId) continue;

                var model = ai.ModelOf(other.id);
                if (model == null)
                {
                    model = new OpponentModel { countryId = other.id };
                    ai.opponentModels.Add(model);
                }

                model.observations++;
                model.confidence = AISystem.EstimateConfidence(
                    state, ai.countryId, other.id, IntelDomain.Military);

                // What this state has visibly done. Counts are converted to a
                // 0..100 intensity and then eased toward, so a single act does not
                // define a country forever and a long pattern is hard to shake.
                model.aggression = Ease(model.aggression,
                    ObservedAggression(state, ai.countryId, other.id));
                model.economicCoercion = Ease(model.economicCoercion,
                    ObservedCoercion(state, other.id));
                model.covertActivity = Ease(model.covertActivity,
                    ObservedSubversion(state, ai.countryId, other.id));
                model.diplomaticActivity = Ease(model.diplomaticActivity,
                    ObservedDiplomacy(state, other.id));

                model.predictedPath = InferPath(state, ai, other, model);
                model.predictedMove = InferMove(state, ai, self, other, model);
            }
        }

        /// <summary>
        /// What we expect them to do next, and how strongly we believe it.
        ///
        /// Returns 0 when we have no usable read — a government that has just met
        /// another should not be confidently preparing for anything.
        /// </summary>
        public static float Expectation(AIState ai, string otherId, PredictedMove move)
        {
            var model = ai.ModelOf(otherId);
            if (model == null) return 0f;
            if (model.observations < SettledAfter) return 0f;
            if (model.predictedMove != move) return 0f;

            float intensity;
            switch (move)
            {
                case PredictedMove.Attack: intensity = model.aggression; break;
                case PredictedMove.Coerce: intensity = model.economicCoercion; break;
                case PredictedMove.Subvert: intensity = model.covertActivity; break;
                case PredictedMove.Court: intensity = model.diplomaticActivity; break;
                default: intensity = 25f; break;
            }

            // Scaled by how good our reporting is. Acting hard on a guess is what
            // caution exists to discourage, and a state with no collection should
            // not be able to counter perfectly.
            return intensity * (0.35f + 0.65f * model.confidence);
        }

        // ---------- inference ----------

        /// <summary>
        /// What we think they are trying to achieve. Coarse, and drawn from the
        /// balance of what they have visibly been doing rather than from their
        /// actual `AIState` — reading that would be telepathy.
        /// </summary>
        static StrategicPath InferPath(GameState state, AIState ai, CountryState other,
            OpponentModel model)
        {
            float military = AISystem.PerceivedStrength(state, ai.countryId, other, IntelDomain.Military);
            float economic = AISystem.PerceivedStrength(state, ai.countryId, other, IntelDomain.Economic);

            if (model.aggression > 55f && military > 55f) return StrategicPath.MilitaryDominance;
            if (model.diplomaticActivity > 55f) return StrategicPath.InstitutionalWeight;
            if (model.economicCoercion > 50f || economic > military + 12f)
                return StrategicPath.EconomicPrimacy;
            if (other.stability < 45f) return StrategicPath.Survival;
            if (model.aggression > 35f) return StrategicPath.RegionalHegemony;
            return StrategicPath.TechnologicalEdge;
        }

        /// <summary>
        /// What we expect them to do next.
        ///
        /// Ordered by what a staff would worry about first: an army massing near
        /// us outranks a trade delegation, whatever the long-run pattern says.
        /// </summary>
        static PredictedMove InferMove(GameState state, AIState ai, CountryState self,
            CountryState other, OpponentModel model)
        {
            // Already shooting at us — the prediction is not the interesting part.
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved) continue;
                if (confrontation.Involves(self.id) && confrontation.Involves(other.id))
                    return PredictedMove.Attack;
            }

            var relationship = state.FindRelationship(self.id, other.id);
            float relations = relationship?.relations ?? 50f;
            float theirMilitary = AISystem.PerceivedStrength(
                state, ai.countryId, other, IntelDomain.Military);

            // Are we the kind of target this state goes after? Weakness invites
            // pressure, and being disliked invites it more.
            bool weLookVulnerable = theirMilitary > self.pillars.military + 6f;
            bool badBlood = relations < 42f;

            // A record of coming for us specifically is its own prediction, and it
            // does not stop being one because we have since caught up. Gating
            // this on looking vulnerable meant a state that grew its own army
            // stopped expecting the wars it had already been having — the fogged
            // estimate of them stays roughly flat while our own pillar climbs, so
            // late in a campaign nobody ever looked vulnerable and nobody ever
            // expected an attack.
            bool hasComeForUsBefore = model.aggression > 60f;

            if (badBlood && (hasComeForUsBefore || (model.aggression > 48f && weLookVulnerable)))
                return PredictedMove.Attack;
            if (model.covertActivity > 45f && badBlood) return PredictedMove.Subvert;
            if (model.economicCoercion > 45f && badBlood) return PredictedMove.Coerce;
            if (model.diplomaticActivity > 50f) return PredictedMove.Court;
            if (model.aggression > 40f && badBlood) return PredictedMove.Coerce;
            if (model.observations < SettledAfter) return PredictedMove.Unknown;
            return theirMilitary > 45f ? PredictedMove.Build : PredictedMove.Hold;
        }

        // ---------- observation, fog-respecting ----------

        /// <summary>
        /// Confrontations they started, and treaties they broke. Both are public
        /// acts — a war is not a secret and neither is walking out of a pact.
        /// </summary>
        static float ObservedAggression(GameState state, string observerId, string subjectId)
        {
            const int MemoryWindowMonths = 120;
            int acts = 0;

            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.initiatorId != subjectId) continue;
                if (state.date.MonthsSince(confrontation.startDate) > MemoryWindowMonths) continue;
                // Aggression aimed at us reads far louder than aggression in general.
                acts += confrontation.Involves(observerId) ? 3 : 1;
            }

            foreach (var treaty in state.treaties)
                if (treaty.broken && treaty.brokenBy == subjectId) acts += 2;

            return Math.Min(100f, acts * 16f);
        }

        /// <summary>Sanctions are announced. Tariffs are published.</summary>
        static float ObservedCoercion(GameState state, string subjectId)
        {
            int acts = 0;
            foreach (var sanction in state.sanctions)
                if (sanction.senderId == subjectId) acts++;
            foreach (var link in state.trade)
                if (link.Involves(subjectId) && (link.embargoed || link.tariff > 40f)) acts++;
            return Math.Min(100f, acts * 18f);
        }

        /// <summary>
        /// Only covert action that was *caught*. An intact network is invisible,
        /// which is exactly what makes running one worthwhile — and what makes a
        /// careless operator teach the world to defend against them.
        ///
        /// Read from the public record rather than from the live `compromised`
        /// flag, which is cleared again within a month or two as the network
        /// rebuilds. That flag answers "is this blown right now"; a government
        /// forming expectations wants "have they been caught doing this", and the
        /// chronicle is where that is written down permanently.
        /// </summary>
        static float ObservedSubversion(GameState state, string observerId, string subjectId)
        {
            const int MemoryWindowMonths = 120;
            int acts = 0;

            foreach (var entry in state.chronicle)
            {
                if (entry.category != ChronicleCategory.Intelligence) continue;
                if (entry.countryId != subjectId) continue;
                if (entry.publicity != Publicity.Public) continue;
                if (state.date.MonthsSince(entry.date) > MemoryWindowMonths) continue;
                if (entry.text.IndexOf("compromised", StringComparison.OrdinalIgnoreCase) < 0) continue;
                acts++;
            }

            return Math.Min(100f, acts * 12f);
        }

        /// <summary>Treaties and coalitions are matters of public record.</summary>
        static float ObservedDiplomacy(GameState state, string subjectId)
        {
            int acts = 0;
            foreach (var treaty in state.treaties)
                if (treaty.Involves(subjectId) && !treaty.broken) acts++;
            foreach (var coalition in state.coalitions)
                if (coalition.leaderId == subjectId) acts += 2;
            return Math.Min(100f, acts * 14f);
        }

        static float Ease(float current, float target)
            => current + (target - current) * Adaptation;
    }
}

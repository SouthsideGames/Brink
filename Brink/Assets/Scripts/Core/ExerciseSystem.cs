using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Friendly war games and joint exercises (GDD Phase 12 follow-up, §15.3).
    ///
    /// Exercises build readiness, interoperability and trust — and they always
    /// teach, even when we come off worse, because a weakness found on exercise
    /// is a weakness not found in a war. The bargain is exposure: the deeper the
    /// participation, the more the partner learns about how we actually fight.
    /// That knowledge is retained if the relationship later sours.
    /// </summary>
    public static class ExerciseSystem
    {
        public static int CostFor(ExerciseScale scale)
        {
            switch (scale)
            {
                case ExerciseScale.Full: return 3;
                case ExerciseScale.Standard: return 2;
                default: return 1;
            }
        }

        public static float TreasuryCostFor(ExerciseScale scale)
        {
            switch (scale)
            {
                case ExerciseScale.Full: return 120f;
                case ExerciseScale.Standard: return 60f;
                default: return 20f;
            }
        }

        /// <summary>
        /// Months before the same partner will exercise with us again. Major
        /// exercises are planned a season at a time, not run back to back.
        /// </summary>
        public const int CooldownMonths = 9;

        /// <summary>Months remaining before this partner will exercise again, 0 if ready.</summary>
        public static int CooldownRemaining(GameState state, string partnerId)
        {
            int mostRecent = int.MinValue;
            for (int i = 0; i < state.exercises.Count; i++)
            {
                var record = state.exercises[i];
                if (record.partnerId != partnerId) continue;
                int monthsAgo = state.date.MonthsSince(record.date);
                if (-monthsAgo > mostRecent) mostRecent = -monthsAgo;
            }
            if (mostRecent == int.MinValue) return 0;

            int elapsed = -mostRecent;
            int remaining = CooldownMonths - elapsed;
            return remaining > 0 ? remaining : 0;
        }

        /// <summary>Exercises require a partner who is at least cooperative.</summary>
        public static bool CanExerciseWith(GameState state, string partnerId, out string reason)
        {
            if (partnerId == state.playerCountryId) { reason = "Cannot exercise with ourselves."; return false; }

            int cooldown = CooldownRemaining(state, partnerId);
            if (cooldown > 0)
            {
                reason = $"Next exercise cycle in {cooldown} months.";
                return false;
            }

            var partner = state.FindCountry(partnerId);
            if (partner == null) { reason = "No such state."; return false; }

            var confrontation = state.ActiveConfrontationFor(state.playerCountryId);
            if (confrontation != null && confrontation.Involves(partnerId))
            {
                reason = "We are in confrontation with them.";
                return false;
            }

            var status = DiplomacySystem.StatusOf(state, state.playerCountryId, partnerId);
            if (status < RelationshipStatus.Cooperative)
            {
                reason = $"Relationship is only {status}; they will not exercise with us.";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>
        /// Conduct a joint exercise. Returns the after-action record, or null if
        /// the exercise could not be run.
        /// </summary>
        public static ExerciseRecord Conduct(GameState state, TurnManager turns,
            string partnerId, ExerciseScale scale, ExerciseFocus focus)
        {
            if (!CanExerciseWith(state, partnerId, out string reason))
            {
                GameLog.Warn("EXERCISE", reason);
                return null;
            }

            int cost = CostFor(scale);
            var partner = state.FindCountry(partnerId);
            if (!turns.SpendCommandPoints(cost, $"{scale} exercise with {partner.displayName}"))
                return null;

            var player = state.PlayerCountry;
            var relationship = state.FindRelationship(state.playerCountryId, partnerId);

            int monthIndex = state.date.MonthsSince(state.startDate);
            var rng = new Random(unchecked(state.rngSeed * 5099 + monthIndex * 331 + Hash.Of(partnerId) + (int)focus));

            player.resources.treasury -= TreasuryCostFor(scale);

            float depth = scale == ExerciseScale.Full ? 1f : scale == ExerciseScale.Standard ? 0.6f : 0.3f;

            // How we showed against them, judged on the branch being exercised.
            float ourShowing = BranchPower(player, focus) * (0.85f + (float)rng.NextDouble() * 0.3f);
            float theirShowing = BranchPower(partner, focus) * (0.85f + (float)rng.NextDouble() * 0.3f);
            bool weOutperformed = ourShowing >= theirShowing;

            // Training value. Coming off worse teaches more, not less.
            float readinessGain = (weOutperformed ? 3.5f : 5f) * depth;
            ApplyReadiness(player, focus, readinessGain);
            ApplyReadiness(partner, focus, readinessGain * 0.7f);

            // **The peacetime route to veterancy, and the reason war games are
            // now worth their exposure cost.** Experience otherwise settles at
            // whatever a force's own training sustains; an exercise is how a
            // country that is not fighting pushes past that ceiling. Both sides
            // gain — a joint exercise is not something done *to* a partner —
            // and coming off worse teaches more here too, on the same principle
            // as the readiness gain above.
            ApplyExperience(player, focus, (weOutperformed ? 2.4f : 3.2f) * depth);
            ApplyExperience(partner, focus, (weOutperformed ? 3.2f : 2.4f) * depth);

            float interoperabilityGain = 7f * depth;
            relationship.interoperability = Clamp(relationship.interoperability + interoperabilityGain);

            // Both sides learn how the other operates — this is the real cost.
            float exposure = 9f * depth;
            relationship.doctrineFamiliarity = Clamp(relationship.doctrineFamiliarity + exposure);

            // The partner's collection against us improves with what they saw.
            var theirNetwork = state.FindNetwork(partnerId, state.playerCountryId);
            if (theirNetwork == null)
            {
                state.networks.Add(new IntelNetwork
                {
                    ownerId = partnerId,
                    targetId = state.playerCountryId,
                    focus = IntelDomain.Military,
                    penetration = exposure
                });
            }
            else
            {
                theirNetwork.penetration = Clamp(theirNetwork.penetration + exposure * 0.8f);
            }

            relationship.relations = Clamp(relationship.relations + 4f * depth);
            relationship.trust = Clamp(relationship.trust + 3f * depth);
            relationship.strategicAlignment = Clamp(relationship.strategicAlignment + 2f * depth);
            relationship.AddMemory(state.date, $"Joint exercise ({scale})", 1.5f * depth);

            string lesson = BuildLesson(player, focus, weOutperformed);

            // A poor showing highlights a specific weakness worth correcting.
            if (!weOutperformed)
            {
                var weakest = WeakestBranch(player);
                weakest.supply = Clamp(weakest.supply + 4f);
            }

            var record = new ExerciseRecord
            {
                date = state.date,
                partnerId = partnerId,
                scale = scale,
                focus = focus,
                weOutperformed = weOutperformed,
                readinessGained = readinessGain,
                interoperabilityGained = interoperabilityGain,
                exposureIncurred = exposure,
                lesson = lesson
            };
            state.exercises.Add(record);

            state.AddNotification(NotificationClass.Advisory,
                $"JOINT EXERCISE — {partner.displayName.ToUpperInvariant()}",
                lesson, partnerId, desk: ReportingDesk.Military);
            state.AddChronicle(ChronicleCategory.Military, state.playerCountryId,
                $"{Phrase.Of(scale)} joint exercise with {partner.displayName} ({Phrase.Of(focus)}).", Publicity.Public);
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 14, "Joint exercise conducted");

            return record;
        }

        static string BuildLesson(CountryState player, ExerciseFocus focus, bool weOutperformed)
        {
            string branch = focus.ToString().ToLowerInvariant();
            if (weOutperformed)
                return $"Our {branch} forces set the pace. Interoperability improved; " +
                       "our procedures are now better understood by the partner.";

            var weakest = WeakestBranch(player);
            return $"Our {branch} performance lagged the partner. Sustainment shortfalls in the " +
                   $"{weakest.branch.ToString().ToLowerInvariant()} arm were identified and are being corrected.";
        }

        static float BranchPower(CountryState country, ExerciseFocus focus)
        {
            var mil = country.military;
            switch (focus)
            {
                case ExerciseFocus.Ground: return mil.ground.EffectivePower;
                case ExerciseFocus.Air: return mil.air.EffectivePower;
                case ExerciseFocus.Naval: return mil.naval.EffectivePower;
                default: return mil.TotalPower / 3f;
            }
        }

        static void ApplyReadiness(CountryState country, ExerciseFocus focus, float amount)
        {
            var mil = country.military;
            switch (focus)
            {
                case ExerciseFocus.Ground: mil.ground.readiness = Clamp(mil.ground.readiness + amount); break;
                case ExerciseFocus.Air: mil.air.readiness = Clamp(mil.air.readiness + amount); break;
                case ExerciseFocus.Naval: mil.naval.readiness = Clamp(mil.naval.readiness + amount); break;
                default:
                    mil.ground.readiness = Clamp(mil.ground.readiness + amount * 0.5f);
                    mil.air.readiness = Clamp(mil.air.readiness + amount * 0.5f);
                    mil.naval.readiness = Clamp(mil.naval.readiness + amount * 0.5f);
                    break;
            }
        }

        /// <summary>
        /// Training value, on the same branch mapping as <see cref="ApplyReadiness"/>
        /// — including Joint, which spreads across all three. Gain shrinks as a
        /// force approaches 100: there is progressively less an exercise can teach
        /// an army that has already been everywhere.
        /// </summary>
        static void ApplyExperience(CountryState country, ExerciseFocus focus, float amount)
        {
            var mil = country.military;

            void Train(BranchForce force, float share)
            {
                float room = (100f - force.experience) / 100f;
                force.experience = Clamp(force.experience + amount * share * room);
            }

            switch (focus)
            {
                case ExerciseFocus.Ground: Train(mil.ground, 1f); break;
                case ExerciseFocus.Air: Train(mil.air, 1f); break;
                case ExerciseFocus.Naval: Train(mil.naval, 1f); break;
                default:
                    Train(mil.ground, 0.5f);
                    Train(mil.air, 0.5f);
                    Train(mil.naval, 0.5f);
                    break;
            }
        }

        static BranchForce WeakestBranch(CountryState country)
        {
            var mil = country.military;
            var weakest = mil.ground;
            if (mil.air.EffectivePower < weakest.EffectivePower) weakest = mil.air;
            if (mil.naval.EffectivePower < weakest.EffectivePower) weakest = mil.naval;
            return weakest;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

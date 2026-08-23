using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Crisis Turns (GDD §6, §23). A crisis interrupts the monthly loop and uses
    /// its own decision structure — never normal Command Points (GDD §7.1).
    ///
    /// It does **not** block the turn. The operator may end the month without
    /// answering; see LapseUnanswered for what that costs.
    ///
    /// Selection is hybrid: <see cref="EventCatalog"/> supplies authored
    /// situations, each declaring the simulation conditions that make it possible
    /// and how likely it is under them. Events therefore arise from the world
    /// state rather than being dealt at random.
    /// </summary>
    public static class CrisisSystem
    {
        /// <summary>Chance per resolved month that a crisis fires (when none is active).</summary>
        public const double MonthlyCrisisChance = 0.08;

        /// <summary>Every authored crisis id.</summary>
        public static string[] CatalogIds
        {
            get
            {
                var ids = new string[EventCatalog.Definitions.Count];
                for (int i = 0; i < ids.Length; i++) ids[i] = EventCatalog.Definitions[i].id;
                return ids;
            }
        }

        /// <summary>Build a crisis from its definition. Situation text may name real countries.</summary>
        public static ActiveCrisis Create(GameState state, string defId)
        {
            var definition = EventCatalog.Find(defId);
            if (definition == null)
                throw new ArgumentException($"Unknown crisis definition '{defId}'.");

            // Everything is materialised *now*, at fire time, and nothing is
            // re-derived when the crisis resolves. A crisis that opens by naming
            // our coldest rival must resolve against that state even if the
            // rankings shift while the operator is thinking about it.
            string subject = definition.subject?.Invoke(state) ?? "";

            return new ActiveCrisis
            {
                defId = definition.id,
                title = definition.title,
                body = definition.body(state),
                startDate = state.date,
                subjectCountryId = subject,
                options = definition.options(state),
                lapseEffectId = definition.lapseEffectId ?? "",
                lapseTargetId = definition.lapseTargetUsesSubject ? subject : "",
                lapseMagnitude = definition.lapseMagnitude
            };
        }

        /// <summary>Trigger a crisis: FLASH traffic and a chronicle entry.</summary>
        public static ActiveCrisis Trigger(GameState state, string defId)
        {
            var crisis = Create(state, defId);
            state.activeCrises.Add(crisis);
            state.crisesFacedThisYear++;
            Telemetry.Record(state, TelemetryKind.Crisis, state.playerCountryId, "FIRED", crisis.defId);
            RecordFired(state, defId);

            state.AddNotification(NotificationClass.Flash, crisis.title,
                "A decision is required. The month can still end without one — at a cost.",
                state.playerCountryId);
            state.AddChronicle(ChronicleCategory.Political, state.playerCountryId,
                $"CRISIS: {crisis.title}.");
            GameLog.Warn("CRISIS", $"{crisis.title} — Crisis Turn interrupt. Decision required.");
            return crisis;
        }

        /// <summary>Apply the chosen option to the player nation and close the crisis.</summary>
        /// <summary>
        /// A crisis the operator did not answer before the month ended.
        ///
        /// The turn used to simply refuse to advance until a crisis was
        /// answered. That is the one thing this game should not do: the operator
        /// is running a government, and a government that fails to decide is a
        /// real and interesting outcome. Blocking replaced a consequence with a
        /// wall.
        ///
        /// So inaction is now permitted and *costs*. Not deciding is worse than
        /// any option on the table — every option represents someone taking
        /// responsibility, and this is nobody doing so — but it is never fatal,
        /// because a mistake the player chose should hurt, not end the save.
        /// </summary>
        public static void LapseUnanswered(GameState state)
        {
            for (int i = state.activeCrises.Count - 1; i >= 0; i--)
            {
                var crisis = state.activeCrises[i];
                var player = state.PlayerCountry;

                if (player != null)
                {
                    // Roughly a third worse than the weakest option available,
                    // and applied to standing rather than the treasury: what
                    // drifting costs a government is authority.
                    float resilience = 1f - TechnologySystem.Effectiveness(player, "CAP_CONTINUITY") * 0.35f;
                    player.stability = Clamp(player.stability - 4f * resilience);
                    player.governmentApproval = Clamp(player.governmentApproval - 5f * resilience);
                    player.nationalUnity = Clamp(player.nationalUnity - 2f * resilience);
                }

                // An alliance call left unanswered is a repudiation. Saying
                // nothing to a partner who asked for help *is* an answer.
                if (crisis.defId == AllianceSystem.PlayerObligationCrisisId)
                    AllianceSystem.ApplyPlayerDecision(state, honored: false);

                // If the operator will not decide, the situation decides for
                // them (GDD §23). Drifting used to cost standing and nothing
                // else, which made ignoring a crisis the cheapest way to avoid
                // its consequences — precisely backwards.
                string worldEffect = CrisisEffects.Apply(
                    state, crisis.lapseEffectId, crisis.lapseTargetId, crisis.lapseMagnitude);

                state.activeCrises.RemoveAt(i);
                Telemetry.Record(state, TelemetryKind.Crisis, state.playerCountryId, "LAPSED",
                    crisis.defId, success: false);

                string drift = $"{crisis.title} passed without a decision from this office. " +
                               "The government is judged to have drifted.";
                if (!string.IsNullOrEmpty(worldEffect)) drift += $" {worldEffect}";

                state.AddNotification(NotificationClass.Priority, "CRISIS WENT UNANSWERED",
                    drift, state.playerCountryId);
                state.AddChronicle(ChronicleCategory.Political, state.playerCountryId,
                    $"{crisis.title}: no decision taken." +
                    (string.IsNullOrEmpty(worldEffect) ? "" : $" {worldEffect}"));
                GameLog.Warn("CRISIS", $"Unanswered crisis lapsed: {crisis.title}.");
            }
        }

        public static void Resolve(GameState state, ActiveCrisis crisis, int optionIndex)
        {
            if (crisis == null || !state.activeCrises.Contains(crisis))
                throw new ArgumentException("Crisis is not active.");
            if (optionIndex < 0 || optionIndex >= crisis.options.Count)
                throw new ArgumentOutOfRangeException(nameof(optionIndex));

            var option = crisis.options[optionIndex];
            var player = state.PlayerCountry;

            // Alliance obligations carry consequences far beyond their deltas.
            if (crisis.defId == AllianceSystem.PlayerObligationCrisisId)
                AllianceSystem.ApplyPlayerDecision(state, honored: optionIndex == 0);

            if (player != null)
            {
                // Contingency planning softens what a crisis costs us (GDD §11).
                float resilience = 1f - TechnologySystem.Effectiveness(player, "CAP_CONTINUITY") * 0.35f;
                float Soften(float delta) => delta < 0f ? delta * resilience : delta;

                player.resources.treasury += Soften(option.treasuryDelta);
                player.stability = Clamp(player.stability + Soften(option.stabilityDelta));
                player.governmentApproval = Clamp(player.governmentApproval + Soften(option.approvalDelta));
                player.nationalUnity = Clamp(player.nationalUnity + Soften(option.unityDelta));
            }

            // What the decision does beyond our own borders (GDD §23). This is
            // the half that did not exist: a crisis could not touch a
            // relationship, a market, a foreign state or a war, so the most
            // dramatic moments in the game left nothing behind them.
            //
            // Deliberately *not* softened by CAP_CONTINUITY — contingency
            // planning cushions what a shock costs us at home; it does not make
            // another government think better of us or call off a war.
            string worldEffect = CrisisEffects.Apply(
                state, option.effectId, option.effectTargetId, option.effectMagnitude);

            state.activeCrises.Remove(crisis);
            state.crisesResolvedThisYear++;
            Telemetry.Record(state, TelemetryKind.Crisis, state.playerCountryId, "RESOLVED",
                $"{crisis.defId} → {option.label}");
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 20, "Crisis resolved");

            string report = string.IsNullOrEmpty(worldEffect)
                ? option.resultText
                : $"{option.resultText} {worldEffect}";

            state.AddNotification(NotificationClass.Priority, $"{crisis.title} — RESOLVED",
                report, state.playerCountryId);
            state.AddChronicle(ChronicleCategory.Political, state.playerCountryId,
                $"{crisis.title}: {report}");
            GameLog.Info("CRISIS", $"{crisis.title} resolved: {option.label}.");
        }

        /// <summary>
        /// Monthly systemic check. Filters the catalog by eligibility and
        /// cooldown, then draws by weight. Deterministic for a given save.
        /// </summary>
        public static void SystemicCheck(GameState state)
        {
            if (state.HasOpenCrisis) return;

            int monthIndex = state.date.MonthsSince(state.startDate);
            var rng = new Random(unchecked(state.rngSeed * 486187739 + monthIndex));
            if (rng.NextDouble() >= MonthlyCrisisChance) return;

            var eligible = new List<EventDefinition>();
            var weights = new List<float>();
            float total = 0f;

            foreach (var definition in EventCatalog.Definitions)
            {
                if (OnCooldown(state, definition, monthIndex)) continue;

                bool passes;
                try { passes = definition.isEligible == null || definition.isEligible(state); }
                catch (Exception e)
                {
                    GameLog.Error("CRISIS", $"Eligibility check for {definition.id} threw: {e.Message}");
                    continue;
                }
                if (!passes) continue;

                float weight = definition.weight != null ? Math.Max(0.05f, definition.weight(state)) : 1f;
                eligible.Add(definition);
                weights.Add(weight);
                total += weight;
            }

            // Nothing in the world currently justifies a crisis. That is a valid
            // outcome — a stable, well-supplied state genuinely has quieter months.
            if (eligible.Count == 0) return;

            double roll = rng.NextDouble() * total;
            for (int i = 0; i < eligible.Count; i++)
            {
                roll -= weights[i];
                if (roll > 0) continue;
                Trigger(state, eligible[i].id);
                return;
            }
            Trigger(state, eligible[eligible.Count - 1].id);
        }

        // ---------- cooldowns ----------

        static bool OnCooldown(GameState state, EventDefinition definition, int monthIndex)
        {
            for (int i = 0; i < state.eventCooldowns.Count; i++)
            {
                var record = state.eventCooldowns[i];
                if (record.defId != definition.id) continue;
                return monthIndex - record.lastFiredMonth < definition.cooldownMonths;
            }
            return false;
        }

        static void RecordFired(GameState state, string defId)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);
            for (int i = 0; i < state.eventCooldowns.Count; i++)
            {
                if (state.eventCooldowns[i].defId != defId) continue;
                state.eventCooldowns[i].lastFiredMonth = monthIndex;
                return;
            }
            state.eventCooldowns.Add(new EventCooldown { defId = defId, lastFiredMonth = monthIndex });
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

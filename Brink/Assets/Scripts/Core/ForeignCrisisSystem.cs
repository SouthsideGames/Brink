using System;
using System.Linq;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Other countries have problems too (GDD §23 amendment).
    ///
    /// `CrisisSystem.SystemicCheck` read `state.PlayerCountry` throughout, so in a
    /// world of sixteen states exactly one ever suffered a food shortage, a
    /// general strike, an energy crisis or a contested succession. Everyone else
    /// was permanently, invisibly fine. That made the world scenery between the
    /// operator's own crises: there was no internal trouble anywhere to detect,
    /// nothing for collection to be *for* beyond counting tanks, and no moment
    /// when a rival was weak for a reason you could have seen coming.
    ///
    /// **This is deliberately not an AI Crisis Turn**, which is settled and stays
    /// settled. A Crisis Turn exists to interrupt the operator's month and force a
    /// decision at the terminal; a foreign government simply decides, in the same
    /// tick, and lives with it. What is being added is the *situation* and its
    /// consequence — simulation the player can actually observe — not an interface
    /// for a country nobody is playing.
    ///
    /// Three rules keep it honest:
    ///
    /// 1. **The same conditions as ours.** A situation fires against a foreign
    ///    state through `EventDefinition.befalls`, which the player's own
    ///    eligibility also delegates to. One threshold, not two.
    /// 2. **How well they handle it depends on the government.** A capable,
    ///    stable state absorbs most of it; a failing one takes it full in the
    ///    face. That is what makes a rival's weakness legible *before* it becomes
    ///    a coup.
    /// 3. **You only learn about it through the record.** It goes to the
    ///    chronicle, and it moves statistics your estimates are built from. A
    ///    state you are not collecting against has quiet trouble you never hear
    ///    about, which is the argument for collection stated as a mechanic
    ///    rather than as a tutorial line.
    /// </summary>
    public static class ForeignCrisisSystem
    {
        /// <summary>
        /// Chance per foreign state per month that an eligible situation fires.
        ///
        /// Deliberately well below the player's rate. Sixteen states rolling
        /// independently would otherwise fill the chronicle with somebody else's
        /// bad news every single month, and the record has to stay readable — a
        /// world where everything is always happening is as flat as one where
        /// nothing is.
        /// </summary>
        public const double MonthlyChancePerState = 0.035;

        /// <summary>Months before the same situation can recur in the same country.</summary>
        public const int CooldownMonths = 30;

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            // Snapshot: a situation can end in secession, which appends to the
            // roster. Same reason RegimeSystem takes one.
            var roster = state.countries.ToArray();

            foreach (var country in roster)
            {
                if (country == null || country.isPlayer) continue;

                var rng = new Random(unchecked(
                    state.rngSeed * 1103515245 + monthIndex * 7919 + Hash.Of(country.id)));

                if (rng.NextDouble() >= MonthlyChancePerState) continue;

                var definition = PickFor(state, country, monthIndex, rng);
                if (definition == null) continue;

                Resolve(state, country, definition, monthIndex, rng);
            }
        }

        /// <summary>
        /// How hard a situation lands on this country, 0.35 (absorbed) to ~1.25
        /// (taken full in the face).
        ///
        /// A competent government with institutions behind it loses very little; a
        /// hollowed-out one loses most of it. **This is the whole reason a rival's
        /// condition is worth watching** — trouble compounds where the state is
        /// already weak, so a government you have been undermining for years is
        /// one bad harvest from something you can use.
        ///
        /// Public and pure so it can be tested directly. The first attempt tested
        /// it by running a decade and summing stability drops, which measured the
        /// *restoring force* rather than the damage: a capable state pinned high
        /// falls toward its natural level every month and scored 193 points of
        /// phantom "crisis damage", while a weak state pinned low rose and scored
        /// almost none. Exactly inverted.
        /// </summary>
        public static float SeverityFor(CountryState country)
        {
            if (country == null) return 1f;

            float capacity = Clamp01(
                (country.pillars.government * 0.55f
                 + country.stability * 0.30f
                 + country.government.leader.competence * 0.15f) / 100f);

            return 0.35f + (1f - capacity) * 0.9f;
        }

        /// <summary>Weighted pick among the situations this country currently qualifies for.</summary>
        static EventDefinition PickFor(GameState state, CountryState country,
            int monthIndex, Random rng)
        {
            var eligible = new List<EventDefinition>();

            foreach (var definition in EventCatalog.Definitions)
            {
                // Only situations written to befall anybody. The rest describe the
                // operator's position and have no meaning here.
                if (definition.befalls == null) continue;
                if (definition.nature != EventNature.Adversity) continue;

                // A chained event reads the player's outcome record — a foreign
                // government's situations resolve in the same tick and leave no
                // lapse to chain from (spec 11 §7).
                if (!string.IsNullOrEmpty(definition.followsFrom)) continue;
                if (OnCooldown(state, country.id, definition.id, monthIndex)) continue;

                bool passes;
                try { passes = definition.befalls(state, country); }
                catch (Exception e)
                {
                    GameLog.Error("CRISIS",
                        $"{definition.id}.befalls threw for {country.id}: {e.Message}");
                    continue;
                }

                if (passes) eligible.Add(definition);
            }

            if (eligible.Count == 0) return null;
            return eligible[rng.Next(eligible.Count)];
        }

        /// <summary>
        /// The foreign government deals with it, well or badly, and the world
        /// records what happened.
        /// </summary>
        static void Resolve(GameState state, CountryState country, EventDefinition definition,
            int monthIndex, Random rng)
        {
            NoteCooldown(state, country.id, definition.id, monthIndex);

            float severity = SeverityFor(country);

            // The same effect the operator would suffer for letting one lapse,
            // scaled by how well they coped. Reusing `CrisisEffects` keeps a
            // foreign country's misfortune made of the same material as ours.
            if (!string.IsNullOrEmpty(definition.lapseEffectId))
                CrisisEffects.Apply(state, definition.lapseEffectId, country.id,
                    definition.lapseMagnitude * severity);

            // Standing damage, because a government that has just been through
            // something is weaker for it whatever the specific situation was.
            country.stability = Clamp(country.stability - 3.5f * severity);
            country.governmentApproval = Clamp(country.governmentApproval - 4f * severity);

            // **The record is how the player finds out.** Public, because these
            // are strikes, shortages and successions — the sort of thing that
            // cannot be hidden. What collection buys is not knowing *that* it
            // happened but reading what it did to them.
            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"{country.displayName}: {definition.title.ToLowerInvariant()}.", Publicity.Public);

            // Traffic only when it is severe enough to matter to us, and only as
            // a WIRE item: somebody else's bad month is news, never a decision.
            if (severity > 0.9f)
                state.AddNotification(NotificationClass.Wire,
                    $"{country.displayName.ToUpperInvariant()} — {definition.title}",
                    $"Reporting indicates {country.displayName} is struggling with this. "
                    + "A government under this kind of pressure is a government with less "
                    + "attention to spare.",
                    country.id, desk: ReportingDesk.Intelligence);

            GameLog.Info("CRISIS", $"{country.id} faced {definition.id} (severity {severity:F2}).");
        }

        // ---------- per-country cooldowns ----------

        /// <summary>
        /// Cooldowns are keyed by country as well as definition, so a shortage in
        /// one state does not make every other state immune to shortages. The
        /// player's own cooldown list is untouched and keeps its bare ids.
        /// </summary>
        static string KeyFor(string countryId, string defId) => countryId + "/" + defId;

        static bool OnCooldown(GameState state, string countryId, string defId, int monthIndex)
        {
            string key = KeyFor(countryId, defId);
            foreach (var cooldown in state.eventCooldowns)
                if (cooldown.defId == key)
                    return monthIndex - cooldown.lastFiredMonth < CooldownMonths;
            return false;
        }

        static void NoteCooldown(GameState state, string countryId, string defId, int monthIndex)
        {
            string key = KeyFor(countryId, defId);
            foreach (var cooldown in state.eventCooldowns)
                if (cooldown.defId == key) { cooldown.lastFiredMonth = monthIndex; return; }

            state.eventCooldowns.Add(new EventCooldown { defId = key, lastFiredMonth = monthIndex });
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

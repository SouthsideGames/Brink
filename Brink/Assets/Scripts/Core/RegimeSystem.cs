using System;
using System.Linq;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Coups, rebellion and regime change (GDD §22).
    ///
    /// Three rules shape everything here:
    ///
    /// 1. **Coups emerge from accumulated conditions**, never from a card draw or
    ///    a button. Conspiracy builds over months from genuine grievance —
    ///    instability, economic misery, a disloyal officer corps, a fractured
    ///    elite — and decays when a government governs well.
    /// 2. **Foreign action accelerates, it does not manufacture.** Covert support
    ///    is multiplied by the target's existing vulnerability, so a stable,
    ///    legitimate state cannot be toppled by clicking at it.
    /// 3. **Catastrophe creates a new gameplay state, not a game over.** A
    ///    successful coup produces a successor government that keeps playing —
    ///    and the operator keeps their post.
    /// </summary>
    public static class RegimeSystem
    {
        /// <summary>Conspiracy level below which no coup is possible.</summary>
        public const float CoupThreshold = 60f;

        /// <summary>Political Capital to buy the loyalty of the officer corps.</summary>
        public const float SecureLoyaltyCost = 5f;

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            // **Iterate this month's roster, not the live list.** Three calls down
            // — UpdateCivilConflict -> SecessionSystem.Fracture -> MakeSuccessor —
            // a breakaway state is appended to `state.countries`, and the
            // enumerator here then throws on its next step. Latent since
            // SecessionSystem landed, because reaching it needs a successful coup
            // followed by an eight-month collapse that neither national unity nor
            // military loyalty recovers from; a thirty-year stochastic harness was
            // the first thing to walk that path.
            //
            // A snapshot is also the correct *semantics*, not merely a way to stop
            // the exception: a state that secedes this month should not then be
            // processed for its own coups and conspiracies in the same tick.
            //
            // `state.countries` is now genuinely mutable at runtime — secession is
            // the only thing in the game that creates a country — so any monthly
            // loop over it that can reach Fracture needs this treatment.
            var roster = state.countries.ToArray();

            foreach (var country in roster)
            {
                var rng = new Random(unchecked(
                    state.rngSeed * 179424673 + monthIndex * 5011 + Hash.Of(country.id)));

                UpdateCivilConflict(state, country);
                UpdateMilitaryLoyalty(country);
                UpdateConspiracy(state, country);
                CheckCoupAttempt(state, country, rng);
            }
        }

        // ---------- loyalty ----------

        static void UpdateMilitaryLoyalty(CountryState country)
        {
            var gov = country.government;

            // Officers follow a state that pays them, functions, and is not
            // visibly failing. Emergency rule and defeat corrode that.
            float target = 45f
                           + country.pillars.government * 0.30f
                           + country.stability * 0.20f
                           - country.warExhaustion * 0.25f
                           - Math.Max(0f, country.economy.inflation - 8f) * 1.2f
                           - (gov.emergencyPowers ? 8f : 0f)
                           - (gov.inCivilConflict ? 15f : 0f);

            gov.militaryLoyalty = Approach(gov.militaryLoyalty, Clamp(target), 0.06f);
        }

        // ---------- conspiracy ----------

        /// <summary>Conspiracy that dissipates every month regardless of level.</summary>
        public const float ConspiracyBaseDecay = 0.25f;

        /// <summary>Share of the standing conspiracy that dissipates each month.</summary>
        public const float ConspiracyProportionalDecay = 0.010f;

        static void UpdateConspiracy(GameState state, CountryState country)
        {
            var gov = country.government;
            float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;

            // Grievance: every term is a real failure of governance.
            float pressure = Math.Max(0f, 50f - country.stability) * 0.020f
                             + Math.Max(0f, 45f - country.governmentApproval) * 0.015f
                             + Math.Max(0f, 50f - backing) * 0.020f
                             + Math.Max(0f, country.economy.inflation - 8f) * 0.030f
                             + Math.Max(0f, country.economy.unemployment - 12f) * 0.020f
                             + country.warExhaustion * 0.010f
                             + Math.Max(0f, 55f - gov.militaryLoyalty) * 0.025f
                             + Math.Max(0f, 45f - country.nationalUnity) * 0.010f;

            // Foreign backing is a multiplier on existing vulnerability, never a
            // substitute for it (GDD §22).
            float vulnerability = Clamp01(pressure / 1.2f);
            float foreign = ForeignSubversionAgainst(state, country.id) * vulnerability;
            if (foreign > 0f && string.IsNullOrEmpty(gov.conspiracyBackerId))
                gov.conspiracyBackerId = StrongestSubverter(state, country.id);

            // A government that is governing well starves conspiracy of oxygen —
            // and every level of conspiracy has a resting point. The decay used
            // to exist *only* below a pressure of 0.25; above it conspiracy was a
            // pure accumulator with two sources (this, and unrest above 55) and
            // no sink short of a coup, which is how measured worlds reached ~100
            // coups in forty years and every second one refilled from 25 in
            // under three years. Proportional decay is the shape the codebase
            // already settled on for grievance, corruption and food: a plot at
            // steady pressure now settles where its pressure holds it, which is
            // above the coup threshold for a truly failing state and below it
            // for a merely troubled one.
            float recovery = ConspiracyBaseDecay + gov.conspiracyLevel * ConspiracyProportionalDecay;
            if (pressure < 0.25f)
                recovery += 1.2f + country.stability * 0.02f + gov.militaryLoyalty * 0.015f;

            // How the state holds its society decides how easily a plot can
            // organise at all (GDD §12). This is the whole case for governing
            // restrictively — and, since a restrictive posture costs legitimacy
            // and unity every month, the whole case against it.
            float postureRate = GovernmentSystem.ConspiracyRateFor(gov);

            gov.conspiracyLevel = Clamp(
                gov.conspiracyLevel + (pressure + foreign) * postureRate - recovery);

            if (gov.conspiracyLevel <= 5f) gov.conspiracyBackerId = "";

            WarnIfDetected(state, country);
        }

        /// <summary>Monthly conspiracy contribution from foreign covert political action.</summary>
        static float ForeignSubversionAgainst(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var network in state.networks)
            {
                if (network.targetId != countryId || network.compromised) continue;
                if (network.focus != IntelDomain.Political) continue;
                total += network.penetration * 0.004f;
            }
            return total;
        }

        static string StrongestSubverter(GameState state, string countryId)
        {
            string best = "";
            float deepest = 0f;
            foreach (var network in state.networks)
            {
                if (network.targetId != countryId || network.compromised) continue;
                if (network.focus != IntelDomain.Political) continue;
                if (network.penetration <= deepest) continue;
                deepest = network.penetration;
                best = network.ownerId;
            }
            return best;
        }

        /// <summary>
        /// Warn the player about danger they could plausibly know about: their own
        /// services report on domestic plots, and collection reports on foreign ones.
        /// </summary>
        static void WarnIfDetected(GameState state, CountryState country)
        {
            var gov = country.government;
            if (gov.conspiracyLevel < 45f) return;

            if (country.isPlayer)
            {
                // Detection depends on our own counterintelligence.
                if (country.counterIntel.counterIntelligence < 35f) return;
                if (gov.conspiracyLevel < 55f) return;
                state.AddNotification(NotificationClass.Flash, "CONSPIRACY DETECTED",
                    "Counterintelligence reports organized disaffection within the officer corps. " +
                    "The loyalty of the command is no longer assured.", country.id,
                    desk: ReportingDesk.Government);
                return;
            }

            // Foreign plots require political collection to see.
            var estimate = IntelligenceSystem.GetEstimate(state, state.playerCountryId, country.id, IntelDomain.Political);
            if (estimate == null || estimate.confidence < ConfidenceGrade.Moderate) return;
            // Intelligence, not Government: this item exists only because the
            // collection above produced it, so it is the intelligence service
            // that either passes it up or does not. A desk is assigned by who
            // actually files the item, not by which file the code lives in.
            state.AddNotification(NotificationClass.Advisory, "INSTABILITY ASSESSED",
                $"Reporting indicates serious internal fracture in {country.displayName}.", country.id,
                desk: ReportingDesk.Intelligence);
        }

        // ---------- the attempt ----------

        static void CheckCoupAttempt(GameState state, CountryState country, Random rng)
        {
            var gov = country.government;
            if (gov.conspiracyLevel < CoupThreshold) return;

            float chance = (gov.conspiracyLevel - CoupThreshold) / 320f
                           * (1f + Math.Max(0f, 60f - gov.militaryLoyalty) / 80f);
            if (rng.NextDouble() >= chance) return;

            // Plotters versus the state's ability to hold its own institutions.
            //
            // Loyalty is the counterweight, not a small modifier: a conspiracy
            // inside an army that will not move is a conversation, not a coup.
            // Adding only `(100 − loyalty) × 0.5` left a maximally loyal, stable,
            // well-governed state losing about two attempts in five, which
            // contradicts the rule that a stable state cannot simply be toppled
            // (GDD §22).
            float plotters = gov.conspiracyLevel * (1.3f - gov.militaryLoyalty / 100f);
            float regime = gov.militaryLoyalty * 1.0f + country.pillars.government * 0.6f
                           + country.stability * 0.4f
                           + (gov.emergencyPowers ? 10f : 0f);

            bool succeeds = rng.NextDouble() < plotters / Math.Max(1f, plotters + regime);

            if (succeeds) SucceedingCoup(state, country, rng);
            else FailedCoup(state, country);
        }

        static void FailedCoup(GameState state, CountryState country)
        {
            var gov = country.government;

            // The purge that follows: the survivors are loyal, and fewer.
            gov.militaryLoyalty = Clamp(gov.militaryLoyalty + 15f);
            gov.conspiracyLevel = 15f;
            gov.conspiracyBackerId = "";
            country.pillars.military = Clamp(country.pillars.military - 8f);
            country.stability = Clamp(country.stability - 10f);
            country.nationalUnity = Clamp(country.nationalUnity - 5f);
            country.pillars.intelligence = Clamp(country.pillars.intelligence - 3f);

            state.AddNotification(
                country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                country.isPlayer ? "COUP ATTEMPT DEFEATED" : "COUP ATTEMPT ABROAD",
                $"An attempt to seize power in {country.displayName} has failed. " +
                "A purge of the officer corps is under way.", country.id,
                desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, country.id,
                "Failed coup attempt. Purge of the officer corps follows.", Publicity.Public);
            GameLog.Warn("REGIME", $"Failed coup in {country.id}.");
        }

        static void SucceedingCoup(GameState state, CountryState country, Random rng)
        {
            var gov = country.government;
            string backerId = gov.conspiracyBackerId;
            var previousType = gov.type;

            gov.coupsExperienced++;

            // Power passes to those who took it. Institutions centralize.
            gov.type = GovernmentType.CentralizedRepublic;
            gov.consecutiveTermLimit = 0;
            gov.termLengthMonths = 0;
            gov.emergencyPowers = false;
            gov.emergencyPowersMonthsRemaining = 0;
            gov.conspiracyLevel = 25f;
            gov.conspiracyBackerId = "";
            gov.militaryLoyalty = Clamp(gov.militaryLoyalty + 20f);
            gov.eliteCohesion = 45f;
            gov.legislativeSupport = 0f;

            var profile = WorldFactory.FindProfile(country.id);
            var priorities = (NationalPriority[])Enum.GetValues(typeof(NationalPriority));
            gov.leader = new Leader
            {
                name = profile != null
                    ? $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                      $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}"
                    : "MILITARY COUNCIL",
                faction = "MILITARY COUNCIL",
                priority = priorities[rng.Next(priorities.Length)],
                competence = 40f + (float)rng.NextDouble() * 40f,
                age = 45f + (float)rng.NextDouble() * 20f,
                monthsInOffice = 0
            };

            // The state is badly shaken.
            country.stability = Clamp(country.stability - 25f);
            country.nationalUnity = Clamp(country.nationalUnity - 15f);
            country.governmentApproval = 45f;
            country.pillars.government = Clamp(country.pillars.government - 10f);
            country.economy.confidence = Clamp(country.economy.confidence - 20f);

            // Commitments made by a government that no longer exists.
            foreach (var treaty in state.treaties)
            {
                if (treaty.broken || !treaty.Involves(country.id)) continue;
                if (!treaty.HasActive(state, TreatyCommitment.MutualDefense)) continue;
                treaty.broken = true;
                treaty.brokenBy = country.id;
            }

            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(country.id)) continue;
                relationship.trust = Clamp(relationship.trust - 20f);
                relationship.AddMemory(state.date, "Government seized by force", -4f);
            }

            // A foreign-backed successor is sovereign, not a puppet (GDD §22).
            if (!string.IsNullOrEmpty(backerId) && backerId != country.id)
            {
                var toBacker = state.FindRelationship(country.id, backerId);
                if (toBacker != null)
                {
                    toBacker.relations = Clamp(toBacker.relations + 15f);
                    toBacker.AddMemory(state.date, "Backed the faction that took power", 2f);
                }

                var backer = state.FindCountry(backerId);
                state.AddNotification(
                    backerId == state.playerCountryId ? NotificationClass.Priority : NotificationClass.Wire,
                    "REGIME CHANGE — SPONSORED FACTION IN POWER",
                    $"The faction we supported now governs {country.displayName}. It is a sovereign " +
                    "government with its own interests, and owes us nothing further.", country.id,
                    desk: ReportingDesk.Government);
                state.AddChronicle(ChronicleCategory.Intelligence, backerId,
                    $"Sponsored faction took power in {country.displayName}.");
            }

            // Total collapse of order tips into open civil conflict.
            if (country.stability < 25f)
            {
                gov.inCivilConflict = true;
                gov.civilConflictMonthsRemaining = 12 + rng.Next(12);
                state.AddChronicle(ChronicleCategory.Political, country.id,
                    "State authority has fractured. Civil conflict has broken out.",
                    Publicity.Public);
            }

            // A junta installs its own people wherever it takes power. This is
            // the single most visible thing a coup does to a state's capability,
            // and while foreign governments had no cabinets it did not happen to
            // them at all.
            ReshuffleCabinetAfterCoup(state, country, rng);

            if (country.isPlayer)
            {
                state.administrationsServed++;
                state.AddNotification(NotificationClass.Flash, "GOVERNMENT SEIZED BY FORCE",
                    $"{gov.leader.name} and the {gov.leader.faction} have taken power. " +
                    "The constitutional order is suspended. You remain at your post.", country.id,
                    desk: ReportingDesk.Government);
            }
            else
            {
                state.AddNotification(NotificationClass.Priority, "COUP ABROAD",
                    $"{gov.leader.name} has seized power in {country.displayName}. " +
                    $"The {previousType} has been swept away.", country.id,
                    desk: ReportingDesk.Government);
            }

            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"COUP: {gov.leader.name} seizes power. Constitutional order suspended.",
                Publicity.Public);
            GameLog.Warn("REGIME", $"Successful coup in {country.id}.");
        }

        static void ReshuffleCabinetAfterCoup(GameState state, CountryState country, Random rng)
        {
            if (country == null) return;
            var profile = WorldFactory.FindProfile(country.id);
            if (profile == null) return;

            foreach (var official in country.cabinet)
            {
                official.displayName = $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                                       $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}";
                official.competence = 35f + (float)rng.NextDouble() * 40f;
                official.loyalty = 55f + (float)rng.NextDouble() * 40f; // the new order's own people
                official.riskTolerance = 30f + (float)rng.NextDouble() * 60f;
                official.trust = 45f + (float)rng.NextDouble() * 15f;
                official.monthsInOffice = 0;
                official.mode = ControlMode.Autonomous;
                official.directiveId = "";
            }
            state.AddChronicle(ChronicleCategory.Political, country.id,
                "Cabinet replaced wholesale by the new order.", Publicity.Public);
        }

        // ---------- civil conflict ----------

        static void UpdateCivilConflict(GameState state, CountryState country)
        {
            var gov = country.government;
            if (!gov.inCivilConflict) return;

            gov.civilConflictMonthsRemaining--;
            gov.civilConflictMonthsElapsed++;

            // A conflict the state is losing badly enough, for long enough, can
            // end with the country in two pieces rather than back together
            // (GDD §17.1). Checked before the damage below so the fracture
            // resolves the conflict rather than racing its countdown.
            if (SecessionSystem.IsFracturing(state, country))
            {
                int monthIndex = state.date.MonthsSince(state.startDate);
                var fractureRng = new Random(
                    unchecked(state.rngSeed * 92821 + monthIndex * 131 + Hash.Of(country.id)));
                if (SecessionSystem.Fracture(state, country, fractureRng) != null) return;
            }

            country.stability = Clamp(country.stability - 0.8f);
            country.nationalUnity = Clamp(country.nationalUnity - 0.6f);
            country.economy.confidence = Clamp(country.economy.confidence - 1.2f);
            country.pillars.economy = Clamp(country.pillars.economy - 0.4f);
            country.pillars.military = Clamp(country.pillars.military - 0.3f);
            country.resources.industrialCapacity = Clamp(country.resources.industrialCapacity - 0.4f);

            if (gov.civilConflictMonthsRemaining > 0) return;

            gov.inCivilConflict = false;
            gov.civilConflictMonthsElapsed = 0;
            country.stability = Clamp(country.stability + 15f);
            gov.militaryLoyalty = Clamp(gov.militaryLoyalty + 10f);
            gov.conspiracyLevel = Clamp(gov.conspiracyLevel - 20f);

            state.AddNotification(
                country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                "CIVIL CONFLICT ENDS",
                $"Organized resistance in {country.displayName} has been broken. " +
                "The state is intact, and much poorer for it.", country.id,
                desk: ReportingDesk.Government);
            state.AddChronicle(ChronicleCategory.Political, country.id,
                "Civil conflict ends. State authority restored at heavy cost.", Publicity.Public);
        }

        // ---------- player action ----------

        /// <summary>
        /// Buy the loyalty of the officer corps: promotions, budgets, patronage.
        /// Effective and quietly corrosive.
        /// </summary>
        public static bool SecureMilitaryLoyalty(GameState state)
        {
            if (!GovernmentSystem.SpendPoliticalCapital(state, SecureLoyaltyCost, "Secure military loyalty"))
                return false;

            var player = state.PlayerCountry;
            player.government.militaryLoyalty = Clamp(player.government.militaryLoyalty + 12f);
            player.government.conspiracyLevel = Clamp(player.government.conspiracyLevel - 8f);
            player.governmentApproval = Clamp(player.governmentApproval - 3f);
            player.pillars.government = Clamp(player.pillars.government - 1f);

            state.AddNotification(NotificationClass.Advisory, "COMMAND LOYALTY SECURED",
                "Promotions and budgets have been directed where they will do the most good.", player.id);
            state.AddChronicle(ChronicleCategory.Political, player.id, "Officer corps loyalty secured.");
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 15, "Secured military loyalty");
            return true;
        }

        static float Approach(float current, float target, float rate) => current + (target - current) * rate;
        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

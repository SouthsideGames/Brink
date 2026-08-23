using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Macroeconomy, trade and economic warfare (GDD Phase 5, §20).
    /// Growth, inflation, employment, debt and confidence interact; sanctions
    /// damage the target but reliably blow back on the sender through inflation
    /// and supply disruption. Economic pressure coerces a government — it never
    /// reduces an opponent's GDP to zero.
    /// </summary>
    public static class EconomySystem
    {
        /// <summary>CP cost to impose or lift a sanctions regime.</summary>
        public const int SanctionCost = 2;

        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
                UpdateCountry(state, country);
        }

        /// <summary>
        /// Demographic recovery. Manpower is drawn down by casualties and by
        /// mobilization, and previously had no path back at all: a country that
        /// fought one war carried the loss for the rest of a decades-long save,
        /// and heavy losses drove the figure negative. A population recovers —
        /// slowly, and faster when the country is fed and holding together.
        /// </summary>
        static void RecoverManpower(CountryState country)
        {
            var resources = country.resources;

            // Saves written before these baselines existed seed them from where
            // the country currently stands.
            if (resources.manpowerBaseline <= 0f)
                resources.manpowerBaseline = Math.Max(1f, resources.manpower);
            if (resources.energyEndowment <= 0f)
                resources.energyEndowment = resources.energy;
            if (resources.materialsEndowment <= 0f)
                resources.materialsEndowment = resources.strategicMaterials;

            if (resources.manpower < 0f) resources.manpower = 0f;
            if (resources.manpower >= resources.manpowerBaseline) return;

            // ~4%/year at full health, so a war costing a third of the recruitable
            // base takes the better part of a decade to make good.
            float health = (country.resources.foodSecurity * 0.5f + country.stability * 0.5f) / 100f;
            float rate = resources.manpowerBaseline * 0.0035f * Math.Max(0.25f, health);

            resources.manpower = Math.Min(resources.manpowerBaseline, resources.manpower + rate);
        }

        /// <summary>
        /// Record downturns in the world's history (GDD §31.3).
        ///
        /// Recessions were the one category on §31.3's list of eight that never
        /// reached the chronicle — elections, wars, treaties, alliances, coups,
        /// territory and technology all did. So the economic half of a
        /// thirty-year alternate history was simply absent: the record showed
        /// every war and no depression.
        /// </summary>
        static void TrackContraction(GameState state, CountryState country, EconomyState eco)
        {
            bool wasInRecession = eco.InRecession;
            bool wasInDepression = eco.InDepression;

            if (eco.growthRate < 0f) eco.contractionMonths++;
            else eco.contractionMonths = 0;

            if (!wasInRecession && eco.InRecession)
            {
                state.AddChronicle(ChronicleCategory.Economic, country.id,
                    $"{country.displayName} enters recession.", Publicity.Public);
                state.AddNotification(
                    country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                    "RECESSION", $"{country.displayName}'s economy is contracting.", country.id,
                    desk: ReportingDesk.Economy);
            }
            else if (wasInRecession && !eco.InRecession)
            {
                state.AddChronicle(ChronicleCategory.Economic, country.id,
                    $"{country.displayName} returns to growth.", Publicity.Public);
            }

            if (!wasInDepression && eco.InDepression)
            {
                state.AddChronicle(ChronicleCategory.Economic, country.id,
                    $"{country.displayName}'s contraction becomes a depression.", Publicity.Public);
                state.AddNotification(
                    country.isPlayer ? NotificationClass.Priority : NotificationClass.Wire,
                    "DEPRESSION",
                    $"{country.displayName} has contracted for a full year.", country.id,
                    desk: ReportingDesk.Economy);
            }
        }

        /// <summary>
        /// The most energy this country can hold: what it was authored with,
        /// plus what a research programme has added, plus what territory it
        /// actually controls supplies (GDD §16, §20).
        ///
        /// Public and shared so that anyone *investing* in energy is bounded by
        /// the same ceiling the monthly model drifts toward. The AI's
        /// `SecureResources` used to add energy with no ceiling at all, which
        /// silently repealed this for every non-player state.
        /// </summary>
        public static float EnergyCeilingFor(GameState state, CountryState country)
            => Clamp(country.resources.energyEndowment
                     + TechnologySystem.Effectiveness(country, "CAP_ENERGY") * 30f
                     + TerritorySystem.EnergySwing(state, country.id)
                     + TradeSystem.Supply(state, country.id, TradeFocus.Energy), 0f, 100f);

        /// <summary>
        /// The most strategic materials this country can hold.
        ///
        /// Takes the world now, because trade supplies it: an authored shortfall
        /// has three answers — invent your way out, take what you need, or buy it
        /// from someone and accept the dependence that comes with them.
        /// </summary>
        public static float MaterialsCeilingFor(GameState state, CountryState country)
            => Clamp(country.resources.materialsEndowment
                     + TradeSystem.Supply(state, country.id, TradeFocus.Materials)
                     + TerritorySystem.MaterialsSwing(state, country.id)
                     + NationalTraitCatalog.ResourceCeilingBonus(country), 0f, 100f);

        static void UpdateCountry(GameState state, CountryState country)
        {
            var eco = country.economy;

            RecoverManpower(country);

            float sanctionPressure = SanctionPressureOn(state, country.id);
            float blowback = SanctionBlowbackFor(state, country.id);
            float tradeHealth = TradeHealth(state, country.id);
            bool atWar = state.IsAtWar(country.id);

            // ---- growth ----
            float structural = (country.pillars.economy - 50f) * 0.035f;
            float industryPull = (country.resources.industrialCapacity - 50f) * 0.012f;
            float confidencePull = (eco.confidence - 50f) * 0.02f;
            float energyDrag = country.resources.energy < 40f ? (40f - country.resources.energy) * 0.05f : 0f;

            float targetGrowth = 1.6f + structural + industryPull + confidencePull
                                 + (tradeHealth - 50f) * 0.014f
                                 - sanctionPressure * 0.55f
                                 - blowback * 0.2f
                                 - energyDrag
                                 - (atWar ? 1.3f : 0f);

            eco.growthRate = Approach(eco.growthRate, targetGrowth, 0.35f);
            TrackContraction(state, country, eco);

            // ---- distress: the crisis regime ----
            //
            // Everything above is a gentle linear nudge calibrated for ordinary
            // times, and until this was added there was nothing else. A country
            // whose market index had fallen from 100 to **7.6** — sectors gutted,
            // confidence at 12, energy at 7 — was still reporting 9% unemployment
            // and 9% inflation. That is a mild recession, and it was the worst
            // outcome the model could produce.
            //
            // Two things followed from that, both bad. Economic warfare could
            // never actually reach a population, so sanctions, blockades and
            // strategic bombing moved numbers nobody feels. And the entire social
            // layer was unreachable: unrest is driven by inflation, unemployment
            // and living standards, so if none of those can reach crisis levels,
            // neither can unrest, and every event gated on it (GENERAL_STRIKE at
            // 58, SEPARATIST_MOVEMENT at 42, PORT_STRIKE at 40) is dead content.
            //
            // Zero in normal play by construction — a healthy index sits near 100
            // and this term does not exist above 55 — so it adds a crisis regime
            // without retuning the ordinary one.
            float distress = Math.Max(0f, 55f - eco.marketIndex) / 55f;

            // ---- inflation ----
            // Coercion is a supply shock: scarcity raises prices even as demand
            // cools, so a sanctioned economy stagflates rather than disinflates.
            float targetInflation = 2.2f
                                    + Math.Max(0f, eco.growthRate - 3.5f) * 0.5f     // demand-side overheating
                                    + sanctionPressure * 1.2f                        // import scarcity
                                    + blowback * 1.0f                                // self-inflicted scarcity
                                    + energyDrag * 1.2f
                                    + distress * 9f                                  // a broken economy prices badly
                                    + (atWar ? 1.6f : 0f)
                                    - Math.Max(0f, 60f - country.resources.industrialCapacity) * 0.008f;
            eco.inflation = Math.Max(-3f, Approach(eco.inflation, targetInflation, 0.3f));

            // ---- employment ----
            //
            // The distress term is what gives this a crisis range at all. Driven
            // only by growth and sanctions, unemployment was structurally
            // incapable of passing ~12 however comprehensively the economy failed,
            // because growth itself is bounded — so "mass unemployment" was not a
            // state this simulation could represent.
            float targetUnemployment = 6.5f - eco.growthRate * 0.9f + sanctionPressure * 0.5f
                                       + distress * 22f
                                       + (atWar ? -0.8f : 0f);
            eco.unemployment = Clamp(Approach(eco.unemployment, targetUnemployment, 0.25f), 1.5f, 35f);

            // ---- debt & treasury ----
            float deficitPressure = atWar ? 0.9f : 0.15f;
            eco.debtToGdp = Clamp(eco.debtToGdp + deficitPressure - Math.Max(0f, eco.growthRate) * 0.18f, 0f, 250f);

            eco.gdp = Math.Max(50f, eco.gdp * (1f + eco.growthRate / 1200f));
            country.resources.treasury += eco.gdp * 0.012f * (1f - eco.debtToGdp / 400f);

            // ---- confidence ----
            float targetConfidence = 50f + eco.growthRate * 6f - Math.Max(0f, eco.inflation - 4f) * 3.5f
                                     - sanctionPressure * 6f - blowback * 3f
                                     + (country.stability - 50f) * 0.25f
                                     - (atWar ? 8f : 0f);
            eco.confidence = Clamp(Approach(eco.confidence, targetConfidence, 0.25f), 0f, 100f);

            // ---- sectors ----
            foreach (var sector in eco.sectors)
            {
                float healthTarget = 88f - sanctionPressure * 9f - blowback * 5f;
                if (sector.sector == EconomicSector.Energy && country.resources.energy < 45f) healthTarget -= 12f;
                if (sector.sector == EconomicSector.Defense && atWar) healthTarget += 8f;
                if (sector.sector == EconomicSector.Finance) healthTarget -= Math.Max(0f, eco.debtToGdp - 90f) * 0.15f;
                sector.health = Clamp(Approach(sector.health, healthTarget, 0.2f), 0f, 100f);

                float outputTarget = sector.output + eco.growthRate * 0.08f - sanctionPressure * 0.35f;
                if (sector.sector == EconomicSector.Defense && atWar) outputTarget += 0.5f;
                sector.output = Clamp(Approach(sector.output, outputTarget, 0.5f), 0f, 100f);
            }

            // Capability compounds slowly into real capacity (GDD §11) — it
            // unlocks the ability to build, it does not hand over the result.
            country.resources.industrialCapacity = Growth.Apply(country.resources.industrialCapacity,
                TechnologySystem.Effectiveness(country, "CAP_ADVMFG") * 0.12f);

            // Held industrial centres are capacity; lost ones are not. Applied as
            // a drift toward the adjusted level so seizing a works does not
            // teleport its output home the month it falls.
            float industrySwing = TerritorySystem.IndustrySwing(state, country.id);
            if (Math.Abs(industrySwing) > 0.01f)
                country.resources.industrialCapacity = Clamp(
                    Approach(country.resources.industrialCapacity,
                             country.resources.industrialCapacity + industrySwing, 0.05f), 0f, 100f);

            // Strategic materials and energy erode under embargo, recover otherwise.
            //
            // Recovery approaches an *endowment* rather than accruing without
            // limit. A flat +0.35/month took every authored energy-poor state to
            // 100 within about nineteen years, so the deliberate vulnerabilities
            // that distinguish the roster (spec 08) simply evaporated a few years
            // into a save and `energyDrag` stopped biting for anyone. A country
            // can invest past its endowment only through capability.
            // Territory held is part of the endowment: an energy region you have
            // taken supplies you, and one taken from you does not (GDD §16).
            float energyCeiling = EnergyCeilingFor(state, country);
            float energyDrift = sanctionPressure > 0.8f
                ? -0.8f
                : Math.Min(0.35f, (energyCeiling - country.resources.energy) * 0.02f);
            country.resources.energy = Clamp(country.resources.energy + energyDrift, 0f, 100f);

            float materialsCeiling = MaterialsCeilingFor(state, country);
            float materialsDrift = sanctionPressure > 1.2f
                ? -0.7f
                : Math.Min(0.25f, (materialsCeiling - country.resources.strategicMaterials) * 0.02f);
            country.resources.strategicMaterials =
                Clamp(country.resources.strategicMaterials + materialsDrift, 0f, 100f);

            // ---- market index (GDD §20.1) ----
            // The index tracks fundamentals and reacts sharply to shocks, rather
            // than compounding indefinitely. 100 is the world-creation baseline.
            float fundamentals = 45f
                                 + eco.confidence * 0.85f
                                 + eco.growthRate * 7f
                                 - Math.Max(0f, eco.inflation - 4f) * 3.5f
                                 - sanctionPressure * 9f
                                 - blowback * 4f
                                 - (atWar ? 12f : 0f);
            fundamentals = Clamp(fundamentals, 8f, 260f);

            // Sentiment converges on fundamentals. Sanctions and war are already
            // priced into `fundamentals`, so there is no separate ongoing shock
            // term — applying one every month would drag the index permanently
            // below its own floor rather than reacting and settling.
            eco.marketIndex += (fundamentals - eco.marketIndex) * 0.14f;
            eco.marketIndex = Math.Max(5f, eco.marketIndex);
            eco.RecordMarket();

            // Severe economic distress is politically corrosive.
            if (eco.inflation > 12f || eco.growthRate < -3f)
            {
                country.governmentApproval = Clamp(country.governmentApproval - 0.6f, 0f, 100f);
                country.stability = Clamp(country.stability - 0.35f, 0f, 100f);
            }
        }

        // ---------- player commands ----------

        /// <summary>
        /// Impose sanctions. Costs CP, damages the target, and imposes real
        /// blowback on the sender — economic pressure is never free (GDD §20).
        /// </summary>
        /// <summary>
        /// Whether we may impose measures at this severity, and why not if we may
        /// not. Views must call this rather than offering a severity that will be
        /// silently refused.
        /// </summary>
        public static bool CanImposeSanctions(GameState state, string targetId,
            SanctionSeverity severity, out string reason)
        {
            if (targetId == state.playerCountryId)
            {
                reason = "We cannot sanction ourselves.";
                return false;
            }
            if (state.FindSanction(state.playerCountryId, targetId) != null)
            {
                reason = "Measures are already in force against that state.";
                return false;
            }
            if (state.FindCountry(targetId) == null)
            {
                reason = "No such state.";
                return false;
            }
            if (severity == SanctionSeverity.Existential
                && ProgressionSystem.EffectValue(state, SkillEffect.ExistentialMeasures) <= 0f)
            {
                reason = "No reach into clearing and insurance — requires Existential Measures.";
                return false;
            }

            reason = "";
            return true;
        }

        public static bool ImposeSanctions(GameState state, TurnManager turns, string targetId, SanctionSeverity severity)
        {
            if (state.FindSanction(state.playerCountryId, targetId) != null)
            {
                GameLog.Warn("ECONOMY", "Sanctions already in force against that state.");
                return false;
            }
            var target = state.FindCountry(targetId);
            if (target == null) return false;

            // Existential measures require reach into clearing and insurance that
            // must be built first (strategic verb, GDD §25.3).
            if (severity == SanctionSeverity.Existential
                && ProgressionSystem.EffectValue(state, SkillEffect.ExistentialMeasures) <= 0f)
            {
                GameLog.Warn("ECONOMY", "We lack the financial reach to impose measures at that severity.");
                return false;
            }

            int cost = ProgressionSystem.DiscountedCost(state, SanctionCost, SkillEffect.CoercionEfficiency);
            if (!turns.SpendCommandPoints(cost, $"Impose {severity} sanctions on {target.displayName}"))
                return false;

            bool imposed = ImposeSanctionsBy(state, state.playerCountryId, targetId, severity);
            if (imposed) ProgressionSystem.AwardXP(state, 12, "Sanctions imposed");
            if (imposed) ProgressionSystem.RecordInitiative(state);
            return imposed;
        }

        /// <summary>Sanctions imposed by any state. AI coercion uses the same model.</summary>
        public static bool ImposeSanctionsBy(GameState state, string senderId, string targetId, SanctionSeverity severity)
        {
            if (senderId == targetId) return false;
            if (state.FindSanction(senderId, targetId) != null) return false;
            var target = state.FindCountry(targetId);
            var sender = state.FindCountry(senderId);
            if (target == null || sender == null) return false;

            state.sanctions.Add(new Sanction
            {
                senderId = senderId,
                targetId = targetId,
                severity = severity,
                imposedDate = state.date
            });

            var link = state.FindTrade(senderId, targetId);
            if (link != null && severity >= SanctionSeverity.Severe)
                link.embargoed = true;

            bool playerInvolved = senderId == state.playerCountryId || targetId == state.playerCountryId;
            state.AddNotification(
                playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                targetId == state.playerCountryId ? "SANCTIONS IMPOSED ON US" : "SANCTIONS IMPOSED",
                $"{sender.displayName}: {Phrase.Of(severity)} measures against {target.displayName}.", targetId,
                desk: ReportingDesk.Economy);
            // Coercion winds a confrontation up even though nobody fires (GDD §18.1).
            ConfrontationSystem.AddPressure(state, senderId, targetId, 6f);

            state.AddChronicle(ChronicleCategory.Economic, senderId,
                $"{Phrase.Of(severity)} sanctions imposed on {target.displayName}.", Publicity.Public);
            GameLog.Info("ECONOMY", $"{senderId}: {severity} sanctions imposed on {targetId}.");
            return true;
        }

        public static bool LiftSanctions(GameState state, TurnManager turns, string targetId)
        {
            var sanction = state.FindSanction(state.playerCountryId, targetId);
            if (sanction == null) return false;
            if (!turns.SpendCommandPoints(1, "Lift sanctions")) return false;

            state.sanctions.Remove(sanction);
            var link = state.FindTrade(state.playerCountryId, targetId);
            if (link != null) link.embargoed = false;

            var target = state.FindCountry(targetId);
            state.AddNotification(NotificationClass.Advisory, "SANCTIONS LIFTED",
                $"Measures against {target?.displayName} withdrawn.", targetId);
            state.AddChronicle(ChronicleCategory.Economic, state.playerCountryId,
                                $"Sanctions on {target?.displayName} lifted.", Publicity.Public);

            // De-escalation is statecraft too. Without this, a player who imposes
            // pressure, extracts a concession and then lifts is credited for only
            // half the sequence, and the Economy pillar grades below an identical
            // player who never lifts anything.
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 10, "Sanctions lifted");
            return true;
        }

        /// <summary>Adjust a tariff on a trade link. Cheap lever, modest effect.</summary>
        public static bool SetTariff(GameState state, TurnManager turns, string partnerId, float tariff)
        {
            var link = state.FindTrade(state.playerCountryId, partnerId);
            if (link == null) return false;
            if (!turns.SpendCommandPoints(1, $"Set tariff {tariff:F0}%")) return false;

            link.tariff = Clamp(tariff, 0f, 100f);
            state.AddChronicle(ChronicleCategory.Economic, state.playerCountryId,
                $"Tariff on {state.FindCountry(partnerId)?.displayName} set to {link.tariff:F0}%.",
                Publicity.Public);
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        // ---------- queries ----------

        /// <summary>Total sanction weight bearing on a country, and ages the regimes.</summary>
        public static float SanctionPressureOn(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var sanction in state.sanctions)
                if (sanction.targetId == countryId) total += sanction.Weight;
            return total;
        }

        /// <summary>Self-inflicted damage from sanctions this country imposes on others.</summary>
        public static float SanctionBlowbackFor(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var sanction in state.sanctions)
                if (sanction.senderId == countryId)
                {
                    var link = state.FindTrade(countryId, sanction.targetId);
                    // Sanctioning a major trade partner hurts far more.
                    float exposure = link != null ? 0.5f + link.volume / 100f : 0.4f;
                    float mitigation = countryId == state.playerCountryId
                        ? Math.Max(0.15f, 1f - ProgressionSystem.EffectValue(state, SkillEffect.SanctionPrecision))
                        : 1f;

                    // Financial reach makes coercion cheaper to sustain (GDD §11).
                    var sender = state.FindCountry(countryId);
                    if (sender != null)
                        mitigation *= 1f - TechnologySystem.Effectiveness(sender, "CAP_FINANCE") * 0.3f;

                    total += sanction.Blowback * exposure * mitigation;
                }
            return total;
        }

        /// <summary>Effective trade health for a country: volume net of tariffs and embargoes.</summary>
        /// <summary>
        /// 0..100 read of how well this country's trade is working for it.
        ///
        /// This is an average per link, lifted modestly by how many partners it
        /// has: breadth is genuine resilience, but it must not scale without
        /// bound. It previously returned the raw *sum*, while the consumer
        /// centred it on 50 — so once the roster grew to 16 countries a hub with
        /// 8 links scored 452 and drew +5.6 points of annual growth, while a
        /// peripheral state scored 42 and drew none. The number of links a
        /// country was authored with, not anything anyone did, decided the
        /// thirty-year economic ranking.
        /// </summary>
        public static float TradeHealth(GameState state, string countryId)
        {
            float total = 0f;
            int links = 0;
            foreach (var link in state.trade)
            {
                if (!link.Involves(countryId)) continue;
                links++;
                if (link.embargoed) continue;
                total += link.volume * (1f - link.tariff / 150f);
            }

            if (links == 0) return 0f;

            float average = total / links;
            float breadth = Math.Min(1.25f, 0.80f + links * 0.05f);

            // Ports and chokepoints are access. Holding them helps trade work;
            // losing them hurts it.
            float access = TerritorySystem.TradeAccessSwing(state, countryId);
            return Clamp(average * breadth + access, 0f, 100f);
        }

        /// <summary>Advance sanction ages; called once per resolved month.</summary>
        public static void AgeSanctions(GameState state)
        {
            for (int i = state.sanctions.Count - 1; i >= 0; i--)
            {
                var sanction = state.sanctions[i];
                sanction.monthsActive++;

                // The player lifts their own measures deliberately. Nothing ever
                // lifted an AI's, and `monthsActive` was recorded and never read,
                // so a sanction imposed in year two was still running in year
                // thirty — and because sanctioned pairs skip the relations-
                // recovery branch entirely, those two states were locked in
                // terminal hostility for the rest of the save.
                if (sanction.senderId == state.playerCountryId) continue;
                if (sanction.monthsActive < SanctionReviewMonths) continue;

                var sender = state.FindCountry(sanction.senderId);
                var target = state.FindCountry(sanction.targetId);
                var relationship = state.FindRelationship(sanction.senderId, sanction.targetId);
                if (sender == null || target == null) continue;

                // Kept in force while they are still regarded as a threat.
                bool stillHostile = relationship != null
                                    && (relationship.relations < 30f
                                        || relationship.ThreatPerceivedBy(sanction.senderId) > 55f);
                if (stillHostile) continue;

                state.sanctions.RemoveAt(i);
                var link = state.FindTrade(sanction.senderId, sanction.targetId);
                if (link != null) link.embargoed = false;

                state.AddNotification(
                    sanction.targetId == state.playerCountryId
                        ? NotificationClass.Priority : NotificationClass.Wire,
                    "SANCTIONS LAPSE",
                    $"{sender.displayName} allows its measures against {target.displayName} to lapse.",
                    sanction.targetId, desk: ReportingDesk.Economy);
                state.AddChronicle(ChronicleCategory.Economic, sanction.senderId,
                    $"Measures against {target.displayName} lapsed.", Publicity.Public);
            }
        }

        /// <summary>Months after which a foreign government revisits a sanctions regime.</summary>
        public const int SanctionReviewMonths = 36;

        static float Approach(float current, float target, float rate) => current + (target - current) * rate;

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);
    }
}

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

        /// <summary>
        /// Treasury income per month as a share of GDP (before the debt haircut).
        ///
        /// This is the unit every recurring cost in the game should be sized
        /// against. It shipped at 0.012 — 15–40 a month for the authored roster —
        /// while war exhaustion (~40–80/month), occupation (~45/month), a research
        /// programme (40–55/month) and an endgame authorization (120) were all
        /// priced as if income were several times that: a belligerent mid-tier
        /// state ended a passive decade several thousand in the red, and the
        /// strategic instruments spec 14 designs around "~8 authorizations" were
        /// unaffordable for every posting. 0.03 puts a great power at ~70–100 a
        /// month, so a war is expensive rather than ruinous and a programme is a
        /// commitment rather than an impossibility. Measured 2026-08-26 with the
        /// headless harness; re-measure with `Report_MultiSeedBalance`.
        /// </summary>
        public const float TreasuryIncomeRate = 0.03f;

        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
                UpdateCountry(state, country);

            TrackTreasuryTrend(state);
        }

        /// <summary>
        /// The player's smoothed monthly treasury delta, measured at the same
        /// point in the pipeline every month so the deltas are comparable.
        ///
        /// A readout, not a rule: nothing reads it but the briefing and the
        /// attention list. It exists because the *real* consequences of deficit
        /// spending arrive on a lag of years, and an operator on a phone is
        /// entitled to hear "we spend more than we make" while it is still an
        /// arithmetic fact rather than a collapsed market.
        /// </summary>
        static void TrackTreasuryTrend(GameState state)
        {
            var player = state.PlayerCountry;
            if (player == null) return;

            if (!state.treasuryTrendSeeded)
            {
                state.treasuryTrendSeeded = true;
                state.lastMonthTreasury = player.resources.treasury;
                return;
            }

            float delta = player.resources.treasury - state.lastMonthTreasury;
            state.lastMonthTreasury = player.resources.treasury;

            // ~5-month memory: quick enough to notice a new programme's bill,
            // slow enough that one bad month is not a klaxon.
            state.treasuryTrend = state.treasuryTrend * 0.8f + delta * 0.2f;
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
            if (resources.foodEndowment <= 0f)
                resources.foodEndowment = resources.foodSecurity;

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

        /// <summary>
        /// The most food security this country can hold: what its own land
        /// supports, plus what its trade actually delivers. Same shape as the
        /// two ceilings above and for the same reason — an authored food-poor
        /// state stays food-poor unless somebody sells to it, and the supply is
        /// exactly as reliable as the relationship behind it.
        /// </summary>
        public static float FoodCeilingFor(GameState state, CountryState country)
            => Clamp(country.resources.foodEndowment
                     + TradeSystem.Supply(state, country.id, TradeFocus.Food), 0f, 100f);

        /// <summary>
        /// How much domestic capacity a sector is losing to imports.
        ///
        /// Only the sector that competes with what is being imported. A country
        /// buying its energy abroad lets its own energy industry wither; one
        /// buying strategic materials lets the industry that processed them go.
        /// General trade presses on consumer manufacturing, which is where broad
        /// import competition actually lands.
        ///
        /// Scaled to volume and softened by tariffs — which is what tariffs are
        /// *for*, and gives that binary 25%/0% toggle a reason to exist beyond
        /// annoying a partner. An embargoed link displaces nothing: nothing is
        /// arriving.
        /// </summary>
        /// <summary>
        /// What this economy's industries are actually managing, 0..100.
        ///
        /// Capacity and functioning multiplied rather than averaged: a sector with
        /// plant it cannot run is not half-productive, it is idle. That is what
        /// makes sabotage and blockade meaningful against an otherwise large
        /// economy — you do not have to destroy the factories, only stop them.
        /// </summary>
        public static float SectorStrength(EconomyState eco)
        {
            if (eco == null || eco.sectors.Count == 0) return 55f;

            float total = 0f;
            foreach (var sector in eco.sectors)
                total += sector.output * (sector.health / 100f);

            return total / eco.sectors.Count;
        }

        public static float ImportDisplacement(GameState state, CountryState country,
            EconomicSector sector)
        {
            TradeFocus competing;
            switch (sector)
            {
                case EconomicSector.Energy: competing = TradeFocus.Energy; break;
                case EconomicSector.Industry: competing = TradeFocus.Materials; break;
                case EconomicSector.Consumer: competing = TradeFocus.General; break;
                case EconomicSector.Agriculture: competing = TradeFocus.Food; break;
                default: return 0f;   // nothing imported competes with these
            }

            float pressure = 0f;
            foreach (var link in state.trade)
            {
                if (!link.Involves(country.id) || link.embargoed) continue;
                if (link.focus != competing) continue;

                // A tariff is protection. At 25% it removes most of the pressure,
                // which is the historical bargain: a shielded industry survives and
                // everyone pays more for what it makes.
                float shielded = 1f - Math.Min(0.8f, link.tariff / 32f);
                pressure += link.volume * 0.006f * shielded;
            }

            return pressure;
        }

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

            // **The sector layer has to reach the economy.**
            //
            // `output` and `health` were written by four systems — the monthly
            // drift, the assessment, industrial programmes, the strategic endgame
            // — and **read by none of them**. Not by growth, not by the market
            // index, not by anything. Seven entries per country across sixteen
            // countries, modelled in detail and consumed nowhere: the largest
            // instance of this codebase's most-repeated bug.
            //
            // Everything aimed at it was therefore inert. `CovertOperation.Sabotage`
            // is documented as damaging "industry/sector health" and did nothing.
            // The endgame's −25 health did nothing. Import displacement, added an
            // hour ago to give trade a domestic cost, did nothing — which is why
            // the diplomatic playstyle's economic component did not move a single
            // point after it landed.
            //
            // Weighted to sit alongside the pillar term rather than dominate it:
            // what a country's industries are actually doing should matter about
            // as much as its institutional capability, not more.
            // Pivoted just below where a healthy economy actually rests (~53:
            // output around 60 against health drifting to 88), so this is close to
            // neutral for a country doing fine, clearly negative for one whose
            // industries have been wrecked, and clearly positive for one that has
            // built them up.
            //
            // The first attempt pivoted at 55 — above the resting point — which
            // made it a universal tax rather than a differentiator: every GDP in
            // the world fell, including passive play's, and the whole scale simply
            // shifted down. A term meant to distinguish states must sit at the
            // level they actually occupy.
            float sectorPull = (SectorStrength(eco) - 50f) * 0.030f;

            float targetGrowth = 1.6f + structural + industryPull + confidencePull + sectorPull
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

            // ---- a long depression costs the country its capability ----
            //
            // **The economy pillar had no downward path in ordinary play.**
            // Recession, sanctions, embargo, debt crisis and collapsing industrial
            // capacity all moved `confidence`, `growthRate` and `marketIndex` and
            // *none of them touched `pillars.economy`* — so a decade of depression
            // left national economic capability exactly where it started, and the
            // only recurring way it could fall was a minister having a bad month.
            // Compare the military pillar, which erodes from losses, peace terms
            // and purges.
            //
            // Consequences: economic warfare could not reach the thing the annual
            // evaluation actually grades, and the telemetry ratchet detector was
            // right to call the pillar one-way — it simply had no magnitude gate
            // to say so proportionately.
            //
            // Driven by the same `distress` term as the crisis regime, so it is
            // **zero by construction in normal play** and cannot quietly retune a
            // healthy economy. Skills and capacity are lost slowly: at full
            // collapse this is about 1.3 points a year, so a bad decade costs real
            // ground without making recovery impossible.
            if (distress > 0.01f)
                country.pillars.economy = Clamp(country.pillars.economy - distress * 0.11f, 0f, 100f);

            // **And the ordinary version of the same thing.**
            //
            // The `distress` term above only fires below a market index of 55 —
            // a genuine collapse — so it closed the crisis case and left the one
            // that actually happens: a decade of stagnation. An economy that is
            // shrinking, with plant idle and people out of work for years, loses
            // capability whether or not the index ever crosses the crisis line,
            // and until now such a decade left `pillars.economy` exactly where it
            // started. Compare the military pillar, which erodes from losses,
            // peace terms and purges.
            //
            // Zero by construction in normal play, deliberately, and by the same
            // discipline as `distress`: growth has to be actually negative and
            // unemployment above 9% before either term is non-zero, so this
            // cannot quietly retune a healthy economy.
            //
            // Sized against the routine ministry contribution (`amount` in
            // `CabinetSystem.ApplyPillarEffect` is 0.05–0.30 a month before
            // damping, so roughly 1.8 points a year). At −3% growth and 14%
            // unemployment this is ~1.4 a year: a bad decade stops the pillar
            // growing rather than destroying it, which is the right severity for
            // a condition a country can govern its way out of. The recovery path
            // is the same one that produced the capability — a working ministry
            // and industrial programmes — and it is reachable the month growth
            // turns positive.
            float stagnation = StagnationDrag(eco);
            if (stagnation > 0.001f)
                country.pillars.economy = Clamp(country.pillars.economy - stagnation, 0f, 100f);

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
            // distress × 22 → 16 (2026-08): with sanctions adapting the spiral
            // no longer locks, but a fully distressed market still put a great
            // power at 28% unemployment for a decade; 16 keeps "mass
            // unemployment" representable (≈22% at the floor) with a way back.
            float targetUnemployment = 6.5f - eco.growthRate * 0.9f + sanctionPressure * 0.5f
                                       + distress * 16f
                                       + (atWar ? -0.8f : 0f);
            eco.unemployment = Clamp(Approach(eco.unemployment, targetUnemployment, 0.25f), 1.5f, 35f);

            // ---- debt & treasury ----
            float deficitPressure = atWar ? 0.9f : 0.15f;
            eco.debtToGdp = Clamp(eco.debtToGdp + deficitPressure - Math.Max(0f, eco.growthRate) * 0.18f, 0f, 250f);

            eco.gdp = Math.Max(50f, eco.gdp * (1f + eco.growthRate / 1200f));
            country.resources.treasury += eco.gdp * TreasuryIncomeRate * (1f - eco.debtToGdp / 400f);

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

                // **What you import, you stop making.**
                //
                // Trade was pure gain: cheaper inputs, a higher resource ceiling,
                // better relations, and no cost anywhere. That is most of why the
                // diplomatic playstyle grades a full point above passive with the
                // best ECON *and* POS components in the game — it compounds
                // without ever paying.
                //
                // The real cost of trade is concentrated and domestic: imports
                // outcompete the industry that used to supply the same thing, the
                // losses land on identifiable sectors while the gains are diffuse,
                // and the capability to restart erodes long before anyone decides
                // to. So a heavy energy link hollows out domestic energy, and a
                // heavy materials link hollows out industry.
                //
                // It is a *trade-off*, not a penalty: the resource ceiling that
                // link buys is usually worth more than the sector it costs. But it
                // is now a decision with two sides, and an industrial programme is
                // the answer to it — which is exactly the sort of coupling this
                // pillar was missing.
                float displacement = ImportDisplacement(state, country, sector.sector);

                float outputTarget = sector.output + eco.growthRate * 0.08f
                                     - sanctionPressure * 0.35f
                                     - displacement;
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
            // Sanctions move the target, never the value — the food rule, ported
            // back to the two drifts it was copied from. The flat erosion here
            // (−0.8/month, floorless) drained *authored energy superpowers* to
            // literal zero under the hot world's standing sanction regimes:
            // played as Russia (endowment 96), twenty sanctioned years ended at
            // energy 0, living standards 0, approval 0 — a country that pumps
            // its own oil starved of it by foreign paperwork. The eleventh
            // instance of the value-versus-target family. The ceiling already
            // loses its trade component under sanctions (`Supply` checks them),
            // so the ×0.55 on what remains is disruption of domestic output —
            // painful, and with a resting point a producer can live at.
            float energyCeiling = EnergyCeilingFor(state, country);
            float energyTarget = sanctionPressure > 0.8f ? energyCeiling * 0.55f : energyCeiling;
            float energyDrift = Math.Max(-0.8f, Math.Min(0.35f,
                (energyTarget - country.resources.energy) * 0.03f));
            country.resources.energy = Clamp(country.resources.energy + energyDrift, 0f, 100f);

            float materialsCeiling = MaterialsCeilingFor(state, country);
            float materialsTarget = sanctionPressure > 1.2f ? materialsCeiling * 0.55f : materialsCeiling;
            float materialsDrift = Math.Max(-0.7f, Math.Min(0.25f,
                (materialsTarget - country.resources.strategicMaterials) * 0.03f));
            country.resources.strategicMaterials =
                Clamp(country.resources.strategicMaterials + materialsDrift, 0f, 100f);

            // Food security moves the same way (GDD §10.1). Written once at world
            // creation and never touched again until this existed, so a siege,
            // an embargo or a lost breadbasket changed a number nobody ate from.
            //
            // **Pressure moves the target, never the value — for every source.**
            // The first version drained flat rates (−0.25/month at war,
            // −0.6/month under heavy sanctions), which is the one-way-value bug
            // in a new costume: measured on seed 1212, a passive great power
            // spends 237 of 240 months under sanctions, so its food ground from
            // 90 to literal zero and the hunger terms pinned its unrest at the
            // cap — caught by `NoSocialValueRunsAwayInEitherDirection`, the
            // ninth instance of the family. Now a war depresses food toward 75%
            // of the ceiling (harvests and distribution run badly; they do not
            // stop) and heavy sanctions toward 50% (siege-level hardship, and a
            // real resting point), with the same proportional drift bringing it
            // home when the pressure lifts. Every level of hardship has
            // somewhere to settle; only the causes decide where.
            float foodCeiling = FoodCeilingFor(state, country);
            float foodTarget = foodCeiling;
            if (atWar) foodTarget = Math.Min(foodTarget, foodCeiling * 0.75f);
            if (sanctionPressure > 1.0f) foodTarget = Math.Min(foodTarget, foodCeiling * 0.5f);
            float foodDrift = Math.Max(-0.6f, Math.Min(0.3f,
                (foodTarget - country.resources.foodSecurity) * 0.03f));
            country.resources.foodSecurity =
                Clamp(country.resources.foodSecurity + foodDrift, 0f, 100f);

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

            // A négotiated détente holds for both sides while it runs — one
            // gate, actor-generic, so the promise binds the player exactly as
            // it binds the AI. War voids it (ConfrontationSystem.BeginBy).
            var relationship = state.FindRelationship(senderId, targetId);
            if (relationship != null && relationship.sanctionsTruceMonths > 0)
            {
                if (senderId == state.playerCountryId)
                    GameLog.Warn("ECONOMY",
                        $"The détente with {target.displayName} holds for another "
                        + $"{relationship.sanctionsTruceMonths} month(s).");
                return false;
            }

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

            // Say why (2026-08): "coercive measures against United States"
            // arrived with no reason and no way to ask. The sender's own view
            // of us is on the relationship; read it back in plain language.
            string why = "";
            if (targetId == state.playerCountryId)
            {
                var view = state.FindRelationship(senderId, targetId);
                if (state.FindSanction(targetId, senderId) != null) why = " A reply to our own measures.";
                else if (view != null && view.ThreatPerceivedBy(senderId) > 55f) why = " It cites the threat our posture presents.";
                else if (view != null && view.relations < 30f) why = " Relations have been poor for some time; this is the next step.";
                else if (state.ActiveConfrontationFor(senderId)?.Involves(targetId) == true) why = " Part of the confrontation between us.";
                else why = " No public justification was offered.";
            }

            if (playerInvolved || WorldWire.Watches(state, senderId) || WorldWire.Watches(state, targetId))
                state.AddNotification(
                    playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                    targetId == state.playerCountryId ? "SANCTIONS IMPOSED ON US" : "SANCTIONS IMPOSED",
                    $"{sender.displayName}: {Phrase.Of(severity)} measures against {target.displayName}.{why}", targetId,
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
        /// <summary>Months over which a sanctioned economy reroutes around a standing regime.</summary>
        public const int SanctionAdaptationMonths = 48;

        /// <summary>Share of a regime's bite that adaptation eventually removes.</summary>
        public const float SanctionAdaptationFloor = 0.5f;

        /// <summary>
        /// Total sanction weight on a country, **net of adaptation** (2026-08).
        ///
        /// A standing regime used to bite at full weight forever, and a regime
        /// lapses only when the sender stops being hostile — so two hostile
        /// neighbours could hold a great power in a permanent depression:
        /// fundamentals pinned, distress feeding unemployment (28%) feeding
        /// living standards (1) feeding unrest (77) feeding approval (0) and a
        /// coup, with nothing the target could do and no path back. The
        /// death-spiral rule: every value that falls needs a reachable recovery
        /// path. An economy reroutes around measures it has lived under for
        /// years; a four-year-old regime bites at half weight. New measures still
        /// land at full weight, so coercion keeps its edge as a *move*.
        /// </summary>
        public static float SanctionPressureOn(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var sanction in state.sanctions)
                if (sanction.targetId == countryId)
                {
                    float adapted = Math.Min(1f, sanction.monthsActive / (float)SanctionAdaptationMonths);
                    total += sanction.Weight * (1f - SanctionAdaptationFloor * adapted);
                }
            return total;
        }

        /// <summary>Self-inflicted damage from sanctions this country imposes on others.</summary>
        /// <summary>
        /// Monthly capability lost to an economy that is shrinking with plant
        /// idle. **Zero at any healthy figure**, by construction — see the
        /// commentary at the call site in `UpdateCountry`.
        /// </summary>
        public static float StagnationDrag(EconomyState eco)
            => Math.Max(0f, -eco.growthRate) * 0.020f
             + Math.Max(0f, eco.unemployment - 9f) * 0.012f;

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

                    // **Multilateral measures cost the sender less.** This is what
                    // a mandate actually buys, and the reason to spend a month's
                    // diplomacy assembling one rather than simply imposing them:
                    // a coalition of senders shares the disruption, and nobody's
                    // exporters can be singled out for it.
                    if (CouncilSystem.SanctionsMandated(state, sanction.targetId))
                        mitigation *= 0.55f;

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
        /// <summary>Months a negotiated détente holds against new sanctions.</summary>
        public const int DetenteTruceMonths = 24;

        /// <summary>
        /// How willing a sender is to lift its measures when asked (spec 02 §4a).
        ///
        /// The trap this answers, in the code's own numbers: sanctions push a
        /// pair's relations down 1.2/month while the automatic lapse needs
        /// relations above 30 — a self-locking cycle with **no verb anywhere to
        /// break it**. Measured on the world census: 40–60 standing AI-AI
        /// regimes, and a pariah great power sanctioned 237 of 240 months with
        /// no road back however it behaved. Willingness prices what actually
        /// moves a sender: the fatigue of an old regime, the blowback it pays
        /// itself, the warmth that survives, minus the threat it still sees.
        /// </summary>
        public static float ReliefWillingness(GameState state, string senderId, string targetId)
        {
            var sanction = state.FindSanction(senderId, targetId);
            var relationship = state.FindRelationship(senderId, targetId);
            if (sanction == null || relationship == null) return 0f;

            float willingness = 12f
                                + relationship.relations * 0.55f
                                + relationship.trust * 0.30f
                                + Math.Min(30f, sanction.monthsActive * 0.5f)   // regimes grow stale
                                + SanctionBlowbackFor(state, senderId) * 8f     // their own cost
                                + relationship.memoryWeight * 1.5f
                                - relationship.ThreatPerceivedBy(senderId) * 0.45f;

            if (state.IsAtWar(targetId)) willingness -= 20f;   // nobody relieves a belligerent
            return willingness;
        }

        /// <summary>Ask a state to lift its measures against us and hold a détente.</summary>
        public static bool SeekSanctionsRelief(GameState state, TurnManager turns, string senderId)
        {
            var sender = state.FindCountry(senderId);
            if (sender == null || state.FindSanction(senderId, state.playerCountryId) == null) return false;
            if (!turns.SpendCommandPoints(2, $"Seek sanctions relief from {sender.displayName}"))
                return false;

            bool lifted = SeekSanctionsReliefBy(state, state.playerCountryId, senderId);
            if (lifted)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 18, "Sanctions relief negotiated");
            }
            return lifted;
        }

        /// <summary>Relief sought by any sanctioned state. AI pariahs use the same door.</summary>
        public static bool SeekSanctionsReliefBy(GameState state, string targetId, string senderId)
        {
            var sanction = state.FindSanction(senderId, targetId);
            var relationship = state.FindRelationship(senderId, targetId);
            var sender = state.FindCountry(senderId);
            var target = state.FindCountry(targetId);
            if (sanction == null || relationship == null || sender == null || target == null) return false;

            // **A sender cannot unilaterally lift what the chamber authorised.**
            // Otherwise a mandate would be worth less than a bilateral regime:
            // the target would simply work the softest member and the whole
            // apparatus would come apart one relationship at a time.
            if (CouncilSystem.SanctionsMandated(state, targetId))
            {
                if (targetId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "RELIEF REFUSED",
                        $"{sender.displayName} cannot lift measures the chamber has authorised. "
                        + "The authorisation is what would have to go.",
                        senderId, desk: ReportingDesk.Diplomacy);
                return false;
            }

            float willingness = ReliefWillingness(state, senderId, targetId);
            if (willingness < 50f)
            {
                relationship.AddMemory(state.date, "Rebuffed a request for sanctions relief", -0.4f);
                if (targetId == state.playerCountryId)
                {
                    // The refusal says what would change it — the NO ASSESSMENT rule.
                    string why = relationship.ThreatPerceivedBy(senderId) > 55f
                        ? "They still regard us as a threat; posture and conduct are what move that."
                        : state.IsAtWar(targetId)
                            ? "Not while we are at war."
                            : "The relationship is not warm enough yet to carry it.";
                    state.AddNotification(NotificationClass.Advisory, "RELIEF REFUSED",
                        $"{sender.displayName} keeps its measures in force. {why}",
                        senderId, desk: ReportingDesk.Diplomacy);
                }
                GameLog.Info("ECONOMY", $"{senderId} refused sanctions relief to {targetId}.");
                return false;
            }

            state.sanctions.Remove(sanction);
            var link = state.FindTrade(senderId, targetId);
            if (link != null) link.embargoed = false;

            relationship.sanctionsTruceMonths = DetenteTruceMonths;
            relationship.relations = Clamp(relationship.relations + 6f, 0f, 100f);
            relationship.trust = Clamp(relationship.trust + 5f, 0f, 100f);
            relationship.AddMemory(state.date, "Negotiated an end to sanctions", 1.5f);

            bool playerInvolved = senderId == state.playerCountryId || targetId == state.playerCountryId;
            state.AddNotification(playerInvolved ? NotificationClass.Priority : NotificationClass.Wire,
                "SANCTIONS LIFTED BY NEGOTIATION",
                $"{sender.displayName} lifts its measures against {target.displayName}. A détente "
                + $"holds for {DetenteTruceMonths} months.",
                targetId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, targetId,
                $"Negotiated an end to {sender.displayName}'s sanctions.", Publicity.Public);
            GameLog.Info("ECONOMY", $"{senderId} lifted sanctions on {targetId} by negotiation.");
            return true;
        }

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

                // A mandated regime does not quietly lapse. Ending measures the
                // chamber authorised is a decision somebody has to take in public,
                // which is the other half of what a mandate is worth.
                if (CouncilSystem.SanctionsMandated(state, sanction.targetId)) continue;

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

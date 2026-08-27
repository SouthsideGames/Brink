using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Armed movements, and the states that arm them (GDD §12, §17.1, §19).
    ///
    /// **Every actor in this game was a government.** Sixteen to twenty-four of
    /// them, each with a cabinet, a treasury and a seat at the table, and the only
    /// violence any of them could do was declared, attributed and scored with a
    /// verdict. There was no way to bleed a rival without becoming a belligerent —
    /// which is the single most characteristic instrument of the era this game is
    /// set in, and the one thing an intelligence service is actually for.
    ///
    /// The pieces were all here and none of them met. `PopularResistance` existed
    /// as a *defence model number*: occupied ground resisted when you attacked it
    /// and did nothing in the months between. `pacification` was a counter with
    /// one producer. `publicGrievance` accrued over decades and fed unrest that
    /// fed conspiracy that ended governments — but only ever *its own*
    /// government, never anybody's ground.
    ///
    /// Five rules make this a system rather than a spawn button, and each of them
    /// is a lesson this codebase has already paid for:
    ///
    /// 1. **Nobody creates an insurgency.** They arise from conditions the
    ///    simulation already computes — foreign troops on ground that is not
    ///    theirs, hardship nobody is answering, a region that does not consider
    ///    itself part of this state. A sponsor finds one and arms it. That is a
    ///    different verb from making one, and it is why a stable, contented,
    ///    ungarrisoned country **cannot be destabilised by clicking** — the same
    ///    rule that governs `RegimeSystem`.
    /// 2. **Support is a target, not a store.** `support` drifts toward whatever
    ///    the conditions on that ground will hold, so repairing the conditions
    ///    genuinely ends it and no value here ratchets. This is the trap that ate
    ///    approval, war exhaustion, manpower, energy, food and doctrine
    ///    familiarity before it; it is not eating this.
    /// 3. **Deniability is a wasting asset.** Every shipment is a chance to be
    ///    traced and a long programme is a programme that will be found. When it
    ///    is, the cost is *standing* — relations, trust, memory, threat — never a
    ///    pillar, for the reason `IntelligenceSystem`'s exposure block spells out
    ///    at length: a cost with no recovery path under the conditions that cause
    ///    it is a disqualification rather than a price.
    /// 4. **Actor-generic, and reachable.** `SupportBy` takes an actorId and
    ///    `ConsiderSponsorship` runs for every AI government, priced in treasury
    ///    and Political Capital. A verb the world can call but never will is this
    ///    codebase's most-repeated bug wearing a new coat.
    /// 5. **It never hands the sponsor the ground.** An occupation insurgency
    ///    that wins *liberates* the province to whoever it belonged to. If arming
    ///    a movement were a cheap route to annexation it would simply be a better
    ///    war, and the sponsor's actual reward is the right one: a rival who
    ///    spent four years and a garrison holding something they no longer have.
    ///
    /// Countries are still created in exactly one place. A separatist movement
    /// that wins does not found a state here — it drives the conditions
    /// `SecessionSystem` reads, and that system does what it has always done.
    /// </summary>
    public static class InsurgencySystem
    {
        // ---------- constants ----------

        /// <summary>Strength at which a movement denies the holder the ground's value.</summary>
        public const float ContestThreshold = 50f;

        /// <summary>Strength and support at which it can take the ground outright.</summary>
        public const float VictoryStrength = 88f;
        public const float VictorySupport = 70f;

        /// <summary>Below this, with nothing feeding it, it stops being a movement.</summary>
        public const float FadeThreshold = 8f;
        public const int FadeMonths = 6;

        /// <summary>Command capacity for the player to open or step up a channel.</summary>
        public const int SupportCost = 2;

        /// <summary>Political Capital an AI government pays for the same decision.</summary>
        public const float SponsorPoliticalCost = 1.5f;

        /// <summary>Treasury one shipment costs the sponsor.</summary>
        public const float ShipmentTreasury = 420f;

        /// <summary>Treasury a full-strength insurgency costs its holder per month (see <c>Bill</c>).</summary>
        public const float InsurgencyBillPerMonth = 24f;

        /// <summary>
        /// Ground-force supply one shipment costs the sponsor.
        ///
        /// Arms come out of somebody's stocks. Charging only money would make
        /// this the one military verb with no military cost, and a large power
        /// could run six programmes off its budget surplus without noticing.
        /// </summary>
        public const float ShipmentSupplyCost = 1.6f;

        // ---------- lookups ----------

        public static Insurgency Find(GameState state, string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < state.insurgencies.Count; i++)
                if (state.insurgencies[i].id == id) return state.insurgencies[i];
            return null;
        }

        public static Insurgency At(GameState state, string locationId)
        {
            if (string.IsNullOrEmpty(locationId)) return null;
            for (int i = 0; i < state.insurgencies.Count; i++)
                if (state.insurgencies[i].locationId == locationId) return state.insurgencies[i];
            return null;
        }

        /// <summary>Every movement currently fighting this country, on any of its ground.</summary>
        public static List<Insurgency> Against(GameState state, string countryId)
        {
            var list = new List<Insurgency>();
            foreach (var insurgency in state.insurgencies)
            {
                var location = state.FindLocation(insurgency.locationId);
                if (location != null && location.ownerId == countryId) list.Add(insurgency);
            }
            return list;
        }

        /// <summary>Every movement this country is arming.</summary>
        public static List<Insurgency> SponsoredBy(GameState state, string sponsorId)
        {
            var list = new List<Insurgency>();
            foreach (var insurgency in state.insurgencies)
                if (insurgency.sponsorId == sponsorId) list.Add(insurgency);
            return list;
        }

        /// <summary>Who this movement is currently shooting at. The ground decides.</summary>
        public static string TargetOf(GameState state, Insurgency insurgency)
        {
            var location = state.FindLocation(insurgency?.locationId);
            return location == null ? "" : location.ownerId;
        }

        /// <summary>
        /// Total insurgent strength this country is fighting on its own ground.
        /// </summary>
        public static float PressureOn(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var insurgency in state.insurgencies)
            {
                var location = state.FindLocation(insurgency.locationId);
                if (location != null && location.ownerId == countryId) total += insurgency.strength;
            }
            return total;
        }

        /// <summary>
        /// How much of the force a standing insurgency ties down, as a shift in
        /// the readiness and sustainment **targets**.
        ///
        /// Read by `MilitarySystem.MonthlyUpkeep` beside the occupation drag, and
        /// for the identical reason: readiness and supply drift back to target
        /// every month, so a flat monthly subtraction is erased before it can be
        /// felt. Capped, so a state fighting three risings is degraded rather
        /// than disarmed.
        /// </summary>
        public static float ForceDrag(GameState state, string countryId)
            => Math.Min(18f, PressureOn(state, countryId) * 0.09f);

        /// <summary>
        /// What an armed movement does to how stable the country can be, as a
        /// shift in the stability **target**. Same rule, same reason.
        /// </summary>
        public static float StabilityDrag(GameState state, string countryId)
            => Math.Min(16f, PressureOn(state, countryId) * 0.08f);

        /// <summary>
        /// What it adds to the pressure behind organised unrest. Fed into
        /// `GovernmentSystem`'s unrest target — people shooting at the government
        /// somewhere in the country is not a mood, and it does not stay local.
        /// </summary>
        public static float UnrestPressure(GameState state, string countryId)
            => Math.Min(14f, PressureOn(state, countryId) * 0.07f);

        /// <summary>
        /// True when this movement is strong enough that the holder draws nothing
        /// from the ground. Read by `TerritorySystem`, so a contested province
        /// stops paying its energy, industry, materials and trade access.
        /// </summary>
        public static bool Denies(GameState state, StrategicLocation location)
        {
            if (location == null) return false;
            var insurgency = At(state, location.id);
            return insurgency != null && insurgency.strength >= ContestThreshold;
        }

        /// <summary>
        /// What an observer may say about who is behind it.
        ///
        /// Sponsorship is secret until it is exposed — with one exception that is
        /// not really one: the sponsor knows perfectly well what it is doing.
        /// </summary>
        public static bool KnownSponsor(GameState state, string observerId, Insurgency insurgency)
        {
            if (insurgency == null || !insurgency.HasSponsor) return false;
            if (insurgency.sponsorExposed) return true;
            return insurgency.sponsorId == observerId;
        }

        // ---------- what the ground will hold ----------

        /// <summary>
        /// The level of support the conditions on this ground can sustain.
        ///
        /// **This is the whole model.** Every effect below moves `support` toward
        /// this number rather than adding to it, so improving the conditions is a
        /// real answer and a movement cannot outlive its cause.
        /// </summary>
        public static float SupportTargetFor(GameState state, Insurgency insurgency)
        {
            var location = state.FindLocation(insurgency.locationId);
            if (location == null) return 0f;

            var holder = state.FindCountry(location.ownerId);
            if (holder == null) return 0f;

            float target = 0f;

            switch (insurgency.cause)
            {
                case InsurgencyCause.Occupation:
                    // Only while somebody else is sitting on it. Recovering your
                    // own ground is not an occupation, so the cause evaporates and
                    // the movement with it.
                    if (!location.IsOccupied) return 0f;
                    target = 34f + Math.Max(0f, 62f - location.pacification) * 0.62f;
                    break;

                case InsurgencyCause.Deprivation:
                    target = Math.Max(0f, 48f - holder.livingStandards) * 0.95f
                           + holder.publicGrievance * 0.32f
                           + holder.socialUnrest * 0.22f
                           - 12f;
                    break;

                default: // Separatism
                    target = Math.Max(0f, 52f - holder.nationalUnity) * 1.05f
                           + holder.publicGrievance * 0.18f
                           - 6f;
                    break;
            }

            // A sponsor buys reach, organisation and a reason to believe somebody
            // is coming — but it cannot manufacture a grievance that is not there.
            // Deliberately a **multiplier on an existing cause**, exactly as
            // `RegimeSystem` prices foreign subversion, so a contented province
            // stays contented however much money is pushed at it.
            if (insurgency.HasSponsor && target > 0f)
                target *= 1.28f;

            return Clamp(target);
        }

        /// <summary>
        /// What a movement with this much backing can grow into. Sponsorship
        /// raises the ceiling; the garrison holds it down.
        /// </summary>
        public static float StrengthCeilingFor(GameState state, Insurgency insurgency)
        {
            var location = state.FindLocation(insurgency.locationId);
            if (location == null) return 0f;

            float ceiling = insurgency.support * 0.92f;
            if (insurgency.HasSponsor) ceiling += 14f + Math.Min(18f, insurgency.armsSupplied * 0.05f);

            // A real garrison caps what can be organised in the open. It does not
            // remove the movement — that is what the support model is for.
            ceiling -= location.garrison * 0.28f;

            return Clamp(ceiling);
        }

        // ---------- arming one ----------

        public static bool CanSupport(GameState state, string actorId, Insurgency insurgency,
            out string reason)
        {
            if (insurgency == null) { reason = "No such movement."; return false; }

            var actor = state.FindCountry(actorId);
            if (actor == null) { reason = "No such state."; return false; }

            string targetId = TargetOf(state, insurgency);
            if (string.IsNullOrEmpty(targetId)) { reason = "The ground has no holder."; return false; }
            if (targetId == actorId)
            {
                reason = "WE HOLD THAT GROUND — arming them would be arming our own enemy.";
                return false;
            }

            if (insurgency.HasSponsor && insurgency.sponsorId != actorId)
            {
                reason = "SOMEBODY IS ALREADY RUNNING THEM. A second quartermaster would "
                       + "be a second security problem, and they know it.";
                return false;
            }

            if (actor.resources.treasury < ShipmentTreasury)
            {
                reason = $"THE TREASURY CANNOT COVER IT — {ShipmentTreasury:F0} a shipment.";
                return false;
            }

            if (actor.military.ground.supply < ShipmentSupplyCost * 3f)
            {
                reason = "OUR OWN STOCKS ARE TOO THIN. Weapons sent are weapons we do not have.";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>
        /// Actor-generic. Ships one consignment: money, equipment out of our own
        /// stocks, and a little more of the deniability we started with.
        /// </summary>
        public static bool SupportBy(GameState state, string actorId, Insurgency insurgency)
        {
            if (!CanSupport(state, actorId, insurgency, out _)) return false;

            var actor = state.FindCountry(actorId);
            var location = state.FindLocation(insurgency.locationId);
            var target = state.FindCountry(TargetOf(state, insurgency));
            if (actor == null || location == null || target == null) return false;

            bool opening = !insurgency.HasSponsor;

            actor.resources.treasury -= ShipmentTreasury;
            actor.military.ground.supply = Clamp(actor.military.ground.supply - ShipmentSupplyCost);

            insurgency.sponsorId = actorId;
            insurgency.armsSupplied += 10f;
            insurgency.strength = Clamp(insurgency.strength + 6f);

            // Being armed is itself an argument: a movement that can protect the
            // people who join it recruits differently from one that cannot.
            insurgency.support = Clamp(insurgency.support + 2.5f);

            // Deniability spends down faster the harder the target is looking.
            insurgency.exposure = Clamp(insurgency.exposure
                + 6f + target.counterIntel.counterIntelligence * 0.045f);

            if (actor.isPlayer)
                state.AddNotification(NotificationClass.Advisory,
                    opening ? "CHANNEL OPENED" : "CONSIGNMENT DELIVERED",
                    $"{location.displayName} — the movement there has our equipment. "
                    + $"Nothing carries our marking. Attribution risk {insurgency.exposure:F0}.",
                    target.id, desk: ReportingDesk.Intelligence);

            state.AddChronicle(ChronicleCategory.Intelligence, actorId,
                $"{actor.displayName} armed the movement at {location.displayName}.",
                Publicity.Secret);

            return true;
        }

        /// <summary>Player order: spends CP, records the initiative.</summary>
        public static bool Support(GameState state, TurnManager turns, Insurgency insurgency)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Intelligence)) return false;
            if (!CanSupport(state, state.playerCountryId, insurgency, out string reason))
            {
                GameLog.Warn("INTEL", reason);
                return false;
            }

            if (!turns.SpendCommandPoints(SupportCost, "Support an insurgency")) return false;
            if (!SupportBy(state, state.playerCountryId, insurgency)) return false;

            ProgressionSystem.AwardXP(state, 18, "Supported an insurgency");
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        /// <summary>
        /// Stop. The movement keeps what it has been given and loses what it was
        /// promised — which is why walking away is a decision rather than a reset.
        /// </summary>
        public static bool WithdrawSupportBy(GameState state, string actorId, Insurgency insurgency)
        {
            if (insurgency == null || insurgency.sponsorId != actorId) return false;

            insurgency.sponsorId = "";
            insurgency.sponsorMonths = 0;

            // Exposure does not vanish with the programme. A shipment already
            // traced is already traced, and the file stays open — it decays with
            // the months like everything else here.
            var location = state.FindLocation(insurgency.locationId);
            var actor = state.FindCountry(actorId);
            if (actor != null && actor.isPlayer && location != null)
                state.AddNotification(NotificationClass.Advisory, "CHANNEL CLOSED",
                    $"We are no longer supplying {location.displayName}. What they hold, "
                    + "they keep.", desk: ReportingDesk.Intelligence);

            return true;
        }

        // ---------- the monthly tick ----------

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            Emerge(state, monthIndex);

            // A snapshot, because resolving one can transfer ground and, through
            // the secession pressure a separatist victory applies, reach the
            // country list. `RegimeSystem` learned this the expensive way.
            var live = new List<Insurgency>(state.insurgencies);
            foreach (var insurgency in live)
                Tick(state, insurgency, monthIndex);

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                ConsiderSponsorship(state, country, monthIndex);
            }
        }

        // ---------- where they come from ----------

        /// <summary>
        /// How much pressure this ground is under, and for what reason. Zero
        /// means nothing is wrong there — which is the normal case, and has to
        /// stay the normal case.
        /// </summary>
        public static float PressureAt(GameState state, StrategicLocation location,
            out InsurgencyCause cause)
        {
            cause = InsurgencyCause.Deprivation;
            if (location == null) return 0f;

            var holder = state.FindCountry(location.ownerId);
            if (holder == null) return 0f;

            float occupation = 0f;
            if (location.IsOccupied)
                occupation = 14f + Math.Max(0f, 60f - location.pacification) * 0.55f;

            float deprivation = Math.Max(0f, 42f - holder.livingStandards) * 0.75f
                              + Math.Max(0f, holder.socialUnrest - 40f) * 0.55f
                              + Math.Max(0f, holder.publicGrievance - 45f) * 0.30f;

            float separatism = location.type == LocationType.Capital
                ? 0f
                : Math.Max(0f, 42f - holder.nationalUnity) * 0.80f
                  + Math.Max(0f, holder.socialUnrest - 55f) * 0.25f;

            float best = occupation;
            cause = InsurgencyCause.Occupation;
            if (deprivation > best) { best = deprivation; cause = InsurgencyCause.Deprivation; }
            if (separatism > best) { best = separatism; cause = InsurgencyCause.Separatism; }

            return best;
        }

        static void Emerge(GameState state, int monthIndex)
        {
            foreach (var location in state.locations)
            {
                if (At(state, location.id) != null) continue;

                float pressure = PressureAt(state, location, out InsurgencyCause cause);
                if (pressure < 30f) continue;

                var rng = new Random(unchecked(
                    state.rngSeed * 22307 + monthIndex * 911 + Hash.Of(location.id)));

                // Deliberately slow. At severe pressure this is roughly one chance
                // in twenty a month, so an occupier has a year or two to make the
                // ground quiet before anyone organises — and an operator who does
                // nothing about a province they have taken will eventually meet
                // somebody who did.
                double chance = (pressure - 30f) / 1400.0;
                if (rng.NextDouble() >= chance) continue;

                Open(state, location, cause, seededSupport: 18f + (float)rng.NextDouble() * 14f);
            }
        }

        /// <summary>
        /// Bring a movement into being. Public, because tests and events have
        /// legitimate reasons to start one — but note that nothing in the *player's*
        /// hands calls it. You find an insurgency; you do not commission one.
        /// </summary>
        public static Insurgency Open(GameState state, StrategicLocation location,
            InsurgencyCause cause, float seededSupport)
        {
            if (location == null) return null;
            if (At(state, location.id) != null) return null;

            var insurgency = new Insurgency
            {
                id = $"INS_{location.id}_{state.date.SortKey}",
                locationId = location.id,
                cause = cause,
                began = state.date,
                support = Clamp(seededSupport),
                strength = Clamp(seededSupport * 0.35f)
            };
            state.insurgencies.Add(insurgency);

            var holder = state.FindCountry(location.ownerId);
            string what = Describe(cause);

            state.AddChronicle(ChronicleCategory.Military, location.ownerId,
                $"An armed movement has taken shape at {location.displayName} — {what}.",
                Publicity.Public);

            if (holder != null && holder.isPlayer)
                state.AddNotification(NotificationClass.Priority, "ARMED MOVEMENT",
                    $"{location.displayName}: an organised movement has appeared, {what}. "
                    + "Security operations will hold it down. Only answering the reason "
                    + "will end it.", location.ownerId, desk: ReportingDesk.Military);

            return insurgency;
        }

        public static string Describe(InsurgencyCause cause)
        {
            switch (cause)
            {
                case InsurgencyCause.Occupation:
                    return "against a foreign garrison on their ground";
                case InsurgencyCause.Separatism:
                    return "for separation from the state";
                default:
                    return "out of hardship nobody in the capital has answered";
            }
        }

        // ---------- one movement's month ----------

        static void Tick(GameState state, Insurgency insurgency, int monthIndex)
        {
            var location = state.FindLocation(insurgency.locationId);
            if (location == null) { state.insurgencies.Remove(insurgency); return; }

            var holder = state.FindCountry(location.ownerId);
            if (holder == null) { state.insurgencies.Remove(insurgency); return; }

            // 1. Support drifts to what the ground will hold. Both directions.
            float supportTarget = SupportTargetFor(state, insurgency);
            insurgency.support = Approach(insurgency.support, supportTarget,
                insurgency.support < supportTarget ? 0.10f : 0.07f);

            // 2. Strength follows support and whatever is being shipped in.
            float ceiling = StrengthCeilingFor(state, insurgency);
            insurgency.strength = Approach(insurgency.strength, ceiling,
                insurgency.strength < ceiling ? 0.12f : 0.16f);

            // 3. What it costs the holder to have it there.
            Bill(state, holder, location, insurgency);

            // 4. Sponsorship: it gets found, or it does not.
            if (insurgency.HasSponsor)
            {
                insurgency.sponsorMonths++;
                CheckAttribution(state, insurgency, location, holder, monthIndex);
            }
            else
            {
                // A closed file cools. Slowly — the accusation outlives the
                // programme, which is what makes running one a lasting decision.
                insurgency.exposure = Clamp(insurgency.exposure - 0.9f);
            }

            // 5. Does it win, and does it end?
            if (insurgency.strength >= VictoryStrength && insurgency.support >= VictorySupport
                && location.garrison < 30f)
            {
                Prevail(state, insurgency, location, holder);
                return;
            }

            if (insurgency.strength < FadeThreshold && insurgency.support < 20f)
            {
                insurgency.fadingMonths++;
                if (insurgency.fadingMonths >= FadeMonths) Fade(state, insurgency, location, holder);
            }
            else insurgency.fadingMonths = 0;
        }

        /// <summary>
        /// The standing bill. Note what it does **not** do: it never writes
        /// `pacification` upward or downward as a store the holder cannot move —
        /// the counter-insurgency verb raises it, this suppresses it, and both
        /// point at the same number so the operator's effort is legible.
        /// </summary>
        static void Bill(GameState state, CountryState holder, StrategicLocation location,
            Insurgency insurgency)
        {
            if (insurgency.strength < 1f) return;

            float intensity = insurgency.strength / 100f;

            // Only values that are genuine stores are written here. Garrison and
            // pacification are moved by orders rather than drifting to a computed
            // level, the treasury is a balance, and war exhaustion decays rather
            // than reverting. **Stability, readiness, supply and unrest are all
            // target-driven**, so this system pushes those through
            // `StabilityDrag`, `ForceDrag` and `UnrestPressure` instead — a flat
            // monthly subtraction on any of them would be erased by the same
            // month's drift, which is how occupation's readiness cost spent a
            // year as dead code.
            location.garrison = Clamp(location.garrison - intensity * 1.7f);
            if (location.IsOccupied)
                location.pacification = Clamp(location.pacification - intensity * 2.2f);

            // Scaled to what a treasury earns (`EconomySystem`: ~gdp × TreasuryIncomeRate a
            // month, 40–100 for the authored roster). A full-strength insurgency
            // costs roughly one month's income per month — a serious, open-ended
            // drain, not a bankruptcy. At 210 it was 6–12× income: Russia was
            // −11,000 by year ten of a passive decade with nobody choosing anything.
            holder.resources.treasury -= intensity * InsurgencyBillPerMonth;
            holder.warExhaustion = Clamp(holder.warExhaustion + intensity * 0.24f);

            if (insurgency.cause == InsurgencyCause.Separatism)
                holder.nationalUnity = Clamp(holder.nationalUnity - intensity * 0.22f);
        }

        // ---------- being found out ----------

        static void CheckAttribution(GameState state, Insurgency insurgency,
            StrategicLocation location, CountryState holder, int monthIndex)
        {
            if (insurgency.sponsorExposed) return;

            var sponsor = state.FindCountry(insurgency.sponsorId);
            if (sponsor == null) { insurgency.sponsorId = ""; return; }

            // Running one is itself a footprint, whether or not anything shipped
            // this month. A programme that stands still still gets found.
            insurgency.exposure = Clamp(insurgency.exposure
                + 0.6f + holder.counterIntel.counterIntelligence * 0.012f);

            var rng = new Random(unchecked(
                state.rngSeed * 15731 + monthIndex * 6779 + Hash.Of(insurgency.id)));

            double chance = insurgency.exposure / 100.0 * 0.09
                            + holder.counterIntel.counterIntelligence / 4000.0;
            if (rng.NextDouble() >= chance) return;

            Attribute(state, insurgency, location, holder, sponsor);
        }

        /// <summary>
        /// The bill for being caught. Standing, exactly as with a blown covert
        /// operation — and larger, because arming people who are shooting at
        /// somebody's soldiers is not the same as reading their mail.
        /// </summary>
        public static void Attribute(GameState state, Insurgency insurgency,
            StrategicLocation location, CountryState holder, CountryState sponsor)
        {
            insurgency.sponsorExposed = true;
            insurgency.exposure = 100f;

            var relationship = state.FindRelationship(sponsor.id, holder.id);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations - 22f);
                relationship.trust = Clamp(relationship.trust - 26f);
                relationship.SetThreatPerceivedBy(holder.id,
                    Clamp(relationship.ThreatPerceivedBy(holder.id) + 18f));
                relationship.AddMemory(state.date,
                    $"Armed the movement at {location.displayName} against us.", 1.8f);
            }

            // Everyone else prices it in too. Arming an insurgency in somebody
            // else's country is the kind of thing other governments file away
            // about you, and it is smaller than the injury to the state it was
            // done to but it is not nothing.
            foreach (var other in state.relationships)
            {
                if (other == relationship) continue;
                if (!other.Involves(sponsor.id)) continue;
                other.trust = Clamp(other.trust - 3.5f);
            }

            state.AddChronicle(ChronicleCategory.Intelligence, sponsor.id,
                $"{sponsor.displayName} was found to be arming the movement at "
                + $"{location.displayName}.", Publicity.Public);

            if (sponsor.isPlayer)
                state.AddNotification(NotificationClass.Priority, "SPONSORSHIP ATTRIBUTED",
                    $"{holder.displayName} has traced the weapons at {location.displayName} to us "
                    + "and said so publicly. The movement is still there; the deniability is not.",
                    holder.id, desk: ReportingDesk.Intelligence);

            if (holder.isPlayer)
                state.AddNotification(NotificationClass.Flash, "FOREIGN HAND",
                    $"The movement at {location.displayName} is being armed by "
                    + $"{sponsor.displayName}. That is now a matter between governments.",
                    sponsor.id, desk: ReportingDesk.Command);
        }

        // ---------- endings ----------

        /// <summary>
        /// It wins. What that means depends entirely on what it wanted, which is
        /// the reason `cause` is stored rather than inferred.
        /// </summary>
        static void Prevail(GameState state, Insurgency insurgency,
            StrategicLocation location, CountryState holder)
        {
            var sponsor = state.FindCountry(insurgency.sponsorId);

            switch (insurgency.cause)
            {
                case InsurgencyCause.Occupation:
                {
                    // The province goes back to whoever it belonged to — **never**
                    // to the sponsor. If arming a movement were a cheap route to
                    // annexation it would simply be a better war.
                    var restored = state.FindCountry(location.originalOwnerId);
                    location.ownerId = location.originalOwnerId;
                    location.garrison = 22f;
                    location.pacification = 0f;

                    holder.warExhaustion = Clamp(holder.warExhaustion + 9f);
                    holder.warSupport = Clamp(holder.warSupport - 11f);
                    holder.governmentApproval = Clamp(holder.governmentApproval - 6f);

                    state.AddChronicle(ChronicleCategory.Military, holder.id,
                        $"{holder.displayName} has lost control of {location.displayName}; "
                        + $"the ground is back in {(restored != null ? restored.displayName : "local")} hands.",
                        Publicity.Public);

                    if (holder.isPlayer)
                        state.AddNotification(NotificationClass.Priority, "POSITION LOST",
                            $"{location.displayName} is no longer ours to hold. The garrison "
                            + "could not stay.", holder.id, desk: ReportingDesk.Military);

                    if (restored != null && restored.isPlayer)
                        state.AddNotification(NotificationClass.Priority, "GROUND RECOVERED",
                            $"{location.displayName} is ours again. Not one of our soldiers "
                            + "took it back.", restored.id, desk: ReportingDesk.Military);

                    if (sponsor != null && sponsor.isPlayer)
                        state.AddNotification(NotificationClass.Priority, "THE GARRISON HAS GONE",
                            $"{holder.displayName} has given up {location.displayName}. We have "
                            + "not fired a shot and we do not hold it either — which was the point.",
                            holder.id, desk: ReportingDesk.Intelligence);
                    break;
                }

                case InsurgencyCause.Separatism:
                {
                    // Countries are created in exactly one place. This drives the
                    // conditions `SecessionSystem` reads and lets that system do
                    // what it has always done.
                    holder.nationalUnity = Clamp(holder.nationalUnity - 18f);
                    holder.stability = Clamp(holder.stability - 12f);
                    holder.government.conspiracyLevel =
                        Clamp(holder.government.conspiracyLevel + 10f);
                    location.garrison = Clamp(location.garrison - 25f);

                    state.AddChronicle(ChronicleCategory.Political, holder.id,
                        $"{location.displayName} is out of {holder.displayName}'s hands in all "
                        + "but name.", Publicity.Public);

                    if (holder.isPlayer)
                        state.AddNotification(NotificationClass.Flash, "REGION OUT OF CONTROL",
                            $"{location.displayName} answers to its own people now. The state "
                            + "has not formally lost it, and that distinction is wearing thin.",
                            holder.id, desk: ReportingDesk.Command);
                    break;
                }

                default:
                {
                    // It made its point. A government that is forced to answer a
                    // deprivation rising has answered it — badly, late and at a
                    // price, which is what the grievance model is for.
                    holder.governmentApproval = Clamp(holder.governmentApproval - 12f);
                    holder.stability = Clamp(holder.stability - 9f);
                    holder.government.legislativeSupport =
                        Clamp(holder.government.legislativeSupport - 8f);
                    holder.publicGrievance = Clamp(holder.publicGrievance + 6f);

                    state.AddChronicle(ChronicleCategory.Political, holder.id,
                        $"The rising at {location.displayName} forced {holder.displayName}'s "
                        + "hand.", Publicity.Public);

                    if (holder.isPlayer)
                        state.AddNotification(NotificationClass.Priority, "THE RISING PREVAILS",
                            $"{location.displayName} has extracted what it wanted. It cost this "
                            + "government more than answering it would have.",
                            holder.id, desk: ReportingDesk.Government);
                    break;
                }
            }

            state.insurgencies.Remove(insurgency);
        }

        /// <summary>It stops. Nobody signs anything; the fighting simply ends.</summary>
        static void Fade(GameState state, Insurgency insurgency,
            StrategicLocation location, CountryState holder)
        {
            state.insurgencies.Remove(insurgency);

            state.AddChronicle(ChronicleCategory.Military, holder.id,
                $"The movement at {location.displayName} has come to nothing.", Publicity.Public);

            if (holder.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "MOVEMENT DISPERSED",
                    $"{location.displayName} is quiet. Whether it stays quiet is a different "
                    + "question and a longer one.", holder.id, desk: ReportingDesk.Military);

            var sponsor = state.FindCountry(insurgency.sponsorId);
            if (sponsor != null && sponsor.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "CHANNEL CLOSED BY EVENTS",
                    $"There is nobody left at {location.displayName} to supply.",
                    desk: ReportingDesk.Intelligence);
        }

        // ---------- the counter-side hook ----------

        /// <summary>
        /// What a counter-insurgency operation does to the movement itself.
        ///
        /// Called from `MilitarySystem` when that verb resolves. Without it the
        /// verb would raise `pacification` — a number the movement reads — and
        /// leave the fighters untouched, which is the "written but never read"
        /// bug one layer down: an operation that appears to work and changes
        /// nothing the operator was aiming at.
        /// </summary>
        public static void OnCounterInsurgency(GameState state, StrategicLocation location,
            float effectiveness)
        {
            var insurgency = At(state, location?.id);
            if (insurgency == null) return;

            insurgency.strength = Clamp(insurgency.strength - 9f * Clamp01(effectiveness));

            // Security operations are not popular with the people they are
            // conducted among. Small, and it is why a purely military answer
            // holds the ground without ever ending the problem.
            insurgency.support = Clamp(insurgency.support + 1.4f);
        }

        // ---------- the world's own hand ----------

        /// <summary>
        /// Whether a government reaches for this, and against whom.
        ///
        /// Placed here rather than in `AISystem`'s objective scoring on the
        /// `ConsiderDetente` precedent: a standing covert programme is not a
        /// strategy competing with the others for this month's action budget, it
        /// is something a government keeps running in the background. Putting it
        /// in the budget would have it lose the cut to war objectives forever,
        /// which is exactly how routine restocking went silent for thirty years.
        /// </summary>
        static void ConsiderSponsorship(GameState state, CountryState country, int monthIndex)
        {
            if (state.insurgencies.Count == 0) return;
            if (country.resources.treasury < ShipmentTreasury + AISystem.DiscretionaryReserve) return;

            var rng = new Random(unchecked(
                state.rngSeed * 3187 + monthIndex * 4409 + Hash.Of(country.id)));

            Insurgency best = null;
            float bestScore = 0f;
            string bestTarget = null;

            foreach (var insurgency in state.insurgencies)
            {
                if (insurgency.HasSponsor && insurgency.sponsorId != country.id) continue;

                string targetId = TargetOf(state, insurgency);
                if (string.IsNullOrEmpty(targetId) || targetId == country.id) continue;

                var relationship = state.FindRelationship(country.id, targetId);
                if (relationship == null) continue;

                // You arm somebody's enemies because they are your problem, not
                // because a movement happens to exist. Hostility is the whole
                // qualification, and it has to be real hostility.
                float hostility = Math.Max(0f, 40f - relationship.relations)
                                + Math.Max(0f, relationship.ThreatPerceivedBy(country.id) - 45f) * 0.8f;
                if (hostility < 12f) continue;

                // A movement with nothing behind it is money into a hole.
                float viability = insurgency.support * 0.5f + insurgency.strength * 0.3f;
                if (viability < 12f) continue;

                float score = hostility + viability;
                if (insurgency.sponsorId == country.id) score += 20f;   // keep what we started
                if (score <= bestScore) continue;

                bestScore = score;
                best = insurgency;
                bestTarget = targetId;
            }

            if (best == null || bestTarget == null) return;

            // Not every month, or every hostile pair in the world runs a pipeline.
            if (rng.NextDouble() > 0.22) return;

            if (!GovernmentSystem.SpendPoliticalCapitalBy(
                    state, country.id, SponsorPoliticalCost, "Sponsor a movement")) return;

            SupportBy(state, country.id, best);
        }

        static float Approach(float current, float target, float rate)
            => Clamp(current + (target - current) * rate);

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

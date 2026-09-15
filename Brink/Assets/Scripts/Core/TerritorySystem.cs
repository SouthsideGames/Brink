using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// What holding ground is actually worth, and what it costs to hold
    /// (GDD §16, §19).
    ///
    /// Locations previously existed only as operation targets: nothing outside
    /// the annual evaluation read <c>ownerId</c>, so capturing an energy region
    /// yielded no energy and an industrial centre yielded no industry. War could
    /// therefore never pay for itself, which is the real reason a military
    /// playstyle looked like a losing strategy.
    ///
    /// Territory now feeds the resource model both ways — you gain what you take
    /// and lose what is taken from you — and occupation carries a standing bill
    /// in treasury, unrest and reputation, so conquest is a trade rather than
    /// free income.
    /// </summary>
    public static class TerritorySystem
    {
        /// <summary>Monthly treasury cost per point of occupied strategic value.</summary>
        public const float OccupationUpkeepPerValue = 0.55f;

        // ---------- what territory yields ----------

        /// <summary>
        /// Net strategic value of one location type held against what this country
        /// started with. Positive means conquest; negative means loss.
        /// </summary>
        public static float Swing(GameState state, string countryId, LocationType type)
        {
            float held = 0f, original = 0f;
            foreach (var location in state.locations)
            {
                if (location.type != type) continue;

                // **Ground in open revolt pays nobody.** A province with a serious
                // armed movement in it still has an owner on the map and produces
                // nothing for them — which is what makes arming one a way to
                // deny a rival an oilfield without taking it, and what makes a
                // rising at home an economic event rather than a security one.
                //
                // Deliberately symmetric and deliberately in `held` only: the
                // original owner keeps counting it in `original`, so a contested
                // province is a loss to whoever it belongs to *and* to whoever is
                // sitting on it. Nobody profits from a place that is fighting.
                bool contested = InsurgencySystem.Denies(state, location);

                if (location.ownerId == countryId && !contested) held += location.strategicValue;
                if (location.originalOwnerId == countryId) original += location.strategicValue;
            }
            return held - original;
        }

        /// <summary>Energy the country's held energy regions add to (or subtract from) its position.</summary>
        public static float EnergySwing(GameState state, string countryId)
            => Swing(state, countryId, LocationType.EnergyRegion) * 0.28f;

        /// <summary>Industrial capacity from held industrial centres.</summary>
        public static float IndustrySwing(GameState state, string countryId)
            => Swing(state, countryId, LocationType.IndustrialCenter) * 0.25f;

        /// <summary>
        /// Strategic materials from held mining and extraction regions.
        ///
        /// The commodity industry, procurement and every research programme draw
        /// on, and until `MaterialsRegion` existed there was nowhere on the map
        /// that produced it — so a state could be starved of materials with no
        /// ground it could take to fix that.
        /// </summary>
        public static float MaterialsSwing(GameState state, string countryId)
            => Swing(state, countryId, LocationType.MaterialsRegion) * 0.26f;

        /// <summary>
        /// Trade access from ports and chokepoints. Holding the sea lanes is
        /// worth something to the economy whether or not anyone is fighting.
        /// </summary>
        public static float TradeAccessSwing(GameState state, string countryId)
            => (Swing(state, countryId, LocationType.Port) * 0.20f)
               + (Swing(state, countryId, LocationType.Chokepoint) * 0.14f);

        /// <summary>
        /// Reach: the readiness a force can be sustained at, because basing is
        /// what lets a military be somewhere.
        ///
        /// Counts ground we hold *and* ground we operate from by agreement. A
        /// partner's airbase is reach we did not have to conquer — which is the
        /// entire strategic point of a Transit commitment, and reading ownership
        /// alone meant basing rights bought nothing at all.
        /// </summary>
        public static float ProjectionSwing(GameState state, string countryId)
            => (Swing(state, countryId, LocationType.Airbase)
                + HostedProjection(state, countryId)) * 0.18f;

        /// <summary>
        /// Strategic value of foreign installations we operate from by agreement.
        /// Worth less than our own: a host can withdraw the right, and does the
        /// moment the relationship sours (spec 04).
        /// </summary>
        public static float HostedProjection(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var location in state.locations)
            {
                if (location.foreignOperatorId != countryId) continue;
                if (location.ownerId == countryId) continue;
                total += location.strategicValue * 0.6f;
            }
            return total;
        }

        /// <summary>Defensive depth from held passes.</summary>
        public static float DefensiveDepth(GameState state, string countryId)
            => Swing(state, countryId, LocationType.MountainPass) * 0.15f;

        /// <summary>
        /// Total strategic value this country holds that was not originally its
        /// own — the occupation it is paying to keep.
        /// </summary>
        public static float OccupiedValue(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var location in state.locations)
            {
                if (location.ownerId != countryId) continue;
                if (!location.IsOccupied) continue;
                total += location.strategicValue;
            }
            return total;
        }

        /// <summary>Strategic value of this country's own ground currently held by someone else.</summary>
        public static float LostValue(GameState state, string countryId)
        {
            float total = 0f;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != countryId) continue;
                if (location.ownerId == countryId) continue;
                total += location.strategicValue;
            }
            return total;
        }

        // ---------- what it costs to hold ----------

        /// <summary>
        /// Occupation is not free income. Garrisons cost money, occupied
        /// populations do not consent, and the world does not forget who is
        /// sitting on whose ground.
        /// </summary>
        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
            {
                float occupied = OccupiedValue(state, country.id);
                float lost = LostValue(state, country.id);

                if (lost > 0f)
                {
                    // A country under occupation is a country with a grievance.
                    country.nationalUnity = Clamp(country.nationalUnity - lost * 0.010f);
                    country.warSupport = Clamp(country.warSupport + lost * 0.012f);
                    country.governmentApproval = Clamp(country.governmentApproval - lost * 0.008f);
                }

                if (occupied <= 0f) continue;

                country.resources.treasury -= occupied * OccupationUpkeepPerValue;

                // Holding hostile ground wears at home. The readiness cost lives
                // in MilitarySystem.MonthlyUpkeep as a shift in the *target*:
                // subtracting it here was undone by the same tick's drift back
                // toward target, so it was a cost that never actually arrived.
                country.stability = Clamp(country.stability - occupied * 0.006f);
                Causal.Apply(state, country.id, CausalMetric.WarExhaustion,
                    CausalReason.Occupation, ref country.warExhaustion,
                    Clamp(country.warExhaustion + occupied * 0.008f), CausalCategory.Military);
            }
        }

        /// <summary>
        /// Transfer a location by treaty: the new owner's title is **recognised**,
        /// not merely held.
        ///
        /// `originalOwnerId` was written once at world creation and never again,
        /// so ground handed over in a signed settlement stayed `IsOccupied`
        /// forever. The consequences compounded badly: the new owner paid
        /// occupation upkeep, stability and war-exhaustion drag in perpetuity;
        /// the ceding state kept a permanent monthly grievance; `StrategicPressure`
        /// counted it as their lost ground for the rest of the save; and
        /// `UpdateBasingRights` barred a ceded port or airbase from ever hosting
        /// an ally. Conquest became a standing liability rather than a gain,
        /// which is the opposite of what GDD §16 asks for.
        ///
        /// Occupation is what you hold by force; cession is what the world has
        /// accepted. Only the latter clears the grievance.
        /// </summary>
        public static void Cede(GameState state, StrategicLocation location, string newOwnerId)
        {
            if (location == null || string.IsNullOrEmpty(newOwnerId)) return;

            location.ownerId = newOwnerId;
            location.originalOwnerId = newOwnerId;

            // A recognised transfer ends any foreign basing arrangement that ran
            // through the previous sovereign.
            location.foreignOperatorId = "";

            state.AddChronicle(ChronicleCategory.Diplomatic, newOwnerId,
                $"{location.displayName} formally transferred.", Publicity.Public);
        }

        /// <summary>
        /// Standing reputational cost of occupation, applied when a location
        /// changes hands rather than every month — the world reacts to the act.
        /// </summary>
        public static void RecordSeizure(GameState state, StrategicLocation location, string seizerId)
        {
            var seizer = state.FindCountry(seizerId);
            if (seizer == null) return;

            // Recovering your own ground is not an annexation.
            bool restoring = location.originalOwnerId == seizerId;
            if (restoring) return;

            seizer.pillars.diplomacy = Clamp(seizer.pillars.diplomacy - location.strategicValue * 0.04f);

            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(seizerId)) continue;
                string other = relationship.PartnerOf(seizerId);
                relationship.SetThreatPerceivedBy(other,
                    Clamp(relationship.ThreatPerceivedBy(other) + location.strategicValue * 0.06f));
                relationship.AddMemory(state.date,
                    $"Seized {location.displayName}", -location.strategicValue * 0.03f);
            }
        }

        // ---------- leaving: the post-war exit (C5, spec 01 §3c) ----------
        //
        // Captured ground used to have no way out once its war was over. A
        // settlement cedes only the objective, `Finish` never touches ground, and
        // `Withdraw` is an operation that needs a live confrontation — so every
        // other position taken in a war stayed occupied, billing its holder
        // upkeep and insurgency for as long as the save ran. Measured before this
        // existed: 92% of all occupation upkeep in an unattended world was paid
        // after the war that took the ground had ended, and it was the largest
        // single reason restructured governments defaulted again.
        //
        // This is a **decision**, not a release: nothing here happens on a timer.
        // The holder chooses to leave (the player through GameController, a
        // foreign government through `AISystem.ConsiderRelinquishment`), and the
        // same gate and the same consequences apply to both.

        /// <summary>Command Points the operator spends to relinquish a position — the price of a Withdraw order.</summary>
        public const int RelinquishCost = 1;

        /// <summary>
        /// Months of the whole holding bill a government wants in hand before it
        /// is content to keep carrying occupied ground. The same three-year runway
        /// the operator's treasury warning uses (`AttentionSystem`).
        /// </summary>
        public const int HoldingRunwayMonths = 36;

        /// <summary>War support a voluntary withdrawal costs: the +6 a capture awarded (`MilitarySystem`), given back.</summary>
        public const float RelinquishWarSupportCost = 6f;

        /// <summary>Relationship memory written with the original owner — the weight `PeaceSystem`'s Withdrawal term records.</summary>
        public const float RelinquishMemoryWeight = 3f;

        /// <summary>
        /// A resource below this is a shortfall. The line `AISystem.ResourcePrize`
        /// and `AssertClaim` read when deciding whether a neighbour's ground is
        /// worth a war; restated here rather than shared so those decision paths
        /// are left exactly as they were.
        /// </summary>
        public const float ShortfallLine = 40f;

        /// <summary>
        /// What one occupied location costs its holder a month: upkeep on its
        /// strategic value, plus the bill for any armed movement on it — exactly
        /// what `MonthlyUpdate` and `InsurgencySystem`'s bill charge. Zero for
        /// ground that is not occupied.
        /// </summary>
        public static float HoldingBillFor(GameState state, StrategicLocation location)
        {
            if (state == null || location == null || !location.IsOccupied) return 0f;

            float bill = location.strategicValue * OccupationUpkeepPerValue;
            var movement = InsurgencySystem.At(state, location.id);
            if (movement != null && movement.strength >= 1f)
                bill += movement.strength / 100f * InsurgencySystem.InsurgencyBillPerMonth;
            return bill;
        }

        /// <summary>The whole monthly bill for every occupied location this country holds.</summary>
        public static float HoldingBill(GameState state, string countryId)
        {
            float total = 0f;
            if (state == null) return total;
            foreach (var location in state.locations)
                if (location.ownerId == countryId) total += HoldingBillFor(state, location);
            return total;
        }

        /// <summary>
        /// Whether this ground supplies what its holder is short of: an energy
        /// region to a state below the energy line, a mining region to one below
        /// the materials line. Says nothing about whether the ground is producing
        /// — a contested location yields nothing to anyone (see `Swing`).
        /// </summary>
        public static bool AnswersOwnShortfall(CountryState holder, StrategicLocation location)
        {
            if (holder == null || location == null) return false;
            return (holder.resources.energy < ShortfallLine && location.type == LocationType.EnergyRegion)
                   || (holder.resources.strategicMaterials < ShortfallLine
                       && location.type == LocationType.MaterialsRegion);
        }

        /// <summary>
        /// Whether <paramref name="actorId"/> may hand this location back now, and
        /// why not. One gate, shared by the operator's order, the Military screen
        /// and the AI, so what is offered and what is accepted cannot differ.
        ///
        /// While a confrontation between the holder and the original owner is
        /// still open the ground is part of that war, and wartime `Withdraw` is
        /// the verb for it.
        /// </summary>
        public static bool CanRelinquish(GameState state, string actorId, string locationId,
            out string reason)
        {
            reason = "";
            var location = state?.FindLocation(locationId);
            if (location == null) { reason = "NO SUCH POSITION."; return false; }
            if (string.IsNullOrEmpty(actorId) || location.ownerId != actorId)
            {
                reason = "WE DO NOT HOLD IT.";
                return false;
            }
            if (!location.IsOccupied)
            {
                reason = "THIS IS OUR OWN GROUND. There is nobody to hand it back to.";
                return false;
            }
            if (state.FindCountry(location.originalOwnerId) == null)
            {
                reason = "THERE IS NO GOVERNMENT LEFT TO HAND IT BACK TO.";
                return false;
            }
            if (ConfrontationSystem.ExistingBetween(state, actorId, location.originalOwnerId) != null)
            {
                reason = "THE WAR FOR IT IS STILL BEING FOUGHT. Withdraw from the front instead.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Hand occupied ground back to the state it was taken from.
        ///
        /// Control returns and the holder stops paying for it — its upkeep and any
        /// armed movement's bill both follow ownership, so nothing here has to
        /// cancel them. What it deliberately does **not** do: it is not a cession
        /// (`Cede` recognises title to a new owner, this restores the old one),
        /// not a seizure, and not an act of war — no confrontation, truce,
        /// sanction, escalation or threat perception is touched. An armed
        /// movement on the ground is left to fade by its own rules, as it does
        /// when ground is recovered any other way.
        /// </summary>
        public static bool RelinquishBy(GameState state, string actorId, string locationId)
        {
            if (!CanRelinquish(state, actorId, locationId, out _)) return false;

            var location = state.FindLocation(locationId);
            var holder = state.FindCountry(actorId);
            var owner = state.FindCountry(location.originalOwnerId);
            if (holder == null) return false;

            location.ownerId = location.originalOwnerId;
            location.garrison = Math.Max(20f, location.garrison);
            location.pacification = 0f;

            holder.warSupport = Clamp(holder.warSupport - RelinquishWarSupportCost);
            state.FindRelationship(actorId, owner.id)
                ?.AddMemory(state.date, "Returned occupied ground", RelinquishMemoryWeight);

            state.AddChronicle(ChronicleCategory.Military, actorId,
                $"{location.displayName} relinquished to {owner.displayName}.", Publicity.Public);

            if (actorId == state.playerCountryId)
                state.AddNotification(NotificationClass.Priority, "POSITION RELINQUISHED",
                    $"Our forces have left {location.displayName}. It is {owner.displayName}'s again, "
                    + "and so are its costs and whatever it produces.", owner.id,
                    desk: ReportingDesk.Military);
            else if (owner.id == state.playerCountryId)
                state.AddNotification(NotificationClass.Priority, "GROUND RETURNED",
                    $"{holder.displayName} has handed {location.displayName} back to us.",
                    holder.id, desk: ReportingDesk.Military);

            GameLog.Info("TERRITORY", $"{actorId} relinquished {location.id} to {owner.id}.");
            return true;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

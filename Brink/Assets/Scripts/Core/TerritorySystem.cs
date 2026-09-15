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


        // ---------- the post-war exit (C5) ----------

        /// <summary>
        /// Months of holding bill a treasury must be able to cover before a
        /// government treats its occupations as affordable.
        ///
        /// Three years: long enough that a state with real reserves is not
        /// pushed off ground it can plainly pay for, short enough that a
        /// treasury living hand to mouth stops pretending the garrison is free.
        /// </summary>
        public const int HoldingRunwayMonths = 36;

        /// <summary>
        /// Whether <paramref name="actorId"/> may hand this location back to the
        /// state it was taken from, and why not when it may not.
        ///
        /// **This is the exit that did not exist.** A settlement cedes only the
        /// objective, closing a confrontation releases nothing else that was
        /// captured, and `Withdraw` is an operation that needs a live war to be
        /// ordered in — so ground taken in a war that ended stayed occupied for
        /// the rest of the save, paying upkeep and insurgency costs forever with
        /// no reachable way to put it down.
        ///
        /// The last clause is the load-bearing one: while the shooting is still
        /// on, giving ground back is a battlefield decision and belongs to the
        /// operation verbs. This is for after.
        /// </summary>
        public static bool CanRelinquish(GameState state, string actorId, string locationId,
            out string reason)
        {
            reason = "";

            var location = state.FindLocation(locationId);
            if (location == null) { reason = "NO SUCH LOCATION."; return false; }

            if (location.ownerId != actorId) { reason = "WE DO NOT HOLD IT."; return false; }

            // Our own ground is not ours to return to anybody.
            if (!location.IsOccupied) { reason = "THIS IS OUR OWN GROUND."; return false; }

            var owner = state.FindCountry(location.originalOwnerId);
            if (owner == null)
            {
                reason = "THERE IS NO GOVERNMENT TO RETURN IT TO.";
                return false;
            }

            for (int i = 0; i < state.confrontations.Count; i++)
            {
                var confrontation = state.confrontations[i];
                if (confrontation.resolved) continue;
                if (!confrontation.Involves(actorId)) continue;
                if (!confrontation.Involves(location.originalOwnerId)) continue;
                reason = "THE WAR WITH " + owner.displayName.ToUpperInvariant() + " IS NOT OVER.";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Hand occupied ground back to the state it was taken from.
        ///
        /// Deliberately **not** <see cref="Cede"/>: a cession is a recognised
        /// transfer of title and rewrites `originalOwnerId`. This is the opposite
        /// act — the title never moved, and returning the ground simply stops us
        /// sitting on it. Nor does it call <see cref="RecordSeizure"/>, which
        /// prices taking ground rather than giving it up.
        ///
        /// What it costs is at home. A government that marches out of a place it
        /// spent soldiers taking has to explain itself, so war support falls; the
        /// state receiving its territory back remembers it, which is the only
        /// thing here that points outward.
        /// </summary>
        public static bool RelinquishBy(GameState state, string actorId, string locationId)
        {
            if (!CanRelinquish(state, actorId, locationId, out _)) return false;

            var location = state.FindLocation(locationId);
            var holder = state.FindCountry(actorId);
            var owner = state.FindCountry(location.originalOwnerId);

            location.ownerId = location.originalOwnerId;

            // A returning administration is not handed an empty province. The
            // floor matters because the ground is immediately eligible to be
            // fought over again, and a garrison of zero would make handing it
            // back indistinguishable from opening it.
            if (location.garrison < ReturnedGarrisonFloor)
                location.garrison = ReturnedGarrisonFloor;

            // Pacification measures how far an *occupier* has settled a hostile
            // population. There is no occupier now, so the figure has no subject.
            location.pacification = 0f;

            if (holder != null)
                holder.warSupport = Clamp(holder.warSupport - RelinquishWarSupportCost);

            var relationship = state.FindRelationship(actorId, location.originalOwnerId);
            if (relationship != null)
                relationship.AddMemory(state.date, "Returned occupied ground",
                    RelinquishMemoryWeight);

            state.AddChronicle(ChronicleCategory.Military, actorId,
                $"{(holder != null ? holder.displayName : actorId)} has returned "
                + $"{location.displayName} to {(owner != null ? owner.displayName : "its government")}.",
                Publicity.Public);

            if (holder != null && holder.isPlayer)
                state.AddNotification(NotificationClass.Priority, "GROUND RETURNED",
                    $"{location.displayName} is no longer ours to hold. The garrison has come "
                    + "home and the bill with it.", holder.id, desk: ReportingDesk.Military);

            if (owner != null && owner.isPlayer)
                state.AddNotification(NotificationClass.Priority, "TERRITORY RESTORED",
                    $"{location.displayName} is ours again. "
                    + $"{(holder != null ? holder.displayName : "The occupying force")} has withdrawn.",
                    owner.id, desk: ReportingDesk.Military);

            return true;
        }

        /// <summary>Garrison a returned province is left standing with.</summary>
        public const float ReturnedGarrisonFloor = 20f;

        /// <summary>War support the holder pays for giving ground up.</summary>
        public const float RelinquishWarSupportCost = 6f;

        /// <summary>Weight of the memory the receiving state keeps of the return.</summary>
        public const float RelinquishMemoryWeight = 3f;

        /// <summary>
        /// What one occupied location costs its holder every month, through the
        /// production rules that already bill it: occupation upkeep, plus the
        /// standing insurgency bill if something is burning there.
        ///
        /// Read rather than re-derived — a second definition of what holding
        /// costs would drift from the one the treasury actually pays.
        /// </summary>
        public static float HoldingBill(GameState state, StrategicLocation location)
        {
            if (location == null || !location.IsOccupied) return 0f;

            float bill = location.strategicValue * OccupationUpkeepPerValue;

            var rising = InsurgencySystem.At(state, location.id);
            if (rising != null && rising.strength >= 1f)
                bill += (rising.strength / 100f) * InsurgencySystem.InsurgencyBillPerMonth;

            return bill;
        }

        /// <summary>Total monthly holding bill across everything this country occupies.</summary>
        public static float HoldingBillFor(GameState state, string countryId)
        {
            float total = 0f;
            for (int i = 0; i < state.locations.Count; i++)
            {
                var location = state.locations[i];
                if (location.ownerId != countryId) continue;
                total += HoldingBill(state, location);
            }
            return total;
        }

        /// <summary>
        /// Whether this ground answers a shortfall the holder actually has.
        ///
        /// The same two thresholds `AISystem.ResourcePrize` reads when it decides
        /// a neighbour's ground is worth taking — an energy-poor state values an
        /// oilfield on the way out for the same reason it valued it on the way
        /// in, and a government using one number to seize and another to let go
        /// would be two governments.
        /// </summary>
        public static bool AnswersShortfall(GameState state, CountryState holder,
            StrategicLocation location)
        {
            if (holder == null || location == null) return false;

            if (holder.resources.energy < ShortfallThreshold
                && location.type == LocationType.EnergyRegion) return true;

            if (holder.resources.strategicMaterials < ShortfallThreshold
                && location.type == LocationType.MaterialsRegion) return true;

            return false;
        }

        /// <summary>The endowment level below which a state is short of something.</summary>
        public const float ShortfallThreshold = 40f;

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

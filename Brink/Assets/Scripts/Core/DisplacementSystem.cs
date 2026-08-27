using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Displacement — the missing externality (GDD §12, §16, §27).
    ///
    /// **Everything bad that happened to a country stayed inside its borders.** A
    /// state could be bombed flat, blockaded into famine and torn apart by an
    /// insurgency, and its neighbours would notice nothing but a market number. A
    /// war was a private arrangement between the two governments fighting it, and
    /// the region it was fought in had no opinion about that. There was no
    /// population movement anywhere in the code: `refugee` appeared in the text of
    /// exactly one event.
    ///
    /// This is the connective tissue between systems that were all already here.
    /// War, insurgency and deprivation displace people; they arrive somewhere;
    /// the receiving state's living standards and unrest move; that feeds the
    /// opposition's theme; and the answer — carry them, or shut the border — is a
    /// decision with a price on both sides.
    ///
    /// The rules are the usual ones, and one new one:
    ///
    /// - **Both values are targets.** `displaced` approaches what the conditions
    ///   at home justify, `hosted` approaches what has actually arrived, so people
    ///   go home when the country becomes liveable again and nothing here
    ///   ratchets.
    /// - **Everything it costs a host is pushed through a target**
    ///   (`StandardsDrag`, `UnrestPressure`) except the treasury, which is a real
    ///   balance. A flat monthly subtraction from living standards or unrest would
    ///   be erased by the same tick.
    /// - **Hosting is a trade, not a penalty.** People who arrive work. The
    ///   labour shows up as industrial capacity over years, so a state that
    ///   carries a crisis is repaid slowly for it — otherwise the only correct
    ///   play would be to shut the border on day one and the decision would not
    ///   be a decision.
    /// - **Distance decides where they go**, through the same authored
    ///   coordinates everything else uses. You inherit your neighbours' problems;
    ///   that is what a neighbour is.
    /// - **Closing the border does not make it stop.** The people who cannot
    ///   leave stay, and the pressure they were escaping goes up at home. That is
    ///   the honest version of the instrument, and it is what makes shutting the
    ///   door a foreign policy rather than a filter.
    /// </summary>
    public static class DisplacementSystem
    {
        /// <summary>Political Capital to open or shut the border.</summary>
        public const float BorderPolicyCost = 2f;

        /// <summary>Distance beyond which a state is not somewhere people can walk to.</summary>
        public const float ReachableDistance = 34f;

        /// <summary>Treasury per point hosted, per month.</summary>
        public const float HostingCostPerPoint = 22f;

        // ---------- what a country produces ----------

        /// <summary>
        /// The share of this country's people the conditions would displace.
        ///
        /// Zero for any country that is at peace, fed and governed — by
        /// construction, so this cannot quietly move a healthy world.
        /// </summary>
        public static float DisplacementTargetFor(GameState state, CountryState country)
        {
            float war = state.IsAtWar(country.id)
                ? 6f + country.warExhaustion * 0.22f
                : country.warExhaustion * 0.06f;

            float fighting = Math.Min(22f, InsurgencySystem.PressureOn(state, country.id) * 0.10f);

            // Measured against the country's own normal, like every other hunger
            // term in the model — an authored food-poor state has adapted, and an
            // absolute line would read its baseline as a standing catastrophe.
            float hunger = Math.Max(0f, country.resources.foodEndowment
                                        - country.resources.foodSecurity - 12f) * 0.35f;

            float deprivation = Math.Max(0f, 26f - country.livingStandards) * 0.55f;

            // Ground under somebody else's army empties out.
            float occupied = Math.Min(14f, TerritorySystem.LostValue(state, country.id) * 0.05f);

            float target = war + fighting + hunger + deprivation + occupied;

            // Nowhere to go. A closed region does not stop people leaving their
            // homes, but it does stop them leaving the country — which is why
            // this is capped rather than zeroed, and why the pressure that could
            // not leave shows up at home in `UnrestPressure` below.
            return Clamp(target);
        }

        /// <summary>Total displaced share this country is currently producing.</summary>
        public static float DisplacedFrom(GameState state, string countryId)
            => state.FindCountry(countryId)?.displacement.displaced ?? 0f;

        // ---------- what it costs a host ----------

        /// <summary>
        /// What hosting does to how well the public lives, as a shift in the
        /// living-standards **target**. Read by `GovernmentSystem`.
        ///
        /// Deliberately mild per point: this is a strain on services, not a
        /// catastrophe, and a host state that is otherwise well run absorbs it.
        /// </summary>
        public static float StandardsDrag(CountryState country)
            => Math.Min(9f, country.displacement.hosted * 0.22f);

        /// <summary>
        /// What hosting adds to the pressure behind organised unrest. The other
        /// half — pressure at *home* from people who could not leave — is
        /// `PressureAtSource`, because they are different countries' problems.
        /// </summary>
        public static float UnrestPressure(CountryState country)
            => Math.Min(10f, country.displacement.hosted * 0.20f);

        /// <summary>
        /// Pressure at *home* from people who cannot leave. Read by
        /// `GovernmentSystem` for the country producing the displacement, not for
        /// its hosts.
        /// </summary>
        public static float PressureAtSource(GameState state, CountryState country)
        {
            float unabsorbed = country.displacement.displaced - AbsorbedShareOf(state, country);
            return Math.Min(12f, Math.Max(0f, unabsorbed) * 0.30f);
        }

        /// <summary>How much of this country's displacement the world has taken in.</summary>
        static float AbsorbedShareOf(GameState state, CountryState country)
        {
            // Not tracked per pair — the model is aggregate on both sides — so
            // this is the share of world hosting capacity that is open to them,
            // which is what actually decides whether anyone got out.
            float open = 0f, near = 0f;
            foreach (var other in state.countries)
            {
                if (other.id == country.id) continue;
                if (GeographySystem.DistanceBetween(country.id, other.id) > ReachableDistance) continue;

                near++;
                if (!other.displacement.bordersClosed) open++;
            }
            if (near <= 0f) return 0f;
            return country.displacement.displaced * (open / near);
        }

        // ---------- the border ----------

        public static bool CanSetBorderPolicy(GameState state, string countryId,
            bool closed, out string reason)
        {
            var country = state.FindCountry(countryId);
            if (country == null) { reason = "No such state."; return false; }
            if (country.displacement.bordersClosed == closed)
            {
                reason = closed ? "THE BORDER IS ALREADY SHUT." : "THE BORDER IS ALREADY OPEN.";
                return false;
            }
            reason = "";
            return true;
        }

        /// <summary>Actor-generic. Shut the border, or open it again.</summary>
        public static bool SetBorderPolicyBy(GameState state, string countryId, bool closed)
        {
            if (!CanSetBorderPolicy(state, countryId, closed, out _)) return false;

            var country = state.FindCountry(countryId);
            if (!GovernmentSystem.SpendPoliticalCapitalBy(
                    state, countryId, BorderPolicyCost,
                    closed ? "Close the border" : "Open the border")) return false;

            country.displacement.bordersClosed = closed;
            country.displacement.monthsClosed = 0;

            // Everyone who is carrying the same crisis notices who stopped
            // carrying it. Standing, not capability — and recoverable, like every
            // other reputational cost in this codebase.
            foreach (var other in state.countries)
            {
                if (other.id == countryId) continue;
                if (other.displacement.hosted < 3f && other.displacement.displaced < 3f) continue;

                var relationship = state.FindRelationship(countryId, other.id);
                if (relationship == null) continue;

                relationship.relations = Clamp(relationship.relations + (closed ? -5f : 3f));
                relationship.trust = Clamp(relationship.trust + (closed ? -4f : 2f));
            }

            state.AddChronicle(ChronicleCategory.Political, countryId,
                closed
                    ? $"{country.displayName} closes its borders to arrivals."
                    : $"{country.displayName} opens its borders again.",
                Publicity.Public);

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory,
                    closed ? "BORDERS CLOSED" : "BORDERS OPEN",
                    closed
                        ? "Nobody else is coming in. The pressure does not go away — it stays "
                          + "on the other side of the line, and the states still carrying it "
                          + "have noticed."
                        : "We are taking people again. It will cost money and it will be "
                          + "argued about.",
                    countryId, desk: ReportingDesk.Government);
            return true;
        }

        /// <summary>Player order: XP and initiative on top of the spend.</summary>
        public static bool SetBorderPolicy(GameState state, bool closed)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Government)) return false;
            if (!CanSetBorderPolicy(state, state.playerCountryId, closed, out string reason))
            {
                GameLog.Warn("GOV", reason);
                return false;
            }
            if (!SetBorderPolicyBy(state, state.playerCountryId, closed)) return false;

            ProgressionSystem.AwardXP(state, 12, "Border policy set");
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        // ---------- the monthly tick ----------

        public static void MonthlyUpdate(GameState state)
        {
            // 1. What each country is producing.
            foreach (var country in state.countries)
            {
                float target = DisplacementTargetFor(state, country);
                var displacement = country.displacement;

                // Quicker to leave than to return: people go when they have to
                // and come back when they believe it. The same asymmetry unrest
                // and living standards already use, for the same reason.
                displacement.displaced = Approach(displacement.displaced, target,
                    displacement.displaced < target ? 0.14f : 0.04f);

                if (displacement.bordersClosed) displacement.monthsClosed++;
                else displacement.monthsClosed = 0;
            }

            // 2. Where they end up. Computed fresh each month from distance and
            //    who is open, rather than stored per pair: the aggregate is what
            //    the rest of the model reads, and a per-pair ledger would be a
            //    second copy of a number nothing needs at that resolution.
            var arrivals = new Dictionary<string, float>();
            foreach (var country in state.countries) arrivals[country.id] = 0f;

            foreach (var source in state.countries)
            {
                if (source.displacement.displaced < 1f) continue;

                float weightTotal = 0f;
                var weights = new Dictionary<string, float>();

                foreach (var host in state.countries)
                {
                    if (host.id == source.id) continue;
                    if (host.displacement.bordersClosed) continue;

                    float distance = GeographySystem.DistanceBetween(source.id, host.id);
                    if (distance > ReachableDistance) continue;

                    // Nearer states carry more of it. You inherit your
                    // neighbours' problems; that is what a neighbour is.
                    float weight = 1f / Math.Max(4f, distance);
                    weights[host.id] = weight;
                    weightTotal += weight;
                }

                if (weightTotal <= 0f) continue;   // nowhere open within reach

                foreach (var pair in weights)
                    arrivals[pair.Key] += source.displacement.displaced * (pair.Value / weightTotal);
            }

            // 3. What each host is carrying, and what it costs.
            foreach (var country in state.countries)
            {
                var displacement = country.displacement;

                displacement.hosted = Approach(displacement.hosted, Clamp(arrivals[country.id]),
                    displacement.hosted < arrivals[country.id] ? 0.12f : 0.06f);

                if (displacement.hosted < 0.5f) continue;

                country.resources.treasury -= displacement.hosted * HostingCostPerPoint;

                // And what it is worth. Slow, and through `Growth.Apply`, because
                // people arriving with nothing take years to be an economy and
                // this must never become a reason to want a neighbour to collapse.
                country.resources.industrialCapacity = Growth.Apply(
                    country.resources.industrialCapacity, displacement.hosted * 0.010f);

                Report(state, country);
            }

            foreach (var country in state.countries)
                if (!country.isPlayer) ConsiderBorderPolicy(state, country);
        }

        /// <summary>
        /// Whether a foreign government shuts its border.
        ///
        /// The same verb at the same price, chosen by what kind of state it is —
        /// a restrictive or non-elective government reaches for the door sooner,
        /// and a treasury that cannot carry the bill closes it whatever its
        /// politics. Placed here rather than in `AISystem`'s objective budget for
        /// the `ConsiderDetente` reason: this is a standing posture, not a
        /// strategy competing for this month's actions, and a verb the world will
        /// not reach for is a verb the world does not have.
        /// </summary>
        static void ConsiderBorderPolicy(GameState state, CountryState country)
        {
            var displacement = country.displacement;
            float bill = displacement.hosted * HostingCostPerPoint;

            bool restrictive = country.government.civicPosture == CivicPosture.Restrictive
                               || !country.government.IsElective;
            float tolerance = restrictive ? 8f : 18f;

            if (!displacement.bordersClosed)
            {
                bool cannotAfford = country.resources.treasury < bill * 6f;
                if (displacement.hosted > tolerance || cannotAfford)
                    SetBorderPolicyBy(state, country.id, true);
                return;
            }

            // And open it again once the reason has passed. Without this the
            // world would shut every border once and never reopen one — the
            // one-way-value bug wearing a policy for a costume.
            if (displacement.monthsClosed >= 12 && displacement.hosted < tolerance * 0.4f)
                SetBorderPolicyBy(state, country.id, false);
        }

        static void Report(GameState state, CountryState country)
        {
            if (!country.isPlayer) return;

            // One item, when it first becomes a real number, and one when it
            // becomes a serious one. PRIORITY rather than FLASH: it is a
            // situation to manage, not a turn that cannot be taken.
            var displacement = country.displacement;
            if (displacement.hosted >= 12f && displacement.hosted < 12.5f)
                state.AddNotification(NotificationClass.Priority, "ARRIVALS",
                    "The country is carrying a significant number of people from elsewhere. "
                    + "It is costing money and it is being argued about. The border can be "
                    + "shut, and that has its own price.",
                    country.id, desk: ReportingDesk.Government);
        }

        static float Approach(float current, float target, float rate)
            => Clamp(current + (target - current) * rate);

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

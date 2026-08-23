using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Buying specific equipment, and paying for it (GDD §19, §20).
    ///
    /// The existing `ProcurementProgram` buys *branch strength* over years, which
    /// is the right model for "we are rebuilding the navy". What it cannot
    /// express is the decision the operator actually wants to make: bombers or
    /// tankers, hulls or aircraft, steel or people. Those are different bets with
    /// different lead times, and a single strength number hides all of it.
    ///
    /// Three rules this follows, each of which is a place the system could have
    /// gone wrong:
    ///
    /// 1. **It costs treasury, and the treasury is the economy's.** An order is a
    ///    real bill against the same pool that funds everything else, so building
    ///    a fleet is a decision with an opportunity cost rather than a menu.
    /// 2. **Steel takes years and people take months.** Lead times are per-class
    ///    (`AssetProfile.leadMonths`), so a carrier ordered in a crisis arrives
    ///    for the war after it. That is the whole reason force structure is a
    ///    strategic decision rather than a tactical one.
    /// 3. **War footing is political, not free.** A government can move money to
    ///    the military faster than normal — but only with the standing to carry
    ///    it, and it costs that standing every month it runs.
    /// </summary>
    public static class AcquisitionSystem
    {
        /// <summary>Command capacity to place an order.</summary>
        public const int OrderCost = 1;

        /// <summary>Political capital to put the state on a war footing.</summary>
        public const float WarFootingCost = 5f;

        /// <summary>Legislative or elite backing needed before that is possible.</summary>
        public const float WarFootingSupport = 45f;

        /// <summary>Monthly political bill for holding a war footing.</summary>
        public const float WarFootingUpkeep = 0.6f;

        /// <summary>How much faster deliveries arrive on a war footing.</summary>
        public const float WarFootingSpeed = 1.9f;

        /// <summary>Share of an order delivered per month at normal tempo.</summary>
        public static float DeliveryRateFor(CountryState country, AssetKind kind)
        {
            var profile = AssetCatalog.For(kind);
            if (profile == null || profile.leadMonths <= 0) return 1f;

            float rate = 1f / profile.leadMonths;

            // Industry is what actually builds things. A state with no industrial
            // capacity can order whatever it likes and wait.
            rate *= 0.55f + country.resources.industrialCapacity / 140f;

            if (country.military.warFooting) rate *= WarFootingSpeed;
            return rate;
        }

        // ---------- ordering ----------

        /// <summary>
        /// Place an order. Actor-generic: an AI government buys the same way and
        /// pays the same bill.
        /// </summary>
        public static bool OrderBy(GameState state, string actorId, AssetKind kind, float count)
        {
            var actor = state.FindCountry(actorId);
            if (actor == null) return false;

            var profile = AssetCatalog.For(kind);
            if (profile == null || count <= 0f) return false;

            float cost = AssetCatalog.CostOf(kind, count);
            if (actor.resources.treasury < cost) return false;

            actor.resources.treasury -= cost;

            var force = actor.military.Get(profile.branch);
            force.inventory.Ensure(kind).onOrder += count;

            if (actor.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "ORDER PLACED",
                    $"{AssetCatalog.Format(count)} {profile.label} ordered. First deliveries in " +
                    $"roughly {profile.leadMonths} months.", actorId, desk: ReportingDesk.Military);

            return true;
        }

        /// <summary>
        /// The asset this country is furthest short of, and how short.
        ///
        /// **One definition, used by everyone.** The player's minister
        /// (`CabinetSystem.ProcureShortfall`), the routine restock every military
        /// desk performs, and the AI's `OrderWhatIsShort` were each computing
        /// this themselves, and they had already drifted — only the AI's copy
        /// knew that a landlocked state should not be ordering carriers.
        /// </summary>
        public static AssetProfile WorstShortfall(CountryState country, out float ratio)
        {
            AssetProfile worst = null;
            ratio = float.MaxValue;
            if (country == null) return null;

            bool landlocked = GeographySystem.AccessOf(country.id) == NavalAccess.Landlocked;

            foreach (var asset in AssetCatalog.All)
            {
                // Nothing to restock in a branch this country cannot field. A
                // landlocked state would otherwise sink its whole treasury into
                // hulls it has nowhere to float.
                if (asset.branch == ForceBranch.Naval && landlocked) continue;

                var force = country.military.Get(asset.branch);
                float target = asset.baselineAt100 * (force.strength / 100f);
                if (target <= 0.01f) continue;

                float have = force.inventory.CountOf(asset.kind) + force.inventory.OnOrderOf(asset.kind);
                float have_ratio = have / target;
                if (have_ratio >= ratio) continue;

                ratio = have_ratio;
                worst = asset;
            }

            if (worst == null) ratio = 1f;
            return worst;
        }

        /// <summary>
        /// Routine replacement of losses, performed by whoever runs the military
        /// desk — in every country, ours and theirs.
        ///
        /// This is maintenance, not strategy. It used to be reachable only down a
        /// four-gate chain inside `AISystem` that required the government to hold
        /// a Security priority, so across thirty measured years **no foreign
        /// government ordered a single piece of equipment** and only the player
        /// could ever replace a loss. Restocking a depleted squadron is what a
        /// defence ministry does by existing; it should not have to win an
        /// argument about grand strategy first.
        ///
        /// Spends treasury, so it is not free for either side. Fires only against
        /// a real gap, so a force at establishment costs nothing.
        /// </summary>
        public static bool RestockRoutine(GameState state, string actorId, float effort)
        {
            var country = state.FindCountry(actorId);
            if (country == null) return false;

            var worst = WorstShortfall(country, out float ratio);
            if (worst == null || ratio > 0.90f) return false;

            // Never more than a quarter of the treasury: a ministry replacing
            // losses does not bankrupt the state doing it.
            float wanted = worst.orderIncrement * Math.Max(0.4f, effort);
            float affordable = country.resources.treasury * 0.25f / Math.Max(0.0001f, worst.unitCost);

            float count = Math.Min(wanted, affordable);
            return count >= 1f && OrderBy(state, actorId, worst.kind, count);
        }

        /// <summary>Player order: spends CP as well, and earns the initiative for it.</summary>
        public static bool Order(GameState state, TurnManager turns, AssetKind kind, float count)
        {
            var profile = AssetCatalog.For(kind);
            if (profile == null) return false;

            float cost = AssetCatalog.CostOf(kind, count);
            if (state.PlayerCountry.resources.treasury < cost)
            {
                GameLog.Warn("MIL", $"The treasury cannot cover {AssetCatalog.Format(count)} " +
                                    $"{profile.label} ({cost:F0}).");
                return false;
            }

            if (!turns.SpendCommandPoints(OrderCost, $"Order {profile.label}")) return false;
            if (!OrderBy(state, state.playerCountryId, kind, count)) return false;

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 8, "Procurement ordered");
            return true;
        }

        // ---------- delivery ----------

        /// <summary>
        /// Monthly deliveries, for every state. Wired in `SimulationPipeline`.
        ///
        /// Deliveries raise the inventory and the branch strength follows, which
        /// is the direction that makes counts meaningful: buying two hundred
        /// fighters has to make the air force measurably stronger, or the whole
        /// exercise is a spreadsheet with no consequence.
        /// </summary>
        public static void MonthlyDeliveries(GameState state)
        {
            foreach (var country in state.countries)
            {
                bool delivered = false;

                foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                {
                    var force = country.military.Get(branch);
                    foreach (var stock in force.inventory.stocks)
                    {
                        if (stock.onOrder <= 0.01f) continue;

                        float rate = DeliveryRateFor(country, stock.kind);
                        float arriving = Math.Min(stock.onOrder, stock.onOrder * rate + 0.5f);

                        stock.onOrder -= arriving;
                        stock.count += arriving;
                        delivered = true;
                    }
                    if (delivered) force.SyncStrength();
                }
            }
        }

        // ---------- war footing ----------

        /// <summary>
        /// Whether this government could put the state on a war footing.
        ///
        /// The gate is political backing, because that is what the request
        /// actually is: moving money to the military faster than the normal
        /// budget allows is something a legislature or an inner circle grants,
        /// not something an operator decides alone.
        /// </summary>
        public static bool CanDeclareWarFooting(GameState state, string countryId, out string reason)
        {
            reason = "";
            var country = state.FindCountry(countryId);
            if (country == null) { reason = "NO SUCH STATE"; return false; }
            if (country.military.warFooting) { reason = "ALREADY ON A WAR FOOTING"; return false; }

            var gov = country.government;
            float backing = gov.IsElective ? gov.legislativeSupport : gov.eliteCohesion;
            if (backing < WarFootingSupport)
            {
                reason = gov.IsElective
                    ? $"THE CHAMBER WILL NOT VOTE THE MONEY — support {backing:F0}, need {WarFootingSupport:F0}"
                    : $"THE INNER CIRCLE WILL NOT BACK IT — cohesion {backing:F0}, need {WarFootingSupport:F0}";
                return false;
            }

            // A country at peace has no case to make, and the political system
            // knows it. This is the one place the state of the world gates a
            // domestic instrument.
            if (!state.IsAtWar(countryId) && TheatreSystem.TotalCommitment(state, countryId) < 0.4f)
            {
                reason = "NO CONFRONTATION TO JUSTIFY IT";
                return false;
            }

            return true;
        }

        /// <summary>Actor-generic. Every government can do this, at the same price.</summary>
        public static bool SetWarFootingBy(GameState state, string countryId, bool active)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return false;
            if (country.military.warFooting == active) return false;

            if (active)
            {
                if (!CanDeclareWarFooting(state, countryId, out string reason))
                {
                    if (countryId == state.playerCountryId) GameLog.Warn("MIL", reason);
                    return false;
                }
                if (!GovernmentSystem.SpendPoliticalCapitalBy(state, countryId, WarFootingCost,
                        "War footing")) return false;
            }

            country.military.warFooting = active;

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Priority,
                    active ? "WAR FOOTING DECLARED" : "WAR FOOTING ENDED",
                    active
                        ? "The budget has been re-cut toward the military. Deliveries will arrive " +
                          "roughly twice as fast, and holding this costs authority every month."
                        : "The budget returns to normal. Deliveries slow to peacetime tempo.",
                    countryId, desk: ReportingDesk.Government);

            state.AddChronicle(ChronicleCategory.Political, countryId,
                active ? "State placed on a war footing." : "War footing ended.", Publicity.Public);
            return true;
        }

        /// <summary>Player wrapper: records the initiative and the experience.</summary>
        public static bool SetWarFooting(GameState state, bool active)
        {
            if (!SetWarFootingBy(state, state.playerCountryId, active)) return false;
            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 14, "War footing set");
            return true;
        }

        /// <summary>
        /// The standing bill. A war footing is a position held, not an act taken,
        /// so it is charged monthly like every other posture in the game — and it
        /// lapses on its own when the state can no longer pay for it, rather than
        /// running forever because nobody remembered to end it.
        /// </summary>
        public static void MonthlyUpkeep(GameState state)
        {
            foreach (var country in state.countries)
            {
                if (!country.military.warFooting) continue;

                bool paid = GovernmentSystem.SpendPoliticalCapitalBy(
                    state, country.id, WarFootingUpkeep, "War footing upkeep");

                // Peacetime removes the justification as surely as an empty
                // treasury removes the means.
                bool stillJustified = state.IsAtWar(country.id)
                                      || TheatreSystem.TotalCommitment(state, country.id) >= 0.4f;

                if (paid && stillJustified) continue;

                country.military.warFooting = false;
                if (country.isPlayer)
                    state.AddNotification(NotificationClass.Advisory, "WAR FOOTING LAPSED",
                        paid
                            ? "With no confrontation to justify it, the emergency budget has expired."
                            : "The government could not sustain the political cost. The budget " +
                              "returns to normal.",
                        country.id, desk: ReportingDesk.Government);
            }
        }
    }
}

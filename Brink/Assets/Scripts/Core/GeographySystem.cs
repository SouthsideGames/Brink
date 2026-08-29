using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Where things are, and how far a state can actually fight (GDD §16, §19).
    ///
    /// Until this existed, geography was decoration: `mapX`/`mapY` fed the ASCII
    /// map and nothing else, so any state could assault any location on earth at
    /// identical cost and identical odds. A landlocked regional power could
    /// campaign across the planet as easily as against its neighbour, and the
    /// navy — the single most expensive thing a country can buy — bought nothing
    /// that distance made valuable.
    ///
    /// Three ideas, and no new save state:
    ///
    /// - **Position is authored, not saved.** Coordinates live in
    ///   `WorldFactory.Profiles`. Geography does not change during a save, so
    ///   storing it in `GameState` would only add schema to keep in sync.
    /// - **The world is a cylinder.** Longitude wraps, which is not a detail:
    ///   the United States is *closer* to Japan across the Pacific than to
    ///   China the long way round, and a model that measures raw column
    ///   distance gets the entire Pacific backwards.
    /// - **Ground you hold abroad is a place you can fight from.**
    ///   `originalOwnerId` says where a location physically *is*;
    ///   `ownerId` says who controls it. A captured port projects from the
    ///   country it sits in, not from the capital of whoever took it — which is
    ///   what makes a forward position worth taking, and makes basing rights
    ///   (`foreignOperatorId`) worth negotiating for.
    /// </summary>
    public static class GeographySystem
    {
        /// <summary>
        /// Width of the authored world grid, and the value longitude wraps at.
        ///
        /// **This is the single authority.** `AsciiWorldMap.Width` refers to it
        /// rather than declaring its own: the renderer and the distance model
        /// have to agree on how wide the world is, and as two independent
        /// literals they silently did not — the map was 78 columns while
        /// distance wrapped at 80, so every trans-meridian pair measured about
        /// two columns farther apart than the map the player was looking at.
        /// Harmless at those values, and exactly the kind of thing that stops
        /// being harmless the moment someone widens the map.
        ///
        /// 78 because `mapX` is authored `0..77` (spec 08).
        /// </summary>
        public const int MapWidth = 78;

        /// <summary>
        /// Latitude weight. The authored grid is roughly 80 × 20 for a whole
        /// world, and terminal cells are about twice as tall as they are wide, so
        /// a step in Y covers about twice the ground a step in X does.
        /// </summary>
        public const float LatitudeWeight = 2f;

        /// <summary>Reach a state has before any force, logistics or basing.</summary>
        public const float BaseReach = 8f;

        /// <summary>
        /// Floor on the reach multiplier. Distance makes a far campaign hard; it
        /// must never make one impossible. Hard geographic gates would repeat the
        /// §18.1 mistake where escalation states blocked operations outright
        /// instead of pricing them.
        ///
        /// 0.35 → 0.2 (2026-08 playtest): at 0.35 a landlocked buffer state with
        /// no navy fought at a third of its strength anywhere on earth, and
        /// Kazakhstan occupied the Gulf Coast Energy Belt and Mexico held
        /// Western Siberia in measured decades. At 0.2 the far end of the map is
        /// still reachable — for a power that has bought the reach — and a state
        /// that has not is fighting at a fifth of its strength, which is what
        /// "beyond reach" should mean.
        /// </summary>
        public const float MinimumReach = 0.2f;

        // ---------- distance ----------

        /// <summary>Great-circle-ish distance between two authored positions.</summary>
        public static float Distance(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx);
            if (dx > MapWidth / 2) dx = MapWidth - dx;      // the short way round
            float dy = (ay - by) * LatitudeWeight;
            return (float)Math.Sqrt(dx * (float)dx + dy * dy);
        }

        /// <summary>Distance between two countries' home positions.</summary>
        public static float DistanceBetween(string aId, string bId)
        {
            var a = WorldFactory.FindProfile(aId);
            var b = WorldFactory.FindProfile(bId);
            if (a == null || b == null) return 0f;
            return Distance(a.mapX, a.mapY, b.mapX, b.mapY);
        }

        /// <summary>
        /// How much of a sea power this country can be (GDD §16). Authored world
        /// data, so it is the same in every save and cannot drift.
        /// </summary>
        public static NavalAccess AccessOf(string countryId)
        {
            var profile = WorldFactory.FindProfile(countryId);
            return profile?.navalAccess ?? NavalAccess.Coastal;
        }

        /// <summary>
        /// Whether the sea reaches this country at all.
        ///
        /// The gate under every naval verb. A blockade, a mining campaign or a
        /// landing against a landlocked state is not a hard operation, it is a
        /// meaningless one, and the order screen says so rather than letting the
        /// player spend command capacity discovering it.
        /// </summary>
        public static bool HasSeaAccess(string countryId)
            => AccessOf(countryId) != NavalAccess.Landlocked;

        /// <summary>
        /// Where a location physically sits — the country that originally held
        /// it, never whoever holds it today. Taking ground moves control, not
        /// the ground.
        /// </summary>
        public static string HostOf(StrategicLocation location)
            => string.IsNullOrEmpty(location.originalOwnerId)
                ? location.ownerId
                : location.originalOwnerId;

        // ---------- reach ----------

        /// <summary>
        /// How far this state can project force. A navy is the main thing that
        /// buys distance, which is the entire strategic argument for having one;
        /// airlift and logistics add to it, and strategic lift multiplies it.
        /// </summary>
        public static float ProjectionRange(GameState state, string countryId)
        {
            var country = state.FindCountry(countryId);
            if (country == null) return BaseReach;

            var mil = country.military;
            return BaseReach
                   + mil.naval.EffectivePower * 35f
                   + mil.air.EffectivePower * 20f
                   + mil.logistics * 0.15f
                   + TechnologySystem.Effectiveness(country, "CAP_LIFT") * 10f
                   // Dual-use (spec 13 §6): moving an army the way we move cargo
                   // (`CAP_STRATLOG`), and our own launch, which reaches
                   // everywhere by definition (`CAP_SPACE`).
                   + TechnologySystem.Effectiveness(country, "CAP_STRATLOG") * 14f
                   + TechnologySystem.Effectiveness(country, "CAP_SPACE") * 12f
                   // A nation whose front door is the sea gets further with the
                   // same fleet than one that merely owns some coast.
                   + NationalTraitCatalog.ProjectionBonus(country);
        }

        /// <summary>
        /// Distance from the nearest place this state can actually operate from
        /// to the given host country. Counts the homeland, any ground it holds
        /// abroad, and anywhere it has basing rights.
        /// </summary>
        public static float EffectiveDistanceTo(GameState state, string actorId, string hostId)
        {
            float best = DistanceBetween(actorId, hostId);

            foreach (var location in state.locations)
            {
                bool holds = location.ownerId == actorId;
                bool hosted = location.foreignOperatorId == actorId;
                if (!holds && !hosted) continue;

                // A partner's base is reach we did not have to conquer, but it is
                // someone else's ground and worth slightly less than our own.
                float penalty = hosted && !holds ? 2f : 0f;

                float from = DistanceBetween(HostOf(location), hostId) + penalty;
                if (from < best) best = from;
            }

            return best;
        }

        /// <summary>
        /// Multiplier on combat power for fighting at this distance, 0.35..1.
        /// Full strength inside our reach, tailing off beyond it.
        /// </summary>
        public static float ReachFactorTo(GameState state, string actorId, string hostId)
        {
            float range = ProjectionRange(state, actorId);
            float distance = EffectiveDistanceTo(state, actorId, hostId);
            if (distance <= range || distance <= 0.01f) return 1f;

            float factor = range / distance;
            return factor < MinimumReach ? MinimumReach : factor;
        }

        /// <summary>Reach against a specific location, measured to where it sits.</summary>
        public static float ReachFactorFor(GameState state, string actorId, StrategicLocation location)
            => location == null ? 1f : ReachFactorTo(state, actorId, HostOf(location));

        /// <summary>
        /// Plain-language reading of a reach factor, for the terminal. The player
        /// has to be able to see why a distant operation went badly.
        /// </summary>
        public static string ReachText(float factor)
        {
            if (factor >= 0.999f) return "WITHIN REACH";
            if (factor >= 0.80f) return "EXTENDED";
            if (factor >= 0.60f) return "OVEREXTENDED";
            return "BEYOND REACH";
        }
    }
}

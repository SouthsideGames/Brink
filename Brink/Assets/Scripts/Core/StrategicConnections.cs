using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Authored gameplay dependencies, not a real-world infrastructure inventory.</summary>
    public static class StrategicConnections
    {
        public enum Kind { Pipeline, ResourceCorridor, IndustrialCorridor, Cable, AirCorridor }

        public readonly struct Connection
        {
            public readonly string name, a, b;
            public readonly Kind kind;
            public readonly LocationType aType, bType;
            public Connection(string name, Kind kind, string a, LocationType aType, string b, LocationType bType)
            { this.name = name; this.kind = kind; this.a = a; this.aType = aType; this.b = b; this.bType = bType; }
        }

        // ponytail: a bounded catalogue over existing sites; no graph, invented sites or save backfill.
        static readonly Connection[] connections = {
            new Connection("SIBERIAN ENERGY LINE", Kind.Pipeline, "RUS_ENR", LocationType.EnergyRegion, "CHN_IND", LocationType.IndustrialCenter),
            new Connection("NORTH AMERICAN ENERGY LINE", Kind.Pipeline, "USA_ENR", LocationType.EnergyRegion, "MEX_IND", LocationType.IndustrialCenter),
            new Connection("CASPIAN RESOURCE CORRIDOR", Kind.ResourceCorridor, "KAZ_MAT", LocationType.MaterialsRegion, "CHN_IND", LocationType.IndustrialCenter),
            new Connection("SOUTH ATLANTIC RESOURCE CORRIDOR", Kind.ResourceCorridor, "BRA_MAT", LocationType.MaterialsRegion, "NGA_MAT", LocationType.MaterialsRegion),
            new Connection("CENTRAL EUROPEAN INDUSTRIAL CORRIDOR", Kind.IndustrialCorridor, "DEU_IND", LocationType.IndustrialCenter, "POL_PAS", LocationType.MountainPass),
            new Connection("NORTH AMERICAN INDUSTRIAL CORRIDOR", Kind.IndustrialCorridor, "MEX_IND", LocationType.IndustrialCenter, "USA_PRT", LocationType.Port),
            new Connection("NORTH ATLANTIC CABLE", Kind.Cable, "USA_PRT", LocationType.Port, "GBR_PRT", LocationType.Port),
            new Connection("NORTH PACIFIC CABLE", Kind.Cable, "USA_PRT", LocationType.Port, "JPN_PRT", LocationType.Port),
            new Connection("INDIAN OCEAN CABLE", Kind.Cable, "IND_PRT", LocationType.Port, "EGY_PRT", LocationType.Port),
            new Connection("EUROPEAN AIR CORRIDOR", Kind.AirCorridor, "DEU_AIR", LocationType.Airbase, "TUR_AIR", LocationType.Airbase),
            new Connection("WEST ASIAN AIR CORRIDOR", Kind.AirCorridor, "TUR_AIR", LocationType.Airbase, "IND_AIR", LocationType.Airbase),
            new Connection("INDO PACIFIC AIR CORRIDOR", Kind.AirCorridor, "IND_AIR", LocationType.Airbase, "AUS_AIR", LocationType.Airbase),
        };

        public static IEnumerable<Connection> All { get { foreach (var c in connections) yield return c; } }
        public const int OutageMonths = 6;
        public const int RepairCost = 1;
        public const float RepairTreasury = 60f;

        public static int OutageRemaining(GameState state, StrategicLocation site)
            => site == null ? 0 : Math.Max(0, site.infrastructureOutageUntilMonth - (state.date.year * 12 + state.date.month));

        public static bool IsEndpoint(string id)
        {
            foreach (var c in connections) if (c.a == id || c.b == id) return true;
            return false;
        }

        public static void Disrupt(GameState state, StrategicLocation site)
        {
            if (site != null && IsEndpoint(site.id))
                site.infrastructureOutageUntilMonth = Math.Max(site.infrastructureOutageUntilMonth,
                    state.date.year * 12 + state.date.month + OutageMonths);
        }

        static bool AtWar(GameState state, string a, string b)
        {
            foreach (var war in state.confrontations)
                if (!war.resolved && war.Involves(a) && war.Involves(b)) return true;
            return false;
        }

        static bool HasAccess(GameState state, string user, StrategicLocation site)
        {
            if (site == null || state.FindCountry(user) == null || state.FindCountry(site.ownerId) == null) return false;
            if (site.ownerId == user) return true;
            if (AtWar(state, user, site.ownerId)) return false;
            var treaty = state.FindTreaty(user, site.ownerId);
            return treaty != null && !treaty.broken && treaty.Carries(state, site.ownerId, TreatyCommitment.Transit);
        }

        /// <summary>Physical endpoints, current access and temporary outages; no mutation or hidden timers in prose.</summary>
        public static bool Open(GameState state, Connection c, string liftSender = null, string liftTarget = null)
        {
            var a = state.FindLocation(c.a); var b = state.FindLocation(c.b);
            if (a == null || b == null || a.type != c.aType || b.type != c.bType) return false;
            string aHome = WorldFactory.LocationHost(c.a), bHome = WorldFactory.LocationHost(c.b);
            return HasAccess(state, aHome, a) && HasAccess(state, bHome, b)
                && !AtWar(state, aHome, bHome)
                && (state.FindSanction(aHome, bHome) == null || liftSender == aHome && liftTarget == bHome)
                && (state.FindSanction(bHome, aHome) == null || liftSender == bHome && liftTarget == aHome)
                && OutageRemaining(state, a) == 0 && OutageRemaining(state, b) == 0;
        }

        static bool Pair(Connection c, string a, string b)
        {
            string x = WorldFactory.LocationHost(c.a), y = WorldFactory.LocationHost(c.b);
            return x == a && y == b || x == b && y == a;
        }

        /// <summary>Ten percent of otherwise delivered commodity supply, once. Never creates supply or bypasses a closure.</summary>
        public static float CommodityFactor(GameState state, string a, string b, TradeFocus focus,
            string liftSender = null, string liftTarget = null)
        {
            foreach (var c in connections)
                if (Pair(c, a, b) && (focus == TradeFocus.Energy && c.kind == Kind.Pipeline
                    || focus == TradeFocus.Materials && (c.kind == Kind.ResourceCorridor || c.kind == Kind.IndustrialCorridor))
                    && Open(state, c, liftSender, liftTarget)) return 1.1f;
            return 1f;
        }

        /// <summary>Existing collection growth uses communications; no network is granted by a cable.</summary>
        public static float CollectionFactor(GameState state, string observer, string target)
        {
            foreach (var c in connections)
                if (c.kind == Kind.Cable && Pair(c, observer, target) && Open(state, c)) return 1.1f;
            return 1f;
        }

        public static bool AirCorridorFor(GameState state, string actor, StrategicLocation destination)
        {
            if (destination == null || destination.type != LocationType.Airbase) return false;
            foreach (var c in connections)
            {
                if (c.kind != Kind.AirCorridor || c.a != destination.id && c.b != destination.id || !Open(state, c)) continue;
                var a = state.FindLocation(c.a); var b = state.FindLocation(c.b);
                // A treaty alone is not an allocated base; both ends must actually host us.
                if ((a.ownerId == actor || a.foreignOperatorId == actor)
                    && (b.ownerId == actor || b.foreignOperatorId == actor)) return true;
            }
            return false;
        }

        public static bool CanRepair(GameState state, string actor, string siteId, out string reason)
        {
            reason = null; var site = state?.FindLocation(siteId); var country = state?.FindCountry(actor);
            if (site == null || country == null || !IsEndpoint(siteId)) reason = "NO MODELLED CONNECTION ENDPOINT";
            else if (site.ownerId != actor) reason = "ONLY THE CURRENT HOLDER CAN REPAIR";
            else if (OutageRemaining(state, site) == 0) reason = "NO CONNECTION OUTAGE HERE";
            else if (country.resources.treasury < RepairTreasury) reason = "REPAIR REQUIRES 60 TREASURY";
            return reason == null;
        }

        public static bool Repair(GameState state, TurnManager turns, string siteId)
        {
            if (!CanRepair(state, state.playerCountryId, siteId, out _)) return false;
            if (!turns.SpendCommandPoints(RepairCost, "Repair connection endpoint")) return false;
            state.PlayerCountry.resources.treasury -= RepairTreasury;
            var site = state.FindLocation(siteId);
            site.infrastructureOutageUntilMonth -= Math.Min(3, OutageRemaining(state, site));
            state.AddChronicle(ChronicleCategory.Economic, state.playerCountryId,
                $"Connection repair at {site.displayName}: up to 3 months restored; other endpoints may still be unavailable.");
            return true;
        }

        public static string Readout(GameState state, Connection c)
        {
            string aHome = WorldFactory.LocationHost(c.a), bHome = WorldFactory.LocationHost(c.b);
            var a = state.FindLocation(c.a); var b = state.FindLocation(c.b);
            bool ours = aHome == state.playerCountryId || bHome == state.playerCountryId
                || a?.ownerId == state.playerCountryId || b?.ownerId == state.playerCountryId
                || a?.foreignOperatorId == state.playerCountryId || b?.foreignOperatorId == state.playerCountryId;
            if (!ours) return "";
            string effect = c.kind == Kind.Cable ? "Existing collection growth x1.10, not free intelligence."
                : c.kind == Kind.AirCorridor ? "Both ends must host us: foreign launch penalty 0.5 instead of 2 distance units."
                : "Matching commodity supply x1.10 once, only through an open trade agreement; not free stock.";
            return c.name + ": " + (a?.displayName ?? c.a + " (not represented in this save)") + " / "
                + (b?.displayName ?? c.b + " (not represented in this save)") + ". "
                + (Open(state, c) ? "CONNECTION AVAILABLE. " : "CONNECTION UNAVAILABLE: endpoint, access, war, sanctions or outage. ") + effect;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// A counted class of military equipment or personnel (GDD §19, amended).
    ///
    /// **This supersedes §19's "no individual vehicle counts" rule**, by explicit
    /// design decision. The original rule existed to stop the game becoming a
    /// logistics spreadsheet, and that concern is still right — which is why an
    /// operation still resolves against *branch strength* rather than counting
    /// airframes. What changed is the recognition that "air strength 68" is not a
    /// fact a player can reason about, and "912 fighters, 140 bombers" is.
    ///
    /// So counts are the **readable and buyable layer**, and branch strength is a
    /// mirror of them. You decide in counts; the simulation still resolves in
    /// aggregates.
    ///
    /// Appended, never reordered — persisted by ordinal.
    /// </summary>
    public enum AssetKind
    {
        // ---- air ----
        Fighters,
        Drones,
        Bombers,
        Tankers,
        AirMissiles,
        AirCrew,

        // ---- naval ----
        Carriers,
        Submarines,
        Destroyers,
        Cruisers,
        CombatShips,
        NavalMissiles,
        NavalCrew,

        // ---- ground ----
        Tanks,
        Helicopters,
        GroundMissiles,
        Soldiers
    }

    /// <summary>How many of one thing a branch currently has.</summary>
    [Serializable]
    public class AssetStock
    {
        public AssetKind kind;
        public float count;

        /// <summary>
        /// Units still in production from funded programmes, delivered monthly.
        /// Kept separate so the operator can see what is coming as well as what
        /// is here — an order placed two years ago is a fact about this month's
        /// decision.
        /// </summary>
        public float onOrder;
    }

    /// <summary>
    /// One branch's inventory. Lives on <see cref="BranchForce"/>.
    ///
    /// Deliberately a list rather than named fields: the catalogue decides which
    /// kinds a branch has, so adding one is an authoring change rather than a
    /// schema change.
    /// </summary>
    [Serializable]
    public class ForceInventory
    {
        public List<AssetStock> stocks = new List<AssetStock>();

        public AssetStock Get(AssetKind kind)
        {
            for (int i = 0; i < stocks.Count; i++)
                if (stocks[i].kind == kind) return stocks[i];
            return null;
        }

        public float CountOf(AssetKind kind) => Get(kind)?.count ?? 0f;

        public float OnOrderOf(AssetKind kind) => Get(kind)?.onOrder ?? 0f;

        public AssetStock Ensure(AssetKind kind)
        {
            var stock = Get(kind);
            if (stock != null) return stock;
            stock = new AssetStock { kind = kind };
            stocks.Add(stock);
            return stock;
        }

        public void Add(AssetKind kind, float amount)
        {
            var stock = Ensure(kind);
            stock.count = Math.Max(0f, stock.count + amount);
        }

        /// <summary>True when this branch has essentially nothing left.</summary>
        public bool IsSpent
        {
            get
            {
                foreach (var stock in stocks)
                    if (stock.count > 0.5f) return false;
                return true;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>What one class of equipment is, costs, and contributes.</summary>
    public class AssetProfile
    {
        public AssetKind kind;
        public ForceBranch branch;
        public string displayName;

        /// <summary>Plural label for a readout, e.g. "FIGHTERS".</summary>
        public string label;

        /// <summary>
        /// How many a branch at strength 100 would hold. The scale that makes a
        /// count legible: 1200 fighters reads as an air force, 12 carriers reads
        /// as a fleet, and neither needs a tooltip.
        /// </summary>
        public float baselineAt100;

        /// <summary>
        /// Fraction of the branch's strength this class accounts for. Shares
        /// within a branch sum to 1, so strength stays 0..100 and one overstocked
        /// category cannot carry a hollow force.
        /// </summary>
        public float share;

        /// <summary>Treasury per unit.</summary>
        public float unitCost;

        /// <summary>Months from order to delivery. Steel takes time; people take less.</summary>
        public int leadMonths;

        /// <summary>Smallest sensible order, so procurement is not one aircraft at a time.</summary>
        public float orderIncrement;
    }

    /// <summary>
    /// The authored equipment list (GDD §19, amended August 2026).
    ///
    /// **Why counts exist at all.** §19 originally said force structure is
    /// "branches and enablers rather than individual vehicle counts", and that
    /// rule was right about the thing it was protecting: a game that asks you to
    /// manage individual airframes is a logistics spreadsheet, not a strategy
    /// simulation. Operations still resolve against *branch strength*, so that
    /// protection holds.
    ///
    /// What the rule got wrong is the information layer. "Air strength 68" is not
    /// something an operator can reason about — it has no units, no comparison
    /// class, and no relationship to the decision in front of them. "912 fighters
    /// against their 340" is a fact you can act on, and deciding whether to buy
    /// bombers or tankers is a real strategic choice that a single aggregate
    /// number cannot express.
    ///
    /// So: **counts are authoritative and strength is derived from them.** You
    /// buy in counts, you lose in counts, you see the enemy in counts (through
    /// the fog) — and the resolution model underneath is unchanged.
    /// </summary>
    public static class AssetCatalog
    {
        static readonly AssetProfile[] profiles =
        {
            // ---------------- air ----------------
            new AssetProfile
            {
                kind = AssetKind.Fighters, branch = ForceBranch.Air,
                displayName = "Fighter aircraft", label = "FIGHTERS",
                baselineAt100 = 1200f, share = 0.30f,
                unitCost = 0.9f, leadMonths = 8, orderIncrement = 25f
            },
            new AssetProfile
            {
                kind = AssetKind.Bombers, branch = ForceBranch.Air,
                displayName = "Strategic bombers", label = "BOMBERS",
                baselineAt100 = 180f, share = 0.18f,
                unitCost = 4.5f, leadMonths = 14, orderIncrement = 4f
            },
            new AssetProfile
            {
                kind = AssetKind.Drones, branch = ForceBranch.Air,
                displayName = "Uncrewed aircraft", label = "DRONES",
                baselineAt100 = 800f, share = 0.12f,
                unitCost = 0.22f, leadMonths = 4, orderIncrement = 50f
            },
            new AssetProfile
            {
                kind = AssetKind.Tankers, branch = ForceBranch.Air,
                displayName = "Aerial refuelling", label = "TANKERS",
                baselineAt100 = 450f, share = 0.10f,
                unitCost = 1.6f, leadMonths = 10, orderIncrement = 10f
            },
            new AssetProfile
            {
                kind = AssetKind.AirMissiles, branch = ForceBranch.Air,
                displayName = "Air-launched munitions", label = "MISSILES",
                baselineAt100 = 3000f, share = 0.15f,
                unitCost = 0.06f, leadMonths = 3, orderIncrement = 200f
            },
            new AssetProfile
            {
                kind = AssetKind.AirCrew, branch = ForceBranch.Air,
                displayName = "Air personnel", label = "PERSONNEL",
                baselineAt100 = 400000f, share = 0.15f,
                unitCost = 0.0016f, leadMonths = 6, orderIncrement = 10000f
            },

            // ---------------- naval ----------------
            new AssetProfile
            {
                kind = AssetKind.Carriers, branch = ForceBranch.Naval,
                displayName = "Carriers", label = "CARRIERS",
                baselineAt100 = 12f, share = 0.22f,
                unitCost = 130f, leadMonths = 48, orderIncrement = 1f
            },
            new AssetProfile
            {
                kind = AssetKind.Submarines, branch = ForceBranch.Naval,
                displayName = "Submarines", label = "SUBMARINES",
                baselineAt100 = 70f, share = 0.20f,
                unitCost = 24f, leadMonths = 30, orderIncrement = 1f
            },
            new AssetProfile
            {
                kind = AssetKind.Destroyers, branch = ForceBranch.Naval,
                displayName = "Destroyers", label = "DESTROYERS",
                baselineAt100 = 90f, share = 0.16f,
                unitCost = 14f, leadMonths = 24, orderIncrement = 1f
            },
            new AssetProfile
            {
                kind = AssetKind.Cruisers, branch = ForceBranch.Naval,
                displayName = "Cruisers", label = "CRUISERS",
                baselineAt100 = 25f, share = 0.08f,
                unitCost = 20f, leadMonths = 28, orderIncrement = 1f
            },
            new AssetProfile
            {
                kind = AssetKind.CombatShips, branch = ForceBranch.Naval,
                displayName = "Littoral combat ships", label = "COMBAT SHIPS",
                baselineAt100 = 140f, share = 0.12f,
                unitCost = 5f, leadMonths = 16, orderIncrement = 2f
            },
            new AssetProfile
            {
                kind = AssetKind.NavalMissiles, branch = ForceBranch.Naval,
                displayName = "Ship-launched munitions", label = "MISSILES",
                baselineAt100 = 2200f, share = 0.12f,
                unitCost = 0.09f, leadMonths = 4, orderIncrement = 150f
            },
            new AssetProfile
            {
                kind = AssetKind.NavalCrew, branch = ForceBranch.Naval,
                displayName = "Naval personnel", label = "PERSONNEL",
                baselineAt100 = 340000f, share = 0.10f,
                unitCost = 0.0016f, leadMonths = 8, orderIncrement = 10000f
            },

            // ---------------- ground ----------------
            new AssetProfile
            {
                kind = AssetKind.Tanks, branch = ForceBranch.Ground,
                displayName = "Main battle tanks", label = "TANKS",
                baselineAt100 = 6000f, share = 0.30f,
                unitCost = 0.5f, leadMonths = 9, orderIncrement = 50f
            },
            new AssetProfile
            {
                kind = AssetKind.Helicopters, branch = ForceBranch.Ground,
                displayName = "Rotary aviation", label = "HELICOPTERS",
                baselineAt100 = 1400f, share = 0.14f,
                unitCost = 0.7f, leadMonths = 7, orderIncrement = 20f
            },
            new AssetProfile
            {
                kind = AssetKind.GroundMissiles, branch = ForceBranch.Ground,
                displayName = "Land-based munitions", label = "MISSILES",
                baselineAt100 = 2600f, share = 0.16f,
                unitCost = 0.05f, leadMonths = 3, orderIncrement = 200f
            },
            new AssetProfile
            {
                kind = AssetKind.Soldiers, branch = ForceBranch.Ground,
                displayName = "Soldiers", label = "SOLDIERS",
                baselineAt100 = 900000f, share = 0.40f,
                unitCost = 0.0014f, leadMonths = 5, orderIncrement = 25000f
            }
        };

        static Dictionary<AssetKind, AssetProfile> byKind;

        public static IReadOnlyList<AssetProfile> All => profiles;

        public static AssetProfile For(AssetKind kind)
        {
            if (byKind == null)
            {
                byKind = new Dictionary<AssetKind, AssetProfile>();
                foreach (var profile in profiles) byKind[profile.kind] = profile;
            }
            return byKind.TryGetValue(kind, out var found) ? found : null;
        }

        public static List<AssetProfile> InBranch(ForceBranch branch)
        {
            var result = new List<AssetProfile>();
            foreach (var profile in profiles)
                if (profile.branch == branch) result.Add(profile);
            return result;
        }

        // ---------- strength is a mirror of the inventory ----------

        /// <summary>
        /// Branch strength implied by what the branch actually holds.
        ///
        /// The direction of authority matters: counts are the truth and strength
        /// is computed, not the other way round. If strength were authoritative
        /// and counts cosmetic, buying two hundred fighters could leave the force
        /// exactly as strong as before — which is the "written but never read"
        /// bug wearing an expensive costume.
        ///
        /// A category can exceed its baseline and contribute more, capped at
        /// 1.6× so an army of nothing but missiles is not a functioning army.
        /// </summary>
        public static float StrengthFrom(ForceInventory inventory, ForceBranch branch)
        {
            if (inventory == null) return 0f;

            float total = 0f;
            foreach (var profile in InBranch(branch))
            {
                if (profile.baselineAt100 <= 0f) continue;
                float ratio = inventory.CountOf(profile.kind) / profile.baselineAt100;
                total += profile.share * Math.Min(1.6f, ratio);
            }
            return Clamp(total * 100f);
        }

        /// <summary>
        /// Fill an inventory so that it implies a given strength — used at world
        /// creation, for a seceding state, and by the save migration that has a
        /// strength but no inventory behind it.
        /// </summary>
        public static void FillToStrength(ForceInventory inventory, ForceBranch branch, float strength)
        {
            if (inventory == null) return;
            float fraction = Clamp(strength) / 100f;

            foreach (var profile in InBranch(branch))
            {
                var stock = inventory.Ensure(profile.kind);
                stock.count = profile.baselineAt100 * fraction;
            }
        }

        /// <summary>
        /// Scale every stock in a branch by the same factor. This is how a loss
        /// that was computed in the aggregate becomes a loss of actual things —
        /// keeping counts and strength honest with each other without every
        /// attrition site having to know what a tanker is.
        /// </summary>
        public static void Scale(ForceInventory inventory, ForceBranch branch, float factor)
        {
            if (inventory == null) return;
            factor = Math.Max(0f, factor);

            foreach (var profile in InBranch(branch))
            {
                var stock = inventory.Get(profile.kind);
                if (stock == null) continue;
                stock.count = Math.Max(0f, stock.count * factor);
            }
        }

        /// <summary>Treasury to buy this many of something.</summary>
        public static float CostOf(AssetKind kind, float count)
            => (For(kind)?.unitCost ?? 1f) * Math.Max(0f, count);

        /// <summary>A count written the way a person would read it.</summary>
        public static string Format(float count)
        {
            if (count >= 1000000f) return $"{count / 1000000f:F1}M";
            if (count >= 10000f) return $"{count / 1000f:F0}K";
            if (count >= 1000f) return $"{count:N0}";
            if (count >= 10f) return $"{count:F0}";
            return $"{count:F0}";
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

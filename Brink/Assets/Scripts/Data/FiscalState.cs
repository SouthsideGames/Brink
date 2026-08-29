using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// How the government funds itself (spec 25 Tranche A).
    ///
    /// Append-only, persisted by ordinal. `Balanced` is declared first so its
    /// ordinal is 0 and a save written before this existed lands on neutral
    /// rather than on a posture nobody chose — the same reasoning that put
    /// `CivicPosture.Standard` first.
    /// </summary>
    public enum BudgetPosture
    {
        Balanced = 0,

        /// <summary>Books first. Buys solvency with living standards and unrest.</summary>
        Austerity = 1,

        /// <summary>Spend through it. Buys growth and standards with the balance.</summary>
        Expansionary = 2
    }

    /// <summary>A standing payment propping up one sector. Renewed or it fades.</summary>
    [Serializable]
    public class SectorSubsidy
    {
        public EconomicSector sector;

        /// <summary>0..100. Drives both the monthly bill and the health it buys.</summary>
        public float level;
    }

    /// <summary>
    /// The fiscal layer (spec 02 §9, spec 25 §4).
    ///
    /// The economy pillar had six operator verbs and **no instrument of public
    /// finance at all**: no tax, no budget, no borrowing, no reserves, no credit
    /// standing. `EconomyState.debtToGdp` was written in one place and read in
    /// three, and nothing the operator could do moved it. Treasury income was a
    /// flat share of GDP whatever the government did.
    ///
    /// That is also why the playtest's fiscal finding was left open as "an
    /// income-scale question": a belligerent mid-tier state ended a decade
    /// several thousand in the red and the operator had no lever to answer with.
    /// Adding the levers is the precondition for tuning income at all.
    /// </summary>
    [Serializable]
    public class FiscalState
    {
        /// <summary>
        /// Share of the economy the state takes, 0..100. `BaselineTaxRate` is
        /// revenue-neutral against the old flat model, so an untouched save and
        /// a new world both start exactly where the measured balance table was
        /// taken.
        /// </summary>
        public float taxRate = BaselineTaxRate;

        public BudgetPosture budgetPosture;

        /// <summary>
        /// The stock of public debt, in treasury units. **This is now the
        /// authoritative figure**; `EconomyState.debtToGdp` is derived from it
        /// (`DebtToGdp`), the way `BranchForce.strength` mirrors the inventory
        /// rather than competing with it.
        /// </summary>
        public float sovereignDebt;

        /// <summary>
        /// 0..100, derived monthly from debt, growth, confidence, war and any
        /// recent restructuring. Gates and prices further borrowing. Not a store
        /// the operator writes — see `FiscalSystem.CreditTarget`.
        /// </summary>
        public float creditStanding = 65f;

        /// <summary>
        /// Months of penance left after a restructuring. Long, because the
        /// memory of a default is what makes writing debt down a real decision
        /// rather than a free reset.
        /// </summary>
        public int restructuringMemoryMonths;

        // ---- strategic reserves ----
        //
        // Buffers, not endowments. They raise the floor that sanctions and war
        // can push a resource's *target* to, and deplete while doing it — so a
        // reserve buys a country time to answer pressure rather than immunity
        // from it, and running one down is a decision with a bottom.

        public float energyReserve;
        public float materialsReserve;
        public float foodReserve;

        public List<SectorSubsidy> subsidies = new List<SectorSubsidy>();

        /// <summary>The revenue-neutral rate. See `taxRate`.</summary>
        public const float BaselineTaxRate = 35f;

        public float SubsidyFor(EconomicSector sector)
        {
            for (int i = 0; i < subsidies.Count; i++)
                if (subsidies[i].sector == sector) return subsidies[i].level;
            return 0f;
        }

        public void AddSubsidy(EconomicSector sector, float amount)
        {
            for (int i = 0; i < subsidies.Count; i++)
                if (subsidies[i].sector == sector)
                {
                    subsidies[i].level = Math.Min(100f, subsidies[i].level + amount);
                    return;
                }
            subsidies.Add(new SectorSubsidy { sector = sector, level = Math.Min(100f, amount) });
        }

        public bool HasRestructured => restructuringMemoryMonths > 0;
    }
}

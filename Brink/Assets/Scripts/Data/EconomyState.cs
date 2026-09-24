using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>Economic sectors (GDD §20). Kept to the strategically meaningful set.</summary>
    public enum EconomicSector
    {
        Energy,
        Agriculture,
        Industry,
        Technology,
        Finance,
        Consumer,
        Defense
    }

    /// <summary>
    /// How far an industrial programme goes (GDD §20 amendment).
    ///
    /// Append-only — persisted by ordinal. `Maintenance` is first so an old save
    /// with a zeroed value lands on the cheapest, least consequential option
    /// rather than on a three-year rebuild nobody ordered.
    /// </summary>
    public enum IndustrialScale
    {
        Maintenance = 0,
        Expansion = 1,
        Modernisation = 2
    }

    /// <summary>Capacity under construction. See `IndustrialSystem`.</summary>
    [Serializable]
    public class IndustrialProgramme
    {
        public EconomicSector sector;
        public IndustrialScale scale;
        public int monthsRemaining;
        public GameDate started;
        /// <summary>Empty for legacy/national work; otherwise an energy-region project.</summary>
        public string locationId;
        /// <summary>Ring-fenced foreign grant, consumed by paid work; zero in older saves.</summary>
        public float externalFunding;
    }

    /// <summary>One sector's condition. Output is capacity; health is current functioning.</summary>
    [Serializable]
    public class SectorState
    {
        public EconomicSector sector;
        public float output; // 0..100 share-weighted capacity
        public float health; // 0..100 disruption-free functioning
    }

    /// <summary>
    /// Layered macroeconomy (GDD §20): growth, inflation, employment, debt,
    /// confidence, plus the sector layer and the fictional National Market Index.
    /// </summary>
    [Serializable]
    public class EconomyState
    {
        public float gdp;
        public float growthRate;   // annualized %, can go negative
        public float inflation;    // annualized %
        public float unemployment; // %
        public float debtToGdp;    // %
        public float confidence = 50f; // 0..100

        /// <summary>National Market Index level; 100 = world creation baseline.</summary>
        public float marketIndex = 100f;

        /// <summary>Recent index history for ASCII charting, oldest first.</summary>
        public List<float> marketHistory = new List<float>();

        public List<SectorState> sectors = new List<SectorState>();

        /// <summary>
        /// Capacity being built (GDD §20 amendment). See `IndustrialSystem` —
        /// the economy pillar's recurring decision and its only treasury sink.
        /// </summary>
        public List<IndustrialProgramme> programmes = new List<IndustrialProgramme>();

        /// <summary>One sector by kind, or null.</summary>
        public SectorState Sector(EconomicSector kind)
        {
            foreach (var sector in sectors)
                if (sector.sector == kind) return sector;
            return null;
        }

        public const int MaxHistory = 60;

        public SectorState GetSector(EconomicSector sector)
        {
            for (int i = 0; i < sectors.Count; i++)
                if (sectors[i].sector == sector) return sectors[i];
            return null;
        }

        public void RecordMarket()
        {
            marketHistory.Add(marketIndex);
            if (marketHistory.Count > MaxHistory)
                marketHistory.RemoveRange(0, marketHistory.Count - MaxHistory);
        }

        /// <summary>Consecutive months of negative growth. Zero when growing.</summary>
        public int contractionMonths;

        /// <summary>
        /// True after two consecutive contracting months.
        ///
        /// Two, not one: a single negative month is noise, and chronicling it
        /// would fill the historical record with a recession that started and
        /// ended in the same quarter.
        /// </summary>
        public bool InRecession => contractionMonths >= 2;

        /// <summary>Twelve months of contraction is no longer a downturn.</summary>
        public bool InDepression => contractionMonths >= 12;
    }
}

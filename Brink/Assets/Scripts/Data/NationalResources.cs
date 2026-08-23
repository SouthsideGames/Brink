using System;

namespace Brink.Data
{
    /// <summary>
    /// Core national resources (GDD §10.1). Compact headline model;
    /// specific dependencies (oil, grain, ...) surface later beneath these.
    /// </summary>
    [Serializable]
    public class NationalResources
    {
        public float treasury;           // government financial capacity
        public float manpower;           // available/recruitable human capacity

        /// <summary>
        /// The recruitable base this country returns to. Wars draw manpower down;
        /// a population recovers toward this over years rather than being spent
        /// once and gone forever. Zero on a save written before this existed —
        /// `EconomySystem` seeds it from current manpower on the first tick.
        /// </summary>
        public float manpowerBaseline;

        /// <summary>
        /// The energy and materials position this country naturally returns to —
        /// its authored endowment (spec 08). Recovery approaches these rather
        /// than climbing to 100 for everyone, which is what makes an
        /// energy-dependent archetype stay energy-dependent. Zero on saves
        /// written before they existed; seeded from current values on first tick.
        /// </summary>
        public float energyEndowment;
        public float materialsEndowment;

        public float energy;             // fuel/electricity security (0..100)
        public float industrialCapacity; // ability to produce, build, maintain (0..100)
        public float strategicMaterials; // aggregated critical materials (0..100)
        public float foodSecurity;       // reliable ability to feed the population (0..100)
    }
}

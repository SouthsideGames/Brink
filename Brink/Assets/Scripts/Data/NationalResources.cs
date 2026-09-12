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

        /// <summary>
        /// The food position this country naturally returns to. Seeded from
        /// current food security on the first tick of an old save, like the two
        /// above. Until this existed, `foodSecurity` was written once at world
        /// creation and never moved again — the last authored stat with no
        /// monthly behaviour at all.
        /// </summary>
        public float foodEndowment;

        /// <summary>
        /// The industrial base this country returns to — what its plant and
        /// workforce can hold, the fourth resource to get the endowment idiom
        /// (2026-09). Until it existed `industrialCapacity` had drains (civil
        /// conflict, bombing, sabotage, a lost or contested works) and only
        /// *deliberate* builders (programmes, procurement, a capability), so
        /// a non-player state whose plant was destroyed never rebuilt it, and
        /// a territory swing applied as a rate ran the figure to zero. Raised
        /// by everything that builds (`EconomySystem.BuildIndustry`); seeded
        /// from the authored profile on the first tick of an old save.
        /// </summary>
        public float industrialEndowment;

        public float energy;             // fuel/electricity security (0..100)
        public float industrialCapacity; // ability to produce, build, maintain (0..100)
        public float strategicMaterials; // aggregated critical materials (0..100)
        public float foodSecurity;       // reliable ability to feed the population (0..100)
    }
}

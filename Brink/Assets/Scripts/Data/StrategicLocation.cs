using System;

namespace Brink.Data
{
    /// <summary>
    /// Meaningful strategic targets only — capitals, ports, airbases, industrial
    /// and energy centers, passes — never every city (GDD §19, §16).
    /// </summary>
    public enum LocationType
    {
        Capital,
        Port,
        Airbase,
        IndustrialCenter,
        EnergyRegion,
        MountainPass,
        Chokepoint,

        /// <summary>
        /// A mining or extraction region supplying strategic materials
        /// (GDD §16, §20).
        ///
        /// Appended, never inserted — the enum is persisted by ordinal and every
        /// saved location carries one.
        ///
        /// It existed as a gap rather than a decision: strategic materials are a
        /// headline national resource that industry, procurement and every
        /// research programme draw on, and there was nowhere on the map that
        /// produced them. The Pilbara was filed as an industrial centre and the
        /// Caspian belt as an energy region, so taking either gave you the wrong
        /// commodity. A resource you can be starved of has to be a resource you
        /// can be starved *from*.
        /// </summary>
        MaterialsRegion
    }

    /// <summary>
    /// A strategic location on the world map. Ownership can change through war
    /// (GDD §16: borders may change through war, secession, annexation).
    /// </summary>
    [Serializable]
    public class StrategicLocation
    {
        public string id;
        public string displayName;
        public LocationType type;
        public string ownerId;         // country id currently holding it
        public string originalOwnerId; // who held it at world creation

        /// <summary>
        /// A foreign power operating from this location **by agreement** — basing
        /// rights, not occupation. Empty when nobody does.
        ///
        /// This is a different fact from <see cref="ownerId"/>: the host keeps
        /// sovereignty and can revoke it, but a partner's presence changes what
        /// both of them can reach. It is also one of the more valuable things
        /// intelligence can tell you about a country, which is why the map only
        /// reveals it with real collection behind it (GDD §14, §16).
        /// </summary>
        public string foreignOperatorId;

        public float defenseValue;     // 0..100 terrain/fortification advantage
        public float strategicValue;   // 0..100 what losing it costs the owner

        /// <summary>Completed energy works stay with the ground. False in old saves.</summary>
        public bool energyWorks;

        /// <summary>Garrison strength currently holding the location, 0..100.</summary>
        public float garrison;

        /// <summary>
        /// 0..100 how reconciled the population is to whoever holds this ground
        /// (GDD §19). Only meaningful while the location is occupied.
        ///
        /// Counter-insurgency raises it and occupation drag falls with it, which
        /// is what makes holding conquered ground a campaign rather than a line
        /// item. Zero is correct for an old save and for ground nobody has taken,
        /// so this needs no migration: unpacified is the honest default.
        /// </summary>
        public float pacification;

        public bool IsOccupied => ownerId != originalOwnerId;

        /// <summary>True when a partner operates from here with the host's consent.</summary>
        public bool HasForeignBase => !string.IsNullOrEmpty(foreignOperatorId);

        /// <summary>Whether this location can host a foreign partner at all.</summary>
        public bool SupportsBasing =>
            type == LocationType.Airbase || type == LocationType.Port || type == LocationType.MountainPass;

        /// <summary>Short terminal code, e.g. "CAP" / "PRT".</summary>
        public string TypeCode
        {
            get
            {
                switch (type)
                {
                    case LocationType.Capital: return "CAP";
                    case LocationType.Port: return "PRT";
                    case LocationType.Airbase: return "AIR";
                    case LocationType.IndustrialCenter: return "IND";
                    case LocationType.EnergyRegion: return "ENR";
                    case LocationType.Chokepoint: return "CHK";
                    case LocationType.MaterialsRegion: return "MAT";
                    default: return "PAS";
                }
            }
        }
    }
}

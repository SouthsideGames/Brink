using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// How a country came by a capability. Matters because knowledge acquired by
    /// theft or observation is shallower than knowledge you developed yourself
    /// (GDD §11).
    /// </summary>
    public enum CapabilitySource
    {
        Developed,   // our own research programme
        Shared,      // transferred by a partner
        Observed,    // learned through exercises and contact
        Stolen       // acquired through espionage
    }

    /// <summary>
    /// A capability a country holds. Technology unlocks what a state *can do*;
    /// it never hands over force structure, trained people, infrastructure or
    /// money (GDD §11).
    /// </summary>
    [Serializable]
    public class HeldCapability
    {
        public string capabilityId;
        public CapabilitySource source;
        public GameDate acquired;

        /// <summary>
        /// 0..100 depth of mastery. Developed knowledge starts deep; stolen
        /// knowledge starts shallow and matures slowly through use.
        /// </summary>
        public float maturity;
    }

    /// <summary>A funded research programme working toward one capability.</summary>
    [Serializable]
    public class ResearchProgram
    {
        public string capabilityId;
        public string label;
        public Pillar pillar;
        public int monthsRemaining;
        public float monthlyCost;
    }

    /// <summary>National research and capability holdings.</summary>
    [Serializable]
    public class TechnologyState
    {
        public List<HeldCapability> capabilities = new List<HeldCapability>();
        public List<ResearchProgram> programs = new List<ResearchProgram>();

        public HeldCapability Find(string capabilityId)
        {
            for (int i = 0; i < capabilities.Count; i++)
                if (capabilities[i].capabilityId == capabilityId) return capabilities[i];
            return null;
        }

        public bool Has(string capabilityId) => Find(capabilityId) != null;

        public bool IsResearching(string capabilityId)
        {
            for (int i = 0; i < programs.Count; i++)
                if (programs[i].capabilityId == capabilityId) return true;
            return false;
        }
    }
}

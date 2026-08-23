using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// The decisive strategic instrument of each pillar (GDD §21). These are not
    /// generic ultimate buttons: each requires capability, preparation and
    /// favourable conditions, and each creates systemic consequences that outlast
    /// the moment it is used.
    /// </summary>
    public enum EndgameType
    {
        StrategicDestruction,   // Military — strategic-force devastation
        SystemicCollapse,       // Economy — banking, currency and confidence failure
        StateDestabilization,   // Intelligence — institutional fracture
        StrategicIsolation,     // Diplomacy — removal of allies, access and legitimacy
        TotalMobilization       // Government — extraordinary domestic mobilization
    }

    /// <summary>
    /// Strategic severity (GDD §21). Existential non-military action can justify
    /// a military response in the target's own strategic logic.
    /// </summary>
    public enum StrategicSeverity
    {
        Routine,
        Pressure,
        Coercive,
        Severe,
        Existential
    }

    /// <summary>An executed endgame, archived because the world remembers.</summary>
    [Serializable]
    public class EndgameRecord
    {
        public GameDate date;
        public EndgameType type;
        public string actorId;
        public string targetId;      // empty for Total Mobilization
        public StrategicSeverity severity;
        public string summary;
    }

    /// <summary>National preparation toward a decisive instrument.</summary>
    [Serializable]
    public class EndgameState
    {
        /// <summary>Months of dedicated preparation, by endgame type.</summary>
        public List<EndgamePreparation> preparations = new List<EndgamePreparation>();

        /// <summary>Set while the state is under extraordinary mobilization.</summary>
        public bool totalMobilization;
        public int mobilizationMonthsRemaining;

        public EndgamePreparation Find(EndgameType type)
        {
            for (int i = 0; i < preparations.Count; i++)
                if (preparations[i].type == type) return preparations[i];
            return null;
        }

        public float ProgressFor(EndgameType type) => Find(type)?.progress ?? 0f;
    }

    /// <summary>Progress toward being able to use one decisive instrument.</summary>
    [Serializable]
    public class EndgamePreparation
    {
        public EndgameType type;

        /// <summary>0..100. Only a fully prepared instrument can be used.</summary>
        public float progress;

        public bool everUsed;
    }
}

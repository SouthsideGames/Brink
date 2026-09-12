using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// What a mandate asks of a posting. Each kind is a claim the ten-year
    /// review can test against the world; none is a conquest checklist (GDD §25).
    /// </summary>
    public enum MandateObjectiveKind
    {
        PillarAtLeast,
        TreatiesAtLeast,
        HoldOriginalGround,
        GdpGrowthAtLeast,
        StabilityAtLeast,
        ApprovalAtLeast,
        UnityAtLeast,
        EnergyAtLeast,
        FoodAtLeast,
        MaterialsAtLeast,
        IndustryAtLeast,
        WarsWonWithoutLoss,
        NoWarLost,
        InstrumentUsed,
        RelationsAtLeast,
        RelationsAtMost,
        CapabilitiesAtLeast,
        Solvent,
        ConstitutionalOrderKept
    }

    [Serializable]
    public class MandateObjective
    {
        public MandateObjectiveKind kind;
        public string text;
        public float threshold;
        public string param = "";
    }

    [Serializable]
    public class Mandate
    {
        public string title;
        public string brief;
        public List<MandateObjective> objectives = new List<MandateObjective>();
        public int reviewMonths = 120;
        public float startGdp;
        public List<string> startLocationIds = new List<string>();
        public string startGovernmentType = "";

        /// <summary>
        /// The operator's answer to the mandate: a persistent doctrine, national
        /// policy choices and self-authored objectives (Phase B). Kept on the
        /// posting rather than in global/meta state so a mandate can be reissued
        /// without erasing the strategy chosen to answer it. Null on an old save
        /// is valid; StrategySystem creates it lazily.
        /// </summary>
        public StrategicPlan strategy;
    }

    public enum MandateVerdict
    {
        Pending,
        Fulfilled,
        Held,
        Failed
    }

    [Serializable]
    public class MandateRecord
    {
        public GameDate date;
        public MandateVerdict verdict = MandateVerdict.Pending;
        public int met;
        public int total;
        public string summary = "";
    }
}
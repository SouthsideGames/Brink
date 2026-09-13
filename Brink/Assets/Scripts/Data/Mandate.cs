using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// What a mandate asks of a posting. Each kind is a claim the ten-year
    /// review can test against the world; none is a conquest checklist (GDD §25).
    /// Append only: these values are serialized by ordinal.
    /// </summary>
    public enum MandateObjectiveKind
    {
        /// <summary>A pillar at or above threshold; param names the pillar.</summary>
        PillarAtLeast,
        /// <summary>Unbroken treaties held with at least threshold states.</summary>
        TreatiesAtLeast,
        /// <summary>Every location held at the start of the posting is still held.</summary>
        HoldOriginalGround,
        /// <summary>GDP growth versus the posting's captured start GDP.</summary>
        GdpGrowthAtLeast,
        StabilityAtLeast,
        ApprovalAtLeast,
        UnityAtLeast,
        EnergyAtLeast,
        FoodAtLeast,
        MaterialsAtLeast,
        IndustryAtLeast,
        /// <summary>At least threshold wars won, with no recorded loss.</summary>
        WarsWonWithoutLoss,
        NoWarLost,
        InstrumentUsed,
        /// <summary>Relationship with param country at or above threshold.</summary>
        RelationsAtLeast,
        /// <summary>Relationship with param country at or below threshold.</summary>
        RelationsAtMost,
        CapabilitiesAtLeast,
        /// <summary>Fiscal condition is healthy enough to be considered solvent.</summary>
        Solvent,
        /// <summary>The constitutional order captured at posting start remains.</summary>
        ConstitutionalOrderKept
    }

    [Serializable]
    public class MandateObjective
    {
        public MandateObjectiveKind kind;
        /// <summary>The line the operator reads.</summary>
        public string text;
        public float threshold;
        /// <summary>Pillar name, country id, or empty depending on kind.</summary>
        public string param = "";
    }

    /// <summary>
    /// The brief a posting opens with. It states what the government expects to
    /// be true at the ten-year review; the save continues whatever the verdict.
    /// Historical baselines live here because several objective types cannot be
    /// evaluated correctly from current state alone.
    /// </summary>
    [Serializable]
    public class Mandate
    {
        public string title;
        public string brief;
        public List<MandateObjective> objectives = new List<MandateObjective>();

        /// <summary>Months into the posting at which the verdict is delivered.</summary>
        public int reviewMonths = 120;

        // Captured when assigned so historical objective types have a baseline.
        public float startGdp;
        public List<string> startLocationIds = new List<string>();
        public string startGovernmentType = "";

        /// <summary>
        /// The operator's answer to the mandate: persistent doctrine, national
        /// policy choice and self-authored standing objectives (Phase B).
        ///
        /// It belongs to the posting rather than global/meta progression. A
        /// mandate reissue changes what the administration asks for without
        /// erasing the strategy the operator has chosen to pursue. Null on an old
        /// save is valid; StrategySystem creates the neutral plan lazily.
        /// </summary>
        public StrategicPlan strategy;
    }

    public enum MandateVerdict
    {
        Pending,
        /// <summary>Every objective met.</summary>
        Fulfilled,
        /// <summary>Most objectives met.</summary>
        Held,
        /// <summary>The posting did not deliver enough of the brief.</summary>
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
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
        /// <summary>A pillar at or above <c>threshold</c>. <c>param</c> names the pillar.</summary>
        PillarAtLeast,
        /// <summary>Unbroken treaties held with at least <c>threshold</c> states.</summary>
        TreatiesAtLeast,
        /// <summary>Every location held at the start of the posting is still held.</summary>
        HoldOriginalGround,
        /// <summary>GDP has grown by at least <c>threshold</c> percent since the posting began.</summary>
        GdpGrowthAtLeast,
        /// <summary>Stability at or above <c>threshold</c>.</summary>
        StabilityAtLeast,
        /// <summary>Government approval at or above <c>threshold</c>.</summary>
        ApprovalAtLeast,
        /// <summary>National unity at or above <c>threshold</c>.</summary>
        UnityAtLeast,
        /// <summary>The energy resource at or above <c>threshold</c>.</summary>
        EnergyAtLeast,
        /// <summary>Food security at or above <c>threshold</c>.</summary>
        FoodAtLeast,
        /// <summary>Strategic materials at or above <c>threshold</c>.</summary>
        MaterialsAtLeast,
        /// <summary>Industrial capacity at or above <c>threshold</c>.</summary>
        IndustryAtLeast,
        /// <summary>At least <c>threshold</c> wars won, and none lost.</summary>
        WarsWonWithoutLoss,
        /// <summary>No war lost.</summary>
        NoWarLost,
        /// <summary>A strategic instrument used by this posting.</summary>
        InstrumentUsed,
        /// <summary>Relations with the state named in <c>param</c> at or above <c>threshold</c>.</summary>
        RelationsAtLeast,
        /// <summary>Relations with the state named in <c>param</c> at or below <c>threshold</c> — a rival kept at arm's length, never courted.</summary>
        RelationsAtMost,
        /// <summary>Holds at least <c>threshold</c> capabilities.</summary>
        CapabilitiesAtLeast,
        /// <summary>The treasury is not in the red.</summary>
        Solvent,
        /// <summary>The government in place at the start is still the constitutional order — no coup.</summary>
        ConstitutionalOrderKept
    }

    [Serializable]
    public class MandateObjective
    {
        public MandateObjectiveKind kind;
        /// <summary>The line the operator reads. Written in the terminal's voice.</summary>
        public string text;
        public float threshold;
        /// <summary>Pillar name, country id, or empty — depends on <see cref="kind"/>.</summary>
        public string param = "";
    }

    /// <summary>
    /// The brief a posting opens with (GDD §25 amendment, 2026-08).
    ///
    /// The game had annual grades, a forty-year tenure review and five strategic
    /// instruments, and nothing that ever said what the operator was there to
    /// do. A mandate is three to four claims about the world the government
    /// expects to be true after ten years — drawn from the country's authored
    /// character and vulnerability (spec 08), never from conquest — and a
    /// verdict on them at the review. Everything else in the game is a way of
    /// getting there.
    /// </summary>
    [Serializable]
    public class Mandate
    {
        public string title;
        public string brief;
        public List<MandateObjective> objectives = new List<MandateObjective>();

        /// <summary>Months into the posting at which the verdict is delivered.</summary>
        public int reviewMonths = 120;

        // Captured when the mandate is assigned, so growth claims have a base.
        public float startGdp;
        public List<string> startLocationIds = new List<string>();
        public string startGovernmentType = "";
    }

    public enum MandateVerdict
    {
        Pending,
        /// <summary>Every objective met.</summary>
        Fulfilled,
        /// <summary>Most objectives met: the posting held its ground.</summary>
        Held,
        /// <summary>The posting did not deliver.</summary>
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

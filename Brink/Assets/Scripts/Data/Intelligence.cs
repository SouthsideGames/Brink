using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>Collection domains a network can prioritize (GDD §14).</summary>
    public enum IntelDomain
    {
        Military,
        Economic,
        Political,
        Diplomatic
    }

    /// <summary>
    /// Confidence grade attached to every estimate (GDD §14). The player never
    /// sees a bare number for foreign capability — they see a range and a grade.
    /// </summary>
    public enum ConfidenceGrade
    {
        None,
        Low,
        Moderate,
        High,
        Confirmed
    }

    /// <summary>
    /// A collection network one country runs against another. Networks are
    /// country-specific and can prioritize a domain (GDD §14). Keyed by owner so
    /// AI states use the same structure and the same uncertainty as the player.
    /// </summary>
    [Serializable]
    public class IntelNetwork
    {
        public string ownerId;
        public string targetId;
        public IntelDomain focus;

        /// <summary>0..100 depth of access into the target.</summary>
        public float penetration;

        /// <summary>Set when the target's counterintelligence has rolled it up.</summary>
        public bool compromised;

        public int monthsActive;

        /// <summary>
        /// How hard this network has been worked lately, 0..100 (spec 03 §6a).
        ///
        /// Every covert operation raises it and it decays monthly, so running
        /// the same network against the same state again and again gets steadily
        /// harder — the target's services notice a tempo even when they never
        /// catch anybody. This is the diminishing-returns rule that stops one
        /// deep network being an unlimited supply of sabotage, and it is
        /// deliberately separate from `DirectedHardening`, which is about being
        /// *caught*: activity draws attention whether or not it is attributed.
        ///
        /// Zero on an old save is correct — no operations recorded, no attention
        /// drawn — so no migration.
        /// </summary>
        public float operationTempo;
    }

    /// <summary>
    /// One analytical estimate. Holds the reported value and margin — never the
    /// truth — plus how stale it is. Estimates can be wrong through collection
    /// failure, analytical error or deliberate deception (GDD §14).
    /// </summary>
    [Serializable]
    public class IntelEstimate
    {
        public string observerId;
        public string targetId;
        public IntelDomain domain;

        public float reportedValue;
        public float margin;          // ± band around reportedValue
        public ConfidenceGrade confidence;
        public GameDate asOf;
        public bool everCollected;

        /// <summary>True when this estimate was shaped by the target's deception.</summary>
        public bool deceived;

        public string RangeText => $"{Math.Max(0f, reportedValue - margin):F0}–{Math.Min(100f, reportedValue + margin):F0}";
    }

    /// <summary>
    /// Defensive intelligence posture (GDD §14): counterintelligence and
    /// deliberate deception are first-class systems, not modifiers.
    /// </summary>
    [Serializable]
    public class CounterIntelState
    {
        /// <summary>0..100 ability to detect and roll up foreign networks.</summary>
        public float counterIntelligence = 35f;

        /// <summary>
        /// 0..20 lasting procedure learned from catching foreign networks — a
        /// term in the baseline `counterIntelligence` reverts to, not a bump to
        /// the value. Every bump to the value was being erased by the monthly
        /// reversion within months, so a decade of caught operations measurably
        /// taught the world nothing: the value-versus-target trap, in the one
        /// system whose whole subject is learning. Decays on a decade scale —
        /// procedures outlive the scare that wrote them. Zero on old saves is
        /// correct: nothing was caught, nothing was learned.
        /// </summary>
        public float institutionalHardening;

        /// <summary>0..100 strength of active deception programs.</summary>
        public float deceptionStrength;

        /// <summary>Direction of deception: positive overstates capability, negative understates it.</summary>
        public float deceptionBias;

        public IntelDomain deceptionDomain = IntelDomain.Military;
    }
}

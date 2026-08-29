using System;

namespace Brink.Data
{
    /// <summary>
    /// A question a service can be told to answer (spec 03 §10, spec 25 §5.3).
    ///
    /// Every one of these reads state the simulation already computes every
    /// month and which currently reaches the operator through almost nothing —
    /// `AIStrategy.StrategicPath`, `AIPrediction.OpponentModel`,
    /// `EndgameSystem.KnownPreparation`. That is the point: collection has been
    /// buying *sharper numbers* about foreign capability, and this is what makes
    /// it buy an answer to "what is this state for".
    ///
    /// Append-only, persisted by ordinal.
    /// </summary>
    public enum EstimateQuestion
    {
        /// <summary>What are they building toward? → `AIStrategy.StrategicPath`.</summary>
        StrategicIntent = 0,

        /// <summary>Are they preparing an instrument? → `EndgameSystem.KnownPreparation`.</summary>
        StrategicProgramme = 1,

        /// <summary>Will they honour their commitments? → treaties + historical memory.</summary>
        TreatyReliability = 2,

        /// <summary>How do they read us? → `AIPrediction.OpponentModel`.</summary>
        TheirReadOfUs = 3,

        /// <summary>Who is arming the rising on our ground? → insurgency sponsorship.</summary>
        SubversionSponsorship = 4
    }

    /// <summary>
    /// A commissioned finished assessment: a question, months of work, and a
    /// written judgement carrying a confidence grade.
    ///
    /// **It can be wrong**, and that does not breach spec 15's reporting rule.
    /// That rule forbids a *desk* misstating a figure it was handed. An estimate
    /// is uncertain by construction, already carries a grade and a margin, and
    /// has been allowed to be wrong since Phase 6 — this is the fog system
    /// working, not a distorted report.
    /// </summary>
    [Serializable]
    public class IntelProduct
    {
        public string observerId;
        public string targetId;
        public EstimateQuestion question;

        public GameDate commissioned;
        public int monthsRemaining;

        /// <summary>The written judgement. Empty until it is delivered.</summary>
        public string answer = "";

        public ConfidenceGrade confidence;

        /// <summary>
        /// Whether the judgement is actually right. Stored so the answer cannot
        /// change on a later read, and never shown — an operator who could see
        /// this would not need the estimate.
        /// </summary>
        public bool accurate;

        public bool delivered;

        public bool IsFor(string observer, string target, EstimateQuestion kind)
            => observerId == observer && targetId == target && question == kind;
    }
}

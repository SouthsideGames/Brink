using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// One selectable response to an active crisis (GDD §23). Crisis decisions
    /// use this dedicated structure and never consume normal Command Points
    /// (GDD §7.1).
    ///
    /// The four scalars cover the common case. Anything that reaches beyond our
    /// own borders — a relationship, a market, a sanction, a war — goes through
    /// <see cref="effectId"/>, resolved by `CrisisEffects`.
    ///
    /// **Why a named id rather than a delegate.** A live crisis is persisted
    /// inside `GameState.activeCrises`, and `JsonUtility` cannot serialize a
    /// `Func` or an `Action`. An option carrying a lambda would work perfectly
    /// until the operator saved with a crisis open, at which point the choice
    /// would silently lose its consequence. A string survives the round trip and
    /// is inspectable in the save file.
    /// </summary>
    [Serializable]
    public class CrisisOption
    {
        public string label;       // short command, e.g. "MOBILIZE RESPONSE"
        public string description; // consequence hint shown to the operator
        public string resultText;  // chronicle/notification text after choosing

        public float treasuryDelta;
        public float stabilityDelta;
        public float approvalDelta;
        public float unityDelta;

        /// <summary>Named world effect. Empty means the scalars are the whole story.</summary>
        public string effectId = "";

        /// <summary>
        /// Which state the effect acts on, captured when the crisis fired rather
        /// than looked up when it resolves. A crisis that opens by naming China
        /// must not resolve against Russia because relations shifted in between.
        /// </summary>
        public string effectTargetId = "";

        public float effectMagnitude;
    }

    /// <summary>When a given event definition last fired, so it can rest (GDD §23).</summary>
    [Serializable]
    public class EventCooldown
    {
        public string defId;
        public int lastFiredMonth;
    }

    /// <summary>
    /// A live Crisis Turn (GDD §6, §23): interrupts the monthly loop and asks
    /// for a decision.
    ///
    /// It does **not** have to be answered. The month ends regardless, and an
    /// unanswered crisis lapses at a cost to standing — see
    /// `CrisisSystem.LapseUnanswered`. The operator is allowed to fail to
    /// decide, because a government that drifts is a real outcome and a more
    /// interesting one than a wall.
    /// </summary>
    [Serializable]
    public class ActiveCrisis
    {
        public string defId;
        public string title;
        public string body;
        public GameDate startDate;
        public List<CrisisOption> options = new List<CrisisOption>();

        /// <summary>The state this crisis is about, if any. Captured when it fired.</summary>
        public string subjectCountryId = "";

        /// <summary>
        /// What happens in the world if nobody decides (GDD §23).
        ///
        /// Drifting used to cost only standing, which made ignoring a crisis a
        /// cheap way to avoid its consequences — the opposite of the intent. If
        /// the operator will not decide, the situation decides for them.
        /// </summary>
        public string lapseEffectId = "";
        public string lapseTargetId = "";
        public float lapseMagnitude;
    }
}

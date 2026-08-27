using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Visible escalation states (GDD §18.1). These describe the situation; they
    /// do not hard-gate actions. Levels can be skipped in both directions.
    /// </summary>
    public enum EscalationState
    {
        Peace,
        Tension,
        Crisis,
        LimitedConflict,
        TotalWar
    }

    /// <summary>Primary Strategy chosen for a confrontation (GDD §18.2).</summary>
    public enum PrimaryStrategy
    {
        Military,
        Economic,
        IntelligencePolitical,
        Diplomatic
    }

    /// <summary>The concession actually being sought (GDD §18.2).</summary>
    /// <summary>
    /// How a confrontation was judged when it closed (GDD §20 amendment).
    ///
    /// Append-only: persisted by ordinal, and `Stalemate` is first so an old save
    /// with no verdict recorded lands on "undecided" rather than on somebody
    /// having won.
    /// </summary>
    public enum WarVerdict
    {
        Stalemate = 0,
        InitiatorVictory = 1,
        DefenderVictory = 2
    }

    public enum ConfrontationObjective
    {
        TerritorialConcession, // seize/keep a strategic location
        PolicyReversal,        // force a policy change
        ResourceAccess,        // secure access to a resource/route
        Deterrence             // force the opponent to stand down
    }

    /// <summary>Delegated operation directive settings (GDD §19).</summary>
    [Serializable]
    /// <summary>
    /// What we intend to do with ground we take (GDD §19).
    ///
    /// The difference between a war of pressure and a war of conquest, stated as
    /// an order rather than inferred from what happens. Without it every
    /// successful assault annexed, so there was no way to hurt a rival without
    /// also taking their land — and no way to fight a limited war at all.
    /// </summary>
    public enum TerritorialIntent
    {
        /// <summary>Break the position and leave. Ownership never changes.</summary>
        Degrade,

        /// <summary>Take it and keep it.</summary>
        Seize
    }

    [Serializable]
    public class OperationDirective
    {
        public float speedPriority = 50f;      // 0 methodical .. 100 rapid
        public float casualtyTolerance = 50f;  // 0 protect force .. 100 accept losses
        public float civilianRiskLimit = 50f;  // 0 strict .. 100 permissive (GDD §27)

        /// <summary>What to do with the objective once it falls (GDD §19).</summary>
        public TerritorialIntent territorialIntent = TerritorialIntent.Seize;

        /// <summary>
        /// The furthest this operation may carry the confrontation (GDD §19).
        ///
        /// An order can be given without handing over the decision to widen the
        /// war. Escalation still happens — it just stops here, and the operator
        /// has to choose to go further themselves.
        /// </summary>
        public EscalationState escalationLimit = EscalationState.TotalWar;
    }

    /// <summary>One executed operation and its outcome, for the after-action archive.</summary>
    [Serializable]
    public class OperationRecord
    {
        public GameDate date;
        public string locationId;
        public string operationType;
        public bool success;
        public float attackerLosses;
        public float defenderLosses;
        public float civilianHarm;
        public string summary;

        /// <summary>
        /// Combat power multiplier from distance at the time this ran, 0.35..1
        /// (GDD §16). Recorded so the after-action report can say the operation
        /// failed because the force could not be brought to bear, rather than
        /// leaving the player to guess.
        /// </summary>
        public float reachFactor = 1f;

        /// <summary>Where this happened (GDD §16). Recorded for the after-action archive.</summary>
        public Brink.Core.Theatre theatre;

        /// <summary>
        /// The odds the staff assessed before the order was given.
        ///
        /// Recorded because an outcome alone cannot distinguish a sound plan that
        /// was unlucky from a plan that was never going to work — and without
        /// that distinction the only available strategy is to repeat the attempt
        /// until the dice land.
        /// </summary>
        public float oddsAtOrder;

        /// <summary>
        /// Why it went the way it did, ranked by what mattered, with one line on
        /// what would change the result (GDD §28.1).
        /// </summary>
        public string explanation = "";
    }

    /// <summary>
    /// An active confrontation between two states (GDD §18). Tracks the
    /// objective, chosen strategy, escalation (visible and hidden pressure),
    /// momentum and the accumulated costs that decide whether either side
    /// still prefers fighting to settling.
    /// </summary>
    [Serializable]
    public class Confrontation
    {
        public string id;
        public string initiatorId;
        public string defenderId;

        public ConfrontationObjective objective;
        public string objectiveLocationId; // for territorial objectives
        public PrimaryStrategy primaryStrategy;

        /// <summary>
        /// Which theatre this war belongs to (GDD §16). Captured when it opens so
        /// the name a player learns it by stays the name it had, and so that
        /// commitment accounting does not have to re-derive geography every tick.
        /// </summary>
        public Brink.Core.Theatre theatre;

        public EscalationState escalation = EscalationState.Tension;

        /// <summary>Hidden pressure underlying the visible state (GDD §18.1).</summary>
        public float escalationPressure;

        public GameDate startDate;
        public int monthsActive;

        /// <summary>Positive favors the initiator; drives settlement willingness.</summary>
        public float momentum;

        // Accumulated costs — a player can win battles and lose the confrontation.
        public float initiatorWarExhaustion;
        public float defenderWarExhaustion;
        public float initiatorCasualties;
        public float defenderCasualties;
        public float civilianHarmTotal;

        public bool resolved;
        public string outcomeSummary = "";

        /// <summary>
        /// Who won, decided by measuring the world rather than by which code path
        /// happened to close the war (GDD §20 amendment).
        ///
        /// `Close` used to take an `objectiveAchieved` bool from its caller —
        /// `CloseWithSettlement` always passed true and conceding always passed
        /// false — so "did we win" was a property of *how the war ended*, not of
        /// *what it achieved*. It was never stored either, only used to pick a
        /// notification headline, so nothing could ever look back and say what a
        /// decade of fighting had come to.
        ///
        /// Stalemate is deliberately a real outcome and the default. Most wars
        /// end without a victor, and a scale with no draw on it turns every
        /// inconclusive peace into a defeat for somebody.
        /// </summary>
        public WarVerdict verdict = WarVerdict.Stalemate;

        /// <summary>Why it was judged that way, in the operator's language.</summary>
        public string verdictReason = "";

        /// <summary>Set once defense commitments have been called upon (GDD §15.2).</summary>
        public bool obligationsInvoked;

        /// <summary>
        /// Months before a foreign government may put terms to the player again
        /// (<see cref="Brink.Core.ConfrontationSystem.OfferCooldownMonths"/>).
        /// </summary>
        public int monthsUntilNextOffer;

        public List<OperationRecord> operations = new List<OperationRecord>();

        public string OpponentOf(string countryId) => countryId == initiatorId ? defenderId : initiatorId;

        public bool Involves(string countryId) => countryId == initiatorId || countryId == defenderId;
    }
}

using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>What the defence minister thinks we should do, and why.</summary>
    public class MilitaryRecommendation
    {
        public OperationType operation;
        public string targetLocationId = "";
        public string targetName = "";

        /// <summary>The odds *as the minister reads them* — not the true odds.</summary>
        public float assessedOdds;

        /// <summary>One line of reasoning in the minister's own voice.</summary>
        public string rationale = "";

        /// <summary>0..1 how much weight to put on this advice, from competence.</summary>
        public float reliability;

        public bool HasTarget => !string.IsNullOrEmpty(targetLocationId);
    }

    /// <summary>
    /// The defence minister's professional opinion (GDD §7.2, §8, §19).
    ///
    /// **The problem this solves.** `Official.competence` decided how well a
    /// minister executed when left autonomous, and how much of the month's
    /// traffic reached the terminal. What it never did was help a player who was
    /// running the war themselves — so during a confrontation, the person
    /// nominally in charge of the military had nothing to say. Appointing a good
    /// one changed outcomes you never saw and none you were deciding.
    ///
    /// Now the minister recommends, and **the recommendation is only as good as
    /// they are**:
    ///
    /// - A competent minister reads the odds close to true, so their advice is
    ///   worth taking.
    /// - A poor one is *confidently wrong*: the noise is in their assessment, not
    ///   in their delivery, so bad advice arrives sounding exactly like good
    ///   advice. That is the whole point — you are trusting a person, and whether
    ///   that was wise is a decision you made months ago at the appointment.
    ///
    /// The player is never overruled. The recommendation is a highlight and a
    /// sentence; the order is still theirs. And when the pillar *is* delegated,
    /// this is what lets the operator see what their minister is doing rather
    /// than watching outcomes appear.
    /// </summary>
    public static class MilitaryAdvice
    {
        /// <summary>Competence below which a minister's read is essentially guesswork.</summary>
        public const float Unreliable = 35f;

        /// <summary>How wrong a minister's assessment can be, at zero competence.</summary>
        public const float MaxMisreading = 0.34f;

        /// <summary>The officer running this pillar, or null.</summary>
        public static Official Minister(GameState state)
            => state.PlayerCountry?.FindOfficial(Pillar.Military);

        /// <summary>
        /// How much a minister's assessment can be trusted, 0..1.
        ///
        /// Exposed so the interface can say "our assessment is unreliable"
        /// without pretending to know the true odds — the operator should be able
        /// to tell a confident minister from a competent one.
        /// </summary>
        public static float ReliabilityOf(Official minister)
            => minister == null ? 0f : Math.Max(0f, Math.Min(1f, minister.competence / 100f));

        /// <summary>
        /// The minister's read of an operation's chances.
        ///
        /// Deterministic for a given (official, target, verb) so the number does
        /// not flicker as the screen refreshes — a recommendation that changed
        /// every time you looked at it would be unusable, and would also leak the
        /// true value by averaging.
        /// </summary>
        public static float AssessOdds(GameState state, StrategicLocation target,
            OperationType operationType, OperationDirective directive, float coalitionSupport)
        {
            float truth = MilitarySystem.EstimateOdds(state, state.playerCountryId, target,
                operationType, directive, coalitionSupport);

            var minister = Minister(state);
            float reliability = ReliabilityOf(minister);

            // Stable pseudo-noise: same desk, same target, same verb, same read.
            int seed = unchecked(Hash.Of(minister?.id ?? "NONE")
                                 + Hash.Of(target.id) * 31
                                 + (int)operationType * 7919);
            float noise = ((seed & 0xFFFF) / 65535f) * 2f - 1f;

            float misreading = MaxMisreading * (1f - reliability);
            return Math.Max(0.01f, Math.Min(0.99f, truth + noise * misreading));
        }

        /// <summary>
        /// What the minister would do, given the board in front of them.
        ///
        /// Returns null when there is no confrontation, no minister, or nothing
        /// orderable — advice with nothing to advise on is noise.
        /// </summary>
        public static MilitaryRecommendation Recommend(GameState state, Confrontation confrontation)
        {
            var minister = Minister(state);
            if (minister == null || confrontation == null || confrontation.resolved) return null;

            // The same rule every other pillar follows: an official either runs
            // their pillar or advises on it. Delegated, they act and report in
            // the monthly briefing; under Direct Control the operator decides and
            // they advise. This is what gives Direct Control an upside beyond raw
            // control — it costs Command Points and their trust, and buys their
            // professional read in return.
            if (!CabinetAdvice.ShouldAdvise(state, Pillar.Military)) return null;

            var directive = new OperationDirective();
            MilitaryRecommendation best = null;
            float bestScore = float.MinValue;

            foreach (var location in state.locations)
            {
                bool theirs = confrontation.Involves(location.ownerId)
                              && location.ownerId != state.playerCountryId;
                bool ours = location.ownerId == state.playerCountryId;
                if (!theirs && !ours) continue;

                foreach (var type in OperationCatalog.AvailableAgainst(
                             state, state.playerCountryId, location, confrontation))
                {
                    if (type == OperationType.Withdraw) continue;

                    float coalition = DiplomacySystem.CoalitionStrength(
                        state, confrontation, state.playerCountryId, type);
                    float odds = AssessOdds(state, location, type, directive, coalition);

                    float score = ScoreFor(state, location, type, odds, confrontation);
                    if (score <= bestScore) continue;

                    bestScore = score;
                    best = new MilitaryRecommendation
                    {
                        operation = type,
                        targetLocationId = location.id,
                        targetName = location.displayName,
                        assessedOdds = odds,
                        reliability = ReliabilityOf(minister),
                        rationale = RationaleFor(state, location, type, odds, minister)
                    };
                }
            }

            return best;
        }

        /// <summary>
        /// How attractive an order looks to a professional.
        ///
        /// Not simply "highest odds" — a minister values a cheap setup that makes
        /// the next thing land, and will not spend an army on a raid that
        /// achieves nothing. This is where the recommendation stops being a
        /// calculator and starts being an opinion.
        /// </summary>
        static float ScoreFor(GameState state, StrategicLocation target, OperationType type,
            float odds, Confrontation confrontation)
        {
            var profile = OperationCatalog.For(type);
            if (profile == null) return float.MinValue;

            float score = odds * 100f;

            // Taking the objective is what ends the war.
            if (target.id == confrontation.objectiveLocationId && profile.canTakeGround)
                score += 30f;

            // A permanent setup is worth more than its own odds suggest, because
            // it pays into every later attempt at the same position.
            if (type == OperationType.SuppressDefenses && target.defenseValue > 45f)
                score += 26f;

            // Cheap probes are attractive when the month is thin.
            score -= profile.cpCost * 4f;

            // A minister does not recommend flattening a city casually.
            score -= profile.civilianFactor * 6f;

            // Defensive work is worth recommending when we are being pressed.
            if (profile.targeting == OperationTargeting.OwnGround)
            {
                bool pressed = confrontation.momentum < -10f
                               || (state.PlayerCountry != null && state.PlayerCountry.warExhaustion > 45f);
                score += pressed ? 22f : -18f;
            }

            return score;
        }

        static string RationaleFor(GameState state, StrategicLocation target, OperationType type,
            float odds, Official minister)
        {
            var profile = OperationCatalog.For(type);
            string confidence = odds > 0.65f ? "We should carry it"
                : odds > 0.45f ? "It is a close thing"
                : "It is a gamble, and I would rather not";

            if (type == OperationType.SuppressDefenses)
                return $"{confidence}. Break the works at {target.displayName} first — the effect "
                       + "is permanent and every later attempt is cheaper for it.";

            if (profile != null && profile.targeting == OperationTargeting.OwnGround)
                return $"{confidence}. We are being pressed. I would spend this month on "
                       + $"{target.displayName} rather than on them.";

            if (profile != null && profile.canTakeGround)
                return $"{confidence}. {target.displayName} is the objective; taking it is what "
                       + "ends this.";

            return $"{confidence}. {target.displayName} is where I would apply pressure this month.";
        }

        /// <summary>
        /// The line shown above the order screen. Says who is advising and how
        /// far to trust them, because that is the actual decision.
        /// </summary>
        public static string Header(GameState state, MilitaryRecommendation recommendation)
        {
            var minister = Minister(state);
            if (minister == null || recommendation == null) return "";

            string trust = minister.competence >= 70f ? "Their record is good"
                : minister.competence >= Unreliable ? "Their record is mixed"
                : "Their judgement has been poor";

            return $"{minister.title.ToUpperInvariant()} {minister.displayName.ToUpperInvariant()} "
                   + $"RECOMMENDS: {MilitarySystem.NameOf(recommendation.operation)} "
                   + $"AT {recommendation.targetName.ToUpperInvariant()}\n"
                   + $"   \"{recommendation.rationale}\"\n"
                   + $"   Their assessment: {recommendation.assessedOdds * 100f:F0}%. "
                   + $"{trust} (competence {minister.competence:F0}).";
        }

        // ---------- what the minister does when left to it ----------

        /// <summary>
        /// The standing instruction a delegated minister is working to.
        ///
        /// This is the other half of what the user asked for: when the pillar is
        /// delegated, the operator should be able to *steer* rather than only
        /// watch. Preparing for war buys equipment and readiness at real cost;
        /// drawing down sells hulls back and returns the money.
        /// </summary>
        public const string PrepareForWar = "MIL_PREPARE";
        public const string DrawDown = "MIL_DRAWDOWN";

        /// <summary>
        /// What a delegated minister actually did this month, in plain language,
        /// so delegation is visible rather than silent.
        /// </summary>
        public static string DescribeStanding(GameState state)
        {
            var minister = Minister(state);
            if (minister == null) return "";

            if (minister.mode == ControlMode.DirectControl)
                return "The military is under your direct command. The minister executes and does "
                       + "not choose.";

            string instruction;
            switch (minister.directiveId)
            {
                case PrepareForWar:
                    instruction = "building toward a war footing — ordering equipment and holding "
                                  + "readiness high, at cost";
                    break;
                case DrawDown:
                    instruction = "drawing the force down — selling hulls and airframes back and "
                                  + "returning the money to the treasury";
                    break;
                case "MIL_READINESS":
                    instruction = "holding readiness above the budget";
                    break;
                case "MIL_CONSERVE":
                    instruction = "conserving the budget";
                    break;
                default:
                    instruction = minister.mode == ControlMode.Directed
                        ? "working to your standing instruction"
                        : "running the pillar on their own judgement";
                    break;
            }

            return $"{minister.displayName} is {instruction}.";
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>One named thing that helped or hurt, and by how much.</summary>
    public class OperationFactor
    {
        public string label;

        /// <summary>
        /// Multiplier applied. Above 1 helped the attacker, below 1 hurt them.
        /// Recorded as a multiplier rather than a delta so factors compose the
        /// way the resolution actually composes them.
        /// </summary>
        public float multiplier = 1f;

        /// <summary>Whether this acted on our power or on theirs.</summary>
        public bool onDefence;

        /// <summary>The concrete number behind it, for a reader who wants it.</summary>
        public string detail = "";

        /// <summary>
        /// How much this moved the outcome, as a signed share. Positive helped
        /// the attacker. Used only for ranking, so scale does not matter as long
        /// as it is consistent.
        /// </summary>
        public float Impact
        {
            get
            {
                // A defensive multiplier above 1 hurts the attacker, so its sign
                // flips. Logged so that "halved" and "doubled" rank equally hard.
                float logged = (float)Math.Log(Math.Max(0.01f, multiplier));
                return onDefence ? -logged : logged;
            }
        }
    }

    /// <summary>
    /// Why an operation went the way it did (GDD §19, §28.1).
    ///
    /// **The problem this solves.** An after-action report used to say the
    /// operation failed and how many people were lost. That is an outcome, not an
    /// explanation, and it leaves the operator with exactly one strategy: try the
    /// same thing again until the dice land. Failing four times and succeeding on
    /// the fifth teaches nothing, and a game that cannot be reasoned about is not
    /// a strategy game however deep the simulation underneath it is.
    ///
    /// So every term in `ResolveOperation` now records itself, and the report
    /// ranks them. The three parts matter in order:
    ///
    /// 1. **What the odds actually were.** A 23% attempt that failed is not the
    ///    same event as a 71% attempt that failed, and the operator cannot tell
    ///    those apart from the outcome.
    /// 2. **What decided it**, ranked by how much each factor moved the result.
    ///    Not a dump of every modifier — the two or three that mattered.
    /// 3. **What would change it.** The single most useful line, and the one that
    ///    turns a retry into a decision: suppress their defences, bring a
    ///    partner, close the distance, finish the other war first.
    ///
    /// It runs for **operations against us as well**. When an enemy assault on
    /// our position fails, the operator learns what held — which is how a player
    /// works out what to build at the next base along.
    /// </summary>
    public class OperationAnalysis
    {
        public readonly List<OperationFactor> factors = new List<OperationFactor>();

        public float odds;
        public float attackPower;
        public float defensePower;

        public void Record(string label, float multiplier, bool onDefence = false, string detail = "")
        {
            // A multiplier of exactly 1 changed nothing and would only be noise
            // in a ranked list.
            if (Math.Abs(multiplier - 1f) < 0.005f) return;

            factors.Add(new OperationFactor
            {
                label = label,
                multiplier = multiplier,
                onDefence = onDefence,
                detail = detail
            });
        }

        /// <summary>Factors ordered by how much they moved the outcome.</summary>
        public List<OperationFactor> Ranked()
        {
            var ranked = new List<OperationFactor>(factors);
            ranked.Sort((a, b) => Math.Abs(b.Impact).CompareTo(Math.Abs(a.Impact)));
            return ranked;
        }

        /// <summary>The single worst thing working against the attacker, if any.</summary>
        public OperationFactor WorstAgainstAttacker()
        {
            OperationFactor worst = null;
            foreach (var factor in factors)
            {
                if (factor.Impact >= 0f) continue;
                if (worst == null || factor.Impact < worst.Impact) worst = factor;
            }
            return worst;
        }

        // ---------- presentation ----------

        /// <summary>
        /// The report as the operator reads it.
        ///
        /// `attackerIsUs` flips the framing: the same analysis explains our own
        /// failure and, when an enemy operation fails against our position, what
        /// held. Both are things a player should be able to learn from.
        /// </summary>
        public string Explain(bool success, bool attackerIsUs, string targetName,
            OperationType operationType)
        {
            var sb = new StringBuilder();
            var ranked = Ranked();

            sb.Append(attackerIsUs
                ? (success ? "Assessed at " : "Assessed at ")
                : (success ? "They assessed roughly " : "They assessed roughly "));
            sb.Append($"{odds * 100f:F0}% before the order was given. ");

            // Naming an unlucky outcome as unlucky is not an excuse — it is the
            // difference between "this plan is wrong" and "this plan was fine".
            if (!success && odds > 0.6f)
                sb.Append("The plan was sound and the attempt was unlucky; repeating it is " +
                          "reasonable. ");
            else if (success && odds < 0.35f)
                sb.Append("That should not have worked. Do not read it as a repeatable method. ");
            else if (!success && odds < 0.25f)
                sb.Append("This was never likely. Repeating it unchanged will fail again. ");

            if (ranked.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(attackerIsUs ? "   WHAT DECIDED IT:" : "   WHAT HELD:");

                int shown = 0;
                foreach (var factor in ranked)
                {
                    if (shown >= 4) break;
                    float percent = (factor.multiplier - 1f) * 100f;
                    string direction = factor.Impact >= 0f ? "+" : "";
                    string side = factor.onDefence ? "their" : "our";

                    sb.AppendLine($"     {factor.label,-30} {direction}{percent:F0}%"
                                  + $"  ({side} side"
                                  + (string.IsNullOrEmpty(factor.detail) ? "" : $", {factor.detail}")
                                  + ")");
                    shown++;
                }
            }

            if (!success && attackerIsUs)
            {
                string advice = Advice(operationType);
                if (!string.IsNullOrEmpty(advice))
                {
                    sb.AppendLine();
                    sb.Append($"   WHAT WOULD CHANGE IT: {advice}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// The one line that turns a retry into a decision.
        ///
        /// Derived from whichever factor hurt most, so it is genuinely about this
        /// operation rather than a generic hint. If the works are what stopped
        /// us, it says to break the works.
        /// </summary>
        public string Advice(OperationType operationType)
        {
            var worst = WorstAgainstAttacker();
            if (worst == null) return "";

            switch (worst.label)
            {
                case Labels.Fortifications:
                    return "Their works are the largest single factor. SUPPRESS DEFENCES first — "
                           + "the effect is permanent, so it pays for every later attempt.";
                case Labels.Garrison:
                    return "The garrison is what is stopping us. A SIEGE, RAID or AIR INTERDICTION "
                           + "wears it down before the assault goes in.";
                case Labels.EnemyAir:
                    return "Their air force is contesting this. A COUNTER-AIR CAMPAIGN is the only "
                           + "thing that destroys it; a NO-FLY ZONE grounds it more cheaply.";
                case Labels.EnemyNavy:
                    return "Their fleet is the obstacle. SEA CONTROL fights it directly and makes "
                           + "every later naval operation cheaper.";
                case Labels.CounterIntelligence:
                    return "Their security service is the defence here, not their army. Degrade it "
                           + "with collection and covert work before trying again.";
                case Labels.Reach:
                    return "The force is arriving light because of distance. Ground held nearer, "
                           + "basing rights, or naval and lift capability would close the gap.";
                case Labels.Commitment:
                    return "We are committed elsewhere and this operation is paying for it. Settle "
                           + "or wind down the other front before pressing here.";
                case Labels.Doctrine:
                    return "Our doctrine is not suited to this. DETERRENCE is built to threaten "
                           + "rather than to take ground.";
                case Labels.MissileDefence:
                    return "Their shield is blunting what we send through the air. A ground or "
                           + "naval approach avoids it entirely.";
                case Labels.Experience:
                    return "The force sent has not done this before. Experience is bought by fighting "
                           + "or by joint exercises, and replacements dilute what the survivors know.";
                case Labels.Insurgency:
                    return "The population is the obstacle, not a garrison. Pacification is slow "
                           + "and there is no fast version of it.";
                default:
                    return "Bring more weight: a coalition partner, a stronger branch for this "
                           + "kind of operation, or a cheaper verb that sets up the next one.";
            }
        }

        /// <summary>
        /// Stable factor names. Constants because <see cref="Advice"/> switches
        /// on them — a renamed label would silently fall through to the generic
        /// line, which is the failure this whole class exists to prevent.
        /// </summary>
        public static class Labels
        {
            public const string Fortifications = "Their fortifications";
            public const string Garrison = "Their garrison";
            public const string EnemyAir = "Their air force";
            public const string EnemyNavy = "Their fleet";
            public const string CounterIntelligence = "Their security service";
            public const string Insurgency = "Local resistance";
            public const string Reach = "Distance";
            public const string Commitment = "Committed elsewhere";
            public const string Coalition = "Coalition partners";
            public const string Doctrine = "Our doctrine";
            public const string Isr = "Our reconnaissance";
            public const string MissileDefence = "Their missile shield";
            public const string Familiarity = "Knowing how they fight";
            public const string Experience = "What our force has learned";
            public const string Speed = "Tempo of the order";
            public const string Depleted = "Their force is spent";
        }
    }
}

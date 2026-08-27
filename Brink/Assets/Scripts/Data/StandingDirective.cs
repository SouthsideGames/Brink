using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// An optional strategic directive (GDD §29): something the government, the
    /// Cabinet or circumstances suggest the operator take on — reduce energy
    /// dependence, restore readiness, settle a frontier — with a deadline and a
    /// reward. Never required; ignoring one costs nothing. Completing one is
    /// worth XP and counts as initiative in the annual evaluation.
    ///
    /// The condition is a <see cref="MandateObjective"/>, so a directive is a
    /// mandate objective in miniature and the same evaluator judges both.
    /// </summary>
    [Serializable]
    public class StandingDirective
    {
        public string id;
        public string title;
        /// <summary>Who suggested it, in the terminal's voice.</summary>
        public string source;
        public MandateObjective objective;
        public GameDate issued;
        public int monthsRemaining;
        public int rewardXP;
        public bool completed;
        public bool expired;
    }
}

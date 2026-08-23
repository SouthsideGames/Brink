using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// One step of the first-posting orientation (GDD §5). Written as an
    /// in-universe induction briefing rather than a UI tour: the operator is
    /// being told how their post works, not shown where the buttons are.
    /// </summary>
    public class TutorialStep
    {
        public string id;
        public string title;

        /// <summary>Which view to point the operator at, or null for no target.</summary>
        public string targetViewId;

        public string body;

        /// <summary>What the operator must actually do to complete this step.</summary>
        public Func<GameState, bool> isSatisfied;

        /// <summary>Shown while the step is incomplete.</summary>
        public string instruction;
    }

    /// <summary>Persisted tutorial progress.</summary>
    [Serializable]
    public class TutorialState
    {
        public bool active;
        public bool completed;
        public int stepIndex;

        /// <summary>Baselines captured when a step begins, so progress can be detected.</summary>
        public int commandPointsAtStep;
        public int initiativesAtStep;
        public int monthsAtStep;
        public List<string> completedStepIds = new List<string>();
    }
}

using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>One intended operation in a campaign plan. Planning never executes it.</summary>
    [Serializable]
    public class PlannedOperation
    {
        public string id;
        public string locationId;
        public string operationType;
        public bool completed;
        public GameDate completedDate;
    }

    /// <summary>
    /// Persistent staff plan for one confrontation. The operator can sequence
    /// intended moves and set the risk rules once; actual execution still spends
    /// CP through the ordinary Military command path.
    /// </summary>
    [Serializable]
    public class OperationPlan
    {
        public string title = "Campaign plan";
        public string confrontationId = "";
        public OperationDirective directive = new OperationDirective();
        public List<PlannedOperation> steps = new List<PlannedOperation>();
        public GameDate created;
        public GameDate revised;

        /// <summary>
        /// The operator has pre-authorized the next incomplete step to execute
        /// after monthly Command Points refresh. Additive: old saves remain
        /// manual, which is the only safe default for an absent authorization.
        /// </summary>
        public bool standingOrder;

        // Number of real confrontation operation records already examined by the
        // planner. Additive save field: old saves default to zero and reconcile
        // safely on the next resolved month. This prevents one real operation
        // from satisfying the same planned step more than once.
        public int reconciledOperationCount;
    }
}

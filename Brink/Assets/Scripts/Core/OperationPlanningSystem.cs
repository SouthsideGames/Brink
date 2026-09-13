using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Persistent staff planning for military campaigns. A plan is intent only:
    /// it never spends CP, changes escalation, or launches an operation. Execution
    /// remains on the ordinary GameController/ConfrontationSystem command path.
    /// </summary>
    public static class OperationPlanningSystem
    {
        public const int MaxSteps = 6;

        public static OperationPlan For(GameState state, string confrontationId)
        {
            if (state == null || string.IsNullOrEmpty(confrontationId)) return null;
            foreach (var p in state.operationPlans)
                if (p != null && p.confrontationId == confrontationId) return p;
            return null;
        }

        public static OperationPlan Create(GameState state, string confrontationId, string title,
            OperationDirective directive = null)
        {
            if (state == null || string.IsNullOrEmpty(confrontationId)) return null;
            var confrontation = state.confrontations.Find(c => c.id == confrontationId && !c.resolved);
            if (confrontation == null || !confrontation.Involves(state.playerCountryId)) return null;

            var existing = For(state, confrontationId);
            if (existing != null) return existing;

            var plan = new OperationPlan
            {
                title = string.IsNullOrWhiteSpace(title) ? "Campaign plan" : title.Trim(),
                confrontationId = confrontationId,
                directive = CopyDirective(directive),
                created = state.date,
                revised = state.date
            };
            state.operationPlans.Add(plan);
            return plan;
        }

        public static bool SetDirective(GameState state, string confrontationId, OperationDirective directive)
        {
            var plan = For(state, confrontationId);
            if (plan == null || directive == null) return false;
            plan.directive = CopyDirective(directive);
            plan.revised = state.date;
            return true;
        }

        public static bool AddStep(GameState state, string confrontationId, string locationId, OperationType type)
        {
            var plan = For(state, confrontationId);
            if (plan == null || plan.steps.Count >= MaxSteps || string.IsNullOrEmpty(locationId)) return false;
            if (state.locations.Find(l => l.id == locationId) == null) return false;
            plan.steps.Add(new PlannedOperation
            {
                id = "PLAN_" + state.NextActionSequence(),
                locationId = locationId,
                operationType = type.ToString()
            });
            plan.revised = state.date;
            return true;
        }

        public static bool RemoveStep(GameState state, string confrontationId, string stepId)
        {
            var plan = For(state, confrontationId);
            if (plan == null) return false;
            int index = plan.steps.FindIndex(s => s.id == stepId && !s.completed);
            if (index < 0) return false;
            plan.steps.RemoveAt(index);
            plan.revised = state.date;
            return true;
        }

        public static PlannedOperation Next(GameState state, string confrontationId)
        {
            var plan = For(state, confrontationId);
            if (plan == null) return null;
            foreach (var step in plan.steps) if (!step.completed) return step;
            return null;
        }

        /// <summary>
        /// Reconcile an operation that was actually launched through the normal
        /// command path. Planning itself never calls this unless a real record exists.
        /// </summary>
        public static void RecordExecution(GameState state, string confrontationId, OperationRecord record)
        {
            if (record == null) return;
            var next = Next(state, confrontationId);
            if (next == null) return;
            if (next.locationId != record.locationId || next.operationType != record.operationType) return;
            next.completed = true;
            next.completedDate = record.date;
            var plan = For(state, confrontationId);
            if (plan != null) plan.revised = state.date;
        }

        public static OperationDirective DirectiveFor(GameState state, string confrontationId)
        {
            var plan = For(state, confrontationId);
            return plan == null ? new OperationDirective() : CopyDirective(plan.directive);
        }

        static OperationDirective CopyDirective(OperationDirective source)
        {
            source = source ?? new OperationDirective();
            return new OperationDirective
            {
                speedPriority = source.speedPriority,
                casualtyTolerance = source.casualtyTolerance,
                civilianRiskLimit = source.civilianRiskLimit,
                territorialIntent = source.territorialIntent,
                escalationLimit = source.escalationLimit
            };
        }
    }
}
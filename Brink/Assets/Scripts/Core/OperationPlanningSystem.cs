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

        static StrategicPlan Strategy(GameState state)
            => state == null ? null : StrategySystem.Ensure(state);

        public static OperationPlan For(GameState state, string confrontationId)
        {
            if (state == null || string.IsNullOrEmpty(confrontationId)) return null;
            var strategy = Strategy(state);
            if (strategy == null) return null;
            foreach (var p in strategy.operationPlans)
                if (p != null && p.confrontationId == confrontationId) return p;
            return null;
        }

        public static OperationPlan Create(GameState state, string confrontationId, string title,
            OperationDirective directive = null)
        {
            if (state == null || string.IsNullOrEmpty(confrontationId)) return null;
            var confrontation = state.FindConfrontation(confrontationId);
            if (confrontation == null || confrontation.resolved || !confrontation.Involves(state.playerCountryId)) return null;

            var existing = For(state, confrontationId);
            if (existing != null) return existing;
            var strategy = Strategy(state);
            if (strategy == null) return null;

            var plan = new OperationPlan
            {
                title = string.IsNullOrWhiteSpace(title) ? "Campaign plan" : title.Trim(),
                confrontationId = confrontationId,
                directive = CopyDirective(directive),
                created = state.date,
                revised = state.date
            };
            strategy.operationPlans.Add(plan);
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
            var confrontation = state.FindConfrontation(confrontationId);
            var location = state.FindLocation(locationId);
            if (confrontation == null || confrontation.resolved || location == null) return false;
            if (!OperationCatalog.CanOrder(state, state.playerCountryId, location, type, out _)) return false;

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

        public static string StatusText(GameState state, string confrontationId)
        {
            var plan = For(state, confrontationId);
            if (plan == null) return "NO CAMPAIGN PLAN ON FILE.";
            int complete = 0;
            foreach (var step in plan.steps) if (step.completed) complete++;
            var next = Next(state, confrontationId);
            string nextText = "NONE";
            if (next != null)
            {
                var location = state.FindLocation(next.locationId);
                nextText = $"{next.operationType.ToUpperInvariant()} — {(location?.displayName ?? next.locationId).ToUpperInvariant()}";
            }
            return $"PLAN: {plan.title.ToUpperInvariant()}   STEPS {complete}/{plan.steps.Count}\n"
                 + $"NEXT: {nextText}\n"
                 + $"LIMITS: ESC {plan.directive.escalationLimit.ToString().ToUpperInvariant()}  "
                 + $"CAS {plan.directive.casualtyTolerance:F0}  CIV {plan.directive.civilianRiskLimit:F0}  "
                 + $"INTENT {plan.directive.territorialIntent.ToString().ToUpperInvariant()}";
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
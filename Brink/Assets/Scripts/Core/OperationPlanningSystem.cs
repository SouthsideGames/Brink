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
            if (strategy.operationPlans == null) strategy.operationPlans = new System.Collections.Generic.List<OperationPlan>();
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
            if (strategy.operationPlans == null) strategy.operationPlans = new System.Collections.Generic.List<OperationPlan>();

            var plan = new OperationPlan
            {
                title = string.IsNullOrWhiteSpace(title) ? "Campaign plan" : title.Trim(),
                confrontationId = confrontationId,
                directive = CopyDirective(directive),
                created = state.date,
                revised = state.date,
                // A plan begins now. Operations already in the war diary happened
                // before this staff plan existed and must never complete its steps.
                reconciledOperationCount = confrontation.operations == null ? 0 : confrontation.operations.Count
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
            if (plan == null || plan.steps == null || plan.steps.Count >= MaxSteps || string.IsNullOrEmpty(locationId)) return false;
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
            if (plan == null || plan.steps == null) return false;
            int index = plan.steps.FindIndex(s => s.id == stepId && !s.completed);
            if (index < 0) return false;
            plan.steps.RemoveAt(index);
            plan.revised = state.date;
            return true;
        }

        public static PlannedOperation Next(GameState state, string confrontationId)
        {
            var plan = For(state, confrontationId);
            if (plan == null || plan.steps == null) return null;
            foreach (var step in plan.steps) if (step != null && !step.completed) return step;
            return null;
        }

        public static bool IsNext(GameState state, string confrontationId, string locationId, OperationType type)
        {
            var next = Next(state, confrontationId);
            return next != null && next.locationId == locationId && Matches(next, type);
        }

        /// <summary>
        /// Reconcile an operation that was actually launched through the normal
        /// command path. Planning itself never calls this unless a real record exists.
        /// </summary>
        public static void RecordExecution(GameState state, string confrontationId, OperationRecord record)
        {
            if (record == null) return;
            // Only our own operations can carry out our plan. A war diary holds
            // both sides' records, so without this an enemy assault on the ground
            // we intended to assault would tick our step off for us.
            if (record.attackerId != state.playerCountryId) return;
            var next = Next(state, confrontationId);
            if (next == null) return;
            if (next.locationId != record.locationId || !Matches(next, record.operationType)) return;
            next.completed = true;
            next.completedDate = record.date;
            var plan = For(state, confrontationId);
            if (plan != null) plan.revised = state.date;
        }

        /// <summary>
        /// Observe the authoritative war diaries and advance plans only for new,
        /// real operation records. This deliberately runs after confrontation
        /// resolution rather than launching anything itself. A plan can therefore
        /// never become an alternate command path or a way around CP/authority.
        /// </summary>
        public static void MonthlyReconcile(GameState state)
        {
            if (state == null) return;
            var strategy = Strategy(state);
            if (strategy == null || strategy.operationPlans == null) return;

            foreach (var plan in strategy.operationPlans)
            {
                if (plan == null || string.IsNullOrEmpty(plan.confrontationId)) continue;
                var confrontation = state.FindConfrontation(plan.confrontationId);
                if (confrontation == null || confrontation.operations == null) continue;

                int start = Math.Max(0, Math.Min(plan.reconciledOperationCount, confrontation.operations.Count));
                for (int i = start; i < confrontation.operations.Count; i++)
                    RecordExecution(state, plan.confrontationId, confrontation.operations[i]);

                plan.reconciledOperationCount = confrontation.operations.Count;
            }
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
            if (plan.steps != null)
                foreach (var step in plan.steps) if (step != null && step.completed) complete++;
            var next = Next(state, confrontationId);
            string nextText = "NONE";
            if (next != null)
            {
                var location = state.FindLocation(next.locationId);
                nextText = $"{next.operationType.ToUpperInvariant()} — {(location?.displayName ?? next.locationId).ToUpperInvariant()}";
            }
            var directive = plan.directive ?? new OperationDirective();
            return $"PLAN: {plan.title.ToUpperInvariant()}   STEPS {complete}/{(plan.steps == null ? 0 : plan.steps.Count)}\n"
                 + $"NEXT: {nextText}\n"
                 + $"LIMITS: ESC {directive.escalationLimit.ToString().ToUpperInvariant()}  "
                 + $"CAS {directive.casualtyTolerance:F0}  CIV {directive.civilianRiskLimit:F0}  "
                 + $"INTENT {directive.territorialIntent.ToString().ToUpperInvariant()}";
        }

        // Case-insensitive on purpose: a planned step stores `OperationType.ToString()`
        // ("Assault") while `MilitarySystem` files the real record upper-cased
        // ("ASSAULT"). An ordinal compare here meant no real operation could ever
        // complete a planned step, while a fixture record in mixed case passed.
        static bool Matches(PlannedOperation step, OperationType type)
            => step != null && string.Equals(step.operationType, type.ToString(), StringComparison.OrdinalIgnoreCase);

        static bool Matches(PlannedOperation step, string type)
            => step != null && string.Equals(step.operationType, type, StringComparison.OrdinalIgnoreCase);

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
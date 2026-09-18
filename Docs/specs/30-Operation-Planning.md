# 30 — Operation Planning (as built)

## Purpose

Operation planning gives the operator a persistent staff plan for an active confrontation. The plan remembers intended operations and the standing risk envelope. It remains inert unless the operator separately issues a standing order.

## Ownership

Campaign plans live under `Mandate.strategy.operationPlans`. They are player-authored strategic intent for the current posting, not autonomous world state. Old saves therefore require no invented migration history: an old posting simply begins with no campaign plans.

## Plan contents

A plan records:

- a human-readable title;
- the confrontation it belongs to;
- up to six sequenced intended operations;
- speed priority;
- casualty tolerance;
- civilian-risk limit;
- territorial intent (`Degrade` or `Seize`);
- an escalation ceiling;
- creation and revision dates.

## Hard rule: planning is not authorization

Creating, revising, or reading a plan must not:

- spend Command Points;
- change treasury, force strength, momentum, escalation, or relationships;
- add an operation record;
- roll RNG;
- award XP or annual initiative credit.

Actual execution remains on the existing confrontation command path. `OperationPlanningSystem.RecordExecution` can mark the *next* planned step complete only after it receives a real `OperationRecord` whose location and operation type match that step **and whose `attackerId` is the player** — a confrontation's diary holds both sides' operations, and the enemy assaulting the ground we meant to assault is not our plan being carried out. The type comparison is case-insensitive because `MilitarySystem` files records upper-cased (`ASSAULT`) while a step stores `OperationType.ToString()` (`Assault`); the first build compared ordinally and no real operation could complete a step (found at the C–E midpoint check, 2026-09-13).

## Standing orders

`OperationPlan.standingOrder` is the operator's explicit preauthorization for the next incomplete step. It defaults false, so old saves and newly created plans remain manual. Issuing it requires current Military authority; cancelling it never does, so an administration change cannot trap an unwanted authorization.

After the next month's Command Points refresh, `ExecuteStandingOrder` attempts at most one preauthorized step across all plans. It contains no second resolver: it parses the stored verb, checks the live target and `OperationCatalog.CanOrder`, checks the ordinary CP price, then calls `ConfrontationSystem.LaunchOperation` with the plan's saved risk envelope. Therefore the order spends normal CP, uses ordinary deterministic resolution and consequences, respects current authority and live availability, and waits when temporarily blocked instead of spending or cancelling itself. The resulting normal `OperationRecord` is completed by the existing reconciliation pass.

Planning remains inert. The separate standing-order toggle is the act that delegates execution; turning it off returns the plan to staff intent without deleting a step.

## Validation contract

Focused edit-mode tests cover intent-only creation, posting-strategy persistence, exact next-step reconciliation, defensive copying of directives, default-manual behaviour, normal CP charging, one-step-per-month execution, blocked-order waiting and save/load persistence.

# 30 — Operation Planning (as built)

## Purpose

Operation planning gives the operator a persistent staff plan for an active confrontation without turning Brink into an automation game. The plan remembers intended operations and the standing risk envelope. It does not execute them.

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

## Hard rule: planning is not execution

Creating, revising, or reading a plan must not:

- spend Command Points;
- change treasury, force strength, momentum, escalation, or relationships;
- add an operation record;
- roll RNG;
- award XP or annual initiative credit.

Actual execution remains on the existing confrontation command path. `OperationPlanningSystem.RecordExecution` can mark the *next* planned step complete only after it receives a real `OperationRecord` whose location and operation type match that step.

This preserves Brink's attention economy: a staff can remember what the operator intends, but the operator still decides whether the next step deserves scarce attention this month.

## Validation contract

Focused edit-mode tests cover intent-only creation, posting-strategy persistence, exact next-step reconciliation, and defensive copying of directives. Full Unity integration is intentionally deferred to the C–G milestone gate.
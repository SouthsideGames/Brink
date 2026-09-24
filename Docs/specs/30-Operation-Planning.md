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

Plans are considered in creation order; the first executable standing order wins the month's single execution slot. A blocked earlier plan does not prevent a later executable plan from running. The panel reports the current reason when an authorization is waiting, and an ended confrontation clears its obsolete authorization.

Target checks use the explicit plan confrontation in the shared operation gate
(spec 01 §4-0), both when adding a step and before execution. A target that passes
to a neutral or another front is not silently reassigned: the standing order
waits with `TARGET IS NOT ON THIS FRONT`. No CP, action-sequence allocation or
operation record is produced by the refused order. A rejected new step likewise
allocates no sequence. The intended step and authorization remain available for
the player to revise; no automatic rerouting or replacement target is invented.

Because execution uses the ordinary command path, it also awards the ordinary XP and initiative credit. Those rewards belong to the operator's earlier act of preauthorization; standing orders do not create a cheaper or anonymous execution path.

Planning remains inert. The separate standing-order toggle is the act that delegates execution; turning it off returns the plan to staff intent without deleting a step.

## Cross-department staged programmes

The OPERATOR panel also owns one current staged programme, separate from military
campaign orders and the older single-objective Cabinet programme. Up to six ordered
stages reuse ordinary paid collection, StrategicIntent assessment, diplomatic
outreach, logistics investment and site energy-works commands. No generic script
language or second action resolver is introduced.

Each stage names a target, an earliest month and advisory deadline (0–120 months
from adoption), and a measurable goal. Collection measures uncompromised own
penetration; outreach requires both relations and trust; logistics measures own
logistics. Energy works waits for actual completion, not authorization. Assessment
waits for the specifically commissioned product to be delivered, never for its
hidden accuracy flag. Numeric goals apply only to the first three numeric stages.

Planning is free and separate from authorization. Editing intent revokes approval.
Execution requires an Autonomous staffed desk, current pillar authority, the next
stage's optional own-state condition (nonnegative treasury or no unresolved own
front), live availability, ordinary CP and a cumulative authorized CP ceiling.
An independently delegated foreign policy on the same target blocks programme
execution to avoid conflicting instructions. After monthly CP refresh, campaign
orders and foreign policy run first; the programme has at most one ordinary action
from the remaining shared budget. Restarting does not create another monthly slot.

Monthly observation can record attainment even while authorization is paused;
conditions gate spending, not recognition of outcomes. Only the first incomplete
stage advances per observation. Attainment is history, not a guarantee that a
relationship, asset or capability remains forever. There is no completion reward.
Deadlines show DELAYED but do not impose an invented penalty or force execution.

The CP ceiling is not a treasury ceiling: real projects retain their ordinary
funding obligations. An ordered project that lapses is not automatically bought
again; abandoning/replacing the plan does not cancel projects, refund costs or
undo consequences. Assessment stages likewise do not repeatedly buy a missing
or delayed answer. Unstarted stages may be removed or moved earlier; started
history stays. Abandoned or completed plans can be archived before starting anew.

The optional record lives under Mandate.strategy, with archived records beside it.
Old saves have no programme and no implied authorization; save version remains7.
No AI authoring policy is added. UI reads are pure and wrapped; controller edits
autosave through the existing isolated-test boundary.

## Validation contract

Focused edit-mode tests cover intent-only creation, posting-strategy persistence, exact next-step reconciliation, defensive copying of directives, default-manual behaviour, normal CP charging, one-step-per-month execution, blocked-order waiting and save/load persistence.

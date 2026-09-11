# 26 — Causal Explainability

**Status: Phase A as-built (2026-09-11).** The framework and a representative
vertical slice. Later phases extend the instrumentation; they should not need to
change the record shape. GDD §28.3 is the principle this implements.

The rule, stated once: **when the simulation changes something important, it
should know why, and where the operator's government is entitled to that reason,
it should be able to say so plainly.** The player does not get the formulas. They
get enough to understand an outcome and learn from it.

---

## 1. Why this is architecture rather than a WHY? button

Brink had two existing answers to "why did that happen", and both are narrow.
`OperationAnalysis` explains one operation, by recording each term of
`ResolveOperation` as it is computed. `MilitaryAdvice` explains one order, before
it is given. Everything else — the whole monthly tick, which is where a save
actually accumulates its outcomes — moved numbers with no account of itself.

That matters more here than in most games because of what this codebase's own
history says. Its most-repeated bug family is a value that ratchets because a
recurring cost was applied to the value instead of the target; the second is a
value written but never read. Both classes are *invisible from the terminal* —
the number moves, or fails to, and nothing on screen distinguishes a designed
consequence from a defect. Two of the three most recent playtest bug reports
turned out to be correct simulation that the game could not explain.

So the requirement is not a panel. It is that the simulation records its reasons
at the point of change, in a shape every later system can reuse.

## 2. The record

`Data/Causality.cs`.

- **`CausalRecord`** — one metric, one country, one month: `previous`,
  `resulting`, `delta`, an ordered `contributions` list, a `reconciliation` mode,
  and `unexplained`.
- **`CausalContribution`** — one reason: a stable `CausalReason` ordinal, a
  signed `value`, a `CausalCategory`, a `CausalKind` (direct or indirect), a
  `CausalVisibility`, an optional `sourceCountryId`, an optional
  `sourceActionId`, and a `confidence`.
- **`CausalLedger`** — the bounded store.

**No prose is stored.** `reason` is an enum ordinal and the wording lives in
`CausalReasons.Label`. A contribution is the one field written on every cause of
every metric of every recorded month; putting sentences there would multiply the
save by the length of the English language, and would freeze today's wording into
history. Retitling a cause is now a one-line change that cannot desynchronise
from records already written.

**`CausalReason` and `CausalMetric` are append-only**, and persisted by ordinal.
Reordering one relabels every explanation already saved — a save would start
saying approval fell because of hunger when it fell because of a tax rise.

## 3. How a decomposition is built

`Core/Causal.cs`. Two shapes, because Brink has two.

**A target approached at a rate.** `Approach(v, target, rate)` is the idiom
almost every recurring pressure was pushed into after the value-versus-target bug
family, and it is *linear in the target*: `delta = rate x (target - previous)`.
So a decomposition of the target scales exactly into a decomposition of the
month's movement. `CommitApproach(target, rate, resulting, baseline)` does that
scaling, and folds the anchor constant and the value's own level into one
`Reversion` line — "the baseline is 52 and you are at 61" is one fact about where
this is heading, not two causes, and splitting it would put a constant on screen
as though the world had done something.

**A sum applied directly.** `CommitAdditive(resulting)`.

**Multipliers are handled without lying about them.** For a target shaped
`M x (t1 + t2 + ...)`, the effect of `M` is exactly `(sum so far) x (M - 1)`,
which is itself an additive term. So national unity damping unrest appears as its
own signed line, the raw hardship terms keep their real magnitudes, and the
column still adds up. `Multiply` must be called in the same order the simulation
applied it, because it acts on the running sum.

**Anything left over is booked, never swallowed.** Clamped targets, clamped
results and uninstrumented terms become a `Bounds` or `Unattributed`
contribution. A decomposition that quietly fails to add up is worse than one that
admits a remainder.

### Instrumenting without changing the arithmetic

Two patterns, and the choice between them is the whole safety story.

- **Named locals.** Where a target is a sum of terms, each term is lifted into a
  `float` local and the sum is written from the locals **in the identical order**
  — `a - b` and `a + (-b)` are the same IEEE result, and the codebase already did
  exactly this with `indexTerm`. One definition, used by both the simulation and
  the explanation, so the two cannot drift. A second copy of the formula for the
  renderer to read would have drifted within a month; this project has shipped
  that bug in six different costumes.
- **`Causal.Apply(..., ref field, value, ...)`** for a value that accumulates
  from scattered sites. The caller's expression is passed in untouched as the
  argument; the helper assigns it and records the movement it actually caused.
  The recorded figure is therefore the real post-clamp change, not the intended
  one.

Recording never reads back into the simulation, never draws from an RNG, and is
inert when `Causal.Enabled` is false.

## 4. Disclosure — the information-security contract

`Core/CausalDisclosure.cs`, and it is the **only** gate. Two screens applying
their own fog rules is two fog rules, and one of them will be wrong; this follows
`OperationCatalog.CanOrder` and `IntelReadout.PersonnelAccessOf`.

Recording is complete — the simulation has to be able to explain itself to
itself. What a *reader* gets is decided here:

| Visibility | Shown as |
|---|---|
| `Known` | Named, with its figure |
| `Estimated` | Named, with its figure and a confidence |
| `Suspected` | Named, **no figure** |
| `Unknown` | Not named; counted as withheld |
| `Classified` | Dropped, at every width, for every reader |

Three existing systems are deferred to rather than restated:

- **Intelligence.** A foreign country's internal reasons are visible only as far
  as our Political-domain estimate of that state supports, capped by its own
  confidence grade. No collection, no explanation — the panel says so and says
  nothing else.
- **Cabinet reporting (spec 15).** A delegated pillar with a poor desk *loses the
  small causes*; they are counted as withheld. It never restates a figure.
  "Missed or buried, never distorted" is the standing rule and this layer is the
  most tempting place in the codebase to break it. Direct Control removes the
  intermediary and the filter, exactly as `CabinetAdvice.ShouldAdvise` already
  works.
- **Classification.** No flag reveals it.

**Withholding is itself reported.** "One further factor is not reported to us" is
information an operator is entitled to, and is what keeps collection worth
buying. Hiding the hole would be the omniscience exploit running the other way.

**If anything was withheld or could not be sized, the explanation drops to
`Qualitative`** and prints ranked bands with no total. A column of figures that
silently omits a term is a lie told with correct numbers. The *net* change stays
disclosable — the operator can read the value on any screen, so concealing its
movement would protect nothing.

## 5. Persistence

`GameState.causal`, bounded three ways: **the player's country only**,
`MonthsKept = 12` per metric, `MaxContributions = 14` per record. Pruning is per
(country, metric), not global, or a chatty metric would evict a quiet one and the
quiet one is the first thing an operator goes looking for.

**No save version bump and no migration step.** The field is additive and *empty
is correct* on an old save — a world that resolved its months before this existed
has no recorded reasons, and manufacturing them afterwards would be inventing
history rather than reporting it. That is the reasoning `warsWon` and
`Bloc.commitments` shipped under. An old save simply starts accumulating from the
first month it resolves.

Foreign recording is available (`Causal.RecordForeign`) and **off for a size
reason, not a secrecy one** — disclosure is a separate concern and applies either
way.

## 6. Presentation

`UI/CausalExplanation.cs` is **pure**: it takes a width and returns a string, so
every width rule can be tested without a panel. `TerminalView.AddWhyPanel` is the
affordance, available to any view for any metric, rendering **nothing but a
button until asked**.

```
WHY DID APPROVAL CHANGE?
MAR 2041

CURRENT ..................................... 46.2
THIS MONTH .................................. -3.8

PRIMARY PRESSURES
COST OF LIVING .............................. -2.1
WAR EXHAUSTION .............................. -1.7
CIVIC POSTURE ............................... -1.2
ECONOMIC GROWTH ............................. +0.8
PUBLIC MESSAGING ............................ +0.4
--------------------------------------------------
NET CHANGE .................................. -3.8
```

Rules carried over from the END MONTH overflow, which was five separate defects
of this kind: no row is built from a hardcoded column count, the label column is
derived from the real panel, a long cause is truncated rather than allowed to run
off the edge, and the block is an `AddFigure` so it is not re-wrapped.

Ranking is **deterministic** — largest effect first, bookkeeping last, ties
broken on the reason's own ordinal — and sorts a *copy*. The recorded order is
the order the simulation applied things and is not the renderer's to rewrite.

## 7. Provenance

A contribution can name the operator verb it traces to, as a `nameof` id rather
than a display string — matching on display strings is how the XP
diminishing-returns counter was defeated twice, and `ActionCatalog` already
learned this. A test reflects over `GameController` and fails when a recorded
`sourceActionId` names no verb, so renaming a verb breaks the build here instead
of orphaning the provenance.

Wired in Phase A: civic posture, public messaging, tax rate, budget posture,
sovereign debt issuance.

## 8. What Phase A does not do

Recorded honestly so the next reader does not assume more than is there.

- **Chains are one hop.** `CausalKind.Indirect` and `sourceActionId` are the
  hooks for `doctrine → decision → policy → economic effect → political
  consequence`, and the record shape supports it. Nothing walks a chain yet, and
  there is deliberately no global dependency graph.
- **Treasury is not instrumented.** It has ~50 write sites across the codebase;
  instrumenting it is the "dangerous global refactor" case, and a partial job
  would produce exactly the misleading partial explanation this framework exists
  to prevent. `SovereignDebt` carries the fiscal representation instead, and is
  complete.
- **Episodic causes outside the pilot metrics are not recorded.** A crisis option
  or a coup that moves approval shows up in that month's record as
  `Unattributed` — visible as OTHER, which is the honest reading.
- **Foreign explanations are not stored**, only disclosed.

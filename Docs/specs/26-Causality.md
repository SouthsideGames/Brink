# 26 — Causal Explainability

**Status: Phase A as-built, plus two episodic-attribution slices.** The framework
and representative vertical slices are live. Answered Crisis Turns and signed
peace terms now identify their approval consequences and the operator decision
behind them; later phases extend the instrumentation without changing the record
shape.
[`GDD_v1.1.md`](../GDD_v1.1.md) §28.3 is the principle this implements.

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

### The month is the unit, and the whole month is accounted for

**§3a — the causal month boundary.** A causal month is the interval between two
consecutive completions of `TurnManager.EndMonth`: from the moment the
operator's turn begins — the previous resolution has finished, its year-end
evaluation included, and the screen shows the values the operator reads — to
the moment this resolution finishes. Everything inside belongs to the month
being resolved: the operator's own verbs, a crisis they answered or let lapse
(`CrisisSystem.LapseUnanswered` runs at the top of `EndMonth`, *before*
`ResolveMonth` fires), the whole pipeline, and whatever `YearEnded` does in a
December. The rule is testable and tested: for every reconciled metric,
`record.previous` is the value when the turn began and `record.resulting` is
the value when `EndMonth` returned.

Three pieces implement it:

- **`Causal.CloseMonth` is wired to `TurnManager.MonthResolved`**, a hook that
  fires after every `ResolveMonth` handler *and* after `YearEnded`, while the
  resolved date is still current. It sets each record's `resulting` to what the
  value now reads, books whatever the named causes do not account for as
  `Unattributed`, and then **re-takes the month-open snapshot** for the month
  that follows. The next month opens where this one closed.
- **`Causal.OpenMonth` fills gaps and never overwrites.** Called at
  `SimulationPipeline.Wire` for a new world or an old save with no snapshot, and
  again as the first pipeline system as a fallback for a hand-wired fixture.
  Overwriting there would push the boundary back to pipeline start.
- **The snapshot is persisted** (`CausalLedger.openings`, see §5). The game
  autosaves after every player verb, so a save can be taken mid-turn; a snapshot
  rebuilt at load would open the causal month *after* the verb.

The first version snapshotted at pipeline start. Measured on two seeds over 60
months, approval's endpoints disagreed with the screen in 13 and 6 months — every
one a month in which a crisis lapsed, because the lapse penalty ran before the
snapshot. Fixing that by special-casing the lapse would have left every other
between-months write (an answered crisis, a debt restructure, a posture change)
in the same gap. A fixture that rewrites the world after wiring is describing
"the world as given", not a month, and calls `Causal.SnapshotOpenings` to say so.

**This is not bookkeeping tidiness — without it the headline figure is wrong.**
Approval is moved by a cabinet action *before* the government tick and by a
crisis *after* it; the market index is set by the economy tick and then moved
again by a market shock. A record anchored on the first *instrumented* site
therefore reported the movement between that site and the last one, not the
movement the operator can read on the screen. Measured on one real month: the
panel claimed approval fell 2.01 when it had fallen 1.95, and claimed the market
index **rose 1.0 when it had fallen 4.4**. An explanation layer that disagrees
with the number it is explaining is worse than none, and
`AMonthsRecordDescribesTheWholeMonthNotJustOnePartOfIt` now fails the build on it.

**One record per metric per month**, shared by the decomposition and by every
scattered `Note`. Two explanations of one movement are two partial accounts, and
a screen would show whichever it happened to fetch;
`OneRecordPerMetricPerMonthEvenWhenManySystemsTouchIt` guards that.

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
| `Classified` | Merged into OTHER before any reader rule runs; never named, never counted, never degrades |

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
- **Classification.** No flag reveals it — **and no count, no band, no short
  column.** `CausalDisclosure.WithoutClassified` merges every `Classified`
  contribution into the record's OTHER line *before* the reader-specific rules
  run, so every reader — the country's own government, a foreign one with
  confirmed collection, one with none — sees exactly what they would have seen
  had the movement simply gone unattributed. The first version counted it into
  the withheld line, which told the operator "there is one cause here you do not
  know about"; and a foreign reader with confirmed collection, who is handed
  sized figures and a NET line, would have been handed a column that no longer
  summed to the net change. Both are existence flags. The magnitude is kept
  rather than dropped because the net movement is on the screen regardless;
  what classification protects is that the movement *had a cause at all*.
  `Unknown` is the deliberate contrast: it *is* counted, because "something is
  here you cannot see" is what collection can fix.

**Withholding is itself reported** — for `Unknown` and for what a desk buried.
"One further factor is not reported to us" is information an operator is
entitled to, and is what keeps collection worth buying. Hiding the hole would be
the omniscience exploit running the other way. Classified is the one thing this
does not apply to, for the reason above.

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

**The contribution cap folds; it never drops.** `CausalLedger.FoldToCap` merges
the smallest contributions into the record's single OTHER line until the record
fits, so the largest named causes survive in the order they were recorded, the
folded magnitude is preserved, and `previous + Σ contributions == resulting`
keeps holding on the busiest month. The first version cut the list with
`RemoveRange` — and the entries appended last are `Bounds`, `Reversion` and
`Unattributed`, precisely the terms that make the column add up, so the busiest
months broke first. Deterministic: smallest absolute value first, ties to the
earliest index, and repeated folding is idempotent and never mints a second
OTHER row.

The month-open snapshot (`CausalLedger.openings`) is **persisted**. It is taken
when a month closes and describes the values on the operator's screen as their
turn began (§3a); since the game autosaves after every player verb, a save taken
mid-turn has to carry it or the reload would open the causal month after the
verb. It was `[NonSerialized]` while the snapshot lived inside one tick; once the
boundary moved to the turn, that reasoning inverted. Absent from an old save,
which every reader treats as "no snapshot" rather than as zero, and
`Causal.OpenMonth` fills it at wiring. No version bump: seven additive entries.

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

The first episodic extension wires answered Crisis Turns through the same
provenance contract. Approval changes use `CrisisDecision`; market shocks keep
their more informative `MarketConditions` reason. Both carry
`nameof(GameController.ResolveCrisis)`, so the monthly debrief classifies the
consequence under the operator's hand without storing crisis prose or inventing
a parallel decision history. A zero-delta option records nothing, as with every
other `Causal.Apply` site. A market crisis left unanswered keeps the existing
`CrisisLapsed` provenance; actor-generic calls to `CrisisEffects.Apply` carry no
operator provenance unless the caller supplies it.

The second episodic extension wires constructed peace settlements. Reparations,
political concessions, prisoner exchanges and the settlement dividend retain
their distinct causes. Terms proposed through `GameController.ProposeTerms`
carry that verb; foreign terms accepted through a Crisis Turn carry
`GameController.ResolveCrisis`. Actor-generic `PeaceSystem` calls remain
Diplomatic and carry no operator provenance, even when the acting country is the
player's country. The arithmetic and clamping expressions are unchanged.

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
- **Most episodic causes are not individually named.** Answered Crisis Turns and
  approval effects applied by constructed peace terms are named, but a coup or
  other one-off write to a tracked value still shows up in that month's record
  as `Unattributed`, rendered as OTHER. The *figure* is still exact —
  `CloseMonth` guarantees the total — but the *reason* is unnamed. The rest are
  later slices to attribute.
- **Foreign explanations are not stored**, only disclosed.

# 25 — Content Update: Economy, Intelligence, Diplomacy, Government, Research

**Status: ALL TRANCHES BUILT (2026-08-29).**
Tranche 0 greened the suite (nine failures, four live bugs). As-built detail
lives in the pillar specs: A in 02 §9, B in 03 §6a–§10, C in 04 §5b–5g, D in
05 §2e–2g, E in 13 §6. This document is now a record of the plan rather than
of the code — the reasoning behind each tranche, and what was deferred out of it
and why. **Where this document and a pillar spec disagree, the pillar spec is
right.**

Two places where the plan below was deliberately *not* followed, both recorded
at the point of departure:

- **Tranche B §5.1** claimed deception was a one-off for the player while the AI
  ran it as a standing posture. It was already a standing programme; nothing was
  built.
- **Tranche D §7.1** proposed making `legislativeSupport` the derived total of
  the faction blocs. It has 36 sites across eleven systems, eight of them
  writes — the ledger became a lens on it instead, contributing to the support
  *target* as `brokeredSupport` already does.

Verb counts, start of the update → end: economy 6 → 13, intelligence 7 → 12,
diplomacy 10 → 16, government 22 → 24, capabilities 13 → 33. Suite 1187 → 1258.

## 1. Why these four

Counted from `ActionCatalog.All`, which a test now keeps complete:

| Pillar | Operator verbs | Depth behind them |
|---|---|---|
| Military | 15 | +23 operations (`OperationCatalog`), +17 counted asset classes, posture / doctrine / procurement / logistics / war footing |
| Government | 22 | verbs are plentiful; the *world behind them* is one leader and one support float |
| Diplomacy | 10 | strong standing structure (treaties, blocs, chamber, accession); no episodic layer |
| Intelligence | 7 | 4 domains x 4 covert operations x 3 agent actions |
| Economy | 6 | **no fiscal instruments of any kind** |

Three structural holes, largest first.

**1. The economy cannot be governed.** `EconomyState.debtToGdp` is written in one
place and read in three; no operator verb moves it. Treasury income is a flat
`gdp * EconomySystem.TreasuryIncomeRate`. There is no tax, budget, borrowing,
reserve, subsidy or credit standing. This is also why the playtest's fiscal
finding (spec 12) was left open as "an income-scale question": a belligerent
mid-tier state runs several thousand into the red over a decade and the operator
has no instrument to answer with. Adding levers is the precondition for tuning
income at all.

**2. Secession creates countries and nothing recognises them.** `SecessionSystem`
is the only thing in the game that constructs a `CountryState` at runtime, and
diplomacy has no verb about a new state. Symmetrically, the world now fights
~3 AI-vs-AI wars per thirty years (spec 06) and the player can only watch them.

**3. Everything the AI thinks is invisible.** `AIStrategy.StrategicPath`,
`AIPrediction.OpponentModel` and `EndgameSystem.KnownPreparation` are computed
every month and reach the operator through almost nothing. That is the natural
payload for intelligence content: collection should buy you what a rival is
*for*, not only how strong it is.

And research: 13 capabilities, maximum prerequisite depth 2, every one granting
an efficiency and none an option. Skills already gate operator verbs (spec 07);
**capabilities gating national options is the clean parallel and is unused.**

## 2. Rules every tranche is held to

These are this project's own scars, restated so a tranche cannot skip one. Each
has a test that has already caught the bug at least once.

1. **Actor-generic or it is a player privilege.** Every new verb ships as
   `<Verb>By(state, actorId, ...)` with a player wrapper that spends the
   resource and then delegates. The single most-repeated bug here is an AI state
   locked out of a player verb (procurement, logistics, restocking, coalitions,
   research). Ask not only whether the AI *can* call it but whether it ever
   *will* — routine maintenance belongs to the relevant desk or the AI's action
   budget, not to a strategic-priority branch four gates deep.
2. **A recurring cost moves a target, never a value.** Twelve instances of this
   family have shipped. If the victim of a cost drifts toward a target every
   month, subtracting from the value is dead code.
3. **Every decrement needs a reachable recovery path** under the same conditions,
   and a non-player state must be able to reach it. Otherwise the cost is a slow
   disqualification, not a price (the exposure-penalty lesson, spec 03).
4. **`RecordInitiative` after every successful resource spend**, or the pillar
   grades worse than inaction (spec 07).
5. **An `ActionCatalog` entry per verb**, with `verbs:` naming the
   `GameController` method via `nameof`. `ActionIndexTests` fails the build
   otherwise.
6. **One gate, shared.** What a view offers and what the system accepts must be
   the same function (`OperationCatalog.CanOrder` precedent). Every refusal goes
   through `TerminalView.Block(button, reason)`; both affordability gates may
   only ever *disable*.
7. **Wire monthly systems in `SimulationPipeline`, never in a caller.** A new
   test file that hand-wires must carry a `NARROW PIPELINE: <reason>` comment;
   `PipelineWiringTests.Grandfathered` is empty and stays empty.
8. **Persisted state means a migration step** where an empty or zero default is
   *wrong* rather than merely empty. Bump `SaveSystem.CurrentSaveVersion` and add
   an ordered `SaveMigration` step. Enums are append-only.
9. **Determinism.** Draws seed from `NextActionSequence()`, never the raw
   counter; hash with `Core.Hash.Of`. Every determinism test needs a non-vacuity
   guard — two runs that both did nothing are also equal.
10. **Assert your setup took hold before judging what it caused.** Three tests in
    one session measured a recovery and called it the damage.
11. **New tests go into a `run-suite.sh` partition** in the same commit, and no
    partition exceeds ~20 fixtures.
12. **Update the pillar spec in the same commit.**

## 3. Tranche 0 — green the suite

`CLAUDE.md` records that the last several commits were authored in an environment
with no Unity and no C# compiler: nine test classes (`ActionIndexTests`,
`InsurgencyTests`, `CouncilTests`, `OppositionTests`, `DisplacementTests`,
`BlocTests`, `HoldTests`, `HistoryCatalogTests`, `DossierTests`) and five monthly
systems have never been compiled or executed.

Run `bash Tools/run-suite.sh` (editor closed) and fix to green before Tranche A.
Expected complainants, in order: the readiness targets in `WorldInvariantTests`
(occupation drag now stacks with `InsurgencySystem.ForceDrag`), the unrest and
living-standards invariants (three new pressure terms), and
`EventCatalogTests.QuietWorld_ProducesNoCrises` (its `SettleTheWorld` helper does
not pin `socialUnrest`, `publicGrievance`, `livingStandards` or leader age).

Nothing below should be built on unknown-red code — that is the
`SimulationPipeline` lesson applied to the suite itself.

## 4. Tranche A — Fiscal statecraft (economy 6 -> ~13)

The economy pillar's missing *standing* choice and its missing *stores*.

### 4.1 Data — `FiscalState` on `CountryState`

| Field | Range | Notes |
|---|---|---|
| `taxRate` | 0..100 | Revenue against growth, approval and grievance |
| `budgetPosture` | enum | `Balanced = 0` first, so an old save lands neutral |
| `sovereignDebt` | unbounded | The *stock*; `debtToGdp` becomes derived |
| `creditStanding` | 0..100 | Derived monthly; gates issuance and prices it |
| `reserves` | per resource | Energy / materials / food buffers |
| `subsidies` | per sector | Standing monthly commitments, decaying |

`budgetPosture`: `Balanced` / `Austerity` / `Expansionary` — a standing choice on
the `CivicPosture` and `MilitaryPosture` model, paid for every month it is held.

`creditStanding` is a **target-driven derivation** of debt-to-GDP, growth,
confidence, whether the state is at war and its recent default history. It is not
a store the operator writes.

### 4.2 Verbs

| Verb | Cost | What it trades |
|---|---|---|
| `SetTaxRate` | 2 PC | Revenue now against growth, approval and grievance |
| `SetBudgetPosture` | 2 CP + monthly | Austerity buys solvency with living standards and unrest; Expansionary the reverse |
| `IssueSovereignDebt` | 1 CP | Treasury now, debt service forever, credit standing down |
| `SubsidiseSector` | 1 CP + monthly treasury | Sector health held up while it is paid for |
| `BuildReserves` | 1 CP + treasury | Buys down future sanction and war bite |
| `ReleaseReserves` | 1 CP | Spends the buffer to hold a ceiling through a shock |
| `RestructureDebt` | 4 PC | Writes down service; craters credit standing for years |

### 4.3 The rules that stop it being a printer

- **Debt service is sized against `gdp * TreasuryIncomeRate`**, so borrowing is a
  real trade rather than free capacity. Any new monthly cost anywhere must be
  sized this way — it is how `DisplacementSystem` and `InsurgencySystem` both
  bankrupted the world.
- **Credit standing gates and prices further issuance**, so the answer to a
  deficit is not always more debt.
- **Reserves use the existing endowment/ceiling idiom** (`EnergyCeilingFor`,
  `MaterialsCeilingFor`, `FoodCeilingFor`) — a reserve raises what pressure has
  to grind through, it does not repeal the endowment.
- **Austerity's cost lands on living standards and grievance**, which the
  economy's crisis regime (spec 02) can already express, and which
  `OppositionSystem` already has a theme for. No new social plumbing.
- **The annual evaluation must see it.** `SolvencyPenalty` exists; extend it so
  that running the country on debt is visible in `position`, or the whole tranche
  is invisible to `Report_MultiSeedBalance` exactly as the original leak was.

### 4.4 AI

`SetBudgetPostureBy` / `IssueSovereignDebtBy` / `BuildReservesBy`. A government
under `DiscretionaryReserve` issues rather than stopping everything; a government
in surplus builds reserves. This runs from the **desk**, not the objective
budget: managing the books is governance, not strategy (the restocking lesson).

### 4.5 Save and tests

`SaveVersion` +1. A backfill is required, not optional: zeroed `taxRate` and
`creditStanding` are *wrong* (a world with no revenue and no credit), which is
the `BranchForce.experience` test. `FiscalTests`; extend
`DisplacementTests.ADecadeOfDoingNothingDoesNotEndInTheRed` to a fiscal control.

## 5. Tranche B — Intelligence products and standing programmes (7 -> ~14)

### 5.1 Deception becomes a programme

Deception is currently a one-off `CovertOperation` for the player while the AI
runs `MountDeception` as a standing posture. Promote the player's to the same:
domain, bias, monthly cost, and a detectability that rises the longer it runs.
Symmetry of consequence, and it makes `deceptionStrength` an operator decision
rather than a side effect.

### 5.2 New covert operations

| Operation | Resolves against | Consequence |
|---|---|---|
| `Provocation` | Target CI | Raises threat perception *between two other states* |
| `TechnologyTheft` | Target CI + programme secrecy | Feeds the existing `TechnologySystem` diffusion hook at shallow maturity |
| `CyberOperation` | Target CI + `CAP_SECCOMMS` | Gated on a new capability; hits sector health without attribution |
| `Exfiltration` | Target CI | Pulls a burned agent out; failing loses them publicly |

Plus the long-standing open item: **diminishing returns on repeated covert
operations** against the same target, on the `ProgressionSystem` XP model
(stable reason buckets, counters cleared annually).

### 5.3 The Special Estimate — collection's visible payoff

Commission a finished answer to one question about one state. 2 CP, resolves
after 2 to 4 months, and the answer carries a `ConfidenceGrade` derived from
network penetration net of their counterintelligence — so **it can be wrong**.

Questions, each reading state that already exists and is currently unsurfaced:

- *What are they building toward?* -> `AIStrategy.StrategicPath`
- *Are they preparing an instrument?* -> `EndgameSystem.KnownPreparation`
- *Will they honour their commitments?* -> treaty commitments + `historicalMemory`
- *Who is behind the rising in X?* -> insurgency sponsorship (needs `CAP_FORENSICS`)
- *How do they read us?* -> `AIPrediction.OpponentModel`

**This does not breach the reporting rule.** Spec 15 forbids a *desk* misstating
a figure it was given. An estimate is uncertain by construction and already
carries a grade and a margin; that is the fog system working, not a distorted
report.

### 5.4 Defensive verbs

`MoleHunt` — spend to learn whether we are penetrated. A false positive purges a
competent minister, so it is a real decision, not a free scan.
`SecurityVetting` — a standing programme feeding `institutionalHardening`
(the reservoir, not the value).

### 5.5 Defectors

A pull channel into `AgentSystem`: a foreign official offers themselves, weighted
by their own state's unrest, purges and our standing. Accepting is a burst of
penetration in one domain plus a relations cost with the parent state; refusing
costs nothing and closes the window. Arrives as a Crisis Turn — it is exactly the
shape section 28.2 reserves FLASH for.

### 5.6 Tests

`IntelProductTests`, `DeceptionProgrammeTests`. The estimate's wrongness must be
**deterministic per (observer, target, question)** — a judgement that flickers on
refresh is unusable, and averaging it leaks the truth (the `MilitaryAdvice`
precedent).

## 6. Tranche C — Diplomacy's episodic layer (10 -> ~17)

Diplomacy has standing structure and no *events between states*.

| Verb | Cost | Notes |
|---|---|---|
| `RecogniseState` / `WithholdRecognition` | 1 CP | For `SecessionSystem` successors. Costs relations with the parent, buys the successor's alignment. **AI states decide too** — a breakaway's legitimacy is the world's verdict, not ours |
| `OfferMediation` | 2 CP | On somebody else's confrontation. Resolves through `PeaceSystem` willingness; success buys standing with both, failure is public |
| `ConveneSummit` | 3 CP + months | Preparation, then a package outcome (relations + trust + a treaty at a discount) or a public collapse |
| `ProposeArmsControl` | 2 CP | A treaty class capping escalation and force growth between the pair. Gives `CAP_VERIFICATION` a second read site |
| `BeginNormalisation` | 2 CP | Post-war. Unwinds `settlementTruceMonths` faster and repairs `historicalMemory` |
| `AssignEnvoy` | 1 INF | A standing cabinet official on one state. Makes `Official.competence` matter in a fifth place |

Rules:

- **Mediation must not be free standing.** Failing costs reputation with both
  parties, or tabling one every month is correct play (the chamber-motion
  lesson).
- **Recognition is a bloc-politics act.** It runs through the same `RivalGravity`
  thresholds as treaty acceptance, or it becomes a way around them.
- **A summit is preparation, not a button.** Months of lead time, visible to
  anyone collecting on us, and cancellable at a cost.

## 7. Tranche D — Government content (verbs are fine; the world is thin)

The pillar has 22 verbs and one leader, one `legislativeSupport` float and one
`faction` string. The content is behind the verbs, not in front of them.

### 7.1 A faction ledger

Replace the single support scalar's *interface* (not its storage — keep
`legislativeSupport` as the derived total, so every existing read site survives)
with three or four named chamber blocs per country, each with an
`OppositionTheme` stance and its own support level. `BuildPoliticalSupport`,
`DistributePatronage` and `ConcedeToOpposition` then take a target.

`legislativeSupport` stays the sum, and `brokeredSupport` stays a term in its
target — this is a lens on an existing number, not a second one.

### 7.2 Corruption with a tail

`DistributePatronage` already says it hollows the state and nothing records it.
A `corruption` stat, fed by patronage and restrictive posture, drained by
`LaunchInquiry` and institutional reform, which:

- produces scandal events (new content for `EventCatalog`),
- is an `OppositionTheme` the opposition can campaign on (`Corruption` exists),
- and lowers what the state's institutions can hold — a term in the
  `stability` / `eliteCohesion` **target**, never a subtraction.

Must have a reachable recovery path at every level, or it is the exposure bug
again.

### 7.3 Constitutional change

`GovernmentType` decides how power works and is immutable for a whole save. Make
it changeable: a multi-year project by referendum (elective) or decree
(centralised), costing PC monthly, opposed by whoever benefits from the status
quo, and genuinely losable. This is the government pillar's largest missing verb
and the natural sink for the PC economy above `ConsolidateAuthority`.

## 8. Tranche E — Research 13 -> ~34 (spec 13)

Three changes, in order.

### 8.1 Capabilities that unlock options, not only efficiencies

The established distinction: **a skill is operator capability; a capability is
national ability.** Skills already gate strategic verbs. No capability gates
anything except the five endgames. Fix that.

### 8.2 Catalogue expansion

A third tier per pillar plus **dual-use** capabilities requiring prerequisites in
two trees.

- **Military** — `CAP_AIRDEFENSE` (missile-defence programme effectiveness),
  `CAP_UNDERSEA`, `CAP_AUTONOMY`, `CAP_HYPERSONIC` -> *unlocks a new operation*.
- **Economy** — `CAP_AGRI` -> **raises the food ceiling**, which closes the
  standing open item that a food-poor state has no route but authored links;
  `CAP_SUBSTITUTION` (materials ceiling), `CAP_LOGNET`, `CAP_RESERVECURR`
  (reduces sanction bite on us; prerequisite for the financial blockade).
- **Intelligence** — `CAP_CYBER` -> *unlocks the cyber operation*;
  `CAP_OVERHEAD` -> low-confidence estimates on states with **no network**, so
  the map stops being blank without repealing the fog; `CAP_FORENSICS` ->
  *unlocks attribution*, the Special Estimate on sponsorship.
- **Diplomacy** — `CAP_ARMSCONTROL` -> *unlocks arms-control treaties*;
  `CAP_DEVAID` (aid that creates dependence); `CAP_BROADCAST` (public diplomacy
  abroad, a counter to `AIPrediction`'s hardening).
- **Government** — `CAP_STATISTICS` (earlier warning; a partial answer to a weak
  reporting desk without breaking spec 15), `CAP_EMERGENCY`, `CAP_CIVILDEF`.

### 8.3 Programme management

Parallel programme slots gated on industrial capacity, and a research posture
(breadth vs depth) trading months against monthly cost. Actor-generic — the AI
already has `BeginResearchBy` and `ConsiderResearch`; both must see the new
catalogue without a hand-written list.

**Guard:** every capability must be *read* somewhere. All 13 current ones are;
adding twenty unread ids would be the "written but never read" family at scale.
A test should fail the build on a capability with no read site.

## 9. Cross-cutting

- **Events 35 -> ~60**, drawing on all the new state: failed bond auction,
  currency pressure, austerity riots, defector walk-in, mole found, scandal,
  summit collapse, recognition dispute, research breakthrough and programme
  failure. `EventNature.Opportunity` is under-used (2 of 35) — a prosperous,
  capable country should still make discoveries.
- **`ActionCatalog`** grows by ~30 entries; `ActionIndexTests` enforces it.
- **Re-run `Report_MultiSeedBalance` between tranches, not once at the end.**
  The current table already predates three shipped systems. Balance on the
  Regional and Full world sizes remains unmeasured throughout.
- **Telemetry.** Every new spend passes through `SpendCommandPoints` /
  `SpendPoliticalCapital` with a **stable reason bucket** — do not interpolate a
  target id into the reason string, which has defeated two counters already.

## 10. Sequencing

Tranche 0 -> A -> B -> C -> D -> E, measuring balance between each, so a shift is
attributable to a cause. A and E interlock (reserves, food and materials
ceilings, financial capabilities); if they are ever merged, measure before
merging, not after.

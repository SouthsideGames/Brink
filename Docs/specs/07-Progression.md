# 07 — Strategist Progression Specification

Source: `Core/ProgressionSystem.cs`, `Core/SkillCatalog.cs`, `Data/Progression.cs`.
GDD §25. Covered by `Tests/EditMode/ProgressionSystemTests.cs` and
`Tests/EditMode/PartialSystemsTests.cs`.

## 1. The hard rule

**Skills grant operator capability, never national power.** Command Points,
Influence, Political Capital, action costs, estimate precision, persuasion,
and access to instruments the operator has trained for — yes. Pillar values,
treasury, force strength — never. A test unlocks every node in the catalog and
asserts national statistics are completely unchanged; keep it green.

All player-derived effects must be gated on `id == state.playerCountryId` so
operator training never leaks into AI states.

Everything here is erased by a full reset (GDD §5.1).

## 2. XP

```
XPForLevel(n) = 200 × (n−1) × n / 2      → L2 200, L3 600, L4 1200, L5 2000
```

| Source | XP |
|---|---|
| Monthly administration | 2 |
| Diplomatic outreach | 6 |
| Counterintelligence sweep | 7 |
| Network established | 8 |
| Public messaging | 8 |
| Sanctions lifted | 10 |
| Posture changed | 10 |
| Logistics investment | 10 |
| Covert operation succeeded | 12 |
| Sanctions imposed | 12 |
| Cabinet direct control | 12 |
| Strategic pivot | 12 |
| Capability acquired by diffusion or theft | 12 |
| Joint exercise conducted | 14 |
| Cabinet appointment | 14 |
| Secured military loyalty | 15 |
| Emergency powers | 16 |
| Doctrine adopted | 16 |
| Research programme authorized | 16 |
| Operation conducted | 18 success / 6 failure |
| Procurement program launched | 18 |
| Confrontation opened | 20 |
| Crisis resolved | 20 |
| Institutional reform | 20 |
| Strategic preparation authorized | 20 |
| Coalition assembled | 25 |
| Treaty concluded | 30 |
| Orientation (tutorial) complete | 30 |
| Capability developed to maturity | 35 |
| Strategic instrument employed | 60 |
| Confrontation settled / settlement on our terms | 80 |
| Annual evaluation | `25 + grade points × 15` |

Evaluation XP is deliberately modest relative to decision XP so a decade of
engagement outweighs a decade of drift.

### Repetition discount

Every figure above is a **first-time** award. `AwardXP` multiplies it by a
per-reason repetition factor before crediting:

```
FreeRepetitions      = 4        first four of a kind each year pay in full
MinimumRepetitionValue = 0.2    floor

excess = count − FreeRepetitions
factor = max(0.2, 1 / (1 + excess/4))     8th ≈ 0.85, 20th ≈ 0.5, 50th ≈ 0.25
awarded = max(1, round(amount × factor))
```

The award is **never less than 1 XP** — a repeated action should be worth less,
not worthless.

Counts live in `GameState.xpReasons` / `xpReasonCounts` (parallel lists, because
`JsonUtility` cannot serialize a dictionary — spec 10 §2) and are cleared by
`CaptureYearSnapshot`. Repetition is therefore measured **per evaluation year**: a
lever worn out last year is worth learning from again after a year of doing
something else.

The reason string is the bucket, so it must name a *kind* of action and not an
instance. Two call sites were collapsed for exactly this reason —
`CabinetSystem` reports `"Direct control"` rather than
`$"Direct control: {label}"`, and `MilitarySystem` reports `"Posture change"`
rather than `$"Posture set to {posture}"`. An interpolated string makes every
invocation a fresh bucket and silently exempts the action from the discount.

**The passive monthly baseline is exempt.** `MonthlyXP` goes through a private
`AddXP` that bypasses the factor entirely: nothing was repeated, so nothing
should decay.

Why it exists: verbs differ enormously in how repeatable they are — a trade
adjustment is a monthly click, prosecuting a war is not — so flat XP per action
measured how repeatable a pillar's verbs were rather than how well the operator
played. An economy-focused decade earned roughly **five times** the XP of a
military one at comparable competence. Measured over 5 seeds × 10 years after the
change, economy means 2627 and military 2393 (spec 12 §6). Tests:
`PartialSystemsTests.RepeatingOneAction_PaysProgressivelyLess`,
`RepetitionDiscount_NeverReachesZero`, `DifferentActions_DoNotDiscountEachOther`,
`RepetitionDiscount_ResetsEachYear`, `MonthlyBaseline_IsNotDiscounted`.

### Initiative is recorded explicitly

`ProgressionSystem.RecordInitiative(state)` is called at **every player decision
point that commits a resource** — **36 sites** across the systems. It is
deliberately *not* inferred from XP magnitude.

An earlier build inferred it from an XP threshold of 10, and measurement showed
the metric was really tracking "which pillar happens to award large XP per
action": intelligence scored 78/100 on initiative because covert operations award
12, while diplomacy scored 41 — **below passive** — because outreach awards 6,
despite making 80+ decisions a decade. Making it explicit moved diplomacy from
−0.32 to +0.22 against passive.

Current distribution: Government 6, Confrontation 5, Intelligence 5, Military 4,
Cabinet 3, Diplomacy 3, Economy 3, Endgame 2, plus one each in Crisis, Exercise,
Technology, Regime and `GameController`.

When adding any new player action, call `RecordInitiative` **after** the resource
spend succeeds, or that pillar will quietly grade worse than doing nothing.
De-escalating counts too: lifting sanctions records initiative, because an
operator who applies pressure, extracts a concession and then lifts is otherwise
credited for only half the sequence.

## 3. Annual evaluation (GDD §25.2)

Graded against a `YearSnapshot` captured at the start of each year, so it measures
**movement relative to where the nation actually started** — not absolute
strength, and never a conquest checklist.

```
trajectory = 50 + pillarDelta × 1.6
economy    = 50 + gdpGrowth% × 3 + marketMove% × 0.6 − max(0, inflation−5) × 2.5
stability  = 50 + Δstability × 1.5 + Δapproval × 0.8 + Δunity × 0.8
position   = 50 + ΔlocationsHeld × 12 + ΔrelationsTotal × 0.35
             + Δtreaties × 9 + ΔdefensePacts × 7
             + (deterrent − 0.5) × 24
crisis     = 68 if none faced, else 30 + resolved/faced × 45
initiative = 40 + min(38, initiatives × 2.2)      saturates ≈ 17 decisions/year

score = trajectory×0.20 + economy×0.22 + stability×0.15
      + position×0.11 + crisis×0.12 + initiative×0.20

adversity = (1 if at war) + sanctionPressure×0.25 + (0.5 if exhaustion > 40)
score += adversity × 6
score += 3 (Challenging) / 6 (Ruthless)
```

Every weight above is verified against `ProgressionSystem.EvaluateYear`.
**`initiative` carries 0.20 and `crisis` carries 0.12** — do not transpose them
(spec 12 §3 documented the pair the wrong way round for a while).

### Why `initiative` is weighted as heavily as `trajectory`

Acting costs treasury, stability and capacity, all of which the other components
penalize. Without a counterweight of this size, measurement showed most active
playstyles grading **below doing nothing**, which inverts the entire premise.
Delegation stays legitimate (GDD §3) — an idle year still passes on the other
five components — but the top grades are reserved for operators who did
something. It saturates around 17 decisions a year, roughly one a month plus
change: beyond that, more clicking is not more statecraft.

### Why a quiet year scores 68, not 50

A year with no crises is a year of successful prevention. Scoring it at the
midpoint punished exactly the governments that kept trouble from starting — the
multi-seed harness found that preventing crises graded *worse* than having them.
68 sits just below a competently-handled crisis year and well above a badly
handled one.

### The `deterrent` term in `position`

```
deterrent = min(1, (TotalPower/3 × 100 × 0.6 + logistics × 0.4) / 100)
```

`MilitaryState.TotalPower` sums three branches of `EffectivePower`, each already
normalized to 0..1, so the sum runs 0..3; dividing by 3 and scaling by 100 puts
it on the same 0..100 range `logistics` uses **before** the two are mixed.
Getting that rescale wrong silently makes the term a rounding error.

Deterrence is the peacetime product of military power (GDD §19), so it belongs in
strategic position: an operator who spent a year making the country genuinely
hard to move on has advanced it, and one who let the force hollow out has not.
Centred at 0.5 and scaled by 24, it swings position by ±12 — comparable to taking
or losing one strategic location. It reads **only our own forces**, never a
foreign true value, so it cannot leak through the fog.

| Grade | Score | Skill points |
|---|---|---|
| S | ≥ 82 | 5 |
| A | ≥ 72 | 4 |
| B | ≥ 60 | 3 |
| C | ≥ 48 | 2 |
| D | ≥ 38 | 1 |
| F | < 38 | 0 |

**Calibration intent:** a delegated, uneventful year should land around B. After
the `TradeHealth` correction (spec 02 §3) removed several points of unearned
growth from the `economy` component, the observed spread is C-to-B rather than
B-to-A. The *ordering* is right — every playstyle beats passive — so if the bands
should sit higher the place to adjust is `GradeFor`, not the economy.

All component scores are stored on `EvaluationRecord` and shown in the UI: the
grade is never a black box.

### The year in review — `FileYearInReview`

`EvaluateYear` also files an **ARCHIVE** notification, `YEAR IN REVIEW {year}`,
before capturing the next year's snapshot. It counts that year's chronicle entries
by category (Military / Political / Diplomatic / Economic / Intelligence), reports
`crisesFacedThisYear` and `crisesResolvedThisYear` and the number of operator
decisions logged, and points the reader at CHRONICLE for the detail.

ARCHIVE is the bottom of the five-class hierarchy (GDD §9.2, spec 09) —
reference material, filed rather than reported. It is the only producer of that
class, and the class had none at all before, which made it a permanently empty
drawer in the enum, the stylesheet and the Briefing filter alike. A year's record
is exactly what belongs in it: nothing to act on this month, findable later. Test:
`PartialSystemsTests.YearEnd_FilesArchiveTraffic`.

The evaluation itself stays PRIORITY — the grade and its skill points are the
thing the operator is meant to read.

## 4. Skill trees

**30 nodes**: five pillar chains, four strategic verbs, six depth nodes and four
cross-pillar hybrids. `cost = tier`, so deep specialization is expensive and
breadth is cheap — costs rise with tier exactly as GDD §25.3 requires. A node is
flagged `isHybrid` automatically when any prerequisite belongs to another pillar.

### Pillar chains

| Tree | Chain |
|---|---|
| Military | Operational Planning → Escalation Discipline → Coercive Credibility |
| Economy | Sanctions Architecture → Exposure Management → Financial Statecraft |
| Intelligence | Analytical Rigor → Collection Tradecraft → Compartmentation → Covert Infrastructure |
| Diplomacy | Standing Channels → Negotiating Craft → Coalition Building |
| Government | Command Capacity → Strategic Reserve → Delegation Doctrine |

### Strategic verbs — instruments that do not otherwise exist

These are the most GDD-faithful unlock type: an action the operator simply
**cannot take** without the training, rather than a discount on something already
available. Each is enforced in the owning system, and each system exposes a
`Can…(…, out string reason)` so a view can explain the block instead of offering
a dead button.

| Node | Tier | Requires | Unlocks | Enforced in |
|---|---|---|---|---|
| `MIL_BASING` Forward Basing | 3 | MIL_2 | **Forward posture** | `MilitarySystem.CanSetPosture` |
| `ECO_STRATEGIC` Strategic Industrial Base | 4 | ECO_2 | **Transformative procurement** | `MilitarySystem.BeginProcurement` |
| `ECO_EXISTENTIAL` Financial Blockade | 5 | ECO_STRATEGIC + DIP_2 | **Existential sanctions** | `EconomySystem.CanImposeSanctions` |
| `INT_DEEPCOVER` Deep Cover Program | 4 | INT_2 | **Deception operations** | `IntelligenceSystem.CanRunCovertOperation` |

`ECO_EXISTENTIAL` is the only strategic verb with a cross-pillar prerequisite — a
financial blockade needs the industrial base *and* the diplomatic craft to hold a
coalition of enforcers together — and at tier 5 it shares the top of the cost
scale with `INT_ALL_SOURCE`, `GOV_CONTINUITY`, `HYB_POLWAR` and `HYB_GRAND`.

### Depth nodes

| Node | Tier | Requires | Effect |
|---|---|---|---|
| `MIL_SUSTAINMENT` Expeditionary Sustainment | 4 | MIL_BASING | Operations cost 1 less CP again |
| `ECO_RESILIENCE` Supply Resilience | 3 | ECO_1 | −20% sanction blowback |
| `INT_ALL_SOURCE` All-Source Fusion | 5 | INT_4 | −15% estimate margin |
| `DIP_BACKCHANNEL` Standing Backchannels | 4 | DIP_3 | +6 settlement leverage |
| `GOV_MACHINERY` Machinery of Government | 4 | GOV_3 | +0.5 Political Capital/month |
| `GOV_CONTINUITY` Continuity of Government | 5 | GOV_MACHINERY | +1 CP reserve cap |

### Hybrids (require nodes from two or three trees)

| Node | Tier | Requires | Effect |
|---|---|---|---|
| Deterrence Doctrine | 4 | MIL_3 + DIP_2 | +10 settlement leverage |
| Economic Warfare | 4 | ECO_2 + INT_2 | −30% sanction blowback |
| Political Warfare | 5 | INT_3 + GOV_2 | −25% exposure chance |
| Grand Strategy | 5 | GOV_3 + MIL_2 + ECO_1 | +1 CP/month |

`EffectValue` **sums** every unlocked node carrying an effect, so the stacking
paths are real: `SanctionPrecision` reaches 0.85 with ECO_2 + ECO_RESILIENCE +
HYB_ECOWAR, and `SettlementLeverage` reaches 24 with MIL_3 + DIP_BACKCHANNEL +
HYB_DETERRENCE. Effects with an explicit floor (blowback mitigation at 0.15,
noise at 0.3, exposure at 0.1) are what stop a full build from zeroing a system
out.

### Effect wiring

| Effect | Applied in |
|---|---|
| `CommandCapacity`, `StrategicReserve`, `DelegationBandwidth` | `TurnManager.EndMonth` |
| `PoliticalOperator` | `GovernmentSystem.AccruePoliticalCapital` |
| `OperationEfficiency`, `EscalationDiscipline`, `SettlementLeverage` | `ConfrontationSystem` |
| `ForwardBasing` | `MilitarySystem.CanSetPosture` |
| `StrategicIndustry` | `MilitarySystem.BeginProcurement` |
| `CoercionEfficiency`, `SanctionPrecision`, `ExistentialMeasures` | `EconomySystem` |
| `AnalyticalPrecision`, `CollectionTradecraft`, `CovertEfficiency`, `Compartmentation`, `DeepCoverProgram` | `IntelligenceSystem` |
| `OutreachEfficiency`, `TreatyPersuasion`, `CoalitionPersuasion` | `DiplomacySystem` |

`DiscountedCost` never reduces a cost below 1 CP (or 0 for actions explicitly
allowed to become free, currently only diplomatic outreach).

## 4a. The career arc (GDD §9 amendment, user decision)

At **480 months of service** the next year-end delivers the **tenure review**
(`ProgressionSystem.DeliverTenureReview`), exactly once per save
(`GameState.tenureReviewed`): a career classification derived from the average
annual grade — the same scale the yearly evaluations use, so the summary cannot
disagree with the record it sums — plus the war record, treaties in force,
administrations served, and operator level. Priority traffic on the Command
desk, plus a chronicle entry.

**The world does not stop.** Nothing is disabled and the month after the review
is a month like any other. The design question it answers came from play: an
operator who reaches the top runs out of shape, and the chosen answer was an
arc, not an ending — a turn limit was explicitly declined. FULL RESET remains
the new-posting path. Old saves get the review at their next year-end past the
line; `tenureReviewed` false on load is correct, not missing data. Covered by
`WorldHeatTests.TenureReviewArrivesAtFortyYears`.

## 5. Extension points

- **More nodes per tree.** Validation shows a decade unlocks well under half the
  catalog, so there is still room — but new effects need real wiring, not flavor
  text.
- **Doctrines as unlockables** — GDD §25.3 mentions these. Military doctrine
  (`MilitaryDoctrine`) now exists as a *purchasable national choice* rather than a
  Strategist unlock; a Strategist-tier doctrine that changes how a system behaves
  for the operator specifically is still unbuilt. **PLANNED.**
- **More strategic verbs.** Four exist; every other node is still a modifier on
  something already available, and the Diplomacy and Government trees have none
  at all. **PLANNED.**

## 6. Open questions

- **`ECO_STRATEGIC` currently unlocks nothing the player can reach.**
  `MilitaryView` only ever calls `BeginProcurement(..., ProgramScale.Major)` —
  there is no Transformative button anywhere in the UI, so the one thing that node
  buys is unreachable by any route except a test. `MilitarySystem` also has no
  `CanBeginProcurement(…, out reason)` companion, so even once the button exists
  it cannot explain itself the way `CanSetPosture` / `CanImposeSanctions` /
  `CanRunCovertOperation` do. A tier-4 purchase that grants a verb with no
  affordance is worse than a dead button: the player pays and sees nothing change.
- **The repetition discount is tuned on one measurement.** `FreeRepetitions = 4`
  and the `1/(1 + excess/4)` curve closed a 5:1 gap between economy and military
  play to roughly 1.1:1 (§2), but the shape was chosen for being gentle and
  legible rather than derived from anything. If XP ever gains mechanical weight
  beyond the level display, the curve deserves a proper pass — and the bucket
  strings become load-bearing, since a call site that interpolates its reason
  exempts itself silently.
- Grade bands may want recentring after the `TradeHealth` correction (§3).

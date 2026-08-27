# 12 — Balance & Telemetry Plan

Source: `Tests/EditMode/VerticalSliceValidationTests.cs`,
`Tests/EditMode/WorldInvariantTests.cs`, `Tests/EditMode/BugRegressionTests.cs`,
`Tests/EditMode/PartialSystemsTests.cs`, `Tests/EditMode/GeographySystemTests.cs`,
`Tests/EditMode/AIDomesticTests.cs`, `Tests/EditMode/ForeignCabinetTests.cs`,
`Docs/VerticalSliceValidation.md`. GDD §34 Phase 12, §36.

The EditMode suite is **607 tests**, all passing.

## 1. The harness is the balance tool

Balance is measured, not asserted. `VerticalSliceValidationTests` plays full
decades using the **real player-facing APIs** under six scripted operators
(PASSIVE plus one per pillar), then checks the design's promises.

To measure balance after any change:

```
Unity.exe -batchmode -projectPath <project> -runTests -testPlatform EditMode ^
  -testFilter "Report_VerticalSliceBalance" -testResults <xml> -logFile <log>
```

The report prints per-playstyle average grade, XP, skill points, decisions taken,
months with Command Points exhausted, final pillar total and GDP, plus the world
state after a decade. Paste the table into `Docs/VerticalSliceValidation.md`.

**For any actual balance decision, run `Report_MultiSeedBalance` instead** — five
seeds with a per-component breakdown of the annual evaluation. A single seed is a
sample of one, and the multi-seed report is what found that preventing crises
scored worse than having them and that the initiative weight was too low. The
single-seed report is for a quick sanity check after a change, not for a verdict.

**PASSIVE is the control.** It ends every month without acting, which is legal
play (delegation is legitimate). Every balance question is really "how does this
compare to drift?"

### What the harness cannot see

The bots are not people, and a mechanic aimed at something only a person does will
read as dead here. **Escalation-pressure boilover is the worked example:** across
the whole suite it fires **zero** times outside its own dedicated tests. 215
confrontations reach Crisis and 214 go straight on to Limited Conflict, because
both the playstyle bots and the AI escalate almost immediately rather than leaving
a crisis standing. The dwell-time accrual that feeds the boil point (spec 01 §5)
therefore has nothing to bite on, even though the path is reachable and
test-covered.

That mechanic is aimed squarely at the human behaviour the bots never exhibit —
sitting on an open crisis for a year because deciding is uncomfortable. **Do not
tune `+3`/month or the boil point of 70 against harness output**: a harness that
cannot exhibit the behaviour a constant targets can only ever report zero, and
tuning toward zero would delete the mechanic. Before concluding any number is
inert, check whether the bots are capable of producing the situation it governs.

## 2. Invariants the harness enforces

These are assertions, not observations — if one fails, something is broken:

| Test | Guards |
|---|---|
| `Decade_CompletesWithoutDegenerating` | No NaN/infinity, no value out of range, no vanished economy, no negative manpower, notification cap held |
| `EveryPillarIsViableAsAPrimaryPlaystyle` | Each pillar averages ≥ C and earns skill points (GDD §3) |
| `NoSinglePlaystyleDominates` | Best minus worst < 2.5 grade steps |
| `EngagementBeatsPassivity` | Every playstyle out-earns doing nothing |
| `ActiveManagementOutperformsDrift` | Best active out-grades passive; passive still passes |
| `CommandPointsAreABindingConstraint` | Some playstyle actually runs dry (GDD §7.1) |
| `CapabilityDoesNotSaturateOverALongSave` | No pillar pins at ≥ 98 in a decade |
| `MarketIndexStaysReadableOverADecade` | Index stays in (5, 400) |
| `WorldEvolvesIndependentlyOverTheDecade` | Chronicle grows, foreign leadership turns over, AI pursues its own objectives |
| `PlayerFacesMeaningfulPressureNotJustGrowth` | A decade contains adversity |
| `ProgressionPacesAcrossADecade` | Levels advance; catalog is not exhausted |
| `DecadeIsReproducibleForDebugging` | Byte-identical replay on a seed |
| `SaveAndResumeMidDecadeIsSeamless` | Resumed save continues identically |

## 3. Primary tuning levers

Ordered by how much they move the game:

| Lever | Where | Effect |
|---|---|---|
| `Growth.TaperRange` (45) | `Core/Growth.cs` | Headroom over a long save. Lower = harder ceiling |
| Grade bands + component weights | `ProgressionSystem.GradeFor` | Where a delegated year lands (target: B) |
| `initiative` component weight (**0.20**) | `ProgressionSystem.EvaluateYear` | How much acting beats drifting |
| `crisis` component weight (0.12) + quiet-year floor (68) | `ProgressionSystem.EvaluateYear` | Whether prevention pays as well as response |
| CP baseline (5) + reserve cap (2) | `CountryState.CommandPointsState` | Core scarcity; the single biggest pacing knob |
| Action costs | Each system's constants | Which pillars feel expensive |
| `MonthlyCrisisChance` (0.08) | `CrisisSystem` | Interruption density |
| AI `commitChance`, claim priority | `AISystem` | World volatility (target ~2–3 confrontations/decade) |
| Sanction weights and blowback | `Data/TradeAndSanctions.cs` | Whether economic coercion is worth it |
| `SanctionReviewMonths` (36) | `EconomySystem` | How long a foreign coercion regime — and the hostility it freezes — persists |
| Market `fundamentals` + reversion (0.14) | `EconomySystem` | Index readability and shock response |
| `TradeHealth` breadth cap (×1.25 at 9 links) | `EconomySystem` | How much an authored trade position is worth before anyone plays |
| Territory yield multipliers (0.28 energy / 0.25 industry / 0.20 port / 0.14 chokepoint / 0.18 projection) | `TerritorySystem` | **Whether war can pay for itself.** The military playstyle's grade is more sensitive to these than to any combat constant |
| `OccupationUpkeepPerValue` (0.55) + the unrest drags | `TerritorySystem` | What holding hostile ground costs — the other half of the same trade |
| Authority distribution per government type | `AuthoritySystem.AuthorityOver` | How many verbs the operator can reach at all under their own constitution; `ApprovalCost` (3 PC) and the 30-point legislative-support refusal set the price of the rest |

**Two of these are not tuning knobs so much as feature switches.**
`TerritorySystem`'s multipliers decide whether conquest yields anything, and
`AuthoritySystem` decides which pillars a given posting can even act on — a
playstyle that grades badly may be one the constitution never let the bot run.
Check both before concluding a pillar is under-tuned.

## 4. Bugs this process has caught

Worth reading before assuming a number is fine:

1. **Emergency powers permanently inflated Command Points** — the bonus was added
   to the baseline and never removed, allowing unlimited stacking.
2. **Sanctions produced disinflation** — demand collapse outweighed scarcity, the
   opposite of the intended stagflation.
3. **Incumbents never lost elections**; then, once fixed, a well-run country still
   never changed leadership until term limits were added.
4. **Doing nothing scored a perfect S every year** — the evaluation measured only
   national trajectory, which autonomous officials deliver unaided.
5. **Capability saturated at 100** within a decade, and the **market index
   compounded to 1192**.
6. **Joint exercises were spammable** — 210 per decade before a cooldown.
7. **Capitals were negotiable** at the settlement table, contradicting GDD §22.
8. **Holding a posture hollowed out the force.** Supply was subtracted every month
   at alert or war, and the only recovery branch was gated behind `burn >= 0` —
   unreachable above Peacetime. Any state that stood alert bled to zero supply,
   cutting `EffectivePower` by 60%, for free, forever. It applied to AI states
   too, so the whole world's armies were quietly hollow. Found while asking why
   the military playstyle's *strategic position* scored below a passive one's.
9. **AI rivalries could never mature.** `ConsiderStrategicInstruments` gated on
   `AIObjective.monthsPursued >= 12`, but `FormObjectives` *replaces* the
   objective list every 3–7 months, so the counter structurally could not exceed
   7. No AI ever built a strategic instrument. Fixed with `AIState.rivalries`,
   which tracks the rivalry rather than the objective expressing it.
10. **Territory was worth nothing.** Nothing outside the annual evaluation read
    `StrategicLocation.ownerId`, so taking an energy region supplied no energy and
    taking a works produced no industry. **War could not pay for itself**, and the
    harness's "military grades below passive" finding was reporting a *missing
    feature*, not a tuning problem — which is why two rounds of combat tuning
    moved the gap by roughly zero. `TerritorySystem` now feeds energy, industry,
    trade access and force projection both ways, and charges for occupation. The
    general lesson: when a playstyle grades badly and tuning does not move it,
    check whether the thing it is optimising for is *read* by anything.
11. **`originalOwnerId` never updated on a legal cession.** It was written once at
    world creation and never again, so ground handed over in a *signed settlement*
    stayed `IsOccupied` forever. The new owner paid occupation upkeep, stability
    and war-exhaustion drag in perpetuity; the ceding state kept a permanent
    monthly grievance feeding unity and approval loss; `StrategicPressure` counted
    it as their lost ground for the rest of the save; and a ceded port or airbase
    could never host an ally again. Winning a territorial settlement was a
    standing net liability. Fixed with `TerritorySystem.Cede`, which moves both
    ids and clears `foreignOperatorId` — **occupation is what you hold by force;
    cession is what the world has accepted**, and only the latter clears the
    grievance.
12. **The panel scale collapsed wherever the platform lied about DPI.** The UI
    panel used `PanelScaleMode.ConstantPhysicalSize` at a 96 reference DPI, which
    requires an honest `Screen.dpi`. Where the platform does not report one — the
    Device Simulator, and some real handsets — Unity falls back to 96, the scale
    collapses to **1**, and the entire terminal renders 13-pixel text on a
    2300-pixel screen. Every readout was technically present and physically
    illegible, and no unit test could see it because the failure lived in a
    platform query. Fixed by deriving the scale from pixel width alone
    (`TerminalScale.ScaleFor`), which makes it a pure function of resolution and
    therefore testable at six resolutions with no device attached. **Never branch
    on `Screen.dpi`** (spec 09 §2). A readability bug is a balance bug: a screen
    the player cannot read is a system the player cannot use.

### The audit sweep

A four-way static audit plus a new long-run invariant harness
(`WorldInvariantTests`) found a further cluster. Two root causes ran through
almost all of it:

**(a) Stats with no mean reversion.** A monthly *rate* accumulated with no target
to approach, or a decrement with no matching recovery:

| Stat | Was | Now |
|---|---|---|
| `governmentApproval` | integrated a rate → pinned at 100 in ~5 years, elections became a formality, and the AI's `ConsolidateHome` (scored on `50 − approval`) went permanently negative | approaches a level |
| `warExhaustion` | one decrement in the entire simulation (−10 on close) → ratcheted up forever and, above 25, permanently disabled `RegimeSystem`'s conspiracy-recovery branch, guaranteeing a coup loop | −0.7/month at peace |
| `warSupport` | drained every war month, never restored → after ~1.5 wars an AI could never escalate (gate > 35) and sued for terms instantly (gate < 20) | approaches 50 at peace |
| `manpower` | bare `-=` with no floor and no regeneration → went negative and displayed as such | floored, recovers toward `manpowerBaseline` |
| `energy` / `strategicMaterials` | flat +0.35/month → every authored energy-poor state reached 100 within ~19 years and the roster's deliberate vulnerabilities evaporated | approach an authored endowment |
| network `penetration` | only ever rose; once above 2× the target's counterintelligence the roll-up check was dead code and fog of war ended in the late game | exposure now scales with footprint, so depth never buys immunity |
| foreign `sanctions` | nothing ever lifted an AI's, and a sanctioned pair *skips the relations-recovery branch entirely* → permanent mutual hostility | reviewed after `SanctionReviewMonths` |
| `pillars.government` from leader competence | raw `+= (competence − 50) × 0.004` per month, the last un-damped capability ratchet — ~17 points a decade at competence 85 | routed through `Growth.Apply` (spec 05 §4) |

The leader-competence ratchet is the clearest argument for keeping
`WorldInvariantTests` running. It sat under the saturation invariant for as long
as AI states had little else raising their Government pillar, and crossed it the
moment foreign cabinets began contributing to the same number every month
(spec 15 §1). **A latent bug in one system was surfaced by a change in another**,
by a test that measures the world rather than the code — which is the only kind of
test that could have caught it. `CapabilityDoesNotSaturateOverALongSave`.

**(b) AI states locked out of player-only verbs.** `strength` is written upward in
exactly one place — a procurement program — which only the player could create.
Combined with monthly `logistics` decay that also only the player could reverse,
every AI army decayed monotonically: a player could permanently disarm a rival by
fighting them once, and intelligence estimates (which read the *pillar*, not the
force) kept reporting paper armies as strong. Fixed with `BeginProcurementBy` /
`InvestInLogisticsBy` and an AI `RebuildForces` step.

Also fixed: `TradeHealth` returned an unnormalized **sum** while its consumer
centred it on 50, so once the roster reached 16 countries a hub scored 452 and
drew +5.6 points of annual growth purely from how many links it was authored
with; `CoalitionWillingness` hardcoded the player as leader, so AI-led coalitions
cohered on their members' relations with the *player* and a player who honoured
an alliance was evicted from it the next month; covert operations shared one RNG
stream per (month, operation type), making a successful theft infinitely
repeatable; and `Hash.Of` replaced `string.GetHashCode()` in every seed so saves
cannot silently fork if the scripting backend's string hashing ever changes.

The pattern: every one of these looked fine in unit tests and only showed up when
the game was actually played for years. Keep the harness honest.

### Systems that were built but never connected

A separate sweep looked for the opposite failure — a field or a class that exists,
is written to, and is read by nothing. `PartialSystemsTests.cs` covers the four it
found:

| Was | Now |
|---|---|
| `Confrontation.escalationPressure` written in three places, read in none — GDD §18.1's hidden pressure was an inert float | Boils a standoff over at 70, suppresses settlement willingness, accrues from dwell time at Crisis, and gives sanctions and covert action a route to escalation (spec 01 §5) |
| Occupation's `−0.010` readiness charge subtracted *after* `MonthlyUpkeep` had already drifted readiness back to target, so it was erased in the same tick | `garrisonDrag = min(25, OccupiedValue × 0.12)` moves the readiness **target**, so the cost persists (spec 01 §2e) |
| `NotificationClass.Archive` present in the enum, the stylesheet and the Briefing filter with no producer anywhere | `ProgressionSystem.FileYearInReview` files the annual record (spec 07 §3) |
| FLASH raised from ~17 sites, most reporting things that had already resolved — a war produced one every month | Eight producers, all of them a decision on the desk (spec 09) |

Three of the four are the same shape as the audit sweep's: a cost or a signal that
*reads* as implemented and does nothing. **Tick order is the one to watch** — a
value that another system re-approaches to a target cannot be charged with a flat
subtraction, no matter how correct that subtraction looks in isolation.

## 5. Telemetry — **PLANNED**

Nothing is collected today (the game is offline and premium; GDD §33). If
telemetry is added later, the questions worth answering are:

- **Where do players stop?** Month index at last session, by playstyle.
- **Which pillars do players actually use?** Action counts by system — validation
  predicts Government and Military are under-used relative to Economy.
- **Is CP scarcity felt?** Fraction of months ending with 0 CP.
- **Are crises interrupting too often or too rarely?** Crises per decade against
  session length.
- **Which skills are taken first, and which are never taken?** A never-taken node
  is a design failure.
- **How long do saves live?** The GDD's premise is decades-long saves; if real
  players stop at year three, the long game needs work.

Constraints: opt-in, no personal data, and it must never influence balance
silently — telemetry informs designer decisions, not runtime difficulty.

## 6. Known balance issues (open)

Current `Report_MultiSeedBalance` means, 5 seeds × 10 years:

```
PLAYSTYLE      MEAN GRADE   VS PASSIVE
PASSIVE           2.18   —
DRIFTER           2.52      +0.34
MILITARY          2.36      +0.18
ECONOMY           2.58      +0.40
INTELLIGENCE      2.68      +0.50
DIPLOMACY         2.84      +0.66
GOVERNMENT        2.76      +0.58
```

**This is the one table.** Re-measured 2026-08-27 after the playtest
fixes — hosting and insurgency cost scale (specs 19 §5, 16 §5),
`TreasuryIncomeRate` 0.012 → 0.03 (spec 02), settlement direction, offered terms
as a decision and the settlement truce (spec 01 §5), `MinimumReach` 0.2, and the
`SolvencyPenalty` on the economy grade (spec 07). Every playstyle is still above
passive; spread 0.48 and MILITARY sits at +0.18 (was +0.08 before the victory dividend, winning-side relief and verdict credit, spec 01 §5 / spec 07) — it wins wars now
(118–65 across the 16-posting harness, net +18 locations) and is charged for
them, and the evaluation deliberately gives conquest no credit. Absolute grades
shifted down ~0.1–0.3 because a deficit now costs the economy component; do not
compare to the previous table (2.32 / 2.80 / 2.66 / 2.96 / 2.96 / 3.02 / 2.84),
which was measured with every posting tens of thousands in the red and graded
as if it were not.

**2026-08-27 evening, after the "played it as a person" pass:** S ≥ 88 and A ≥ 76
(S was being handed out for a first year of answering two crises — it is now 2 of
~2,240 measured year-grades, B the norm); war appetite tuned (`WarRecoveryMonths`,
terms sought at exhaustion 35, truce 24) — a passive USA now spends ~40
confrontation-months a decade, from 92. Re-measure this table before trusting it.

Previous table, for the record, measured the same morning on —
hot world (spec 06 §7c), bloc gravity (spec 04 §8a), food security (spec 02
§5), faction arithmetic (spec 05 §2b-1), fiscal reserve, détente and treaty
deepening (spec 04 §5a, spec 02 §4a). Where another section needs these
figures it points here rather than quoting them, because two transcriptions of
the same run drift.

Read of the table: spread **0.36** — the tightest ever measured — with every
playstyle above passive, no dead pillar and no dominant one. The long-standing
**ECONOMY-outlier question is closed** (+0.92 at its peak → +0.64, mid-pack),
resolved by the world's own pressure rather than tuning. MILITARY's floor
position and net-zero-territory war outcomes retell the measured
opportunity-cost-of-commitment story unchanged. Drifting costs −0.22 against
the answering twin (was −0.32). Passive itself rose 2.24 → 2.32: the harness
passive *answers crises*, and a busier world pays a present operator more —
ad-hoc passive baselines on other countries and seeds measured 2.00–2.15, so
**passive is country- and seed-sensitive; re-measure it on the exact scenario
before judging any margin**. Regional and Full world sizes remain unmeasured.

### Foreign cabinets compressed the margins, which is the expected direction

Against the previous run (PASSIVE 2.44; MILITARY +0.34, ECONOMY +0.72,
INTELLIGENCE +0.62, DIPLOMACY +0.28, GOVERNMENT +0.28), **every margin over
passive narrowed except DIPLOMACY's**, which widened (+0.28 → +0.44). Passive
itself rose slightly, 2.44 → 2.50.

Compression is what this change should do. A rival's capability used to come from
a single AI routine that raised pillars along the leader's priority; it now comes
from five officials working every pillar every month, subject to competence,
variance and the national priority multiplier. **The world is simply more
competent than it was**, so the same player performance is measured against a
higher field and the relative advantage of playing well narrows. A change that
made foreign governments run properly and left the player's *margin* untouched
would have meant the margin was never being measured against the world in the
first place.

**Every playstyle still beats passive.** That is the Phase 12 invariant and the
only one this table exists to protect; the absolute levels and the spread are
tuning, the ordering is the promise.

DIPLOMACY widening is worth a note rather than a fix. It is the playstyle whose
output is other governments' dispositions, so it is the one that gains rather than
loses when those governments become more capable actors — a stronger partner is
worth more than a weak one, and a treaty network of competent states compounds
where a network of stagnant ones does not.

**The two blocks below are stale.** The XP / decisions / GDP table and the
evaluation-component table are both from the pre-domestic-pass run and have not
been re-measured since — neither for the domestic pass nor for foreign cabinets,
and the latter has to have moved the component figures, since a more capable world
changes every relative reading in them. The prose in the rest of this section
refers to them. Read them for shape, not for level, and **re-run
`Report_MultiSeedBalance` before quoting any figure from either**.

Mean XP, decisions and GDP:

```
PLAYSTYLE        MEAN XP   MEAN DECISIONS   MEAN GDP
PASSIVE             1039                9       2990
MILITARY            2393              126       2332
ECONOMY             2627              334       2754
INTELLIGENCE        2073              279       2879
DIPLOMACY           1579               92       3141
GOVERNMENT          1257               24       2932
```

Mean evaluation components, all years, all seeds:

```
PLAYSTYLE       TRAJ   ECON   STAB    POS   CRIS   INIT
PASSIVE          55.4   53.3   52.7   58.0   70.1   42.0
MILITARY         53.9   30.8   49.3   58.2   70.1   66.4
ECONOMY          57.2   44.2   51.9   55.6   70.1   78.0
INTELLIGENCE     43.5   53.0   51.5   56.9   70.1   78.0
DIPLOMACY        55.2   54.3   52.8   70.0   70.1   55.2
GOVERNMENT       58.5   51.7   56.6   57.5   70.1   45.2
```

**Every playstyle grades above passivity**, which is the Phase 12 promise and was
not previously true — military sat at −0.10. Two things moved it: the supply and
force-rebuild fixes (a military bot's army is no longer hollow, and its decisions
per decade rose 46 → 126), and the `TradeHealth` normalization, which removed the
large unearned growth that passive hub-trading was collecting.

Absolute grades and GDP both fell sharply because that same `TradeHealth` fix cut
roughly 5 points of annual growth from well-connected states. The old figures were
inflated: USA compounded ×14.6 over thirty years purely on link count. The spread
is C-to-B rather than B-to-A, which is a *scale* change, not a regression — but if
the grade bands are meant to centre higher, the place to adjust is `GradeFor`, not
the economy.

**The XP column is no longer a proxy for how repeatable a pillar's verbs are.**
Economy earned roughly five times military's XP at comparable competence, because
a trade adjustment is a monthly click and prosecuting a war is not. The per-kind
repetition discount (spec 07 §2) closes it to 2627 against 2393.

Open:

- **MILITARY's `ECON` component sits at 30.8, the lowest figure in the table by a
  wide margin, and nothing so far has moved it.** A militarised decade gives up
  roughly a sixth of GDP against passive (2332 against 2990) and the evaluation
  sees nothing in return: the `economy` component reads GDP growth, market move
  and inflation, none of which register that the spending bought a force, ground
  or deterrence. Territory yields (spec 01 §3a) were supposed to be the answer and
  are visibly not enough on their own. This is the outstanding design question in
  the balance model, not a tuning knob — the honest fix is either an economy
  component that prices strategic assets, or an acceptance that militarism costs
  growth and should be paid for elsewhere in the score.

  **`WarOutcomeReport` now answers half of it.** `Report_MultiSeedBalance` prints
  a second table asking whether fighting achieves anything, because the low `ECON`
  figure had two possible readings that call for opposite fixes: militarism
  correctly modelled as a losing proposition unless it takes something, or a bot
  that fights decade-long wars and never captures anything. Across the five seeds
  the military bot **holds the contested lane at the end of 5/5** and finishes with
  **net territory 0 or −1** — it wins the war it started and loses ground elsewhere
  while committed. So the low `ECON` component is the **opportunity cost of
  commitment**, not a failure to achieve anything, and **raising conquest yields
  would reward a war the player is already winning** while leaving the actual cost
  untouched. Median **3 player confrontations per decade**, which is inside the
  `~2–3` target in §3.

  **Filter by `Involves(playerCountryId)`.** `state.confrontations` holds every war
  on the map, AI-vs-AI included; the report prints the player's count and the
  world's in separate columns for exactly that reason. Conflating them turned a
  normal decade into a false reading of constant player warfare and would have sent
  a tuning pass after a problem that was not there.
- Government play makes ~24 decisions per decade; it needs more verbs (spec 05).
- Intelligence has the weakest trajectory score of any playstyle (43.5) — covert
  work does not advance the pillars — yet grades well above passive on initiative
  alone. Worth checking that initiative is not carrying it too far.
- Grade bands may want recentring after the growth correction (see above).

## 7. Changes measured as balance-neutral

Not every system is a balance change, and it is worth recording the ones that are
not — both to save a re-measurement and because an unexpected *movement* in these
numbers later is itself a finding. The converse is recorded too: the AI domestic
pass was expected to move the table and did (§6).

### Cabinet reporting (`ReportingSystem`, spec 15)

The 5-seed playstyle table was **byte-identical** across three consecutive runs:
before `ReportingSystem` landed, after it landed, and again after the three
follow-up fixes (the `MIL_READINESS` directive, per-official desk attribution, and
the national-priority clarification). Those runs predate the §6 numbers, which
moved for unrelated reasons — the AI domestic pass and foreign cabinets. The claim
here is about the three reporting runs, not about the current level.

The reason is structural rather than lucky. Reporting alters what the **operator
knows** and never what **happens**: its entire write surface is one enum field on
a `Notification` and one `RemoveAt`. It touches no pillar, resource, relationship,
confrontation or chronicle entry. The harness bots also do not read the briefing,
so they are the wrong instrument for measuring the mechanic's *value* — that is a
playtest question, not a harness one.

Identical grades are therefore the **expected** result and the assertion worth
keeping. If a future reporting change moves this table, it indicates a leak from
the information layer into the simulation and should be investigated as a bug
before it is accepted as balance.

### Geography and reach (`GeographySystem`, spec 01 §3b)

Distance now multiplies attacker power at operation resolution and bounds the AI's
`AssertClaim` scoring (spec 01 §3b), which sounds like a combat change and
measures as none. **The §6 table is the post-geography measurement.** Against the
run before it, MILITARY moved +0.32 → **+0.30** and ECONOMY +0.68 → **+0.66**,
with the other four rows unchanged. Two rows shifting by 0.02 across different
pillars is **seed noise, not a geography effect** — a real reach effect would land
on MILITARY and leave ECONOMY alone.

The reason is the roster, not luck. **A superpower's reach already covers its
rivals**: `ProjectionRange` weights naval power ×35, and the states the playstyle
bots play as and fight over are exactly the ones with the fleets, so their reach
factor is 1.0 for almost every objective they pursue. Geography prices
**regional** powers' overreach — a landlocked or coastal mid-tier campaigning
across the planet — which is a situation the six scripted playstyles never
produce. The effect is real and test-covered (`GeographySystemTests`,
13 tests); it simply does not intersect what the harness measures.

Two consequences worth keeping in mind. First, this is another case of §1's
warning: **do not conclude the reach constants are inert because the table did not
move** — check whether the bots can produce the situation the constant governs.
Second, if a future roster change or a new playstyle *does* move this table, the
likely cause is a state whose reach no longer covers its own theatre, and the
place to look is `mapX`/`mapY` (spec 08 §2) before any combat constant.

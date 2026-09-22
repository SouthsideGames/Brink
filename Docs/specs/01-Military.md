# 01 — Military System Specification

Source: `Core/MilitarySystem.cs`, `Core/ConfrontationSystem.cs`,
`Core/TerritorySystem.cs`, `Core/GeographySystem.cs`, `Core/PeaceSystem.cs`,
`Core/ExerciseSystem.cs`, `Data/MilitaryForces.cs`, `Data/StrategicLocation.cs`,
`Data/Confrontation.cs`.
GDD §16, §18, §19, §15.3, §27. Covered by `Tests/EditMode/MilitarySystemTests.cs`,
`MilitaryVerbsTests.cs`, `TerritorySystemTests.cs`, `GeographySystemTests.cs`,
`PeaceSystemTests.cs` and `PartialSystemsTests.cs`.

The military endgame (Strategic Destruction) lives in `Core/EndgameSystem.cs` and
is specified in **spec 14**, not here.

## 1. Force structure

Three branches per country — Ground, Air, Naval — each with three values in
`0..100`:

| Value | Meaning | Volatility |
|---|---|---|
| `strength` | Durable force capacity | Slow; damaged by losses |
| `readiness` | Trained, manned, deployable now | Monthly drift toward a posture target |
| `supply` | Fuel, munitions, sustainment | Consumed at alert/war, rebuilt from industry |

```
EffectivePower = strength × (0.35 + 0.65 × readiness/100)
                          × (0.40 + 0.60 × supply/100) / 100
```

Both multipliers have a floor, so a large unready force retains some capability —
but a fully ready, fully supplied force is roughly 4× a stripped one. `TotalPower`
is the sum across branches.

**No individual unit counts.** Force structure is branches and enablers only
(GDD §19). Do not add vehicle or division counts.

## 2. The four verbs

The pillar is not just "have an army and fight with it". Four standing choices
sit under the operator's hand, each with a real monthly bill, and together they
are most of what a military playstyle actually *does* between confrontations.

### 2a. Posture — `MilitaryPosture`

`Peacetime` / `Alert` / `Forward`. Standing **up** costs CP (`PostureCost`:
Forward 2, Alert 1); standing **down** is free, because de-escalation should
never be rationed by the same budget as escalation.

| Posture | Readiness target | Monthly treasury | Supply drag |
|---|---|---|---|
| Peacetime | `55 + economyPillar × 0.15` | 0 | 0 |
| Alert | 85 | 12 | 18 |
| Forward | 92 | 34 | 30 |
| *(at war, any posture)* | 90 | ≥ 12 | +12 on top |

`ReadinessTargetFor` checks war **first**, so a country at war holds 90 even at
Peacetime — but a Forward force at war still holds 92, and pays 34.

Alert is set automatically when a confrontation reaches Limited Conflict and
cleared when it closes. **Forward is a strategic verb** gated on
`SkillEffect.ForwardBasing` (node `MIL_BASING`): without access agreements and
prepositioned stocks there is nothing to stand forward *from*.
`CanSetPosture(…, out reason)` reports the block so the view can say so.

A Forward posture is read abroad as a threat: every relationship involving that
country gains **+0.6 threat perceived by the partner, every month**, which
compounds into rivalries and AI counter-objectives (GDD §15.1). Standing forward
is not free even when nobody shoots.

### 2b. Procurement — `ProcurementProgram`

Multi-year funded programmes, maximum **2 concurrent** (`MaxPrograms`). Requires
treasury of at least three months' cost up front, and a programme the treasury
cannot fund in a later month is **cancelled, not carried**.

| Scale | CP | Months | Strength/month | Treasury/month |
|---|---|---|---|---|
| Modest | 2 | 12 | 0.30 | 18 |
| Major | 2 | 24 | 0.40 | 34 |
| Transformative | 3 | 36 | 0.55 | 55 |

Each month a running programme applies `Growth.Apply` (diminishing, spec 12 §3)
three times over:

- `branch.strength += strengthPerMonth` — the only place in the entire simulation
  that writes force strength upward
- `pillars.military += strengthPerMonth × 0.5` — real force structure is national
  military capability, so it shows in the headline pillar (GDD §10). Without
  this, defense investment was pure cost and never visible.
- `industrialCapacity += strengthPerMonth × 0.25` — defense spending builds the
  industry that produces it. This does not repay the programme (the treasury cost
  is far larger), but modelling armament as purely extractive made it strictly
  self-defeating.

**Transformative is a strategic verb** gated on `SkillEffect.StrategicIndustry`
(node `ECO_STRATEGIC`) — yards, lines and skills that take a decade to build.
`MilitaryView` offers all three scales as a selector row, with Transformative
refused (and the reason printed) until the skill is held.

**`CanBeginProcurement(state, scale, out reason)` / `CanInvestInLogistics(state,
out reason)` are one gate shared by the order screen and the order** — the same
rule `OperationCatalog.CanOrder` follows. All three procurement refusals
(programme slots, the Transformative lock, three months' treasury) previously
lived only inside `BeginProcurement`, where the operator met them as a bright
button that spent **no Command Points and reported nothing**. Reported from play
as "sometimes I press a button to use CP and it does not go down". Money and
industrial slots are not things a player can infer from a screen that does not
mention them, so the panel now prints the treasury figure beside the buttons.

**`BeginProcurementBy` is actor-generic and must stay that way.** Strength being
writable in exactly one place, once reachable only by the player, meant every AI
army decayed monotonically: a player could permanently disarm a rival by fighting
them once, and intelligence estimates (which read the *pillar*, not the force)
kept reporting paper armies as strong.

#### Visible procurement commitments (#24)

MILITARY shows each procurement programme's full saved label, branch, remaining
funded months, saved monthly cost and their product as remaining treasury
commitment. This is future spending at current terms, not reserved money.
Negative legacy duration/cost is displayed as zero without modifying the save;
no start date, original duration or previously spent total is reconstructed.
Capability accrues during funded months. Failure to fund terminates the programme,
does not pause it, and refunds no prior spending. Completion is not a final
counted-equipment shipment.

Existing authorization, funding-failure and completion chronicle entries gain
distinct `PROCUREMENT AUTHORIZED:`, `PROCUREMENT TERMINATED:` and
`PROCUREMENT COMPLETED:` prefixes. Both authorization paths use the same prefix.
Their count, country ownership and publicity are unchanged. The panel shows the
latest five own Military entries bearing these prefixes, newest first. Old
generic entries remain in the Chronicle rather than being reclassified by guess.

Counted equipment orders are different: treasury is paid upfront, then the
aggregate backlog for each asset class delivers incrementally. The panel shows
our outstanding quantities without claiming separate orders, original prices or
a fixed completion date. Industry and war footing alter delivery tempo. There
is no monthly purchase instalment; ordinary upkeep and war-footing costs remain
separate. The order receipt now describes this actual delivery rule rather than
claiming first deliveries wait for the catalogue lead time.

This is presentation only: no funding, delivery, force-growth, CP, XP, AI, schema,
save-version or pipeline change. Wrapped prose uses the existing text policy.
Native Unity verification and hardware remain separate from author harness checks.

### 2c. Logistics

1 CP + 70 treasury for `Growth.Apply(logistics, 9)`. The treasury is checked
*before* the CP is spent — a negative treasury cancels running procurement and
suspends research, so an unchecked click could strand the player.

Logistics decays **−0.15/month** without maintenance, and raises the supply
ceiling, softens posture drag, and feeds the `deterrent` term in the annual
evaluation (spec 07 §3). `InvestInLogisticsBy` is likewise actor-generic; the AI
`RebuildForces` step uses it.

### 2d. Doctrine — `MilitaryDoctrine`

2 CP (`DoctrineCost`). `Balanced` / `Maneuver` / `Attrition` / `Deterrence`.
Doctrine **changes how operations resolve; it grants no capability** — see §4.
`Deterrence` additionally adds **+10 settlement willingness** whenever that state
proposes terms, and Forward posture adds **+6**: a force built and postured to
deter is more believable when it demands something (§5).

### 2e. Where the four verbs land — `MonthlyUpkeep`

`MilitarySystem.MonthlyUpkeep` runs for every country, in this order: advance
procurement, then per branch —

- **Readiness** approaches
  `ReadinessTargetFor(…) + TerritorySystem.ProjectionSwing
  + ReadinessDirectiveBonus − garrisonDrag` at
  **4.0/month while holding a posture or at war, 2.5/month otherwise**. Basing is
  reach: held airbases raise the readiness a force can be sustained at, and losing
  them lowers it (GDD §19). Garrison duty pulls the other way:

  ```
  garrisonDrag = min(25, TerritorySystem.OccupiedValue × 0.12)
  ```

  applied identically to every branch. It moves the **target**, never the current
  value: the drift above pulls readiness back to target every month, so a flat
  monthly subtraction taken anywhere else in the tick is erased before it can be
  felt — which is exactly what happened to the occupation readiness cost that used
  to live in `TerritorySystem.MonthlyUpdate`. Occupation looked costly in the code
  and was free in play. The cap at 25 means a large occupier is **degraded, not
  disarmed**, and unlike a one-off subtraction the cost persists for as long as the
  ground is held. Tests:
  `PartialSystemsTests.Occupation_LowersReadinessAndSurvivesTheMonthlyTick`,
  `Occupation_DegradesButDoesNotDisarm`.

  `ReadinessDirectiveBonus` is **+8** while the player's Military official is
  `Directed` with `MIL_READINESS`, and **0 for every other country** — the Cabinet
  is the operator's institution, and directing our own defence ministry must not
  raise every army on the map (spec 15 §5). It is a target shift for the same
  reason `garrisonDrag` is.
- **Supply** always moves toward an equilibrium at 3.0/month. The ceiling is
  `55 + industrialCapacity × 0.30 + logistics × 0.20 + CAP_LIFT × 12`. Holding a
  posture *lowers that equilibrium* rather than draining without bound:

  ```
  drag = 30 (Forward) | 18 (Alert or at war) | 0 (Peacetime)
  drag += 12 if at war
  drag ×= max(0.35, 1 − logistics/160) × (1 − CAP_LIFT × 0.35)
  supply → clamp(ceiling − drag)
  ```

  A force at alert settles lower and a force forward lower still, but neither
  hollows out on its own. **This was previously a flat monthly subtraction whose
  only recovery branch was unreachable above Peacetime**, so any state that stood
  alert bled to zero supply — halving its `EffectivePower` — by doing nothing but
  staying ready. It affected AI states equally. Regression tests:
  `MilitarySystemTests.HoldingAPosture_SettlesTheForceLowerButDoesNotEmptyIt`
  and siblings.

- Then `treasury −= PostureUpkeep`, `logistics −= 0.15`, and the Forward threat
  broadcast.

## 3. Strategic locations

Meaningful targets only (GDD §19) — never every city. Types: `Capital`, `Port`,
`Airbase`, `IndustrialCenter`, `EnergyRegion`, `MountainPass`, `Chokepoint`.

Each has `defenseValue` (terrain/fortification), `strategicValue` (what losing it
costs the owner), `garrison`, `ownerId` and `originalOwnerId` (so occupation is
visible via `IsOccupied`).

Ownership changes through a successful `Assault`, a `Withdraw` from ground we
hold, or a negotiated settlement.

### 3a. What territory is worth (`TerritorySystem`)

Locations used to be operation targets and nothing else: no system outside the
annual evaluation read `ownerId`, so taking an energy region supplied no energy
and taking a works produced no industry. **War could not pay for itself**, which
is the actual reason a military playstyle graded below doing nothing — the
balance harness was reporting a missing feature, not a tuning problem.

`Swing(state, country, type)` is the strategic value of a type currently held
minus what that country started with — positive for conquest, negative for loss,
and exactly symmetric between the two parties. It feeds:

| Yield | Multiplier | Consumer |
|---|---|---|
| `EnergySwing` | 0.28 | energy ceiling in `EconomySystem` |
| `IndustrySwing` | 0.25 | industrial capacity, applied as a drift so a works does not teleport home |
| `TradeAccessSwing` | 0.20 port / 0.14 chokepoint | `TradeHealth` |
| `ProjectionSwing` | 0.18 | readiness target in `MilitarySystem` — basing is reach |
| `OccupiedValue` | 0.12, capped at 25 | readiness target in `MilitarySystem` — garrison drag, the other direction |
| `DefensiveDepth` | 0.15 | reserved for defensive resolution (PLANNED) |

Completed energy-site works additionally contribute 7 energy-ceiling points per
owned, non-denied EnergyRegion (`energyWorks`; economy spec §3b). This is separate
from held-minus-original strategic value: construction at home must not cancel
itself. The works stay with the site through ownership changes; denial suppresses
their output without destroying them. Other territory yields, original values,
occupation bills and military resolution are unchanged.

**Occupation is a trade, not income.** `TerritorySystem.MonthlyUpdate` charges,
per point of occupied strategic value: −0.55 treasury, −0.006 stability, +0.008
war exhaustion. The readiness cost is charged in a different place and in a
different form — `MilitarySystem.MonthlyUpkeep` shifts every branch's readiness
*target* down by `garrisonDrag` (§2e), because an army sitting on hostile ground
is not available elsewhere. It has to be a target shift: `MonthlyUpdate` runs
after the readiness drift, so a subtraction taken here is undone in the same tick.
The occupied country loses unity and approval but *gains* war support — it hardens
rather than folds. `RecordSeizure` additionally costs the
seizer diplomacy and raises every other state's threat perception, but only for
ground that was not theirs to begin with: **recovering your own territory is not
an annexation.**

### 3b. Geography and reach (`GeographySystem`, GDD §16)

Until this existed, geography was decoration. `mapX`/`mapY` fed the ASCII world
map and **nothing else**, so any state could assault any location on earth at
identical cost and identical odds. A landlocked regional power could campaign
across the planet as easily as against its neighbour, and a navy — the single
most expensive thing a country can buy — bought nothing that distance made
valuable. The same shape as §3a's finding: a field written at world creation and
read by no system that decides anything.

**Position is authored, not saved.** Coordinates stay in
`WorldFactory.Profiles`. Geography does not change during a save, so putting it
in `GameState` would only add schema to keep in sync and a migration step to
write. Reach therefore costs **zero save-schema change** — the only persisted
value it adds is one float on an existing `OperationRecord` (below), which
defaults to `1f` and so reads correctly out of any older save.

#### The world is a cylinder

`Distance(ax, ay, bx, by)` measures the short way round: `dx` is folded at
`MapWidth / 2`, with `MapWidth = 78`.

```
dx = |ax − bx| ;  if dx > MapWidth/2 then dx = MapWidth − dx
dy = (ay − by) × LatitudeWeight
distance = √(dx² + dy²)
```

**Wraparound is load-bearing, not a nicety.** Measured as raw column distance,
the United States (x 14) is *closer to China* (x 62, Δ48) than to Japan (x 71,
Δ57), and every trans-Pacific relationship in the roster inverts: the Pacific
partners become the far ones and the Pacific rival becomes the near one. Folded
at 39, Japan is Δ21 and China Δ30, which is the reading the roster was authored
to express. `GeographySystemTests.LongitudeWraps` is the guard.

**`GeographySystem.MapWidth` is the single authority on the width of the world.**
`AsciiWorldMap.Width` is declared `= GeographySystem.MapWidth` rather than as its
own literal, and must stay that way: the renderer and the distance model have to
agree, because the whole premise of the system is that what the operator sees on
the MAP view *is* where things are. Two independent literals is a latent bug —
distances would fold at one modulus while the chart the player reasons from was
drawn at another, and the disagreement would show up only as operations resolving
at weights the map did not predict. Core owns the constant; the view derives from
it (spec 09 §9).

`LatitudeWeight = 2`. The authored grid is roughly 78 × 21 for a whole world and
terminal cells are about twice as tall as they are wide, so a step in Y covers
about twice the ground a step in X does. Weighting them equally would make
north–south neighbours look like hemispheres apart.

#### `originalOwnerId` is geography; `ownerId` is control

`HostOf(location)` returns `originalOwnerId`, falling back to `ownerId` only
where the former is blank. It answers *where a location physically sits*, never
*who holds it today*.

A captured port projects from the country it sits in, not from the capital of
whoever took it. Measuring it from the conqueror instead would let a state
**teleport its own geography** by taking ground — seize one thing anywhere and
the whole map draws itself around your capital again. Reading the host is what
makes a forward position worth taking, and what makes basing rights
(`foreignOperatorId`) worth negotiating for. Test:
`TakingGroundMovesControlNotTheGround`.

#### `ProjectionRange` — what buys distance

```
range = BaseReach (8)
      + naval.EffectivePower × 35
      + air.EffectivePower   × 20
      + logistics            × 0.15
      + CAP_LIFT effectiveness × 10
```

| Term | Why it is weighted that way |
|---|---|
| `BaseReach` 8 | Every state can act in its own neighbourhood without owning anything. Roughly one grid step plus a latitude band |
| Naval ×35 | **A navy is the main thing that buys distance, which is the entire strategic argument for having one.** It is deliberately the largest coefficient in the formula: before this, the most expensive branch in the game had no effect that ground or air did not also have |
| Air ×20 | Airlift extends reach but does not sustain a campaign the way hulls do |
| Logistics ×0.15 | A 0..100 standing value rather than a branch power, so a small coefficient still contributes ~15 at the ceiling |
| `CAP_LIFT` ×10 | Strategic lift is a capability, so it multiplies what the force can do rather than adding force (spec 13) |

`EffectivePower` is already gated on readiness and supply (§1), so an unready
fleet buys correspondingly less reach — a paper navy does not project.

#### `EffectiveDistanceTo` — the nearest place we can fight from

The minimum over three origins: the homeland, **any location we hold abroad**,
and **anywhere we have basing rights**, each measured with `HostOf` to the
target's host country.

```
best = DistanceBetween(actor, host)
for each location where ownerId == actor or foreignOperatorId == actor:
    penalty = 2 if hosted-only, 0 if held
    best = min(best, DistanceBetween(HostOf(location), host) + penalty)
```

The **+2 hosted penalty** is the difference between a base and a base of our
own: a partner's ground is reach we did not have to conquer, but it is someone
else's, subject to their politics and their revocation, and worth slightly less
than ground we hold. It is a small number on purpose — hosting should be clearly
worth negotiating for, just never quite as good as owning. Tests:
`CapturedGroundBecomesAForwardPosition`, `BasingRightsExtendReachWithoutConquest`.

#### `ReachFactorTo` / `ReachFactorFor` — the multiplier

```
if distance ≤ range  →  1.0
else                 →  max(MinimumReach, range / distance)
MinimumReach = 0.2      (0.35 until the 2026-08 playtest: Kazakhstan was occupying the Gulf Coast)
```

`ReachFactorFor(state, actorId, location)` is the same call routed through
`HostOf`, and returns `1f` for a null location.

**Distance prices a far campaign; it never forbids one.** The floor is the whole
rule. A hard geographic gate would repeat the §18.1 mistake — escalation states
used to block operations outright instead of pricing them, and the fix there was
to charge +2 CP and self-escalate rather than refuse the order (§4). Geography
gets the same treatment: an expedition beyond reach fights at 35% of full weight,
which is usually a bad idea and never an unavailable one. Tests:
`FightingAcrossTheWorldCostsStrength`, `ReachNeverFallsBelowTheFloor`.

Note the converse, which is the point of the whole system:
`FightingNextDoorIsAlwaysFullStrength` asserts that a neighbour is inside
anyone's reach regardless of force. Fighting near home is the one advantage a
smaller power reliably has, and reach must not take it away.

`ReachText` translates the factor for the terminal:

| Factor | Reads |
|---|---|
| ≥ 0.999 | `WITHIN REACH` |
| ≥ 0.80 | `EXTENDED` |
| ≥ 0.60 | `OVEREXTENDED` |
| < 0.60 | `BEYOND REACH` |

#### Where it lands in resolution

`ResolveOperation` multiplies **`attacker.military.TotalPower × reach`** before
coalition support is added, and records the value on
`OperationRecord.reachFactor`. Coalition contribution is deliberately outside the
multiplier: a partner fighting in its own region is not subject to our distance.

**The defender is never scaled.** Distance is a tax on the attacker only —
fighting near home is the smaller power's standing advantage (above), and
applying reach symmetrically would hand the tax straight back. `Withdraw` returns
before the reach calculation and is unaffected: leaving is not an operation
distance can interfere with.

When an operation fails and `reachFactor < 0.8`, the after-action summary appends
`Force projected at N% of full weight (BAND)`. This is not decoration: **a player
who cannot see that the force never arrived at full weight reads a run of
failures as unfair dice rather than as the map.** The threshold matches the
`EXTENDED` band, so the line appears exactly when distance was a plausible
contributor and stays quiet when it was not. Tests:
`DistanceWeakensAnActualOperation` (nearby resolves at exactly 1.0, distant
below it, through the real resolver), `AFailedDistantOperationSaysWhy`.

Reach is measured against the **target's host**, so the same objective costs
different states different weight — which is what finally makes basing,
chokepoints and a blue-water fleet strategically distinct rather than three ways
of spending treasury.

#### Reach bounds AI ambition, not AI fear

`AISystem` multiplies the **`AssertClaim`** objective's priority by
`GeographySystem.ReachFactorTo(state, country, other)` (spec 06 §4). A government
does not press a claim it has no way to prosecute, so **a weak neighbour is a far
more attractive target than an equally weak state on the other side of the
world.** Without it, regional powers picked fights across the planet and
geography was invisible in how the world *behaved* — visible only in how an
operation resolved once someone had already made an implausible decision. This is
the main channel through which the system reaches the simulation at all.

**`CounterRival` is deliberately left unweighted.** A distant threat is still a
threat, and discounting it by reach would model a government as unconcerned about
a rival it cannot invade — which is not how states behave. The answer to a threat
you cannot reach is sanctions, alignment and collection, all of which
`CounterRival` can already choose. Only *ambition* is bounded by reach; alarm is
not. Test: `GeographySystemTests.AmbitionIsBoundedByReach`.

#### The operator is told before the order, not after

`MilitaryView` prints a **REACH** line in the operation-planning block as soon as
a target is selected, above the operation-type buttons:

```
REACH: WITHIN REACH — force arrives at full weight.
REACH: OVEREXTENDED — force arrives at 62% of full weight.
                      Basing or ground held nearer would help.
```

Dimmed at full weight, bright otherwise, and it names the remedy rather than only
the penalty. This is the principle established by the reporting work (spec 15):
**a penalty the player cannot see coming reads as a bug rather than as a
consequence.** The after-action line (above) is the same information arriving too
late to act on; the planning line is what makes reach a decision instead of a
post-mortem.

### 3c. The post-war exit (`CanRelinquish` / `RelinquishBy`) — C5

> **Reconstructed C5. Original acceptance verdict: FAIL.** The mechanism below is
> the one that was built and measured. It is known to lack strategic-containment
> awareness — a burdensome occupation that is also *containing a recent aggressor*
> is released on the same terms as any other. That defect is C5B's subject and is
> deliberately still present here. Nothing in this section certifies the rule.

Ground taken in a war could not be put down once the war ended:

- a settlement cedes only the **objective**, not everything else captured;
- closing a confrontation releases nothing at all;
- `Withdraw` is an operation and needs a **live** confrontation to be ordered in;
- no AI government had any verb for it.

So `OccupationUpkeepPerValue` and the insurgency bill ran against that ground for
the rest of the save with no reachable exit. C4 fixed the fiscal *rules* and this
was what remained underneath them.

**`CanRelinquish(state, actorId, locationId, out reason)`** succeeds only when the
location exists, the actor holds it, it is occupied, the original owner still
exists as a state, and **there is no unresolved confrontation between the holder
and that owner**. The last clause is the load-bearing one: while the shooting is
on, giving ground back is a battlefield decision and belongs to the operation
verbs. This is for after.

**`RelinquishBy(state, actorId, locationId)`** sets `ownerId = originalOwnerId`,
floors the returned garrison at `ReturnedGarrisonFloor` (20), zeroes
`pacification`, charges the holder `RelinquishWarSupportCost` (6) war support,
adds a `+RelinquishMemoryWeight` (3) *"Returned occupied ground"* memory to the
pair, and files a public chronicle entry.

What it deliberately does **not** do, each for a reason:

| Not done | Why |
|---|---|
| `Cede` | A cession rewrites `originalOwnerId` — a recognised transfer of title. The title never moved here. |
| `RecordSeizure` | That prices *taking* ground. |
| Open a confrontation | Handing ground back is not an act of war. |
| Create a truce | Truces are settlement machinery; this is not a settlement. |
| Touch sanctions, basing, fiscal or restructuring state | None of them are what occupation is. |
| Delete the insurgency | A movement fades through `SupportTargetFor` once the ground stops being occupied — the existing mechanism, not a special case. |

**`HoldingBill(state, location)`** reads what the treasury actually pays —
`strategicValue × OccupationUpkeepPerValue` plus the standing insurgency bill if
something is burning there. Read rather than re-derived: a second definition of
what holding costs would drift from the one the money leaves by.

**`AnswersShortfall(state, holder, location)`** uses the same
`ShortfallThreshold` (40) on energy and strategic materials that
`AISystem.ResourcePrize` reads when it decides a neighbour's ground is worth
taking. A government using one number to seize and another to let go would be two
governments.

**`HoldingRunwayMonths` = 36** — months of holding bill a treasury must cover
before the occupation counts as affordable. See [spec 06 §6b](06-AI.md) for the
decision that reads it.

The player's route is `GameController.RelinquishLocation`, 1 CP, gated on
`MayCommand(Pillar.Military)` and the same `CanRelinquish`, recording initiative.
**Never automatic** — a garrison the operator chose to leave in place stays there
however much it costs.

## 4. Operations

**Everything about an operation lives in one record.** `Core/OperationCatalog.cs`
holds an `OperationProfile` per `OperationType`: domain, cost, branch weights,
intensity, civilian factor, defence model, targeting, and its availability
requirements. This replaced six scattered switch statements in `MilitarySystem`,
where a verb given a row in five of them was a differently-named assault that
looked finished. At twenty-three verbs that stopped being a risk and became a
certainty. `OperationCatalogTests.EveryOperationHasAProfile` fails the build if
the enum and the catalog drift apart.

Twenty-three verbs across four domains (`OperationDomain`), which is also how the
order screen groups them — a flat button row does not fit a phone.

| Verb | Domain | CP | Fought against | Notes |
|---|---|---|---|---|
| `Assault` | Ground | 3 | Garrison | **Takes ground** |
| `Raid` | Ground | 2 | Garrison | Hit and withdraw |
| `Siege` | Ground | 2 | Garrison | Isolate and grind |
| `PreparedDefense` | Ground | 2 | Unopposed | **Own ground**; `defenseValue +9` |
| `CounterInsurgency` | Ground | 2 | Insurgency | **Own occupied ground**; `pacification +12` |
| `Withdraw` | Ground | 1 | Unopposed | Either side's ground |
| `NavalBlockade` | Naval | 3 | Their fleet | Strangles national trade |
| `SeaControl` | Naval | 3 | Their fleet | Needs them to *have* a fleet |
| `CommerceRaiding` | Naval | 2 | Their fleet ×0.6 | Trade **and** treasury |
| `MineWarfare` | Naval | 2 | Their fleet ×0.45 | Ports and chokepoints only |
| `ConvoyEscort` | Naval | 2 | Their fleet ×0.55 | **Own ground**; the answer to a blockade |
| `SuppressDefenses` | Air | 2 | Fortifications | `defenseValue −18`, permanent |
| `AirStrike` | Air | 2 | Air defences | Civilian factor **2.6** |
| `CounterAirCampaign` | Air | 3 | Their air force | The only way to destroy enemy air power |
| `NoFlyZone` | Air | 3 | Their air force ×0.7 | Grounds rather than destroys |
| `AirInterdiction` | Air | 2 | Air defences | Cuts their supply |
| `StrategicBombing` | Air | 3 | Air defences | Civilian factor **3.0**; industry/energy/capital only |
| `LeadershipStrike` | Air | 3 | Counterintelligence | Capitals only; see below |
| `SpecialOperation` | Joint | 1 | Counterintelligence | Cheap, precise, deniable |
| `AmphibiousAssault` | Joint | 4 | Garrison ×1.3 | **Takes ground**; coastal only |
| `CyberOperation` | Joint | 1 | Counterintelligence | Runs on the **intelligence pillar** |
| `MissileDefense` | Joint | 3 | Unopposed | **Own ground**; `missileDefense +10` |
| `NoncombatantEvacuation` | Joint | 1 | Unopposed | Removes a liability before it starts |

A flat price would leave one verb strictly best now that they no longer resolve
the same way. Priced apart, **sequencing is a decision**: suppression then
assault costs 5 CP and buys much better odds than a bare 3 CP assault. That it
must *not* be strictly dominant is tested
(`OperationVerbTests.SofteningFirstIsAChoiceRatherThanTheAnswer`).

**`LeadershipStrike` is deliberately double-edged.** On success their
`militaryLoyalty −9` and `stability −7`, but their `warSupport +14` and
`nationalUnity +6` — a country whose government is struck closes ranks. It costs
the attacker `diplomacy −7` and **trust with every state in the world**, charged
whether or not it achieved anything. Without that it would simply be the best
opening move and the diplomacy pillar would stop mattering. Test:
`OperationCatalogTests.ALeadershipStrikeCostsUsStandingEvenWhenItWorks`.

`ConfrontationSystem.OperationCostFor` is the single source of truth — the order
screen prints it and `LaunchOperation` charges it. They must not be computed
separately: `TerminalView.GateOnAffordability` parses the printed `[N CP]` tag,
so drift silently disables affordability dimming.

Ordering an overt operation while below Limited Conflict costs **+2 CP** and
escalates the confrontation to Limited Conflict by itself — GDD §18.1 requires
escalation states to describe the situation rather than gate orders, so the act
is priced rather than refused. **Defensive verbs on our own ground are exempt
from both**: fortifying a position we hold cannot be the thing that starts the
shooting.

`WithinEscalationLimit` is checked before the player wrapper spends CP and again
inside actor-generic resolution. This shared preflight matters for standing
orders: an authorization whose saved ceiling forbids opening Limited Conflict
waits without paying for an operation the resolver will refuse. Manual and
standing execution use the same rule.

### 4-0. Availability — `OperationCatalog.CanOrder`

One gate, shared by the order screen and `LaunchOperationBy`, so what the
operator is offered and what the simulation accepts can never disagree. It
returns a *reason*, and the reason matters as much as the refusal: `WE HAVE NO
FLEET` reads as the consequence of a procurement decision made three years ago,
which is what it is, while a greyed-out button with no explanation reads as a bug.

Checks, in order: whose ground it is (`OperationTargeting`), whether the ground is
occupied (`needsOccupied`), the location type (`locationTypes`), whether we have
the branch (`needsOwnNavy` / `needsOwnAir`), whether the target country has sea
access at all (`needsMaritimeTarget`), and whether they have anything to fight
with (`needsEnemyAir` / `needsEnemyNavy`).

It is a *possibility* check only. Affordability is handled separately, and
ordering something unwise remains the player's right.

**Sea access is authored world data** (`CountryProfile.navalAccess`, three levels:
`Landlocked` / `Coastal` / `Maritime`), following the same rule as map
coordinates — a coastline does not change during a playthrough, so it does not
belong in the save. Naval strength is derived from it at world creation
(`WorldFactory.NavalScaleFor`: 0 / 0.55 / 0.95 of the military score), replacing
a flat 0.75× for every country. Before this, landlocked Kazakhstan launched
fleets and drew global naval reach on exactly the terms the United States did,
and "we cannot blockade them, they have no sea" was a sentence the simulation had
no way to say.

> **Draw the naval roll unconditionally and then zero it.** Skipping the `Jitter`
> call for a landlocked state shifts the world-generation random stream for
> everything created after it, silently regenerating the world downstream of
> Kazakhstan and moving measured balance figures that have nothing to do with
> navies. This cost a debugging pass.

### 4-1. Targeting — the pillar finally has defensive vocabulary

`OperationTargeting` is `EnemyGround`, `OwnGround` or `Either`. Until the wider
list every operation was conducted on someone else's ground, so fortifying a
position, pacifying occupied territory, escorting our own convoys and defending
against missiles could not be expressed at all. The MILITARY order screen now
offers our own locations as objectives for exactly this reason.

**Defensive programmes need no confrontation.**
`ConfrontationSystem.RequiresConfrontation(type)` is false for every
`OwnGround` verb, and `LaunchOperation` / `LaunchOperationBy` accept a null
confrontation for them. `MilitaryView.BuildDefensiveProgrammes` is its own panel,
built *before* the confrontation console and outside it.

This matters more than it sounds. Fortifying a position, pacifying occupied
ground, escorting our own shipping and building the shield are most valuable
*before* a war — reachable only during one, every single one of them was
reachable only once it was too late to be worth having.

With no confrontation in play the resolution path skips what does not exist: no
momentum, no war exhaustion, no casualties on a war ledger, no coalition support
(nobody joins us in fortifying our own ground). The record goes to the
**chronicle** rather than to a confrontation's after-action log — it is not part
of anybody's war, and filing it under one would be the wrong history. Player
notifications drop to `Advisory`, because routine national work does not deserve
a battle's weight.

> **The sequence counter must advance.** Peacetime programmes seed their RNG from
> `state.NextActionSequence()`, not from the raw `actionSequence` field. Reading
> the field seeded every programme in a month identically, so twenty consecutive
> orders were one draw repeated twenty times and a failed roll could never be
> retried — the covert-op RNG collision this project already fixed once, running
> the other way. Caught by
> `OperationCatalogTests.DefensiveProgrammesDoNotNeedAWar`, and the determinism
> test beside it now carries a non-vacuity guard because two runs that both did
> nothing are also equal.

Offensive verbs still require a confrontation
(`OperationCatalogTests.OffensiveVerbsStillNeedOne`): shooting at another state
*is* a confrontation by definition, and peacetime reachability must not become a
way to conduct a war nobody declared.

#### 4-1a. On our own ground there is no opponent

Reported from play: *"I am trying to improve the defence of a territory I took
over but I keep failing with no direction on why."* Four separate defects, all
from one root — `defender` is `state.FindCountry(target.ownerId)`, which for an
`OwnGround` verb is **us**.

1. **Every "and now bill the other side" line billed us a second time.**
   Manpower twice over, war exhaustion twice over, and `AwardExperience` called
   for both winning and losing the same engagement. Fortifying a position we
   held cost more than attacking one we did not. `ApplyOperationCosts` now nulls
   `defender` when `defender.id == attacker.id`; every downstream site was
   already `defender != null`-guarded, so nothing else had to change.
2. **Falling short was reported as a lost battle.** The failure branch was
   shared with offensive operations, so it charged −5 war support, wrote
   *"Operation against \<a place we own\> failed"*, and chronicled
   `Publicity.Public` — which put **"Failed operation at …"** out on the world
   wire. `OwnGround` now has its own branch: no war-support cost, wording that
   describes unfinished work, and a `Publicity.Secret` entry (ours to read in
   CHRONICLE, never on the wire).
3. **Depletion ran backwards.** `DepletionFactor` reads the *target's* garrison
   and works as "the position is collapsing", which on our own ground meant a
   thinly held occupation was scored as *easier* to pacify. It is skipped
   entirely for `OwnGround`; the opposition there is the insurgency, or nobody.
4. **The report could not say why.** `RecordDefence` deliberately skipped
   `DefenseModel.Unopposed`, so a failed programme produced an analysis with
   **no defence factor in it** — nothing to rank, nothing for `Advice` to switch
   on, and therefore no `WHAT WOULD CHANGE IT` line. The one class whose entire
   job is to explain an outcome could not. `Labels.Undertaking` ("The scale of
   the work") now records it, with advice naming ground strength, readiness,
   supply and logistics as what decides an unopposed programme.

Also `Labels.Speed` had no advice case and fell through to the generic line.
`ForceInventoryTests.EveryFactorLabelHasAdviceBehindIt` now **reflects over the
`Labels` constants** instead of a hand-copied list beside them, with an explicit
exempt set for the four labels that can only ever help the attacker (`Coalition`,
`Isr`, `Familiarity`, `Depleted`). That list may only shrink.

**The panel says it too.** `BuildDefensiveProgrammes` prints each verb's assessed
odds on its own button (through `MilitarySystem.EstimateOdds`, the same function
that resolves it), flags occupied ground explicitly, and shows the last
programme's after-action report — which previously existed only as one ADVISORY
notification, since a peacetime programme has no confrontation diary to live in.

### 4a. Scale — `PowerScale`

`MilitaryState.TotalPower` is `0..3`; `garrison` and `defenseValue` are `0..100`.
These were **added directly**, so a national army contributed roughly 4% of the
defence figure and `odds` sat near 8% no matter what had been built. Procurement,
readiness, doctrine, ISR, reach and coalition support were all multipliers on a
term too small to matter — which is exactly what "I recruited allies and it
changed nothing" feels like from inside the game.

`MilitarySystem.PowerScale = 30f` converts branch power onto the garrison scale.
**Anything comparing a force to a fortification must go through it.**

### 4b. Force composition — `BranchPowerFor`

Which branches actually carry out the operation. This is what makes force
composition a decision rather than a stat: build a lopsided military and whole
categories of operation stop being available in practice, without anything
forbidding them.

Weights live on the profile (`ground` / `air` / `naval`) and need not sum to one —
`SpecialOperation` sums to 0.50 because it is small by definition, and makes up in
precision what it lacks in weight. Representative values:

| Verb | Ground | Air | Naval |
|---|---|---|---|
| `Assault` / `Withdraw` | 0.60 | 0.25 | 0.15 |
| `AmphibiousAssault` | 0.45 | 0.15 | 0.40 |
| `Siege` | 0.85 | 0.15 | — |
| `CounterInsurgency` | 0.90 | 0.10 | — |
| `Raid` | 0.50 | 0.50 | — |
| `SpecialOperation` | 0.35 | 0.15 | — |
| `AirStrike` / `CounterAirCampaign` / `NoFlyZone` / `StrategicBombing` | — | 1.00 | — |
| `SuppressDefenses` | — | 0.75 | 0.25 |
| `NavalBlockade` | — | — | 1.00 |
| `MineWarfare` | — | 0.10 | 0.90 |
| `CyberOperation` | — | — | — |

`CyberOperation` has **no branch weight at all**: `usesIntelligence` routes its
power through `pillars.intelligence` instead. It is the only verb like this, and
it is why the flag exists rather than a fourth branch weight — a country with a
small army and a good intelligence service should be dangerous in exactly this one
way. `MilitarySystem.OperationPower(country, type)` is the wrapper that handles
both; `BranchPowerFor` remains the pure branch calculation.

Resolution (`MilitarySystem.ResolveOperation`):

```
reach        = GeographySystem.ReachFactorFor(attacker, target)   ← §3b, 0.2..1
attackPower  = OperationPower(attacker, type) × PowerScale × reach
               + max(0, coalitionSupport) × PowerScale
defensePower = DefensePowerFor(profile, attacker, defender, target)   ← §4b-1

doctrineFamiliarity: attack ×= 1 + fam/320 ;  defense ×= 1 + fam/400
missile defence:     attack ×= 1 − their missileDefense/220   (aerial verbs only)

Assault / AmphibiousAssault: attack ×= 0.85 + speed × 0.35
Siege:                       attack ×= 0.75 ; defense ×= 0.70
Raid:                        attack ×= 0.60 ; defense ×= 0.80

CAP_ISR: attack ×= 1 + isr × 0.15
doctrine (see table below): attack ×= …

odds = attackPower / max(0.01, attackPower + defensePower)
success = roll < odds
```

`IsAerialDelivery` gates the missile-defence term to `AirStrike`,
`StrategicBombing` and `LeadershipStrike`. A shield that made a ground assault
harder would be a generic defence bonus in a costume, and the verb would stop
meaning anything.

Only `Assault` and `AmphibiousAssault` transfer ownership (`CanTakeGround`).
`Withdraw` always succeeds, costs momentum, and preserves the force.

### 4b-1. Defence models — `DefenseModel`

The single most important idea in the verb list. If everything resolves against
the garrison then every verb is an assault with a different name, and twenty-three
buttons collapse back into one.

| Model | Defence |
|---|---|
| `Garrison` | `defender.TotalPower × PowerScale × 0.55 + garrison × 0.35`, then `× (1 + defenseValue/140)` |
| `Fortifications` | `defenseValue × 0.55` |
| `AirDefenses` | `garrison × 0.12 × (1 + defenseValue/90)` |
| `EnemyAir` | `their air.EffectivePower × PowerScale × 1.1` |
| `EnemyNavy` | `their naval.EffectivePower × PowerScale × 1.1` |
| `CounterIntel` | `garrison × 0.15 + counterIntelligence × 0.55` |
| `Insurgency` | `strategicValue × 0.30 + max(0, 60 − pacification) × 0.45` |
| `Unopposed` | `12 + defenseValue × 0.05` |

`defenseScale` on the profile then multiplies the result — `AmphibiousAssault` at
1.3 because an opposed landing is the hardest thing a military can attempt,
`MineWarfare` at 0.45 because minelaying is not a fleet action.

### 4c. What a non-capturing success does — `ApplyNonCapturingSuccess`

Each of these leaves the map unchanged and the *situation* different.

| Verb | Effect |
|---|---|
| `SuppressDefenses` | `defenseValue` −18, **permanent** |
| `AirStrike` | garrison −14; defender industrial capacity and stability down |
| `NavalBlockade` | every trade link of theirs −8 volume; ground/naval supply and economic confidence down |
| `SpecialOperation` | garrison −10; defender `militaryLoyalty` −2 |
| `Siege` | garrison −12 |
| `Raid` | garrison −8 |
| `PreparedDefense` | **our** `defenseValue` +9, garrison +4 |
| `CounterInsurgency` | `pacification` +12; our stability +0.8 |
| `SeaControl` | their naval strength −7, naval readiness −9 |
| `CommerceRaiding` | their trade −4/link, treasury −70, confidence −2.5 |
| `MineWarfare` | their trade −6/link, naval supply −10; `defenseValue` −6 |
| `ConvoyEscort` | **our** trade +5/link, confidence +3, naval supply +4 |
| `CounterAirCampaign` | their air strength −8, air readiness −10 |
| `NoFlyZone` | their air readiness −16, air supply −10, stability −1 |
| `AirInterdiction` | garrison −6; their ground supply −11, naval supply −4 |
| `StrategicBombing` | their industry −5, energy −4, confidence −7, **warSupport +5** |
| `LeadershipStrike` | their `militaryLoyalty` −9, stability −7, **warSupport +14**, unity +6; our diplomacy −7 and trust −6 with everyone |
| `AmphibiousAssault` | garrison −16 (when not holding) |
| `CyberOperation` | their readiness −5 on all three branches, confidence −3 |
| `MissileDefense` | **our** `missileDefense` +10 |
| `NoncombatantEvacuation` | our stability +1.5, warSupport +3 |

Suppression is the clearest case: it takes nothing, kills almost nobody, and
makes every later operation against that position easier — a strategic move the
three-verb list had no way to express.

Note the two that raise the *defender's* `warSupport`. Bombing a country's
industry or striking its government hardens it; neither is a shortcut to a
settlement. That is the §27 rule — atrocity is never rewarded — applied to the
conventional instruments that most invite it.

`OperationCatalogTests.EverySuccessfulOperationChangesSomething` resolves every
verb until it succeeds and fails the build if the world comes back byte-identical.
That is the "written but never read" bug class in its operational costume, and it
has caught real decoration in this codebase more than once.

### 4c-1. Attrition — losses land on what fought

`ApplyAttackerAttrition` distributes losses across branches **by the same profile
weights that decided the outcome**, so it can never fall out of step as verbs are
added. `usesIntelligence` verbs cost no branch strength at all.

`ApplyDefenderAttrition` is keyed by `DefenseModel`, mirroring `DefensePowerFor`
exactly. The two must agree: computing odds against their fleet and then billing
the losses to a garrison is how repeated blockades used to empty a position the
navy never went near.

| Model | Their losses come off |
|---|---|
| `EnemyNavy` | their naval strength ×0.5 |
| `EnemyAir` | their air strength ×0.5 |
| `Fortifications` | `defenseValue` ×0.5, their air ×0.4 |
| `AirDefenses` | garrison ×0.7, their air ×0.25 |
| `CounterIntel` | garrison ×0.4 |
| `Insurgency` | `pacification` **+**0.3 — what is worn down is the resistance |
| `Unopposed` | nothing |
| `Garrison` | garrison ×1.0, their ground ×0.3 |

### 4d. Coalitions

`DiplomacySystem.CoalitionStrength(state, confrontation, leaderId, operationType?)`.
Each non-leader member contributes `BranchPowerFor(member, operationType) × 0.3 ×
(1 + interoperability/130)`; the figure enters resolution as
`coalitionSupport × PowerScale`.

Passing the operation type matters: summing a partner's whole military into an
air strike we fly with our air force alone would make force composition matter
for us and not for them, and would make a landlocked ally useful in a blockade.
The no-type overload remains for the diplomacy screen's general "added strength".

Both the MILITARY order screen and resolution use the typed form, and the screen
prints `OUR COMMITTED WEIGHT` beside `PARTNERS ADD` — the comparison *is* the
message. Coalition weight was always real; it was invisible and added to a figure
too small to move an outcome, which is why recruiting allies felt inert.

### 4e. AI operation selection — `AISystem.ChooseOperation`

Governments other than the player pick from the same list. The AI previously
chose only between `Siege` and `Assault`, so every verb added for the player was
one the world could not use.

**It considers only what `OperationCatalog.AvailableAgainst` returns.** Preference
has to be expressed inside what is actually possible, or a maritime doctrine
spends every month proposing blockades of a landlocked neighbour and achieving
nothing.

Ordered: suppress dug-in works (`defenseValue > 50`, p=0.55) → contest the sky
(`CounterAirCampaign`, p=0.30) → if naval-heavy against a sea-trading opponent,
blockade (0.35) or commerce raiding (0.30), else sea control (0.30) or mining
(0.25) → a government on `EconomicPrimacy` reaches for `StrategicBombing` (0.40),
breaking the country rather than the army → cautious governments reach for
`AirStrike` (0.35), `CyberOperation` (0.30) or `SpecialOperation` (0.30) before
spending their own people → `AirInterdiction` against a strong garrison (0.30) →
`Siege` when momentum is bad → `AmphibiousAssault` if it is the only way in, or
if the fleet outweighs the army (0.30) → otherwise `Assault`.

**Every branch falls through to `Assault`**, or to whatever is possible at all,
because a government that never takes ground never wins.
`OperationVerbTests.TheWorldCanUseTheVerbsTheOperatorCan` simulates 90 AI-only
years and fails if the world reaches for fewer than three kinds of operation.

### Doctrine changes how the force fights (GDD §19)

Applied after the operation-type and capability multipliers, and carried into the
loss and civilian-harm calculations:

| Doctrine | Attack | Own losses | Enemy losses | Civilian harm |
|---|---|---|---|---|
| Balanced | ×1.00 | ×1.00 | ×1.00 | ×1.00 |
| Maneuver | ×1.12 | ×0.75 | ×1.00 | ×1.35 |
| Attrition | ×1.05 | ×1.30 | ×1.45 | ×1.00 |
| Deterrence | ×0.88 | ×0.85 | ×1.00 | ×0.70 |

Each is a genuine trade rather than a tier. **Maneuver** buys tempo and spares
its own troops, and pays for it in civilian harm — which raises the defender's
war support and costs the attacker diplomacy, so a fast war is a war that hardens
its opponent. **Attrition** wins the exchange and ruins both armies doing it.
**Deterrence** is built to threaten rather than to attack: it is the *worst*
doctrine for taking ground and the only one that improves terms at the table
(+10 settlement willingness, §5). A country cannot have all three.

`CAP_PRECISION` reduces civilian harm by up to 45% *before* the doctrine
multiplier, so precision and Maneuver partially cancel — the capability buys back
what the tempo costs.

### Delegated directive (GDD §19)

`OperationDirective` carries three `0..100` sliders that materially change
outcomes:

| Slider | Effect |
|---|---|
| `speedPriority` | Raises assault power; raises civilian harm |
| `casualtyTolerance` | Raises own losses (`0.6 + tol/100 × 0.8` multiplier) |
| `civilianRiskLimit` | Scales civilian harm directly |

### Costs applied per operation

**Intensity** — how much force is actually committed — sets both sides' losses:

| Verb | Intensity |
|---|---|
| `SpecialOperation` | 0.15 |
| `NavalBlockade` | 0.30 |
| `SuppressDefenses` | 0.35 |
| `Raid` | 0.50 |
| `AirStrike` | 0.55 |
| `Siege` | 0.70 |
| `Assault` | 1.00 |

**Attrition is routed to the force that actually fought**
(`ApplyAttackerAttrition` / `ApplyDefenderAttrition`). Charging every operation
to the ground force and the garrison made the verb list a lie — repeated
blockades emptied a garrison the fleet never engaged, and the army paid for
sorties it never flew, so blockade and suppression were just slow assaults.

| Verb | Our losses come off | Their losses come off |
|---|---|---|
| `AirStrike` | air ×0.6 | garrison ×0.7, their air ×0.25 |
| `SuppressDefenses` | air ×0.6 | `defenseValue` ×0.5, their air ×0.4 |
| `NavalBlockade` | naval ×0.6 | their naval ×0.5 — **garrison untouched** |
| `SpecialOperation` | ground ×0.3 | garrison, their ground ×0.3 |
| everything else | ground ×0.6 | garrison, their ground ×0.3 |

Flat for all: ground supply −6, air supply −4, treasury −45, manpower −losses × 4
on both sides. Both accrue war exhaustion.

**Civilian harm** (GDD §27) scales with intensity, permissiveness and speed, and
doubles against capitals and industrial centers. It also carries a **per-verb
factor**, because how force is applied matters more than how much of it is
committed — `AirStrike` ×2.6, `SuppressDefenses` ×1.2, `NavalBlockade` ×0.5,
`SpecialOperation` ×0.4. Without this the order screen's "heavy civilian risk"
warning on an air strike was false: its lower intensity made the standoff option
the *kindest* one available.

Above a threshold civilian harm reduces the attacker's diplomacy pillar and
*raises the defender's war support* — atrocity is never rewarded.

## 5. Confrontations

Opened with an **objective** (`TerritorialConcession`, `PolicyReversal`,
`ResourceAccess`, `Deterrence`) and a **Primary Strategy**. Cost 2 CP. One active
confrontation per country at a time.

### Escalation

`Peace → Tension → Crisis → LimitedConflict → TotalWar`. Levels may be skipped.

```
cost = 1 × steps + premium,  premium = max(0, steps − 1)
premium reduced by SkillEffect.EscalationDiscipline
```

Skipping levels also costs stability (−2/step) and war support (−3/step).
De-escalation may skip freely and costs nothing. Operations do **not** require
Limited Conflict — see §4.

### Escalation pressure (GDD §18.1)

`Confrontation.escalationPressure` is the hidden state underneath the visible
level. The level is what the world can see; pressure is what is actually building
beneath it. Floored at 0, never displayed.

| Source | Δ pressure |
|---|---|
| Escalating | +12 per step |
| De-escalating | −8 per step |
| A month spent standing at Crisis | **+3** |
| A month spent below Crisis | −2 |
| Sanctions imposed on the other party (`EconomySystem.ImposeSanctionsBy`) | +6 |
| A covert operation run against them (`IntelligenceSystem.RunCovertOperation`) | +8 |
| Boiling over | −25 |

**Dwell time is what makes the boil point reachable.** Every other input is a
one-off — a step up, a sanction, an operation — against a standing decay, so with
those alone pressure could not arithmetically reach 70 and the mechanic was inert
in a decade of measured play. At +3/month a crisis neither side defuses crosses
over in a little over a year: the cost of leaving one open is that eventually it
stops being your decision. The decay branch still applies **below** Crisis, so a
standoff deliberately held at Tension can sit there indefinitely — de-escalating
is worth something precisely because it stops the clock.

`ConfrontationSystem.AddPressure(state, actorId, targetId, amount)` is the entry
point for the non-military systems. It is a **no-op unless the two states are in
an active unresolved confrontation together**: pressure cannot leak into an
unrelated standoff, and coercion aimed at a state we are not confronting adds
nothing. This is the mechanism by which non-military coercion escalates a
standoff — sanctions and covert action wind a situation toward a shooting war
without anyone firing.

Pressure has two consumers:

- **`CheckPressureBoilover`**, run from `TickConfrontation` every month. At
  **pressure ≥ 70** with the escalation below Limited Conflict, the confrontation
  self-escalates one level via `SetEscalationBy(initiatorId)` and sheds 25. The
  step itself adds 12, so the net release is **−13** — the situation must be wound
  up again before it can climb further, and a boilover cannot run away inside one
  tick. It **never crosses beyond Limited Conflict**: a wider war is always a
  decision, never an accident. The player gets a PRIORITY "SITUATION
  DETERIORATES".
- **`BaseSettlementWillingness`**, which subtracts `escalationPressure × 0.15`.
  Nobody settles while the situation is still winding up, which is why a
  confrontation that looks quiet on paper can be impossible to close.

Tests: `PartialSystemsTests.Sanctions_WindUpAConfrontationTheyAreAimedAt`,
`Pressure_DoesNotLeakIntoUnrelatedConfrontations`,
`HighPressure_EventuallyEscalatesWithoutAnyoneChoosingTo`,
`Boilover_StopsShortOfWiderWar`, `Boilover_DoesNotRunAwayInASingleTick`,
`Pressure_MakesASituationHarderToClose`,
`AStandingCrisis_EventuallyBoilsOverOnItsOwn`, `ADefusedStandoff_DoesNotBoilOver`.

**Do not tune these constants against the validation harness.** The bots escalate
a crisis almost the month it opens — 215 reach Crisis and 214 go straight on to
Limited Conflict — so boilover fires zero times outside its own tests and the
dwell accrual has nothing to bite on. The behaviour this targets is a human one:
sitting on an open crisis because deciding is uncomfortable. Spec 12 §1.

### Primary Strategy and Strategic Pivot

`primaryStrategy` was stored, printed and read by nothing, so it was a label on a
war rather than a choice. `ConfrontationSystem.StrategicPressure` now adds
non-military coercion to settlement willingness, weighted **×1.0 for the domain
we committed to and ×0.35 for the others** — committing concentrates effort, and
concentrated effort is what breaks a government's resolve:

| Domain | Reads |
|---|---|
| Economic | sanction pressure ×9, negative growth ×2.2, confidence shortfall ×0.18 |
| Intelligence/Political | stability shortfall ×0.22, conspiracy ×0.12, approval shortfall ×0.10 |
| Diplomatic | each third party's hostility toward them ×0.035, +5 if they field no coalition |
| Military | their territory we hold ×0.10 |

This is what makes GDD §20's "economic victory coerces the opposing government"
reachable. Before it, willingness read only war exhaustion, momentum, war support
and the government pillar — none of which sanctions, covert action or isolation
touch — so four of the five Primary Strategies had no route to their objective.

**Strategic Pivot** (`Pivot`, GDD §18.2): 3 CP, −12 momentum, −5 war support.
Always available, never free; effort spent in the old domain does not transfer.

### Monthly tick

Every unresolved confrontation ticks, including AI-vs-AI. At Limited Conflict the
drain is 1.1/month (2.2 at Total War) applied to exhaustion, war support,
approval and treasury on both sides. Below Limited Conflict nothing drains and the
tick moves escalation pressure instead: **+3 at Crisis, −2 below it**.
`CheckPressureBoilover` runs last, after whichever branch was taken.

### Settlement (GDD §26)

No surrender button. Opponent willingness:

```
willingness = opponentExhaustion × 0.8
            + momentumAgainstThem × 0.6
            − theirWarSupport × 0.35
            − theirGovernmentPillar × 0.12
            − escalationPressure × 0.15               ← nobody settles mid-wind-up
            + StrategicPressure(...)                  ← see below
            + SkillEffect.SettlementLeverage          (player only)
            + 10 if our doctrine is Deterrence
            + 6  if our posture is Forward
            + CAP_VERIFICATION effectiveness × 12
            ────────────────────────────────── BaseSettlementWillingness
            + 30 if we already hold the objective
            − 12 for an ordinary territorial demand
            − 120 for a capital            ← effectively non-negotiable
accepted if willingness > 20
```

The **base** figure is what `PeaceSystem` prices terms against; the three lines
below the rule are the legacy single-objective path and must not be applied twice
(`SettlementWillingnessFor` deliberately exposes the base only). A separate
threshold of 35 on the base is what makes the opponent *signal* that they are
willing to talk.

`CAP_VERIFICATION` earns its place here: a rival can only accept terms it is able
to audit, and verification is what actually closes a settlement (GDD §11).

**A capital can never be won at the table** (GDD §22) — only by occupation.
Conceding is always available and costs war support and approval.

**The objective belongs to the initiator** (`ConfrontationSystem.Settle`). Terms
proposed by the demanding side deliver the demand; terms proposed by the side
demanded of are the status quo — the claim is withdrawn and nothing changes hands.
A territorial cession also requires that the ground is currently held by one of
the two parties; a location a third state has since taken is *renounced*, not
transferred. (Shipped behaviour handed the objective to whoever *proposed*, so a
defender suing for peace "ceded" its own ground under the initiator's name, and a
war with Russia over a location Brazil held transferred Brazil's title.)

**A foreign offer is a decision.** When an AI government proposes to the player,
`ProposeSettlementBy` raises a `TERMS_OFFERED` Crisis Turn (accept / refuse)
instead of closing the war on the player's computed willingness. Accepting a
*demand* is a concession and priced like one (approval −6, stability −2);
accepting a status-quo offer is free. A refusal — or an unanswered offer — waits
`OfferCooldownMonths = 6` before the same government asks again. AI-vs-AI
settlements are unchanged. The harness bots answer an offer by the player's own
`WouldAcceptTermsFrom` calculus — the pre-decision behaviour — so balance
measurements stay comparable.

**A verdict pays** (`RecordWarResult`, 2026-08). Until the playtest a verdict
changed a counter and nothing else, so the winner had paid every month of the war
and got no rally for it — across 888 measured decades the military playstyle,
the only one that gains ground, graded at or below doing nothing. The winner now
takes approval +8, unity +5, war support +12, stability +3, exhaustion −15 and
military pillar +2 (`Growth.Apply`); the loser approval −5, war support −8,
unity −3. One-off store writes, because a verdict is an event. The annual
evaluation credits wins and lost wars in the position component (spec 07).
Guarded by `BugRegressionTests.WinningAWar_RalliesTheCountryAndCountsInTheEvaluation`.

**A settlement binds.** `Close` sets `Relationship.settlementTruceMonths =
SettlementTruceMonths (12)`; `CanOpenAgainst` refuses a new confrontation between
the pair while it runs, and refuses a territorial demand for ground the defender
does not hold. `Begin` checks both before charging CP. Guarded by the five
`Settlement_*` / `Confrontation_*` cases in `BugRegressionTests`.

### Negotiated terms (`Core/PeaceSystem.cs`, GDD §26)

A settlement is assembled from terms rather than accepted wholesale. The willingness
figure above is what the opponent will *pay* to stop; each term has a price, and
the proposal is signed only when willingness ≥ total price.

| Demand | Costs them | | Concession | Pays them |
|---|---|---|---|---|
| Territorial cession | 30 + value×0.35 (−28 if we hold it, 200 for a capital) | | We withdraw | −24 − value×0.2 per held location |
| Reparations | ~28+ | | We lift sanctions | −26 |
| Demilitarization | 35 + their military×0.2 | | Prisoner exchange | −8 |
| Resource access | 18 | | We guarantee them | −18 − their exhaustion×0.15 |
| Recognition | 14 | | | |
| Treaty revision | 22 | | | |

This is the negotiation grammar: a hard demand becomes signable when paired with
something they actually want. **Asking for everything gets nothing** — there is a
test that confirms a six-demand proposal is refused by an opponent who would have
signed a two-term one.

Signed settlements are archived in `GameState.settlements` with their terms.
Their approval effects are recorded at the write site for causal explanation:
reparations, political concessions, prisoner exchange and the settlement
dividend remain distinct. An operator proposal carries `ProposeTerms`; accepting
an incoming package carries `ResolveCrisis`; actor-generic settlement calls do
not invent operator authorship.

**Foreign governments construct settlements too.** `AISystem` routes both its
ordinary war-weariness decision and its strategic-programme pre-emption through
`PeaceSystem.ProposeConstructedSettlementBy`. Against another AI it chooses the
best package the recipient will accept and signs through the same term applier
as the player. Against the player it puts its opening package into a
`TERMS_OFFERED` Crisis Turn; the exact list is shown, saved, and applied only if
the operator accepts. Refusal changes nothing and starts the ordinary six-month
offer cooldown. The legacy objective-only path remains for concessions and old
offer crises loaded without a package.

`BestAcceptableProposal` never signs a package after every demand has been
stripped. Concessions make a demand signable; they are not a settlement by
themselves. If no substantive term survives, the constructed attempt returns no
deal and the existing AI path may later concede explicitly. This prevents a
token prisoner exchange from closing an unresolved claim and resetting the
world's war cycle without settling anything.

A defender's suggested package asks for `Recognition` of the status quo rather
than `TerritorialCession`: the objective belongs to the initiator, and asking to
have ground already held by the defender ceded back to it would be an inert term.

## 5b. Entering somebody else's war (GDD §15.2, user decision 2026-08-27)

`ConfrontationSystem.BeginObligationBy(state, allyId, aggressorId, onBehalfOfId)`
is how honouring a defence commitment produces a front. It differs from `BeginBy`
in exactly three ways, and each is deliberate:

1. **It bypasses `CanOpenAnother`.** `MaxCommitment = 2.2` exists to stop a state
   *choosing* more war than it can fight. It has no business refusing a war
   somebody else started — a ceiling that could block an alliance call-in would
   make the game forbid the operator from keeping their word, and a refusal the
   operator cannot see reads as a broken control (spec 09 §12). The cost of a
   wider war stays real but **priced**: `TheatreSystem.FocusFactor` drags every
   operation by commitment elsewhere, so a state honouring three pacts at once
   fights badly on all three fronts. Same "priced, never gated" rule escalation
   (§18.1) and geography (§16) already follow.
2. **It bypasses the settlement truce** and clears both truce counters on the
   pair. A truce is a promise between two states about what *they* will start.
3. **It opens at `LimitedConflict`, not `Tension`.** Entering a war already being
   fought is not a period of tension.

It returns the existing confrontation unchanged if the pair are already at war —
the obligation is discharged by the war they are in — and it calls
`AllianceSystem.InvokeObligations` on the confrontation it opened, which is what
continues the cascade. See spec 04 §8.

`BelligerentRoster` (spec 09 §8) is the operator-facing side: once a cascade can
hand you belligerents you never declared against, "who am I fighting" stops being
answerable from the front selector alone.

## 5c. Satellite fronts, and the settlement fog restored (core stability repair, 2026-09)

**A front opened by honouring a guarantee is a satellite of the war it was
joined for.** `Confrontation.obligationRootId` names that war and
`obligationOnBehalfOfId` names the ally; both are empty on a war of the
initiator's own choosing and on every old save (correct, not merely blank —
those fronts were never satellites). Two things hang on it:

- **It closes with the war it was joined for.** `ConfrontationSystem.Close`
  finishes every unresolved satellite of a closing root war ("the war X joined
  in defence of Y has ended; the front closes with it"). Before this an
  obligation front carried `Deterrence` and no location, was never the war the
  AI managed (it managed the first in the list) and so had no exit at all — it
  drained exhaustion and treasury on both sides for the rest of the save.
  Measured: 21 of 22 AI wars in a world were these.
- **It tells a defensive call from an offensive one** — see spec 04 §8a.

`BeginObligationBy` no longer erases the settlement truce between the entering
ally and the aggressor (a sanctions truce still does not survive a shooting
war); the truce stops that pair *choosing* a new war with each other, which a
call-in is not.

**The AI seeks terms on every front** (`AISystem.SeekTermsIfWorn`), not only on
`ActiveConfrontationFor`, the first unresolved war in list order. Operations
still go to one front — that is what an army does; seeking terms on all of them
is what a foreign ministry does.

**The settlement oracle is closed again.** The player-facing surface reads only
the assessment layer:

| Surface | Was | Now |
|---|---|---|
| "THEY WOULD SIGN THIS TODAY" + ACCEPT | `PeaceSystem.BestAcceptableProposal` (walks `WouldAccept`) | `PeaceSystem.RecommendedProposal` (walks `Assess`); "OUR STAFF'S RECOMMENDATION — THEY WOULD LIKELY / MIGHT SIGN"; no recommendation without reporting |
| OPPONENT POSTURE: OPEN TO TERMS / RESISTING | `ConfrontationSystem.OpponentWouldAccept` (true bit) | `PeaceSystem.AssessDisposition` → `SettlementDisposition` (LIKELY RECEPTIVE … HIGHLY RESISTANT, five bands with High/Confirmed reporting, three with Moderate/Low, NO READ with none) |
| THEIR EXHAUSTION 41.3 | true float | `IntelReadout.ForeignExhaustion` — a bin on a fixed grid (5/10/20/25 wide by grade), never centred on the truth |
| After-action LOSSES … / ENEMY 7.3 | true per-operation figure | `IntelReadout.OperationLosses` — banded, and sided by `OperationRecord.attackerId` (an enemy assault on our position reads OWN for *our* losses) |
| ATTENTION "They would accept terms" | true bit | disposition ≥ POTENTIALLY RECEPTIVE |

`PeaceSystem.Assess` now keeps a dead-band at every grade (`DeadBandFor`:
Confirmed ±4, High ±10, Moderate ±25, Low ±35) — Confirmed/High used to answer
the exact sign of the margin, so a well-collected operator could walk the term
list to the precise acceptance boundary. Better reporting narrows the band of
doubt; it never removes it. `SettlementFogTests` scans every file under
`Scripts/UI` (plus `AttentionSystem`) for the oracle identifiers and fails the
build on a new one; `IntelReadout.cs` is the fog boundary and is exempt. The
"OPPONENT SIGNALS TERMS" notification stands: a government choosing to signal is
a public act — but its wording no longer promises what they would sign.

## 6. Joint exercises (GDD §15.3)

Requires a partner at `Cooperative` or better, not currently an opponent, and off
cooldown (**9 months per partner**). Cost 1/2/3 CP and 20/60/120 treasury by
scale.

| Scale | Depth | Interop gain | Exposure |
|---|---|---|---|
| Limited | 0.3 | 2.1 | 2.7 |
| Standard | 0.6 | 4.2 | 5.4 |
| Full | 1.0 | 7.0 | 9.0 |

- Readiness gain is `(3.5 if we outperformed else 5.0) × depth` — **losing teaches
  more**, and additionally repairs the weakest branch's supply.
- Exposure raises the partner's `doctrineFamiliarity` with us and creates or
  deepens *their* collection network against us.
- `doctrineFamiliarity` is permanent and aids operations against a former partner.
- `interoperability` multiplies that partner's coalition contribution
  (`× 1 + interop/130`).

## 7. Extension points

- **New location types** — add to `LocationType`, give it a `TypeCode`, and decide
  whether it should be exempt from AI claim targeting (capitals currently are).
- **New operation types** — append to `OperationType` (never insert; the enum is
  persisted by ordinal), then add an `OperationProfile` to `OperationCatalog` and
  a case to `ApplyNonCapturingSuccess`. **Two places, not six.** Cost, branch
  weights, intensity, civilian factor, defence model, targeting, ground-holding
  and availability are all data on the profile; attrition derives from the
  weights and the defence model, so it cannot go stale.
  `OperationCatalogTests` fails the build if a type has no profile, no
  description, no way to fight, or if a successful resolution leaves the world
  byte-identical.
  If the verb needs a genuinely new *kind* of defence, add a `DefenseModel` and
  give it a row in both `DefensePowerFor` and `ApplyDefenderAttrition` — those two
  must agree or losses land on a force that was never engaged.
- **New postures or doctrines** — add to the enum, then give it a row in
  `ReadinessTargetFor`, `PostureUpkeep` / the doctrine switch in
  `ResolveOperation`, and a settlement-willingness term if it should change how
  seriously the state's demands are taken. A posture or doctrine that touches only
  one of those is a label, which is exactly what `primaryStrategy` was before §5.
- **Enablers as multipliers on `EffectivePower`.** Two exist already but neither
  goes through `EffectivePower`: logistics acts on the supply ceiling and posture
  drag, and `CAP_ISR` acts on attack power at resolution time. **Air defense is
  the obvious missing one** — it has no representation at all, and it is the
  natural candidate for a defender-side multiplier. **PLANNED.**

## 8. Open questions / PLANNED

- **Transformative procurement has no UI.** `MilitaryView` hardcodes
  `ProgramScale.Major`, so the largest programme scale — and with it the whole
  point of the `ECO_STRATEGIC` skill — cannot be authorized in play. Needs a scale
  selector plus a `CanBeginProcurement(…, out reason)` so the gate can explain
  itself, matching `CanSetPosture`.
- **Multi-front confrontations.** One confrontation per country is a simplifying
  rule; a great-power game may eventually need concurrent theaters.
- **Strategic Destruction is implemented** — see `Core/EndgameSystem.cs` and
  **spec 14**, not this document. It is the military pillar's endgame (GDD §21):
  gated on the Integrated ISR capability at ≥ 0.6 maturity, military pillar ≥ 65,
  roughly eight funded authorizations of preparation, an active confrontation
  **at Total War**, and it is spent on use. Its consequences (target military
  −40, force −45, industry −30, manpower −300, stability −30, their war support
  *rises*, and every state in the world takes −35 trust and +40 threat of us,
  scaled by `Recidivism`) are documented there.

  What remains open is that it has **no limited form** — it is all-or-nothing,
  where GDD §21 implies a severity ladder within the instrument. **PLANNED.**
- **Sieges are currently a one-shot resolution** rather than a persisted state.
  GDD §19 wants sieges that can be maintained, bypassed, negotiated or abandoned
  over time.
- **Respect Conditions** (GDD §15.3) — per-country conditions that accelerate
  special relationships — are not implemented.

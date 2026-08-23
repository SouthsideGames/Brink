# 11 — Events & Crisis Specification

Source: `Core/CrisisSystem.cs`, `Core/EventCatalog.cs`, `Data/Crisis.cs`,
`Data/Notification.cs`. GDD §23, §6.

The hybrid architecture is complete: the simulation decides *whether* a situation
can arise and how likely it is, and authored content decides how good it is when
it does. What is **not** complete is the other half of the loop — a crisis
currently cannot change the world that produced it. See §6.

## 1. Crisis Turns

A crisis interrupts the monthly loop and **blocks End Month until answered**.
`TurnManager.EndMonth` returns `false` while `GameState.HasBlockingCrisis` is
true, logs `"End Month suspended"`, and the shell shows a modal overlay over the
whole terminal (spec 09) plus a persistent header indicator. `StartMonth`
re-emits a FLASH "CRISIS PENDING" if one is still open.

Crisis decisions use their own structure and **never consume Command Points** —
GDD §7.1 is explicit that the player must not be helpless because normal capacity
was already spent this month.

A Crisis Turn is one of only eight producers of FLASH traffic (spec 09). FLASH is
reserved for a decision that is actually on the desk this month, which is what
makes a crisis legible the moment it opens: if the briefing shows FLASH, something
is waiting on the operator. Anything the catalog adds that merely *reports* an
outcome belongs at PRIORITY.

Lifecycle:

1. `CrisisSystem.Trigger(state, defId)` — builds the crisis from its definition
   (situation text is generated *at trigger time* from live state), adds it to
   `state.activeCrises`, increments `crisesFacedThisYear`, records the cooldown,
   emits FLASH traffic and writes a Political chronicle entry.
2. The overlay presents the options; END MONTH refuses.
3. `CrisisSystem.Resolve(state, crisis, optionIndex)` — applies the deltas,
   removes the crisis, increments `crisesResolvedThisYear`, calls
   `ProgressionSystem.RecordInitiative`, awards **20 XP**, emits PRIORITY traffic
   and a chronicle entry.
4. `GameController.ResolveCrisis` autosaves immediately afterward (GDD §30:
   consequences are meant to stick, so the checkpoint is taken after the choice,
   not before).

Active crises persist in the save, so a loaded game still blocks End Month.

### Contingency planning softens the blow

`Resolve` scales **negative deltas only**:

```
resilience = 1 − TechnologySystem.Effectiveness(player, "CAP_CONTINUITY") × 0.35
delta < 0 → delta × resilience
```

A matured continuity capability removes up to 35% of what a crisis costs
(GDD §11). It never inflates a gain — preparedness limits damage, it does not
make disasters profitable.

Social values (`stability`, `governmentApproval`, `nationalUnity`) are clamped to
`0..100`; treasury is not.

### The one crisis that is not in the catalog

`ALLIANCE_OBLIGATION` (`AllianceSystem.PlayerObligationCrisisId`) is constructed
directly by `AllianceSystem.RaisePlayerObligation` rather than drawn from the
catalog, because it is raised by a specific event in a specific war rather than
selected by likelihood. `CrisisSystem.Resolve` special-cases it: option 0 honors
and option 1 repudiates, routed to `AllianceSystem.ApplyPlayerDecision` **before**
the ordinary deltas are applied. Its own deltas are small (honor: approval −4,
stability −2; repudiate: approval +2) because the real consequences are
relational and live in spec 04 §5a.

This is the only crisis today whose choice reaches outside the four player
scalars, and it does so by bypassing the option structure entirely.

## 2. The event definition

```csharp
class EventDefinition
{
    string id, title;
    Func<GameState, string> body;                  // may name real countries/officials
    Func<GameState, bool>  isEligible;             // systemic half
    Func<GameState, float> weight;                 // relative likelihood when eligible
    int cooldownMonths = 24;                       // per save, per definition
    Func<GameState, List<CrisisOption>> options;   // authored half
}
```

Both `body` and `options` are functions of state, so situation text reads live
values: `CABINET_DISSENT` names the actual disaffected official and their title,
`BORDER_INCIDENT` names the actual coldest rival, `INFLATION_PROTESTS` quotes the
actual inflation figure, `CHOKEPOINT_INCIDENT` names the actual chokepoint.

## 3. Selection (`CrisisSystem.SystemicCheck`)

Subscribed to `TurnManager.ResolveMonth` — and registered **last**, after the AI,
progression and confrontation ticks, so a crisis raised this month blocks the
*next* End Month rather than the one being resolved.

```
if state.HasBlockingCrisis            → return        (never stack crises)

monthIndex = date.MonthsSince(startDate)
rng = new Random(rngSeed × 486187739 + monthIndex)    ← deterministic per save
if rng.NextDouble() >= 0.08           → return        (MonthlyCrisisChance)

for each definition in EventCatalog.Definitions:
    skip if monthIndex − lastFiredMonth < cooldownMonths
    skip if isEligible(state) is false
    skip if isEligible throws (logged as a CRISIS error, never fatal)
    w = max(0.05, weight(state))
    accumulate

if nothing eligible                   → return        (a genuinely quiet month)

roll = rng.NextDouble() × totalWeight
walk the eligible list subtracting weights; trigger the first to cross zero
(the last entry is the fallback if float error exhausts the walk)
```

Two independent routes produce a quiet month, and both are intentional:

- **The 8% roll.** Chosen to keep long unattended simulations moving rather than
  from play feel. It is a per-month gate, so the expected rate is under one
  crisis per year even with a fully eligible catalog.
- **An empty eligible set.** A stable, well-supplied state at peace with nobody
  compromised, no disaffected minister and no disrupted trade partner produces
  *no* eligible definition, and no crisis fires. This is correct rather than a
  gap to paper over — quiet is what competent government buys, and the
  multi-seed balance harness explicitly checks that preventing crises is not
  scored worse than having them.

Exactly one RNG stream is drawn per month regardless of outcome (`NextDouble` for
the gate, then at most one more for the weighted draw), so the sequence is stable
across saves and reloads.

Cooldowns persist in `GameState.eventCooldowns` as `{defId, lastFiredMonth}`,
written by `RecordFired` inside `Trigger` — including for a forced crisis from
the debug shell, so FORCE CRISIS cannot be used to bypass a rest period.

## 4. The catalog (15 definitions)

`CrisisSystem.CatalogIds` enumerates them for the debug shell's FORCE CRISIS
control. Weight is a bare number where it is constant and a formula where it
scales with the condition that produced it.

### 4.1 Domestic pressure

| Id / title | Eligible when | Weight | Cooldown |
|---|---|---|---|
| `BORDER_INCIDENT` — BORDER INCIDENT | Any non-player state is at alert posture, **or** the coldest rival's `relations` < 40 | `2.5` if anyone is at alert, else `1.0` | 18 |
| `MARKET_PANIC` — MARKET PANIC | `economy.confidence` < 58 **or** `debtToGdp` > 85 | `2.5` if confidence < 45, else `1.2` | 20 |
| `FOOD_SHORTAGE` — REGIONAL FOOD SHORTAGE | `resources.foodSecurity` < 65 | `(70 − foodSecurity) / 20` | 24 |
| `INFLATION_PROTESTS` — PROTESTS OVER LIVING COSTS | `inflation` > 7 **and** `governmentApproval` < 60 | `inflation / 5` | 15 |
| `ENERGY_CRISIS` — ENERGY SUPPLY CRISIS | `resources.energy` < 50 | `(55 − energy) / 15` | 24 |
| `INDUSTRIAL_ACCIDENT` — MAJOR INDUSTRIAL ACCIDENT | `industrialCapacity` > 50 **and** the Industry sector's `health` < 82 | `0.9` | 30 |
| `CABINET_DISSENT` — CABINET DISSENT | The least-trusting official's `trust` < 42 | `1.4` | 18 |
| `WAR_WEARINESS` — WAR WEARINESS | `warExhaustion` > 40 **and** actually at war | `warExhaustion / 25` | 12 |

`INDUSTRIAL_ACCIDENT` deliberately requires *both* a large industrial base and a
degraded one. Accidents follow strain and neglect; a well-maintained sector is
not a crisis waiting to happen, so merely being industrialized must not qualify.

`WAR_WEARINESS` carries the shortest cooldown in the catalog (12 months) because
a long war should be able to ask the question more than once.

**Options and deltas:**

| Id | Option | treasury | stability | approval | unity |
|---|---|---:|---:|---:|---:|
| `BORDER_INCIDENT` | MOBILIZE RESPONSE FORCE | −60 | +2 | +3 | — |
| | FILE DIPLOMATIC PROTEST | — | — | −2 | — |
| | SUPPRESS THE REPORT | — | −3 | −4 | −2 |
| `MARKET_PANIC` | EMERGENCY LIQUIDITY INJECTION | −120 | — | +2 | — |
| | LET MARKETS CORRECT | — | −2 | −4 | — |
| `FOOD_SHORTAGE` | EMERGENCY IMPORTS | −80 | — | +1 | — |
| | REQUISITION RESERVES | — | +1 | −1 | — |
| | LOCAL AUTHORITIES HANDLE IT | — | −2 | −3 | −1 |
| `INFLATION_PROTESTS` | SUBSIDIZE ESSENTIALS | −100 | +2 | +5 | — |
| | MEET THE ORGANIZERS | — | −1 | +2 | +2 |
| | ENFORCE PUBLIC ORDER | — | +3 | −6 | −4 |
| `ENERGY_CRISIS` | PURCHASE AT MARKET PRICE | −140 | — | +2 | — |
| | RATION INDUSTRIAL SUPPLY | — | −1 | −2 | — |
| | APPROACH A SUPPLIER STATE | −40 | — | +1 | — |
| `INDUSTRIAL_ACCIDENT` | FULL PUBLIC INQUIRY | −70 | — | +2 | +2 |
| | RESTART UNDER REVIEW | — | — | −3 | −2 |
| | BLAME THE OPERATOR | — | −2 | −1 | — |
| `CABINET_DISSENT` | BRING THEM BACK IN | — | +1 | −1 | +2 |
| | ISOLATE THEM | — | −2 | — | −2 |
| | PUBLIC REBUKE | — | +1 | +2 | −3 |
| `WAR_WEARINESS` | ADDRESS THE NATION | — | — | +4 | +3 |
| | EXPAND VETERANS' SUPPORT | −110 | — | +3 | +4 |
| | RESTRICT COVERAGE | — | +2 | −5 | −4 |

The recurring shape is deliberate: the expensive option buys approval, the free
option costs it, and the coercive option buys stability at the price of unity.
`ENFORCE PUBLIC ORDER` and `RESTRICT COVERAGE` are the clearest cases — order is
purchasable, consent is not.

### 4.2 Events driven by the rest of the world

These read foreign state, so they cannot fire in a world where nothing else is
happening.

| Id / title | Eligible when | Weight | Cooldown |
|---|---|---|---|
| `INTELLIGENCE_SCANDAL` — SERVICE EXPOSED ABROAD | Any network **we own** is `compromised` | `1.8` | 20 |
| `SUPPLY_DISRUPTION` — SUPPLY CHAIN DISRUPTION | Any trade link involving us is `embargoed`, or its partner is at war | `1.6` | 18 |
| `DEFECTION` — DEFECTION | Any foreign network targets **us** and is not compromised | `1.3` | 30 |
| `CHOKEPOINT_INCIDENT` — CHOKEPOINT INCIDENT | A `Chokepoint` we do not own is held by a state whose `relations` with us are < 55 | `1.5` | 24 |
| `FOREIGN_COUP_FALLOUT` — GOVERNMENT FALLS ABROAD | Any foreign state has `coupsExperienced` > 0 **and** its leader's `monthsInOffice` < 18 | `2.0` | 18 |
| `TREATY_PRESSURE` — PARTNER SEEKS ASSURANCES | We hold at least one unbroken treaty | `1.2` | 20 |
| `REFUGEE_PRESSURE` — DISPLACEMENT AT THE BORDER | Any foreign state has `stability` < 35, is `inCivilConflict`, or is at war | `1.4` | 24 |

`CHOKEPOINT_INCIDENT` requires a *cold* holder, not merely a foreign one. A
chokepoint only becomes a problem when the state holding it has some reason to
squeeze us; the Bosphorus and the Malacca Approaches existing is not a crisis.

`DEFECTION` is the mirror of `INTELLIGENCE_SCANDAL` — it fires because someone
else's service is operating against us, which is the same fact that makes
counterintelligence worth funding (spec 03).

`FOREIGN_COUP_FALLOUT` is the only event whose eligibility is produced entirely
by `RegimeSystem` (spec 05 §7a); it is the seam where regime change abroad
becomes a decision at home.

**Options and deltas:**

| Id | Option | treasury | stability | approval | unity |
|---|---|---:|---:|---:|---:|
| `INTELLIGENCE_SCANDAL` | ACCEPT RESPONSIBILITY | — | +1 | −5 | — |
| | DISAVOW THE OPERATION | — | — | −1 | −3 |
| | ORDER AN INQUIRY | — | −1 | −2 | — |
| `SUPPLY_DISRUPTION` | SUBSIDIZE ALTERNATIVE SOURCING | −90 | +1 | — | — |
| | DRAW DOWN STRATEGIC STOCKS | — | — | +1 | — |
| | LET INDUSTRY ABSORB IT | — | — | −4 | −2 |
| `DEFECTION` | ACCEPT AND EXPLOIT | — | −1 | +1 | — |
| | ACCEPT QUIETLY | — | — | — | — |
| | REFUSE | — | — | −1 | — |
| `CHOKEPOINT_INCIDENT` | SEND AN ESCORT | −110 | +1 | +3 | — |
| | NEGOTIATE TRANSIT TERMS | −60 | — | −1 | — |
| | ACCEPT THE DELAYS | — | — | −3 | −1 |
| `FOREIGN_COUP_FALLOUT` | RECOGNIZE IMMEDIATELY | — | +1 | −2 | — |
| | WITHHOLD RECOGNITION | — | — | +3 | +2 |
| | QUIETLY OPEN A CHANNEL | — | — | — | — |
| `TREATY_PRESSURE` | REAFFIRM PUBLICLY | — | +1 | −1 | — |
| | REASSURE PRIVATELY | — | — | — | — |
| | DECLINE TO ELABORATE | — | — | +1 | −1 |
| `REFUGEE_PRESSURE` | RECEIVE AND PROCESS | −120 | −1 | — | −3 |
| | FUND REGIONAL CONTAINMENT | −90 | — | — | — |
| | CLOSE THE FRONTIER | — | — | +3 | −4 |

Three options in this group (`ACCEPT QUIETLY`, `QUIETLY OPEN A CHANNEL`,
`REASSURE PRIVATELY`) carry **no deltas at all**. That is the point of them in the
fiction — the discreet answer costs nothing and gains nothing. It is also the
clearest illustration of the limitation in §6: the quiet channel *should* buy a
relationship and today it buys literally zero.

## 5. Non-crisis events — PLANNED

Not every event should be a modal interrupt. The same architecture could emit a
PRIORITY or ADVISORY briefing item with no decision attached, which is how the
world gets texture without constant interruption. `EventDefinition` has no
`priority` or `blocksEndMonth` field today; every catalog entry is a full Crisis
Turn. Budget interrupts carefully when this lands: the Crisis Turn has weight
*because* it is rare, and a decisionless event must never be filed at FLASH — the
class means "answer this", not "this is important".

## 6. Crises feed back into the world (`Core/CrisisEffects.cs`)

A `CrisisOption` used to resolve to exactly four player scalars —
`treasuryDelta`, `stabilityDelta`, `approvalDelta`, `unityDelta` — applied to the
player's own country and nothing else. A crisis could not change a relationship,
move a market, touch a foreign state, or open a war. The situations arose
systemically and resolved cosmetically.

An option now also carries `effectId`, `effectTargetId` and `effectMagnitude`,
applied by `CrisisEffects.Apply` after the scalars.

### Why a named string and not a delegate

`ActiveCrisis` and its options are **persisted** (spec 10). An option carrying an
`Action<GameState>` would work perfectly right up until the operator saved with a
crisis open, at which point the choice would silently lose its consequence. A
string survives the round trip, is inspectable in the save file, and is checkable
by a test. `CrisisEffectTests.ACrisisSurvivesBeingSavedAndReloaded` covers it.

### The effects

| Id | Acts on |
|---|---|
| `RELATIONS` / `TRUST` / `THREAT` | The relationship with the target |
| `OPEN_CONFRONTATION` | Begins a confrontation **we** initiate |
| `SUFFER_CONFRONTATION` | Begins one **they** initiate against us |
| `READINESS` / `WAR_SUPPORT` | Our own force and public will |
| `IMPOSE_SANCTION` / `SUFFER_SANCTION` | Real sanctions, severity from magnitude |
| `TRADE_SHOCK` | Volume on links with the target, or all of ours if none named |
| `MARKET_SHOCK` | Economic confidence and the market index |
| `EXPOSE_NETWORK` | Burns one of our networks, **publicly** — which teaches the world we run them (spec 06 §7b) |
| `FOREIGN_UNREST` | A foreign state's stability and conspiracy level |
| `CONSPIRACY` | Plotting against our own government |

Three rules the implementation is held to, each with a test:

1. **The switch is exhaustive by test.** `CrisisEffectTests.EveryEffectTheCatalogNamesIsOneWeCanApply`
   walks every option and every lapse in the authored catalog and fails the build
   on an id `CrisisEffects` does not handle. A typo would otherwise produce a
   choice that resolves, prints its result text, and does nothing — which is the
   exact failure this system was built to fix, one layer down.
2. **A contradiction is quiet, never a crash.** The world moves while a crisis
   sits open: the target may be gone, the war may already have started, the link
   may have been severed. Every effect no-ops rather than throwing, and
   `OPEN_CONFRONTATION` returns empty rather than reporting a war it did not
   start.
3. **The operator is told.** `Apply` returns a line describing what happened
   abroad, which `Resolve` appends to the notification and the chronicle. A
   consequence the player discovers three months later reads as the simulation
   cheating.

**Effects are deliberately not softened by `CAP_CONTINUITY`.** Contingency
planning cushions what a shock costs us at home; it does not make another
government think better of us or call off a war.

### The subject is captured when the crisis fires

`EventDefinition.subject` resolves once, in `CrisisSystem.Create`, and the options
close over the same value. A crisis that opens by naming our coldest rival must
resolve against *that* state even if the rankings shift while the operator is
deliberating. `ActiveCrisis.subjectCountryId` records it.

### Drifting has consequences too

`EventDefinition.lapseEffectId` / `lapseTargetUsesSubject` / `lapseMagnitude`
give a crisis something the world does when nobody decides. Ignoring a crisis used
to cost standing and nothing else, which made drifting the **cheapest way to dodge
a decision's consequences** — precisely backwards. `BORDER_INCIDENT` and
`CHOKEPOINT_INCIDENT` both lapse into `SUFFER_CONFRONTATION`: a state that can
close a strait to us and meet no answer draws the obvious conclusion.

`AllianceSystem` still reaches around the option structure via a `defId` check in
`Resolve`, because honouring or repudiating a defence pact touches coalitions and
reputation in ways a single effect id would not capture. That special case is
expected to stay.

## 7. Extension points

- **Adding an effect** — add the constant to `CrisisEffects`, list it in `All`,
  and give it a case in `Apply`. The exhaustiveness test then covers it
  automatically, and `EveryEffectActuallyChangesTheWorld` will fail the build if
  it turns out to be decoration. Effects must tolerate a missing target.
- **Crisis chains** — an unresolved or badly-handled crisis seeding a follow-up
  months later is the cheapest way to make the world feel causal. Now unblocked:
  §6 gives a lapse somewhere to record itself, and the chronicle carries the
  outcome. A `followsFrom` field on `EventDefinition` is the missing piece.
- **Official competence gating** (GDD §28.1) — a low-competence official should
  sometimes fail to surface an item at all, making delegation part of the
  information experience. Nothing implements this yet.
- **Crisis response quality** as a skill effect — the `SkillEffect` enum has room;
  reducing an option's downside would give the Government tree more to offer, and
  `CAP_CONTINUITY` already proves the softening mechanism works.
- **Foreign crises** — crises only ever fire for the player. `SystemicCheck` reads
  `state.PlayerCountry` throughout and every eligibility helper is written from
  the player's viewpoint. AI states facing their own crises would create
  observable instability the player could exploit, and would make the
  world-reading events in §4.2 fire from real causes far more often.
- **Mutual exclusion** — there is no `excludes` field; two thematically
  overlapping definitions can fire back to back, held apart only by their own
  cooldowns.

## 8. Open questions

- The 8%/month rate was chosen to keep long unattended simulations moving, not
  from play feel. With 15 definitions the eligible set is rarely empty for a
  stressed state, so the roll is now the dominant limiter. Tune against
  `Report_MultiSeedBalance`, not a single seed — a decade should feel eventful
  without being exhausting, roughly 8–12 crises per decade.
- Cooldowns range 12–30 months with no principle behind the spread beyond
  "rarer situations rest longer". Once §6 lands and crises have real
  consequences, cooldown is the main dial for how much a bad decade compounds.
- The catalog skews domestic: eight of fifteen definitions read only the player's
  own numbers. Diplomacy and intelligence are under-represented as *sources* of
  crises relative to how much simulation stands behind them.

# 15 — Cabinet & Reporting Specification

Source: `Core/CabinetSystem.cs`, `Core/ReportingSystem.cs`, `Data/Official.cs`,
`Data/CountryState.cs`, `Data/Notification.cs`, `Data/WorldFactory.cs`
(`MakeCabinets` / `MakeCabinetFor` / `AppointCabinet`), `UI/Views/CabinetView.cs`,
`UI/Views/IntelligenceView.cs`. GDD §7.2, §7.3, §8, §28.1, §34.1.

Covered by `Tests/EditMode/CabinetSystemTests.cs` (11 tests),
`Tests/EditMode/ReportingSystemTests.cs` (15 tests),
`Tests/EditMode/ForeignCabinetTests.cs` (16 tests) and three directive tests in
`Tests/EditMode/PartialSystemsTests.cs`.

Two systems share this document because they share a single number. The Cabinet
decides how well a country executes; reporting decides how much of the world the
operator is allowed to see. Both read `Official.competence`, and until reporting
existed that field was consulted at exactly one gameplay site.

Sections 1–8 are the Cabinet. Sections 9–18 are reporting. The asymmetry between
them is deliberate and worth stating up front: **every government has a Cabinet;
only the player's government has an operator standing outside it.** The officials
are universal, the control modes and the reporting filter are not.

## 1. Five officials, one per pillar, in every government (GDD §8)

`CountryState.cabinet` holds exactly five `Official` records, one per `Pillar`.
Every country in the roster has one — all sixteen, not the player's alone.

Foreign states used to have none. Their capability grew from
`AISystem.InvestInPillars`, a bespoke routine with no people behind it, so a
rival's progress had **no explanation, nothing to collect against, and nothing a
coup could damage**. The same five offices now run every state, which makes a
rival's economy something that is run well or badly by a named person, makes that
fact a legitimate intelligence target (§17a), and gives `RegimeSystem` something
real to wreck.

Titles come from **that country's** profile `officeTitles` array, indexed by
pillar order, so a monarchy's defence office is not named like a republic's and a
foreign minister does not carry the player nation's job title. Names are drawn
from the same country's `firstNames` / `lastNames` pools. Test:
`ForeignCabinetTests.AForeignCabinetBelongsToItsOwnNation`.

### 1a. `GameState.cabinet` is a view, not a second list

```csharp
public List<Official> cabinet => PlayerCountry?.cabinet ?? EmptyCabinet;
```

A **property**, deliberately. `JsonUtility` serializes fields and not properties,
so the player's officials are persisted **exactly once**, on the country that owns
them (spec 10 §2). Keeping a second copy on `GameState` would be one concept with
two homes, which is how the two drift apart across a save/load.

Making it a property rather than deleting it is what let the move happen without
touching the UI views, `ReportingSystem` or the test suite: every reader of
`state.cabinet` kept working unchanged and now reads through to the country. Test:
`ForeignCabinetTests.ThePlayerCabinetIsTheSameListAsTheirCountrys` asserts the two
are the *same object*, not equal contents.

`GameState.legacyCabinet` is the field the player's Cabinet used to occupy. It
exists only so a pre-v2 save still deserializes into somewhere; `SaveMigration`
empties it onto the player country on load and nothing ever writes to it again
(spec 10 §4).

### 1b. Official ids are country-qualified

```
id = $"OFF_{country.id}_{office.ToString().ToUpperInvariant()}"
```

The old scheme keyed on the pillar alone. With one cabinet in the world that was
unique; with sixteen it **collided on every single id**. Test:
`ForeignCabinetTests.OfficialIdsAreUniqueAcrossTheWorld`.

| Field | Range | What it actually drives |
|---|---|---|
| `competence` | 0..100 | Autonomous output (§7) **and** reporting quality (§11) |
| `loyalty` | 0..100 | Softens trust damage when the operator overrides or sidelines (§8) |
| `riskTolerance` | 0..100 | Monthly variance and the significant-outcome rate (§7) |
| `trust` | 0..100 | Relationship state, not currency (GDD §7.3). Feeds reporting below 40 |
| `monthsInOffice` | int | Display only |
| `mode` | enum | §3 |
| `directiveId` | string | §5; empty unless `Directed` |

`loyalty` and `riskTolerance` are read nowhere except `CabinetSystem`.
`competence` is read in `CabinetSystem.MonthlyAct` and
`ReportingSystem.MishandleChanceFor`, and nowhere else.

## 2. Seeding and turnover

`WorldFactory.MakeCabinets` walks every country at world creation and calls
`MakeCabinetFor`, which rolls each office from **that country's** name pools,
rejecting duplicate names within the cabinet, and seeds:

| Stat | Range at world creation |
|---|---|
| `competence` | **40–78** |
| `loyalty` | 35–80 |
| `riskTolerance` | 20–80 |
| `trust` | 50–70 |
| `mode` | `Autonomous` |

The 40–78 band is load-bearing for §11 and must not be widened without
recalibrating `ReliableCompetence`.

`MakeCabinetFor` returns silently for a country with no authored profile, so a
hand-built country in a test harness is cabinet-less rather than crashing.
`WorldFactory.AppointCabinet(rng, country)` is the same routine exposed publicly,
and exists for exactly one caller: the v1→v2 migration filling in the foreign
cabinets an older save does not have (spec 10 §4).

### Turnover reaches every cabinet

Turnover is not run by this system, and both of its drivers are now
**actor-generic**:

| Event | Routine | Scope |
|---|---|---|
| Administration change | `GovernmentSystem.ReshuffleCabinetOf(state, country, rng)` | every government |
| Successful coup | `RegimeSystem.ReshuffleCabinetAfterCoup(state, country, rng)` | every government |
| `DismissOfficial` (4 PC) | `GovernmentSystem` | the player's, one office |

A reshuffle replaces each office on a **coin flip**, with competence 40–78,
loyalty 35–80, risk 20–80, trust 50–62, reverting to `Autonomous` with the
directive cleared (spec 05 §7). A coup replaces the cabinet **wholesale** at
competence 35–75, loyalty **55–95** (the new order's own people), risk 30–90,
trust 45–60. `DismissOfficial` rerolls one office in place at competence 42–82,
loyalty 45–85, trust 55–65 — a hand-picked appointee is more loyal and starts
with more of the operator's confidence, but is barely more competent. All three
draw the replacement's name from the country's own profile pools.

Making these generic is not tidiness. Without it a foreign minister appointed at
world creation would still be in office fifty years and six elections later,
which reads as a frozen world; and a coup — the single most visible thing that can
happen to a state's capability — would leave the deposed government's ministers
running the country for the junta. Tests:
`ForeignCabinetTests.AForeignCoupReplacesThatCountrysCabinet`.

Nothing else changes an official's stats, ever: they do not learn, age or
decay (§19).

## 3. Control modes are the operator's, and only the operator's (GDD §7.2)

The five offices are universal; the three control modes are not. A foreign
cabinet is **always `Autonomous`**, because there is nobody standing outside it to
direct or override anyone — the modes are the player's interface to their own
government, not a property of cabinets in general. `MonthlyActFor` never writes
`mode` at all, and `SetMode` **rejects any official who is not the player
country's holder of that office**:

```csharp
if (state.PlayerCountry?.FindOfficial(official.office) != official)
{
    GameLog.Warn("CABINET", "That official does not answer to this government.");
    return false;
}
```

Identity, not `isPlayer`. Comparing the object against the one the player country
holds for that office is the check that actually means "this is mine" — an
`isPlayer` test on a country the caller supplied separately would be checking a
different thing than the one being commanded.

The guard runs **after** the already-in-that-mode early return and **before** the
authority check and the Influence charge, so a rejected foreign official costs
nothing and no constitutional machinery is invoked on another country's behalf.

**No caller currently hands it a foreign official** — CABINET iterates
`state.cabinet`, the player's five (§1a). The check exists anyway because with
fifteen other cabinets in the world "no caller does that" is a weaker guarantee
than a check, and **commanding another country's ministers would be the most
fundamental fog break available**: not a leaked estimate but direct control of a
sovereign government's institutions. That is the class of bug worth paying two
lines and a branch to make unreachable rather than merely unreached. Tests:
`ForeignCabinetTests.WeCannotCommandAnotherCountrysMinisters` and
`ForeignCabinetsAreAlwaysAutonomous`, which runs 24 months and asserts the
outcome, so a future caller that found a route would fail the suite.

The same line is why the AI has no Influence, and why it is **not going to get
one** (settled by decision; spec 06 §9): Influence exists to buy a directive from
an official who would otherwise choose for themselves. There is no foreign
directive to buy, because there is no foreign operator to buy it.

| Mode | Cost to enter | Cost per action | Official acts monthly | Reporting filter |
|---|---|---|---|---|
| `Autonomous` | free | 0 CP | yes | full (§11) |
| `Directed` | **1 Influence** | 0 CP | yes, toward a directive | halved |
| `DirectControl` | 3 PC **if the constitution requires approval** | CP per action (§6) | **no** | none |

`CabinetSystem.SetMode` rules, in order:

1. Setting the mode an official is already in returns `true` and does nothing.
2. **The official must be ours** — the player country's holder of that office, by
   identity. Otherwise `false`, with nothing charged (above).
3. Entering `DirectControl` calls `AuthoritySystem.ObtainAuthority` for that
   pillar and **refuses the mode change if it fails** (spec 05 §1a). Taking
   personal command of a pillar is a constitutional act; directing and advising
   are always ours. This is the only call site of the authority system.
4. Entering `Directed` costs 1 Influence and fails when `state.influence < 1`,
   leaving the official where they were. It also installs
   `GetDirectives(office)[0].id` as the opening directive, so a Directed official
   is never idle.
5. Any other mode clears `directiveId`.
6. `RecordInitiative` fires **only when Influence was actually spent**.

Rule 6 is a patched exploit, not a stylistic choice. Crediting a free mode change
let a player toggle Autonomous → Autonomous-adjacent modes back and forth and bank
the whole `initiative` component of the annual evaluation — 0.20 of the grade —
without committing a resource (spec 07 §3).

`SetDirective` changes the active directive of an already-Directed official for
1 Influence, and returns `false` (charging nothing) for a non-Directed official or
for the directive already in force. Test:
`CabinetSystemTests.SetDirective_CostsInfluence_NoChargeForSameDirective`.

## 4. The Influence economy (GDD §7.3)

```
GameState.InfluenceCap      = 6
GameState.InfluencePerMonth = 3
```

Accrued in `TurnManager.EndMonth`, after the month resolves:

```
gain      = InfluencePerMonth + SkillEffect.DelegationBandwidth
influence = min(InfluenceCap + gain − InfluencePerMonth, influence + gain)
```

The cap term is written that way so the `DelegationBandwidth` skill raises the
ceiling and the income by the same amount — a wider bandwidth buys a bigger
reserve, not just a faster refill. With no skill the effective cap is 6 and the
income 3, so **an operator who spends nothing banks two months of delegation and
no more**, and an operator who redirects the whole Cabinet in one month is out of
Influence for two.

Six is deliberately small relative to five offices: the resource exists to make
delegation a choice about *which* pillar gets a stated priority this quarter, not
a currency that accumulates into permanent control of all five.

Test: `CabinetSystemTests.Influence_RegeneratesMonthlyWithCap`.

## 5. The directive catalog

Two directives per pillar, ten in total, returned by `GetDirectives(pillar)`. The
first in each pair is the default installed on entering `Directed`.

| Pillar | Id | Label |
|---|---|---|
| Military | `MIL_READINESS` | RAISE READINESS |
| | `MIL_CONSERVE` | CONSERVE BUDGET |
| Economy | `ECO_GROWTH` | PURSUE GROWTH |
| | `ECO_AUSTERITY` | AUSTERITY |
| Intelligence | `INT_COLLECTION` | EXPAND COLLECTION |
| | `INT_COUNTERINTEL` | COUNTERINTELLIGENCE |
| Diplomacy | `DIP_OUTREACH` | BROAD OUTREACH |
| | `DIP_PRESSURE` | PRESSURE RIVALS |
| Government | `GOV_APPROVAL` | PUBLIC APPROVAL |
| | `GOV_STABILITY` | INTERNAL STABILITY |

### What a directive changes

`ApplyPillarEffect(player, pillar, directiveId, amount)` is the single mapping
from an official's or the operator's effort to national effect. An empty
`directiveId` is the Autonomous default. Every pillar gain routes through
`Growth.Apply`, so it tapers to nothing over the last 45 points of the scale
(spec 12); treasury, approval and stability do not.

| Pillar | Directive | Effect for effort `a` |
|---|---|---|
| Military | *(default)* | military **+a** |
| | `MIL_READINESS` | military +0.6a, treasury **−10a**, plus a readiness target shift (below) |
| | `MIL_CONSERVE` | military +0.4a, treasury **+8a** |
| Economy | *(default)* | economy +0.7a, treasury +10a |
| | `ECO_GROWTH` | economy **+a**, treasury +6a |
| | `ECO_AUSTERITY` | economy +0.3a, treasury **+16a** |
| Intelligence | *(default)* | intelligence +0.8a |
| | `INT_COLLECTION` | intelligence **+a** |
| | `INT_COUNTERINTEL` | intelligence +0.6a, stability +0.3a |
| Diplomacy | *(default)* | diplomacy +0.8a |
| | `DIP_OUTREACH` | diplomacy **+a** |
| | `DIP_PRESSURE` | diplomacy +0.5a, military +0.2a |
| Government | *(default)* | approval +0.5a, stability +0.5a, government +0.3a |
| | `GOV_APPROVAL` | approval **+a**, government +0.3a |
| | `GOV_STABILITY` | stability **+a**, government +0.3a |

The shape is mostly consistent: the "focus" directive concentrates the whole
effort on one number, and the "restraint" directive converts most of it into
treasury or into a second-order value. Stating a priority is worth roughly a 25%
uplift on the thing named, paid for by everything else.

`MIL_READINESS` is the exception, and the only directive that buys something
outside `ApplyPillarEffect`. "Prioritize force readiness over budget" trades
pillar growth (0.6a rather than a) and **real money out** (−10a, the only
treasury debit in the catalog) for a force actually held at a higher state.

### A directive that moves a drifting stat must move its target

`ApplyPillarEffect` deliberately does **not** touch `force.readiness`. Readiness
drifts toward a posture target at 4/month in `MilitarySystem.MonthlyUpkeep`, so
anything added to the value directly is erased before the player can feel it. The
readiness half of the directive is therefore a **target shift**:

```
MilitarySystem.ReadinessDirectiveBonus(state, country) = 8f
    when country.isPlayer
     and the Military official is Directed with MIL_READINESS
     otherwise 0
```

folded into the branch readiness target in `MonthlyUpkeep` alongside
`ProjectionSwing` and `garrisonDrag` (spec 01 §2e). Because it moves the target,
the +8 persists for exactly as long as the directive is in force and unwinds by
itself when it is lifted — which is the behaviour the label promises.

**This is the second time that trap has produced dead code here.** Occupation's
readiness cost was a flat monthly subtraction in `TerritorySystem.MonthlyUpdate`
that ran *before* the upkeep drift and was silently annulled every month;
occupation looked expensive in the source and was free in play (spec 01 §2e).
The rule generalises: **a directive, penalty or bonus that wants to move a stat
which drifts toward a target has to move the target, not the value.** Check the
tick order before writing the simpler version.

`ReadinessDirectiveBonus` returns 0 for every non-player country. It checks
`isPlayer` rather than relying on the directive being empty, which is belt and
braces — a foreign official is never `Directed` (§3) — but the check is what makes
the intent legible: directing our own defence ministry must not raise every army
on the map. AI states reach the same place through posture, which costs them
treasury the same way.

Tests: `CabinetSystemTests.Directive_ShiftsOfficialFocus` measures 24 months of
`GOV_STABILITY` against `GOV_APPROVAL` and requires the stability gain to differ;
`PartialSystemsTests.RaiseReadinessDirective_ActuallyRaisesReadiness`,
`RaiseReadinessDirective_IsPaidForInTreasury` and
`ReadinessDirective_DoesNotLeakToForeignArmies`.

## 6. Direct Control actions

One per pillar, from `GetDirectAction(pillar)`. These are the operator acting
personally, and they are the only Cabinet verb priced in Command Points.

| Pillar | Id | Label | CP | Additional cost |
|---|---|---|---|---|
| Military | `DA_MIL` | EMERGENCY READINESS DRIVE | 2 | treasury −40 |
| Economy | `DA_ECO` | DIRECT STIMULUS ORDER | 2 | treasury −60 |
| Intelligence | `DA_INT` | SURGE COLLECTION | 2 | — |
| Diplomacy | `DA_DIP` | PERSONAL SUMMIT | 2 | — |
| Government | `DA_GOV` | EXECUTIVE ADDRESS | 1 | — |

`TryDirectAction` spends the CP through `TurnManager.SpendCommandPoints` (and
aborts on failure, changing nothing), then applies `ApplyPillarEffect` with
`directiveId = ""` and **`amount = 1.4`** — roughly 5.7 months of a median
official's work in one act, which is what the operator is buying.

It then charges the official whose pillar was taken over:

```
trust −= 2.0 × (1.5 − loyalty/100)      → −1.0 at loyalty 100, −2.0 at 50, −3.0 at 0
```

and files a **PRIORITY** notification on the **Command** desk (an echo of an order
the operator just issued — §16), a Political chronicle entry,
`RecordInitiative`, and 12 XP under the reason `"Direct control"`.

The XP bucket is deliberately not interpolated with the action label. One bucket
per *kind* of action is what makes the repetition discount work, and Direct
Control is the most repeatable verb in the game (spec 07 §2).

Tests: `DirectAction_SpendsCPAppliesEffectAndErodesTrust`,
`DirectAction_FailsWithoutCommandPoints`.

## 7. `MonthlyAct` — how an official performs

Wired to `TurnManager.ResolveMonth`. `MonthlyAct` iterates `state.countries` and
calls `MonthlyActFor` on each, so **every government's ministers do a month's
work**. `MonthlyActFor` returns immediately for a country with an empty cabinet,
which is how a headless harness that never appointed one stays runnable.

```
monthIndex = date.MonthsSince(startDate)
rng        = Random(rngSeed × 92821 + monthIndex × 31 + (int)office
                    + Hash.Of(country.id) × 7)

variance    = (U(0,1) × 2 − 1) × riskTolerance/100 × 0.3
performance = clamp(competence/100 + variance, 0, 1.2)
amount      = 0.05 + performance × 0.25
amount     ×= GovernmentSystem.PriorityMultiplierFor(leader.priority, office)
ApplyPillarEffect(country, office, directiveId, amount)
```

**The country id has to be in that seed.** Without it all sixteen cabinets drew
the same variance in the same month from the same three terms, so every government
in the world had a good year together and a bad year together — the world moved in
lockstep and nobody's cabinet was distinguishable from anybody else's. `Hash.Of` is
the stable in-house hash rather than `string.GetHashCode` for the reason in
spec 10 §7a. Test:
`ForeignCabinetTests.ForeignGovernmentsDoNotAllHaveAGoodYearTogether`.

Who runs a rival's ministries is meant to matter: at risk tolerance 0 to isolate
competence from variance, a cabinet at competence 90 outgrows one at 20 over 36
months (`CompetentForeignCabinetsOutperformIncompetentOnes`), and a foreign
cabinet left alone raises its own country's pillars over 24
(`AForeignCabinetGrowsItsOwnCountry`).

### This replaced the AI's capability growth; it did not add to it

`AISystem.InvestInPillars` used to be where a foreign government's pillars came
from. It is now reduced to `RebuildForces` alone (spec 06 §5). Leaving both in
place would **pay a government twice for one month's work** — the ministers raise
the pillar, and the AI routine would raise it again on the same tick.

What survives in `InvestInPillars` is the part a cabinet does not cover: ministers
raise a *pillar*, and only a procurement programme writes force `strength`, so
`RebuildForces` still has to be reachable from somewhere.

**This is the general rule for adding anything to the monthly tick.** Before
wiring a new source of capability, check whether an existing one already covers
the same ground for the same actor.

The `0.05` floor means even a hopeless minister moves the pillar slightly; the
`0.25` span means competence is worth at most five times the floor. A seeded
official (competence 40–78) produces **0.15 to 0.245 per month** before the
priority multiplier — so a decade of pure delegation is worth on the order of
20 points of a pillar before `Growth.Apply` taper, which is real but not a
strategy on its own.

`riskTolerance` widens `performance` by up to ±0.3 in either direction. A bold
official is not better, only less predictable; the same field also raises how
often the dice are rolled at all.

**The national priority multiplier applies to Directed officials too**, not only
Autonomous ones — only `DirectControl` exits before it, having already returned.
`PriorityMultiplierFor` returns ×1.35 for the favoured pillar and ×0.85 for the
rest (spec 05 §3), so a leader whose priority is `Prosperity` suppresses four of
the five desks by 15% whatever the operator asked for.

That is intended, not an oversight. The operator's directive decides *how* a
ministry works; the government's stated priority still decides what it is
**resourced** to do. Paying Influence buys a ministry's method, never a way
around the administration it serves — which is the whole premise of GDD §3, and
the reason `Set national priority` (3 PC) is a Cabinet-wide instrument rather
than a nudge to the idle. An operator who wants a pillar to run at full weight has
to either win the priority or take the desk over personally.

### Significant outcomes

```
eventChance = 0.04 + riskTolerance/100 × 0.06         → 4%..10% per official-month
success     = U(0,1) < performance × 0.7 + 0.15
```

| Result | Effect | Trust | Traffic (player's cabinet) |
|---|---|---|---|
| Success | `ApplyPillarEffect(..., amount × 3)` | +1 | **ADVISORY** `<OFFICE> INITIATIVE SUCCEEDS` |
| Failure | `ApplyPillarEffect(..., −amount × 2)` | −1 | **PRIORITY** `<OFFICE> SETBACK` |

The pillar effects and the trust movement are identical for every country. Only
the traffic branches, in `ReportCabinetOutcome` — see §7a.

All three of `MonthlyAct`'s **domestic** notifications — both of the above and the
Directed WIRE item below — are filed to `ReportingSystem.DeskFor(official.office)`,
so **an official's news travels through their own desk**. A weak minister is
therefore bad at reporting their own setback, which is the most characteristic
thing a weak minister does, and it is the one item of traffic whose loss the
player can attribute without help.

The first pass filed all of it under `Government` instead, on the reasoning that
cabinet performance is domestic political news. That was wrong in practice: one
weak Government minister then buried every *other* minister's news, so the symptom
pointed at the wrong office and the mechanic became undiagnosable. Routing them to
`Command` would have been the safe alternative, but it would have exempted the one
category of traffic where the filter is most legible. Test:
`ReportingSystemTests.AWeakMinisterDoesNotBuryTheOtherMinistersNews`.

Success is bounded at `0.15 + 1.2 × 0.7 = 0.99` and floored at 0.15, so nobody is
guaranteed and nobody is hopeless. The asymmetry (×3 up, ×2 down) is what keeps
delegation net-positive across a decade for a median cabinet while still making a
bad appointment visibly expensive.

Across five officials this fires roughly **4–6 times a year**, which is the
intended rate: often enough that the Cabinet is a source of news, rare enough
that the briefing is not a performance review.

### Delegation and being sidelined

```
Autonomous:    trust += 0.10 / month
Directed:      trust += 0.05 / month, plus a WIRE "<OFFICE> DIRECTIVE" item
               on that official's own desk
DirectControl: no action at all; trust −= 0.15 × (1.5 − loyalty/100)
```

The WIRE directive item is gated on `country.isPlayer` as well as on the mode.
The mode gate alone would be sufficient today, since a foreign official is never
`Directed` (§3), but the explicit `isPlayer` check states the rule at the call
site rather than relying on an invariant enforced two hundred lines away.

A sidelined official does nothing for the country — the pillar simply stops being
worked — and resents it at between −0.075 and −0.225 per month depending on
loyalty. At default loyalty that is roughly −1.5 trust a year, so permanent Direct
Control over one pillar is affordable for a term and corrosive over a decade.

Tests: `MonthlyAct_AutonomousOfficialsImproveTheNation`,
`MonthlyAct_IsDeterministicPerSeed` (36 months, JSON-identical),
`DirectControl_SidelinedOfficialDoesNotActAndTrustDecays`,
`Cabinet_SurvivesSaveRoundTrip`,
`ForeignCabinetTests.CabinetsSurviveASaveRoundTrip` (all sixteen).

## 7a. `ReportCabinetOutcome` — a foreign minister's news is intelligence

One routine files the traffic for a significant outcome, and it branches on
`country.isPlayer`:

| Whose minister | Success | Failure | Desk |
|---|---|---|---|
| Ours | **ADVISORY** `<OFFICE> INITIATIVE SUCCEEDS` | **PRIORITY** `<OFFICE> SETBACK` | `DeskFor(office)` — their own |
| Theirs | *nothing* | **WIRE** `FOREIGN CABINET SETBACK` | `Intelligence` |

Two decisions, both of which are about fog rather than about volume.

**A foreign item goes to the Intelligence desk, not to the pillar desk.** Our own
ministers reach the operator's desk through their own desk's reporting quality,
because that is the institution that would carry the paperwork (§16). A foreign
minister has no such route into our building — the only way that fact becomes ours
to know is through the service whose job it is to find out. Filing it under
`DeskFor(office)` would have read as our Economy Ministry reporting on *their*
Economy Ministry, which is **a fog leak dressed as a notification**: it would imply
an observation channel the intelligence layer never granted. It also means the item
is subject to our Intelligence official's reporting quality, so a weak intelligence
desk loses foreign cabinet news, which is exactly right.

**Only a setback travels.** A foreign success files nothing at all. A rival
minister quietly having a good quarter is not the sort of thing that surfaces
without collection behind it, and filing every foreign success would put roughly
seventy-five official-months of routine foreign competence a year through the
briefing — which would bury our own cabinet's news under the world's. The message
names the official, their own country's office title and their country, so the
item still reads as a specific person in a specific government.

The pillar and trust consequences are unchanged either way. **The world happens
identically; only what reaches the operator differs.** That is the same separation
§18 asserts for the reporting filter, held here at the point of filing.

## 8. Trust is a state, not a currency

Trust is never spent and gates nothing directly. It moves on delegation (+),
override (−), and the official's own results (±1). Its one mechanical consumer is
§11: below 40 it degrades reporting. That is the whole of GDD §7.3's "repeated
overrides can damage official trust" — the damage is that the desk stops bringing
the operator bad news.

---

## 9. Reporting: the operator does not observe the world (GDD §28.1)

GDD §28.1 requires that "official competence influences what is surfaced or
missed, making delegation part of the information experience."

Before `ReportingSystem`, `Official.competence` was read at exactly one gameplay
site — scaling autonomous output in §7 — so appointing a capable minister changed
how well a pillar *performed* and nothing about how well the operator could *see
it*. Delegation was a throughput decision. It is now also an information
decision.

The premise: every item on the briefing except the operator's own command traffic
passes through the official who runs that pillar. A weak desk reports badly —
things arrive stripped of urgency, or never arrive at all.

## 10. Desks

`Notification.desk` carries a `ReportingDesk`: `Command`, `Military`, `Economy`,
`Intelligence`, `Diplomacy`, `Government`. The five pillar desks map one-to-one
onto Cabinet offices via `OfficeFor` / `DeskFor`.

**`Command` is the default parameter value on `GameState.AddNotification`, and it
is never filtered.** This is the single most important structural decision in the
system. It means:

- The operator's own traffic — turn structure, crises, alliance obligations,
  their own orders coming back confirmed — has no intermediary that could lose it.
- A notification added by a system written next year, by someone who has never
  read this document, **cannot silently go missing**. Making something filterable
  is always an explicit act at the call site.

The inverse default would have been a trap: the failure mode of a filter is
invisible, and a missing briefing item looks exactly like a bug in the system that
should have produced it.

There are currently **59 explicit desk assignments across 13 systems**. Everything
else in the game is Command traffic by omission.

Test: `ReportingSystemTests.CommandTrafficIsNeverFiltered` sets every official to
competence 0 and files 60 Command items; all 60 survive.

## 11. `MishandleChanceFor`

```
ReliableCompetence = 60
FailingCompetence  = 25

official = OfficialFor(desk)
if official == null              → 0        (Command desk)
if mode == DirectControl         → 0        (rule 2, §12.2)

shortfall = clamp01((60 − competence) / (60 − 25))
if shortfall <= 0                → 0
chance    = shortfall × 0.55
if mode == Directed  chance ×= 0.5
if trust < 40        chance += (40 − trust) × 0.004
return clamp(chance, 0, 0.75)
```

### Why 60 and 25

Both numbers are calibrated against the seeded cabinet's **40–78** competence
band, not chosen for roundness. That band is set in two places and both must stay
in step: `WorldFactory.MakeCabinetFor` at world creation, and the administration
reshuffle in `GovernmentSystem` (§2).

**The filter reads the player's cabinet only.** `ReportingSystem.OfficialFor`
resolves a desk through `state.cabinet` — the view onto the player country (§1a) —
so the sixteen foreign cabinets have competence, but no reporting filter of their
own. An AI government reasons directly from its own estimates with no institutional
layer in between (spec 06 §9). Our ministers can lose the news; a rival's never can.

`ReliableCompetence = 60` sits just above the middle of that band, so a *median*
appointment reports nearly everything and only the bottom of the roll degrades.
An earlier calibration put the pair at **78 / 35** — the top of the seeded range —
which meant every default cabinet lost roughly **a quarter of the world's news
from month one**, before the player had made a single decision. That does not read
as a mediocre minister; it reads as an empty game. Degradation has to be something
the player did.

`FailingCompetence = 25` sits *below* the seeded floor of 40, so a barely-reporting
desk is unreachable by an unlucky roll at world creation. Getting there takes a bad
dismissal, a reshuffle, a purge or a coup — an event the player can point at.

The `0.55` scale means the worst reachable Autonomous desk still files 45% of its
routine traffic. A hopeless minister is not a blackout.

### The Directed factor, the trust term and the ceiling

`Directed` halves the chance **before** the trust term is added. A stated priority
focuses a desk on what the operator asked about, so directing is a genuine
information purchase for 1 Influence — but it is applied to competence only, so a
resentful desk is proportionally *worse* under Directed than a trusting one.

The trust term adds up to +0.16 at trust 0. Trust is the willingness to bring the
operator bad news, and it is the only channel by which overriding an official
comes back at you (§8).

The `0.75` ceiling is a guard rail that current parameters never reach: the
realizable maximum is `0.55 + 0.16 = 0.71`, at competence ≤ 25, Autonomous, trust
0. It exists so that no future parameter change can accidentally produce a desk
that reports nothing at all.

Tests: `BetterAppointmentsMeanBetterReporting`,
`DirectedReportsBetterThanAutonomous`, `ADistrustedDeskReportsWorse`,
`AFailingDeskLosesRoutineTraffic` (competence 10, 150 WIRE items, some lost),
`AStrongDeskLosesNothing` (competence 100, 150 items, none lost).

## 12. The three fairness rules

A filter on information is only a good mechanic while it cannot take away the
player's ability to act, and while the player can see it coming. Each rule below
has a test whose assertion message states the reason, so the rule survives a
refactor by someone who has not read this file.

### 12.1 A decision is never withheld

FLASH is skipped unconditionally in `FilterMonth`, before any roll.

FLASH means an answer is required this month. Withholding one strands the player
in front of a turn they cannot take — a blocking crisis they were never told
about, an alliance obligation whose deadline passes in silence. **Incompetence
costs awareness, never agency.**

Test: `ReportingSystemTests.ADecisionIsNeverWithheld` — competence 0, 60 FLASH
items, all 60 survive.

### 12.2 What you run yourself, you see yourself

`MishandleChanceFor` returns 0 for any desk in `DirectControl`.

Direct Control removes the intermediary, so it removes the filter. Its costs are
CP per action and the official's trust (§6, §7) — not blindness. This also gives
Direct Control a second reason to exist beyond raw throughput: an operator who
cannot see a theatre clearly can take the desk over and read it personally,
paying in political and relational terms rather than in ignorance.

Test: `ReportingSystemTests.WhatYouRunYourselfYouSeeYourself` — competence 0 under
Direct Control, 60 WIRE items, all 60 survive.

### 12.3 The record stays honest

Nothing in `ReportingSystem` touches `GameState.chronicle`.

A missed item still happened. It is still in the world's record, still filterable
by state and category in the CHRONICLE view, still counted in the year-in-review
tally. Finding out in March what your Foreign Ministry did not tell you in
November is not a consolation prize for the mechanic — it *is* the mechanic.

Test: `ReportingSystemTests.TheChronicleKnowsWhatTheBriefingDidNotSay`.

## 13. The two effects

A failed roll does one of two things, by class:

| Class | On a failed roll |
|---|---|
| FLASH | never rolled (§12.1) |
| **PRIORITY** | **downgraded to ADVISORY** — survives, loses its urgency |
| ADVISORY / WIRE / ARCHIVE | **removed** |

Burying rather than deleting PRIORITY is the difference between "your minister is
unreliable" and "your minister is hostile". Important news reaches the operator
buried in the routine traffic instead of at the top of the briefing, which is a
recognisable institutional failure rather than a conspiracy. It also means a
downgraded item is still there to be found by a player who reads the whole
briefing — the penalty is on attention, not access.

`FilterMonth` returns the count of items that never reached the desk and logs it
at Debug level under the `REPORT` tag.

Test: `ImportantNewsIsBuriedButNeverLost` asserts both halves — all 60 PRIORITY
items survive, and at least one has been downgraded.

## 14. Where it runs in the month

`TurnManager.EndMonth`:

```
trafficStart = State.notifications.Count        ← index of this month's first item
ResolveMonth?.Invoke(State)                     ← the whole world resolves
if (resolvedDate.IsYearEnd) YearEnded?.Invoke() ← annual evaluation, year in review
ReportingSystem.FilterMonth(State, trafficStart)
State.date = resolvedDate.NextMonth()
... CP reset, Influence accrual, MONTH START notification
```

Three properties of that position matter:

- **After the world resolves**, so every system files its traffic naively and none
  of them needs to know reporting exists.
- **After the year-end evaluation**, so the year-in-review ARCHIVE item is subject
  to the same filter as everything else it summarises.
- **Before the operator reads the briefing** and before the new month's Command
  traffic is filed — `MONTH START` and `CRISIS PENDING` are added afterwards and
  are Command class anyway.

The `trafficStart` index is what scopes the pass to one month. Filtering the whole
list would re-roll every historical item every month and eventually delete the
entire archive; scoping by index also means the loop is O(this month's traffic),
not O(save length). The loop walks **backwards** from the end to `trafficStart`
so that `RemoveAt` cannot disturb indices it has not yet visited.

`FilterMonth` returns 0 immediately if `Disabled` (§17) or if the player's cabinet
is empty — which is the case in a headless harness that never called
`MakeCabinetFor`, and in a hand-built state whose player country has no authored
profile.

## 15. Determinism

```
rng = Random(rngSeed × 15485863 + monthIndex × 6151 + (int)desk × 97 + i × 389)
```

seeded per item, from the save's seed, the month index, the desk and the item's
**position in the notification list**. All four multipliers are primes, so
adjacent months, desks and indices do not collide.

A reloaded save must be told exactly the same things. If the roll came from a
shared stream, a player could reload and re-roll the briefing until the news they
wanted appeared — which would make the mechanic a slot machine and the save file a
cheat. Because the seed includes `i`, it is also stable within a month regardless
of how many items were filed.

`NextActionSequence()` is deliberately **not** mixed in: `FilterMonth` runs exactly
once per month, so there is no second call in the same month to disambiguate
(spec 10 §3).

Test: `ReportingSystemTests.FilteringIsDeterministic` builds two identical worlds
from seed 5150, files 150 identical items into each, and requires the same survivor
count.

## 16. Assigning a desk to new traffic

**Assign by who files the item, not by which file the code lives in.**

The worked example is in `RegimeSystem.ReportForeignInstability`. It emits
`INSTABILITY ASSESSED` about a foreign government's internal fracture. The code
lives in `RegimeSystem`, which is the government pillar's file — but the
notification exists *only* because the two lines above it obtained a Political
estimate at Moderate confidence or better. The intelligence service produced it,
so the intelligence service is what either passes it up or does not. It is
`ReportingDesk.Intelligence`.

The same logic puts `RegimeSystem`'s domestic coup, purge and civil-conflict items
on the Government desk, and `TechnologySystem`'s programme reporting on the Economy
desk (a research programme is funded and administered through the economic
ministry, not the laboratory). It also puts each official's own performance news
on that official's desk rather than the Government one (§7): the minister who had
the setback is the person who has to report it.

`ReportCabinetOutcome` (§7a) is the case where the same event routes to two
different desks depending on whose it is. A domestic setback comes up through that
ministry's own paperwork; the identical event in a foreign ministry has no such
route and is Intelligence traffic. **The desk is decided by the channel the item
actually travelled, and a foreign fact has only one channel.**

Two standing exceptions stay on `Command`:

1. **A notification that *is* the presentation of a decision.** A Crisis Turn, an
   alliance call-in, an offered settlement — the operator is being asked, not
   informed. Filtering these would violate §12.1 by another route, since not all
   of them are FLASH.
2. **An echo of an order the player just personally issued in the same call.**
   `TryDirectAction`'s confirmation, a sanction the operator just imposed, a
   treaty they just proposed. There is no intermediary between an operator and
   their own order; filtering it would tell them their own action did not happen.

When in doubt, leave it on `Command`. Under-filtering costs a little atmosphere;
over-filtering costs the player a decision they never knew existed.

## 17. What the player sees

An information penalty the player cannot see coming reads as the game being
broken, not as a consequence of who they appointed. Two surfaces exist so that
never happens.

**The CABINET view** renders a `REPORTING` bar in every official's dossier, beneath
COMPETENCE / LOYALTY / RISK APPETITE / TRUST, driven by

```
ReportingQualityFor(desk) = 100 − MishandleChanceFor(desk) × 100 / 0.75
```

so the same 0..100 scale as the other four bars. Below it, `ReportingText` states
the consequence in plain language, in one of four bands:

| Chance | Line |
|---|---|
| ≤ 0.001, Direct Control | REPORTING: DIRECT — you read this desk yourself. |
| ≤ 0.001 | REPORTING: RELIABLE — this desk files everything it has. |
| < 0.18 | REPORTING: SOUND — the occasional item arrives late or understated. |
| < 0.35 | REPORTING: UNEVEN — routine traffic from this desk is unreliable. |
| ≥ 0.35 | REPORTING: POOR — much of this desk's traffic never reaches you. Decisions still reach you; awareness does not. |

For an Autonomous desk at trust ≥ 40 those bands correspond to competence ≥ 60
(reliable), 48.6–60 (sound), 37.7–48.6 (uneven) and below 37.7 (poor). Under
`Directed` the halving pushes POOR out of reach on competence alone — the chance
tops out at 0.275 — so a Directed desk only reads POOR if trust has fallen below
about 21.

The POOR line names the fairness rule out loud. A player who reads "decisions
still reach you" knows they are losing awareness and not agency, which is the one
thing they must not have to discover experimentally.

**SYSTEM** carries `DBG FULL REPORTING`, toggling the static
`ReportingSystem.Disabled` and logging which state it is in. Every significant
system exposes a debug control (GDD §34.1); for this one it is also the only way
to answer "did my minister lose that, or did the system never file it?" while
debugging another system. The flag is static and **not** saved — it is a session
tool, and it is reset in the test fixture's `SetUp` and `TearDown` so a leaked
`true` cannot silently pass the rest of the suite.

Test: `TheDebugSwitchRestoresEverything`.

## 17a. The foreign cabinet dossier (INTELLIGENCE view)

`IntelligenceView.BuildForeignCabinetDossier` is the only place the player can see
that foreign cabinets exist at all, and a system with no surface reads as no
system. It is gated on the player's `IntelNetwork` penetration against the selected
target, in **stages**, because who holds an office is close to public and judging
how good they are at it is not:

| Penetration | What is shown |
|---|---|
| < 20, or network compromised | Nothing but a line saying to establish a network |
| 20–54 | Office and name for all five, competence `UNASSESSED` |
| ≥ 55 | Office, name, and a competence **band** |

The bands are `CAPABLE` (≥ 70), `ADEQUATE` (≥ 50), `WEAK` (≥ 35),
`OUT OF DEPTH` below. **A band, never a number** — printing `63.4` would claim a
precision collection does not have, and the pillar rule holds here as everywhere
else: a view must never print a foreign country's true value (spec 03).

Tests: `ForeignCabinetTests.ForeignOfficialsAreNotIdentifiedWithoutCollection`
(a fresh world has no network against a rival, so the dossier's first gate is
closed at world creation rather than open by default) and
`CompetenceIsReportedAsABandNeverANumber` (every official's competence stays
inside 0..100, which is what the band mapping assumes — a value outside it would
fall through `CompetenceBand`'s ladder to `OUT OF DEPTH` and misreport a capable
minister as a hopeless one).

## 18. The mechanic is balance-neutral by construction

Reporting changes what the operator **knows**, never what **happens**. It does not
touch a pillar, a resource, a relationship, a confrontation or the chronicle; its
entire write surface is one enum field on a notification and one `RemoveAt`.

That is deliberate. An information mechanic that also carried a statistical
penalty would be impossible to reason about — a player could not tell whether a
bad year came from missing the news or from a hidden modifier — and it would make
the annual evaluation a function of appointment luck.

The 5-seed playstyle table is **byte-identical** before and after
`ReportingSystem` landed, and again after the three fixes above — recorded in
spec 12 §7. Identical grades are the *expected* result, not a happy one: a future
reporting change that moves those numbers would be evidence of a leak from the
information layer into the simulation, and should be treated as a bug before it is
treated as balance.

`ReportingSystemTests.APlayableGameStillArrivesWithTrafficEveryMonth` is the
backstop: the worst cabinet in the world — every official at competence 5,
Autonomous — plays 24 real months with the Cabinet, Economy, Military and
Government ticks wired in, and the briefing is still not empty.

## 19. Remaining weaknesses

**The Cabinet is far shallower than GDD §8 asks for.** Everything below is
specified in the GDD and absent from the code:

- **No candidate pool.** `DismissOfficial` rerolls the incumbent object in place,
  so dismissal is a gamble on hidden dice rather than a choice between named
  people with known competence, ideology and baggage. §8's "the technically
  strongest candidate may be strategically or politically incompatible with the
  player" cannot happen, because there is never more than one candidate.
- **No ages, no lifecycle.** Officials do not age, retire, resign, lose office or
  die. `monthsInOffice` counts up and is displayed; nothing reads it. Only an
  administration change, a dismissal or a coup ever replaces anyone.
- **Officials never disagree, leak, criticize, resign or obstruct.** The entire
  §8 clause about officials as political actors is unbuilt. Trust degrades
  reporting and nothing else; there is no threshold at which an official refuses
  an order, briefs against the operator, or joins `RegimeSystem`'s conspiracy —
  even though military loyalty and conspiracy are already simulated next door.
- **Nothing about an official evolves.** No experience, no traits, no
  relationships with each other, no reputation. Competence is fixed from the roll
  that created them until the day they are replaced, so a long-serving minister
  is never better at the job than on their first month.
- **`loyalty` and `riskTolerance` are read only by `CabinetSystem`.** Two of the
  four personality axes affect nothing outside this file.
- **Only one directive reaches its pillar's actual subsystem.** `MIL_READINESS`
  now moves the readiness target as well as the pillar (§5); the other nine are
  still scalar policy one layer above the systems they are named after. EXPAND
  COLLECTION moves the intelligence pillar and never touches an `IntelNetwork`;
  BROAD OUTREACH moves the diplomacy pillar and never touches a `Relationship`.
  `MIL_READINESS` is the template for fixing the rest, including the
  target-not-value rule and the player-only guard.

**Reporting only drops or buries.** It never *delays* an item to a later month and
never delivers it **distorted** — a figure wrong by a margin, a confidence grade
inflated, an attribution mistaken. GDD §28.1's "surfaced or missed" arguably
implies both, and distortion in particular would fit the existing estimate
machinery (spec 03) almost exactly: an item filed by a weak desk could carry the
same margin treatment a low-confidence foreign estimate does. Delay is the
cheaper of the two to build — a `holdUntil` date on `Notification` and a release
pass at the top of `FilterMonth` — and would produce the distinctly governmental
experience of learning something true three months after it would have been
useful.

**The AI has the officials but not the institution.** Every government now runs a
Cabinet (§1) and its capability comes from those five people. What a foreign
government still does not have is the layer *around* them: no control modes, no
Influence, no `AuthoritySystem`, and no reporting filter — it reasons from its own
estimates with nothing between the world and the decision (spec 06 §9). The
operator's ministers can lose the news; a rival's never can.

The gap this opens is that a foreign cabinet is currently **inert as a target**.
The player can identify a rival's ministers and assess their competence (§17a),
and a coup will wreck them (§2), but there is no verb that reaches an individual
foreign official: no recruitment, no compromise, no discrediting, no
assassination. `CovertOperation` acts on networks and national statistics, never
on a person. Now that the people exist and are visible, that is the obvious next
thing for the intelligence pillar to be able to do.

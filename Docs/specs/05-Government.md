# 05 — Government System Specification

Source: `Core/GovernmentSystem.cs`, `Core/AuthoritySystem.cs`,
`Core/RegimeSystem.cs`, `Data/Government.cs`. GDD §13, §7.3, §12, §22, §3.

## 1. Government types change how power works

Five structural types. These are institutional descriptions, **not judgments about
real governments** — keep that framing in code comments and UI text.

| Type | Leadership changes by | Term limit | Backing metric |
|---|---|---|---|
| `PresidentialRepublic` | Scheduled election | **2 consecutive** | `legislativeSupport` |
| `ParliamentaryRepublic` | Election; may be called early | none | `legislativeSupport` |
| `DominantPartyState` | Internal succession | n/a | `eliteCohesion` |
| `CentralizedRepublic` | Elite-brokered succession | n/a | `eliteCohesion` |
| `Monarchy` | Internal succession | n/a | `eliteCohesion` |

`GovernmentState.IsElective` is true for the two republics and is the switch every
other rule reads. `AllowsEarlyElection` is true for `ParliamentaryRepublic` alone.

The type must change *behavior*, not apply a modifier. Concretely:

- Emergency powers cost **×1.4** in an elective system, **×0.7** in a
  non-elective one.
- Dismissing an official costs **×1.25** elective / **×0.85** non-elective, and
  only elective systems also lose legislative goodwill (−4).
- Only `ParliamentaryRepublic` may call an early election.
- Only `PresidentialRepublic` sets a term limit (`consecutiveTermLimit = 2`;
  every other type is 0, meaning no limit).
- The type sets where constitutional authority sits — §1a, which is the one that
  matters most.

Seating comes from the country profiles in `WorldFactory`: `termLengthMonths` is
48 or 60 for elective states and 0 for the rest, `legislativeSupport` starts
48–70, `eliteCohesion` 55–80, and the first election is staggered to
`max(6, termLengthMonths − monthsInOffice)` months out so a new save does not
open on one.

## 1a. Constitutional authority (`AuthoritySystem`, GDD §3)

GDD §3 lists as **non-negotiable** that the operator "may directly direct a pillar
when political and constitutional authority permits, and otherwise must influence
or recommend." This had no implementation whatsoever: Direct Control was
available over any pillar, in any government type, at any time, for flat CP, and
government type changed only Political Capital *prices*. That rule is the hinge
of the premise — an operator inside an institution he does not own is a different
game from a player with uniform access wearing a bureaucratic costume.

`AuthorityOver(state, pillar)` returns one of three levels for the **player's**
government:

- **Direct** — the executive commands it. Free.
- **RequiresApproval** — obtainable, at `ApprovalCost` = **3 PC**, and a
  legislature below **30** support **refuses** (the capital is spent in the asking
  either way). Succeeding costs **2.5** legislative support: reaching over an
  institution's head costs standing with it.
- **AdvisoryOnly** — outside the writ. Directed and Autonomous modes remain
  available; Direct Control does not.

| Government | Military | Economy | Intelligence | Diplomacy | Government |
|---|---|---|---|---|---|
| Presidential republic | Direct | Approval | Direct | Direct | Approval |
| Parliamentary republic | Approval | Advisory | Approval | Direct | Advisory |
| Dominant-party state | Direct | Approval | Direct | Direct | Direct |
| Centralized republic | Direct | Direct | Direct | Direct | Approval |
| Monarchy | Direct | Advisory | Approval | Direct | Direct |

The shape differs per constitution, not the size of a modifier: a presidential
executive owns force and foreign affairs but goes to the legislature for the
economy; a parliamentary one holds almost nothing without assent; a dominant-party
state commands the apparatus but not the ministries of production; a monarch's
writ runs to the army and the court but not the treasury.

**Emergency powers suspend the whole distribution** — every pillar becomes
Direct, checked before the type switch. That is precisely what makes them worth
their political price, and it gives the emergency-powers verb a strategic reason
to exist beyond +2 CP.

**Where it is enforced:** `CabinetSystem.SetMode` calls
`AuthoritySystem.ObtainAuthority` when an official is moved *into* Direct Control,
and refuses the mode change if it fails. Directing and advising are always ours
and cost nothing constitutional. Authority is therefore paid for once, at the
point of taking command of a pillar — not per action.

Tests: `StrategyAndAuthorityTests.DifferentConstitutions_PutAuthorityInDifferentPlaces`,
`AParliamentaryOperator_CannotSimplyCommandTheEconomy`,
`AuthorityThatMustBeObtained_CostsPoliticalCapital`,
`AnInstitutionThatDoesNotBackUs_CanRefuse`,
`ExecutiveAuthority_IsDirectAndFree`,
`EmergencyPowers_SuspendTheOrdinaryDistributionOfAuthority`.

## 2. Political Capital (GDD §7.3)

**Not a player resource.** Every government has one, capped at
`GameState.PoliticalCapitalCap` = 20, accrued monthly in `AccruePoliticalCapital`
after every country's political condition has been resolved. The player's pool is
`GameState.politicalCapital`; each AI government's is `AIState.politicalCapital`,
stored there rather than on `CountryState` so the player's save schema is
untouched by the addition (spec 06 §5a).

`PoliticalCapitalIncomeFor(CountryState)` is the **one formula**, and it does not
know who is running the government:

```
income = 0.6 + approval/60 + governmentPillar/90
       + (legislativeSupport or eliteCohesion)/120
       + 0.5 if emergency powers are in force
       + CAP_CIVADMIN effectiveness × 0.6     ← a state apparatus that executes (GDD §11)
```

```
player only, added by the caller:
       + SkillEffect.PoliticalOperator
```

Keeping one formula is the point: a popular government with a cohesive elite is
well funded politically whoever holds it, and one that has spent its authority
rebuilds it the same way. **Operator skill is excluded from the shared formula on
purpose** — skills grant operator capability and never national power (spec 07),
and a `PoliticalOperator` term inside `PoliticalCapitalIncomeFor` would hand the
player's skill tree to fifteen foreign governments.

Two accessors carry the actor-generic half:

- `PoliticalCapitalOf(state, countryId)` — reads either pool.
- `SpendPoliticalCapitalBy(state, countryId, amount, reason)` — debits either, and
  returns false when the government cannot afford the act. It must bite for an AI
  exactly as it bites for the player, or the budget is decoration.

| Instrument | Cost | Effect |
|---|---|---|
| Distribute patronage | 1 + 200 treasury | §2b |
| Bargain for support | 2 | §2b |
| Public messaging | 2 | Approval +4×eff, unity +1.5×eff, where `eff = 1 + governmentPillar/120` |
| Set national priority | 3 | Redirects every delegated official; rejected as a no-op if already set |
| Call early election | 3 | Parliamentary only; sets the election to this month |
| Prepare a successor | 3 | §2c |
| Set civic posture | 3 | §2a |
| Obtain authority over a pillar | 3 | §1a |
| Public inquiry | 4 | §2b |
| Dismiss & replace official | 4 (×1.25 / ×0.85) | New appointee: loyal (45–85), fresh trust (55–65), reverts to Autonomous; −4 legislative support if elective |
| Emergency powers | 5 (×1.4 / ×0.7) | 6 months of extraordinary authority |
| Secure military loyalty | 5 | §7a |
| Institutional reform | 6 | `Growth.Apply(governmentPillar, 4)`, stability +3, counterintel +2; backing −3 elective / −5 non-elective |
| **Consolidate authority** | **12** | §2d — the one large purchase |

Every one of these calls `ProgressionSystem.RecordInitiative` after the spend
succeeds — omit it on a new verb and the Government pillar quietly grades below
inaction (spec 07).

> **Why the list grew.** The pillar graded at **+0.12 over passive**, the weakest
> in the game, for two structural reasons rather than tuning. It had almost
> nothing to do month to month — 26 decisions in a decade against the economy's
> 344 — and **Political Capital had no sink above 7 against a cap of 20**, so an
> operator who was not spending simply sat at the cap with nothing worth buying.
> The fix is a cheap repeatable workhorse (§2b), a standing choice (§2a) and one
> genuinely large purchase (§2d).

## 2a. Civic posture — the pillar's standing choice (GDD §12, §13)

The military has posture and doctrine; government had only one-off interventions.
`CivicPosture` is a position you hold and pay for every month, which is a
different kind of decision from an action you take once.

| | Order | Unity | Approval | Plots form at | Monthly bill |
|---|---|---|---|---|---|
| `Open` | −4 | +9 elective / +4 otherwise | +4 | ×1.35 | — |
| `Standard` | — | — | — | ×1.00 | — |
| `Restrictive` | +9 | −10 | −9 elective / −4 otherwise | ×0.60 | 0.45 PC |

**Government type is the interesting part.** A system that faces the voters pays
more than twice as much approval for governing restrictively, and gains more unity
from governing openly. That is §13's requirement that the type change *how power
works* rather than hand out a modifier, applied to the one standing choice the
pillar has.

`Standard` is declared **first** in the enum so its ordinal is zero and an old
save deserializes to the neutral setting. Display order is the view's business.

The conspiracy multiplier is read by `RegimeSystem.UpdateConspiracy` and applies
to grievance *and* foreign subversion. It is the whole case for closing civic
space — and, since the posture costs legitimacy, unity and political capital every
month it is held, the whole case against.

## 2b. Political bargaining (GDD §13)

Three verbs that pull against each other, which is what makes them decisions.

**`BuildPoliticalSupport` / `…By`** (2 PC) — the workhorse. Raises
`GovernmentState.brokeredSupport`, with diminishing returns
(`+3 + max(0, 60 − current) × 0.14`).

> **It moves the target, not the value.** `legislativeSupport` and `eliteCohesion`
> both drift toward a computed target every month, so a verb that added to them
> directly would be erased before the player could feel it — the same trap that
> made occupation's readiness cost dead code. `brokeredSupport` is added into both
> targets at ×0.45 and decays at `BrokeredSupportDecay = 0.94`/month, so support
> is something a government **maintains** rather than buys once. That decay is
> also what gives the pillar something to do most months.

What it buys is concrete: below 30 legislative support the legislature refuses the
operator direct authority over a pillar outright (§1a), and the political capital
is spent asking either way.

**`DistributePatronage` / `…By`** (1 PC + 200 treasury) — the same destination by a
different road. Raises `brokeredSupport` less, raises every cabinet official's
loyalty +5, and applies `Growth.Apply(governmentPillar, −1.4)`. Losses are felt in
full, so governing habitually this way genuinely hollows the state out. A rich
government spends treasury; a well-regarded one spends authority.

**`LaunchInquiry` / `…By`** (4 PC) — the inverse. Raises the **weakest** minister's
competence +7 (and drops their loyalty −4; nobody enjoys being audited),
`Growth.Apply(governmentPillar, +2.5)`, counterintelligence +1.5, at the cost of
`brokeredSupport` −9 and backing. Targeting the weakest desk rather than the
strongest is deliberate: competence decides what reaches the terminal at all
(spec 15), so this buys *awareness* as well as performance.

## 2b-1. Faction arithmetic (GDD §13)

`Leader.faction` was a display string no rule read. It now feeds the backing
targets through two pure helpers, both tested in `FactionTests`:

**`FactionSupportShift`** (elective) — a leader who arrived by turnover
(`"OPPOSITION"`, `"REFORM BLOC"`) governs against a chamber still partly held by
the people they defeated: **−12** on the legislative-support target, decaying
linearly over `FactionConsolidationMonths = 30` in office. A term in a target,
never a ratchet — and `brokeredSupport` (×0.45, §2b) can outbid the whole
penalty, which finally gives the workhorse verb its most natural customer: a
new government buying its chamber.

**`FactionCohesionShift`** (non-elective) —
- `"MILITARY COUNCIL"`: cohesion tracks the officer corps,
  `(militaryLoyalty − 65) × 0.25`. Undermining the army *is* undermining the
  junta — the specific vulnerability GDD §22 promises a coup-born government.
- `"PROVISIONAL AUTHORITY"`: −10 decaying over 36 months; a breakaway is a
  state still deciding whether it is one.
- `"PARTY LEADERSHIP"` and every other label: zero. A label that penalises by
  accident of wording would be worse than one that does nothing.

**Confidence** — `GovernmentType.ParliamentaryRepublic`'s declaration
("government falls with confidence") is now implemented:
`GovernmentSystem.CheckConfidence` runs monthly for parliamentary systems
between elections; below `ConfidenceThreshold = 30` legislative support there is
a `ConfidenceCollapseChance = 0.15`/month the government falls —
`InstallNewLeadership("lost the confidence of the chamber")`, snap-election
calendar reset, public chronicle entry. Probabilistic so the fall arrives like
an ambushed division vote; deterministic per save like everything else.
Recovery stays reachable the whole way down (bargaining, patronage, messaging
all move the target this reads). A presidential system rides a hostile chamber
out to the scheduled date — this is what makes the two elective types play
differently. Player-visible in GOVERNMENT: the chamber line and a
`CONFIDENCE AT RISK` warning below the threshold.

## 2c. Succession preparation (GDD §13)

`GroomSuccessor` / `…By` (3 PC, +22 readiness, capped). Administrations come and
go while the operator persists, and until now a transition was purely something
that happened *to* the player. Two payoffs, both consumed at the transition:

- **Competence floor.** The incoming leader rolls 40–85; readiness raises the
  floor to `40 + readiness × 0.35`. It cannot manufacture a great leader — it only
  stops the state being handed to someone who has never seen the papers.
- **An orderly handover.** `CheckSuccession` marks a succession contested only if
  `eliteCohesion < 45` **and** `successorReadiness < 50`. A contested one costs 12
  stability and 8 unity, so this is the half the player actually feels.

## 2d. Consolidating authority (GDD §3, §13)

`ConsolidateAuthority(state, pillar)` — **12 PC**, permanently raises one pillar to
`AuthorityLevel.Direct` via `GovernmentState.authorityUpgradeMask`.

The large purchase the Political Capital economy did not have. Roughly five months
of income, and unlike emergency powers it does not lapse and costs nothing to hold.

- Refused (**before** the charge) if elective and `legislativeSupport < 45` — so
  bargaining first is the answer rather than repeatedly paying to be told no.
- Costs backing −6/−5 and approval −2. The institutions that gave it up do not
  forget.
- Read in `AuthoritySystem.AuthorityOver` **after** the emergency-powers
  short-circuit and **before** the structural table.

**Deliberately player-only, and deliberately not national power.** Authority is an
operator interface: it describes what this particular advisor may order without
asking, not what the state is capable of. A foreign government has no operator
standing outside it to be granted anything — the same reasoning that settled
Influence and Crisis Turns. `GovernmentVerbTests.AuthorityIsOperatorCapabilityAndNeverNationalPower`
asserts it moves no national statistic.

It is **not** behind `GameController.MayCommand`: amending what the operator may
command cannot itself require the authority being amended, or a parliamentary
operator — the one who most needs it — could never reach it.

### Actor-generic verbs

`PublicMessagingBy(state, countryId)` and `InstitutionalReformBy(state, countryId)`
are the real implementations; `PublicMessaging` and `InstitutionalReform` are thin
player wrappers that delegate and then award XP and call `RecordInitiative`. Both
of those are player-only by definition — an AI government has no Strategist file —
so they stay in the wrapper rather than the shared path. Any government can
address its people and any government can reform its institutions, and all of them
pay the same Political Capital and the same goodwill for it: reform disturbs
entrenched interests wherever it happens (`AIDomesticTests.ReformCostsAForeignGovernmentItsGoodwillToo`).

The bargaining verbs follow the same shape:
`BuildPoliticalSupportBy`, `DistributePatronageBy`, `LaunchInquiryBy`,
`GroomSuccessorBy` and `SetCivicPostureBy` are the real implementations, with thin
player wrappers over each. Every government bargains for its own support, buys
loyalty when it has money and not standing, audits itself, prepares a succession
and decides how to hold its own society — and each pays for it from its own pool
(`GovernmentVerbTests.ForeignGovernmentsPayForThemLikeAnyoneElse`).

`AISystem.ConsolidateHome` is the only non-player caller (spec 06 §5a).

> **It scores every instrument and takes the worst problem it can afford.**
>
> A ladder was tried twice and failed the same way twice. Institutional reform was
> unconditionally first, and since most states sit below its pillar threshold for
> most of a save, **nothing below it was ever reached** — every verb added
> underneath was unreachable by any foreign government. Demoting reform then let
> *bargaining* crowd out the four verbs below **it**, because bargaining costs
> 2 PC and almost always succeeds.
>
> Scoring is the only shape that lets a rare problem win when it is genuinely the
> pressing one: backing gap, structural gap, weakest minister, an ageing leader
> with nobody prepared, conspiracy above 28, an apparatus no longer needed, and
> public approval all compete on a single scale. A failed attempt falls through to
> the next, so a government that cannot afford reform this month still sends its
> minister out to speak rather than doing nothing.
>
> The objective feeding it was also mis-scored: each term is now floored at zero.
> Unfloored, healthy stability scored **negative** and subtracted from the case
> for shoring up a chamber that had genuinely turned on the government — so the
> moment stability gained a restoring force and stopped sitting low, the whole
> objective became unreachable.

**Reform runs through `Growth.Apply`, not a flat +4.** A raw addition was
survivable while the player was the only reformer. Opening the verb to fifteen AI
governments saturated the Government pillar at its ceiling within a decade and
`WorldInvariantTests.CapabilityDoesNotSaturateOverALongSave` correctly refused it.
Diminishing returns are also simply true of the thing being modelled: reform gets
harder as institutions get better.

The remaining instruments stay player-only, because each depends on something the
AI does not have — a Cabinet the operator appoints, an operator's command
capacity, or the `AuthoritySystem` distribution (§2d).

**Emergency powers** additionally cost approval −6 (elective) or −3
(non-elective) and stability −2 on declaration, grant an immediate +2 CP, and
drain approval −0.8, unity −0.4 and legislative support −1.2 every month they
remain in force. They do not survive a change of leadership or a coup.
While in force they also lower Liberty blocs' civic-policy goodwill target by
10, through the ordinary monthly drift (§2g below), not an immediate penalty.

**The monthly +2 CP is derived, not baked in.** It is computed each month in
`TurnManager` from `government.emergencyPowers`. An earlier build added it
permanently to the CP baseline, letting a player stack declarations for unlimited
capacity — do not reintroduce that pattern.

## 3. National priority

`Security`, `Prosperity`, `Influence`, `Cohesion`. Sets a multiplier applied to
every **delegated** official's monthly work — Directed as well as Autonomous; only
Direct Control escapes it (`PriorityMultiplierFor`):

```
favored pillar ×1.35, all others ×0.85
Security → Military + Intelligence · Prosperity → Economy
Influence → Diplomacy · Cohesion → Government
```

Directed officials are included deliberately: the operator's directive decides
*how* a ministry works, but the government's stated priority still decides what it
is resourced to do, so paying Influence does not buy a way around the
administration (spec 15 §7).

Leaders arrive with their own priority; changing it costs 3 PC. AI governments
invest along their own leader's priority (spec 06 §5), and a new administration
rolls a fresh one at random — which is how a foreign state's whole strategic
posture can shift without anyone acting against it.

## 4. Political condition

Monthly, for **every** country:

```
approvalPull   = growth × 0.5 − max(0, inflation − 4) × 0.45
               − max(0, unemployment − 7) × 0.25 − warExhaustion × 0.03
approvalTarget = clamp(50 + approvalPull × 6 + ApprovalShiftFor(posture))
approval      → approvalTarget          (rate 0.06)

stabilityTarget = clamp(38 + governmentPillar × 0.30 + approval × 0.20
                        − warExhaustion × 0.25 − 22 if in civil conflict
                        + StabilityShiftFor(posture))
stability     → stabilityTarget         (rate 0.05)

unityTarget   = clamp(42 + governmentPillar × 0.20 + stability × 0.25
                      − warExhaustion × 0.20 + UnityShiftFor(posture))
unity         → unityTarget             (rate 0.04)

brokeredSupport ×= 0.94
if restrictive: spend 0.45 PC

governmentPillar = Growth.Apply(governmentPillar, (leaderCompetence − 50) × 0.004)

elective:     legislativeSupport → approval × 0.7 + governmentPillar × 0.3
                                   + brokeredSupport × 0.45                   (0.08)
non-elective: eliteCohesion → 40 + governmentPillar × 0.35 + stability × 0.25
                              − warExhaustion × 0.15 + brokeredSupport × 0.45
                              − 8 if emergency powers                          (0.06)
```

**Stability and unity had no restoring force at all** — the only two political
stats in the simulation without one, and the fifth instance of this bug class. A
dozen flat drains pushed them down (war, occupation, inflation above 12, civil
conflict, emergency powers, lost territory, total mobilization) and they recovered
only through discrete events. **National unity had exactly one repeatable player
source in the entire game** (`PublicMessagingBy`, +1.5×eff). A country that had a
bad decade could not be governed back to health.

Both now sit at a level the state's own institutions and standing can hold. Note
the coupling: unity is anchored partly on stability, so the two move together the
way they should, and the government pillar raises the ceiling on both — which is
what makes institutional reform worth its 6 PC beyond the one-off +3.

Test: `GovernmentVerbTests.StabilityAndUnityCanBeGovernedBackFromABadDecade`, and
its mirror `NeitherRunsAwayUpwardEither` — a restoring force has to *hold a level*,
not simply point the ratchet the other way.

**Approval approaches a level; it does not integrate a rate.** Accumulating
`approvalPull` each month drove a healthy economy's approval to 100 in about five
years and pinned it there for the rest of the save. That made elections a
formality, and — because the AI scores `ConsolidateHome` on `(50 − approval)` —
stopped every AI government from ever attending to its own domestic condition
again. Test: `BugRegressionTests.Approval_DoesNotSaturateInAHealthyCountry`.

**Leader competence routes through `Growth.Apply` for the same reason.** It was a
raw monthly addition — the last un-damped capability ratchet in the simulation —
worth roughly **17 points of the Government pillar per decade** for a leader at
competence 85, with nothing tapering it as the pillar climbed.

It survived that long because it was *nearly* invisible: an AI state had little
else raising its Government pillar, so the ratchet stayed under the long-run
saturation invariant on its own. It crossed the moment foreign cabinets started
contributing to the same pillar every month (spec 15 §1) — a change made
elsewhere, in another system, that turned a latent bug into a failing test. Caught
by `WorldInvariantTests.CapabilityDoesNotSaturateOverALongSave`.

`Growth.Apply` damps gains and **leaves losses fully felt**, which is the right
asymmetry here: a competent leader's institutional gains taper as the pillar
approaches the ceiling, and a leader below competence 50 still erodes institutions
at the undamped rate. Incompetence is not subject to diminishing returns.

### Peacetime recovery

While the country is **not** at war:

```
warExhaustion −= 0.7   (floored at 0)
warSupport    → 50     (rate 0.02)
```

War exhaustion previously had exactly one decrement in the entire simulation
(−10 when a confrontation closed), so it ratcheted up permanently — and because
it feeds `RegimeSystem` coup pressure, any state that fought two wars was locked
into a coup cycle for the rest of the game. War support was drained by every war
month and never restored, so after roughly a war and a half an AI government
could no longer escalate (gate: > 35) and sued for terms the moment it opened
anything (gate: < 20); the world stopped being dangerous. Tests:
`BugRegressionTests.WarExhaustion_FadesInPeacetime` and
`WarSupport_RecoversInPeacetime`.

## 5. Elections

Run when `date >= nextElectionDate`, elective systems only. Centered on 50 so an
average government in average conditions is a genuine coin flip:

```
score = 50
      + (approval − 50) × 0.8
      + growth × 3.0
      + (unity − 50) × 0.15
      + (legislativeSupport − 50) × 0.2
      − max(0, inflation − 4) × 1.8
      − warExhaustion × 0.3
      − termsServed × 6                ← anti-incumbent fatigue
      + random(−14, +14)
incumbent holds if score ≥ 50
```

The next election is scheduled `termLengthMonths` out before the result is
applied, so the calendar keeps running whoever wins.

**Term limits fire first**, before the score is applied: a term-limited leader
leaves office however popular they are, though a winning score means their
faction retains power (the successor's `faction` is set to `GOVERNING PARTY`
rather than `OPPOSITION`). This is what guarantees turnover in a well-run country
— without it, a strong player re-elected the same leader for decades.

A retained incumbent gets `termsServed++`, legislative support +6 and approval +4.

## 6. Succession (non-elective)

```
risk = max(0, leaderAge − 68) × 0.004
     + (35 − eliteCohesion) × 0.003 when cohesion < 35
```

Rolled monthly. A succession with `eliteCohesion < 45` is **contested**:
stability −12, unity −8, and cohesion resolves upward by +15 afterward. The
traffic is **PRIORITY if it is our own government and WIRE otherwise** — a
leadership fight abroad is news, not a decision. It was not player-gated at all
before, so a succession anywhere in the world raised FLASH on the operator's own
briefing (spec 09, `PartialSystemsTests.ForeignLeadershipStruggle_DoesNotFlashOurBriefing`).

## 7. Administration change

New leadership always brings: a new name drawn from the country profile, a new
faction string, a **new national priority chosen at random**, competence 40–85,
age 48–70, a fresh honeymoon (approval 55–65, legislative support 58–70 if
elective), and emergency powers cleared.

And, for **every** country, a Cabinet reshuffle:
`ReshuffleCabinetOf(state, country, rng)` replaces each of the five offices on a
coin flip with a new appointee (competence 40–78, loyalty 35–80, risk 20–80,
trust 50–62) drawn from **that country's own** name pools, reverting to Autonomous
mode with directives cleared. It returns without doing anything for a country with
no authored profile.

That routine was player-only. Making it actor-generic is what stops a foreign
minister appointed at world creation from still holding office fifty years and six
elections later — a government whose personnel never change reads as a frozen
world, and once foreign cabinets actually run their countries (spec 15 §1) it
would also mean a rival's competence was fixed for the length of the save.

**The chronicle entry is filed against the country it happened in:**
`"Cabinet reshuffle in {displayName}: N office(s) changed hands."` against
`country.id`. It used to be filed against `state.playerCountryId`, which was
harmless for exactly as long as this only ever ran for the player and began
writing **every foreign reshuffle in the world into our own national record** the
moment it went generic. Test:
`ForeignCabinetTests.AForeignReshuffleIsRecordedInTheirHistoryNotOurs`.

That is the characteristic hazard of making a routine actor-generic, and it is
worth naming: the loop over officials was written correctly, and the bug was in a
line that never mentioned an official at all. **When a player-only routine is
generalised, audit every identifier it writes, not only the ones it reads** — a
hardcoded `playerCountryId` in a chronicle entry, a notification, a log tag or a
lookup key is invisible while there is only one actor and wrong for fifteen of
sixteen the moment there is not.

For the player's country additionally: `administrationsServed++` and
`AuthoritySystem.ClearGrantedAuthority` — delegated authority was personal to the
government that granted it (§1a).

**Capabilities are inherited unchanged** (GDD §13) — a new administration does
not re-arm or re-industrialize the country. There is a test asserting this
(`GovernmentSystemTests.NewAdministration_InheritsCapabilitiesUnchanged`).

**The player persists.** Operator XP, level, skill points and unlocked skills
survive every administration change; you are the operator, not the officeholder.

### 7b. …and the game has to *say* so

Reported from play as a straight objection: *"I am playing as the US and someone
else was elected, however I still control the country? This makes no sense."*

The premise is right — GDD §13 and line 203: *the player is the persistent
strategic operator/advisor, not necessarily the elected leader; administrations
come and go while the save continues.* What was wrong is that the game stated it
in **one trailing clause of a filterable notification**. The NEW ADMINISTRATION
item carried `desk: ReportingDesk.Government`, so a mediocre minister could strip
its urgency and a poor one could drop it outright (spec 15) — and the same event
silently revoked every granted authority (§1a). The operator could therefore meet
a change of government as an unexplained change of name on a readout.

Three changes, no simulation change:

1. **`desk: ReportingDesk.Command`.** News about *this office* is not something
   the government of the day forwards at its discretion — the same exemption
   that covers the operator's own orders. Still `Priority`, not `Flash`: §28.2
   reserves FLASH for a turn that cannot be taken without deciding, and nothing
   here needs answering. The item now names what changes (priority, cabinet,
   granted authority) and what does not (the post, the record, the skills, and
   every authority the office holds in its own right). Guarded by
   `GovernmentSystemTests.NewAdministration_IsCommandTrafficAndCannotBeBuried`.
2. **One handover, one item.** The term-limit branch filed its own notification
   *after* `InstallNewLeadership` had already filed one, so a term-limited
   succession produced two items on the operator's desk for one event. The
   foreign copy is unchanged; the player copy is now only the one that explains
   itself. `GovernmentSystemTests.ATermLimitedHandoverIsReportedOnce`.
   The incumbent-retained item is also Command-desk for the player's own
   country, and says the authority position is unchanged.
3. **The panel and the tutorial say it too.** `GovernmentView`'s ADMINISTRATION
   block now opens with `THIS OFFICE: PERMANENT STRATEGIC OPERATOR — not
   elected, not replaced` and relabels the leader row `HEAD OF GOVERNMENT`, and
   the tutorial's **first** step is YOUR POST. A player who thinks they are the
   head of state reads the next election as the end of their game and reads
   surviving one as the simulation being broken; every other step is confusing
   until that is settled.

## 7a. Coups and regime change (`Core/RegimeSystem.cs`, GDD §22)

Three rules govern this system:

1. Coups **emerge from accumulated conditions**, never from a card or a button.
2. Foreign action **accelerates, it does not manufacture**.
3. Catastrophe creates **a new gameplay state, not a game over**.

Order each month, per country: civil conflict tick → loyalty → conspiracy → coup
check.

### Military loyalty

```
target = 45 + governmentPillar × 0.30 + stability × 0.20
       − warExhaustion × 0.25 − max(0, inflation − 8) × 1.2
       − 8 under emergency powers − 15 during civil conflict
approach 0.06
```

Starts at 65.

### Conspiracy

```
pressure = max(0, 50 − stability)        × 0.020
         + max(0, 45 − approval)         × 0.015
         + max(0, 50 − backing)          × 0.020
         + max(0, inflation − 8)         × 0.030
         + max(0, unemployment − 12)     × 0.020
         + warExhaustion                 × 0.010
         + max(0, 55 − militaryLoyalty)  × 0.025
         + max(0, 45 − unity)            × 0.010

vulnerability = clamp01(pressure / 1.2)
foreign       = Σ(uncompromised hostile Political network penetration × 0.004) × vulnerability
recovery      = 1.2 + stability × 0.02 + loyalty × 0.015   (only when pressure < 0.25)

conspiracy += pressure + foreign − recovery
```

`backing` is legislative support or elite cohesion by type. The deepest
uncompromised political penetration is recorded as `conspiracyBackerId`, cleared
when conspiracy falls to 5 or below.

The `vulnerability` multiplier is the load-bearing part: against a stable,
legitimate state it collapses to near zero, so **twenty years of maximum
subversion cannot topple a well-governed country**
(`RegimeSystemTests.ForeignSubversion_CannotTopplaAStableState`).

### The attempt

Possible only above `CoupThreshold` (60).

```
chance   = (conspiracy − 60)/320 × (1 + max(0, 60 − loyalty)/80)
plotters = conspiracy × (1.3 − loyalty/100)
regime   = loyalty × 1.0 + governmentPillar × 0.6 + stability × 0.4
         + 10 under emergency powers
succeeds if roll < plotters / max(1, plotters + regime)
```

**Loyalty is the counterweight, not a small modifier.** The previous formula —
`plotters = conspiracy + (100 − loyalty) × 0.5`, `regime = loyalty × 0.8 +
governmentPillar × 0.5 + stability × 0.3` — left a maximally loyal, stable,
well-governed state (loyalty 95, stability 90, government 95, conspiracy 95)
losing roughly **two attempts in five**, which flatly contradicts GDD §22's rule
that a stable state cannot simply be toppled. Under the current formula a
conspiracy inside an army that will not move is a conversation, not a coup: at
loyalty 100 the plotters retain only 30% of their organizational weight. Test:
`RegimeSystemTests.AStrongState_DefeatsTheOverwhelmingMajorityOfAttempts` runs 60
seeds and requires survivals to outnumber successful coups more than 4:1.

**Failure** triggers a purge: loyalty +15, conspiracy hard-reset to 15 and its
backer cleared, military −8, stability −10, unity −5, intelligence −3 — the
survivors are loyal, and fewer.

**Success** installs a military council:

- Government type becomes `CentralizedRepublic`; term limit and term length go to
  0, legislative support to 0, elite cohesion to 45, emergency powers cleared.
- Loyalty +20, conspiracy reset to 25, `coupsExperienced++`.
- New leader with faction `MILITARY COUNCIL` and a random priority.
- Stability −25, unity −15, approval set to 45, government pillar −10, economic
  confidence −20.
- **Every unbroken mutual-defense treaty they hold is repudiated**, and every
  relationship takes trust −20 plus a −4 memory entry.
- If stability then falls below 25 the state tips into **civil conflict** for
  12–23 months: stability −0.8, unity −0.6, confidence −1.2, economy pillar −0.4,
  military pillar −0.3 and industrial capacity −0.4 per month, resolving with
  stability +15, loyalty +10 and conspiracy −20 — authority restored at heavy cost.

A **foreign-backed** successor gains relations +15 with its sponsor and a +2
memory, but **no alignment shift and no obedience** — it is a sovereign
government with its own interests (GDD §22), and keeps its own AI.

**The Cabinet is replaced wholesale, in whichever country the coup happened.**
`ReshuffleCabinetAfterCoup(state, country, rng)` overwrites all five offices —
competence 35–75, loyalty **55–95** (the new order's own people), risk 30–90,
trust 45–60, all `Autonomous` with directives cleared, names from that country's
own pools. A junta installs its own people wherever it takes power, and this is
the single most visible thing a coup does to a state's capability. While foreign
governments had no cabinets it did not happen to them at all, so the most
consequential domestic event in the game left a rival's ministries untouched. Test:
`ForeignCabinetTests.AForeignCoupReplacesThatCountrysCabinet`.

For the player's country the save additionally continues:
`administrationsServed++`, and **the operator keeps their post and all
progression**.

### Player levers

`SecureMilitaryLoyalty` (5 PC) buys the officer corps with promotions and
budgets: loyalty +12, conspiracy −8, approval −3, government pillar −1.

**Detection**: a domestic plot is reported only with counterintelligence ≥ 35 and
conspiracy ≥ 55 — blind services see nothing coming. A foreign one needs a
Political estimate at Moderate confidence or better, and conspiracy ≥ 45.

## 2e–2g. Government content (spec 25 Tranche D, 2026-08-28)

**Status: as-built.** `Tests/EditMode/CorruptionTests.cs`. The pillar had
plenty of verbs — 22 of them — and the *world behind them* was one leader, one
support float and one cohesion float. These three give it a shape.

### 2e. Corruption

`GovernmentState.corruption`, 0..100.

`DistributePatronage` has said in its own comment since it shipped that it
"hollows the state out if it becomes the habitual instrument", and **nothing in
the simulation recorded that it had** — the cost was 1.4 points off a pillar
that regrows, so the habit was effectively free. Separately,
`OppositionTheme.Corruption` existed and was proxied by a weak government pillar
plus conspiracy plus poor elite cohesion: a measure of the state being *feeble*,
which is not the same as the state being *bought*. A capable, united,
unplotted-against government running entirely on favours could never draw that
campaign.

```
patronage        +7
inquiry          −11        (more than one round, less than two)
decay            0.20 + corruption × 0.012      per month
RevenueLeakage   min(0.33, corruption / 300)
```

**Proportional decay**, so every level has a resting point — the
`publicGrievance` lesson applied before it could become the same bug. A flat
rate means any sustained inflow above it pins at the cap forever.

Four read sites, so it is not decoration: the opposition's case (×0.75, ahead of
the three proxy terms), the **stability target** (−0.12/point), treasury revenue
leakage, and the existing pillar hit. The money one is the most concrete — an
operator feels it without being told.

Relief is deliberately smaller than two rounds of patronage, so buy-then-audit
is a losing cycle rather than a way to launder support into permanence.

### 2f. Constitutional change

`GovernmentType` decides how power works — elective vs internal succession, term
limits, what emergency powers cost, whether an early election is possible — and
it was **fixed for the whole of a decades-long save**. The pillar's largest
missing verb, and the natural large purchase for a PC economy whose only sink
above 7 was `ConsolidateAuthority` at 12.

```
opening      6 PC
upkeep       1.2 PC every month it runs
duration     30 months
threshold    60 support at the end
drift/mo     (backing − 50) × 0.06 − unrest × 0.020 − corruption × 0.015
```

**The route differs by what you already are**, which is §13's requirement that
the type change *how power works* rather than hand out a modifier: an elective
system needs the chamber (45 legislative support to begin), a non-elective one
needs the elite (45 cohesion).

**Genuinely losable, and the loss is the point.** Running out of PC collapses
the process; ending below the threshold abandons it at −6 approval and −7 elite
cohesion, because it was public and it told everyone what this government
wanted. Succeeding resets the old settlement: emergency powers lapse, brokered
support halves, and becoming elective schedules an election that did not
previously exist. Corruption feeds the drift, so a state that runs on favours
finds it harder to rewrite its own rules.

### 2g. The faction ledger

`GovernmentState.factions` — three named blocs with a `share`, a `disposition`
and an `OppositionTheme`.

**A lens on `legislativeSupport`, not a replacement for it.** Spec 25 proposed
making that field the derived total of the blocs; it has **36 sites across
eleven systems, eight of them writes** (elections, coups, inquiries, emergency
powers, the assessment, authority checks). Turning it into a computed sum would
have broken every writer for no behaviour — a large diff in exchange for a
data-model preference. The ledger instead contributes a term to the *support
target*, exactly as `brokeredSupport` already does.

```
FactionSupport = Σ (disposition − 50) × share / Σ share × 0.4,  clamped ±20
```

Zero at indifference. Initial dispositions are 58/48/50 with deterministic
jitter, so the initial contribution is small, not necessarily zero.

What makes it a decision rather than a readout is that **instruments reach
specific people**. Patronage courts the hardship bloc, because money speaks
loudest where people are short of it. Courting a named bloc is worth 9 to that
bloc; the undirected bargain is worth 2 to everyone. Knowing who you are talking
to is the value. Blocs drift toward their held-policy resting goodwill at
0.02/month (normally indifference; Liberty exceptions below), so a
coalition is *maintained* rather than bought once — `brokeredSupport`'s rule
applied to people.

Seeded lazily by `EnsureFactions` and deterministically from the country id, so
an old save gains a coalition at its first monthly update or bargain — **no
migration** — and gains the same one every time. `FactionsFor` previews that
same seed without persisting it; opening GOVERNMENT never establishes a ledger.

**Playable ledger (roadmap #23, first slice).** GOVERNMENT now shows each named
bloc, its existing power share, disposition, concern and the ledger's current
contribution to the legislative-support or elite-cohesion target. Each bloc has
a COURT control through the existing `GameController.CourtFaction` path: normal
Government authority, 2 PC, one initiative, nominal 10 XP under the repetition
rule, and autosave. The chosen theme must exist before any spend; an absent
theme no longer silently buys an untargeted bargain. Authority and affordability
refusals are printed by the common terminal gates. The notice identifies the
bloc, rather than claiming only a generic chamber bargain.

The existing consequences are unchanged: +9 disposition to the selected bloc,
other dispositions untouched, plus ordinary `brokeredSupport`; general bargaining
still gives +2 to every bloc. Both routes consume the same PC. Shares never
change through courting. Disposition drifts toward its policy target monthly; backing affects
the support target, not an immediate grant of votes or authority.

The ledger persists across Cabinet/leader replacement and constitutional changes
under the existing rules; there is no new reset or migration. Empty ledgers seed
from the government type at first use. These are constituencies, not officials
or the opposition case: courting neither settles a grievance nor changes policy.
The first slice exposed existing politics. The policy slice below adds two
opposed reactions; the influence slice then adds gradual power shifts.
Regime-aware renaming remains outside them. Roadmap #23 is partial.

**Patronage and inquiry reactions (#23, second slice).** Both actor-generic
verbs now reach the existing ledger, after their normal affordability gates.
No new action, currency, persistent field, AI selection rule or reward is added.

| Existing bloc concern | Patronage | Inquiry removing 11 corruption |
| --- | --- | --- |
| Hardship | +7 disposition | −4 disposition |
| Liberty or Corruption | −5 | +4 |
| Drift, War, other | 0 | 0 |

Patronage rewards recipients of favours at the expense of clean-government
constituencies. An inquiry removes those favours, reversing who approves.
The inquiry response scales by `min(11, max(0, corruptionBefore)) / 11`:
5.5 corruption buys half the response, zero buys none. Its other established
institutional effects and costs still apply at zero corruption. Each disposition
clamps to 0–100. The rule uses persisted concerns, not current regime labels;
every matching bloc reacts, and an absent concern does **not** redistribute
its reaction to unrelated blocs through the generic courting fallback.

The panel uses the same response functions and shows the clamped change at
current conditions, without seeding a ledger. Successful player notices name
each affected bloc and the applied movement, or explicitly say unchanged.
Inquiries now establish an empty ledger too. Shares/names stay fixed during the action; changes
persist through the existing save. The monthly 2% approach to the policy target and paid
named courting remain recovery routes. An inquiry is not a free reversal:
one full patronage/inquiry pair costs 5 PC and 200 treasury and leaves integrity
disposition down 1, hardship up 3 (before clamps); after a clean-state patronage,
only 7/11 of an inquiry response is available.

Foreign governments use these same verbs in their existing consolidation path,
so their backing targets can change and unattended trajectories need not match
the previous slice. Exact magnitudes are authored initial tuning, not certified
balance. This policy slice did not implement broader reactions, shifting shares
or regime-aware names; see the subsequent influence slice below.

**Condition-driven influence (#23, third slice).** Shares now evolve monthly;
goodwill and power are deliberately different. Courting changes disposition,
not the share target. No new command, reward, budget, field or migration exists.

Each bloc has an authored founding weight: Drift .45, Liberty/Corruption .30,
Hardship .25. Other concerns use neutral .25. A bloc's raw target weight is
`foundingWeight * (1 + pressure)`, with pressure bounded to 0–1:

- Hardship: `clamp((50 - livingStandards) / 50, 0, 1)`.
- Corruption: `clamp(corruption / 100, 0, 1)`.
- Liberty: 1 under Restrictive civic posture, otherwise 0.
- Drift, War and unrecognised concerns: 0 in this deliberately limited slice.

Normalize all raw weights to one common pool. Every month each share closes
2% of the gap to its target. Current shares are also normalized (nonnegative
weights; an all-zero ledger starts at targets) so even a non-unit legacy ledger
cannot create power. Targets never use today's shares as founding weights:
that would compound influence until one bloc owned everything. Duplicate
concerns each receive the corresponding authored weight, and list order and
names do not determine targets. Seeding remains the existing 45/30/25 balance;
viewing an empty ledger computes the same targets without persisting it.

For the ordinary three-bloc ledger, maximum hardship alone changes the target
45/30/25 to 36/24/40; maximum institutional pressure alone gives about
34.62/46.15/19.23. Shares approach rather than instantly take these values.
Removing pressure restores the founding target, including after save/load.
No bloc can disappear under the positive target weights, and increasing one
share reduces the rest. This is influence inside government, **not** a forecast
of election seats or a new public-opinion model.

The existing government update calls the rule once, after domestic conditions
update and before `FactionSupport` feeds that month's backing target. Both
player and foreign governments use it. No AI-specific caller or bonus exists.
Goodwill, names, authority and rewards are not changed by the share update;
the ordinary goodwill decay still runs separately.

GOVERNMENT shows current share, the target if current conditions persist, the
relevant own driver and whether extra pressure is present. It also explains that
other blocs' pressure can dilute this bloc, and that recovery moves shares:
the own driver is not a claim about the sole cause of its movement. It does not promise
that next month's conditions will stay unchanged. A player-only Wire notice
records before/after shares and contemporaneous targets/drivers at application
when movement is visible at two decimal percentage precision. Small movements
that do not change that displayed precision do not generate traffic. This is
ordinary Government-desk reporting, not a new unfilterable interruption.

Numbers are initial authored tuning, requiring independent Unity balance and
device verification. Bloc continuity survives regime changes (naming below); broader
policy reactions beyond the civic slice below remain deferred. World trajectories
can change because actual backing targets now read evolving influence.

**Held civic policy (#23, fourth slice).** Liberty constituencies now judge
the policy held over time, using the existing monthly disposition drift:

| Persisted concern | Open target | Standard target | Restrictive target |
| --- | --- | --- | --- |
| Liberty | 65 | 50 | 35 |
| All other concerns, including Corruption and unknown values | 50 | 50 | 50 |

Every month closes 2% of the gap from current disposition to that target,
replacing (not stacking with) the old return-to-50 step. At 50, one Open month
gives 50.3 and one Restrictive month 49.7. Holding a posture converges instead
of accumulating an unbounded reward or penalty. Standard recovers toward 50;
Open recovers toward 65. A courted Liberty bloc at 74 drifts to 73.82 under
Open: policy does not preserve purchased goodwill above its resting level.
These numbers are initial authored tuning, requiring native balance review.

The persisted concern determines the response, not the bloc name, share, regime
or whether the country is the player. Every duplicate Liberty bloc responds.
A non-elective country retaining a Liberty constituency still has that
constituency; a Corruption concern is not silently treated as pro-repression.
No identity, concern, share rule, new field or migration is introduced.

The call remains once in the existing government month, after lazy seeding and
before backing is calculated. Monthly drift grants no XP, initiative or new
cost. The existing civic-order costs, rewards, authority, AI caller and autosave
are unchanged. Switching posture transfers no goodwill or shares immediately
and does not establish an empty ledger; switching repeatedly without time
passing buys no bloc goodwill. The posture held at monthly resolution is the
one judged, as with its existing effects; no within-month policy history is
invented. Ordinary action rewards for switching remain the pre-existing rule.

GOVERNMENT shows each bloc's three possible resting targets, its current one
and the 2% rate before ordering. The successful civic-order notice explains
the gradual Liberty response and no immediate transfer. Reads stay pure and
do not seed old saves. Existing influence notices now explicitly explain
shared-pool dilution, rather than suggesting an absent own pressure caused it.
No new recurring goodwill notification is added.

Native Unity balance and device verification remain separate gates. Reactions
to other policy instruments remain outside this slice; roadmap #23 is still partial.

**Regime-aware identities (#23, fifth slice).** The existing ledger survives a
constitutional settlement or successful coup, including its object order,
concerns, shares and dispositions. Only recognised authored names change:

| Persisted concern | Elective institutions | Non-elective institutions |
|---|---|---|
| Drift | THE CHAMBER MAJORITY | THE PARTY APPARATUS (dominant-party), THE STATE APPARATUS (centralized), THE ROYAL COURT (monarchy) |
| Liberty | THE REFORM BENCH | THE REFORM CIRCLE |
| Corruption | THE OVERSIGHT BLOC | THE SECURITY ORGANS |

THE PROVINCES and other concerns keep their names. This is institutional
vocabulary, not a new faction or a claim that all non-elective regimes are
identical. A retained Liberty constituency never becomes a Corruption bloc:
policy reactions still follow the persisted concern. Custom names and names
not matching that concern's authored aliases are untouched.

Both successful transition paths refresh immediately; failure does not. An
old save with obsolete authored names is repaired on its next `EnsureFactions`
mutation (monthly update, bargain or bloc reaction), never by `FactionsFor` or
opening a view. An empty ledger stays empty at transition and is seeded later
using the current institutions. Renaming draws no randomness and has no cost,
reward, backing change, new field or save-version bump. Repeated repair is inert.

Each actual rename batch appends one secret political chronicle entry listing
old → new names, leaving earlier entries intact; only the player receives its
Government-desk Wire notice. Foreign internal identities are not broadcast.
The notice explicitly distinguishes renaming from changed concerns, goodwill
or influence. Existing panels, COURT controls and action receipts read the
persisted names. Hardware layout remains a device gate; #23 remains partial.

**Exact named courting (#23 correctness follow-up).** Each COURT control now
captures its ledger index, rather than passing only its concern. Two blocs
sharing a theme can be courted independently, including a smaller constituency;
the receipt identifies the actual selected entry. Existing theme-based callers
still choose the largest matching bloc, and general bargaining still raises
every disposition by 2. The exact route shares the existing bargain application:
+9 clamped disposition on that entry only, unchanged shares, normal diminishing
brokered support, 2 PC, one initiative, nominal 10 XP and player autosave.

Indices are validated before constitutional approval, spending or lazy seeding
at the controller and before spending/seeding at the actor-generic entry point.
An empty ledger uses the same deterministic preview and seed order. This is a
current-ledger index, not a new persistent ID: normal production paths retain
ledger order. The UI captures a separate index per button, not the advancing
loop counter. No policy weight, drift, cost, reward or AI choice was retuned.
Duplicate-theme targeting is closed; the other recorded review notes remain
separate, as do device testing and any future multi-seed policy-tuning gate.

**Held emergency authority (#23, next policy slice).** Liberty blocs judge
extraordinary authority for as long as it is held, not just on the declaration.
The existing `FactionDispositionTarget` combines civic posture with the existing
`emergencyPowers` flag:

| Liberty resting goodwill | Open | Standard | Restrictive |
|---|---|---|---|
| Ordinary authority | 65 | 50 | 35 |
| Emergency authority | 55 | 40 | 25 |

The ten-point reduction is authored initial tuning, smaller than the existing
fifteen-point civic-posture step; it requires multi-seed sustained-policy review,
not merely the standard bots' balance report. Other concerns, including
Corruption and unknown values, still target 50. No bloc is presumed to favor
emergency rule from its name or government type. Duplicate and retained Liberty
blocs obey the same rule, including in foreign/non-elective governments if the
flag is present. Declaring emergency powers remains an operator-only instrument;
this adds no AI caller.

This replaces the resting target in the existing 2% monthly drift, never stacks
a second drift or a flat monthly subtraction. A bloc at 50 under Standard
emergency rule moves to 49.8 in the first month. Repeated months approach 40
rather than draining forever. Bought goodwill above the target still fades.
The government month drifts before decrementing emergency duration, so the last
authorized month counts. Expiry, leadership replacement or constitutional change
clears the flag through existing paths; the next drift uses the ordinary target.
Lost goodwill does not instantly return. No new field, counter, migration or
pipeline hook is added; existing saves already preserve the required state.

Declaration changes no bloc immediately and does not seed an empty ledger. It
retains its existing PC cost, CP benefit, authority, duration, initiative, XP and
autosave. Civic changes while an emergency is active report the combined target.
The bloc panel and emergency control explain the effect before ordering;
declaration and lapse notices distinguish a restored target from restored
goodwill. Names, concerns, influence rules and shares are not directly changed.
Changed goodwill can affect backing and later world outcomes through existing
systems.

Author-side comparison uses five seeds, three held civic postures, and never /
one declaration / affordable renewal for 120 months followed by 60 recovery
months on both base and feature. It is not a native Unity certification; Claude
must independently verify the committed tree. Hardware testing is deferred by
Kareem until #23 is fully implemented. This slice alone does not close #23.

### 7b. Conspiracy has a resting point (core stability repair, 2026-09)

`RegimeSystem.UpdateConspiracy` decayed conspiracy *only* below a pressure of
0.25; above it the level was a pure accumulator with two sources (its own
pressure terms and unrest above 55) and no sink short of a coup. Measured worlds
reached ~100 coups in forty years, and every second coup refilled from 25 in
under three years because the coup itself degraded the terms that feed it. Now
`recovery = ConspiracyBaseDecay 0.25 + conspiracy × ConspiracyProportionalDecay
0.010` every month, plus the old bonus below 0.25 — the shape grievance,
corruption and food already use. A plot at steady pressure settles where its
pressure holds it: moderate, steady failure (stability 40, approval 35) rests
below the coup threshold; severe failure still reaches it within four years.
Coups over 40 years fell from 95–114 to 24–48 on the audit's seeds.

## 8. Extension points

- **Adding a government verb.** Write the `…By(state, countryId)` implementation,
  charge it through `SpendPoliticalCapitalBy`, add a thin player wrapper that
  delegates and then calls `ProgressionSystem.RecordInitiative` **and** awards XP
  — omit the initiative call and the pillar quietly grades below inaction
  (spec 07). Then give it a `GameController` wrapper behind `MayCommand`, a row
  in `ActionCatalog`, a button in `GovernmentView`, and a caller in
  `AISystem.ConsolidateHome` unless it is genuinely operator-interface.
  If it moves `legislativeSupport`, `eliteCohesion`, `stability`, `unity` or
  `approval`, **move the target, not the value** — all five drift.
- **Parties and factions** — leader-faction arithmetic (§2b-1), the playable
  bloc ledger, patronage/inquiry reactions, held-civic-policy goodwill and
  condition-driven influence and regime-aware names (§2g) exist. Further policy reactions extend them,
  rather than requiring a parallel faction model.
- **Secession and state dissolution** — civil conflict currently degrades a state
  but never splits it. Border change (GDD §16) is the missing piece.
- **Hereditary succession.** Saudi Arabia is seated as a `Monarchy`, so the type
  *is* exercised — but it runs the same age-and-cohesion succession as a
  dominant-party state, with a randomly generated successor. Nothing hereditary
  is modelled.

## 9. Open questions / PLANNED

- **Five types, two code paths.** Everything outside the §1a authority table
  branches on `IsElective` (plus one `AllowsEarlyElection` check). A monarchy, a
  dominant-party state and a centralized republic run bit-identical succession,
  backing and coup arithmetic. The authority table is currently the only place
  the distinction is real, and it should not stay that way.
- **No candidate pool for appointments.** `DismissOfficial` rerolls the incumbent
  object in place — there is no slate of named officials with known competence
  and loyalty to choose between, so dismissal is a gamble rather than a decision.
  The same is true of a new administration's reshuffle.
- **`AuthoritySystem.CanDirectlyCommand` is never called**, and no view surfaces
  the authority level. The rule is enforced at `CabinetSystem.SetMode` and
  otherwise invisible: an operator learns the shape of their own constitution by
  being refused. That is atmospheric but bad UI.
- **Government play has low decision density** (18 decisions/decade in
  validation). It needs more verbs — budget allocation, appointments beyond
  dismissal, emergency legislation, public inquiries.
- **`warSupport` and `warExhaustion`** live on `CountryState` and are now
  connected to peacetime recovery and to coup pressure, but the fuller
  social-pressure model of GDD §12 — protest, mobilization, conscription
  politics — is not implemented.
- **AI governments run the political economy and now the ministries, but not the
  authority model.** They have elections, succession, coups, political condition,
  Political Capital on the same formula and cap as the player spent on the same two
  actor-generic verbs (§2, spec 06 §5a), and — since foreign cabinets — five
  officials whose competence drives their pillars, reshuffled by every
  administration change and replaced by every coup (spec 15 §1, §2). What they
  still have no model of is **authority**: a structural type changes an AI
  government's succession and its coup arithmetic and nothing about what its
  leadership may personally command. `AuthoritySystem` remains player-only, as do
  the control modes and the six player-only instruments in §2's table — the
  cabinet now exists to act on, but the constitutional layer around it does not.

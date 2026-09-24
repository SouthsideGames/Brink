# 03 — Intelligence System Specification

Source: `Core/IntelligenceSystem.cs`, `Data/Intelligence.cs`, `UI/IntelReadout.cs`.
GDD §14.

**Roadmap 25 cables.** Named North Atlantic (USA_PRT–GBR_PRT), North Pacific
(USA_PRT–JPN_PRT) and Indian Ocean (IND_PRT–EGY_PRT) connections multiply an
existing uncompromised network's monthly penetration growth by 1.10 when both
endpoints and access are available (spec 02). No network, estimate or confidence
grade is created by a cable. Disruption, missing endpoints or lost access returns
the growth factor to 1; it does not erase accumulated knowledge. Compromised
networks keep their existing recovery path. The same rule applies to AI networks.
This is a bounded communications advantage, not a wiretap-right or guaranteed
intelligence. Endpoint attack/repair/expiry follow the shared connection rules.

## 1. The central rule

**Nobody reads true foreign state — not the player, not the AI.** Observers hold
estimates with a margin and a confidence grade. Any UI or AI code that wants a
foreign country's capability must call
`IntelligenceSystem.GetEstimate(state, observerId, targetId, domain)` or
`UI.IntelReadout`, and must render "NO ASSESSMENT" when nothing has been
collected. Violating this silently breaks the game's core fantasy.

The player's own values are always known exactly. `GetEstimate` returns `null`
when `observerId == targetId`.

## 2. Domains and grades

Domains: `Military`, `Economic`, `Political`, `Diplomatic`. Each maps to a truth
value via `IntelligenceSystem.TrueValue`:

| Domain | Measures |
|---|---|
| Military | `pillars.military` |
| Economic | `pillars.economy` |
| Political | `stability` |
| Diplomatic | `pillars.diplomacy` |

Confidence grades by effective access:

| Access | Grade |
|---|---|
| ≥ 60 | Confirmed |
| ≥ 40 | High |
| ≥ 22 | Moderate |
| ≥ 8 | Low |
| < 8 | None (treated as no usable assessment) |

## 3. Networks and collection

`IntelNetwork` is keyed by `ownerId` + `targetId` with a `focus` domain — the
same structure serves AI observers. Establishing costs 2 CP and starts at 18
penetration; expanding costs 1 CP for +12.

`MonthlyCollection` walks every network in the world. Each network draws from its
own deterministic stream, seeded on `rngSeed`, the month index and both party
ids, so a long run replays identically.

```
penetration += 0.6
             + SkillEffect.CollectionTradecraft        (player only)
             + CAP_SIGINT effectiveness × 1.4          (any state)
             or += 0.4 while compromised (slow rebuild; recovers above 25)

access = penetration × focusFactor
       − targetCounterIntel × 0.55 × (1 + CAP_SECCOMMS effectiveness × 0.5)
focusFactor = 1.0 for the focus domain, 0.45 for incidental collection
access ×= 0.3 while compromised, floored at 0
```

### Estimate production

```
noiseScale = max(1.5, 26 − access × 0.35)
noiseScale ×= 1 − SkillEffect.AnalyticalPrecision   (player only, floor 0.3)
noiseScale ×= 1 − CAP_ANALYTICS effectiveness × 0.3 (any state)
noise      = uniform(−1, +1) × noiseScale × 0.5
reported   = clamp(truth + deceptionEffect + noise)
margin     = noiseScale
```

Margin never reaches zero: no estimate is ever certain.

### Material revisions to the collected picture (remaining core #19)

After a player's network produces a new estimate, a Priority Intelligence report
and a Secret own-country Chronicle entry retain a material revision. Both the
previous and current estimates must have been collected at Moderate or better
in consecutive months. The midpoint must move at least ten points and 30% of
its prior magnitude, and the old/new uncertainty bands must not overlap or touch.
Reports show both dated bands and grades, not true foreign values, and explicitly
do not identify a hidden cause. First collection, overhead-only reporting, stale
gaps, foreign services and repeated collection in the same month produce no
revision. Views remain read-only. No new RNG draw, reward, AI effect, saved field
or pipeline hook is added; normal collection, deception and estimates are intact.
This is a belief-change event, not proof of a secret or a replacement for a
discoverable-finding response lifecycle. The numerical reporting threshold is
an initial notification policy, not a balance certification.

### Roll-up — **depth is a footprint, not a shield**

Each month, before collection, a network may be rolled up:

```
footprint = 0.30 + penetration / 160
exposure  = targetCounterIntelligence × footprint / 100
if not compromised and exposure > 0 and roll < exposure × 0.06:
    compromised = true; penetration ×= 0.35   (PRIORITY notification to the player)
```

A deeper network means more officers, more communications and more chances to be
caught, so **penetration raises the odds of being rolled up and never lowers
them**. At 18 penetration the footprint is 0.41; at 100 it is 0.93 — a mature
network is more than twice as exposed as a new one against the same service.

This inverts what an earlier build did, which was
`exposure = (counterIntelligence − penetration × 0.5) / 100` — penetration as
protection. That formula makes exposure *negative* the moment a network exceeds
twice the target's counterintelligence, at which point the roll-up branch is dead
code and the network can never be lost. Networks reliably crossed that line
within about a decade, so **fog of war simply ended in the late game**: every
mature save converged on perfect mutual information, which is the one thing this
system exists to prevent. Do not reintroduce it. A network is protected by
`Compartmentation` and by the target being weak, never by being large.

### Staleness

Estimates older than one month widen by 0.4/month (cap 45) and drop one
confidence grade after six months, down to `Low`. Stop collecting and your
picture decays.

## 4. Deception (GDD §14)

`CounterIntelState` per country: `counterIntelligence`, `deceptionStrength`,
`deceptionBias` (+1 overstate, −1 understate), `deceptionDomain`.

```
if deceptionStrength > 0 and domain == deceptionDomain:
    if access − deceptionStrength × 0.6 < 0:
        reported += bias × (deceptionStrength/100) × 18
        estimate.deceived = true
```

Weak access against a strong program is fooled; deep penetration sees through it.
Because AI observers use the same path, **the player's deception genuinely
misleads the AI** — there is a test asserting this in both directions.

Both defensive values decay monthly: deception −1.2, counterintelligence drifts
toward a baseline of `25 + intelligencePillar × 0.45` (±0.8 down, ±0.4 up).

## 5. Estimating a garrison — `TryEstimateGarrison`

The military map needs to show what a foreign position is defended by, and the
garrison is a per-location true value with no estimate of its own. This lives in
`IntelligenceSystem` rather than in the view precisely because it needs the true
value to distort, and **truth must not leave the intelligence boundary**.

```
if location.ownerId == observerId:
    exact value, Confirmed                    ← our own ground, and ground we hold

estimate = GetEstimate(observer, location.ownerId, Military)
if estimate is null or confidence == None: return false   → view prints nothing

bias      = clamp(0.5, 2.0, estimate.reportedValue / TrueValue(owner, Military))
perceived = clamp(location.garrison × bias)
band      = estimate.margin × 0.6
low, high = clamp(perceived ∓ band, 0, 100)
```

The band is centred on the **distorted** figure, not the real one: the same bias
our headline military estimate of that state carries is applied proportionally to
the garrison, so a rival running a deception program bends the numbers the player
actually plans an assault from. An earlier build centred the band on truth, which
meant deception changed the INTELLIGENCE screen and changed nothing the player
ever acted on. The bias is clamped to 0.5–2.0 so a wild estimate against a
near-zero pillar cannot produce an absurd garrison.

## 6. Covert operations

Cost 2 CP (reduced by `SkillEffect.CovertEfficiency`). Require an existing
network except for `Deception`, which is a domestic program. **`Deception` is
additionally gated on a skill** (`SkillEffect.DeepCoverProgram`, node
`INT_DEEPCOVER`) — legends built over years cannot be improvised.
`CanRunCovertOperation` reports the block so the view can say why.

```
access         = penetration − targetCounterIntel × 0.6
successChance  = clamp01(0.2 + access/90)
exposureChance = clamp01(0.18 + targetCounterIntel/260)
                 × (1 − SkillEffect.Compartmentation)     floor 0.1
```

| Operation | Effect on success |
|---|---|
| Sabotage | Industry health −14, industrial capacity −7, confidence −5 |
| PoliticalInfluence | Stability −7, approval −5, unity −4, plus conspiracy |
| TheftOfPlans | Own penetration +20, own intelligence pillar +2 |
| Deception | Own deception strength +30, sets bias |

`PoliticalInfluence` adds `conspiracyLevel += 6 × clamp01((60 − stability)/60)`
and records us as the backer above 20. Support to opposition **accelerates an
existing plot; it cannot conjure one where there is no grievance** (GDD §22) —
against a stable state the receptiveness factor is zero and the operation
achieves nothing on that axis.

**Exposure is rolled independently of success** — an operation can succeed and
still be attributed. Exposure compromises the network, cuts penetration by 25,
costs 4 diplomacy, and permanently hardens the target's counterintelligence by 6.

**Covert action winds a confrontation up.** `RunCovertOperation` calls
`ConfrontationSystem.AddPressure(state, playerId, targetId, 8)` before the roll —
a no-op unless the two states are already confronting each other. It applies
whether or not the operation succeeds and whether or not it is ever traced back:
the target's services know something is being done to them. Pressure makes the
standoff harder to settle and can eventually escalate it on its own (GDD §18.1,
spec 01 §5).

### The RNG is per invocation, not per month

Both `RunCovertOperation` and `StrengthenCounterIntelligence` seed a **fresh
`Random` on every call**, mixing in `state.NextActionSequence()` — a monotonic
per-action counter persisted in `GameState`:

```
covert sweep : rngSeed × 40503 + monthIndex × 7919 + (int)operation × 131
               + Hash.Of(targetId) × 17 + NextActionSequence() × 104729
CI sweep     : rngSeed × 22801 + monthIndex × 613 + NextActionSequence() × 104729
```

Two operations in one month must be two independent gambles. Sharing one stream
per (month, operation type) made a successful `TheftOfPlans` **infinitely
repeatable**: the second run drew the same roll against a success threshold its
own +20 penetration had just raised, so it could not fail, and the player could
farm a single month. The same defect let a second counterintelligence sweep
replay the identical draws against the networks that survived the first.

`Hash.Of` is used rather than `string.GetHashCode()` so saves cannot silently
fork if the scripting backend's string hashing changes.

Counterintelligence sweeps cost 1 CP, add +8, and give a 40% chance per hostile
network of rolling it up.

## 7. Where intelligence surfaces

- Pillar/foreign readouts: `UI.IntelReadout.ForDomain` → `EST 41–63 CONF MODERATE`
- Enemy garrisons on the military and country maps: `TryEstimateGarrison` (§5)
- Government view: foreign stability shown as an estimate
- AI reasoning: `AISystem.PerceivedStrength` / `EstimateConfidence`
- `AsciiCountryMap.LevelFor` gates how much of a country's interior renders at
  all (spec 09 §10)

## 7a. Institutional hardening (GDD §24.2 amendment)

`CounterIntelState.institutionalHardening` (0..20): lasting procedure learned
from catching foreign networks. **A term in the baseline `counterIntelligence`
reverts to, never a bump to the value** — the monthly reversion (−0.8 toward
`25 + intelligence pillar × 0.45`) erased every bump the catches wrote, so a
decade of caught operations measurably hardened the world by 0.15 points and
the anti-memorisation promise (spec 06 §7b) failed silently the moment the
hotter world crowded the AI's `HardenSecurity` policy out of its action budget.
The value-versus-target trap, tenth instance, in the one system whose whole
subject is learning.

Both catch paths write it (+4 per roll-up, capped 20) alongside their immediate
+6 to the value; it decays ×0.995/month — procedures outlive the scare that
wrote them, fading on a decade scale. Zero on old saves is correct (nothing
caught, nothing learned); no migration. `HardenSecurity`'s priority is also
×1.6 on observed subversion — steep scaling, deliberately **not** a flat floor,
which was tried and hardened everyone against ordinary background suspicion,
raising the no-subversion baseline five points and shrinking the exact signal
it existed to protect. Covered by
`AIStrategyTests.RepeatedlyBeingCaughtSubvertingHardensTheWorldAgainstUs`
(two-seed averaged — in a world that fights its own wars, single-seed decade
comparisons measure the seed's luck).

## 7b. Directed hardening — the world learns to catch *you* (2026-08-28)

§7a was necessary and not sufficient. `institutionalHardening` is **global**: a
service that catches anyone hardens against everyone. In a sixteen-state world
where every government runs networks it saturates from background espionage —
measured at 10.5 of a cap of 20 whether or not the operator ran a single network,
and *marginally lower* in the careless arm than the careful one. The player was
one sixteenth of the signal, so the anti-memorisation property was diluted to
nothing (+0.5 counter-intelligence points against a required +1.0).

The claim was never "the world gets better at counter-intelligence". It is **"the
world gets better at catching *you*"**.

```
DirectedHardening(defender, actor)
    = MaxDirectedHardening (14)
      × clamp01(ObservedSubversion(actor) / 100)
      × (0.35 + 0.65 × defender's estimate confidence about actor)

EffectiveCounterIntelligence(defender, actor)
    = clamp(counterIntelligence + DirectedHardening, 0, 100)
```

Every read that resists a **named** actor goes through
`EffectiveCounterIntelligence` — network roll-up, estimate access, and covert
operation success and exposure. The bare field remains the service's general
condition and is what the operator is shown.

Three properties are load-bearing:

- **Derived, so there is no new save state and no migration.** It reads the
  chronicle through the same function the AI's expectation layer uses, so the
  structural layer and the policy layer cannot disagree about who has been doing
  what.
- **It decays on the same 120-month window.** That is the design, not a
  shortcut: a permanent reputation would make reloading the correct play, which
  this game refuses. Stop, and the world eventually stops watching for it.
- **Symmetric by construction.** It takes a country and an actor id, so it
  protects the operator from a serial infiltrator exactly as it protects the
  world from the operator.

### The reader was blind to most of what it was meant to see

Two bugs underneath, and together they mattered more than global-versus-directed.

1. **A caught covert operation was filed `Publicity.Secret`.**
   `GameState.AddChronicle` defaults to Secret and the call omitted the argument,
   so the single most attributable act in the pillar was recorded as a secret —
   beside a notification whose own text reads *"others have noticed."* Nothing
   reasoning from the public record could see it.
2. **The reader matched only the string `"compromised"`**, which is the
   rolled-up-*network* line. A caught covert operation ("exposed") and a blown
   approach to a foreign official ("was caught") — the two loudest, most
   attributable things an operator can be caught at — taught the world nothing.

`IntelligenceSystem.CaughtMarkers` is now the single list, with
`IsPublicSubversionRecord` the single predicate shared by every reader.
**A new "we were caught" chronicle line must add its marker there and be filed
`Publicity.Public`, or the world cannot learn from it.**

`AIPrediction.ObservedSubversion` is memoised per month on the state instance
(`GameState.subversionMemo`, `[NonSerialized]`), because it is wanted once per
network per month against a record that reaches a few thousand lines. The memo
must **never** be keyed on the seed: two worlds built from the same seed and
played differently are exactly what the counter-play tests compare.

## 6a. Diminishing returns on working a network (2026-08-28)

`IntelNetwork.operationTempo`, 0..100. Every covert operation adds
`TempoPerOperation = 14`; it decays `×0.93` a month and subtracts
`tempo × TempoResistance (0.55)` from the operation's effective access.

A deep network used to be an unlimited supply of sabotage — nothing about the
tenth operation against a state differed from the first except whatever had been
caught in between. This was the oldest open item on the project's own list.

**Distinct from `DirectedHardening` on purpose.** That one is about being
*caught*; this is about being *busy*. A target's services notice a tempo even
when they attribute nothing, so it prices the careful operator too. And it
**fades**, because a penalty with no recovery path is a disqualification rather
than a price: the answer to a burned-out network is patience. Zero on old saves;
no migration.

## 6b. Three appended covert verbs

Ordinals preserved — the enum is persisted in `ActiveCrisis` and in telemetry
buckets.

| Verb | What it does | Attribution |
|---|---|---|
| `Provocation` | Sets the target against a *third* state: relations −12, trust −9, threat both ways, and a diplomatic memory | **0.45** |
| `TechnologyTheft` | Takes a capability the target holds via `TechnologySystem.StealCapability`, at `Stolen` maturity (25 against a programme's 70) | **1.25** |
| `CyberOperation` | −5 health across every sector and −7 confidence, shielded by `CAP_SECCOMMS` up to 60% | **0.55** |

**Deniability is the axis, and it is what makes them worth reaching for.**
`AttributionFactor` multiplies the exposure roll: a provocation is meant to be
blamed on somebody else, and a cyber operation leaves no hand to shake. Both buy
that with a smaller effect — a provocation touches no statistic of the target's
at all, and cyber damage is *functioning* rather than capacity, repaired within
the year. Technology theft is the inverse and the most attributable thing in the
list, because they notice the moment they see us fielding it.

`Provocation` needs an existing quarrel to widen (`ColdestRivalOf` requires
relations below 55). It cannot invent an enemy — the same rule that stops a
covert operation conjuring a conspiracy where there is no grievance.

`StealCapability` still applies the industrial and pillar floors, so espionage is
a shortcut through the *years*, never through the prerequisites.

## 7c. The mole hunt

`MoleHuntBy(state, actorId)`, 2 CP, actor-generic. Finds the deepest
uncompromised foreign network inside us at
`0.20 + counterIntelligence/100 × 0.55 + penetration/260` — a deep network is
*easier* to find, the same footprint reasoning the monthly roll-up uses.

**A hunt that finds nothing damages the people it searched**: −4 elite cohesion,
and −6 competence / −8 trust on a randomly chosen minister, who knows they were
investigated. That is the whole design. A free scan would be strictly correct to
run every month, which is not a decision; a government that hunts constantly
hollows out its own cabinet.

A catch here files the same public `compromised` record as the monthly roll-up,
so the counter-play chain sees it — this session found two separate places where
a "we were caught" line never reached the public record, and a new catch path
must not become the third. A test asserts it.

## 10. Finished intelligence — the Special Estimate

`IntelProduct`, `IntelProductSystem`, `GameState.intelProducts`, 2 CP, three
months, at most two outstanding.

**This is what collection is for.** Networks and estimates buy sharper numbers
about foreign *capability*; nothing in the game told the operator what a rival
was trying to achieve, whether it would honour a pact, or how it read them —
despite `AIStrategy.StrategicPath`, `AIPrediction.OpponentModel` and
`EndgameSystem.KnownPreparation` being computed every month for every
government. The largest body of unread state in the codebase, in the one pillar
whose subject is knowing things.

| Question | Reads |
|---|---|
| What are they building toward? | `AIStrategy.StrategicPath` |
| Are they preparing an instrument? | `EndgameSystem.KnownPreparation` |
| Will they honour their commitments? | Treaties, broken-treaty record, trust |
| How do they read us? | `OpponentModel.predictedMove` |
| Who is arming the rising on our ground? | Insurgency sponsorship |

Three rules:

1. **It can be wrong, and wrong plausibly.** Accuracy is
   `0.30 + 0.62 × confidence`, and a wrong answer is a *different valid
   conclusion* — a real strategic path, a real predicted move — stated with the
   same confidence as a right one. Noise would be obviously worthless and
   therefore free to ignore; an operator who can spot the bad assessments is not
   being asked to trust anybody.
2. **The answer is fixed when delivered.** Deterministic per (observer, target,
   question, commissioned month), stored, never recomputed. A judgement that
   flickered on refresh would be unusable and averaging repeated reads would leak
   the truth — the `MilitaryAdvice` precedent. Tested across a save round-trip
   too, because a reload that shakes a different answer loose is the thing GDD
   §30 refuses.
3. **It needs collection.** Commissioning requires a network; the grade is what
   thin reporting costs.

**This does not breach spec 15's reporting rule.** That rule forbids a *desk*
misstating a figure it was handed. An estimate is uncertain by construction,
already carries a grade and a margin, and has been allowed to be wrong since
Phase 6. This is the fog system working, not a distorted report.

Delivered assessments are pruned after 60 months. Empty on an old save is
correct, so no migration.

### Deferred from this tranche

- **`Exfiltration`** — needs a model of agents as losable assets that
  `AgentSystem` does not currently have. Building one to hang a single verb on
  would be the tail wagging the dog.
- **`SecurityVetting`** — marginal over the existing counterintelligence sweep,
  which already feeds the same reservoir. Two verbs for one effect is the
  `MIL_READINESS` mistake.
- **Defectors as a pull channel** — wants a crisis definition and a
  `CrisisEffects` id; real work, and better done alongside the Tranche C event
  expansion than bolted on here.
- **Deception as a standing programme** — **already built**, and spec 25 §5.1 was
  wrong to list it. `RunCovertOperation`'s `Deception` branch sets strength,
  bias and domain and decays 1.2/month; the AI's `MountDeception` is the mirror.
  Nothing to do.

## 8. Extension points

- **Named assets** — GDD §14 allows high-value sources to become named characters.
  The hook is a per-network list of assets with their own exposure risk.
  **PLANNED.**
- **Analytical error as a distinct failure mode** — currently noise and deception
  cover this; a "confident but wrong" analysis event would be stronger drama.
  **PLANNED.**
- **Intelligence sharing** — the `IntelligenceSharing` treaty commitment exists but
  does not yet transfer estimates between allies. Straightforward to add.
  **PLANNED.**

## 9. Open questions

- Covert operations are repeatable at will against the same target. The RNG fix
  removed the guaranteed-success exploit, but nothing stops a player running one
  every month; intelligence play accumulates initiative faster than any pillar
  except economy for that reason. Consider diminishing returns or a per-target
  cooldown similar to joint exercises.
- **The AI never runs deception.** `AISystem` has no path to
  `CovertOperation.Deception`, so the player can be deceived by nothing and
  deceives freely. Half of §4 is single-directional in practice despite the
  machinery being symmetric.
- Counterintelligence currently defends everything uniformly. Per-domain
  hardening would let a player protect what matters most.
- Intelligence has the weakest trajectory score of any playstyle in the harness —
  covert work does not advance the pillars — yet grades well above passive on
  initiative alone (spec 12 §6).

# 03 — Intelligence System Specification

Source: `Core/IntelligenceSystem.cs`, `Data/Intelligence.cs`, `UI/IntelReadout.cs`.
GDD §14.

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

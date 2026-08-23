# 14 — Strategic Endgames Specification

Source: `Core/EndgameSystem.cs`, `Data/Endgame.cs`. GDD §21.

## 1. The rule

Every pillar can reach an effect capable of breaking an opponent. **None of them
is a button.** Each requires a capability the state actually commands, years of
preparation, and conditions that permit it — and each produces systemic
consequences that outlast the moment it is used.

| Pillar | Instrument | Severity | Requires capability |
|---|---|---|---|
| Military | Strategic Destruction | Existential | Integrated ISR |
| Economy | Systemic Financial Collapse | Existential | Financial Infrastructure |
| Intelligence | State Destabilization | Severe | Analytic Computing |
| Diplomacy | Strategic Isolation | Severe | Convening Infrastructure |
| Government | Total National Mobilization | Coercive | Civil Administration Reform |

## 2. Gates

**To prepare:** hold the required capability at ≥ 0.6 maturity (holding it is not
commanding it), and a pillar ≥ 65.

**To prepare further:** 2 CP and 120 treasury per authorization, +13.5 progress
each; roughly eight authorizations to reach 100.

**To use:** preparation at 100, plus conditions —

- All offensive instruments require an **active confrontation with the target**.
  These are not instruments of routine diplomacy.
- Strategic Destruction additionally requires **Total War**.
- Total Mobilization requires national unity ≥ 40 — a divided country will not
  follow.

Using an instrument **spends it**: progress resets to zero and must be rebuilt.

## 3. Consequences

**Strategic Destruction** — military −40, force strength −45, industry −30,
manpower −300, stability −30. The target's **war support rises**: they do not
fold, they harden. We take −25 diplomacy, and **every state in the world** takes
−35 trust, −25 relations and +40 threat perception of us, with a permanent
memory entry.

**Systemic Financial Collapse** — confidence −45, growth −6, inflation +12,
market index cut by more than half. **Contagion** reaches every trading partner
in proportion to their exposure, including ours, and they remember who did it.

**State Destabilization** — stability −30, unity −25, military loyalty −25, and
conspiracy +35 with us recorded as the backer. Their strategic alignment with us
is **unchanged**: we can fracture a state, we cannot choose what replaces it
(GDD §22).

**Strategic Isolation** — every treaty they hold with a third party is broken,
relations and alignment with everyone fall, diplomacy −30, and all their trade
volumes are cut by 40%. Costs us 6 diplomacy to run.

**Total National Mobilization** — Forward posture, +20 readiness across branches,
+20 war support, then 18 months of −90 treasury, −12 manpower, −0.35 growth,
−1.2 approval and −0.8 unity per month, in exchange for compounding military and
industrial capacity. It ends with a war-support and unity hangover.

## 4. Escalation logic

`JustifiesMilitaryResponse(type)` returns true for Existential instruments. When
one is used inside an active confrontation, `ProvokeResponse` raises the target's
war support by 20 and escalates that confrontation to **Total War** on the
target's own authority. Per GDD §21, a state whose financial system has just been
destroyed does not treat that as an economic disagreement. Severe instruments
(isolation, destabilization) do *not* trigger this — they hurt without entitling
a shooting answer.

## 5. Every state can do this

The instruments are actor-generic. `CanExecuteBy` / `PrepareBy` / `ExecuteBy`
take an actor id and apply identical gates and consequences to anyone; the player
methods spend command points and delegate. Command points are the player's
attention budget, not a national resource, so AI preparation costs treasury and
time only.

`AISystem.ConsiderStrategicInstruments` runs each month per government:

- **Prepare** only against a rival of ≥ 24 months' standing, with treasury ≥ 400.
  Patience and difficulty planning horizon set the monthly chance. It builds
  toward whichever instrument its pillars already favour, preferring one
  part-prepared over starting fresh.

  Rivalry is tracked separately from objectives (`AIState.rivalries`) because
  `FormObjectives` re-scores and *replaces* the objective list every 3–7 months,
  resetting `monthsPursued`. Objective age therefore cannot measure a lasting
  enmity. A rivalry gains a month for each month it is actively pursued and needs
  three quiet months to shed one — enmity forms faster than it fades.
- **Use** only from desperation — losing momentum, high exhaustion, collapsing
  stability, or having suffered an existential attack from that opponent within
  the last twelve months (which adds +0.45 to willingness). Aggression raises the
  chance, caution lowers it. It is never used opportunistically from strength.
- **Mobilize** at a flat 25% once in limited conflict or worse.

There is a **second route into preparation**: `PreemptProgramme`'s step 2 calls
`PrepareBy` directly when a government has detected an instrument aimed at it
(spec 06 §5b). It applies the same `CanPrepare` gates, so fear cannot buy a
capability a state does not have, and it still pays the 120 treasury per
authorization — but it bypasses the 24-month rivalry requirement and the
400-treasury war chest, because a state that has just seen a finished programme is
not in the position those two thresholds describe.

## 6. Visibility

`EndgameSystem.KnownPreparation(state, observerId, targetId, type)` returns −1
unless `penetration + progress × 0.45 ≥ 45`. An early programme needs collection
against that state to see at all; a *finished* one crosses the threshold on its
own signature, so the world gets exactly one free warning and it is that the
thing is ready. The STRATEGIC
view's *Foreign Programmes* panel reads through this and says so explicitly when
empty: "Nothing reported. This is not the same as nothing existing."

Detection feeds AI reasoning too. `AISystem.DetectedProgramme` takes the worst
detected programme's `progress × weight` (0.60 existential, 0.35 severe) and both
adds it to the `CounterRival` threat term and — above an alarm of **15** — raises a
dedicated `PreemptProgramme` objective at `60 + alarm × 1.6` (spec 06 §5b). A
programme nobody has detected frightens nobody, which is precisely what makes
concealment worth something; a programme somebody *has* detected now changes what
they do.

## 7. Recidivism

`Recidivism(state, actorId)` counts that state's prior Severe-or-worse uses and
returns `min(2.5, 1 + prior × 0.5)`. Every reputational cost — diplomacy loss,
trust, relations, threat perception, relationship memories — is scaled by it. A
state doing this for the third time loses twice what a first-time user loses.
Per GDD §21 the world stops judging the incident and starts judging the pattern.

It scales *reputation only*, never the physical effect: a second strategic
destruction is no more or less destructive, it is only less survivable
politically.

## 8. Open questions / PLANNED

- Strategic Destruction has no *limited* form; it is all-or-nothing. GDD §21
  implies a severity ladder within the instrument.
- ~~Detection raises threat, but the AI has no *distinct* response to it.~~
  **Closed.** `AIObjectiveType.PreemptProgramme` (spec 06 §5b) makes pre-emption
  and suing-for-terms first-class objectives: an alarm above 15 raises an objective
  that outscores every other candidate, is not discounted by caution and is not
  weighted by reach, and executes as *seek terms if we are the target → prepare an
  instrument of our own → fall back to `CounterRival`*. `TrackRivalries` counts it
  as rivalry, because pre-empting a state is the strongest way of treating it as
  one. What is still missing is **breadth of response**: a threatened government
  cannot strike the programme itself, buy off its sponsor, or trade a concession
  elsewhere for its cancellation.
- Recidivism is permanent and global. There is no path to rehabilitation, which
  a long campaign probably needs.

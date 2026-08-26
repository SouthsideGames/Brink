# 04 — Diplomacy System Specification

Source: `Core/DiplomacySystem.cs`, `Core/AllianceSystem.cs`, `Data/Diplomacy.cs`,
`Data/StrategicLocation.cs`. GDD §15, §16, §19.

## 1. The relationship model

One `Relationship` per unordered country pair, created at world generation by
`SeedRelationships`. Six dimensions plus two exercise-derived values:

| Dimension | Meaning | Symmetric? |
|---|---|---|
| `relations` | Broad diplomatic temperature | Yes |
| `trust` | Belief commitments will be honored | Yes |
| `dependenceAOnB` / `dependenceBOnA` | Asymmetric need | **No** |
| `threatPerceptionOfA` / `threatPerceptionOfB` | How dangerous each looks | **No** |
| `strategicAlignment` | Do current interests point the same way | Yes |
| `memory` / `memoryWeight` | Historical record and its weight | Yes |
| `interoperability` | Ability to operate together (§15.3) | Yes |
| `doctrineFamiliarity` | Knowledge of how the other fights | Yes |

Asymmetry matters: you can need them far more than they need you, and that is
what makes coercion expensive.

Seeding: `relations`, `trust` and `strategicAlignment` start at 50; dependence
starts at `tradeVolume × 0.6` in **both** directions from the authored trade
network; threat perception starts at `military × 0.45`. Everything asymmetric
about a pair is therefore earned during play, not authored.

Use `DependenceOf(id)` / `ThreatPerceivedBy(id)` and their `Set…` counterparts
rather than the raw A/B fields. Which country is A is an implementation detail of
world-generation order, and getting it backwards is silent.

`memory` is capped at 30 entries (oldest trimmed); `memoryWeight` accumulates
without a cap and decays monthly.

## 2. Status is derived, never stored

`DiplomacySystem.StatusOf` computes status on every call:

```
score = relations × 0.40 + trust × 0.25 + alignment × 0.25
      − (threatOfA + threatOfB) × 0.5 × 0.30
      + memoryWeight × 0.5
      + 18 MutualDefense / + 8 IntelligenceSharing / + 5 NonAggression

active confrontation: −15 (Tension) / −30 (Crisis) / forced Hostile at war

Ally ≥ 78 (requires MutualDefense) · StrategicPartner ≥ 70 · Friendly ≥ 60
Cooperative ≥ 50 · Neutral ≥ 35 · Rival ≥ 20 · Hostile below
```

Consequences: a warm relationship still cools if they come to fear you, an
alliance requires an actual defense commitment however warm the feelings, and
shooting at each other overrides warm paperwork.

## 3. Player commands and their costs

| Command | CP | Discount | XP | Notes |
|---|---:|---|---:|---|
| `Outreach` | 1 | `SkillEffect.OutreachEfficiency`, floor **0** | 6 | Can become free |
| `ProposeTreaty` | 2 | — | 30 | CP spent on the attempt, not the acceptance |
| `RequestCoalition` | 2 | — | 25 | CP spent even if nobody joins |
| `BreakTreaty` | 0 | — | 0 | Always available; the cost is reputational |

All three of the spending commands call `ProgressionSystem.RecordInitiative`
after the spend succeeds. Adding a new diplomatic verb without that call makes
the pillar grade *worse* than doing nothing (spec 07).

### Outreach has diminishing returns

```
effectiveness = 1 + ourDiplomacyPillar / 100
relations = Growth.Apply(relations, 3 × effectiveness)
trust     = Growth.Apply(trust,     1 × effectiveness)
```

`Growth.Apply` means the warmer things already are, the less another visit
achieves — repeated courtesy calls cannot manufacture an alliance.

## 4. Monthly evolution

`DiplomacySystem.MonthlyUpdate` runs `UpdateBasingRights` first (§6), then walks
every relationship, then `UpdateConfrontationEffects` and `UpdateCoalitions`.

- **Threat perception** tracks `military × 0.45 + 15 if at alert`, approach 0.10.
- **Dependence** tracks `tradeVolume × 0.6`, or **0 if the link is embargoed**,
  approach 0.08. Embargoing a partner destroys the dependence that gave you
  leverage over them.
- **Sanctions** (either direction) cost relations −1.2 and trust −0.4 per month.
  Otherwise relations drift toward `strategicAlignment` at 0.02.
- **Treaties** add trust +0.25 and alignment +0.15 per month.
- **Memory** decays ×0.985/month — it fades but never disappears.
- **Confrontations** damage relations by 0.3 (Tension) to 2.5 (Total War) per
  month, trust by half that and alignment by 0.4× that, and write a memory entry
  (−3) every six months of active fighting.

## 5. Treaties

Six commitments: `MutualDefense`, `IntelligenceSharing`, `Transit`,
`JointPlanning`, `NonAggression`, `TradePreference`. Proposal costs 2 CP.

```
willingness = relations × 0.45 + trust × 0.30 + alignment × 0.25
            + theirDependenceOnUs × 0.15
            + ourDiplomacyPillar × 0.12
            + memoryWeight × 1.5
            − theirThreatPerceptionOfUs × 0.25
            + SkillEffect.TreatyPersuasion        (player proposals only)
            + CAP_CONVENING effectiveness × 10    (GDD §11)

commitment costs: MutualDefense −22, JointPlanning −12, IntelligenceSharing −10,
                  Transit −8, TradePreference −2, NonAggression +4

if the proposer is in a confrontation:
    with them            → −60      (nobody signs with the state fighting them)
    they are Friendly+ with our opponent → −25
    they are Rival− with our opponent    → +12

accepted if willingness ≥ 50
```

On acceptance: relations +8, trust +5, alignment +10 and a +2 memory entry.
Rejection writes a −0.5 memory entry.

`ProposeTreatyBy(state, proposerId, targetId, commitments)` is the actor-generic
form and skips the CP spend, the player-only skill term and the player XP award;
`ProposeTreaty` spends CP and delegates to it. AI diplomacy uses the same
willingness function, so a treaty the AI would sign is a treaty the player could
have signed on the same terms.

### Breaking a treaty

Always possible, and the cost is reputational rather than mechanical:

- With the partner: relations −25, trust −35, memory −6
- **Every third party**: trust −8 and a memory entry ("Observed treaty violation")
- Your diplomacy pillar: −6

The treaty is marked `broken` with `brokenBy` rather than deleted, so the record
survives in the save and in the Chronicle.

## 6. Foreign basing rights

`UpdateBasingRights`, run at the top of `MonthlyUpdate`. GDD §16, §19.

A `Transit` commitment is permission to operate from a partner's soil.
`StrategicLocation.foreignOperatorId` records who currently does, and
`HasForeignBase` reports it. Only `Airbase`, `Port` and `MountainPass` locations
can host at all (`SupportsBasing`).

Each month, per location:

```
if !SupportsBasing or IsOccupied      → foreignOperatorId = ""   (see below)

if HasForeignBase:
    keep it while StillWelcome(host, operator), otherwise revoke and
    emit an ADVISORY + Diplomatic chronicle entry

else:
    among all unbroken treaties involving the host that carry Transit,
    where the partner is StillWelcome, grant it to the partner with the
    highest military pillar; emit ADVISORY + chronicle

StillWelcome(host, partner) =
       an unbroken treaty between them carrying Transit
    && relations ≥ 35
    && partner's threat as perceived by the host ≤ 70
```

Notifications are only raised when the player is the host or the operator;
chronicle entries are written either way, so a third party's arrangements are
discoverable after the fact.

### Why basing and occupation are deliberately different facts

`ownerId` answers *who holds this ground*. `foreignOperatorId` answers *who
operates from it with the owner's consent*. They are separate fields because they
are separate political facts, and conflating them would lose the thing that makes
basing interesting:

- **Basing is consensual and revocable.** The host keeps sovereignty. It ends the
  month the treaty lapses, the relationship cools below 35, or the host comes to
  fear the guest above 70 — nobody hosts a force they have come to fear. The
  operator gets no say. Occupation ends only through an assault, a withdrawal or
  a settlement (spec 01).
- **Occupied ground is held, not hosted.** `IsOccupied` clears
  `foreignOperatorId` outright. A power that has taken a port is not a guest
  there, and describing it as one would let a conqueror claim the diplomatic
  standing of an invited partner. Regression test:
  `MapAndLayoutTests.OccupiedGround_IsHeldNotHosted`.
- **Occupation costs the occupier every month** (spec 01 §3a: treasury,
  readiness, stability, exhaustion). Basing costs nothing but the relationship
  that sustains it. That asymmetry is the argument for diplomacy over conquest as
  a route to reach.
- **Basing is intelligence, not public record.** The map only reveals a foreign
  operator at `DetailLevel.Detailed` or better — real collection against that
  state. Knowing who operates from a rival's soil is one of the more valuable
  things collection can tell you (GDD §14). Tests:
  `AForeignBase_IsOnlyVisibleWithGoodCollection`,
  `ATransitTreaty_GrantsBasingAndLosingItTakesThemBack`,
  `BasingRights_SurviveASave`.

Surfaced in the MAP view: `AsciiCountryMap` draws `*` for a foreign presence
(overridden by `!` for occupation) and `DescribeSites` prints
`FOREIGN FORCE PRESENT`; `WorldMapView.BuildForeignPresence` lists every
arrangement on the selected country's soil, and says so explicitly when there are
none.

**PLANNED — basing has no mechanical effect yet.** `TerritorySystem.ProjectionSwing`
reads airbase *ownership* (`ownerId` vs `originalOwnerId`), so a base held by
agreement does not raise the operator's readiness target the way a captured one
does, and does not lower the host's. Granting transit is currently a visible,
revocable, intelligence-relevant fact with no effect on operations. Wiring
`foreignOperatorId` into `ProjectionSwing` — or into operation range at all — is
the obvious next step and is what would make `Transit` worth its −8 willingness
cost to ask for.

## 7. Coalitions

Formed around an active confrontation for 2 CP by `RequestCoalition`. The player
is added as leader immediately; every other non-target state is polled once.
Each candidate decides for itself.

```
CoalitionWillingness(state, leaderId, candidateId, targetId):

if candidateId == leaderId → 100        (the leader is not recruited)

willingness = relationsWithLeader × 0.35
            + trustInLeader       × 0.25
            + alignmentWithLeader × 0.20
            + (50 − theirRelationsWithTarget) × 0.5
            + theirThreatPerceptionOfTarget   × 0.35
            − theirDependenceOnTarget         × 0.60     ← the strongest brake
            + 25 MutualDefense with the leader
            + 10 JointPlanning with the leader
            − 70 if they hold MutualDefense with the target
            + SkillEffect.CoalitionPersuasion   ← only when the player leads

joins if ≥ 50; leaves mid-conflict if it falls below 30
```

This is GDD §15.2's "exploit an enemy's rocky relationships" made literal — and
the dependence term means a state that trades heavily with your target will stay
out however much it likes you.

### The leader is explicit, and this fixed a real bug

`CoalitionWillingness` takes `leaderId` because **both sides of a war can field a
coalition**: `AllianceSystem.Honor` builds one led by the *defender* (§8). The
function formerly hardcoded the player as leader, with two consequences:

1. **An AI-led coalition cohered on its members' relations with the player.**
   India recruiting Russia against China scored Russia's warmth toward *the
   United States*. A candidate who liked the leader and hated the target could be
   refused because they happened to dislike the player, who was not involved.
   Regression test:
   `BugRegressionTests.ACoalitionLedByAnotherState_DoesNotScoreOnRelationsWithUs`.
2. **A player who honored an alliance was evicted the next month.** As a
   non-leader member of the defender's coalition, the player was scored against
   `FindRelationship(player, player)` — a relationship that does not exist,
   because relationships are stored per unordered *pair* of distinct countries.
   The lookup returned null, willingness returned 0, and `UpdateCoalitions`
   dropped them for falling below 30. Honoring a defense commitment therefore
   cost the reputational price and delivered a coalition membership that lasted
   one month. Regression test:
   `APlayerJoiningSomeoneElsesCoalition_IsNotEvictedByAMissingSelfRelationship`.

`UpdateCoalitions` and `AllianceSystem` now pass `coalition.leaderId`
throughout, and `GameState.FindCoalitionLedBy(confrontationId, leaderId)`
replaces the old single-coalition-per-confrontation lookup.
`GameState.FindCoalition(confrontationId)` survives as a convenience wrapper that
substitutes the player id — use it only where the player's own coalition is
genuinely what you mean.

### `CoalitionPersuasion` is player-led only

`SkillEffect.CoalitionPersuasion` (+12, from `DIP_3` "Coalition Building") is
applied only when
`leaderId == state.playerCountryId`. Applying it to every coalition on the map
made the operator's own training hold together the alliances fielded *against*
them — a direct violation of the Phase 10 rule that skills grant operator
capability and change no national statistic. Regression test:
`CoalitionPersuasion_DoesNotHoldTogetherForeignAlliances` asserts that unlocking
every node leaves an India-led coalition's willingness unchanged.

The same rule applies to `SkillEffect.TreatyPersuasion` in §5: it is added only
when the player is the proposer.

### Coalition strength

```
strength = Σ member.TotalPower × 0.3 × (1 + interoperability/130)
```

`CoalitionStrength(state, confrontation, leaderId)` sums the members of the
coalition led by that state, excluding the leader itself, using the leader's
`interoperability` with each member. The one-argument overload defaults to the
player. Forces that have trained together contribute materially more — see §9.

## 8. Alliance obligations (`Core/AllianceSystem.cs`)

A defense commitment is only meaningful if it is called upon. When a
confrontation first reaches **Limited Conflict**, every unbroken `MutualDefense`
treaty held by the defender is invoked exactly once
(`Confrontation.obligationsInvoked` guards re-entry). The aggressor is never
called to defend against itself. Signatories are collected into a list *before*
any are resolved, because honoring mutates `state.coalitions`.

```
honorWillingness = 30
                 + relationsWithDefender × 0.35
                 + trustInDefender × 0.25
                 + interoperabilityWithDefender × 0.15
                 + threatPerceptionOfAggressor × 0.30
                 − dependenceOnAggressor × 0.45      ← the strongest brake
                 − ourWarExhaustion × 0.35
                 − max(0, 55 − ourStability) × 0.5
                 − max(0, 45 − ourWarSupport) × 0.3
                 + memoryWeightWithDefender × 1.2
honors if ≥ 50
```

**Honoring** joins — and creates, if this is the first ally — a coalition led by
the *defender* (`COAL_DEF_{confrontationId}`), sets alert posture, costs the ally
5 war support, and starts a de facto war with the aggressor: relations −30, trust
−15, memory −5. With the defender: relations +12, trust +15, memory +6.

**Repudiating** marks the treaty broken and is far more damaging than an ordinary
treaty breach: −35 relations, −45 trust and a −10 memory entry with the abandoned
partner, −8 diplomacy, and **−12 trust plus a −2.5 memory entry with every third
party** who now discounts that state's guarantees.

When **the player** is the signatory, this becomes a blocking Crisis Turn
(`AllianceSystem.PlayerObligationCrisisId` = `"ALLIANCE_OBLIGATION"`) with two
options — honor or repudiate. `CrisisSystem.Resolve` routes option 0/1 to
`AllianceSystem.ApplyPlayerDecision` before applying the option's own (small)
deltas. This crisis is built directly rather than drawn from `EventCatalog`; see
spec 11 §1.

## 9. Joint exercises (GDD §15.3)

`Core/ExerciseSystem.cs` is documented in full in **spec 01 §6** because its
primary outputs are military (readiness, doctrine familiarity, operation
resolution). It is listed here because GDD §15.3 files war games under
diplomacy, and three of its effects are purely relational:

- Requires a partner at `Cooperative` or better by `StatusOf`, not currently an
  opponent, and off a **9-month per-partner cooldown**.
- Per exercise: relations `+4 × depth`, trust `+3 × depth`, alignment
  `+2 × depth`, and a `+1.5 × depth` memory entry, where depth is 0.3 / 0.6 / 1.0
  by scale.
- `interoperability` (`+7 × depth`) is a relationship dimension and multiplies
  that partner's coalition contribution by `1 + interop/130` (§7), and feeds
  `HonorWillingness` at ×0.15 (§8). Training together is what makes an alliance
  worth having when it is called.
- The price is `doctrineFamiliarity` (`+9 × depth`) **in both directions**, plus
  a real intelligence network created or deepened for the partner against us.
  Familiarity is permanent and aids operations against a *former* partner, so
  every exercise is a bet that the relationship lasts.

## 5a. Treaty deepening (GDD §15 amendment)

`DiplomacySystem.DeepenTreaty / DeepenTreatyBy` — add commitments to a standing
treaty. Found by playing: with no amendment path, the first signature per pair
was the last, so a friendship treaty **permanently** locked that relationship
out of ever becoming an alliance (a campaign ended with fourteen treaties and
one defence pact — the pact possible only where signing had been deliberately
refused for years).

- Judged on the **added** burden by `TreatyWillingness` — the rival-tie and
  encirclement penalties included, so deepening into a pact answers to bloc
  politics like any pact — plus a history bonus (+8, +0.1/month of treaty age
  capped at +12): a partner with history signs what a stranger would not.
- Success appends commitments to the existing treaty (never a second treaty,
  never duplicates), warms the pair, and goes on the record; refusal costs a
  small memory and, for the player, says what would change it.
- **The AI deepens too** (`SeekTreaty`): a warm, trusted treaty partner
  (relations > 68, trust > 55) gets asked for `MutualDefense` — which is how AI
  blocs solidify and alliance obligations get real signatories.
- UI: the DIPLOMACY panel's treaty area, which previously went silent once a
  treaty existed, now offers ADD-commitment buttons with the willingness gate
  explained on refusal. Covered by `DiplomacySecondActTests`.

## 9a. Bloc politics (GDD §24 amendment)

Measured with a befriend-everyone bot: warm relations with **all fifteen** other
states in twenty years, unresisted, on every seed — a diplomatic playthrough
solved itself and ended in boredom (reported from play in exactly those terms).
Three mechanisms fix it, all in `DiplomacySystem`:

- **`RivalGravity(a,b)`** (0..1) — strongest case over any third state `c` of
  one side being *deeply aligned* with `c` (alignment > 68) while the other is
  in *genuine enmity* with `c` (relations < 22). Applied monthly as a
  **ceiling**: relations and trust approach `100 − gravity × 85`, alignment
  erodes ×0.3. A ceiling, not a drag — a drag loses to outreach spam
  (+3/month beats any fraction). `Outreach` itself is scaled by
  `1 − gravity × 0.8`: envoys are received as warmly as bloc politics allows.
  Thresholds are deliberately severe and load-bearing: a gentler calibration
  (60/30, drag-only) froze the entire planet — gravity spread coldness, which
  fed more gravity, until 93 of 120 pairs were hostile.
- **Treaty acceptance** uses the same thresholds (−40 × rivalTie): *we will not
  pact with our enemy's ally* — so the door and the room agree.
- **`PactAnxiety`** (0..1, defence pacts past the fourth /6) — an alliance web
  reads as encirclement to everyone outside it: +14 on their threat-perception
  target monthly, −25 × anxiety on the next treaty's acceptance. Hegemony is a
  held position, not a finish line.

Post-fix measurement: a bot doing nothing but diplomacy for twenty years tops
out at **11–12 friendships with at least one state going hostile**. Covered by
`WorldHeatTests`.

## 10. Extension points

- **Treaty expiry / renegotiation** — treaties are permanent until broken. Note
  that basing (§6) already depends on a treaty remaining in force, so expiry
  would give it a second, quieter way to end.
- **Multilateral treaties** — all treaties are bilateral. A real bloc needs a
  first-class multilateral object.
- **Intelligence sharing** should transfer estimates (see spec 03). Today the
  commitment only contributes +8 to `StatusOf` and −10 to willingness.
- **`TradePreference` and `JointPlanning`** are priced in willingness and read by
  `StatusOf` / `CoalitionWillingness` respectively, but neither changes trade
  volume or operation planning. `Transit` is the same story (§6).

## 11. Open questions

- Historical memory is a flat weighted list. GDD §15.1 wants countries to
  "remember history rather than reducing all diplomacy to a current relationship
  number" — richer typed memories (betrayal, rescue, humiliation) with different
  decay rates would deliver more of that. Today a −10 abandonment and twenty
  −0.5 rejections are indistinguishable to every formula that reads
  `memoryWeight`.
- Respect Conditions (GDD §15.3) are unimplemented.
- Basing is granted automatically to the strongest eligible partner rather than
  negotiated. A host that would rather invite the *second* strongest — precisely
  because the strongest frightens it — has no way to say so.

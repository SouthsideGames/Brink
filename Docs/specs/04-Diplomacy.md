# 04 — Diplomacy System Specification

Source: `Core/DiplomacySystem.cs`, `Core/AllianceSystem.cs`, `Data/Diplomacy.cs`,
`Data/StrategicLocation.cs`. GDD §15, §16, §19.

**Roadmap 25 negotiated clearance.** DIPLOMACY offers current foreign holders of
our active trade dependencies a survey/clearance request. The player pays 2 CP
per eligible attempt, with 40 treasury transferred to the holder only on acceptance.
Acceptance reads the existing trade willingness against 35; the preview uses the
existing collection-confidence outlook, not the hidden true answer. A successful
service removes at most three months from this site's mine deadline only. Clear
ground still incurs the disclosed survey fee; there is no exact foreign timer in
eligibility or the receipt. Refusal changes no hazard, right or treasury.

Unknown/self/nonmaritime/unrelated sites, an active war with the current holder,
sanctions either way, or insufficient treasury refuse before CP. All checks are
repeated by the command, so a capture or reroute cannot use a stale grant. This is
the holder doing the work, not player access: ownership, military targeting,
Transit clauses, basing and treaty lists are unchanged. Other mined dependencies
can still reduce delivery. The attempt is autosaved, including spent CP on refusal.
No permanent permission, automatic renewal or AI request policy is implied.

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

Seven commitments: `MutualDefense`, `IntelligenceSharing`, `Transit`,
`JointPlanning`, `NonAggression`, `TradePreference`, `ArmsControl`. Proposal
costs 2 CP.

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

### Constructed terms and direction

The negotiation screen authors a `TreatyClause` for every commitment. Its side
is read from the proposer's perspective: `Mutual`, `TheyProvide`, or
`WeProvide`. `Treaty.Carries(country, commitment)` answers who made the promise;
`Treaty.Receives(country, commitment)` answers who benefits. Treaties loaded
from older saves have no clause records and continue to read as mutual.

Direction is authoritative at the mechanical point of use:

- only a country carrying `MutualDefense` is called to fight;
- only a host carrying `Transit` opens its locations to the partner;
- only a capability-holder carrying `IntelligenceSharing` may transfer it;
- only a country carrying `ArmsControl` can breach that restraint.

Relationship status may still ask whether the treaty *contains* a commitment;
that describes the agreement's political character rather than assigning an
obligation. The standing-agreement display prints `[BOTH]`, `[THEY]`, or `[WE]`
beside every term so the player sees the same direction the simulation obeys.

Negotiated acceptance is calculated once from the actual sides. It must not
pass the clause-aware willingness gate and then be run through the flat,
implicitly mutual calculation a second time. Successful player proposals record
one initiative in the shared conclusion path, not another in the controller.

### Conditional and time-limited clauses

Each `TreatyClause` may additionally carry one live trigger and a term:

- `Always` (the zero/default value) or `ConflictWithCountry`, naming a third
  state. The latter applies while either treaty signatory is in an unresolved
  `LimitedConflict`-or-higher confrontation with that state.
- `RelationsAtLeast60`: bilateral relations must currently be at least 60,
  including equality. Missing relations do not activate it.
- `NoMutualOccupation`: neither signatory currently holds a site titled to the
  other (`originalOwnerId` is the current title, including settled cessions).
  Third-party occupation is outside this bilateral promise; missing signatories
  do not activate it. Withdrawal reactivates it and reoccupation suspends it.
- `durationMonths`, measured from the clause's `effectiveDate` (or
  `Treaty.signedDate` for old saves); zero is permanent.

The constraints compose. A five-year transit clause tied to conflict with a
named state is usable only during that conflict and only before its five-year
term ends. Missing clause records, zero-valued fields and old saves remain
unconditional and permanent.

The two bilateral triggers append enum values without adding saved fields or
changing existing values. They name no third state. Unknown saved triggers stay
inactive. These are live conditions on an existing commitment, not automatic
sanctions removal, a withdrawal order or a DMZ enforcement system. Expiry always
wins. They use the existing conditional scope discount (0.70); that shared
pricing assumption is not a claim of balance certification.

`Treaty.ClauseIsActive` is the single activation rule. Defense call-ins, foreign
basing, deliberate intelligence sharing and arms-control enforcement all use
the state-aware `Carries` overload, so presentation and mechanics cannot invent
different conditions. Dormant and expired clauses stay in the signed record and
are labelled `[DORMANT]`; they are not silently deleted or treated as broken.
They stop shaping current political status and scoring. An expired bounded
clause may be renewed through treaty deepening; renewal preserves its side and
trigger, is judged at that same negotiated scope, and restarts only that clause's
clock. Notifications and the chronicle name renewals rather than presenting them
as newly added promises.

The negotiation panel applies the selected trigger and 1/3/5-year or permanent
term to the drafted clauses. A narrower burden carried by the accepting state is
easier to accept; a narrower benefit offered to it is worth less. Trigger and
duration discounts multiply just as the constraints do, and even a five-year
term remains distinguishable from a permanent promise. The fields
remain per clause even though the first authoring surface applies one condition
to the whole package, leaving amendment without a second agreement model.
An untriggered conditional guarantee is historically signed but confers no live
Ally status, access or bloc weight until its named condition is active.

`ProposeTreatyBy(state, proposerId, targetId, commitments)` is the actor-generic
form and skips the CP spend. `ProposeTreaty` spends CP and delegates to it; a
successful player proposal receives the player-only skill term and player
progression award in the shared path. AI diplomacy uses the same willingness
function, so a treaty the AI would sign is a treaty the player could have signed
on the same terms.

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
confrontation first reaches **Limited Conflict**, every guarantee held by the
defender is invoked exactly once (`Confrontation.obligationsInvoked` guards
re-entry). The aggressor is never called to defend against itself. Signatories
are collected into a list *before* any are resolved, because honoring mutates
`state.coalitions` — and each is re-checked against `StillObliged` as the loop
runs, since an earlier repudiation can dissolve the very bloc a later guarantee
came from.

**`GuarantorsOf(state, defenderId, aggressorId)` is the one definition of who is
obliged**, and it reads two sources: unbroken bilateral `MutualDefense` treaties,
and membership of a `Bloc` that carries `MutualDefense` (spec 20 §4a). Where a
state is bound both ways the **bloc wins**: it is the public commitment, owed to
everyone at once, and walking away from it is seen by every other member. The
call-in, the UI roster and `PactAnxiety` all read this one function, because a
guarantee the game honours and a guarantee the game displays must be the same
guarantee.

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
                 + (8 + blocCohesion × 0.18)         ← if the call is a bloc's
                 − totalCommitment × 9               ← what we are already carrying
honors if ≥ 50
```

The bloc term is why a public alliance holds better than a private one: every
other member is watching, and the stronger the bloc believes in itself the more
it costs to be the one who would not come. The commitment term is where
multi-front pressure reaches the *diplomacy* of a war rather than only the
fighting — a state in two wars is not eager for a third.

**Honoring** does three things, and the first one is new (user decision,
2026-08-27):

1. **`ConfrontationSystem.BeginObligationBy` opens a real confrontation** against
   the aggressor, at `LimitedConflict`, with a `Deterrence` objective. Honouring
   used to add the ally to a coalition and stop there — a strength multiplier on
   somebody else's defence — so the operator was told they had entered a war in
   which they had no front, no objective, and no order they could give. It
   **deliberately bypasses `CanOpenAnother` and the settlement truce**: those
   gates stop a state *choosing* more war than it can fight and have no business
   refusing a war somebody else started. A `MaxCommitment` ceiling that could
   block a call-in would make the game forbid the operator from keeping their
   word. The cost of a wider war stays real but priced —
   `TheatreSystem.FocusFactor` drags every operation by commitment elsewhere.
2. Joins — and creates, if this is the first ally — a coalition led by the
   *defender* (`COAL_DEF_{confrontationId}`), so the alliance still coordinates
   on the defender's own front. Both, because one without the other is either a
   war nobody helps with or help in a war nobody is having.
3. Sets alert posture, costs the ally 5 war support, and moves standing:
   aggressor relations −30, trust −15, memory −5; defender relations +12, trust
   +15, memory +6. If the call came through a bloc, **cohesion +5 and trust +7
   with every other member** — a public commitment kept is kept in front of
   everyone in the room.

**A decision can be overtaken before it is taken.** The cascade can leave two
obligations open at once, and answering the first can dissolve the alliance
behind the second — repudiating expels us from the bloc, and a two-member bloc
dies with the expulsion. `ApplyPlayerDecision` returns `false` in that case and
rewrites the chosen `CrisisOption` in place: result text replaced with "the call
has been overtaken", deltas and `effectId` zeroed, plus an `OBLIGATION OVERTAKEN`
notification. Without it `CrisisSystem.Resolve` reports "we have entered the
conflict alongside them" for a war that never opened — an outcome the game cannot
honour, which is the terminal lying about the world.

The option is edited rather than the crisis removed from `state.activeCrises`
deliberately: `LapseUnanswered` walks that list by index and removes as it goes,
so mutating it from inside a decision would make the lapse path drop the wrong
element. `CrisisOption` is a reference `Resolve` already holds, which makes it the
one edit safe on both paths.

### The cascade

Entering a war *is* an attack, so `BeginObligationBy` calls `InvokeObligations`
on the confrontation it just opened — where the original aggressor is now the
defender, and **their** guarantors are asked in turn. This is how a pact between
three states and a pact between three others becomes one war between six, each
government having decided for itself.

It terminates because a pair may hold only one confrontation and
`obligationsInvoked` fires once per confrontation; `MaxCascadeDepth = 4` is belt
and braces against an authoring mistake, not the mechanism. Before the cascade,
`InvokeObligations` walked only the *defender's* treaties, so an aggressor's own
alliance was never called and a bloc-versus-bloc war was impossible by
construction.

**Repudiating** marks the treaty broken and costs, in this order:

- **Standing, never capability.** −35 relations, −45 trust and a −10 memory entry
  with the abandoned partner, plus **−12 trust and a −2.5 memory entry with every
  third party** who now discounts that state's guarantees. The old `−8
  pillars.diplomacy` is **removed**: a national capability hit has no recovery
  path for the operator who incurred it, so it functioned as a slow
  disqualification rather than a price — the same fix already made for
  intelligence exposure (spec 03).
- **Sanctions**, from the abandoned state and from anyone who shared the
  guarantee, at a severity scaled by `ClosenessTo` the abandoned party —
  `Coercive` ≥ 70, `Pressure` ≥ 40, else `Routine`. Routed through
  `EconomySystem.ImposeSanctionsBy`, so the truce rule still owns itself.
- **Preferential trade withdrawn** — the `TradePreference` commitment and its
  clause are removed (the treaty is *not* broken outright: the non-aggression
  clause between two states that no longer trust each other is exactly the clause
  worth keeping), trade volume × 0.65, tariff +15. Not embargoed — that is what
  the sanctions above are for, and doubling the consequence would price one act
  twice.
- **Expulsion from the bloc**, cohesion −14, and the id recorded in
  `Bloc.repudiatedBy` (−30 to any later `JoinWillingness`). A member who would
  not come when the bloc was called is not a member.
- **Threat perception +10 + closeness × 0.10 and alignment −18** with everyone
  let down. This is the term the AI's own rivalry reasoning reads, so an
  abandoned ally can become an enemy **in its own time and on its own judgement**
  — nothing here is scripted revenge.

When **the player** is the signatory, this becomes a blocking Crisis Turn
(`AllianceSystem.PlayerObligationCrisisId` = `"ALLIANCE_OBLIGATION"`) with two
options — honor or repudiate. `CrisisSystem.Resolve` routes option 0/1 to
`AllianceSystem.ApplyPlayerDecision` before applying the option's own (small)
deltas. **A lapse is a repudiation**: saying nothing to a partner who asked for
help is an answer.

The crisis body names the guarantee being invoked *and* lists the aggressor's own
guarantors, because with the cascade in place the answer is no longer obvious —
entering this war puts the same question to them. `ActiveCrisis.contextId`
carries the confrontation id: a cascade can put two obligations in front of the
operator in the same month, and answering the second by scanning for the first is
how somebody ends up in a war they declined to enter. Empty on an old save falls
back to that scan. This crisis is built directly rather than drawn from `EventCatalog`; see
spec 11 §1.

### 8a. The cascade, damped (core stability repair, 2026-09)

**2026-09-25 acceptance revision (user-approved, native recheck pending).**
The historical 72-front ceiling described below is superseded, not raised.
Six 360-month worlds still require at least four AI wars/fronts overall, activity
in at least three worlds, and at most 40 chosen non-player wars. Front totals
remain reported. Monthly post-turn observations now require each country,
including the player and successors, to spend **less than half its observed
months** in unresolved LimitedConflict-or-higher confrontations. Overlapping
fronts count once per country-month. Each month's unresolved pairs must be
unique (either direction), and every open obligation front must have an open
root. Successors accrue observations only after they appear.

This deliberately changes the acceptance contract: accumulated front count
does not distinguish repeated short wars from sustained warfare. Native review
on the preceding patched tree measured 47 versus 77 fronts, no observed orphan
fronts, and all-country armed exposure 7.5% versus 9.2%; non-player exposure
5.3% versus 7.1%. That is increased activity, not demonstrated equivalence or
certified balance. The new 50% criterion is a design choice, not a statistical
confidence bound inferred from six seeds. Tests exercise its exact boundary and
reject synthetic duplicate fronts and closed roots.

Limits: monthly snapshots can miss within-month fighting; this is not a rolling
window guarantee, a peak-concurrency cap, or a balance certification. Legal
defensive commitments remain priced by the game, not capped by a new test
constant. The separate 110 confrontation-month limit remains unchanged. Earlier
figures and the original ceiling below are retained as historical evidence.

The audit measured ~11 AI wars per 30-year world against a baseline of 1.25,
21 of 22 being obligation entries, and single states carrying eight fronts. The
mechanism was correct; the decision under it was near-unconditional. Four
repairs, all systemic:

- **Warmth relative to neutral.** `HonorWillingness` read `30 + relations×0.35 +
  trust×0.25 − …`, which at the world's 50/50 defaults was 60 against a
  threshold of 50: two states with no history honoured a pact with ten points
  to spare and a bloc member started near 80. Now `18 + (relations−50)×0.55 +
  (trust−50)×0.45 + …`: an indifferent signatory declines, a warm bilateral
  partner honours when free, a bloc member (+8 + cohesion×0.18) honours through
  a second front and hesitates at a third. Load weighs `LoadWeight` 12 per unit
  of `TotalCommitment` (was 9), a war of one's own just ended weighs
  `RecoveryWeight` 10, and distance weighs `(1 − reach) × DistanceWeight 25` —
  a guarantor across an ocean used to answer identically to a neighbour.
- **Defensive versus offensive.** `AllianceSystem.IsDirectCall` reads the
  satellite chain (spec 01 §5c): on the original war the defender is the
  victim and the call is direct; on a front a guarantor opened, the "defender"
  is the original aggressor or a state that came in on its side, and the call
  is **offensive**. An offensive call is a war of choice: judged at
  `willingness − IndirectPenalty (20)`, held to `CanJoinOffensively` (the
  ceiling, the domestic bar, `WarRecoveryMonths`), and declining it costs
  standing with the asker alone (`Decline`: relations −10, trust −8, a memory)
  — no treaty broken, no bloc expulsion, no sanctions. The player is asked in
  those words ("AN ALLY ASKS US INTO ITS WAR"). A bloc-versus-bloc war is still
  possible; it is chosen, not switched.
- **The depth guard defers rather than burns** — `obligationsInvoked` is set
  after the depth check.
- **A satellite closes with its root, and the AI seeks terms on every front**
  (spec 01 §5c).

Measured after, eight 30-year worlds: AI-vs-AI root wars 2–7 per world (the
design's ~1.25 plus the wars a healthier world can afford), satellite fronts
3–10 per world, all defensive, max simultaneous fronts 3–5 (one seed 10, an
aggressor swarmed by a large bloc), confrontation-months 71–152 per decade (was
110–246). `WorldHeatTests.TheWorldFightsItsOwnWars` now counts wars and fronts
separately: wars a government chose against the old ceiling of 40 across six
worlds, fronts against 72.

**Those ceilings held, and the breach that looked like tuning was not** (2026-09
suite repair). The test failed at 61 chosen wars for months, which reads as an
aggression problem and is not one: `EndgameSystem.KnownPreparation` missed its
own disclosure boundary by about 1.2×10⁻⁶, so no government ever pre-empted a
finished foreign programme and the action budget went to `AssertClaim` instead
(spec 14 §6, spec 06). Repairing it took the same six seeds to **7** chosen wars,
38 fronts, no repeated pairings and none unresolved. Neither ceiling was moved.

**The lower figure is not offered as evidence of a healthier world.** Most of
the fall was objective starvation, since a permanently visible programme took
the single Standard action slot; spec 06 has the three-way census and the
eligibility correction, and records that `AssertClaim` stays at 132
government-months either way. What this section's ceilings still assert is
unchanged: the world must not be scenery, and it must not be in flames. When a
world-health bound breaks, look for the mechanism that stopped working before
concluding the bound is stale — and then check what the repair did to everything
the mechanism touches.

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

## 5h. Specific leverage — supply for a commitment (roadmap #21, first slice)

`Core/DiplomaticLeverage.cs`; `GameController.OfferSupplyForCommitment(targetId,
focus, commitment)`; the LEVERAGE panel on DIPLOMACY; `DiplomaticLeverageTests`.

The intended thought is *"I can identify something this particular government
needs from us and offer a concrete concession for a specific commitment."* Both
halves of that already existed — a resource `TradeRelation` genuinely lifts the
importer's ceiling (spec 02 §5, `TradeSystem.Supply`), and a negotiated
`TreatyClause` says who carries what (§5 above) — but they were judged and
applied by two verbs on two screens, so the exchange itself could not be said.
This slice puts them on one table. **It creates no leverage currency**: the
offer is priced by the systems that own its halves and applied through the
paths they already use.

**The offer.** A commodity we hold at or above `SurplusFloor` (60 — the same line
`TradeSystem` uses to call a partner short), opened to them as an **ordinary
trade link** at `OfferVolume` 50 and `OfferTariff` 10 (the better end for them,
or our existing terms where better), in exchange for one commitment they carry
(`ClauseSide.TheyProvide`, unconditional, permanent). 2 CP, spent on the
attempt, exactly as a treaty proposal.

**What the supply obligation is, truthfully.** The link is not a guarantee and
the game never calls it one. What it delivers each month follows the trade
rules — `TradeSystem.Supply` reads our *live* stock, the tariff, embargoes and
sanctions either way — so our depletion, a raised tariff or a sanctions regime
shrinks or closes it. We may change or withdraw it later through the ordinary
trade verbs at their usual cost (`WITHDRAW FROM TRADE` is 1 CP and −10
relations / −8 trust; `SET TARIFF` is 1 CP). **Doing so does not cancel their
commitment**: the clause they carry is a treaty term that only `BreakTreaty`
ends, at the treaty-break price. The LEVERAGE panel, the acceptance notice and
the command index all say this at the decision point. No enforcement or
penalty was added for a withdrawn offer; that asymmetry is a documented
limitation, not a hidden rule.

**Acceptance, one test for both halves.**

```
willingness = TreatyWillingness(clauses = [TheyProvide commitment])
            + SupplyGain × WillingnessPerCeilingPoint (1.5)
accepted if willingness ≥ 50

SupplyGain = min( our stock × MaxSupplyShare × (offered throughput − current throughput),
                  max(0, 100 − their current ceiling for that commodity) )
```

The first term is what `TradeSystem.Supply` would actually add for them over any
link that already exists, priced off the link acceptance would leave behind
(the better of the existing terms and the offered ones) — so once they draw our
energy on these terms the supply is worth nothing more and cannot buy a second
commitment. The second
term is **need**: a state already at its ceiling gains nothing from it, so our
surplus alone buys nothing from a state that does not need it. Dependence,
rival gravity, encirclement, legitimacy, our reciprocity and the rest all act
through `TreatyWillingness` unchanged.

**Application, together or not at all.** Accepted: the link is created or raised
(focus set, volume `max`, tariff `min`, embargo cleared — terms are only ever
kept or improved), the importer's
dependence on us rises by `volume × 0.25` (the direction `TradeSystem` records —
here they are the buyer), and the clause is written through
`DiplomacySystem.ConcludeNegotiatedTreaty` (no standing treaty) or
`DiplomacySystem.RecordDeepening` (a standing one) — the same code a negotiated
treaty uses, so the side is stored relative to `countryA` by one rule and
`Carries`/`Receives`/`ClauseIsActive` read it identically. Reciprocity is charged
on `ValueOf(commitment) − SupplyGain / 6`, so trading a trickle for a defence
pact still costs our name. Declined: a −0.5 memory and an ADVISORY that says what
would change it; no link, no clause, no dependence. Invalid: nothing is spent.

**Rewards (spec 07).** One accepted offer is one Diplomacy decision and records
exactly one initiative. The helpers own the treaty act's award: `ConcludeTreaty`
records the initiative and 30 XP when a treaty is created, so the wrapper adds
only the exchange's 12 XP (42 total); `RecordDeepening` awards nothing, so when a
standing treaty is extended the wrapper records the initiative and pays the
deepening's 20 XP plus the exchange's 12 (32 total). Ordinary proposals (30 XP,
one initiative) and ordinary deepening (20 XP, one initiative) are unchanged.

**Existing links.** A link that already carries a *different commodity* is theirs
to keep and blocks the offer ("change it through TRADE"): converting it would
silently take that supply away. A `General` link may be converted — it supplies
no commodity (`Supply` returns zero for it; its only focus-specific effect is
consumer-sector import displacement, a cost, not a benefit), and conversion
keeps its volume and tariff where they are better — the same re-focusing an
ordinary trade proposal performs. Declined or invalid attempts leave any
existing link byte-identical.

**Validity reads public facts only** — our stock, a signed link on a different
commodity, a signed treaty, a broken treaty, a
sanctions regime either way, a war between us, and the arms-control regime — so
a refusal never leaks their position. Their live figure is read once, inside
the true test, exactly as `TradeSystem.CostToPartner` reads a partner's
shortfall.

**Information boundary on screen.** The LEVERAGE panel reads our own stocks and
the *authored* endowment (`WorldFactory.Profiles`, public by spec 08 — every
state is written with a genuine vulnerability); a breakaway with no profile reads
"no authored endowment on record". The outlook is graded by our political
collection on them (`TradeSystem.Assess`'s rule: LIKELY / UNCERTAIN / UNLIKELY),
never the true reception. A test scans the panel for any read of the target's
live resources or the true willingness.

**Not in this slice.** Other concessions (tariff cuts, arms transfers,
recognition — lifting our sanctions is §5i) and other asks (basing terms, a vote in the
chamber, a break with a third state) are the rest of the roadmap item; the
acceptance shape above is the seam they would use. One commodity per pair (the
trade model has one link per pair). The AI has no caller yet — an AI government
short of energy still proposes trade and treaties separately.

## 5i. Specific leverage — lift our sanctions for a commitment (roadmap #21, slice 2)

`DiplomaticLeverage.CanOfferRelief / OfferReliefBy`;
`GameController.OfferSanctionsReliefForCommitment(targetId, commitment)`; the
LIFT OUR SANCTIONS FOR A COMMITMENT panel on DIPLOMACY; `SanctionsExchangeTests`.

*"We will lift the sanctions we imposed on your government if you agree to
this commitment."* **Ours on them, only.** A regime they run against us is
theirs to lift (SEEK SANCTIONS RELIEF, spec 02 §4a — unchanged); a third
state's regime is not ours to trade; the panel and `CanOfferRelief` say which
of these applies. A chamber-mandated regime is refused (the mandate is not ours
to trade away — the same rule relief already applies), as is an offer to a
state we are fighting (a war voids any détente the moment it is signed).

**The concession is the regime itself.** Accepted, it is removed exactly as
`EconomySystem.LiftSanctions` removes it — the `Sanction` record goes, and an
embargo on our trade link lifts unless their remaining Severe-or-above regime
still imposes it (spec 02 §3) — and the **existing détente** is
set: `Relationship.sanctionsTruceMonths = max(existing, DetenteTruceMonths)`
(24), the same field `SeekSanctionsReliefBy` sets; a longer truce already
running on the pair is kept, never shortened. Relations +6 / trust +5 and the "Negotiated
an end to sanctions" memory follow, as for negotiated relief. The commitment is
then written through `ConcludeNegotiatedTreaty` / `RecordDeepening` exactly as
in §5h, so direction, conditions and reciprocity behave identically.

**Pricing, from the sanction mechanics that already exist.**

```
value      = Weight(severity) × (1 − SanctionAdaptationFloor × min(1, monthsActive / SanctionAdaptationMonths))
             — the exact term SanctionPressureOn charges them for this regime today
supply     = min( TradeSystem.SupplyIfLifted(them, focus, us→them) − TradeSystem.Supply(them, focus),
                  100 − their current ceiling )
             — the ceiling points that actually resume for them on our link's commodity
willingness = TreatyWillingness([TheyProvide commitment]) + value × 12 + supply × 1.5
accepted if willingness ≥ 50
```

**The supply half is priced by what the lift delivers, never by the embargo
flag.** `TradeSystem.SupplyIfLifted(state, country, focus, liftSender,
liftTarget)` is `Supply` read as if that one regime were lifted the way
`LiftSanctions` lifts it — the record gone, the pair's link no longer
embargoed — with every other closure read exactly as it stands: their own
regime on us, a third state's regime, another embargo, the focus, the tariff,
the partner's stock. It shares `Supply`'s single loop (with no pair named it
*is* `Supply`, and that is the only path anything else calls), it is read-only
(no preview edits and restores the live world), and `SupplyReliefGain` takes
the difference and caps it by their headroom. Consequences the tests pin: a
regime *they* still run on us keeps the link closed under the supply rules, so
the lift resumes nothing and is priced at nothing whatever the flag says; a
sub-Severe regime of ours (which sets no flag) that was closing an open
commodity link is priced at exactly what reopening it delivers; a `General`
link or no link is nothing; a third state's supply they already draw is never
re-priced as a gain; the price is bounded by the room they have. In every case
`SupplyReliefGain` equals the change in their ceiling the accepted exchange
produces, and phantom supply cannot carry an ask the honest price refuses.

A fresh Coercive regime (1.5) is worth +18 — enough to carry a Transit ask
(−12.8 in the treaty test) a relationship would otherwise refuse; Severe (2.4)
+28.8, Existential (3.6) +43; a Routine regime (0.35) +4.2. A regime they have
adapted to (48 months) is worth half, because it is costing them half. Nothing
else is priced: no leverage currency, no second sanction model. Reciprocity is
charged on `ValueOf(commitment) − value × 2`.

**Durability — exactly what the game already enforces, and nothing more.**
While the détente runs, `ImposeSanctionsBy` refuses new measures from
*either* side (the player's IMPOSE command spends its CP first and then fails,
which is that verb's pre-existing behaviour under any détente). A declaration
of war between the pair voids the détente (`ConfrontationSystem.BeginBy`),
after which measures may return. After 24 months we may sanction them again at
the ordinary cost with no penalty. **Their commitment is a treaty term** and
stands until the treaty is broken at the treaty-break price. The panel, the
acceptance notice and the command index say all of this; no obligation is
implied that the game does not enforce.

**Rewards.** One accepted offer records exactly one Diplomacy initiative; XP is
the treaty act's own award plus 12 for the exchange (42 created / 32 extended),
under the existing repetition rule. The ordinary LIFT SANCTIONS verb's 10 XP is
deliberately not added — one decision is paid once. Ordinary lifting, relief
requests, proposals, deepening and the supply exchange are unchanged.

**Information.** Everything the panel prints is ours: our regime's severity and
age, a mandate, a running détente. The outlook is graded by our political
collection on them (§5h's rule); a test scans the panel for any read of their
live resources or the true reception.

**Reciprocal-embargo follow-up corrected.** All six sanction-removal paths now
share `EconomySystem.RemoveSanction`: a surviving Severe/Existential regime
keeps the pair's flag set, so `TradeHealth`, import displacement, dependence,
map/status readers and event/AI readers no longer lose that embargo when only
one side lifts. Ending the last severe regime clears it. Commodity supply and
its counterfactual pricing remain unchanged: any remaining sanction blocks
supply, including sub-Severe regimes which intentionally do not impose the full
embargo (spec 02 §3). This is not an old-save repair for flags already cleared
by the former bug, nor a unification of the two different severity rules.

**Not in this slice.** Partial lifting or severity reduction; lifting for a
conditional or bounded clause; recognition or tariff bargaining; an AI caller.
## 5j. Specific leverage — recognise a state for a commitment (roadmap #21, slice 3)

`DiplomaticLeverage.CanOfferRecognition / OfferRecognitionBy`;
`GameController.OfferRecognitionForCommitment(targetId, commitment)`; the
RECOGNITION FOR A COMMITMENT panel on DIPLOMACY; `RecognitionExchangeTests`.

*"We will recognise your state if you agree to this commitment."* **Only a
breakaway, and only recognition we have not yet given.** Eligibility is
exactly `DiplomacySystem.CanRecognise` — a `SecessionSystem` successor
(`IsSuccessor`: founded after the world was), not ourselves, not one we already
recognise — followed by the treaty gates the other exchanges apply (a broken
standing treaty, a commitment they already carry, arms control without a
verification regime). Recognition is granted in exactly one place in the game
(`RecogniseBy`) and is never withdrawn by any rule, so it cannot be sold twice,
before or after a save.

**The concession is our actual recognition, through the existing path.**
Accepted, `RecogniseBy` runs unchanged: the flag, the grateful new state
(relations +16, trust +12, alignment +10, a memory), the parent's reaction
(relations −14, trust −10, a memory), the public chronicle line. The commitment
is then written through `ConcludeNegotiatedTreaty` / `RecordDeepening` exactly
as in §5h, so direction, conditions and reciprocity behave identically.
Declined: a memory and an explanation only. Invalid or unaffordable: nothing.

**Pricing, from what recognition is already worth.** Treaty acceptance reads
the *proposer's* legitimacy (`TreatyWillingness` charges a breakaway proposer
`(1 − Legitimacy) × 45`), so when we propose to a breakaway that term is zero
and our recognition reaches their judgement only through the warmth it writes.
The exchange therefore reads the ask **as they would read it once recognised**
— the same `TreatyWillingness`, on a detached `Relationship.AsIf()` copy
carrying exactly the warmth `ApplyRecognitionWarmth` writes (one definition,
shared with `RecogniseBy`), with rival gravity read through a
`RelationshipLookup` in which a detached copy of our relationship with their
**parent** carries exactly the cost `ApplyRecognitionParentCost` charges (the
other definition `RecogniseBy` shares). The parent is a third state gravity
reads: a breakaway committed to its parent (alignment past 68, a defence pact,
a shared bloc) pulls against us once recognition drops our relations with the
parent under 22, and a preview that warmed the pair without cooling the parent
read gravity from a world that would not exist after acceptance — measured up
to 37 points high, enough to flip acceptance (at parent relations 10 and
alignment 90 the uncorrected read was 70.0 against an actual 43.5). Both
consequences, nothing live touched — plus the one share of the world's acceptance our
recognition adds (`LegitimacyGain`, the `Legitimacy` arithmetic read forward
one recognition), at `LegitimacyWillingnessWeight` (45), the weight the treaty
test itself puts on legitimacy:

```
once     = TreatyWillingness([TheyProvide commitment]) read on AsIf(relationship) with recognition's warmth applied
share    = Legitimacy(after one more recognition) − Legitimacy(now)       — 1/(N−1) until the world is unanimous
willingness = once + share × 45
value    = (once − plain) + share × 45                                    — what our recognition is worth to them
accepted if willingness ≥ 50
```

Measured on the fixture world (17 states; relations, trust and alignment
equal): the warmth alone lifts a Transit ask by 14.65 points and the share
adds 2.81 (17.46 in all), so recognition carries an ask a relationship at the
same warmth would refuse — at warmth 35 the bare ask reads 32.7 and the
recognition-backed one 50.2. Accepted at that warmth, the pair moved
relations 35 → 59 (16 from recognition, 8 from signing), the parent 34 → 20. Nothing is granted before acceptance — the preview copy is never
stored — and nothing is priced twice: the warmth is inside `once`, the share is
the one term `TreatyWillingness` cannot see for a target. Reciprocity is
charged on `ValueOf(commitment) − value / 6` (six willingness points per
treaty-value unit, the sanctions exchange's ratio).

**Durability.** Recognition is permanent: no verb, tick or event clears
`Relationship.recognised`. Their commitment is a treaty term and stands until
the treaty is broken at the treaty-break price. The exchange promises nothing
beyond those two facts and the panel says so.

**Rewards.** 2 CP (`OfferCost`). One Diplomacy initiative; XP is the treaty
act's own award plus 12 for the exchange (42 created / 32 extended) under the
repetition rule. RECOGNISE A STATE's own 14 XP and initiative are deliberately
not added — one decision is paid once. Ordinary recognition (1 CP, 14 XP) and
both earlier exchanges are unchanged.

**Information.** The panel prints only public facts: the state, who it broke
away from (`ParentOf`), how many states recognise it (every recognition is a
Public chronicle act), our own position, the commitment, direction and cost.
The outlook is graded by our political collection on them (§5h's rule); a
test scans the panel for any read of legitimacy, the true reception or their
resources.

## 5k. Specific leverage — conditional and time-limited requested commitments (roadmap #21, final core slice)

`DiplomaticLeverage.RequestedClause / RenewableClause`; the `terms` parameter on
`CanOffer / CanOfferRelief / CanOfferRecognition`, the three willingness and
offer functions and the three `GameController` verbs; the TERMS FOR WHAT THEY
WOULD CARRY panel on DIPLOMACY; `LeverageTermsTests`.

**One clause, carried whole.** Every exchange asks for one clause they carry.
The operator may bound it with the existing conditional-agreement model — the
`ConflictWithCountry` trigger naming a third state, either bilateral condition
described above, a supported term (permanent,
one, three or five years: `DiplomacySystem.SupportedTermMonths`), or both. The
requested clause is built once (`RequestedClause`: commitment, `TheyProvide`,
exactly the requested trigger, state and term) and is the clause the gate
validates, the treaty test prices and the treaty record writes; nothing rebuilds
a default clause after terms are chosen. Direction is stored relative to
`countryA` by the shared `ConcludeNegotiatedTreaty` / `RecordDeepening` rule, and
`effectiveDate` is the day of acceptance.

**Validation before anything is spent.** `DiplomacySystem.ClauseTermsAreValid`
is the one rule for a clause's condition and term — non-negative term, and a
conflict trigger that names a real third state, never a signatory — extracted
from the negotiated-proposal path (which still refuses on it silently). The
exchanges add the panel's supported-term set. An invalid request is a refused
control with its reason, spends nothing and changes nothing.

**Pricing.** The ordinary clause test prices the requested clause exactly once:
trigger and term discounts apply through the same `TreatyWillingness` clause
overload the negotiation panel uses, and each concession's verified value —
actual marginal supply (§5h), live sanction pressure plus the supply that
actually resumes (§5i), recognition's warmth, parent cost and legitimacy share
(§5j) — is added exactly as before. The recognition counterfactual reads the
requested clause on its detached copies. Default (unconditional, permanent)
offers are unchanged.

**Existing commitments.** A promise they still carry — active or dormant — is
not overwritten (`THEY ALREADY CARRY …`). A commitment the treaty settles the
other way cannot be reversed here (`… THE OTHER WAY`). A commitment the treaty
records without a clause is permanent and unconditional, so it reads as still
carried. An **expired** promise renews on its recorded terms only
(`RenewableClause`): renewal keeps the recorded side, trigger and term, restarts
only that clause's clock through `RecordDeepening`'s renewal path, is priced at
that recorded scope, and is announced as a renewal; a request on different terms
is refused and says what the record holds. Unrelated clauses keep their clocks.

**Concession versus commitment.** The terms bound only what they carry. What we
give follows its own existing rules: a supply link is an ordinary trade link we
may change or withdraw through TRADE; lifted sanctions bring the existing
détente of at least 24 months; recognition is permanent. Their commitment
expiring or lying dormant does not reverse any of it, and no rule is implied
that the game does not enforce. The panel, the acceptance notice and the
chronicle name the condition and the calendar date the term ends, in the words
the signed record uses (`IF CONFLICT WITH X`, `EXPIRES BEFORE MMM YYYY`).

**Rewards** are unchanged: 2 CP per attempt, one initiative, the treaty act's own
award plus 12 (42 created / 32 extended or renewed) under the repetition rule.

**Not in this slice:** an AI caller for any exchange; the embargo-flag follow-up
of §5i (subsequently corrected there for reciprocal severe regimes); renewal
on different terms (an amendment model does not exist).

## 9a. Bloc politics (GDD §24 amendment)

Measured with a befriend-everyone bot: warm relations with **all fifteen** other
states in twenty years, unresisted, on every seed — a diplomatic playthrough
solved itself and ended in boredom (reported from play in exactly those terms).
Three mechanisms fix it, all in `DiplomacySystem`:

- **`RivalGravity(a,b)`** (0..1) — strongest case over any third state `c` of
  one side being *deeply aligned* with `c` (alignment > 68) while the other is
  in *genuine enmity* with `c` (relations < 22). Read at the point of use, never
  written: see §9c. `Outreach` is scaled by `1 − gravity × 0.8`, so envoys are
  received as warmly as bloc politics allows.
  Thresholds are deliberately severe and load-bearing: a gentler calibration
  (60/30, drag-only) froze the entire planet — gravity spread coldness, which
  fed more gravity, until 93 of 120 pairs were hostile.
- **Treaty acceptance** reads the same constraint, through the same computation
  (§9c): *we will not pact with our enemy's ally* — so the door and the room
  agree by construction rather than by two formulas kept in step. The separate
  `−40 × rivalTie` charge is retired; it was the directional half of the same
  symmetric maximum, on un-augmented terms, and therefore priced one fact twice.
- **`PactAnxiety`** (0..1, defence pacts past the fourth /6) — an alliance web
  reads as encirclement to everyone outside it: +14 on their threat-perception
  target monthly, −25 × anxiety on the next treaty's acceptance. Hegemony is a
  held position, not a finish line.

Post-fix measurement: a bot doing nothing but diplomacy for twenty years tops
out at **11–12 friendships with at least one state going hostile**. Covered by
`WorldHeatTests`.

### `PactAnxiety` counts states, not documents

`PactAnxiety` (spec 04 §8a) now counts `AllianceSystem.GuarantorsOf(state, id,
null)` rather than walking `state.treaties`. While every alliance was bilateral
the two were the same number; the moment a bloc could carry `MutualDefense` the
old form became a hole — a twelve-member defence bloc registered as **zero**
pacts, so the multilateral route paid no encirclement anxiety at all, strictly
dominated the bilateral one, and let the operator quietly collect the map again.
That is the exact failure the world-heat work was built to close. What is counted
is how many governments would come, which does not care how the promise was
papered.

## 9b. Gravity reads acts, and cold alignment thaws (core stability repair, 2026-09)

Three changes to the monthly bilateral tick, each a value-versus-target fix:

- **Sanctions chill relations to a target.** A sanctioned pair's relations
  approach `strategicAlignment − SanctionChill (8)` at the ordinary 0.02
  rate, instead of losing 1.2 a month and being excluded from the recovery
  branch. Trust erodes 0.15 a month under sanctions toward
  `SanctionTrustFloor` (15), not to zero. Sized so a pair at the alignment
  baseline settles above `EconomySystem.SanctionHostilityLine` — a neutral
  pair is not a hostile one, and its measures lapse at review.
- **Rival gravity capped alignment rather than draining it** (superseded by
  §9c, which stops it writing alignment at all). The old
  `alignment −= gravity × 0.3` had no floor; gravity attracts where relations
  are cold, cold pairs are what sanctions make, so every sanctioned pair's
  alignment ran to zero and the chill target ran to zero with it — the
  "relations 0 on every standing regime" the sanction dump showed, and the
  feedback that froze the planet in the first calibration. The cap bounded that
  loop; §9c removes it.
- **Cold alignment thaws, upward only.** Below `AlignmentBaseline` (40), and
  absent a war between the pair, alignment approaches the baseline at
  `AlignmentReversion` 0.004 a month (~20 years). Warm alignment is earned by
  treaties and blocs and keeps until something spends it: a symmetric pull held
  every alliance at ~75, under the 68 line gravity radiates from.

And gravity itself now reads **acts** on both sides. `Warmth` is the larger of
the alignment reading, `PactWarmth` (0.6) for a signed mutual-defence treaty
and `BlocWarmth` (0.75) for a shared bloc — a treaty partner's alignment sits in
the low seventies, barely over the line, so gravity from a signed pact was a
rounding error. `Coldness` is 1 for a pair at war and at least `SanctionEnmity`
(0.5) for a pair under standing measures. Measured: the bot tops out at ≤ 13 of
15 again, in a world that no longer resists it by sanctioning it for other
reasons.

## 9c. Disposition versus permitted closeness

**Countries may genuinely like everyone. They may not functionally stand beside
everyone.** Rival gravity represents geopolitical incompatibility, so it limits
how far a warm relationship can *progress* — it does not decide how two states
feel. The two questions now have two readings of the same relationship:

- **`AffinityOf` / `StatusOf` — disposition.** The six-dimension score and the
  status ladder, unchanged and canonical. What the operator is shown. Every
  hostility consumer reads this: `SanctionCauseStands`, the AI threat term,
  `AssertClaim`, `MostHatedState`, `ColdestRivalOf`, sanctions relief,
  diplomatic isolation, insurgency motive, `CouncilSystem.VoteScore`, mandates,
  standing directives, history seeding, event eligibility, bloc cohesion, the
  sanction chill target and the alignment thaw.
- **`PermittedWarmth(a,b) = 100 − gravity × AlignmentGravityWeight`** — the
  warmth the world presently permits. Derived, never stored: no save field, no
  migration. `Permitted(r, stored)` is `min(stored, PermittedWarmth)`, the
  continuous form read by the partnership gates.
- **`FunctionalCloseness(a,b)`** — the categorical form. `min(StatusOf,
  CeilingBand(PermittedWarmth))`, passed through unchanged at Neutral or colder.
  `CeilingBand` reads the status ladder against `RelationalWeight × permitted`,
  where `RelationalWeight` is the sum of the score's three relationship weights
  (0.40 + 0.25 + 0.25). At committed gravity 0.5 that is 51.75 → Cooperative,
  one band below the friendship line — the arithmetic the friendship invariant
  always asserted.

Two properties are load-bearing and are asserted, not assumed:

1. **Gravity cannot manufacture hostility.** `CeilingBand`'s final branch
   returns Neutral, so `FunctionalCloseness` can never report Rival or Hostile
   from a warm disposition. Capping the score's *inputs* instead would let the
   untouched threat, memory and confrontation terms carry a warm pair below the
   Rival line — the same defect, relocated into classification.
2. **Gravity cannot warm a relationship.** `min` against the disposition.

Measured across eight Challenging seeds × 480 months, against certified
production as control: the write-back moved the affinity score on **31,263**
pair-months by up to **10.7** points; the read-time form moved it on **0 of
464,816**. Zero Rival/Hostile outputs from a warm disposition and zero warming,
over the same population. The constraint binds on 3.5% of pair-months and
removes 41 of 99 warm relationships from functional friendship at end of run.
Gravity itself is preserved, not amplified: pair-months bearing gravity
45.5% → 47.2%, committed 29.1% → 30.0%, mean at end 0.290 → 0.301. Standing
sanctions fall 17.7% because gravity can no longer push relations under the AI's
hostility line. The `0.706` gravity at which the old ceiling crossed
`AlignmentBaseline` remains a **diagnostic** figure only — it is not a threshold
and nothing reads it.

**Routed to permitted closeness** (partnership permission): treaty acceptance
and deepening, `CoalitionWillingness`' leader-side terms, `BlocSystem`
accession and member compatibility, `AccessionSystem`, `AllianceSystem`
honour-willingness relations and observer closeness, summit and mediation
gates, basing rights, the AI's partnership-seeking and deepen-to-pact gates,
reunification, `ExerciseSystem` eligibility (the one band-form consumer), and
the annual grade's relationship term — `position` measures standing the
operator can exercise.

**Two terms are deliberately left on disposition**, and say so at the call
site: `BlocSystem.JoinWillingness`' and `AllianceSystem.HonorWillingness`'
trust terms. GDD §15.1 defines trust as the belief that commitments will be
honoured — a claim about a partner's reliability, not about how close bloc
politics lets us stand. `HonorWillingness` asks exactly "will they come when
called", so capping it would let a third state's alignment make an ally look
unreliable, which is a disposition claim gravity may not make. Bloc cohesion
stays on disposition for the same family of reason and one stronger one:
`BlocSystem.Bind` writes cohesion into `strategicAlignment`, so a
gravity-derived cohesion would carry gravity back into stored disposition.

**Presentation.** The `[STATUS]` label, the relationship bars, the world-map
glyphs and the dossier all keep reading disposition — a desk may bury a figure,
never distort one (spec 15). The DIPLOMACY panel adds one line, shown only when
the two readings differ, naming the functional band and the third state whose
alignment is binding; gravity-gated refusals say the same thing through the
existing `Block` / `UNAVAILABLE` channel (`DiplomacySystem.BlockedByRival`).

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

# 20 — Blocs

**Status: as-built.** `Core/BlocSystem.cs`, `Data/Bloc.cs`,
`Assets/Tests/EditMode/BlocTests.cs`. GDD §15.2, §24.

## 1. Why it exists

Blocs existed only as a **term in a formula**. `DiplomacySystem.RivalGravity`
prices being deeply aligned with somebody's enemy, and it does that well — but
there was nothing on the map a state could join, lead, be excluded from, or defect
from. Coalitions are raised for one war and dissolve after it; treaties are
bilateral; alliance obligations are a consequence of a signature rather than a
structure. The world had sides and no side had a name.

## 2. What a bloc is not

**It carries no commitments.** Defence, transit and trade preference stay in
`Treaty`, where the acceptance logic and the reputational cost of breaking them
already live. A bloc is not a promise; it is a declaration of who you are with.
Keeping it that way is what stops this becoming a second treaty system with its
own drifting copy of the same rules.

## 3. Data

`GameState.blocs : List<Bloc>` — empty on an old save, **no migration step**.

| Field | Meaning |
|---|---|
| `name` | In-universe, e.g. "THE UNITED STATES UNDERSTANDING" |
| `leaderId` | Who holds it together and pays to |
| `memberIds` | Everyone in it, leader included |
| `cohesion` | 0..100, drifts toward `CohesionTargetFor` |

Bounded at `MaxBlocs = 3`. A world of twenty-four one-member blocs is a list, not
a set of sides.

## 4. Joining

`JoinWillingness` against a threshold of 50:

```
20 + (relations−50)×0.55 + (trust−50)×0.45 + (alignment−50)×0.40
   + dependenceOnLeader×0.20 + cohesion×0.12
   − max(0, threatPerceivedOfLeader − 40)×0.55
   − 12 per member the candidate is hostile to (<25 relations)
   + 10 for any treaty, +12 more for a defence pact
```

The threat term is what stops a large power collecting the map: the stronger and
more frightening the leader looks, the harder the sell. Every other term is
something the diplomacy pillar can buy, so membership is *earned* through the same
instruments as everything else.

## 5. What it is worth

Two readers, both already load-bearing:

- **`CouncilSystem.VoteScore`** — `+30` for a bloc partner's motion, `−34` when
  the subject is a bloc partner, `−14` when the mover is in the opposing bloc.
  The chamber is the room where acting together is the whole point, and until
  blocs existed there was no way to arrive in it as a side.
- **`Bind`** — members drift into `strategicAlignment` at `cohesion/100 × 0.45`
  per month. Through the existing dimension rather than a new one, so everything
  that already reads alignment reads this too.

## 6. What it costs

`UpkeepPerMember = 0.35 PC` per member beyond the leader, every month. A leader
who cannot pay loses 4 cohesion instead. Leading is work, and a free bloc is a
free alliance — which is the thing this codebase keeps deleting.

`CohesionTargetFor` is the average of `relations × 0.6 + alignment × 0.4` across
every member pair, plus up to 15 for a **shared enemy** (a bloc holds together
better on a threat than on enthusiasm). Below `CohesionFloor = 22` the bloc sheds
its least convinced member — one at a time, so a bad month is a warning rather
than a collapse.

Leaving costs trust −9, alignment −10 and a memory with every member still in it.
A bloc you can leave for nothing is a bloc that means nothing. A **leader** who
leaves dissolves it.

## 7. The world's own

`ConsiderBlocs` runs monthly at a 10% chance, outside the objective budget (the
`ConsiderDetente` precedent). It grows an existing bloc before founding a new one
— an alignment with members is worth more to the world than another banner with
one state under it — and otherwise the most capable unaligned diplomat founds one
for 2 PC. A bloc with nobody in it after twelve months dissolves.

## 4a. Commitments — the multilateral alliance (user decision, 2026-08-27)

**This supersedes the original rule that a bloc carries no commitments.** That
rule was written when a bloc was read in two places and it was right about what
it protected: acceptance logic and the reputational cost of breaking a promise
live in `Treaty`, and duplicating them would have been a second treaty system.

What it could not express is what an alliance actually is. A mutual defence pact
between eight states is not twenty-eight bilateral treaties — it is one signature
that obliges you to everybody at once. Building NATO out of `Treaty` meant N²
agreements *and* fought `PactAnxiety`, which makes every pact past the fourth
harder to sign. The multilateral layer had to live somewhere, and this object
already had a leader, a membership, a cohesion figure and a way in and out.

The division of labour is now:

| | `Treaty` | `Bloc` |
|---|---|---|
| Parties | exactly two | any number |
| Terms | negotiated per clause, `ClauseSide` allows asymmetry | **uniform** — every member carries all of them |
| Edited after signing | yes, via `DeepenTreatyBy` | **never** |
| What it is | the bilateral bargain | the multilateral alliance |

`Bloc.commitments` is a `List<TreatyCommitment>`; `Guarantees(commitment)` is the
predicate. **Empty on an old save is correct rather than merely blank** — blocs
that predate this genuinely carried no commitments, so they load as the
declarations of alignment they were and no migration is owed.

Three rules keep it from becoming a second treaty system:

1. **Uniform, with no `ClauseSide`.** A bloc in which some members are guaranteed
   and others only guarantee is not an alliance, it is a protectorate with extra
   steps. Asymmetric bargains stay in `Treaty`, which is what `ClauseSide` is for.
2. **Fixed at founding, never amended.** A leader who could add a defence
   obligation later would be binding members to something they never agreed to.
   Widening the terms means founding a new bloc — the same principle
   `DeepenTreatyBy` follows by making every addition answer to acceptance again.
3. **Joining is priced on the terms.** `JoinWillingness` subtracts
   `DiplomacySystem.BurdenOf(commitment) × 0.45` per term, through the same
   function the bilateral acceptance logic uses, so the two routes to an alliance
   cannot disagree about what a promise is worth. It is divided down because a
   burden owed to a group is shared with that group — which is the honest reason
   multilateral alliances are easier to build than N bilateral ones, and the whole
   reason to have this object. A defence bloc then adds back
   `min(24, (members − 1) × 6)`: the same terms are worth more the more states
   already carry them.

`Bloc.repudiatedBy` records anyone expelled for refusing a call (`BlocSystem.Expel`,
−30 to any later `JoinWillingness`). Distinct from `LeaveBy` because leaving is a
policy and being expelled for refusing a call is a disgrace.

### What it is worth, and what it costs

The alliance is invoked through `AllianceSystem.GuarantorsOf` — see **spec 04 §8**
for the call-in, the cascade, and what honouring and repudiating cost. Two
consequences worth naming here:

- **`PactAnxiety` counts bloc guarantees**, or the multilateral route would pay
  no encirclement anxiety and strictly dominate the bilateral one.
- **Honouring is seen by the whole room**: cohesion +5 and trust +7 with every
  other member. Repudiating is cohesion −14 and expulsion.

The player founds one of three shapes from DIPLOMACY — **defence pact**
(`MutualDefense`), **economic union** (`TradePreference`), or **alignment** (no
terms, the original behaviour). Three buttons rather than a clause editor: what a
bloc obliges its members to is the decision, and it is worth making it legible
rather than configurable. `AISystem`/`ConsiderBlocs` founds a defence pact when the
founder has somebody to be frightened of and an economic union otherwise, plus
`IntelligenceSharing` at intelligence ≥ 60 — without which every foreign bloc
would be a talking shop while the player's was an alliance, which is this
codebase's most-repeated bug in a new coat.

# 17 — The Multilateral Chamber

**Status: as-built.** `Core/CouncilSystem.cs`, `Data/Council.cs`,
`Assets/Tests/EditMode/CouncilTests.cs`. GDD §15.2, §20, §28.

## 1. Why it exists

Diplomacy was bilateral outreach, bilateral treaties, and coalitions assembled
for one war and dissolved after it. There was no room where everybody was
present, no vote, and no public finding a state could be made to wear.

Two consequences were concrete rather than atmospheric:

- **Sanctions were unilateral by construction.** An operator who wanted five
  states to squeeze one had to persuade five states one at a time, and then watch
  each pay full blowback for doing it. There was no such thing as a joint regime.
- **You could not act on somebody else's war at all**, short of joining it.

## 2. The four rules

1. **Votes are read, never stored.** `VoteScore` is computed from the six
   dimensions of `Relationship` every time it is called. There is no second
   opinion model to drift out of step with the diplomatic one, and a state that
   was bought off this month votes differently this month.
2. **The veto is the point, not a flaw.** A permanent member can stop anything,
   so a great power cannot be censured while it has a friend in the chamber. It
   is not free: vetoing is public and costs standing with every state that voted
   for the motion.
3. **Calling a vote you lose costs you.** Otherwise the correct play is to table
   a motion every month and see what sticks.
4. **One motion at a time, worldwide** (`AgendaCooldownMonths = 4`). A chamber
   that resolves four things a month is a ticker; the scarcity is what makes a
   seat on the agenda worth 2 CP.

## 3. Seating

`EnsureSeated` fills `PermanentSeats = 5` lazily from
`military + economy + diplomacy × 0.5`, ties broken by country id. Lazy rather
than done in `WorldFactory` so an existing save picks up a chamber on load
without a migration step, and deterministic so the same world always seats the
same five.

**Never revised.** A chamber whose permanent membership tracked this year's
league table would have no grievance in it, and the grievance is the interesting
part. A test runs a decade and asserts the seats do not move.

## 4. The agenda

`AvailableMotions` derives candidates entirely from live world state — the
chamber has no agenda of its own and cannot invent a grievance:

| Motion | Raised when |
|---|---|
| Condemnation (aggression) | The subject initiated a confrontation now at Limited Conflict or above |
| Condemnation (occupation) | `TerritorySystem.OccupiedValue > 20` |
| Condemnation (subversion) | An exposed insurgency sponsorship (spec 16) |
| Sanctions mandate | Two or more states already sanction them, and no mandate stands |
| Relief | The subject's living standards are below 30 |

## 5. Voting

```
loyalty   = (relations−50)×0.45 + (alignment−50)×0.25 + (trust−50)×0.15      // toward the mover
hostility = (45 − relations)×0.55 + max(0, threatPerceived − 45)×0.40        // toward the subject
interest  = −dependenceOnSubject×0.55  − 18 if any treaty  − 22 more if a defence pact
threshold = 14 for a sanctions mandate, 0 otherwise

score = loyalty + hostility + interest − threshold
```

Yes above +12, no below −12, abstain between. The subject votes −100 against
anything but relief. A motion carries on `yes > no && yes >= 3`, unless a
permanent member voted no, in which case it is **vetoed** whatever the count.

The `interest` term is what makes trade dependence a diplomatic asset and not
only an economic one: you do not vote to sanction your own supplier.

## 6. What passing is worth

Deliberately mechanical. A resolution that only printed a line in the chronicle
would be this codebase's oldest bug with a gavel.

**Censure** (`CensureMonths = 24`) — subject reciprocity −8; relations −6 and
threat +4 with every supporter; and `ConfrontationSystem.StrategicPressure` adds
**+8 isolation** while it stands, which is real weight at the settlement table.

**Sanctions mandate** (`MandateMonths = 36`):

- `EconomySystem.SanctionBlowbackFor` — every sender's mitigation × **0.55**.
  This is the reason to spend a month assembling one rather than simply imposing
  measures.
- `EconomySystem.AgeSanctions` — a mandated regime **does not lapse**.
- `EconomySystem.SeekSanctionsReliefBy` — **refused outright**. Otherwise the
  target works the softest member and the apparatus comes apart one relationship
  at a time.
- Immediately: subject economic confidence −6.

**Relief** — every supporter that can afford it pays `ReliefContribution = 280`;
the subject gets the total, living standards `+total/400`, grievance
`−total/600`; contributors gain relations +5 and trust +3 with them.

**The mover**, on a carried motion, gains relations +2.5 and alignment +2 with
every supporter. On a lost one: relations −4 with the state they named, a
memory, and reciprocity −2.

## 7. The world's own motions

`ConsiderMotions` picks **one** mover per opportunity rather than letting every
state try in turn — a loop would simply mean the first state in the list always
spoke. A government counts the room first (`VoteScore` over all states) and only
spends the agenda on something it expects to carry, which is what stops the
chamber filling with motions nobody supports. Costs `MotionPoliticalCost = 2 PC`
and requires treasury above `AISystem.DiscretionaryReserve`.

## 8. Surface

DIPLOMACY — **THE CHAMBER**: the permanent seats named in full, what currently
stands against whom and for how long, the last three votes with their tallies,
and one button per available motion. Refusals carry their reason
(`THE AGENDA IS TAKEN — n month(s)`).

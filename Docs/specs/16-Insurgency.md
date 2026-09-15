# 16 — Insurgency and Proxy War

**Status: as-built.** `Core/InsurgencySystem.cs`, `Data/Insurgency.cs`,
`Assets/Tests/EditMode/InsurgencyTests.cs`. GDD §12, §17.1, §19.

## 1. Why it exists

Every actor in the simulation was a government. Sixteen to twenty-four of them,
each with a cabinet, a treasury and a seat at the table, and the only violence
any of them could do was declared, attributed, and closed with a verdict. There
was no way to bleed a rival without becoming a belligerent — which is the most
characteristic instrument of the era the game is set in, and the thing an
intelligence service is actually for.

The pieces existed and never met:

- `DefenseModel.PopularResistance` was a *number an operation resolved against*.
  Occupied ground resisted when you attacked it and did nothing in the months
  between.
- `pacification` had exactly one producer (the `CounterInsurgency` verb) and one
  consumer (operation odds).
- `publicGrievance` accrued over decades, fed `socialUnrest`, and fed the
  conspiracy that ends governments — but only ever *its own* government. It could
  never cost anybody a province.

## 2. The five rules

Each is a lesson this codebase has already paid for once.

1. **Nobody creates an insurgency.** They arise from conditions the simulation
   already computes. A sponsor finds one and arms it — a different verb from
   making one. There is no `GameController` method that starts a rising, and a
   stable, contented, ungarrisoned country **cannot be destabilised by clicking**
   (the `RegimeSystem` rule).
2. **Support is a target, not a store.** `support` drifts toward
   `SupportTargetFor`, so repairing the conditions ends the movement and nothing
   here ratchets.
3. **Deniability is a wasting asset.** Every consignment is a chance to be traced;
   a long programme will be found. Being found costs *standing* — relations,
   trust, memory, threat perception — never a pillar (the rule
   `IntelligenceSystem`'s exposure block spells out at length).
4. **Actor-generic and reachable.** `SupportBy` takes an actorId and
   `ConsiderSponsorship` runs for every AI government at a real price.
5. **The sponsor never gets the ground.** A victorious occupation rising
   *liberates* the province to its original owner. If arming a movement were a
   route to annexation it would simply be a better war.

Countries are still created in exactly one place. A separatist victory drives the
conditions `SecessionSystem` reads; it does not found a state itself.

## 3. Data

`GameState.insurgencies : List<Insurgency>` — empty is correct on an old save
(the war-verdict reasoning), so **no migration step**.

| Field | Meaning |
|---|---|
| `locationId` | The ground it is rooted in. **Its holder is who it fights** — no target is stored. |
| `cause` | `Occupation` / `Deprivation` / `Separatism`. Decides what would end it, and what winning means. |
| `strength` | 0..100 what it can do. Approaches `StrengthCeilingFor`. |
| `support` | 0..100 who is behind it. Approaches `SupportTargetFor`. |
| `sponsorId` | Secret until `sponsorExposed`. |
| `exposure` | 0..100 attribution risk. |
| `armsSupplied` | Cumulative consignments. Raises the strength ceiling, capped. |
| `fadingMonths` | Consecutive months below the floor; `FadeMonths = 6` ends it. |

## 4. The model

**Emergence** (`PressureAt`, monthly, one draw per location):

```
occupation  = IsOccupied ? 14 + max(0, 60 − pacification) × 0.55 : 0
deprivation = max(0, 42 − livingStandards) × 0.75
            + max(0, socialUnrest − 40)    × 0.55
            + max(0, publicGrievance − 45) × 0.30
separatism  = (not a capital)
            ? max(0, 42 − nationalUnity) × 0.80 + max(0, socialUnrest − 55) × 0.25 : 0

pressure = max of the three; cause = which one
chance/month = (pressure − 30) / 1400,  and 0 below 30
```

At a fully unpacified occupation that is ~1.2%/month: an occupier has a year or
two to make the ground quiet before anybody organises.

**Support target:**

| Cause | Target |
|---|---|
| Occupation | `34 + max(0, 62 − pacification) × 0.62`, and **0 the moment the ground stops being occupied** |
| Deprivation | `max(0, 48 − livingStandards) × 0.95 + grievance × 0.32 + unrest × 0.22 − 12` |
| Separatism | `max(0, 52 − nationalUnity) × 1.05 + grievance × 0.18 − 6` |

A sponsor multiplies the target by **1.28** — a multiplier on an existing cause,
exactly as `RegimeSystem` prices foreign subversion, so money cannot manufacture
a grievance that is not there.

**Strength ceiling:** `support × 0.92 + (sponsored ? 14 + min(18, arms × 0.05) : 0)
− garrison × 0.28`.

## 5. What it costs the holder

Only genuine stores are written monthly (`Bill`): garrison attrition,
pacification suppression, treasury, war exhaustion, and national unity for a
separatist movement.

The treasury line is `intensity × InsurgencyBillPerMonth (24)`, i.e. a
full-strength insurgency costs roughly one month's income (`gdp × TreasuryIncomeRate`,
40–100 for the roster) per month. It shipped at 210 — 6–12× income — which put a
passive-decade Russia at −11,000 on its own; see spec 19 §5 for the same class of
scale bug in hosting costs.

**Everything target-driven is pushed through a target instead**, because a flat
monthly subtraction on any of them is erased by the same month's drift — the trap
that made occupation's readiness cost dead code for a year:

| Helper | Read by | Effect |
|---|---|---|
| `ForceDrag` | `MilitarySystem.MonthlyUpkeep` | Lowers the readiness/supply target, capped at 18 |
| `StabilityDrag` | `GovernmentSystem` stability target | Capped at 16 |
| `UnrestPressure` | `GovernmentSystem` unrest pressure | Capped at 14 |

**`Denies`** — at `strength ≥ 50` the ground pays nobody. `TerritorySystem.Swing`
excludes a contested location from `held` while leaving it in `original`, so a
province in revolt is a loss to whoever owns it *and* to whoever is sitting on
it. This is what makes arming a movement a way to deny a rival an oilfield
without taking it.

## 6. Sponsorship

`ShipmentTreasury = 420`, `ShipmentSupplyCost = 1.6` ground supply,
`SupportCost = 2 CP` (player) or `SponsorPoliticalCost = 1.5 PC` (AI). Arms come
out of somebody's stocks: charging only money would make it the one military verb
with no military cost.

Exposure rises `6 + counterIntelligence × 0.045` per consignment and
`0.6 + counterIntelligence × 0.012` per month a programme merely stands.
Attribution rolls at `exposure/100 × 0.09 + counterIntelligence/4000`.

Being attributed: relations −22, trust −26, threat +18, a diplomatic memory at
weight 1.8, and −3.5 trust with **everyone else**. No pillar cost.

`ConsiderSponsorship` runs for every AI government outside the objective budget
(the `ConsiderDetente` precedent — a standing covert programme is not a strategy
competing for this month's actions). It requires real hostility, a viable
movement, and treasury above `AISystem.DiscretionaryReserve`.

### A sponsor sees a rising through its reporting (2026-09)

`ConsiderSponsorship` read `insurgency.support` and `strength` exactly, where
the player is shown bands unless they hold collection on the holder.
`PerceivedViability(observer, insurgency, holder)` now goes through the
sponsor's Political estimate of the holder: no reporting, nothing seen (−1,
below any bar); coarse reporting quantises both figures to wide bins so two
risings a poor service cannot tell apart read the same; a movement the sponsor
already arms is its own asset and is known exactly. See spec 06 §7d.

## 7. Endings

| Cause | On victory (`strength ≥ 88`, `support ≥ 70`, `garrison < 30`) |
|---|---|
| Occupation | Ground reverts to `originalOwnerId`. Holder: exhaustion +9, war support −11, approval −6 |
| Separatism | Unity −18, stability −12, conspiracy +10 — the conditions `SecessionSystem` reads |
| Deprivation | Approval −12, stability −9, legislative support −8, grievance +6 |

`OnCounterInsurgency` (called from `MilitarySystem` when that verb resolves):
strength −9, **support +1.4**. A purely military answer holds the ground and does
not end the problem.

### 7a. The holder simply leaves (C5)

An occupation rising has a third ending that is neither victory nor suppression:
the holder puts the ground down. `TerritorySystem.RelinquishBy`
([spec 01 §3c](01-Military.md)) sets `ownerId = originalOwnerId`, and **nothing in
it touches `state.insurgencies`**.

That is deliberate. `SupportTargetFor` reads occupation as a cause, so the moment
the ground stops being occupied the movement's support target collapses and it
fades through `FadeMonths` on the existing path. Deleting the rising in the
relinquishment verb would have been a second, special-case ending that skipped
the model — and would have made walking out a way to *erase* an insurgency rather
than to stop causing one.

The rising's bill is also what makes the ground likely to be the one released:
`HoldingBill` counts `strength/100 × InsurgencyBillPerMonth` on top of upkeep, so
a contested province is the most expensive thing a distressed government holds.
And a rising is one of the two things that disqualifies ground from the
"answers our shortfall, keep it" exemption — an oilfield that is burning is not
supplying anybody.

## 8. Surfaces

- **INTELLIGENCE — ARMED MOVEMENTS.** Every movement in the world. Existence is
  public; the quartermaster is not. Strength and support are precise on our own
  ground and **banded** on anybody else's, widened by how thin our network is.
- **MILITARY — the defensive-programmes panel.** A movement on ground we hold,
  with precise numbers, whether the ground is producing anything, and what would
  end it.
- **COMMAND INDEX** — "Arm a movement", refused with a reason when no rising is
  known anywhere.

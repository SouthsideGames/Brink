# 19 — Displacement

**Status: as-built.** `Core/DisplacementSystem.cs`, `Data/Displacement.cs`,
`Assets/Tests/EditMode/DisplacementTests.cs`. GDD §12, §16, §27.

## 1. Why it exists

**Everything bad that happened to a country stayed inside its borders.** A state
could be bombed flat, blockaded into famine and torn apart by an insurgency, and
its neighbours would notice nothing but a market number. A war was a private
arrangement between the two governments fighting it, and the region it was fought
in had no opinion about that. There was no population movement anywhere in the
code: `refugee` appeared in the text of exactly one event.

This is the connective tissue between systems that were already here. War,
insurgency and deprivation displace people; they arrive somewhere; the receiving
state's living standards and unrest move; that feeds the opposition's theme
(spec 18); and the answer — carry them or shut the border — is a decision with a
price on both sides.

## 2. Data

`CountryState.displacement : DisplacementState`. Zero on an old save is correct —
nobody was displaced in a world with no way to displace them — so **no migration
step**.

| Field | Meaning |
|---|---|
| `displaced` | 0..100 share of this country's own people who have left |
| `hosted` | 0..100 share-equivalent this country is carrying for others |
| `bordersClosed` | Whether it is refusing arrivals. A **national** posture, not per pair |
| `monthsClosed` | How long it has been shut |

Border policy is national rather than bilateral deliberately: a government either
accepts people or it does not, and making it bilateral would turn one political
decision into twenty-three administrative ones. Same shape as `CivicPosture`.

## 3. Who leaves

```
war         = at war ? 6 + exhaustion×0.22 : exhaustion×0.06
fighting    = min(22, InsurgencySystem.PressureOn × 0.10)
hunger      = max(0, foodEndowment − foodSecurity − 12) × 0.35
deprivation = max(0, 26 − livingStandards) × 0.55
occupied    = min(14, TerritorySystem.LostValue × 0.05)
```

Zero for a country that is fed, at peace and governed — by construction, the same
discipline as `distress` and `StagnationDrag`. Hunger is measured against the
country's **own endowment**, never an absolute line: an authored food-poor state
has adapted, and an absolute threshold would read its baseline as a permanent
catastrophe (the mistake spec 02 already corrected once).

`displaced` approaches that target at 0.14 rising and 0.04 falling — people go
when they have to and come back when they believe it.

## 4. Where they go

Recomputed each month from the authored map coordinates rather than stored per
pair: every open state within `ReachableDistance = 34` takes a share weighted by
`1 / max(4, distance)`. You inherit your neighbours' problems; that is what a
neighbour is.

**#25 stable geography correction.** Both arrival allocation and the open-region
read behind `PressureAtSource` use state-aware geographic distance. A successor
without an authored country profile is located from its titled ground (spec 01
§3b), not assigned zero distance to every other country. Unknown positions are
out of range. Thresholds, weights, costs and drift rates are unchanged; outcomes
in worlds with successors can change. No new persistent state is introduced.

`hosted` approaches its share at 0.12 rising, 0.06 falling.

## 5. What hosting costs, and what it is worth

| Effect | Route |
|---|---|
| `HostingCostPerPoint = 0.4` treasury/month, paid only from what is there (never drives the balance below zero) | Written directly — a treasury is a balance |
| `StandardsDrag = min(9, hosted × 0.22)` | Living-standards **target** in `GovernmentSystem` |
| `UnrestPressure = min(10, hosted × 0.20)` | Unrest **pressure** in `GovernmentSystem` |
| `industrialCapacity + hosted × 0.010` | Through `Growth.Apply` |

**Hosting is a trade, not a penalty.** People who arrive work, and the labour
shows up slowly. Without that the only correct play would be to shut the border
on day one and the decision would not be a decision.

**The bill is scaled to income.** `EconomySystem` pays roughly `gdp × TreasuryIncomeRate` a
month — 40–100 for the authored roster — so a full load of 100 hosted costs about
one month's income. It shipped at 22 per point (≈70× income): every AI government
closed within four years, the player became the world's only open door, and every
posting under every playstyle was −30,000 to −170,000 by year ten. Because research
is gated on a positive treasury, no strategic instrument was reachable in 224
measured decades. `DisplacementTests.HostingIsABurdenNotABankruptcy` and
`ADecadeOfDoingNothingDoesNotEndInTheRed` guard the scale.

`PressureAtSource` is the other half: unrest at *home* from people who could not
get out, scaled by how much of the region is closed to them. Closing the door has
to be a foreign policy, not a filter.

## 6. The border

`SetBorderPolicyBy` costs `BorderPolicyCost = 2 PC` in either direction. Closing
it costs relations −5 and trust −4 with **every state that is itself carrying or
producing displacement** — they notice who stopped carrying their share. Opening
it pays a smaller amount back.

`ConsiderBorderPolicy` runs for every AI government: a restrictive or
non-elective state has a tolerance of 8 against an open society's 18, and any
government whose treasury cannot carry six months of the bill closes regardless
of its politics. It reopens after twelve months once the load has fallen — without
that the world would shut every border once and never reopen one, which is the
one-way-value bug wearing a policy for a costume.

## 7. Knock-on

`CouncilSystem` gains a second Relief trigger: a state hosting 12 or more can have
the chamber share the cost. That is the case a multilateral body exists for, and
it connects spec 17 to this one without either knowing much about the other.

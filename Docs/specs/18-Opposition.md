# 18 — The Opposition

**Status: as-built.** `Core/OppositionSystem.cs`, fields on
`Data/Government.cs`, `Assets/Tests/EditMode/OppositionTests.cs`. GDD §13, §27.

## 1. Why it exists

The government pillar had an election and no politics. Faction became arithmetic
(spec 05 §2b-1), a chamber could withdraw confidence, and an election resolved as
a roll against incumbency fatigue — but nobody was ever *campaigning*. Nothing
accumulated a case, nothing chose an issue, and the operator had no way to answer
one. The only domestic antagonist was a coup: either nothing was happening, or
the army was in the building.

## 2. The idea

**The theme decides which answer works.**

- A case built on hardship or a war cannot be shouted down. Confronting it makes
  it *worse*, because the thing being denied is visible from every kitchen in the
  country.
- A case built on drift — this government has run out of ideas — is almost
  entirely mood, and mood is what a communications operation is for.
- Conceding always works and always costs something real, chosen to match what
  was conceded.

So the operator is not moving a difficulty slider. They are reading what the
country is actually angry about and deciding whether to pay for it or fight it,
and the wrong instrument is worse than nothing.

## 3. Data

Three fields on `GovernmentState`, zero/`Drift` on an old save (correct: a
government that has not been campaigned against has nothing standing against it),
so **no migration step**.

| Field | Meaning |
|---|---|
| `oppositionCase` | 0..100, drifts toward `CaseTargetFor` |
| `oppositionTheme` | `Drift`(0) / `Hardship` / `War` / `Corruption` / `Liberty` |
| `oppositionMonths` | Consecutive months above `NoiseFloor = 25` |

`Drift` is ordinal 0 deliberately: a save written before oppositions existed
deserializes to the vaguest case rather than to an accusation nobody made.

## 4. The case

```
hardship   = max(0, 48 − livingStandards)×0.85 + max(0, inflation−5)×1.6
           + max(0, unemployment−7)×1.4 + grievance×0.20
war        = at war ? exhaustion×0.75 + max(0, 50 − warSupport)×0.55
                    : exhaustion×0.30
corruption = max(0, 55 − pillars.government)×0.60 + conspiracy×0.25
           + max(0, 50 − eliteCohesion)×0.20
liberty    = restrictive ? 18 + grievance×0.35 + unrest×0.20
                         : max(0, unrest − 55)×0.30
drift      = monthsInOffice×0.055 + max(0, 45 − approval)×0.35
```

The largest wins and becomes the theme. A **restrictive** civic posture multiplies
every theme but `Liberty` by 0.65 — it suppresses the *campaign* while every
underlying cause keeps accruing, the same bargain the posture already makes with
unrest, and the same bill arrives when it relaxes.

The theme only changes when the new grievance exceeds the standing case by 8,
so a campaign does not re-brand itself monthly on noise.

## 5. Where it is read

Three places, so it is a force rather than a readout:

| Reader | Term |
|---|---|
| `GovernmentSystem` legislative-support / cohesion target | `SupportDrag = max(0, case − 25) × 0.42` |
| `GovernmentSystem.HoldElection` | `ElectionDrag = max(0, case − 25) × 0.55` |
| `GovernmentSystem` unrest pressure | `UnrestPressure = max(0, case − 55) × 0.12` |

All three move **targets**, never values — the rule this project has now
re-learned eleven times.

## 6. The two answers

**Concede** (2 PC). Case −26, approval +3, and a cost by theme:

| Theme | Cost |
|---|---|
| Hardship | Up to 900 treasury as a programme; living standards up to +3.5, grievance −4 |
| War | War support −10 (the people being asked to keep fighting heard it), exhaustion −4 |
| Corruption | Elite cohesion −7, government pillar +1.5 through `Growth.Apply` |
| Liberty | **Civic posture drops to Standard.** There is no way to grant this one and keep the instrument. Grievance −6 |
| Drift | Unity +2 |

**Confront** (3 PC). Case `−24 × effectiveness`, grievance up by the *inverse* of
effectiveness — going after the people making an argument hardens the people who
agree with them.

| Theme | Effectiveness |
|---|---|
| Drift | 0.95 |
| Corruption | 0.45 |
| Liberty | 0.35 |
| War | 0.20 |
| Hardship | 0.15 |

Below 0.33 it **backfires**: case +9 and approval −3. Denying something everybody
can see becomes part of the case.

Grievance rather than unrest is the currency on both sides, because
`socialUnrest` is target-driven and a direct write would be erased by the next
tick.

## 7. The world runs it too

`ConsiderAnswer` gives every foreign government the same two verbs at the same
prices above a case of 45. A restrictive or non-elective state reaches for the
confrontation even where it will not work — not a cheat, the characteristic
mistake.

## 8. Surface

GOVERNMENT — **THE OPPOSITION**: what they are campaigning on in words, the case,
how long it has stood, what it is costing in the chamber, and a plain line saying
whether this is an argument that can be denied. Both buttons are always offered;
`CONFRONT THEM` is styled `danger` where it would backfire.

Traffic is PRIORITY, never FLASH: §28.2 reserves FLASH for a turn that cannot be
taken without deciding, and an opposition can be ignored — expensively.

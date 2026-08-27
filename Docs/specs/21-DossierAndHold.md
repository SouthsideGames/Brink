# 21 — The Dossier, and Holding

**Status: as-built.** `UI/Views/DossierView.cs`, `Core/HoldSystem.cs`,
`Assets/Tests/EditMode/DossierTests.cs`, `HoldTests.cs`. GDD §6, §28.1.

## 1. The dossier — why it exists

**Every fact was already in the game and nothing collected them by subject.** To
answer "what is this state actually doing?" an operator visited INTELLIGENCE for
the estimates, networks and armed movements, MILITARY for the balance of forces,
DIPLOMACY for relations, treaties and the chamber, ECONOMY for trade and
sanctions, and CHRONICLE for the history — and assembled the picture themselves,
every time. The GDD promised three information layers; the shell was one flat rail
of tabs, each organised by *our* pillar rather than by their country.

The argument is not tidiness. **Collection had no visible payoff**: buying
intelligence improved numbers scattered across five screens, so the reward for a
decade of patient network-building was diffuse and the pillar felt thinner than it
measures. The dossier is the one page where a well-collected state visibly reads
differently from an uncollected one — same layout, same headings, and half of it
saying NO REPORTING.

## 2. What it shows, and what it is allowed to show

| Section | Source | Fog |
|---|---|---|
| Identity | Government type, leader, faction, war record, traits | Public — who holds office is not a secret |
| What we believe they have | `IntelReadout.ForDomain` ×5 + legend | Estimates only, never a true value |
| Who runs it | `IntelReadout.PersonnelAccessOf` | None / Identities / Assessed |
| What they are signed to | Treaties, bloc, censure, mandate, permanent seat | Public — a signature is observable |
| What they are doing | Confrontations, occupation, **attributed** sponsorship, accession | Secret programmes stay secret |
| Their condition | Insurgencies on their ground, sanctions, displacement | Public — a rising is people in the street |
| What has passed between us | The six relationship dimensions + memory | Ours by definition |
| The record | Chronicle entries, public ones plus our own | Never somebody else's covert file |

**`IntelReadout.PersonnelAccessOf` is shared with the INTELLIGENCE panel.** Two
screens rendering the same dossier with two copies of a threshold is two
thresholds, and the second one drifts — the same reasoning that made
`OperationCatalog.CanOrder` the single gate for both the order screen and the
launch path. `NameThreshold = 20`, `AssessThreshold = 55`, and a compromised
network reports nothing whatever its penetration.

## 3. Holding — why it exists

There was one way to pass time and it was a tap. An operator running a stable
country through a quiet stretch — waiting on a procurement programme, a research
capability, a treaty to bed in — pressed END MONTH, read a briefing with nothing
in it, and pressed END MONTH again, twenty times. That is not pacing, it is a lack
of one, and it makes inattention feel like an accident rather than a choice.

## 4. The three rules

1. **It ends the moment the game needs the operator.** `Interruption` checks, in
   order: an open crisis, any **FLASH** traffic filed this month, a vacant office,
   a war, an emptying treasury (`treasuryTrend < −4` with under `RunwayMonths = 24`
   of runway), an empty one. FLASH is the right tripwire because §28.2 already
   defines it as traffic that cannot be left alone — one definition, read here
   rather than a second list that would drift away from it.
2. **The months are genuinely forgone.** Command Points are not banked beyond the
   ordinary Strategic Reserve, so holding six months means six months of capacity
   nobody spent.
3. **It is graded as what it is.** No XP, no initiative, so the annual evaluation
   sees a held year the way it sees a passive one — and the harness has already
   measured what that costs (`DRIFTER`, −0.22 of a grade).

`CanHold` refuses outright on an open crisis, a vacancy or a war: beginning a hold
on top of an open decision would resolve that decision by lapsing it, which is a
legitimate outcome of ending a month and an illegitimate side effect of asking for
quiet.

Capped at `MaxMonths = 12`. A hold is a decision to stand back from a quiet
stretch, not a way to skip to the end of the save.

## 5. Surface

The control sits on the **BRIEFING**, not the status bar: that bar already carries
a classification, a date, three resource readouts and END MONTH on a landscape
phone, and the briefing is the screen where an operator decides nothing needs them.
The last hold's outcome is printed underneath, because a control that silently
advances four months and stops is indistinguishable from one that is broken.

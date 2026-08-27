# 22 — The Mandate

**Status: as-built.** `Core/MandateSystem.cs`, `Data/Mandate.cs`,
`Assets/Tests/EditMode/MandateTests.cs`, STRATEGIST view. GDD §25 amendment.

## 1. Why it exists

A sixteen-posting, ~1,300-decade playtest (2026-08) had to invent its own
definition of winning — won a war, fired an instrument, graded B — because the
game had none. Annual grades say how a year went. The tenure review arrives
after forty years. The five strategic instruments are means. Nothing ever said
what the operator was *for*, and a player asking "what am I playing toward?"
got a grade letter.

A mandate is what a real posting comes with: three or four claims about the
world the government expects to be true after ten years, written from the
country's authored character and vulnerability (spec 08 §5), and a verdict on
them.

## 2. Rules

1. **Never a conquest checklist** (GDD §25). No mandate asks for foreign ground.
   A military posting is asked to hold what it has, win the wars it fights, or
   keep a rival at arm's length — outcomes, not annexations.
   `MandateTests.AMandate_NeverAsksForForeignGround`.
2. **Judged from the world, not the path.** Every objective is a test against
   state at review time (`MandateSystem.IsMet`). How the operator got there is
   the annual evaluation's business.
3. **The save always continues.** The verdict is a record and a moment; the
   tenure review still comes at forty years.
4. **Every posting has one.** Sixteen are authored in `MandateCatalog`; the
   expansion roster derives one from its traits (`Derived`) so no posting opens
   without a purpose. `MandateTests.EveryPosting_OpensWithAMandate` covers the
   Full roster.

## 3. Data

`GameState.mandate : Mandate` — title, brief, objectives, `reviewMonths = 120`,
and the bases growth claims are measured from (`startGdp`, `startLocationIds`,
`startGovernmentType`), captured at assignment. `GameState.mandateRecord :
MandateRecord` — the verdict, met/total, and the review text. Both null on an
old save; `MonthlyUpdate` assigns a mandate at the next month, so **no
migration step**.

Objective kinds (`MandateObjectiveKind`): pillar / stability / approval / unity
/ energy / food / materials / industry thresholds, treaties held, hold original
ground, GDP growth since arrival, wars won without loss, no war lost, an
instrument used, relations with a named state at least / at most, capabilities
held, solvent, constitutional order kept.

## 4. The verdict

At `reviewMonths` since `startDate`:

```
met == total        → FULFILLED   +400 XP, approval +8, stability +4
met × 2 ≥ total     → HELD        +150 XP
otherwise           → FAILED      approval −8
```

FLASH `MANDATE REVIEW` with each undertaking marked MET / NOT MET; a public
chronicle line; the record stays on the STRATEGIST panel.

## 5. Authoring

Each Standard-roster mandate is written from that country's spec 08 §5 line —
Germany is asked to secure energy and materials and keep its industrial base,
Nigeria to hold stability and unity and keep the constitutional order,
Kazakhstan to stay on terms with *both* larger neighbours and stay intact. A new
country needs either a `MandateCatalog.For` case or a trait the derived mandate
can read.

Objective text is the line the operator reads; write it in the terminal's
voice and as a claim about the world ("Energy security at 55 or better"), never
as an instruction.

## 6. Surfaces

- STRATEGIST panel: brief, each undertaking marked as it stands, the verdict once
  delivered.
- **The record closes on screen** (2026-08-27): the verdict — and the forty-year
  tenure review — take the briefing overlay
  (`TerminalShellController.ShowRecordClosedIfDue`), ACKNOWLEDGE, then the
  month's briefing. `AudioDirector.Sync` plays the positive or negative outcome
  cue for it.
- A `YOUR_MANDATE` tutorial step points a new operator at it.

## 7. Open

- Mandates are fixed at assignment; a mid-tenure administration change (spec 05)
  could plausibly reissue one.

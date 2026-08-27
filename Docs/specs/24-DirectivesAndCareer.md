# 24 — Standing Directives and the Career Record

**Status: as-built (2026-08-28).** `Core/StandingDirectiveSystem.cs`,
`Data/StandingDirective.cs`, `Core/CareerRecord.cs`,
`Tests/EditMode/StandingDirectiveTests.cs`, `Tests/EditMode/CareerRecordTests.cs`,
STRATEGIST view. GDD §29, §25.

## 1. Standing directives (GDD §29)

§29 asked for *optional strategic directives* — the government, the Cabinet or
circumstances suggesting something worth doing (reduce energy dependence,
restore readiness, settle a frontier) that improves XP and the evaluation. It
was the one §29 item still unbuilt. (`DirectiveSystem`, the CABINET RECOMMENDS
block, is advice about *verbs*; this is an *undertaking* with a deadline.)

Rules:

1. **Suggested, never imposed.** PRIORITY traffic and a block on STRATEGIST.
   Ignoring one costs nothing; a lapse is an ADVISORY and a line in the record.
2. **Circumstances pick them.** Every catalogue template has an eligibility
   test on the world and sets its threshold from the *current* value
   (`Reach(current, step)`), so the directive is a reach, never a box already
   ticked.
3. **The mandate's evaluator judges them.** The condition is a
   `MandateObjective`; `MandateSystem.IsMet` decides.
4. **At most two standing**, a new one no more than every four months, none
   before month two, and a template is not re-offered within three years.

Catalogue (ten): energy independence, restore readiness, settle the frontier,
balance the books, steady the country, find a partner, build the industrial
base, field a capability, feed the country, win the public back.

Reward: `rewardXP` (120–160), one `RecordInitiative`, and
`directivesCompletedThisYear × 6` in the evaluation's initiative component
(spec 07). `GameState.standingDirectives` persists; `lastDirectiveOfferMonth`
paces offers. Empty list on an old save is correct — **no migration step**.

## 2. The career record

Every posting used to end with its save and nothing else. `CareerRecord`
keeps `career.json` beside the save slots: one entry per (seed, posting) with
the mandate verdict, years served, mean grade, war record, difficulty and
world size. Written by `MandateSystem` at the verdict, by
`ProgressionSystem.DeliverTenureReview`, and refreshed on every autosave so an
abandoned posting still shows what it was.

**It is a record, not progression.** Nothing in it is read by the simulation;
a second game starts exactly as the first did. STRATEGIST shows the last six
postings under CAREER, the current one marked ►.

## 3. Mandate reissue

A new administration (spec 05) may reissue the mandate: if more than five
years remain to the review, `MandateSystem.Reissue` replaces the objectives
with a fresh derived set weighted to the incoming leader's national priority,
keeps the review date and the original bases, and files `MANDATE REVISED`.
Within five years the brief stands — a government that arrives in year eight
inherits its predecessor's undertakings.

## 4. Difficulty is chosen

`NewGameFromAssessment` took no difficulty and every real game ran at
**Standard** while every balance figure was measured at **Challenging**. The
assessment screen now offers STANDARD / CHALLENGING / RUTHLESS beside the world
size (default Challenging), and the record stores which.

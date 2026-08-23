# Vertical Slice Validation Report

GDD §34, Phase 12. Ten simulated years, four countries, all systems live.
Regenerate with the `Report_VerticalSliceBalance` test.

## Method

Six scripted operators play the same seed for 120 months at Challenging
difficulty, using the real player-facing APIs (spending Command Points,
Political Capital and Influence). PASSIVE is the control: it ends every month
without doing anything, which is legal play since delegation is legitimate.

## Result (seed 20260820, current)

| Playstyle | Avg grade | XP | SP | Decisions | CP-dry months | Pillars | GDP |
|---|---|---|---|---|---|---|---|
| PASSIVE | 2.50 | 1005 | 25 | 7 | 0 | 429 | 3644 |
| MILITARY | 2.70 | 1325 | 27 | 28 | 6 | 427 | 3481 |
| ECONOMY | 2.50 | 1101 | 25 | 328 | 42 | 441 | 3602 |
| INTELLIGENCE | 2.80 | 2434 | 28 | 260 | 67 | 355 | 3642 |
| DIPLOMACY | 2.30 | 1371 | 23 | 79 | 98 | 429 | **3837** |
| GOVERNMENT | 2.70 | 1135 | 27 | 15 | 0 | 450 | 3701 |

Military play runs 12 joint exercises and 3 confrontations per decade, paying for
the training in treasury (GDP 3481 versus 3644 for drift).

Grade scale: F=0 D=1 C=2 B=3 A=4 S=5.

World after a decade: capability spread preserved (US MIL 92 / China ECO 92 /
Russia ECO 46), market indexes 118–160, 163 chronicle entries, four treaties,
one confrontation, foreign leadership turned over.

## Problems found and fixed

1. **Capability saturated.** Every major power pinned at ~100 within ten years,
   leaving nothing to play for in a decades-long save. Fixed with `Core/Growth.cs`
   — gains taper as a pillar nears its ceiling; losses are never damped.
2. **Market index ran away.** Compounding drift took the index from 100 to 1192
   in a decade, making the ASCII chart useless. Replaced with a
   fundamentals-anchored model that reacts to shocks but stays bounded.
3. **Doing nothing scored a perfect S every year.** The evaluation only measured
   national trajectory, which autonomous officials deliver on their own. Fixed by
   damping the trajectory/economy weights, raising grade bands, and adding an
   **initiative** component that credits what the operator personally
   accomplished. A delegated year still passes (C+); top grades must be earned.
4. **Evaluation XP dwarfed decision XP**, so a decade of war earned no more than
   a decade of silence. Rebalanced.

## Follow-up work completed after the first report

5. **Military play had nothing to do between confrontations** (18 decisions per
   decade). Implemented joint exercises (GDD §15.3): readiness, interoperability
   and trust bought with exposure, losses that still teach, and doctrine
   knowledge retained after a friendship ends. Decisions rose to 31 and the
   military grade from 2.60 to 2.70.
6. **Exercises were spammable** — the first pass ran 210 in a decade. Added a
   nine-month per-partner cooldown, giving 12 per decade.
7. **The world was too quiet** — one confrontation per decade. Tuned AI claim
   assertion (hostility itself now invites pressure, and thin reporting makes a
   government hesitant rather than paralyzed). Now 2–3 per decade.

## Follow-up round two

8. **Alliance obligations now fire** (`Core/AllianceSystem.cs`). Reaching Limited
   Conflict calls in every defense commitment held by the defender; honoring
   creates a defender-led coalition, repudiating breaks the treaty and costs
   trust with every third party. When the player is the signatory it becomes a
   blocking Crisis Turn.
9. **Events are now eligibility-driven** (`Core/EventCatalog.cs`). Nine authored
   crises, each declaring the world conditions that make it possible, with
   weights and per-definition cooldowns. A genuinely stable state now has quiet
   months instead of manufactured drama.
10. **Diplomatic outreach was a free, repeatable action worth zero XP.** It now
    awards 6 XP and applies diminishing returns, so courtesy calls cannot
    manufacture an alliance.
11. **The annual evaluation gave no credit for treaties or alliances.** Strategic
    position now scores treaty and defense-pact gains alongside territory.

## Multi-seed balance (5 seeds × 10 years)

Single-seed tables are noise. Regenerate this one with the
`Report_MultiSeedBalance` test, and tune against it rather than the table above.

| Playstyle | Mean grade | Mean XP | Mean decisions | Mean GDP | vs passive |
|---|---|---|---|---|---|
| PASSIVE | 2.60 | 1024 | 7 | 3392 | — |
| MILITARY | 2.50 | 1065 | 16 | 3489 | −0.10 |
| ECONOMY | 2.60 | 1096 | 325 | 3330 | 0.00 |
| INTELLIGENCE | 2.92 | 2537 | 263 | 3391 | **+0.32** |
| DIPLOMACY | 2.28 | 1369 | 81 | **3878** | −0.32 |
| GOVERNMENT | 2.72 | 1142 | 17 | 3419 | +0.12 |

### Mean evaluation components — where grades actually come from

| Playstyle | TRAJ | ECON | STAB | POS | CRIS | INIT |
|---|---|---|---|---|---|---|
| PASSIVE | 55.8 | 59.6 | 57.3 | 49.7 | 70.4 | 43.2 |
| MILITARY | 55.8 | 59.7 | 57.6 | 50.3 | 69.4 | 44.1 |
| ECONOMY | 57.6 | 53.7 | 57.0 | 46.5 | 70.1 | 45.4 |
| INTELLIGENCE | 43.9 | 59.6 | 57.1 | 49.7 | 70.4 | **78.0** |
| DIPLOMACY | 55.8 | **67.6** | 56.6 | **56.2** | 68.6 | 41.4 |
| GOVERNMENT | 58.8 | 60.2 | 59.5 | 49.7 | 70.4 | 45.5 |

This table is the most useful diagnostic in the project — it shows *which*
component drives each playstyle, and it is how the crisis-prevention flaw below
was found.

## Follow-up round three

12. **Preventing crises scored worse than having them.** A crisis-free year
    scored 50 while a year of facing and resolving crises scored 75, so good
    governance that kept trouble from starting was penalized. Quiet years now
    score 68 — prevention is worth nearly as much as competent response.
13. **Acting was under-rewarded relative to its cost.** Operations, exercises and
    subsidies all spend treasury and stability, which the other components
    penalize. The initiative component's weight rose from 0.12 to 0.20.
14. **The diplomacy validation bot was breaking treaties to "upgrade" them**,
    repudiating standing commitments and tanking trust with every state watching.
    Removed — and worth remembering as a design note: there is no treaty
    *amendment* path, only signing and breaking.

## Follow-up round four — the initiative metric

15. **Initiative was inferred from an XP threshold of 10**, so it measured which
    pillar happened to award large XP per action rather than how engaged the
    operator was. Intelligence scored 78/100 (covert ops award 12) while
    diplomacy scored 41 — *below passive* — despite 80+ decisions a decade,
    because outreach awards 6. Now recorded explicitly at 27 decision points.

**Result:** diplomacy moved from −0.32 to **+0.22** against passive, and four of
five playstyles now beat drift.

| Playstyle | Before | After | INIT before → after |
|---|---|---|---|
| ECONOMY | +0.02 | **+0.48** | 45.4 → 78.0 |
| INTELLIGENCE | +0.32 | +0.34 | 78.0 → 78.0 |
| DIPLOMACY | **−0.32** | **+0.22** | 41.4 → 57.5 |
| GOVERNMENT | +0.12 | +0.14 | 45.5 → 43.7 |
| MILITARY | −0.10 | −0.14 | 44.1 → 43.6 |

## Known weaknesses (not blocking, worth addressing before content expansion)

- **Military is the last playstyle below passive (−0.14).** Its initiative score
  (43.6) reflects a genuine content gap rather than a metric flaw: confrontations
  are one-at-a-time, exercises sit on a nine-month cooldown, and there is little
  else for a military operator to *do* in peacetime. It needs more verbs —
  posture management, force restructuring, procurement decisions.
- **XP is inflated by repeatable actions.** The economy playstyle earns ~5× the
  military's XP by repeating direct-control actions. Harmless today because XP
  only drives the level display, but it would matter if XP ever gained mechanical
  weight.
- **Grade spread is clustered near band boundaries**, which makes mean grades
  jumpy — a playstyle with better mean components can still average a lower grade
  through bucketing. Worth revisiting the band widths.
- **Government play still has low decision density** (18 per decade). Its
  instruments are Political Capital rather than Command Points, so it acts less
  often by design — but it likely needs more verbs.
- **Intelligence XP is roughly twice other pillars'** because covert operations
  are repeatable at will. Consider diminishing returns per target.
- **The economic playstyle underperforms** (2.50, level with drift). Sanctions
  cost as much as they deliver against these four countries; worth revisiting
  when the roster expands and dependencies get more asymmetric.

## Verdict

The loop holds together mechanically: no degeneracy, no NaN, no stalls, saves
resume seamlessly, the decade is reproducible, every pillar is viable, and no
playstyle dominates. The remaining weaknesses are content and tuning, not
architecture — the slice is sound enough to build on.

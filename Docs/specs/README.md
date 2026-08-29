# Phase 2 System Specifications

Required by GDD §36 before full content production. Written **after** the
vertical slice was built and validated, so each document describes what the code
actually does — with real constants and formulas — rather than what was hoped
for. Where a specification describes something not yet implemented, it is marked
**PLANNED**.

Use these when expanding content or changing a system's balance. **If you change
behaviour, update the spec in the same commit** — the two specs that fell out of
date (08 documenting four countries against sixteen in code, 11 documenting nine
crises against fifteen) both drifted because that rule was skipped.

| # | Specification | Covers | Status |
|---|---|---|---|
| 01 | [Military](01-Military.md) | Branches, readiness, logistics, locations, territory yields, operations, confrontations, primary strategy, exercises | As-built |
| 02 | [Economy](02-Economy.md) | Macro model, sectors, trade, sanctions, market index | As-built |
| 03 | [Intelligence](03-Intelligence.md) | Collection, estimates, confidence, deception, covert action | As-built |
| 04 | [Diplomacy](04-Diplomacy.md) | Relationship dimensions, treaties, coalitions, foreign basing | As-built |
| 05 | [Government](05-Government.md) | Government types, constitutional authority, elections, succession, political capital, regime change | As-built |
| 06 | [AI](06-AI.md) | Objectives, scoring, rivalry, perception, deception, difficulty, player assessment | As-built |
| 07 | [Progression](07-Progression.md) | XP, annual evaluation, skill trees | As-built |
| 08 | [Country Bible](08-CountryBible.md) | Authoring format, the 16-country roster, locations, trade, adding a country | As-built |
| 09 | [UI/UX](09-UIUX.md) | Panel scaling, measured columns, text policy, readability, palettes, display settings | As-built |
| 10 | [Save & Data Schema](10-SaveSchema.md) | Persistent state, versioning, migration, determinism, cloud | As-built + plan |
| 11 | [Events & Crises](11-EventsCrises.md) | Triggers, the 15 authored events, Crisis Turn rules | As-built |
| 12 | [Balance & Telemetry](12-BalanceTelemetry.md) | Validation harness, tuning levers, bugs caught, known issues | As-built |
| 13 | [Technology](13-Technology.md) | Research programmes, capabilities, diffusion | As-built |
| 14 | [Strategic Endgames](14-Endgames.md) | Decisive instruments, preparation, consequences, AI use, detection | As-built |
| 15 | [Cabinet & Reporting](15-CabinetReporting.md) | Every government's five officials; control modes, Influence, directives, Direct Control, what the operator is told | As-built |
| 16 | [Insurgency](16-Insurgency.md) | Armed movements, where they come from, sponsorship and attribution, contested ground | As-built |
| 17 | [The Chamber](17-Council.md) | Multilateral motions, voting, the veto, censure and sanctions mandates | As-built |
| 18 | [The Opposition](18-Opposition.md) | The case against a government, themes, conceding and confronting | As-built |
| 19 | [Displacement](19-Displacement.md) | Who leaves, where they go, what hosting costs, the border | As-built |
| 20 | [Blocs](20-Blocs.md) | Standing sides with names: joining, cohesion, upkeep, defection | As-built |
| 21 | [Dossier & Hold](21-DossierAndHold.md) | The per-country deep terminal, and standing back through quiet months | As-built |
| 22 | [The Mandate](22-Mandate.md) | What a posting is for: objectives, the ten-year verdict, reissue | As-built |
| 23 | [Audio](23-Audio.md) | Cues, music contexts, the mixer, and what drives them | As-built |
| 24 | [Directives & Career](24-DirectivesAndCareer.md) | Standing directives, the career record across postings | As-built |
| 25 | [Content Update](25-ContentUpdate.md) | Fiscal statecraft, intel products, episodic diplomacy, government content, research expansion | Built; plan of record |

Spec 25 is a special case: **all its tranches are built** (2026-08-29) and the
as-built detail lives in the pillar specs it fed — A in 02 §9, B in 03 §6a–§10,
C in 04 §5b–5g, D in 05 §2e–2g, E in 13 §6. It is kept as the record of the plan
and of what was deliberately *not* followed. **Where spec 25 and a pillar spec
disagree, the pillar spec is right.**

Coverage of the GDD as a whole — what is done, partial, missing and
contradicted, section by section — is tracked separately in
[`../GDD_Coverage.md`](../GDD_Coverage.md), which was last audited 2026-08-22
and now predates a good deal of the code. Returning to the project after a
break: start from [`../PickUpHere.md`](../PickUpHere.md).

## Conventions used throughout

- **Ranges.** Pillars, resources and most social values are `0..100` floats.
  Money (treasury, GDP) is unbounded positive. Rates (growth, inflation,
  unemployment) are annualized percentages and may go negative where meaningful.
- **Determinism.** Every random draw seeds from `state.rngSeed` combined with the
  month index and a stable identifier, so a save replays identically. Never use
  `UnityEngine.Random` or `DateTime.Now` in simulation code, and hash strings
  with `Core.Hash.Of` rather than `string.GetHashCode()` — the latter is not
  contractually stable across processes, so a save could silently fork from the
  session that wrote it. Anything that can happen twice in one month must also
  mix in `GameState.NextActionSequence()`.
- **Actor-generic APIs.** Systems expose `*By(...)` variants taking an actor id
  that skip player Command Point costs; the player wrapper spends CP and
  delegates. AI must use the `*By` variants.
- **Player-only effects.** Anything derived from Strategist skills must check
  `id == state.playerCountryId` so operator training never leaks into AI states.
- **Fog discipline.** UI and AI must never read a foreign country's true values.
  Route through `IntelligenceSystem.GetEstimate` / `UI.IntelReadout`.
- **Mean reversion.** A monthly tick that decrements a value must leave a
  *reachable* recovery path under the same conditions, and a non-player state
  must be able to reach it. Nearly every bug in the audit sweep (spec 12 §4) was
  one of those two omissions.
- **Terminal width.** No view may hardcode a column count; use
  `TerminalMetrics.Columns` (spec 09).

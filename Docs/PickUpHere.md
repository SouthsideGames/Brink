# Pick up here

Written 2026-08-29, on stepping away from Brink to finish another game.
This is the landing page for coming back. Read it before `CLAUDE.md` — that
file is the accumulated design record and is long; this one is short and says
where the work actually stands.

---

## 1. Read this first: the last commit on `main` was a clobber, and it is undone

**You do not need to do anything about this. It is already fixed.** It is
recorded here because the history looks alarming if you meet it cold.

`f6f463f` ("8/29") was pushed at 01:57 on 2026-08-29, **two and a half minutes
after PR #7 was merged** at 01:54. Its tree is byte-identical to `7c6504b`
(Aug 27) — the state of the repository *before* both PR #6 and PR #7. It was a
push from a working copy that had never pulled either merge, so it reverted
~10,500 lines of merged, green work in a single commit called `8/29`.

What it removed:

| | |
|---|---|
| PR #6 | Multi-party alliances, and the wars they cascade into |
| PR #7 | The content update — tranches 0 / A / B / C / D / E |
| Deleted outright | `FiscalSystem`, `IntelProductSystem`, `BelligerentRoster`, `FiscalState`, `IntelProduct`, spec 25, and five test fixtures |
| Reverted in place | ~90 files, including 564 lines of `CLAUDE.md` and seven specs |

Undone in two commits:

- **`3399f57`** — plain revert of `f6f463f`. The tree is now identical to
  `5458ad1`, the merge of PR #7. Nothing was rescued from `f6f463f` because it
  introduced nothing; its tree matched Aug 27 exactly.
- **`9c50d46`** — restores four partition entries in `Tools/run-suite.sh`
  that a *different* accident had already eaten (see §3).

**The lesson worth keeping:** the damage was invisible in the tooling. The
commit was green, the message was a date, and the diff was only alarming if you
looked at it. `git pull` before `git commit -a` on this repo, and if a commit
message is a bare date, check `git diff --stat` against the previous commit
before pushing it.

---

## 2. What state the game is in

The tree at HEAD is the union of everything through PR #7. In feature terms
this is the most complete Brink has ever been:

- **Five pillars**, each with real verbs and a world behind them.
- **The content update landed in full** (spec 25, all tranches built). Verb
  counts across the update: economy 6 → 13, intelligence 7 → 12, diplomacy
  10 → 16, government 22 → 24, research capabilities 13 → 33.
- **Multi-party alliances** — blocs carry commitments, `GuarantorsOf` is the one
  definition of who is obliged, honouring opens a real war, and it cascades.
- 25 specs in `Docs/specs/`, 24 of them as-built.

### What is verified, and what is not — this is the important part

| Tree | Suite | Balance |
|---|---|---|
| `f36809e` (Tranche E) | **1258 tests, green** | Measured, 5 seeds, frozen tree |
| `5458ad1` → HEAD (the union) | **never run** | **never measured** |
| + causal explainability (spec 26) | **1312 tests via the dotnet harness; 30 failures, all pre-existing** | n/a by design |

Two specific gaps:

1. **`MultilateralAllianceTests` (28 tests) has never run.** PR #6's own
   `CLAUDE.md` entry says so plainly: *"Not verified by a test run — no Unity or
   C# compiler in the environment this was written in."* That is still true.
2. **The union has never been run as a whole.** Tranche E's green suite was the
   content-update branch *before* it took main's alliance work. The two halves
   are each individually plausible and have never met in a test run.

There are ~1,300 test methods on disk across 90 fixtures. Treat the current
tree as **unverified** until the suite runs.

The causal explainability layer (spec 26) **has** now been compiled and run —
not in Unity, which is still absent here, but through
[`Tools/dotnet-harness/`](../Tools/dotnet-harness/README.md), which builds the
runtime and the test assembly against a shim for the slice of UnityEngine this
project uses. 1312 tests, 1282 passed, 30 failed. The same harness on the commit
before Phase A gives 1285 / 1255 / 30 with a **byte-identical failure set**, so
every one of those 30 predates it.

**That is not the same as a Unity run.** 23 of the 30 are the harness having no
asset pipeline — every stylesheet, palette, touch-target and audio test — and
they say nothing about the code. The other 7 are real and pre-existing, and six
of them are things "Recommended next" in `CLAUDE.md` already names as unmeasured.
**`bash Tools/run-suite.sh` in Unity is still the first thing to do**, and the
seven real failures are the first thing to look at when you do.

The last balance table (Tranche E, 5 seeds) — **it predates the alliance
cascade, so treat it as the previous game**:

```
PASSIVE 2.54 baseline
DIPLOMACY +0.54   ECONOMY +0.44   MILITARY +0.38
INTELLIGENCE +0.38   GOVERNMENT +0.30   DRIFTER +0.08
```

---

## 3. The first thing to do when you sit down

```
bash Tools/run-suite.sh          # Unity editor must be CLOSED
```

Expect it to take a while and expect failures — nothing has compiled since the
merge. Work them to green before starting anything new.

**Before `9c50d46` this command aborted without running a single test.** The
merge `113a589` resolved a conflict on `run-suite.sh`'s single-line `PART_A`
definition by taking main's side: that gained `MultilateralAllianceTests` and
silently dropped the four the content update had added (`CorruptionTests`,
`RecognitionAndMediationTests`, `FiscalTests`, `IntelProductTests`). The test
*files* merged fine — only their registration was lost. `verify_coverage` caught
it exactly as designed, by scanning the test directory and refusing to run.

That is the same failure as §1 in miniature: **a line-level merge conflict in a
list, resolved by keeping one side.** `run-suite.sh`'s partitions, the
`SimulationPipeline` system list and `ActionCatalog` are all single lists that
several branches touch — check them by hand after any merge.

Two standing hazards when running the suite, both of which fail *silently*:

- **Never put more than ~20 fixtures in one `-runTests` invocation.** The editor
  session ages; past ~1,050 tests a single full run never finishes.
  `run-suite.sh` partitions for this reason.
- **A killed run leaves a complete-looking results file.** `extract-failures.sh`
  requires Unity's clean-shutdown line and refuses to report a pass when the log
  contains `error CS`.

---

## 4. Then, in priority order

1. **Re-measure balance.** `Report_MultiSeedBalance` (five seeds, per-component
   breakdown — never the single-seed report). The table above predates the
   alliance cascade entirely.
2. **Measure the cascade.** Bloc pacts plus obligation cascades can produce
   multi-belligerent wars the harness has never seen, and `MaxCommitment` no
   longer caps a state's total fronts — only the ones it opens itself. Watch
   `TheatreSystem.TotalCommitment`, and compare against the pinned **92
   confrontation-months per unattended decade**.
3. **The world sanctions itself into a permanent depression.** Measured,
   confirmed, **not fixed** — and pre-existing, not caused by any tranche.
   `state.sanctions.Count` only ever grows: AI states impose faster than the
   36-month review and `SeekSanctionsReliefBy` remove, so pressure ratchets
   worldwide, and the market index subtracts `sanctionPressure × 9`.

   ```
   month   ruined   mean index   sanctions   mean pressure
       0        0        100.0           0            0.00
     240        8         33.7          63            2.05
     480    15/16         16.6         100            2.54
   ```

   Across six seeds, 36 of 43 ruined states were still ruined twenty years
   later with no intervention. **Do not fix this by raising the index floor —
   the floor is the symptom.** Candidates worth measuring, in order: whether AI
   states impose too readily, whether the 36-month review actually fires,
   whether `sanctionsTruceMonths` détente is reachable often enough to matter.
   **The diagnostic is the sanction count over time, not the index.** Re-run
   `Tools/Recovery.cs` against any change.
4. **World heat is flat at 1.25 AI wars per 30-year world** (eight seeds:
   1,1,4,0,0,3,1,0), unchanged by Tranche C. The reason is structural, not
   tuning: recognition needs a secession (rare) and mediation is player-only, so
   neither gives a foreign government a new reason to collide with another one.
   If the world is to be warmer, the honest lever is **a cause the AI can act on
   that is not resource desperation** — `ResourcePrize`'s energy/materials-below-40
   gate is what the economy repair closed off. `Tools/Wars.cs` is the probe.
5. **Balance on Regional (10) and Full (24) world sizes is unmeasured.** Every
   figure is the Standard 16-state world. Also worth deciding whether the
   harness playstyles should run on Full at all — eight more states change
   coalition and sanction arithmetic.
6. **Re-audit `Docs/GDD_Coverage.md`.** It is dated 2026-08-22 and predates
   insurgency, the chamber, the opposition, displacement, blocs, the mandate,
   multi-party alliances and the whole content update. Several entries are
   already hand-marked stale. Regenerate it by re-auditing against the code —
   never edit it to match intentions.
7. **Touch-target layout cost on a real device.** Buttons went to 44 panel px
   and grew ~30% taller; nobody has looked on hardware. MILITARY (domain tabs +
   up to 7 verbs + the defensive panel) and GOVERNMENT (four control rows) are
   the two screens to check first.
8. **Diminishing returns on repeated covert ops.** Long-standing and niche.

**Closed, do not re-add to this list:** an AI route to a foreign food ceiling —
Tranche E's `CAP_AGRI` closed it (read in `EconomySystem`), and it appeared
twice on the old list after it was already done.

---

## 5. Things that are decisions, not gaps

An audit will keep re-proposing these. They were each settled deliberately and
the reasoning is in `CLAUDE.md`; do not rebuild them.

- **AI states get no Influence economy and no Crisis Turns.** Both are *operator
  interfaces*, not government machinery. The rule the AI is held to is symmetry
  of *consequence*, never symmetry of interface.
- **Reporting is "missed or buried", never distorted.** A weak desk can lose an
  item or strip its urgency; it may not report a wrong number.
- **No manual save slots.** Autosave only, per GDD §30 — consequences stick.
- **Telemetry is local only.** No network call, no identifier, no SDK. Adding
  transmission needs informed consent this project has not asked for.
- **Conquest counts in the annual grade** (2026-08-28, supersedes GDD §25).
  Mandates still never ask for foreign ground.
- **Roster is real countries** (supersedes GDD §2/§31.1). Baselines are gameplay
  archetypes, not real-world claims, and the flashpoint is a deliberately
  generic "Contested Sea Lane".

`Docs/GDD_Coverage.md` also carries a "Decisions, not gaps" section listing nine
done-by-decision items. Read it before believing any gap list.

---

## 6. Orientation, if it has been a while

- **`CLAUDE.md`** — the design record and the working rules. Long, and the top
  ("Build phases", "Architecture rules") plus the bottom four sections
  (terminal layout, readability, Android, running tests, conventions) are the
  parts that constrain new code.
- **`Docs/GDD_v1.1.md`** — source of truth for design. `GDD_v1.0.md` sits beside
  it as the unedited historical record; v1.1 wins where they differ.
- **`Docs/specs/`** — 25 as-built system specs. `README.md` there is the index.
  **If you change a system's behaviour, update its spec in the same commit.**
- **`Docs/AndroidTestChecklist.md`** — device test checklist.

The recurring bug families this project has actually shipped, worth re-reading
before writing a monthly tick:

1. **One-way values.** A value that decrements with no *reachable* recovery path
   under the same conditions, for a *non-player* state as well. Shipped at least
   eleven times.
2. **Value versus target.** A recurring cost must move the target, not the
   value, or the same tick's drift erases it.
3. **Written but never read** — and its inverse, read constantly but quietly
   incomplete.
4. **The ladder.** Score every instrument; whatever sits on top of a fixed
   ladder is the only thing that ever runs.
5. **A harness that does not run the thing it validates**, which is worse than
   no harness because it is trusted.

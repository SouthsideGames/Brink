# Brink ("Unknown Game")

Persistent geopolitical strategy simulation presented as a retro classified
government command terminal. Landscape mobile (iOS/Android), Unity 6
(6000.3.9f1), UI Toolkit / UI Builder. Single-player, offline-first.

**Source of truth for design:** `Docs/GDD_v1.0.md` (extracted from
`Unknown_Game_Consolidated_GDD_v1.0.docx`). Read it before designing any system.

## Repository layout

- `Brink/` — the Unity project (open this folder in Unity).
- `Docs/` — design documents.
- Runtime code: `Brink/Assets/Scripts/` (asmdef `Brink.Runtime`, root namespace `Brink`).
  - `Data/` — serializable simulation state (GameState, CountryState, GameDate, ...).
  - `Core/` — plain-C# systems (TurnManager, SaveSystem, GameController, GameLog)
    plus `GameBootstrap` (the only scene-independent Unity entry point).
  - `UI/` — UI Toolkit controllers.
- UI assets: `Brink/Assets/Resources/UI/` (UXML/USS/TSS loaded by `GameBootstrap`
  at runtime — there is **no scene wiring**; any scene boots the game).
- Tests: `Brink/Assets/Tests/EditMode/` (asmdef `Brink.Tests.EditMode`).

## Architecture rules

- Simulation code is plain C# with no UnityEngine scene/component dependencies
  (UnityEngine used only for JsonUtility/Debug) so it is edit-mode testable and
  can run long headless simulations (GDD Phase 9 requires unattended runs).
- All persistent state lives in `GameState` (single-object JSON save).
  Bump `SaveSystem.CurrentSaveVersion` on breaking schema changes.
- Slot 0 is the autosave; the game autosaves after every resolved month.
- Every significant system must expose debug/test controls (GDD §34.1).
- New systems require edit-mode tests before the phase is considered locked.

## Build phases (GDD §34) — status

- [x] Phase 0 — Foundation: data models, game state, turn manager (monthly loop,
      CP with strategic reserve, year-end hook), save/load, logging, debug shell.
- [x] Phase 1 — Command Terminal UI: responsive shell (compact/medium/large
      breakpoints via `Breakpoints`, safe-area aware), JetBrains Mono (OFL),
      ASCII presentation helpers (`AsciiChart`), view system
      (Briefing / 5 pillar dashboards / System debug console).
- [x] Phase 2 — Turn + CP loop: notification classes (FLASH..ARCHIVE) in
      `GameState.notifications`, Crisis Turns (`CrisisSystem` with authored
      catalog + deterministic systemic trigger, blocks End Month, modal overlay
      UI), priority traffic on the Briefing, FORCE CRISIS debug control.
      Escalation premium deferred to confrontations (Phase 4).
- [x] Phase 3 — Cabinet: five officials (`Official`, seeded generation in
      WorldFactory), `CabinetSystem` monthly behavior, Autonomous / Directed
      (Influence economy, directive catalog) / Direct Control (CP actions,
      trust erosion on override), CABINET view with mode controls.
      Scroll bars hidden shell-wide per user preference.
- [x] Phase 4 — Military vertical: branches with readiness/supply
      (`MilitaryState`), 11 strategic locations, `MilitarySystem` (upkeep +
      objective-based operation resolution with directive risk settings and
      civilian harm), `ConfrontationSystem` (objectives, Primary Strategy,
      escalation with Escalation Premium for skipped levels, opponent
      settlement willingness, negotiated peace), MILITARY view.
      Note: capitals cannot be won at the table — only by occupation (GDD §22).
- [x] Phase 5 — Economy: `EconomyState` (growth/inflation/unemployment/debt/
      confidence + 7 sectors + market index history), trade network with
      tariffs/embargoes, `Sanction` with severity scale and sender blowback,
      `EconomySystem` monthly macro model, `AsciiChart.LineChart` market index,
      ECONOMY view. Veldt is deliberately energy-dependent on Korval.
- [x] Phase 6 — Intelligence: estimates with confidence grades keyed by
      observer (AI uses the same fog), `IntelNetwork` collection with focus
      domains, counterintelligence rollups, deception that bends estimates,
      covert ops (sabotage/influence/theft/deception) with exposure risk,
      INTELLIGENCE view. **Rule: views must never print a foreign country's
      true value — route through `IntelReadout` / `IntelligenceSystem.GetEstimate`.**
- [x] Phase 7 — Diplomacy: six-dimension `Relationship` (relations, trust,
      asymmetric dependence, threat perception, alignment, historical memory),
      emergent `StatusOf` (never stored), treaties with explicit commitments +
      acceptance logic + reputation damage for breaking them, coalitions that
      recruit on each state's own interests and can withdraw mid-conflict,
      DIPLOMACY view. Coalition strength feeds operation resolution.
- [x] Phase 8 — Government: `GovernmentState` with structural types that change
      how power works (elective vs internal succession, term limits, emergency-
      power cost), Political Capital economy, elections with incumbency fatigue,
      internal succession (age + elite cohesion), administration turnover that
      reshuffles the Cabinet while the player persists, national priority that
      redirects every delegated official, GOVERNMENT view.
      Placeholder `PillarView` deleted — all five pillars have real modules.
- [x] Phase 9 — World AI: `AISystem` goal-driven government AI (objective
      scoring → action execution), personality profiles so states don't converge,
      reasoning from estimates only (`PerceivedStrength`), behavioral assessment
      of the player, difficulty = reasoning quality (action budget + planning
      horizon, no stat cheats), AI-vs-AI confrontations. Verified 30-year
      unattended runs stay coherent and deterministic.
      **Systems are now actor-generic**: `*By(...)` variants (BeginBy,
      SetEscalationBy, LaunchOperationBy, ProposeSettlementBy, ImposeSanctionsBy,
      ProposeTreatyBy) take an actorId and skip player CP. Player wrappers spend
      CP then delegate. Use these when adding AI behavior.
- [x] Phase 10 — Progression: XP from meaningful decisions across all pillars,
      annual evaluation graded on trajectory + adversity (never a conquest
      checklist, component scores exposed), 18-node skill catalog across five
      trees with cross-pillar hybrids, STRATEGIST view.
      **Rule: skills grant operator capability only** (CP/Influence/PC, action
      costs, estimate precision, persuasion) — a test asserts unlocking every
      node changes zero national statistics. Player-only effects must check
      `== state.playerCountryId` so AI states are unaffected.
- [x] Phase 11 — Assessment / new game: 10 in-universe scenarios with hidden
      scoring (`AssessmentCatalog`), doctrine axes + pillar affinities, posting
      assignment matched to operator profile, national traits with paired
      strength/vulnerability, controlled randomness so answers aren't a recipe,
      one reassignment before acceptance, first-launch flow in the shell.
      `WorldFactory.CreateWorld(seed, countryId)` — **the player can be posted
      to any authored nation**; don't assume USA. Full reset returns to the
      assessment. SYSTEM has DBG QUICK START to skip it while testing.
- [x] Phase 12 — Vertical slice validation: `VerticalSliceValidationTests` plays
      full decades under six playstyles and asserts the design's promises
      (no degeneracy, every pillar viable, no dominant strategy, engagement
      rewarded, world evolves, saves resume seamlessly, decade reproducible).
      Findings and remaining weaknesses: `Docs/VerticalSliceValidation.md`.
      Balance fixes: `Core/Growth.cs` diminishing capability returns,
      fundamentals-anchored market index, initiative component in the annual
      evaluation. Run `Report_VerticalSliceBalance` to regenerate the numbers.

Post-MVP work completed:
- [x] Joint exercises / war games (GDD §15.3) — `ExerciseSystem`: readiness +
      interoperability + trust bought with exposure, losing still teaches,
      `doctrineFamiliarity` persists after a friendship ends and aids operations
      against a former partner, interoperability strengthens coalitions.
      Nine-month per-partner cooldown.
- [x] AI aggression tuning — hostility itself invites pressure; thin reporting
      makes a government hesitant, not paralyzed. ~2-3 confrontations/decade.

- [x] Phase 2 system specifications (GDD §36) — `Docs/specs/`, twelve documents
      covering all five pillars, AI, progression, country authoring, UI/UX,
      save schema, events and balance. Written as-built with real constants.
      **If you change a system's behavior, update its spec in the same commit.**

- [x] Alliance obligations (`AllianceSystem`) — defense pacts are called in when
      a war starts; honoring builds a defender-led coalition, repudiating costs
      trust with everyone. Player obligations arrive as a blocking Crisis Turn.
      **Coalition lookups now take a leader** (`FindCoalitionLedBy`) since both
      sides can field one.
- [x] Hybrid event architecture (`EventCatalog`) — 9 crises with world-state
      eligibility, weights and cooldowns. A stable state now has quiet months.

- [x] Coups / regime change (`RegimeSystem`, GDD §22) — military loyalty and
      conspiracy accumulate from real conditions; foreign subversion is
      multiplied by existing vulnerability so a stable state **cannot** be
      toppled by clicking; failed attempts purge the officer corps; successful
      ones install a military council, repudiate defense pacts and can tip into
      civil conflict. A sponsored successor is sovereign, not a puppet. The save
      always continues and the operator keeps their post. Spec 05 §7a.

- [x] Save migration chain (`SaveMigration`) — ordered steps wired into
      `SaveSystem.FromJson`, refuses future saves, fails loudly on a missing
      step, validates the migrated world. Empty step list (no shipped versions);
      template comment is in `SaveMigration.Steps`. Spec 10 §4.
- [x] Multi-seed balance measurement (`Report_MultiSeedBalance`) — five seeds
      with **per-component breakdown**. Use this, not the single-seed report,
      for any balance decision. It found that preventing crises scored worse
      than having them (now fixed) and that initiative weight was too low.

- [x] Initiative recorded explicitly (`ProgressionSystem.RecordInitiative`) at
      27 player decision points instead of inferred from XP magnitude. Fixed
      diplomacy grading below passive (−0.32 → +0.22). **When adding a new
      player action, call `RecordInitiative` after the resource spend succeeds**,
      or that pillar will quietly grade worse than inaction. Spec 07.

- [x] Military pillar verbs — posture (Peacetime/Alert/Forward, real upkeep and
      threat perception), multi-year procurement programs that build force
      structure *and* the military pillar, logistics investment, and doctrine
      (Maneuver/Attrition/Deterrence) that changes how operations resolve.
- [x] **Roster expanded to 16 authored countries** (spec 08). Archetype-diverse:
      major powers, regional powers, resource powers, industrial mid-tiers, a
      maritime archipelago and buffer states. 41 locations, 37 trade links,
      every country carries a genuine vulnerability. Tests assert this.
- [x] ASCII world map (`AsciiWorldMap`, MAP view) — selectable countries,
      standing markers, chokepoints, fog-respecting detail panel.
- [x] Content: event catalog 9 → 15 (including world-referencing events), skill
      catalog 18 → 28 with **strategic verbs** — Forward posture, Transformative
      procurement, Existential sanctions and deception operations are now gated
      behind skills and unavailable without them.
- [x] Interactive tutorial (`TutorialSystem`, GDD §5) — six in-universe
      orientation steps that teach by making the operator act in the live
      simulation. Never blocks, always dismissable.

- [x] Technology & research (`TechnologySystem`, GDD §11) — capability-based
      across all five pillars, multi-year funded programmes, diffusion through
      treaties/exercises/espionage, maturity so stolen knowledge is shallower.
      **Capabilities unlock ability, never force structure, people or money.**
      Spec 13.
- [x] Negotiated peace terms (`PeaceSystem`, GDD §26) — ten terms priced
      individually; concessions offset demands; overreaching prolongs the war.
      Settlements archived in `GameState.settlements`. Spec 01 §5a.
- [x] World Chronicle screen (CHRONICLE view) — filter by state and category,
      paged, grouped by year.
- [x] Strategic endgames (`EndgameSystem`, GDD §21) — one decisive instrument
      per pillar, each gated on a matured capability, ~8 authorizations of
      preparation, and conditions. **Actor-generic**: `PrepareBy`/`ExecuteBy`
      apply the same gates and consequences to every state, and AI governments
      prepare against rivals of 24+ months' standing and use them from
      desperation. Existential instruments escalate the target's confrontation
      to Total War. Foreign programmes are visible only through
      `KnownPreparation`, never for free. STRATEGIC view. Spec 14.

- [x] Audit sweep + long-run invariants (`WorldInvariantTests`, `BugRegressionTests`)
      — 20+ bugs found and fixed across four static audits and a new 30-year
      world-health harness. Two root causes dominated: **stats with no mean
      reversion** (approval, war exhaustion, war support, manpower, energy,
      network penetration, foreign sanctions all ratcheted one way forever) and
      **AI states locked out of player-only verbs** (procurement and logistics
      were player-only, so every AI army decayed monotonically and could never be
      rebuilt). Also fixed a coalition-leader bug that evicted the player from any
      alliance they honoured, a settlement screen that leaked the opponent's true
      acceptance test, and a covert-op RNG collision that made a successful
      operation infinitely repeatable. Full write-up: spec 12 §4.
      **Every playstyle now grades above passive** for the first time. Seeds and
      absolute grades shifted — see spec 12 §6 before comparing to old numbers.

**When adding a monthly tick, ask two questions:** does every value it decrements
have a *reachable* recovery path under the same conditions, and can a non-player
state reach that path? Nearly every bug in the sweep was one of those two.

- [x] GDD coverage audit — `Docs/GDD_Coverage.md`. All 310 requirements in §1-37
      checked against code: **195 done, 87 partial, 24 missing, 4 contradicted**.
      All 13 build phases are real. Two contradictions fixed (full reset now
      erases every slot; SYSTEM debug console gated out of player builds), one is
      a stale GDD line superseded by the real-countries decision, one open
      (escalation states hard-gate operations instead of pricing them).
      **Read that file before planning new work** — it names what is actually
      missing rather than what feels missing.

- [x] The three largest GDD gaps closed:
      - **Territory is worth something** (`TerritorySystem`, GDD §16/§19) — held
        energy regions supply energy, industrial centres build capacity, ports
        and chokepoints feed trade, airbases extend readiness. Symmetric: what
        you take, they lose. Occupation costs treasury, ties down the army and
        corrodes stability, and the occupied country *hardens*. Recovering your
        own ground is not an annexation. **Six airbases authored** — the type
        existed with no location using it.
      - **Primary Strategy decides the campaign** (`StrategicPressure`, GDD §18.2/§20)
        — sanction pressure, institutional fracture and diplomatic isolation now
        move settlement willingness, weighted ×1.0 for the committed domain and
        ×0.35 for the rest. Strategic Pivot added (3 CP + momentum). Economic and
        political campaigns are now ways to win rather than labels on a war.
      - **Constitutional authority** (`AuthoritySystem`, GDD §3 non-negotiable) —
        what the operator may personally command depends on the constitution.
        Direct / RequiresApproval (3 PC, refusable below 30 legislative support) /
        AdvisoryOnly, per pillar per government type. Emergency powers suspend the
        distribution, which is what makes them worth their price.
      Also: escalation no longer hard-gates operations (§18.1 — priced at +2 CP
      and self-escalating instead), and `Withdraw` can relinquish captured ground.

- [x] Half-wired systems finished (tranche 1) — `PartialSystemsTests` (18 tests,
      suite 530 → 548). Each was documented behaviour that existed only as
      decoration:
      - **Escalation pressure is now read** (GDD §18.1). Written 3×, read 0×.
        `CheckPressureBoilover` self-escalates one level at ≥70 pressure below
        Limited Conflict then releases 25 (net −13, so it must be wound up
        again); `BaseSettlementWillingness` subtracts `pressure × 0.15`; new
        actor-generic `ConfrontationSystem.AddPressure` is fed by sanctions (+6)
        and covert ops (+8). **This is how the non-military strategies escalate
        a standoff** — a wider war is still always a decision, never an accident.
        A confrontation standing at Crisis also accrues +3/month: with only
        one-off inputs against a standing decay, 70 was unreachable and the
        mechanic never fired once in a decade of measured play. Note it *still*
        never fires in the harness — the playstyle bots and AI escalate Crisis →
        Limited Conflict almost immediately rather than letting a situation
        fester, so the dwell-time path has nothing to bite on. It is reachable
        and covered by a test; expect it to matter for human players, who do sit
        on an open crisis. Do not tune the constant against the harness.
      - **Occupation's readiness cost applies.** The flat subtraction in
        `TerritorySystem` was erased by `MonthlyUpkeep`'s drift back to target
        every tick. Now `garrisonDrag = min(25, OccupiedValue × 0.12)` moves the
        *target*. **Any recurring cost must move a target, not the value.**
      - **ARCHIVE has a producer** — `ProgressionSystem.FileYearInReview` files a
        year-in-review. The class had styling, a filter and no source.
      - **FLASH means a decision is required** (§28.2). 10 decisionless sites
        demoted to PRIORITY; a war used to emit a FLASH every month. Also fixed:
        CONTESTED SUCCESSION was ungated, so a leadership fight in *any* country
        raised FLASH on the player's own briefing.
      - **XP diminishing returns per action kind.** First 4 of a reason each year
        pay full, then `1/(1+excess/4)` floored at 0.2; counters clear annually;
        the passive monthly baseline is exempt. Economy-vs-military XP went from
        ~5:1 to 2627 vs 2390. **Reasons must be stable buckets** — two call sites
        interpolated the target into the string and defeated the counter.
      `Tools/extract-failures.sh` now refuses to report a pass when the log
      contains `error CS`: Unity re-runs the last good DLL, so a test assembly
      that fails to compile reports the previous run's totals and reads green.

- [x] **Cabinet reporting** (`ReportingSystem`, GDD §28.1) — "official competence
      influences what is surfaced or missed". `Official.competence` used to be
      read at exactly one site (scaling autonomous outcomes), so appointing well
      changed how a pillar *performed* and nothing about how well you could see
      it. Each month's traffic now passes through the official who runs the
      originating pillar: a weak desk buries PRIORITY items as ADVISORY and loses
      routine ones. Three rules make it fair rather than punishing, and all three
      have tests:
      1. **A decision is never withheld.** FLASH always arrives — incompetence
         costs awareness, never agency. Withholding one would strand the player
         in front of a turn they cannot take.
      2. **What you run yourself, you see yourself.** Direct Control removes the
         intermediary and so removes the filter; its cost stays CP and trust.
      3. **The record stays honest.** Nothing touches `chronicle`, so a missed
         item is still discoverable in CHRONICLE — which is what learning what
         your government did not tell you should feel like.
      `Notification.desk` (`ReportingDesk`) defaults to `Command` = unfiltered,
      so **making something filterable is always an explicit decision** and a new
      notification can never silently go missing. 58 sites are attributed; assign
      a desk by *who files the item*, not by which file the code lives in.
      Thresholds are calibrated against the seeded cabinet's 40–78 competence
      roll so a median appointment reports nearly everything — at the first
      calibration every default cabinet lost ~24% of world news from month one,
      which reads as an empty game rather than a mediocre minister. Player-facing
      REPORTING bar and plain-language line in CABINET; `DBG FULL REPORTING`
      toggle in SYSTEM. Balance is unchanged by construction: it alters what the
      operator knows, never what happens.
      Fixed alongside it: `MIL_READINESS` had **no branch** in
      `ApplyPillarEffect` since Phase 3 — it cost 1 Influence and was
      bit-identical to leaving the minister autonomous. It now trades pillar
      growth and treasury for a readiness **target** shift
      (`MilitarySystem.ReadinessDirectiveBonus`, player-only).
      `EveryDirectiveChangesSomething` guards the class of bug: any directive
      whose outcome matches autonomy now fails the build.

- [x] **Geography and reach** (`GeographySystem`, GDD §16) — `mapX`/`mapY` fed
      the ASCII map and nothing else, so any state could assault any location on
      earth at identical cost and odds, and a navy — the most expensive thing a
      country can buy — bought nothing that distance made valuable. Three ideas,
      **no new save state**:
      - **Position is authored, not saved.** Coordinates stay in
        `WorldFactory.Profiles`; geography does not change during a save.
      - **The world is a cylinder.** Longitude wraps at `MapWidth = 80`. This is
        not a detail: without it the USA is "closer" to China than to Japan and
        the entire Pacific reads backwards. Latitude is weighted ×2 for the
        grid's aspect.
      - **Ground you hold abroad is a place you can fight from.**
        `originalOwnerId` says where a location physically *is*; `ownerId` says
        who controls it. A captured port projects from the country it sits in,
        which is what makes a forward position worth taking — and
        `foreignOperatorId` gives the same reach without conquest.
      `ProjectionRange` = 8 + naval×35 + air×20 + logistics×0.15 + CAP_LIFT×10;
      `ReachFactor` = range/distance, floored at **0.35** — distance makes a far
      campaign hard, never impossible (no hard geographic gates, per the §18.1
      lesson). Multiplies attacker power in `ResolveOperation` only: fighting
      near home is the one advantage a smaller power reliably has. A failed
      distant operation now says so in its after-action summary.
      **Content fix found by this work:** Ramstein was authored as USA-owned, so
      a base whose entire identity is being forward sat at Washington's
      coordinates. It is now German ground with `foreignOperatorId = "USA"` —
      also the world's first authored use of the hosting mechanic, which had
      shipped with no starting world using it.
- [x] **The AI's domestic half** (GDD §13, §27) — the foreign side had matched the
      player's verbs for a while; the domestic side had not. `ConsolidateHome`
      and `SecureResources` wrote stability, approval, energy and materials
      **directly, every month, for nothing** — verbs the player has no access to
      at a price the player cannot match. Same asymmetry this project already
      fixed once pointing the other way (AI states locked *out* of player verbs).
      Two consequences worth naming: a rival could ride out any domestic damage
      the player inflicted because repairing it cost zero, and `SecureResources`
      silently repealed the endowment model — whose own comment says a country
      can invest past its endowment "only through capability" — so an
      energy-poor archetype could stop being energy-poor by wanting to.
      - `AIState.politicalCapital` gives every AI government the player's PC
        economy. One shared income formula
        (`GovernmentSystem.PoliticalCapitalIncomeFor`), so standing funds a
        government whoever runs it. Operator **skills are added only to the
        player's pool** — capability, never national power.
      - `PublicMessagingBy` / `InstitutionalReformBy` are actor-generic; the
        player wrappers delegate and then award XP/initiative.
      - `SecureResources` costs treasury and is bounded by
        `EconomySystem.EnergyCeilingFor` / `MaterialsCeilingFor`, now public so
        investment and monthly drift share one definition of the ceiling.
      - **Pre-emption** (`PreemptProgramme`, spec 14 §8) — a detected foreign
        programme used to raise threat and change nothing. A government now sues
        for terms, builds its own deterrent, or falls back to countering.
        Deliberately not gated on caution: it is the one conclusion a cautious
        government is *more* moved by.
      **The line is governance vs intervention, not "nothing may move".** Routine
      state functioning is free on both sides — the player's Autonomous Cabinet
      and the AI's `InvestInPillars` are equivalents. What must cost is
      discretionary repair. `AIDomesticTests` enforces that.
      Balance *improved*: every playstyle gained margin over passive
      (GOVERNMENT +0.18 → +0.28) because damage the player inflicts now sticks.
      Also fixed: `InstitutionalReform` added a flat +4 Government pillar, which
      was survivable with one reformer and saturated the pillar once fifteen AI
      governments could do it. It uses `Growth.Apply` now.
- [x] **Every government has a cabinet** (GDD §8) — foreign states had none, so
      their capability grew from `AISystem.InvestInPillars`, a bespoke routine
      with no people behind it: a rival's progress had no explanation, nothing to
      collect against, and nothing a coup could damage.
      - **Cabinets belong to countries.** `CountryState.cabinet` is the one home;
        `GameState.cabinet` is now a **property** returning the player country's
        list, so all ~30 call sites kept working and the concept is not stored
        twice. Official ids are country-qualified — the old per-pillar scheme
        collided on every id across sixteen cabinets.
      - **`CabinetSystem.MonthlyAct` runs every state.** `InvestInPillars` is
        reduced to `RebuildForces` alone; leaving both would pay a government
        twice for one month's work. The month RNG mixes in the country, or all
        sixteen cabinets draw the same variance and the world moves in lockstep.
      - **Control modes stay the player's.** A foreign cabinet is always
        Autonomous: there is no operator standing outside it to direct anyone.
      - **Turnover reaches foreign cabinets.** `ReshuffleCabinetOf` and the coup
        reshuffle are actor-generic — otherwise a foreign minister appointed at
        world creation was still in office fifty years and six elections later.
      - **First real save migration.** `SaveVersion` 1 → 2, and `SaveMigration`'s
        step list is no longer empty. The step moves `legacyCabinet` onto the
        player country and appoints the foreign cabinets old saves lack, seeded
        from the save's own `rngSeed` so migrating twice gives the same world.
      Also fixed, found by the long-run invariant: `pillars.government +=
      (leaderCompetence − 50) × 0.004` was the **only un-damped capability
      ratchet left** — a raw monthly addition worth ~17 points a decade. It hid
      under the saturation threshold while AI states had little else raising the
      pillar and crossed it the moment foreign cabinets contributed. Now through
      `Growth.Apply`.
      Suite 591 → 607. Balance: margins compressed but every playstyle still beats passive
      (MILITARY +0.24 … ECONOMY +0.58). Expected — the world got more competent.
- [x] **Reporting stays "missed or buried" — never distorted** (user decision).
      A weak desk can lose an item or strip its urgency. It may **not** report a
      wrong number. The player can trust every figure they actually receive; the
      only question is whether they receive it. If numbers could be wrong, no
      readout would be trustworthy and the terminal would become exhausting
      rather than tense. Do not add distortion to `ReportingSystem`, and do not
      relist §28.1's "distortion" reading as missing work.
- [x] **AI states get no Influence economy and no Crisis Turns** (user decision).
      **This is settled — do not build it, and do not let an audit relist it as
      missing work.** Both are *operator interfaces*, not government machinery.
      Influence prices the friction between an operator and officials who would
      otherwise choose for themselves; a foreign state has no operator standing
      outside it, so there is nothing to price. A Crisis Turn exists to interrupt
      the player's month and force a decision at the terminal; an AI government
      simply decides. Building either adds simulation the player can never see
      for no behaviour they could ever detect.
      **The rule the AI is held to is symmetry of *consequence*** — nothing free
      for one side that the other pays for — which the Political Capital economy
      and treasury costs already satisfy. Symmetry of *interface* was never it.
- [x] **Manual save slots: autosave only** (user decision). Slots exist in code
      but ship no player-facing UI, per GDD §30's "no reload-because-I-disliked-
      the-outcome" rule. Consequences stick. Do not add a save/load screen
      without asking.

- [x] **`SimulationPipeline` — one wiring list.** The monthly system order existed
      **twice**: in `GameController.Attach`, and hand-copied inside
      `VerticalSliceValidationTests.BuildSimulation`. They had drifted by **five
      systems** — `RegimeSystem`, `TechnologySystem`, `EndgameSystem`,
      `TerritorySystem` and `CabinetLifecycle`.
      **Every balance figure the harness ever produced was measured in a world
      with no coups, no research, no strategic instruments, no value to holding
      ground and no cabinet turnover** — and reported as if it were the game. A
      validation harness that does not run the thing it validates is worse than
      no harness, because it is trusted. Any balance number recorded before this
      fix should be treated as measuring a different game.
      **Wire every new monthly system in `SimulationPipeline`, never in a caller.**
      Found only because cabinet turnover fired zero times in an entire suite run.
- [x] **Cabinet lifecycle** (`CabinetLifecycle`, GDD §7.3) — officials age, retire
      and occasionally die, and a vacant office is filled from a **shortlist with
      a real trade-off**: the able candidate the establishment distrusts, the safe
      pair of hands, the wild card. Left alone for three months the government
      confirms the *safe* one — so declining to choose costs the operator the
      pick, never the turn. Foreign seats fill immediately (a shortlist nobody
      reads is not a decision). Seeded ages 48–70 so turnover is a feature of a
      decade rather than a thirty-year rarity; measured ~1300 retirements to 13
      deaths across a suite run, which is the intended ratio — mortality is a
      shock, not something to plan around.
      Save v2 → **v3**: `age` needed a backfill because its zero default is not
      merely empty, it is wrong (a cabinet of infants who never retire).
      §7.3's *dissent* half — leaking, obstructing, resigning in protest — is
      deliberately out of scope (user decision).

Two balance questions were **measured and closed** — see
`Report_MultiSeedBalance`'s war-outcome table, and do not reopen either without
re-running it:

- **Militarism's ECON component (30.8 vs 44–54) is correct.** The suspicion was
  that its wars achieved nothing. They do: the military bot **takes its objective
  in 5 seeds out of 5**. What it does *not* do is end with net territory — it
  wins the lane it fought for and loses one of its own locations elsewhere while
  committed. So the ECON penalty is the **opportunity cost of commitment**, not a
  failure to win, and raising conquest yields would reward a war the player is
  already winning. Median 3 player confrontations per decade, consistent with the
  authored ~2–3. (An earlier read of "49 confrontations" was a measurement bug:
  `state.confrontations` includes AI-vs-AI wars. Count `Involves(playerCountryId)`.)
- **Grade bands are calibrated; leave them.** They were recentred upward on the
  theory that a competent decade "reading as a C" was the scale calling good play
  mediocre. Measurement said otherwise: the C belonged to *passive* play. Lifting
  the bands moved doing nothing to a **B** and compressed the reward for
  engagement from +0.36 to +0.16 over passive. Reverted. If they ever do move,
  check the **passive baseline** — an uneventful year is the anchor the scale
  hangs from — and retune `SkillPointsFor` in the same commit, since the band
  also sets progression pace.

- [x] **Operation verbs widened, and the scale bug under them fixed** (spec 01 §4).
      Reported from play as "coalitions do nothing and I fail most missions".
      That was not tuning: `TotalPower` is 0–3 and `garrison` is 0–100, and
      `ResolveOperation` **added them directly**, so the entire national army was
      ~4% of the defence figure and `odds` sat near 8% regardless of what had been
      built. Procurement, readiness, doctrine, ISR, reach and coalition support
      were all multipliers on a term too small to matter.
      `MilitarySystem.PowerScale = 30f` reconciles them — **anything comparing a
      force to a fortification must go through it.**
      Four verbs added (`AirStrike`, `SuppressDefenses`, `NavalBlockade`,
      `SpecialOperation`), each resolving against something *other* than the
      garrison, priced apart so sequencing is a decision, with per-verb intensity,
      civilian-harm factor, and attrition routed to the branch that actually
      fought. That last one was a live bug the new tests caught: every operation
      charged `defenderLosses` to `target.garrison`, so repeated blockades emptied
      a garrison the fleet never engaged.
      Coalition weight is now computed per operation type, so partners contribute
      the branches the operation actually uses, and the order screen prints
      `OUR COMMITTED WEIGHT` beside `PARTNERS ADD`.
      `AISystem.ChooseOperation` gives the world the same verbs (it previously
      chose only Siege or Assault) — a test simulates 90 AI-only years and fails
      if governments reach for fewer than three kinds of operation.
      **A new `OperationType` must be given a row in six places** — see spec 01 §7.

- [x] **Operation list widened to 23 verbs, and `OperationCatalog` built under it**
      (spec 01 §4, GDD §19 amendment). Verbs chosen by the user across four
      service domains. Everything about an operation now lives in **one
      `OperationProfile` record** — cost, branch weights, intensity, civilian
      factor, defence model, targeting, availability — replacing six scattered
      switches in which a verb given a row in five of them was a differently-named
      assault that looked finished. **Adding a verb is now two places, not six**,
      and a test fails the build if the enum and the catalog drift.
      - **`DefenseModel` is the load-bearing idea.** Each verb resolves against
        what it would actually fight — air defences, a fleet, an air force, a
        security service, the works, popular resistance. If everything resolves
        against the garrison, every verb is an assault with a different name.
        `ApplyDefenderAttrition` is keyed by the same enum, so odds and losses
        cannot disagree.
      - **The pillar finally has defensive verbs** (`OperationTargeting.OwnGround`):
        prepared defence, counter-insurgency, convoy escort, missile defence.
        These do not escalate a confrontation and pay no abruptness surcharge —
        fortifying ground we hold cannot be what starts the shooting.
      - **Sea access is authored** (`CountryProfile.navalAccess`, Landlocked /
        Coastal / Maritime), following the coordinates precedent — a coastline
        does not change during a save. Naval strength derives from it (0 / 0.55 /
        0.95 of the military score) instead of a flat 0.75× for everyone.
        Landlocked Kazakhstan used to launch fleets and draw global naval reach.
        **Draw the naval roll unconditionally and zero it** — skipping the
        `Jitter` call shifts the world-generation RNG stream for everything
        created after it and silently moves balance figures.
      - Unavailable orders are **shown, disabled, and given a reason**
        (`WE HAVE NO FLEET`, `TARGET IS LANDLOCKED`). One gate,
        `OperationCatalog.CanOrder`, shared by the order screen and
        `LaunchOperationBy`, so what is offered and what is accepted cannot differ.
      - `LeadershipStrike` is deliberately double-edged: it costs diplomacy and
        trust with **every** state, and raises the target's war support and unity.
        Without that it is simply the best opening move.
      Order screen groups by domain — 23 buttons is not a row on a phone.

- [x] **The AI got a theory of how it wins, and a memory of you** (spec 06 §7a/§7b,
      GDD §24 amendments). Two systems, `AIStrategy` and `AIPrediction`.
      - **`StrategicPath`** — each government commits to economic primacy,
        military dominance, regional hegemony, a technological edge, institutional
        weight or survival, **chosen from its own endowments**. `ObjectiveBias` is
        what stops it being a label: a state on Economic Primacy puts `AssertClaim`
        at −22 and declines a war it could win. Reviewed rarely
        (`MinimumMonthsOnPath = 30`) — a government that re-plans monthly reads as
        noise. Progress is measured against *estimates*, so a deceived state can
        believe it is winning.
      - **`OpponentModel` + counter-play** — this is the answer to "memorise the
        AI, reset, run the known opening". **Randomness is not the answer**; a
        world that behaves differently for no reason is worse than one that
        behaves the same for good reasons. AI behaviour is a **function of what
        the player does**: an operator caught running covert action finds hardened
        security services by year three, one who rushes militarily finds fortified
        neighbours. Reset and repeat and the same counter emerges — the way past
        it is to change what you do.
      - Observations are **only what could be seen**, and prediction quality
        scales with collection, so intelligence stays worth buying and deception
        stays worth running. It **decays when the behaviour stops** — a permanent
        reputation would make reloading correct, which the game refuses.
      - Found and fixed alongside: `PlayerAssessment.perceivedAggression` was
        computed every month and read by **one line** (a threat nudge). The
        "written but never read" class again. Exposure was chronicled **for the
        player only**, so no AI state could build a reputation for subversion —
        and it read the transient `compromised` flag, which decay clears within a
        month or two. It now reads the public record. Objectives kept was capped
        at 2 while Ruthless is granted **3** actions, so its extra reasoning was
        unreachable.
      Balance after all of it: every playstyle still beats passive, MILITARY
      +0.56 → **+0.64**, ECONOMY +0.90. INTELLIGENCE fell +0.60 → **+0.48**,
      which is the counter-play working as designed. GOVERNMENT (+0.12) is now
      clearly the weakest pillar and the next thing worth attention.

- [x] **Government verbs** (spec 05 §2a–§2d) — the pillar graded **+0.12 over
      passive**, the weakest in the game, for two structural reasons rather than
      tuning. It had almost nothing to do month to month (26 decisions a decade
      against the economy's 344), and **Political Capital had no sink above 7
      against a cap of 20**, so an operator who was not spending simply sat at
      the cap with nothing worth buying.
      - **`CivicPosture`** (Open / Standard / Restrictive) — the pillar's missing
        *standing* choice, mirroring military posture. Order against legitimacy,
        and it multiplies how fast conspiracies organise (×1.35 / ×1.0 / ×0.6).
        Restrictive costs 0.45 PC **every month it is held**. Government type is
        the interesting part: an elective system pays more than twice the
        approval to govern restrictively. `Standard` is declared **first** so its
        ordinal is 0 and old saves land on neutral.
      - **`BuildPoliticalSupport`** (2 PC) — the workhorse. Writes to
        `brokeredSupport`, which **moves the target** for `legislativeSupport` /
        `eliteCohesion` rather than the value, because both drift every month —
        the same trap that made occupation's readiness cost dead code. It decays
        at 0.94/month, so support is *maintained*, which is what gives the pillar
        something to do most months.
      - **`DistributePatronage`** (1 PC + 200 treasury) — the same destination by
        a different road, plus cabinet loyalty, minus institutional quality. A
        rich government spends treasury; a well-regarded one spends standing.
      - **`LaunchInquiry`** (4 PC) — the inverse of patronage. Raises the
        **weakest** minister, which buys awareness as well as performance since
        competence decides what reaches the terminal at all.
      - **`GroomSuccessor`** (3 PC) — a transition you saw coming. Raises the
        incoming leader's competence *floor* and keeps the handover uncontested.
      - **`ConsolidateAuthority`** (12 PC) — the large purchase the PC economy
        lacked. Permanently makes one pillar the operator's to command. **Player-
        only by design and not national power** — authority is an operator
        interface, same reasoning that settled Influence and Crisis Turns; a test
        asserts it moves no national statistic.
      **Bug found under it: `stability` and `nationalUnity` had no restoring
      force at all** — the last two political stats without one, and the fifth
      instance of this class. National unity had exactly one repeatable player
      source in the entire game. A country that had a bad decade could not be
      governed back to health. Both now drift to a level the state's institutions
      and standing can hold.
      Also fixed: `ConsolidateHome` walked a fixed ladder with institutional
      reform unconditionally first, and since most states sit below the pillar
      threshold for most of a save, **nothing below it was ever reached** by any
      foreign government. It now picks the instrument matching the larger gap.
      Its objective priority was also mis-scored — terms unfloored, so *healthy*
      stability scored negative and subtracted from the case for shoring up a
      chamber that had turned on the government. The moment stability gained a
      restoring force, the whole objective became unreachable.
      **GOVERNMENT +0.12 → +0.40**, decisions 26 → 90, XP 1343 → 2034, initiative
      45.7 → 58.9. The spread across playstyles is now +0.40 … +0.92 (was +0.12 …
      +0.90) — no dead pillar and no dominant one. ECONOMY (+0.92) is now the
      high outlier and the next thing worth looking at.

- [x] **Defensive programmes reachable in peacetime** (spec 01 §4-1) — the five
      `OwnGround` verbs no longer need a confrontation.
      `ConfrontationSystem.RequiresConfrontation` is false for them, the launch
      path accepts a null confrontation, and `MilitaryView` has its own
      DEFENSIVE PROGRAMMES panel built outside the confrontation console. They
      were most valuable *before* a war and reachable only during one. With no
      war in play the resolution skips what does not exist (momentum, exhaustion,
      coalition) and the record goes to the **chronicle**, not a war diary.
      Offensive verbs still require one — shooting at another state *is* a
      confrontation by definition, and a test asserts it.
      **Bug found doing it:** the peacetime RNG seeded from the raw
      `state.actionSequence` instead of `NextActionSequence()`, so twenty
      consecutive orders were one draw repeated twenty times and a failed roll
      could never be retried. Same collision class already fixed once for covert
      ops, running the other way. Its determinism test now carries a non-vacuity
      guard — two runs that both did nothing are also equal, which is how the
      first version passed while nothing worked.

- [x] **Touch targets decided and enforced at 44 panel px** (spec 09).
      `.cmd-button` was ≈31, `.nav-button` ≈35, `bp-short` compressed both to
      ≈23. All three interactive selectors now carry `min-height: 44px`, the
      slider dragger is 22×22, and `.bp-compact .nav-rail` grew 44 → 54 so it
      can hold a full-size button plus margins. `TouchTargetTests` parses the
      real stylesheet and fails the build on regression.
      **No DPI anywhere** — the panel is `ConstantPixelSize` with a scale derived
      from resolution, so a panel pixel is proportional to physical size and the
      target is verifiable without hardware. 44pt ≈ 3.4× body text; body text is
      13px, so 44 panel px is the direct analogue. A test pins `BaseFontPx == 13`
      so the constant cannot silently stop meaning that.
      **`bp-short` was decided against shrinking**: a cramped screen may buy rows
      with smaller text and tighter margins, never with a smaller target.

- [x] **Crises reach the world** (`CrisisEffects`, spec 11 §6, GDD §23 amendment).
      A `CrisisOption` resolved to four scalars on the player's own country and
      nothing else — it could not change a relationship, move a market, touch a
      foreign state or open a confrontation. The situations arose systemically
      and resolved cosmetically.
      - An option now carries `effectId` / `effectTargetId` / `effectMagnitude`.
        **A named string, not a delegate**: `ActiveCrisis` is persisted, so a
        lambda would work perfectly until the operator saved mid-crisis and then
        silently lose its consequence. A test saves and reloads a live crisis.
      - 14 effects across all five pillars, including `OPEN_CONFRONTATION` and
        `SUFFER_CONFRONTATION` — **an event can now start a war**, which nothing
        in the game could do before.
      - **The switch is exhaustive by test.** `CrisisEffectTests` walks every
        option and lapse in the catalog and fails the build on an unknown id — a
        typo would otherwise be a choice that resolves and does nothing, which is
        the failure this system was built to fix, one layer down.
      - **Drifting has consequences.** `lapseEffectId` gives the world something
        to do when nobody decides; ignoring a crisis used to cost standing and
        nothing else, making it the cheapest way to dodge a decision.
      - The subject is captured **when the crisis fires**, so a situation that
        opens naming one state resolves against that same state.
      **Third instance of the ladder bug, in `AISystem.ConsolidateHome`.** Fixing
      reform-first simply let *bargaining* crowd out the four verbs below it. It
      now **scores every instrument** and takes the worst problem it can afford.
      A ladder means whatever sits on top is the only thing that ever runs — do
      not add another one.
      Balance: margins compressed ~0.06–0.08 (passive 2.46 → 2.58) but the
      ordering held and every playstyle still beats passive (+0.32 … +0.82).
      Player confrontations 11 → 12 across five seeds, so crises are **not**
      flooding the world with wars. The compression looks like band noise rather
      than a systematic effect; worth re-checking if it drifts further.

- [x] **Session recording and review** (`Telemetry`, `TelemetryAnalysis`,
      `TelemetryFile`) — balance and correctness had been measured entirely by
      harness bots, which play the way the harness was written to play. With one
      tester, an evening of real play is the only evidence available about which
      of 23 military verbs a person reaches for, whether capacity sits unspent,
      or which crisis is always ignored.
      - **Hooked at the resource spends, not at forty call sites.** Almost every
        player action passes through `SpendCommandPoints` or
        `SpendPoliticalCapital` carrying a *reason string*, so a verb added later
        is captured without anyone remembering to instrument it. Same reasoning
        that put the monthly system list in `SimulationPipeline`.
      - **Reasons reduce to stable buckets** (`Telemetry.Bucket`). "Assault at
        Norfolk" and "Assault at Shanghai" must aggregate as one, or every count
        is 1 — the identical trap that defeated XP diminishing returns.
      - **The analysis is the point, not the log.** `TelemetryAnalysis.Findings`
        flags one-way values (the bug class this project has shipped five times),
        resources pinned at a ceiling (how the Government pillar was diagnosed),
        verbs nobody reached, actions repeatedly refused, and a world doing only
        one kind of thing. Tests plant each fault and assert it is caught — **and
        assert a healthy session is not flagged**, because a review that cries
        wolf is a review nobody reads.
      - **Local only, and it must stay that way.** No network call, no
        identifier, no SDK. Defaults on in development builds and off in player
        builds, following the SYSTEM console precedent. Adding transmission would
        be an outward-facing act requiring informed consent this project has not
        asked for — do not add it without asking.
      SYSTEM view shows the findings live; `WRITE SESSION FILE` dumps review +
      log to `persistentDataPath/sessions/session_<seed>_<country>.txt`.
      Pull from a device with:
      `adb pull /sdcard/Android/data/com.southsidegames.brink/files/sessions/`

- [x] **The world became structure rather than backdrop** — eight items from a
      fresh coverage audit, built in dependency order (locations → social layer →
      identity → theatres → fronts → secession → events).
      - **`LocationType.MaterialsRegion`** — strategic materials are drawn on by
        industry, procurement and every research programme, and nowhere on the
        map produced them. The Pilbara was filed as an *industrial centre*, so
        taking the largest mining region on earth paid out in factory capacity.
      - **Maritime content**: 3 ports → 15, 3 chokepoints → 8. Nine of 23 verbs
        need a maritime target; most of the naval verb list was a button with
        nothing to point at. Every authored maritime power now has a port.
      - **Social layer** (GDD §12): `livingStandards`, `socialUnrest`,
        `publicGrievance`, on deliberately different timescales — years, months,
        decades. Approval used to read the current quarter's growth figure, so
        history had no weight. **Unrest is organisation, not opinion**: a state
        can be disliked and calm, and only unrest feeds conspiracy. A restrictive
        posture suppresses the *expression* while grievance keeps accruing, so
        order bought that way is rented. Save v3 → **v4** with a real backfill.
      - **National identity**: temperament is now **authored per country** rather
        than rolled uniformly per save, plus a 10-trait catalogue with 2 traits
        on every state. A country that plays differently every game defeats an AI
        designed to answer a repeated opening — the player needs a stable read on
        who they are answering. Every trait is read somewhere; a test proves it.
      - **Theatres** (`TheatreSystem`, GDD §16) — derived from authored
        coordinates, not stored. Geography priced *distance*; this prices **how
        much of the force is already busy**. `FocusFactor` drags operations by
        commitment elsewhere, floored at 0.55, and `IsOverstretched` is read by
        the AI as opportunity. That last one is what makes a distant war a
        neighbour's opening — the point where the map stops being a picture.
      - **Simultaneous fronts** — the hard one-at-a-time block is gone, replaced
        by a priced constraint plus a `MaxCommitment` ceiling. The MILITARY view
        gained a front selector. Same pair still cannot open two wars.
      - **Secession and unification** (`SecessionSystem`, GDD §17.1) — **the
        first thing in the game that creates a country.** `new CountryState`
        previously appeared once, at world creation, so the map could only shrink.
        A breakaway gets ground, a cabinet, an AI mind and relationships with
        everyone. Its ground is set to `originalOwnerId = successor` or it would
        permanently occupy itself. If the *player's* country splits they keep
        their post — severe, never terminal, as with a coup.
      - **Events 15 → 28**, drawing on everything above (unrest, grievance,
        overstretch, breakaways, ports, materials). 15 events on a 24-month
        cooldown exhausts in ~3 years of a decades-long save.

- [x] **The military got counted, explained, and buyable** (GDD §19 amended).
      - **`AssetCatalog` — 17 counted classes** across air (fighters, bombers,
        drones, tankers, missiles, personnel), naval (carriers, submarines,
        destroyers, cruisers, combat ships, missiles, personnel) and ground
        (tanks, helicopters, missiles, soldiers).
        **This supersedes §19's "no individual vehicle counts" rule** by explicit
        decision. The rule was right about what it protected — operations still
        resolve against *branch strength*, not airframes — but wrong about the
        information layer: "air strength 68" is not a fact an operator can reason
        about and "912 fighters against their 340" is.
      - **Counts are authoritative; `strength` is a mirror.** All 12 sites that
        wrote strength now route through `BranchForce.SetStrength`, which scales
        the inventory to match, so a loss computed in the aggregate destroys
        actual aircraft. The other direction would have let you buy 200 fighters
        and be no stronger — the "written but never read" bug in an expensive
        costume. Save v4 → **v5** with a backfill (empty is *wrong* here: a loaded
        save with no inventory would recompute to zero and disarm the world).
      - **Foreign inventories are bands, never truth** (`IntelReadout.ForeignAssetCount`).
        Width comes from the estimate's own confidence and the centre is bent by
        deception, so better collection genuinely sharpens the picture and buying
        intelligence before buying a war is a real decision.
      - **`AcquisitionSystem`** — order specific classes; steel takes years and
        people take months (`leadMonths` per class), industry decides throughput.
        **War footing** is gated on *political backing*, not money — moving the
        budget is something a chamber grants — costs PC monthly, roughly doubles
        delivery tempo, and lapses when it cannot be paid for or justified.
        Actor-generic: the AI orders what it is short of and surges too.
      - **`OperationAnalysis` — after-action reports that say why.** Every term in
        `ResolveOperation` records itself; the report states **the odds that were
        assessed**, ranks what decided it, and ends with **one line on what would
        change it**. It distinguishes an unlucky 80% from a hopeless 15%, and it
        runs for attacks *against* us too, so a failed enemy assault teaches what
        held. Without this the only available strategy was to retry unchanged.
        `Advice` switches on label constants and a test proves none falls through.
      - **A stripped position falls** (`DepletionFactor`). Below ~22 garrison and
        ~20 works the defence collapses rather than scaling, floored at 0.12 so a
        capture is never a formality. The reward for a siege is now an open door
        rather than a slightly cheaper assault.

- [x] **The defence minister has an opinion** (`MilitaryAdvice`, GDD §7.2, §8).
      `Official.competence` decided how well a minister executed autonomously and
      how much traffic reached the terminal — it did **nothing** for a player
      running the war themselves. So during a confrontation the officer nominally
      in charge of the military had nothing to say, and appointing a good one
      changed outcomes you never saw and none you were deciding.
      - **The recommendation is only as good as the minister.** A competent one
        reads the odds near-true; a poor one is *confidently wrong* — the noise is
        in their assessment, not their delivery, so bad advice arrives sounding
        exactly like good advice. You are trusting a person, and whether that was
        wise was decided months ago at the appointment.
      - Assessments are **deterministic per (official, target, verb)**. A number
        that flickered on refresh would be unusable, and averaging it would leak
        the true value.
      - Shown in a new **`sig-advice` yellow** (11.9:1 worst case, AAA on all four
        palettes) with a **★ glyph**, so the reading survives any palette and any
        colour vision. Deliberately *not* `sig-rival`: that set carries standing,
        and overloading it would weaken both readings.
      - **Never binding.** A test asserts an order the minister did not recommend
        still resolves.
      - `MilitarySystem.ComputePowers` / `EstimateOdds` extracted so the preview
        and the resolution share **one** formula — two copies of something that
        long would drift within a month and make the advice quietly wrong.
      - Two new directives that act on the *inventory* rather than the pillar:
        **PREPARE FOR WAR** orders what the force is short of, **DRAW DOWN** sells
        hulls back at 55% (full value would make oscillating a free way to park
        money). `DescribeStanding` makes a delegated pillar visible instead of
        silent.

Recommended next:
- **ECONOMY is now the high outlier at +0.92 over passive** (next is MILITARY at
  +0.62). Worth a look, but check it is not simply that 344 decisions a decade is
  the most engagement any playstyle offers — the evaluation rewards initiative on
  purpose.
- `foodSecurity` is written once at world creation and never changes — the last
  authored stat with no monthly behaviour at all.
- Diminishing returns on repeated covert ops.
- **Crisis chains** — now unblocked. A badly-handled or ignored crisis has
  somewhere to record itself, so a follow-up months later is the cheapest
  remaining way to make the world feel causal. Needs a `followsFrom` on
  `EventDefinition` (spec 11 §7).
- **Crises only ever fire for the player.** `SystemicCheck` reads
  `state.PlayerCountry` throughout and every eligibility helper is written from
  the operator's viewpoint. AI states facing their own crises would create
  instability the player could read and exploit.
- `Leader.faction` is a display string **no rule reads**. Now that
  `legislativeSupport` is central (spec 05 §2b), parliamentary arithmetic would
  give it somewhere to bite.
- **Not yet verified by a test run.** The eight structural items above compile
  clean but the suite could not run — the Unity editor was open and batch mode
  cannot share the project lock. Run EditMode → Run All before trusting any of
  it, and re-check `Report_MultiSeedBalance`: theatres, simultaneous fronts and
  the social layer all touch balance.
- Touch targets are now 44 panel px and test-guarded, but the **layout cost has
  not been checked on a device**: every button grew ~30% taller, so MILITARY
  (domain tabs + up to 7 verbs + the new defensive panel) and GOVERNMENT (four
  control rows) are the two screens to look at first.

Process per GDD: Prompt → Implement → Test → Fix → Lock. Do not start a phase
until the prior one passes its acceptance tests.

## Terminal layout — do not reintroduce fixed widths

The shell is a character grid under `white-space: pre`. Nothing wraps, so
anything wider than the panel runs off the screen.

- **Sizing the boxes is not enough.** Prose written into a notification or hint
  is arbitrary length and will run off the right edge on its own.
  `TerminalShellController.ApplyTextPolicy` walks every `terminal-text` label
  after each refresh and hard-wraps it via `AsciiChart.WrapBlock`, so a view
  added later cannot forget. Labels marked `terminal-figure` (via `AddFigure()`)
  are skipped — they are already built to an exact grid and wrapping corrupts them.
- **Size classes are measured in columns, not panel points** (`Breakpoints.FromColumns`).
  Points stopped meaning anything once the scale was derived to hit a column
  target — panel width in points is then roughly constant on every device, which
  made `bp-large` unreachable and its layout rules dead.
- **Rich text inside a `terminal-text` label distorts the width.** A colour tag is
  ~24 invisible characters and the wrapper counted them, wrapping coloured lines
  early. Prefer **one label per coloured line** with a USS class (see
  `BriefingView`'s priority traffic) — that also makes the colour palette-aware,
  which an inline hex is not.
- **Affordability is an affordance, not a rule.** `TerminalView.GateOnAffordability`
  reads the `[N CP]` / `[N INF]` / `[N PC]` tag off each button label and dims
  what the player cannot pay for; `GameState.CommandCapacitySpent` drives the
  END MONTH pulse. Core validation remains the authority — keep the label
  convention or buttons silently stop being gated.
- **Never hardcode a column count in a view.** Use `TerminalMetrics.Columns`,
  which is measured from the real panel at runtime. Views used to hardcode
  64–78; that fits a tablet and overflows a phone.
- **Never trust `Screen.dpi`.** The panel uses `ConstantPixelSize` with a scale
  from `TerminalScale.ScaleFor(width, height)`. `ConstantPhysicalSize` was
  falling back to 96 DPI wherever the platform didn't report one — including the
  Device Simulator — collapsing the scale to 1 and rendering 13px text on a
  2300px screen.
- `TerminalMetrics.ShortScreen` marks a wide, short panel (a foldable's cover
  display). The `bp-short` USS class turns the nav rail back to a narrow
  vertical strip, because there height is scarce and width is not.
- ASCII figures take explicit `columns`/`rows`; `AsciiWorldMap` and
  `AsciiCountryMap` sample themselves into whatever grid they are given.

## Readability

This is a game made entirely of text, so legibility is a correctness concern.
`ReadabilityTests` parses the real stylesheet and fails the build on regression.

- **Every text colour must clear WCAG AAA (7:1)** against the terminal
  background. `terminal-text-dim` was 4.74:1 and is used for 35 secondary
  readouts — that was the weak point.
- **Never use a fixed padding in a table row** (`{name,-22}`). Use
  `AsciiChart.Cell(text, AsciiChart.NameWidth(W, share))`, which truncates with
  an ellipsis instead of pushing the row off a narrow screen.
- Readout text carries leading via `-unity-paragraph-spacing`; ASCII figures opt
  out with `AddFigure()` / `.terminal-figure`, because a gap between rows breaks
  the vertical strokes that make a map a map.
- Body prose stays sentence case. Uppercase is for labels and headers only —
  all-caps prose measurably slows reading.
- **Four palettes** (`theme-green` / `theme-amber` / `theme-soft` / `theme-signal`), all AAA.
  `signal` is a neutral bone base whose purpose is to free the hue wheel so
  colour can carry *standing* instead of atmosphere. **The GDD never specifies a
  colour** — green was an implementation choice, not a requirement.
- **Semantic colour** (`sig-hostile` / `sig-rival` / `sig-neutral` /
  `sig-friendly` / `sig-ally`) is one set that clears AAA on all four bases.
  Orange/blue, never red/green — that pair is the one to avoid for
  hostile-versus-allied. Colour is always a *second* channel: the map keeps its
  `! ~ - +` glyphs so the reading survives any palette and any colour vision.
  PLANNED: colouring glyphs *inside* the ASCII map needs `-unity-rich-text`, and
  the width measurement must strip tags or the overflow tests will misread.
  A theme is a comfort choice, never a legibility one, and a test enforces that
  for every colour in every palette.
- **`DisplaySettings`** (text size / palette / line spacing) lives in
  `PlayerPrefs`, deliberately *not* in `GameState` — ergonomics belong to the
  device and the reader, not to the save. Reached via **DSP** in the status bar,
  which ships in every build.
- Leading is applied in **code** (`unityParagraphSpacing`), not USS: a mistyped
  USS property fails silently, and a silently-ignored readability setting is
  worse than none. Never add a per-breakpoint `font-size` for `.terminal-text` —
  it double-shrinks content and overrides the reader's choice.

## Android builds

Configured via `Assets/Editor/BrinkBuildSetup.cs` (menu: **Brink →**), not by
hand-editing `ProjectSettings.asset`. Settings live in code so they are
reviewable and survive a reset.

- `Brink → Check Android Readiness` — reports config problems without building.
- `Brink → Build Android APK` / `Build Android App Bundle (AAB)`.
- Headless: `-executeMethod Brink.EditorTools.BrinkBuildSetup.BuildAndroidApk`.

Current: `com.southsidegames.brink`, IL2CPP, ARM64, minSdk 25, targetSdk 35,
landscape-only, version 0.1.0+1. First APK: 34 MB, ~7 min. Output goes to
`Brink/Builds/` (gitignored, along with `*.keystore` / `*.jks`).

**No keystore is configured** — Unity signs with its debug key, which is fine for
sideloading and rejected by Google Play. Creating one is the user's to do; the
passwords must never be committed and losing the file forfeits the Play listing.

Device test checklist: `Docs/AndroidTestChecklist.md`.

## Running tests

In-editor: Window → General → Test Runner → EditMode → Run All.

Headless (Unity editor must be CLOSED — the project lock blocks batch mode):

```
"C:\Program Files\Unity\Hub\Editor\6000.3.9f1\Editor\Unity.exe" -batchmode ^
  -projectPath "D:\Southside Games\Brink\Brink" -runTests -testPlatform EditMode ^
  -testResults "C:\Temp\brink_test_results.xml" -logFile "C:\Temp\brink_test_log.txt"
```

Quick compile check without Unity (catches syntax/type errors only): compile
`Assets/Scripts` with Unity's Roslyn (`Editor\Data\DotNetSdkRoslyn\csc.dll`)
against `Editor\Data\Managed\UnityEngine\*.dll` + `Editor\Data\NetStandard\ref\2.1.0`.
Regenerate the response file whenever a source file is added — a stale one
silently compiles the old set.

Unity nests NUnit suites, so grepping the results XML for `result="Failed"` also
matches ancestor suites. `Tools/extract-failures.sh [results.xml]` prints the
pass/fail totals and each failing test's name and assertion message.

## Conventions

- **Roster: real countries** (supersedes GDD §2/§31.1's fictional-world rule,
  per user decision). Slice roster: USA (player), CHN, RUS, IND — authored
  profiles in `WorldFactory.Profiles`, expandable toward the ~16-country target.
  Country stat baselines are gameplay archetypes, not real-world claims; keep
  that framing in comments. The flashpoint is a deliberately generic
  "Contested Sea Lane", **not** any real disputed territory — avoid modeling
  live real-world territorial claims (also an app-store risk in some regions).
- In-game calendar starts JAN 1984 (placeholder epoch; revisit for real-world roster).
- Terminal voice: cold governmental language, uppercase headers ("MAR 1984",
  "CP 5", FLASH/PRIORITY/ADVISORY/WIRE/ARCHIVE notification classes).
- Officials get country-appropriate titles (`Official.title`) from the profile.
- Monetization: premium/one-time unlock only — never add pay-to-win hooks.

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

- [x] **Every pillar's official advises, and every delegated official reports**
      (`CabinetAdvice`, spec 15 §12). The rule is symmetrical and it is the whole
      feature: **an official is either running their pillar or advising on it,
      never both.** `CabinetAdvice.ShouldAdvise` is the single gate
      (`mode == DirectControl`) and `MilitaryAdvice.Recommend` is held to it too.
      - Advising a pillar the operator has *delegated* is noise at best and a
        standing invitation to interfere with somebody doing their job at worst.
      - Direct Control cost CP and the official's trust in exchange for nothing
        but control. The expert's opinion is what it now buys.
      - A delegated view says so in words. An absent panel reads as a missing
        feature rather than as a consequence of a choice the operator made.
      Reliability is `competence/100` and is **printed** ("Their record is mixed
      (competence 54)"), so a bad recommendation is fair rather than a trap. A
      poor desk does not emit noise — it reaches for a plausible *wrong*
      instrument, which is what a weak minister actually does.
      `GameState.cabinetReport` is **cleared every month** (it describes the month
      just resolved; `chronicle` is the permanent record) and carries
      `ownJudgement`, so the briefing's YOUR CABINET section distinguishes the
      operator's instruction being carried out from somebody else's decision being
      made for them.
- [x] **Routine restocking belongs to the military desk, in every country.**
      Replacing losses is maintenance, not strategy. It used to sit four gates
      deep in `AISystem.RebuildForces` behind a `Security`-priority check, so
      across thirty measured years **no foreign government ordered a single piece
      of equipment** while the player could replace anything. Same most-repeated
      bug — an AI state locked out of a player verb — by a new route: not a
      missing API, but an API placed somewhere unreachable. **When you add a verb,
      ask not only whether the AI *can* call it but whether it ever *will*.**
      `AcquisitionSystem.WorstShortfall` is now the one definition of "what are we
      short of"; the three copies had already drifted, and only the AI's knew a
      landlocked state should not order carriers.
- [x] **`EventNature.Opportunity`** — the invariant "a stable world manufactures
      no crises" could previously only be satisfied by a world where nothing
      *good* ever happened either. A prosperous, capable country should still make
      a discovery; adversity is the thing calm must not invent. Events default to
      `Adversity`, so marking one an opportunity is always deliberate.
- [x] **National unity damps hardship; it does not cancel it.** `unrestPressure`
      subtracted `nationalUnity × 0.25`, giving every country an absolute immunity
      budget — at an ordinary unity of 60 the first 15 points of hardship
      registered as nothing, and four years of severe deprivation produced
      literally zero organised anger. A subtraction is the wrong shape for a
      resilience term. Now a multiplier (`1.25 − unity/125`).
- [x] **The hand-wired-pipeline sweep — `SimulationPipeline` was only half-fixed**
      (`PipelineWiringTests`). The earlier fix repaired
      `VerticalSliceValidationTests` and stopped there. **Twenty-seven other test
      files hand-wire their own monthly system list**, and the bug class had
      already struck a second time undetected:
      `OverALongGame_SomeGovernmentBuildsAnInstrument` registered three systems,
      so `EstimateConfidence` stayed pinned at its 0.35 floor (making the caution
      penalty on threat perception permanently maximal, so **no AI rivalry could
      ever form**, so nothing reached the 24-month standing an instrument needs),
      no AI pillar could move past the ≥65 gate, AI political capital was never
      credited — the whole `ConsolidateHome` branch silently no-opped — and there
      was no treasury income, which is why the test injected 30,000 to paper over
      it. It concluded that no government builds a strategic instrument in twenty
      years. **What it had actually built was a world where the AI could not
      think**, and it reported the result as a fact about the AI.
      **A narrow pipeline is still legitimate** — a test asserting one month of
      economic arithmetic should not pay for sixteen AI governments to deliberate.
      What is not legitimate is doing it *by accident*, or while making a claim
      about how the world behaves over decades. So the rule is **hand-wire on
      purpose, in writing**: a `NARROW PIPELINE: <reason>` comment naming which
      systems are omitted and why the assertions survive without them. The 26
      unreviewed files are listed in `PipelineWiringTests.Grandfathered` as
      **written-down debt — the list may only shrink**, and a new file that
      hand-wires fails the build. The bug was never that 26 exist; it is that
      number 27 could be written without anybody noticing.
      **The sweep is now complete.** All 26 were audited. **Eight were making
      long-run claims about a world that could not behave** and were converted:
      `WorldInvariantTests` (360mo), `AISystemTests` (360mo),
      `GovernmentSystemTests.LongRun` (240mo), `ChronicleTests` (240mo),
      `SaveMigrationTests` long-run (240mo), the `CrisisSystemTests` and
      `EventCatalogTests` determinism helpers (200mo), `ProgressionSystemTests`
      `TenYearRun` (120mo), `AsciiWorldMapTests` (120mo) and one
      `BugRegressionTests` site. The other 18 set their own preconditions and
      assert one system's arithmetic; each now carries a `NARROW PIPELINE:` line
      naming what it omits and why its assertions survive the omission.
      Three worth remembering:
      - **`WorldInvariantTests` had no `AcquisitionSystem`**, so
        `ThirtyYears_TheWorldsArmiesDoNotHollowOut` was measuring supply in a
        world where nobody could buy anything — the harness that should have
        caught the restocking bug was structurally blind to it — and
        `ASaveResumesIndistinguishably` never exercised equipment orders or
        secession-created states for save fidelity.
      - **Two 200-month "is deterministic" tests ran with only
        `CrisisSystem.SystemicCheck` attached** — a world frozen at month zero.
        Event eligibility reads economy, government, social and intel state, so
        they sampled one static eligibility set forever and called it determinism.
      - **`AsciiWorldMapTests`** claimed a decade of war and regime change must not
        corrupt the map while omitting the only two systems that create countries
        (`SecessionSystem`) or move ground (`TerritorySystem`).
      `AISystemTests.FullSimulation` carried the doc comment *"every system live,
      as the real game runs it"*. It was false for eight systems. **A comment
      asserting correctness is not a check.**
      The `Grandfathered` list is now **empty and must stay empty** — a grandfather
      list that outlives its debt stops recording what needs fixing and quietly
      becomes a list of permitted exceptions.
      Still open: `EventCatalogTests.QuietWorld_ProducesNoCrises` is proven only
      against a *frozen* world — `SettleTheWorld` does not pin `socialUnrest`,
      `publicGrievance`, `livingStandards` or leader age, so under the real
      pipeline `SUCCESSION_QUESTION` and `DEFECTION` would fire for reasons
      unrelated to its claim. Making it real needs the helper extended.

- [x] **The economy had no crisis regime** — found by chasing a social-layer test.
      A country whose market index had fallen from 100 to **7.6** (sectors gutted,
      confidence 12, energy 7) was still reporting 9% unemployment, 9% inflation
      and living standards of 31.7. That was the *worst outcome the model could
      produce*. Three things were structurally impossible: **mass unemployment**
      (driven only by growth and sanctions, and growth is bounded, so it could
      never pass ~12); **real deprivation** (the market-index term ran at
      0.15/point in both directions, so total collapse subtracted only 14 points);
      and **grievance accruing at all** (it needed living standards below 35,
      which yielded 0.026/month against a *flat* 0.045 decay — pinned at zero —
      or unrest above 45, which unrest could not reach without the grievance it
      was gated behind: a circular deadlock where the memory of hardship required
      hardship the country was not allowed to suffer).
      Consequences: **economic warfare could never reach a population** (sanctions,
      blockades and bombing moved numbers nobody feels), and **every event gated on
      unrest was dead content** — `GENERAL_STRIKE` (58), `SEPARATIST_MOVEMENT` (42)
      and `PORT_STRIKE` (40) could not fire in any playthrough.
      Fixed with a `distress` term (`max(0, 55 − marketIndex)/55`) feeding
      unemployment ×22 and inflation ×9, **zero by construction in normal play** so
      it adds a crisis regime without retuning the ordinary one; a steeper
      market-index slope below the line (0.42) than above it (0.15); grievance
      decay made **proportional** (`0.045 + grievance × 0.004`) so every level of
      hardship has a resting point instead of ratcheting to the cap; and unrest
      rising faster than it falls (0.11 / 0.05) because anger organises quickly and
      disperses slowly. **The crisis regime has a multi-year lead time** — the
      market index is fundamentals-anchored, so a test wanting to observe a real
      collapse must run four years, not two.

- [x] **Restocking was impossible for everyone** — `BranchForce.SetStrength`
      rescales the inventory to match, so `have == target` held **by construction**
      and `WorstShortfall` could never return a ratio below 1.0. The AI's
      `OrderWhatIsShort`, the player's PREPARE FOR WAR minister and the routine
      desk restock all rested on a comparison that could only answer "fully
      stocked". `AcquisitionSystem.EstablishmentFor` anchors on `pillars.military`
      instead: **capability is what the nation can support, branch strength is what
      it has fielded today.** Both paths to a gap now work — losing a division
      drops fielded strength below establishment, and growing the pillar raises
      establishment above what is fielded.

- [x] **Balance re-measured after the pipeline audit and the economy fix.**
      **These supersede every earlier figure** — the previous numbers predate the
      crisis regime, the social layer's real range and eight harness conversions.

      | Playstyle | Was | Now |
      |---|---|---|
      | PASSIVE (baseline) | 2.58 | **2.24** |
      | DIPLOMACY | +0.82 | **+0.84** |
      | ECONOMY | +0.92 | **+0.78** |
      | GOVERNMENT | +0.40 | **+0.44** |
      | MILITARY | +0.62 | **+0.40** |
      | INTELLIGENCE | +0.48 | **+0.38** |

      Healthier than before: every playstyle still beats passive and the spread
      **tightened from a 0.60 range to 0.46**, with ECONOMY no longer an outlier.
      The passive baseline falling is the economy work landing — a world that can
      express hardship punishes inattention.
      Two notes for whoever reads this table next:
      - **`CRIS` is 70.1 for all six playstyles, identical to the decimal.** Not a
        bug: every harness bot resolves every crisis it faces, so the ratio is
        always 1.0 and only the count of quiet years varies. **11% of the grade is
        a constant in every measurement we have**, and the harness cannot say
        whether crisis handling is tuned. A bot that lets crises lapse would be
        needed to find out.
      - **INTELLIGENCE's trajectory is 43.4 against 53–57 for everyone else** — a
        10-point gap and the specific reason it is now last. Consistent with the
        counter-play design (an operator caught running covert action finds
        hardened services), but worth checking it is not overtuned.
      - MILITARY's ECON component (17.2 vs 28–51) still reflects the documented
        opportunity cost of commitment: the bot takes its objective in 4/5 seeds
        and still ends at net −1 location. **That conclusion survived the harness
        being corrected**, which is the point of re-running it.

- [x] **Being caught spying cost the wrong thing** (GDD §14). `IntelligenceSystem`
      charged exposure `pillars.diplomacy -= 4f` — raw, undamped — beside a
      notification reading *"Diplomatic damage taken"* while **nothing in the block
      touched a single relationship**. Three faults: it charged national
      *capability* rather than *standing* (an expelled station chief changes how
      others regard you, not how capable your foreign ministry is); it had **no
      recovery path for the operator incurring it**, since only the diplomacy
      playstyle regrows that pillar (~0.16/month against a −4 hit); and because it
      moved a pillar the whole cost landed in the evaluation's *trajectory*
      component instead of *position*. At ~65 exposures a decade it demanded ~260
      points from a pillar with a range of 70.
      **Fixing it by 80% did nothing** — cut to −0.8, trajectory moved 43.4 → 43.3,
      because even a fifth of the cost outran the only repair. The rule that
      settles it is this file's own: *does every value it decrements have a
      reachable recovery path under the same conditions?* Repairing that pillar
      meant abandoning the playstyle that damaged it, so the cost was a slow
      **disqualification, not a price**. Removed entirely; exposure now costs
      relations −9, trust −12, a diplomatic memory, and trust −1.5 with everyone
      else — all recoverable, all landing in `position` where standing belongs.
      Found alongside: the success path was `pillars.intelligence + 2f`, raw and
      undamped — the same ratchet family — and it was **masking** the penalty by
      inflating one of the five pillars trajectory sums. Now via `Growth.Apply`.
      **Two bugs pointing opposite ways net out to a plausible-looking number**,
      which is why the component breakdown mattered more than the headline grade.
      Result: INTELLIGENCE TRAJ 43.3 → **56.6**, grade +0.34 → **+0.64**.

- [x] **The `CRIS` grade component was a constant** — every harness bot resolved
      every crisis, so the resolved/faced ratio was 1.0 in every run and the
      component read 70.1 for all six playstyles, identical to the decimal. **11%
      of the grade was unmeasured in every balance figure this project ever took.**
      `Play(..., answerCrises: false)` adds a `DRIFTER` row; nothing in the game
      needed changing (`TurnManager.EndMonth` already lapses), the harness simply
      never exercised the path. Drifting measures **−0.32 of a grade** against the
      identical routine that answers — so ignoring a decision genuinely costs, and
      that design promise now has a measurement behind it. `CRIS` 70.1 → 46.7 for
      the drifter.

- [x] **`doctrineFamiliarity` never decayed** — written in one place, read in two,
      with **no decay path anywhere**. The one-way-value family again, running
      upward: knowledge of a 1984 doctrine stayed current in 2034, and the test
      asserting it "outlives the friendship" could not fail. Now decays
      proportionally in `DiplomacySystem.MonthlyUpdate` — it *should* outlive the
      friendship, so a decade of usefulness is right and permanence is not.
      **Seventh instance of this bug family.**

- [x] **Balance after all of the above — the tightest spread yet.**
      | Playstyle | Prev | Now |
      |---|---|---|
      | PASSIVE (baseline) | 2.24 | 2.24 |
      | DIPLOMACY | +0.84 | **+0.84** |
      | ECONOMY | +0.78 | **+0.78** |
      | INTELLIGENCE | +0.34 | **+0.64** |
      | DRIFTER (new) | — | **+0.52** |
      | GOVERNMENT | +0.44 | **+0.44** |
      | MILITARY | +0.40 | **+0.40** |
      Range **0.44** (was 0.60 two measurements ago). MILITARY is now the low
      outlier; its ECON component (17.2) is the documented opportunity cost of
      commitment and has survived every harness correction, so treat it as
      **measured and closed** unless the war-outcome table changes.
      Note `DRIFTER` runs the *diplomacy* routine, so the honest comparison is
      DRIFTER vs DIPLOMACY (−0.32), not DRIFTER vs the other playstyles.

**Harness hazards found the hard way (all three fail silently):**
1. **A killed run leaves a complete-looking results file.** No compile error, no
   abort message, previous totals read green. `extract-failures.sh` now requires
   Unity's clean-shutdown line. Caught only by checking a renamed test appeared.
2. **`GameLog.MirrorToUnityConsole` during a balance run produced a 577 MB log**
   (every mirrored call carries a ~40-line stack trace × seventy country-decades)
   and that is what killed the run. Mirroring is now on only to print the report;
   16.7 MB. **Never mirror during a playthrough.**
3. **A narrow fixture can be the only thing holding a test up.**
   `TerritorySystemTests` has three tests whose injected values drift *the
   direction the assertion looks for* — adding `GovernmentSystem` there converts
   three real tests into three that always pass. Documented at the wiring line;
   the robust fix is A/B against an unoccupied control.

**Test-authoring rule learned three times in one session: assert your setup took
hold before judging what it caused.** Three tests injected a symptom that a
system downstream recomputes from causes, then measured the recovery and called
it the damage — `HardshipEventuallyOrganises` (inflation/unemployment, erased by
`EconomySystem` before the social tick read them),
`SevereDistress_ErodesApprovalAndStability` (inflation/growth, approached back to
normal by month 3 of a 6-month test) and a drawdown test that watched one asset
class while the code sold another. One line — `Assert.Greater(inflation, 8f, "the
economy never became distressed")` — turns a silently-wrong test into one that
reports its own broken fixture. A sweep for this across the suite is worthwhile.
- [x] **A secession successor gets a real cabinet.** `WorldFactory.AppointCabinet`
      silently returned when a country had no authored profile, which every
      breakaway state is by definition — so a sovereign state existed with nobody
      governing it. It now falls back to the parent's name pool and **warns**
      instead of returning quietly.

- [x] **The military pillar compounds** (`BranchForce.experience`, save v5 → **v6**).
      Every other pillar accumulates — the economy grows, networks deepen,
      treaties stack, institutions strengthen — and **a decade of war left the
      army where it started, minus the losses.** That, not conquest yields, is the
      structural reason militarism graded lowest (ECON component 17.2 vs 28–51).
      Three rules stop it being an eighth one-way value:
      1. **It decays without use** toward a ceiling set by the force's own
         readiness and doctrine investment. Nothing in the monthly tick raises it;
         peacetime improvement must be bought.
      2. **Replacements dilute it** (`AbsorbReplacements`, applied on delivery).
         A force that takes 45% casualties and buys them back is at full strength
         on paper and measurably worse in the field. **This is what finally makes
         attrition cost something that does not refill.**
      3. **Everyone has it.** Written and read actor-generically.
      Modest multiplier (0.88 → 1.18), deliberately the smallest of the three on
      `EffectivePower`: what a force has learned should matter, never enough to
      beat having more of it. **Exercises are the peacetime route to it**, which
      finally justifies their exposure cost.
      **Scaled by the assessed odds, not by losses taken.** The first version used
      losses, on the reasoning that a bloody fight teaches more — it measured
      backwards, because losses follow verb intensity and casualty appetite far
      more than what you were up against. The odds are the only term in the
      resolution that knows how hard the fight was.
- [x] **Three military readouts** — all data that existed and was never shown:
      - **`WHAT WE ARE LEAVING UNCOVERED`** — `TheatreSystem.IsOverstretched` had
        two readers, `AISystem` (as an opening to attack us) and an event gate.
        **The AI could see our overstretch and the operator could not**, and the
        harness showed the cost: the military bot takes its objective in 4 seeds
        of 5 and still ends at net −1 location. Losing ground while committed is a
        fine thing to happen; happening *invisibly* is the difference between a
        trap and a decision.
      - **Casualties** — `initiatorCasualties`/`defenderCasualties` were counted
        every operation and had **zero readers in the entire UI**. Ours stated
        plainly, theirs as a band through `IntelReadout`: we count our own dead and
        estimate the enemy's, which is correct fog discipline *and* an honest
        description of what a government at war knows.
      - **`BALANCE OF FORCES`** — `MilitaryAdvice` says whether *this strike* is
        wise and only exists once a war is running, so intelligence paid off
        during a war and never in the decision to start one. Reports
        `EffectivePower`, not paper strength, so a large force that cannot move
        reads as one. **NO ASSESSMENT for an uncollected state is the feature.**

- [x] **A latent crash since `SecessionSystem` shipped.** `RegimeSystem.MonthlyUpdate`
      enumerated `state.countries` while, three calls down, `SecessionSystem.Fracture`
      appended a breakaway to it. Needs a successful coup then an eight-month
      collapse neither unity nor loyalty recovers from, so nothing exercised it and
      a 30-year stochastic run found it first. Fixed by iterating a snapshot, which
      is also the right semantics (a state that secedes this month should not then
      be processed for its own coups in the same tick). **`state.countries` is
      genuinely mutable at runtime now** — any monthly loop over it that can reach
      `Fracture` needs the same treatment. Deterministic regression test added.

**Three tests found to be measuring something other than what they claimed** — all
three had passed for a long time:
- `AHardFightTeachesMoreThanAWalkover`: the easy arm **captured** the position on
  the first assault and then "attacked" ground we already held four more times,
  banking four free wins. The fixture was running a different experiment.
- `RaiseReadinessDirective_IsPaidForInTreasury`: the autonomous *Economy* minister
  adds `treasury += amount * 10`, precisely what the military directive subtracts.
  They cancelled, so the result turned on which official rolled better competence
  — it was **passing on a coin flip**. Third instance this session of *measuring a
  net when the claim is about a delta*; measure against a control run.
- `ADecadeOfCrisesLeavesAMarkOnTheWorld`: sampled summed relations plus two
  counters, and crisis effects in that world are overwhelmingly TRUST and
  MARKET_SHOCK. The decades genuinely differed (trust 44 vs 28, confidence 4.5 vs
  0.0, conspiracy 20.4 vs 31.9) and it reported them identical. **Relations is the
  worst possible field to sample over a decade** — it mean-reverts and clamps, so
  a one-off ±8 nudge is erased inside ~20 months, and any test relying on one
  surviving is flaky by construction. It had been passing only because
  `DIPLOMATIC_INSULT` was eligible in every world; tightening that eligibility
  removed the coincidence.
  (`crisesFacedThisYear` is useless as a non-vacuity counter — it resets annually
  and reads 0 at the end of a decade run. Count locally.)

- [x] **The telemetry ratchet detector cried wolf.** It gated on `up + down < 6`,
      which counts *months that moved*, not *how far* — seventeen nudges of 0.11
      cleared it as easily as six moves of five points. It flagged the economy
      pillar for travelling 85.0 → 86.9 in three years: a heavily damped value near
      its ceiling doing nothing, the opposite of a ratchet. With the only
      ordinary-play downward path firing at ~3%/month, P(no decrease in 35 months)
      ≈ 34% across six watched values — **a healthy session flagging something was
      close to the expected outcome.** Added a series-level magnitude gate
      (`max(5, 5% of start)`); the planted-fault tests travel 11.5 points and still
      catch.
      **Separately real and NOT fixed:** recession, sanctions, embargo, debt crisis
      and falling industrial capacity all move `economy.confidence`/`growthRate`/
      `marketIndex` and **never touch `pillars.economy`** — a decade of depression
      leaves national economic *capability* untouched, its only recurring downward
      path being a minister's bad month. Compare the military pillar, which erodes
      from losses, peace terms and purges. Adding a drag to silence the detector
      would have left the detector still eager; this deserves its own measurement.

- [x] **Wars have verdicts, countries have records** (GDD §20 amendment).
      `Close` took an `objectiveAchieved` bool **from its caller** — settling
      always passed true, conceding always passed false — so "did we win" was a
      property of *how the war ended*, not what it achieved, and it was never
      stored, only used to pick a notification headline. `DetermineVerdict` now
      measures it, in this priority order:
      1. **Conquest overrides the declared objective.** A war opened to dent a
         rival's army that ends with every one of their locations in our hands is
         a victory whatever the paperwork said — the case an objective-only test
         gets wrong.
      2. The declared objective (holding what was demanded of you wins a
         defensive war).
      3. The balance of the terms (`PeaceSystem.IsDemand` names which way a term
         points, so pricing and verdict cannot disagree).
      4. Relative damage — **casualties as a share of the force that took them**,
         or the larger power loses every war it fights.
      5. `Stalemate`, which is the default and a real answer.
      `CountryState.warsWon/Lost/Drawn` is kept for **every** country. Only
      escalations that reached fighting count. Zero is correct for old saves — a
      world that predates this has no *recorded* history and inventing verdicts
      nobody measured would be guessing — so no migration step.
- [x] **STANDING — ranked by what we believe, not what is true.** A global power
      table built from real values would hand over every rival's strength for free
      and make collection pointless. Order comes from our own estimates, uncollected
      states are unranked, and a state running deception sits in the wrong place on
      purpose. Being wrong about who is second is a consequence of not having looked.
- [x] **Directives report completion** — reported from play: *"I can tell the
      military leader to prepare for war but I never know when we are ready."*
      Every directive was an order issued into silence. `DirectiveDef.progress`
      (0..1, or **-1 for a standing policy with no finish line**) drives a bar in
      CABINET; crossing 1.0 fires a **PRIORITY** briefing item once. PRIORITY not
      FLASH — §28.2 reserves FLASH for a turn that cannot be taken without
      deciding, and this needs no decision.
      PREPARE FOR WAR measures the shortfall **through the same call the ordering
      uses**, so bar and buying cannot disagree. RAISE READINESS uses
      `min(readiness, supply)` — readiness alone would call an unsupplied army
      ready. The flag resets if the goal is lost again *and* when the directive
      changes, so an instruction can complete more than once per save.
      AUSTERITY / DRAW DOWN / PRESSURE RIVALS deliberately have no bar and say so.

- [x] **The END MONTH overflow — five defects, one report.** "Screens fit, then I
      hit END MONTH and words are off screen."
      - **`AsciiChart.LineChart` was always ten columns wider than asked.** Each
        row prepends a `{value,8:F1} │` gutter and it plotted `width` points on top
        of it. `marketHistory` gains an entry per resolved month, so the chart grew
        **one column every END MONTH** — fine for three years, then creeping off a
        phone one character at a time. Figures opt out of `ApplyTextPolicy` by
        design, so nothing downstream catches it. `GutterColumns` names the cost;
        `TheMarketChart_NeverExceedsTheColumnsItWasGiven` guards it, the equivalent
        of the map guards that already existed.
      - **The briefing wrapped to the wrong width.** `.crisis-panel` is `width: 80%`
        and a *sibling* of the content host, but the overlay was wrapped to
        `TerminalMetrics.Columns`. New `TerminalMetrics.OverlayColumns` derives it;
        keep the 0.8 in step with the USS.
      - **`Line()` added one CSS class.** A briefing line built as `sig-rival`
        failed `IsReadout` and escaped the wrapper, while a plain line carried
        `white-space: pre` and could not soft-wrap — *two different failure modes on
        adjacent lines*, which is why **some** words broke.
      - **END MONTH never refreshed the active view.** `EndMonth` does not raise
        `StateReplaced` and the log handler only touches the status bar, so the
        screen behind the briefing kept last month's numbers until a nav tap.
      - BALANCE OF FORCES was built `name + 49` fixed characters — ~61 columns on a
        49-column phone, in an unwrapped figure. **Never hardcode a column count in
        a view**; it is now two lines per state derived from `W`.
- [x] **Tutorial was unfinishable on a phone.** `.tutorial-panel` was
      `flex-shrink: 0` with no cap, so a long step pushed ACKNOWLEDGED and DISMISS
      below the fold with nothing scrollable — the tutorial could be started, not
      finished, and not dismissed. Only the *prose* scrolls now, capped against
      `TerminalMetrics.PanelHeight`; title and buttons are pinned outside it.
      Scrolling the whole panel would have left the buttons off-screen until the
      operator discovered a box with no scrollbar scrolls.
      Also: the panel showed during the **assessment** as an empty green box —
      `RefreshAll` returns early while awaiting assessment, so `Refresh` (the only
      thing that hides it) never ran. Hidden explicitly on that path now.
- [x] **"NO ASSESSMENT" was three situations wearing one label.** Reported from
      play as a bug: an operator tasked a network against Russia, opened MILITARY,
      and read NO ASSESSMENT. Collection covers *all* domains (focus 1.0, others
      0.45) but **only runs at END MONTH**, so a network bought this month reports
      next month. Now `UNTASKED` / `COLLECTING` / `BURNED` with a legend — an
      absence should say what would change it, like the after-action reports.

- [x] **Four things reported from a play session, and the bug class under two of
      them.** All four were "the game is broken" reports about mechanics that
      were working — which is its own kind of broken.
      - **"Sometimes I press a button to use CP and it doesn't go down."**
        `GateOnAffordability` ran *after* `Build()` and called
        `SetEnabled(affordable)` — so it **re-enabled every button a view had
        already refused for its own reasons**. CONDUCT EXERCISE stayed bright
        through its cooldown, patronage stayed pressable with no treasury, an
        inapplicable peace term stayed selectable. Pressing any of them spent
        nothing and said nothing. **Both gates now only ever disable**; a view
        rebuilds its buttons every refresh, so there is nothing legitimate to
        re-enable. Every refusal goes through `TerminalView.Block(button,
        reason)`, first reason wins, and `ExplainBlockedCommands` prints one
        wrapped `UNAVAILABLE:` line under any row holding a refused command —
        **a tooltip is dead weight on a phone**. Procurement and logistics got
        the gates they never had (`CanBeginProcurement` / `CanInvestInLogistics`,
        one gate shared with the order, per the `OperationCatalog.CanOrder`
        precedent): treasury and programme slots refuse just as firmly as CP and
        nothing on that screen mentioned either. `AddButton`/`MakeRow` were lifted
        to `TerminalView` — six byte-identical copies returned `void`, which is
        precisely why a precondition the caller knew about had nowhere to go.
      - **"What is the difference between STG and STR?"** STRATEGIC/STG
        (the state's decisive instruments) sat next to STRATEGIST/STR (the
        operator's own record) in a rail three characters wide. Now ENDGAME/EGM
        and OPERATOR/OPR, each opening with a line saying what it is *not*.
        View ids are plain strings addressed by `AttentionSystem`, `ActionCatalog`
        and `TutorialSystem` with nothing binding them to a panel that exists, so
        `BuildPanels` is now public and static and three tests walk the **real**
        rail. A fourth fails the build when two short codes are within
        Levenshtein distance 1. The old test compared against a list hand-copied
        into the test file — it would have kept passing against panels that no
        longer existed.
      - **"I'm improving the defence of a territory I took over and I keep
        failing with no direction on why."** Four defects from one root:
        `defender` is whoever owns the target, which for an `OwnGround` verb is
        **us**. Every "bill the other side" line billed us twice (manpower, war
        exhaustion, experience for both winning and losing the same engagement),
        so fortifying a position we held cost more than attacking one we did not.
        Falling short shared the offensive failure branch, so it cost war
        support, read *"Operation against \<our own port\> failed"*, and went out
        on the **world wire** as a public failure. `DepletionFactor` ran
        backwards, scoring a thinly held occupation as *easier* to pacify. And
        the report could not explain itself: `RecordDefence` skipped
        `DefenseModel.Unopposed`, so a failed programme produced an analysis with
        **no defence factor at all** — nothing to rank, nothing for `Advice` to
        switch on, and therefore no `WHAT WOULD CHANGE IT` line. The one class
        whose whole job is to say why, silent. `Labels.Undertaking` fixes it, and
        `EveryFactorLabelHasAdviceBehindIt` now reflects over the constants
        instead of a hand-copied list (which is how the gap survived; `Speed` was
        missing too). The panel now prints each verb's assessed odds on its
        button, flags occupied ground, and shows the last programme's
        after-action — which previously existed only as one ADVISORY notification,
        since a peacetime programme has no confrontation diary to live in.
      - **"Someone else was elected and I still control the country."** Not a
        bug — it is GDD §13, and the design is right. But the game said so in a
        **trailing clause of a filterable notification**: NEW ADMINISTRATION
        carried the Government desk, so a poor minister could drop the only
        explanation of the event most likely to read as broken, while the same
        event silently revoked every granted authority. Now `ReportingDesk.Command`
        (the operator's own standing is not the government's to forward at its
        discretion), one item per handover instead of two on the term-limit path,
        an ADMINISTRATION block that opens `THIS OFFICE: PERMANENT STRATEGIC
        OPERATOR` and relabels the leader `HEAD OF GOVERNMENT`, and a new **first**
        tutorial step. A player who thinks they are the head of state reads the
        next election as the end of their game.
      **The lesson common to the first and third:** a refusal the operator cannot
      see is indistinguishable from a broken control, and an outcome the game
      cannot explain is indistinguishable from unfair dice. Both were *correct
      simulation* reported as bugs.
      **Not yet verified by a test run** — no Unity available in the environment
      these were written in. Run EditMode → Run All before trusting any of it.

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

**This works for `Assets/Scripts` and does NOT work for `Assets/Tests`.** The
NUnit assembly Unity ships is a net40 build, so compiling test sources outside
Unity produces ~1000 `CS0012 mscorlib` errors on every `[Test]` attribute, and
that flood **suppresses semantic binding in the files behind it** — a genuinely
broken call like `record.analysis` on a type with no such member is reported by
Unity and not by the local check. Filtering the CS0012 lines out makes the
output look clean, which is worse than not running it. Attempts to supply the
facade (`shims/netfx`, MonoBleedingEdge `mscorlib`) collide with `netstandard`
and produce `CS0518 System.Void is not defined`.
**So: the local check verifies runtime code only. For test code, Unity is the
authority — run the suite.** A compile check that passes on broken code is the
same class of hazard as a validation harness that does not run the game.

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

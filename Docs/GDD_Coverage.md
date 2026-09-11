# GDD Coverage — re-audited 2026-08-22

Audited against `GDD_v1.0.md`. Section numbering is unchanged in `GDD_v1.1.md`,
so every citation below still resolves; v1.1's own amendments (28.3 Causal
Legibility) postdate this audit and are **not** counted in the totals.

Every discrete requirement in the GDD §1–37, checked against the code rather
than against `CLAUDE.md`'s phase claims. Regenerate this by re-auditing; do not
edit it to match intentions.

## Current

| Range | Requirements | DONE | PARTIAL | MISSING | CONTRADICTED |
|---|---|---|---|---|---|
| §1–15 (fantasy, authority, cabinet, pillars, tech, society, government, intel, diplomacy) | 158 | 117 | 31 | 10 | 0 |
| §16–27 (map, world evolution, confrontations, military, economy, endgames, regime change, events, AI, progression, peace, civilian impact) | 101 | 62 | 25 | 14 | 0 |
| §28–37 (information architecture, saves, world content, business model, build strategy, removed concepts, Phase 2 docs) | 98 | 78 | 14 | 6 | 0 |
| **Total** | **357** | **257 (72%)** | **70 (20%)** | **30 (8%)** | **0** |

Nine of the DONE items are **done-by-decision**: an explicit choice was made not
to build the GDD's original clause. They are listed in §"Decisions, not gaps"
below and must never be re-counted as missing work.

**Compare percentages, not counts.** This pass enumerated at a finer grain than
the last one — 357 items against 307 — because §2/§10.1/§15.1's tables were split
per row and §34/§35/§36's lists per entry. Against the previous audit
(2026-08-21: 205 done / 78 partial / 22 missing / 2 contradicted of 307):

| | Then | Now |
|---|---|---|
| DONE | 67% | **72%** |
| PARTIAL | 25% | **20%** |
| MISSING | 7% | **8%** |
| CONTRADICTED | 2 items | **0** |

Both remaining contradictions are gone: the fictional-world clause was amended in
the GDD itself, and the settings-reset behaviour was corrected. Missing ticks up
slightly because the finer grain exposed clauses the coarser one had folded into
a neighbouring requirement.

"PARTIAL" mostly means the system exists and works but a clause of the GDD's
sentence is unbuilt — not that it is half-finished. The unbuilt clause is named
in every case below.

**Method.** Three independent passes, one per section range, each checking source
rather than specs — `CLAUDE.md` and `Docs/specs/` describe intent and have been
wrong before. Where a spec claimed something worked, it was verified against code.

## Closed since the previous audit

- **§16/§19 territory is worth something** — `TerritorySystem`. Held energy
  regions, industry, ports, chokepoints and airbases now feed the resource and
  military models symmetrically, and occupation costs treasury, stability and
  war exhaustion. Six airbases authored; all seven `LocationType` values now have
  instances.
- **§18.2 Primary Strategy decides a campaign** — `StrategicPressure` weights the
  committed domain ×1.0 and the others ×0.35, so economic, political and
  diplomatic campaigns have a route to the objective. **Strategic Pivot** added.
- **§18.1 escalation no longer hard-gates operations** — priced at +2 CP and
  self-escalating. Was CONTRADICTED.
- **§16 staging access** — foreign basing via a Transit commitment
  (`foreignOperatorId`), revocable, and worth real reach through
  `HostedProjection`.
- **§14 the fog runs both ways** — `AISystem.MountDeception`. The player could
  previously deceive everyone and be deceived by nobody.
- **§24.2 a reputation can be outlived** — 120-month memory window.
- **§30 the debug console no longer ships** — was CONTRADICTED. Full reset moved
  to the settings panel so it did not go with it.
- **§30 determinism hardened** — `Core/Hash.cs` (stable FNV-1a) and
  `GameState.actionSequence`.
- **§5.1 full reset erases every slot** — was CONTRADICTED.
- **§28.2 notifications** are palette-aware USS classes rather than inline hexes.
- **§31.1/§31.2** roster, locations, trade and authored posture all verified.
- **§19 `Withdraw`** can relinquish captured ground.

## Closed since (2026-08-21, later the same day)

Verified against the code, not against intent.

- **§18.1 hidden escalation pressure is read.** Was written 3× and read 0×.
  `CheckPressureBoilover` (self-escalation at ≥70, stopping short of a wider
  war), a settlement-willingness penalty, and `AddPressure` fed by sanctions and
  covert action. Also gained dwell-time accrual at Crisis, without which the
  threshold was arithmetically unreachable. Spec 01 §5.
- **§19 occupation's readiness cost applies.** The flat subtraction was erased
  every tick by `MonthlyUpkeep`'s drift back to target; the cost now moves the
  target (`min(25, OccupiedValue × 0.12)`). Spec 01 §2e.
- **§28.2 `ARCHIVE` has a producer** — `ProgressionSystem.FileYearInReview`.
- **§28.2 FLASH means "immediate decision required".** Ten decisionless sites
  demoted; a war used to emit FLASH every month. A gating bug was fixed with it:
  CONTESTED SUCCESSION was un-gated, so a leadership fight in any country raised
  FLASH on the player's own briefing.
- **§25.1 XP measures judgement, not repetition.** Per-kind diminishing returns
  within the year; economy-vs-military XP moved from roughly 5:1 to 1.1:1.
- **§31.3 recessions reach the chronicle** — `EconomySystem.TrackContraction`
  writes entry, exit and depression. (This item was already stale when the audit
  was written.)
- **§3 constitutional authority is enforced** — `GameController.MayCommand`
  now guards 21 verbs, not the one call site this audit recorded.

## The two remaining contradictions

1. **§2/§4 "hand-authored *fictional* world" vs 16 real countries.** Superseded by
   an explicit user decision recorded in `CLAUDE.md`. The GDD text is stale, not
   the code — worth amending the GDD so the two stop disagreeing.
2. **§5.1/§1 "a Settings reset erases the entire game state."** Now correct in
   behaviour and reachable in a shipped build, but listed here because the fix
   was made *during* this audit and has not been exercised on device.

## The gaps that matter most now

### 1. §28.1's information hierarchy is still one flat layer

**Partly closed.** The clause that gives the Cabinet strategic weight — "official
competence influences what is surfaced or missed" — is now implemented:
`ReportingSystem` filters each month's traffic through the official who runs the
originating pillar, so a weak minister's desk buries or loses routine items. Two
rules keep it fair: FLASH is never withheld (incompetence costs awareness, never
agency) and Direct Control removes the filter along with the intermediary. The
chronicle stays honest, so a missed item is discoverable later.

What remains is the *structural* half. Three layers are still a 12-tab rail with
a full national readout on the Briefing every month — the Deep Terminal tier
(dossiers, detailed reports, archives distinct from the summary view) does not
exist as a separate layer.

### 2. The AI's domestic half is still a reduced simulation

**Mostly closed.** AI governments now run a Political Capital economy
(`AIState.politicalCapital`) filled by the same income formula as the player's,
and their domestic repair goes through actor-generic `PublicMessagingBy` /
`InstitutionalReformBy` at the same price. `SecureResources` costs treasury and
respects the endowment ceilings it used to ignore. `PreemptProgramme` gives a
detected foreign programme distinct behaviour — suing for terms, building a
deterrent, or falling back to countering.

**Cabinets are now universal too.** Every country has five officials and
`CabinetSystem.MonthlyAct` runs all of them, so foreign capability comes from
named people who can be good or bad at their jobs, replaced by an election, or
swept out by a coup. Control modes remain the player's — a foreign cabinet is
always Autonomous, because no operator stands outside it.

**Closed. The Influence economy and Crisis Turns are deliberately not given to
AI states** (user decision, 2026-08-21). This is a decision, not a gap — do not
list it as missing work in a future audit, and do not build it.

Both are **operator interfaces, not government machinery**. Influence exists to
buy a directive *from an official who would otherwise choose for themselves* —
it prices the friction between an operator and the government they serve, and a
foreign state has no operator standing outside it, so there is nothing for the
resource to price. Crisis Turns exist to interrupt the *player's* month and
force a decision at the terminal; an AI government simply decides. Giving either
to a foreign state would add simulation the player can never see, in exchange
for no behaviour they could ever detect.

The fairness principle that made this section a top gap — that nothing may be
free for one side and paid for by the other — is satisfied by the Political
Capital economy and treasury costs above. Symmetry of *consequence* is the
requirement; symmetry of *interface* is not.

### 3. Geography has value but no *theatres*

**Mostly closed.** `GeographySystem` gives distance (cylindrical, so longitude
wraps) and projection range built from naval and air power, logistics and
strategic lift. Reach multiplies attacker power in `ResolveOperation`, floored at
0.35 so distance prices a far campaign rather than forbidding it. Ground held
abroad and hosted basing rights both move the measuring point, so a forward
position is worth taking and a Transit commitment is worth negotiating.

What remains is §16's *theatre* language proper: confrontations have no named
region, no theatre-level grouping, and no sense that a war in one place changes
what is possible in another. Reach is per-operation, not per-theatre.

## Smaller open items

- ~~No crisis or event can open a confrontation; `CrisisOption` resolves to four
  player scalars and cannot touch a relationship, market or foreign state.~~ **Stale (2026-08-27):** `CrisisEffects` has had `OpenConfrontation`, `SufferConfrontation`, `ImposeSanction`, `ForeignUnrest`, `Relations`, `Threat` and `Trust` effects for some time; 35 definitions now.
- No materials location type — a mining region pays industry or energy.
- ~~15 of 16 countries have no national traits, and AI personality is rolled per
  save rather than authored.~~ **Stale (2026-08-27):** all 25 profiles carry authored
  `traitIds` and an authored personality (±8 jitter per save). What a posting lacked
  was a purpose — see spec 22, the mandate.
- No authored histories; the chronicle starts empty at month zero.
- Cabinet **lifecycle is built** — ages, retirement, death, and a shortlist with
  a genuine trade-off when a seat opens (`CabinetLifecycle`). What remains of
  §7.3 is the *dissent* half: officials never disagree, leak, criticize,
  obstruct or resign in protest. **Out of scope by decision**, not pending.
- §12 has no Living Standards or Social Unrest, and no domestic historical memory.
- §29 optional strategic directives: unbuilt.
- No ASCII institutional art; no command prompt; no CRT scanlines or typing effects.
- Touch targets are ~18–35pt against a ~44pt guideline, and no test guards them.
- Manual save slots exist in code but have no player-facing UI since the debug
  console was gated out (GDD §30 lists slots as planned, so this is a decision).
- Reporting only drops or buries an item; it never delivers a *wrong* one.
  **Decided, not missing** (2026-08-21): a weak desk may lose an item or strip
  its urgency, never misstate a figure. Every number the operator receives is
  true; the only question is whether it arrives. Distortion would make no readout
  trustworthy and the terminal exhausting rather than tense.
- Cabinet **lifecycle** (ages, retirement, death, candidate pool) is approved
  work; §7.3's *dissent* half — officials leaking, obstructing, resigning in
  protest — is out of scope for now, as it overlaps `AuthoritySystem` and changes
  what the game is about.

# BRINK
## Consolidated Game Design Document v1.1 — Implementation-Aligned
**Updated:** September 10, 2026

> This document supersedes `GDD_v1.0.md` where the two conflict. Version 1.0 remains the historical high-level skeleton. Detailed formulas, constants, save rules and edge cases remain authoritative in `Docs/specs/`; runtime behavior is ultimately defined by the code and tests.

---

# 1. Core Experience

**Brink is a persistent geopolitical command simulation about being the strategic operator behind a government rather than the immortal ruler of a country.**

The player occupies a permanent strategic post inside a real-country government. Administrations, leaders, ministers, political coalitions and national circumstances can change around that post while the operator persists. The player receives imperfect information through institutions, decides what deserves personal attention, delegates the rest, and lives with consequences that continue to propagate through an independently simulated world.

The primary fantasy is not “control every system.” It is:

> **Read an imperfect world, decide what matters, decide what you are actually allowed to control, and spend limited attention and political authority trying to move a country without owning it.**

The world is intended to create history rather than wait for the player to create content for it.

---

# 2. Product Definition

| Area | Current Direction |
|---|---|
| Genre | Persistent geopolitical strategy / government command simulation |
| Primary platform | iOS and Android |
| Orientation | Landscape |
| Engine | Unity 6 |
| UI | UI Toolkit / UI Builder |
| Presentation | Retro classified terminal; ASCII maps, charts and reports with responsive conventional controls |
| World | Authored real-country roster used as gameplay archetypes, not claims of real capability |
| Primary mode | Persistent single-player posting |
| Connectivity | Offline-first |
| Monetization | Premium purchase or free demo + one-time unlock; no pay-to-win progression |
| Scenario Mode | Deferred |

The shipped simulation is substantially beyond the original four-country MVP. The authored world supports Standard, Regional and Full world-size configurations; the core authored roster and expansion profiles are defined in the Country Bible and `WorldFactory`.

---

# 3. Non-Negotiable Design Rules

1. **The player is the operator, not the state and not necessarily the head of government.**
2. **Every government has institutions and five Cabinet officials.** Foreign countries are not faceless stat blocks.
3. **Player control is scarce.** Command Points price direct intervention; Influence prices direction of autonomous officials; Political Capital prices institutional and political acts.
4. **Constitutional authority matters.** Some pillars may be directly commanded, some require political approval, and some are advisory-only unless exceptional authority is obtained.
5. **Delegation is legitimate play.** Autonomous officials are expected to run large portions of government.
6. **Delegation changes information as well as output.** Weak or distrusted desks can bury or lose routine information; decisions requiring action are never withheld.
7. **Nobody reads true foreign state.** Player and AI reason through estimates, public information and collection. Deception must be capable of misleading both.
8. **The AI pays consequences rather than receiving hidden national bonuses.** Difficulty changes reasoning quality and action bandwidth, not starting statistics.
9. **Prefer prices and consequences over arbitrary prohibitions.** Bad strategic choices are allowed when the country and institution can physically make them.
10. **Systems must connect.** War affects politics, economics, territory, displacement and alliances; sanctions affect markets and escalation; domestic conditions can create opposition, insurgency or regime failure; diplomacy changes who appears when a war starts.
11. **A recurring negative must have a reachable recovery path.** The simulation should produce bad decades, not irreversible numerical ratchets by accident.
12. **Skills grant operator capability, never magical national power.** National capabilities come from the state; strategist skills change what the operator can see, afford or personally do.

---

# 4. The Player’s Posting

A new game begins with an in-universe strategic assessment. Its hidden scoring helps assign a posting and strategic profile. The player may ultimately be posted to an authored country and chooses difficulty and world size.

The operator persists across administrations. Elections, succession, coups and government turnover may replace the head of government and reshuffle the Cabinet without replacing the player.

The player’s long-term identity is therefore closer to a permanent strategic office than a president, monarch or prime minister.

## 4.1 The Mandate

Every posting receives a **Mandate**: several country-specific claims about what the government expects to be true after roughly ten years. Mandates are derived from national character, vulnerabilities and circumstances and deliberately avoid becoming conquest checklists.

At review the mandate is judged as **FULFILLED, HELD or FAILED**. The save continues regardless.

A sufficiently early change of administration may reissue the mandate to reflect the incoming government’s priorities while retaining the original review horizon and posting history.

## 4.2 Standing Directives

The simulation may also offer optional, shorter strategic undertakings such as restoring readiness, balancing the books, securing energy, finding a partner, strengthening industry or improving public support. These are suggestions rather than quests: ignoring one carries no direct penalty.

## 4.3 Career Record

Postings are retained in a career record containing items such as mandate verdict, years served, mean grade, war record, difficulty and world size.

**Career is historical record, not meta-progression.** It grants no cross-save national advantage.

---

# 5. Time and Session Loop

A normal turn is one in-game month.

**MONTH START**
1. Receive Command traffic, briefing items and intelligence.
2. Inspect the country, Cabinet, foreign dossiers and strategic map.
3. Decide what can remain autonomous.
4. Direct selected officials toward desired outcomes.
5. Take Direct Control where authority permits and the issue justifies Command Points.
6. Resolve crises or alliance obligations that require an immediate answer.
7. End the month.
8. The complete world simulation resolves.
9. Reporting filters what reaches the operator.
10. Read the consequences and begin the next month.

A quiet period may be **Held** for multiple months. Holding is genuine non-intervention: Command Points are not banked and the game interrupts the hold when the operator is needed by war, crisis, a vacant office, dangerous fiscal conditions or other FLASH-level traffic.

There is no fixed campaign ending. A world can continue for decades.

---

# 6. Command, Delegation and Authority

Each of the five Cabinet offices can operate in one of three player-facing modes:

| Mode | Meaning |
|---|---|
| Autonomous | The official chooses priorities and acts according to competence, personality, national priority and conditions. |
| Directed | The operator states the desired direction; the official chooses implementation. Costs Influence. |
| Direct Control | The official stands aside and the operator chooses individual actions. Costs Command Points and may require constitutional authority. |

Foreign governments also have five officials, but their cabinets remain Autonomous because there is no external operator standing outside those governments.

### Command Points
Command Points represent limited personal strategic attention and intervention capacity. They are the core pacing resource.

### Influence
Influence is the bandwidth used to steer officials without personally running their desks.

### Political Capital
Political Capital is a government resource used for political bargaining, appointments, emergency powers, institutional reform, constitutional authority and related statecraft. Foreign governments have their own Political Capital economy.

### Trust and Reporting
Cabinet trust is a relationship state, not currency. Repeated overrides can erode trust. Competence, mode and trust affect whether routine information is surfaced, buried or missed.

FLASH-level decisions are never withheld, Direct Control removes the desk filter, and the Chronicle always preserves what actually happened.

---

# 7. The Five Strategic Pillars

## 7.1 Military

Military play is not an individual-unit wargame. Countries maintain Ground, Air and Naval branches with strength, readiness and supply, supported by logistics, doctrine, procurement, posture and strategic capabilities.

The operator can manage readiness posture, procurement, logistics, doctrine, strategic locations, operations, confrontation objectives and settlement strategy. Geography, distance, basing, logistics and commitments in other theatres affect usable power.

Territory has systemic value: energy regions, industrial centers, ports, airbases and chokepoints feed other systems. Occupation also carries persistent fiscal, readiness, political and insurgency costs.

Multiple wars are possible. Existing commitments reduce effectiveness elsewhere rather than functioning as a simple one-war rule.

## 7.2 Economy

The economy simulates growth, inflation, unemployment, GDP, confidence, sectors, trade, sanctions, strategic resources and a market index.

The current implementation also includes **public finance**: taxation, budget posture, sovereign debt, credit standing, debt service, reserves and related fiscal decisions. Deficits finance themselves into debt rather than disappearing as negative treasury bookkeeping.

Economic warfare has blowback. Trade exposure, sanctions, embargoes, reserves, adaptation and diplomatic cooperation determine whether coercion hurts the sender, the target or both.

## 7.3 Intelligence

Foreign capability is observed through estimates with margins and confidence grades. Networks collect in Military, Economic, Political and Diplomatic domains; stale information degrades.

Deception can bend estimates. Counterintelligence can compromise networks. Covert action can sabotage, influence, steal, provoke, support armed movements and interact with technologies and agents according to available capabilities.

Intelligence also produces higher-order assessments about foreign intentions and programmes rather than only reporting numerical strength.

The **Dossier** is the main payoff surface: one foreign country’s identity, estimated capabilities, personnel, agreements, conflicts, internal conditions, bilateral relationship and historical record are assembled without bypassing fog of war.

## 7.4 Diplomacy

Diplomacy is built on relations, trust, asymmetric dependence, threat perception, strategic alignment and historical memory.

Bilateral statecraft includes outreach, treaties, treaty deepening, basing/transit, sanctions-related negotiation, mediation, recognition, summits, normalization and other episodic instruments.

The world also contains first-class multilateral structures:

- **Coalitions** coordinate a particular confrontation.
- **Blocs** create persistent strategic sides and may carry uniform multilateral commitments.
- **The Chamber** provides a worldwide agenda for condemnations, sanctions mandates, relief and public votes, including permanent seats and vetoes.
- **Alliance obligations** can cascade. Honouring a guarantee opens a real confrontation and can invoke the opposing side’s guarantors in turn.

A promise is therefore not just a modifier. It can determine who is at war tomorrow.

## 7.5 Government

Government type changes how power works rather than granting a generic modifier. Presidential republics, parliamentary republics, dominant-party states, centralized republics and monarchies distribute authority differently and use different succession logic.

The pillar includes Political Capital, constitutional authority, civic posture, public messaging, political bargaining, patronage, inquiries, institutional reform, appointments, emergency powers, leadership succession and elections.

Domestic politics now contains an **Opposition** with a concrete case and theme—Hardship, War, Corruption, Liberty or Drift. The operator may concede or confront it, but the correct instrument depends on what the public is actually angry about; confronting a visible hardship or unpopular war can make the case stronger.

Regime breakdown, coups, civil conflict and secession emerge from accumulated conditions rather than random game-over cards. The operator remains at the terminal when the country enters a worse political state.

---

# 8. Cross-System World Simulation

The defining implementation change since v1.0 is that Brink is no longer primarily a collection of five pillar dashboards. The monthly pipeline deliberately connects them.

The world resolves Cabinet activity, military upkeep, acquisition, economy, fiscal policy, industry, sanctions, intelligence collection and products, agents, bilateral diplomacy, blocs, accession, the Chamber, government, opposition, regime stability, secession, technology, strategic instruments, territory, insurgency, displacement, AI strategy, progression, confrontations, foreign crises, player crises, mandates, directives and telemetry/history in a load-bearing order.

The intended result is **second- and third-order consequence**:

- A war can damage trade, exhaust the public, create displacement, activate guarantees and change domestic politics.
- Occupation can create an insurgency; an adversary may secretly sponsor it; exposure can damage the sponsor’s standing.
- Economic deprivation can generate opposition or armed movements rather than remaining an economy-screen number.
- Displaced populations can burden or benefit neighboring states, alter unrest, and become a multilateral relief question.
- Sanctions can affect markets, bilateral hostility, confrontation pressure and bloc/chamber behavior.
- A new government can reshuffle officials, alter national priorities, revoke previously granted authority and revise the operator’s mandate.
- A bloc-versus-bloc guarantee cascade can transform a local confrontation into several simultaneous wars.

The simulation—not authored narrative—is the main story generator.

---

# 9. Insurgency, Proxy War and Displacement

## Insurgency
Armed movements are generated by existing conditions such as occupation, deprivation and separatist pressure. The player cannot simply click “create insurgency.” A sponsor may find an existing movement and support it.

Movements have support, strength, sponsorship, exposure and a cause. They can deny territory’s economic value, impose military and political costs, liberate occupied land or worsen separatist and domestic instability.

Sponsorship is available to AI governments as well as the player and carries attribution risk.

## Displacement
War, insurgency, occupation, hunger and deprivation can displace populations into nearby states. Host countries absorb fiscal and social costs but can also gain labor/industrial benefits.

Border policy is a national political posture. Closing a border protects capacity while generating diplomatic and source-country consequences; AI governments make the same kind of decision.

---

# 10. Technology and Strategic Instruments

Technology remains capability-based rather than a sixth pillar or Civilization-style universal tree. Research programmes require time, industrial capacity and funding. Knowledge can diffuse through alliances, exercises, observation, espionage and sharing.

The capability catalogue has expanded substantially from the original skeleton and includes capabilities that unlock entirely new options as well as efficiencies.

Each pillar has access to a decisive strategic instrument after years of preparation and the required national capability:

- Military — Strategic Destruction
- Economy — Systemic Financial Collapse
- Intelligence — State Destabilization
- Diplomacy — Strategic Isolation
- Government — Total National Mobilization

These are not victory buttons. Preparation is visible or discoverable, use has systemic consequences, AI states can pursue them, and severe use can justify wider escalation.

---

# 11. AI and World Autonomy

Foreign governments pursue objectives from imperfect information. They maintain personalities, rivalries, cabinets, political resources, fiscal behavior, diplomacy, research and war management.

Difficulty modifies reasoning bandwidth, planning horizon and the number of objectives a government can actively pursue. It does not grant hidden national-stat bonuses.

AI governments use actor-generic versions of the same national instruments wherever practical. The fairness rule is **symmetry of consequence**, not symmetry of operator interface: foreign states do not receive player-only Command Points, Influence or Crisis Turns because those resources represent the existence of the player’s permanent operator post.

The world is expected to produce events the player did not cause.

---

# 12. Progression, Evaluation and Purpose

The operator earns XP through meaningful strategic decisions and receives an annual evaluation with visible components. Repeating the same kind of action pays diminishing XP within the year so progression rewards judgment rather than button frequency.

Skill Points develop the operator across the five pillars and hybrid disciplines. Skills may improve information, intervention capacity, persuasion, efficiency or access to advanced operator actions; they do not directly increase the nation’s force strength, treasury or pillar values.

Annual evaluation measures trajectory, economic performance, political stability, strategic position, crisis handling, initiative and efficiency, with adversity considered. Conquest can contribute to strategic position and grading, but the game does not require conquest and mandates do not ask for foreign land.

A ten-year Mandate provides medium-term purpose; annual evaluations provide feedback; standing directives create shorter opportunities; the persistent world provides the long-term reason to continue.

---

# 13. Information Architecture and Presentation

Brink is presented through an old classified government terminal. The visual constraint is functional as well as aesthetic: complex world state is communicated with text, ASCII maps, charts, dossiers, institutional reports and alerts rather than large quantities of bespoke animated art.

The UI is responsive to phones, large phones, foldables and tablets. Terminal text is laid out on a measured character grid so larger screens expose more information rather than granting gameplay advantage.

Notification classes distinguish immediate decisions from priority reporting, advisory traffic, general wire traffic and archival records.

Audio responds to strategic context—peace, tension, crisis and war—and to important terminal events and outcomes.

---

# 14. Persistence and History

`GameState` is the authoritative serialized world. The game autosaves after resolved months and important forced decisions so consequences stick.

The Chronicle preserves world history independently from what the operator was successfully told at the time. Historical records therefore matter both as flavor and as a way to discover information a weak institution failed to surface when it happened.

A full reset erases the save-specific world, posting and strategist progression. The separate Career Record is historical only.

---

# 15. Current Implementation Status

The original v1.0 §34–§37 roadmap is **superseded** as a description of current development state.

The twelve original vertical-slice phases are built. The project now has 25 system specifications, with the major post-MVP systems implemented, including:

- constitutional authority and universal Cabinets
- reporting quality and institutional information loss
- strategic geography, territory value and multiple theatres/fronts
- fiscal statecraft
- expanded research/capabilities
- advanced intelligence products and additional intelligence content
- diplomatic second-act actions
- blocs and multilateral alliance obligations
- the multilateral Chamber
- insurgency and proxy war
- domestic Opposition
- displacement and border policy
- dossiers and Hold
- posting Mandates
- standing directives and Career Record
- driven contextual audio

The project should therefore be treated as an **integrated simulation in balance/validation and content-refinement work**, not as a design skeleton awaiting Phase 2 specifications.

## Current validation priorities

The August 29 return document records that the post-merge union of the alliance work and content update had not yet been validated as one complete tree. Before adding major systems, the first engineering priority remains running the complete suite and then re-measuring the current world rather than trusting pre-merge balance tables.

Areas specifically requiring measurement or hardware validation include long-run sanctions behavior, alliance-cascade/world-heat behavior, Regional and Full world-size balance, and real-device touch/readability cost on dense screens.

---

# 16. Explicitly Superseded v1.0 Statements

The following v1.0 ideas should no longer be used to infer current behavior:

- **“Phase 2 specifications are the next work.”** They exist; 25 system specs now document the implementation.
- **The world is only a four-country vertical slice.** That was an MVP validation target, not the current authored simulation.
- **Blocs are merely alignment identity.** They can now carry fixed uniform multilateral commitments and participate in alliance cascades.
- **The player lacks an explicit objective beyond yearly grades.** Every posting now has a ten-year Mandate and can receive standing directives.
- **Strategist history ends completely with a save.** Mechanical progression still does, but a non-mechanical Career Record persists posting history.
- **Conquest receives no annual-evaluation credit.** Conquest now contributes to strategic position and annual scoring, while Mandates remain non-conquest objectives.
- **The information layer is a flat set of pillar dashboards.** Dossiers, reporting filters, Chronicle, Briefing hierarchy and Hold now create distinct information/pacing layers.
- **Foreign capability is largely abstract AI growth.** Every government has a five-person Cabinet whose competence affects its national performance.

Still intentionally deferred or excluded:

- launch Scenario Mode
- pay-to-win progression or energy timers
- individual-unit / province-painting warfare
- cross-save power progression from the Career Record
- AI Influence and AI Crisis Turns (operator interfaces, not national machinery)
- numeric misinformation from the Cabinet reporting filter; desks may miss or bury information, while uncertain intelligence estimates may still be wrong by design

---

# 17. Source-of-Truth Order

When sources disagree, use this order:

1. **Runtime behavior and tests** — what Brink actually does.
2. **As-built system specifications in `Docs/specs/`** — detailed design contract for that behavior.
3. **This GDD v1.1** — current product identity, cross-system rules and high-level direction.
4. **GDD v1.0 / historical planning documents** — rationale and superseded history.

Any behavioral change should update its owning system specification in the same commit, and any change that materially alters what Brink *is* should also update this document.

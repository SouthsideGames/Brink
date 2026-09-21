
# Unknown Game / Brink — Consolidated GDD v1.1

**This is the source of truth for design.** It supersedes `GDD_v1.0.md`, which is
kept unchanged as the historical record of what was extracted from
`Unknown_Game_Consolidated_GDD_v1.0.docx`. Where the two disagree, v1.1 is right.

v1.1 is v1.0 plus the amendments the implementation has since established as
non-negotiable. Amendments carry their date and the reasoning that produced
them, in place rather than in a changelog, so a reader meeting a rule for the
first time also meets the argument for it.

**Changes in v1.1**

- **28.3 Causal Legibility** (2026-09-11) — new, non-negotiable. Important
  player-facing outcomes must be causally legible. Implemented by
  [`specs/26-Causality.md`](specs/26-Causality.md).

Section numbering is unchanged from v1.0, so every existing citation — in
`CLAUDE.md`, in `Docs/specs/`, and in `GDD_Coverage.md` — still resolves.

---

﻿

UNKNOWN GAMECONSOLIDATED GAME DESIGN DOCUMENTVersion 1.0 - High-Level Skeleton LockedAugust 19, 2026
CORE EXPERIENCEA persistent geopolitical strategy simulation presented through an old classified government command terminal. The player directs a nation through five interconnected pillars while an independently evolving world reacts to every decision.

1. Executive Summary
Unknown Game is a landscape mobile geopolitical strategy game built in Unity with UI Toolkit/UI Builder. It combines a persistent national simulation, optional deep control, autonomous government officials, imperfect information, emergent international history, and a retro ASCII/terminal presentation. The player is not expected to micromanage every system: each of five pillar leaders can operate autonomously, be directed toward an outcome, or be overridden through direct player control.
There is no mandatory campaign ending. A save can continue for decades. At the end of each in-game year, performance is evaluated and permanent Skill Points are awarded within that save. A Settings reset erases the entire game state and progression for a completely fresh start.
2. Product Definition
Amendment, August 21, 2026: the World row below originally read 'Hand-authored fictional geopolitical world with familiar strategic archetypes.' That direction was superseded by an explicit design decision to author real countries instead. The change is recorded rather than removed; see Section 31.1 for the amended direction and the rules it carries.
Area
Locked Direction
Genre
Persistent geopolitical strategy / government command simulation
Primary platform
iOS and Android
Orientation
Landscape
Engine
Unity
UI technology
UI Toolkit / UI Builder
Target screens
Phones, large phones, foldables, tablets; PC may follow later
Presentation
Retro classified terminal: ASCII maps/art/charts + responsive conventional UI controls
World
Hand-authored roster of real countries treated as familiar strategic archetypes, then evolved systemically
Primary mode
Persistent single-player national simulation
Scenario Mode
Deferred to a future update
Connectivity
Primarily offline; online services may support cloud saves/achievements later
Monetization
Premium or free demo + one-time full-game unlock; no pay-to-win Skill Points
3. Design Pillars / Non-Negotiable Rules
The player directs a government; they do not inherently operate every subsystem.
Every strategic pillar has an appointed leader capable of autonomous operation.
The player may directly direct a pillar when political/constitutional authority permits; otherwise they influence/recommend.
Direct intervention consumes scarce Command Points; delegation preserves player bandwidth.
Prefer consequences over artificial restrictions. Bad decisions are allowed when institutionally/capability-wise possible.
Strategic pivots are allowed, but previous commitments, preparation, credibility, losses and momentum still matter.
The AI plays the same strategic simulation under imperfect information and may make understandable mistakes.
Every pillar must be viable as a primary playstyle and capable of contributing to victory.
Progression primarily expands strategic knowledge, control, options and specialization rather than granting magical national power.
The world does not wait for the player. Countries pursue their own interests and create history independently.
Depth is optional. Players can deeply command the pillar they enjoy and delegate the others.
4. Terminal Fantasy & Visual Direction
The earlier 'kidnapped strategist' concept is tonal inspiration only, not a literal storyline. There is no captor plot, escape objective, previous-operator mystery, or narrative campaign. The game should feel like the player is isolated at an old government strategic terminal with classified access and immense responsibility.
ASCII world map and strategic geographic displays.
ASCII institutional art for government, military, intelligence, economic and diplomatic panels.
ASCII National Market Index / stock-style charts comparing countries on the roster.
Monospaced terminal typography with touch-friendly UI Toolkit controls.
Cold governmental language, command prompts, classified briefings and system alerts.
Optional CRT-inspired scan lines, cursor blink, restrained flicker/glitches and typing effects.
Responsive layouts: larger screens show more information simultaneously, never gameplay advantages.
5. First Launch & New Game
First launch begins with an in-universe strategic/personality assessment of approximately 8-12 scenarios.
Answers influence government, strategic profile, pillar strengths/weaknesses, national character, doctrine, traits and starting parameters.
Hidden scoring is not exposed as '+2 Intelligence' style feedback.
Controlled randomness prevents the assessment from becoming a solved recipe.
The generated nation receives one final controlled intervention before acceptance (exact intervention menu to be detailed later).
The player then enters the command terminal and completes an interactive tutorial across the five pillars.
After onboarding, the persistent simulation begins.
5.1 Full Reset
Reset Game is available from Settings and means a true fresh start. It erases the nation, world history, Cabinet, relationships, Strategist level, XP, Skill Points and all save-specific progression. The player returns to the first-launch flow.
6. Time, Turns & Mobile Session Loop
System
Rule
Normal turn
1 in-game month
Crisis Turn
Interrupt for urgent decisions; does not necessarily advance the month
End date
None; the simulation can continue indefinitely
Real-world absence
Does not continuously advance and punish the player
Return experience
Daily/return briefing summarizes priority items and world developments
MONTH START  > Strategic briefing / alerts  > Review public information and intelligence  > Officials form autonomous intentions  > Player delegates, directs, or directly controls selected pillars  > Direct intervention consumes Command Points  > Countries and officials act  > Markets / diplomacy / politics / operations resolve  > Crisis Turns interrupt when necessary  > Consequences update the world  > END MONTH
7. Command, Delegation & Political Authority
7.1 Command Points
Command Points (CP) represent the operator's limited capacity for personal strategic intervention. Baseline is approximately 5 CP per month, subject to government, leaders, stability, doctrines and events. A limited Strategic Reserve may preserve some unused capacity; exact values are balance work.
Routine information review is free.
Direct pillar actions, abrupt escalation, major executive intervention and strategic pivots can consume CP.
Autonomous official actions consume no player CP.
Crisis responses use a dedicated response structure so the player is not helpless because normal CP was already spent.
Abrupt escalation can impose an Escalation Premium; preparation can reduce it.
7.2 Control Modes
Mode
Behavior
Primary Cost
Autonomous
Official chooses priorities/actions based on competence, doctrine, personality, information and conditions.
0 CP
Directed
Player states the desired outcome/priority; official chooses execution.
Influence
Direct Control
Player chooses individual strategic actions/operations when authority permits.
Command Points
7.3 Influence, Political Capital & Trust
Influence steers officials without full micromanagement.
Political Capital pays for difficult appointments, dismissals, reforms and institutional changes.
Trust is a relationship state, not spendable currency.
Repeated overrides can damage official trust; successful judgment can restore or improve it.
Government structure determines which actions are direct authority and which are recommendations requiring approval/influence.
8. Cabinet & Political Characters
Each pillar has a simulated leader who is both an advisor and an optional autonomous manager.
Pillar
Representative Office
Military
Supreme Commander / Defense leader
Economy
Finance / Economic Minister
Intelligence
Intelligence Director
Diplomacy
Foreign Minister
Government
Domestic political/government leader appropriate to the system
Dynamic candidate pools include competence, specialization, ideology, personality, loyalty, reputation, risk tolerance, public approval and political baggage.
The technically strongest candidate may be strategically or politically incompatible with the player.
Officials gain experience, traits, relationships and reputations over time.
Officials age, retire, resign, lose office and occasionally die; new generations replace them.
Officials can disagree, leak, criticize, resign, obstruct, and in extreme constitutional breakdown participate in refusal/conspiracy/coup behavior.
A prominent military or Cabinet figure may later enter politics or become a national leader.
Succession follows the political system: elections/constitutional succession, coalition changes, authoritarian succession struggles, monarchy rules, etc.
9. The Five Strategic Pillars
The permanent pillars are Military, Economy, Intelligence, Diplomacy and Government. Technology and society operate through/cross these systems rather than becoming separate pillars.
Cross-Pillar Combination
Example
Military + Economy
Blockade, industrial attrition, resource denial
Military + Diplomacy
Deterrence, access agreements, coalition warfare
Military + Intelligence
Deception, targeting, sabotage
Economy + Diplomacy
Coordinated sanctions / trade isolation
Intelligence + Government
Political destabilization / regime-fracture pathway
Intelligence + Diplomacy
Classified leverage / backchannel pressure
Cross-pillar investment also unlocks hybrid Strategist skills, rewarding both specialization and deliberate diversification.
10. National Power Model & Strategic Resources
Layer
Purpose
Headline Pillars
Fast read of Military, Economy, Intelligence, Diplomacy and Government
Capabilities
What the country can actually do
Strategic Resources
Finite/replenishing national capacity
National Traits
Memorable structural identity, strengths and vulnerabilities
10.1 Core National Resources
Resource
Meaning
Treasury
Government financial capacity
Manpower
Available/recruitable human capacity
Energy
Fuel/electricity security
Industrial Capacity
Ability to produce, build and maintain
Strategic Materials
Aggregated critical materials/minerals
Food Security
Reliable ability to feed the population
Specific dependencies such as oil, uranium, semiconductors, rare minerals or grain appear when strategically relevant beneath the compact headline resource model.
11. Technology
Technology is capability-based and distributed across the five pillars, not a sixth pillar and not a single Civilization-style tree. Countries fund research programs and strategic capabilities. Development depends on investment, economy, institutions, partnerships, espionage and use.
Technology unlocks capability but does not automatically provide force structure, trained personnel, infrastructure or money.
Knowledge can spread through trade, alliances, joint exercises, espionage, foreign investment and captured knowledge.
Research programs can exist in Military, Economy, Intelligence, Government and diplomatic/institutional domains.
12. Population, Politics & Social Pressure
Track major social pressures such as Government Approval, Living Standards, War Support, National Unity, Social Unrest, War Exhaustion and Economic Confidence.
Important subgroups may emerge when politically relevant, but the game does not continuously micromanage dozens of demographic factions.
Public state affects elections, protests, recruitment, productivity, stability and officials.
Public opinion has historical memory; long wars, depressions, victories and betrayals leave lasting political effects.
Amendment, August 23, 2026: this section listed Living Standards and Social Unrest as tracked pressures and neither existed; approval was computed directly from the current quarter's growth, inflation and unemployment figures, so the economy reached the politics instantly and history had no weight at all. Historical memory existed only as a diplomatic quantity between states, never as something a country's own public carried. The three quantities now sit on deliberately different timescales, and the separation is the point:
Living standards accumulate over years. They are how a decade of prosperity becomes political capital and a decade of decline becomes a government nobody can save. Approval follows them rather than the current figures.
Social unrest moves over months and is not the same thing as low approval. Approval is an opinion; unrest is organisation. A state can be widely disliked and perfectly calm, or quietly approved of and coming apart in three cities. Only unrest feeds the conspiracy that ends governments.
Public grievance moves over decades and never quite clears. A country that has been put through something does not respond to the next thing the way a fresh one would.
A restrictive civic posture suppresses the expression of unrest without touching its cause, so grievance keeps accruing underneath. Order bought that way is rented, not owned.
13. Government Pillar
Government type changes how power works rather than only providing modifiers.
Democratic, parliamentary, authoritarian, monarchical and other systems use different institutions and succession logic.
Leaders and parties/factions have priorities and can materially redirect national strategy.
Major policy settings influence autonomous officials.
Elections and leadership changes alter priorities without instantly changing inherited national capabilities.
Government specialists manage Cabinet, coalitions, institutional reform, public messaging, emergency powers and political bargaining.
Amendment, August 22, 2026: 'political bargaining' was listed in this section from the beginning and was the one item of the six with no instrument behind it. The pillar graded barely above doing nothing, and the cause was structural rather than balance: it had almost nothing to do month to month, and Political Capital had no purchase large enough to be worth saving for. The rules the expansion carries:
The pillar has a standing choice, not only one-off interventions. Civic posture - how open or restrictive the state is toward its own society - is held every month and paid for every month, trading order against legitimacy and deciding how quickly conspiracies can organise at all.
Support can be bought as well as earned, and bought support decays. A government maintains its standing with its legislature or its elite rather than purchasing it once. There are two roads: spending the operator's political standing, or spending the treasury, and the second hollows out the institutions over time.
Government type changes what these cost. A system that faces the voters pays far more to govern restrictively than one that does not, and gains more from governing openly.
Preparing for a succession is an available decision. Administrations come and go while the operator persists, and a transition should be something a competent operator can see coming and prepare for, not only something that happens to them.
Constitutional authority can be permanently amended at a price. This is the one large purchase in the political economy, and it is an operator interface rather than national power: it changes what this advisor may order without asking, never what the state is capable of.
Stability and national unity must have a level the state's own institutions and standing can hold them at. They were previously able only to fall, which meant a country that had a bad decade could not be governed back to health - a direct contradiction of a pillar whose subject is governing.
The player is the persistent strategic operator/advisor, not necessarily the elected leader; administrations come and go while the save continues.
14. Information, Intelligence & Deception
Layer
Behavior
Public
Normally observable/accurate facts such as leaders, government, declared policy, election timing and public economic conditions
Estimated
Ranges/grades with confidence; accuracy depends on collection, analysis, counterintelligence and deception
Classified
Hidden intentions, covert activity, vulnerabilities, secret agreements, war plans, coup preparation
Intelligence can be wrong through collection failure, analytical error, compromised sources or deliberate deception.
Networks are country-specific and can prioritize military, economic, political and diplomatic collection.
Collection capabilities remain strategically abstract; high-value assets may become named characters.
Covert operations include sabotage, political influence, support to opposition, theft of plans and deception.
Counterintelligence and defensive deception are first-class systems.
AI receives estimates and uncertainty just like the player; it cannot read hidden player state.
15. Diplomacy, Relationships, Alliances & Joint Exercises
15.1 Relationship Model
Dimension
Meaning
Relations
Broad diplomatic temperature / affinity
Trust
Belief that commitments will be honored
Dependence
Asymmetric need for the other country
Threat Perception
How dangerous the other country appears
Strategic Alignment
How closely current national interests point in the same direction
Historical Memory
Past wars, betrayals, aid, treaties, exercises and other major events
Relationship statuses such as Hostile, Rival, Neutral, Cooperative, Friendly, Strategic Partner and Ally emerge from the full relationship state rather than a single threshold.
15.2 Treaties & Coalitions
Alliances contain explicit commitments: defense, intelligence sharing, transit, joint planning, offensive support, guarantees, etc.
Individual commitments may be permanent or time-limited, and may be conditioned on a live confrontation with a named third state. Conditions and expiry compose; a promise applies only while every term attached to it holds. Older and unconditional agreements remain permanent. Expired commitments remain in the historical treaty but stop granting current political or mechanical benefits; they can be renewed without extending unrelated clauses.
Commitments may be broken, but trustworthiness and future diplomacy suffer.
Countries join coalitions according to their own interests, rivalries, threat perceptions, governments and expected gains.
The player can exploit an enemy's rocky relationships to recruit military, economic, intelligence or diplomatic support.
Coalition participation can change mid-conflict because of elections, casualties, bargaining, domestic opposition or changing interests.
15.3 Friendly War Games / Joint Exercises
Friendly countries can conduct customizable military exercises that provide experience, interoperability and relationship benefits.
Losing an exercise still provides learning and can expose weaknesses before a real conflict.
Participation depth trades greater training/trust for greater intelligence exposure.
Former partners may retain useful knowledge of doctrine and procedures if the relationship later becomes hostile.
Countries can possess distinct Respect Conditions that accelerate special relationships without becoming the only friendship mechanic.
16. Geography & World Map
The game uses an interactive ASCII strategic world map. Geography matters for access, logistics, trade, military projection, diplomacy and economic dependence, but the game is not a province-painting or individual-unit board game.
Selectable countries/borders, oceans/seas, routes, chokepoints, staging access, infrastructure and strategic resources.
Dynamic confrontation theaters derive from geography.
Amendment, August 23, 2026: this line had no implementation. Geography priced distance - a far campaign arrived lighter - but a confrontation belonged to no region, and a state could only ever have one confrontation at a time, so two wars in different parts of the world were literally impossible to have and the map was decoration. The requirement and the rules it carries:
A confrontation belongs to a named theatre derived from where the defender is, captured when it opens, so a player can hold two simultaneous wars apart in their head.
A state may be committed on more than one front. That is priced, not forbidden: every commitment elsewhere drags operation power in every other theatre, because two wars on opposite sides of the world are not two wars - the second is fought with whatever the first left over.
The drag is floored. Another war makes a campaign harder, never impossible, for the same reason escalation states price an order rather than refusing it.
There is still a ceiling. Beyond what the force can sustain a state simply cannot take on another front, which is a fact about armies rather than a rule about turns, and the refusal says which.
A power visibly committed in one theatre is weak in another, and the world reads that as an opening. This is what makes a distant war a neighbour's opportunity, and it is the point at which the map stops being a picture.
Borders may change through war, secession, annexation, unification and state collapse.
Physical geography remains mostly persistent; infrastructure and routes can change strategic importance.
Historical map snapshots should be retained so players can compare the current world to earlier decades.
17. World Evolution & Universal Simulation
17.1 Persistent National Identity + Emergent History
Countries have durable Strategic DNA: geography, foundational traits, cultural/institutional tendencies and historical identity.
Leaders/governments change moderately; capabilities, economies, militaries, relationships and global rankings can change constantly.
National character can evolve slowly over decades due to major historical experiences.
The simulation does not protect the starting world order. Major powers can decline and minor powers can rise.
Rare civil wars, fragmentation, unification or regime transformations may permanently alter the map.
Amendment, August 23, 2026: two halves of this section were unimplemented, and both mattered.
Durable Strategic DNA: personality was rolled uniformly at world creation, so a country was a different country in every save, and fifteen of sixteen states carried no traits at all - they were the same numbers with a different flag. Temperament and traits are now authored per nation and stable across saves, with only a small per-save jitter. This is not flavour: an AI designed to answer a player who repeats an opening cannot do that if the player has no stable read on who they are answering.
Fragmentation and unification: a new country had never been created anywhere except at world creation, so conquest and accession only moved title between the same sixteen states and the map could only ever shrink. A state can now come apart, and what comes apart can be put back together. The rules this carries: it is earned rather than rolled, out of a civil conflict already being lost with unity already gone; the successor is a real state with ground, a government, a cabinet, a mind and a place in every relationship, because a breakaway nobody can negotiate with is scenery; it is rare, because a world that shatters every decade has no stakes; and if the operator's own country splits, they keep their post in what remains - severe, never terminal, exactly as with a coup.
Countries remember history rather than reducing all diplomacy to a current relationship number.
17.2 Universal Simulation + Adaptive Detail
Every country continuously maintains government, economy, relationships, priorities, conflicts and long-term development. High-detail operational resolution is activated only when strategically relevant; distant events can resolve at a lower tactical fidelity without becoming fake/stateless.
The player receives a filtered Global Wire rather than manually inspecting every country every month.
18. Confrontations, Escalation & Victory
18.1 Escalation
PEACE -> TENSION -> CRISIS -> LIMITED CONFLICT -> TOTAL WAR
Visible states describe the situation; they do not hard-gate actions.
Player or AI can jump escalation levels when capability permits, paying greater command, political, diplomatic and economic costs.
Hidden escalation pressure underlies the visible state.
De-escalation can skip levels, including unilateral ceasefires.
Non-military coercion can become severe enough to justify military retaliation in the target's strategic logic.
18.2 Objectives & Primary Strategy
A confrontation begins with an Objective: the actual concession/outcome sought.
The player chooses a Primary Strategy: Military, Economic, Intelligence/Political, Diplomatic, or later hybrid approaches.
Supporting pillars remain usable without changing the Primary Strategy.
Strategic Pivot is always possible but costs time/CP, loses momentum and may damage credibility or morale.
Intelligence and preparation are strategy-specific; knowledge does not automatically transfer between domains.
The opponent continually evaluates whether resistance is preferable to accepting terms.
Failure is valid and produces consequences rather than automatic rescue.
18.3 How Confrontations Begin
Confrontations may be player-initiated, AI-initiated, or emerge from crises such as border incidents, alliance obligations, trade disputes, coups or other systemic events.
19. Military Pillar
Deep optional national-campaign simulation; non-specialists may delegate to the military leader.
Strategic locations include capitals, ports, airbases, industrial centers, energy regions, passes and other meaningful targets rather than every city.
Force structure uses branches/sub-branches and enablers rather than individual vehicle counts.
Amendment, August 23, 2026: this line is superseded in part, by explicit design decision. It was right about what it was protecting - a game that asks the player to manage individual airframes is a logistics spreadsheet rather than a strategy simulation - and operations still resolve against branch strength, so that protection holds. What it got wrong is the information layer. A branch strength of 68 has no units, no comparison class and no relationship to the decision in front of the operator; 912 fighters against their 340 is a fact that can be reasoned about, and choosing between bombers and tankers is a real strategic decision that a single aggregate number cannot express. The rules the amendment carries:
Counted classes are the readable and buyable layer, and branch strength is a mirror computed from them. Never the reverse: if strength were authoritative and counts cosmetic, buying two hundred fighters could leave a force exactly as strong as before.
Losses are losses of actual things. A defeat computed in the aggregate destroys real aircraft and hulls.
A foreign inventory is never shown as truth. It is a band whose width comes from collection quality and whose centre can be bent by deception, exactly as every other foreign figure is. Better intelligence narrows the band and never removes it.
Equipment takes time that varies by what it is. A carrier ordered during a crisis arrives for the war after it; a recruit intake does not. This is what makes force structure a strategic decision rather than a tactical one.
A position stripped of troops and works should fall rather than grind. The reward for a successful siege or bombardment is an open door, not a marginally cheaper assault - though never a guaranteed one.
An after-action report must explain the outcome, not merely state it. It should say what the odds were assessed at before the order, rank what actually decided it, and say what would change the result. A report that says only that an operation failed leaves the operator with one strategy - repeat it until the dice land - which is not a strategy at all. This applies equally to attacks against the player: a failed enemy assault should teach what held.
Readiness, logistics, posture, supply, attrition, access and projection matter.
Operations are objective-based; sieges can be assaulted, bypassed, negotiated, maintained or abandoned.
A player can win battles but lose the confrontation through manpower, treasury, political, diplomatic or public costs.
Delegated directives specify objective, casualty priority, speed, risk tolerance, territorial intent and escalation limits.
Standing orders may separately preauthorize the next step of a player-authored campaign plan. They execute through the ordinary command path after monthly command capacity refresh, spend the ordinary cost, respect current authority and live availability, and attempt no more than one operation per month. A plan without that explicit authorization remains intent only.
Amendment, August 22, 2026: 'operations are objective-based' is expanded to a named list of twenty-three verbs across four service domains (ground, naval, air, joint), chosen with the designer rather than derived. The original line was satisfied by three interchangeable ground verbs, which made force composition irrelevant and every campaign the same campaign. The rules the expansion carries:
Each verb must resolve against something other than the garrison where that is what it would actually fight - air defences, a fleet, an air force, a security service, the works themselves, or popular resistance. If everything resolves against the garrison then every verb is an assault with a different name.
Only two verbs take and hold ground: deliberate attack and amphibious assault. Everything else breaks, degrades or denies, so a longer list is not a longer list of ways to conquer.
The list includes defensive verbs conducted on ground we already hold - prepared defence, counter-insurgency, convoy escort, missile defence. Before these the pillar had no defensive vocabulary at all. Defensive work does not escalate a confrontation and is not surcharged as an act of war.
Verbs are priced apart so that sequencing is a decision. Softening a position before assaulting it must buy real odds and must cost real command capacity, so that it is a judgement call rather than the answer.
An operation that cannot be conducted is shown and disabled with a stated reason, never hidden. A landlocked country cannot be blockaded, mined or landed on; a country with no air force cannot be fought for the sky. Sea access is authored per country at three levels (landlocked, coastal, maritime) and naval strength derives from it, rather than every country receiving a fleet proportional to its defence budget.
Standoff fires carry civilian cost out of proportion to the force committed. Strategic bombing and strikes on a government raise the target's war support rather than lowering it: neither is a shortcut to a settlement.
20. Economy Pillar
Layered macroeconomy: growth, inflation, employment, debt, treasury, confidence and related conditions.
Sector layer may include energy, food/agriculture, industry, technology, finance, consumer/services and defense industry.
Strategic dependencies and supply architecture are central.
Trade, tariffs, embargoes, sanctions, investment, financial access, resource contracts and emergency deals are strategic tools.
Economic pressure can backfire through domestic inflation, supply disruption, banking exposure or global contagion.
Economic victory coerces the opposing government; it is not 'reduce GDP to zero.'
20.1 Market Presentation
Simulated National Market Indexes are shown through ASCII stock-style charts and react to wars, elections, coups, sanctions, shortages, agreements and confidence shocks.
21. Strategic Endgames
Every pillar can reach endgame-level strategic effects capable of breaking an opponent, but these are not generic ultimate buttons. They require capability, preparation and favorable conditions, and they can create systemic consequences.
Pillar
Strategic Endgame
Core Effect
Military
Strategic Destruction
WMD/strategic-force devastation and deterrence
Economy
Systemic Financial Collapse
Banking, currency, markets, trade and confidence failure
Intelligence
State Destabilization
Institutional penetration, political fracture and information breakdown
Diplomacy
Strategic Isolation
Removal of allies, access, investment, trade support and legitimacy
Government
Total National Mobilization
Extraordinary domestic mobilization that amplifies national effort at serious political risk
Strategic Severity spans Routine -> Pressure -> Coercive -> Severe -> Existential. Existential non-military attacks can trigger military responses. Economic collapse can spread through contagion; intelligence destabilization does not guarantee a friendly successor government.
22. Coups, Rebellion, Regime Change & Collapse
Coups/rebellions emerge from accumulated conditions rather than random cards or one-click actions.
Relevant factors include stability, military loyalty, executive authority, elite cohesion, economy, public support and organized conspirators.
Intelligence can detect, accelerate or exploit vulnerabilities, but cannot make a stable state collapse by repeatedly clicking a button.
A successful foreign-supported regime change creates a new sovereign government with its own interests, not an automatic puppet.
Catastrophic events often create new gameplay states rather than immediate game-over: lost territory, election defeat, coup, depression, civil war and successor governments can all continue.
True permanent loss is rare and reserved for cases such as total annexation/state dissolution or other situations where meaningful state continuity ends.
23. Events & Crisis System
Events use a hybrid systemic + authored architecture. Simulation conditions determine eligibility, while authored situations provide high-quality choices and presentation.
Examples: protests from inflation/election pressure, border incidents from military buildup, shortages from supplier wars, scandals, disasters and political crises.
Crisis Turns interrupt the normal monthly loop when immediate decisions are required.
Events should arise from the world state rather than feel like disconnected random cards.
Amendment, August 22, 2026: this section said events must arise from the world, and they did - but they could not act back on it. A crisis response resolved to four numbers on the player's own country and nothing else: it could not change a relationship, move a market, touch a foreign state, or open a confrontation. The situations arose systemically and resolved cosmetically, which is half of what this section asks for. The requirement and the rules it carries:
A crisis response must be able to reach the world that produced it - relationships, trust, threat perception, markets, trade, sanctions, intelligence networks, a foreign state's stability, and the opening of a confrontation.
The state a situation names when it fires is the state it acts on when it resolves, even if the world has moved on while the operator was deliberating.
Doing nothing is a decision with consequences of its own. If the operator will not decide, the situation decides for them - drifting must not be the cheapest way to dodge what a choice would have cost.
The operator is told what happened abroad in the same report that tells them what happened at home. A consequence discovered three months later reads as the simulation cheating.
A response that resolves into a contradiction - a state that no longer exists, a war already under way - does nothing quietly rather than failing.
24. AI Architecture & Difficulty
24.1 Goal-Driven Government AI
Each government evaluates national interests, threats, opportunities, capabilities, relationships, domestic politics, leader personality, ideology/doctrine and known/estimated intelligence.
AI generates strategic objectives and pursues them through the same pillars available to the player.
AI uses incomplete information and can be deceived.
Leaders can make mistakes because of aggression, caution, corruption, ideology, pressure or faulty intelligence.
The AI should not converge into a single perfect optimizer.
Amendment, August 22, 2026: 'generates strategic objectives' is expanded to require that each government hold a theory of how it wins, not only a list of month-to-month reactions. Each state commits to a strategic path - economic primacy, military dominance, regional hegemony, a technological edge, institutional weight, or survival - chosen from its own endowments rather than assigned, and that path shifts what objectives it will reach for. A government pursuing economic primacy declines wars it could win. Paths are reviewed rarely and change on sustained failure or a lost war, because a government that re-plans every month reads as noise rather than intention, and nothing the player learns about it stays true. A government judges its own progress against estimates of its rivals, so a deceived state can believe it is winning.
24.2 Learning the Player
Within a save, countries build behavioral assessments from observed player actions. Repeated patterns can influence their planning, but this is systemic pattern recognition, not magical machine learning. The player can deliberately become predictable and later change doctrine as strategic deception.
Amendment, August 22, 2026: 'can influence their planning' is strengthened to a requirement, because it was previously satisfied by an assessment that was computed every month and read by almost nothing - a player assessed as maximally dangerous changed nothing about how the world behaved, which is telemetry rather than a prediction. The requirement and the rules it carries:
Governments form an expectation of what each other state will do next, and act on it before it happens: preparing defences against an expected attack, insulating an economy against expected coercion, hardening security against expected subversion, building a bloc against one being built.
This is the answer to a player learning the AI in one save, resetting, and running a known-winning opening. Randomness is not the answer to that; a world that behaves differently for no reason is worse than one that behaves the same for good reasons. AI behaviour must be a function of what the player does, so that repeating an opening summons the same counter to it and the way past it is to change what you do.
Expectations are formed only from what the observer could actually have seen - public acts, and covert acts that were caught - and their quality scales with collection. A government with poor intelligence predicts badly. This is what keeps intelligence worth buying and deception worth running.
Preparation is never free. It costs treasury and political capital like everything else a government does, under the same symmetry-of-consequence rule the rest of the AI is held to.
The assessment must decay when the behaviour stops. A reputation is a read on current conduct, not a permanent sentence - a permanent one would make reloading the correct response, which this game refuses.
24.3 Difficulty
Difficulty primarily changes AI reasoning quality: planning horizon, cross-pillar coordination, opportunity recognition, deception, diplomacy and crisis competence. Avoid large hidden stat cheats; any extreme-difficulty modifiers should be small and disclosed.
25. Experience, Annual Evaluation & Skill Progression
25.1 XP
XP is earned continuously from meaningful decisions and outcomes across all five pillars, providing frequent feedback and Strategist-level progression.
25.2 Annual Evaluation
At the end of each in-game year, performance is evaluated relative to circumstances rather than by a conquest checklist. Evaluation considers national trajectory, objectives, crisis management, efficiency/cost, stability, strategic position, pillar performance and difficulty/context.
Annual grades award permanent Skill Points within the current save. Exact grade-to-point values are balance work.
25.3 Strategist Skill Trees
Skill Points are invested into branching Military, Economy, Intelligence, Diplomacy and Government trees.
Upgrades primarily unlock information, direct-control options, doctrines, planning tools, efficiencies and strategic verbs rather than raw national stats.
Country capability is still developed through investment, officials, technology, industry, policy, conflict and world events.
Cross-pillar prerequisites unlock hybrid strategies.
Officials can compensate for weak player specialization when delegated.
Costs rise at higher levels so specialization remains meaningful over long saves.
All Strategist progression is erased by a full Settings reset.
26. Peace Negotiation & Settlement
Peace is a negotiated settlement system based on objectives, leverage and exhaustion rather than a single Surrender button.
Possible terms include territory, withdrawal, reparations, sanctions removal, demilitarization, resource access, recognition, prisoner exchange, guarantees, political concessions and treaty changes.
AI may reject unreasonable demands even while losing.
Overreaching can prolong a war that could otherwise have ended.
27. Civilian Impact & International Consequences
Civilian harm is modeled through risk, humanitarian impact, international law/reputation, domestic politics and strategic consequences rather than as an atrocity-reward system.
Operations can trade speed/effectiveness against civilian risk.
Civilian harm affects international support, enemy resistance, domestic approval, coalition cohesion, sanctions, future relationships and historical memory.
28. Information Architecture & Notifications
28.1 Three-Layer Information Hierarchy
Layer
Purpose
Briefing
Only items requiring attention or high-value awareness
Pillar Dashboards
Military/Economy/Intelligence/Diplomacy/Government summaries
Deep Terminal
Detailed reports, charts, dossiers, treaties, force composition, dependencies and archives
Official competence influences what is surfaced or missed, making delegation part of the information experience.
28.2 Notification Priority
Class
Meaning
FLASH
Immediate decision required
PRIORITY
Important this turn
ADVISORY
Strategically relevant
WIRE
General world news
ARCHIVE
Historical/informational
28.3 Causal Legibility
Amendment, September 11, 2026. Non-negotiable, alongside 28.1's information hierarchy.

Important player-facing outcomes must be causally legible. When the simulation changes something that matters, the game is expected to know why, and where the player's government is entitled to that knowledge, the game must be able to state it plainly: what changed, by how much, which factors contributed, which of them were the player's own decisions, and which were direct rather than downstream.

The player does not get the formulas. Brink is a deep interconnected simulation and its internals are not the subject; the requirement is that a player can understand why an important outcome happened and learn from it. A number that moves for reasons the operator cannot reconstruct is indistinguishable from a number that moves arbitrarily, and this project has already shipped that failure twice in other clothes - a refusal the operator could not see reads as a broken control, and an outcome the game could not explain reads as unfair dice. Both were correct simulation reported as bugs.

Three rules bound it.

Explanations are recorded where the change is applied, never reconstructed afterwards. A value re-derived after the fact can only guess at its own history, and a guess presented as an explanation teaches the wrong lesson.

Explanations obey the information rules. This is subordinate to Section 14's fog of war, to 28.1's rule that official competence governs what is surfaced or missed, and to the standing rule that reporting may bury or miss an item but may never distort one. An explanation layer that reported the true cause of everything would be a free intelligence service and would repeal the reason to buy collection. Where the government does not know, the game says that it does not know - an unattributed factor is itself worth reporting, because knowing that something is acting on you and not knowing what is the condition intelligence exists to fix.

The presentation matches the mathematics. Where contributions genuinely account for a change, figures are shown and they add up. Where they cannot, the game gives direction and rank and claims no total. Fabricated additive precision is the numeric form of the distortion this design already forbids.

29. Optional Strategic Directives
The government, Cabinet or circumstances may suggest optional strategic directives such as reducing energy dependence, resolving a border dispute or restoring readiness. They can improve XP/evaluation but are not mandatory quests and should never become generic daily-task chores.
30. Saves
Persistent autosave with frequent safe checkpoints.
Multiple strategic save slots are planned.
Cloud save support is desirable for mobile.
Suspend/resume should be immediate.
No routine 'reload last turn because I disliked the outcome' flow; consequences matter.
Developer/testing modes may support rollback and forced-state controls.
31. World Content
31.1 Country Creation
Amendment, August 21, 2026: this section originally specified a hand-authored fictional world, and the section itself was titled 'Fictional World Content'. That direction was superseded by an explicit design decision to author real countries instead. The concern behind the original direction - that a geopolitical simulation must not make claims about real nations or take a position on live real-world disputes - has not changed; it is now addressed by the framing rules below rather than by inventing nations. The amendment is recorded here rather than hidden.
Use hand-authored core countries with systemic evolution, not fully procedural nations. The launch roster is composed of real countries, hand-authored as gameplay archetypes and then evolved systemically. Each country receives geography, history, government, national traits, strategic interests, military/economic architecture, relationships, cultural/political tendencies, starting leaders and unique vulnerabilities. The change is which nations are authored, not how thoroughly they are authored.
Framing rules for a real-country roster:
Starting values are balance-tuned archetype baselines, not claims about real-world capability. They exist to make each nation play distinctly.
Government types are structural descriptions of how power works, not judgments. Use institutional vocabulary; never evaluative labels.
Do not model live real-world territorial disputes. The single authored flashpoint is a deliberately generic 'Contested Sea Lane'. Naming a real disputed territory invites needless controversy and creates regional app-store problems.
Leader and official names are generated from authored per-country name pools. Never depict a real living official.
31.2 Launch World Size
Target approximately 16 core countries for the full launch design, balancing major powers, regional powers, smaller strategic states, islands, resource powers and buffer states. The exact final count can move during content production.
31.3 World Chronicle
Major events are automatically archived by country and globally: elections, wars, treaties, recessions, alliances, coups, territory changes, major technology breakthroughs and other historical milestones. Long saves become their own alternate-history chronicle.
32. Scenario Mode Roadmap
Scenario Mode is explicitly deferred to a future update. The intended concept is self-contained country-vs-country or challenge sandboxes using the same simulation rules, but it is not part of the launch MVP or current implementation scope.
33. Business Model & Connectivity
Preferred monetization: premium purchase or free demo followed by a one-time full-game unlock.
No pay-to-win Skill Points, energy timers, paid Political Capital or daily-streak pressure.
Future major content/world expansions may be sold as expansions if the game succeeds.
Core simulation is primarily offline single-player.
Optional online services may later provide cloud saves, achievements and future community features.
34. MVP / Claude Build Strategy
The MVP is a four-country vertical slice built incrementally in Unity using Claude through a strict Prompt -> Implement -> Test -> Fix -> Lock cycle. No major system begins until the prior system passes explicit acceptance tests.
Phase
Build Target
Acceptance Focus
0
Foundation
Landscape project, UI Toolkit, data models, game state, turn manager, save/load, debug/logging
1
Command Terminal UI
Responsive phone/foldable/tablet shell and ASCII presentation
2
Turn + CP Loop
Monthly turns, CP, reserve, End Month, basic crisis response
3
Cabinet
Five officials and Autonomous / Directed / Direct Control
4
Military Vertical
Basic branches, readiness/logistics, strategic locations, operations, one confrontation
5
Economy
Macro basics, trade, sanctions, resources, ASCII market index
6
Intelligence
Estimates, confidence, misinformation, counterintelligence
7
Diplomacy
Relationship dimensions, treaties, coalition request
8
Government
Approval, stability, political capital, basic elections/succession
9
World AI
Three AI countries act independently; run long unattended simulations
10
Progression
XP, annual evaluation, Skill Points, small test skill trees
11
Assessment/New Game
Personality assessment generates starting nation
12
Vertical Slice Validation
Play one nation for ~10 simulated years; do not expand until it is genuinely fun
34.1 Claude Prompt Contract
Goal: one concrete deliverable.
Existing Architecture: tell Claude exactly what already exists.
Requirements: explicit behavioral rules.
Boundaries: state what Claude must not redesign/touch.
Acceptance Tests: exact steps proving completion.
Every significant system exposes debug/test controls so states can be forced without playing dozens of turns.
The launch-content target is larger than the MVP. The four-country slice proves the core simulation first; only then should the project expand toward the approximately 16-country authored world and deeper content.
35. Explicitly Superseded / Removed Concepts
No fixed 20-year campaign/run ending.
No roguelite run-reset loop as the primary mode.
No Strategic Archive meta-progression between runs.
No automatic retention of Strategist progression after Reset Game.
No literal kidnapping/captor storyline, previous-operator mystery or escape objective.
No launch Scenario Mode; it is a future-update concept.
No separate Technology pillar.
No requirement that a conflict progress normally through escalation states before Total War becomes possible.
No requirement that a player remain permanently locked to the initial victory method; pivots are possible but costly.
No hand-authored fictional nations; the launch roster uses real countries under the framing rules in Section 31.1.
36. Phase 2 Design Documents Required Before Full Production
Military System Specification: branches, enablers, operations, logistics, locations, combat resolution and delegation.
Economy System Specification: macro model, sectors, trade, markets, dependencies and economic warfare.
Intelligence System Specification: collection, confidence, deception, covert operations and counterintelligence.
Diplomacy System Specification: negotiation grammar, treaties, alliances, coalition logic and joint exercises.
Government System Specification: government types, authority, parties/factions, elections/succession, public state and political capital.
AI System Specification: goals, utility/scoring, planning horizon, memory, information model and adaptive-detail simulation.
Strategist Progression Specification: XP sources, annual scoring model, five branching skill trees and hybrid unlocks.
Country Bible: full authored launch roster, map, histories, governments, strategic DNA and starting relationships.
UI/UX Specification: screen hierarchy, responsive breakpoints, terminal visual rules, dashboards, deep reports and notifications.
Save/Data Schema Specification: persistent world state, historical archive, versioning/migration and cloud strategy.
Event/Crisis Specification: systemic triggers, authored templates, severity and Crisis Turn rules.
Balance/Telemetry Plan: values to tune after the vertical slice proves the loop.
37. Current Design Status
High-level skeleton: COMPLETE. Questions 1-60 have been resolved through the conversation, including later revisions and removals. This consolidated document is the new source of truth and supersedes prior GDD versions where they conflict.
Next recommended work: begin Phase 2 detailed system specifications before generating production implementation prompts. The implementation roadmap should then translate each specification into small Claude tasks with explicit acceptance tests.

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

Industrial investments are surfaced as **visible national projects**: a descriptive
sector/scale/start-date name, funded progress, remaining commitment and records of
completion, cancellation or funding failure. These reuse the existing industrial
programme, not a second construction simulation. Completed work reports actual
applied benefits; later conditions can still erode capacity and functioning.
This first #24 slice does not add map sites, bespoke megaprojects or research-project
presentation. See spec 02 §3a.

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

Internal blocs react differently to patronage and public inquiries: hardship
constituencies welcome favours, while liberty/integrity constituencies resist
them; removing real corruption reverses that trade-off. An empty inquiry earns
no bloc goodwill. Reactions use the existing persistent dispositions and backing
targets, with visible consequences before ordering and recovery toward neutral.
Political influence evolves separately: hardship, corruption and restrictive
policy gradually increase the weight of the corresponding constituencies at
others' expense. Shares approach bounded condition-driven targets; easing the
pressure restores the founding balance. Courting buys goodwill, not power, and
influence is not a prediction of electoral seats. Held civic policy separately
sets Liberty blocs' resting goodwill: Open 65, Standard 50, Restrictive 35,
approached at 2% of the gap each month. Other concerns retain neutral 50.
Emergency authority lowers Liberty's resting target by ten while in force,
through that same drift. Declaring it grants no immediate bloc movement;
expiry restores the ordinary target, not the goodwill lost while it was held.
Switching grants no immediate goodwill; bought support above the resting level
still fades. Persisted concerns, not current regime labels, determine responses
for player and foreign governments alike. Constitutional settlements and coups
rename authored blocs to fit elective or non-elective institutions without
replacing their concerns, goodwill or influence. Old-to-new records preserve
continuity; custom identities survive. Further policy reactions remain future
work (spec 05 §2g).

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

Research project readouts show full names, remaining funding at saved terms and
recent own-country authorization, funding-failure and acquisition records.
Acquisition source remains explicit: receiving knowledge is not the same as
developing it. No start date or elapsed work is invented from data the save does
not retain. This is #24 presentation work, not physical construction (spec 13).

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

A ten-year Mandate provides medium-term purpose; annual evaluations provide feedback; standing directives create shorter opportunities; and one measured player objective may be authorized as a bounded autonomous Cabinet programme. The persistent world provides the long-term reason to continue.

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

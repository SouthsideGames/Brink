

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

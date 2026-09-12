# 27 — Standing Strategy / National Policy / Player Objectives

## Status
Phase B implementation candidate. Runtime/tests remain authoritative until Unity verification.

## Purpose
Phase A made consequences legible. The stability pass made long histories viable. Phase B gives the operator a durable answer to **what should the government do when I am not personally spending attention on it?**

This is not a victory-path selector and does not lock actions. Brink remains consequence-driven.

## Standing doctrine
A posting may adopt one of six doctrines: Balanced, Deterrence, Prosperity, Influence, Resilience, Transformation.

The first adoption is free. Revising an established doctrine costs 2 Influence. Doctrine does not add national resources or directly change a statistic. `StrategyCabinetBridge` translates it into default instructions for Autonomous player officials before the Cabinet works each month. Directed officials keep the player's explicit instruction. Direct Control is untouched.

This is the hierarchy:

`Standing strategy -> default delegated intent -> explicit Cabinet directive -> Direct Control`

The layer farther right wins because it represents more operator attention.

Strategy authoring itself is **not annual-evaluation initiative**. Adopting/revising doctrine, selecting a national policy, and adding/removing a personal objective do not call `RecordInitiative`; otherwise those free/cheap verbs become a grade-point farm.

Deterrence uses the existing autonomous `PREPARE FOR WAR` procurement path rather than the Directed-only readiness-target hook. It therefore has a real cost and a real force-structure consequence, and naturally stops ordering once establishment is filled.

## Country-specific national policy
Each measured Standard-roster country has an authored national-policy option. A policy is available only to its country. It changes the default emphasis of both a favoured and a strained Autonomous desk through existing Cabinet directives; it does not create a parallel bonus/penalty system.

Policies are trade-offs in strategic posture, not claims about real countries. Their purpose is to make different national archetypes produce different unattended priorities.

## Player-authored objectives
The operator may maintain up to three self-authored objectives. The OPERATOR panel supplies a measure, target and optional country id for relationship goals. Supported conditions are current-state conditions that can be evaluated without borrowing the mandate's historical baseline.

Self-authored objectives:
- give no XP, Skill Points, or annual-evaluation initiative;
- impose no penalty when removed or missed;
- do not alter the ten-year mandate;
- are **standing conditions**, not sticky quest completions;
- show MET only while the condition is currently true;
- remember first attainment for the historical record without continuing to report it as currently met;
- create one notification/Chronicle line on first attainment only.

They exist so a player can decide that this posting is about, for example, maintaining stability, building industrial capacity, staying solvent, or changing relations with a particular state.

## Cabinet reporting
An Autonomous official carrying a doctrine/policy instruction must not report that work as their own strategic judgement. `StrategySystem.ClarifyCabinetReport` runs immediately after Cabinet resolution and relabels only strategy-steered player reports as `worked under standing strategy (...)`. It is reporting-only and cannot alter simulation state.

## Persistence
`StrategicPlan` is stored on the posting's `Mandate`. Old saves may have a null plan; `StrategySystem.Ensure` creates the neutral default lazily. A mandate reissue mutates the existing Mandate and therefore does not erase the operator's strategy.

No save-version bump is required for the additive fields.

## UI
The OPERATOR panel shows the standing strategy between the mandate and optional desk directives. It provides doctrine controls, the player's country policy, and an objective authoring form.

## Verification targets
Unity verification must establish:
1. old saves/null plans load into neutral strategy;
2. first doctrine is free and revision costs Influence;
3. strategy affects Autonomous Cabinet defaults but never overwrites Directed or Direct Control behavior;
4. Deterrence produces a measurable force-structure trade-off instead of a Directed-only dead benefit;
5. foreign-country policy cannot be adopted and every Standard policy expresses both sides of its trade-off;
6. authored goals are bounded to three, dynamic (MET can become unmet), and cannot generate XP/SP/initiative;
7. strategy survives mandate reissue and save/load;
8. Cabinet reporting distinguishes standing strategy from ministerial judgement;
9. deterministic runs remain deterministic;
10. Phase A causality and the accepted stability baseline do not regress;
11. OPERATOR controls remain usable at narrow/foldable/wide terminal sizes;
12. `StrategyTests` is included in the authoritative partitioned suite.
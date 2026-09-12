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

## Country-specific national policy
Each measured Standard-roster country has an authored national-policy option. A policy is available only to its country. It changes the default emphasis of the favoured Autonomous desk through the existing Cabinet directive machinery; it does not create a parallel bonus system.

Policies are trade-offs in strategic posture, not claims about real countries. Their purpose is to make playing different national archetypes produce different unattended priorities.

## Player-authored objectives
The operator may maintain up to three self-authored objectives. The OPERATOR panel supplies a measure, target and optional country id for relationship goals. Supported conditions are current-state conditions that can be evaluated without borrowing the mandate's historical baseline.

Self-authored objectives:
- give no XP or Skill Points;
- impose no penalty when removed or missed;
- do not alter the ten-year mandate;
- are marked achieved when their condition becomes true;
- are archived through the ordinary Chronicle notification path.

They exist so a player can decide that this posting is about, for example, reaching a stability threshold, building industrial capacity, maintaining solvency, or changing relations with a particular state.

## Persistence
`StrategicPlan` is stored on the posting's `Mandate`. Old saves may have a null plan; `StrategySystem.Ensure` creates the neutral default lazily. A mandate reissue mutates the existing Mandate and therefore does not erase the operator's strategy.

No save-version bump is required for the additive field.

## UI
The OPERATOR panel now shows the standing strategy between the mandate and optional desk directives. It provides doctrine controls, the player's country policy, and an objective authoring form.

## Verification targets
Unity verification must establish:
1. old saves/null plans load into neutral strategy;
2. first doctrine is free and revision costs Influence;
3. strategy affects Autonomous Cabinet defaults but never overwrites Directed or Direct Control behavior;
4. foreign-country policy cannot be adopted;
5. authored goals are bounded to three and cannot generate XP;
6. strategy survives mandate reissue and save/load;
7. deterministic runs remain deterministic;
8. Phase A causality and the accepted stability baseline do not regress;
9. OPERATOR controls remain usable at narrow/foldable/wide terminal sizes.
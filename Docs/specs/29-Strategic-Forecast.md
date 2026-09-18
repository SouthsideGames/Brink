# 29 — Long-Term Plan Frame and Strategic Forecast

## Status
As built: long-term plan frame, bounded autonomous programme, and strategic forecast.

## Purpose
Brink already lets the operator adopt a standing doctrine and write their own objectives. This slice turns those pieces into an explicit long-term plan and gives the operator a way to ask **what if we ran the government this way for a while?** without secretly simulating a parallel future.

## Plan frame
`StrategicPlan` now carries:

- `planTitle` — the operator's own name for the plan;
- `horizonMonths` — 12, 36, 60 or 120 months;
- `horizonSet` — when that planning frame was last set.

A horizon is organizational context, not a deadline. Reaching it gives no reward or penalty, changes no mandate verdict, and awards no initiative/XP.

`StrategySystem.SetPlanFrame` changes only those planning fields.

## Autonomous programme

One unfinished measured player objective may be authorized as the plan's active
programme. It does not create a second executor: `StrategyCabinetBridge` maps
the objective onto an existing Cabinet directive and only steers that desk while
its official remains Autonomous. Directed and Direct Control orders still win.

The first programme is free; replacing or reauthorizing one costs 1 Influence.
Cancellation is free and does not reset that revision price. When the existing
mandate evaluator says the objective is met, the authorization clears and the
operator is notified. Freeform intent and objectives without an honest Cabinet
mapping cannot be authorized.

## Forecast / what-if
`StrategicForecastSystem` is read-only. It:

- never advances the `TurnManager`;
- never consumes RNG;
- never writes the save;
- uses only the player's own national state and already-known exposure;
- uses the same doctrine → Cabinet mapping as runtime through `StrategyCabinetBridge.PreviewLabel`.

The forecast separates three kinds of statement:

1. **Arithmetic** — e.g. treasury extrapolated from the player's existing balance trend; GDP extrapolated from the current annualized growth rate.
2. **Exact delegated intent** — which existing Cabinet instruction each pillar would receive under the previewed doctrine.
3. **World-dependent exposure** — active fronts, sanctions involving the player, crises, and explicit `YES, BUT` trade-offs. These are not predicted as future outcomes.

The UI explicitly says this is a staff estimate, not a future save-state preview.

## Yes, but
The forecast is the first concrete implementation of Brink's `YES, BUT` design rule. A strategic direction is presented with the thing it buys and the room it gives up:

- Deterrence fills real force shortfalls but consumes treasury/economic room.
- Prosperity favours growth while conserving military expansion.
- Influence favours diplomacy/intelligence while restraining force growth.
- Resilience favours stability/hardening over diplomatic expansion.
- Transformation favours economic/intelligence modernization while tying up government attention.
- Balanced leaves more to ordinary ministerial judgement.

These are descriptions of existing Cabinet behavior, not hidden modifiers.

## UI
The OPERATOR panel now includes:

- editable plan name;
- 1/3/5/10-year horizon selector;
- doctrine what-if selector;
- independent forecast horizon selector;
- read-only forecast output.

Preview controls spend nothing and do not alter the adopted doctrine.

## Tests
`StrategyTests` now assert:

- an old/null plan receives the neutral 60-month default;
- setting the plan frame changes no Influence, initiative, treasury or pillar value;
- horizon input snaps to the supported 12/36/60/120-month set;
- identical forecasts are deterministic;
- serializing the save before and after a forecast is byte-identical;
- forecast text uses the real runtime doctrine mapping (`PREPARE FOR WAR` under Deterrence);
- plan title/horizon survive mandate reissue.

## Deferred
This is deliberately not a Monte Carlo future simulator. A later feature may provide scenario seeds or deeper staff estimates, but it must not reveal foreign truth the player's government does not possess and must remain observational unless the player explicitly commits to an action.

# 31 — Strategic Board (as built)

## Purpose

Phase E begins by turning Brink's existing causal record into an attention tool. The Strategic Board answers three questions without making a decision for the operator:

1. What currently deserves attention?
2. Is it improving or deteriorating?
3. What is the strongest cause this government is actually entitled to see?

The board is not a new simulation system. It is a read-only interpretation of authoritative state.

## Source discipline

`StrategicBoardSystem` reads the newest player-country `CausalRecord` for each instrumented metric. Driver text is produced only after `CausalDisclosure.Disclose` has applied Cabinet reporting, intelligence and classification rules. Raw classified causes are never rendered by the board.

An old save with an empty causal ledger reports that no resolved-month analysis exists. It does not manufacture historical explanations.

## Attention ranking

The board ranks adverse movement together with dangerous current levels. Low approval/living standards/markets/treasury and high unrest/grievance/exhaustion receive additional urgency. Debt growth is itself attention-worthy. The ranking is deterministic and does not mutate state.

This is deliberately an attention ranking, not an optimization recommendation. Brink's core problem remains deciding what deserves scarce attention; the game may organize evidence but should not play itself.

## Presentation contract

Each item carries:
- metric label;
- current value;
- latest delta;
- improving / deteriorating / stable direction;
- dominant disclosed driver;
- an incomplete-report flag when causes are withheld.

The compact renderer defaults to the five highest-attention items and explicitly states that it does not choose for the player.

## Validation authored

Focused EditMode coverage checks:
- acute deterioration outranks a healthy improving metric;
- building the board does not alter the causal ledger;
- classified causes cannot leak through the board;
- empty old-save history is described honestly.

Full Unity integration certification is intentionally deferred to the C/D/E checkpoint.
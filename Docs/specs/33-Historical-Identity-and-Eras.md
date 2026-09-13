# 33 — Historical Identity and Strategic Eras

## Status
Phase G implementation specification.

## Purpose
A long-running Brink save should feel as though it is accumulating political-strategic history, not merely advancing a calendar. Phase G begins by turning the Chronicle and standing strategy into legible historical identity and named strategic eras.

## Historical identity
- Identity is interpreted from the player's own recorded Chronicle plus a small number of visible inherited national characteristics.
- Repeated military, economic, diplomatic, or political history can make labels such as SECURITY STATE, COMMERCIAL POWER, BROKER STATE, or CONTESTED ORDER legible.
- Existing structural characteristics can add secondary traditions such as ARMED TRADITION or INDUSTRIAL TRADITION.
- Identity is descriptive. It grants no modifier, resource, probability adjustment, permission, or restriction.
- Other countries' Chronicle records do not count toward the player's historical identity.

## Strategic eras
- Once a standing doctrine has been chosen, the posting receives a human-readable era name.
- Deterrence -> Shield Era; Prosperity -> Growth Era; Influence -> Reach Era; Resilience -> Hardening Era; Transformation -> Reconstruction Era; Balanced -> Stewardship Era.
- Current national pressure can qualify the era as Wartime, Crisis, or Austerity without changing the underlying doctrine.
- Era names are narrative handles. They do not alter doctrine effects or national power.

## Information and determinism
Both systems are read-only. They consume no RNG, spend no player resource, advance no date, and mutate no save state. They interpret only information already legitimately available in the player's own state and historical record.

## Design intent
The player should be able to look back after twenty years and say not only what happened, but what kind of government and strategic period those events amounted to. Brink's history should acquire names and patterns without those labels becoming character classes.

## Tests
`HistoricalIdentityTests` covers identity emerging from repeated player-country history, the foreign-history boundary, and non-mutation.

`StrategicEraTests` covers doctrine-to-era naming and pressure qualifiers without changing the standing doctrine or action sequence.

# 33 — Historical Identity, Precedent, and Strategic Eras

## Status
Phase G implementation specification.

## Purpose
A long-running Brink save should feel as though it is accumulating political-strategic history, not merely advancing a calendar. Phase G turns the Chronicle and standing strategy into legible historical identity, precedent, named strategic eras, and visible strategic reversals.

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

## Precedent
- The existing Chronicle is the source of truth. Phase G does not manufacture a second history ledger or retroactively invent events the game failed to record.
- Major player-country military, diplomatic, political, and strategically relevant economic entries can be read as precedent years later.
- A precedent says what later governments can point to, not what they are forced to repeat.
- Foreign-country entries do not become player precedent.
- Precedent itself applies no modifier. Existing simulation systems may already remember relationships and consequences; this layer makes the recorded historical meaning legible without double-counting it.

## Strategic reversals
- `StrategicPlan.revisionCount` is interpreted as continuity, course revision, second turn, or strategic break.
- Revising doctrine does not erase the earlier course. The reversal becomes part of how the posting is described.
- A crowded political Chronicle can make repeated revision read as part of a broader period of adjustment, but this remains interpretation rather than a penalty.

## Information and determinism
All Phase G readers are read-only. They consume no RNG, spend no player resource, advance no date, and mutate no save state. They interpret only information already legitimately available in the player's own state and historical record.

## Design intent
The player should be able to look back after twenty years and say not only what happened, but what kind of government and strategic period those events amounted to. A war, settlement, political rupture, economic choice, or doctrinal reversal should remain something later leaders can point back to. Brink's history should acquire names and patterns without those labels becoming character classes.

## Tests
`HistoricalIdentityTests` covers identity emerging from repeated player-country history, the foreign-history boundary, and non-mutation.

`StrategicEraTests` covers doctrine-to-era naming and pressure qualifiers without changing the standing doctrine or action sequence.

`PrecedentTests` covers player-country military precedent, the foreign-history boundary, and non-mutation.

`StrategicReversalTests` covers revision history and continuity when no revision has occurred.

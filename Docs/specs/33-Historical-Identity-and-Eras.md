# 33 — Historical Identity, Precedent, Credibility, and Strategic Eras

## Status
As-built and player-facing. Eras, reversal, precedent, credibility memory and
the recovery file render in STRATEGIST's STRATEGIC RECORD section, and
historical identity now renders there beside them. Covered by
`CabinetConsultationTests`; not yet certified as production.

## Purpose
A long-running Brink save should feel as though it is accumulating political-strategic history, not merely advancing a calendar. Phase G turns existing authoritative records into legible historical identity, precedent, named strategic eras, visible strategic reversals, diplomatic credibility memory, and recovery history.

## Historical identity
- Identity is interpreted from the player's own recorded Chronicle plus a small number of visible inherited national characteristics.
- Repeated military, economic, diplomatic, or political history can make labels such as SECURITY STATE, COMMERCIAL POWER, BROKER STATE, or CONTESTED ORDER legible.
- Existing structural characteristics can add secondary traditions such as ARMED TRADITION or INDUSTRIAL TRADITION.
- Identity is descriptive. It grants no modifier, resource, probability adjustment, permission, or restriction.
- Other countries' Chronicle records do not count toward the player's historical identity.
- **Where the operator reads it.** STRATEGIST prints the identity immediately
  after the era line, inside STRATEGIC RECORD: the era names the phase the
  posting is in, the identity names what the record has made of it. The view is
  a pass-through to `HistoricalIdentitySystem.Render` — the labels and their
  evidence thresholds exist in exactly one place, and a test fails if a view
  ever names one itself.
  Reading it changes nothing: no save write, no counter, no clock. A test
  plants two dozen foreign chronicle entries and asserts our identity is
  unmoved, which guards the fog rule above as well as the record rule.

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

## Credibility and promise memory
- Brink already stores bilateral `Relationship.memory`, `memoryWeight`, trust, treaty commitments, and who broke a treaty. Phase G exposes that authoritative record instead of creating another credibility meter.
- The player can see which partners carry durable history, how many commitments remain active, and whether past treaties were broken by us or by them.
- Trust remains the simulation's existing diplomatic variable. The credibility file explains the record; it does not secretly modify trust or create a parallel score.

## Failure and recovery
- Failure is allowed to remain history rather than becoming a forced game-over.
- Durable setback evidence such as wars won/lost and administrations served remains visible.
- The recent causal ledger supplies an explicitly labelled twelve-month recovery/pressure reading. Metrics whose increase is harmful (unrest, grievance, debt, war exhaustion) are interpreted in the correct direction.
- Recovery never deletes the setback. A state can be described as recovering after defeat while the defeat remains part of its record.
- The reader does not manufacture long-range causal history beyond the ledger's actual retention window.

## Information and determinism
All Phase G readers are read-only. They consume no RNG, spend no player resource, advance no date, and mutate no save state. They interpret only information already legitimately available in the player's own state and historical record.

## Design intent
The player should be able to look back after twenty years and say not only what happened, but what kind of government and strategic period those events amounted to. A war, settlement, promise, broken treaty, political rupture, economic choice, defeat, recovery, or doctrinal reversal should remain something later leaders can point back to. Brink's history should acquire names and patterns without those labels becoming character classes.

## Tests
`HistoricalIdentityTests` covers identity emerging from repeated player-country history, the foreign-history boundary, and non-mutation.

`StrategicEraTests` covers doctrine-to-era naming and pressure qualifiers without changing the standing doctrine or action sequence.

`PrecedentTests` covers player-country military precedent, the foreign-history boundary, and non-mutation.

`StrategicReversalTests` covers revision history and continuity when no revision has occurred.

`CredibilityMemoryTests` covers existing bilateral memory becoming legible, broken-treaty attribution, and preservation of the authoritative trust/memory values.

`RecoveryHistoryTests` covers recovery coexisting with a recorded setback and whole-state non-mutation.

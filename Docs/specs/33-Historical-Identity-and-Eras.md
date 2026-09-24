# 33 — Historical Identity, Precedent, Credibility, and Strategic Eras

## Status
Roadmap #26 implements evidence-backed country identity, dated turning points
and an AI public-history consumer. Author checks and independent native review
are separate; balance and hardware are deferred, not completion gates. Existing
Roadmap #27 adds earned, durable own-posting doctrine/era names to the older
declared-course labels. Author verification is not independent certification.

## #27 earned doctrines and eras

The selected doctrine still produces a **declared** strategic era. That is intent,
not a historical achievement. Separately, a classified own-office conduct record
samples each resolved month after all resolution and year-end handlers. Choosing
a doctrine, visiting a screen or repeatedly invoking the recorder in one month
cannot earn a name. No RNG, resource, XP, action budget, AI modifier or score changes.

Five concrete patterns are sampled, once per month regardless of multiplicity:

| Name component | True at the end of the resolved month |
| --- | --- |
| Defence Partnership | At least one active mutual-defence commitment involving us |
| Economic Pressure | At least one sanction regime sent by us remains in force |
| Forward Presence | Our military posture is Forward |
| Collection Network | At least one own uncompromised foreign station has positive penetration |
| Commercial Partnership | At least one active trade-preference commitment involves us |

Treaty conditions, expiry and breakage use `HasActive`, not the historical `Has`.
These are held-policy observations, not numbers of clicks or claims about money
spent, regionality, naval deployments, sanction-before-war sequencing, annexation
restraint or uninterrupted duration. Inherited agreements and delegation count
as national conduct; the text explicitly does not award personal authorship.

Each complete non-overlapping 60-month window receives a dated System-category
Chronicle review. Patterns observed in at least36 months qualify; the two longest
qualifying durations supply a deterministic composite name, with table order
breaking ties. The review names both the doctrine and era, prints all five counts,
the observed interval and the naming rule. With none qualifying it says no settled
doctrine was earned, rather than forcing a flattering title. The thresholds are
narrative classification rules, not tuned mechanical rewards.

Every later review cites the last earned name and its original review date,
whether the new window sustains it, changes course or has no settled pattern.
Earlier records never change. The latest review appears in our historical
portrait (STRATEGIST, CHRONICLE and our DOSSIER); all reviews remain in the
Chronicle. This is classified posting history, not a free revelation of foreign
collection or intent. Foreign portraits do not derive or show these records.

`CountryState.strategicConduct` is optional: dates, observed month count, five
integer durations and the last earned name/date. Missing legacy records start at
the first actually observed month; a gap resets the unfinished window without
filling it from today's policies. Duplicate/backward calls do nothing. A successor
has its own empty record, not the parent's years. Save version7 is unchanged.
Completed reviews use appended `HistoricalEvent.ConductReview` (3), no second
list of historical eras or parsing of old prose. A fresh reset starts fresh.

Scope: names and definitions earned by the operator's government from bounded
observations. No new player-naming UI, AI history powers, foreign secret classifier
or general-purpose natural-language historian. Balance and hardware remain separate.

## #26 evidence and memory

STRATEGIST, CHRONICLE (selected country, ours under ALL) and DOSSIER share the
same pure reader. Known-for evidence names initiated confrontations (not
necessarily wars), current/final economic strategies (not past pivots), distinct
recorded sanctions targets, signed intelligence-sharing partners (not necessarily
active access), and agreements broken by the subject. Ten years with no recorded
initiated confrontation permits a qualified absence claim, not proof of pacifism,
avoided land wars or complete legacy records. Successors start at their own founding.

Old category labels are explicitly *archive emphasis*, not strategic verdicts.
Backstory and future entries do not establish earned identity. Current military,
industrial and unrest values no longer manufacture historical traits; structural
national traits stay on their existing surfaces. Foreign secrets and hidden
numeric state cannot change a historical portrait. No class bonuses or restrictions.

Turning points combine saved confrontation starts, treaty signatures and tagged
Chronicle entries: sanctions, industrial/site completion, coups, civil-conflict
recovery, secession/reunion and settlements. Latest eight are dated, deterministically
ordered and deduplicated; the complete Chronicle is still accessible. Participants
share public events. Missing dates are not invented. Readers return detached data.

ChronicleEntry gains HistoricalEvent (None=0, SanctionsImposed=1, TurningPoint=2)
and counterpartyId. Producers annotate their existing entry, without another event
or reward. Legacy prose remains untyped; no text parsing or backfill. Version7.

AIPrediction counts the union of live sanction targets and typed public targets
within120 months inclusive, once per partner, then adds its existing tariff/embargo
observations. At121 months lifted measures leave the AI window, not the lifetime
archive. Live measures continue counting. Secret/future/backstory acts are excluded.
Existing18-point intensity, cap, easing, confidence and counter-objective rules
remain. OpponentModel.coercionPrecedent persists a dated public citation, refreshed
on observation; missing old-save citations are empty, not invented. The legacy
PlayerAssessment is unchanged. This intentionally changes AI responses after
sanctions are lifted, without another budget slot or permanent penalty. The UI
shows what governments *can* cite, never their private plans. No trajectory-parity
or general balance certification follows from this implementation.

## Purpose
A long-running Brink save should feel as though it is accumulating political-strategic history, not merely advancing a calendar. Phase G turns existing authoritative records into legible historical identity, precedent, named strategic eras, visible strategic reversals, diplomatic credibility memory, and recovery history.

## Historical identity
- Identity is interpreted from the selected country's observer-visible record,
  saved confrontations and agreements, not current hidden national statistics.
- Repeated military, economic, diplomatic, or political history can make labels such as SECURITY STATE, COMMERCIAL POWER, BROKER STATE, or CONTESTED ORDER legible.
- #26 removes the former current-statistic-derived tradition labels; current
  capacity is not evidence of a historical tradition.
- Identity is descriptive. It grants no modifier, resource, probability adjustment, permission, or restriction.
- Other countries' unrelated records do not count toward the subject's identity.
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
All historical presentation readers are read-only. They consume no RNG, spend no
resource, advance no date and mutate no save. They use our own record or public
foreign evidence. AIPrediction's observation step, separately, updates its saved
model and citation using that public evidence.

## Design intent
The player should be able to look back after twenty years and say not only what happened, but what kind of government and strategic period those events amounted to. A war, settlement, promise, broken treaty, political rupture, economic choice, defeat, recovery, or doctrinal reversal should remain something later leaders can point back to. Brink's history should acquire names and patterns without those labels becoming character classes.

## Tests
`HistoricalIdentityTests` additionally covers real sanction/project producers,
removal without forgetting, deduplication,119/120/121-month boundaries, secret and
future exclusions, existing AI prediction/counter-objective consumers, successor
dates, old-save defaults, dated/detached turning points, narrow real UI surfaces,
pure reads and a24-month pipeline save/resume determinism comparison. Mutation
results are author evidence until reproduced independently in native Unity.

`StrategicEraTests` retains declared-course coverage and adds five real pattern
predicates,59/60-month and35/36-held boundaries, stable composite ranking, gaps,
duplicate calls, legacy/successor defaults, expiry, private evidence, repeated
historical references, real pipeline/save-resume, two240-month observed/control
world comparisons and four-width real UI tests. Author mutation results must be
reproduced independently before being called native certification.

`PrecedentTests` covers player-country military precedent, the foreign-history boundary, and non-mutation.

`StrategicReversalTests` covers revision history and continuity when no revision has occurred.

`CredibilityMemoryTests` covers existing bilateral memory becoming legible, broken-treaty attribution, and preservation of the authoritative trust/memory values.

`RecoveryHistoryTests` covers recovery coexisting with a recorded setback and whole-state non-mutation.

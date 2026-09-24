# 32 — Institutional Personality

## Status
As-built and player-facing. The personality, meeting, alignment and
intervention readings render on CABINET; the choice reading is reached there
too, through **Cabinet Consultation** (below). Covered by
`CabinetConsultationTests`; not yet certified as production.

## Purpose
Cabinet officials should become people the operator learns to govern with rather than interchangeable pillar stat blocks. This layer makes existing competence, loyalty, risk tolerance, trust, office, control mode, and current national pressure legible as institutional personality.

## Rules
- Personality is derived from existing authoritative state. It creates no new currency or hidden bonus.
- The player-facing personality layer may profile only the player's own seated officials. It must not bypass intelligence/fog to characterize foreign ministers.
- Each official exposes a stable institutional identity, temperament, relationship posture, strategic instinct, and resistance level.
- **Minister tenure is separate from ministry inheritance.** The seated
  official's existing `monthsInOffice` reads as newly appointed (under one
  year), settled (one to four years), established (four to eight years), or
  entrenched (eight years onward). CABINET shows that reading in both the
  meeting and the official dossier. Replacement naturally returns the office to
  newly appointed because a new official begins at month zero; old saves already
  carry the authoritative tenure and need no migration.
- Tenure is not automatic hostility or a hidden competence bonus. It adds one
  level of expected resistance only when an entrenched office is already
  strained (`trust < 50`), capped by the existing resistance scale. A trusted
  long-serving ministry remains cooperative.
- Cabinet meetings show those identities alongside the existing state-dependent concern and pressure.
- Directed officials acknowledge the human-readable directive label, never an internal command id.
- Cabinet fault lines are deterministic disagreements derived from meaningful differences such as risk appetite, low trust, asymmetric operator direction, and the structural Military/Economy budget tension.
- Fault lines are advice and political context, not automatic vetoes. Brink remains a game about consequences rather than arbitrary restrictions.
- Cabinet alignments identify natural allies where institutional interests, risk appetite, trust, or current operator posture converge. They are descriptive coalitions, not a new faction meter.
- Before a Direct Control intervention, the institutional layer can explain how the affected official is likely to receive the bypass and whether aligned offices are likely to notice it. This is a consequence preview, never a permission check.
- Strategic choice readings interpret major directions such as escalation, peace, spending, fiscal restraint, covert risk, diplomatic compromise, and domestic posture through the actual player's Cabinet. They are expected reactions, never votes or vetoes.
- **Cabinet Consultation is how the operator reaches that reading.** It is
  deliberately *operator-initiated*: the player goes to CABINET, selects the
  direction they are weighing, and reads the room. The alternative — a standing
  preview beside every pillar action — was rejected because it would put
  permanent advice on screens this project has already had to de-noise once,
  and because asking is itself the operator's decision.
  The panel offers `CabinetChoiceReadingSystem.ChoiceKind` by enumerating the
  enum rather than restating it, so a direction added to the reader cannot
  become unreachable. Selecting the current direction puts the question down
  again, which keeps CABINET short on a phone.
  Consultation calls no `GameController` verb, so by construction it cannot
  spend Command Points or Influence, advance the month, draw RNG, execute the
  direction, move an official's trust/loyalty/competence/control mode, gate
  eligibility, veto anything, change AI behaviour, or write to the save.
  `CabinetConsultationTests` asserts that, save JSON included.
- Strategic surprise is represented as exposure to uncertainty rather than a hidden-truth oracle. It can say that collection is weak, fiscal room is deteriorating, domestic consent is brittle, or foreign reaction remains contingent; it cannot reveal what an unseen foreign actor will actually do.
- Surprise and choice readings are observational. They do not alter action resolution, probabilities, official traits, resources, or the simulation pipeline.
- All presentation queries are read-only. Rendering a meeting, profile, alignment, intervention reading, choice reading, or surprise assessment cannot spend CP/Influence, advance time, consume RNG, or mutate the save.

## Ministry inheritance (remaining core #16)

CountryState owns one optional InstitutionalMemory per office, independent of
the seated minister: successes, setbacks, signed risk imprint and last outcome
date. Existing significant Cabinet outcomes add +2 after success or -3 after
failure, capped to [-12,12]. Ordinary routine work creates no invented success.
The old imprint decays by 0.99 per elapsed month before a new outcome is added;
pure reads use the same dated decay, including vacancies and Direct Control.
Counts remain history, not an indefinitely stacking bonus.

CabinetSystem uses clamp(minister risk + dated imprint, 0,100) in its existing
monthly performance variance and significant-outcome chance. More appetite
means more variance and more opportunities for both success and failure, not
more competence. There is no extra RNG draw, action, reward or pipeline hook.
The current month's outcome changes future readings, not that month's draw.
The rule is identical for foreign ministries; foreign profiles remain blocked.
Direct Control neither executes these delegated effects nor writes an outcome.

Replacement resets minister tenure but preserves the country's ministry record.
The meeting and dossier show both readings, recorded counts and effective risk.
Missing/null/default legacy history is neutral; no past is inferred from prose.
This is a bounded experience-based institutional character, not invented
procurement, coordination or political-influence statistics. The +2/-3, cap and
decay are implementation tuning, not balance-certified values.

## Design intent
The desired player thought is: “I know what this minister is likely to argue, I know who will line up with them, I know what bypassing that office again is likely to do, and I know where my government's assumptions are fragile.” The system should add institutional texture without turning Brink into a relationship-management game or converting uncertainty into omniscience.

## Tests
`CabinetMeetingTests` covers read-only behavior, pressure ordering, human directive labels, stable profiles, visible cabinet fault lines, and the foreign-official information boundary.
It also pins all four continuity thresholds, the strained-only resistance rule,
player-facing meeting text, and byte-identical save state after reading it.

`CabinetDynamicsTests` covers natural Cabinet alignments, high-resistance intervention readings, and the read-only contract.

`CabinetChoiceReadingTests` covers choice-driven institutional disagreement, no-veto/read-only behavior, and the player-Cabinet information boundary.

`StrategicSurpriseTests` covers collection blind spots without foreign truth leakage, acute domestic exposure, and the read-only contract.

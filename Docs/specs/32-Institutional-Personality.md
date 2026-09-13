# 32 — Institutional Personality

## Status
Phase F implementation specification.

## Purpose
Cabinet officials should become people the operator learns to govern with rather than interchangeable pillar stat blocks. This layer makes existing competence, loyalty, risk tolerance, trust, office, control mode, and current national pressure legible as institutional personality.

## Rules
- Personality is derived from existing authoritative state. It creates no new currency or hidden bonus.
- The player-facing personality layer may profile only the player's own seated officials. It must not bypass intelligence/fog to characterize foreign ministers.
- Each official exposes a stable institutional identity, temperament, relationship posture, strategic instinct, and resistance level.
- Cabinet meetings show those identities alongside the existing state-dependent concern and pressure.
- Directed officials acknowledge the human-readable directive label, never an internal command id.
- Cabinet fault lines are deterministic disagreements derived from meaningful differences such as risk appetite, low trust, asymmetric operator direction, and the structural Military/Economy budget tension.
- Fault lines are advice and political context, not automatic vetoes. Brink remains a game about consequences rather than arbitrary restrictions.
- Cabinet alignments identify natural allies where institutional interests, risk appetite, trust, or current operator posture converge. They are descriptive coalitions, not a new faction meter.
- Before a Direct Control intervention, the institutional layer can explain how the affected official is likely to receive the bypass and whether aligned offices are likely to notice it. This is a consequence preview, never a permission check.
- The system is read-only. Rendering a meeting, profile, alignment, or intervention reading cannot spend CP/Influence, advance time, consume RNG, or mutate the save.

## Design intent
The desired player thought is: “I know what this minister is likely to argue, I know who will line up with them, and I know what bypassing that office again is likely to do to the working relationship.” The system should add institutional texture without turning Brink into a relationship-management game.

## Tests
`CabinetMeetingTests` covers read-only behavior, pressure ordering, human directive labels, stable profiles, visible cabinet fault lines, and the foreign-official information boundary.

`CabinetDynamicsTests` covers natural Cabinet alignments, high-resistance intervention readings, and the read-only contract.

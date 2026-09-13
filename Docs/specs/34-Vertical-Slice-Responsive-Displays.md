# 34 — Vertical Slice: Responsive Displays

**Status:** ACTIVE — vertical-slice requirement

Brink is a landscape-first mobile command simulation. The vertical slice must treat conventional phones, large phones, foldable cover displays, unfolded foldables, tablets, and resizable/windowed play as first-class layouts.

## Rule

**Small screens prioritize. Large screens contextualize. Neither changes the rules of the game.**

Layouts are selected from usable terminal width, not from device names or hard-coded model dimensions. The existing column breakpoints remain the authority: Compact below 64 columns, Medium from 64, Large from 82.

A fold/unfold, rotation, safe-area change, or window resize must be handled at runtime without restarting the session.

## Vertical-slice acceptance widths

Every primary player-facing surface must remain usable at 41, 49, 60, 64, 79, 82, 100, and 104 terminal columns. This includes COMMAND CENTER, BRIEFING, ACTIONS, WORLD MAP, DOSSIER, CABINET, all five pillar desks, STRATEGIST, and CHRONICLE.

Compact layouts may stack information and shorten navigation labels. Medium and Large layouts should spend extra space on context: wider maps, fuller labels, less wrapping, and where useful persistent secondary information. Large displays must not merely magnify the compact phone layout.

No command may become unavailable because a display is smaller. No larger-display layout may reveal simulation truth that the compact layout correctly hides.

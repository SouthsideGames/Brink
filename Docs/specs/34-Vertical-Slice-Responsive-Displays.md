# 34 — Vertical Slice: Responsive Displays

**Status:** implementation rule for the vertical slice

## Purpose

Brink is mobile-first, but mobile no longer means one narrow rectangle. The vertical slice must be deliberately usable on conventional phones, large phones, foldable cover displays, unfolded foldables, and tablet/desktop-class canvases.

The interface must scale by **usable information space**, not by device marketing name. A larger display should reveal more of Brink at once rather than simply making the same interface physically larger.

## Canonical layout classes

Brink continues to classify terminal space by measured monospace columns:

- **Compact — below 64 columns.** Conventional phones, narrow cover displays, constrained split-screen/windowed layouts. Use compact navigation, one primary information stream, wrapping, scrolling, and abbreviated labels where necessary.
- **Medium — 64–81 columns.** Large phones and many foldable/windowed configurations. Preserve readable single-column flow but expose more context, fuller labels, and taller/more informative ASCII presentation.
- **Large — 82+ columns.** Unfolded foldables, tablets, desktop and other expansive canvases. Use the extra space for information density, persistent context, wider maps/readouts, and—where it genuinely improves comprehension—two-region presentation. Do not merely enlarge fonts and controls.

These are content breakpoints. Physical inches, DPI, platform and model names must not decide the layout.

## Foldable requirements

1. **Outer and inner displays are both first-class targets.** Brink must remain operable when a foldable is closed and should become materially more informative when opened.
2. **Runtime resizing must be safe.** A fold/unfold, orientation change, split-screen resize, or window resize must remeasure terminal columns and reflow without requiring a restart or losing the active game state.
3. **No hinge assumptions.** The UI must not place a required control at a hard-coded screen center. Safe-area and runtime geometry remain authoritative.
4. **No stretched phone UI on the inner display.** Large layouts should spend their additional width on strategic context: navigation labels, map/readout width, side-by-side context when appropriate, and fewer unnecessary screen transitions.
5. **No foldable-only gameplay.** Larger displays improve legibility and information density; they never expose commands or simulation capability unavailable on Compact layouts.
6. **Touch remains primary.** Larger canvases do not justify smaller targets or mouse-only affordances.

## Vertical-slice device matrix

Every major player-facing vertical-slice surface must be checked at representative terminal widths:

- 41 columns — constrained phone / cover / narrow window
- 49 columns — normal compact landscape target
- 60 columns — large compact target
- 64 columns — Medium boundary
- 79 columns — broad Medium / foldable transition
- 82 columns — Large boundary
- 100 columns — unfolded foldable / tablet-like canvas
- 104 columns — broad large target

The checks apply at minimum to Command Center, Briefing, Actions, World Map modes, Dossier, Cabinet, five pillar views, Strategist/Forecast, Chronicle/history surfaces, operation planning, and crisis presentation.

## Command Center rule

The Command Center is the first vertical-slice surface and sets the pattern:

- Compact: attention-first vertical briefing; decisions and urgent pressure before context.
- Medium: same hierarchy with fuller context and fewer abbreviations.
- Large: preserve the attention-first reading order while using additional width for simultaneous strategic context rather than larger typography.

## Acceptance criteria

A responsive surface is acceptable when:

- no essential command is clipped or unreachable;
- terminal text does not horizontally overflow its intended region;
- controls remain touch-usable;
- information hierarchy survives all three size classes;
- opening a foldable provides meaningfully more usable information;
- closing/resizing does not lose state or require navigation recovery;
- safe areas and system cutouts do not obscure required controls;
- the same simulation actions are available in every size class.

## Design principle

**Small screens prioritize. Large screens contextualize. Neither changes the rules of the game.**

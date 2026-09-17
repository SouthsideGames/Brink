# 28 — ASCII Presentation Engine and Strategic Map Modes

## Status
Phase C development slice. Static implementation complete; comprehensive Unity integration verification is deferred to the C–G milestone gate. The five pillar dashboards now surface their live institutional signatures directly beneath the authority badge.

## Principle
ASCII is Brink's visual medium, not decoration around a text interface. Presentation code therefore gets reusable drawing primitives and live strategic layers rather than one-off strings embedded in views.

## AsciiCanvas
`AsciiCanvas` is a fixed-size retained ASCII surface with:

- clipped point plotting;
- clipped text placement;
- Bresenham line drawing;
- box drawing;
- import from an existing text figure;
- exact-width/exact-height rendering.

It owns clipping and layering rules so maps, scenes and future pillar art do not each reimplement them.

## World map modes
The MAP world layer now exposes six modes:

1. **Political** — the existing public standing map.
2. **Military** — live confrontation routes, Total War emphasis and occupied-ground signals.
3. **Trade** — the player's trade routes, embargoes and sanctions involving the player.
4. **Intelligence** — only networks owned by the player's government, with access bands derived from penetration already known to that government.
5. **Blocs** — standing bloc membership drawn from public alliance structure.
6. **Activity** — countries with one or multiple public events on last month's
   World Wire, so the map shows where the simulation just moved rather than only
   standing structures.

Modes are views over the authoritative save. They add no persistent state and do not alter simulation resolution.

## Fog rules
Map modes follow the same information contract as the rest of Brink:

- Political standing is public.
- Active wars and territorial occupation are public.
- Trade mode shows the player's own commercial exposure rather than revealing every hidden economic dependency in the world.
- Intelligence mode reads only `IntelNetwork` objects owned by the player and never plots a foreign service's network.
- Bloc membership is treated as public diplomatic structure.
- Activity delegates visibility to `WorldWire` and never reads secret or
  Intelligence-category chronicle entries directly.

A presentation layer must never become a shortcut around `IntelligenceSystem`.

## Responsive contract
Every mode renders into the exact grid requested by the terminal. The authored world remains 80×21 conceptually, while country coordinates and route geometry scale into the current device grid. Overlay lines do not overwrite country labels or the underlying landmass unless an explicit signal must win the cell.

## Tests
Focused tests cover:

- canvas clipping and fixed dimensions;
- non-destructive line layering;
- import of an existing ASCII figure;
- exact grid dimensions in all five map modes;
- Political mode remaining byte-identical to the existing map;
- foreign intelligence networks staying invisible;
- live bloc/trade state changing the relevant overlay;
- player-facing summary counts excluding unrelated foreign trade.
- Activity mode excluding secret and older chronicle entries, and distinguishing
  one event from several without exposing their hidden causes;
- fixed five-row institutional signatures for all five pillars;
- the 32–100 column responsive contract, for every pillar;
- that all five pillar dashboards draw their signature through the shared
  `AddPillarArt` path, which is otherwise a line in a view no headless
  assertion reaches;
- that the state house is never overwritten by its own readout, pinned at the
  32-column floor with three-digit figures, which is the tightest case the
  labels can reach;
- that the market skyline varies across a high band of sector outputs, and
  still reads level for a genuinely balanced economy.

Both of those set their inputs explicitly rather than reading the fixture seed,
and both are mutation-checked. The skyline test is the reason why: an earlier
version asserted against the seeded world and silently stopped guarding the
calibration, because seed 6120 puts one sector at 59.7 — below the old 68-point
bucket edge — so two heights appeared even under the broken mapping. Every
output it now sets sits above that edge, so reverting to absolute thresholds
collapses them into one bucket and fails. **A regression test for a calibration
has to choose values that straddle nothing.**

Two calibration rules the pillar figures are held to. The Government readout
sits on the heading row, not on the building's foundation row: at the 32-column
floor the centred facade reaches within a few columns of both edges, and labels
drawn onto that row fused into it. The market skyline takes each column's height
from a sector's capacity scaled against that country's own spread — a healthy
economy occupies a narrow high band, so absolute 0..100 thresholds put every
sector in one bucket and flatten the skyline as completely as an arithmetic bug
would.

`AsciiMapModeTests` is registered in `Tools/run-suite.sh` so the suite coverage guard continues to protect the fixture.

## Next Phase C slices
The same canvas should continue to be reused for:

- institutional scenes and Cabinet meeting layouts;
- event/crisis scenes;
- reusable icons and sprites;
- lightweight frame animation driven by UI scheduling rather than simulation state.

These are presentation-only. No Phase C visual should consume RNG or write simulation state.

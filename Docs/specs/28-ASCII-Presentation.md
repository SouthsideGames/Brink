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
The MAP world layer now exposes five modes:

1. **Political** — the existing public standing map.
2. **Military** — live confrontation routes, Total War emphasis and occupied-ground signals.
3. **Trade** — the player's trade routes, embargoes and sanctions involving the player.
4. **Intelligence** — only networks owned by the player's government, with access bands derived from penetration already known to that government.
5. **Blocs** — standing bloc membership drawn from public alliance structure.

Modes are views over the authoritative save. They add no persistent state and do not alter simulation resolution.

## Fog rules
Map modes follow the same information contract as the rest of Brink:

- Political standing is public.
- Active wars and territorial occupation are public.
- Trade mode shows the player's own commercial exposure rather than revealing every hidden economic dependency in the world.
- Intelligence mode reads only `IntelNetwork` objects owned by the player and never plots a foreign service's network.
- Bloc membership is treated as public diplomatic structure.

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
- fixed five-row institutional signatures for all five pillars;
- the 32–100 column responsive contract for pillar art.

`AsciiMapModeTests` is registered in `Tools/run-suite.sh` so the suite coverage guard continues to protect the fixture.

## Next Phase C slices
The same canvas should continue to be reused for:

- institutional scenes and Cabinet meeting layouts;
- event/crisis scenes;
- reusable icons and sprites;
- lightweight frame animation driven by UI scheduling rather than simulation state.

These are presentation-only. No Phase C visual should consume RNG or write simulation state.

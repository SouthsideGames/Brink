# 13 — Technology & Research Specification

Source: `Core/TechnologySystem.cs`, `Core/CapabilityCatalog.cs`,
`Data/Technology.cs`. GDD §11.

## 1. Four rules

1. **Capability-based, not a tech tree.** Research is distributed across the five
   pillars. There is no sixth pillar, no linear ladder, and no single tree.
2. **Technology unlocks capability, not substance.** A completed programme never
   hands over force structure, trained personnel, infrastructure or money. Those
   must still be built, recruited, financed and maintained. A test asserts that
   granting capabilities changes force power, treasury and manpower by zero.
3. **Knowledge spreads.** Through treaties, exercises, contact and espionage. An
   advantage cannot be held close forever.
4. **Not all knowledge is equal.** What you developed you understand; what you
   stole you merely possess, until you have used it for years.

## 2. Research programmes

```
cost to start: 2 CP
concurrent:    2 per state (MaxPrograms)
duration:      20–36 months, charged monthly to the treasury
```

A programme requires prerequisites, a minimum industrial base, a minimum pillar
level, and four months' funding in hand. An unfunded programme is **wound up**,
not run for free.

## 3. Capability catalog

| Pillar | Capability | Effect |
|---|---|---|
| Military | Precision Munitions | −45% collateral harm at full maturity |
| Military | Strategic Lift | Higher supply ceiling; −35% posture burn |
| Military | Integrated ISR | +15% operation power (requires Precision) |
| Economy | Advanced Manufacturing | Industrial capacity compounds monthly |
| Economy | Domestic Energy Programme | Energy security climbs year on year |
| Economy | Financial Infrastructure | −30% sanction blowback (requires Adv. Mfg) |
| Intelligence | Signals Architecture | +1.4 network penetration per month |
| Intelligence | Secure Communications | +50% effective counterintelligence |
| Intelligence | Analytic Computing | −30% estimate margin (requires SIGINT) |
| Diplomacy | Convening Infrastructure | +10 treaty willingness |
| Diplomacy | Verification Regimes | +12 settlement willingness (requires Convening) |
| Government | Civil Administration Reform | +0.6 Political Capital per month |
| Government | National Resilience Planning | −35% crisis downside (requires Civ. Admin) |

All effects scale with **maturity**, so a freshly stolen capability delivers a
fraction of what a matured one does.

## 4. Diffusion

Checked monthly per country, at most one acquisition per month:

```
treaty partner holds it        → 0.8%/mo, 1.8% with IntelligenceSharing → Shared
interoperability + warm ties   → interop × 0.012% + (relations−55) × 0.006% → Observed
our network in their country   → penetration × 0.022%                    → Stolen
```

Starting maturity by source: **Developed 70, Shared 45, Observed 30, Stolen 25.**
Maturity then grows 1.2/mo (developed), 0.9 (shared), 0.6 (stolen or observed).

This is the mechanism behind the GDD's "knowledge can spread through trade,
alliances, joint exercises, espionage" — and it means a technological lead is a
timed asset, not a permanent one.

## 5. AI behaviour

AI states fund research along their leader's national priority (6% chance per
month to open a programme when one is free). They are subject to the same
prerequisites, industrial floors and funding rules.

## 6. Extension points

- **Captured knowledge from operations** — GDD §11 mentions captured knowledge;
  taking a location with an industrial or research character could grant a
  chance at the owner's capability.
- **Joint programmes** — two treaty partners splitting cost and both receiving
  the capability at Shared maturity.
- **Denial** — sanctions or export controls that slow a rival's programme.

## 7. Open questions

- Capability count (13) is small enough that a long save will hold most of them.
  More breadth, or mutually exclusive branches, would keep late saves interesting.
- Nothing currently *obsoletes*. A capability held for fifty years is as good as
  new, which suits an abstract model but flattens long-run technological churn.

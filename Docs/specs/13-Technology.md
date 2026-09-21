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

### Visible research projects (#24, presentation slice)

RESEARCH now shows the full saved programme label, remaining months, saved
monthly cost and their product (AT SAVED TERMS). This is remaining commitment,
not money already spent or a promised completion date. ResearchProgram stores
neither start date nor original duration; the view does not invent either from
the current catalogue. Negative legacy remaining months display as zero without
changing saved state. The next-instalment reading is a current treasury snapshot,
explicitly subject to intervening income and commitments.

Unfunded work is wound up, not paused, without a refund. Completion develops
capability, not equipment, personnel, infrastructure or money. If diffusion has
already provided the capability while research continues, the panel states the
existing rule honestly: funding continues, but Grant cannot award a second copy.
No cancellation command or change to that rule is introduced.

The existing authorization, funding-failure and acquisition chronicle entries
gain RESEARCH AUTHORIZED, RESEARCH WOUND UP and CAPABILITY ACQUIRED prefixes.
Entry counts, publicity and foreign-notification rules are unchanged. The
latest five matching own-country System entries appear newest-first; acquisitions
retain their Developed/Shared/Observed/Stolen source and are not all presented as
research completions. Legacy generic entries remain in history, not retrofitted.
Foreign starts/acquisitions remain unchronicled as before; existing foreign
funding-failure entries are unchanged in count and excluded from this panel.

No new persistent field, schema, RNG, grant, cost, maturity or pipeline change.
Controller start autosave and resolved-month save behavior remain unchanged.
Hardware remains unverified. #24 remains partial: physical sites, bespoke
construction and procurement presentation are not part of this slice.

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

## 6. The catalogue expansion (spec 25 Tranche E, 2026-08-28)

**Status: as-built.** 13 → **33 capabilities**.
`TechnologySystemTests.EveryCapabilityIsReadSomewhere`.

### Capabilities that unlock options, not only efficiencies

The idea the catalogue was missing. The distinction already existed and was
only ever used in one direction: **a skill is operator capability and gates
operator verbs** (spec 07); **a capability is national ability** — and every one
of the original 13 granted a multiplier, with nothing gated behind them except
the five strategic instruments.

| Capability | Unlocks |
|---|---|
| `CAP_CYBER` | `CovertOperation.CyberOperation` — the gate Tranche B was written expecting |
| `CAP_FORENSICS` | The `SubversionSponsorship` Special Estimate; without tracing there is no answer |
| `CAP_ARMSCONTROL` | `TreatyCommitment.ArmsControl`. Willingness returns **0** without it — proposing a limitation you cannot verify is a piece of paper, and the gate is deliberately on the whole treaty, so bundling the clause makes the package unsignable |
| `CAP_HYPERSONIC` | Reads straight through air defences (×0.55) — nothing fielded intercepts it |
| `CAP_OVERHEAD` | Estimates on states we have **nobody in** |

`CAP_OVERHEAD` is the one that changes what the map *looks like* rather than how
sharp it is. Capped at `ConfidenceGrade.Low` with a margin of `30 − reach × 8`,
and **skipped entirely where a network exists**: it must not make networks
redundant, it makes the decision about where to put them better informed.

### `CAP_AGRI` closes a standing open item

A food-poor state had **no route to raise its own ceiling** — only authored trade
links and the player's own deals, neither of which a foreign government can reach
for. An AI state born short of food stayed short for fifty years whatever it did.
`FoodCeilingFor` now adds `Effectiveness × 22`.

### Dual-use — prerequisites in two pillars

The catalogue was five separate ladders. `CAP_SPACE` (ISR + Overhead),
`CAP_STRATLOG` (Lift + LogNet) and `CAP_TECHTRANSFER` (AdvMfg + Convening) are
the rungs that need both, and they are the reason to build breadth rather than
depth in one place.

`CAP_TECHTRANSFER` accelerates maturity **only for what was not developed here**
— what we built we already understand, so there is nothing to absorb.

### The guard

`EveryCapabilityIsReadSomewhere` scans the runtime sources for `CAP_*` outside
`CapabilityCatalog.cs` and fails on any capability nobody reads. Written *before*
the wiring, deliberately: a capability with no read site is funded for years,
completes, and changes nothing — the "written but never read" family, and at 33
entries it would be that family at scale. The catalogue declaring and
cross-referencing an id does not count as somebody reading it.

**Adding a capability is therefore two places, not one:** the catalogue, and a
system that reads it.

### Full list of new read sites

```
CAP_AIRDEFENSE    DefensePowerFor, AirDefenses model      (×1.7 at maturity)
CAP_UNDERSEA      DefensePowerFor, EnemyNavy model        (×1.45)
CAP_AUTONOMY      attacker losses only                    (−30%)
CAP_HYPERSONIC    pierces air defences                    (×0.55)
CAP_AGRI          FoodCeilingFor                          (+22)
CAP_SUBSTITUTION  MaterialsCeilingFor                     (+18)
CAP_LOGNET        targetGrowth, via trade health
CAP_RESERVECURR   sanction pressure on growth             (−35%)
CAP_CYBER         gates the cyber operation
CAP_OVERHEAD      CollectFromOverhead
CAP_FORENSICS     gates the sponsorship question
CAP_ARMSCONTROL   gates limitation treaties
CAP_DEVAID        treaty willingness, via dependence
CAP_BROADCAST     treaty willingness, where relations are cold
CAP_STATISTICS    MishandleChanceFor                      (−45%)
CAP_EMERGENCY     emergency powers PC cost                (−35%)
CAP_CIVILDEF      DisplacementSystem.StandardsDrag        (−40%)
CAP_SPACE         ProjectionRange                         (+12)
CAP_STRATLOG      ProjectionRange                         (+14)
CAP_TECHTRANSFER  maturity of non-developed capabilities  (+60%)
```

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

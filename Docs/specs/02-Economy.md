# 02 — Economy System Specification

Source: `Core/EconomySystem.cs`, `Data/EconomyState.cs`,
`Data/NationalResources.cs`, `Data/TradeAndSanctions.cs`. GDD §20, §20.1.

## 1. Macro model

Per country, updated monthly in `EconomySystem.MonthlyUpdate`. All rates are
annualized percentages; each moves toward a computed target rather than jumping.

### Growth (approach rate 0.35)

```
target = 1.6
       + (economyPillar − 50) × 0.035
       + (industrialCapacity − 50) × 0.012
       + (confidence − 50) × 0.020
       + (tradeHealth − 50) × 0.014
       − sanctionPressure × 0.55
       − sanctionBlowback × 0.20
       − energyDrag
       − 1.3 if at war
```

`energyDrag = (40 − energy) × 0.05` when energy < 40, else 0.

`tradeHealth` is centred on 50 here, which is why §3's normalization matters:
the term is a *deviation from an average trading position*, not a headcount of
links.

### Inflation (approach 0.30, floor −3)

```
target = 2.2
       + max(0, growth − 3.5) × 0.5      demand-side overheating
       + sanctionPressure × 1.2          import scarcity
       + blowback × 1.0                  self-inflicted scarcity
       + energyDrag × 1.2
       + 1.6 if at war
       − max(0, 60 − industrialCapacity) × 0.008
```

**Coercion is modeled as a supply shock.** The scarcity terms dominate the
demand-side term deliberately, so a sanctioned economy *stagflates* — output
falls while prices rise. An earlier build had these reversed and produced
disinflation under sanctions, which was wrong.

### Employment, debt, treasury

```
unemploymentTarget = 6.5 − growth × 0.9 + sanctionPressure × 0.5 − 0.8 if at war
unemployment approaches at 0.25, clamped to [1.5, 35]

debtToGdp += (0.9 at war else 0.15) − max(0, growth) × 0.18, clamped [0, 250]
gdp       ×= 1 + growth/1200,  floored at 50
treasury  += gdp × TreasuryIncomeRate (0.03) × (1 − debtToGdp/400)
```

**Sanctions adapt (2026-08-28).** `SanctionPressureOn` is net of adaptation: a
regime's weight falls linearly to half over `SanctionAdaptationMonths = 48`
(`SanctionAdaptationFloor = 0.5`). Before this a regime bit at full weight
forever and lapsed only when the *sender* stopped being hostile, so two hostile
neighbours could hold a great power in a permanent depression — fundamentals
pinned, `distress` feeding unemployment (28%), living standards (1), unrest
(77), approval (0), a coup — with nothing the target could do. A passive USA
was overthrown in 3 of 6 measured decades. New measures still land at full
weight, so coercion keeps its edge as a move; what it loses is permanence.

**`TreasuryIncomeRate` is the unit every recurring cost is sized against.** It
shipped at 0.012 (15–40/month for the roster) while war exhaustion, occupation,
research programmes and endgame authorizations were priced as if income were
several times that; a belligerent mid-tier state ended a passive decade thousands
in the red and no posting could afford the strategic instruments spec 14 designs
around. 0.03 puts a great power at ~70–100/month (2026-08-26 playtest; spec 12
§6 holds the re-measured table). A new monthly cost should be stated as a
fraction of `gdp × TreasuryIncomeRate`, and `ProgressionSystem.SolvencyPenalty`
now makes a deficit cost the annual grade (spec 07).

The GDP floor is what makes "coercion never zeroes an economy" (§4) structural
rather than a matter of tuning.

### Confidence (approach 0.25, clamped 0..100)

```
target = 50 + growth × 6
       − max(0, inflation − 4) × 3.5
       − sanctionPressure × 6 − blowback × 3
       + (stability − 50) × 0.25
       − 8 if at war
```

### Political spillover

If `inflation > 12` or `growth < −3`: approval −0.6/month, stability −0.35/month.
Economic crisis becomes political crisis.

## 2. Sector layer

Seven sectors: `Energy`, `Agriculture`, `Industry`, `Technology`, `Finance`,
`Consumer`, `Defense`. Each has `output` (capacity) and `health` (functioning).

```
healthTarget = 88 − sanctionPressure × 9 − blowback × 5
               − 12  (Energy, when national energy < 45)
               + 8   (Defense, at war)
               − max(0, debtToGdp − 90) × 0.15  (Finance)
health approaches at 0.2, clamped 0..100

outputTarget = output + growth × 0.08 − sanctionPressure × 0.35
               + 0.5  (Defense, at war)
output approaches at 0.5, clamped 0..100
```

## 3. Trade

`TradeRelation` per unordered pair: `volume` (strategic weight `0..100`),
`tariff` (`0..100`%), `embargoed`.

`TradeHealth` is a **0..100 read of how well a country's trade is working for
it** — an average per link, lifted modestly by breadth:

```
for every link involving the country:
    links++                                  ← embargoed links still counted here
    if embargoed: contribute nothing
    else: total += volume × (1 − tariff/150)

if links == 0: return 0

average = total / links
breadth = min(1.25, 0.80 + links × 0.05)     ← saturates at 9 partners
result  = clamp(average × breadth + TerritorySystem.TradeAccessSwing, 0, 100)
```

Two details carry the design:

- **An embargo counts in the denominator but contributes nothing.** Cutting a
  partner off does not improve your average by removing a weak link; it costs
  you the share of your trade that partner was.
- **Breadth is capped at ×1.25.** Having many partners is genuine resilience,
  but it must not scale without bound.

### Why it is an average and not a sum

This previously returned the raw **sum** while its only consumer centred it on
50. Once the roster reached 16 countries (spec 08) a hub with 8 authored links
scored **452** against a peripheral consumer's 42 — so the hub drew about
**+5.6 points of annual growth** and the periphery drew none. Nothing anyone did
in play moved that number: the count of links a country was *authored with*
decided the thirty-year economic ranking, and passive hub-trading beat active
economic statecraft for free. Correcting it removed roughly 5 points of unearned
annual growth from well-connected states, which is why absolute grades and GDP
across the whole balance harness fell at the same time (spec 12 §6).

Setting a tariff costs 1 CP. Embargo is applied automatically by Severe-or-above
sanctions and cleared when they are lifted or lapse.

## 4. Sanctions and blowback

Five severities with a damage weight, and blowback at 40% of that weight
(`Sanction.Weight` / `Sanction.Blowback`):

| Severity | Weight | Blowback base |
|---|---|---|
| Routine | 0.35 | 0.14 |
| Pressure | 0.80 | 0.32 |
| Coercive | 1.50 | 0.60 |
| Severe | 2.40 | 0.96 |
| Existential | 3.60 | 1.44 |

Imposing costs 2 CP (`SanctionCost`, reduced by `SkillEffect.CoercionEfficiency`);
lifting costs 1. **Existential measures are gated on a skill**
(`SkillEffect.ExistentialMeasures`, node `ECO_EXISTENTIAL`): without reach into
clearing and insurance the severity is simply unavailable, and
`CanImposeSanctions` reports why so the view can dim the option rather than
offer a button that silently fails.

**Blowback scales with your own exposure to the target:**

```
blowback = Σ  weight × 0.4 × (0.5 + linkVolume/100) × mitigation
mitigation  = 1 − SkillEffect.SanctionPrecision      (player only, floor 0.15)
mitigation ×= 1 − CAP_FINANCE effectiveness × 0.3    (any state, GDD §11)
```

With no trade link at all the exposure factor is 0.4 — sanctioning a state you
do not trade with still costs something, but far less. Sanctioning your largest
trade partner costs roughly three times as much as sanctioning a marginal one;
that asymmetry is the core economic dilemma.

**Coercion never zeroes an economy.** GDP has a floor and the model has no
collapse spiral; economic pressure coerces a government (GDD §20). A test runs a
decade of Existential sanctions and asserts the target still functions.

**Sanctions wind a confrontation up.** `ImposeSanctionsBy` calls
`ConfrontationSystem.AddPressure(state, senderId, targetId, 6)`, which is a no-op
unless the two states are already in an active confrontation together. Coercion
applied to a standoff raises the hidden escalation pressure underneath it, makes
the other side less willing to settle, and can eventually tip the confrontation up
a level without either party choosing to (GDD §18.1). Nobody fires a shot; the
situation gets closer to a war anyway. Spec 01 §5.

### Sanctions lapse — `SanctionReviewMonths = 36`

`AgeSanctions` runs once per resolved month and increments `monthsActive` on
every regime. The player lifts their own measures deliberately, so their
sanctions never lapse on their own. **A foreign government's do**: after 36
months in force, the sender reconsiders, and the regime is dropped unless the
target is still regarded as a threat —

```
stillHostile = relations < 30  OR  threat perceived by the sender > 55
```

Lapsing clears the embargo on the link and announces itself (`SANCTIONS LAPSE`).

This exists because nothing ever lifted an AI's measures and `monthsActive` was
written and never read, so a sanction imposed in year two was still running in
year thirty. Since a sanctioned pair *skips the relations-recovery branch
entirely*, those two states were locked in terminal hostility for the rest of
the save — one AI decision permanently removed a diplomatic partner from the
board.

## 4a. Sanctions détente (GDD §20 amendment)

The trap, in the code's own numbers: sanctions push a pair's relations down
1.2/month while the automatic lapse (§4) requires relations above 30 — a
self-locking cycle with **no verb anywhere to break it**. Measured: a pariah
great power sanctioned 237 of 240 months regardless of conduct, and 40–60
standing AI-AI regimes grinding the world.

`EconomySystem.SeekSanctionsRelief / …By` (player 2 CP; AI 1.5 PC via
`ConsiderDetente`, run outside the objective budget like war management —
living under sanctions is a condition, not a strategy). `ReliefWillingness`
prices what actually moves a sender: regime fatigue (+0.5/month, capped 30),
the sender's **own blowback** (×8 — their cost is the lever), surviving warmth
and trust, minus the threat they still perceive (×0.45) and −20 while the
target is at war. Threshold 50; refusal tells the player which lever is short.

Success lifts the sanction, un-embargoes the link, warms the pair, and sets a
**détente**: `Relationship.sanctionsTruceMonths = 24`, during which
`ImposeSanctionsBy` refuses new measures between the pair — one gate, binding
the player exactly as it binds the AI. Opening a confrontation between the
pair voids the truce (`ConfrontationSystem.BeginBy`). Zero on old saves is
correct; no migration. Covered by `DiplomacySecondActTests`.

## 5. Resources: manpower, energy, materials, food

These live on `NationalResources` and are updated from the economy tick because
they are what the economy actually runs on. Each carries an **authored ceiling**
seeded at world creation (`manpowerBaseline`, `energyEndowment`,
`materialsEndowment`, `foodEndowment`) and lazily backfilled from current values
for saves written before the fields existed.

### `RecoverManpower`

```
manpower floored at 0
if manpower ≥ manpowerBaseline: nothing to do

health = (foodSecurity × 0.5 + stability × 0.5) / 100
rate   = manpowerBaseline × 0.0035 × max(0.25, health)
manpower = min(manpowerBaseline, manpower + rate)
```

About **4.2%/year at full health**, so a war costing a third of the recruitable
base takes the better part of a decade to make good, and a broken, hungry country
recovers at a quarter of that rate. Manpower previously had no path back at all:
a country that fought one war carried the loss for the rest of a decades-long
save, and heavy losses drove the figure negative and displayed it as such.

### Energy and materials approach an endowment

```
energyCeiling  = clamp(energyEndowment
                       + CAP_ENERGY effectiveness × 30
                       + TerritorySystem.EnergySwing, 0, 100)
energyTarget   = sanctionPressure > 0.8 ? energyCeiling × 0.55 : energyCeiling
energyDrift    = clamp((energyTarget − energy) × 0.03, −0.8, +0.35)

materialsCeiling = clamp(materialsEndowment + ..., 0, 100)
materialsTarget  = sanctionPressure > 1.2 ? materialsCeiling × 0.55 : materialsCeiling
materialsDrift   = clamp((materialsTarget − materials) × 0.03, −0.7, +0.25)
```

**Sanctions move the target, never the value** — the food rule (above), ported
back to the two drifts it was copied from. The original flat erosion (−0.8,
floorless) drained *authored energy superpowers* to literal zero under the hot
world's standing sanction regimes: played as Russia (endowment 96), twenty
sanctioned years ended at energy 0, living standards 0, approval 0 — a country
that pumps its own oil starved of it by foreign paperwork. Eleventh instance of
the value-versus-target family, found by playing. The ceiling already loses its
trade component under sanctions (`Supply` checks them), so the ×0.55 on what
remains is disruption of domestic output — painful, with a resting point a
producer can live at (sanctioned Russia now settles near 54).

**The ceiling is the point.** A flat +0.35/month with no ceiling took every
authored energy-poor state to 100 within about nineteen years, so the deliberate
vulnerabilities that distinguish the roster (spec 08) evaporated a few years into
a save and `energyDrag` stopped biting for anyone. A country can invest past its
endowment only through **capability** (`CAP_ENERGY`, worth up to +30) or
**territory** — an energy region you have taken supplies you, and one taken from
you does not (GDD §16). Strategic materials have no capability route today.

**`EnergyCeilingFor(state, country)` and `MaterialsCeilingFor(country)` are public,
and they are the single definition of the ceiling.** Any path that *adds* energy or
materials must be bounded by them, not by a constant of its own — the monthly drift
above and the AI's resource investment (spec 06 §5a) both read these functions for
exactly that reason. The AI's `SecureResources` previously added energy with no
ceiling at all, which silently repealed this section for every non-player state
while leaving the code above it looking correct. A second definition of a ceiling
is a repeal of the first.

### Food security moves the same way

`foodSecurity` was written once at world creation and never moved again — the
last authored stat with no monthly behaviour at all. Now:

```
foodCeiling = clamp(foodEndowment + TradeSystem.Supply(country, Food), 0, 100)
foodTarget  = foodCeiling
              × (atWar ? 0.75 : 1)              // harvests run badly, not never
              × (sanctionPressure > 1.0 ? 0.5 : 1, whichever is lower)
foodDrift   = clamp((foodTarget − food) × 0.03, −0.60, +0.30)
```

- **`TradeFocus.Food`** (appended to the enum so stored ordinals survive) makes
  grain a negotiable commodity like energy and materials: `Supply` reads the
  partner's own `foodSecurity`, `CostToPartner` prices selling what they are
  short of, and food imports press on the **Agriculture** sector through
  `ImportDisplacement`, completing that switch.
- **Pressure moves the target, never the value — for every source.** War
  depresses food toward 75% of the ceiling, heavy sanctions toward 50%
  (siege-level hardship with a real resting point), and the same proportional
  drift brings it home when the pressure lifts, for every country. The first
  version drained flat rates instead; measured on seed 1212, a passive great
  power spends 237 of 240 months under sanctions, so its food ground from 90 to
  literal zero and unrest pinned at the cap (the one-way-value family, ninth
  instance; caught by `NoSocialValueRunsAwayInEitherDirection`). Every level of
  hardship has somewhere to settle; only the causes decide where.
- **What hunger does** lives in the social layer (spec 05 §4), measured as a
  **drop below the country's own endowment**, never an absolute line:
  living-standards target −0.5/point of `(foodEndowment − food − 5)`, unrest
  pressure +0.45/point of `(foodEndowment − food − 10)`, both floored at zero.
  An absolute threshold read Saudi Arabia's authored food 18 as a standing
  humanitarian crisis and rippled phantom unrest through the measured world; a
  state authored food-poor has adapted, and what starves people is collapse
  relative to its own normal — which war, siege and sanctions genuinely cause.
  Zero by construction at every authored baseline. Hunger also still scales
  `RecoverManpower`.
- **The starting world authors three food dependencies** (the first authored
  *focused* links anywhere — the Ramstein lesson): AUS→JPN, USA→KOR, IND→SAU.
  Saudi Arabia's food 18 against an energy 100 is the designed mirror of the
  energy-dependent archetypes: rich, capable, and fed by ships.

`FoodCeilingFor` is public and is the single definition of the ceiling, same
rule as energy and materials. Covered by `FoodSecurityTests`.

### Industrial capacity

Two inputs, both drifts rather than jumps:

```
industrialCapacity = Growth.Apply(industrialCapacity, CAP_ADVMFG effectiveness × 0.12)
industrialCapacity → approach(industrialCapacity + IndustrySwing, rate 0.05)
```

Capability compounds slowly into real capacity (GDD §11) — it unlocks the ability
to build, it does not hand over the result. Territory is applied as a slow drift
so seizing a works does not teleport its output home the month it falls.
`MilitarySystem`'s procurement adds a third input (spec 01 §2).

## 5a. The treasury trend readout

`GameState.treasuryTrend` — the player's smoothed month-over-month treasury
delta (EWMA ×0.8/0.2, ~5-month memory), written at a fixed point in
`EconomySystem.MonthlyUpdate` so deltas are comparable, lazily seeded
(`treasuryTrendSeeded`; old saves start reading a month after load). **A
readout, not a rule** — only the briefing and `AttentionSystem` consume it.

Playtested into existence: deficit spending punishes on a lag of *years*
(debt → confidence → markets → living standards), and a 20-year test campaign
bankrupted a healthy country to −4,905 without the game ever saying "we spend
more than we make." The briefing's TREASURY row now carries `(−82/MO)`, grows a
plain-language warning when the trend runs below −4 with under 36 months of
runway, and `AttentionSystem` raises Information at that threshold — Decision
once the account is dry and still sinking. Deliberately quiet otherwise: a
surplus, a trivial drift, or a nine-decade runway raises nothing (the
cried-wolf rule, applied on day one). Covered in `EconomySystemTests`.

## 6. National Market Index (GDD §20.1)

Baseline 100 at world creation. **Not a compounding series** — an earlier build
compounded to 1192 in a decade and made the chart useless.

```
fundamentals = 45 + confidence × 0.85 + growth × 7
             − max(0, inflation − 4) × 3.5
             − sanctionPressure × 9 − blowback × 4
             − 12 if at war
fundamentals clamped [8, 260]

index += (fundamentals − index) × 0.14,  floor 5
```

**There is no separate ongoing shock term, and one must not be reintroduced.**
Sanctions and war are already priced into `fundamentals` — they appear in it
directly and again through `confidence` and `growth`. An earlier build applied a
further monthly `−sanctionPressure × 1.2 − 0.9 if at war` on top, which is not a
shock at all but a permanent subtraction: it dragged the index *below its own
clamped floor* and kept it there, so a sanctioned economy's chart went to the
bottom of the axis and stayed flat instead of falling and settling at a new
level. Fundamentals move sharply on their own when conditions change; the 0.14
reversion is what makes that read as sentiment catching up.

History is capped at 60 entries (`EconomyState.MaxHistory`) and rendered by
`AsciiChart.LineChart` in the ECONOMY view.

## 7. Extension points

- **Specific dependencies** (oil, grain, semiconductors) — GDD §10 says these
  appear "when strategically relevant" beneath the headline resources. The natural
  hook is a per-country list of critical inputs checked against trade links.
  **PLANNED.**
- **Investment / resource contracts / emergency deals** — GDD §20 lists these as
  tools; only tariffs, embargoes and sanctions exist today. **PLANNED.**
- **A materials capability.** Energy has `CAP_ENERGY` to invest past its
  endowment; strategic materials have no equivalent, so a materials-poor state
  has no route out at all. **PLANNED.**

## 8. Open questions

- Economic play now grades **highest** of the six harness playstyles (3.18
  against passive's 2.40, spec 12 §6), largely on initiative: sanctions, tariffs
  and lifts are cheap, repeatable and each records a decision. It also earns
  roughly **4× the XP** of military play for the same reason. Harmless while XP
  only drives the level display; worth a per-action-type cap if XP ever gains
  mechanical weight.
- The sixteen-country roster gives coercion the asymmetric, low-exposure targets
  the four-country slice never had, which is what closed the old "sanctions cost
  about what they deliver" gap. The opposite question is now the live one:
  whether repeatable low-cost measures should have diminishing returns against
  the same target.
- `foodSecurity` feeds `RecoverManpower` and the Agriculture sector, but is
  written once at world creation and never changes. A country cannot starve and
  cannot feed itself better.
- Treasury income is a flat function of GDP and debt. A real budget (revenue,
  expenditure lines, deficit choices) would give the Economy pillar more verbs.

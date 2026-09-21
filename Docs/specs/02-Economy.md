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

outputTarget = output
               + (SectorAnchor − output) × CapacityReversion   (0.06)
               + growth × 0.08 − sanctionPressure × 0.35 − importDisplacement
               + 0.5  (Defense, at war)
output approaches at 0.5, clamped 0..100
```

### Capacity reverts to the nation's fundamentals (2026-08-28)

`SectorAnchor(country, sector)` is the level capacity returns toward, and it is
**`WorldFactory`'s authored baseline, shared rather than retyped** — Energy to
national energy, Agriculture to food security, Industry to industrial capacity,
Defense to `military × 0.8`, the rest to `pillars.economy`. Every one of those
has its own recovery path, so capacity inherits one instead of having none.

Zero by construction at world creation: at month zero output *is* this value
(±the authoring jitter), so the term contributes nothing to a healthy country.

**Why it exists.** The line read `outputTarget = output + …`, which is not a
target at all: `Approach(v, v + d, 0.5)` is `v + 0.5d`, an accumulating rate
wearing a target's clothes, with no anchor and no restoring force. Because
growth is *derived* from capacity (`SectorStrength` feeds `targetGrowth`), zero
was an absorbing state — output 0 gives deep negative growth, which drives
output further down.

Measured on seed 1212: six of the seven US sectors sat at exactly 0.0 output
with **healthy** sector health, a market index of 7, permanent −4%/yr growth,
and no recovery across thirty-six isolated economy ticks. Everything downstream
followed from it — unemployment 24, living standards 0, unrest and grievance
pinned at 100, four coups and recurring civil conflict. **The whole collapse was
this one line.** Fourteenth instance of the value-versus-target family, and the
one sitting under the entire economy.

### `StagnationFloor` — how far a depression can erode capability

`StagnationDrag` subtracts from `pillars.economy` every month. It had no floor,
while `SectorAnchor` reads that pillar for Technology, Finance and Consumer — so
it closed the same loop one level up. Its own comment claimed recovery was
"reachable the month growth turns positive", which cannot happen once the pillar
is gone. Measured at 0.27/month (≈3.2 a year, against the ~1.4 the comment
estimated from milder figures): a great power's entire economic capability
inside twenty years.

```
StagnationFloor = 12 + industrialCapacity × 0.35
```

Anchored to the physical base the country still holds rather than to a bare
constant — a state whose plant survives keeps the capability to use it. It is
**never a bound on how bad the economy gets**: growth, confidence, employment
and the market index still collapse in full and the crisis regime is unchanged.
It bounds only how much long-run *capability* a downturn takes with it.

The floor **only stops the drag taking; it never gives.** Written as a bare
`Math.Max(floor, value − stagnation)` it lifted any pillar already below the
floor for some other reason — a regression test that drives `pillars.economy = 5`
to force a contraction had it silently raised to 36 on the first tick, and the
recession it was measuring never happened.

### The invariant that guards all of this

`WorldStructureTests.NoSocialValueRunsAwayInEitherDirection` no longer asserts
that no social value reaches its ceiling. These values are target-driven, so a
country whose market has collapsed *should* read unrest 100 — that is the model
describing a real condition, and forbidding it would forbid the crisis regime
the economy was given on purpose. **A ceiling is not a ratchet.**

It now asserts **recoverability**: lift every sanction, war and rising, hold them
off for ten years, and the country has to climb out. Measured after these fixes
on seed 1212 — unrest 100 → 58.8, living standards 0 → 17.5, index 8 → 78,
`pillars.economy` 29 → 51, growth −2.97 → +1.60. Before them it stayed at 0/100,
so all three bugs above fail the new test and none of them was visible to the
old one.

The pressures are held off **every month**, not cleared once: a wrecked great
power in a hot world is a target, and clearing them a single time measures how
long the neighbours take to open the next war rather than whether a collapse can
be recovered from.

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
sanctions. Ending one regime clears it only if no Severe-or-above regime
remains on that pair in either direction. Ordinary lifting, negotiated relief,
monthly lapse, a leverage exchange and either peace term share
`EconomySystem.RemoveSanction` after their own gates. The helper removes only
the named record and reconciles that pair's flag; no cost, reward, notice or
other regime is changed by it. Removing an absent record is inert.

**The full embargo and commodity closure are distinct.** Routine, Pressure and
Coercive regimes still block commodity `Supply` in either direction but do not
impose a full trade embargo; lifting the last severe regime while one of those
remains restores the existing general-trade readers, not commodity supply.
This correction preserves that severity rule rather than turning every sanction
into a full embargo. No save field or migration is added. It fixes removal-time
state; it does not retroactively repair an already-cleared flag in an old save.

## 3a. Visible national projects — industrial first slice (#24)

`IndustrialSystem.ProjectName` and `ProjectProgress`, surfaced under NATIONAL
PROJECTS in ECONOMY, reuse `IndustrialProgramme`'s sector, scale, start date and
remaining months. Names are descriptive (`National Energy Works / Expansion /
JAN 1984`), not unique identifiers or user-authored names; restarting the same
sector/scale in the same month can repeat one. An absent legacy start month reads
START DATE UNKNOWN without backfilling the save. "Works" labels sector investment,
not a newly simulated geographical site.

Progress is **funded work**, total authored duration minus remaining months,
not calendar age. Remaining commitment is months × current monthly cost, labelled
AT CURRENT TERMS rather than money already spent or a guaranteed completion date.
The next-instalment reading compares the treasury now with one instalment; it
explicitly warns that income and other commitments can change it before work
resolves. No extra forecast, charge, pause, grace period or automatic financing.

The existing rules remain: 2 CP to begin, at most three programmes and one per
sector, monthly payments, no partial completion yield, cancellation without a
refund, and lapse when the next instalment cannot be paid. Maintenance/Expansion/
Modernisation retain 12/24/36 months and 95/190/340 per month. Effects and rewards
are unchanged. The existing STOP action and its save timing are unchanged: it
does not itself autosave; the cancelled state and record persist on the next
normal save. Beginning still uses the controller's autosave.

The existing chronicle now records PROJECT BEGUN, CANCELLED and LAPSED as secret
economic entries for the actor, once at the transition. Completion keeps its
existing public entry and notification count, now named. The player's completion
receipt reports applied sector-output/health, industrial/energy-endowment and
Economy-pillar changes after clamping, rounded to at most two decimal places.
Foreign public completion remains descriptive: it never publishes the hidden
applied figures. No extra upkeep is implied by the receipt.

The project panel shows the latest five matching entries for our country, newest
first; older entries remain in the existing history. Legacy generic completion
entries are not rewritten or guessed into project records. Readout uses the
terminal text policy and does not change state. No new field, schema version,
pipeline, catalogue entry, RNG call or funding/effect arithmetic.

**#24 scope reconciliation:** research presentation is implemented in spec 13,
energy-site construction in §3b below, and procurement presentation in spec 01
§2b. Bespoke additional types and custom persistent names are expansions, not
automatically missing requirements. Active construction-policy measurement and
device confirmation remain separate gates; this historical first slice is not
by itself a completion claim.

## 3b. Energy works tied to a site (#24)

Refused BUILD controls use the shared `Block` / `UNAVAILABLE` presentation so
their existing core refusal reason is available without hovering.

The first physical project reuses `IndustrialProgramme`, with an optional
`locationId`. Empty means the existing national investment, unchanged. The new
order is `BeginEnergySiteProject`: select an owned EnergyRegion, spend 2 CP under
Economy authority, and fund 12 months at 95/month (1140 total). It shares the
three-programme capacity and the one-Energy-programme slot, including national
Energy work. A completed site cannot be ordered again. Invalid, contested,
already-built and unavailable sites refuse before spending; affordability still
uses the ordinary command-point rule. Actor-generic `BeginSiteBy` skips operator
CP and authorization rewards but has the same eligibility and monthly funding. No AI caller
is added in this slice.

On each industrial tick, resolve site availability before charging. Missing,
wrong-type or differently owned sites **abandon** the project, with a single
secret PROJECT ABANDONED record and player notice; no further payment, benefits
or refund. Ownership is observed at resolution, not recorded as an event stream.
An owned site denied by `InsurgencySystem.Denies` **pauses**: no charge, no progress,
no repeated record, and no funding lapse even if treasury is empty. Once denial
ends, work resumes from its remaining funded months; then ordinary failure to
pay lapses it. Cancellation retains the existing no-refund and next-save timing.
Pausing continues to occupy the queue slot.

Completion sets `StrategicLocation.energyWorks` once. Each owned, non-denied
EnergyRegion with works adds **7** to `TerritorySystem.EnergySwing`, hence the
energy ceiling, before the existing 0..100 national clamp. It does not refill
energy instantly or grant national sector, endowment or pillar bonuses. The
economy tick precedes industry, so monthly resource recovery sees newly finished
work on the next economy tick. The receipt records the actual immediate ceiling
change, which can be smaller than seven or zero at the cap. Completion retains
the existing 30 XP; authorization retains 14 XP and one initiative, subject to
normal repetition rules. Foreign completion is descriptive, without hidden
applied figures or player rewards/notices.

Works stay on the location after capture, return, annexation or secession: the
current owner receives the contribution and the former owner loses it. Denial
suppresses it for everyone until control returns; it does not erase the works.
The original strategicValue is unchanged, so home construction cannot cancel
itself out in the held-minus-original calculation. No extra upkeep, damage,
demolition, upgrade or repeat-build loop is introduced. Seven points is initial
tuning, not certified multi-seed policy balance.

ECONOMY names every owned energy site, its state, funding terms and failure
rules; each BUILD button captures that site's id. Full names sit outside the
short button. Own-country MAP installations share the readout, but foreign
construction and exact operating contributions are not revealed there. Existing
project history carries site names at the event, falling back to id if a site
has disappeared. Names are descriptive, not new persistent identities.

Save version remains 7: missing locationId is national work and missing
energyWorks is false. Both non-default values survive reload; no migration or
retroactive development is required. Native Unity, hardware, new AI behavior and
broader physical project types remain separate work. #24 is not declared complete.

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

Lapsing reconciles the pair's embargo as in §3 and announces itself (`SANCTIONS LAPSE`).

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

Success lifts the sanction, reconciles the link's embargo (§3), warms the pair, and sets a
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

### Industrial capacity approaches an endowment (2026-09)

The fourth resource to get the idiom, and the last:

```
industryCeiling = clamp(industrialEndowment + TerritorySystem.IndustrySwing, 0, 100)
industrialCapacity += clamp((industryCeiling − industrialCapacity) × 0.03, −0.4, +0.25)
```

`NationalResources.industrialEndowment` is authored (`profile.industry`) and
**seeded from the authored profile** on the first tick of a save that predates
it — not from the current value like the other three, because this one arrives
after measured decades in which the bug below ran countries to zero, and
"current value" would enshrine the damage the endowment exists to repair
(`EconomySystem.EnsureIndustrialEndowment`; a state with no profile seeds from
what it has). Additive field, no version bump.

**What it replaces.** `approach(capacity, capacity + IndustrySwing, 0.05)` was
not a target but an accumulating *rate* wearing a target's clothes — the
sector-capacity bug one level up. A works lost, or in revolt
(`InsurgencySystem.Denies`), subtracted 5% of its value every month with nothing
to stop at: measured on seed 1212, one contested industrial centre took India
from 58 to literal zero in six years and held it there, and a lost works ran
China from 92 to 1 in Unity. `StagnationFloor` is anchored on this figure, so
the economy pillar followed it down to 12 and a country that had lost one
province could never grow again — the absorbing state under
`NoSocialValueRunsAwayInEitherDirection`'s relief arm for CHN, IND and MEX. And
plant destroyed by bombing, sabotage or a civil conflict had **no recovery path
for a non-player state**: programmes are the operator's, procurement and
research need a treasury the collapse has emptied.

**Builders raise the endowment too.** `EconomySystem.BuildIndustry(country,
amount)` applies `Growth.Apply` to the value and adds the same gain to the
endowment, so what was built is not taken back by the next month's drift. Its
five callers: industrial programmes (Industry × 0.45, Technology × 0.20),
procurement (`strengthPerMonth × 0.25`), a matured `CAP_ADVMFG` (× 0.12 a
month), arrivals put to work (`hosted × 0.010`), and mobilisation (+0.3).
Damage writes the value alone and heals toward the ceiling; a strategic
instrument's destruction (−30) takes the endowment with it, so it stays the
permanent loss it was. Tests: `EconomySystemTests.LosingAWorksCostsWhatItWasWorth…`,
`PlantDestroyedByAShockRegrowsTowardWhatTheCountryCanHold`,
`WhatIsBuiltRaisesWhatTheCountryCanHold`, `AnOldSaveSeedsTheIndustrialEndowment…`.

### A breakaway is born with an economy (2026-09)

`SecessionSystem.MakeSuccessor` used to hand a successor 30% of its parent's
economy pillar and industrial capacity, no sectors, a fifth of the parent's
*overdraft*, the parent's energy and materials endowments with the levels left
at zero, and food security with no food endowment. Measured in Unity (seed 1212):
born at pillar 6, industry 6, treasury −1,576, growth −1.6 for twenty isolated
years. Now: the pillar is born at `StagnationFloor` at the least (the floor
never gives, so a state born under it stayed there), industry at
`MinimumBirthIndustry` (10) at the least, the seven sectors at their anchors
with the parent's sector health, a share of the account only when it is in
credit (the debt stays with the rump; a successor is born owing nothing), every
resource *level* and endowment copied from the parent — including the
industrial endowment, so the breakaway's plant regrows toward what the ground
can hold. Still by design: a third of the parent's *capability*, which the
growth formula's structural term reads as a contraction until a ministry rebuilds
it; the isolated arm (no ministry) shows industry regrowing, the contraction
easing, and no crisis regime (`ABreakawayIsBornWithAnEconomyItCanRecoverOn`).

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

## 9. Public finance (spec 25 Tranche A, 2026-08-28)

**Status: as-built.** `Data/FiscalState.cs`, `Core/FiscalSystem.cs`,
`Tests/EditMode/FiscalTests.cs`, the PUBLIC FINANCE panel in ECONOMY.

The pillar had six operator verbs and **no instrument of public finance at
all**: no tax, no budget, no borrowing, no reserves, no credit standing.
`debtToGdp` was written in one place, read in three, and moved by nothing the
operator could do. That is also why the playtest's fiscal finding could only be
recorded as an open question — a belligerent mid-tier state ended a decade
several thousand in the red and there was no lever to answer with.

### The three rules the layer rests on

**1. The authored defaults are revenue-neutral by construction.**
`TaxMultiplier` and `PostureIncomeMultiplier` are exactly 1.0 at
`BaselineTaxRate = 35` and `BudgetPosture.Balanced`, and `TaxGrowthDrag` /
`TaxApprovalDrag` are exactly 0. An untouched world raises precisely what it
raised before this existed, so the measured balance table stays comparable.
`FiscalTests.TheAuthoredDefaultsAreRevenueNeutral` asserts it for every country
— without that, a "harmless" addition is a silent rebalance.

**2. A deficit finances itself into debt.** Governments do not stop paying the
army because the account is empty. Before this, treasury went negative and
*nothing happened*, which is why "several thousand in the red" was a number
rather than a consequence. Now the shortfall is added to `sovereignDebt` and the
treasury floors at zero; a surplus above `SurplusBuffer` retires 10% of the
stock a month, so the stock is not one-way.

**3. `debtToGdp` is derived, never stored.** It used to be a free-floating
accumulator (+0.9/month at war, +0.15 otherwise, minus growth) answering to no
money anyone spent. `FiscalState.sovereignDebt` is now the authority and
`FiscalSystem` recomputes the ratio each month, so the six existing readers keep
working — the `BranchForce.strength` discipline: a mirror, never a competitor.

### Constants

```
BaselineTaxRate      35      revenue-neutral
TaxMultiplier        0.5 + taxRate/70            (1.0 at baseline)
TaxGrowthDrag        (taxRate − 35) × 0.030      annualized growth points
TaxApprovalDrag      (taxRate − 35) × 0.060

posture              income   growth   living standards
  Balanced            1.00     +0.0      +0
  Austerity           1.14     −0.7      −5
  Expansionary        0.84     +0.8      +4

MonthlyInterestRate  0.0018 + (100 − credit)/100 × 0.0042
                     → 2.2%/yr at credit 100, 7.2%/yr at credit 0
CreditTarget         78 − max(0, debtToGdp − 55) × 0.55 + growth × 2.2
                        + (confidence − 50) × 0.16 − 9 (at war)
                        − 34 × (restructuring memory remaining)
IssueShareOfGdp      0.12    per issue, and −6 credit on asking
MinimumCreditToIssue 18      below this nobody lends
IssueDebtCeiling     200%    of GDP
RestructuringMemory  60 months
```

Austerity is not extra revenue in reality — it is spending forgone — but the
simulation has no general outlay model, so the net effect on the treasury is the
honest equivalent and is documented as such rather than dressed up.

### Reserves

`ReserveFloorBonus` (0..0.35) raises the floor that sanctions and war can push
the energy, materials and food **targets** to, and the reserve depletes while it
is doing that work. Pressure moves the target, never the value — the rule this
file has now applied to food, energy, materials and sector capacity. A reserve
nobody needs keeps; one under real pressure drains at 0.6/month.

### The AI half

`AISystem.ManageTheBooks`, **outside the objective budget** — the
`ConsiderDetente` and routine-restocking precedent. Funding the state is
governance, not a strategy competing with starting a war for this month's
actions; put it in the action cut and it goes silent the moment the world gets
busy, which is exactly how no foreign government ordered equipment for an entire
measured decade. A government borrows below `DiscretionaryReserve`, stockpiles
what it is short of when flush, restructures when the debt has run away, and
reviews its posture at 4%/month so it does not re-plan the budget every month.

### The trend readout had to change with it

`treasuryTrend` exists because deficit consequences arrive years late, and it is
the operator's only timely warning. Financing the deficit **stopped the treasury
falling**, so a government living entirely on borrowed money read as roughly
breaking even — a forced 300/month drain reported −93. It now measures the
*fiscal balance*: the treasury delta less the debt taken on, sampled at the end
of `FiscalSystem` rather than mid-month in `EconomySystem`.

**The general hazard, worth remembering: when you give a value a recovery path,
check what was reading its absence.** The same change that fixes a leak can
blind the instrument that detected it.

### Save

`SaveVersion` 6 → **7**. A zeroed `FiscalState` is *wrong* rather than empty: it
would silently forgive whatever debt the save was already carrying, since
`debtToGdp` was a real number in those saves and is now derived from a stock
that would not exist. The step seeds the stock from the save's own ratio and
GDP, so a migrated world lands where it stood and migrating twice gives the same
answer.

## 9a. Fiscal condition, arrears, and the sanction cause (core stability repair, 2026-09)

The audit measured every unattended 40-year world converging on sovereign debt
at 300–700% of GDP, a sanction count that only grew, ~100 coups and 13–15 of 16
states ruined. Three things in this pillar were the loop's edges.

### Deficits answer to creditworthiness

The automatic deficit financing in `FiscalSystem.Tick` had no gate at all — the
voluntary verb answered to `MinimumCreditToIssue` (18) and a 200% ceiling, the
deficit did not. Now the deficit is financed only while `creditStanding >=
MinimumCreditToIssue`; below it the shortfall is **arrears**: the account stays
negative, `FiscalState.arrearsMonths` counts, and confidence's target carries
`ArrearsConfidenceDrag` (12) while it lasts. `FiscalState.deficitFinancedMonths`
counts consecutive financed months. Both fields are additive, zero on old saves,
no version bump.

**And only up to the ceiling** (2026-09). The credit gate was applied to the
automatic path and `IssueDebtCeiling` (200%) was not, so the deficit had a
second, looser definition of what the market will absorb. Measured on seed
6301: Turkey, shut out for three years of war, accrued a hole of −4,633, and
the month its standing crossed the issuing line the whole of it was borrowed at
once — 88% → 400% of GDP in one tick, standing back to zero, shut out again — a
ratchet with the five-year period of the restructuring memory. Financing now
stops at the ceiling; whatever the market will not absorb stays arrears, where
a default can reach it. **A default settles the arrears as well as the stock**:
`RestructureDebtBy` floors the account at zero, because a write-down that left
the hole standing kept the creditors unpaid through the whole memory and set up
the same lump the month credit returned (measured on a 400% case: three
write-downs in twenty years, arrears counted to 233 months, the ratio back to
400 the moment credit recovered). The reputational price is unchanged.

### One fiscal condition

`FiscalSystem.ConditionOf` is the only definition of solvency, read by the
annual grade (`ProgressionSystem.SolvencyPenalty`), the `Solvent` mandate
objective, the AI's books and the operator's desk:

| `FiscalCondition` | Meaning |
|---|---|
| `Sound` | balanced or in surplus, nothing borrowed lately |
| `CashNegativeButCreditworthy` | a financed shortfall this month or last |
| `DeficitFinanced` | ≥ 6 consecutive financed months, or ≥ 3 at ≥ 80% of GDP |
| `DebtStressed` | ≥ 120% of GDP, or credit < 35, or shallow arrears |
| `Crisis` | deep arrears (> ¼ year's income), shut out *and* in arrears, or restructured within the last year |

A heavy debt nobody will lend into, with the account in the black, is
`DebtStressed` rather than `Crisis` — the ordinary budget's problem, not the
emergency programme's. `SolvencyPenalty` ranks the profiles (0 / ≥2 / 6–12 /
12–18 / 20); a posting three times its GDP in debt with a zeroed balance used to
grade penalty-free. `MandateObjectiveKind.Solvent` is met at
`CashNegativeButCreditworthy` or better — it was a free tick in five authored
mandates.

### The finance ministry answers a crisis

`FiscalSystem.SteadyTheBooks` runs from the fiscal tick for every non-player
state and from the operator's **autonomous** Economy desk (`CabinetSystem`;
a directed desk leaves the budget to the operator who is directing it). At
`DebtStressed` or worse: austerity where the economy can bear it, the tax rate
stepped (`CrisisTaxStep` 2/month) toward `CrisisTaxCeiling` 46 — or
`DepressionTaxCeiling` 40 — and a write-down once arrears exceed
`DefaultArrearsShare` (½ a year's income) or a state shut out of credit sits
above `DefaultRatio` (200% of GDP). **Austerity is not prescribed into a
depression** (`DepressionLine`: market index 55): the first version cut spending
whatever the economy was doing and a ruined state never left crisis — the cuts
held growth at −4% a year, the shrinking economy raised the ratio through the
denominator, the ratio kept the state in crisis, and the crisis kept the cuts;
twenty measured years of it. `AusterityAdvisable` is shared with the AI's
budget review, and the AI now also moves its tax rate (`SetTaxRateBy` had no AI
caller at all). The isolated recovery arm of `NoSocialValueRunsAwayInEitherDirection`
climbs from index 8 to 78 and debt 149% to zero over twenty years with these in
place; re-measured after the ceiling and the endowment (2026-09, harness, seed
1212, India entering at index 8, debt 66%, credit 12, −332 in arrears): `Sound`
by year 3, debt zero by year 5, index 95 and living standards 42 by year 20.

### Sanctions: a cause, a chill, a lapse

`EconomySystem.SanctionCauseStands` is the one definition of "hostile enough to
sanction": the pair is at war, or the sender's relations with the target are
below `SanctionHostilityLine` (30). The AI's `CounterRival` imposes only while it
stands (at 20% a month, was 35%), and `AgeSanctions` lifts a regime at its
36-month review when it does not. Threat perception is deliberately not part of
it: it tracks capability and never fades, so measures against any strong state
could never lapse. `Sanction.cause` (RIVALRY / REPUDIATION / CRISIS / PLAYER)
is recorded for the long-run probe and read by nothing else.

The self-lock is broken on the diplomacy side (spec 04 §9b): a sanctioned pair's
relations settle `SanctionChill` (8) below alignment as a target, instead of
draining 1.2 a month to zero and being excluded from the recovery branch.

`EconomySystem.WouldGrantRelief` / `ReliefMargin`: a request for relief is an
early review — a foreign sender whose cause is gone lifts when asked (unless it
still regards the target as a major threat, `ReliefFearLine` 55), and the
player's own measures are never lifted by a computed rule. `AssessRelief` is the
assessment layer over it, gated on the target's *Diplomatic* reporting on the
sender, and `AISystem.ConsiderDetente` screens through it rather than through
the sender's acceptance function.

**Measured after (40 years, seeds 5171 / 1212 / 9090):** ruined states 3 / 3 / 3
of 16 (was 15 / 14 / 13), mean market index 85–97 (was 16–27), sanctions 28–34
(was 72–99), coups 24–48 (was 95–114), median debt 0% with a maximum of
246–333% (was 340–692% mean). The count still creeps; the residual regimes are
RIVALRY ones on pairs that stay genuinely cold, which is the intended reading.
Probe: `Tools/Stability.cs`.

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

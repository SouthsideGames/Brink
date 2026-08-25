# 08 — Country Bible

Source: `Data/WorldFactory.cs`, `Data/CountryState.cs`, `Data/NationalResources.cs`,
`Data/Government.cs`, `Data/StrategicLocation.cs`, `Data/TradeAndSanctions.cs`.
GDD §31.1, §31.2, §10, §16, §19.

## 1. Framing rule — read this first

The roster uses **real countries** (a decision that supersedes the GDD's original
fictional-world direction, §2/§31.1). That carries obligations:

- **Starting values are gameplay archetype baselines, not claims about real
  capability.** The class comment on `CountryProfile` says so, and so should any
  new entry. They exist to make each nation *play* distinctly, and they are
  balanced, not researched.
- **Government types are structural descriptions, not judgments.** Use the
  institutional vocabulary in `GovernmentType` (`DominantPartyState`,
  `CentralizedRepublic`) rather than evaluative labels; spec 05 explains what each
  changes mechanically.
- **Do not model live real-world territorial disputes.** The one flashpoint is a
  deliberately generic "Contested Sea Lane". Naming a real disputed territory
  invites needless controversy and creates regional app-store problems.
- Keep leader and official names **generated** from the profile's name pools.
  Never depict a real living official.

## 2. Authoring format

A country is one `CountryProfile` entry in the `WorldFactory.Profiles` array.
Every field, what it actually drives, and the range the shipped roster uses:

| Field | Type | Drives | Authored range |
|---|---|---|---|
| `id` | 3-letter string | Save key, every cross-reference (locations, trade, posture, relationships) | Stable forever — changing one orphans saves |
| `displayName` | string | All terminal output | — |
| `military` | 0..100 | Branch strengths, threat perception seeding, Defense sector output | 30–84 |
| `economy` | 0..100 | Starting GDP, readiness target, four of seven sector outputs | 40–88 |
| `intelligence` | 0..100 | Counterintelligence baseline; estimate quality against others | 34–86 |
| `diplomacy` | 0..100 | Treaty and coalition weight | 48–78 |
| `government` | 0..100 | Branch readiness, political resilience | 38–78 |
| `treasury` | absolute | Money. Not 0..100 — it is spent and earned | 400–2400 |
| `manpower` | absolute | Recruitable base; also copied to `manpowerBaseline` un-jittered | 220–1800 |
| `energy` | 0..100 | Energy security; also copied to `energyEndowment` | 18–100 |
| `industry` | 0..100 | Industrial capacity → supply ceiling, procurement, Industry sector | 32–92 |
| `materials` | 0..100 | Strategic materials; also copied to `materialsEndowment` | 24–92 |
| `food` | 0..100 | Food security, Agriculture sector output | 18–94 |
| `approval` | 0..100 | Starting government approval | 42–62 |
| `stability` | 0..100 | Starting stability; feeds coup and crisis eligibility | 36–78 |
| `unity` | 0..100 | Starting national unity | 34–72 |
| `governmentType` | enum | How power works — succession, term limits, emergency cost (spec 05) | All five types must stay represented |
| `termLengthMonths` | int | Election cadence; `0` for non-elective, which falls back to 48 in `GovernmentState` | 36–72, or 0 |
| `mapX`, `mapY` | int | Marker position on the 78×21 ASCII world map **and the country's actual position in the simulation** — `GeographySystem` measures every distance, projection range and operation reach factor from these two numbers (spec 01 §3b) | `0..77` / `0..20` |
| `mapCode` | 2-char string | The letters drawn on the map and used as a filter button label | Exactly 2 characters |
| `firstNames` / `lastNames` | 8 uppercase entries each | Generated leader **and Cabinet** names, for this country's own government | 8 each |
| `officeTitles` | exactly 5 | Cabinet titles in pillar order: Military, Economy, Intelligence, Diplomacy, Government | 5, country-appropriate |

`mapX`/`mapY` used to be presentation only, which made them the cheapest field in
the table to author carelessly. They are now geography: longitude wraps at
`GeographySystem.MapWidth` (**78**, matching the authored `0..77` range) so the
grid is a cylinder, and latitude is weighted ×2 because a terminal cell is about
twice as tall as it is wide. Place a new country where it belongs relative to its
trade partners and its rivals, not where there is room on the map.

`GeographySystem.MapWidth` is the one authority on that width;
`AsciiWorldMap.Width` derives from it. The renderer and the distance model must
agree, or the chart the operator reasons from is not the world the simulation
measures (spec 01 §3b).

`firstNames`, `lastNames` and `officeTitles` were near-cosmetic while only the
player's nation had a Cabinet — for the other fifteen they named one leader every
few decades. Every country now appoints five officials from its own pools at world
creation, at every administration change and after every coup (spec 15 §2), and
those officials are visible to the player through collection (spec 15 §17a). A
thin or generic name pool is now something a player can read on screen. Eight
entries each is the floor, not the target: `MakeCabinetFor` rejects duplicate
names within a cabinet, so a pool of eight and eight has to supply five distinct
pairings.

`energyEndowment` and `materialsEndowment` are the reason a profile keeps its
character over a fifty-year save: recovery in `EconomySystem` approaches the
authored endowment rather than climbing toward 100 for everyone, so an
energy-poor archetype stays energy-poor.

### 2a. Jitter — no two saves open identically

`MakeCountry` perturbs the authored values so the opening position is familiar
but not memorised:

```
pillars, energy, industry, materials, food   ±4 / ±5, clamped 0..100
treasury, manpower                           × 0.92 .. 1.08
manpowerBaseline, energyEndowment,
materialsEndowment                           authored value exactly, no jitter
```

Author well clear of any threshold you care about — the roster tests below are
run against a jittered world.

### 2b. Derived — do not hand-author

Everything here is computed from the profile. Adding hand-authored versions of
these fields would let a country's parts disagree with its identity.

| Derived | Formula |
|---|---|
| Ground strength | `military × 0.95` ±5 |
| Air strength | `military × 0.90` ±5 |
| Naval strength | `military × 0.75` ±6 |
| Ground/air readiness | `55 + government × 0.15` ±6 |
| Naval readiness | `50 + government × 0.15` ±6 |
| Ground/air supply | `60 + industry × 0.25` ±6 |
| Naval supply | `58 + industry × 0.25` ±6 |
| War support | `45` ±6 (identical for everyone — nobody starts mobilised) |
| Counterintelligence | `25 + intelligence × 0.45` ±4 |
| GDP | `600 + economy × 22 + U(−80, 80)` |
| Growth / inflation / unemployment | `U(0.8, 3.4)` / `U(1.5, 4.5)` / `U(4, 9)` |
| Debt-to-GDP / confidence | `U(28, 75)` / `U(45, 68)` |
| Market index | `100` for everyone, one history point recorded |
| Sector output | Energy←`energy`, Agriculture←`food`, Industry←`industry`, Defense←`military × 0.8`, all others←`economy`; each `+U(−8, 8)`. Health `U(70, 95)` |
| Leader | Name drawn from the profile's pools; `competence U(45, 85)`, `age U(50, 72)`, `monthsInOffice 0..23`, random `NationalPriority` |
| Faction label | `"GOVERNING PARTY"` if elective, else `"PARTY LEADERSHIP"` |
| Consecutive term limit | `2` for `PresidentialRepublic`, `0` (none) for everything else |
| Legislative support / elite cohesion | `U(48, 70)` / `U(55, 80)` |
| First election | `startDate + max(6, termLength − monthsInOffice)` for elective systems, so no save opens on an election |
| Cabinet | Five officials, one per pillar, for **every** country: `competence U(40, 78)`, `loyalty U(35, 80)`, `riskTolerance U(20, 80)`, `trust U(50, 70)`, all `Autonomous`, names unique within the Cabinet and drawn from that country's own pools, titles from its own `officeTitles`. Id `OFF_{countryId}_{OFFICE}` |
| Relationship seeding | Every unordered pair; dependence `volume × 0.6` from the trade link, threat perception `military × 0.45` |
| AI profile | Every non-player state: `aggression`, `caution`, `opportunism`, `patience` each `U(20, 80)` — see §10 |

## 3. The roster — sixteen authored countries

Pillars in the order Military / Economy / Intelligence / Diplomacy / Government.
Term is `termLengthMonths` (0 = non-elective, no scheduled contest).

| id | Name | Government | Term | Mil | Eco | Int | Dip | Gov |
|---|---|---|---|---|---|---|---|---|
| USA | United States | Presidential republic | 48 | 84 | 82 | 86 | 76 | 66 |
| CHN | China | Dominant-party state | 0 | 76 | 88 | 72 | 68 | 78 |
| RUS | Russia | Centralized republic | 0 | 72 | 46 | 78 | 50 | 62 |
| IND | India | Parliamentary republic | 60 | 64 | 68 | 58 | 70 | 64 |
| DEU | Germany | Parliamentary republic | 48 | 52 | 80 | 62 | 78 | 76 |
| JPN | Japan | Parliamentary republic | 48 | 56 | 78 | 64 | 70 | 74 |
| BRA | Brazil | Presidential republic | 48 | 48 | 60 | 44 | 66 | 54 |
| TUR | Türkiye | Presidential republic | 60 | 58 | 52 | 60 | 62 | 56 |
| NGA | Nigeria | Presidential republic | 48 | 38 | 40 | 34 | 50 | 38 |
| SAU | Saudi Arabia | Monarchy | 0 | 46 | 58 | 48 | 58 | 60 |
| AUS | Australia | Parliamentary republic | 36 | 42 | 62 | 58 | 64 | 74 |
| KOR | South Korea | Presidential republic | 60 | 60 | 74 | 60 | 58 | 68 |
| MEX | Mexico | Presidential republic | 72 | 36 | 58 | 40 | 56 | 48 |
| IDN | Indonesia | Presidential republic | 60 | 40 | 54 | 42 | 60 | 52 |
| POL | Poland | Parliamentary republic | 48 | 50 | 56 | 48 | 54 | 62 |
| KAZ | Kazakhstan | Centralized republic | 0 | 30 | 42 | 38 | 48 | 50 |

Resources, social baselines and map placement:

| id | Treasury | Manpower | Energy | Industry | Materials | Food | Appr | Stab | Unity | Map (x,y) | Code |
|---|---|---|---|---|---|---|---|---|---|---|---|
| USA | 2100 | 900 | 78 | 70 | 42 | 88 | 48 | 64 | 46 | 14, 6 | US |
| CHN | 2400 | 1800 | 38 | **92** | 68 | 60 | 62 | 72 | 70 | 62, 8 | CN |
| RUS | 900 | 700 | 96 | 56 | 86 | 70 | 55 | 58 | 60 | 52, 4 | RU |
| IND | 1100 | 1700 | 38 | 62 | 48 | 74 | 58 | 60 | 56 | 56, 11 | IN |
| DEU | 1500 | 420 | 30 | 86 | 34 | 70 | 52 | 74 | 66 | 37, 5 | DE |
| JPN | 1400 | 380 | 22 | 84 | 26 | 44 | 50 | 78 | 72 | 71, 7 | JP |
| BRA | 800 | 900 | 72 | 56 | 80 | **94** | 46 | 52 | 58 | 23, 15 | BR |
| TUR | 600 | 620 | 26 | 58 | 40 | 76 | 50 | 50 | 48 | 45, 8 | TR |
| NGA | 400 | 1100 | 82 | 32 | 56 | 54 | 42 | **36** | **34** | 36, 12 | NG |
| SAU | 1900 | 300 | **100** | 34 | 44 | **18** | 56 | 62 | 64 | 47, 10 | SA |
| AUS | 900 | 220 | 78 | 44 | **92** | 90 | 52 | 76 | 68 | 68, 17 | AU |
| KOR | 1000 | 400 | **18** | 88 | 24 | 38 | 46 | 66 | 60 | 66, 6 | KR |
| MEX | 600 | 700 | 54 | 66 | 50 | 62 | 50 | 44 | 52 | 15, 10 | MX |
| IDN | 550 | 1000 | 62 | 48 | 66 | 64 | 54 | 54 | 46 | 65, 14 | ID |
| POL | 500 | 380 | 42 | 64 | 38 | 72 | 48 | 60 | 62 | 41, 5 | PL |
| KAZ | 450 | 260 | 88 | 36 | 84 | 66 | 52 | 54 | 50 | 51, 7 | KZ |

`WorldFactory.PlayerCountryId` is `"USA"`, but that is only the debug-world
default. `CreateWorld(seed, countryId)` accepts **any** id in the table, the
assessment can post the operator to any of them (spec 11), and a test builds a
world for every profile in turn. Never assume the player is the United States.

## 3a. The expansion roster and world sizes (GDD §31.2 amendment)

The authored catalogue is **24 countries**; how many a save opens with is chosen
at the assessment (`WorldSize`, stored on the save):

- **STANDARD (16)** — `WorldFactory.StandardRoster`, the table above. **The
  measured world**: every balance figure in the project was taken here, and
  `CreateDebugWorld` builds it, so the harness keeps measuring the game the
  default plays. Kept as an explicit id list so growing `Profiles` can never
  silently grow it.
- **REGIONAL (10)** — USA, CHN, RUS, IND, DEU, JPN, KOR, SAU, TUR, AUS.
  Curated, not truncated: the great-power core stays (the AI rivalry systems
  were tuned against it), every member keeps trade links inside the set, and
  all three authored food dependencies survive.
- **FULL WORLD (24)** — everything, including the eight below.

| id | Name | Government | Term | Vulnerability | Map (x,y) | Code |
|---|---|---|---|---|---|---|
| GBR | United Kingdom | Parliamentary republic | 60 | materials 30 | 34, 4 | GB |
| FRA | France | Presidential republic | 60 | materials 32, unity 48 | 35, 6 | FR |
| ITA | Italy | Parliamentary republic | **36** | energy 24, gov 48 | 38, 7 | IT |
| CAN | Canada | Parliamentary republic | 48 | military 38, manpower 200 | 14, 3 | CA |
| EGY | Egypt | Centralized republic | 0 | **food 30**, economy 38 | 43, 10 | EG |
| ZAF | South Africa | Parliamentary republic | 60 | energy 40, stability 42 | 40, 17 | ZA |
| ARG | Argentina | Presidential republic | 48 | economy 42, treasury 250 (food **96**) | 22, 18 | AR |
| VNM | Vietnam | Dominant-party state | 0 | materials 38, beside CHN | 63, 11 | VN |

**The tail rule is load-bearing.** Expansion profiles, locations, trade links and
posture rows are authored strictly *after* every Standard entry, and `CreateWorld`
skips an excluded profile or location **without consuming a random draw** — the
two rules that keep the Standard world bit-identical to the measured one
(`WorldSizeTests.GrowingTheCatalogueDidNotMoveTheStandardWorld` asserts it).
Author new content at the tail or you silently regenerate the measured world.

Rosters interact with the flow: `AssessmentSystem.AssignPosting` defaults to the
Standard roster and takes an allowed-ids overload; the assessment screen offers
the three sizes on the intervention step and re-fits the posting when the chosen
world does not contain it; `CreateWorld` falls a foreign posting back to the
default post. Smaller worlds prune trade links and hosted-operator references to
what both ends of exist. Balance on Regional and Full is **unmeasured** — run
`Report_MultiSeedBalance` before tuning against either.

## 4. Archetypes

The GDD (§31.2) asks for a mix rather than a shelf of peers, and the reason is
mechanical: sixteen great powers would leave economic coercion with no affordable
target and diplomacy with nobody to swing. The array is grouped by archetype with
comment headers, and the groups are:

| Archetype | Countries | What they are for |
|---|---|---|
| Major powers | USA, CHN, RUS, IND, DEU | The states that can act globally. Five of them, not two, so the world is not bipolar and coalitions have real choices |
| Regional powers | JPN, BRA, TUR, NGA | Coalition swing states — the actual diplomatic game. Strong enough to matter, not strong enough to act alone |
| Resource powers | SAU, AUS | Leverage without mass. Sanction-resistant, courted by everyone, militarily thin |
| Industrial mid-tiers | KOR, MEX | Supply-chain chokepoints. Wealthy, productive, structurally exposed |
| Maritime / archipelago | IDN | Sits astride the sea lanes, which is what makes chokepoints and naval reach matter at all |
| Buffer states | POL, KAZ | Low capability, high strategic value, wedged between larger neighbours. The natural coup, pressure and crisis candidates |

`RealWorldRosterTests.Roster_CoversTheDesignedArchetypes` asserts the roster is
14–18 entries and that **all five government types are exercised**. The monarchy
(SAU) and the two centralized republics (RUS, KAZ) exist partly to keep the
non-elective succession and coup pathways in spec 05 reachable in every save.

## 5. Authored vulnerabilities

Every country carries at least one genuine weakness. This is not flavour: a
uniformly capable state is inert, because nothing an opponent can do to it lands.
`RealWorldRosterTests.EveryCountry_HasAGenuineVulnerability` enforces it, and a
country passes only if — after jitter — at least one of the following holds:

```
energy < 45  ·  food < 50  ·  materials < 45  ·  industry < 45
stability < 55  ·  unity < 50  ·  military < 50  ·  economy < 55
```

| id | The vulnerability | Where it shows in the numbers |
|---|---|---|
| USA | Imported critical minerals, and politically divided at home | materials 42, unity 46, approval 48 |
| CHN | Industrial weight resting on imported energy | energy 38 against industry 92 |
| RUS | Energy- and materials-rich but economically thin and diplomatically isolated | economy 46, diplomacy 50, treasury 900 |
| IND | Populous and well-placed, with an energy gap and shallow reporting | energy 38, intelligence 58 |
| DEU | Industrial heavyweight with a standing energy import problem | energy 30, materials 34, military 52 |
| JPN | Structurally short of energy, materials, food and people | energy 22, materials 26, food 44, manpower 380 |
| BRA | Agricultural and resource depth, thin institutions and treasury | treasury 800, stability 52, intelligence 44 |
| TUR | Leverage from position rather than mass; no energy of its own | energy 26, treasury 600, stability 50, unity 48 |
| NGA | Populous energy exporter carrying real internal fragility | stability 36, unity 34, industry 32, every pillar ≤ 50 |
| SAU | Energy superpower that cannot feed itself | food 18, manpower 300, industry 34 |
| AUS | Materials and food surplus, tiny population, long sea lines | manpower 220, industry 44, military 42 |
| KOR | Advanced industry in a dangerous neighbourhood, no energy of its own | energy 18, materials 24, food 38 |
| MEX | Manufacturing tied tightly to one neighbour's market | USA link volume 62, stability 44, military 36 |
| IDN | Astride the lanes, but internally fragmented and militarily light | unity 46, military 40, intelligence 42 |
| POL | Frontline industrial buffer: exposed, and knows it | treasury 500, manpower 380, opens at 22 relations with RUS |
| KAZ | Landlocked resource buffer between two larger neighbours | military 30, manpower 260, industry 36 |

The vulnerabilities are also what the AI reads when it scores objectives (spec
06) — a state with low stability and a hostile neighbour is a subversion target
without anybody authoring it as one.

## 6. Strategic locations — forty-one

`MakeMap` authors meaningful targets only (GDD §19): a capital plus one or two
economic or military nodes per country, three chokepoints, six airbases. Garrison
is rolled `U(30, 60)` for every location; `originalOwnerId` is set to the owner,
so occupation is always visible through `IsOccupied`.

The local `Add(...)` helper takes an optional seventh argument:

```
Add(id, name, type, owner, defense, value, hostedOperator = null)
```

`hostedOperator` writes `foreignOperatorId` (empty when omitted), so a location
can open the save already operated by a foreign power. Everything else is
positional and required.

**`originalOwnerId` must be the country the location physically sits in** — never
the country that operates from it, and never the country it is aimed at. The
`owner` argument sets both `ownerId` and `originalOwnerId`, and `GeographySystem`
measures reach from `HostOf(location)`, which reads `originalOwnerId`. Getting
this wrong is now a **geography bug** rather than a labelling one: the location
will project force from, and be defended at, the wrong point on the planet. Use
`hostedOperator` to say who flies from it.

| id | Name | Type | Owner | Def | Value |
|---|---|---|---|---|---|
| USA_CAP | Washington D.C. | Capital | USA | 74 | 95 |
| USA_PRT | Norfolk Naval Complex | Port | USA | 55 | 72 |
| USA_ENR | Gulf Coast Energy Belt | EnergyRegion | USA | 38 | 76 |
| CHN_CAP | Beijing | Capital | CHN | 76 | 95 |
| CHN_PRT | Port of Shanghai | Port | CHN | 52 | 80 |
| CHN_IND | Pearl River Delta | IndustrialCenter | CHN | 44 | 82 |
| CHN_AIR | Southern Theatre Airfields | Airbase | CHN | 54 | 76 |
| RUS_CAP | Moscow | Capital | RUS | 72 | 95 |
| RUS_ENR | Western Siberian Fields | EnergyRegion | RUS | 40 | 84 |
| RUS_AIR | Western Military District Airfields | Airbase | RUS | 56 | 74 |
| IND_CAP | New Delhi | Capital | IND | 68 | 95 |
| IND_IND | Mumbai Industrial Region | IndustrialCenter | IND | 46 | 74 |
| IND_AIR | Western Air Command | Airbase | IND | 50 | 70 |
| DEU_CAP | Berlin | Capital | DEU | 64 | 92 |
| DEU_IND | Ruhr Industrial Belt | IndustrialCenter | DEU | 44 | 80 |
| DEU_AIR | Ramstein Air Complex | Airbase | DEU, **operated by USA** | 58 | 80 |
| JPN_CAP | Tokyo | Capital | JPN | 66 | 92 |
| JPN_PRT | Port of Yokohama | Port | JPN | 50 | 76 |
| BRA_CAP | Brasília | Capital | BRA | 58 | 88 |
| BRA_ENR | Santos Basin | EnergyRegion | BRA | 34 | 70 |
| TUR_CAP | Ankara | Capital | TUR | 60 | 88 |
| TUR_CHK | Bosphorus Transit | Chokepoint | TUR | 58 | 78 |
| TUR_AIR | İncirlik Air Complex | Airbase | TUR | 52 | 78 |
| NGA_CAP | Abuja | Capital | NGA | 48 | 85 |
| NGA_ENR | Niger Delta Fields | EnergyRegion | NGA | 26 | 78 |
| SAU_CAP | Riyadh | Capital | SAU | 62 | 90 |
| SAU_ENR | Eastern Province Fields | EnergyRegion | SAU | 44 | 94 |
| AUS_CAP | Canberra | Capital | AUS | 56 | 86 |
| AUS_MAT | Pilbara Mining Region | IndustrialCenter | AUS | 30 | 76 |
| AUS_AIR | Northern Airfields | Airbase | AUS | 40 | 64 |
| KOR_CAP | Seoul | Capital | KOR | 70 | 92 |
| KOR_IND | Ulsan Industrial Complex | IndustrialCenter | KOR | 46 | 78 |
| MEX_CAP | Mexico City | Capital | MEX | 54 | 88 |
| MEX_IND | Bajío Manufacturing Belt | IndustrialCenter | MEX | 36 | 68 |
| IDN_CAP | Jakarta | Capital | IDN | 52 | 86 |
| IDN_CHK | Malacca Approaches | Chokepoint | IDN | 48 | 88 |
| POL_CAP | Warsaw | Capital | POL | 62 | 88 |
| POL_PAS | Eastern Frontier Corridor | MountainPass | POL | 66 | 72 |
| KAZ_CAP | Astana | Capital | KAZ | 50 | 84 |
| KAZ_MAT | Caspian Resource Belt | EnergyRegion | KAZ | 28 | 80 |
| CONTESTED_LANE | Contested Sea Lane | Chokepoint | CHN | 62 | 70 |

By type: 16 capitals, 6 airbases, 6 industrial centers, 6 energy regions,
3 chokepoints, 3 ports, 1 mountain pass.

Notes on the authoring:

- **Capitals** run `defenseValue` 48–76 and `strategicValue` 84–95. They are
  deliberately the hardest and most valuable objects on the map, and per GDD §22
  a capital **cannot be won at the table** — the settlement model prices it at
  −120 willingness (spec 01 §5).
- **The six airbases were added late.** `LocationType.Airbase` existed for the
  whole project with no location using it, which meant the one thing that gives a
  force reach could never change hands. They are spread across powers and
  partners on purpose, so projection is contestable rather than a birthright;
  `ProjectionSwing` in `TerritorySystem` reads them into the readiness target.
- **Ramstein is German ground the United States operates from**, not American
  territory. It was authored as USA-owned, which was harmless while position meant
  nothing and became wrong the moment geography did (spec 01 §3b): a forward base
  whose entire identity is that it is *forward* sat at Washington's coordinates
  and gave its operator no European reach at all. It is now
  `Add("DEU_AIR", …, "DEU", 58, 80, hostedOperator: "USA")` — German-owned,
  US-operated — so it projects from central Europe, Germany can revoke it if the
  relationship sours, and it can be lost to an attack on Germany rather than only
  to an attack on the United States.
- **Basing.** `SupportsBasing` covers Airbase, Port and MountainPass — ten
  locations. `DEU_AIR` is the **only** authored hosting arrangement; every other
  foreign basing right in a save was negotiated in play (spec 04). It exists
  because `foreignOperatorId` had shipped with **no starting world using it**, so
  the strategic argument for a Transit commitment — reach you did not have to
  conquer — had nothing in the opening position to point at.
- **There is no materials location type.** `AUS_MAT` (a mining region) is typed
  `IndustrialCenter` and `KAZ_MAT` `EnergyRegion`, because `TerritorySystem`
  yields are keyed off the type. Taking the Pilbara therefore pays industry, not
  materials. Adding a `MaterialsRegion` type is the honest fix — see §10.
- **The flashpoint** is `CONTESTED_LANE`, held by CHN, and it is deliberately
  generic. Do not add real disputed territory.

## 7. Trade network

`MakeTradeNetwork` authors **37 undirected links** as `TradeRelation` records —
`volume` 0..100 with `tariff` and `embargoed` starting clear. Volume is the whole
economic model of a relationship: it feeds `tradeHealth` (spec 02 §3), seeds
`dependenceAOnB` / `dependenceBOnA` at `volume × 0.6`, and scales sanction
blowback at `0.5 + volume/100`, so the volume you author *is* the price of
coercing that partner later.

Three deliberate tiers:

| Tier | Range | Links | Purpose |
|---|---|---|---|
| Heavy interdependence | 46–78 | USA–CHN **78**, USA–MEX 62, CHN–KOR 52, CHN–JPN 50, USA–JPN 48, DEU–POL 46 | Coercing these costs the sender most |
| Substantial but manageable | 26–44 | CHN–RUS 44, CHN–AUS 44, CHN–IND 40, DEU–USA 40, CHN–IDN 38, BRA–CHN 36, JPN–AUS 36, USA–IND 34, USA–KOR 34, RUS–KAZ 34, SAU–IND 34, DEU–TUR 32, SAU–JPN 32, RUS–IND 30, SAU–KOR 30, KOR–JPN 28, IDN–JPN 26 | The ordinary texture of the world |
| Marginal | 8–26 | USA–AUS 26, KAZ–CHN 26, USA–SAU 24, DEU–RUS 24, USA–BRA 22, TUR–RUS 22, USA–TUR 18, USA–POL 16, USA–IDN 16, IND–NGA 16, USA–NGA 14, USA–RUS 12, BRA–NGA 12, USA–KAZ 8 | The affordable targets for economic coercion |

Designed dependencies worth knowing:

- **USA–CHN at 78 is the heaviest link in the world** and the central economic
  dilemma: the most obvious target is also the most expensive one.
  `TradeNetwork_MakesTheChinaLinkTheHeaviest` locks the ordering
  USA–CHN > USA–IND > USA–RUS.
- The United States has a link to **all fifteen** other states, eight of them
  below volume 25. `EconomicCoercion_HasAffordableTargets` requires at least four
  such targets; without them, sanctions were a lever nobody could afford to pull.
- SAU sells energy to IND/JPN/KOR — the three states authored with the worst
  energy positions (38 / 22 / 18). KAZ sells to RUS and CHN and to nobody else of
  weight, which is what makes a landlocked buffer a buffer.
- MEX at 62 with one partner and nothing else substantial is the sharpest
  single-market exposure in the roster.

## 8. Diplomatic posture seeding

Two passes run in order:

1. `DiplomacySystem.SeedRelationships` creates a `Relationship` for **every
   unordered pair** — 120 for sixteen countries — with `relations`, `trust` and
   `strategicAlignment` at their defaults of 50, dependence from the trade link,
   and `threatPerceptionOfX = military × 0.45`. Threat is therefore emergent from
   the roster: a strong military looks dangerous to everyone from month one.
2. `WorldFactory.SeedDiplomaticPosture` overwrites relations/trust/alignment for
   **29 authored pairs**. Everything else stays at neutral 50/50/50, which is the
   correct answer for two states with little to do with each other.

| Group | Pairs (relations / trust / alignment) |
|---|---|
| Rivalries and cool distances | USA–CHN 34/28/25 · USA–RUS 26/22/20 · CHN–IND 36/32/34 · CHN–JPN 34/30/28 · RUS–POL 22/18/20 · RUS–DEU 34/28/30 · KOR–JPN 48/42/58 · SAU–TUR 44/38/42 |
| Partnerships and blocs | USA–AUS 82/80/84 · USA–JPN 80/76/82 · USA–DEU 76/72/78 · USA–KOR 76/70/78 · JPN–AUS 74/70/76 · USA–POL 72/68/74 · DEU–POL 70/64/72 · USA–MEX 66/58/62 · RUS–KAZ 66/56/64 · CHN–RUS 64/52/66 · USA–IND 62/55/60 · CHN–KAZ 58/48/56 · RUS–IND 58/54/52 · IND–IDN 58/52/56 · USA–SAU 58/48/54 · USA–BRA 58/52/56 · BRA–IND 56/50/54 |
| Buffer hedging | KAZ–TUR 54/46/50 · IDN–AUS 56/48/52 · NGA–USA 52/44/48 · NGA–CHN 56/46/52 |

Two properties are load-bearing. RUS–POL at 22/18/20 is the lowest pair in the
world and is what makes a buffer state's position legible without any scripting.
The hedging group is authored *warm with both sides and committed to neither* —
NGA is 52 with the US and 56 with China — so buffer states have somewhere to move
when pressure arrives. Status (`Ally`, `Hostile`, …) is never stored; it is
derived from these dimensions plus treaties (spec 04).

## 9. Adding a country

The catalogue is at 24 (16 Standard + 8 expansion, §3a). If you add one, this is
the work — in a single commit, per the spec-maintenance rule in the specs README.
**Author everything at the tail** (profiles, locations, links, posture) and add
the id to the roster(s) it belongs in; a new country outside `StandardRoster`
exists only in the Full world.

1. **Profile.** Add a `CountryProfile` under the right archetype comment block in
   `WorldFactory.Profiles`. Fill every field in §2, including `mapX`/`mapY`,
   `mapCode`, 8+8 names and exactly 5 office titles.
2. **Give it one genuine vulnerability**, clear of the §5 thresholds by more than
   the jitter spread (±4/±5) so it passes on every seed.
3. **Map placement.** `mapX` in `0..77`, `mapY` in `0..20`, `mapCode` exactly two
   characters. Markers occupy four cells on a row — no other country on the same
   `mapY` may sit within 4 columns.
4. **Locations** in `MakeMap`: a capital (`defenseValue` 48–76,
   `strategicValue` 84–95) plus **at least one** more, since a test requires two
   held locations including a capital. Prefer a node that expresses the
   vulnerability rather than another generic industrial center. Author the `owner`
   as the country the location **physically sits in**, and if a partner operates
   from it, say so with `hostedOperator:` rather than by changing the owner (§6).
5. **Trade links** in `MakeTradeNetwork`: at least two, with volumes that create
   an exposure asymmetry. Do not exceed USA–CHN 78, and keep at least four
   non-player states below volume 25 with the player.
6. **Posture** in `SeedDiplomaticPosture` for any pair that should not start
   neutral. Unset pairs are fine and are cheaper than authoring 16 more rows.
7. **Run the roster tests.** These assert roster properties directly:

   | Test | Asserts |
   |---|---|
   | `RealWorldRosterTests.Roster_CoversTheDesignedArchetypes` | 14 ≤ `StandardRoster.Length` ≤ 18; all five government types present |
   | `WorldSizeTests` (whole file) | Standard world bit-identical to the measured one; Regional/Full complete: ground, links, minds, relationships, vulnerabilities |
   | `RealWorldRosterTests.EveryCountry_HasAGenuineVulnerability` | The §5 weakness check, per country |
   | `RealWorldRosterTests.EveryCountry_HasStrategicLocations` | A capital plus ≥ 2 locations held |
   | `RealWorldRosterTests.EconomicCoercion_HasAffordableTargets` | ≥ 4 states at trade volume < 25 with the player |
   | `RealWorldRosterTests.TradeNetwork_MakesTheChinaLinkTheHeaviest` | USA–CHN > USA–IND > USA–RUS |
   | `RealWorldRosterTests.Profiles_ProduceDistinctNationalCharacters` | RUS energy > USA energy; CHN industry > RUS industry |
   | `AsciiWorldMapTests.CountryPositions_DoNotCollide` | No two markers overlap |
   | `AsciiWorldMapTests.Positions_FitWithinTheGrid` | Coordinates in range, `mapCode` length 2 |
   | `AsciiWorldMapTests.EveryCountry_AppearsOnTheMap` | Every `mapCode` is drawn |
   | `AssessmentSystemTests.PlayerCanBePostedToAnyAuthoredNation` | A world builds, with a full Cabinet, for **every** profile |
   | `ForeignCabinetTests.EveryCountryHasFiveOfficialsOnePerPillar` | Every country, one official per pillar |
   | `ForeignCabinetTests.OfficialIdsAreUniqueAcrossTheWorld` | No id collides across the roster |
   | `ForeignCabinetTests.AForeignCabinetBelongsToItsOwnNation` | Names from that country's pools, titles from its `officeTitles` |
   | `DiplomacySystemTests.Factory_SeedsFullRelationshipGraph` | `n(n−1)/2` relationships (derives from the count) |
   | `AISystemTests.Factory_SeedsAIForEveryNonPlayerState` | one AI state per non-player country |
   | `MilitarySystemTests.Factory_BuildsForcesAndMap` | `locations.Count > 30` |

8. **Re-run `Report_MultiSeedBalance`** (five seeds, per-component breakdown) and
   update `Docs/VerticalSliceValidation.md` if the numbers move. A new country
   changes coalition arithmetic and sanction availability for everyone.
9. **Update this spec** — the roster, vulnerability, location, trade and posture
   tables above are the whole point of it.

## 10. Open questions / PLANNED

Honest gaps against GDD §31.1, which asks each country for "geography, history,
government, national traits, strategic interests, military/economic architecture,
relationships, cultural/political tendencies, starting leaders and unique
vulnerabilities". Government, architecture, relationships, leaders and
vulnerabilities are in. These are not:

- **No authored histories. PLANNED.** A save's history begins at month zero.
  Relationships carry `memory` and `memoryWeight`, but both start empty, so
  nothing in the world happened before the operator arrived — RUS–POL at 22
  relations is a number with no story behind it. Seeding a few memory entries per
  authored pair would cost little and give the Chronicle a past to extend.
- **No per-country national traits. PLANNED.** `NationalTrait` exists and is
  applied, but only to the **player's** nation and only from the assessment
  (`AssessmentSystem.TraitsFor` draws two from a pool of ten keyed on the
  operator's doctrine, spec 11). Fifteen AI states have no traits at all. GDD §10
  lists National Traits as a layer of the national power model, so this is a real
  omission rather than a deferral: an authored `traits` array on `CountryProfile`
  applied for every country is the shape of the fix.
- **AI personality is rolled, not authored.** `AISystem.SeedAI` gives every
  non-player state `aggression`, `caution`, `opportunism` and `patience` at
  `U(20, 80)`. A country's temperament therefore **differs between saves** — the
  same nation can be timid in one game and reckless in the next. That is defensible
  as replay variety, but it is not the "cultural/political tendencies" the GDD
  asks for, and it means no country has a recognisable strategic character across
  saves. Authored means with a rolled spread around them would keep both.
- **No materials location type.** Materials regions are currently typed as
  industrial or energy nodes (§6), so conquering a mining region pays the wrong
  yield. Adding `LocationType.MaterialsRegion` needs a `TypeCode`, a
  `TerritorySystem` multiplier and a consumer in `EconomySystem`.
- **Where does history start?** The calendar opens JAN 1984, a placeholder from
  the fictional-world era. With a real roster this reads oddly and should be
  revisited — either a present-day start or an explicitly alternate-history framing.
- **Border changes** (GDD §16: secession, annexation, unification, collapse) are
  not implemented; only location ownership changes. A country can lose everything
  it holds and still exist as a full entry in `state.countries`.
- **Historical map snapshots** (GDD §16) for comparing decades are not implemented.
- **Specific dependencies** (oil, grain, semiconductors — GDD §10) are not
  modelled; trade is a single `volume` per pair. The authoring hook would be a
  per-country list of critical inputs checked against links (spec 02 §7).

# 10 — Save & Data Schema Specification

Source: `Core/SaveSystem.cs`, `Data/GameState.cs`. GDD §30, §36.

## 1. Model

**One object is the save.** `GameState` is the complete, authoritative world
state; everything the simulation needs to resume must live on it or be derivable
from it. Systems are static and stateless — they take `GameState` and mutate it.
This is what makes save/load, determinism and headless simulation all fall out
for free.

Serialization is `JsonUtility` (Unity's), which imposes real constraints:

- Only **fields** serialize, not properties. Every persisted type needs
  `[Serializable]` and public fields.
- **No dictionaries.** Use `List<T>` plus a `Find*` helper on `GameState` (see
  `FindCountry`, `FindRelationship`, `FindNetwork`, `FindEstimate`, `FindTrade`,
  `FindSanction`, `FindTreaty`, `FindCoalition`, `FindAI`, `FindLocation`).
- **No polymorphism.** No interfaces or abstract types in persisted data.
- `null` collections deserialize as empty; a missing field takes its default.

## 2. Top-level contents

| Field | Contents |
|---|---|
| `saveVersion`, `rngSeed` | Schema version (§2b) and the determinism seed |
| `date`, `startDate` | Calendar |
| `playerCountryId`, `countries` | Roster; the player may be posted to any nation |
| `commandPoints`, `influence`, `politicalCapital` | Player resources |
| `legacyCabinet` | Empty. Where the player's officials used to live (§2c) |
| `locations`, `confrontations` | Strategic map and conflicts |
| `trade`, `sanctions` | Economic relations |
| `networks`, `estimates` | Intelligence, keyed by observer |
| `relationships`, `treaties`, `coalitions`, `exercises` | Diplomacy |
| `aiStates`, `difficulty` | AI reasoning state |
| `notifications`, `chronicle`, `activeCrises` | Traffic, history, live interrupts |
| `strategistXP`, `strategistLevel`, `skillPoints`, `unlockedSkills`, `evaluations`, `yearSnapshot` | Progression |
| `assessment`, `administrationsServed` | First-launch result and tenure |
| `settlements`, `endgameRecords` | Archived peace terms and decisive instruments used |
| `technology` (per country), `eventCooldowns`, `tutorial` | Capabilities, event pacing, orientation |
| `actionSequence` | Draw counter for repeated actions in one month (§7b) |
| `xpReasons`, `xpReasonCounts` | Per-kind XP repetition counters for the current evaluation year (§2a) |

Per-country state (`CountryState`) nests `PillarScores`, `NationalResources`,
`MilitaryState`, `EconomyState`, `CounterIntelState`, `GovernmentState`,
`TechnologyState`, `EndgameState`, and `cabinet` — five `Official` records, one
per pillar, for **every** country (§2c).

### 2a. Parallel lists stand in for dictionaries

`xpReasons` (`List<string>`) and `xpReasonCounts` (`List<int>`) are a map from an
XP reason to the number of times it has already paid this year, held as two lists
indexed in lockstep because **`JsonUtility` cannot serialize a dictionary** — it
would write `{}` and the repetition discount would silently reset on every load
(spec 07 §2). Anything that mutates one must mutate the other in the same
statement; `ProgressionSystem.RepetitionFactor` is the only writer, and
`CaptureYearSnapshot` clears both together at the year boundary.

They are additive and default to empty, so an older save loads with a clean slate
for the year in progress and needs no migration step. Use the same pattern for any
future keyed counter rather than reaching for a dictionary the serializer will
drop.

### Baselines and endowments

Several fields exist purely so a value has something to return *to*. They are
additive and default to zero on an older save, so each is seeded from the current
value on first tick rather than migrated:

| Field | Returns to | Seeded in |
|---|---|---|
| `NationalResources.manpowerBaseline` | Recruitable population after war losses | `EconomySystem.RecoverManpower` |
| `NationalResources.energyEndowment` | Authored energy position | `EconomySystem.RecoverManpower` |
| `NationalResources.materialsEndowment` | Authored materials position | `EconomySystem.RecoverManpower` |

Without them these values only ever fell, so every country drifted toward an
identical resource profile and the roster's authored vulnerabilities evaporated
(spec 12 §4).

Also new and persisted: `StrategicLocation.foreignOperatorId` (consensual basing,
spec 04) and `AIState.rivalries` (rivalry tracked separately from objectives,
spec 06).

### 2b. `saveVersion` defaults to the oldest schema and is *stamped* at creation

The field is declared `public int saveVersion = 1` — the **oldest** schema, on
purpose. A JSON blob that carries no version field at all is by definition from
before versioning existed, and `JsonUtility` gives an absent field its default, so
defaulting to 1 makes an unversioned save migrate forward rather than be trusted
as current. Defaulting to `CurrentSaveVersion` would silently declare every legacy
save already up to date and skip the chain entirely.

That default is correct for *deserialization* and wrong for *creation*, so
`WorldFactory.CreateWorld` stamps it explicitly:

```csharp
state.saveVersion = Core.SaveSystem.CurrentSaveVersion;
```

Without the stamp, a freshly created world claimed to be from the oldest version,
so a live state and the same state round-tripped through save/load **disagreed
about their own version** — the live one would be migrated on its next load and
the reloaded one would not, which is the kind of divergence determinism tests are
built to catch and had no reason to look for.

**The rule generalises: a default is for a blob that has already been written; a
constructor is for a world this build just made.** They are not the same value.

### 2c. `cabinet` belongs to the country; `legacyCabinet` is a tombstone

`CountryState.cabinet` is the single home for a government's five officials, and
every country has one (spec 15 §1). `GameState.cabinet` still exists and still
returns the player's five, but it is a **property**:

```csharp
public List<Official> cabinet => PlayerCountry?.cabinet ?? EmptyCabinet;
```

`JsonUtility` serializes fields and not properties, which is what makes this safe:
the data is persisted **exactly once**, on the country that owns it, and the
property is a view rather than a second copy. Two homes for one concept is how the
two drift apart across a save/load. It also kept every existing reader of
`state.cabinet` — the CABINET and GOVERNMENT views, `ReportingSystem`, the test
suite — working unchanged while the second home was removed.

`GameState.legacyCabinet` is the `List<Official>` field the player's Cabinet used
to occupy. It survives only so that a save written before v2 still deserializes
into somewhere. The v1→v2 step empties it onto the player country and **nothing
ever writes to it again**; in any save this build produces it is `[]`.

Official ids are now country-qualified (`OFF_{countryId}_{OFFICE}`). The previous
per-pillar scheme was unique while one cabinet existed and collided on every id
across sixteen.

### Bounded collections

| Collection | Cap | Enforced in |
|---|---|---|
| `notifications` | 200 | `GameState.AddNotification` |
| `EconomyState.marketHistory` | 60 | `RecordMarket` |
| `Relationship.memory` | 30 | `AddMemory` |

`chronicle`, `evaluations`, `exercises`, `confrontations` and
`Confrontation.operations` are **unbounded by design** — they are the historical
record. Over a very long save these grow; if save size becomes a problem,
compact old chronicle entries rather than discarding them.

## 3. Persistence

- **Atomic writes.** `SaveSystem.Save` writes to `slot_N.json.tmp`, deletes the
  target, then moves. A crash mid-write cannot corrupt the previous save.
- **Slot 0 is the autosave**, written after every resolved month and after every
  consequential player action (crisis resolution, treaty, operation, unlock).
  Additional slots are available for manual saves.
- Location: `Application.persistentDataPath/saves`, overridable via
  `SaveSystem.SaveDirectoryOverride` (used by tests).
- Suspend/resume is immediate because the whole state is one object; a validation
  test proves a mid-decade save resumes bit-identically to an uninterrupted run.

## 4. Versioning and migration (`Core/SaveMigration.cs`)

`CurrentSaveVersion` is **2**. The chain is wired into `SaveSystem.FromJson` and,
for the first time, has a step in it — this machinery shipped and had never
executed once.

```
FromJson → JsonUtility → SaveMigration.Migrate → step v1→v2 → v2→v3 … → Validate
```

Each `Step` upgrades a state by exactly one version, so a save from any shipped
build walks forward to the current schema. Two failures are deliberately loud
rather than silent:

- **A save from a future build** throws with a message telling the player to
  update, instead of loading a mismatched world.
- **A missing step for a version gap** throws, so a breaking schema change made
  without a migration is caught in development rather than in a player's save.

`Validate` runs after migration and rejects states the simulation cannot run: no
countries, a `playerCountryId` matching nothing, territory owned by an unknown
country, or null collections. A test runs twenty years of full simulation and
asserts the result still validates.

### 4a. v1 → v2: "Cabinets belong to countries; every government has one"

The first real step. It does two things:

1. **Moves the player's officials.** If `legacyCabinet` is non-empty and the
   player country's own `cabinet` is empty, append the contents across. Then clear
   `legacyCabinet` unconditionally, so the tombstone is empty even on the paths
   where nothing was moved.
2. **Appoints the fifteen cabinets a v1 save does not have.** For every country
   with an empty `cabinet`, call `WorldFactory.AppointCabinet` — the same routine
   world creation uses, which is why it is public.

The guard in both halves is `cabinet.Count == 0`, so the step is **idempotent**: a
state that already has officials is left alone rather than given a second set.

**Seeded from the save's own `rngSeed`, per country:**

```csharp
new Random(unchecked(state.rngSeed * 7717 + Hash.Of(country.id)))
```

not from a fresh `Random()`. Migrating the same save twice has to produce the same
world. A migration whose output changed on every load would be worse than refusing
the save outright — the player's rivals would be run by different people each time
the game started, and nothing would report an error. `Hash.Of` is the stable
in-house hash for the reason in §7a; `string.GetHashCode` here would put the
determinism of a *migrated* save at the mercy of the scripting backend.

Tests: `ForeignCabinetTests.ASaveFromBeforeCabinetsMovedStillLoads` reconstructs
the v1 shape (officials on `legacyCabinet`, every `country.cabinet` cleared,
`saveVersion = 1`), round-trips it, and asserts the version, the player's five,
an empty `legacyCabinet` and five officials in every country.
`MigratingTheSameSaveTwiceGivesTheSameWorld` migrates the same v1 save twice and
compares every id, name and competence in the world.

### Rules when changing persisted data

- **Additive changes are free.** New fields default; no migration needed. Prefer
  them.
- **Never repurpose a field name.** Add a new one and migrate.
- **Never reorder an enum.** Values persist as integers. Append only.
- Bump `CurrentSaveVersion` and add the step in the **same commit** as any
  breaking change. §4a is the worked example to copy.
- **Seed every draw inside a step from `state.rngSeed`.** A migration is not
  exempt from §7 — it is the one place where a stray `new Random()` produces a
  different world on each load of the same file.
- **Guard each half on the state it is repairing**, not on the version number the
  chain already checked. An idempotent step survives being run against a
  half-migrated save; one that appends unconditionally does not.
- A field that is being vacated becomes a **tombstone**, not a deletion:
  `legacyCabinet` stays declared so an old blob still deserializes, and the step
  empties it. Removing the field outright would leave the old JSON key with
  nowhere to land and the data would be silently dropped.

## 5. Reset (GDD §5.1)

`GameController.ResetGame` calls `SaveSystem.DeleteAll()` — **every slot**, not
just the autosave — and clears the in-memory state, returning the shell to the
first-launch assessment. Because all progression lives on `GameState`, nothing
survives, which is exactly the specified behaviour. There is no cross-save
meta-progression, and none should be added (GDD §35).

It previously deleted the autosave only, which meant a manual save in slot 1
restored the entire nation, world history and Strategist progression the reset
was supposed to erase — a direct contradiction of §5.1.
`BugRegressionTests.AFullReset_LeavesNothingLoadable` guards it.

**Display preferences are deliberately exempt.** `DisplaySettings` (text size,
palette, line spacing) lives in `PlayerPrefs`, not `GameState`, and therefore
survives a reset. Ergonomics belong to the reader and the handset, not to the
save; a save copied to another device must not drag one phone's type size onto
another. See spec 09.

## 6. Cloud saves — **PLANNED**

GDD §30 wants cloud save for mobile. The single-object model makes this
straightforward, but two things need designing first:

- **Conflict resolution.** Two devices, two divergent worlds. Last-write-wins is
  hostile in a game about long saves; prefer presenting both with their dates and
  letting the player choose.
- **Payload size.** Measure a 50-year save before committing to a sync approach;
  compaction of the chronicle may be required.

## 7. Determinism contract

Any change that breaks this contract is a bug:

- Seed every random draw from `rngSeed` + month index + a stable identifier.
- Never call `UnityEngine.Random` or `DateTime.Now` in simulation code.
- Iterate lists in stored order; do not depend on hash ordering.
- Same seed + same inputs must produce byte-identical JSON. Several tests assert
  this over 120–360 month runs; keep them green.

### 7a. Stable hashing (`Core/Hash.cs`)

Per-actor seeds mix in a country or target id so that two states drawing in the
same month get independent streams. Those seeds used `string.GetHashCode()`,
which .NET does **not** contractually guarantee to be stable across processes.
It happens to be stable on Unity's current backends, which is why nothing broke —
and every determinism test runs both halves in one process, so none of them could
ever have noticed.

Had that guarantee lapsed — a scripting backend change, a runtime with randomized
string hashing — a loaded save would have drawn from different streams than the
session that wrote it. The world would silently fork from the save and nothing
would throw.

`Hash.Of` is a fixed in-house FNV-1a over the characters. **Never change its
implementation**: doing so changes every seed in every existing save.
`BugRegressionTests.TheStableHash_NeverChanges` pins its output for known inputs
so a platform change fails a test rather than a player's save.

### 7b. Repeated actions in one month (`GameState.actionSequence`)

A seed built only from `rngSeed + monthIndex + operationType` is identical for two
invocations in the same month. That was live: two covert operations run in one
month rebuilt the same stream and returned the same rolls, so a successful theft
of plans was infinitely repeatable — its own effect raised the success threshold
while the roll stayed fixed. The mirror case was as bad: a failure could never be
retried successfully within the month.

Anything that can happen more than once in a month must mix in
`state.NextActionSequence()`. It is a persisted field, so a save resumes the
sequence rather than replaying it. Currently used by covert operations,
counterintelligence sweeps and official dismissals.

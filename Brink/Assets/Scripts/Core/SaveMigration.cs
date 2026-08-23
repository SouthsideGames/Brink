using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Save schema migration (GDD §30, spec 10 §4).
    ///
    /// A save written by an older build must load in a newer one. Migrations run
    /// in order, each upgrading a state by exactly one version, so a save from
    /// any shipped version can walk forward to the current schema.
    ///
    /// **Rules when changing persisted data:**
    /// - Additive changes are free — a new field takes its default. Prefer them.
    /// - Never repurpose a field name; add a new one and migrate.
    /// - Never reorder an enum; values persist as integers. Append only.
    /// - Bump <see cref="SaveSystem.CurrentSaveVersion"/> and add the step here
    ///   in the same commit as any breaking change.
    /// </summary>
    public static class SaveMigration
    {
        /// <summary>One step, upgrading a state from `FromVersion` to `FromVersion + 1`.</summary>
        public class Step
        {
            public int fromVersion;
            public string description;
            public Action<GameState> apply;
        }

        static readonly List<Step> Steps = new List<Step>
        {
            new Step
            {
                fromVersion = 1,
                description = "Cabinets belong to countries; every government has one",
                apply = state =>
                {
                    // The player's cabinet used to hang off GameState. Move it to
                    // the country that owns it, and leave the legacy list empty
                    // forever after.
                    var player = state.PlayerCountry;
                    if (player != null && state.legacyCabinet.Count > 0 && player.cabinet.Count == 0)
                        player.cabinet.AddRange(state.legacyCabinet);
                    state.legacyCabinet.Clear();

                    // Foreign governments had no officials at all before this
                    // version. Appoint them, deterministically from the save's
                    // own seed so a migrated world is identical every time it is
                    // migrated — a migration that produced a different world on
                    // each load would be worse than refusing the save.
                    foreach (var country in state.countries)
                    {
                        if (country.cabinet.Count > 0) continue;
                        var rng = new Random(unchecked(state.rngSeed * 7717 + Hash.Of(country.id)));
                        WorldFactory.AppointCabinet(rng, country);
                    }
                }
            },
            new Step
            {
                fromVersion = 2,
                description = "Officials have ages",
                apply = state =>
                {
                    // `age` defaults to 0, which would mean a cabinet of infants
                    // who never retire. Additive fields are usually free; this
                    // one needs a backfill because its zero value is not merely
                    // empty, it is wrong.
                    foreach (var country in state.countries)
                    {
                        var rng = new Random(unchecked(state.rngSeed * 4409 + Hash.Of(country.id)));
                        foreach (var official in country.cabinet)
                        {
                            if (official.age > 0f) continue;

                            // Age them by however long they have already served,
                            // so a long-tenured minister is not suddenly young.
                            official.age = 44f + (float)rng.NextDouble() * 22f
                                           + official.monthsInOffice / 12f;
                        }
                    }
                }
            },

            new Step
            {
                fromVersion = 3,
                description = "Living standards, social unrest and public grievance",
                apply = state =>
                {
                    // `livingStandards` has a sensible field default, but a
                    // *loaded* save overwrites every field from JSON — an old
                    // save has no key for it, so JsonUtility leaves the
                    // constructed default in place and it is already correct.
                    //
                    // What is not correct is 55 for a country whose economy has
                    // been collapsing for a decade, so it is seeded from the
                    // economy that produced it. Grievance is seeded from war
                    // exhaustion for the same reason: a state that has fought
                    // three wars did not arrive at this build with a clean
                    // public memory.
                    foreach (var country in state.countries)
                    {
                        var eco = country.economy;

                        if (country.livingStandards <= 0f || Math.Abs(country.livingStandards - 55f) < 0.01f)
                            country.livingStandards = Clamp(
                                52f + (eco.marketIndex - 100f) * 0.15f
                                    + eco.growthRate * 1.6f
                                    - Math.Max(0f, eco.inflation - 4f) * 1.5f
                                    - Math.Max(0f, eco.unemployment - 6f) * 1.2f);

                        if (country.socialUnrest <= 0f)
                            country.socialUnrest = Clamp(
                                Math.Max(0f, 45f - country.livingStandards) * 0.55f
                                + country.warExhaustion * 0.22f);

                        if (country.publicGrievance <= 0f)
                            country.publicGrievance = Clamp(country.warExhaustion * 0.30f);
                    }
                }
            },

            new Step
            {
                fromVersion = 4,
                description = "Branch inventories behind branch strength",
                apply = state =>
                {
                    // An old save has a strength with nothing behind it. Empty is
                    // not merely missing here, it is wrong: strength is now a
                    // mirror of the inventory, so a country loaded without one
                    // would recompute to zero and disarm the entire world.
                    foreach (var country in state.countries)
                    {
                        foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                        {
                            var force = country.military.Get(branch);
                            if (force.inventory == null) force.inventory = new ForceInventory();
                            if (!force.inventory.IsSpent) continue;

                            AssetCatalog.FillToStrength(force.inventory, branch, force.strength);
                        }
                    }
                }
            },
            new Step
            {
                fromVersion = 5,
                description = "Branch experience (veterancy)",
                apply = state =>
                {
                    // Zero is wrong rather than empty, again. `EffectivePower`
                    // multiplies by an experience factor that bottoms out at 0.88,
                    // so a save loaded with no experience would quietly weaken
                    // every army in the world by ~9% and move balance for reasons
                    // nobody could see.
                    //
                    // Backfilled from readiness and doctrine investment, which is
                    // the same thing the peacetime training ceiling is computed
                    // from — so a migrated world lands where an equivalent live
                    // world would have settled, rather than at an arbitrary
                    // constant. A country at war keeps a little more: it has been
                    // doing the thing that teaches.
                    foreach (var country in state.countries)
                    {
                        bool atWar = state.IsAtWar(country.id);

                        foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                        {
                            var force = country.military.Get(branch);
                            if (force.experience > 0.01f) continue;   // already set

                            force.experience = Clamp(
                                18f + force.readiness * 0.22f
                                + country.military.logistics * 0.08f
                                + (atWar ? 12f : 0f));
                        }
                    }
                }
            },
        };

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);

        /// <summary>Highest version this build can produce after migrating.</summary>
        public static int TargetVersion => SaveSystem.CurrentSaveVersion;

        public static bool NeedsMigration(GameState state) => state.saveVersion < TargetVersion;

        /// <summary>True when the save is too new for this build to understand.</summary>
        public static bool IsFromFuture(GameState state) => state.saveVersion > TargetVersion;

        /// <summary>
        /// Walk a loaded state forward to the current schema. Throws when a save
        /// is from a future build, or when a step is missing for a version gap —
        /// both are far safer than silently loading a mismatched world.
        /// </summary>
        public static GameState Migrate(GameState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));

            if (IsFromFuture(state))
            {
                throw new InvalidOperationException(
                    $"Save version {state.saveVersion} was written by a newer build " +
                    $"(this build reads up to {TargetVersion}). Update the game to open it.");
            }

            if (!NeedsMigration(state)) return state;

            int startVersion = state.saveVersion;
            while (state.saveVersion < TargetVersion)
            {
                var step = FindStep(state.saveVersion);
                if (step == null)
                {
                    throw new InvalidOperationException(
                        $"No migration step from save version {state.saveVersion}. " +
                        "A breaking schema change was made without adding one.");
                }

                step.apply?.Invoke(state);
                state.saveVersion++;
                GameLog.Info("SAVE", $"Migrated save to v{state.saveVersion}: {step.description}");
            }

            GameLog.Info("SAVE", $"Save migrated from v{startVersion} to v{state.saveVersion}.");
            Validate(state);
            return state;
        }

        static Step FindStep(int fromVersion)
        {
            foreach (var step in Steps)
                if (step.fromVersion == fromVersion) return step;
            return null;
        }

        /// <summary>
        /// Post-migration sanity check. Catches a migration that produced a state
        /// the simulation cannot actually run, before it corrupts a session.
        /// </summary>
        public static void Validate(GameState state)
        {
            if (state.countries == null || state.countries.Count == 0)
                throw new InvalidOperationException("Migrated save has no countries.");

            if (state.PlayerCountry == null)
                throw new InvalidOperationException(
                    $"Migrated save has no country matching playerCountryId '{state.playerCountryId}'.");

            foreach (var location in state.locations)
            {
                if (state.FindCountry(location.ownerId) == null)
                    throw new InvalidOperationException(
                        $"Location '{location.id}' is owned by unknown country '{location.ownerId}'.");
            }

            // Collections are never null after a migration; JsonUtility leaves
            // absent lists empty, but a hand-written step could not.
            if (state.relationships == null || state.notifications == null
                || state.chronicle == null || state.aiStates == null)
                throw new InvalidOperationException("Migrated save has null collections.");
        }

        /// <summary>Human-readable description of the chain, for logs and support.</summary>
        public static string DescribeChain()
        {
            if (Steps.Count == 0) return $"No migrations defined; current schema is v{TargetVersion}.";

            var lines = new List<string>();
            foreach (var step in Steps)
                lines.Add($"v{step.fromVersion} → v{step.fromVersion + 1}: {step.description}");
            return string.Join("\n", lines);
        }
    }
}

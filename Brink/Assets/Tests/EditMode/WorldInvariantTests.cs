using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Long-run world health. Every bug of consequence in this project has come
    /// from a value behaving badly over years of simulated time rather than from
    /// a wrong line of arithmetic, so these tests run the whole world forward
    /// with every system wired and assert that nothing drifts somewhere absurd.
    ///
    /// They deliberately assert *bands*, not exact numbers: the point is to catch
    /// a stat that saturates, collapses, compounds or goes non-finite, without
    /// breaking every time balance is retuned.
    /// </summary>
    public class WorldInvariantTests
    {
        /// <summary>
        /// A fully wired game, exactly as the shipping game assembles one.
        ///
        /// **This must go through `SimulationPipeline.Wire` and nothing else.**
        /// It used to be a hand-copied third version of the monthly list and had
        /// already drifted by six systems — `CabinetLifecycle`, both halves of
        /// `AcquisitionSystem`, `AccessionSystem`, `SecessionSystem`,
        /// `TerritorySystem` and `Telemetry` were all absent. A world-health
        /// harness that omits systems is worse than no harness: it certifies as
        /// healthy a world nobody plays. Never re-expand this into a list.
        /// </summary>
        static TurnManager FullyWired(GameState state)
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            return turns;
        }

        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>Advance the world, clearing any crisis that blocks the turn.</summary>
        static void RunMonths(GameState state, TurnManager turns, int months)
        {
            for (int i = 0; i < months; i++)
            {
                state.commandPoints.current = 6;
                if (!turns.EndMonth())
                {
                    // A blocking crisis: take the first option, as an absent
                    // operator's staff eventually would, and continue.
                    while (state.activeCrises.Count > 0)
                        CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                    turns.EndMonth();
                }
            }
        }

        // ---------- the invariants ----------

        static void AssertFinite(float value, string what, string where)
        {
            Assert.IsFalse(float.IsNaN(value), $"{where}: {what} is NaN.");
            Assert.IsFalse(float.IsInfinity(value), $"{where}: {what} is infinite.");
        }

        static void AssertPercent(float value, string what, string where)
        {
            AssertFinite(value, what, where);
            Assert.GreaterOrEqual(value, 0f, $"{where}: {what} fell below 0 ({value:F2}).");
            Assert.LessOrEqual(value, 100f, $"{where}: {what} rose above 100 ({value:F2}).");
        }

        static void AssertCountrySane(CountryState c, string where)
        {
            where = $"{where} {c.id}";

            AssertPercent(c.governmentApproval, "approval", where);
            AssertPercent(c.stability, "stability", where);
            AssertPercent(c.nationalUnity, "unity", where);
            AssertPercent(c.warSupport, "war support", where);
            AssertPercent(c.warExhaustion, "war exhaustion", where);

            AssertPercent(c.pillars.military, "military pillar", where);
            AssertPercent(c.pillars.economy, "economy pillar", where);
            AssertPercent(c.pillars.intelligence, "intelligence pillar", where);
            AssertPercent(c.pillars.diplomacy, "diplomacy pillar", where);
            AssertPercent(c.pillars.government, "government pillar", where);

            AssertPercent(c.resources.energy, "energy", where);
            AssertPercent(c.resources.industrialCapacity, "industrial capacity", where);
            AssertPercent(c.resources.strategicMaterials, "strategic materials", where);
            AssertPercent(c.resources.foodSecurity, "food security", where);
            AssertFinite(c.resources.treasury, "treasury", where);
            AssertFinite(c.resources.manpower, "manpower", where);
            Assert.GreaterOrEqual(c.resources.manpower, 0f, $"{where}: manpower went negative.");

            AssertPercent(c.economy.confidence, "confidence", where);
            AssertFinite(c.economy.gdp, "gdp", where);
            AssertFinite(c.economy.growthRate, "growth", where);
            AssertFinite(c.economy.inflation, "inflation", where);
            AssertFinite(c.economy.marketIndex, "market index", where);
            Assert.Greater(c.economy.gdp, 0f, $"{where}: GDP collapsed to zero or below.");
            foreach (var sector in c.economy.sectors)
            {
                AssertPercent(sector.output, $"sector {sector.sector} output", where);
                AssertPercent(sector.health, $"sector {sector.sector} health", where);
            }

            AssertPercent(c.military.logistics, "logistics", where);
            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
            {
                var force = c.military.Get(branch);
                AssertPercent(force.strength, $"{branch} strength", where);
                AssertPercent(force.readiness, $"{branch} readiness", where);
                AssertPercent(force.supply, $"{branch} supply", where);
            }

            AssertPercent(c.counterIntel.counterIntelligence, "counter-intelligence", where);
            AssertPercent(c.government.militaryLoyalty, "military loyalty", where);
            AssertPercent(c.government.conspiracyLevel, "conspiracy", where);
            AssertPercent(c.government.eliteCohesion, "elite cohesion", where);
            AssertPercent(c.government.legislativeSupport, "legislative support", where);
        }

        [Test]
        public void ThirtyYears_NoStatEverLeavesItsRange()
        {
            foreach (int seed in new[] { 11, 4242, 90210 })
            {
                var state = WorldFactory.CreateWorld(seed, "USA");
                var turns = FullyWired(state);

                for (int month = 0; month < 360; month++)
                {
                    RunMonths(state, turns, 1);
                    string where = $"seed {seed} {state.date.DisplayString}";
                    foreach (var country in state.countries)
                        AssertCountrySane(country, where);
                }
            }
        }

        /// <summary>
        /// A market index is anchored to fundamentals; it must not compound away.
        /// An earlier build reached 1192 after a decade.
        /// </summary>
        [Test]
        public void ThirtyYears_MarketsStayAnchoredToFundamentals()
        {
            var state = WorldFactory.CreateWorld(777, "USA");
            var turns = FullyWired(state);
            RunMonths(state, turns, 360);

            foreach (var country in state.countries)
            {
                Assert.Greater(country.economy.marketIndex, 5f,
                    $"{country.id} market index collapsed to {country.economy.marketIndex:F1}.");
                Assert.Less(country.economy.marketIndex, 500f,
                    $"{country.id} market index compounded to {country.economy.marketIndex:F1}.");
            }
        }

        /// <summary>
        /// The regression that mattered: a state that holds a posture must not
        /// hollow out its own army by standing still — and neither must the AI,
        /// which is what made the whole world's forces quietly empty.
        /// </summary>
        [Test]
        public void ThirtyYears_TheWorldsArmiesDoNotHollowOut()
        {
            var state = WorldFactory.CreateWorld(2024, "USA");
            var turns = FullyWired(state);
            RunMonths(state, turns, 360);

            var hollow = new List<string>();
            foreach (var country in state.countries)
            {
                float meanSupply = (country.military.ground.supply
                                    + country.military.air.supply
                                    + country.military.naval.supply) / 3f;
                if (meanSupply < 20f) hollow.Add($"{country.id} {meanSupply:F0}");
            }

            Assert.IsEmpty(hollow,
                "States with no sustainment after 30 years (supply should settle at an "
                + "equilibrium, never drain to nothing): " + string.Join(", ", hollow));
        }

        /// <summary>
        /// Nothing may saturate the whole world. If every country pins a stat to
        /// its ceiling or floor, that stat has stopped being a meaningful choice.
        /// </summary>
        [Test]
        public void ThirtyYears_NoStatSaturatesAcrossTheWholeWorld()
        {
            var state = WorldFactory.CreateWorld(31337, "USA");
            var turns = FullyWired(state);
            RunMonths(state, turns, 360);

            void AssertNotUniversallyPinned(string what, Func<CountryState, float> read)
            {
                int pinned = 0;
                foreach (var country in state.countries)
                {
                    float value = read(country);
                    if (value >= 99.5f || value <= 0.5f) pinned++;
                }
                Assert.Less(pinned, state.countries.Count,
                    $"Every country in the world has {what} pinned at its limit — "
                    + "it is no longer a variable.");
            }

            AssertNotUniversallyPinned("military pillar", c => c.pillars.military);
            AssertNotUniversallyPinned("economy pillar", c => c.pillars.economy);
            AssertNotUniversallyPinned("intelligence pillar", c => c.pillars.intelligence);
            AssertNotUniversallyPinned("diplomacy pillar", c => c.pillars.diplomacy);
            AssertNotUniversallyPinned("government pillar", c => c.pillars.government);
            AssertNotUniversallyPinned("stability", c => c.stability);
            AssertNotUniversallyPinned("industrial capacity", c => c.resources.industrialCapacity);
            AssertNotUniversallyPinned("energy", c => c.resources.energy);
            AssertNotUniversallyPinned("counter-intelligence", c => c.counterIntel.counterIntelligence);
        }

        /// <summary>
        /// Unbounded collections are a save-size and performance problem in a game
        /// whose premise is decades-long saves.
        /// </summary>
        [Test]
        public void ThirtyYears_NoCollectionGrowsWithoutBound()
        {
            var state = WorldFactory.CreateWorld(5150, "USA");
            var turns = FullyWired(state);
            RunMonths(state, turns, 360);

            Assert.Less(state.notifications.Count, 500,
                $"Notifications accumulated to {state.notifications.Count} — they must be trimmed.");
            Assert.Less(state.estimates.Count, 2000,
                $"Estimates accumulated to {state.estimates.Count}.");
            Assert.Less(state.confrontations.Count, 400,
                $"Confrontations accumulated to {state.confrontations.Count}.");

            // The chronicle is meant to grow — it is the point of a long save —
            // but it should be a readable history, not a per-month log dump.
            Assert.Less(state.chronicle.Count, 4000,
                $"Chronicle reached {state.chronicle.Count} entries in 30 years "
                + $"({state.chronicle.Count / 360f:F1} per month); it is logging, not chronicling.");

            float megabytes = SaveSystem.ToJson(state).Length / 1_000_000f;
            Assert.Less(megabytes, 12f, $"A 30-year save is {megabytes:F1} MB.");
        }

        /// <summary>
        /// A save must resume identically. This runs the same world two ways —
        /// straight through, and interrupted by a save/load — and requires the
        /// results to be indistinguishable.
        /// </summary>
        [Test]
        public void ASaveResumesIndistinguishablyFromNeverHavingStopped()
        {
            GameState Straight()
            {
                var state = WorldFactory.CreateWorld(606, "USA");
                RunMonths(state, FullyWired(state), 120);
                return state;
            }

            GameState Interrupted()
            {
                var state = WorldFactory.CreateWorld(606, "USA");
                RunMonths(state, FullyWired(state), 60);

                var reloaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
                RunMonths(reloaded, FullyWired(reloaded), 60);
                return reloaded;
            }

            Assert.AreEqual(SaveSystem.ToJson(Straight()), SaveSystem.ToJson(Interrupted()),
                "Saving and reloading changed the future — some live state is not in GameState.");
        }

        [Test]
        public void TheSameSeed_ProducesTheSameThirtyYears()
        {
            GameState Run()
            {
                var state = WorldFactory.CreateWorld(4096, "USA");
                RunMonths(state, FullyWired(state), 360);
                return state;
            }

            Assert.AreEqual(SaveSystem.ToJson(Run()), SaveSystem.ToJson(Run()));
        }

        /// <summary>
        /// The player can be posted anywhere (spec 11). Every authored country
        /// must survive being the player's post for a decade.
        /// </summary>
        [Test]
        public void EveryAuthoredCountry_SurvivesADecadeAsThePlayersPost()
        {
            var roster = WorldFactory.CreateDebugWorld(1);
            var ids = new List<string>();
            foreach (var country in roster.countries) ids.Add(country.id);

            foreach (string id in ids)
            {
                var state = WorldFactory.CreateWorld(808, id);
                var turns = FullyWired(state);
                RunMonths(state, turns, 120);

                foreach (var country in state.countries)
                    AssertCountrySane(country, $"posted to {id}");

                Assert.IsNotNull(state.PlayerCountry, $"Posted to {id}: the player's country vanished.");
                Assert.IsTrue(state.PlayerCountry.isPlayer);
            }
        }

        /// <summary>
        /// An unattended world must still be dangerous. `CLAUDE.md` records the
        /// AI aggression tuning as producing roughly 2–3 confrontations a decade;
        /// a world where nobody ever moves on anybody is a world with no stakes,
        /// and nothing previously asserted this.
        /// </summary>
        [Test]
        public void AnUnattendedWorld_StillProducesConfrontations()
        {
            var counts = new List<string>();
            int totalConfrontations = 0;

            foreach (int seed in new[] { 101, 202, 303 })
            {
                var state = WorldFactory.CreateWorld(seed, "USA");
                state.difficulty = Difficulty.Challenging;
                var turns = FullyWired(state);
                RunMonths(state, turns, 360);

                totalConfrontations += state.confrontations.Count;
                counts.Add($"seed {seed}: {state.confrontations.Count}");
            }

            Assert.Greater(totalConfrontations, 6,
                "Three unattended thirty-year worlds produced almost no confrontations "
                + $"({string.Join(", ", counts)}). The world has no stakes.");
        }

        /// <summary>
        /// Standard difficulty is a gentler world, not an inert one. Difficulty is
        /// meant to change AI *reasoning quality* (GDD §24.3) — action budget and
        /// planning horizon — not whether anything ever happens.
        /// </summary>
        [Test]
        public void EvenTheGentlestWorld_IsNotCompletelyInert()
        {
            int confrontations = 0;
            var counts = new List<string>();

            foreach (int seed in new[] { 404, 505, 606, 707 })
            {
                var state = WorldFactory.CreateWorld(seed, "USA");
                state.difficulty = Difficulty.Standard;
                var turns = FullyWired(state);
                RunMonths(state, turns, 360);

                confrontations += state.confrontations.Count;
                counts.Add($"seed {seed}: {state.confrontations.Count}");
            }

            Assert.Greater(confrontations, 0,
                "Four unattended thirty-year worlds at Standard difficulty produced "
                + $"not one confrontation ({string.Join(", ", counts)}). Difficulty "
                + "should change how well the AI reasons, not whether it acts.");
        }

        /// <summary>
        /// Reports world health for a designer to eyeball. Not an assertion —
        /// the numbers that look wrong here become the next test.
        /// </summary>
        [Test]
        public void Report_ThirtyYearWorldHealth()
        {
            GameLog.MirrorToUnityConsole = true;
            var state = WorldFactory.CreateWorld(20260820, "USA");
            var turns = FullyWired(state);
            RunMonths(state, turns, 360);

            var report = new System.Text.StringBuilder();
            report.AppendLine();
            report.AppendLine("=== 30-YEAR WORLD HEALTH (seed 20260820) ===");
            report.AppendLine("ID    MIL  ECO  INT  DIP  GOV | STAB  GDP     MKT   SUPPLY  TREASURY");

            foreach (var c in state.countries)
            {
                float supply = (c.military.ground.supply + c.military.air.supply
                                + c.military.naval.supply) / 3f;
                report.AppendLine(
                    $"{c.id,-5} {c.pillars.military,4:F0} {c.pillars.economy,4:F0} " +
                    $"{c.pillars.intelligence,4:F0} {c.pillars.diplomacy,4:F0} {c.pillars.government,4:F0} | " +
                    $"{c.stability,5:F0} {c.economy.gdp,7:F0} {c.economy.marketIndex,5:F0} " +
                    $"{supply,7:F0} {c.resources.treasury,9:F0}");
            }

            report.AppendLine();
            report.AppendLine($"confrontations: {state.confrontations.Count}   " +
                              $"settlements: {state.settlements.Count}   " +
                              $"treaties: {state.treaties.Count}   " +
                              $"chronicle: {state.chronicle.Count}   " +
                              $"notifications: {state.notifications.Count}   " +
                              $"estimates: {state.estimates.Count}");
            report.AppendLine($"save size: {SaveSystem.ToJson(state).Length / 1000f:F0} KB");

            int negativeTreasury = 0;
            foreach (var c in state.countries) if (c.resources.treasury < 0f) negativeTreasury++;
            report.AppendLine($"countries with negative treasury: {negativeTreasury}/{state.countries.Count}");

            GameLog.Info("REPORT", report.ToString());
        }
    }
}

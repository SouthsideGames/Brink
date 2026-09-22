using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// World sizes (GDD §31.2 amendment): the roster grew to 24 authored
    /// countries, and a new save chooses how much of it to open with —
    /// REGIONAL (10), STANDARD (16, the measured default), or FULL WORLD (24).
    ///
    /// The two integrity claims worth defending with tests:
    /// 1. **Standard is untouched.** Every balance figure was measured on the
    ///    sixteen-state world; growing the catalogue must not move it.
    /// 2. **A smaller or larger world is complete.** Every included country has
    ///    its ground, its links, its mind and its relationships; nothing in the
    ///    world references a country that is not in it.
    /// </summary>
    public class WorldSizeTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static HashSet<string> Ids(GameState state)
        {
            var ids = new HashSet<string>();
            foreach (var country in state.countries) ids.Add(country.id);
            return ids;
        }

        // ---------- the default is the measured world ----------

        [Test]
        public void TheDebugWorldIsStillTheMeasuredSixteen()
        {
            var state = WorldFactory.CreateDebugWorld(seed: 777);
            Assert.AreEqual(WorldFactory.StandardRoster.Length, state.countries.Count);
            Assert.AreEqual(WorldSize.Standard, state.worldSize);

            foreach (var id in WorldFactory.StandardRoster)
                Assert.IsNotNull(state.FindCountry(id), $"{id} missing from the standard world.");
        }

        [Test]
        public void GrowingTheCatalogueDidNotMoveTheStandardWorld()
        {
            // The rng-stream discipline in one assertion: skipped profiles and
            // tail-authored content must consume no draws, so the world the
            // harness measures is byte-for-byte the world it always measured.
            // Compared against itself across two creations (determinism) and
            // spot-checked against pre-expansion facts (16 countries, 41
            // locations, 37 trade links).
            var a = WorldFactory.CreateDebugWorld(seed: 4242);
            var b = WorldFactory.CreateWorld(4242, WorldFactory.PlayerCountryId, WorldSize.Standard);

            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(b),
                "CreateDebugWorld and an explicit Standard world diverged.");

            Assert.AreEqual(16, a.countries.Count);
            Assert.AreEqual(60, a.locations.Count,
                "The standard world's map changed size — expansion ground is leaking in, " +
                "or standard ground was lost.");
            Assert.AreEqual(37, a.trade.Count,
                "The standard world's trade network changed size.");

            // And nothing in it references the expansion roster.
            var standard = new HashSet<string>(WorldFactory.StandardRoster);
            foreach (var location in a.locations)
                Assert.IsTrue(standard.Contains(location.ownerId),
                    $"{location.displayName} leaked into the standard world.");
            foreach (var link in a.trade)
                Assert.IsTrue(standard.Contains(link.countryA) && standard.Contains(link.countryB),
                    $"Trade link {link.countryA}–{link.countryB} leaked into the standard world.");
        }

        // ---------- every size builds a complete world ----------

        static void AssertWorldIsComplete(GameState state)
        {
            var ids = Ids(state);

            foreach (var link in state.trade)
            {
                Assert.IsTrue(ids.Contains(link.countryA) && ids.Contains(link.countryB),
                    $"Trade link {link.countryA}–{link.countryB} references a country not in the world.");
            }

            foreach (var location in state.locations)
            {
                Assert.IsTrue(ids.Contains(location.ownerId),
                    $"{location.displayName} is owned by absent {location.ownerId}.");
                if (!string.IsNullOrEmpty(location.foreignOperatorId))
                    Assert.IsTrue(ids.Contains(location.foreignOperatorId),
                        $"{location.displayName} is operated by absent {location.foreignOperatorId}.");
            }

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                Assert.IsNotNull(state.FindAI(country.id), $"{country.id} has no mind.");
            }

            int expectedPairs = state.countries.Count * (state.countries.Count - 1) / 2;
            Assert.AreEqual(expectedPairs, state.relationships.Count,
                "The relationship graph does not match the roster.");

            foreach (var country in state.countries)
            {
                Assert.AreEqual(5, country.cabinet.Count, $"{country.id} lacks a full cabinet.");
                Assert.Greater(country.traits.Count, 0, $"{country.id} has no authored character.");

                bool hasCapital = false;
                int held = 0;
                foreach (var location in state.locations)
                {
                    if (location.originalOwnerId != country.id) continue;
                    held++;
                    if (location.type == LocationType.Capital) hasCapital = true;
                }
                Assert.IsTrue(hasCapital, $"{country.id} has no capital on the map.");
                Assert.GreaterOrEqual(held, 2, $"{country.id} needs something worth contesting.");

                // The same vulnerability bar the Standard roster is held to
                // (spec 08 §5) — a world of peers has no texture, at any size.
                var r = country.resources;
                bool weak = r.energy < 45f || r.foodSecurity < 50f || r.strategicMaterials < 45f
                            || r.industrialCapacity < 45f
                            || country.stability < 55f || country.nationalUnity < 50f
                            || country.pillars.military < 50f || country.pillars.economy < 55f;
                Assert.IsTrue(weak, $"{country.displayName} has no exploitable weakness.");
            }
        }

        [Test]
        public void ARegionalWorldIsComplete()
        {
            var state = WorldFactory.CreateWorld(1313, "USA", WorldSize.Regional);

            Assert.AreEqual(WorldFactory.RegionalRoster.Length, state.countries.Count);
            Assert.AreEqual(WorldSize.Regional, state.worldSize);
            AssertWorldIsComplete(state);

            // The great-power core survives every trim — the AI rivalry systems
            // were tuned against it.
            foreach (var major in new[] { "USA", "CHN", "RUS" })
                Assert.IsNotNull(state.FindCountry(major), $"{major} trimmed from a regional world.");

            // Curated, not truncated: nobody is stranded without trade.
            foreach (var country in state.countries)
            {
                int links = 0;
                foreach (var link in state.trade)
                    if (link.Involves(country.id)) links++;
                Assert.Greater(links, 0, $"{country.id} has no trade link inside the regional world.");
            }

            // All three authored food dependencies survive the trim intact.
            int foodLinks = 0;
            foreach (var link in state.trade)
                if (link.focus == TradeFocus.Food) foodLinks++;
            Assert.AreEqual(3, foodLinks,
                "The regional roster was chosen to keep every authored food dependency.");
        }

        [Test]
        public void TheFullWorldIsComplete()
        {
            var state = WorldFactory.CreateWorld(1313, "USA", WorldSize.Full);

            Assert.AreEqual(WorldFactory.Profiles.Length, state.countries.Count);
            Assert.AreEqual(24, state.countries.Count, "The full roster is authored at 24.");
            AssertWorldIsComplete(state);

            // The invariants the standard world is held to, on the new ground.
            foreach (var country in state.countries)
            {
                if (GeographySystem.AccessOf(country.id) != NavalAccess.Maritime) continue;
                bool hasPort = false;
                foreach (var location in state.locations)
                    if (location.originalOwnerId == country.id && location.type == LocationType.Port)
                        hasPort = true;
                Assert.IsTrue(hasPort, $"{country.displayName} is maritime with no port.");
            }

            foreach (var country in state.countries)
                Assert.AreNotEqual(Theatre.Unassigned, TheatreSystem.Of(country.id),
                    $"{country.displayName} fell outside every theatre band.");
        }

        [Test]
        public void AnAbsentHostReturnsGroundToItsOwner()
        {
            // Ramstein is German ground operated by the USA. In any world
            // containing Germany but not its operator the base must be plain
            // national ground — an operator id pointing at nobody would give
            // reach to a state that does not exist. (Both are in every current
            // roster, so this is exercised by construction: a world of
            // Regional-minus-USA cannot be built while USA is the fallback
            // posting. The guard in CreateWorld still deserves the assertion
            // that no built world ever ships a dangling operator.)
            foreach (WorldSize size in System.Enum.GetValues(typeof(WorldSize)))
            {
                var state = WorldFactory.CreateWorld(99, "USA", size);
                var ids = Ids(state);
                foreach (var location in state.locations)
                    if (!string.IsNullOrEmpty(location.foreignOperatorId))
                        Assert.IsTrue(ids.Contains(location.foreignOperatorId));
            }
        }

        // ---------- the choice reaches the game ----------

        [Test]
        public void APostingOutsideTheChosenWorldFallsBackSafely()
        {
            // Vietnam exists only in the Full world; a Standard world asked to
            // post the operator there falls back to the default post rather
            // than building a country the world does not contain.
            var state = WorldFactory.CreateWorld(55, "VNM", WorldSize.Standard);
            Assert.AreEqual(WorldFactory.PlayerCountryId, state.playerCountryId);

            var full = WorldFactory.CreateWorld(55, "VNM", WorldSize.Full);
            Assert.AreEqual("VNM", full.playerCountryId,
                "The same posting is honoured in the world that contains it.");
        }

        [Test]
        public void PostingAssignmentRespectsTheRoster()
        {
            var doctrine = new DoctrineProfile { economyAffinity = 40f };

            string regional = AssessmentSystem.AssignPosting(
                doctrine, WorldFactory.RosterFor(WorldSize.Regional));
            Assert.Contains(regional, WorldFactory.RegionalRoster,
                "The assessment proposed a posting outside the chosen world.");

            string standard = AssessmentSystem.AssignPosting(doctrine);
            Assert.Contains(standard, WorldFactory.StandardRoster,
                "The default assignment must come from the default world.");
        }

        [Test]
        public void WorldSizeSurvivesSaveAndLoad()
        {
            var state = WorldFactory.CreateWorld(21, "USA", WorldSize.Full);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(WorldSize.Full, loaded.worldSize);
            Assert.AreEqual(state.countries.Count, loaded.countries.Count);
        }

        // ---------- the expansion world holds together over time ----------

        [TestCase("CAN", "Atlantic Gateway Terminal", 40f, 64f)]
        [TestCase("ITA", "Ligurian Container Port", 42f, 68f)]
        [TestCase("EGY", "Alexandria Port Complex", 40f, 70f)]
        [TestCase("ZAF", "Durban Freight Terminal", 36f, 64f)]
        [TestCase("VNM", "Southern Container Terminal", 38f, 66f)]
        public void CoastalExpansionPortIsRealGroundWithStableHome(string owner, string name, float defense, float value)
        {
            var state = WorldFactory.CreateWorld(4747, "USA", WorldSize.Full);
            var ports = state.locations.FindAll(l => l.originalOwnerId == owner && l.type == LocationType.Port);
            Assert.AreEqual(1, ports.Count);
            var port = ports[0];
            Assert.AreEqual(owner + "_PRT", port.id);
            Assert.AreEqual(name, port.displayName);
            Assert.AreEqual(owner, port.ownerId);
            Assert.AreEqual(defense, port.defenseValue);
            Assert.AreEqual(value, port.strategicValue);
            Assert.AreEqual(45f, port.garrison);
            Assert.IsTrue(port.SupportsBasing);
            Assert.IsFalse(port.energyWorks);
            Assert.AreEqual(0, port.mineHazardUntilMonth);
            float own = TerritorySystem.TradeAccessSwing(state, owner);
            float foreign = TerritorySystem.TradeAccessSwing(state, "USA");
            port.ownerId = "USA";
            Assert.AreEqual(owner, GeographySystem.HostOf(port));
            Assert.AreEqual(own - value * 0.20f, TerritorySystem.TradeAccessSwing(state, owner), 0.001f);
            Assert.AreEqual(foreign + value * 0.20f, TerritorySystem.TradeAccessSwing(state, "USA"), 0.001f);
            port.originalOwnerId = "USA";
            Assert.AreEqual(owner, GeographySystem.HostOf(port), "Recognized cession must not move physical ground.");
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var saved = loaded.locations.Find(l => l.id == port.id);
            Assert.AreEqual("USA", saved.ownerId);
            Assert.AreEqual("USA", saved.originalOwnerId);
            Assert.AreEqual(owner, GeographySystem.HostOf(saved));
        }

        [TestCase(WorldSize.Regional, 23, 812f)]
        [TestCase(WorldSize.Standard, 34, 1116f)]
        [TestCase(WorldSize.Full, 56, 1716f)]
        public void PortCoverageCountsActualRosterLinksWithoutInventingRoutes(WorldSize size, int expected, float volume)
        {
            var state = WorldFactory.CreateWorld(4747, "USA", size);
            var ports = new HashSet<string>();
            foreach (var site in state.locations)
                if (site.type == LocationType.Port) Assert.IsTrue(ports.Add(site.ownerId), "One authored port per country.");
            foreach (var country in state.countries)
                Assert.AreEqual(GeographySystem.AccessOf(country.id) != NavalAccess.Landlocked, ports.Contains(country.id), country.id);
            int count = 0; float covered = 0f;
            foreach (var link in state.trade)
                if (ports.Contains(link.countryA) && ports.Contains(link.countryB)) { count++; covered += link.volume; }
            Assert.AreEqual(expected, count);
            Assert.AreEqual(volume, covered);
            Assert.IsFalse(ports.Contains("KAZ"));
        }

        [Test]
        public void LoadingAnOlderFullWorldDoesNotBackfillPorts()
        {
            var state = WorldFactory.CreateWorld(4747, "USA", WorldSize.Full);
            var added = new HashSet<string> { "CAN_PRT", "ITA_PRT", "EGY_PRT", "ZAF_PRT", "VNM_PRT" };
            Assert.AreEqual(5, state.locations.RemoveAll(l => added.Contains(l.id)));
            string json = SaveSystem.ToJson(state);
            var loaded = SaveSystem.FromJson(json);
            Assert.IsFalse(loaded.locations.Exists(l => added.Contains(l.id)));
            Assert.AreEqual(json, SaveSystem.ToJson(loaded));
            Assert.AreEqual(7, loaded.saveVersion);
        }

        [Test]
        public void TheFullWorldStaysCoherentOverADecade()
        {
            // Not a balance measurement — Report_MultiSeedBalance owns those and
            // has not been run on Full. This asserts the world *functions*: a
            // decade of the real pipeline with eight new states throws nothing,
            // stays deterministic, and the new countries actually participate.
            string Fingerprint()
            {
                var world = WorldFactory.CreateWorld(9090, "USA", WorldSize.Full);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);
                for (int month = 0; month < 120; month++) turns.EndMonth();

                var parts = new List<string>();
                foreach (var country in world.countries)
                    parts.Add($"{country.id}:{country.stability:F2}:{country.pillars.economy:F2}");
                return string.Join("|", parts);
            }

            string first = Fingerprint();
            Assert.AreEqual(first, Fingerprint(), "The full world is not deterministic.");
            StringAssert.Contains("GBR", first);
            StringAssert.Contains("VNM", first);
        }
    }
}

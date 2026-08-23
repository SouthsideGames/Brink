using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AsciiWorldMapTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 9100);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>
        /// The map is no longer a fixed 78×21 — it renders into whatever grid the
        /// panel can afford, which is what stopped it running off a phone. The
        /// invariant that still matters, and matters more, is that every row is
        /// exactly the width it was asked for: a single ragged row breaks the
        /// alignment of the whole chart.
        /// </summary>
        [Test]
        public void Map_RendersExactlyTheGridItIsGiven()
        {
            foreach (var (columns, rows) in new[] { (78, 21), (64, 17), (40, 11), (100, 23) })
            {
                string map = AsciiWorldMap.Render(state, null, columns, rows);
                var lines = map.Split('\n');

                Assert.AreEqual(rows, lines.Length, $"Expected {rows} rows.");
                foreach (var line in lines)
                    Assert.AreEqual(columns, line.Length,
                        $"A row was {line.Length} wide in a {columns}-column render — "
                        + "terminal alignment breaks on a single ragged row.");
            }
        }

        [Test]
        public void EveryCountry_AppearsOnTheMap()
        {
            string map = AsciiWorldMap.Render(state);

            foreach (var profile in WorldFactory.Profiles)
                StringAssert.Contains(profile.mapCode, map,
                    $"{profile.displayName} is not drawn anywhere on the map.");
        }

        [Test]
        public void CountryPositions_DoNotCollide()
        {
            // Each marker occupies four cells on one row; overlapping markers
            // would render as unreadable mush.
            var profiles = WorldFactory.Profiles;
            for (int i = 0; i < profiles.Length; i++)
            for (int j = i + 1; j < profiles.Length; j++)
            {
                var a = profiles[i];
                var b = profiles[j];
                if (a.mapY != b.mapY) continue;

                bool overlap = a.mapX < b.mapX + 4 && b.mapX < a.mapX + 4;
                Assert.IsFalse(overlap,
                    $"{a.displayName} and {b.displayName} overlap on the map at row {a.mapY}.");
            }
        }

        [Test]
        public void Positions_FitWithinTheGrid()
        {
            foreach (var profile in WorldFactory.Profiles)
            {
                Assert.GreaterOrEqual(profile.mapX, 0, profile.displayName);
                Assert.LessOrEqual(profile.mapX + 4, AsciiWorldMap.Width, profile.displayName);
                Assert.GreaterOrEqual(profile.mapY, 0, profile.displayName);
                Assert.Less(profile.mapY, AsciiWorldMap.Height, profile.displayName);
                Assert.AreEqual(2, profile.mapCode.Length, $"{profile.displayName} needs a two-letter code.");
            }
        }

        [Test]
        public void PlayerNation_IsMarkedDistinctly()
        {
            string map = AsciiWorldMap.Render(state);
            var profile = WorldFactory.FindProfile(state.playerCountryId);
            StringAssert.Contains($"[{profile.mapCode}]", map, "Our own post should be unmistakable.");
        }

        [Test]
        public void Markers_ReflectDiplomaticStanding()
        {
            // Make one state hostile and one a partner, then confirm the map says so.
            var hostile = state.FindRelationship(state.playerCountryId, "CHN");
            hostile.relations = 5f; hostile.trust = 5f; hostile.strategicAlignment = 5f;

            var partner = state.FindRelationship(state.playerCountryId, "AUS");
            partner.relations = 90f; partner.trust = 90f; partner.strategicAlignment = 90f;
            partner.threatPerceptionOfA = 0f; partner.threatPerceptionOfB = 0f;

            string map = AsciiWorldMap.Render(state);
            StringAssert.Contains("!CN", map, "A hostile state should be flagged on the map.");
            StringAssert.Contains("+AU", map, "A partner should be flagged on the map.");
        }

        [Test]
        public void Selection_IsMarkedAndDoesNotBreakTheGrid()
        {
            string map = AsciiWorldMap.Render(state, "RUS", AsciiWorldMap.Width, AsciiWorldMap.Height);
            StringAssert.Contains(">RU<", map);

            foreach (var line in map.Split('\n'))
                Assert.AreEqual(AsciiWorldMap.Width, line.Length);
        }

        [Test]
        public void Describe_UsesEstimatesForForeignCapability()
        {
            // With no collection, a foreign country's strength is unknown.
            string cold = AsciiWorldMap.Describe(state, "CHN");
            StringAssert.Contains("NO ASSESSMENT", cold,
                "Foreign capability must come through the intelligence layer (GDD §14).");

            // Our own nation is known exactly.
            string own = AsciiWorldMap.Describe(state, state.playerCountryId);
            StringAssert.Contains("POSTURE", own);
            Assert.IsFalse(own.Contains("NO ASSESSMENT"), "We know our own strength.");
        }

        [Test]
        public void Describe_ShowsPublicFactsExactly()
        {
            var china = state.FindCountry("CHN");
            string description = AsciiWorldMap.Describe(state, "CHN");

            StringAssert.Contains(china.government.leader.name, description,
                "Who governs is public information (GDD §14).");
            StringAssert.Contains("DOMINANT-PARTY STATE", description);
        }

        [Test]
        public void Describe_ShowsOccupiedTerritoryToEveryone()
        {
            var lane = state.FindLocation("CONTESTED_LANE");
            lane.ownerId = state.playerCountryId;

            string ours = AsciiWorldMap.Describe(state, state.playerCountryId);
            StringAssert.Contains("occupied", ours.ToLowerInvariant());

            string theirs = AsciiWorldMap.Describe(state, lane.originalOwnerId);
            StringAssert.Contains("LOST", theirs, "The former owner's loss should be visible.");
        }

        [Test]
        public void Map_SurvivesAFullWorldSimulation()
        {
            var turns = new TurnManager(state);
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;

            for (int i = 0; i < 120; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            string map = AsciiWorldMap.Render(state, "IND", AsciiWorldMap.Width, AsciiWorldMap.Height);
            foreach (var line in map.Split('\n'))
                Assert.AreEqual(AsciiWorldMap.Width, line.Length,
                    "A decade of war and regime change must not corrupt the map.");

            foreach (var profile in WorldFactory.Profiles)
                Assert.IsNotEmpty(AsciiWorldMap.Describe(state, profile.id));
        }
    }
}

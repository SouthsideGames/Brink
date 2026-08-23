using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Geography and reach (GDD §16). Before this, any state could assault any
    /// location on earth at identical cost and identical odds, and a navy — the
    /// most expensive thing a country can buy — bought nothing that distance
    /// made valuable.
    /// </summary>
    public class GeographySystemTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8080);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- the map is a cylinder ----------

        [Test]
        public void LongitudeWraps()
        {
            // The United States is closer to Japan across the Pacific than to
            // China the long way round. A model that measures raw column
            // distance gets the entire Pacific backwards.
            float toJapan = GeographySystem.DistanceBetween("USA", "JPN");
            float toChina = GeographySystem.DistanceBetween("USA", "CHN");

            Assert.Less(toJapan, toChina,
                "Measured without wraparound, every trans-Pacific relationship is inverted.");
        }

        [Test]
        public void DistanceIsSymmetric()
        {
            Assert.AreEqual(GeographySystem.DistanceBetween("BRA", "NGA"),
                            GeographySystem.DistanceBetween("NGA", "BRA"), 0.001f);
        }

        [Test]
        public void NeighboursAreCloserThanStrangers()
        {
            Assert.Less(GeographySystem.DistanceBetween("DEU", "POL"),
                        GeographySystem.DistanceBetween("DEU", "AUS"),
                        "Germany and Poland are neighbours; Australia is not.");
            Assert.Less(GeographySystem.DistanceBetween("USA", "MEX"),
                        GeographySystem.DistanceBetween("USA", "IDN"));
        }

        // ---------- reach ----------

        [Test]
        public void ANavyBuysDistance()
        {
            var country = state.PlayerCountry;
            country.military.naval.strength = 10f;
            country.military.naval.readiness = 10f;
            country.military.naval.supply = 10f;
            float coastal = GeographySystem.ProjectionRange(state, country.id);

            country.military.naval.strength = 95f;
            country.military.naval.readiness = 95f;
            country.military.naval.supply = 95f;
            float bluewater = GeographySystem.ProjectionRange(state, country.id);

            Assert.Greater(bluewater, coastal,
                "A fleet is the main thing that buys reach — that is the strategic argument for one.");
        }

        [Test]
        public void FightingNextDoorIsAlwaysFullStrength()
        {
            // A small power at home must not be penalized. Fighting near home is
            // the one advantage a smaller state reliably has.
            var mexico = state.FindCountry("MEX");
            mexico.military.naval.strength = 5f;
            mexico.military.logistics = 5f;

            Assert.AreEqual(1f, GeographySystem.ReachFactorTo(state, "MEX", "USA"), 0.001f,
                "A neighbour is inside anyone's reach.");
        }

        [Test]
        public void FightingAcrossTheWorldCostsStrength()
        {
            var mexico = state.FindCountry("MEX");
            mexico.military.naval.strength = 10f;
            mexico.military.naval.readiness = 20f;
            mexico.military.air.strength = 10f;
            mexico.military.logistics = 10f;

            float far = GeographySystem.ReachFactorTo(state, "MEX", "IDN");

            Assert.Less(far, 1f, "A regional power should not campaign across the planet at full weight.");
            Assert.GreaterOrEqual(far, GeographySystem.MinimumReach,
                "Distance must make a far campaign hard, never impossible — no hard geographic gates.");
        }

        [Test]
        public void ReachNeverFallsBelowTheFloor()
        {
            var country = state.PlayerCountry;
            country.military.naval.strength = 0f;
            country.military.air.strength = 0f;
            country.military.logistics = 0f;

            foreach (var other in state.countries)
            {
                float factor = GeographySystem.ReachFactorTo(state, country.id, other.id);
                Assert.GreaterOrEqual(factor, GeographySystem.MinimumReach);
                Assert.LessOrEqual(factor, 1f);
            }
        }

        // ---------- ground you hold abroad is a place you can fight from ----------

        [Test]
        public void CapturedGroundBecomesAForwardPosition()
        {
            var mexico = state.FindCountry("MEX");
            mexico.military.naval.strength = 10f;
            mexico.military.naval.readiness = 20f;
            mexico.military.logistics = 10f;

            float before = GeographySystem.ReachFactorTo(state, "MEX", "JPN");

            // Take something in Korea — next door to Japan.
            foreach (var location in state.locations)
                if (location.originalOwnerId == "KOR") { location.ownerId = "MEX"; break; }

            float after = GeographySystem.ReachFactorTo(state, "MEX", "JPN");

            Assert.Greater(after, before,
                "Holding ground near the objective is what makes a forward position worth taking.");
        }

        [Test]
        public void BasingRightsExtendReachWithoutConquest()
        {
            var mexico = state.FindCountry("MEX");
            mexico.military.naval.strength = 10f;
            mexico.military.naval.readiness = 20f;
            mexico.military.logistics = 10f;

            float before = GeographySystem.ReachFactorTo(state, "MEX", "JPN");

            // China, not Korea: Korea is authored with only a capital and an
            // industrial centre, so nothing there can host anyone.
            bool granted = false;
            foreach (var location in state.locations)
                if (location.originalOwnerId == "CHN" && location.SupportsBasing)
                {
                    location.foreignOperatorId = "MEX";
                    granted = true;
                    break;
                }
            Assert.IsTrue(granted, "Test needs a basing-capable location near the objective.");

            float after = GeographySystem.ReachFactorTo(state, "MEX", "JPN");

            Assert.Greater(after, before,
                "A partner's base is reach that did not have to be conquered — " +
                "the whole strategic point of a Transit commitment.");
        }

        [Test]
        public void TakingGroundMovesControlNotTheGround()
        {
            var location = state.FindLocation("CONTESTED_LANE") ?? state.locations[0];
            string physicalHome = GeographySystem.HostOf(location);

            location.ownerId = "AUS";

            Assert.AreEqual(physicalHome, GeographySystem.HostOf(location),
                "A captured port is still where it always was. Measuring it from the " +
                "conqueror's capital would let a state teleport its own geography.");
        }

        // ---------- it reaches the simulation ----------

        [Test]
        public void DistanceWeakensAnActualOperation()
        {
            float NearAndFar(string hostId)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 8080);
                var attacker = world.FindCountry("MEX");
                attacker.military.naval.strength = 10f;
                attacker.military.naval.readiness = 20f;
                attacker.military.air.strength = 10f;
                attacker.military.logistics = 10f;

                StrategicLocation target = null;
                foreach (var location in world.locations)
                    if (location.originalOwnerId == hostId && location.type != LocationType.Capital)
                    { target = location; break; }
                Assert.IsNotNull(target, $"No target location in {hostId}.");

                var confrontation = ConfrontationSystem.BeginBy(world, "MEX", hostId,
                    ConfrontationObjective.TerritorialConcession, target.id, PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(world, confrontation,
                    EscalationState.LimitedConflict, "MEX");

                var record = MilitarySystem.ResolveOperation(world, confrontation, "MEX", target,
                    OperationType.Assault, new OperationDirective(), new System.Random(4321));
                return record.reachFactor;
            }

            float nearby = NearAndFar("USA");
            float distant = NearAndFar("IDN");

            Assert.AreEqual(1f, nearby, 0.001f);
            Assert.Less(distant, nearby,
                "Reach has to reach the resolver, or it is another decorative field.");
        }

        [Test]
        public void AmbitionIsBoundedByReach()
        {
            // A government does not press a claim it has no way to prosecute.
            // Without this, regional powers picked fights across the planet and
            // geography was invisible in how the world actually behaved.
            var mexico = state.FindCountry("MEX");
            mexico.military.naval.strength = 8f;
            mexico.military.air.strength = 8f;
            mexico.military.logistics = 8f;

            float nearby = GeographySystem.ReachFactorTo(state, "MEX", "USA");
            float distant = GeographySystem.ReachFactorTo(state, "MEX", "IDN");

            Assert.Greater(nearby, distant,
                "A weak neighbour has to be a more attractive target than an equally " +
                "weak state on the other side of the world.");
        }

        [Test]
        public void AFailedDistantOperationSaysWhy()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 8080);
            var attacker = world.FindCountry("MEX");
            attacker.military.naval.strength = 5f;
            attacker.military.naval.readiness = 10f;
            attacker.military.air.strength = 5f;
            attacker.military.logistics = 5f;

            StrategicLocation target = null;
            foreach (var location in world.locations)
                if (location.originalOwnerId == "IDN" && location.type != LocationType.Capital)
                { target = location; break; }

            target.defenseValue = 100f;
            target.garrison = 100f;

            var confrontation = ConfrontationSystem.BeginBy(world, "MEX", "IDN",
                ConfrontationObjective.TerritorialConcession, target.id, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(world, confrontation,
                EscalationState.LimitedConflict, "MEX");

            var record = MilitarySystem.ResolveOperation(world, confrontation, "MEX", target,
                OperationType.Assault, new OperationDirective(), new System.Random(999));

            Assert.IsFalse(record.success, "Test needs a failure to inspect.");
            StringAssert.Contains("full weight", record.summary,
                "A player who cannot see that the force never arrived at full weight " +
                "reads a run of failures as unfair dice rather than as the map.");
        }
    }
}

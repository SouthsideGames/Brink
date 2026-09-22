using Brink.Core;
using Brink.Data;
using Brink.UI;
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
        public void RecognizedTransfersChangeTitleNotPhysicalGround()
        {
            var site = state.FindLocation("CHN_PRT");
            site.ownerId = "MEX";
            float occupiedReach = GeographySystem.EffectiveDistanceTo(state, "MEX", "JPN");
            TerritorySystem.Cede(state, site, "MEX");
            Assert.AreEqual("MEX", site.originalOwnerId);
            Assert.IsFalse(site.IsOccupied, "Do not restore occupation costs to fix geography.");
            Assert.AreEqual("CHN", GeographySystem.HostOf(site));
            Assert.AreEqual(occupiedReach, GeographySystem.EffectiveDistanceTo(state, "MEX", "JPN"));
            Assert.Greater(GeographySystem.EffectiveDistanceTo(state, "USA", GeographySystem.HostOf(site)), 0f);
            TerritorySystem.Cede(state, site, "BRA");
            Assert.AreEqual("CHN", GeographySystem.HostOf(site));
            Assert.AreEqual("BRA", site.originalOwnerId);
        }

        [Test]
        public void AbsorptionPreservesEverySitesPhysicalHome()
        {
            ConquestSystem.Absorb(state, state.FindCountry("MEX"), state.FindCountry("CHN"));
            foreach (var id in new[] { "CHN_CAP", "CHN_PRT", "CHN_AIR", "CONTESTED_LANE" })
            {
                var site = state.FindLocation(id);
                Assert.AreEqual("MEX", site.ownerId);
                Assert.AreEqual("MEX", site.originalOwnerId);
                Assert.IsFalse(site.IsOccupied);
                Assert.AreEqual("CHN", GeographySystem.HostOf(site), id);
            }
        }

        CountryState FractureChina()
        {
            var parent = state.FindCountry("CHN");
            parent.government.inCivilConflict = true;
            parent.government.civilConflictMonthsElapsed = 12;
            parent.government.civilConflictMonthsRemaining = 12;
            parent.nationalUnity = 10f;
            parent.government.militaryLoyalty = 20f;
            var successor = SecessionSystem.Fracture(state, parent, new System.Random(7));
            Assert.IsNotNull(successor);
            return successor;
        }

        [Test]
        public void SuccessorDistanceIsGroundedInInheritedSitesInBothDirections()
        {
            var successor = FractureChina();
            float expected = GeographySystem.DistanceBetween("MEX", "CHN");
            Assert.Greater(expected, 0f);
            Assert.AreEqual(expected, GeographySystem.DistanceBetween(state, "MEX", successor.id));
            Assert.AreEqual(expected, GeographySystem.DistanceBetween(state, successor.id, "MEX"));
            foreach (var site in state.locations)
                if (site.originalOwnerId == successor.id)
                {
                    Assert.IsFalse(site.IsOccupied);
                    Assert.AreEqual("CHN", GeographySystem.HostOf(site));
                }
            // No zero-distance shortcut for a weak successor, as actor or target.
            foreach (var country in new[] { successor, state.FindCountry("MEX") })
            {
                country.military.naval.strength = 0;
                country.military.air.strength = 0;
                country.military.logistics = 0;
                country.technology = new TechnologyState();
            }
            Assert.Less(GeographySystem.ReachFactorTo(state, successor.id, "MEX"), 1f);
            Assert.Less(GeographySystem.ReachFactorTo(state, "MEX", successor.id), 1f);
        }

        [Test]
        public void LegacyTransferredSitesRecoverPositionWithoutMutatingTheSave()
        {
            // This is also the shape of v7 saves written before the repair.
            var site = state.FindLocation("CONTESTED_LANE");
            site.ownerId = site.originalOwnerId = "MEX";
            var successor = FractureChina();
            string json = SaveSystem.ToJson(state);
            var restored = SaveSystem.FromJson(json);
            Assert.AreEqual("CHN", GeographySystem.HostOf(restored.FindLocation(site.id)));
            Assert.AreEqual(GeographySystem.DistanceBetween(state, successor.id, "MEX"),
                GeographySystem.DistanceBetween(restored, successor.id, "MEX"));
            Assert.AreEqual(json, SaveSystem.ToJson(state));
            Assert.AreEqual(json, SaveSystem.ToJson(restored));
        }

        [Test]
        public void UnknownGeographyIsNotGlobalProximityAndCustomSitesRetainFallback()
        {
            Assert.AreEqual(float.PositiveInfinity, GeographySystem.DistanceBetween("UNKNOWN", "USA"));
            Assert.AreEqual(float.PositiveInfinity, GeographySystem.DistanceBetween(state, "USA", "UNKNOWN"));
            Assert.AreEqual(GeographySystem.MinimumReach, GeographySystem.ReachFactorTo(state, "USA", "UNKNOWN"));
            var custom = new StrategicLocation { id = "CUSTOM", ownerId = "MEX", originalOwnerId = "CHN" };
            Assert.AreEqual("CHN", GeographySystem.HostOf(custom));
            custom.originalOwnerId = null;
            Assert.AreEqual("MEX", GeographySystem.HostOf(custom));
        }

        [Test]
        public void SuccessorHomeSelectionIsDeterministicAndSurvivesOccupation()
        {
            var a = state.FindLocation("CHN_PRT");
            var b = state.FindLocation("BRA_PRT");
            a.originalOwnerId = b.originalOwnerId = "SUCCESSOR";
            a.strategicValue = b.strategicValue = 80;
            Assert.AreEqual("BRA", GeographySystem.PositionFor(state, "SUCCESSOR").id);
            state.locations.Reverse();
            Assert.AreEqual("BRA", GeographySystem.PositionFor(state, "SUCCESSOR").id);
            a.strategicValue = 81;
            Assert.AreEqual("CHN", GeographySystem.PositionFor(state, "SUCCESSOR").id);
            var capital = state.FindLocation("MEX_CAP");
            capital.originalOwnerId = "SUCCESSOR";
            capital.strategicValue = 1;
            capital.ownerId = "USA";
            Assert.AreEqual("MEX", GeographySystem.PositionFor(state, "SUCCESSOR").id);
        }

        [Test]
        public void ActualOperationUsesTheCededTargetsPhysicalHome()
        {
            var target = state.FindLocation("CHN_PRT");
            TerritorySystem.Cede(state, target, "BRA");
            var attacker = state.FindCountry("MEX");
            attacker.military.naval.strength = 0;
            attacker.military.air.strength = 0;
            attacker.military.logistics = 0;
            attacker.technology = new TechnologyState();
            float expected = GeographySystem.ReachFactorTo(state, "MEX", "CHN");
            Assert.Less(expected, 1f);
            Assert.AreNotEqual(expected, GeographySystem.ReachFactorTo(state, "MEX", "BRA"));
            var front = ConfrontationSystem.BeginBy(state, "MEX", "BRA",
                ConfrontationObjective.TerritorialConcession, target.id, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, front, EscalationState.LimitedConflict, "MEX");
            var record = MilitarySystem.ResolveOperation(state, front, "MEX", target,
                OperationType.Assault, new OperationDirective(), new System.Random(4321));
            Assert.AreEqual(expected, record.reachFactor);
        }

        [Test]
        public void HostedPositionsKeepTheirLocationAndPenaltyAfterCession()
        {
            var site = state.FindLocation("CHN_PRT");
            TerritorySystem.Cede(state, site, "BRA");
            site.foreignOperatorId = "MEX";
            float expected = GeographySystem.DistanceBetween("CHN", "JPN") + 2f;
            Assert.Less(expected, GeographySystem.DistanceBetween("MEX", "JPN"));
            Assert.AreEqual(expected, GeographySystem.EffectiveDistanceTo(state, "MEX", "JPN"));
            site.foreignOperatorId = "";
            Assert.Greater(GeographySystem.EffectiveDistanceTo(state, "MEX", "JPN"), expected);
        }

        [Test]
        public void SuccessorDisplacementUsesNearbyHostsNotMissingProfileZeroDistance()
        {
            var successor = FractureChina();
            state.countries.RemoveAll(c => c.id != successor.id && c.id != "BRA" && c.id != "JPN");
            state.confrontations.Clear();
            state.insurgencies.Clear();
            foreach (var c in state.countries)
            {
                c.warExhaustion = 0;
                c.livingStandards = 80;
                c.resources.foodSecurity = c.resources.foodEndowment;
                c.displacement.displaced = c.displacement.hosted = 0;
                c.displacement.bordersClosed = true;
            }
            successor.displacement.displaced = 20;
            state.FindCountry("BRA").displacement.bordersClosed = false;
            Assert.Greater(GeographySystem.DistanceBetween(state, successor.id, "BRA"),
                DisplacementSystem.ReachableDistance);
            Assert.AreEqual(6f, DisplacementSystem.PressureAtSource(state, successor), 0.001f,
                "A distant open border cannot absorb this successor's displaced people.");
            state.FindCountry("JPN").displacement.bordersClosed = false;
            Assert.AreEqual(0f, DisplacementSystem.PressureAtSource(state, successor));
            DisplacementSystem.MonthlyUpdate(state);
            Assert.AreEqual(0f, state.FindCountry("BRA").displacement.hosted);
            Assert.Greater(state.FindCountry("JPN").displacement.hosted, 0f);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void ChokepointMarkersDoNotMoveWhenTitleChanges(int width)
        {
            string before = AsciiWorldMap.Render(state, "USA", width, 21);
            TerritorySystem.Cede(state, state.FindLocation("CONTESTED_LANE"), "MEX");
            string json = SaveSystem.ToJson(state);
            string after = AsciiWorldMap.Render(state, "USA", width, 21);
            Assert.AreEqual(before, after, "Only site title changed, not national markers or physical geography.");
            foreach (var line in after.Split('\n')) Assert.AreEqual(width, line.Length);
            Assert.AreEqual(json, SaveSystem.ToJson(state));
        }

        [Test]
        public void OccupationMarkerStaysOnTheOccupiedGroundNotTheOccupiersCapital()
        {
            state.confrontations.Clear();
            var site = state.FindLocation("CHN_PRT");
            site.ownerId = "MEX";
            string first = AsciiMapModes.Render(state, "USA", WorldMapMode.Military, 104, 30);
            StringAssert.Contains("O", first);
            site.ownerId = "BRA";
            Assert.AreEqual(first, AsciiMapModes.Render(state, "USA", WorldMapMode.Military, 104, 30));
        }

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

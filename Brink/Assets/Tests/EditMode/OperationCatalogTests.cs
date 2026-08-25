using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The operation catalog, the availability gate, and the new verbs (GDD §19).
    ///
    /// The catalog exists because an operation's behaviour used to be spread
    /// across six switch statements, and a verb given a row in five of them was a
    /// differently-named assault that looked finished. At twenty-three verbs that
    /// stopped being a risk and became a certainty. Most of these tests are
    /// completeness guards: they fail the build when the enum and the catalog
    /// drift apart, which is the failure this whole structure exists to prevent.
    /// </summary>
    public class OperationCatalogTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8181);
            state.commandPoints.current = 90;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static IEnumerable<OperationType> AllTypes()
            => (OperationType[])Enum.GetValues(typeof(OperationType));

        StrategicLocation LocationIn(string countryId, LocationType type)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type == type) return location;
            return null;
        }

        StrategicLocation AnyEnemyLocation(string countryId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type != LocationType.Capital)
                    return location;
            return null;
        }

        // ---------- completeness ----------

        [Test]
        public void EveryOperationHasAProfile()
        {
            foreach (var type in AllTypes())
                Assert.IsNotNull(OperationCatalog.For(type),
                    $"{type} is in the enum with no catalog entry. It would resolve as a " +
                    "generic assault with a blank description and a default cost.");
        }

        [Test]
        public void EveryOperationIsDescribedAndNamed()
        {
            foreach (var type in AllTypes())
            {
                var profile = OperationCatalog.For(type);
                Assert.IsNotEmpty(profile.displayName, $"{type} has no label for its button.");
                Assert.IsNotEmpty(profile.description,
                    $"{type} appears as a button with no explanation of what it does.");
                Assert.Greater(profile.cpCost, 0, $"{type} is free, which cannot be right.");
            }
        }

        [Test]
        public void EveryOperationCanActuallyBeCarriedOut()
        {
            // A verb with no branch weight and no intelligence source has nothing
            // to fight with, so its odds would be zero however good the country is.
            foreach (var type in AllTypes())
            {
                var profile = OperationCatalog.For(type);
                if (profile.usesIntelligence) continue;
                Assert.Greater(profile.ground + profile.air + profile.naval, 0.01f,
                    $"{type} draws on no branch of the armed forces and no intelligence " +
                    "service, so nothing we build could ever make it succeed.");
            }
        }

        [Test]
        public void EverySuccessfulOperationChangesSomething()
        {
            // The failure this codebase keeps repeating in another costume: a verb
            // that resolves, prints a summary, and alters nothing in the world.
            foreach (var type in AllTypes())
            {
                if (type == OperationType.Withdraw) continue; // handled on its own path

                var world = WorldFactory.CreateDebugWorld(seed: 8181);
                var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "CHN",
                    ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(world, confrontation,
                    EscalationState.LimitedConflict, world.playerCountryId);

                var profile = OperationCatalog.For(type);
                StrategicLocation target = null;
                foreach (var location in world.locations)
                {
                    bool ours = location.ownerId == world.playerCountryId;
                    if (profile.targeting == OperationTargeting.OwnGround && !ours) continue;
                    if (profile.targeting == OperationTargeting.EnemyGround && ours) continue;
                    if (location.originalOwnerId != "CHN" && !ours) continue;
                    target = location;
                    break;
                }
                if (target == null) continue;

                string before = Snapshot(world, target);
                bool changed = false;
                for (int i = 0; i < 40 && !changed; i++)
                {
                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, type, new OperationDirective(),
                        new System.Random(i), 0f);
                    if (record != null && record.success && Snapshot(world, target) != before)
                        changed = true;
                }

                Assert.IsTrue(changed,
                    $"{type} succeeded and left the world byte-identical. It is decoration.");
            }
        }

        /// <summary>A coarse fingerprint of everything an operation might touch.</summary>
        static string Snapshot(GameState world, StrategicLocation target)
        {
            var us = world.PlayerCountry;
            var them = world.FindCountry(target.ownerId == us.id ? "CHN" : target.ownerId);
            float trade = 0f;
            foreach (var link in world.trade) trade += link.volume;

            return $"{target.garrison:F2}|{target.defenseValue:F2}|{target.pacification:F2}|" +
                   $"{target.ownerId}|{trade:F2}|{us.military.missileDefense:F2}|" +
                   $"{us.stability:F2}|{us.warSupport:F2}|{us.economy.confidence:F2}|" +
                   $"{us.pillars.diplomacy:F2}|" +
                   $"{them?.military.air.strength:F2}|{them?.military.naval.strength:F2}|" +
                   $"{them?.military.ground.readiness:F2}|{them?.resources.industrialCapacity:F2}|" +
                   $"{them?.government.militaryLoyalty:F2}|{them?.stability:F2}|" +
                   $"{them?.warSupport:F2}|{them?.resources.treasury:F2}";
        }

        // ---------- the availability gate ----------

        [Test]
        public void ALandlockedCountryCannotBeBlockaded()
        {
            // Kazakhstan is authored landlocked. Before naval access existed it
            // had a fleet at 0.75x its military score and could be blockaded,
            // mined and invaded from the sea like anyone else.
            var target = AnyEnemyLocation("KAZ");
            Assert.IsNotNull(target, "KAZ has no location to test against.");

            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", target,
                OperationType.NavalBlockade, out string reason));
            StringAssert.Contains("LANDLOCKED", reason);

            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", target, OperationType.MineWarfare));
            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", target, OperationType.SeaControl));
        }

        [Test]
        public void ALandlockedCountryHasNoNavyAtAll()
        {
            var kaz = state.FindCountry("KAZ");
            Assert.AreEqual(0f, kaz.military.naval.strength, 0.001f,
                "A fleet you cannot float is not a small fleet. It is no fleet.");

            var usa = state.FindCountry("USA");
            Assert.Greater(usa.military.naval.strength, 0f);
        }

        [Test]
        public void AMaritimePowerOutbuildsACoastalOneAtSea()
        {
            // Naval strength has to follow the coastline rather than the defence
            // budget, or "maritime archipelago" is a word in a comment.
            Assert.Greater(WorldFactory.NavalScaleFor(NavalAccess.Maritime),
                WorldFactory.NavalScaleFor(NavalAccess.Coastal));
            Assert.AreEqual(0f, WorldFactory.NavalScaleFor(NavalAccess.Landlocked), 0.0001f);
        }

        [Test]
        public void WithoutAFleetTheNavalVerbsAreRefused()
        {
            var player = state.PlayerCountry;
            player.military.naval.strength = 0f;

            var target = AnyEnemyLocation("CHN");
            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", target,
                OperationType.NavalBlockade, out string reason));
            StringAssert.Contains("FLEET", reason);
        }

        [Test]
        public void ACountryWithNoAirForceCannotBeFoughtForTheSky()
        {
            var them = state.FindCountry("CHN");
            them.military.air.strength = 0f;

            var target = AnyEnemyLocation("CHN");
            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", target,
                OperationType.CounterAirCampaign, out string reason));
            StringAssert.Contains("AIR FORCE", reason);

            // But bombing the place they cannot defend is still perfectly possible.
            Assert.IsTrue(OperationCatalog.CanOrder(state, "USA", target, OperationType.AirStrike));
        }

        [Test]
        public void DefensiveVerbsOnlyWorkOnOurOwnGround()
        {
            var theirs = AnyEnemyLocation("CHN");
            var ours = LocationIn("USA", LocationType.Port);
            Assert.IsNotNull(ours);

            foreach (var type in new[]
                     {
                         OperationType.PreparedDefense, OperationType.MissileDefense,
                         OperationType.ConvoyEscort
                     })
            {
                Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", theirs, type),
                    $"{type} is defensive and must not be orderable against their ground.");
                Assert.IsTrue(OperationCatalog.CanOrder(state, "USA", ours, type),
                    $"{type} must be orderable on ground we hold.");
            }
        }

        [Test]
        public void CounterInsurgencyNeedsGroundWeTookRatherThanGroundWeOwn()
        {
            var home = LocationIn("USA", LocationType.Port);
            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", home,
                OperationType.CounterInsurgency, out string reason));
            StringAssert.Contains("OCCUPIED", reason);

            // Take something, and the verb becomes available on it.
            var seized = AnyEnemyLocation("MEX");
            seized.ownerId = "USA";
            Assert.IsTrue(OperationCatalog.CanOrder(state, "USA", seized,
                OperationType.CounterInsurgency));
        }

        [Test]
        public void SomeVerbsOnlyMakeSenseAgainstCertainPlaces()
        {
            var industrial = LocationIn("CHN", LocationType.IndustrialCenter);
            var capital = LocationIn("CHN", LocationType.Capital);
            Assert.IsNotNull(industrial);
            Assert.IsNotNull(capital);

            Assert.IsTrue(OperationCatalog.CanOrder(state, "USA", industrial,
                OperationType.StrategicBombing));
            Assert.IsFalse(OperationCatalog.CanOrder(state, "USA", industrial,
                OperationType.LeadershipStrike),
                "A leadership strike is aimed at a government, not a factory.");
            Assert.IsTrue(OperationCatalog.CanOrder(state, "USA", capital,
                OperationType.LeadershipStrike));
        }

        [Test]
        public void EveryObjectiveOffersSomethingToDo()
        {
            // A selected target with no available verb is a dead screen.
            foreach (var location in state.locations)
            {
                var available = OperationCatalog.AvailableAgainst(state, "USA", location);
                Assert.Greater(available.Count, 0,
                    $"Nothing at all can be ordered against {location.displayName}.");
            }
        }

        // ---------- the new verbs do what they claim ----------

        [Test]
        public void SuppressingIsCheaperThanFightingThroughTheWorks()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            Assert.Less(
                MilitarySystem.OperationCost(OperationType.SuppressDefenses)
                + MilitarySystem.OperationCost(OperationType.Assault),
                MilitarySystem.OperationCost(OperationType.AmphibiousAssault)
                + MilitarySystem.OperationCost(OperationType.Assault),
                "An opposed landing has to be the most expensive way in, or nobody " +
                "would ever bother going overland.");
        }

        [Test]
        public void PreparedDefenceMakesOurGroundHarderToTake()
        {
            var ours = LocationIn("USA", LocationType.Port);
            ours.defenseValue = 40f;
            float before = ours.defenseValue;

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);

            for (int i = 0; i < 12; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    ours, OperationType.PreparedDefense, new OperationDirective(),
                    new System.Random(i), 0f);

            Assert.Greater(ours.defenseValue, before,
                "The one verb that raises a defence value rather than lowering one.");
        }

        [Test]
        public void MissileDefenceBluntsWhatArrivesThroughTheAir()
        {
            float RateAgainstShield(float shield)
            {
                int wins = 0;
                for (int i = 0; i < 150; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 8181);
                    world.FindCountry("CHN").military.missileDefense = shield;

                    var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "CHN",
                        ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(world, confrontation,
                        EscalationState.LimitedConflict, world.playerCountryId);

                    StrategicLocation target = null;
                    foreach (var location in world.locations)
                        if (location.originalOwnerId == "CHN" && location.type != LocationType.Capital)
                        { target = location; break; }

                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, OperationType.AirStrike,
                        new OperationDirective(), new System.Random(i), 0f);
                    if (record.success) wins++;
                }
                return wins / 150f;
            }

            Assert.Greater(RateAgainstShield(0f), RateAgainstShield(100f),
                "A shield has to stop something, or building it is a tax on caution.");
        }

        [Test]
        public void AShieldDoesNotStopInfantry()
        {
            // The reason IsAerialDelivery exists. A missile shield that made a
            // ground assault harder would be a generic defence bonus wearing a
            // costume, and the verb would stop meaning anything.
            Assert.IsTrue(MilitarySystem.IsAerialDelivery(OperationType.AirStrike));
            Assert.IsTrue(MilitarySystem.IsAerialDelivery(OperationType.StrategicBombing));
            Assert.IsTrue(MilitarySystem.IsAerialDelivery(OperationType.LeadershipStrike));

            Assert.IsFalse(MilitarySystem.IsAerialDelivery(OperationType.Assault));
            Assert.IsFalse(MilitarySystem.IsAerialDelivery(OperationType.NavalBlockade));
            Assert.IsFalse(MilitarySystem.IsAerialDelivery(OperationType.CyberOperation));
        }

        [Test]
        public void ALeadershipStrikeCostsUsStandingEvenWhenItWorks()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.PolicyReversal, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            var capital = LocationIn("CHN", LocationType.Capital);
            var us = state.PlayerCountry;
            var them = state.FindCountry("CHN");
            float ourStandingBefore = us.pillars.diplomacy;
            float theirResolveBefore = them.warSupport;

            bool landed = false;
            for (int i = 0; i < 40 && !landed; i++)
            {
                var record = MilitarySystem.ResolveOperation(state, confrontation,
                    state.playerCountryId, capital, OperationType.LeadershipStrike,
                    new OperationDirective(), new System.Random(i), 0f);
                landed = record.success;
            }

            Assert.IsTrue(landed, "The strike never once succeeded, so this proves nothing.");
            Assert.Less(us.pillars.diplomacy, ourStandingBefore,
                "Decapitation has to cost us standing, or it is simply the best opening move.");
            Assert.Greater(them.warSupport, theirResolveBefore,
                "A country whose government is struck closes ranks. It does not fold.");
        }

        [Test]
        public void CyberRunsOnTheIntelligenceServiceNotTheArmy()
        {
            var player = state.PlayerCountry;
            player.military.ground.strength = 0f;
            player.military.air.strength = 0f;
            player.military.naval.strength = 0f;
            player.pillars.intelligence = 90f;

            Assert.Greater(MilitarySystem.OperationPower(player, OperationType.CyberOperation), 0.5f,
                "A country with no army and a good intelligence service must still be " +
                "dangerous in this one specific way.");
            Assert.AreEqual(0f, MilitarySystem.OperationPower(player, OperationType.Assault), 0.001f);
        }

        [Test]
        public void CyberCostsNoSoldiers()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            var target = AnyEnemyLocation("CHN");
            var us = state.PlayerCountry;
            float groundBefore = us.military.ground.strength;
            float airBefore = us.military.air.strength;

            for (int i = 0; i < 20; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.CyberOperation, new OperationDirective(),
                    new System.Random(i), 0f);

            Assert.AreEqual(groundBefore, us.military.ground.strength, 0.01f);
            Assert.AreEqual(airBefore, us.military.air.strength, 0.01f);
        }

        [Test]
        public void FortifyingOurOwnGroundIsNotAnActOfWar()
        {
            // Defensive work must not be the thing that starts the shooting, and
            // must not pay the surcharge for abruptness that opening fire does.
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Diplomatic);
            Assert.Less(confrontation.escalation, EscalationState.LimitedConflict);

            var ours = LocationIn("USA", LocationType.Port);
            ConfrontationSystem.LaunchOperationBy(state, confrontation, state.playerCountryId,
                ours.id, OperationType.PreparedDefense, new OperationDirective());

            Assert.Less(confrontation.escalation, EscalationState.LimitedConflict,
                "Digging in at home cannot escalate a standoff into a war.");

            Assert.AreEqual(MilitarySystem.OperationCost(OperationType.PreparedDefense),
                ConfrontationSystem.OperationCostFor(state, confrontation, OperationType.PreparedDefense),
                "No escalation premium on defensive work.");
        }

        // ---------- peacetime reachability ----------

        [Test]
        public void DefensiveProgrammesDoNotNeedAWar()
        {
            // These were reachable only from inside the confrontation console,
            // which meant a shield, a fortification and a convoy escort could
            // only be started once it was already too late for any of them to be
            // worth having.
            Assert.IsNull(state.ActiveConfrontation, "Test premise: we are at peace.");

            var ours = LocationIn("USA", LocationType.Port);
            ours.defenseValue = 40f;
            float before = ours.defenseValue;

            bool landed = false;
            for (int i = 0; i < 20 && !landed; i++)
            {
                var record = ConfrontationSystem.LaunchOperationBy(state, null,
                    state.playerCountryId, ours.id, OperationType.PreparedDefense,
                    new OperationDirective());
                Assert.IsNotNull(record, "The order was refused outright with no war on.");
                landed = record.success;
            }

            Assert.IsTrue(landed, "It never once succeeded.");
            Assert.Greater(ours.defenseValue, before,
                "The programme resolved in peacetime but changed nothing.");
        }

        [Test]
        public void EveryDefensiveVerbIsReachableInPeacetime()
        {
            Assert.IsNull(state.ActiveConfrontation);

            foreach (var profile in OperationCatalog.All)
            {
                if (profile.targeting != OperationTargeting.OwnGround) continue;
                Assert.IsFalse(ConfrontationSystem.RequiresConfrontation(profile.type),
                    $"{profile.type} is defensive but still demands a confrontation.");
            }
        }

        [Test]
        public void OffensiveVerbsStillNeedOne()
        {
            // The other half: peacetime reachability must not become a way to
            // conduct a war nobody has declared.
            Assert.IsNull(state.ActiveConfrontation);

            var theirs = AnyEnemyLocation("CHN");
            var record = ConfrontationSystem.LaunchOperationBy(state, null,
                state.playerCountryId, theirs.id, OperationType.Assault, new OperationDirective());

            Assert.IsNull(record,
                "An assault resolved with no confrontation open. Shooting at another state " +
                "has to be a confrontation by definition.");
            Assert.IsTrue(ConfrontationSystem.RequiresConfrontation(OperationType.Assault));
        }

        [Test]
        public void APeacetimeProgrammeGoesOnTheNationalRecordNotAWarDiary()
        {
            var ours = LocationIn("USA", LocationType.Port);
            int chronicleBefore = state.chronicle.Count;

            for (int i = 0; i < 20; i++)
                ConfrontationSystem.LaunchOperationBy(state, null, state.playerCountryId,
                    ours.id, OperationType.PreparedDefense, new OperationDirective());

            Assert.Greater(state.chronicle.Count, chronicleBefore,
                "Peacetime work left no trace anywhere. It is not part of any war, so the " +
                "chronicle is the only record it belongs in.");
        }

        /// <summary>
        /// Reported from play: "I am trying to improve the defence of a territory
        /// I took over but I keep failing with no direction on why."
        ///
        /// The report was empty because `RecordDefence` skipped
        /// `DefenseModel.Unopposed` entirely, so a failed programme produced an
        /// analysis with no defence factor — nothing to rank, and nothing for
        /// `Advice` to switch on. The one class in the game whose entire job is
        /// to say why could not.
        /// </summary>
        [Test]
        public void AFailedDefensiveProgrammeSaysWhatWouldChangeIt()
        {
            var ours = LocationIn("USA", LocationType.Port);

            // Read the analysis directly rather than rolling until something
            // fails: a test that needs a particular die is a flaky test, and the
            // defect is in what the report contains, not in how often it appears.
            MilitarySystem.ComputePowers(state, state.playerCountryId, ours,
                OperationType.PreparedDefense, new OperationDirective(), 0f,
                out _, out var analysis);

            Assert.IsNotNull(analysis.WorstAgainstAttacker(),
                "An unopposed programme recorded no factor working against it, so there is "
                + "nothing for the report to rank or advise on.");

            string report = analysis.Explain(success: false, attackerIsUs: true,
                targetName: ours.displayName, operationType: OperationType.PreparedDefense);

            StringAssert.Contains("WHAT WOULD CHANGE IT", report,
                "A failed programme explains nothing the operator can act on.");
        }

        /// <summary>
        /// A programme on our own ground is not a failed attack.
        ///
        /// The failure path was shared with offensive operations, so falling
        /// short while digging in at one of our own positions cost war support
        /// and was announced on the world wire as a public failure — which is
        /// what the operator actually saw: "UNITED STATES — Failed operation at
        /// Eastern Mediterranean Anchorage."
        /// </summary>
        [Test]
        public void FallingShortOnOurOwnGroundIsNotWorldNews()
        {
            var ours = LocationIn("USA", LocationType.Port);
            var player = state.PlayerCountry;

            // Guarantee the failure rather than fishing for one. A hollow force
            // cannot finish the work, which is the situation being tested.
            player.military.ground.SetStrength(1f);
            player.military.air.SetStrength(1f);
            player.military.naval.SetStrength(1f);
            player.military.ground.readiness = 1f;

            float supportBefore = player.warSupport;

            bool anyFailed = false;
            for (int i = 0; i < 40; i++)
            {
                var record = ConfrontationSystem.LaunchOperationBy(state, null,
                    state.playerCountryId, ours.id, OperationType.PreparedDefense,
                    new OperationDirective());
                if (record != null && !record.success) anyFailed = true;
            }

            Assert.IsTrue(anyFailed, "Nothing failed, so there is nothing to check.");
            Assert.GreaterOrEqual(player.warSupport, supportBefore,
                "Falling short on our own construction cost war support, as though we had " +
                "attacked somebody and lost.");

            foreach (var entry in state.chronicle)
                if (entry.countryId == player.id && entry.text.Contains("Failed operation at"))
                    Assert.Fail("A defensive programme was filed as a failed operation. It is "
                                + "the offensive wording, and it goes out on the world wire.");
        }

        /// <summary>
        /// On our own ground there is no opponent — so nothing may bill us twice.
        ///
        /// `defender` is whoever owns the target, which for a defensive
        /// programme is us. Every "and now charge the other side" line was
        /// charging us a second time: manpower twice over and war exhaustion
        /// twice over, so fortifying a position we held cost more than
        /// attacking one we did not.
        /// </summary>
        [Test]
        public void ADefensiveProgrammeChargesUsOnce()
        {
            var ours = LocationIn("USA", LocationType.Port);
            var player = state.PlayerCountry;

            float manpowerBefore = player.resources.manpower;
            float exhaustionBefore = player.warExhaustion;

            var record = ConfrontationSystem.LaunchOperationBy(state, null,
                state.playerCountryId, ours.id, OperationType.PreparedDefense,
                new OperationDirective());
            Assert.IsNotNull(record);

            float manpowerSpent = manpowerBefore - player.resources.manpower;
            float exhaustionAdded = player.warExhaustion - exhaustionBefore;

            // Our own losses only. `defenderLosses` belongs to whoever was
            // holding the ground against us, and on our own ground nobody was.
            Assert.AreEqual(record.attackerLosses * 4f, manpowerSpent, 0.01f,
                "The programme charged us for the defenders' losses as well as our own.");
            Assert.AreEqual(record.attackerLosses * 0.5f * 0.5f, exhaustionAdded, 0.01f,
                "The programme accrued war exhaustion for both sides of an engagement with " +
                "ourselves.");
        }

        [Test]
        public void PeacetimeProgrammesAreDeterministic()
        {
            // They draw from `state.actionSequence` rather than a confrontation's
            // operation count, so the usual reproducibility guarantee has to
            // survive the substitution.
            float RunOnce()
            {
                var world = WorldFactory.CreateDebugWorld(seed: 8181);
                StrategicLocation ours = null;
                foreach (var location in world.locations)
                    if (location.originalOwnerId == "USA" && location.type == LocationType.Port)
                    { ours = location; break; }

                float before = ours.defenseValue;
                for (int i = 0; i < 12; i++)
                    ConfrontationSystem.LaunchOperationBy(world, null, world.playerCountryId,
                        ours.id, OperationType.PreparedDefense, new OperationDirective());

                // Non-vacuity: two runs that both did nothing are also equal, and
                // that is exactly how the first version of this test passed while
                // the sequence counter was not advancing at all.
                Assert.AreNotEqual(before, ours.defenseValue,
                    "Twelve programmes changed nothing, so equality proves nothing.");
                return ours.defenseValue;
            }

            Assert.AreEqual(RunOnce(), RunOnce(), 0.0001f,
                "The same seed produced two different peacetime outcomes.");
        }

        [Test]
        public void AnOrderTheCatalogRefusesIsNotResolved()
        {
            // The gate and the simulation share one authority, so what the screen
            // offers and what the game accepts cannot disagree.
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "KAZ",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            var target = AnyEnemyLocation("KAZ");
            var record = ConfrontationSystem.LaunchOperationBy(state, confrontation,
                state.playerCountryId, target.id, OperationType.NavalBlockade,
                new OperationDirective());

            Assert.IsNull(record, "A blockade of a landlocked state must not resolve at all.");
        }
    }
}

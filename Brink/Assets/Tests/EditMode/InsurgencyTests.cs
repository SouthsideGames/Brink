using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Armed movements and the states that arm them (GDD §12, §17.1, §19).
    ///
    /// The tests are organised around the five rules the system is built on,
    /// because those are the things that would quietly stop being true: a rising
    /// nobody can commission, support that is a target rather than a store,
    /// deniability that wastes, a verb the world can actually use, and a sponsor
    /// who never ends up holding the ground.
    /// </summary>
    public class InsurgencyTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5171);
            state.commandPoints.current = 60;

            // The constitution is not what these tests are about. `AuthoritySystem`
            // has its own fixture; granting the brief here keeps a refusal there
            // from reading as a failure of this system.
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- helpers ----------

        /// <summary>Put a location under our occupation, unpacified — the classic case.</summary>
        StrategicLocation OccupyForeignGround()
        {
            foreach (var location in state.locations)
            {
                if (location.ownerId == state.playerCountryId) continue;
                if (location.type == LocationType.Capital) continue;

                location.ownerId = state.playerCountryId;
                location.pacification = 0f;
                location.garrison = 40f;
                return location;
            }
            return null;
        }

        Insurgency Plant(StrategicLocation location, InsurgencyCause cause, float support)
            => InsurgencySystem.Open(state, location, cause, support);

        // ---------- 1. nobody commissions one ----------

        [Test]
        public void AContentedWorldGrowsNoMovements()
        {
            // Make the world comfortable: fed, unified, nobody occupying anybody.
            foreach (var country in state.countries)
            {
                country.livingStandards = 70f;
                country.socialUnrest = 5f;
                country.publicGrievance = 5f;
                country.nationalUnity = 75f;
            }
            foreach (var location in state.locations)
            {
                location.ownerId = location.originalOwnerId;
                location.pacification = 100f;
            }

            for (int month = 0; month < 120; month++)
            {
                turns.EndMonth();

                // Keep it comfortable — the claim is about conditions, not about
                // whether the economy happens to hold up for a decade. Ownership
                // is restored too: an AI war that takes a province would create a
                // real occupation, and this test is not about that.
                foreach (var country in state.countries)
                {
                    country.livingStandards = 70f;
                    country.socialUnrest = 5f;
                    country.publicGrievance = 5f;
                    country.nationalUnity = 75f;
                }
                foreach (var location in state.locations)
                {
                    location.ownerId = location.originalOwnerId;
                    location.pacification = 100f;
                }
            }

            Assert.AreEqual(0, state.insurgencies.Count,
                "A world with no occupation, no hardship and no separatism produced armed "
                + "movements anyway. Risings must come out of conditions, or they are weather.");
        }

        [Test]
        public void OccupiedUnpacifiedGroundIsUnderPressureAndPacifiedGroundIsNot()
        {
            // The deterministic half of emergence. Whether the dice come up in
            // any particular decade is a separate question from whether the
            // condition registers at all, and only one of those can be asserted
            // without a stochastic run.
            var occupied = OccupyForeignGround();
            Assert.IsNotNull(occupied, "the fixture found no foreign ground to occupy");
            Assert.IsTrue(occupied.IsOccupied, "the fixture did not actually occupy anything");

            float resentful = InsurgencySystem.PressureAt(state, occupied,
                out InsurgencyCause cause);
            Assert.AreEqual(InsurgencyCause.Occupation, cause,
                "Unpacified foreign ground did not read as an occupation grievance.");
            Assert.Greater(resentful, 30f,
                "An entirely unpacified occupation registered too little pressure to ever "
                + "produce a movement.");

            occupied.pacification = 100f;
            float reconciled = InsurgencySystem.PressureAt(state, occupied, out _);
            Assert.Less(reconciled, resentful,
                "Pacifying occupied ground did not reduce the pressure on it, so the "
                + "counter-insurgency verb has nothing to buy.");
        }

        [Test]
        public void ADeeplyResentfulProvinceEventuallyOrganises()
        {
            var occupied = OccupyForeignGround();
            Assert.IsNotNull(occupied, "the fixture found no foreign ground to occupy");

            var player = state.PlayerCountry;

            bool rose = false;
            for (int month = 0; month < 240 && !rose; month++)
            {
                // A province held by a garrison, among a population with nothing
                // to lose. Held at that all the way through, because the claim is
                // "these conditions produce a rising", not "this seed does".
                occupied.ownerId = player.id;
                occupied.pacification = 0f;
                player.livingStandards = 10f;
                player.socialUnrest = 85f;
                player.publicGrievance = 90f;

                turns.EndMonth();
                rose = InsurgencySystem.At(state, occupied.id) != null;
            }

            Assert.IsTrue(rose,
                "Twenty years of unpacified occupation over a destitute, angry population "
                + "produced no movement at all. Occupation is supposed to be a standing "
                + "problem, not a line item.");
        }

        // ---------- 2. support is a target, not a store ----------

        [Test]
        public void RepairingTheCauseEndsTheMovement()
        {
            var occupied = OccupyForeignGround();
            var rising = Plant(occupied, InsurgencyCause.Occupation, 70f);
            rising.strength = 60f;

            // Hand the ground back. The cause is gone; the movement should not
            // outlive it, and nothing should have to shoot it.
            occupied.ownerId = occupied.originalOwnerId;

            float first = InsurgencySystem.SupportTargetFor(state, rising);
            Assert.AreEqual(0f, first, 0.01f,
                "An occupation movement still had a support target after the occupation ended.");

            for (int month = 0; month < 60; month++) turns.EndMonth();

            Assert.IsNull(InsurgencySystem.At(state, occupied.id),
                "A movement whose entire cause was removed was still fighting five years later. "
                + "Every value here must have a reachable recovery path.");
        }

        [Test]
        public void SupportFallsWhenConditionsImproveAndRisesWhenTheyWorsen()
        {
            var player = state.PlayerCountry;
            var home = state.locations.Find(l => l.ownerId == player.id
                                                 && l.type != LocationType.Capital);
            Assert.IsNotNull(home, "the fixture found no home ground");

            var rising = Plant(home, InsurgencyCause.Deprivation, 40f);

            player.livingStandards = 12f;
            player.publicGrievance = 80f;
            player.socialUnrest = 70f;
            float hungry = InsurgencySystem.SupportTargetFor(state, rising);

            player.livingStandards = 78f;
            player.publicGrievance = 6f;
            player.socialUnrest = 4f;
            float comfortable = InsurgencySystem.SupportTargetFor(state, rising);

            Assert.Greater(hungry, comfortable + 20f,
                "Deprivation and comfort produced near-identical support targets, so the "
                + "conditions are not actually driving the movement.");
            Assert.AreEqual(0f, comfortable, 0.01f,
                "A well-fed, contented, un-aggrieved population still sustained a rising.");
        }

        // ---------- 3. deniability wastes ----------

        [Test]
        public void ArmingThemCostsMoneyAndOurOwnStocks()
        {
            var occupied = OccupyForeignGround();

            // Somebody else's problem: give the ground to a third state so we can
            // legitimately arm the movement against them.
            var rival = state.countries.Find(c => !c.isPlayer && c.id != occupied.originalOwnerId);
            occupied.ownerId = rival.id;

            var rising = Plant(occupied, InsurgencyCause.Occupation, 45f);

            var player = state.PlayerCountry;
            player.resources.treasury = 20000f;
            player.military.ground.supply = 80f;

            float treasury = player.resources.treasury;
            float supply = player.military.ground.supply;

            Assert.IsTrue(InsurgencySystem.Support(state, turns, rising),
                "the order was refused: " + Why(rising));

            Assert.Less(player.resources.treasury, treasury,
                "Arming a movement cost the treasury nothing.");
            Assert.Less(player.military.ground.supply, supply,
                "Weapons sent are weapons we do not have. The stocks did not move.");
            Assert.AreEqual(state.playerCountryId, rising.sponsorId);
        }

        [Test]
        public void ASustainedProgrammeIsEventuallyAttributed()
        {
            var occupied = OccupyForeignGround();
            var rival = state.countries.Find(c => !c.isPlayer && c.id != occupied.originalOwnerId);
            occupied.ownerId = rival.id;
            rival.counterIntel.counterIntelligence = 70f;

            var rising = Plant(occupied, InsurgencyCause.Occupation, 55f);
            rising.sponsorId = state.playerCountryId;
            rising.exposure = 40f;

            for (int month = 0; month < 240 && !rising.sponsorExposed; month++)
            {
                turns.EndMonth();
                if (!state.insurgencies.Contains(rising)) break;
                occupied.ownerId = rival.id;
                rising.support = 55f;      // hold the movement alive for the claim being tested
                rising.sponsorId = state.playerCountryId;
            }

            Assert.IsTrue(rising.sponsorExposed,
                "Twenty years of running a supply pipeline against an alert security service "
                + "was never traced. Deniability is supposed to be a wasting asset.");
        }

        [Test]
        public void BeingAttributedCostsStandingAndNotAPillar()
        {
            var occupied = OccupyForeignGround();
            var rival = state.countries.Find(c => !c.isPlayer && c.id != occupied.originalOwnerId);
            occupied.ownerId = rival.id;

            var rising = Plant(occupied, InsurgencyCause.Occupation, 50f);
            rising.sponsorId = state.playerCountryId;

            var player = state.PlayerCountry;
            var relationship = state.FindRelationship(player.id, rival.id);
            float relations = relationship.relations;
            float pillar = player.pillars.intelligence;
            float diplomacyPillar = player.pillars.diplomacy;

            InsurgencySystem.Attribute(state, rising, occupied, rival, player);

            Assert.Less(relationship.relations, relations - 10f,
                "Being caught arming an insurgency cost no standing with the injured state.");
            Assert.AreEqual(pillar, player.pillars.intelligence, 0.001f,
                "Exposure moved a national capability. It is supposed to cost how we are "
                + "regarded — the one thing an operator can rebuild while still doing this job.");
            Assert.AreEqual(diplomacyPillar, player.pillars.diplomacy, 0.001f,
                "Exposure charged the diplomacy pillar, which is the exact cost "
                + "IntelligenceSystem removed for having no recovery path.");
        }

        // ---------- 4. the world can use it ----------

        [Test]
        public void AGovernmentWillArmARivalsInsurgencyWithoutThePlayer()
        {
            // A hostile pair, and a rising in the one the other cannot stand.
            var a = state.countries.Find(c => !c.isPlayer);
            var b = state.countries.Find(c => !c.isPlayer && c.id != a.id);

            var ground = state.locations.Find(l => l.ownerId == b.id
                                                   && l.type != LocationType.Capital);
            Assert.IsNotNull(ground, "the fixture found no ground belonging to the second state");

            var relationship = state.FindRelationship(a.id, b.id);
            Assert.IsNotNull(relationship);
            relationship.relations = 8f;
            relationship.SetThreatPerceivedBy(a.id, 80f);

            a.resources.treasury = 60000f;
            a.military.ground.supply = 90f;

            var rising = Plant(ground, InsurgencyCause.Deprivation, 55f);
            rising.strength = 40f;

            bool armed = false;
            for (int month = 0; month < 60 && !armed; month++)
            {
                turns.EndMonth();
                if (!state.insurgencies.Contains(rising)) break;

                // Keep the preconditions the claim is about; everything else moves.
                relationship.relations = 8f;
                a.resources.treasury = 60000f;
                rising.support = 55f;
                armed = rising.sponsorId == a.id;
            }

            Assert.IsTrue(armed,
                "No foreign government ever armed a movement against a state it despises, "
                + "in five years, with money in the bank. A verb the world will not reach for "
                + "is a verb the world does not have.");
        }

        // ---------- 5. the sponsor never gets the ground ----------

        [Test]
        public void AVictoriousOccupationRisingLiberatesRatherThanTransfers()
        {
            var occupied = OccupyForeignGround();
            string rightfulOwner = occupied.originalOwnerId;

            var rival = state.countries.Find(c => !c.isPlayer && c.id != rightfulOwner);
            occupied.ownerId = rival.id;
            occupied.garrison = 5f;

            var rising = Plant(occupied, InsurgencyCause.Occupation, 95f);
            rising.strength = 95f;
            rising.support = 90f;
            rising.sponsorId = state.playerCountryId;

            turns.EndMonth();

            Assert.AreEqual(rightfulOwner, occupied.ownerId,
                "A movement we armed handed us the ground. Arming an insurgency must never "
                + "be a cheaper route to annexation than a war.");
            Assert.AreNotEqual(state.playerCountryId, occupied.ownerId);
        }

        [Test]
        public void ContestedGroundPaysNobody()
        {
            var player = state.PlayerCountry;
            var industry = state.locations.Find(l => l.ownerId == player.id
                                                     && l.type == LocationType.IndustrialCenter);
            if (industry == null) Assert.Ignore("this world authored the player no industrial centre");

            float before = TerritorySystem.IndustrySwing(state, player.id);

            var rising = Plant(industry, InsurgencyCause.Deprivation, 80f);
            rising.strength = InsurgencySystem.ContestThreshold + 10f;

            float after = TerritorySystem.IndustrySwing(state, player.id);

            Assert.Less(after, before,
                "A province in open revolt kept paying its owner in full. Denying a rival the "
                + "use of ground is the entire point of arming somebody on it.");
        }

        // ---------- the counter side ----------

        [Test]
        public void CounterInsurgencyWeakensTheMovementAndHardensTheGrievance()
        {
            var player = state.PlayerCountry;
            var home = state.locations.Find(l => l.ownerId == player.id
                                                 && l.type != LocationType.Capital);
            var rising = Plant(home, InsurgencyCause.Deprivation, 60f);
            rising.strength = 60f;

            float strength = rising.strength;
            float support = rising.support;

            InsurgencySystem.OnCounterInsurgency(state, home, 1f);

            Assert.Less(rising.strength, strength,
                "A counter-insurgency operation left the fighters untouched.");
            Assert.Greater(rising.support, support,
                "Security operations among a population made that population fonder of the "
                + "government running them. A purely military answer holds ground; it does "
                + "not end the problem.");
        }

        [Test]
        public void MovementsSurviveASaveAndReload()
        {
            var occupied = OccupyForeignGround();
            var rising = Plant(occupied, InsurgencyCause.Occupation, 62f);
            rising.strength = 44f;
            rising.sponsorId = state.playerCountryId;
            rising.exposure = 31f;

            string json = SaveSystem.ToJson(state);
            var loaded = SaveSystem.FromJson(json);

            var restored = InsurgencySystem.At(loaded, occupied.id);
            Assert.IsNotNull(restored, "the movement did not survive the save");
            Assert.AreEqual(rising.strength, restored.strength, 0.01f);
            Assert.AreEqual(rising.sponsorId, restored.sponsorId);
            Assert.AreEqual(rising.exposure, restored.exposure, 0.01f);
        }

        string Why(Insurgency rising)
        {
            InsurgencySystem.CanSupport(state, state.playerCountryId, rising, out string reason);
            return reason;
        }
    }
}

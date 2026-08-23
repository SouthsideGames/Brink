using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The world as structure rather than backdrop: theatres, simultaneous
    /// fronts, fragmentation, the social layer, and the content volume behind
    /// them (GDD §12, §16, §17.1).
    /// </summary>
    public class WorldStructureTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1212);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static GameState Run(int seed, int months, Action<GameState, int> each = null)
        {
            var world = WorldFactory.CreateDebugWorld(seed);
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);
            for (int month = 0; month < months; month++)
            {
                each?.Invoke(world, month);
                turns.EndMonth();
            }
            return world;
        }

        // ---------- content volume ----------

        [Test]
        public void ThereIsEnoughMaritimeGroundForTheNavalVerbs()
        {
            // Nine of twenty-three operations need a maritime target. With three
            // ports in the world, most of the naval half of the verb list was a
            // button with nothing to point at.
            int ports = 0, chokepoints = 0;
            foreach (var location in state.locations)
            {
                if (location.type == LocationType.Port) ports++;
                if (location.type == LocationType.Chokepoint) chokepoints++;
            }

            Assert.GreaterOrEqual(ports, 10, $"Only {ports} ports in the world.");
            Assert.GreaterOrEqual(chokepoints, 6, $"Only {chokepoints} chokepoints in the world.");
        }

        [Test]
        public void EveryMaritimePowerHasSomewhereToPutItsFleet()
        {
            foreach (var country in state.countries)
            {
                if (GeographySystem.AccessOf(country.id) != NavalAccess.Maritime) continue;

                bool hasPort = false;
                foreach (var location in state.locations)
                    if (location.originalOwnerId == country.id && location.type == LocationType.Port)
                        hasPort = true;

                Assert.IsTrue(hasPort,
                    $"{country.displayName} is authored as a maritime power with no port. " +
                    "Its navy has nowhere to be, and nowhere to be attacked.");
            }
        }

        [Test]
        public void StrategicMaterialsHaveGeography()
        {
            // The commodity industry, procurement and every research programme
            // draw on had nowhere on the map that produced it.
            int regions = 0;
            foreach (var location in state.locations)
                if (location.type == LocationType.MaterialsRegion) regions++;

            Assert.GreaterOrEqual(regions, 3,
                "Strategic materials are a headline national resource with no ground behind them.");
        }

        [Test]
        public void TakingAMaterialsRegionSuppliesMaterials()
        {
            StrategicLocation region = null;
            foreach (var location in state.locations)
                if (location.type == LocationType.MaterialsRegion
                    && location.originalOwnerId != state.playerCountryId)
                { region = location; break; }
            Assert.IsNotNull(region);

            var player = state.PlayerCountry;
            float before = EconomySystem.MaterialsCeilingFor(state, player);

            region.ownerId = player.id;
            float after = EconomySystem.MaterialsCeilingFor(state, player);

            Assert.Greater(after, before,
                "Holding a mining region did not raise what we can supply ourselves.");
        }

        [Test]
        public void TheEventCatalogueCanSustainALongSave()
        {
            // 15 events on a 24-month cooldown exhausts in about three years, and
            // a save is meant to run decades.
            Assert.GreaterOrEqual(EventCatalog.Definitions.Count, 25,
                $"Only {EventCatalog.Definitions.Count} authored events. A player will see the " +
                "whole catalogue inside three years and repeats forever after.");
        }

        [Test]
        public void EveryEventIsReachable()
        {
            // An event whose eligibility can never be true is content that does
            // not exist. Checked across many worlds and a long horizon.
            var seen = new HashSet<string>();

            foreach (int seed in new[] { 1212, 3434, 5656 })
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);

                for (int month = 0; month < 240; month++)
                {
                    foreach (var definition in EventCatalog.Definitions)
                    {
                        if (seen.Contains(definition.id)) continue;
                        try
                        {
                            if (definition.isEligible(world)) seen.Add(definition.id);
                        }
                        catch (Exception e)
                        {
                            Assert.Fail($"{definition.id} threw while testing eligibility: {e.Message}");
                        }
                    }
                    turns.EndMonth();
                }
            }

            // Not every event will fire in three sampled worlds — some need a
            // specific rare state. But most should be reachable, and any that
            // never becomes eligible anywhere is worth a look.
            Assert.GreaterOrEqual(seen.Count, EventCatalog.Definitions.Count / 2,
                $"Only {seen.Count} of {EventCatalog.Definitions.Count} events ever became " +
                "eligible across three sixty-year worlds.");
        }

        // ---------- national identity ----------

        [Test]
        public void EveryCountryHasAnAuthoredCharacter()
        {
            foreach (var country in state.countries)
                Assert.Greater(country.traits.Count, 0,
                    $"{country.displayName} has no traits. Fifteen of sixteen states used to be " +
                    "the same numbers with a different flag.");
        }

        [Test]
        public void EveryAuthoredTraitResolves()
        {
            foreach (var profile in WorldFactory.Profiles)
                foreach (var id in profile.traitIds)
                    Assert.IsNotNull(NationalTraitCatalog.Find(id),
                        $"{profile.id} carries unknown trait '{id}' — a typo becomes a country " +
                        "quietly missing its character.");
        }

        [Test]
        public void ACountryPlaysLikeItselfAcrossSaves()
        {
            // The point of authoring temperament. Personality used to be rolled
            // uniformly, so nothing a player learned about a nation carried into
            // the next game — which quietly defeats an AI designed to answer a
            // repeated opening.
            float SpreadOf(string countryId)
            {
                float lowest = float.MaxValue, highest = float.MinValue;
                foreach (int seed in new[] { 11, 22, 33, 44, 55 })
                {
                    var world = WorldFactory.CreateDebugWorld(seed);
                    float aggression = world.FindAI(countryId).profile.aggression;
                    lowest = Math.Min(lowest, aggression);
                    highest = Math.Max(highest, aggression);
                }
                return highest - lowest;
            }

            Assert.Less(SpreadOf("RUS"), 25f,
                "The same country's temperament varies too much between saves to be learnable.");

            // And two countries authored differently must still read differently.
            var calm = WorldFactory.FindProfile("AUS");
            var forceful = WorldFactory.FindProfile("RUS");
            Assert.Greater(forceful.aggression, calm.aggression + 20f,
                "Two deliberately different nations were authored with the same temperament.");
        }

        [Test]
        public void TraitsChangeSomething()
        {
            // A trait that reads nowhere is decoration.
            var martial = new CountryState();
            martial.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.Martial));
            Assert.Less(NationalTraitCatalog.WarSupportResilience(martial),
                NationalTraitCatalog.WarSupportResilience(new CountryState()));

            var resourceState = new CountryState();
            resourceState.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.ResourceState));
            Assert.Greater(NationalTraitCatalog.ResourceCeilingBonus(resourceState), 0f);

            var fractious = new CountryState();
            fractious.traits.Add(NationalTraitCatalog.Find(NationalTraitCatalog.Fractious));
            Assert.Greater(NationalTraitCatalog.UnrestVolatility(fractious), 1f);
        }

        // ---------- the social layer ----------

        [Test]
        public void LivingStandardsFollowTheEconomyOverYears()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 1212);
            var country = world.PlayerCountry;
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);

            country.livingStandards = 55f;
            for (int month = 0; month < 72; month++)
            {
                country.economy.marketIndex = 40f;   // a long, deep decline
                country.economy.growthRate = -4f;
                turns.EndMonth();
            }

            Assert.Less(country.livingStandards, 45f,
                $"Six years of contraction left living standards at {country.livingStandards:F0}. " +
                "The economy has to reach the public eventually.");
        }

        [Test]
        public void UnrestIsNotJustLowApproval()
        {
            // The distinction that justifies tracking it separately: a state can
            // be widely disliked and perfectly calm.
            var world = WorldFactory.CreateDebugWorld(seed: 1212);
            var country = world.PlayerCountry;
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);

            country.livingStandards = 80f;
            country.nationalUnity = 85f;
            country.governmentApproval = 20f;   // unpopular, but comfortable

            for (int month = 0; month < 24; month++)
            {
                country.livingStandards = 80f;
                country.nationalUnity = 85f;
                turns.EndMonth();
            }

            Assert.Less(country.socialUnrest, 25f,
                $"A prosperous, cohesive country with an unpopular government reached " +
                $"{country.socialUnrest:F0} unrest. Unrest is organisation, not opinion.");
        }

        [Test]
        public void HardshipEventuallyOrganises()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 1212);
            var country = world.PlayerCountry;
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);

            for (int month = 0; month < 48; month++)
            {
                country.livingStandards = 15f;
                country.economy.inflation = 18f;
                country.economy.unemployment = 20f;
                turns.EndMonth();
            }

            Assert.Greater(country.socialUnrest, 40f,
                "Four years of severe hardship produced no organised anger at all.");
            Assert.Greater(country.publicGrievance, 5f,
                "And left nothing in the public memory.");
        }

        [Test]
        public void ARestrictivePostureSuppressesUnrestWithoutFixingIt()
        {
            float UnrestUnder(CivicPosture posture, out float grievance)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 1212);
                var country = world.PlayerCountry;
                country.government.civicPosture = posture;
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);

                for (int month = 0; month < 48; month++)
                {
                    country.livingStandards = 18f;
                    country.government.civicPosture = posture;
                    turns.EndMonth();
                }
                grievance = country.publicGrievance;
                return country.socialUnrest;
            }

            float openUnrest = UnrestUnder(CivicPosture.Open, out float openGrievance);
            float closedUnrest = UnrestUnder(CivicPosture.Restrictive, out float closedGrievance);

            Assert.Less(closedUnrest, openUnrest, "Closing civic space has to suppress the expression.");
            Assert.Greater(closedGrievance, openGrievance * 0.6f,
                "And it must not fix the cause — grievance should keep accruing underneath, " +
                "or a restrictive posture would be free order.");
        }

        [Test]
        public void NoSocialValueRunsAwayInEitherDirection()
        {
            var world = Run(1212, 240);
            foreach (var country in world.countries)
            {
                Assert.Less(country.livingStandards, 99.5f, $"{country.id} living standards pinned high.");
                Assert.Less(country.socialUnrest, 99.5f, $"{country.id} unrest pinned high.");
                Assert.Less(country.publicGrievance, 99.5f, $"{country.id} grievance pinned high.");
            }
        }

        // ---------- theatres ----------

        [Test]
        public void CountriesAreDistributedAcrossTheatres()
        {
            var theatres = new HashSet<Theatre>();
            foreach (var country in state.countries) theatres.Add(TheatreSystem.Of(country.id));

            Assert.Greater(theatres.Count, 3,
                $"The whole world sits in {theatres.Count} theatre(s), so theatre means nothing.");
            Assert.IsFalse(theatres.Contains(Theatre.Unassigned),
                "A country fell outside every theatre band.");
        }

        [Test]
        public void AWarInOnePlaceDragsOperationsInAnother()
        {
            // The whole point of theatres: two wars on opposite sides of the
            // world are not two wars — the second is fought with what the first
            // left over.
            var target = FirstLocationOf("CHN");
            float alone = TheatreSystem.FocusFactorFor(state, state.playerCountryId, target);
            Assert.AreEqual(1f, alone, 0.001f, "Uncommitted, the force should arrive whole.");

            ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, state.ActiveConfrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            float committed = TheatreSystem.FocusFactorFor(state, state.playerCountryId, target);
            Assert.Less(committed, alone,
                "A war in Europe did not make an operation in the Indo-Pacific any harder.");
            Assert.Greater(committed, 0.5f,
                "Distance and commitment price a campaign; they must never forbid one.");
        }

        [Test]
        public void BeingTiedDownIsVisibleToTheWorld()
        {
            Assert.IsFalse(TheatreSystem.IsOverstretched(state, state.playerCountryId));

            ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, state.ActiveConfrontation,
                EscalationState.TotalWar, state.playerCountryId);

            Assert.IsTrue(TheatreSystem.IsOverstretched(state, state.playerCountryId),
                "A power in a total war is not visibly committed, so nobody can exploit it.");
        }

        // ---------- simultaneous fronts ----------

        [Test]
        public void ASecondFrontIsPossible()
        {
            var first = ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(first);

            var second = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(second,
                "One confrontation at a time was a hard block, which made the map decoration " +
                "and left a military operator with nothing to do once their war settled.");

            Assert.AreEqual(2, state.ActiveConfrontationsFor(state.playerCountryId).Count);
        }

        [Test]
        public void TheSamePairCannotOpenTwoWars()
        {
            ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            var duplicate = ConfrontationSystem.BeginBy(state, state.playerCountryId, "DEU",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Economic);

            Assert.IsNull(duplicate, "Two states fighting each other twice is one war.");
        }

        [Test]
        public void ThereIsStillACeiling()
        {
            // Priced, not forbidden — but a state cannot fight everybody.
            var opponents = new[] { "DEU", "MEX", "BRA", "NGA", "POL", "TUR" };
            foreach (var opponent in opponents)
            {
                var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, opponent,
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                if (confrontation != null)
                    ConfrontationSystem.SetEscalationBy(state, confrontation,
                        EscalationState.LimitedConflict, state.playerCountryId);
            }

            Assert.Less(state.ActiveConfrontationsFor(state.playerCountryId).Count, opponents.Length,
                "A single state opened a war against everybody it could reach.");
            Assert.IsFalse(ConfrontationSystem.CanOpenAnother(state, state.playerCountryId, out string why));
            Assert.IsNotEmpty(why, "A refusal the operator cannot explain reads as a bug.");
        }

        // ---------- fragmentation ----------

        [Test]
        public void AFailingStateCanComeApart()
        {
            // `new CountryState` appeared exactly once in the codebase — at world
            // creation — so the map could only ever shrink.
            var country = state.FindCountry("NGA");
            var gov = country.government;

            gov.inCivilConflict = true;
            gov.civilConflictMonthsElapsed = 12;
            gov.civilConflictMonthsRemaining = 12;
            country.nationalUnity = 10f;
            gov.militaryLoyalty = 20f;

            int before = state.countries.Count;
            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));

            Assert.IsNotNull(successor, "A state that had entirely failed did not fracture.");
            Assert.AreEqual(before + 1, state.countries.Count);
            Assert.IsFalse(gov.inCivilConflict, "The fracture has to resolve the conflict.");
        }

        [Test]
        public void ABreakawayIsARealStateNotAFlag()
        {
            var country = state.FindCountry("NGA");
            country.government.inCivilConflict = true;
            country.government.civilConflictMonthsElapsed = 12;
            country.nationalUnity = 10f;
            country.government.militaryLoyalty = 20f;

            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));
            Assert.IsNotNull(successor);

            Assert.Greater(successor.cabinet.Count, 0, "It has nobody governing it.");
            Assert.IsNotNull(state.FindAI(successor.id), "It has no mind, so it will never act.");
            Assert.IsNotNull(state.FindRelationship(successor.id, state.playerCountryId),
                "It cannot be talked to, sanctioned or allied with — it is scenery.");
            Assert.Greater(successor.traits.Count, 0);

            int held = 0;
            foreach (var location in state.locations)
                if (location.ownerId == successor.id) held++;
            Assert.Greater(held, 0, "It holds no ground.");
        }

        [Test]
        public void ABreakawayOwnsItsOwnGroundRatherThanOccupyingIt()
        {
            var country = state.FindCountry("NGA");
            country.government.inCivilConflict = true;
            country.government.civilConflictMonthsElapsed = 12;
            country.nationalUnity = 10f;
            country.government.militaryLoyalty = 20f;

            var successor = SecessionSystem.Fracture(state, country, new System.Random(7));

            foreach (var location in state.locations)
            {
                if (location.ownerId != successor.id) continue;
                Assert.IsFalse(location.IsOccupied,
                    "The successor is permanently occupying its own territory, so it pays " +
                    "garrison drag forever and reads as a conqueror of itself.");
            }
        }

        [Test]
        public void AHealthyStateDoesNotFracture()
        {
            // Fragmentation must be the end of a story, not a dice roll.
            var world = Run(1212, 240);
            foreach (var country in world.countries)
                Assert.IsFalse(country.id.EndsWith("_S", StringComparison.Ordinal)
                               && country.nationalUnity > 60f,
                    "A state fractured without ever having failed at anything.");
        }

        [Test]
        public void TheOperatorKeepsTheirPostWhenTheirCountrySplits()
        {
            // The same rule the coup system follows: severe, never terminal.
            var player = state.PlayerCountry;
            player.government.inCivilConflict = true;
            player.government.civilConflictMonthsElapsed = 12;
            player.nationalUnity = 8f;
            player.government.militaryLoyalty = 18f;

            var successor = SecessionSystem.Fracture(state, player, new System.Random(3));
            Assert.IsNotNull(successor);

            Assert.AreEqual(player.id, state.playerCountryId,
                "The operator lost their post because the country split.");
            Assert.IsNotNull(state.PlayerCountry);
        }

        [Test]
        public void ADecadeStaysDeterministic()
        {
            string Fingerprint(GameState world)
            {
                var parts = new List<string>();
                foreach (var country in world.countries)
                    parts.Add($"{country.id}:{country.livingStandards:F2}:{country.socialUnrest:F2}");
                parts.Add($"countries={world.countries.Count}");
                return string.Join("|", parts);
            }

            Assert.AreEqual(Fingerprint(Run(9182, 180)), Fingerprint(Run(9182, 180)),
                "The same seed no longer produces the same world.");
        }

        StrategicLocation FirstLocationOf(string countryId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type != LocationType.Capital)
                    return location;
            return null;
        }
    }
}

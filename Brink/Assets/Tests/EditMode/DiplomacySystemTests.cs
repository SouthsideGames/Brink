using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class DiplomacySystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7070);
            turns = new TurnManager(state);
            // NARROW PIPELINE: every test writes the six relationship dimensions
            // it cares about by hand and then runs at most 24 months to assert
            // DiplomacySystem's own monthly arithmetic (relations cooling under
            // sanctions, dependence collapsing under embargo, war memory). The
            // AI is the significant omission and is omitted on purpose — a
            // foreign government acting on these relationships would overwrite
            // the very values the tests set as preconditions.
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static List<TreatyCommitment> Commitments(params TreatyCommitment[] items) => new List<TreatyCommitment>(items);

        [Test]
        public void Factory_SeedsFullRelationshipGraph()
        {
            // Every unordered pair gets a relationship.
            int expectedPairs = state.countries.Count * (state.countries.Count - 1) / 2;
            Assert.AreEqual(expectedPairs, state.relationships.Count);
            Assert.NotNull(state.FindRelationship("USA", "CHN"));
            Assert.NotNull(state.FindRelationship("CHN", "USA"), "Lookup must be order-independent.");

            foreach (var relationship in state.relationships)
                Assert.Greater(relationship.threatPerceptionOfA, 0f);

            // Dependence comes from trade, so it exists only where trade does —
            // most pairs in a sixteen-country world have little to do with each other.
            Assert.Greater(state.FindRelationship("USA", "CHN").dependenceAOnB, 0f,
                "A heavy trade link should seed real dependence.");
            Assert.AreEqual(0f, state.FindRelationship("POL", "BRA").dependenceAOnB,
                "Countries with no trade link owe each other nothing.");
        }

        [Test]
        public void Status_IsDerivedFromFullState_NotOneNumber()
        {
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 85f;
            relationship.trust = 85f;
            relationship.strategicAlignment = 85f;
            relationship.threatPerceptionOfA = 0f;
            relationship.threatPerceptionOfB = 0f;
            var warm = DiplomacySystem.StatusOf(state, "USA", "IND");

            // Same warmth, but now they see us as a serious threat.
            relationship.threatPerceptionOfB = 100f;
            relationship.threatPerceptionOfA = 100f;
            var feared = DiplomacySystem.StatusOf(state, "USA", "IND");

            Assert.Greater((int)warm, (int)feared,
                "Threat perception must be able to cool a warm relationship.");
        }

        [Test]
        public void Ally_RequiresMutualDefenseTreaty()
        {
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 95f;
            relationship.trust = 95f;
            relationship.strategicAlignment = 95f;
            relationship.threatPerceptionOfA = 0f;
            relationship.threatPerceptionOfB = 0f;

            Assert.AreNotEqual(RelationshipStatus.Ally, DiplomacySystem.StatusOf(state, "USA", "IND"),
                "Without a defense commitment there is no alliance, however warm.");

            state.treaties.Add(new Treaty
            {
                id = "T1",
                countryA = "USA",
                countryB = "IND",
                commitments = Commitments(TreatyCommitment.MutualDefense)
            });

            Assert.AreEqual(RelationshipStatus.Ally, DiplomacySystem.StatusOf(state, "USA", "IND"));
        }

        [Test]
        public void ActiveWar_ForcesHostileStatus()
        {
            var relationship = state.FindRelationship("USA", "CHN");
            relationship.relations = 100f;
            relationship.trust = 100f;
            relationship.strategicAlignment = 100f;

            state.commandPoints.current = 40;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.LimitedConflict);

            Assert.AreEqual(RelationshipStatus.Hostile, DiplomacySystem.StatusOf(state, "USA", "CHN"),
                "Shooting at each other overrides warm paperwork.");
        }

        [Test]
        public void Outreach_CostsCPAndWarmsRelations()
        {
            var relationship = state.FindRelationship("USA", "IND");
            float relationsBefore = relationship.relations;
            int cpBefore = state.commandPoints.current;

            Assert.IsTrue(DiplomacySystem.Outreach(state, turns, "IND"));

            Assert.AreEqual(cpBefore - DiplomacySystem.DiplomaticOutreachCost, state.commandPoints.current);
            Assert.Greater(relationship.relations, relationsBefore);
        }

        [Test]
        public void Treaty_RejectedWhenRelationshipTooCold()
        {
            state.commandPoints.current = 20;
            var relationship = state.FindRelationship("USA", "CHN");
            relationship.relations = 10f;
            relationship.trust = 10f;
            relationship.strategicAlignment = 10f;

            Assert.IsFalse(DiplomacySystem.ProposeTreaty(state, turns, "CHN",
                Commitments(TreatyCommitment.MutualDefense)));
            Assert.IsNull(state.FindTreaty("USA", "CHN"));
            Assert.Less(relationship.memoryWeight, 0f, "A refusal should leave a mark.");
        }

        [Test]
        public void Treaty_AcceptedWhenRelationshipIsWarmAndTermsAreLight()
        {
            state.commandPoints.current = 20;
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 88f;
            relationship.trust = 85f;
            relationship.strategicAlignment = 85f;

            Assert.IsTrue(DiplomacySystem.ProposeTreaty(state, turns, "IND",
                Commitments(TreatyCommitment.NonAggression, TreatyCommitment.TradePreference)));

            var treaty = state.FindTreaty("USA", "IND");
            Assert.NotNull(treaty);
            Assert.IsTrue(treaty.Has(TreatyCommitment.NonAggression));
            Assert.Greater(relationship.memoryWeight, 0f);
        }

        [Test]
        public void HeavierCommitments_AreHarderToObtain()
        {
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 70f;
            relationship.trust = 70f;
            relationship.strategicAlignment = 70f;

            float lightTerms = DiplomacySystem.TreatyWillingness(state, "IND",
                Commitments(TreatyCommitment.TradePreference));
            float heavyTerms = DiplomacySystem.TreatyWillingness(state, "IND",
                Commitments(TreatyCommitment.MutualDefense, TreatyCommitment.JointPlanning));

            Assert.Greater(lightTerms, heavyTerms);
        }

        [Test]
        public void NobodySignsWithUsWhileWeFightThem()
        {
            state.commandPoints.current = 40;
            ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var relationship = state.FindRelationship("USA", "CHN");
            relationship.relations = 95f;
            relationship.trust = 95f;
            relationship.strategicAlignment = 95f;

            Assert.Less(DiplomacySystem.TreatyWillingness(state, "CHN",
                Commitments(TreatyCommitment.NonAggression)), 50f);
        }

        [Test]
        public void BreakingTreaty_DamagesTrustWithEveryone()
        {
            state.commandPoints.current = 20;
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 90f;
            relationship.trust = 90f;
            relationship.strategicAlignment = 90f;
            DiplomacySystem.ProposeTreaty(state, turns, "IND", Commitments(TreatyCommitment.NonAggression));

            var thirdParty = state.FindRelationship("USA", "RUS");
            float thirdPartyTrustBefore = thirdParty.trust;
            float diplomacyBefore = state.PlayerCountry.pillars.diplomacy;

            Assert.IsTrue(DiplomacySystem.BreakTreaty(state, "IND"));

            Assert.IsNull(state.FindTreaty("USA", "IND"), "A broken treaty is no longer in force.");
            Assert.Less(relationship.trust, 90f);
            Assert.Less(thirdParty.trust, thirdPartyTrustBefore, "Third parties revise their view of our reliability.");
            Assert.Less(state.PlayerCountry.pillars.diplomacy, diplomacyBefore);
        }

        [Test]
        public void Coalition_RecruitsRivalsOfTheTargetAndRefusesTheirPartners()
        {
            state.commandPoints.current = 40;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);

            // India: friendly with us, hostile toward the target, low dependence on it.
            var usaInd = state.FindRelationship("USA", "IND");
            usaInd.relations = 85f; usaInd.trust = 85f; usaInd.strategicAlignment = 85f;
            var indChn = state.FindRelationship("IND", "CHN");
            indChn.relations = 5f; indChn.dependenceAOnB = 0f; indChn.dependenceBOnA = 0f;
            indChn.threatPerceptionOfB = 90f; // India sees China as dangerous

            // Russia: aligned with the target instead.
            var usaRus = state.FindRelationship("USA", "RUS");
            usaRus.relations = 10f; usaRus.trust = 10f; usaRus.strategicAlignment = 10f;
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.relations = 90f; rusChn.dependenceAOnB = 80f;

            state.commandPoints.current = 40;
            var coalition = DiplomacySystem.RequestCoalition(state, turns);

            Assert.NotNull(coalition);
            CollectionAssert.Contains(coalition.memberIds, "IND");
            CollectionAssert.DoesNotContain(coalition.memberIds, "RUS");
            CollectionAssert.DoesNotContain(coalition.memberIds, "CHN", "The target never joins.");
        }

        [Test]
        public void Coalition_RefusedByStatesDependentOnTheTarget()
        {
            state.commandPoints.current = 40;
            ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var usaInd = state.FindRelationship("USA", "IND");
            usaInd.relations = 80f; usaInd.trust = 80f;
            var indChn = state.FindRelationship("IND", "CHN");
            indChn.relations = 30f;
            indChn.dependenceAOnB = 100f; // India cannot afford to act
            indChn.dependenceBOnA = 100f;

            Assert.Less(DiplomacySystem.CoalitionWillingness(state, "USA", "IND", "CHN"), 50f,
                "Deep dependence on the target should argue against joining.");
        }

        [Test]
        public void MutualDefenseWithTarget_BlocksCoalitionParticipation()
        {
            state.commandPoints.current = 40;
            ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var usaRus = state.FindRelationship("USA", "RUS");
            usaRus.relations = 95f; usaRus.trust = 95f; usaRus.strategicAlignment = 95f;
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.relations = 5f;

            state.treaties.Add(new Treaty
            {
                id = "T_RUS_CHN",
                countryA = "RUS",
                countryB = "CHN",
                commitments = Commitments(TreatyCommitment.MutualDefense)
            });

            Assert.Less(DiplomacySystem.CoalitionWillingness(state, "USA", "RUS", "CHN"), 50f,
                "A defense commitment to the target must override warmth toward us.");
        }

        [Test]
        public void Coalition_AddsWeightToOperations()
        {
            state.commandPoints.current = 60;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);

            Assert.AreEqual(0f, DiplomacySystem.CoalitionStrength(state, confrontation));

            var coalition = new Coalition
            {
                id = "C1",
                leaderId = "USA",
                confrontationId = confrontation.id,
                targetId = "CHN"
            };
            coalition.memberIds.Add("USA");
            coalition.memberIds.Add("IND");
            state.coalitions.Add(coalition);

            Assert.Greater(DiplomacySystem.CoalitionStrength(state, confrontation), 0f);
        }

        [Test]
        public void Coalition_DissolvesWhenConfrontationEnds()
        {
            state.commandPoints.current = 60;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var coalition = new Coalition
            {
                id = "C1",
                leaderId = "USA",
                confrontationId = confrontation.id,
                targetId = "CHN"
            };
            coalition.memberIds.Add("USA");
            state.coalitions.Add(coalition);

            ConfrontationSystem.ProposeSettlement(state, confrontation, concedeInstead: true);
            turns.EndMonth();

            Assert.IsTrue(coalition.dissolved);
        }

        [Test]
        public void Sanctions_ColdenRelationsOverTime()
        {
            var relationship = state.FindRelationship("USA", "CHN");
            float relationsBefore = relationship.relations;

            state.commandPoints.current = 40;
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Severe);
            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.Less(relationship.relations, relationsBefore);
        }

        [Test]
        public void War_ErodesRelationsAndLeavesHistoricalMemory()
        {
            state.commandPoints.current = 60;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
            state.commandPoints.current = 60;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.TotalWar);

            var relationship = state.FindRelationship("USA", "CHN");
            float relationsBefore = relationship.relations;

            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.Less(relationship.relations, relationsBefore);
            Assert.Greater(relationship.memory.Count, 0, "A long war should be remembered.");
            Assert.Less(relationship.memoryWeight, 0f);
        }

        [Test]
        public void Dependence_FollowsLiveTradeAndCollapsesUnderEmbargo()
        {
            var relationship = state.FindRelationship("USA", "CHN");
            state.commandPoints.current = 40;
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Severe); // embargoes the link

            for (int i = 0; i < 24; i++) turns.EndMonth();

            Assert.Less(relationship.dependenceAOnB, 10f,
                "An embargoed link should stop producing dependence.");
        }

        [Test]
        public void Diplomacy_SurvivesSaveRoundTrip()
        {
            state.commandPoints.current = 40;
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 90f; relationship.trust = 88f; relationship.strategicAlignment = 88f;
            DiplomacySystem.ProposeTreaty(state, turns, "IND",
                Commitments(TreatyCommitment.IntelligenceSharing, TreatyCommitment.NonAggression));
            for (int i = 0; i < 6; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(state.relationships.Count, loaded.relationships.Count);
            Assert.AreEqual(state.treaties.Count, loaded.treaties.Count);

            var restoredTreaty = loaded.FindTreaty("USA", "IND");
            Assert.NotNull(restoredTreaty);
            Assert.IsTrue(restoredTreaty.Has(TreatyCommitment.IntelligenceSharing));

            var restored = loaded.FindRelationship("USA", "IND");
            Assert.AreEqual(relationship.relations, restored.relations);
            Assert.AreEqual(relationship.memory.Count, restored.memory.Count);
            Assert.AreEqual(DiplomacySystem.StatusOf(state, "USA", "IND"),
                            DiplomacySystem.StatusOf(loaded, "USA", "IND"));
        }
    }

    public class RealWorldRosterTests
    {
        [Test]
        public void World_UsesAuthoredRealCountries()
        {
            var state = WorldFactory.CreateDebugWorld(1);
            Assert.AreEqual(WorldFactory.StandardRoster.Length, state.countries.Count,
                "The debug world is the measured Standard roster; growing Profiles must not grow it.");
            Assert.AreEqual("USA", state.playerCountryId);
            Assert.AreEqual("United States", state.PlayerCountry.displayName);
            Assert.NotNull(state.FindCountry("CHN"));
            Assert.NotNull(state.FindCountry("RUS"));
            Assert.NotNull(state.FindCountry("IND"));
        }

        [Test]
        public void Profiles_ProduceDistinctNationalCharacters()
        {
            var state = WorldFactory.CreateDebugWorld(99);
            var usa = state.FindCountry("USA");
            var chn = state.FindCountry("CHN");
            var rus = state.FindCountry("RUS");

            Assert.Greater(rus.resources.energy, usa.resources.energy, "Russia is authored energy-rich.");
            Assert.Greater(chn.resources.industrialCapacity, rus.resources.industrialCapacity,
                "China is authored industry-heavy.");
            Assert.Greater(usa.pillars.intelligence, rus.pillars.economy);
        }

        [Test]
        public void Cabinet_UsesCountryAppropriateTitles()
        {
            var state = WorldFactory.CreateDebugWorld(5);
            var defense = state.FindOfficial(Pillar.Military);
            Assert.AreEqual("Secretary of Defense", defense.title);
            Assert.IsNotEmpty(defense.displayName);

            var diplomacy = state.FindOfficial(Pillar.Diplomacy);
            Assert.AreEqual("Secretary of State", diplomacy.title);
        }

        [Test]
        public void Roster_CoversTheDesignedArchetypes()
        {
            var state = WorldFactory.CreateDebugWorld(4242);

            // The *Standard world* is GDD §31.2's roughly-sixteen; the authored
            // catalogue may grow past it for the Full world.
            Assert.GreaterOrEqual(WorldFactory.StandardRoster.Length, 14,
                "GDD §31.2 targets roughly sixteen authored countries in the default world.");
            Assert.LessOrEqual(WorldFactory.StandardRoster.Length, 18);
            Assert.GreaterOrEqual(WorldFactory.Profiles.Length, WorldFactory.StandardRoster.Length);

            // Archetype diversity is the point — a world of peers has no texture.
            bool hasMonarchy = false, hasDominantParty = false,
                 hasPresidential = false, hasParliamentary = false, hasCentralized = false;
            foreach (var country in state.countries)
            {
                switch (country.government.type)
                {
                    case GovernmentType.Monarchy: hasMonarchy = true; break;
                    case GovernmentType.DominantPartyState: hasDominantParty = true; break;
                    case GovernmentType.PresidentialRepublic: hasPresidential = true; break;
                    case GovernmentType.ParliamentaryRepublic: hasParliamentary = true; break;
                    case GovernmentType.CentralizedRepublic: hasCentralized = true; break;
                }
            }
            Assert.IsTrue(hasMonarchy && hasDominantParty && hasPresidential
                          && hasParliamentary && hasCentralized,
                "Every government type should be exercised by the roster.");
        }

        [Test]
        public void EveryCountry_HasAGenuineVulnerability()
        {
            var state = WorldFactory.CreateDebugWorld(4243);

            foreach (var country in state.countries)
            {
                var r = country.resources;
                bool weak = r.energy < 45f || r.foodSecurity < 50f || r.strategicMaterials < 45f
                            || r.industrialCapacity < 45f
                            || country.stability < 55f || country.nationalUnity < 50f
                            || country.pillars.military < 50f || country.pillars.economy < 55f;

                Assert.IsTrue(weak,
                    $"{country.displayName} has no exploitable weakness — that is what creates play (spec 08).");
            }
        }

        [Test]
        public void EconomicCoercion_HasAffordableTargets()
        {
            var state = WorldFactory.CreateDebugWorld(4244);

            int cheapTargets = 0;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var link = state.FindTrade(state.playerCountryId, country.id);
                if (link == null || link.volume < 25f) cheapTargets++;
            }

            Assert.GreaterOrEqual(cheapTargets, 4,
                "Sanctions need targets we are not ruinously exposed to (spec 02 §7).");
        }

        [Test]
        public void EveryCountry_HasStrategicLocations()
        {
            var state = WorldFactory.CreateDebugWorld(4245);

            foreach (var country in state.countries)
            {
                bool hasCapital = false;
                int held = 0;
                foreach (var location in state.locations)
                {
                    if (location.ownerId != country.id) continue;
                    held++;
                    if (location.type == LocationType.Capital) hasCapital = true;
                }
                Assert.IsTrue(hasCapital, $"{country.displayName} has no capital.");
                Assert.GreaterOrEqual(held, 2, $"{country.displayName} needs something worth contesting.");
            }
        }

        [Test]
        public void TradeNetwork_MakesTheChinaLinkTheHeaviest()
        {
            var state = WorldFactory.CreateDebugWorld(3);
            var chinaLink = state.FindTrade("USA", "CHN");
            var indiaLink = state.FindTrade("USA", "IND");
            var russiaLink = state.FindTrade("USA", "RUS");

            Assert.Greater(chinaLink.volume, indiaLink.volume);
            Assert.Greater(indiaLink.volume, russiaLink.volume);
        }
    }
}

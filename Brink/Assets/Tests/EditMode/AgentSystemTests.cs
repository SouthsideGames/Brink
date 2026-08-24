using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Operations against a person (GDD §14 amendment).
    ///
    /// The foreign cabinet dossier has rendered every rival minister's name and
    /// competence band since foreign cabinets existed, with a comment in the code
    /// explaining why a rival's incompetent economy minister is "a real,
    /// exploitable fact about them" — and nothing in the game could act on it.
    /// A surface with no verb.
    /// </summary>
    public class AgentSystemTests
    {
        GameState state;
        TurnManager turns;
        CountryState target;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8181);
            state.commandPoints.current = 60;
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            target = state.FindCountry("CHN");

            // Deep access, so the tests are about the approach rather than about
            // whether we can reach anybody at all.
            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = target.id,
                focus = IntelDomain.Political,
                penetration = 80f
            });
            target.counterIntel.counterIntelligence = 10f;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        Official Minister(Pillar office) => target.FindOfficial(office);

        // ---------- reaching a person at all ----------

        [Test]
        public void AMinisterCannotBeReachedWithoutRealAccess()
        {
            var shallow = state.FindNetwork(state.playerCountryId, target.id);
            shallow.penetration = 10f;

            Assert.IsFalse(AgentSystem.CanAct(state, state.playerCountryId, target.id,
                Minister(Pillar.Economy), AgentAction.Cultivate, out string reason));
            StringAssert.Contains("ACCESS", reason,
                "The refusal should say what is missing, not merely refuse.");
        }

        [Test]
        public void NoNetworkMeansNoApproach()
        {
            state.networks.Clear();
            Assert.IsFalse(AgentSystem.CanAct(state, state.playerCountryId, target.id,
                Minister(Pillar.Economy), AgentAction.Cultivate, out _));
        }

        // ---------- recruitment is a campaign, not a roll ----------

        [Test]
        public void YouCannotRecruitAStranger()
        {
            // The whole point of the verb's shape. Intelligence's other three
            // operations are one roll each; this one has a state that accumulates
            // over months and can be lost.
            var minister = Minister(Pillar.Economy);
            minister.cultivation = 0f;

            Assert.IsFalse(AgentSystem.CanAct(state, state.playerCountryId, target.id,
                minister, AgentAction.Recruit, out string reason));
            StringAssert.Contains("CULTIVATED", reason);
        }

        [Test]
        public void CultivationAccumulatesOverMonths()
        {
            var minister = Minister(Pillar.Economy);
            minister.loyalty = 30f;   // a candidate
            float before = minister.cultivation;

            for (int i = 0; i < 4; i++)
                AgentSystem.RunBy(state, state.playerCountryId, target.id,
                    minister, AgentAction.Cultivate);

            Assert.Greater(minister.cultivation, before,
                "Four months of contact built no relationship at all.");
        }

        [Test]
        public void ADisaffectedMinisterIsEasierThanALoyalOne()
        {
            // Who you approach is the decision. Without this, the target list is
            // five interchangeable names.
            float CultivationAfterOneApproach(float loyalty, float trust)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 8181);
                var rival = world.FindCountry("CHN");
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId, targetId = rival.id,
                    focus = IntelDomain.Political, penetration = 80f
                });

                var minister = rival.FindOfficial(Pillar.Economy);
                minister.loyalty = loyalty;
                minister.trust = trust;
                minister.cultivation = 0f;

                AgentSystem.RunBy(world, world.playerCountryId, rival.id,
                    minister, AgentAction.Cultivate);
                return minister.cultivation;
            }

            Assert.Greater(CultivationAfterOneApproach(10f, 10f),
                CultivationAfterOneApproach(95f, 95f),
                "A loyal, well-treated minister was as easy to cultivate as a bitter one.");
        }

        [Test]
        public void ARecruitedMinisterReportsFromTheirOwnDesk()
        {
            var minister = Minister(Pillar.Economy);
            minister.loyalty = 5f;
            minister.cultivation = 100f;

            // Recruitment is probabilistic; try until it lands or we run out.
            bool recruited = false;
            for (int i = 0; i < 25 && !recruited; i++)
            {
                minister.cultivation = 100f;
                AgentSystem.RunBy(state, state.playerCountryId, target.id, minister, AgentAction.Recruit);
                recruited = minister.recruitedById == state.playerCountryId;
            }

            Assert.IsTrue(recruited, "Twenty-five approaches to a disaffected minister all failed.");
            Assert.Less(minister.loyalty, 30f, "Someone working for us is not still loyal.");
        }

        [Test]
        public void AnApproachThatFailsHardensThem()
        {
            // Being refused is worse than never asking: now they know.
            var minister = Minister(Pillar.Military);
            minister.loyalty = 95f;
            minister.cultivation = AgentSystem.RecruitThreshold + 1f;
            target.counterIntel.counterIntelligence = 95f;

            float loyaltyBefore = minister.loyalty;
            AgentSystem.RunBy(state, state.playerCountryId, target.id, minister, AgentAction.Recruit);

            Assert.IsTrue(minister.recruitedById != state.playerCountryId,
                "A near-impossible approach succeeded; the fixture is wrong.");
            Assert.GreaterOrEqual(minister.loyalty, loyaltyBefore,
                "A failed approach left them no more wary than before.");
            Assert.Less(minister.cultivation, AgentSystem.RecruitThreshold,
                "The file should be set back when an approach fails.");
        }

        // ---------- consequences ----------

        [Test]
        public void DiscreditingRuinsTheMinisterAndIsPublic()
        {
            var minister = Minister(Pillar.Government);
            minister.competence = 80f;
            minister.cultivation = 60f;
            int chronicleBefore = state.chronicle.Count;

            for (int i = 0; i < 20 && minister.competence > 70f; i++)
                AgentSystem.RunBy(state, state.playerCountryId, target.id,
                    minister, AgentAction.Discredit);

            Assert.Less(minister.competence, 80f,
                "Twenty attempts to discredit a minister left them untouched.");
            Assert.Greater(state.chronicle.Count, chronicleBefore,
                "Ruining a foreign minister left no trace in the record.");
        }

        [Test]
        public void BeingCaughtCostsStandingNotCapability()
        {
            // Same rule the covert-operation exposure was fixed to follow: the
            // price is how they regard us, which is recoverable. A pillar hit
            // would be a cost with no repair path under the conditions causing it.
            var minister = Minister(Pillar.Military);
            minister.loyalty = 99f;
            minister.cultivation = AgentSystem.RecruitThreshold + 1f;
            target.counterIntel.counterIntelligence = 100f;

            var relationship = state.FindRelationship(state.playerCountryId, target.id);
            float relationsBefore = relationship.relations;
            float pillarBefore = state.PlayerCountry.pillars.intelligence;

            AgentSystem.RunBy(state, state.playerCountryId, target.id, minister, AgentAction.Recruit);

            Assert.Less(relationship.relations, relationsBefore,
                "Being caught approaching their cabinet cost us nothing with them.");
            Assert.AreEqual(pillarBefore, state.PlayerCountry.pillars.intelligence, 0.01f,
                "Exposure charged the intelligence pillar. Getting caught changes how we are "
                + "regarded, not how capable our service is.");
        }

        [Test]
        public void AnAgentIsLostWhenTheNetworkBurns()
        {
            var minister = Minister(Pillar.Economy);
            minister.recruitedById = state.playerCountryId;

            state.FindNetwork(state.playerCountryId, target.id).compromised = true;
            AgentSystem.MonthlyUpdate(state);

            Assert.IsEmpty(minister.recruitedById,
                "An agent we have no way to run is not an agent. The access is the point.");
        }

        // ---------- symmetry and the save ----------

        [Test]
        public void ForeignServicesCanRunAgentsToo()
        {
            // A verb the world cannot use is this codebase's most-repeated bug.
            var us = state.PlayerCountry;
            state.networks.Add(new IntelNetwork
            {
                ownerId = "RUS", targetId = us.id,
                focus = IntelDomain.Political, penetration = 80f
            });

            var ourMinister = us.FindOfficial(Pillar.Economy);
            ourMinister.loyalty = 10f;

            Assert.IsTrue(AgentSystem.RunBy(state, "RUS", us.id, ourMinister, AgentAction.Cultivate),
                "A foreign service cannot approach our cabinet.");
            Assert.Greater(ourMinister.cultivation, 0f);
        }

        [Test]
        public void AgentStateSurvivesASaveRoundTrip()
        {
            var minister = Minister(Pillar.Diplomacy);
            minister.recruitedById = state.playerCountryId;
            minister.cultivation = 71.5f;

            var restored = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restoredMinister = restored.FindCountry("CHN").FindOfficial(Pillar.Diplomacy);

            Assert.AreEqual(state.playerCountryId, restoredMinister.recruitedById);
            Assert.AreEqual(71.5f, restoredMinister.cultivation, 0.01f);
        }

        [Test]
        public void ADecadeStaysDeterministic()
        {
            string Fingerprint(int seed)
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var localTurns = new TurnManager(world);
                SimulationPipeline.Wire(localTurns, world);
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId, targetId = "CHN",
                    focus = IntelDomain.Political, penetration = 80f
                });

                var minister = world.FindCountry("CHN").FindOfficial(Pillar.Economy);
                for (int month = 0; month < 60; month++)
                {
                    AgentSystem.RunBy(world, world.playerCountryId, "CHN", minister, AgentAction.Cultivate);
                    localTurns.EndMonth();
                }
                return $"{minister.cultivation:F3}|{minister.recruitedById}|{minister.loyalty:F3}";
            }

            Assert.AreEqual(Fingerprint(8181), Fingerprint(8181));
        }
    }
}

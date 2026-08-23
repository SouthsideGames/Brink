using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Absorbing a country that trusts you (GDD §15.1, §22).
    ///
    /// A third route to another nation's land, beside conquest and a sponsored
    /// coup, and the only one that costs no soldiers. It runs entirely on
    /// friendship — which is what makes it a betrayal, and why the bill falls
    /// due among the states that liked you rather than the ones that did not.
    /// </summary>
    public class AccessionTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3535);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
            state.politicalCapital = 20f;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>A friend who relies on us and has stopped holding together.</summary>
        void MakeRipe(string targetId, AccessionRoute route)
        {
            var relationship = state.FindRelationship(state.playerCountryId, targetId);
            relationship.relations = 90f;
            relationship.trust = 85f;
            relationship.SetDependenceOf(targetId, 70f);

            var target = state.FindCountry(targetId);
            target.counterIntel.counterIntelligence = 0f;
            if (route == AccessionRoute.Elite) target.government.eliteCohesion = 20f;
            else target.nationalUnity = 15f;
        }

        // ---------- it requires genuine friendship ----------

        [Test]
        public void AStrangerCannotBeAbsorbed()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "RUS");
            relationship.relations = 30f;
            relationship.trust = 20f;

            Assert.IsFalse(AccessionSystem.CanBegin(
                state, state.playerCountryId, "RUS", AccessionRoute.Elite, out string why));
            StringAssert.Contains("close enough", why,
                "The whole instrument runs on being trusted. It cannot be used on someone who is wary.");
        }

        [Test]
        public void AFriendWhoDoesNotRelyOnUsCannotBeAbsorbed()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            relationship.relations = 95f;
            relationship.trust = 95f;
            relationship.SetDependenceOf("DEU", 0f);
            state.FindCountry("DEU").government.eliteCohesion = 10f;

            Assert.IsFalse(AccessionSystem.CanBegin(
                state, state.playerCountryId, "DEU", AccessionRoute.Elite, out string why));
            StringAssert.Contains("rely on us", why,
                "Affection is not leverage. They have to actually need us.");
        }

        [Test]
        public void AConfidentCountryDoesNotDissolveItself()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            var target = state.FindCountry("DEU");
            target.government.eliteCohesion = 90f;
            target.nationalUnity = 90f;

            Assert.IsFalse(AccessionSystem.CanBegin(
                state, state.playerCountryId, "DEU", AccessionRoute.Elite, out _),
                "A united establishment has no reason to sell.");
            Assert.IsFalse(AccessionSystem.CanBegin(
                state, state.playerCountryId, "DEU", AccessionRoute.Popular, out _),
                "A people who still believe in their state will not vote it away.");
        }

        // ---------- it runs on the friendship continuing ----------

        [Test]
        public void LosingTheirTrustCollapsesTheEffort()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            Assert.IsTrue(AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Elite));

            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            relationship.relations = 20f;
            relationship.trust = 10f;

            AccessionSystem.MonthlyUpdate(state);

            Assert.IsNull(AccessionSystem.FindCampaign(state, state.playerCountryId, "DEU"),
                "You have to stay their friend the entire time. That is what makes it a betrayal " +
                "rather than a slow invasion.");
        }

        [Test]
        public void ItCostsPoliticalCapitalEveryMonth()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Elite);

            float before = state.politicalCapital;
            AccessionSystem.MonthlyUpdate(state);

            Assert.Less(state.politicalCapital, before,
                "Sustained political work draws on authority every month it runs.");
        }

        [Test]
        public void AnUnfundedEffortCollapses()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Elite);
            state.politicalCapital = 0f;

            AccessionSystem.MonthlyUpdate(state);

            Assert.IsNull(AccessionSystem.FindCampaign(state, state.playerCountryId, "DEU"));
        }

        // ---------- the two routes trade off against each other ----------

        [Test]
        public void BuyingTheCabinetIsFasterThanWinningThePeople()
        {
            float RateFor(AccessionRoute route)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 3535);
                var relationship = world.FindRelationship(world.playerCountryId, "DEU");
                relationship.relations = 90f;
                relationship.trust = 85f;
                relationship.SetDependenceOf("DEU", 70f);

                var target = world.FindCountry("DEU");
                target.counterIntel.counterIntelligence = 0f;
                target.government.eliteCohesion = 20f;
                target.nationalUnity = 15f;

                AccessionSystem.BeginBy(world, world.playerCountryId, "DEU", route);
                return AccessionSystem.MonthlyProgress(
                    world, AccessionSystem.FindCampaign(world, world.playerCountryId, "DEU"));
            }

            Assert.Greater(RateFor(AccessionRoute.Elite), RateFor(AccessionRoute.Popular),
                "Buying an establishment is quicker than persuading a population — " +
                "and the resentment afterwards is the price of the shortcut.");
        }

        [Test]
        public void TheirCounterintelligenceSlowsUsDown()
        {
            float RateWithCounterintel(float level)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 3535);
                var relationship = world.FindRelationship(world.playerCountryId, "DEU");
                relationship.relations = 90f;
                relationship.trust = 85f;
                relationship.SetDependenceOf("DEU", 70f);

                var target = world.FindCountry("DEU");
                target.government.eliteCohesion = 20f;
                target.counterIntel.counterIntelligence = level;

                AccessionSystem.BeginBy(world, world.playerCountryId, "DEU", AccessionRoute.Elite);
                return AccessionSystem.MonthlyProgress(
                    world, AccessionSystem.FindCampaign(world, world.playerCountryId, "DEU"));
            }

            Assert.Greater(RateWithCounterintel(0f), RateWithCounterintel(90f),
                "The target must be able to defend itself, or this is an ambush with no answer.");
        }

        // ---------- it completes, and it costs ----------

        AccessionCampaign RunToCompletion(string targetId, AccessionRoute route)
        {
            MakeRipe(targetId, route);
            AccessionSystem.BeginBy(state, state.playerCountryId, targetId, route);
            var campaign = AccessionSystem.FindCampaign(state, state.playerCountryId, targetId);

            for (int i = 0; i < 400 && state.accessions.Count > 0; i++)
            {
                state.politicalCapital = 20f; // keep it funded
                var relationship = state.FindRelationship(state.playerCountryId, targetId);
                relationship.relations = 90f;
                relationship.trust = 85f;
                AccessionSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }
            return campaign;
        }

        [Test]
        public void AnAccessionTransfersTerritoryAndResources()
        {
            var player = state.PlayerCountry;
            var target = state.FindCountry("DEU");
            target.resources.energyEndowment = 70f;
            float energyBefore = player.resources.energyEndowment;

            RunToCompletion("DEU", AccessionRoute.Elite);

            Assert.IsTrue(ConquestSystem.HoldsEverything(state, player.id, "DEU") == false,
                "Once absorbed there is no original German territory left to hold.");
            Assert.Greater(player.resources.energyEndowment, energyBefore,
                "Absorbing a country is absorbing a country, however it was agreed.");
        }

        [Test]
        public void AbsorbingAFriendCostsUsEveryOtherFriend()
        {
            // The mechanical heart of the feature, and the downside that makes it
            // a real choice rather than a strictly better war.
            var player = state.PlayerCountry;

            float Friendliness()
            {
                float total = 0f;
                foreach (var relationship in state.relationships)
                {
                    if (!relationship.Involves(player.id) || relationship.Involves("DEU")) continue;
                    total += relationship.relations + relationship.trust;
                }
                return total;
            }

            // Make the world fond of us, so there is something to lose.
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(player.id)) continue;
                relationship.relations = 80f;
                relationship.trust = 80f;
            }

            float before = Friendliness();
            float diplomacyBefore = player.pillars.diplomacy;

            RunToCompletion("DEU", AccessionRoute.Elite);

            Assert.Less(Friendliness(), before,
                "Absorbing a partner tells every remaining partner what our friendship is worth.");
            Assert.Less(player.pillars.diplomacy, diplomacyBefore);
        }

        [Test]
        public void TheCloserTheyWereTheHarderTheyTakeIt()
        {
            var player = state.PlayerCountry;

            // One devoted friend, one wary acquaintance.
            var devoted = state.FindRelationship(player.id, "JPN");
            devoted.relations = 95f; devoted.trust = 95f;
            var wary = state.FindRelationship(player.id, "RUS");
            wary.relations = 15f; wary.trust = 10f;

            float devotedBefore = devoted.relations;
            float waryBefore = wary.relations;

            RunToCompletion("DEU", AccessionRoute.Elite);

            float devotedDrop = devotedBefore - devoted.relations;
            float waryDrop = waryBefore - wary.relations;

            Assert.Greater(devotedDrop, waryDrop,
                "A rival who already assumed the worst learns little. A friend who thought " +
                "they were safe has just watched what happens to friends.");
        }

        [Test]
        public void AnEliteAccessionLeavesUsHoldingItDown()
        {
            var player = state.PlayerCountry;
            player.nationalUnity = 70f;
            float before = player.nationalUnity;

            RunToCompletion("DEU", AccessionRoute.Elite);

            Assert.Less(player.nationalUnity, before,
                "A union nobody was consulted about is held together by less.");
        }

        // ---------- it can be seen coming ----------

        [Test]
        public void AVigilantTargetCanCatchUs()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            state.FindCountry("DEU").counterIntel.counterIntelligence = 100f;
            AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Elite);

            bool exposed = false;
            for (int i = 0; i < 120 && !exposed; i++)
            {
                state.politicalCapital = 20f;
                var relationship = state.FindRelationship(state.playerCountryId, "DEU");
                relationship.relations = 90f;
                relationship.trust = 85f;

                AccessionSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();

                var campaign = AccessionSystem.FindCampaign(state, state.playerCountryId, "DEU");
                if (campaign == null) break;
                exposed = campaign.exposed;
            }

            Assert.IsTrue(exposed,
                "Nothing here may be an unstoppable ambush — a watchful state has to be able " +
                "to work out what is being done to it.");
        }

        [Test]
        public void BeingCaughtDamagesTheFriendshipItRunsOn()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            state.FindCountry("DEU").counterIntel.counterIntelligence = 100f;
            AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Elite);

            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            float trustBefore = relationship.trust;

            for (int i = 0; i < 120; i++)
            {
                state.politicalCapital = 20f;
                AccessionSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
                var campaign = AccessionSystem.FindCampaign(state, state.playerCountryId, "DEU");
                if (campaign == null || campaign.exposed) break;
            }

            Assert.Less(relationship.trust, trustBefore,
                "The relationship it was exploiting is the first casualty of being found out.");
        }

        // ---------- housekeeping ----------

        [Test]
        public void CampaignsSurviveASave()
        {
            MakeRipe("DEU", AccessionRoute.Popular);
            AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Popular);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = AccessionSystem.FindCampaign(loaded, loaded.playerCountryId, "DEU");

            Assert.IsNotNull(restored, "A years-long effort must not vanish on reload.");
            Assert.AreEqual(AccessionRoute.Popular, restored.route);
        }

        [Test]
        public void AnAbandonedEffortLeavesTheFriendshipIntact()
        {
            MakeRipe("DEU", AccessionRoute.Elite);
            AccessionSystem.BeginBy(state, state.playerCountryId, "DEU", AccessionRoute.Elite);

            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            float trust = relationship.trust;

            Assert.IsTrue(AccessionSystem.Abandon(state, state.playerCountryId, "DEU"));

            Assert.AreEqual(trust, relationship.trust, 0.01f,
                "Thinking better of it costs the work, not the friend.");
        }
    }
}

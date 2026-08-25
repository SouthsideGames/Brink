using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Food security's monthly behaviour (GDD §10.1, §12).
    ///
    /// `foodSecurity` was written once at world creation and never moved again —
    /// the last authored stat with no monthly behaviour at all. A siege, an
    /// embargo or a lost breadbasket changed a number nobody ate from, and the
    /// FOOD_SHORTAGE crisis could only ever fire for a state *authored* hungry.
    ///
    /// NARROW PIPELINE: these tests run `EconomySystem.MonthlyUpdate` or
    /// `GovernmentSystem.MonthlyUpdate` alone. Each asserts one system's
    /// arithmetic against preconditions it sets itself; the full-pipeline
    /// consequences (events firing, AI reading weakness) are covered by the
    /// existing world-invariant and event tests.
    /// </summary>
    public class FoodSecurityTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8181);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void RunEconomyMonths(int months)
        {
            for (int i = 0; i < months; i++)
            {
                EconomySystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }
        }

        // ---------- the drift ----------

        [Test]
        public void AnOldSaveSeedsItsEndowmentFromWhereItStands()
        {
            var country = state.FindCountry("RUS");
            country.resources.foodEndowment = 0f;   // a save from before the field
            float authored = country.resources.foodSecurity;

            RunEconomyMonths(1);

            Assert.Greater(country.resources.foodEndowment, 0f,
                "the endowment was never seeded — every pre-existing save has a country " +
                "whose food drifts toward zero.");
            Assert.AreEqual(authored, country.resources.foodEndowment, 0.01f,
                "the endowment should seed from where the country currently stands, " +
                "like manpower and energy do.");
        }

        [Test]
        public void FoodRecoversTowardTheEndowment()
        {
            var country = state.FindCountry("RUS");
            RunEconomyMonths(1);   // seed the endowment
            float endowment = country.resources.foodEndowment;

            country.resources.foodSecurity = 30f;
            RunEconomyMonths(96);

            Assert.Greater(country.resources.foodSecurity, 52f,
                "eight years after the shock the country is still hungry — the decrement " +
                "has no reachable recovery path, this codebase's most-repeated bug.");
            Assert.LessOrEqual(country.resources.foodSecurity, endowment + 1f,
                "recovery overshot the endowment — an authored food-poor state should " +
                "stay food-poor rather than drift to plenty for free.");
        }

        [Test]
        public void SevereSanctionsStarveTheImports()
        {
            var country = state.FindCountry("RUS");
            RunEconomyMonths(1);
            float before = country.resources.foodSecurity;

            state.sanctions.Add(new Sanction
            {
                senderId = "USA",
                targetId = "RUS",
                severity = SanctionSeverity.Coercive,
                imposedDate = state.date
            });
            RunEconomyMonths(12);

            Assert.Less(country.resources.foodSecurity, before - 5f,
                "a year under coercive sanctions moved food security by less than five " +
                "points — economic siege still cannot reach a population's supply.");
        }

        [Test]
        public void WarDisruptsTheHarvest()
        {
            var country = state.FindCountry("RUS");
            RunEconomyMonths(1);
            float before = country.resources.foodSecurity;

            state.confrontations.Add(new Confrontation
            {
                initiatorId = "RUS",
                defenderId = "KAZ",
                escalation = EscalationState.LimitedConflict
            });
            RunEconomyMonths(24);

            Assert.Less(country.resources.foodSecurity, before - 4f,
                "two years of war left the food supply untouched.");

            // And the recovery path: peace, then time.
            state.confrontations[state.confrontations.Count - 1].resolved = true;
            float atPeace = country.resources.foodSecurity;
            RunEconomyMonths(72);

            Assert.Greater(country.resources.foodSecurity, atPeace + 3.5f,
                "the war ended and the supply never recovered — a decrement with no " +
                "path back under the same conditions.");
        }

        // ---------- trade ----------

        [Test]
        public void AFoodTradeLinkRaisesTheCeiling()
        {
            RunEconomyMonths(1);
            var japan = state.FindCountry("JPN");

            float withTrade = EconomySystem.FoodCeilingFor(state, japan);

            Assert.Greater(withTrade, japan.resources.foodEndowment + 5f,
                "Japan's authored food link with Australia supplies nothing — the first " +
                "authored focused link is decoration.");

            // Cutting the link takes the ceiling with it. The supply is exactly
            // as reliable as the relationship behind it, which is the point.
            foreach (var link in state.trade)
                if (link.Involves("JPN") && link.focus == TradeFocus.Food)
                    link.embargoed = true;

            Assert.AreEqual(japan.resources.foodEndowment,
                EconomySystem.FoodCeilingFor(state, japan), 0.01f,
                "an embargoed food link still supplies food.");
        }

        [Test]
        public void TheAuthoredWorldHasFoodDependencies()
        {
            int foodLinks = 0;
            foreach (var link in state.trade)
                if (link.focus == TradeFocus.Food) foodLinks++;

            Assert.GreaterOrEqual(foodLinks, 3,
                "the starting world authors no food dependencies, so the lever exists " +
                "and nothing in the world hangs from it — the Ramstein lesson.");
        }

        // ---------- what hunger does ----------

        [Test]
        public void HungerReachesTheStreet()
        {
            // Two identical worlds, differing only in food. GovernmentSystem runs
            // alone so nothing recomputes food between ticks and anything that
            // moves is attributable.
            float[] Standards(float food)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 8181);
                var player = world.PlayerCountry;
                for (int i = 0; i < 36; i++)
                {
                    player.resources.foodSecurity = food;
                    GovernmentSystem.MonthlyUpdate(world);
                    world.date = world.date.NextMonth();
                }
                Assert.AreEqual(food, player.resources.foodSecurity, 0.01f,
                    "the fixture did not hold — something recomputed food, so the " +
                    "comparison measures that system rather than hunger");
                return new[] { player.livingStandards, player.socialUnrest };
            }

            var hungry = Standards(20f);
            var fed = Standards(80f);

            Assert.Less(hungry[0], fed[0] - 5f,
                "three years at food security 20 and people live no worse than at 80 — " +
                "hunger is a readout, not a condition.");
            Assert.Greater(hungry[1], fed[1] + 5f,
                "three years of hunger organised nobody.");
        }

        [Test]
        public void NormalPlayIsUntouched()
        {
            // The deprivation terms gate at 50 and 40, so ordinary variation in a
            // fed country changes nothing — the `distress` idiom: a crisis regime
            // added without retuning the ordinary one.
            float[] Standards(float food)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 8181);
                var player = world.PlayerCountry;
                for (int i = 0; i < 24; i++)
                {
                    player.resources.foodSecurity = food;
                    GovernmentSystem.MonthlyUpdate(world);
                    world.date = world.date.NextMonth();
                }
                return new[] { player.livingStandards, player.socialUnrest };
            }

            var comfortable = Standards(65f);
            var abundant = Standards(95f);

            Assert.AreEqual(abundant[0], comfortable[0], 0.01f,
                "living standards distinguish 65 food from 95 — the term is retuning " +
                "normal play instead of adding a deprivation regime.");
            Assert.AreEqual(abundant[1], comfortable[1], 0.01f,
                "unrest distinguishes 65 food from 95.");
        }
    }
}

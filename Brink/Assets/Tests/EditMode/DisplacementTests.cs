using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Displacement — the missing externality (GDD §12, §16, §27).
    ///
    /// Everything bad that happened to a country used to stay inside its borders.
    /// The claims here are that a crisis now leaves the country it started in,
    /// that carrying it is a trade rather than a penalty, that refusing to carry
    /// it costs something too, and that people go home — because a flow with no
    /// return path is the one-way-value bug with a human face.
    /// </summary>
    public class DisplacementTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6608);
            state.commandPoints.current = 60;
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

        /// <summary>Make a country genuinely unliveable, without touching displacement itself.</summary>
        void Ruin(CountryState country)
        {
            country.livingStandards = 6f;
            country.warExhaustion = 80f;
            country.resources.foodSecurity =
                System.Math.Max(0f, country.resources.foodEndowment - 45f);
        }

        // ---------- a peaceful world moves nobody ----------

        [Test]
        public void AWorldAtPeaceDisplacesNobody()
        {
            foreach (var country in state.countries)
            {
                country.livingStandards = 65f;
                country.warExhaustion = 0f;
                country.resources.foodSecurity = country.resources.foodEndowment;

                Assert.AreEqual(0f, DisplacementSystem.DisplacementTargetFor(state, country), 0.001f,
                    $"{country.displayName} was displacing people while fed, at peace and "
                    + "governed. This term has to be zero by construction in a healthy world.");
            }
        }

        // ---------- a crisis leaves the country it started in ----------

        [Test]
        public void ARuinedCountryDisplacesItsPeople()
        {
            var subject = state.countries.Find(c => !c.isPlayer);
            Ruin(subject);

            Assert.Greater(DisplacementSystem.DisplacementTargetFor(state, subject), 15f,
                "A starving country at the end of a long war displaced nobody.");
        }

        [Test]
        public void ANeighboursCollapseArrivesHere()
        {
            var player = state.PlayerCountry;

            // Ruin everybody else, so whoever the player's neighbours are, they
            // are in trouble. The claim is about the mechanism, not the map.
            foreach (var country in state.countries)
                if (!country.isPlayer) Ruin(country);

            float before = player.displacement.hosted;

            for (int month = 0; month < 36; month++)
            {
                foreach (var country in state.countries)
                    if (!country.isPlayer) Ruin(country);
                player.displacement.bordersClosed = false;
                turns.EndMonth();
            }

            Assert.Greater(player.displacement.hosted, before + 1f,
                "Every other country in the world collapsed and not one person arrived here. "
                + "A war used to be a private arrangement between the governments fighting "
                + "it; this is the whole point of the system.");
        }

        // ---------- carrying it is a trade ----------

        [Test]
        public void HostingCostsMoneyAndStandardsAndPaysBackSlowly()
        {
            var player = state.PlayerCountry;
            player.displacement.hosted = 20f;
            player.resources.treasury = 50000f;

            float treasury = player.resources.treasury;
            float capacity = player.resources.industrialCapacity;

            Assert.Greater(DisplacementSystem.StandardsDrag(player), 1f,
                "Carrying twenty points of arrivals put no strain on services at all.");

            // Measured on the system alone: the bill is now smaller than a
            // month's income, so the whole-month balance can rise while hosting
            // still costs exactly what it says.
            DisplacementSystem.MonthlyUpdate(state);
            Assert.Less(player.resources.treasury, treasury,
                "Hosting cost the treasury nothing.");

            turns.EndMonth();
            Assert.Greater(player.resources.industrialCapacity, capacity,
                "People who arrive work. If hosting were pure cost the only correct play "
                + "would be to shut the border on day one, and the decision would not be "
                + "a decision.");
        }

        // ---------- and refusing it costs too ----------

        [Test]
        public void ShuttingTheBorderCostsStandingWithEveryoneCarryingIt()
        {
            var player = state.PlayerCountry;
            var burdened = state.countries.Find(c => !c.isPlayer);
            burdened.displacement.hosted = 20f;

            var relationship = state.FindRelationship(player.id, burdened.id);
            float before = relationship.relations;

            Assert.IsTrue(DisplacementSystem.SetBorderPolicyBy(state, player.id, true));

            Assert.Less(relationship.relations, before,
                "We stopped carrying our share and the states still carrying it thought no "
                + "less of us.");
        }

        [Test]
        public void AShutBorderDoesNotMakeThePressureGoAway()
        {
            var subject = state.countries.Find(c => !c.isPlayer);
            Ruin(subject);
            subject.displacement.displaced = 30f;

            // Nobody will take them.
            foreach (var country in state.countries)
                country.displacement.bordersClosed = true;

            float trapped = DisplacementSystem.PressureAtSource(state, subject);

            foreach (var country in state.countries)
                if (country.id != subject.id) country.displacement.bordersClosed = false;

            float released = DisplacementSystem.PressureAtSource(state, subject);

            Assert.Greater(trapped, released,
                "A world with every border shut was no harder on the country people were "
                + "trying to leave than a world with them open. Closing the door has to be "
                + "a foreign policy, not a filter.");
        }

        // ---------- people go home ----------

        [Test]
        public void DisplacementRecedesWhenTheCountryBecomesLiveableAgain()
        {
            var subject = state.countries.Find(c => !c.isPlayer);
            Ruin(subject);
            subject.displacement.displaced = 40f;

            // Repair it, and keep it repaired.
            for (int month = 0; month < 96; month++)
            {
                subject.livingStandards = 70f;
                subject.warExhaustion = 0f;
                subject.resources.foodSecurity = subject.resources.foodEndowment;
                turns.EndMonth();
            }

            Assert.Less(subject.displacement.displaced, 12f,
                "Eight years after the country became liveable again, the same share of its "
                + "people were still displaced. Every value in this system has to have a "
                + "reachable path back.");
        }

        // ---------- the world uses the verb ----------

        [Test]
        public void AForeignGovernmentWillShutItsOwnBorder()
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                Ruin(country);
                country.displacement.displaced = 45f;
            }

            bool anyClosed = false;
            for (int month = 0; month < 60 && !anyClosed; month++)
            {
                foreach (var country in state.countries)
                {
                    if (country.isPlayer) continue;
                    Ruin(country);
                    country.displacement.displaced = 45f;
                }
                turns.EndMonth();

                foreach (var country in state.countries)
                    if (!country.isPlayer && country.displacement.bordersClosed) anyClosed = true;
            }

            Assert.IsTrue(anyClosed,
                "In five years of a region-wide catastrophe, no foreign government ever shut "
                + "its border. A verb the world will not reach for is a verb the world does "
                + "not have.");
        }

        [Test]
        public void DisplacementSurvivesASaveAndReload()
        {
            var player = state.PlayerCountry;
            player.displacement.hosted = 17f;
            player.displacement.displaced = 4f;
            player.displacement.bordersClosed = true;
            player.displacement.monthsClosed = 9;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.PlayerCountry.displacement;

            Assert.AreEqual(17f, restored.hosted, 0.01f);
            Assert.AreEqual(4f, restored.displaced, 0.01f);
            Assert.IsTrue(restored.bordersClosed);
            Assert.AreEqual(9, restored.monthsClosed);
        }

        // ---------- and it cannot bankrupt a state ----------

        /// <summary>
        /// The 2026-08 playtest: at 22 treasury per point a full load cost
        /// ~70× a great power's monthly income, every AI closed its border, the
        /// player carried the world, and every posting under every playstyle was
        /// tens of thousands in the red by year ten. Hosting is a burden on the
        /// order of a month's income, and it is paid from what is there.
        /// </summary>
        [Test]
        public void HostingIsABurdenNotABankruptcy()
        {
            var player = state.PlayerCountry;
            float monthlyIncome = player.economy.gdp * EconomySystem.TreasuryIncomeRate;

            float fullLoadBill = 100f * DisplacementSystem.HostingCostPerPoint;
            Assert.Less(fullLoadBill, monthlyIncome * 2f,
                $"A full load of arrivals bills {fullLoadBill:F0} a month against income of " +
                $"{monthlyIncome:F0}. That is not a burden, it is a bankruptcy.");

            player.displacement.hosted = 100f;
            player.resources.treasury = 3f;
            turns.EndMonth();
            Assert.GreaterOrEqual(player.resources.treasury, -50f,
                "Hosting drove an empty treasury below zero. People who have arrived cannot " +
                "un-arrive, so this is the one cost that must be paid from what is there.");
        }

        [Test]
        public void ADecadeOfDoingNothingDoesNotEndInTheRed()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 7);
            var sim = new TurnManager(world);
            SimulationPipeline.Wire(sim, world);
            for (int month = 0; month < 120; month++)
            {
                while (world.HasOpenCrisis)
                    CrisisSystem.Resolve(world, world.activeCrises[0], 0);
                sim.EndMonth();
            }

            // The bug this guards produced −30,000 to −170,000 for every posting.
            // Wars, occupation and posture still run a belligerent mid-tier state
            // a few thousand into the red over a decade — a balance question, not
            // a leak — so the lines are drawn where only a leak can cross them.
            Assert.Greater(world.PlayerCountry.resources.treasury, -2000f,
                "A passive operator ended the decade deep in the red with nobody acting. " +
                "A world that bankrupts itself on its own is not a balance problem, it is a leak.");
            int bankrupt = 0;
            foreach (var country in world.countries)
                if (country.resources.treasury < -15000f) bankrupt++;
            Assert.AreEqual(0, bankrupt,
                $"{bankrupt} of {world.countries.Count} states are more than 15,000 in the red after a " +
                "passive decade — a monthly cost somewhere is out of scale with treasury income.");
        }
    }
}

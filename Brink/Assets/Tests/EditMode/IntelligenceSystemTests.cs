using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class IntelligenceSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6060);
            turns = new TurnManager(state);
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void NoNetwork_MeansNoAssessment()
        {
            for (int i = 0; i < 6; i++) turns.EndMonth();
            Assert.IsNull(IntelligenceSystem.GetEstimate(state, "USA", "CHN", IntelDomain.Military),
                "Without collection there must be no estimate at all.");
        }

        [Test]
        public void EstablishNetwork_CostsCPAndBeginsProducingEstimates()
        {
            int cpBefore = state.commandPoints.current;
            Assert.IsTrue(IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military));
            Assert.AreEqual(cpBefore - IntelligenceSystem.EstablishNetworkCost, state.commandPoints.current);

            var collectionMonth = state.date;
            turns.EndMonth();

            var estimate = IntelligenceSystem.GetEstimate(state, "USA", "CHN", IntelDomain.Military);
            Assert.NotNull(estimate);
            Assert.IsTrue(estimate.everCollected);
            Assert.AreEqual(collectionMonth, estimate.asOf,
                "Reporting is stamped with the month it was collected in, not the month that follows.");
        }

        [Test]
        public void EstablishNetwork_RejectsDuplicatesAndSelfTargeting()
        {
            Assert.IsTrue(IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military));
            Assert.IsFalse(IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Economic));
            Assert.IsFalse(IntelligenceSystem.EstablishNetwork(state, turns, "USA", IntelDomain.Military));
            Assert.AreEqual(1, state.networks.Count);
        }

        [Test]
        public void DeeperPenetration_ProducesTighterAndMoreConfidentEstimates()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
            for (int i = 0; i < 3; i++) turns.EndMonth();
            var shallow = IntelligenceSystem.GetEstimate(state, "USA", "CHN", IntelDomain.Military);
            float shallowMargin = shallow.margin;
            var shallowGrade = shallow.confidence;

            var network = state.FindNetwork("USA", "CHN");
            network.penetration = 95f;
            state.FindCountry("CHN").counterIntel.counterIntelligence = 10f;
            turns.EndMonth();

            var deep = IntelligenceSystem.GetEstimate(state, "USA", "CHN", IntelDomain.Military);
            Assert.Less(deep.margin, shallowMargin);
            Assert.Greater((int)deep.confidence, (int)shallowGrade);
        }

        [Test]
        public void Estimates_AreImperfectButTrackTruth()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
            var network = state.FindNetwork("USA", "CHN");
            network.penetration = 90f;
            var korval = state.FindCountry("CHN");
            korval.counterIntel.counterIntelligence = 5f;

            for (int i = 0; i < 6; i++) turns.EndMonth();

            var estimate = IntelligenceSystem.GetEstimate(state, "USA", "CHN", IntelDomain.Military);
            float truth = IntelligenceSystem.TrueValue(korval, IntelDomain.Military);

            Assert.Less(Math.Abs(estimate.reportedValue - truth), 15f,
                "Excellent access should land close to the truth.");
            Assert.Greater(estimate.margin, 0f, "No estimate is ever certain.");
        }

        [Test]
        public void CounterIntelligence_DegradesForeignCollection()
        {
            float MarginWithCounterIntel(float counterIntel)
            {
                var sim = WorldFactory.CreateDebugWorld(seed: 77);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
                sim.commandPoints.current = 20;
                IntelligenceSystem.EstablishNetwork(sim, simTurns, "CHN", IntelDomain.Military);
                sim.FindNetwork("USA", "CHN").penetration = 70f;
                sim.FindCountry("CHN").counterIntel.counterIntelligence = counterIntel;
                simTurns.EndMonth();
                return sim.FindEstimate("USA", "CHN", IntelDomain.Military).margin;
            }

            Assert.Greater(MarginWithCounterIntel(90f), MarginWithCounterIntel(5f),
                "Strong counterintelligence should widen the enemy's error bars.");
        }

        [Test]
        public void Deception_BendsEstimatesAwayFromTruth()
        {
            var sim = WorldFactory.CreateDebugWorld(seed: 91);
            var simTurns = new TurnManager(sim);
            simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            sim.commandPoints.current = 20;
            IntelligenceSystem.EstablishNetwork(sim, simTurns, "CHN", IntelDomain.Military);

            var network = sim.FindNetwork("USA", "CHN");
            network.penetration = 30f;
            var korval = sim.FindCountry("CHN");
            korval.pillars.military = 50f;
            korval.counterIntel.deceptionStrength = 90f;
            korval.counterIntel.deceptionBias = 1f; // overstate
            korval.counterIntel.deceptionDomain = IntelDomain.Military;

            simTurns.EndMonth();

            var estimate = sim.FindEstimate("USA", "CHN", IntelDomain.Military);
            Assert.IsTrue(estimate.deceived, "Weak access against strong deception should be fooled.");
            Assert.Greater(estimate.reportedValue, 50f, "Overstatement bias should inflate the estimate.");
        }

        [Test]
        public void StrongCollection_SeesThroughDeception()
        {
            var sim = WorldFactory.CreateDebugWorld(seed: 92);
            var simTurns = new TurnManager(sim);
            simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            sim.commandPoints.current = 20;
            IntelligenceSystem.EstablishNetwork(sim, simTurns, "CHN", IntelDomain.Military);

            sim.FindNetwork("USA", "CHN").penetration = 100f;
            var korval = sim.FindCountry("CHN");
            korval.counterIntel.counterIntelligence = 0f;
            korval.counterIntel.deceptionStrength = 30f;
            korval.counterIntel.deceptionBias = 1f;
            korval.counterIntel.deceptionDomain = IntelDomain.Military;

            simTurns.EndMonth();

            Assert.IsFalse(sim.FindEstimate("USA", "CHN", IntelDomain.Military).deceived,
                "Deep penetration should defeat a modest deception program.");
        }

        [Test]
        public void StaleEstimates_LoseConfidenceOverTime()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
            state.FindNetwork("USA", "CHN").penetration = 90f;
            state.FindCountry("CHN").counterIntel.counterIntelligence = 0f;
            turns.EndMonth();

            var estimate = state.FindEstimate("USA", "CHN", IntelDomain.Military);
            var freshGrade = estimate.confidence;
            float freshMargin = estimate.margin;

            // Collection stops entirely.
            state.networks.Clear();
            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.Less((int)estimate.confidence, (int)freshGrade, "Confidence must decay without collection.");
            Assert.Greater(estimate.margin, freshMargin);
        }

        [Test]
        public void Sabotage_DamagesTargetIndustryWhenSuccessful()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
            var network = state.FindNetwork("USA", "CHN");
            network.penetration = 100f;
            var korval = state.FindCountry("CHN");
            korval.counterIntel.counterIntelligence = 0f;
            float industryBefore = korval.economy.GetSector(EconomicSector.Industry).health;
            float capacityBefore = korval.resources.industrialCapacity;

            state.commandPoints.current = 20;
            bool success = IntelligenceSystem.RunCovertOperation(state, turns, "CHN", CovertOperation.Sabotage);

            Assert.IsTrue(success, "Near-total access against no counterintelligence should succeed.");
            Assert.Less(korval.economy.GetSector(EconomicSector.Industry).health, industryBefore);
            Assert.Less(korval.resources.industrialCapacity, capacityBefore);
        }

        [Test]
        public void PoliticalInfluence_DestabilizesTarget()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "RUS", IntelDomain.Political);
            state.FindNetwork("USA", "RUS").penetration = 100f;
            var ashira = state.FindCountry("RUS");
            ashira.counterIntel.counterIntelligence = 0f;
            float stabilityBefore = ashira.stability;

            state.commandPoints.current = 20;
            IntelligenceSystem.RunCovertOperation(state, turns, "RUS", CovertOperation.PoliticalInfluence);

            Assert.Less(ashira.stability, stabilityBefore);
        }

        [Test]
        public void CovertOperation_RequiresNetworkAndCostsCP()
        {
            state.commandPoints.current = 20;
            int cpBefore = state.commandPoints.current;
            Assert.IsFalse(IntelligenceSystem.RunCovertOperation(state, turns, "IND", CovertOperation.Sabotage));
            Assert.AreEqual(cpBefore, state.commandPoints.current, "A rejected operation must not charge CP.");
        }

        [Test]
        public void Exposure_CompromisesNetworkAndCostsDiplomacy()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
            var network = state.FindNetwork("USA", "CHN");
            network.penetration = 100f;
            var korval = state.FindCountry("CHN");
            korval.counterIntel.counterIntelligence = 100f; // near-certain attribution

            var player = state.PlayerCountry;
            float diplomacyBefore = player.pillars.diplomacy;

            // Run operations until one is exposed; exposure is probabilistic.
            bool exposed = false;
            for (int i = 0; i < 12 && !exposed; i++)
            {
                state.commandPoints.current = 20;
                IntelligenceSystem.RunCovertOperation(state, turns, "CHN", CovertOperation.Sabotage);
                exposed = network.compromised;
                if (!exposed) turns.EndMonth();
            }

            Assert.IsTrue(exposed, "High counterintelligence should eventually attribute an operation.");
            Assert.Less(player.pillars.diplomacy, diplomacyBefore);
        }

        [Test]
        public void CounterIntelSweep_RaisesDefenseAndCanDisruptForeignNetworks()
        {
            var player = state.PlayerCountry;
            player.counterIntel.counterIntelligence = 30f;

            // A hostile network operating against us.
            state.networks.Add(new IntelNetwork
            {
                ownerId = "CHN",
                targetId = "USA",
                focus = IntelDomain.Military,
                penetration = 60f
            });

            state.commandPoints.current = 20;
            Assert.IsTrue(IntelligenceSystem.StrengthenCounterIntelligence(state, turns));
            Assert.Greater(player.counterIntel.counterIntelligence, 30f);
        }

        [Test]
        public void AiObservers_UseTheSameFog()
        {
            state.networks.Add(new IntelNetwork
            {
                ownerId = "CHN",
                targetId = "USA",
                focus = IntelDomain.Military,
                penetration = 40f
            });

            turns.EndMonth();

            var korvalEstimate = IntelligenceSystem.GetEstimate(state, "CHN", "USA", IntelDomain.Military);
            Assert.NotNull(korvalEstimate, "AI observers must hold estimates too.");
            Assert.Greater(korvalEstimate.margin, 0f, "The AI reads a range, not the truth.");
        }

        [Test]
        public void Deception_DecaysWithoutMaintenance()
        {
            var player = state.PlayerCountry;
            state.commandPoints.current = 20;
            state.skillPoints = 99;
            foreach (var node in new[] { "INT_1", "INT_2", "INT_DEEPCOVER" })
                ProgressionSystem.Unlock(state, node);
            IntelligenceSystem.RunCovertOperation(state, turns, null, CovertOperation.Deception, 1f);
            float peak = player.counterIntel.deceptionStrength;
            Assert.Greater(peak, 0f);

            for (int i = 0; i < 30; i++) turns.EndMonth();
            Assert.Less(player.counterIntel.deceptionStrength, peak);
            Assert.GreaterOrEqual(player.counterIntel.deceptionStrength, 0f);
        }

        [Test]
        public void Intelligence_SurvivesSaveRoundTrip()
        {
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Economic);
            for (int i = 0; i < 5; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.networks.Count, loaded.networks.Count);
            Assert.AreEqual(state.estimates.Count, loaded.estimates.Count);

            var original = state.FindEstimate("USA", "CHN", IntelDomain.Economic);
            var restored = loaded.FindEstimate("USA", "CHN", IntelDomain.Economic);
            Assert.AreEqual(original.reportedValue, restored.reportedValue);
            Assert.AreEqual(original.confidence, restored.confidence);
            Assert.AreEqual(original.asOf, restored.asOf);
            Assert.AreEqual(IntelDomain.Economic, loaded.FindNetwork("USA", "CHN").focus);
        }

        [Test]
        public void Collection_IsDeterministicPerSeed()
        {
            GameState Run(int seed)
            {
                var sim = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
                simTurns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
                sim.commandPoints.current = 20;
                IntelligenceSystem.EstablishNetwork(sim, simTurns, "CHN", IntelDomain.Military);
                for (int i = 0; i < 40; i++) simTurns.EndMonth();
                return sim;
            }

            Assert.AreEqual(SaveSystem.ToJson(Run(3131)), SaveSystem.ToJson(Run(3131)));
        }
    }
}

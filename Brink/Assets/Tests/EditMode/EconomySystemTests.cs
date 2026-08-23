using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    public class EconomySystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5150);
            turns = new TurnManager(state);
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += EconomySystem.AgeSanctions;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Factory_SeedsEconomiesSectorsAndTrade()
        {
            foreach (var country in state.countries)
            {
                Assert.Greater(country.economy.gdp, 0f);
                Assert.AreEqual(7, country.economy.sectors.Count);
                Assert.AreEqual(100f, country.economy.marketIndex);
                Assert.AreEqual(1, country.economy.marketHistory.Count);
            }
            Assert.Greater(state.trade.Count, 20, "The trade network should cover the roster.");
            Assert.NotNull(state.FindTrade("USA", "CHN"));
            Assert.NotNull(state.FindTrade("CHN", "USA"), "Trade lookup must be order-independent.");
        }

        [Test]
        public void MonthlyUpdate_GrowsEconomyAndChartsMarket()
        {
            var eco = state.PlayerCountry.economy;
            float gdpBefore = eco.gdp;

            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.Greater(eco.gdp, gdpBefore, "A healthy peacetime economy should expand.");
            Assert.AreEqual(13, eco.marketHistory.Count);
            Assert.LessOrEqual(eco.marketHistory.Count, EconomyState.MaxHistory);
        }

        [Test]
        public void MarketHistory_TrimsToCap()
        {
            for (int i = 0; i < EconomyState.MaxHistory + 20; i++) turns.EndMonth();
            Assert.AreEqual(EconomyState.MaxHistory, state.PlayerCountry.economy.marketHistory.Count);
        }

        [Test]
        public void Sanctions_DamageTargetGrowthAndMarket()
        {
            GameState Run(bool sanctioned)
            {
                var sim = WorldFactory.CreateDebugWorld(seed: 61);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                simTurns.ResolveMonth += EconomySystem.AgeSanctions;
                if (sanctioned)
                {
                    sim.commandPoints.current = 20;
                    EconomySystem.ImposeSanctions(sim, simTurns, "CHN", SanctionSeverity.Severe);
                }
                for (int i = 0; i < 24; i++) simTurns.EndMonth();
                return sim;
            }

            var baseline = Run(false).FindCountry("CHN").economy;
            var pressured = Run(true).FindCountry("CHN").economy;

            // Stagflation: output falls while scarcity pushes prices up.
            Assert.Less(pressured.growthRate, baseline.growthRate);
            Assert.Less(pressured.marketIndex, baseline.marketIndex);
            Assert.Greater(pressured.inflation, baseline.inflation, "Import scarcity should raise prices.");
            Assert.Less(pressured.confidence, baseline.confidence);
        }

        [Test]
        public void Sanctions_BackfireOnTheSender()
        {
            GameState Run(bool sanctioned)
            {
                var sim = WorldFactory.CreateDebugWorld(seed: 62);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                simTurns.ResolveMonth += EconomySystem.AgeSanctions;
                if (sanctioned)
                {
                    sim.commandPoints.current = 20;
                    EconomySystem.ImposeSanctions(sim, simTurns, "CHN", SanctionSeverity.Severe);
                }
                for (int i = 0; i < 24; i++) simTurns.EndMonth();
                return sim;
            }

            var baseline = Run(false).PlayerCountry.economy;
            var sender = Run(true).PlayerCountry.economy;

            Assert.Greater(sender.inflation, baseline.inflation, "Sanctioning a major partner must cost the sender.");
            Assert.Less(sender.growthRate, baseline.growthRate);
        }

        [Test]
        public void Blowback_ScalesWithTradeExposure()
        {
            state.commandPoints.current = 40;
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Coercive); // volume 62
            float highExposure = EconomySystem.SanctionBlowbackFor(state, "USA");

            var other = WorldFactory.CreateDebugWorld(seed: 5150);
            var otherTurns = new TurnManager(other);
            other.commandPoints.current = 40;
            EconomySystem.ImposeSanctions(other, otherTurns, "IND", SanctionSeverity.Coercive); // volume 38
            float lowExposure = EconomySystem.SanctionBlowbackFor(other, "USA");

            Assert.Greater(highExposure, lowExposure,
                "Sanctioning a heavy trade partner should blow back harder than a marginal one.");
        }

        [Test]
        public void ImposeSanctions_CostsCPAndRejectsDuplicates()
        {
            int cpBefore = state.commandPoints.current;
            Assert.IsTrue(EconomySystem.ImposeSanctions(state, turns, "RUS", SanctionSeverity.Pressure));
            Assert.AreEqual(cpBefore - EconomySystem.SanctionCost, state.commandPoints.current);

            Assert.IsFalse(EconomySystem.ImposeSanctions(state, turns, "RUS", SanctionSeverity.Severe));
            Assert.AreEqual(1, state.sanctions.Count);
        }

        [Test]
        public void SevereSanctions_EmbargoTradeLink_LiftRestoresIt()
        {
            state.commandPoints.current = 20;
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Severe);
            var link = state.FindTrade("USA", "CHN");
            Assert.IsTrue(link.embargoed);

            float healthUnderEmbargo = EconomySystem.TradeHealth(state, "USA");
            EconomySystem.LiftSanctions(state, turns, "CHN");

            Assert.IsFalse(link.embargoed);
            Assert.AreEqual(0, state.sanctions.Count);
            Assert.Greater(EconomySystem.TradeHealth(state, "USA"), healthUnderEmbargo);
        }

        [Test]
        public void Tariffs_ReduceEffectiveTradeHealth()
        {
            float before = EconomySystem.TradeHealth(state, "USA");
            state.commandPoints.current = 10;
            Assert.IsTrue(EconomySystem.SetTariff(state, turns, "CHN", 60f));
            Assert.Less(EconomySystem.TradeHealth(state, "USA"), before);
        }

        [Test]
        public void War_DepressesGrowthAndRaisesInflation()
        {
            GameState Run(bool war)
            {
                var sim = WorldFactory.CreateDebugWorld(seed: 71);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                simTurns.ResolveMonth += ConfrontationSystem.MonthlyTick;
                if (war)
                {
                    sim.commandPoints.current = 40;
                    var confrontation = ConfrontationSystem.Begin(sim, simTurns, "USA", "CHN",
                        ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
                    sim.commandPoints.current = 40;
                    ConfrontationSystem.SetEscalation(sim, simTurns, confrontation, EscalationState.TotalWar);
                }
                for (int i = 0; i < 18; i++) simTurns.EndMonth();
                return sim;
            }

            var peace = Run(false).PlayerCountry.economy;
            var war = Run(true).PlayerCountry.economy;

            Assert.Less(war.growthRate, peace.growthRate);
            Assert.Greater(war.inflation, peace.inflation);
            Assert.Greater(war.debtToGdp, peace.debtToGdp);
        }

        [Test]
        public void EnergyShortage_DragsGrowthAndFuelsInflation()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 5f;
            var control = WorldFactory.CreateDebugWorld(seed: 5150);
            var controlTurns = new TurnManager(control);
            controlTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
            control.PlayerCountry.resources.energy = 95f;

            for (int i = 0; i < 12; i++) { turns.EndMonth(); controlTurns.EndMonth(); }

            Assert.Less(player.economy.growthRate, control.PlayerCountry.economy.growthRate);
            Assert.Greater(player.economy.inflation, control.PlayerCountry.economy.inflation);
        }

        [Test]
        public void SevereDistress_ErodesApprovalAndStability()
        {
            var player = state.PlayerCountry;
            player.economy.inflation = 25f;
            player.economy.growthRate = -6f;
            float approvalBefore = player.governmentApproval;
            float stabilityBefore = player.stability;

            for (int i = 0; i < 6; i++) turns.EndMonth();

            Assert.Less(player.governmentApproval, approvalBefore);
            Assert.Less(player.stability, stabilityBefore);
        }

        [Test]
        public void Economy_NeverCollapsesToZero()
        {
            state.commandPoints.current = 60;
            state.skillPoints = 99;
            foreach (var node in new[] { "ECO_1", "ECO_2", "ECO_STRATEGIC", "DIP_1", "DIP_2", "ECO_EXISTENTIAL" })
                ProgressionSystem.Unlock(state, node);
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Existential);
            var korval = state.FindCountry("CHN");

            for (int i = 0; i < 120; i++) turns.EndMonth();

            Assert.Greater(korval.economy.gdp, 0f, "Economic pressure coerces; it does not zero out a GDP.");
            Assert.Greater(korval.economy.marketIndex, 0f);
            Assert.GreaterOrEqual(korval.economy.unemployment, 1.5f);
            Assert.LessOrEqual(korval.economy.unemployment, 35f);
        }

        [Test]
        public void Sanctions_AgeOverTime()
        {
            state.commandPoints.current = 20;
            EconomySystem.ImposeSanctions(state, turns, "RUS", SanctionSeverity.Pressure);
            for (int i = 0; i < 5; i++) turns.EndMonth();
            Assert.AreEqual(5, state.sanctions[0].monthsActive);
        }

        [Test]
        public void Economy_SurvivesSaveRoundTrip()
        {
            state.commandPoints.current = 20;
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Coercive);
            for (int i = 0; i < 8; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var original = state.PlayerCountry.economy;
            var restored = loaded.PlayerCountry.economy;

            Assert.AreEqual(original.gdp, restored.gdp);
            Assert.AreEqual(original.marketHistory.Count, restored.marketHistory.Count);
            Assert.AreEqual(original.sectors.Count, restored.sectors.Count);
            Assert.AreEqual(state.sanctions.Count, loaded.sanctions.Count);
            Assert.AreEqual(state.trade.Count, loaded.trade.Count);
            Assert.AreEqual("CHN", loaded.sanctions[0].targetId);
        }

        [Test]
        public void Simulation_IsDeterministicPerSeed()
        {
            GameState Run(int seed)
            {
                var sim = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += CabinetSystem.MonthlyAct;
                simTurns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                simTurns.ResolveMonth += EconomySystem.AgeSanctions;
                for (int i = 0; i < 60; i++) simTurns.EndMonth();
                return sim;
            }

            Assert.AreEqual(SaveSystem.ToJson(Run(4242)), SaveSystem.ToJson(Run(4242)));
        }
    }

    public class MarketChartTests
    {
        [Test]
        public void LineChart_RendersRequestedHeightPlusAxis()
        {
            var values = new float[] { 100, 104, 98, 110, 121, 118 };
            string chart = AsciiChart.LineChart(values, 40, 6);
            var lines = chart.Split('\n');
            Assert.AreEqual(7, lines.Length, "Six plot rows plus the axis row.");
            StringAssert.Contains("│", lines[0]);
            StringAssert.Contains("└", lines[6]);
        }

        [Test]
        public void LineChart_HandlesFlatAndTinySeries()
        {
            Assert.IsNotEmpty(AsciiChart.LineChart(new float[] { 50, 50, 50 }, 20, 4));
            Assert.AreEqual(string.Empty, AsciiChart.LineChart(new float[0], 20, 4));
            Assert.AreEqual(string.Empty, AsciiChart.LineChart(null, 20, 4));
        }

        [Test]
        public void LineChart_SamplesTailWhenSeriesExceedsWidth()
        {
            var values = new float[200];
            for (int i = 0; i < values.Length; i++) values[i] = i;
            string chart = AsciiChart.LineChart(values, 30, 5);
            StringAssert.Contains("199.0", chart, "Newest value should anchor the scale.");
        }
    }
}

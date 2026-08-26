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
            // Must use the real pipeline: this class runs decade-length
            // invariants on the live world (Economy_NeverCollapsesToZero holds a
            // foreign economy under existential sanctions for 120 months,
            // MarketHistory_TrimsToCap runs longer still). A hand-wired subset
            // measures an economy that no government, cabinet, acquisition
            // programme or AI can respond to — a different game.
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- the treasury trend warning ----------

        [Test]
        public void TreasuryTrend_TracksASustainedDeficit()
        {
            // Force a drain larger than the world's own noise and check the
            // readout reports roughly that number, in the right direction.
            for (int month = 0; month < 12; month++)
            {
                state.PlayerCountry.resources.treasury -= 300f;
                turns.EndMonth();
            }

            Assert.IsTrue(state.treasuryTrendSeeded, "the trend was never seeded");
            Assert.Less(state.treasuryTrend, -100f,
                $"A forced 300/month drain reads as {state.treasuryTrend:F0}/month. Deficit " +
                "spending punishes on a lag of years, so this readout is the only timely " +
                "warning the operator gets — a test campaign bankrupted a healthy country " +
                "to −4,905 without one.");
        }

        [Test]
        public void ASinkingTreasuryRaisesAttention()
        {
            state.treasuryTrendSeeded = true;
            state.treasuryTrend = -80f;
            state.PlayerCountry.resources.treasury = 900f; // ~11 months of runway

            bool warned = false;
            foreach (var item in AttentionSystem.Collect(state))
                if (item.viewId == "ECONOMY" && item.summary.Contains("exceeds income")) warned = true;
            Assert.IsTrue(warned, "eleven months of runway at the current burn raised nothing");

            state.PlayerCountry.resources.treasury = -500f;
            bool urgent = false;
            foreach (var item in AttentionSystem.Collect(state))
                if (item.viewId == "ECONOMY" && item.level == AttentionLevel.Decision) urgent = true;
            Assert.IsTrue(urgent, "an account already dry and sinking is not flagged as a decision");
        }

        [Test]
        public void AHealthyAccountStaysQuiet()
        {
            // A warning that cries wolf is a warning nobody reads — the
            // telemetry ratchet detector lesson, applied here on day one.
            state.treasuryTrendSeeded = true;
            state.treasuryTrend = 12f;
            state.PlayerCountry.resources.treasury = 800f;

            foreach (var item in AttentionSystem.Collect(state))
                Assert.IsFalse(item.viewId == "ECONOMY" && item.summary.Contains("income"),
                    "a growing treasury triggered the deficit warning");

            // And a deficit with years of runway is information the briefing
            // line already carries — not an attention item.
            state.treasuryTrend = -4.5f;
            state.PlayerCountry.resources.treasury = 5000f; // ~1100 months of runway
            foreach (var item in AttentionSystem.Collect(state))
                Assert.IsFalse(item.viewId == "ECONOMY" && item.summary.Contains("exceeds income"),
                    "a nine-decade runway is being escalated as if it were a crisis");
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
                // NARROW PIPELINE: a controlled A/B where both arms are wired
                // identically and the sanction is the only difference — omitting
                // the AI, government and cabinet keeps the comparison about
                // EconomySystem's own sanction arithmetic rather than about how
                // a rival government chose to respond.
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
                // NARROW PIPELINE: same controlled A/B as above — both arms are
                // wired identically, so omitting the AI/government/cabinet keeps
                // the measured difference the sender's own blowback arithmetic.
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
                // NARROW PIPELINE: peace-vs-war A/B with both arms wired the
                // same. The AI is omitted deliberately — if it could settle or
                // widen the war the two arms would stop differing only by the
                // war, and the assertion is EconomySystem's war-cost arithmetic.
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
            // The control arm must be wired exactly like the treatment arm (the
            // shared `turns`, which runs the real pipeline), or the comparison
            // measures the difference in pipelines rather than in energy.
            SimulationPipeline.Wire(controlTurns, control);
            control.PlayerCountry.resources.energy = 95f;

            for (int i = 0; i < 12; i++) { turns.EndMonth(); controlTurns.EndMonth(); }

            // `resources.energy` drifts toward `EnergyCeilingFor` every month, so
            // both arms of this A/B are being pulled back toward the same authored
            // endowment (78 for the USA) the whole time it runs. `energyDrag` — the
            // entire mechanism under test — only exists below 40, so if the shortage
            // arm has climbed past that line the assertions below are comparing two
            // healthy economies and mean nothing.
            Assert.Less(player.resources.energy, 40f,
                $"The shortage arm recovered to {player.resources.energy:F1} energy, above the 40 "
                + "threshold where energyDrag exists, so this asserts nothing about a shortage.");
            Assert.Greater(control.PlayerCountry.resources.energy, 40f,
                $"The control arm fell to {control.PlayerCountry.resources.energy:F1} energy and is "
                + "itself short, so the two arms no longer differ by the thing being measured.");

            Assert.Less(player.economy.growthRate, control.PlayerCountry.economy.growthRate);
            Assert.Greater(player.economy.inflation, control.PlayerCountry.economy.inflation);
        }

        [Test]
        public void SevereDistress_ErodesApprovalAndStability()
        {
            var player = state.PlayerCountry;
            float approvalBefore = player.governmentApproval;
            float stabilityBefore = player.stability;

            // Sustain the distress by its causes, and run long enough to outlast
            // a government working against it.
            //
            // This used to assign inflation and growth once and run six months.
            // EconomySystem approaches inflation back toward ~2.2 at 30%/month,
            // so the "severe distress" was over by month three — and once this
            // fixture was wired to the real pipeline, six months of a functioning
            // cabinet left the country *better off than it started*. The test was
            // measuring a brief shock followed by a recovery and calling the
            // result erosion.
            //
            // A government resisting a slump is correct behaviour, so the distress
            // has to be ongoing for the assertion to mean anything.
            // Four years. The market index is fundamentals-anchored and falls
            // slowly, and it is what drives the crisis regime, so at two years the
            // distress term had only reached a third of its depth and inflation
            // sat at 7.4 — the precondition below caught that rather than letting
            // the real assertions pass or fail on a fixture that had not taken.
            for (int i = 0; i < 48; i++)
            {
                player.resources.energy = 10f;
                player.economy.confidence = 15f;
                foreach (var sector in player.economy.sectors) sector.health = 15f;
                turns.EndMonth();
            }

            Assert.Greater(player.economy.inflation, 8f,
                "The economy never became distressed, so this asserts nothing about distress.");

            Assert.Less(player.governmentApproval, approvalBefore,
                $"Four years of severe distress left approval at {player.governmentApproval:F1} " +
                $"against {approvalBefore:F1} before it.");
            Assert.Less(player.stability, stabilityBefore,
                $"Four years of severe distress left stability at {player.stability:F1} " +
                $"against {stabilityBefore:F1} before it.");
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
                // Must use the real pipeline: a determinism claim about "the
                // simulation" is worth nothing if it covers four of its systems.
                SimulationPipeline.Wire(simTurns, sim);
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

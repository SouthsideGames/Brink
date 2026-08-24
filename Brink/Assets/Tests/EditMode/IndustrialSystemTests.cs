using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The economy's recurring decision (GDD §20 amendment).
    ///
    /// A verb audit found **zero treasury spends** across `EconomySystem`,
    /// `TradeSystem` and `DiplomacySystem`, against six in the military. Every
    /// economy control was a one-shot or a toggle — trade is once per partner,
    /// sanctions are a standing regime, tariffs are a binary flip — so the pillar
    /// that earns the money could not spend a penny of it, and after roughly year
    /// two the screen had nothing left to press.
    /// </summary>
    public class IndustrialSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 2244);
            state.commandPoints.current = 40;
            state.PlayerCountry.resources.treasury = 400000f;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- it is a real sink ----------

        [Test]
        public void AProgrammeCostsTreasuryEveryMonthItRuns()
        {
            var player = state.PlayerCountry;
            Assert.IsTrue(IndustrialSystem.Begin(state, turns,
                EconomicSector.Industry, IndustrialScale.Expansion));

            float before = player.resources.treasury;
            turns.EndMonth();

            Assert.Less(player.resources.treasury, before,
                "An industrial programme drew nothing from the treasury. The economy "
                + "pillar exists to give the money somewhere to go.");
        }

        [Test]
        public void CapacityArrivesYearsLaterNotImmediately()
        {
            var player = state.PlayerCountry;
            float outputBefore = player.economy.Sector(EconomicSector.Industry).output;

            IndustrialSystem.Begin(state, turns, EconomicSector.Industry, IndustrialScale.Expansion);

            // One month in: paid for, nothing delivered.
            turns.EndMonth();
            Assert.LessOrEqual(player.economy.Sector(EconomicSector.Industry).output,
                outputBefore + 1f,
                "Capacity appeared the month it was ordered. Building takes years — "
                + "that delay is what makes it a decision about the future.");

            for (int month = 0; month < IndustrialSystem.MonthsFor(IndustrialScale.Expansion); month++)
                turns.EndMonth();

            Assert.Greater(player.economy.Sector(EconomicSector.Industry).output, outputBefore + 5f,
                "Two years and a small fortune bought no capacity at all.");
        }

        [Test]
        public void IndustryFeedsWhatTheCountryCanBuildAndFinanceDoesNot()
        {
            // A bigger bank does not make more aircraft. If every sector fed
            // industrial capacity, the choice of *where* to invest would be
            // decoration.
            float CapacityAfter(EconomicSector sector)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 2244);
                world.commandPoints.current = 40;
                world.PlayerCountry.resources.treasury = 400000f;
                var localTurns = new TurnManager(world);
                SimulationPipeline.Wire(localTurns, world);

                float before = world.PlayerCountry.resources.industrialCapacity;
                IndustrialSystem.Begin(world, localTurns, sector, IndustrialScale.Modernisation);
                for (int m = 0; m < IndustrialSystem.MonthsFor(IndustrialScale.Modernisation) + 2; m++)
                    localTurns.EndMonth();

                return world.PlayerCountry.resources.industrialCapacity - before;
            }

            Assert.Greater(CapacityAfter(EconomicSector.Industry),
                CapacityAfter(EconomicSector.Finance),
                "Rebuilding industry and rebuilding finance bought the same ability to "
                + "build things, so where the money goes does not matter.");
        }

        // ---------- and it is bounded ----------

        [Test]
        public void OnlyThreeProgrammesRunAtOnce()
        {
            Assert.IsTrue(IndustrialSystem.Begin(state, turns, EconomicSector.Industry, IndustrialScale.Maintenance));
            Assert.IsTrue(IndustrialSystem.Begin(state, turns, EconomicSector.Energy, IndustrialScale.Maintenance));
            Assert.IsTrue(IndustrialSystem.Begin(state, turns, EconomicSector.Technology, IndustrialScale.Maintenance));

            Assert.IsFalse(IndustrialSystem.Begin(state, turns, EconomicSector.Finance, IndustrialScale.Maintenance),
                "A fourth programme started. Investing everywhere at once means never "
                + "having to say what the country is for.");
        }

        [Test]
        public void OneProgrammePerSector()
        {
            Assert.IsTrue(IndustrialSystem.BeginBy(state, state.playerCountryId,
                EconomicSector.Industry, IndustrialScale.Expansion));
            Assert.IsFalse(IndustrialSystem.BeginBy(state, state.playerCountryId,
                EconomicSector.Industry, IndustrialScale.Modernisation),
                "Two overlapping builds in one sector is the operator paying twice "
                + "for one thing.");
        }

        [Test]
        public void AProgrammeWeCannotPayForStops()
        {
            // Without this the treasury goes negative and the build carries on,
            // which makes the cost decoration — the trap that made war footing
            // meaningless until it could lapse.
            var player = state.PlayerCountry;
            IndustrialSystem.Begin(state, turns, EconomicSector.Industry, IndustrialScale.Modernisation);

            player.resources.treasury = 10f;
            turns.EndMonth();

            Assert.AreEqual(0, player.economy.programmes.Count,
                "A programme the treasury cannot carry kept running.");
            Assert.GreaterOrEqual(player.resources.treasury, 0f,
                "It spent money the country did not have.");
        }

        [Test]
        public void CancellingForfeitsWhatWasSpent()
        {
            var player = state.PlayerCountry;
            IndustrialSystem.Begin(state, turns, EconomicSector.Energy, IndustrialScale.Expansion);

            for (int month = 0; month < 6; month++) turns.EndMonth();
            float spentSoFar = 400000f - player.resources.treasury;
            float outputBefore = player.economy.Sector(EconomicSector.Energy).output;

            Assert.IsTrue(IndustrialSystem.Cancel(state, player.id, EconomicSector.Energy));
            turns.EndMonth();

            Assert.Greater(spentSoFar, 0f, "Nothing was spent, so nothing could be forfeited.");
            Assert.LessOrEqual(player.economy.Sector(EconomicSector.Energy).output, outputBefore + 1f,
                "Cancelling paid out anyway. A commitment you can walk away from whole "
                + "is not a commitment.");
        }

        // ---------- the world can do it too ----------

        [Test]
        public void ForeignStatesCanInvestInTheirOwnEconomies()
        {
            // A verb the world cannot use is this codebase's most-repeated bug.
            var chn = state.FindCountry("CHN");
            chn.resources.treasury = 400000f;

            Assert.IsTrue(IndustrialSystem.BeginBy(state, "CHN",
                EconomicSector.Industry, IndustrialScale.Expansion),
                "A foreign state cannot invest in its own industry.");

            float before = chn.economy.Sector(EconomicSector.Industry).output;
            for (int month = 0; month < IndustrialSystem.MonthsFor(IndustrialScale.Expansion) + 2; month++)
                turns.EndMonth();

            Assert.Greater(chn.economy.Sector(EconomicSector.Industry).output, before,
                "A foreign programme ran to completion and delivered nothing.");
        }

        // ---------- the save ----------

        [Test]
        public void ProgrammesSurviveASaveRoundTrip()
        {
            IndustrialSystem.Begin(state, turns, EconomicSector.Technology, IndustrialScale.Modernisation);
            var restored = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(1, restored.PlayerCountry.economy.programmes.Count);
            Assert.AreEqual(EconomicSector.Technology,
                restored.PlayerCountry.economy.programmes[0].sector);
            Assert.AreEqual(IndustrialScale.Modernisation,
                restored.PlayerCountry.economy.programmes[0].scale);
        }

        [Test]
        public void AnOldSaveWithNoProgrammesIsFine()
        {
            // Empty is genuinely correct here, unlike branch inventories: a world
            // that predates this simply has nothing under construction.
            var restored = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsNotNull(restored.PlayerCountry.economy.programmes);
            Assert.AreEqual(0, restored.PlayerCountry.economy.programmes.Count);
        }
    }
}

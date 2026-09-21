using System;
using System.Linq;
using System.Reflection;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using Brink.UI.Views;
using NUnit.Framework;
using UnityEngine.UIElements;

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

        [TestCase(IndustrialScale.Maintenance)]
        [TestCase(IndustrialScale.Expansion)]
        [TestCase(IndustrialScale.Modernisation)]
        public void ProjectIdentityAndFundedProgressSurviveReloadWithoutReadSideEffects(IndustrialScale scale)
        {
            var player = state.PlayerCountry;
            Assert.IsTrue(IndustrialSystem.Begin(state, turns, EconomicSector.Energy, scale));
            var programme = player.economy.programmes.Single();
            string name = IndustrialSystem.ProjectName(programme);
            StringAssert.Contains("National Energy Works", name);
            StringAssert.Contains(programme.started.DisplayString, name);
            StringAssert.Contains(name, state.chronicle.Last().text);
            Assert.AreEqual(Publicity.Secret, state.chronicle.Last().publicity);
            float money = player.resources.treasury;
            IndustrialSystem.MonthlyUpdate(state); // one paid month; no other systems in this measurement
            Assert.AreEqual(money - IndustrialSystem.MonthlyCostFor(scale), player.resources.treasury);
            state.date = new GameDate(2000, 1); // calendar time is not funded progress
            string before = SaveSystem.ToJson(state);
            string progress = IndustrialSystem.ProjectProgress(player, programme);
            StringAssert.Contains($"FUNDED WORK: 1/{IndustrialSystem.MonthsFor(scale)}", progress);
            StringAssert.Contains("Treasury now covers", progress);
            Assert.AreEqual(name, IndustrialSystem.ProjectName(programme));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
            var loaded = SaveSystem.FromJson(before);
            Assert.AreEqual(name, IndustrialSystem.ProjectName(loaded.PlayerCountry.economy.programmes.Single()));
            Assert.AreEqual(progress, IndustrialSystem.ProjectProgress(loaded.PlayerCountry, loaded.PlayerCountry.economy.programmes.Single()));
        }

        [Test]
        public void LegacyProjectHasAnHonestUnknownDateAndReadsDoNotBackfillIt()
        {
            var programme = new IndustrialProgramme { sector = EconomicSector.Finance,
                scale = IndustrialScale.Expansion, monthsRemaining = 17 };
            state.PlayerCountry.economy.programmes.Add(programme);
            string before = SaveSystem.ToJson(state);
            StringAssert.Contains("START DATE UNKNOWN", IndustrialSystem.ProjectName(programme));
            StringAssert.Contains("FUNDED WORK: 7/24", IndustrialSystem.ProjectProgress(state.PlayerCountry, programme));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ProjectFailureKeepsItsNameAndLossWithoutDeliveringOrRefunding(bool cancel)
        {
            var player = state.PlayerCountry;
            Assert.IsTrue(IndustrialSystem.BeginBy(state, player.id, EconomicSector.Industry, IndustrialScale.Expansion));
            var programme = player.economy.programmes.Single();
            string name = IndustrialSystem.ProjectName(programme);
            IndustrialSystem.MonthlyUpdate(state);
            float output = player.economy.Sector(EconomicSector.Industry).output;
            float endowment = player.resources.industrialEndowment;
            if (!cancel) player.resources.treasury = IndustrialSystem.MonthlyCostFor(programme.scale) - 1;
            float money = player.resources.treasury;
            if (cancel) Assert.IsTrue(IndustrialSystem.Cancel(state, player.id, programme.sector));
            else
            {
                StringAssert.Contains("falls short", IndustrialSystem.ProjectProgress(player, programme));
                IndustrialSystem.MonthlyUpdate(state);
            }
            Assert.IsEmpty(player.economy.programmes);
            Assert.AreEqual(money, player.resources.treasury);
            Assert.AreEqual(output, player.economy.Sector(EconomicSector.Industry).output);
            Assert.AreEqual(endowment, player.resources.industrialEndowment);
            StringAssert.Contains(name, state.chronicle.Last().text);
            StringAssert.StartsWith(cancel ? "PROJECT CANCELLED:" : "PROJECT LAPSED:", state.chronicle.Last().text);
            Assert.AreEqual(Publicity.Secret, state.chronicle.Last().publicity);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.chronicle.Last().text, loaded.chronicle.Last().text);
            int count = state.chronicle.Count;
            IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(count, state.chronicle.Count, "failure is recorded once, not every month");
        }

        [TestCase(EconomicSector.Industry)]
        [TestCase(EconomicSector.Energy)]
        [TestCase(EconomicSector.Finance)]
        public void CompletedProjectReportsAppliedClampedBenefitsAndPersistsItsRecord(EconomicSector kind)
        {
            var player = state.PlayerCountry;
            var sector = player.economy.Sector(kind);
            sector.output = 99f; sector.health = 99.5f;
            player.resources.industrialEndowment = 100f;
            player.resources.industrialCapacity = 100f;
            player.resources.energyEndowment = 100f;
            Assert.IsTrue(IndustrialSystem.BeginBy(state, player.id, kind, IndustrialScale.Maintenance));
            string name = IndustrialSystem.ProjectName(player.economy.programmes.Single());
            for (int m = 0; m < IndustrialSystem.MonthsFor(IndustrialScale.Maintenance); m++) IndustrialSystem.MonthlyUpdate(state);
            Assert.IsEmpty(player.economy.programmes);
            Assert.AreEqual(100f, sector.output); Assert.AreEqual(100f, sector.health);
            var entry = state.chronicle.Last();
            StringAssert.Contains(name, entry.text);
            StringAssert.Contains("sector output +1, sector health +0.5", entry.text);
            StringAssert.Contains("industrial endowment 0, energy endowment 0", entry.text);
            Assert.AreEqual(Publicity.Public, entry.publicity);
            Assert.AreEqual(entry.text, state.notifications.Last(n => n.title == "PROGRAMME COMPLETE").body);
            Assert.AreEqual(entry.text, SaveSystem.FromJson(SaveSystem.ToJson(state)).chronicle.Last().text);
        }

        [TestCase(EconomicSector.Industry, 3.15f, 0f)]
        [TestCase(EconomicSector.Technology, 1.4f, 0f)]
        [TestCase(EconomicSector.Energy, 0f, 2.45f)]
        public void CompletedProjectNamesTheRealEndowmentItBuilt(EconomicSector kind, float industryGain, float energyGain)
        {
            var player = state.PlayerCountry;
            player.resources.industrialCapacity = player.resources.industrialEndowment = 50f;
            player.resources.energyEndowment = 50f;
            Assert.IsTrue(IndustrialSystem.BeginBy(state, player.id, kind, IndustrialScale.Maintenance));
            for (int m = 0; m < 12; m++) IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(50f + industryGain, player.resources.industrialEndowment, .0001f);
            Assert.AreEqual(50f + energyGain, player.resources.energyEndowment, .0001f);
            StringAssert.Contains($"industrial endowment {industryGain:+0.##;-0.##;0}, energy endowment {energyGain:+0.##;-0.##;0}", state.chronicle.Last().text);
            int count = state.chronicle.Count;
            IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(count, state.chronicle.Count, "completion recorded once");
        }

        [Test]
        public void RefusedProjectDoesNotInventAnEvent()
        {
            Assert.IsTrue(IndustrialSystem.BeginBy(state, state.playerCountryId, EconomicSector.Energy, IndustrialScale.Expansion));
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(IndustrialSystem.BeginBy(state, state.playerCountryId, EconomicSector.Energy, IndustrialScale.Expansion));
            Assert.IsFalse(IndustrialSystem.BeginBy(state, "UNKNOWN", EconomicSector.Energy, IndustrialScale.Expansion));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void ForeignCompletionDoesNotPublishHiddenAppliedFiguresOrNotifyThePlayer()
        {
            var foreign = state.FindCountry("CHN");
            foreign.resources.treasury = 400000f;
            Assert.IsTrue(IndustrialSystem.BeginBy(state, foreign.id, EconomicSector.Energy, IndustrialScale.Maintenance));
            int notices = state.notifications.Count;
            for (int m = 0; m < 12; m++) IndustrialSystem.MonthlyUpdate(state);
            var entry = state.chronicle.Last();
            Assert.AreEqual(foreign.id, entry.countryId);
            Assert.AreEqual(Publicity.Public, entry.publicity);
            StringAssert.StartsWith("PROJECT COMPLETE:", entry.text);
            StringAssert.DoesNotContain("Applied points", entry.text);
            Assert.AreEqual(notices, state.notifications.Count);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void ProjectPanelIsPureBoundedAndShowsOnlyOurLatestFiveRecords(int columns)
        {
            var gc = GameController.Instance;
            var previous = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                state.authorizedPillarMask = ~0;
                Assert.IsTrue(IndustrialSystem.BeginBy(state, state.playerCountryId, EconomicSector.Energy, IndustrialScale.Expansion));
                for (int n = 0; n < 7; n++) state.AddChronicle(ChronicleCategory.Economic, state.playerCountryId, "PROJECT TEST " + n);
                state.AddChronicle(ChronicleCategory.Economic, "CHN", "PROJECT FOREIGN SECRET");
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new EconomyView();
                view.Refresh(); // existing council/view lazy initialization is outside this read measurement
                string before = SaveSystem.ToJson(state);
                view.Refresh(); view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                var labels = view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text"));
                string text = System.Text.RegularExpressions.Regex.Replace(string.Join(" ", labels.Select(l => l.text)), @"\s+", " ");
                StringAssert.Contains("NATIONAL PROJECTS", text);
                StringAssert.Contains("National Energy Works", text);
                StringAssert.Contains("FUNDED WORK: 0/24", text);
                StringAssert.Contains("4560 AT CURRENT TERMS", text);
                StringAssert.Contains("PROJECT TEST 2", text);
                StringAssert.Contains("PROJECT TEST 6", text);
                StringAssert.DoesNotContain("PROJECT TEST 1", text);
                StringAssert.DoesNotContain("PROJECT FOREIGN SECRET", text);
                foreach (var label in labels)
                    foreach (var line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
            }
            finally
            {
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                TerminalMetrics.ResetForTests();
            }
        }

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

        // ---------- the layer has to reach the economy ----------

        /// <summary>
        /// A damaged sector layer has to show up in the economy.
        ///
        /// **`output` and `health` were written by four systems and read by
        /// none** — not by growth, not by the market index, not by anything. Seven
        /// entries per country, modelled in detail and consumed nowhere, which
        /// made every instrument aimed at them inert: `CovertOperation.Sabotage`
        /// is documented as damaging sector health and did nothing, the strategic
        /// endgame's −25 health did nothing, and import displacement did nothing.
        ///
        /// This is the test whose absence let an entire layer sit decorative. It
        /// asserts consequence, not a formula, so it survives retuning.
        /// </summary>
        [Test]
        public void GuttingTheSectorsHurtsGrowth()
        {
            float GrowthAfter(float sectorHealth, float sectorOutput)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 2244);
                var localTurns = new TurnManager(world);
                SimulationPipeline.Wire(localTurns, world);
                var player = world.PlayerCountry;

                for (int month = 0; month < 24; month++)
                {
                    foreach (var sector in player.economy.sectors)
                    {
                        sector.health = sectorHealth;
                        sector.output = sectorOutput;
                    }
                    localTurns.EndMonth();
                }
                return player.economy.growthRate;
            }

            float healthy = GrowthAfter(90f, 80f);
            float gutted = GrowthAfter(20f, 25f);

            Assert.Greater(healthy, gutted,
                $"An economy whose industries are running at 90/80 grew at {healthy:F2} and one "
                + $"at 20/25 grew at {gutted:F2}. If the sector layer does not reach growth, "
                + "everything aimed at it — sabotage, blockade, industrial programmes, import "
                + "competition — is decoration.");
        }

        [Test]
        public void CapacityWithoutFunctioningIsIdle()
        {
            // Multiplied rather than averaged: a sector with plant it cannot run
            // is not half-productive. That is what makes stopping an economy a
            // real alternative to destroying it.
            var eco = state.PlayerCountry.economy;

            foreach (var sector in eco.sectors) { sector.output = 90f; sector.health = 10f; }
            float paralysed = EconomySystem.SectorStrength(eco);

            foreach (var sector in eco.sectors) { sector.output = 90f; sector.health = 90f; }
            float running = EconomySystem.SectorStrength(eco);

            Assert.Less(paralysed, running * 0.4f,
                $"Plant at 90 capacity and 10 functioning scored {paralysed:F1} against "
                + $"{running:F1} when running. Idle capacity is not most of a working economy.");
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

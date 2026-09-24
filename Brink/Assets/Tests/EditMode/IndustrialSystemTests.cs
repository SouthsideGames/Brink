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

        [TestCase(12, 0)] [TestCase(11, 1)] [TestCase(5, 2)] [TestCase(0, 0)]
        public void DamageLosesOnlyCompletedWorkAndReplacementIsStillPaid(int remaining, int lost)
        {
            var owner = state.PlayerCountry;
            owner.economy.programmes.Clear();
            IndustrialSystem.BeginBy(state, owner.id, EconomicSector.Industry, IndustrialScale.Maintenance);
            var work = owner.economy.programmes.Single(); work.monthsRemaining = remaining;
            work.locationId = ""; // old serializers may normalize a missing national location
            float cash = owner.resources.treasury;
            Assert.AreEqual(lost, IndustrialSystem.DamageWork(state, owner.id, EconomicSector.Industry));
            Assert.AreEqual(remaining + lost, work.monthsRemaining);
            Assert.AreEqual(cash, owner.resources.treasury, "No instant refund or charge.");
            if (lost > 0)
            {
                Assert.AreEqual(Publicity.Secret, state.chronicle.Last().publicity);
                StringAssert.Contains("Replacement work", state.chronicle.Last().text);
                IndustrialSystem.MonthlyUpdate(state);
                Assert.AreEqual(cash - 95f, owner.resources.treasury);
                Assert.AreEqual(remaining + lost - 1, work.monthsRemaining);
            }
        }

        [Test]
        public void SuccessfulSabotageSetsBackIndustryWorkWithoutRevealingTheForeignQueue()
        {
            var target = state.FindCountry("CHN"); target.economy.programmes.Clear();
            IndustrialSystem.BeginBy(state, target.id, EconomicSector.Industry, IndustrialScale.Maintenance);
            IndustrialSystem.BeginBy(state, target.id, EconomicSector.Energy, IndustrialScale.Maintenance);
            var industry = target.economy.programmes.Single(p => p.sector == EconomicSector.Industry);
            var energy = target.economy.programmes.Single(p => p.sector == EconomicSector.Energy);
            industry.monthsRemaining = energy.monthsRemaining = 5;
            IntelligenceSystem.EstablishNetwork(state, turns, target.id, IntelDomain.Military);
            state.FindNetwork(state.playerCountryId, target.id).penetration = 100f;
            target.counterIntel.counterIntelligence = 0f;
            Assert.IsTrue(IntelligenceSystem.RunCovertOperation(state, turns, target.id, CovertOperation.Sabotage));
            Assert.AreEqual(7, industry.monthsRemaining); Assert.AreEqual(5, energy.monthsRemaining);
            var record = state.chronicle.Single(e => e.text.StartsWith("PROJECT SET BACK:"));
            Assert.AreEqual(target.id, record.countryId); Assert.AreEqual(Publicity.Secret, record.publicity);
            Assert.IsFalse(WorldWire.CanShow(state, record));
        }

        [Test]
        public void SiteStrikeDamagesOnlyWorkAtItsTargetAndCannotCreateOrCompleteAProject()
        {
            var site = EnergySite(); var owner = state.PlayerCountry;
            owner.economy.programmes.Clear();
            IndustrialSystem.BeginSiteBy(state, owner.id, site.id);
            var work = owner.economy.programmes.Single(); work.monthsRemaining = 5;
            var other = EnergySite("OTHER");
            var apply = typeof(MilitarySystem).GetMethod("ApplyNonCapturingSuccess", BindingFlags.Static | BindingFlags.NonPublic);
            apply.Invoke(null, new object[] { state, state.FindCountry("CHN"), owner, other, OperationType.AirStrike });
            Assert.AreEqual(5, work.monthsRemaining);
            apply.Invoke(null, new object[] { state, state.FindCountry("CHN"), owner, site, OperationType.AirStrike });
            Assert.AreEqual(7, work.monthsRemaining);
            for (int i = 0; i < 20; i++) IndustrialSystem.DamageWork(state, owner.id, EconomicSector.Energy, site.id);
            Assert.AreEqual(12, work.monthsRemaining);
            Assert.IsFalse(site.energyWorks); Assert.AreEqual(1, owner.economy.programmes.Count);
            owner.resources.treasury = 0;
            IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(0, owner.economy.programmes.Count); Assert.IsFalse(site.energyWorks);
        }

        StrategicLocation EnergySite(string id = "TEST_ENERGY")
        {
            var site = new StrategicLocation { id = id, displayName = "Test Energy Region " + id,
                type = LocationType.EnergyRegion, ownerId = state.playerCountryId, originalOwnerId = state.playerCountryId };
            state.locations.Add(site);
            return site;
        }

        [TestCase(50f, 7f)]
        [TestCase(98f, 2f)]
        [TestCase(100f, 0f)]
        public void SiteCompletionBuildsGroundNotNationalBonusesAndReportsTheAppliedCeiling(float endowment, float gain)
        {
            var site = EnergySite();
            var player = state.PlayerCountry;
            state.trade.Clear(); player.technology.capabilities.Clear();
            player.resources.energyEndowment = endowment;
            float ceiling = EconomySystem.EnergyCeilingFor(state, player);
            Assert.AreEqual(endowment, ceiling, .001f, "fixture must have no other ceiling input");
            string sectors = UnityEngine.JsonUtility.ToJson(player.economy);
            float energy = player.resources.energy, pillar = player.pillars.economy;
            float money = player.resources.treasury;
            int xp = state.strategistXP, initiative = state.initiativesThisYear, cp = state.commandPoints.current;
            Assert.IsTrue(IndustrialSystem.BeginSite(state, turns, site.id));
            Assert.AreEqual(cp - 2, state.commandPoints.current);
            Assert.AreEqual(initiative + 1, state.initiativesThisYear);
            for (int i = 0; i < 11; i++) IndustrialSystem.MonthlyUpdate(state);
            Assert.IsFalse(site.energyWorks);
            Assert.AreEqual(ceiling, EconomySystem.EnergyCeilingFor(state, player));
            IndustrialSystem.MonthlyUpdate(state);
            Assert.IsTrue(site.energyWorks);
            Assert.IsEmpty(player.economy.programmes);
            Assert.AreEqual(money - 12 * 95f, player.resources.treasury);
            Assert.AreEqual(ceiling + gain, EconomySystem.EnergyCeilingFor(state, player), .001f);
            Assert.AreEqual(energy, player.resources.energy, "completion is not a refill");
            Assert.AreEqual(endowment, player.resources.energyEndowment);
            Assert.AreEqual(pillar, player.pillars.economy);
            Assert.AreEqual(sectors, UnityEngine.JsonUtility.ToJson(player.economy));
            Assert.AreEqual(xp + 44, state.strategistXP, "14 begin + 30 completion, first use");
            StringAssert.Contains($"applied energy-ceiling change {gain:+0.##;-0.##;0}", state.chronicle.Last().text);
            StringAssert.Contains(site.displayName, state.chronicle.Last().text);
            Assert.IsFalse(IndustrialSystem.BeginSiteBy(state, player.id, site.id));
            int records = state.chronicle.Count;
            IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(records, state.chronicle.Count);
        }

        [Test]
        public void ContestedSitePausesWithoutPaymentOrProgressAndResumesAfterReload()
        {
            var site = EnergySite();
            Assert.IsTrue(IndustrialSystem.BeginSiteBy(state, state.playerCountryId, site.id));
            IndustrialSystem.MonthlyUpdate(state);
            state.insurgencies.Add(new Insurgency { locationId = site.id, strength = InsurgencySystem.ContestThreshold });
            state.PlayerCountry.resources.treasury = 0f;
            var programme = state.PlayerCountry.economy.programmes.Single();
            int history = state.chronicle.Count;
            for (int i = 0; i < 6; i++) IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(11, programme.monthsRemaining);
            Assert.AreEqual(0f, state.PlayerCountry.resources.treasury);
            Assert.AreEqual(history, state.chronicle.Count, "paused work does not lapse or spam records");
            StringAssert.Contains("PAUSED", IndustrialSystem.SiteReadout(state, site));
            state = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(site.id, state.PlayerCountry.economy.programmes.Single().locationId);
            state.insurgencies.Clear();
            state.PlayerCountry.resources.treasury = 11 * 95;
            for (int i = 0; i < 11; i++) IndustrialSystem.MonthlyUpdate(state);
            Assert.IsTrue(state.FindLocation(site.id).energyWorks);
            Assert.AreEqual(0f, state.PlayerCountry.resources.treasury);
        }

        [TestCase("lost")]
        [TestCase("missing")]
        [TestCase("cancel")]
        [TestCase("unfunded")]
        public void UnfinishedSiteLosesSpentMoneyButNeverDeliversOrChargesAfterAbandonment(string cause)
        {
            var site = EnergySite();
            Assert.IsTrue(IndustrialSystem.BeginSiteBy(state, state.playerCountryId, site.id));
            IndustrialSystem.MonthlyUpdate(state);
            if (cause == "lost")
            {
                site.ownerId = "CHN";
                state.insurgencies.Add(new Insurgency { locationId = site.id, strength = 100f });
            }
            if (cause == "missing") state.locations.Remove(site);
            if (cause == "unfunded") state.PlayerCountry.resources.treasury = 94f;
            float money = state.PlayerCountry.resources.treasury;
            if (cause == "cancel") Assert.IsTrue(IndustrialSystem.Cancel(state, state.playerCountryId, EconomicSector.Energy));
            else IndustrialSystem.MonthlyUpdate(state);
            Assert.IsEmpty(state.PlayerCountry.economy.programmes);
            Assert.AreEqual(money, state.PlayerCountry.resources.treasury);
            Assert.IsFalse(site.energyWorks);
            StringAssert.StartsWith(cause == "cancel" ? "PROJECT CANCELLED:" : cause == "unfunded" ? "PROJECT LAPSED:" : "PROJECT ABANDONED:", state.chronicle.Last().text);
            int history = state.chronicle.Count;
            IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(history, state.chronicle.Count);
        }

        [Test]
        public void CompletedSiteFollowsOwnershipAndContestationWithoutChangingItsOriginalValue()
        {
            var site = EnergySite();
            Assert.IsTrue(IndustrialSystem.BeginSiteBy(state, state.playerCountryId, site.id));
            for (int i = 0; i < 12; i++) IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(7f, TerritorySystem.EnergySwing(state, state.playerCountryId), .001f);
            float foreignBefore = TerritorySystem.EnergySwing(state, "CHN");
            site.ownerId = "CHN";
            Assert.AreEqual(0f, TerritorySystem.EnergySwing(state, state.playerCountryId), .001f);
            Assert.AreEqual(foreignBefore + 7f, TerritorySystem.EnergySwing(state, "CHN"), .001f);
            state.insurgencies.Add(new Insurgency { locationId = site.id, strength = InsurgencySystem.ContestThreshold });
            Assert.AreEqual(foreignBefore, TerritorySystem.EnergySwing(state, "CHN"), .001f);
            state.insurgencies.Clear(); site.ownerId = state.playerCountryId;
            Assert.AreEqual(7f, TerritorySystem.EnergySwing(state, state.playerCountryId), .001f);
            Assert.AreEqual(0f, site.strategicValue);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(loaded.FindLocation(site.id).energyWorks);
            Assert.AreEqual(7f, TerritorySystem.EnergySwing(loaded, loaded.playerCountryId), .001f);
        }

        [Test]
        public void SiteCeilingReachesTheLiveEconomyTickAndAnUnfundedResumeLapses()
        {
            var site = EnergySite();
            state.PlayerCountry.resources.energyEndowment = 30f;
            state.PlayerCountry.resources.energy = 30f; // near the target, not pinned at the +0.35 recovery-speed cap
            state.PlayerCountry.technology.capabilities.Clear(); state.trade.Clear(); state.sanctions.Clear();
            var control = SaveSystem.FromJson(SaveSystem.ToJson(state));
            site.energyWorks = true;
            EconomySystem.MonthlyUpdate(control); EconomySystem.MonthlyUpdate(state);
            Assert.Greater(state.PlayerCountry.resources.energy, control.PlayerCountry.resources.energy,
                "a built site must reach real resources, not just its readout");
            site.energyWorks = false;
            Assert.IsTrue(IndustrialSystem.BeginSiteBy(state, state.playerCountryId, site.id));
            state.insurgencies.Add(new Insurgency { locationId = site.id, strength = 100 });
            state.PlayerCountry.resources.treasury = 0;
            IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(1, state.PlayerCountry.economy.programmes.Count);
            state.insurgencies.Clear(); IndustrialSystem.MonthlyUpdate(state);
            Assert.IsEmpty(state.PlayerCountry.economy.programmes);
            Assert.IsFalse(site.energyWorks);
            StringAssert.StartsWith("PROJECT LAPSED:", state.chronicle.Last().text);
        }

        [TestCase("foreign")]
        [TestCase("type")]
        [TestCase("complete")]
        [TestCase("contested")]
        [TestCase("busy")]
        [TestCase("full")]
        [TestCase("cp")]
        [TestCase("missing")]
        public void InvalidSiteOrderIsPureAndDoesNotSpend(string problem)
        {
            var site = EnergySite();
            if (problem == "foreign") site.ownerId = "CHN";
            if (problem == "type") site.type = LocationType.Capital;
            if (problem == "complete") site.energyWorks = true;
            if (problem == "contested") state.insurgencies.Add(new Insurgency { locationId = site.id, strength = 100 });
            if (problem == "busy") Assert.IsTrue(IndustrialSystem.BeginBy(state, state.playerCountryId, EconomicSector.Energy, IndustrialScale.Expansion));
            if (problem == "full")
                foreach (var sector in new[] { EconomicSector.Industry, EconomicSector.Finance, EconomicSector.Technology })
                    Assert.IsTrue(IndustrialSystem.BeginBy(state, state.playerCountryId, sector, IndustrialScale.Maintenance));
            if (problem == "cp") state.commandPoints.current = 1;
            if (problem == "missing") state.locations.Remove(site);
            state.authorizedPillarMask = ~0;
            string before = SaveSystem.ToJson(state);
            Assert.IsFalse(IndustrialSystem.BeginSite(state, turns, site.id));
            Assert.AreEqual(before, SaveSystem.ToJson(state));
        }

        [Test]
        public void LegacyFieldsDefaultToNationalWorkAndNoSiteOutput()
        {
            Assert.IsTrue(IndustrialSystem.BeginBy(state, state.playerCountryId, EconomicSector.Energy, IndustrialScale.Maintenance));
            string json = System.Text.RegularExpressions.Regex.Replace(SaveSystem.ToJson(state), @",?\s*""locationId""\s*:\s*(null|"""")", "");
            json = System.Text.RegularExpressions.Regex.Replace(json, @",?\s*""energyWorks""\s*:\s*false", "");
            var loaded = SaveSystem.FromJson(json);
            Assert.IsTrue(string.IsNullOrEmpty(loaded.PlayerCountry.economy.programmes.Single().locationId));
            Assert.IsFalse(loaded.locations.Any(s => s.energyWorks));
            float original = loaded.PlayerCountry.resources.energyEndowment;
            for (int i = 0; i < 12; i++) IndustrialSystem.MonthlyUpdate(loaded);
            Assert.AreEqual(Math.Min(100, original + 2.45f), loaded.PlayerCountry.resources.energyEndowment, .001f);
            Assert.AreEqual(7, loaded.saveVersion);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void SitePanelButtonsTargetTheirOwnGroundAndReadsStayPure(int columns)
        {
            var gc = GameController.Instance;
            var previous = gc.State; var previousTurns = gc.Turns;
            try
            {
                var first = EnergySite("FIRST"); var second = EnergySite("SECOND");
                state.authorizedPillarMask = ~0;
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                typeof(GameController).GetProperty("Turns").SetValue(gc, turns);
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new EconomyView(); view.Refresh();
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                var buttons = view.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("BUILD ENERGY WORKS")).ToList();
                Assert.GreaterOrEqual(buttons.Count, 2);
                foreach (var b in buttons) Assert.LessOrEqual(b.text.Length + 4, columns);
                foreach (var label in view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text")))
                    foreach (var line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                SaveSystem.Save(state); // sentinel: the persisted order must come from the click below
                int cp = state.commandPoints.current;
                var button = buttons.Last();
                var clickable = typeof(Button).GetProperty("clickable")?.GetValue(button);
                if (clickable != null) clickable.GetType().GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(clickable, new object[] { null });
                else typeof(Button).GetMethod("SendClick").Invoke(button, null);
                Assert.AreEqual(second.id, state.PlayerCountry.economy.programmes.Single().locationId);
                Assert.AreEqual(cp - 2, state.commandPoints.current);
                Assert.AreEqual(second.id, SaveSystem.Load().PlayerCountry.economy.programmes.Single().locationId,
                    "the controller saves the selected site, not merely the in-memory order");
                Assert.IsFalse(first.energyWorks);
                state.commandPoints.current = 100; // exclude a generic affordability refusal
                Assert.IsFalse(IndustrialSystem.CanBeginSite(state, state.playerCountryId, second.id, out string siteReason));
                view.Refresh();
                var refused = view.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("BUILD ENERGY WORKS")).ToList();
                foreach (var b in refused)
                {
                    Assert.IsFalse(b.enabledSelf);
                    Assert.AreEqual(siteReason, TerminalView.BlockedReason(b));
                }
                string refusalText = string.Join(" ", view.Root.Query<Label>().ToList().Select(l => l.text));
                StringAssert.Contains("UNAVAILABLE:", refusalText);
            }
            finally
            {
                typeof(GameController).GetProperty("State").SetValue(gc, previous);
                typeof(GameController).GetProperty("Turns").SetValue(gc, previousTurns);
                TerminalMetrics.ResetForTests();
            }
        }

        [Test]
        public void ForeignSiteWorkUsesTheSameFundingButPublishesNoHiddenFigures()
        {
            var site = EnergySite(); site.ownerId = site.originalOwnerId = "CHN";
            var country = state.FindCountry("CHN");
            country.resources.treasury = 1140f;
            int notices = state.notifications.Count, cp = state.commandPoints.current, xp = state.strategistXP;
            Assert.IsTrue(IndustrialSystem.BeginSiteBy(state, "CHN", site.id));
            for (int i = 0; i < 12; i++) IndustrialSystem.MonthlyUpdate(state);
            Assert.AreEqual(0f, country.resources.treasury);
            Assert.IsTrue(site.energyWorks);
            Assert.AreEqual(notices, state.notifications.Count);
            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(xp, state.strategistXP);
            Assert.AreEqual(Publicity.Public, state.chronicle.Last().publicity);
            StringAssert.DoesNotContain("applied", state.chronicle.Last().text);
            StringAssert.DoesNotContain("+7", state.chronicle.Last().text);
        }

        [Test]
        public void MapShowsOwnSiteStatusButDoesNotRevealForeignConstruction()
        {
            var site = EnergySite();
            Assert.IsTrue(IndustrialSystem.BeginSiteBy(state, state.playerCountryId, site.id));
            var gc = GameController.Instance; var previous = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                var view = new WorldMapView();
                typeof(WorldMapView).GetField("zoomed", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, true);
                view.Refresh();
                string Labels() => string.Join(" ", view.Root.Query<Label>().ToList().Select(l => l.text));
                StringAssert.Contains("ENERGY SITE:", Labels());
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(state));
                site.ownerId = "CHN"; site.energyWorks = true;
                typeof(WorldMapView).GetField("selectedCountryId", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(view, "CHN");
                view.Refresh();
                StringAssert.DoesNotContain("ENERGY SITE:", Labels());
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, previous); }
        }

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

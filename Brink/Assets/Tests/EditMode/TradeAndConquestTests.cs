using Brink.Core;
using Brink.Data;
using NUnit.Framework;
using System.Linq;
using Brink.UI;
using Brink.UI.Views;
using UnityEngine.UIElements;

namespace Brink.Tests
{
    /// <summary>
    /// Negotiated trade (GDD §20) and total conquest (GDD §16, §19, §22).
    ///
    /// Both close the same kind of hole: a country's authored vulnerability had
    /// only one honest answer. Trade could not be opened at all, so an energy
    /// dependency could be fixed by research or invasion and nothing else — and
    /// taking every inch of a rival's ground ended nothing, leaving the winner
    /// paying occupation costs on land nobody was coming back for.
    /// </summary>
    public class TradeAndConquestTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1919);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void Warm(string partnerId)
        {
            var relationship = state.FindRelationship(state.playerCountryId, partnerId);
            relationship.relations = 85f;
            relationship.trust = 80f;
        }

        GameState RoutingWorld()
        {
            var world = WorldFactory.CreateWorld(1919, "IND", WorldSize.Full);
            world.commandPoints.current = 40;
            world.PlayerCountry.resources.treasury = 1000;
            return world;
        }

        [Test]
        public void DetourTradesPassageExposureForPersistentFreightCostButCannotAvoidPorts()
        {
            var world = RoutingWorld(); var t = new TurnManager(world);
            var link = world.FindTrade("IND", "CHN"); link.volume = 50;
            int end = world.date.year * 12 + world.date.month + 6;
            world.FindLocation("IDN_CHK").mineHazardUntilMonth = end;
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50));
            Assert.IsTrue(TradeSystem.SetDetour(world, t, "CHN", true));
            Assert.AreEqual(39, world.commandPoints.current);
            Assert.AreEqual(47, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50));
            Assert.AreEqual(47, TradeSystem.EffectiveVolume(world, "CHN", "IND", 50));
            world.FindLocation("IDN_CHK2").mineHazardUntilMonth = end;
            Assert.AreEqual(41, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50));
            world.FindLocation("IND_PRT").mineHazardUntilMonth = end;
            Assert.AreEqual(41, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50));
            Assert.AreEqual(0, TradeSystem.EffectiveVolume(world, "IND", "CHN", 2));
            foreach (var site in world.locations) site.mineHazardUntilMonth = 0;
            Assert.AreEqual(47, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50), "Detour cost continues after recovery until cancelled.");
            Assert.IsTrue(TradeSystem.SetDetour(world, t, "CHN", false));
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50));
            Assert.AreEqual(50, link.volume, "The agreement was not damaged or refunded.");
        }

        [Test]
        public void DetourRequiresCompleteGeographyAndCanAlwaysBeCancelledAfterSiteLoss()
        {
            var world = RoutingWorld(); var t = new TurnManager(world);
            Assert.IsTrue(TradeSystem.SetDetour(world, t, "CHN", true));
            world.locations.RemoveAll(l => l.id == "IDN_CHK2");
            Assert.IsFalse(TradeSystem.CanDetour(world, "IND", "CHN"));
            world.FindLocation("IDN_CHK").mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "IND", "CHN", 50));
            Assert.IsTrue(TradeSystem.SetDetour(world, t, "CHN", false));
            string before = SaveSystem.ToJson(world);
            Assert.IsFalse(TradeSystem.SetDetour(world, t, "CHN", true));
            Assert.AreEqual(before, SaveSystem.ToJson(world));
        }

        [TestCase("SAU", "CHN", "SAU_CHK", 41)]
        [TestCase("SAU", "CHN", "IDN_CHK", 47)]
        [TestCase("SAU", "CHN", "IDN_CHK2", 41)]
        [TestCase("EGY", "SAU", "SAU_CHK", 41)]
        [TestCase("EGY", "SAU", "EGY_CHK", 47)]
        [TestCase("SAU", "IND", "EGY_CHK", 41)]
        [TestCase("FRA", "CHN", "SAU_CHK", 47)]
        public void DetoursPreserveTheirOwnPhysicalEntranceAndExitDependencies(string a, string b, string mined, int expected)
        {
            var world = WorldFactory.CreateWorld(1919, a, WorldSize.Full);
            world.trade.Clear(); world.trade.Add(new TradeRelation { countryA = a, countryB = b, volume = 50, avoidPassages = true });
            world.FindLocation(mined).mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            Assert.AreEqual(expected, TradeSystem.EffectiveVolume(world, a, b, 50));
            Assert.AreEqual(expected, TradeSystem.EffectiveVolume(world, b, a, 50));
            Assert.IsFalse(TradeSystem.CanDetour(world, "DEU", "POL"), "No invented ocean bypass into the Baltic.");
        }

        [Test]
        public void DetourRoundTripsAndRetainsEmbargoAndSanctionClosures()
        {
            var world = RoutingWorld(); var t = new TurnManager(world);
            var link = world.FindTrade("IND", "CHN"); link.focus = TradeFocus.Energy;
            Assert.IsTrue(TradeSystem.SetDetour(world, t, "CHN", true));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(world));
            Assert.IsTrue(loaded.FindTrade("IND", "CHN").avoidPassages);
            loaded.trade.RemoveAll(l => !l.Involves("CHN") || !l.Involves("IND"));
            loaded.sanctions.Clear();
            var saved = loaded.FindTrade("IND", "CHN"); saved.embargoed = true;
            Assert.AreEqual(0, TradeSystem.Supply(loaded, "IND", TradeFocus.Energy));
            saved.embargoed = false;
            loaded.sanctions.Add(new Sanction { senderId = "CHN", targetId = "IND" });
            Assert.AreEqual(0, TradeSystem.Supply(loaded, "IND", TradeFocus.Energy));
        }

        [Test]
        public void HolderClearanceChargesTheRequestAndFeeWithoutGrantingOperatingRights()
        {
            var world = RoutingWorld(); var t = new TurnManager(world);
            var site = world.FindLocation("IDN_CHK");
            site.mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            var relation = world.FindRelationship("IND", "IDN"); relation.relations = 100; relation.trust = 100;
            relation.SetThreatPerceivedBy("IDN", 0);
            float holderCash = world.FindCountry("IDN").resources.treasury;
            Assert.IsTrue(DiplomacySystem.RequestClearance(world, t, site.id));
            Assert.AreEqual(38, world.commandPoints.current);
            Assert.AreEqual(960, world.PlayerCountry.resources.treasury);
            Assert.AreEqual(holderCash + 40, world.FindCountry("IDN").resources.treasury);
            Assert.AreEqual(3, MilitarySystem.MineMonthsRemaining(world, site));
            Assert.AreEqual("IDN", site.ownerId);
            Assert.IsFalse(OperationCatalog.CanOrder(world, "IND", site, OperationType.ConvoyEscort, out _));
            Assert.IsNull(world.FindTreaty("IND", "IDN"));
        }

        [Test]
        public void ClearanceRefusalSpendsOnlyTheNegotiationAndInvalidRequestsSpendNothing()
        {
            var world = RoutingWorld(); var t = new TurnManager(world);
            var site = world.FindLocation("IDN_CHK");
            site.mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            var relation = world.FindRelationship("IND", "IDN"); relation.relations = 0; relation.trust = 0;
            float treasury = world.PlayerCountry.resources.treasury;
            Assert.IsFalse(DiplomacySystem.RequestClearance(world, t, site.id));
            Assert.AreEqual(38, world.commandPoints.current);
            Assert.AreEqual(treasury, world.PlayerCountry.resources.treasury);
            Assert.AreEqual(6, MilitarySystem.MineMonthsRemaining(world, site));
            world.sanctions.Add(new Sanction { senderId = "IND", targetId = "IDN" });
            string before = SaveSystem.ToJson(world);
            Assert.IsFalse(DiplomacySystem.RequestClearance(world, t, site.id));
            Assert.AreEqual(before, SaveSystem.ToJson(world));
        }

        [Test]
        public void RoutingAndClearancePreviewsArePureAndDoNotRevealForeignDeadlines()
        {
            var world = RoutingWorld(); var site = world.FindLocation("IDN_CHK");
            string before = SaveSystem.ToJson(world);
            for (int i = 0; i < 10; i++)
            {
                TradeSystem.CanDetour(world, "IND", "CHN");
                TradeSystem.RoutedVolume(world, "IND", "CHN", 50, true);
                DiplomacySystem.ClearanceOutlook(world, "IND", site.id);
            }
            Assert.AreEqual(before, SaveSystem.ToJson(world));
            var outlook = DiplomacySystem.ClearanceOutlook(world, "IND", site.id);
            site.mineHazardUntilMonth = world.date.year * 12 + world.date.month + 99;
            Assert.AreEqual(outlook, DiplomacySystem.ClearanceOutlook(world, "IND", site.id));
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void GeographyControlsAreVisibleWrappedAndReadOnly(int columns)
        {
            var world = RoutingWorld(); var gc = GameController.Instance; var old = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, world);
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var economy = new EconomyView(); var diplomacy = new DiplomacyView(); var military = new MilitaryView();
                economy.Refresh(); diplomacy.Refresh(); military.Refresh(); // Existing chamber view may initialize its own seats once.
                string before = SaveSystem.ToJson(world);
                economy.Refresh(); diplomacy.Refresh(); military.Refresh();
                Assert.AreEqual(before, SaveSystem.ToJson(world));
                var buttons = economy.Root.Query<Button>().ToList();
                Assert.IsTrue(buttons.Any(b => b.text == "TAKE DETOUR [1 CP]"));
                Assert.IsTrue(buttons.Any(b => b.text == "REPAIR 60 TREASURY [1 CP]"));
                Assert.IsTrue(diplomacy.Root.Query<Button>().ToList().Any(b => b.text == "REQUEST CLEARANCE [2 CP]"));
                Assert.IsTrue(military.Root.Query<Label>().ToList().Any(l => (l.text ?? "").Contains("HELD")));
                foreach (var view in new TerminalView[] { economy, diplomacy, military })
                    foreach (var label in view.Root.Query<Label>().ToList().Where(l => l.ClassListContains("terminal-text-dim")))
                        foreach (var line in (label.text ?? "").Split('\n')) Assert.LessOrEqual(line.Length, columns, line);
                foreach (var button in buttons.Where(b => b.text.StartsWith("TAKE DETOUR") || b.text.StartsWith("REPAIR")))
                    Assert.LessOrEqual(button.text.Length, columns);
                foreach (var site in world.locations.Where(s => s.ownerId == world.playerCountryId)) StrategicConnections.Disrupt(world, site);
                world.commandPoints.current = 0;
                economy.Refresh();
                foreach (var button in economy.Root.Query<Button>().ToList().Where(b => b.text.StartsWith("REPAIR")))
                {
                    Assert.IsFalse(button.enabledSelf, "Repair follows the shared CP gate.");
                    StringAssert.Contains("CP", button.tooltip);
                }
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, old); TerminalMetrics.ResetForTests(); }
        }

        [TestCase("routing")]
        [TestCase("repair")]
        [TestCase("clearance")]
        public void GeographyCommandsRespectBudgetAndPersistTheirActualEffect(string command)
        {
            Assert.IsNotNull(SaveSystem.SaveDirectoryOverride, "The assembly must isolate saving controller commands.");
            var world = RoutingWorld();
            world.PlayerCountry.government.type = GovernmentType.CentralizedRepublic;
            var site = world.FindLocation(command == "repair" ? "IND_AIR" : "IDN_CHK");
            StrategicConnections.Disrupt(world, world.FindLocation("IND_AIR"));
            site.mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            var relation = world.FindRelationship("IND", "IDN"); relation.relations = 100; relation.trust = 100;
            relation.SetThreatPerceivedBy("IDN", 0);
            var gc = new GameController();
            typeof(GameController).GetMethod("Attach", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(gc, new object[] { world });
            System.Func<bool> act = () => command == "routing" ? gc.SetFreightDetour("CHN", true)
                : command == "repair" ? gc.RepairConnectionEndpoint(site.id) : gc.RequestPassageClearance(site.id);
            world.commandPoints.current = 0;
            string before = SaveSystem.ToJson(world);
            Assert.IsFalse(act()); Assert.AreEqual(before, SaveSystem.ToJson(world));
            world.commandPoints.current = 10;
            if (command != "clearance")
            {
                world.PlayerCountry.government.type = GovernmentType.ParliamentaryRepublic;
                before = SaveSystem.ToJson(world);
                Assert.IsFalse(act(), "Economic commands require constitutional authority.");
                Assert.AreEqual(before, SaveSystem.ToJson(world));
                world.PlayerCountry.government.type = GovernmentType.CentralizedRepublic;
            }
            Assert.IsTrue(act());
            Assert.AreEqual(command == "clearance" ? 8 : 9, world.commandPoints.current);
            var saved = SaveSystem.Load(GameController.AutosaveSlot);
            Assert.AreEqual(SaveSystem.ToJson(world), SaveSystem.ToJson(saved));
            if (command == "routing") Assert.IsTrue(saved.FindTrade("IND", "CHN").avoidPassages);
            else if (command == "repair") Assert.AreEqual(3, StrategicConnections.OutageRemaining(saved, saved.FindLocation(site.id)));
            else Assert.AreEqual(3, MilitarySystem.MineMonthsRemaining(saved, saved.FindLocation(site.id)));
        }

        [Test]
        public void OldSavesDefaultToStandardRoutingAndAvailableConnectionsWithoutBackfill()
        {
            var world = RoutingWorld();
            world.FindTrade("IND", "CHN").avoidPassages = true;
            StrategicConnections.Disrupt(world, world.FindLocation("IND_AIR"));
            world.locations.RemoveAll(l => l.id == "IDN_CHK2");
            string json = SaveSystem.ToJson(world);
            json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""avoidPassages""\s*:\s*(true|false)", "");
            json = System.Text.RegularExpressions.Regex.Replace(json, @",\s*""infrastructureOutageUntilMonth""\s*:\s*\d+", "");
            StringAssert.DoesNotContain("avoidPassages", json);
            StringAssert.DoesNotContain("infrastructureOutageUntilMonth", json);
            var loaded = SaveSystem.FromJson(json);
            Assert.IsFalse(loaded.FindTrade("IND", "CHN").avoidPassages);
            Assert.AreEqual(0, StrategicConnections.OutageRemaining(loaded, loaded.FindLocation("IND_AIR")));
            Assert.IsNull(loaded.FindLocation("IDN_CHK2"));
            Assert.IsFalse(TradeSystem.CanDetour(loaded, "IND", "CHN"));
        }

        [TestCase(WorldSize.Regional, 23)]
        [TestCase(WorldSize.Standard, 34)]
        [TestCase(WorldSize.Full, 56)]
        public void PortDependenciesCoverOnlyPresentRosterEndpoints(WorldSize size, int count)
        {
            var world = WorldFactory.CreateWorld(1919, "USA", size);
            Assert.AreEqual(count, world.trade.Count(l => TradeSystem.PortFor(world, l.countryA) != null
                && TradeSystem.PortFor(world, l.countryB) != null));
            Assert.IsNull(TradeSystem.PortFor(world, "KAZ"));
            Assert.IsNull(TradeSystem.PortFor(world, "ABSENT"));
            Assert.IsNull(TradeSystem.PortFor(null, "USA"));
        }

        [Test]
        public void OnlyDependentPortsDisruptMappedLinksWhileFallbackRemainsNational()
        {
            int until = state.date.year * 12 + state.date.month + 6;
            var unrelated = state.FindLocation("CONTESTED_LANE");
            unrelated.mineHazardUntilMonth = until;
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(state, "USA", "CHN", 50));
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(state, "KAZ", "CHN", 50));
            var port = state.FindLocation("CHN_PRT");
            port.mineHazardUntilMonth = until;
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(state, "USA", "CHN", 50));
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(state, "BRA", "CHN", 50));
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(state, "USA", "BRA", 50));
            port.ownerId = "BRA"; port.originalOwnerId = "BRA";
            Assert.AreSame(port, TradeSystem.PortFor(state, "CHN"));
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(state, "USA", "BRA", 50));
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(state, "CHN", "USA", 50));
            state.FindLocation("USA_PRT").mineHazardUntilMonth = until;
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(state, "USA", "CHN", 50), "Two endpoints must not double-charge.");
            Assert.AreEqual(0, TradeSystem.EffectiveVolume(state, "USA", "CHN", 4));
        }

        [Test]
        public void SuccessorNeedsTitledAuthoredPortAndSelectionIgnoresOccupationAndListOrder()
        {
            state.countries.Add(new CountryState { id = "NEW", displayName = "Successor" });
            Assert.IsNull(TradeSystem.PortFor(state, "NEW"));
            var china = state.FindLocation("CHN_PRT");
            china.ownerId = "NEW";
            Assert.IsNull(TradeSystem.PortFor(state, "NEW"), "Occupation alone grants no endpoint.");
            china.originalOwnerId = "NEW";
            Assert.AreSame(china, TradeSystem.PortFor(state, "NEW"));
            china.ownerId = "USA";
            Assert.AreSame(china, TradeSystem.PortFor(state, "NEW"));
            var brazil = state.FindLocation("BRA_PRT"); brazil.originalOwnerId = "NEW";
            Assert.AreSame(brazil, TradeSystem.PortFor(state, "NEW"));
            state.locations.Reverse();
            Assert.AreSame(brazil, TradeSystem.PortFor(state, "NEW"));
            china.originalOwnerId = "CHN"; brazil.originalOwnerId = "BRA";
            state.locations.Add(new StrategicLocation { id = "CUSTOM", type = LocationType.Port,
                ownerId = "NEW", originalOwnerId = "NEW" });
            Assert.IsNull(TradeSystem.PortFor(state, "NEW"), "Custom ground has no authored physical endpoint.");
        }

        [Test]
        public void OldMissingPortUsesDisclosedFallbackAndReadNeverSeedsOrRefunds()
        {
            var world = WorldFactory.CreateWorld(1919, "USA", WorldSize.Full);
            world.locations.RemoveAll(l => l.id == "CAN_PRT");
            var port = world.FindLocation("USA_PRT");
            port.mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            var link = world.FindTrade("USA", "CAN");
            float volume = link.volume;
            string before = SaveSystem.ToJson(world);
            StringAssert.Contains("ROUTE NOT MODELLED", TradeSystem.PortDependencyReadout(world, link));
            Assert.AreEqual(volume - 6, TradeSystem.EffectiveVolume(world, "USA", "CAN", volume));
            Assert.AreEqual(before, SaveSystem.ToJson(world));
            var loaded = SaveSystem.FromJson(before);
            Assert.IsNull(TradeSystem.PortFor(loaded, "CAN"));
            Assert.AreEqual(volume - 6, TradeSystem.EffectiveVolume(loaded, "CAN", "USA", volume));
            for (int i = 0; i < 6; i++) loaded.date = loaded.date.NextMonth();
            Assert.AreEqual(volume, TradeSystem.EffectiveVolume(loaded, "USA", "CAN", volume));
            Assert.AreEqual(volume, loaded.FindTrade("USA", "CAN").volume);
        }

        [TestCase(TradeFocus.Energy)]
        [TestCase(TradeFocus.Materials)]
        [TestCase(TradeFocus.Food)]
        public void DeliveredCommodityUsesPhysicalEndpointAfterCaptureAndStillHonorsClosures(TradeFocus focus)
        {
            state.trade.Clear(); state.sanctions.Clear();
            var link = new TradeRelation { countryA = "USA", countryB = "CHN", volume = 50, focus = focus };
            state.trade.Add(link);
            var supplier = state.FindCountry("CHN");
            supplier.resources.energy = supplier.resources.strategicMaterials = supplier.resources.foodSecurity = 80;
            float clear = TradeSystem.Supply(state, "USA", focus);
            var port = state.FindLocation("CHN_PRT");
            port.mineHazardUntilMonth = state.date.year * 12 + state.date.month + 6;
            port.ownerId = "BRA";
            float disrupted = TradeSystem.Supply(state, "USA", focus);
            Assert.AreEqual(clear * 44f / 50f, disrupted, 0.0001f);
            Assert.AreEqual(50, link.volume);
            link.embargoed = true;
            Assert.AreEqual(0, TradeSystem.Supply(state, "USA", focus));
            Assert.AreEqual(disrupted, TradeSystem.SupplyIfLifted(state, "USA", focus, "USA", "CHN"), 0.0001f);
            state.sanctions.Add(new Sanction { senderId = "CHN", targetId = "USA" });
            Assert.AreEqual(0, TradeSystem.SupplyIfLifted(state, "USA", focus, "USA", "CHN"));
        }

        [Test]
        public void OwnRouteReadoutDisclosesExposureButNotForeignDeadlinesOrResources()
        {
            var link = state.FindTrade("USA", "CHN");
            var port = state.FindLocation("CHN_PRT");
            port.mineHazardUntilMonth = state.date.year * 12 + state.date.month + 6;
            string first = TradeSystem.PortDependencyReadout(state, link);
            StringAssert.Contains("Port of Shanghai", first);
            StringAssert.Contains("Mine exposure", first);
            port.mineHazardUntilMonth += 100;
            state.FindCountry("CHN").resources.energy = 1;
            Assert.AreEqual(first, TradeSystem.PortDependencyReadout(state, link));
            Assert.AreEqual("", TradeSystem.PortDependencyReadout(state, state.FindTrade("CHN", "JPN")));
        }

        [Test]
        public void SupplyOfferPricesTheCapturedEndpointItWillActuallyUse()
        {
            state.trade.Clear(); state.sanctions.Clear();
            var link = new TradeRelation { countryA = "USA", countryB = "CHN", volume = 4,
                tariff = 0, focus = TradeFocus.Energy };
            state.trade.Add(link);
            state.FindCountry("USA").resources.energy = 80;
            state.FindCountry("CHN").resources.energy = 0;
            var port = state.FindLocation("CHN_PRT");
            port.mineHazardUntilMonth = state.date.year * 12 + state.date.month + 6;
            port.ownerId = "BRA";
            Assert.AreEqual(0, EconomySystem.ImportDisplacement(state, state.PlayerCountry, EconomicSector.Energy));
            float before = TradeSystem.Supply(state, "CHN", TradeFocus.Energy);
            float priced = DiplomaticLeverage.LinkGain(state, "USA", "CHN", TradeFocus.Energy);
            link.volume = DiplomaticLeverage.OfferVolume;
            Assert.AreEqual(TradeSystem.Supply(state, "CHN", TradeFocus.Energy) - before, priced, .0001f);
            Assert.Greater(priced, 0);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void OwnPortDependencyPanelIsWrappedAndPure(int columns)
        {
            var gc = GameController.Instance; var previous = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, state);
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new EconomyView(); view.Refresh();
                string before = SaveSystem.ToJson(state);
                view.Refresh();
                var label = view.Root.Query<Label>().ToList().Single(l => (l.text ?? "").Contains("PORT DEPENDENCY:"));
                StringAssert.Contains("Norfolk", label.text);
                StringAssert.Contains("Shanghai", label.text);
                foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                Assert.AreEqual(before, SaveSystem.ToJson(state));
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, previous); TerminalMetrics.ResetForTests(); }
        }

        [TestCase("IND", "CHN", "IDN_CHK")]
        [TestCase("IND", "JPN", "IDN_CHK")]
        [TestCase("IND", "KOR", "IDN_CHK")]
        [TestCase("SAU", "CHN", "SAU_CHK,IDN_CHK")]
        [TestCase("SAU", "JPN", "SAU_CHK,IDN_CHK")]
        [TestCase("SAU", "KOR", "SAU_CHK,IDN_CHK")]
        [TestCase("SAU", "IND", "SAU_CHK")]
        [TestCase("FRA", "IND", "EGY_CHK,SAU_CHK")]
        [TestCase("FRA", "CHN", "EGY_CHK,SAU_CHK,IDN_CHK")]
        [TestCase("EGY", "SAU", "EGY_CHK")]
        [TestCase("DEU", "POL", "DEU_CHK")]
        [TestCase("FRA", "MEX", "MEX_CHK")]
        [TestCase("GBR", "MEX", "MEX_CHK")]
        [TestCase("JPN", "RUS", "JPN_CHK")]
        [TestCase("CHN", "VNM", "CONTESTED_LANE")]
        [TestCase("CHN", "IDN", "CONTESTED_LANE")]
        public void NamedPassagesAreExactPhysicalUnorderedDependencies(string a, string b, string ids)
        {
            var world = WorldFactory.CreateWorld(1919, a, WorldSize.Full);
            int until = world.date.year * 12 + world.date.month + 6;
            foreach (var site in world.locations)
            {
                site.mineHazardUntilMonth = until;
                bool dependent = site.id == a + "_PRT" || site.id == b + "_PRT"
                    || ids.Split(',').Contains(site.id);
                Assert.AreEqual(dependent ? 44 : 50, TradeSystem.EffectiveVolume(world, a, b, 50), site.id);
                Assert.AreEqual(dependent ? 44 : 50, TradeSystem.EffectiveVolume(world, b, a, 50), site.id);
                site.mineHazardUntilMonth = 0;
            }
            foreach (var site in world.locations) site.mineHazardUntilMonth = until;
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, a, b, 50), "Never stack hazards.");
            Assert.AreEqual(0, TradeSystem.EffectiveVolume(world, a, b, 4));
        }

        [Test]
        public void PassageIdentitySurvivesHostRemovalAndSuccessorEndpointChanges()
        {
            var world = WorldFactory.CreateWorld(1919, "FRA", WorldSize.Full);
            world.FindLocation("IDN_CHK").mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            world.FindLocation("IDN_CHK").ownerId = "BRA";
            world.FindLocation("IDN_CHK").originalOwnerId = "BRA";
            world.countries.RemoveAll(c => c.id == "IDN");
            world.countries.Add(new CountryState { id = "NEW" });
            world.FindLocation("CHN_PRT").ownerId = "NEW";
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "FRA", "NEW", 50), "Occupation is not title.");
            world.FindLocation("CHN_PRT").originalOwnerId = "NEW";
            world.locations.Reverse();
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "FRA", "NEW", 50));
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50), "Parent keeps physical endpoint.");
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "NEW", "CHN", 50), "Same endpoint has no table row.");
            world.FindLocation("BRA_PRT").originalOwnerId = "NEW";
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "FRA", "NEW", 50), "New ordinal endpoint changes dependencies.");
        }

        [Test]
        public void MissingPassagesDoNotErasePresentDependenciesOrInvokeNationalFallback()
        {
            var world = WorldFactory.CreateWorld(1919, "FRA", WorldSize.Full);
            int until = world.date.year * 12 + world.date.month + 6;
            world.locations.RemoveAll(l => l.id == "EGY_CHK");
            world.FindLocation("IDN_CHK").type = LocationType.Port;
            world.FindLocation("IDN_CHK").mineHazardUntilMonth = until;
            var redSea = world.FindLocation("SAU_CHK"); redSea.mineHazardUntilMonth = until;
            var link = new TradeRelation { countryA = "FRA", countryB = "CHN", volume = 50 };
            string json = SaveSystem.ToJson(world);
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50));
            StringAssert.Contains("Suez Transit (not represented in this save).", TradeSystem.PortDependencyReadout(world, link));
            StringAssert.Contains("Malacca Approaches (not represented in this save).", TradeSystem.PortDependencyReadout(world, link));
            Assert.AreEqual(json, SaveSystem.ToJson(world));
            world = SaveSystem.FromJson(json);
            world.FindLocation("SAU_CHK").mineHazardUntilMonth = 0;
            world.FindLocation("CONTESTED_LANE").ownerId = "FRA";
            world.FindLocation("CONTESTED_LANE").mineHazardUntilMonth = until;
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50), "No national union or wrong-type passage.");
            world.FindLocation("FRA_PRT").mineHazardUntilMonth = until;
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50), "Endpoint still counts.");
            world.locations.RemoveAll(l => l.id == "FRA_PRT");
            Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50), "Missing endpoint switches to national rule.");
            world.FindLocation("CONTESTED_LANE").mineHazardUntilMonth = 0;
            world.FindLocation("SAU_CHK").mineHazardUntilMonth = until;
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50), "Fallback never adds passage exposure.");
        }

        [TestCase(WorldSize.Regional, 3, 96)]
        [TestCase(WorldSize.Standard, 6, 220)]
        [TestCase(WorldSize.Full, 8, 278)]
        public void OpeningPassageCoverageIsBoundedAndIncludesSaudiIndianFood(WorldSize size, int count, int volume)
        {
            var world = WorldFactory.CreateWorld(1919, "IND", size);
            int until = world.date.year * 12 + world.date.month + 6;
            foreach (var site in world.locations.Where(l => l.type == LocationType.Chokepoint)) site.mineHazardUntilMonth = until;
            var affected = world.trade.Where(l => TradeSystem.PortFor(world, l.countryA) != null
                && TradeSystem.PortFor(world, l.countryB) != null
                && TradeSystem.EffectiveVolume(world, l.countryA, l.countryB, l.volume) < l.volume).ToList();
            Assert.AreEqual(count, affected.Count); Assert.AreEqual(volume, affected.Sum(l => l.volume));
            var food = world.FindTrade("SAU", "IND");
            Assert.AreEqual(TradeFocus.Food, food.focus); Assert.AreEqual(34, food.volume);
            Assert.Contains(food, affected); Assert.IsFalse(affected.Any(l => l.Involves("USA")));
        }

        [TestCase(TradeFocus.Energy)]
        [TestCase(TradeFocus.Materials)]
        [TestCase(TradeFocus.Food)]
        public void ThirdPartyPassagePricingMatchesDeliveredSupplyAndClosures(TradeFocus focus)
        {
            var world = WorldFactory.CreateWorld(1919, "SAU", WorldSize.Full);
            world.trade.Clear(); world.sanctions.Clear();
            var link = new TradeRelation { countryA = "SAU", countryB = "IND", volume = 50, focus = focus };
            world.trade.Add(link);
            var supplier = world.FindCountry("SAU"); var recipient = world.FindCountry("IND");
            supplier.resources.energy = supplier.resources.strategicMaterials = supplier.resources.foodSecurity = 80;
            recipient.resources.energy = recipient.resources.strategicMaterials = recipient.resources.foodSecurity = 0;
            world.FindLocation("SAU_CHK").ownerId = "BRA";
            world.FindLocation("SAU_CHK").mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            float supplied = 80 * TradeSystem.MaxSupplyShare * .44f;
            Assert.AreEqual(supplied, TradeSystem.Supply(world, "IND", focus), .0001f);
            Assert.AreEqual(0, TradeSystem.Supply(world, "IND", TradeFocus.General));
            link.volume = 4;
            float priced = DiplomaticLeverage.LinkGain(world, "SAU", "IND", focus);
            link.volume = DiplomaticLeverage.OfferVolume;
            Assert.AreEqual(TradeSystem.Supply(world, "IND", focus), priced, .0001f);
            world.sanctions.Add(new Sanction { senderId = "SAU", targetId = "IND" }); link.embargoed = true;
            Assert.AreEqual(0, TradeSystem.Supply(world, "IND", focus));
            float relief = DiplomaticLeverage.SupplyReliefGain(world, "SAU", "IND");
            Assert.Greater(relief, 0);
            Assert.AreEqual(TradeSystem.SupplyIfLifted(world, "IND", focus, "SAU", "IND"), relief, .0001f);
            world.sanctions.Add(new Sanction { senderId = "IND", targetId = "SAU" });
            Assert.AreEqual(0, DiplomaticLeverage.SupplyReliefGain(world, "SAU", "IND"));
        }

        [Test]
        public void PassageRecoveryRequiresLastHazardToExpireWithoutChangingAgreement()
        {
            var world = WorldFactory.CreateWorld(1919, "FRA", WorldSize.Full);
            var link = new TradeRelation { countryA = "FRA", countryB = "CHN", volume = 50 };
            world.trade.Add(link);
            int now = world.date.year * 12 + world.date.month;
            world.FindLocation("EGY_CHK").mineHazardUntilMonth = now + 3;
            world.FindLocation("SAU_CHK").mineHazardUntilMonth = now + 6;
            world.FindLocation("IDN_CHK").mineHazardUntilMonth = now + 6;
            for (int i = 0; i < 6; i++)
            {
                Assert.AreEqual(44, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50));
                if (i == 3) world.FindLocation("SAU_CHK").mineHazardUntilMonth = 0;
                world.date = world.date.NextMonth();
            }
            Assert.AreEqual(50, TradeSystem.EffectiveVolume(world, "FRA", "CHN", 50));
            Assert.AreEqual(50, link.volume);
        }

        [TestCase(34)]
        [TestCase(49)]
        [TestCase(64)]
        [TestCase(104)]
        public void PassagePanelExplainsAbsenceAndAuthorityWithoutForeignTimers(int columns)
        {
            var world = WorldFactory.CreateWorld(1919, "FRA", WorldSize.Full);
            world.trade.Clear();
            var link = new TradeRelation { countryA = "FRA", countryB = "CHN", volume = 50 };
            world.trade.Add(link); world.locations.RemoveAll(l => l.id == "EGY_CHK");
            world.FindLocation("SAU_CHK").mineHazardUntilMonth = world.date.year * 12 + world.date.month + 6;
            string first = TradeSystem.PortDependencyReadout(world, link);
            world.FindLocation("SAU_CHK").mineHazardUntilMonth += 100;
            Assert.AreEqual(first, TradeSystem.PortDependencyReadout(world, link));
            var gc = GameController.Instance; var previous = gc.State;
            try
            {
                typeof(GameController).GetProperty("State").SetValue(gc, world);
                TerminalMetrics.Update((columns + 1) * 8f, 8f, 500f, Breakpoints.FromColumns(columns));
                var view = new EconomyView(); view.Refresh();
                string json = SaveSystem.ToJson(world); view.Refresh();
                var label = view.Root.Query<Label>().ToList().Single(l => (l.text ?? "").Contains("PORT DEPENDENCY:"));
                foreach (string line in label.text.Split('\n')) Assert.LessOrEqual(line.Length, columns);
                string joined = System.Text.RegularExpressions.Regex.Replace(label.text, @"\s+", " ");
                StringAssert.Contains("Suez Transit (not represented in this save).", joined);
                StringAssert.Contains("Southern Red Sea Narrows", joined); StringAssert.Contains("Malacca Approaches", joined);
                StringAssert.Contains("Only the current holder can order clearance there", joined);
                StringAssert.Contains("Mine exposure", joined);
                Assert.AreEqual(json, SaveSystem.ToJson(world));
            }
            finally { typeof(GameController).GetProperty("State").SetValue(gc, previous); TerminalMetrics.ResetForTests(); }
        }

        // ---------- trade can actually be opened ----------

        [Test]
        public void ATradeAgreementCanBeConcluded()
        {
            Warm("DEU");
            var deal = TradeSystem.BestAcceptableDeal(state, state.playerCountryId, "DEU", TradeFocus.General);
            Assert.IsNotNull(deal, "A friendly partner should come to the table.");

            Assert.IsTrue(TradeSystem.ProposeAgreement(state, turns, deal));
            Assert.IsNotNull(state.FindTrade(state.playerCountryId, "DEU"),
                "Signing an agreement has to create the link.");
        }

        [Test]
        public void AHostileStateWillNotSign()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "RUS");
            relationship.relations = 2f;
            relationship.trust = 0f;
            relationship.SetThreatPerceivedBy("RUS", 100f);

            var deal = new TradeDeal { partnerId = "RUS", volume = 80f, tariff = 0f };
            Assert.IsFalse(TradeSystem.WouldAccept(state, state.playerCountryId, deal),
                "Trade is a relationship. It cannot be demanded from someone who distrusts us.");
        }

        [Test]
        public void SanctionsCloseTheTable()
        {
            Warm("DEU");
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "DEU", SanctionSeverity.Severe);

            Assert.IsNull(TradeSystem.BestAcceptableDeal(state, state.playerCountryId, "DEU", TradeFocus.General),
                "A sanctions regime is the opposite of a commercial arrangement.");
        }

        [Test]
        public void GivingGroundMakesADealPossible()
        {
            // Cool but not hostile: a greedy ask fails, a generous one lands.
            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            relationship.relations = 52f;
            relationship.trust = 45f;

            var greedy = new TradeDeal { partnerId = "IND", volume = 90f, tariff = 0f };
            var generous = new TradeDeal
            {
                partnerId = "IND", volume = 20f, tariff = 40f, preferentialTerms = true
            };

            Assert.IsFalse(TradeSystem.WouldAccept(state, state.playerCountryId, greedy));
            Assert.IsTrue(TradeSystem.WouldAccept(state, state.playerCountryId, generous),
                "Concessions are what make an arrangement signable — the same rule as peace terms.");
        }

        // ---------- trade is a real answer to a dependency ----------

        [Test]
        public void AnEnergyAgreementRaisesOurCeiling()
        {
            // The whole point. The Cabinet advises buying your way out of a
            // shortfall, and until now that advice pointed at nothing.
            var player = state.PlayerCountry;
            player.resources.energyEndowment = 25f;
            float before = EconomySystem.EnergyCeilingFor(state, player);

            Warm("RUS"); // authored energy power
            var deal = TradeSystem.BestAcceptableDeal(state, state.playerCountryId, "RUS", TradeFocus.Energy);
            Assume.That(deal, Is.Not.Null);
            TradeSystem.ProposeAgreementBy(state, state.playerCountryId, deal);

            Assert.Greater(EconomySystem.EnergyCeilingFor(state, player), before,
                "An energy agreement with an energy power has to actually supply energy.");
        }

        [Test]
        public void APartnerCanOnlySellWhatTheyHave()
        {
            var player = state.PlayerCountry;
            player.resources.energyEndowment = 25f;

            Warm("DEU");
            var poorPartner = state.FindCountry("DEU");
            poorPartner.resources.energy = 5f; // Germany is authored energy-poor

            var deal = TradeSystem.BestAcceptableDeal(state, state.playerCountryId, "DEU", TradeFocus.Energy);
            Assume.That(deal, Is.Not.Null);
            TradeSystem.ProposeAgreementBy(state, state.playerCountryId, deal);

            Assert.Less(TradeSystem.Supply(state, state.playerCountryId, TradeFocus.Energy), 5f,
                "Buying energy from a state that has none must not conjure any.");
        }

        [Test]
        public void ASanctionedSupplyStops()
        {
            Warm("RUS");
            var deal = TradeSystem.BestAcceptableDeal(state, state.playerCountryId, "RUS", TradeFocus.Energy);
            Assume.That(deal, Is.Not.Null);
            TradeSystem.ProposeAgreementBy(state, state.playerCountryId, deal);
            Assume.That(TradeSystem.Supply(state, state.playerCountryId, TradeFocus.Energy), Is.GreaterThan(0f));

            EconomySystem.ImposeSanctionsBy(state, "RUS", state.playerCountryId, SanctionSeverity.Severe);

            Assert.AreEqual(0f, TradeSystem.Supply(state, state.playerCountryId, TradeFocus.Energy), 0.01f,
                "Supply is exactly as reliable as the relationship behind it. That is the point.");
        }

        [Test]
        public void ATradeAgreementCreatesDependence()
        {
            Warm("RUS");
            var relationship = state.FindRelationship(state.playerCountryId, "RUS");
            float before = relationship.DependenceOf(state.playerCountryId);

            var deal = TradeSystem.BestAcceptableDeal(state, state.playerCountryId, "RUS", TradeFocus.Energy);
            Assume.That(deal, Is.Not.Null);
            TradeSystem.ProposeAgreementBy(state, state.playerCountryId, deal);

            Assert.Greater(relationship.DependenceOf(state.playerCountryId), before,
                "Buying your way out of a shortfall leaves you relying on somebody. " +
                "That is the cost the other two answers do not carry.");
        }

        // ---------- total conquest ----------

        void TakeEverythingFrom(string targetId, string conquerorId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == targetId) location.ownerId = conquerorId;
        }

        [Test]
        public void HoldingEveryLocationEndsTheWar()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            TakeEverythingFrom("MEX", state.playerCountryId);
            ConquestSystem.CheckForTotalConquest(state, confrontation);

            Assert.IsTrue(confrontation.resolved,
                "A state with no territory left has nothing to negotiate with.");
        }

        [Test]
        public void ConqueredGroundIsAnnexedNotOccupied()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            TakeEverythingFrom("MEX", state.playerCountryId);
            ConquestSystem.CheckForTotalConquest(state, confrontation);

            foreach (var location in state.locations)
            {
                if (location.ownerId != state.playerCountryId) continue;
                Assert.IsFalse(location.IsOccupied,
                    "Annexed ground must stop costing garrison upkeep and generating grievance — " +
                    "nobody is coming back for it.");
            }

            Assert.AreEqual(0f, TerritorySystem.OccupiedValue(state, state.playerCountryId), 0.01f);
        }

        [Test]
        public void ConquestInheritsLandAndResources()
        {
            var player = state.PlayerCountry;
            var target = state.FindCountry("MEX");
            target.resources.energyEndowment = 80f;
            target.resources.treasury = 1000f;

            float energyBefore = player.resources.energyEndowment;
            float treasuryBefore = player.resources.treasury;

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            TakeEverythingFrom("MEX", state.playerCountryId);
            ConquestSystem.CheckForTotalConquest(state, confrontation);

            Assert.Greater(player.resources.energyEndowment, energyBefore,
                "Land-bound resources come with the land, or the gain erodes back to nothing.");
            Assert.Greater(player.resources.treasury, treasuryBefore);
        }

        [Test]
        public void TheConqueredStateSurvivesTheSave()
        {
            // GDD §22: catastrophe produces a new gameplay state, not a game
            // over — and that holds for the loser. Removing a country would also
            // orphan every relationship, trade link and AI state pointing at it.
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            TakeEverythingFrom("MEX", state.playerCountryId);
            ConquestSystem.CheckForTotalConquest(state, confrontation);

            Assert.IsNotNull(state.FindCountry("MEX"), "The conquered state must still exist.");
            Assert.DoesNotThrow(() => SaveMigration.Validate(state),
                "Conquest must not produce a world that cannot be loaded.");
            Assert.DoesNotThrow(() => SaveSystem.FromJson(SaveSystem.ToJson(state)));
        }

        [Test]
        public void TheWorldIsAlarmedByAnAnnexation()
        {
            // The guard against conquest being simply the best move available.
            var player = state.PlayerCountry;
            float diplomacyBefore = player.pillars.diplomacy;

            float threatBefore = 0f;
            foreach (var relationship in state.relationships)
                if (relationship.Involves(player.id) && !relationship.Involves("MEX"))
                    threatBefore += relationship.ThreatPerceivedBy(relationship.PartnerOf(player.id));

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            TakeEverythingFrom("MEX", state.playerCountryId);
            ConquestSystem.CheckForTotalConquest(state, confrontation);

            float threatAfter = 0f;
            foreach (var relationship in state.relationships)
                if (relationship.Involves(player.id) && !relationship.Involves("MEX"))
                    threatAfter += relationship.ThreatPerceivedBy(relationship.PartnerOf(player.id));

            Assert.Less(player.pillars.diplomacy, diplomacyBefore,
                "Annexation is the loudest signal of intent a government can send.");
            Assert.Greater(threatAfter, threatBefore,
                "Everyone watching must revise what they think we are prepared to do.");
        }

        [Test]
        public void PartialOccupationIsNotConquest()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);

            // All but one.
            bool skippedOne = false;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != "MEX") continue;
                if (!skippedOne) { skippedOne = true; continue; }
                location.ownerId = state.playerCountryId;
            }

            ConquestSystem.CheckForTotalConquest(state, confrontation);

            Assert.IsFalse(confrontation.resolved,
                "One location still held is a war still being fought.");
        }

        [Test]
        public void AnAlreadyAnnexedStateIsNotConqueredTwice()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "MEX",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            TakeEverythingFrom("MEX", state.playerCountryId);
            ConquestSystem.CheckForTotalConquest(state, confrontation);

            float treasury = state.PlayerCountry.resources.treasury;

            Assert.IsFalse(ConquestSystem.HoldsEverything(state, state.playerCountryId, "MEX"),
                "A state with no original territory left cannot be conquered again.");
            Assert.AreEqual(treasury, state.PlayerCountry.resources.treasury, 0.01f,
                "Spoils must not be collectable twice.");
        }
    }
}

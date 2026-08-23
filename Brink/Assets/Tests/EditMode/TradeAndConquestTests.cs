using Brink.Core;
using Brink.Data;
using NUnit.Framework;

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

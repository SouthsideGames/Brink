using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Negotiated settlement (GDD §26): a peace is assembled from terms, priced
    /// term by term, and refused when the asking price exceeds what the other
    /// side will pay to stop fighting.
    /// </summary>
    public class PeaceSystemTests
    {
        GameState state;
        TurnManager turns;
        Confrontation confrontation;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1300);
            turns = new TurnManager(state);
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            state.commandPoints.current = 60;

            confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>Put the opponent under enough pressure that they want it to stop.</summary>
        void ExhaustTheOpponent()
        {
            var opponent = state.FindCountry("CHN");
            opponent.warSupport = 5f;
            opponent.pillars.government = 10f;
            confrontation.defenderWarExhaustion = 85f;
            confrontation.momentum = 55f;
        }

        [Test]
        public void Demands_CostThemAndConcessionsPayThem()
        {
            float cession = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.TerritorialCession);
            float reparations = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.Reparations);
            float prisoners = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.PrisonerExchange);
            float guarantee = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.SecurityGuarantee);

            Assert.Greater(cession, 0f, "Giving up ground costs them.");
            Assert.Greater(reparations, 0f, "So does paying us.");
            Assert.Less(prisoners, 0f, "A prisoner exchange is worth something to them.");
            Assert.Less(guarantee, 0f, "So is a guarantee of their security.");
        }

        [Test]
        public void GroundWeAlreadyHold_IsFarEasierToSignAway()
        {
            float whileTheirs = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.TerritorialCession);

            state.FindLocation("CONTESTED_LANE").ownerId = state.playerCountryId;
            float whileOurs = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.TerritorialCession);

            Assert.Less(whileOurs, whileTheirs,
                "Possession is leverage — they are conceding a fact rather than a place.");
        }

        [Test]
        public void ACapitalIsNeverSignedAway()
        {
            confrontation.objectiveLocationId = "CHN_CAP";
            float cost = PeaceSystem.TermCost(state, confrontation, state.playerCountryId,
                PeaceTerm.TerritorialCession);

            Assert.Greater(cost, 150f, "Ceding the seat of state ends state continuity (GDD §22).");

            ExhaustTheOpponent();
            Assert.IsFalse(PeaceSystem.WouldAccept(state, confrontation, state.playerCountryId,
                PeaceProposal.Of(PeaceTerm.TerritorialCession)),
                "Even a collapsing state will not sign its capital away.");
        }

        [Test]
        public void Overreaching_ProlongsAWarThatCouldHaveEnded()
        {
            ExhaustTheOpponent();

            var modest = PeaceProposal.Of(PeaceTerm.TerritorialCession, PeaceTerm.PrisonerExchange);
            Assert.IsTrue(PeaceSystem.WouldAccept(state, confrontation, state.playerCountryId, modest),
                "Precondition: a reasonable settlement is available.");

            var greedy = PeaceProposal.Of(
                PeaceTerm.TerritorialCession, PeaceTerm.Reparations, PeaceTerm.Demilitarization,
                PeaceTerm.ResourceAccess, PeaceTerm.Recognition, PeaceTerm.TreatyRevision);

            Assert.IsFalse(PeaceSystem.WouldAccept(state, confrontation, state.playerCountryId, greedy),
                "Asking for everything gets nothing (GDD §26).");
            Assert.IsFalse(PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId, greedy));
            Assert.IsFalse(confrontation.resolved, "A refusal leaves the war running.");
        }

        [Test]
        public void Concessions_MakeAHardTermSignable()
        {
            var opponent = state.FindCountry("CHN");
            opponent.warSupport = 30f;
            opponent.pillars.government = 30f;
            confrontation.defenderWarExhaustion = 55f;
            confrontation.momentum = 25f;

            // We hold ground of theirs and are sanctioning them — both are
            // currency at the table.
            state.FindLocation("CHN_PRT").ownerId = state.playerCountryId;
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", SanctionSeverity.Severe);

            var bare = PeaceProposal.Of(PeaceTerm.TerritorialCession, PeaceTerm.Reparations);
            var sweetened = PeaceProposal.Of(PeaceTerm.TerritorialCession, PeaceTerm.Reparations,
                PeaceTerm.Withdrawal, PeaceTerm.SanctionsRelief, PeaceTerm.PrisonerExchange);

            float bareCost = PeaceSystem.ProposalCost(state, confrontation, state.playerCountryId, bare);
            float sweetenedCost = PeaceSystem.ProposalCost(state, confrontation, state.playerCountryId, sweetened);

            Assert.Less(sweetenedCost, bareCost,
                "Offering something they want lowers the price of what we want.");
        }

        [Test]
        public void AcceptedTerms_AreActuallyApplied()
        {
            ExhaustTheOpponent();
            var opponent = state.FindCountry("CHN");
            var player = state.PlayerCountry;

            opponent.resources.treasury = 1000f;
            float playerTreasuryBefore = player.resources.treasury;
            float opponentReadinessBefore = opponent.military.ground.readiness;

            var proposal = PeaceProposal.Of(
                PeaceTerm.TerritorialCession, PeaceTerm.Reparations,
                PeaceTerm.Demilitarization, PeaceTerm.PrisonerExchange);

            // Make it affordable to them despite the demands.
            confrontation.defenderWarExhaustion = 100f;
            confrontation.momentum = 100f;

            Assert.IsTrue(PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId, proposal));

            Assert.IsTrue(confrontation.resolved);
            Assert.AreEqual(state.playerCountryId, state.FindLocation("CONTESTED_LANE").ownerId);
            Assert.Greater(player.resources.treasury, playerTreasuryBefore, "Reparations were paid.");
            Assert.Less(opponent.military.ground.readiness, opponentReadinessBefore, "They stood down.");
            Assert.AreEqual(1, state.settlements.Count);
            CollectionAssert.Contains(state.settlements[0].terms, PeaceTerm.Reparations);
        }

        [Test]
        public void Withdrawal_ReturnsOccupiedGround()
        {
            ExhaustTheOpponent();
            var port = state.FindLocation("CHN_PRT");
            port.ownerId = state.playerCountryId;
            Assert.IsTrue(port.IsOccupied, "Precondition: we hold their ground.");

            Assert.IsTrue(PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId,
                PeaceProposal.Of(PeaceTerm.Recognition, PeaceTerm.Withdrawal)));

            Assert.AreEqual("CHN", port.ownerId, "What we agreed to return, we returned.");
            Assert.IsFalse(port.IsOccupied);
        }

        [Test]
        public void SanctionsRelief_ActuallyLiftsThem()
        {
            ExhaustTheOpponent();
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", SanctionSeverity.Severe);
            Assert.NotNull(state.FindSanction(state.playerCountryId, "CHN"));

            Assert.IsTrue(PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId,
                PeaceProposal.Of(PeaceTerm.Recognition, PeaceTerm.SanctionsRelief)));

            Assert.IsNull(state.FindSanction(state.playerCountryId, "CHN"));
            Assert.IsFalse(state.FindTrade(state.playerCountryId, "CHN").embargoed);
        }

        [Test]
        public void ASettlementImprovesTheRelationshipWhateverWasInIt()
        {
            ExhaustTheOpponent();
            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            float relationsBefore = relationship.relations;
            int memoryBefore = relationship.memory.Count;

            PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId,
                PeaceProposal.Of(PeaceTerm.Recognition, PeaceTerm.PrisonerExchange));

            Assert.Greater(relationship.relations, relationsBefore, "The fighting has stopped.");
            Assert.Greater(relationship.memory.Count, memoryBefore, "And it is remembered.");
        }

        [Test]
        public void EmptyProposal_IsNotASettlement()
        {
            ExhaustTheOpponent();
            Assert.IsFalse(PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId,
                new PeaceProposal()));
            Assert.IsFalse(confrontation.resolved);
        }

        [Test]
        public void SuggestedProposal_IsSomethingAPlayerCanActuallyOpenWith()
        {
            var proposal = PeaceSystem.SuggestProposal(state, confrontation, state.playerCountryId);

            Assert.Greater(proposal.terms.Count, 0);
            Assert.IsTrue(proposal.Has(PeaceTerm.TerritorialCession),
                "A territorial confrontation should open by asking for the territory.");
            Assert.IsTrue(proposal.Has(PeaceTerm.PrisonerExchange),
                "And should include the cheap thing that costs us nothing.");
        }

        [Test]
        public void Settlements_SurviveSaveRoundTrip()
        {
            ExhaustTheOpponent();
            PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId,
                PeaceProposal.Of(PeaceTerm.TerritorialCession, PeaceTerm.PrisonerExchange));

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(1, loaded.settlements.Count);
            CollectionAssert.Contains(loaded.settlements[0].terms, PeaceTerm.TerritorialCession);
            Assert.IsNotEmpty(loaded.settlements[0].summary);
            Assert.IsNull(loaded.ActiveConfrontation, "The war stayed over.");
        }

        [Test]
        public void LegacySettlementPath_StillWorks()
        {
            // The objective-only path is still used by the AI and by concession.
            ExhaustTheOpponent();
            Assert.IsTrue(ConfrontationSystem.ProposeSettlement(state, confrontation));
            Assert.IsTrue(confrontation.resolved);
        }
    }
}

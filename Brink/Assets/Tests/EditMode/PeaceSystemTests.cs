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
            // NARROW PIPELINE: the confrontation tick alone, and in fact no
            // test in this file ever advances a month — each case sets war
            // support, exhaustion, momentum and ownership directly and then
            // asserts PeaceSystem's term pricing, acceptance test and
            // application, so the omitted economy, AI, government and regime
            // ticks have no opportunity to contribute anything.
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
        public void ADefenderDoesNotDemandTheObjectiveItAlreadyOwns()
        {
            var proposal = PeaceSystem.SuggestProposal(state, confrontation, "CHN");

            Assert.IsFalse(proposal.Has(PeaceTerm.TerritorialCession),
                "The defender offered to have its own ground ceded back to itself.");
            Assert.IsTrue(proposal.Has(PeaceTerm.Recognition),
                "A defender's opening package should ask the claimant to recognize the status quo.");
        }

        [Test]
        public void ForeignGovernmentsSettleOnConstructedTermsToo()
        {
            var foreignWar = ConfrontationSystem.BeginBy(state, "RUS", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            Assert.IsNotNull(foreignWar);
            foreignWar.defenderWarExhaustion = 100f;
            foreignWar.momentum = 100f;
            state.FindCountry("IND").warSupport = 0f;
            state.FindCountry("IND").pillars.government = 0f;

            Assert.IsTrue(PeaceSystem.ProposeConstructedSettlementBy(state, foreignWar, "RUS"));
            Assert.IsTrue(foreignWar.resolved);

            var record = state.settlements[state.settlements.Count - 1];
            Assert.AreEqual("RUS", record.proposerId);
            CollectionAssert.Contains(record.terms, PeaceTerm.Recognition);
            CollectionAssert.Contains(record.terms, PeaceTerm.PrisonerExchange);
            Assert.Greater(record.terms.Count, 1,
                "The AI still closed its war through a single objective-only settlement.");
        }

        [Test]
        public void ForeignGovernmentsDoNotSignAConcessionsOnlyPackage()
        {
            confrontation.objectiveLocationId = "CHN_CAP";
            confrontation.defenderWarExhaustion = 100f;
            confrontation.momentum = 100f;
            state.FindCountry("CHN").warSupport = 0f;
            state.FindCountry("CHN").pillars.government = 0f;

            var proposal = PeaceSystem.BestAcceptableProposal(
                state, confrontation, state.playerCountryId);

            Assert.IsNull(proposal,
                "The substantive demand was impossible, but a prisoner exchange alone was "
                + "laundered into a settlement on our terms.");
        }

        [Test]
        public void AForeignConstructedOfferReachesThePlayerIntact()
        {
            var foreignWar = ConfrontationSystem.BeginBy(state, "RUS", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            Assert.IsNotNull(foreignWar);

            Assert.IsTrue(PeaceSystem.ProposeConstructedSettlementBy(state, foreignWar, "RUS"));
            Assert.IsFalse(foreignWar.resolved, "A foreign offer decided for the player.");

            var offer = state.activeCrises[state.activeCrises.Count - 1];
            CollectionAssert.AreEqual(
                new[] { PeaceTerm.Recognition, PeaceTerm.PrisonerExchange },
                offer.offeredPeaceTerms);
            StringAssert.Contains("WE RECOGNIZE THEIR POSITION", offer.body);
            StringAssert.Contains("PRISONER EXCHANGE", offer.body);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var loadedOffer = loaded.activeCrises[loaded.activeCrises.Count - 1];
            CollectionAssert.AreEqual(offer.offeredPeaceTerms, loadedOffer.offeredPeaceTerms,
                "Saving with terms on the table erased or changed the package.");

            CrisisSystem.Resolve(state, offer, 0);
            Assert.IsTrue(foreignWar.resolved);
            var record = state.settlements[state.settlements.Count - 1];
            CollectionAssert.AreEqual(offer.offeredPeaceTerms, record.terms,
                "The signed settlement differs from the offer the player accepted.");
        }

        [Test]
        public void RefusingAConstructedOfferAppliesNothing()
        {
            var foreignWar = ConfrontationSystem.BeginBy(state, "RUS", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            Assert.IsTrue(PeaceSystem.ProposeConstructedSettlementBy(state, foreignWar, "RUS"));
            var offer = state.activeCrises[state.activeCrises.Count - 1];

            CrisisSystem.Resolve(state, offer, 1);

            Assert.IsFalse(foreignWar.resolved);
            Assert.AreEqual(0, state.settlements.Count);
            Assert.AreEqual(ConfrontationSystem.OfferCooldownMonths,
                foreignWar.monthsUntilNextOffer);
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

        /// <summary>
        /// Every term has to be classified as a demand or a concession.
        ///
        /// `IsDemand` decides which way a term points, and the war verdict reads
        /// it to work out whether a settlement fell harder on us or on them. An
        /// unclassified term would default to "concession", so *demanding*
        /// something would be scored as having cost us — and nothing would fail.
        ///
        /// The switch is exhaustive by intent; this is what makes it exhaustive
        /// by construction. Same guard `CrisisEffectTests` puts on the effect ids,
        /// for the same reason: a silent default is a bug that ships.
        /// </summary>
        [Test]
        public void EveryPeaceTermIsClassified()
        {
            GameLog.Clear();

            foreach (PeaceTerm term in System.Enum.GetValues(typeof(PeaceTerm)))
                PeaceSystem.IsDemand(term);

            foreach (var entry in GameLog.Entries)
                Assert.AreNotEqual(LogLevel.Error, entry.level,
                    $"A peace term is not classified in PeaceSystem.IsDemand: {entry.message}");
        }

        /// <summary>
        /// **The bug this test exists for shipped and was found by a player.**
        /// `Humanize` lived privately in MilitaryView and ended in a bare
        /// `default: return "WE GUARANTEE THEM"`, so `PoliticalConcessions` —
        /// appended after that switch was written — rendered as a second button
        /// with an identical label. Two different demands, one name, no failure.
        ///
        /// A distinctness check rather than a spelling check: what matters is
        /// that no two terms collide, not what any one of them says.
        /// </summary>
        [Test]
        public void EveryPeaceTermHasItsOwnLabel()
        {
            GameLog.Clear();
            var seen = new System.Collections.Generic.Dictionary<string, PeaceTerm>();

            foreach (PeaceTerm term in System.Enum.GetValues(typeof(PeaceTerm)))
            {
                string label = PeaceSystem.Describe(term);

                Assert.IsFalse(string.IsNullOrWhiteSpace(label), $"{term} has a blank label.");
                Assert.IsFalse(seen.ContainsKey(label),
                    $"{term} and {(seen.ContainsKey(label) ? seen[label].ToString() : "")} both "
                    + $"render as \"{label}\". The settlement screen would show two identical "
                    + "buttons doing different things.");
                seen[label] = term;
            }

            foreach (var entry in GameLog.Entries)
                Assert.AreNotEqual(LogLevel.Error, entry.level,
                    $"A peace term fell through PeaceSystem.Describe: {entry.message}");
        }

        [Test]
        public void EveryIncomingPeaceTermHasItsOwnLabel()
        {
            GameLog.Clear();
            var seen = new System.Collections.Generic.HashSet<string>();

            foreach (PeaceTerm term in System.Enum.GetValues(typeof(PeaceTerm)))
            {
                string label = PeaceSystem.DescribeReceived(term);
                Assert.IsFalse(string.IsNullOrWhiteSpace(label));
                Assert.IsTrue(seen.Add(label), $"Incoming term label is duplicated: {label}");
            }

            foreach (var entry in GameLog.Entries)
                Assert.AreNotEqual(LogLevel.Error, entry.level,
                    $"A peace term fell through PeaceSystem.DescribeReceived: {entry.message}");
        }

        /// <summary>
        /// Asked for from play: a war fought while under embargo could be won and
        /// leave the embargo standing, because the only sanctions term in the game
        /// lifted *ours*.
        /// </summary>
        [Test]
        public void TheirSanctionsCanBeNegotiatedAway()
        {
            EconomySystem.ImposeSanctionsBy(state, "CHN", state.playerCountryId, SanctionSeverity.Coercive);
            Assert.IsNotNull(state.FindSanction("CHN", state.playerCountryId),
                "Fixture failed to place their sanction on us.");

            Assert.IsTrue(PeaceSystem.IsDemand(PeaceTerm.SanctionsLifted),
                "Asking them to stand down their embargo is something we extract, not something we give.");

            float cost = PeaceSystem.TermCost(state, confrontation,
                state.playerCountryId, PeaceTerm.SanctionsLifted);
            Assert.Greater(cost, 0f,
                "A coercive embargo is an instrument they are counting on; giving it up must cost them something.");

            // Make them desperate enough to sign, then check it actually happened.
            ExhaustTheOpponent();
            confrontation.defenderWarExhaustion = 95f;
            confrontation.momentum = 80f;   // positive favours the initiator, which is us

            var proposal = PeaceProposal.Of(PeaceTerm.SanctionsLifted);
            Assert.IsTrue(PeaceSystem.WouldAccept(state, confrontation, state.playerCountryId, proposal),
                "A collapsing opponent refused to lift sanctions to end the war.");

            Assert.IsTrue(PeaceSystem.ProposeTerms(state, confrontation, state.playerCountryId, proposal),
                "The terms were acceptable but the proposal was refused.");

            Assert.IsNull(state.FindSanction("CHN", state.playerCountryId),
                "The settlement was signed and their sanctions were still in force — "
                + "the term resolved and changed nothing.");
        }

        /// <summary>
        /// A term whose condition is unmet prices to zero, which the settlement
        /// screen used to render as an ordinary free concession. It must instead
        /// say why it is unavailable.
        /// </summary>
        [Test]
        public void ATermThatCouldAchieveNothingSaysSo()
        {
            // Nobody has sanctioned anybody in the fixture.
            Assert.IsNotNull(PeaceSystem.WhyInert(state, confrontation,
                state.playerCountryId, PeaceTerm.SanctionsLifted),
                "Demanding they lift sanctions they never imposed read as a live term.");

            Assert.IsNotNull(PeaceSystem.WhyInert(state, confrontation,
                state.playerCountryId, PeaceTerm.SanctionsRelief),
                "Offering to lift sanctions we never imposed read as a live concession.");

            EconomySystem.ImposeSanctionsBy(state, "CHN", state.playerCountryId, SanctionSeverity.Pressure);
            Assert.IsNull(PeaceSystem.WhyInert(state, confrontation,
                state.playerCountryId, PeaceTerm.SanctionsLifted),
                "Their sanction exists, so the term is live and must not be greyed out.");

            // Reparations are always askable — the guard must not over-reach and
            // disable ordinary terms.
            Assert.IsNull(PeaceSystem.WhyInert(state, confrontation,
                state.playerCountryId, PeaceTerm.Reparations));
        }

        [Test]
        public void DemandsAndConcessionsArePointedOppositeWays()
        {
            // The classification has to mean something, not merely exist.
            Assert.IsTrue(PeaceSystem.IsDemand(PeaceTerm.TerritorialCession),
                "Taking their territory is a demand.");
            Assert.IsTrue(PeaceSystem.IsDemand(PeaceTerm.Reparations));
            Assert.IsFalse(PeaceSystem.IsDemand(PeaceTerm.Withdrawal),
                "Handing back what we occupy is something we give up.");
            Assert.IsFalse(PeaceSystem.IsDemand(PeaceTerm.SecurityGuarantee));
        }
    }
}

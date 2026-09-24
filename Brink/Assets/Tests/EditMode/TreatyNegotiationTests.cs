using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Treaties as a negotiation, and what a one-sided one costs
    /// (GDD §15.1 amendment).
    ///
    /// A treaty was a flat list of commitments both sides implicitly received, so
    /// every agreement was symmetrical by construction and there was nothing to
    /// negotiate: pick a partner, pick clauses, signed. Together with trade it is
    /// why the diplomatic playstyle graded a full point above passive holding the
    /// best economic *and* positional components in the game — the pillar
    /// compounded and never paid.
    /// </summary>
    public class TreatyNegotiationTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3131);
            state.commandPoints.current = 60;
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static List<TreatyClause> Clauses(params (TreatyCommitment c, ClauseSide s)[] items)
        {
            var list = new List<TreatyClause>();
            foreach (var item in items)
                list.Add(new TreatyClause { commitment = item.c, side = item.s });
            return list;
        }

        void Warm(string id, float relations = 80f, float trust = 75f)
        {
            var relationship = state.FindRelationship(state.playerCountryId, id);
            relationship.relations = relations;
            relationship.trust = trust;
            relationship.strategicAlignment = 70f;

            // Threat perception subtracts from every willingness calculation, and
            // leaving it at whatever the world rolled makes a fixture that is
            // "warm" on three dimensions and hostile on a fourth. An earlier
            // version omitted this and reported "a desperate state refused terms"
            // — which reads as a finding about the game rather than a fixture that
            // never took.
            relationship.SetThreatPerceivedBy(id, 0f);
        }

        /// <summary>
        /// Sign, having first checked the offer would actually be accepted.
        ///
        /// Asserting the precondition separately is what turns "the treaty is
        /// missing" into "they were only 42 willing" — the difference between a
        /// broken fixture and a statement about the design.
        /// </summary>
        void SignExpectingAcceptance(string targetId, List<TreatyClause> clauses)
        {
            float willingness = DiplomacySystem.TreatyWillingness(
                state, state.playerCountryId, targetId, clauses);

            Assert.GreaterOrEqual(willingness, 50f,
                $"The fixture did not take: {targetId} is only {willingness:F1} willing, so "
                + $"nothing below tests what it claims. Balance was "
                + $"{DiplomacySystem.BalanceOf(clauses):F1}.");

            Assert.IsTrue(DiplomacySystem.ProposeNegotiatedTreatyBy(
                    state, state.playerCountryId, targetId, clauses),
                "Willing at 50+ and still refused.");
        }

        // ---------- balance is a real axis ----------

        [Test]
        public void RelationshipConditionSignsAndReactivatesAtItsExactBoundary()
        {
            state.treaties.Clear(); Warm("IND", 95, 95);
            SignExpectingAcceptance("IND", new List<TreatyClause> { new TreatyClause {
                commitment = TreatyCommitment.Transit, side = ClauseSide.TheyProvide,
                trigger = TreatyClauseTrigger.RelationsAtLeast60, durationMonths = 12 } });
            var treaty = state.FindTreaty(state.playerCountryId, "IND");
            var relation = state.FindRelationship(state.playerCountryId, "IND");
            relation.relations = 59.99f;
            Assert.IsFalse(treaty.Carries(state, "IND", TreatyCommitment.Transit));
            relation.relations = 60;
            Assert.IsTrue(treaty.Carries(state, "IND", TreatyCommitment.Transit));
            Assert.IsFalse(treaty.Carries(state, state.playerCountryId, TreatyCommitment.Transit));
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(loaded.FindTreaty(state.playerCountryId, "IND").Carries(loaded, "IND", TreatyCommitment.Transit));
            for (int i = 0; i < 12; i++) state.date = state.date.NextMonth();
            Assert.IsFalse(treaty.ClauseIsActive(state, TreatyCommitment.Transit), "Expiry still wins over a satisfied condition.");
        }

        [Test]
        public void OccupationConditionFollowsBothSidesCurrentTitleAndHolding()
        {
            state.treaties.Clear(); Warm("IND", 95, 95);
            SignExpectingAcceptance("IND", new List<TreatyClause> { new TreatyClause {
                commitment = TreatyCommitment.Transit, trigger = TreatyClauseTrigger.NoMutualOccupation } });
            var treaty = state.FindTreaty(state.playerCountryId, "IND");
            Assert.IsTrue(treaty.ClauseIsActive(state, TreatyCommitment.Transit));
            var site = state.locations.Find(s => s.originalOwnerId == "IND");
            Assert.IsNotNull(site); site.ownerId = state.playerCountryId;
            Assert.IsFalse(treaty.ClauseIsActive(state, TreatyCommitment.Transit));
            site.ownerId = "IND";
            Assert.IsTrue(treaty.ClauseIsActive(state, TreatyCommitment.Transit));
            var ours = state.locations.Find(s => s.originalOwnerId == state.playerCountryId);
            ours.ownerId = "IND";
            Assert.IsFalse(treaty.ClauseIsActive(state, TreatyCommitment.Transit));
            ours.ownerId = "CHN";
            Assert.IsTrue(treaty.ClauseIsActive(state, TreatyCommitment.Transit), "Third-party occupation is not this condition.");
        }

        [TestCase(TreatyClauseTrigger.RelationsAtLeast60)]
        [TestCase(TreatyClauseTrigger.NoMutualOccupation)]
        public void NewConditionsSurviveLeverageCopiesAndRejectThirdCountryArguments(TreatyClauseTrigger trigger)
        {
            var terms = new TreatyClause { trigger = trigger, durationMonths = 36 };
            Assert.IsTrue(DiplomacySystem.ClauseTermsAreValid(state, state.playerCountryId, "IND", terms, out _));
            var copy = DiplomaticLeverage.RequestedClause(TreatyCommitment.Transit, terms);
            Assert.AreEqual(trigger, copy.trigger); Assert.AreEqual(36, copy.durationMonths);
            Assert.AreEqual(ClauseSide.TheyProvide, copy.side);
            StringAssert.Contains("WHILE", DiplomacySystem.TermsText(state, copy, state.date));
            terms.triggerCountryId = "CHN";
            Assert.IsFalse(DiplomacySystem.ClauseTermsAreValid(state, state.playerCountryId, "IND", terms, out _));
        }

        [Test]
        public void UnknownSavedTriggerDoesNotMasqueradeAsConflict()
        {
            var treaty = new Treaty { countryA = state.playerCountryId, countryB = "IND" };
            treaty.commitments.Add(TreatyCommitment.Transit);
            treaty.clauses.Add(new TreatyClause { commitment = TreatyCommitment.Transit,
                trigger = (TreatyClauseTrigger)999, triggerCountryId = "CHN" });
            state.confrontations.Add(new Confrontation { initiatorId = state.playerCountryId,
                defenderId = "CHN", escalation = EscalationState.LimitedConflict });
            Assert.IsFalse(treaty.ClauseIsActive(state, TreatyCommitment.Transit));
        }

        [Test]
        public void AnEvenTreatyIsBalancedHoweverLargeItIs()
        {
            // Scale and fairness are different axes. Five mutual commitments is a
            // heavy agreement and a fair one.
            var even = Clauses(
                (TreatyCommitment.MutualDefense, ClauseSide.Mutual),
                (TreatyCommitment.IntelligenceSharing, ClauseSide.Mutual),
                (TreatyCommitment.Transit, ClauseSide.Mutual),
                (TreatyCommitment.JointPlanning, ClauseSide.Mutual));

            Assert.AreEqual(0f, DiplomacySystem.BalanceOf(even), 0.01f,
                "A treaty both sides carry equally came out lopsided.");
        }

        [Test]
        public void TakingMoreThanYouGiveShowsInTheBalance()
        {
            var lopsided = Clauses(
                (TreatyCommitment.MutualDefense, ClauseSide.TheyProvide),
                (TreatyCommitment.Transit, ClauseSide.TheyProvide),
                (TreatyCommitment.NonAggression, ClauseSide.WeProvide));

            Assert.Greater(DiplomacySystem.BalanceOf(lopsided), 5f,
                "Asking for a defence guarantee and basing while offering only not to "
                + "attack them registered as even.");
        }

        // ---------- who can afford to refuse ----------

        [Test]
        public void AnIndependentStateRefusesTermsADependentOneAccepts()
        {
            // This is the whole shape of the decision: extraction is *possible*,
            // against somebody who cannot say no.
            float WillingnessWithDependence(float dependence)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 3131);
                var relationship = world.FindRelationship(world.playerCountryId, "MEX");
                relationship.relations = 75f;
                relationship.trust = 70f;
                relationship.strategicAlignment = 70f;
                relationship.SetDependenceOf("MEX", dependence);

                return DiplomacySystem.TreatyWillingness(world, world.playerCountryId, "MEX",
                    Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide),
                            (TreatyCommitment.Transit, ClauseSide.TheyProvide)));
            }

            Assert.Greater(WillingnessWithDependence(85f), WillingnessWithDependence(5f),
                "A state that depends on us was no more willing to accept one-sided terms "
                + "than one that does not need us at all.");
        }

        [Test]
        public void AFairOfferIsEasierToSignThanAnExtractiveOne()
        {
            Warm("DEU");

            float fair = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU",
                Clauses((TreatyCommitment.NonAggression, ClauseSide.Mutual),
                        (TreatyCommitment.IntelligenceSharing, ClauseSide.Mutual)));

            float extractive = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU",
                Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide),
                        (TreatyCommitment.Transit, ClauseSide.TheyProvide),
                        (TreatyCommitment.IntelligenceSharing, ClauseSide.TheyProvide)));

            Assert.Greater(fair, extractive,
                "Demanding everything and offering nothing was received as warmly as an "
                + "even bargain.");
        }

        // ---------- and what extraction costs ----------

        [Test]
        public void AOneSidedTreatyDamagesOurNameAsAPartner()
        {
            Warm("MEX", 85f, 80f);
            state.FindRelationship(state.playerCountryId, "MEX").SetDependenceOf("MEX", 90f);

            float before = state.PlayerCountry.reciprocity;

            SignExpectingAcceptance("MEX",
                Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide),
                        (TreatyCommitment.Transit, ClauseSide.TheyProvide),
                        (TreatyCommitment.IntelligenceSharing, ClauseSide.TheyProvide)));
            Assert.Less(state.PlayerCountry.reciprocity, before,
                "Extracting everything from a state that could not refuse cost us nothing.");
        }

        [Test]
        public void ThirdPartiesNoticeExtraction()
        {
            // The mechanism the whole feature rests on: a habit of extraction is
            // expensive, not just one deal.
            Warm("MEX", 85f, 80f);
            state.FindRelationship(state.playerCountryId, "MEX").SetDependenceOf("MEX", 90f);

            var bystander = state.FindRelationship(state.playerCountryId, "DEU");
            float trustBefore = bystander.trust;

            SignExpectingAcceptance("MEX",
                Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide),
                        (TreatyCommitment.Transit, ClauseSide.TheyProvide),
                        (TreatyCommitment.IntelligenceSharing, ClauseSide.TheyProvide)));

            Assert.Less(bystander.trust, trustBefore,
                "A government watching us strip a weaker neighbour thought no less of us.");
        }

        [Test]
        public void ABadNameMakesEveryoneHarderToDealWith()
        {
            Warm("DEU");
            var clauses = Clauses((TreatyCommitment.NonAggression, ClauseSide.Mutual));

            state.PlayerCountry.reciprocity = 95f;
            float trusted = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", clauses);

            state.PlayerCountry.reciprocity = 10f;
            float distrusted = DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", clauses);

            Assert.Greater(trusted, distrusted,
                "A reputation for stripping partners made no difference to the next state "
                + "asked to sign something. Then extraction is free after all.");
        }

        [Test]
        public void DealingEvenlyRebuildsTheReputationSlowly()
        {
            // Cheap to spend, expensive to earn back — otherwise the operator
            // simply alternates.
            var player = state.PlayerCountry;
            player.reciprocity = 30f;
            Warm("DEU");

            float before = player.reciprocity;
            DiplomacySystem.ProposeNegotiatedTreatyBy(state, state.playerCountryId, "DEU",
                Clauses((TreatyCommitment.NonAggression, ClauseSide.Mutual),
                        (TreatyCommitment.IntelligenceSharing, ClauseSide.Mutual)));

            float gained = player.reciprocity - before;
            Assert.Greater(gained, 0f, "An even deal did nothing for our name.");
            Assert.Less(gained, 4f,
                "One fair treaty repaired a reputation faster than a bad one damaged it, "
                + "so alternating would be free.");
        }

        // ---------- the record, and the ministry ----------

        [Test]
        public void ATreatyRemembersWhoCarriesWhat()
        {
            Warm("DEU");
            DiplomacySystem.ProposeNegotiatedTreatyBy(state, state.playerCountryId, "DEU",
                Clauses((TreatyCommitment.Transit, ClauseSide.TheyProvide),
                        (TreatyCommitment.NonAggression, ClauseSide.Mutual)));

            var treaty = state.FindTreaty(state.playerCountryId, "DEU");
            Assert.IsNotNull(treaty);
            Assert.AreEqual(ClauseSide.TheyProvide,
                treaty.SideFor(state.playerCountryId, TreatyCommitment.Transit),
                "The agreement forgot which side granted basing.");
            Assert.AreEqual(ClauseSide.WeProvide,
                treaty.SideFor("DEU", TreatyCommitment.Transit),
                "The same clause has to read the other way round from their side.");
        }

        [Test]
        public void ClauseDirectionControlsWhoCarriesAndReceives()
        {
            var treaty = new Treaty
            {
                id = "T_DIRECTION",
                countryA = state.playerCountryId,
                countryB = "DEU"
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.MutualDefense,
                side = ClauseSide.WeProvide
            });

            Assert.IsTrue(treaty.Carries(state.playerCountryId, TreatyCommitment.MutualDefense));
            Assert.IsTrue(treaty.Receives("DEU", TreatyCommitment.MutualDefense));
            Assert.IsFalse(treaty.Carries("DEU", TreatyCommitment.MutualDefense));
            Assert.IsFalse(treaty.Receives(state.playerCountryId, TreatyCommitment.MutualDefense));
        }

        [Test]
        public void AOneWayGuaranteeCallsOnlyThePromisingSide()
        {
            state.blocs.Clear();
            var treaty = new Treaty
            {
                id = "T_GUARANTEE",
                countryA = state.playerCountryId,
                countryB = "DEU"
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.MutualDefense,
                side = ClauseSide.WeProvide
            });
            state.treaties.Add(treaty);

            Assert.IsTrue(HasGuarantor(
                AllianceSystem.GuarantorsOf(state, "DEU", "CHN"), state.playerCountryId),
                "We promised to defend Germany, but its guarantor list omitted us.");
            Assert.IsFalse(HasGuarantor(
                AllianceSystem.GuarantorsOf(state, state.playerCountryId, "CHN"), "DEU"),
                "Germany was called to defend us despite never making that promise.");
        }

        [Test]
        public void ConditionalGuaranteeWakesOnlyDuringTheNamedConflict()
        {
            state.blocs.Clear();
            var treaty = new Treaty
            {
                id = "T_CONDITIONAL_GUARANTEE", countryA = state.playerCountryId,
                countryB = "DEU", signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.MutualDefense,
                side = ClauseSide.WeProvide,
                trigger = TreatyClauseTrigger.ConflictWithCountry,
                triggerCountryId = "CHN"
            });
            state.treaties.Add(treaty);

            Assert.IsFalse(HasGuarantor(
                AllianceSystem.GuarantorsOf(state, "DEU", "CHN"), state.playerCountryId),
                "A dormant conditional guarantee was called as if it were permanent.");

            var war = ConfrontationSystem.BeginBy(state, "DEU", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(war);
            war.escalation = EscalationState.LimitedConflict;

            Assert.IsTrue(HasGuarantor(
                AllianceSystem.GuarantorsOf(state, "DEU", "CHN"), state.playerCountryId),
                "The named conflict began but the conditional guarantee stayed dormant.");
        }

        [Test]
        public void TriggerAndTermComposeAndLegacyClausesStayPermanent()
        {
            var treaty = new Treaty
            {
                id = "T_COMPOSED", countryA = state.playerCountryId,
                countryB = "DEU", signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.Transit);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.Transit,
                trigger = TreatyClauseTrigger.ConflictWithCountry,
                triggerCountryId = "CHN",
                durationMonths = 1
            });
            state.treaties.Add(treaty);

            var war = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(war);
            war.escalation = EscalationState.LimitedConflict;
            Assert.IsTrue(treaty.ClauseIsActive(state, TreatyCommitment.Transit),
                "Both live conditions held but the clause was dormant.");

            state.date = state.date.NextMonth();
            Assert.IsFalse(treaty.ClauseIsActive(state, TreatyCommitment.Transit),
                "The trigger kept an expired clause alive.");

            var old = new Treaty
            {
                id = "T_LEGACY", countryA = state.playerCountryId,
                countryB = "IND", signedDate = state.date
            };
            old.commitments.Add(TreatyCommitment.Transit);
            Assert.IsTrue(old.ClauseIsActive(state, TreatyCommitment.Transit),
                "A clause-less treaty from an old save stopped being permanent.");
        }

        [Test]
        public void NarrowerPromisesAreEasierToAccept()
        {
            var permanent = Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide));
            var bounded = Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide));
            bounded[0].trigger = TreatyClauseTrigger.ConflictWithCountry;
            bounded[0].triggerCountryId = "CHN";
            bounded[0].durationMonths = 12;

            Assert.Greater(
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", bounded),
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", permanent),
                "A one-year promise for one named conflict cost exactly as much as a permanent guarantee.");

            var permanentGift = Clauses((TreatyCommitment.MutualDefense, ClauseSide.WeProvide));
            var boundedGift = Clauses((TreatyCommitment.MutualDefense, ClauseSide.WeProvide));
            boundedGift[0].durationMonths = 12;
            Assert.Less(
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", boundedGift),
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", permanentGift),
                "A short-lived gift was valued exactly like a permanent benefit.");

            var fiveYears = Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide));
            fiveYears[0].durationMonths = 60;
            Assert.Greater(
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", fiveYears),
                DiplomacySystem.TreatyWillingness(state, state.playerCountryId, "DEU", permanent),
                "A five-year promise cost exactly as much as a permanent one.");
        }

        [Test]
        public void ExpiredClausesCanBeRenewedWithoutExtendingTheirNeighbours()
        {
            Warm("DEU");
            var treaty = new Treaty
            {
                id = "T_RENEW", countryA = state.playerCountryId,
                countryB = "DEU", signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.Transit);
            treaty.commitments.Add(TreatyCommitment.IntelligenceSharing);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.Transit,
                side = ClauseSide.TheyProvide,
                durationMonths = 12,
                effectiveDate = state.date
            });
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.IntelligenceSharing,
                durationMonths = 36,
                effectiveDate = state.date
            });
            state.treaties.Add(treaty);

            for (int i = 0; i < 12; i++) state.date = state.date.NextMonth();
            GameDate neighbourExpiry = treaty.ClauseExpiry(TreatyCommitment.IntelligenceSharing);
            Assert.IsTrue(treaty.ClauseIsExpired(state, TreatyCommitment.Transit));

            Assert.AreEqual("EXPIRES BEFORE JAN 1985",
                DiplomacySystem.ClauseTermText(treaty, TreatyCommitment.Transit));

            Assert.IsTrue(DiplomacySystem.DeepenTreatyBy(state, state.playerCountryId, "DEU",
                new List<TreatyCommitment> { TreatyCommitment.Transit }));
            Assert.IsTrue(treaty.HasActive(state, TreatyCommitment.Transit));
            Assert.AreEqual(ClauseSide.TheyProvide,
                treaty.SideFor(state.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(neighbourExpiry, treaty.ClauseExpiry(TreatyCommitment.IntelligenceSharing),
                "Renewing one clause silently extended another term.");
            Assert.AreEqual("TREATY RENEWED", state.notifications[state.notifications.Count - 1].title);
            StringAssert.Contains("renew their agreement",
                state.notifications[state.notifications.Count - 1].body);
        }

        [Test]
        public void RenewalIsJudgedAtTheClausesActualScope()
        {
            var treaty = new Treaty
            {
                id = "T_PRICE_RENEWAL", countryA = state.playerCountryId,
                countryB = "DEU", signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.MutualDefense,
                side = ClauseSide.TheyProvide,
                trigger = TreatyClauseTrigger.ConflictWithCountry,
                triggerCountryId = "CHN",
                durationMonths = 12,
                effectiveDate = state.date
            });
            state.treaties.Add(treaty);
            for (int i = 0; i < 12; i++) state.date = state.date.NextMonth();

            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            bool foundBoundary = false;
            for (int warmth = 0; warmth <= 100 && !foundBoundary; warmth++)
            {
                relationship.relations = warmth;
                relationship.trust = warmth;
                relationship.strategicAlignment = warmth;
                float history = 8f + state.date.MonthsSince(treaty.signedDate) * 0.1f;
                var scoped = Clauses((TreatyCommitment.MutualDefense, ClauseSide.TheyProvide));
                scoped[0].trigger = TreatyClauseTrigger.ConflictWithCountry;
                scoped[0].triggerCountryId = "CHN";
                scoped[0].durationMonths = 12;
                float scopedReading = DiplomacySystem.TreatyWillingness(
                    state, state.playerCountryId, "DEU", scoped) + history;
                float flatReading = DiplomacySystem.TreatyWillingness(state,
                    state.playerCountryId, "DEU",
                    new List<TreatyCommitment> { TreatyCommitment.MutualDefense }) + history;
                foundBoundary = scopedReading >= 50f && flatReading < 50f;
            }

            Assert.IsTrue(foundBoundary, "Fixture never separated scoped renewal from permanent-mutual pricing.");
            Assert.IsTrue(DiplomacySystem.DeepenTreatyBy(state, state.playerCountryId, "DEU",
                new List<TreatyCommitment> { TreatyCommitment.MutualDefense }),
                "Renewal was judged as a permanent mutual promise instead of its actual narrow scope.");
        }

        [Test]
        public void AiRenewsAnExpiredDefenseClauseThroughItsRealTreatyPath()
        {
            for (int i = 0; i < AISystem.TreatyWarmUpMonths + 1; i++)
                state.date = state.date.NextMonth();

            var relationship = state.FindRelationship("DEU", "IND");
            relationship.relations = 95f;
            relationship.trust = 95f;
            relationship.strategicAlignment = 95f;
            var treaty = new Treaty
            {
                id = "T_AI_RENEW", countryA = "DEU", countryB = "IND",
                signedDate = state.startDate
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.MutualDefense,
                durationMonths = 12,
                effectiveDate = state.startDate
            });
            state.treaties.Add(treaty);

            var method = typeof(AISystem).GetMethod("SeekTreaty",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            bool renewed = (bool)method.Invoke(null,
                new object[] { state, state.FindCountry("DEU"), "IND" });

            Assert.IsTrue(renewed);
            Assert.AreEqual(state.date, treaty.clauses[0].effectiveDate);
            Assert.IsTrue(treaty.HasActive(state, TreatyCommitment.MutualDefense));
        }

        [Test]
        public void ExpiredPromisesStopShapingCurrentRelationshipStatus()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            relationship.relations = 90f;
            relationship.trust = 90f;
            relationship.strategicAlignment = 90f;
            var treaty = new Treaty
            {
                id = "T_STATUS", countryA = state.playerCountryId,
                countryB = "DEU", signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.MutualDefense,
                durationMonths = 1,
                effectiveDate = state.date
            });
            state.treaties.Add(treaty);

            Assert.AreEqual(RelationshipStatus.Ally,
                DiplomacySystem.StatusOf(state, state.playerCountryId, "DEU"));
            state.date = state.date.NextMonth();
            Assert.AreNotEqual(RelationshipStatus.Ally,
                DiplomacySystem.StatusOf(state, state.playerCountryId, "DEU"));
        }

        [Test]
        public void AConditionMustNameARealThirdState()
        {
            var clause = Clauses((TreatyCommitment.Transit, ClauseSide.Mutual));
            clause[0].trigger = TreatyClauseTrigger.ConflictWithCountry;
            clause[0].triggerCountryId = "DEU";
            Assert.IsFalse(DiplomacySystem.ProposeNegotiatedTreatyBy(
                    state, state.playerCountryId, "DEU", clause),
                "A treaty used its own signatory as the external trigger.");

            clause[0].triggerCountryId = "NOT_A_COUNTRY";
            Assert.IsFalse(DiplomacySystem.ProposeNegotiatedTreatyBy(
                    state, state.playerCountryId, "DEU", clause),
                "A treaty was conditioned on a state that does not exist.");
        }

        [Test]
        public void TransitRunsOnlyFromTheCountryThatGrantedIt()
        {
            StrategicLocation host = null;
            foreach (var location in state.locations)
                if (location.ownerId == "IND" && location.SupportsBasing) { host = location; break; }
            Assert.IsNotNull(host, "The authored world needs an Indian basing location.");
            host.foreignOperatorId = "";

            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            relationship.relations = 85f;
            relationship.trust = 80f;
            relationship.SetThreatPerceivedBy("IND", 0f);

            var treaty = new Treaty
            {
                id = "T_TRANSIT",
                countryA = state.playerCountryId,
                countryB = "IND"
            };
            treaty.commitments.Add(TreatyCommitment.Transit);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.Transit,
                side = ClauseSide.WeProvide
            });
            state.treaties.Add(treaty);

            DiplomacySystem.MonthlyUpdate(state);
            Assert.AreNotEqual(state.playerCountryId, host.foreignOperatorId,
                "Our grant of transit was misread as India hosting us.");

            treaty.clauses[0].side = ClauseSide.TheyProvide;
            DiplomacySystem.MonthlyUpdate(state);
            Assert.AreEqual(state.playerCountryId, host.foreignOperatorId,
                "India granted transit, but its base remained unavailable to us.");
        }

        [Test]
        public void ConditionalTransitDoesNotOpenABaseUntilItsConflictBegins()
        {
            StrategicLocation host = null;
            foreach (var location in state.locations)
                if (location.ownerId == "IND" && location.SupportsBasing) { host = location; break; }
            Assert.IsNotNull(host);
            host.foreignOperatorId = "";

            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            relationship.relations = 85f;
            relationship.SetThreatPerceivedBy("IND", 0f);

            var treaty = new Treaty
            {
                id = "T_CONDITIONAL_TRANSIT", countryA = state.playerCountryId,
                countryB = "IND", signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.Transit);
            treaty.clauses.Add(new TreatyClause
            {
                commitment = TreatyCommitment.Transit,
                side = ClauseSide.TheyProvide,
                trigger = TreatyClauseTrigger.ConflictWithCountry,
                triggerCountryId = "CHN"
            });
            state.treaties.Add(treaty);

            host.foreignOperatorId = state.playerCountryId;
            DiplomacySystem.MonthlyUpdate(state);
            Assert.AreNotEqual(state.playerCountryId, host.foreignOperatorId,
                "Dormant conditional transit opened a foreign base.");

            var war = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(war);
            war.escalation = EscalationState.LimitedConflict;
            DiplomacySystem.MonthlyUpdate(state);
            Assert.AreEqual(state.playerCountryId, host.foreignOperatorId,
                "The named conflict began but its transit promise stayed dormant.");
        }

        [Test]
        public void NegotiatedTermsAreJudgedOnceOnTheirActualSides()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "DEU");
            relationship.SetThreatPerceivedBy("DEU", 0f);
            relationship.SetDependenceOf("DEU", 0f);
            var clauses = Clauses(
                (TreatyCommitment.MutualDefense, ClauseSide.WeProvide),
                (TreatyCommitment.Transit, ClauseSide.WeProvide));
            var flat = new List<TreatyCommitment>
            {
                TreatyCommitment.MutualDefense,
                TreatyCommitment.Transit
            };

            bool foundBoundary = false;
            for (int warmth = 0; warmth <= 100; warmth++)
            {
                relationship.relations = warmth;
                relationship.trust = warmth;
                relationship.strategicAlignment = warmth;
                if (DiplomacySystem.TreatyWillingness(
                        state, state.playerCountryId, "DEU", clauses) >= 50f
                    && DiplomacySystem.TreatyWillingness(
                        state, state.playerCountryId, "DEU", flat) < 50f)
                {
                    foundBoundary = true;
                    break;
                }
            }

            Assert.IsTrue(foundBoundary,
                "Fixture never reached the boundary between the directional and flat offers.");
            Assert.IsTrue(DiplomacySystem.ProposeNegotiatedTreatyBy(
                    state, state.playerCountryId, "DEU", clauses),
                "A clause-aware offer passed, then a second direction-blind check rejected it.");
        }

        [Test]
        public void TheMinistryRecommendsAgainstOurWorstGap()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 20f;
            Assert.AreEqual(TreatyCommitment.TradePreference,
                DiplomacySystem.SuggestedCommitmentFor(player),
                "The diplomat did not notice we are short of energy.");

            player.resources.energy = 90f;
            player.resources.strategicMaterials = 90f;
            player.pillars.military = 30f;
            Assert.AreEqual(TreatyCommitment.MutualDefense,
                DiplomacySystem.SuggestedCommitmentFor(player),
                "Well supplied and militarily weak, the ministry should be looking for a guarantee.");
        }

        [Test]
        public void ClausesSurviveASaveRoundTrip()
        {
            Warm("DEU");
            var clauses = Clauses((TreatyCommitment.Transit, ClauseSide.TheyProvide));
            clauses[0].trigger = TreatyClauseTrigger.ConflictWithCountry;
            clauses[0].triggerCountryId = "CHN";
            clauses[0].durationMonths = 36;
            DiplomacySystem.ProposeNegotiatedTreatyBy(
                state, state.playerCountryId, "DEU", clauses);

            var restored = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var treaty = restored.FindTreaty(restored.playerCountryId, "DEU");

            Assert.IsNotNull(treaty);
            Assert.AreEqual(ClauseSide.TheyProvide,
                treaty.SideFor(restored.playerCountryId, TreatyCommitment.Transit));
            Assert.AreEqual(TreatyClauseTrigger.ConflictWithCountry, treaty.clauses[0].trigger);
            Assert.AreEqual("CHN", treaty.clauses[0].triggerCountryId);
            Assert.AreEqual(36, treaty.clauses[0].durationMonths);
            Assert.AreEqual(state.date, treaty.clauses[0].effectiveDate);
            Assert.AreEqual(state.PlayerCountry.reciprocity, restored.PlayerCountry.reciprocity, 0.01f);
        }

        [Test]
        public void AnOldTreatyWithNoClausesReadsAsMutual()
        {
            // Old saves recorded no sides, and what they described *were*
            // reciprocal agreements — so that is what they must come back as.
            var treaty = new Treaty
            {
                id = "T_OLD", countryA = state.playerCountryId, countryB = "DEU",
                signedDate = state.date
            };
            treaty.commitments.Add(TreatyCommitment.MutualDefense);
            state.treaties.Add(treaty);

            Assert.AreEqual(ClauseSide.Mutual,
                treaty.SideFor(state.playerCountryId, TreatyCommitment.MutualDefense),
                "A treaty signed before sides existed must read as an even one.");
            Assert.IsTrue(treaty.Carries(state.playerCountryId, TreatyCommitment.MutualDefense));
            Assert.IsTrue(treaty.Carries("DEU", TreatyCommitment.MutualDefense));
            Assert.IsTrue(treaty.Receives(state.playerCountryId, TreatyCommitment.MutualDefense));
            Assert.IsTrue(treaty.Receives("DEU", TreatyCommitment.MutualDefense));
        }

        static bool HasGuarantor(List<AllianceSystem.Guarantor> guarantors, string countryId)
        {
            foreach (var guarantor in guarantors)
                if (guarantor.countryId == countryId) return true;
            return false;
        }
    }
}

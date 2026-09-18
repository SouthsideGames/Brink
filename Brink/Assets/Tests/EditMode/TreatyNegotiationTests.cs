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
            DiplomacySystem.ProposeNegotiatedTreatyBy(state, state.playerCountryId, "DEU",
                Clauses((TreatyCommitment.Transit, ClauseSide.TheyProvide)));

            var restored = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var treaty = restored.FindTreaty(restored.playerCountryId, "DEU");

            Assert.IsNotNull(treaty);
            Assert.AreEqual(ClauseSide.TheyProvide,
                treaty.SideFor(restored.playerCountryId, TreatyCommitment.Transit));
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

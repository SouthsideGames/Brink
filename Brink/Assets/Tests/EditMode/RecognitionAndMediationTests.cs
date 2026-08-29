using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Diplomacy's episodic layer (spec 04 §5b, §5c).
    ///
    /// Two gaps this closes. `SecessionSystem` is the only thing in the game
    /// that constructs a country at runtime and diplomacy had **no verb about
    /// one** — a state could come into existence and the world had no way to
    /// take a position on whether it existed. And the world now fights its own
    /// wars, which the operator could only ever watch.
    /// </summary>
    public class RecognitionAndMediationTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5512);
            state.commandPoints.current = 60;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>
        /// A breakaway, made the way `SecessionSystem` makes one: a new country
        /// with the parent's id plus `_S`, founded after the world was.
        /// </summary>
        CountryState PlantABreakaway(string parentId)
        {
            var parent = state.FindCountry(parentId);
            var successor = new CountryState
            {
                id = parentId + "_S",
                displayName = parent.displayName + " (Breakaway)",
                foundedDate = state.date.NextMonth(),
                stability = 38f,
                nationalUnity = 66f
            };
            state.countries.Add(successor);

            foreach (var other in state.countries)
            {
                if (other.id == successor.id) continue;
                state.relationships.Add(new Relationship
                {
                    countryA = successor.id,
                    countryB = other.id,
                    relations = 45f,
                    trust = 40f
                });
            }
            return successor;
        }

        // ---------- recognition ----------

        [Test]
        public void OnlyAStateThatJustDeclaredItselfCanBeRecognised()
        {
            // The verb is about a *new* state. Offering it against a founding
            // member of the world would be an index entry that is always refused,
            // which teaches the operator to stop reading the index.
            Assert.IsFalse(DiplomacySystem.CanRecognise(state, state.playerCountryId, "CHN",
                    out string reason),
                "A country that has been there since world creation was offered recognition.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void ABreakawayStartsUnrecognisedByEverybody()
        {
            var successor = PlantABreakaway("IND");

            Assert.AreEqual(0, DiplomacySystem.RecognitionCount(state, successor.id),
                "A state that declared itself this month was already recognised by somebody.");
            Assert.Less(DiplomacySystem.Legitimacy(state, successor), 0.05f,
                "It began with the legitimacy of an established country, so recognition buys "
                + "nothing and the whole situation it has to work out of does not exist.");

            // And an established state is unaffected by any of this.
            Assert.AreEqual(1f, DiplomacySystem.Legitimacy(state, state.PlayerCountry), 0.0001f,
                "Legitimacy is not 1.0 for a founding member, so every country in the world "
                + "now carries a penalty meant for breakaways.");
        }

        [Test]
        public void RecognisingBuysTheNewStateAndCostsTheOldOne()
        {
            var successor = PlantABreakaway("IND");
            var withSuccessor = state.FindRelationship(state.playerCountryId, successor.id);
            var withParent = state.FindRelationship(state.playerCountryId, "IND");

            float toSuccessor = withSuccessor.relations;
            float toParent = withParent.relations;

            Assert.IsTrue(DiplomacySystem.RecogniseBy(state, state.playerCountryId, successor.id));

            Assert.Greater(withSuccessor.relations, toSuccessor,
                "The state we just admitted exists was not grateful.");
            Assert.Less(withParent.relations, toParent,
                "Recognising a breakaway cost nothing with the country it broke away from, so "
                + "the decision has only an upside and is not a decision.");
            Assert.IsTrue(withSuccessor.recognised);
        }

        [Test]
        public void RecognitionIsNotOfferedTwice()
        {
            var successor = PlantABreakaway("IND");
            DiplomacySystem.RecogniseBy(state, state.playerCountryId, successor.id);

            Assert.IsFalse(DiplomacySystem.CanRecognise(state, state.playerCountryId,
                    successor.id, out _),
                "We were offered the chance to recognise a state we already recognise, which is "
                + "a free way to keep buying the same goodwill.");
        }

        [Test]
        public void AnUnrecognisedStateIsHarderToGovernAndCannotSignTreaties()
        {
            // Recognition has to be worth something concrete to the state
            // receiving it, or it is a line on a screen.
            var successor = PlantABreakaway("IND");

            float unrecognisedWillingness = DiplomacySystem.TreatyWillingness(
                state, successor.id, "CHN",
                new System.Collections.Generic.List<TreatyCommitment> { TreatyCommitment.NonAggression });

            foreach (var other in state.countries)
            {
                if (other.id == successor.id) continue;
                DiplomacySystem.RecogniseBy(state, other.id, successor.id);
            }

            float recognisedWillingness = DiplomacySystem.TreatyWillingness(
                state, successor.id, "CHN",
                new System.Collections.Generic.List<TreatyCommitment> { TreatyCommitment.NonAggression });

            Assert.Greater(recognisedWillingness, unrecognisedWillingness,
                "A state the whole world now accepts found treaties no easier to sign than one "
                + "nobody admitted existed. Recognition has to be the thing a breakaway needs "
                + "first, or there is no reason to spend standing on it.");
            Assert.Greater(DiplomacySystem.Legitimacy(state, successor), 0.9f);
        }

        [Test]
        public void ForeignGovernmentsTakeTheirOwnPositions()
        {
            // The most-repeated bug in this codebase is an AI state locked out of
            // a player verb. If only the operator could recognise anybody, a
            // breakaway's legitimacy would be entirely the player's gift.
            var successor = PlantABreakaway("IND");

            // Make the new state attractive and the parent widely disliked, so
            // somebody has a reason to move.
            foreach (var relationship in state.relationships)
            {
                if (relationship.Involves(successor.id)) { relationship.relations = 78f; relationship.trust = 70f; }
                if (relationship.Involves("IND")) relationship.relations = 12f;
            }

            for (int month = 0; month < 36; month++) turns.EndMonth();

            int recognitions = DiplomacySystem.RecognitionCount(state, successor.id);
            Assert.Greater(recognitions, 0,
                "Three years on, a popular breakaway from a widely disliked state had been "
                + "recognised by nobody. A verb the AI can call but never will is the same bug "
                + "as one it cannot call.");
        }

        // ---------- mediation ----------

        Confrontation AWarBetweenOthers()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "RUS",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, "CHN");
            return confrontation;
        }

        // ---------- summits ----------

        [Test]
        public void ASummitTakesMonthsAndThenDelivers()
        {
            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            relationship.relations = 60f;
            float trust = relationship.trust;

            Assert.IsTrue(DiplomacySystem.ConveneSummitBy(state, state.playerCountryId, "CHN"));
            Assert.AreEqual(DiplomacySystem.SummitPreparation, relationship.summitMonthsRemaining);
            Assert.AreEqual(trust, relationship.trust, 0.001f,
                "Announcing a summit delivered its benefit immediately, so the preparation is "
                + "decoration and this is an instant relationship purchase.");

            for (int month = 0; month < DiplomacySystem.SummitPreparation; month++)
                turns.EndMonth();

            Assert.AreEqual(0, relationship.summitMonthsRemaining);
            Assert.Greater(relationship.trust, trust, "The summit met and bought nothing.");
        }

        [Test]
        public void ASummitIsJudgedOnTheMonthItMeetsNotTheMonthItWasCalled()
        {
            // **The whole reason it takes four months.** The world can move
            // underneath a commitment, which is what separates convening a
            // summit from buying a relationship.
            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            relationship.relations = 60f;

            DiplomacySystem.ConveneSummitBy(state, state.playerCountryId, "CHN");

            // It all goes wrong while the delegations are packing.
            relationship.relations = 8f;
            float before = relationship.relations;

            for (int month = 0; month < DiplomacySystem.SummitPreparation; month++)
                turns.EndMonth();

            Assert.AreEqual(0, relationship.summitMonthsRemaining);
            Assert.LessOrEqual(relationship.relations, before,
                "A summit convened in a warm month still paid out after the relationship "
                + "collapsed, so nothing that happens during the preparation matters.");
        }

        [Test]
        public void AStateWeAreFightingWillNotSitDown()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(confrontation);

            Assert.IsFalse(DiplomacySystem.CanConveneSummit(state, state.playerCountryId, "CHN",
                    out string reason),
                "Talks were convened with a state we are in a confrontation with, which is what "
                + "mediation and settlement are for.");
            Assert.IsNotEmpty(reason);
        }

        // ---------- arms control ----------

        [Test]
        public void VerificationIsWhatLetsRivalsSignALimitation()
        {
            // `CAP_VERIFICATION`'s description has always promised "monitoring
            // that lets rivals believe each other", and until this tranche it
            // had almost no read site to make that true.
            var armsControl = new System.Collections.Generic.List<TreatyCommitment>
                { TreatyCommitment.ArmsControl };

            // The regime itself is a separate capability and gates the clause
            // outright (spec 13 §6) — without it, willingness is zero and this
            // would compare nothing against nothing. What is being measured here
            // is what *verification* adds on top of having the regime at all.
            state.PlayerCountry.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = "CAP_ARMSCONTROL",
                source = CapabilitySource.Developed,
                acquired = state.date,
                maturity = 100f
            });

            float blind = DiplomacySystem.TreatyWillingness(
                state, state.playerCountryId, "CHN", armsControl);
            Assert.Greater(blind, 0f,
                "the fixture's state cannot sign a limitation at all, so this measured nothing");

            state.PlayerCountry.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = "CAP_VERIFICATION",
                source = CapabilitySource.Developed,
                acquired = state.date,
                maturity = 100f
            });

            float verified = DiplomacySystem.TreatyWillingness(
                state, state.playerCountryId, "CHN", armsControl);

            Assert.Greater(verified, blind,
                "Being able to verify made no difference to whether a rival would sign a "
                + "limitation, so the capability's whole stated purpose does nothing.");
        }

        [Test]
        public void GoingToWarWithAPartnerVoidsTheLimitation()
        {
            // Priced, never blocked — GDD §18.1's rule that escalation must not
            // hard-gate what an operator may do. The agreement breaks and
            // everyone watching adjusts what our signature is worth.
            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            relationship.relations = 70f;
            relationship.trust = 70f;

            // Held by reference: `GameState.FindTreaty` skips broken treaties, so
            // looking it up again after the escalation returns null rather than
            // the broken agreement we want to inspect.
            var pact = new Treaty
            {
                id = "TEST_ARMS",
                countryA = state.playerCountryId,
                countryB = "CHN",
                commitments = new System.Collections.Generic.List<TreatyCommitment>
                    { TreatyCommitment.ArmsControl },
                signedDate = state.date
            };
            state.treaties.Add(pact);

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(confrontation, "the fixture could not open a confrontation");

            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            Assert.IsTrue(pact.broken,
                "Opening hostilities against a state we had signed a limitation with left the "
                + "agreement standing, so an arms-control treaty constrains nothing.");
            Assert.AreEqual(state.playerCountryId, pact.brokenBy);
        }

        [Test]
        public void AnyGovernmentCanBeSeenToBreakItsWord()
        {
            // `BreakTreaty` was player-only, so no foreign government could ever
            // be *observed* breaking a treaty — and the counter-play layer reads
            // exactly that record.
            var pact = new Treaty
            {
                id = "TEST_FOREIGN",
                countryA = "CHN",
                countryB = "RUS",
                commitments = new System.Collections.Generic.List<TreatyCommitment>
                    { TreatyCommitment.NonAggression },
                signedDate = state.date
            };
            state.treaties.Add(pact);

            Assert.IsTrue(DiplomacySystem.BreakTreatyBy(state, "CHN", "RUS"));

            Assert.IsTrue(pact.broken);
            Assert.AreEqual("CHN", pact.brokenBy,
                "A foreign state broke a treaty and the record blamed somebody else.");

            bool chronicled = false;
            foreach (var entry in state.chronicle)
                if (entry.countryId == "CHN" && entry.text.Contains("broken")) chronicled = true;
            Assert.IsTrue(chronicled,
                "A foreign treaty violation never reached the public record, so nothing that "
                + "reasons from reputation can see it.");
        }

        // ---------- normalisation ----------

        [Test]
        public void ThereMustBeAWarToPutBehindUs()
        {
            Assert.IsFalse(DiplomacySystem.CanNormalise(state, state.playerCountryId, "CHN",
                    out string reason),
                "Normalisation was offered to a state we have never fought, where it is just "
                + "outreach under another name.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void NormalisingIsTheOneThingThatReducesWhatTwoCountriesRemember()
        {
            // `memoryWeight` is read by treaty acceptance, sanctions relief and
            // alliance willingness, and until this verb it only ever
            // accumulated: a pair who fought in 1986 carried it identically in
            // 2020 whatever either did about it. The one-way-value family, in
            // the diplomatic model.
            var relationship = state.FindRelationship(state.playerCountryId, "CHN");
            relationship.AddMemory(state.date, "A war neither has forgotten.", 3f);
            relationship.settlementTruceMonths = 20;

            float memory = relationship.memoryWeight;
            float approval = state.PlayerCountry.governmentApproval;

            Assert.IsTrue(DiplomacySystem.BeginNormalisationBy(state, state.playerCountryId, "CHN"));

            Assert.Less(relationship.memoryWeight, memory,
                "Normalising left the historical memory exactly where it was, so nothing in the "
                + "game can say two countries got over something.");
            Assert.Less(state.PlayerCountry.governmentApproval, approval,
                "Reconciling with a recent enemy cost nothing at home, so it is free and there "
                + "is no reason not to do it the month the guns stop.");
        }

        // ---------- envoys ----------

        [Test]
        public void APostedEnvoyIsWorthWhatTheMinisterIsWorth()
        {
            // The first place the diplomatic minister's quality shows up in a
            // *relationship* rather than in the pillar.
            var official = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            Assert.IsNotNull(official);

            Assert.AreEqual(0f, DiplomacySystem.EnvoyWeight(state, state.playerCountryId, "CHN"), 0.0001f,
                "An unposted minister was already worth something abroad.");

            Assert.IsTrue(DiplomacySystem.AssignEnvoyBy(state, state.playerCountryId, "CHN"));

            official.competence = 20f;
            float weak = DiplomacySystem.EnvoyWeight(state, state.playerCountryId, "CHN");
            official.competence = 95f;
            float strong = DiplomacySystem.EnvoyWeight(state, state.playerCountryId, "CHN");

            Assert.Greater(strong, weak,
                "A capable envoy was worth no more than an incapable one, so who holds the post "
                + "does not matter and the appointment is not a decision.");
            Assert.AreEqual(0f, DiplomacySystem.EnvoyWeight(state, state.playerCountryId, "RUS"), 0.0001f,
                "One posting covered a state the envoy is not in. An envoy who is everywhere is "
                + "a modifier, not an allocation.");
        }

        [Test]
        public void AnEnvoyHoldsTheRelationshipWarmWithoutMonthlyCommandPoints()
        {
            var official = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            official.competence = 95f;
            official.mode = ControlMode.Autonomous;
            DiplomacySystem.AssignEnvoyBy(state, state.playerCountryId, "CHN");

            var posted = state.FindRelationship(state.playerCountryId, "CHN");
            var unposted = state.FindRelationship(state.playerCountryId, "RUS");
            posted.relations = 50f;
            unposted.relations = 50f;

            for (int month = 0; month < 12; month++) turns.EndMonth();

            Assert.Greater(posted.relations, unposted.relations,
                "A year with the foreign minister living in their capital left that relationship "
                + "no better than one nobody visited.");
        }

        [Test]
        public void AnEnvoyCanBeRecalled()
        {
            DiplomacySystem.AssignEnvoyBy(state, state.playerCountryId, "CHN");
            Assert.Greater(DiplomacySystem.EnvoyWeight(state, state.playerCountryId, "CHN"), 0f);

            Assert.IsTrue(DiplomacySystem.AssignEnvoyBy(state, state.playerCountryId, ""));
            Assert.AreEqual(0f, DiplomacySystem.EnvoyWeight(state, state.playerCountryId, "CHN"), 0.0001f,
                "The minister could not be brought home, so the first posting is permanent.");
        }

        [Test]
        public void ABelligerentCannotMediateItsOwnWar()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            Assert.IsFalse(DiplomacySystem.CanMediate(state, state.playerCountryId,
                    confrontation, out string reason),
                "A party to the war was allowed to mediate it.");
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void BothSidesHaveToBeWillingToHaveUsInTheRoom()
        {
            var confrontation = AWarBetweenOthers();

            var withChina = state.FindRelationship(state.playerCountryId, "CHN");
            withChina.relations = 5f;

            Assert.IsFalse(DiplomacySystem.CanMediate(state, state.playerCountryId,
                    confrontation, out string reason),
                "A state one belligerent despises was accepted as an honest broker.");
            StringAssert.Contains("ROOM", reason.ToUpperInvariant());
        }

        [Test]
        public void MediationCanEndSomebodyElsesWar()
        {
            var confrontation = AWarBetweenOthers();

            // Every term in our favour: trusted by both, a strong diplomatic
            // pillar, and two exhausted belligerents.
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(state.playerCountryId)) continue;
                relationship.relations = 92f;
                relationship.trust = 88f;
            }
            state.PlayerCountry.pillars.diplomacy = 95f;
            state.FindCountry("CHN").warExhaustion = 90f;
            state.FindCountry("RUS").warExhaustion = 90f;
            confrontation.momentum = 0f;

            bool settled = false;
            for (int attempt = 0; attempt < 6 && !settled; attempt++)
                settled = DiplomacySystem.OfferMediationBy(state, state.playerCountryId, confrontation);

            Assert.IsTrue(settled,
                "Six offers from a trusted great power to two exhausted belligerents never "
                + "ended the war, so mediation cannot succeed under any conditions.");
            Assert.IsTrue(confrontation.resolved, "It was accepted and the war carried on.");
        }

        [Test]
        public void BeingRefusedIsPublicAndCosts()
        {
            // **Failing has to cost**, or tabling an offer every month and seeing
            // what sticks is the correct play — the same reasoning that prices a
            // lost chamber motion.
            var confrontation = AWarBetweenOthers();

            var withChina = state.FindRelationship(state.playerCountryId, "CHN");
            var withRussia = state.FindRelationship(state.playerCountryId, "RUS");
            withChina.relations = 40f;
            withRussia.relations = 40f;
            state.PlayerCountry.pillars.diplomacy = 1f;
            state.FindCountry("CHN").warExhaustion = 0f;
            state.FindCountry("RUS").warExhaustion = 0f;
            confrontation.momentum = 100f;

            float before = withChina.relations;
            bool settled = DiplomacySystem.OfferMediationBy(state, state.playerCountryId, confrontation);

            Assert.IsFalse(settled, "the fixture's hopeless offer was accepted anyway");
            Assert.Less(withChina.relations, before,
                "Asking two states to stop fighting and being refused in public cost nothing, "
                + "so the correct play is to offer every month until one sticks.");
        }
    }
}

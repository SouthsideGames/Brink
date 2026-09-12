using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The AI reasons from estimates, not truth (GDD Phase 9, spec 06 §7).
    ///
    /// The audit found AI decisions about *other* countries reading those
    /// countries' hidden state directly: the treaty offer tailored off the
    /// target's true pillars, the regional-hegemony path scored off every
    /// neighbour's true military, the opponent model reading true stability,
    /// operations planned from the true garrison, deception mounted off the
    /// rival's own network record, a sponsor reading a rising's exact figures,
    /// and détente screened through the sender's acceptance function. Each is
    /// now held to the same two properties: changing the hidden truth without
    /// changing what the AI has been told does not change the decision, and
    /// changing what it has been told does.
    /// </summary>
    public class AIInformationTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8181);
            state.difficulty = Difficulty.Challenging;
        }

        void Forget(string observerId, string targetId)
            => state.estimates.RemoveAll(e => e.observerId == observerId && e.targetId == targetId);

        void Tell(string observerId, string targetId, IntelDomain domain, float value,
            ConfidenceGrade grade = ConfidenceGrade.Confirmed)
        {
            state.estimates.RemoveAll(e => e.observerId == observerId && e.targetId == targetId && e.domain == domain);
            state.estimates.Add(new IntelEstimate
            {
                observerId = observerId, targetId = targetId, domain = domain,
                reportedValue = value, margin = 2f, confidence = grade, everCollected = true,
            });
        }

        // ---------- treaty offers ----------

        [Test]
        public void TheTreatyOfferFollowsTheEstimate_NotTheHiddenPillar()
        {
            var chn = state.FindCountry("CHN");
            var usa = state.PlayerCountry;
            Forget("CHN", "USA");
            state.FindRelationship("CHN", "USA").SetThreatPerceivedBy("USA", 20f);

            usa.pillars.military = 20f;
            var weakHidden = AISystem.TreatyOfferFor(state, chn, "USA");
            usa.pillars.military = 85f;
            var strongHidden = AISystem.TreatyOfferFor(state, chn, "USA");
            CollectionAssert.AreEqual(weakHidden, strongHidden,
                "the offer changed with a pillar China has no reporting on");

            Tell("CHN", "USA", IntelDomain.Military, 20f);
            var readWeak = AISystem.TreatyOfferFor(state, chn, "USA");
            Tell("CHN", "USA", IntelDomain.Military, 85f);
            var readStrong = AISystem.TreatyOfferFor(state, chn, "USA");
            CollectionAssert.Contains(readWeak, TreatyCommitment.MutualDefense,
                "a partner read as weak is offered a guarantee");
            CollectionAssert.DoesNotContain(readStrong, TreatyCommitment.MutualDefense);
        }

        // ---------- the regional path ----------

        [Test]
        public void NeighbourhoodDominanceReadsEstimates()
        {
            var rus = state.FindCountry("RUS");
            foreach (var other in state.countries) if (other.id != "RUS") Forget("RUS", other.id);

            var kaz = state.FindCountry("KAZ");
            Assume.That(GeographySystem.ReachFactorTo(state, "RUS", "KAZ"), Is.GreaterThanOrEqualTo(0.6f),
                "the fixture needs a genuine neighbour");

            kaz.pillars.military = 10f;
            float weakHidden = AIStrategy.NeighbourhoodDominance(state, rus);
            kaz.pillars.military = 95f;
            float strongHidden = AIStrategy.NeighbourhoodDominance(state, rus);
            Assert.AreEqual(weakHidden, strongHidden, 0.001f,
                "a neighbour's true strength moved Russia's path scoring with no reporting on it");

            Tell("RUS", "KAZ", IntelDomain.Military, 10f);
            float readWeak = AIStrategy.NeighbourhoodDominance(state, rus);
            Tell("RUS", "KAZ", IntelDomain.Military, 95f);
            float readStrong = AIStrategy.NeighbourhoodDominance(state, rus);
            Assert.Greater(readWeak, readStrong, "a neighbour read as weak makes the neighbourhood look more dominated");
        }

        // ---------- the opponent model ----------

        [Test]
        public void ThePredictedPathReadsPoliticalReporting_NotTrueStability()
        {
            var ai = state.FindAI("CHN");
            var usa = state.PlayerCountry;
            Forget("CHN", "USA");
            var model = new OpponentModel { countryId = "USA" };

            usa.stability = 15f;
            var fragileHidden = AIPrediction.InferPath(state, ai, usa, model);
            usa.stability = 85f;
            var stableHidden = AIPrediction.InferPath(state, ai, usa, model);
            Assert.AreEqual(fragileHidden, stableHidden,
                "the inferred path changed with a stability figure China has no reporting on");

            Tell("CHN", "USA", IntelDomain.Political, 15f);
            Assert.AreEqual(StrategicPath.Survival, AIPrediction.InferPath(state, ai, usa, model),
                "a state read as fragile is read as fighting to survive");
            Tell("CHN", "USA", IntelDomain.Political, 85f);
            Assert.AreNotEqual(StrategicPath.Survival, AIPrediction.InferPath(state, ai, usa, model));
        }

        // ---------- operations ----------

        [Test]
        public void TheOperationIsChosenFromTheGarrisonEstimate()
        {
            var chn = state.FindCountry("CHN");
            var ai = state.FindAI("CHN");
            ai.profile.caution = 85f;
            var usa = state.PlayerCountry;
            var target = state.locations.Find(l => l.ownerId == "USA" && l.type != LocationType.Capital);
            Assume.That(target, Is.Not.Null);
            var war = ConfrontationSystem.BeginBy(state, "CHN", "USA",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Forget("CHN", "USA");

            OperationType Choose(int seed) => AISystem.ChooseOperation(state, chn, ai, target, war, 0f, new Random(seed));

            target.garrison = 90f;
            var heavyHidden = Choose(11);
            target.garrison = 5f;
            var lightHidden = Choose(11);
            Assert.AreEqual(heavyHidden, lightHidden,
                "the operation chosen changed with a garrison China has no reporting on");

            // Reported: the choice may now differ, and the perceived garrison does.
            Tell("CHN", "USA", IntelDomain.Military, usa.pillars.military);
            target.garrison = 90f;
            float readHeavy = AISystem.PerceivedGarrison(state, "CHN", target);
            target.garrison = 5f;
            float readLight = AISystem.PerceivedGarrison(state, "CHN", target);
            Assert.Greater(readHeavy, readLight, "confirmed reporting should separate a held position from an empty one");
            Assert.AreEqual(50f, AISystem.PerceivedGarrison(state, "RUS", target), 0.001f,
                "with no reporting the AI plans from a flat prior, not the truth");
        }

        // ---------- deception ----------

        [Test]
        public void SuspectingCollectionComesFromOurOwnSideOfTheFog()
        {
            var chn = state.FindCountry("CHN");
            state.networks.RemoveAll(n => n.ownerId == "USA" && n.targetId == "CHN");
            chn.counterIntel.counterIntelligence = 30f;
            state.FindRelationship("CHN", "USA").relations = 30f;

            Assert.IsFalse(AISystem.SuspectsCollection(state, chn, "USA"), "nothing to suspect");

            // A deep, uncaught network: invisible to a weak service, however deep.
            state.networks.Add(new IntelNetwork { ownerId = "USA", targetId = "CHN", focus = IntelDomain.Military, penetration = 90f });
            Assert.IsFalse(AISystem.SuspectsCollection(state, chn, "USA"),
                "reading the rival's penetration directly was a free mole hunt");

            // Caught: known.
            state.networks[state.networks.Count - 1].compromised = true;
            Assert.IsTrue(AISystem.SuspectsCollection(state, chn, "USA"));

            // Or a strong service assuming it of a cold state, network or none.
            state.networks.RemoveAll(n => n.ownerId == "USA" && n.targetId == "CHN");
            chn.counterIntel.counterIntelligence = 70f;
            Assert.IsTrue(AISystem.SuspectsCollection(state, chn, "USA"));
        }

        // ---------- sponsorship ----------

        [Test]
        public void ARisingIsUnseenWithoutReporting_AndCoarseWithLittle()
        {
            var rising = new Insurgency { support = 42f, strength = 33f, sponsorId = "" };
            Forget("RUS", "IND");

            Assert.Less(InsurgencySystem.PerceivedViability(state, "RUS", rising, "IND"), 0f,
                "a movement nobody has reporting on cannot be found to arm");

            Tell("RUS", "IND", IntelDomain.Political, 50f, ConfidenceGrade.Low);
            float coarseA = InsurgencySystem.PerceivedViability(state, "RUS", rising, "IND");
            rising.support = 48f; rising.strength = 30f;
            float coarseB = InsurgencySystem.PerceivedViability(state, "RUS", rising, "IND");
            Assert.AreEqual(coarseA, coarseB, 0.001f, "poor reporting cannot tell these two risings apart");

            Tell("RUS", "IND", IntelDomain.Political, 50f, ConfidenceGrade.Confirmed);
            rising.support = 42f; rising.strength = 33f;
            float fineA = InsurgencySystem.PerceivedViability(state, "RUS", rising, "IND");
            rising.support = 48f; rising.strength = 30f;
            float fineB = InsurgencySystem.PerceivedViability(state, "RUS", rising, "IND");
            Assert.AreNotEqual(fineA, fineB, "confirmed reporting should separate them");
        }

        // ---------- détente ----------

        [Test]
        public void DetenteIsScreenedThroughReporting_NotTheSendersAcceptanceFunction()
        {
            state.sanctions.Add(new Sanction
            { senderId = "USA", targetId = "RUS", severity = SanctionSeverity.Coercive, imposedDate = state.date });
            var pair = state.FindRelationship("USA", "RUS");
            pair.relations = 5f; pair.trust = 5f;
            pair.SetThreatPerceivedBy("USA", 90f);
            Assume.That(EconomySystem.ReliefWillingness(state, "USA", "RUS"), Is.LessThan(EconomySystem.ReliefThreshold - 40f),
                "the fixture needs a sender who would plainly refuse");

            Forget("RUS", "USA");
            Assert.AreEqual(SettlementOutlook.Unknown, EconomySystem.AssessRelief(state, "USA", "RUS"),
                "with no reporting on the sender, Russia does not know — and may ask and be refused");

            Tell("RUS", "USA", IntelDomain.Diplomatic, 50f, ConfidenceGrade.High);
            Assert.AreEqual(SettlementOutlook.Unlikely, EconomySystem.AssessRelief(state, "USA", "RUS"),
                "good reporting reads a plain refusal as one");
        }
    }
}

using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Finished intelligence (spec 03 §10, spec 25 §5.3).
    ///
    /// Collection used to buy sharper numbers about foreign *capability* and
    /// nothing else, while `AIStrategy.StrategicPath`,
    /// `AIPrediction.OpponentModel` and `EndgameSystem.KnownPreparation` were
    /// computed every month for every government and read by almost nothing.
    /// The claims here are the three the system rests on: it needs collection,
    /// the answer is fixed once given, and a poor service is *plausibly* wrong
    /// rather than noisy.
    /// </summary>
    public class IntelProductTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7714);
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

        CountryState SomeoneElse()
        {
            foreach (var country in state.countries)
                if (!country.isPlayer) return country;
            return null;
        }

        IntelNetwork GiveUsANetwork(string targetId, float penetration)
        {
            var network = new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = targetId,
                focus = IntelDomain.Political,
                penetration = penetration
            };
            state.networks.Add(network);
            return network;
        }

        // ---------- analysis is a product of collection ----------

        [Test]
        public void NothingCanBeAskedWithoutANetwork()
        {
            var target = SomeoneElse();
            Assert.IsFalse(IntelProductSystem.CanCommission(state, state.playerCountryId,
                    target.id, EstimateQuestion.StrategicIntent, out string reason),
                "A service with no collection anywhere answered a question about a foreign "
                + "state. That is a free oracle, not intelligence.");
            Assert.IsNotEmpty(reason, "A refusal the operator cannot see is a broken button.");

            Assert.IsNull(IntelProductSystem.CommissionBy(state, state.playerCountryId,
                    target.id, EstimateQuestion.StrategicIntent),
                "The gate and the verb disagreed — they must be the same function.");
        }

        [Test]
        public void TheShopHasACapacity()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 50f);

            Assert.IsNotNull(IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent));
            Assert.IsNotNull(IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.TreatyReliability));

            Assert.IsFalse(IntelProductSystem.CanCommission(state, state.playerCountryId,
                    target.id, EstimateQuestion.TheirReadOfUs, out _),
                $"More than {IntelProductSystem.MaxOutstanding} assessments ran at once. An "
                + "analytical shop with no capacity limit makes the CP cost the only "
                + "constraint, and CP is not what is scarce here.");
        }

        [Test]
        public void TheSameQuestionIsNotAskedTwiceAtOnce()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 50f);

            IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent);

            Assert.IsFalse(IntelProductSystem.CanCommission(state, state.playerCountryId,
                    target.id, EstimateQuestion.StrategicIntent, out _),
                "The same question was accepted twice while the first was still running, which "
                + "is a way to buy two rolls at one answer.");
        }

        // ---------- it takes time, and it arrives ----------

        [Test]
        public void AnAssessmentTakesMonthsAndThenArrives()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 60f);

            var product = IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent);

            Assert.IsFalse(product.delivered, "It answered instantly.");

            for (int month = 0; month < IntelProductSystem.BaseMonths; month++) turns.EndMonth();

            Assert.IsTrue(product.delivered,
                $"{IntelProductSystem.BaseMonths} months on, the assessment had still not "
                + "landed — so nothing the operator commissions ever comes back.");
            Assert.IsNotEmpty(product.answer, "It arrived with nothing written on it.");
        }

        // ---------- the answer is fixed once given ----------

        [Test]
        public void TheJudgementDoesNotChangeOnceDelivered()
        {
            // A number that flickers on refresh is unusable, and averaging
            // repeated reads would leak the true value. The `MilitaryAdvice`
            // precedent, applied to a written judgement.
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 60f);

            var product = IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.StrategicIntent);
            for (int month = 0; month < IntelProductSystem.BaseMonths; month++) turns.EndMonth();

            string first = product.answer;
            var grade = product.confidence;

            for (int month = 0; month < 6; month++) turns.EndMonth();

            Assert.AreEqual(first, product.answer,
                "The delivered judgement rewrote itself months later.");
            Assert.AreEqual(grade, product.confidence, "The confidence grade drifted after delivery.");
        }

        [Test]
        public void TwoIdenticalWorldsProduceTheIdenticalJudgement()
        {
            // Deterministic per (observer, target, question, month), so reloading
            // cannot shake a different answer loose.
            string Run()
            {
                var world = WorldFactory.CreateDebugWorld(seed: 7714);
                var runTurns = new TurnManager(world);
                SimulationPipeline.Wire(runTurns, world);

                var subject = world.countries.Find(c => !c.isPlayer);
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId,
                    targetId = subject.id,
                    focus = IntelDomain.Political,
                    penetration = 55f
                });

                var product = IntelProductSystem.CommissionBy(world, world.playerCountryId,
                    subject.id, EstimateQuestion.StrategicIntent);
                for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
                    runTurns.EndMonth();
                return product.answer;
            }

            string a = Run();
            string b = Run();

            Assert.IsNotEmpty(a, "the fixture delivered nothing, so this compared two blanks");
            Assert.AreEqual(a, b, "The same commission in the same world gave two answers.");
        }

        // ---------- a poor service is plausibly wrong ----------

        [Test]
        public void GoodCollectionIsRightFarMoreOftenThanNone()
        {
            // The property that makes buying intelligence worth doing. Measured
            // across many commissions rather than one, because a single draw
            // says nothing about a probability.
            int SharpHits(float penetration, ConfidenceGrade grade)
            {
                int correct = 0;
                for (int i = 0; i < 40; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 9000 + i);
                    var runTurns = new TurnManager(world);
                    SimulationPipeline.Wire(runTurns, world);

                    var subject = world.countries.Find(c => !c.isPlayer);
                    world.networks.Add(new IntelNetwork
                    {
                        ownerId = world.playerCountryId,
                        targetId = subject.id,
                        focus = IntelDomain.Political,
                        penetration = penetration
                    });

                    // Pin the grade the analysis is scored against, so this
                    // measures the accuracy curve rather than how fast a network
                    // happens to deepen.
                    var estimate = new IntelEstimate
                    {
                        observerId = world.playerCountryId,
                        targetId = subject.id,
                        domain = IntelDomain.Political,
                        confidence = grade,
                        everCollected = true,
                        asOf = world.date
                    };
                    world.estimates.Add(estimate);

                    var product = IntelProductSystem.CommissionBy(world, world.playerCountryId,
                        subject.id, EstimateQuestion.StrategicIntent);

                    for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
                    {
                        estimate.confidence = grade;   // hold it against collection drift
                        runTurns.EndMonth();
                    }

                    if (product.accurate) correct++;
                }
                return correct;
            }

            int blind = SharpHits(4f, ConfidenceGrade.None);
            int sharp = SharpHits(85f, ConfidenceGrade.Confirmed);

            Assert.Greater(sharp, blind,
                $"A service with deep, confident access was right {sharp}/40 against {blind}/40 "
                + "for one with almost nothing. If collection does not sharpen the judgement, "
                + "there is no reason to buy any.");
        }

        [Test]
        public void AWrongAnswerIsPlausibleRatherThanNoise()
        {
            // **The load-bearing property.** A poor service must return a
            // different defensible conclusion, stated exactly like a good one —
            // noise would be obviously worthless and therefore free to ignore,
            // and an operator who can spot the bad assessments is not being
            // asked to trust anybody.
            var descriptions = new HashSet<string>();
            foreach (StrategicPath path in Enum.GetValues(typeof(StrategicPath)))
                descriptions.Add(AIStrategy.Describe(path));

            int checkedAnswers = 0;
            for (int i = 0; i < 30; i++)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 4400 + i);
                var runTurns = new TurnManager(world);
                SimulationPipeline.Wire(runTurns, world);

                var subject = world.countries.Find(c => !c.isPlayer);
                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId,
                    targetId = subject.id,
                    focus = IntelDomain.Political,
                    penetration = 3f
                });

                var product = IntelProductSystem.CommissionBy(world, world.playerCountryId,
                    subject.id, EstimateQuestion.StrategicIntent);
                for (int month = 0; month < IntelProductSystem.BaseMonths; month++)
                    runTurns.EndMonth();

                if (!product.delivered) continue;
                if (product.answer.StartsWith("No coherent")) continue;

                checkedAnswers++;
                bool namesARealPath = false;
                foreach (string description in descriptions)
                    if (product.answer.Contains(description)) namesARealPath = true;

                Assert.IsTrue(namesARealPath,
                    $"A wrong assessment did not name a real strategy: \"{product.answer}\". "
                    + "It has to be a conclusion the operator could act on and be wrong about.");
            }

            Assert.Greater(checkedAnswers, 10,
                "the fixture delivered almost nothing, so this asserted nothing");
        }

        // ---------- it survives a save ----------

        [Test]
        public void AnAssessmentSurvivesASaveRoundTrip()
        {
            var target = SomeoneElse();
            GiveUsANetwork(target.id, 60f);

            var product = IntelProductSystem.CommissionBy(state, state.playerCountryId,
                target.id, EstimateQuestion.TreatyReliability);
            for (int month = 0; month < IntelProductSystem.BaseMonths; month++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.intelProducts.Find(p =>
                p.IsFor(loaded.playerCountryId, target.id, EstimateQuestion.TreatyReliability));

            Assert.IsNotNull(restored, "The assessment did not survive the save at all.");
            Assert.AreEqual(product.answer, restored.answer,
                "The judgement changed across a save — which is a reload that shakes a "
                + "different answer loose, the thing this game refuses.");
            Assert.AreEqual(product.confidence, restored.confidence);
        }
    }
}

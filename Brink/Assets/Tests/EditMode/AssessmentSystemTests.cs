using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AssessmentSystemTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>Answer every scenario with the same option index.</summary>
        static List<int> AnswerAll(int choice)
        {
            var answers = new List<int>();
            for (int i = 0; i < AssessmentCatalog.Questions.Count; i++)
            {
                int options = AssessmentCatalog.Questions[i].options.Length;
                answers.Add(System.Math.Min(choice, options - 1));
            }
            return answers;
        }

        [Test]
        public void Catalog_MeetsTheDesignedScenarioCount()
        {
            int count = AssessmentCatalog.Questions.Count;
            Assert.GreaterOrEqual(count, 8, "GDD §5 calls for roughly 8-12 scenarios.");
            Assert.LessOrEqual(count, 12);

            foreach (var question in AssessmentCatalog.Questions)
            {
                Assert.IsNotEmpty(question.situation);
                Assert.IsNotEmpty(question.prompt);
                Assert.GreaterOrEqual(question.options.Length, 3, "Each scenario needs real alternatives.");
                foreach (var option in question.options)
                    Assert.IsNotEmpty(option.text);
            }
        }

        [Test]
        public void Evaluate_ProducesACompleteResult()
        {
            var result = AssessmentSystem.Evaluate(AnswerAll(0), seed: 1);

            Assert.IsNotEmpty(result.assignedCountryId);
            Assert.NotNull(WorldFactory.FindProfile(result.assignedCountryId), "Posting must be a real nation.");
            Assert.IsNotEmpty(result.classificationText);
            Assert.IsNotEmpty(result.doctrineText);
            Assert.AreEqual(2, result.traits.Count, "Two national traits are drawn.");
        }

        /// <summary>
        /// Pick, for each scenario, whichever option scores highest on one axis.
        /// Option ordering is deliberately inconsistent across scenarios, so a
        /// profile must be built from what answers mean, not where they sit.
        /// </summary>
        static List<int> AnswersFavoring(System.Func<DoctrineProfile, float> axis)
        {
            var answers = new List<int>();
            foreach (var question in AssessmentCatalog.Questions)
            {
                int best = 0;
                float bestValue = float.MinValue;
                for (int i = 0; i < question.options.Length; i++)
                {
                    float value = axis(question.options[i].scores);
                    if (value > bestValue) { bestValue = value; best = i; }
                }
                answers.Add(best);
            }
            return answers;
        }

        [Test]
        public void DifferentAnswers_ProduceDifferentProfiles()
        {
            var forceful = AssessmentSystem.Evaluate(AnswersFavoring(s => s.force), seed: 7);
            var cooperative = AssessmentSystem.Evaluate(AnswersFavoring(s => s.coalition), seed: 7);

            Assert.AreNotEqual(forceful.doctrineText, cooperative.doctrineText,
                "Different answers must yield a different assessed disposition.");
            Assert.Greater(forceful.doctrine.force, cooperative.doctrine.force);
            Assert.Greater(cooperative.doctrine.coalition, forceful.doctrine.coalition);
        }

        [Test]
        public void OptionOrdering_IsNotPositionallyPredictable()
        {
            // The "aggressive" answer is not always option A — a player cannot
            // game the assessment by always choosing the same position.
            var forcePositions = new HashSet<int>();
            foreach (var question in AssessmentCatalog.Questions)
            {
                int best = 0;
                float bestValue = float.MinValue;
                for (int i = 0; i < question.options.Length; i++)
                {
                    float value = question.options[i].scores.force;
                    if (value > bestValue) { bestValue = value; best = i; }
                }
                forcePositions.Add(best);
            }

            Assert.Greater(forcePositions.Count, 1,
                "The forceful option should not always sit in the same position.");
        }

        [Test]
        public void ControlledRandomness_PreventsASolvedRecipe()
        {
            var answers = AnswerAll(1);
            var a = AssessmentSystem.Evaluate(answers, seed: 11);
            var b = AssessmentSystem.Evaluate(answers, seed: 12);

            bool traitsDiffer = a.traits[0].id != b.traits[0].id || a.traits[1].id != b.traits[1].id;
            bool doctrineDiffers = System.Math.Abs(a.doctrine.force - b.doctrine.force) > 0.001f;

            Assert.IsTrue(traitsDiffer || doctrineDiffers,
                "Identical answers must not always produce an identical outcome (GDD §5).");
        }

        [Test]
        public void SameSeedAndAnswers_AreReproducible()
        {
            var answers = AnswerAll(1);
            var a = AssessmentSystem.Evaluate(answers, seed: 42);
            var b = AssessmentSystem.Evaluate(answers, seed: 42);

            Assert.AreEqual(a.assignedCountryId, b.assignedCountryId);
            Assert.AreEqual(a.traits[0].id, b.traits[0].id);
            Assert.AreEqual(a.doctrine.force, b.doctrine.force);
        }

        [Test]
        public void Scoring_IsNeverExposedAsNumbers()
        {
            var result = AssessmentSystem.Evaluate(AnswerAll(0), seed: 3);

            foreach (var text in new[] { result.classificationText, result.doctrineText })
            {
                foreach (char c in text)
                    Assert.IsFalse(char.IsDigit(c),
                        $"Assessment feedback must be in-universe prose, not scores: \"{text}\"");
            }
        }

        [Test]
        public void Posting_MatchesTheOperatorProfile()
        {
            // A profile weighted hard toward intelligence and secrecy should be
            // posted to a nation whose intelligence institutions are strong.
            var doctrine = new DoctrineProfile { intelligenceAffinity = 30f, secrecy = 10f };
            string posting = AssessmentSystem.AssignPosting(doctrine);
            var profile = WorldFactory.FindProfile(posting);

            Assert.NotNull(profile);
            Assert.GreaterOrEqual(profile.intelligence, 70f,
                "An intelligence-weighted operator belongs at a capable service.");
        }

        [Test]
        public void Posting_ReflectsEconomicVersusMilitaryWeighting()
        {
            string economic = AssessmentSystem.AssignPosting(new DoctrineProfile { economyAffinity = 40f });
            string military = AssessmentSystem.AssignPosting(new DoctrineProfile { militaryAffinity = 40f });

            var economicProfile = WorldFactory.FindProfile(economic);
            var militaryProfile = WorldFactory.FindProfile(military);

            Assert.GreaterOrEqual(economicProfile.economy, 80f);
            Assert.GreaterOrEqual(militaryProfile.military, 70f);
        }

        [Test]
        public void StartingPriority_FollowsTheStrongestAffinity()
        {
            Assert.AreEqual(NationalPriority.Prosperity,
                AssessmentSystem.PriorityFor(new DoctrineProfile { economyAffinity = 20f }));
            Assert.AreEqual(NationalPriority.Influence,
                AssessmentSystem.PriorityFor(new DoctrineProfile { diplomacyAffinity = 20f }));
            Assert.AreEqual(NationalPriority.Security,
                AssessmentSystem.PriorityFor(new DoctrineProfile { militaryAffinity = 20f }));
            Assert.AreEqual(NationalPriority.Cohesion,
                AssessmentSystem.PriorityFor(new DoctrineProfile { governmentAffinity = 20f }));
        }

        [Test]
        public void Traits_CarryBothStrengthAndVulnerability()
        {
            var baseline = WorldFactory.CreateWorld(500, "USA");
            var withTrait = WorldFactory.CreateWorld(500, "USA");

            var result = new AssessmentResult
            {
                assignedCountryId = "USA",
                startingPriority = NationalPriority.Security,
                traits = new List<NationalTrait>
                {
                    new NationalTrait { id = "TRAIT_MARTIAL", name = "Martial Tradition", description = "x" }
                }
            };
            AssessmentSystem.ApplyToWorld(withTrait, result);

            // Strength: war support and military rise. Vulnerability: diplomacy falls.
            Assert.Greater(withTrait.PlayerCountry.warSupport, baseline.PlayerCountry.warSupport);
            Assert.Greater(withTrait.PlayerCountry.pillars.military, baseline.PlayerCountry.pillars.military);
            Assert.Less(withTrait.PlayerCountry.pillars.diplomacy, baseline.PlayerCountry.pillars.diplomacy);
        }

        [Test]
        public void OpaqueStateTrait_TradesTrustForCounterintelligence()
        {
            var baseline = WorldFactory.CreateWorld(501, "USA");
            var world = WorldFactory.CreateWorld(501, "USA");

            AssessmentSystem.ApplyToWorld(world, new AssessmentResult
            {
                assignedCountryId = "USA",
                traits = new List<NationalTrait>
                {
                    new NationalTrait { id = "TRAIT_OPAQUE", name = "Opaque State", description = "x" }
                }
            });

            Assert.Greater(world.PlayerCountry.counterIntel.counterIntelligence,
                           baseline.PlayerCountry.counterIntel.counterIntelligence);

            float baselineTrust = baseline.FindRelationship("USA", "IND").trust;
            Assert.Less(world.FindRelationship("USA", "IND").trust, baselineTrust,
                "Opacity should cost us trust abroad.");
        }

        [Test]
        public void ApplyToWorld_SetsPriorityAndRecordsHistory()
        {
            var state = WorldFactory.CreateWorld(502, "IND");
            var result = AssessmentSystem.Evaluate(AnswerAll(1), seed: 5);
            result.assignedCountryId = "IND";
            result.startingPriority = NationalPriority.Influence;

            AssessmentSystem.ApplyToWorld(state, result);

            Assert.AreEqual(NationalPriority.Influence, state.PlayerCountry.government.leader.priority);
            Assert.NotNull(state.assessment);
            Assert.Greater(state.chronicle.Count, 1, "The posting should be entered into the record.");
        }

        [Test]
        public void PlayerCanBePostedToAnyAuthoredNation()
        {
            foreach (var profile in WorldFactory.Profiles)
            {
                var state = WorldFactory.CreateWorld(600, profile.id);

                Assert.AreEqual(profile.id, state.playerCountryId);
                Assert.IsTrue(state.PlayerCountry.isPlayer);
                Assert.AreEqual(profile.displayName, state.PlayerCountry.displayName);
                Assert.AreEqual(5, state.cabinet.Count, "Every posting gets a full Cabinet.");
                Assert.AreEqual(WorldFactory.Profiles.Length - 1, state.aiStates.Count,
                    "Every other state is AI-driven.");
                Assert.IsNull(state.FindAI(profile.id), "The player's own nation is never AI-driven.");
            }
        }

        [Test]
        public void PostingToAnotherNation_UsesThatNationsInstitutions()
        {
            var state = WorldFactory.CreateWorld(601, "CHN");

            Assert.AreEqual(GovernmentType.DominantPartyState, state.PlayerCountry.government.type);
            Assert.IsFalse(state.PlayerCountry.government.IsElective);
            Assert.AreEqual("Minister of National Defense", state.FindOfficial(Pillar.Military).title,
                "Cabinet titles follow the nation you serve.");
        }

        [Test]
        public void AssessmentResult_SurvivesSaveRoundTrip()
        {
            var state = WorldFactory.CreateWorld(603, "RUS");
            var result = AssessmentSystem.Evaluate(AnswerAll(0), seed: 9);
            result.assignedCountryId = "RUS";
            AssessmentSystem.ApplyToWorld(state, result);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.NotNull(loaded.assessment);
            Assert.AreEqual(result.assignedCountryId, loaded.assessment.assignedCountryId);
            Assert.AreEqual(result.traits.Count, loaded.assessment.traits.Count);
            Assert.AreEqual(result.traits[0].id, loaded.assessment.traits[0].id);
            Assert.AreEqual(result.doctrine.force, loaded.assessment.doctrine.force);
            Assert.AreEqual("RUS", loaded.playerCountryId);
        }

        [Test]
        public void GeneratedWorld_IsFullyPlayableAndSimulates()
        {
            var result = AssessmentSystem.Evaluate(AnswerAll(1), seed: 21);
            var state = WorldFactory.CreateWorld(700, result.assignedCountryId);
            AssessmentSystem.ApplyToWorld(state, result);
            ProgressionSystem.CaptureYearSnapshot(state);

            var turns = new TurnManager(state);
            turns.ResolveMonth += CabinetSystem.MonthlyAct;
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;
            turns.ResolveMonth += ProgressionSystem.MonthlyXP;
            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);

            for (int i = 0; i < 60; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            Assert.AreEqual(60, state.date.MonthsSince(state.startDate));
            Assert.AreEqual(5, state.evaluations.Count);
            Assert.Greater(state.PlayerCountry.economy.gdp, 0f);
        }
    }
}

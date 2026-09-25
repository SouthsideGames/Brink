using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class ProgressionSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1010);
            turns = new TurnManager(state);
            // NARROW PIPELINE: the economy tick and monthly XP only — the AI,
            // military, intelligence, regime and crisis ticks are omitted
            // because every case on this fixture runs at most one year and
            // asserts ProgressionSystem's own XP, evaluation-component and
            // skill-effect arithmetic from preconditions it sets directly,
            // while the one decade-long run in the file
            // (TenYearRun_ProducesEvaluationsAndRemainsDeterministic) does make
            // a claim about the world and therefore uses the real pipeline.
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += ProgressionSystem.MonthlyXP;
            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- XP ----------

        [Test]
        public void XP_AccumulatesAndRaisesLevel()
        {
            Assert.AreEqual(1, state.strategistLevel);
            ProgressionSystem.AwardXP(state, ProgressionSystem.XPForLevel(2), "test");
            Assert.AreEqual(2, state.strategistLevel);
            Assert.Greater(ProgressionSystem.XPForLevel(3), ProgressionSystem.XPForLevel(2),
                "Levels should cost progressively more.");
        }

        [Test]
        public void XP_AwardedForMeaningfulDecisionsAcrossPillars()
        {
            state.commandPoints.current = 40;
            state.politicalCapital = 20f;

            int start = state.strategistXP;
            EconomySystem.ImposeSanctions(state, turns, "RUS", SanctionSeverity.Pressure);
            int afterEconomic = state.strategistXP;

            GovernmentSystem.InstitutionalReform(state);
            int afterGovernment = state.strategistXP;

            CrisisSystem.Resolve(state, CrisisSystem.Trigger(state, "MARKET_PANIC"), 0);
            int afterCrisis = state.strategistXP;

            Assert.Greater(afterEconomic, start, "Economic statecraft should earn XP.");
            Assert.Greater(afterGovernment, afterEconomic, "Governing should earn XP.");
            Assert.Greater(afterCrisis, afterGovernment, "Crisis management should earn XP.");
        }

        [Test]
        public void MonthlyAdministration_EarnsBaselineXP()
        {
            int before = state.strategistXP;
            for (int i = 0; i < 6; i++) turns.EndMonth();
            Assert.Greater(state.strategistXP, before);
        }

        // ---------- annual evaluation ----------

        [Test]
        public void Evaluation_RunsAtYearEndAndAwardsSkillPoints()
        {
            Assert.AreEqual(0, state.evaluations.Count);
            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.AreEqual(1, state.evaluations.Count);
            var record = state.evaluations[0];
            Assert.AreEqual(state.startDate.year, record.year);
            Assert.AreEqual(record.skillPointsAwarded, state.skillPoints);
        }

        [Test]
        public void Evaluation_GradesOnTrajectoryNotAbsoluteStrength()
        {
            // A weak country that improves should out-grade a strong one that decays.
            EvaluationGrade GradeFor(bool improving)
            {
                var sim = WorldFactory.CreateDebugWorld(55);
                var simTurns = new TurnManager(sim);
                simTurns.YearEnded += year => ProgressionSystem.EvaluateYear(sim, year);
                var player = sim.PlayerCountry;

                if (improving)
                {
                    player.pillars.military = 20f; player.pillars.economy = 20f;
                    ProgressionSystem.CaptureYearSnapshot(sim);
                    player.pillars.military = 34f; player.pillars.economy = 32f;
                    player.stability += 10f; player.governmentApproval += 10f;
                    player.economy.gdp *= 1.08f;
                }
                else
                {
                    player.pillars.military = 90f; player.pillars.economy = 90f;
                    ProgressionSystem.CaptureYearSnapshot(sim);
                    player.pillars.military = 74f; player.pillars.economy = 72f;
                    player.stability -= 15f; player.governmentApproval -= 15f;
                    player.economy.gdp *= 0.92f;
                }

                return ProgressionSystem.EvaluateYear(sim, sim.date.year).grade;
            }

            Assert.Greater((int)GradeFor(true), (int)GradeFor(false),
                "A rising weak power should out-grade a declining strong one (GDD §25.2).");
        }

        [Test]
        public void Evaluation_CreditsPerformanceUnderAdversity()
        {
            float ScoreUnder(bool atWar)
            {
                var sim = WorldFactory.CreateDebugWorld(56);
                ProgressionSystem.CaptureYearSnapshot(sim);
                if (atWar)
                {
                    var confrontation = ConfrontationSystem.BeginBy(sim, "USA", "CHN",
                        ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(sim, confrontation, EscalationState.TotalWar, "USA");
                }
                return ProgressionSystem.EvaluateYear(sim, sim.date.year).score;
            }

            Assert.Greater(ScoreUnder(true), ScoreUnder(false),
                "Holding the same line during a war should score better than during calm.");
        }

        // ---------- difficulty adjustment is visible (spec 07 §3) ----------

        [Test]
        public void DifficultyBonus_IsTheScoreDifferenceAndIsRecorded()
        {
            EvaluationRecord At(Difficulty difficulty)
            {
                var sim = WorldFactory.CreateDebugWorld(58);
                sim.difficulty = difficulty;
                ProgressionSystem.CaptureYearSnapshot(sim);
                return ProgressionSystem.EvaluateYear(sim, sim.date.year);
            }

            var standard = At(Difficulty.Standard);
            foreach (Difficulty difficulty in System.Enum.GetValues(typeof(Difficulty)))
            {
                var record = At(difficulty);
                float bonus = ProgressionSystem.DifficultyScoreBonus(difficulty);
                Assert.AreEqual(standard.score + bonus, record.score, 0.0001f,
                    "The stated difficulty bonus must be exactly what the score gained.");
                Assert.AreEqual(bonus, record.difficultyBonus, "the record stores the bonus applied");
                Assert.AreEqual(difficulty.ToString(), record.difficultyApplied);
                StringAssert.Contains($"+{bonus:F0}", ProgressionSystem.DifficultyLine(record));
            }
            Assert.AreEqual(0f, ProgressionSystem.DifficultyScoreBonus(Difficulty.Standard));
            Assert.AreEqual(3f, ProgressionSystem.DifficultyScoreBonus(Difficulty.Challenging));
            Assert.AreEqual(6f, ProgressionSystem.DifficultyScoreBonus(Difficulty.Ruthless));
        }

        [Test]
        public void DifficultyNotes_StateEveryBonusFromTheSharedDefinition()
        {
            string note = ProgressionSystem.DifficultyChoiceNote();
            foreach (Difficulty difficulty in System.Enum.GetValues(typeof(Difficulty)))
                StringAssert.Contains($"+{ProgressionSystem.DifficultyScoreBonus(difficulty):F0} at {difficulty}", note);
            StringAssert.Contains("never their statistics", note);
            StringAssert.Contains("above 100", ProgressionSystem.PositionScoreNote());
        }

        [Test]
        public void DifficultyLine_IsAbsentForRecordsThatNeverStoredIt()
        {
            var legacy = new EvaluationRecord { year = 1984, score = 70f };
            Assert.IsNull(ProgressionSystem.DifficultyLine(legacy),
                "An old save's evaluation must not be given a bonus nobody recorded.");

            var sim = WorldFactory.CreateDebugWorld(59);
            sim.difficulty = Difficulty.Ruthless;
            ProgressionSystem.CaptureYearSnapshot(sim);
            ProgressionSystem.EvaluateYear(sim, sim.date.year);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(sim));
            Assert.AreEqual("Ruthless", loaded.evaluations[0].difficultyApplied);
            Assert.AreEqual(6f, loaded.evaluations[0].difficultyBonus);
        }

        [Test]
        public void Evaluation_CrisisHandlingCounts()
        {
            float ScoreWithCrises(int faced, int resolved)
            {
                var sim = WorldFactory.CreateDebugWorld(57);
                ProgressionSystem.CaptureYearSnapshot(sim);
                sim.crisesFacedThisYear = faced;
                sim.crisesResolvedThisYear = resolved;
                return ProgressionSystem.EvaluateYear(sim, sim.date.year).score;
            }

            Assert.Greater(ScoreWithCrises(4, 4), ScoreWithCrises(4, 0),
                "Resolving crises should score better than leaving them unhandled.");
        }

        [Test]
        public void Evaluation_ExposesComponentScores()
        {
            for (int i = 0; i < 12; i++) turns.EndMonth();
            var record = state.evaluations[0];

            Assert.Greater(record.trajectoryScore, 0f);
            Assert.Greater(record.economyScore, 0f);
            Assert.Greater(record.stabilityScore, 0f);
            Assert.Greater(record.positionScore, 0f);
            Assert.Greater(record.crisisScore, 0f);
            Assert.Greater(record.initiativeScore, 0f);
            Assert.IsNotEmpty(record.summary, "The grade must never be a black box.");
        }

        [Test]
        public void Evaluation_ResetsTheBaselineForTheNextYear()
        {
            for (int i = 0; i < 12; i++) turns.EndMonth();
            Assert.AreEqual(state.date.year, state.yearSnapshot.year,
                "A new snapshot should anchor the new year.");
            Assert.AreEqual(0, state.crisesFacedThisYear);
        }

        [Test]
        public void Grades_MapToRisingSkillPointAwards()
        {
            // Assert the shape, not the magic numbers. The payout curve is
            // deliberately flatter than the grade scale and gets retuned whenever
            // the bands move (they are two jobs welded to one number — see
            // ProgressionSystem.GradeFor); pinning S to a literal made this test
            // fail for a change that was working exactly as intended.
            int previous = -1;
            foreach (var grade in new[]
            {
                EvaluationGrade.F, EvaluationGrade.D, EvaluationGrade.C,
                EvaluationGrade.B, EvaluationGrade.A, EvaluationGrade.S
            })
            {
                int points = ProgressionSystem.SkillPointsFor(grade);
                Assert.GreaterOrEqual(points, previous, $"{grade} pays less than the grade below it.");
                previous = points;
            }

            Assert.AreEqual(0, ProgressionSystem.SkillPointsFor(EvaluationGrade.F),
                "A failed year earns nothing.");
            Assert.Greater(ProgressionSystem.SkillPointsFor(EvaluationGrade.S),
                ProgressionSystem.SkillPointsFor(EvaluationGrade.B),
                "The top grade has to be worth reaching for.");

            Assert.AreEqual(EvaluationGrade.S, ProgressionSystem.GradeFor(95f));
            Assert.AreEqual(EvaluationGrade.F, ProgressionSystem.GradeFor(10f));
        }

        // ---------- skill trees ----------

        [Test]
        public void Catalog_CoversAllFivePillarsWithRisingCosts()
        {
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                Assert.Greater(SkillCatalog.ForPillar(pillar).Count, 0, $"{pillar} tree is empty.");

            foreach (var node in SkillCatalog.Nodes)
            {
                Assert.AreEqual(node.tier, node.cost, "Cost should rise with tier (GDD §25.3).");
                Assert.IsNotEmpty(node.description);
                foreach (var prerequisite in node.prerequisites)
                    Assert.NotNull(SkillCatalog.Find(prerequisite), $"Dangling prerequisite {prerequisite}.");
            }
        }

        [Test]
        public void Catalog_ContainsCrossPillarHybrids()
        {
            int hybrids = 0;
            foreach (var node in SkillCatalog.Nodes)
                if (node.isHybrid) hybrids++;
            Assert.Greater(hybrids, 0, "Cross-pillar prerequisites should unlock hybrids (GDD §25.3).");
        }

        [Test]
        public void Unlock_RequiresPointsAndPrerequisites()
        {
            state.skillPoints = 0;
            Assert.IsFalse(ProgressionSystem.Unlock(state, "GOV_1"), "No points, no unlock.");

            state.skillPoints = 10;
            Assert.IsFalse(ProgressionSystem.Unlock(state, "GOV_2"), "Prerequisite not met.");
            Assert.IsTrue(ProgressionSystem.Unlock(state, "GOV_1"));
            Assert.IsTrue(ProgressionSystem.Unlock(state, "GOV_2"));
            Assert.IsFalse(ProgressionSystem.Unlock(state, "GOV_1"), "Cannot unlock twice.");

            Assert.AreEqual(10 - 1 - 2, state.skillPoints);
        }

        [Test]
        public void Hybrid_RequiresBothTrees()
        {
            state.skillPoints = 99;
            ProgressionSystem.Unlock(state, "MIL_1");
            ProgressionSystem.Unlock(state, "MIL_2");
            ProgressionSystem.Unlock(state, "MIL_3");

            Assert.IsFalse(ProgressionSystem.Unlock(state, "HYB_DETERRENCE"),
                "The hybrid needs its diplomatic prerequisite too.");

            ProgressionSystem.Unlock(state, "DIP_1");
            ProgressionSystem.Unlock(state, "DIP_2");
            Assert.IsTrue(ProgressionSystem.Unlock(state, "HYB_DETERRENCE"));
        }

        // ---------- skill effects are operator capability, not national power ----------

        [Test]
        public void Skills_DoNotGrantRawNationalPower()
        {
            var before = state.PlayerCountry;
            float pillarsBefore = before.pillars.military + before.pillars.economy
                                  + before.pillars.intelligence + before.pillars.diplomacy
                                  + before.pillars.government;
            float treasuryBefore = before.resources.treasury;
            float forceBefore = before.military.TotalPower;

            state.skillPoints = 99;
            foreach (var node in SkillCatalog.Nodes)
                ProgressionSystem.Unlock(state, node.id);

            float pillarsAfter = before.pillars.military + before.pillars.economy
                                 + before.pillars.intelligence + before.pillars.diplomacy
                                 + before.pillars.government;

            Assert.AreEqual(pillarsBefore, pillarsAfter, 0.001f,
                "Skills must not raise national statistics (GDD §25.3).");
            Assert.AreEqual(treasuryBefore, before.resources.treasury, 0.001f);
            Assert.AreEqual(forceBefore, before.military.TotalPower, 0.001f);
        }

        [Test]
        public void CommandCapacity_RaisesMonthlyCommandPoints()
        {
            int CommandPointsAfterTwoMonths(bool skilled)
            {
                var sim = WorldFactory.CreateDebugWorld(1010);
                var simTurns = new TurnManager(sim);
                if (skilled)
                {
                    sim.skillPoints = 5;
                    ProgressionSystem.Unlock(sim, "GOV_1"); // +1 CP/month
                }
                simTurns.EndMonth();
                simTurns.EndMonth();
                return sim.commandPoints.current;
            }

            Assert.Greater(CommandPointsAfterTwoMonths(true), CommandPointsAfterTwoMonths(false));
        }

        [Test]
        public void DelegationBandwidth_RaisesMonthlyInfluence()
        {
            state.skillPoints = 10;
            ProgressionSystem.Unlock(state, "GOV_1");
            ProgressionSystem.Unlock(state, "GOV_2");
            ProgressionSystem.Unlock(state, "GOV_3"); // +1 Influence/month

            state.influence = 0;
            turns.EndMonth();
            Assert.AreEqual(GameState.InfluencePerMonth + 1, state.influence);
        }

        [Test]
        public void OperationEfficiency_ReducesCommandPointCost()
        {
            state.commandPoints.current = 40;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
            state.commandPoints.current = 40;
            ConfrontationSystem.SetEscalation(state, turns, confrontation, EscalationState.LimitedConflict);

            state.commandPoints.current = 20;
            ConfrontationSystem.LaunchOperation(state, turns, confrontation, "CONTESTED_LANE",
                OperationType.Assault, new OperationDirective());
            int costWithoutSkill = 20 - state.commandPoints.current;

            state.skillPoints = 10;
            ProgressionSystem.Unlock(state, "MIL_1"); // operations cost 1 less

            state.commandPoints.current = 20;
            ConfrontationSystem.LaunchOperation(state, turns, confrontation, "CONTESTED_LANE",
                OperationType.Assault, new OperationDirective());
            int costWithSkill = 20 - state.commandPoints.current;

            Assert.Less(costWithSkill, costWithoutSkill);
            Assert.GreaterOrEqual(costWithSkill, 1, "Costs never fall below one CP.");
        }

        [Test]
        public void AnalyticalPrecision_TightensOurEstimatesOnly()
        {
            float MarginFor(bool skilled)
            {
                var sim = WorldFactory.CreateDebugWorld(88);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
                sim.commandPoints.current = 20;
                if (skilled)
                {
                    sim.skillPoints = 5;
                    ProgressionSystem.Unlock(sim, "INT_1");
                }
                IntelligenceSystem.EstablishNetwork(sim, simTurns, "CHN", IntelDomain.Military);
                sim.FindNetwork("USA", "CHN").penetration = 60f;
                simTurns.EndMonth();
                return sim.FindEstimate("USA", "CHN", IntelDomain.Military).margin;
            }

            Assert.Less(MarginFor(true), MarginFor(false),
                "Analytical training should narrow our own error bars.");
        }

        [Test]
        public void SanctionPrecision_ReducesOurBlowbackOnly()
        {
            state.commandPoints.current = 40;
            EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Severe);
            float blowbackBefore = EconomySystem.SanctionBlowbackFor(state, "USA");

            state.skillPoints = 10;
            ProgressionSystem.Unlock(state, "ECO_1");
            ProgressionSystem.Unlock(state, "ECO_2"); // -35% blowback

            Assert.Less(EconomySystem.SanctionBlowbackFor(state, "USA"), blowbackBefore);

            // An AI sanctioning someone is unaffected by our training.
            EconomySystem.ImposeSanctionsBy(state, "RUS", "IND", SanctionSeverity.Severe);
            float aiBlowback = EconomySystem.SanctionBlowbackFor(state, "RUS");
            Assert.Greater(aiBlowback, 0f);
        }

        [Test]
        public void TreatyPersuasion_ImprovesReceptionOfOurProposals()
        {
            var relationship = state.FindRelationship("USA", "IND");
            relationship.relations = 60f; relationship.trust = 60f; relationship.strategicAlignment = 60f;
            var commitments = new List<TreatyCommitment> { TreatyCommitment.IntelligenceSharing };

            float before = DiplomacySystem.TreatyWillingness(state, "IND", commitments);

            state.skillPoints = 10;
            ProgressionSystem.Unlock(state, "DIP_1");
            ProgressionSystem.Unlock(state, "DIP_2");

            Assert.Greater(DiplomacySystem.TreatyWillingness(state, "IND", commitments), before);
        }

        [Test]
        public void SettlementLeverage_MakesOpponentsAcceptSooner()
        {
            state.commandPoints.current = 40;
            var confrontation = ConfrontationSystem.Begin(state, turns, "USA", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var chn = state.FindCountry("CHN");
            chn.warSupport = 40f;
            chn.pillars.government = 40f;
            confrontation.defenderWarExhaustion = 30f;
            confrontation.momentum = 10f;

            bool acceptedBefore = ConfrontationSystem.OpponentWouldAccept(state, confrontation);

            state.skillPoints = 20;
            ProgressionSystem.Unlock(state, "MIL_1");
            ProgressionSystem.Unlock(state, "MIL_2");
            ProgressionSystem.Unlock(state, "MIL_3");
            ProgressionSystem.Unlock(state, "DIP_1");
            ProgressionSystem.Unlock(state, "DIP_2");
            ProgressionSystem.Unlock(state, "HYB_DETERRENCE");

            Assert.IsTrue(ConfrontationSystem.OpponentWouldAccept(state, confrontation),
                "Coercive credibility should bring opponents to terms sooner.");
            Assert.IsFalse(acceptedBefore, "Precondition: they were still resisting.");
        }

        // ---------- strategic verbs: instruments you cannot otherwise reach ----------

        [Test]
        public void ForwardPosture_RequiresForwardBasing()
        {
            state.commandPoints.current = 40;
            Assert.IsFalse(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Forward),
                "Standing forward needs basing arrangements we have not built.");
            Assert.AreEqual(MilitaryPosture.Peacetime, state.PlayerCountry.military.posture);

            state.skillPoints = 99;
            ProgressionSystem.Unlock(state, "MIL_1");
            ProgressionSystem.Unlock(state, "MIL_2");
            ProgressionSystem.Unlock(state, "MIL_BASING");

            Assert.IsTrue(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Forward));
        }

        [Test]
        public void TransformativePrograms_RequireAStrategicIndustrialBase()
        {
            state.commandPoints.current = 40;
            state.PlayerCountry.resources.treasury = 6000f;

            Assert.IsFalse(MilitarySystem.BeginProcurement(state, turns,
                ForceBranch.Naval, MilitarySystem.ProgramScale.Transformative));
            Assert.IsTrue(MilitarySystem.BeginProcurement(state, turns,
                ForceBranch.Naval, MilitarySystem.ProgramScale.Major),
                "Ordinary programs remain available to everyone.");

            state.PlayerCountry.military.programs.Clear();
            state.skillPoints = 99;
            ProgressionSystem.Unlock(state, "ECO_1");
            ProgressionSystem.Unlock(state, "ECO_2");
            ProgressionSystem.Unlock(state, "ECO_STRATEGIC");

            Assert.IsTrue(MilitarySystem.BeginProcurement(state, turns,
                ForceBranch.Naval, MilitarySystem.ProgramScale.Transformative));
        }

        [Test]
        public void ExistentialSanctions_RequireFinancialReach()
        {
            state.commandPoints.current = 40;
            Assert.IsFalse(EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Existential));
            Assert.IsTrue(EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Severe),
                "Lesser measures remain available.");

            state.sanctions.Clear();
            state.skillPoints = 99;
            foreach (var node in new[] { "ECO_1", "ECO_2", "ECO_STRATEGIC", "DIP_1", "DIP_2", "ECO_EXISTENTIAL" })
                ProgressionSystem.Unlock(state, node);

            Assert.IsTrue(EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Existential));
        }

        [Test]
        public void DeceptionOperations_RequireADeepCoverProgram()
        {
            state.commandPoints.current = 40;
            Assert.IsFalse(IntelligenceSystem.RunCovertOperation(state, turns, null,
                CovertOperation.Deception, 1f));
            Assert.AreEqual(0f, state.PlayerCountry.counterIntel.deceptionStrength);

            state.skillPoints = 99;
            ProgressionSystem.Unlock(state, "INT_1");
            ProgressionSystem.Unlock(state, "INT_2");
            ProgressionSystem.Unlock(state, "INT_DEEPCOVER");

            Assert.IsTrue(IntelligenceSystem.RunCovertOperation(state, turns, null,
                CovertOperation.Deception, 1f));
            Assert.Greater(state.PlayerCountry.counterIntel.deceptionStrength, 0f);
        }

        [Test]
        public void StrategicVerbs_StillGrantNoNationalPower()
        {
            var player = state.PlayerCountry;
            float pillarsBefore = player.pillars.military + player.pillars.economy
                                  + player.pillars.intelligence + player.pillars.diplomacy
                                  + player.pillars.government;

            state.skillPoints = 999;
            foreach (var node in SkillCatalog.Nodes)
                ProgressionSystem.Unlock(state, node.id);

            float pillarsAfter = player.pillars.military + player.pillars.economy
                                 + player.pillars.intelligence + player.pillars.diplomacy
                                 + player.pillars.government;

            Assert.AreEqual(pillarsBefore, pillarsAfter, 0.001f,
                "Unlocking every skill, including the verbs, must change no national statistic.");
        }

        // ---------- persistence and reset ----------

        [Test]
        public void Progression_SurvivesSaveRoundTrip()
        {
            state.skillPoints = 10;
            ProgressionSystem.Unlock(state, "GOV_1");
            ProgressionSystem.AwardXP(state, 450, "test");
            for (int i = 0; i < 12; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(state.strategistXP, loaded.strategistXP);
            Assert.AreEqual(state.strategistLevel, loaded.strategistLevel);
            Assert.AreEqual(state.skillPoints, loaded.skillPoints);
            Assert.AreEqual(state.unlockedSkills.Count, loaded.unlockedSkills.Count);
            Assert.IsTrue(loaded.HasSkill("GOV_1"));
            Assert.AreEqual(state.evaluations.Count, loaded.evaluations.Count);
            Assert.AreEqual(state.evaluations[0].grade, loaded.evaluations[0].grade);
            Assert.AreEqual(ProgressionSystem.EffectValue(state, SkillEffect.CommandCapacity),
                            ProgressionSystem.EffectValue(loaded, SkillEffect.CommandCapacity));
        }

        [Test]
        public void FullReset_ErasesAllProgression()
        {
            state.skillPoints = 10;
            ProgressionSystem.Unlock(state, "GOV_1");
            ProgressionSystem.AwardXP(state, 1000, "test");

            // A full reset builds a brand new world (GDD §5.1).
            var fresh = WorldFactory.CreateDebugWorld(999);

            Assert.AreEqual(0, fresh.strategistXP);
            Assert.AreEqual(1, fresh.strategistLevel);
            Assert.AreEqual(0, fresh.skillPoints);
            Assert.AreEqual(0, fresh.unlockedSkills.Count);
            Assert.AreEqual(0, fresh.evaluations.Count);
        }

        [Test]
        public void TenYearRun_ProducesEvaluationsAndRemainsDeterministic()
        {
            string Run(int seed)
            {
                var sim = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(sim);

                // A decade-long run makes a claim about how the world behaves,
                // so it must use the real pipeline. The hand-copied list this
                // replaced had drifted by a dozen systems; a test that measures
                // a different game than the one that ships is worse than no
                // test, because it is trusted. `Wire` also attaches the
                // year-end evaluation, so it is not registered separately here.
                SimulationPipeline.Wire(simTurns, sim);

                for (int i = 0; i < 120; i++)
                {
                    while (sim.HasOpenCrisis) CrisisSystem.Resolve(sim, sim.activeCrises[0], 0);
                    simTurns.EndMonth();
                }

                Assert.AreEqual(10, sim.evaluations.Count, "Ten years, ten evaluations.");
                Assert.Greater(sim.strategistXP, 0);
                return SaveSystem.ToJson(sim);
            }

            Assert.AreEqual(Run(3690), Run(3690));
        }
    }
}

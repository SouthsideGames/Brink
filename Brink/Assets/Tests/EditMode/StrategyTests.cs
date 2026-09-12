using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class StrategyTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 712);
            MandateSystem.Assign(state);
            state.influence = GameState.InfluenceCap;
        }

        [Test]
        public void OldPostingLazilyGetsStrategicPlan()
        {
            state.mandate.strategy = null;
            var plan = StrategySystem.Ensure(state);
            Assert.NotNull(plan);
            Assert.AreEqual(StrategicDoctrine.Balanced, plan.doctrine);
        }

        [Test]
        public void FirstDoctrineIsFreeRevisionCostsInfluenceButNeverInitiative()
        {
            int before = state.influence;
            int initiative = state.initiativesThisYear;
            Assert.IsTrue(StrategySystem.SetDoctrine(state, StrategicDoctrine.Prosperity));
            Assert.AreEqual(before, state.influence);
            Assert.AreEqual(initiative, state.initiativesThisYear);
            Assert.IsTrue(StrategySystem.SetDoctrine(state, StrategicDoctrine.Deterrence));
            Assert.AreEqual(before - StrategySystem.DoctrineRevisionInfluence, state.influence);
            Assert.AreEqual(initiative, state.initiativesThisYear);
        }

        [Test]
        public void DoctrineSteersOnlyAutonomousOfficials()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Deterrence);
            var military = state.PlayerCountry.FindOfficial(Pillar.Military);
            var economy = state.PlayerCountry.FindOfficial(Pillar.Economy);
            var diplomacy = state.PlayerCountry.FindOfficial(Pillar.Diplomacy);
            military.mode = ControlMode.Autonomous;
            economy.mode = ControlMode.Autonomous;
            diplomacy.mode = ControlMode.Directed;
            diplomacy.directiveId = "DIP_OUTREACH";

            StrategyCabinetBridge.Prepare(state);

            Assert.AreEqual(MilitaryAdvice.PrepareForWar, military.directiveId);
            Assert.AreEqual("ECO_AUSTERITY", economy.directiveId);
            Assert.AreEqual("DIP_OUTREACH", diplomacy.directiveId, "Standing strategy must not overwrite an explicit order.");
        }

        [Test]
        public void CountryPolicyIsActuallyCountrySpecificAndNotInitiative()
        {
            var policies = StrategySystem.AvailablePolicies(state);
            Assert.AreEqual(1, policies.Length);
            Assert.AreEqual("USA", policies[0].countryId);
            Assert.IsFalse(StrategySystem.SetPolicy(state, "CHN_INDUSTRIAL_SECURITY"));
            int initiative = state.initiativesThisYear;
            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));
            Assert.AreEqual(initiative, state.initiativesThisYear);
        }

        [Test]
        public void PlayerObjectivesAreBoundedStandingAndNotAnInitiativeFarm()
        {
            int xp = state.strategistXP;
            int initiative = state.initiativesThisYear;
            for (int i = 0; i < StrategySystem.MaxObjectives; i++)
                Assert.IsTrue(StrategySystem.AddObjective(state, "Goal " + i,
                    new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));
            Assert.IsFalse(StrategySystem.AddObjective(state, "Too many",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));

            StrategySystem.MonthlyUpdate(state);
            Assert.AreEqual(xp, state.strategistXP);
            Assert.AreEqual(initiative, state.initiativesThisYear);
            foreach (var objective in state.mandate.strategy.objectives)
            {
                Assert.IsTrue(objective.achieved);
                Assert.IsTrue(objective.everAchieved);
            }

            state.PlayerCountry.stability = 0f;
            StrategySystem.MonthlyUpdate(state);
            foreach (var objective in state.mandate.strategy.objectives)
            {
                Assert.IsFalse(objective.achieved, "Standing objectives must become unmet again when the world moves away from the target.");
                Assert.IsTrue(objective.everAchieved, "First attainment remains part of the record.");
            }
        }

        [Test]
        public void StandingStrategyReportingDoesNotPretendItWasMinisterialJudgement()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Prosperity);
            StrategyCabinetBridge.Prepare(state);
            CabinetSystem.MonthlyAct(state);
            StrategySystem.ClarifyCabinetReport(state);

            bool found = false;
            foreach (var line in state.cabinetReport)
            {
                if (line.pillar != Pillar.Economy) continue;
                found = true;
                Assert.IsFalse(line.ownJudgement);
                StringAssert.Contains("standing strategy", line.summary);
            }
            Assert.IsTrue(found);
        }

        [Test]
        public void ObjectivesCannotUseBaselineDependentMandateConditions()
        {
            Assert.IsFalse(StrategySystem.AddObjective(state, "Exploit mandate baseline",
                new MandateObjective { kind=MandateObjectiveKind.GdpGrowthAtLeast, threshold=1f, text="Grow." }));
            Assert.IsFalse(StrategySystem.AddObjective(state, "Exploit original ground",
                new MandateObjective { kind=MandateObjectiveKind.HoldOriginalGround, text="Hold." }));
        }

        [Test]
        public void StrategyPersistsOnMandateReissue()
        {
            StrategySystem.SetDoctrine(state, StrategicDoctrine.Influence);
            var before = state.mandate.strategy;
            MandateSystem.Reissue(state, "test administration");
            Assert.AreSame(before, state.mandate.strategy);
            Assert.AreEqual(StrategicDoctrine.Influence, state.mandate.strategy.doctrine);
        }
    }
}
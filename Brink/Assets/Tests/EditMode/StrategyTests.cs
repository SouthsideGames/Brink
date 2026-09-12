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
        public void FirstDoctrineIsFreeRevisionCostsInfluence()
        {
            int before = state.influence;
            Assert.IsTrue(StrategySystem.SetDoctrine(state, StrategicDoctrine.Prosperity));
            Assert.AreEqual(before, state.influence);
            Assert.IsTrue(StrategySystem.SetDoctrine(state, StrategicDoctrine.Deterrence));
            Assert.AreEqual(before - StrategySystem.DoctrineRevisionInfluence, state.influence);
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

            Assert.AreEqual("MIL_READINESS", military.directiveId);
            Assert.AreEqual("ECO_AUSTERITY", economy.directiveId);
            Assert.AreEqual("DIP_OUTREACH", diplomacy.directiveId, "Standing strategy must not overwrite an explicit order.");
        }

        [Test]
        public void CountryPolicyIsActuallyCountrySpecific()
        {
            var policies = StrategySystem.AvailablePolicies(state);
            Assert.AreEqual(1, policies.Length);
            Assert.AreEqual("USA", policies[0].countryId);
            Assert.IsFalse(StrategySystem.SetPolicy(state, "CHN_INDUSTRIAL_SECURITY"));
            Assert.IsTrue(StrategySystem.SetPolicy(state, "USA_ALLIANCE_FIRST"));
        }

        [Test]
        public void PlayerObjectivesHaveNoRewardAndAreBounded()
        {
            int xp = state.strategistXP;
            for (int i = 0; i < StrategySystem.MaxObjectives; i++)
                Assert.IsTrue(StrategySystem.AddObjective(state, "Goal " + i,
                    new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));
            Assert.IsFalse(StrategySystem.AddObjective(state, "Too many",
                new MandateObjective { kind=MandateObjectiveKind.StabilityAtLeast, threshold=1f, text="Stability at 1." }));
            StrategySystem.MonthlyUpdate(state);
            Assert.AreEqual(xp, state.strategistXP, "Self-authored goals define success; they are not an XP farm.");
            foreach (var objective in state.mandate.strategy.objectives) Assert.IsTrue(objective.achieved);
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
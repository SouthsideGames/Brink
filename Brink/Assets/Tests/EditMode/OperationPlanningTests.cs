using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class OperationPlanningTests
    {
        GameState state;
        Confrontation confrontation;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 713);
            MandateSystem.Assign(state);
            var target = state.countries.Find(c => !c.isPlayer);
            confrontation = new Confrontation
            {
                id = "PLAN_TEST_WAR",
                initiatorId = state.playerCountryId,
                defenderId = target.id,
                escalation = EscalationState.LimitedConflict,
                startDate = state.date
            };
            state.confrontations.Add(confrontation);
        }

        [Test]
        public void CreatingPlanChangesIntentOnly()
        {
            float treasury = state.PlayerCountry.resources.treasury;
            int cp = state.commandPoints.current;
            float momentum = confrontation.momentum;

            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Northern Pressure");

            Assert.NotNull(plan);
            Assert.AreEqual("Northern Pressure", plan.title);
            Assert.AreEqual(treasury, state.PlayerCountry.resources.treasury);
            Assert.AreEqual(cp, state.commandPoints.current);
            Assert.AreEqual(momentum, confrontation.momentum);
            Assert.AreEqual(0, confrontation.operations.Count);
        }

        [Test]
        public void PlanPersistsUnderPostingStrategyAndIsBounded()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Campaign");
            Assert.AreSame(plan, StrategySystem.Ensure(state).operationPlans[0]);
            Assert.AreSame(plan, OperationPlanningSystem.For(state, confrontation.id));
        }

        [Test]
        public void ExecutionReconciliationOnlyAdvancesMatchingNextStep()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Campaign");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId);
            Assert.NotNull(target);

            OperationType type = OperationType.Assault;
            if (!OperationCatalog.CanOrder(state, state.playerCountryId, target, type, out _))
                Assert.Ignore("Debug-world target cannot accept an assault in this fixture.");

            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, type));
            var step = plan.steps[0];
            OperationPlanningSystem.RecordExecution(state, confrontation.id,
                new OperationRecord { date=state.date, locationId=target.id, operationType=OperationType.AirStrike.ToString() });
            Assert.IsFalse(step.completed);

            OperationPlanningSystem.RecordExecution(state, confrontation.id,
                new OperationRecord { date=state.date, locationId=target.id, operationType=type.ToString() });
            Assert.IsTrue(step.completed);
            Assert.IsNull(OperationPlanningSystem.Next(state, confrontation.id));
        }

        [Test]
        public void DirectiveIsCopiedNotAliased()
        {
            var source = new OperationDirective
            {
                speedPriority = 80f,
                casualtyTolerance = 25f,
                civilianRiskLimit = 10f,
                territorialIntent = TerritorialIntent.Degrade,
                escalationLimit = EscalationState.LimitedConflict
            };
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Limited War", source);
            source.casualtyTolerance = 99f;

            Assert.AreEqual(25f, plan.directive.casualtyTolerance);
            Assert.AreEqual(TerritorialIntent.Degrade, plan.directive.territorialIntent);
            Assert.AreEqual(EscalationState.LimitedConflict, plan.directive.escalationLimit);
        }
    }
}
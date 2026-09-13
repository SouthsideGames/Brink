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
        public void PlanPersistsUnderPostingStrategyAndOldNullCollectionRepairsLazily()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Campaign");
            Assert.AreSame(plan, StrategySystem.Ensure(state).operationPlans[0]);
            Assert.AreSame(plan, OperationPlanningSystem.For(state, confrontation.id));

            StrategySystem.Ensure(state).operationPlans = null;
            Assert.IsNull(OperationPlanningSystem.For(state, confrontation.id));
            Assert.NotNull(StrategySystem.Ensure(state).operationPlans);
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
            Assert.IsTrue(OperationPlanningSystem.IsNext(state, confrontation.id, target.id, type));

            // Records are shaped exactly as MilitarySystem files them: the type is
            // upper-cased and the attacker is named. The first version of this
            // test built mixed-case records with no attacker, and passed while no
            // real operation could ever complete a step.
            OperationPlanningSystem.RecordExecution(state, confrontation.id,
                new OperationRecord { date=state.date, locationId=target.id, attackerId=state.playerCountryId,
                    operationType=OperationType.AirStrike.ToString().ToUpperInvariant() });
            Assert.IsFalse(step.completed, "a different verb at the planned location must not advance the plan");

            OperationPlanningSystem.RecordExecution(state, confrontation.id,
                new OperationRecord { date=state.date, locationId=target.id, attackerId=confrontation.defenderId,
                    operationType=type.ToString().ToUpperInvariant() });
            Assert.IsFalse(step.completed, "the enemy carrying out our planned verb at our planned target is not us carrying out our plan");

            OperationPlanningSystem.RecordExecution(state, confrontation.id,
                new OperationRecord { date=state.date, locationId=target.id, attackerId=state.playerCountryId,
                    operationType=type.ToString().ToUpperInvariant() });
            Assert.IsTrue(step.completed);
            Assert.IsNull(OperationPlanningSystem.Next(state, confrontation.id));
        }

        [Test]
        public void ARealOperationThroughTheCommandPathCompletesThePlannedStep()
        {
            var turns = new TurnManager(state);
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Campaign");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Assault, out _));
            Assert.NotNull(target, "the debug world must offer one assaultable enemy location for this test to mean anything");
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Assault));

            state.commandPoints.current = 20;
            var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation, target.id,
                OperationType.Assault, OperationPlanningSystem.DirectiveFor(state, confrontation.id));
            Assert.NotNull(record, "the real command path refused the order; the fixture is not testing execution");
            Assert.IsFalse(plan.steps[0].completed, "launching alone must not complete the step; the monthly reconcile does");

            OperationPlanningSystem.MonthlyReconcile(state);
            Assert.IsTrue(plan.steps[0].completed, "a real operation record must complete the matching planned step");
            Assert.AreEqual(confrontation.operations.Count, plan.reconciledOperationCount);

            OperationPlanningSystem.MonthlyReconcile(state);
            Assert.AreEqual(1, plan.steps.Count, "re-reconciling the same diary must not touch the plan again");
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
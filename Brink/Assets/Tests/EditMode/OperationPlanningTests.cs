using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;
using UnityEngine.UIElements;

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

        [Test]
        public void StandingOrderExecutesOnlyTheNextStepThroughTheRealCommandPath()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Advance");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Raid, out _));
            Assert.NotNull(target);
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Raid));
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.SuppressDefenses));
            Assert.IsTrue(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));
            int cost = ConfrontationSystem.OperationCostFor(state, confrontation, OperationType.Raid);

            state.commandPoints.current = 0;
            Assert.IsTrue(turns.EndMonth());

            Assert.AreEqual(1, confrontation.operations.FindAll(o => o.attackerId == state.playerCountryId).Count,
                "a standing order must attempt at most one player step per month");
            Assert.AreEqual(state.commandPoints.baselinePerMonth - cost, state.commandPoints.current,
                "the operation bypassed the normal Command Point price after refresh");
            Assert.IsFalse(plan.steps[0].completed,
                "execution should still be reconciled from the authoritative diary, not marked optimistically");

            Assert.IsTrue(turns.EndMonth());
            Assert.IsTrue(plan.steps[0].completed);
            Assert.AreEqual(2, confrontation.operations.FindAll(o => o.attackerId == state.playerCountryId).Count);

            Assert.IsTrue(turns.EndMonth());
            Assert.IsTrue(plan.steps[1].completed);
            Assert.IsFalse(plan.standingOrder, "a completed plan kept an empty authorization active");
            Assert.AreEqual(2, confrontation.operations.FindAll(o => o.attackerId == state.playerCountryId).Count);
            CollectionAssert.AreEqual(new[] { "RAID", "SUPPRESSDEFENSES" },
                confrontation.operations.FindAll(o => o.attackerId == state.playerCountryId).ConvertAll(o => o.operationType));
        }

        [Test]
        public void APlanWithoutAStandingOrderNeverExecutesItself()
        {
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Intent only");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Assault, out _));
            Assert.NotNull(target);
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Assault));

            turns.EndMonth();

            Assert.IsFalse(plan.standingOrder);
            Assert.AreEqual(0, confrontation.operations.FindAll(o => o.attackerId == state.playerCountryId).Count);
        }

        [Test]
        public void BlockedStandingOrderWaitsWithoutSpendingCommandPoints()
        {
            var turns = new TurnManager(state);
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Hold");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Assault, out _));
            Assert.NotNull(target);
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Assault));
            Assert.IsTrue(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));
            state.commandPoints.current = 0;

            var record = OperationPlanningSystem.ExecuteStandingOrder(state, turns);

            Assert.IsNull(record);
            Assert.AreEqual(0, state.commandPoints.current);
            Assert.AreEqual(0, confrontation.operations.Count);
            Assert.IsFalse(plan.steps[0].completed);
            Assert.IsTrue(plan.standingOrder, "a temporary refusal should not silently cancel the authorization");
        }

        [Test]
        public void StandingOrderRespectsItsEscalationCeilingBeforeSpending()
        {
            var turns = new TurnManager(state);
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Do not widen",
                new OperationDirective { escalationLimit = EscalationState.Crisis });
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Assault, out _));
            Assert.NotNull(target);
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Assault));
            Assert.IsTrue(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));
            confrontation.escalation = EscalationState.Crisis;
            state.commandPoints.current = 10;

            Assert.IsNull(OperationPlanningSystem.ExecuteStandingOrder(state, turns));
            Assert.AreEqual(10, state.commandPoints.current);
            Assert.AreEqual(EscalationState.Crisis, confrontation.escalation);
            Assert.AreEqual(0, confrontation.operations.Count);
            Assert.IsTrue(plan.standingOrder);
        }

        [Test]
        public void ManualOrderAlsoChecksItsEscalationCeilingBeforeSpending()
        {
            var turns = new TurnManager(state);
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Assault, out _));
            Assert.NotNull(target);
            confrontation.escalation = EscalationState.Crisis;
            state.commandPoints.current = 10;

            var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                target.id, OperationType.Assault,
                new OperationDirective { escalationLimit = EscalationState.Crisis });

            Assert.IsNull(record);
            Assert.AreEqual(10, state.commandPoints.current,
                "the ordinary command path charged an operation it then refused");
            Assert.AreEqual(EscalationState.Crisis, confrontation.escalation);
            Assert.AreEqual(0, confrontation.operations.Count);
        }

        [Test]
        public void StandingOrderPersistsAndOldPlansRemainManual()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Advance");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Raid, out _));
            Assert.NotNull(target);
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Raid));
            Assert.IsTrue(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(OperationPlanningSystem.For(loaded, confrontation.id).standingOrder);

            string legacyJson = SaveSystem.ToJson(state);
            const string field = "\"standingOrder\": true,";
            StringAssert.Contains(field, legacyJson);
            legacyJson = legacyJson.Replace(field, "");
            var legacy = SaveSystem.FromJson(legacyJson);
            Assert.IsFalse(OperationPlanningSystem.For(legacy, confrontation.id).standingOrder,
                "a save from before standing orders did not retain the default-manual behaviour");
            Assert.AreEqual(1, OperationPlanningSystem.For(legacy, confrontation.id).steps.Count);
        }

        [Test]
        public void ABlockedStandingOrderExplainsWhyItIsWaiting()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Hold");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Raid, out _));
            Assert.NotNull(target);

            StringAssert.Contains("Add an incomplete operation", OperationPlanningSystem.StandingOrderIssueBlockReason(state, confrontation.id));
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Raid));
            Assert.IsTrue(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));
            state.PlayerCountry.government.type = GovernmentType.ParliamentaryRepublic;
            state.authorizedPillarMask = 0;

            StringAssert.Contains("Military authority", OperationPlanningSystem.StandingOrderPendingReason(state, confrontation.id));
            StringAssert.Contains("PENDING — MILITARY AUTHORITY", OperationPlanningSystem.StatusText(state, confrontation.id));
            Assert.IsTrue(plan.standingOrder, "a blocked order should remain cancellable rather than silently disappearing");
        }

        [Test]
        public void TheIssueControlAndCommandIndexShowTheSameBlockedReason()
        {
            OperationPlanningSystem.Create(state, confrontation.id, "Hold");
            var panel = new OperationPlanningPanel(null);
            panel.Build(state, confrontation);

            Button issue = null;
            panel.Root.Query<Button>().ForEach(b => { if (b.text == "ISSUE STANDING ORDER") issue = b; });
            Assert.NotNull(issue);
            Assert.IsFalse(issue.enabledSelf);
            Assert.AreEqual("Add an incomplete operation to the campaign plan.", issue.tooltip);
            bool visible = false;
            panel.Root.Query<Label>().ForEach(l => visible |= l.text.Contains("STANDING ORDER UNAVAILABLE — ADD AN INCOMPLETE OPERATION"));
            Assert.IsTrue(visible, "the disabled control hid its reason in a hover-only tooltip");

            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Raid, out _));
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Raid));
            state.PlayerCountry.government.type = GovernmentType.ParliamentaryRepublic;
            state.authorizedPillarMask = 0;
            var entry = ActionCatalog.All(state).Find(e => e.label == "Issue a standing order");

            Assert.NotNull(entry);
            Assert.IsFalse(entry.available);
            Assert.AreEqual("Military authority is required.", entry.blockedReason);
            panel.Build(state, confrontation);
            issue = null;
            panel.Root.Query<Button>().ForEach(b => { if (b.text == "ISSUE STANDING ORDER") issue = b; });
            Assert.IsFalse(issue.enabledSelf);
            Assert.AreEqual(entry.blockedReason, issue.tooltip);

            OperationPlanningSystem.For(state, confrontation.id).standingOrder = true;
            panel.Build(state, confrontation);
            string panelText = "";
            panel.Root.Query<Label>().ForEach(l => panelText += l.text + "\n");
            StringAssert.Contains("STANDING ORDER REMAINS AUTHORIZED BUT WILL WAIT — MILITARY AUTHORITY IS REQUIRED.", panelText);
            StringAssert.DoesNotContain("THE NEXT STEP WILL ATTEMPT", panelText);
        }

        [Test]
        public void AResolvedConfrontationClearsItsObsoleteStandingOrder()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Advance");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Raid, out _));
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Raid));
            Assert.IsTrue(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));

            confrontation.resolved = true;
            OperationPlanningSystem.MonthlyReconcile(state);

            Assert.IsFalse(plan.standingOrder);
        }

        [Test]
        public void StandingOrderCannotBypassConstitutionalAuthority()
        {
            var plan = OperationPlanningSystem.Create(state, confrontation.id, "Advance");
            var target = state.locations.Find(l => l.ownerId == confrontation.defenderId
                && OperationCatalog.CanOrder(state, state.playerCountryId, l, OperationType.Raid, out _));
            Assert.NotNull(target);
            Assert.IsTrue(OperationPlanningSystem.AddStep(state, confrontation.id, target.id, OperationType.Raid));
            state.PlayerCountry.government.type = GovernmentType.ParliamentaryRepublic;
            state.authorizedPillarMask = 0;

            Assert.IsFalse(OperationPlanningSystem.SetStandingOrder(state, confrontation.id, true));
            Assert.IsFalse(plan.standingOrder);
        }
    }
}

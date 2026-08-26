using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class TutorialSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 9900);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
            TutorialSystem.Begin(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Catalog_TeachesTheThingsAPlayerCannotPlayWithout()
        {
            var ids = new HashSet<string>();
            foreach (var step in TutorialSystem.Steps)
            {
                Assert.IsTrue(ids.Add(step.id), $"Duplicate orientation step {step.id}.");
                Assert.IsNotEmpty(step.title);
                Assert.IsNotEmpty(step.body);
                Assert.IsNotEmpty(step.instruction);
                Assert.NotNull(step.isSatisfied);
            }

            // The five things the GDD says a new operator must understand.
            CollectionAssert.Contains(ids, "COMMAND_POINTS");
            CollectionAssert.Contains(ids, "DELEGATION");
            CollectionAssert.Contains(ids, "INTELLIGENCE");
            CollectionAssert.Contains(ids, "THE_MONTH");
            CollectionAssert.Contains(ids, "CRISIS");
        }

        [Test]
        public void Orientation_StartsAtTheFirstStep()
        {
            Assert.IsTrue(state.tutorial.active);
            Assert.IsFalse(state.tutorial.completed);

            // YOUR_POST first, deliberately: a player who thinks they are the
            // head of state reads the next election as the end of their game
            // (GDD §5, the four-reports tranche).
            Assert.AreEqual("YOUR_POST", TutorialSystem.CurrentStep(state).id);
        }

        [Test]
        public void ReadOnlySteps_AdvanceOnAcknowledgement()
        {
            Assert.AreEqual("YOUR_POST", TutorialSystem.CurrentStep(state).id);
            TutorialSystem.Evaluate(state);
            Assert.AreEqual("BRIEFING", TutorialSystem.CurrentStep(state).id,
                "An informational step should advance when acknowledged.");
        }

        [Test]
        public void ActionSteps_WaitForTheOperatorToActuallyAct()
        {
            TutorialSystem.Evaluate(state); // past YOUR_POST
            TutorialSystem.Evaluate(state); // past BRIEFING
            Assert.AreEqual("COMMAND_POINTS", TutorialSystem.CurrentStep(state).id);

            // Acknowledging does nothing while the action is outstanding.
            TutorialSystem.Evaluate(state);
            Assert.AreEqual("COMMAND_POINTS", TutorialSystem.CurrentStep(state).id,
                "The step must wait until Command Points are actually spent.");

            turns.SpendCommandPoints(2, "orientation");
            TutorialSystem.Evaluate(state);
            Assert.AreEqual("DELEGATION", TutorialSystem.CurrentStep(state).id);
        }

        [Test]
        public void DelegationStep_CompletesOnAControlModeChange()
        {
            AdvanceTo("DELEGATION");

            TutorialSystem.Evaluate(state);
            Assert.AreEqual("DELEGATION", TutorialSystem.CurrentStep(state).id);

            CabinetSystem.SetMode(state, state.FindOfficial(Pillar.Economy), ControlMode.Directed);
            TutorialSystem.Evaluate(state);

            Assert.AreEqual("INTELLIGENCE", TutorialSystem.CurrentStep(state).id);
        }

        [Test]
        public void IntelligenceStep_CompletesOnEstablishingANetwork()
        {
            AdvanceTo("INTELLIGENCE");

            TutorialSystem.Evaluate(state);
            Assert.AreEqual("INTELLIGENCE", TutorialSystem.CurrentStep(state).id);

            state.commandPoints.current = 40;
            IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
            TutorialSystem.Evaluate(state);

            Assert.AreEqual("THE_MONTH", TutorialSystem.CurrentStep(state).id);
        }

        [Test]
        public void MonthStep_CompletesOnEndingAMonth()
        {
            AdvanceTo("THE_MONTH");

            TutorialSystem.Evaluate(state);
            Assert.AreEqual("THE_MONTH", TutorialSystem.CurrentStep(state).id);

            turns.EndMonth();
            TutorialSystem.Evaluate(state);

            Assert.AreEqual("CRISIS", TutorialSystem.CurrentStep(state).id);
        }

        [Test]
        public void Orientation_CompletesAndStaysCompleted()
        {
            for (int i = 0; i < 40 && TutorialSystem.CurrentStep(state) != null; i++)
            {
                SatisfyCurrentStep();
                TutorialSystem.Evaluate(state);
            }

            Assert.IsTrue(state.tutorial.completed);
            Assert.IsFalse(state.tutorial.active);
            Assert.IsNull(TutorialSystem.CurrentStep(state));

            // Further evaluation must not restart or throw.
            TutorialSystem.Evaluate(state);
            Assert.IsTrue(state.tutorial.completed);
        }

        [Test]
        public void Orientation_CanBeDismissedAtAnyPoint()
        {
            TutorialSystem.Skip(state);

            Assert.IsFalse(state.tutorial.active);
            Assert.IsTrue(state.tutorial.completed);
            Assert.IsNull(TutorialSystem.CurrentStep(state),
                "A dismissed orientation must not reappear.");
        }

        [Test]
        public void Orientation_NeverBlocksTheGame()
        {
            // Every action remains available while orientation is running.
            state.commandPoints.current = 40;
            Assert.IsTrue(turns.EndMonth(), "Ending the month must work during orientation.");
            Assert.IsTrue(IntelligenceSystem.EstablishNetwork(state, turns, "RUS", IntelDomain.Political));
            Assert.IsTrue(state.tutorial.active, "Orientation is still running, and did not interfere.");
        }

        [Test]
        public void Orientation_SurvivesSaveRoundTrip()
        {
            AdvanceTo("INTELLIGENCE");
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.IsTrue(loaded.tutorial.active);
            Assert.AreEqual(state.tutorial.stepIndex, loaded.tutorial.stepIndex);
            Assert.AreEqual("INTELLIGENCE", TutorialSystem.CurrentStep(loaded).id);
            Assert.AreEqual(state.tutorial.completedStepIds.Count, loaded.tutorial.completedStepIds.Count);
        }

        [Test]
        public void ExistingSaves_WithoutOrientation_AreUnaffected()
        {
            var plain = WorldFactory.CreateDebugWorld(4321);
            Assert.IsFalse(plain.tutorial.active, "A debug world starts without orientation.");
            Assert.IsNull(TutorialSystem.CurrentStep(plain));

            var plainTurns = new TurnManager(plain);
            Assert.IsTrue(plainTurns.EndMonth());
        }

        // ---------- helpers ----------

        void AdvanceTo(string stepId)
        {
            for (int i = 0; i < 20; i++)
            {
                var step = TutorialSystem.CurrentStep(state);
                if (step == null || step.id == stepId) return;
                SatisfyCurrentStep();
                TutorialSystem.Evaluate(state);
            }
            Assert.Fail($"Could not reach orientation step {stepId}.");
        }

        /// <summary>Do whatever the current step is asking for.</summary>
        void SatisfyCurrentStep()
        {
            var step = TutorialSystem.CurrentStep(state);
            if (step == null) return;

            switch (step.id)
            {
                case "COMMAND_POINTS":
                    state.commandPoints.current = 40;
                    turns.SpendCommandPoints(1, "orientation");
                    break;
                case "DELEGATION":
                    CabinetSystem.SetMode(state, state.FindOfficial(Pillar.Military), ControlMode.Directed);
                    break;
                case "INTELLIGENCE":
                    state.commandPoints.current = 40;
                    IntelligenceSystem.EstablishNetwork(state, turns, "CHN", IntelDomain.Military);
                    break;
                case "THE_MONTH":
                    while (state.HasOpenCrisis)
                        CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                    turns.EndMonth();
                    break;
            }
        }
    }
}

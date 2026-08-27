using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Optional strategic directives (GDD §29): suggested by circumstance,
    /// judged by the mandate's evaluator, rewarded when done, harmless when not.
    /// </summary>
    public class StandingDirectiveTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 9191);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void Months(int n)
        {
            for (int i = 0; i < n; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }
        }

        [Test]
        public void CircumstancesSuggestADirective_AndNeverMoreThanTwo()
        {
            state.PlayerCountry.resources.energy = 30f;     // eligible: energy
            state.PlayerCountry.stability = 40f;            // eligible: stability
            state.PlayerCountry.governmentApproval = 30f;   // eligible: approval
            Months(14);

            Assert.GreaterOrEqual(state.standingDirectives.Count, 1, "A year of obvious need suggested nothing.");
            Assert.LessOrEqual(StandingDirectiveSystem.Standing(state).Count, StandingDirectiveSystem.MaxStanding,
                "More than two standing directives reads as a quest log.");
            Assert.IsTrue(state.notifications.Exists(n => n.title == "STRATEGIC DIRECTIVE"),
                "The suggestion never reached the terminal.");
        }

        [Test]
        public void ADirectiveMet_PaysXPAndCountsAsInitiative()
        {
            var directive = new StandingDirective
            {
                id = "TEST", title = "Test", source = "The test",
                objective = new MandateObjective { kind = MandateObjectiveKind.StabilityAtLeast, threshold = 10f, text = "x" },
                issued = state.date, monthsRemaining = 12, rewardXP = 120
            };
            state.standingDirectives.Add(directive);
            int xp = state.strategistXP;
            int initiatives = state.initiativesThisYear;

            StandingDirectiveSystem.MonthlyUpdate(state);

            Assert.IsTrue(directive.completed);
            Assert.AreEqual(xp + 120, state.strategistXP);
            Assert.Greater(state.initiativesThisYear, initiatives, "Completing a directive is initiative.");
            Assert.AreEqual(1, state.directivesCompletedThisYear);
        }

        [Test]
        public void ADirectiveIgnored_LapsesWithoutCost()
        {
            var directive = new StandingDirective
            {
                id = "TEST", title = "Test", source = "The test",
                objective = new MandateObjective { kind = MandateObjectiveKind.StabilityAtLeast, threshold = 100f, text = "x" },
                issued = state.date, monthsRemaining = 1, rewardXP = 120
            };
            state.standingDirectives.Add(directive);
            float approval = state.PlayerCountry.governmentApproval;
            float stability = state.PlayerCountry.stability;

            StandingDirectiveSystem.MonthlyUpdate(state);

            Assert.IsTrue(directive.expired);
            Assert.AreEqual(approval, state.PlayerCountry.governmentApproval, 0.001f, "Optional means optional.");
            Assert.AreEqual(stability, state.PlayerCountry.stability, 0.001f);
            Assert.AreEqual(0, StandingDirectiveSystem.Standing(state).Count);
        }

        [Test]
        public void DirectivesSurviveASave()
        {
            state.PlayerCountry.resources.energy = 30f;
            Months(8);
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(state.standingDirectives.Count, loaded.standingDirectives.Count);
            if (state.standingDirectives.Count > 0)
                Assert.AreEqual(state.standingDirectives[0].objective.kind, loaded.standingDirectives[0].objective.kind);
        }
    }
}

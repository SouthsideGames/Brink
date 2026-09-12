using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The order the world resolves in, in one place.
    ///
    /// This existed twice: once in `GameController.Attach` for the real game and
    /// once, hand-copied, inside the validation harness. The two drifted, and the
    /// failure mode is silent and expensive — a system wired only into
    /// `GameController` runs in play but is **absent from every decade-long
    /// validation run**, so the harness reports a green, balanced world that is
    /// not the world the player gets.
    ///
    /// **Wire every new monthly system here, not in a caller.** The order is
    /// load-bearing; see the comments against individual entries.
    /// </summary>
    public static class SimulationPipeline
    {
        public static void Wire(TurnManager turns, GameState state)
        {
            // The causal month opens where the previous one closed. Fills gaps
            // only: a loaded save may already carry its month-open snapshot.
            Causal.OpenMonth(state);
            turns.ResolveMonth += Causal.OpenMonth;

            // Lifecycle first: an official who retires this month should not also
            // have worked it, and a seat filled this month should.
            turns.ResolveMonth += CabinetLifecycle.MonthlyUpdate;

            // Standing strategy is intent, not a second Cabinet. It writes the
            // default instruction only for Autonomous player desks after seats
            // exist and before anybody works. Directed and Direct Control desks
            // therefore remain the more explicit layer and win naturally.
            turns.ResolveMonth += StrategyCabinetBridge.Prepare;
            turns.ResolveMonth += CabinetSystem.MonthlyAct;

            // CabinetSystem predates standing strategy and describes Autonomous
            // work as ministerial judgement. Correct only the rebuilt player
            // report immediately after the Cabinet resolves; this is reporting
            // only and cannot move the simulation.
            turns.ResolveMonth += StrategySystem.ClarifyCabinetReport;

            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;

            // Deliveries before the economy runs, so equipment that arrived this
            // month is counted in this month's picture rather than next month's.
            turns.ResolveMonth += AcquisitionSystem.MonthlyDeliveries;
            turns.ResolveMonth += AcquisitionSystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;

            // Straight after the economy: service debt against income the month
            // actually produced, then let industry and sanctions see that result.
            turns.ResolveMonth += FiscalSystem.MonthlyUpdate;
            turns.ResolveMonth += IndustrialSystem.MonthlyUpdate;
            turns.ResolveMonth += EconomySystem.AgeSanctions;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;

            // After collection, so products delivered this month are graded on
            // this month's access rather than last month's.
            turns.ResolveMonth += IntelProductSystem.MonthlyUpdate;
            turns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
            turns.ResolveMonth += AgentSystem.MonthlyUpdate;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;

            // Blocs pull alignment before the chamber reads the diplomatic world.
            turns.ResolveMonth += BlocSystem.MonthlyUpdate;
            turns.ResolveMonth += AccessionSystem.MonthlyUpdate;
            turns.ResolveMonth += CouncilSystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;

            // Opposition reads the month just governed; regime change follows it.
            turns.ResolveMonth += OppositionSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;

            // A breakaway created this month is not tested for reunification in
            // the same month it declared.
            turns.ResolveMonth += SecessionSystem.MonthlyUpdate;
            turns.ResolveMonth += TechnologySystem.MonthlyUpdate;
            turns.ResolveMonth += EndgameSystem.MonthlyUpdate;
            turns.ResolveMonth += TerritorySystem.MonthlyUpdate;

            // Insurgency follows territory because current control decides who a
            // movement is fighting. Displacement follows both.
            turns.ResolveMonth += InsurgencySystem.MonthlyUpdate;
            turns.ResolveMonth += DisplacementSystem.MonthlyUpdate;

            // AI thinks after the world-state systems it is expected to observe.
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ProgressionSystem.MonthlyXP;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;

            // Foreign crises resolve before the player's systemic check, so their
            // month is already on the record when ours is raised.
            turns.ResolveMonth += ForeignCrisisSystem.MonthlyUpdate;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;
            turns.ResolveMonth += MandateSystem.MonthlyUpdate;
            turns.ResolveMonth += StandingDirectiveSystem.MonthlyUpdate;

            // Self-authored objectives are standing measurements of the resolved
            // world, so judge them after the month's consequences rather than at
            // its opening state.
            turns.ResolveMonth += StrategySystem.MonthlyUpdate;

            // Last, so telemetry describes the month as it ended.
            turns.ResolveMonth += Telemetry.RecordMonth;

            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);

            // Causality closes after YearEnded as well, so December includes the
            // annual evaluation and the record ends where the screen does.
            turns.MonthResolved += Causal.CloseMonth;
        }
    }
}

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
            Causal.OpenMonth(state);
            turns.ResolveMonth += Causal.OpenMonth;
            turns.ResolveMonth += CabinetLifecycle.MonthlyUpdate;
            turns.ResolveMonth += StrategyCabinetBridge.Prepare;
            turns.ResolveMonth += CabinetSystem.MonthlyAct;
            turns.ResolveMonth += StrategySystem.ClarifyCabinetReport;
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += AcquisitionSystem.MonthlyDeliveries;
            turns.ResolveMonth += AcquisitionSystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += FiscalSystem.MonthlyUpdate;
            turns.ResolveMonth += IndustrialSystem.MonthlyUpdate;
            turns.ResolveMonth += EconomySystem.AgeSanctions;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += IntelProductSystem.MonthlyUpdate;
            turns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
            turns.ResolveMonth += AgentSystem.MonthlyUpdate;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += BlocSystem.MonthlyUpdate;
            turns.ResolveMonth += AccessionSystem.MonthlyUpdate;
            turns.ResolveMonth += CouncilSystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += OppositionSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;
            turns.ResolveMonth += SecessionSystem.MonthlyUpdate;
            turns.ResolveMonth += TechnologySystem.MonthlyUpdate;
            turns.ResolveMonth += EndgameSystem.MonthlyUpdate;
            turns.ResolveMonth += TerritorySystem.MonthlyUpdate;
            turns.ResolveMonth += InsurgencySystem.MonthlyUpdate;
            turns.ResolveMonth += DisplacementSystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ProgressionSystem.MonthlyXP;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;

            // Planning observes the authoritative operation diary after the
            // confrontation has resolved. It never launches anything itself.
            turns.ResolveMonth += OperationPlanningSystem.MonthlyReconcile;

            turns.ResolveMonth += ForeignCrisisSystem.MonthlyUpdate;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;
            turns.ResolveMonth += MandateSystem.MonthlyUpdate;
            turns.ResolveMonth += StandingDirectiveSystem.MonthlyUpdate;
            turns.ResolveMonth += StrategySystem.MonthlyUpdate;
            turns.ResolveMonth += Telemetry.RecordMonth;

            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);
            turns.MonthResolved += Causal.CloseMonth;
            // Observe the whole resolved month, including December's year-end.
            // This writes history only: no action, score, RNG or national modifier.
            turns.MonthResolved += StrategicEraSystem.RecordMonth;
            turns.MonthStarted += _ => OperationPlanningSystem.ExecuteStandingOrder(state, turns);
        }
    }
}

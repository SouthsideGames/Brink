using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The order the world resolves in, in one place. Wire every monthly system
    /// here so play and validation execute the same world.
    /// </summary>
    public static class SimulationPipeline
    {
        public static void Wire(TurnManager turns, GameState state)
        {
            Causal.OpenMonth(state);
            turns.ResolveMonth += Causal.OpenMonth;

            turns.ResolveMonth += CabinetLifecycle.MonthlyUpdate;
            // Phase B strategy supplies default instructions only after lifecycle
            // has filled seats and before those officials work the month.
            turns.ResolveMonth += StrategyCabinetBridge.Prepare;
            turns.ResolveMonth += CabinetSystem.MonthlyAct;

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
            turns.ResolveMonth += ForeignCrisisSystem.MonthlyUpdate;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;
            turns.ResolveMonth += MandateSystem.MonthlyUpdate;
            turns.ResolveMonth += StandingDirectiveSystem.MonthlyUpdate;
            // Evaluate self-authored goals after the world has resolved, so a
            // goal reached this month is recognized this month.
            turns.ResolveMonth += StrategySystem.MonthlyUpdate;
            turns.ResolveMonth += Telemetry.RecordMonth;

            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);
            turns.MonthResolved += Causal.CloseMonth;
        }
    }
}
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
    /// not the world the player gets. `CabinetLifecycle` was added that way and
    /// produced exactly zero retirements across the entire suite before anyone
    /// noticed the harness had never been running it.
    ///
    /// **Wire every new monthly system here, not in a caller.** The order is
    /// load-bearing; see the comments against individual entries.
    /// </summary>
    public static class SimulationPipeline
    {
        /// <summary>
        /// Attach the full monthly simulation to a turn manager. `evaluateYear`
        /// is separate because the annual evaluation needs the live state
        /// reference the caller owns.
        /// </summary>
        public static void Wire(TurnManager turns, GameState state)
        {
            // Lifecycle first: an official who retires this month should not also
            // have worked it, and a seat filled this month should.
            turns.ResolveMonth += CabinetLifecycle.MonthlyUpdate;
            turns.ResolveMonth += CabinetSystem.MonthlyAct;

            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;

            // Deliveries before the economy runs, so equipment that arrived this
            // month is counted in this month's picture rather than next month's.
            turns.ResolveMonth += AcquisitionSystem.MonthlyDeliveries;
            turns.ResolveMonth += AcquisitionSystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += EconomySystem.AgeSanctions;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += AccessionSystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;

            // After the regime tick, because that is where a state fractures —
            // a breakaway created this month should not be tested for
            // reunification in the same month it declared.
            turns.ResolveMonth += SecessionSystem.MonthlyUpdate;
            turns.ResolveMonth += TechnologySystem.MonthlyUpdate;
            turns.ResolveMonth += EndgameSystem.MonthlyUpdate;
            turns.ResolveMonth += TerritorySystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ProgressionSystem.MonthlyXP;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;

            // Last, so the snapshot describes the month as it ended rather than
            // as it was halfway through being resolved.
            turns.ResolveMonth += Telemetry.RecordMonth;

            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);
        }
    }
}

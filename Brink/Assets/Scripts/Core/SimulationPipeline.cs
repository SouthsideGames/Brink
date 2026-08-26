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
            turns.ResolveMonth += IndustrialSystem.MonthlyUpdate;
            turns.ResolveMonth += EconomySystem.AgeSanctions;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += IntelligenceSystem.MonthlyDecay;
            turns.ResolveMonth += AgentSystem.MonthlyUpdate;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += AccessionSystem.MonthlyUpdate;

            // After the bilateral diplomacy tick: the chamber votes on the world
            // as it stands this month, and a state bought off last week should
            // vote like it.
            turns.ResolveMonth += CouncilSystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;

            // Straight after the government tick, so the case is built from the
            // month that has just been governed rather than the one before it —
            // and before `RegimeSystem`, because an opposition answered in public
            // is the alternative to one that ends up conspiring.
            turns.ResolveMonth += OppositionSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;

            // After the regime tick, because that is where a state fractures —
            // a breakaway created this month should not be tested for
            // reunification in the same month it declared.
            turns.ResolveMonth += SecessionSystem.MonthlyUpdate;
            turns.ResolveMonth += TechnologySystem.MonthlyUpdate;
            turns.ResolveMonth += EndgameSystem.MonthlyUpdate;
            turns.ResolveMonth += TerritorySystem.MonthlyUpdate;

            // After territory, because who holds a province this month decides
            // who its movement is shooting at — and before the AI thinks, so a
            // government reasons about a rising that has already happened rather
            // than about last month's map.
            turns.ResolveMonth += InsurgencySystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ProgressionSystem.MonthlyXP;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            // Foreign states face their own situations — before the player check, so
            // a rival's bad month is already on the record when our own crisis
            // (if any) is raised.
            turns.ResolveMonth += ForeignCrisisSystem.MonthlyUpdate;
            turns.ResolveMonth += CrisisSystem.SystemicCheck;

            // Last, so the snapshot describes the month as it ended rather than
            // as it was halfway through being resolved.
            turns.ResolveMonth += Telemetry.RecordMonth;

            turns.YearEnded += year => ProgressionSystem.EvaluateYear(state, year);
        }
    }
}

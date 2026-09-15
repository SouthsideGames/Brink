using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Fiscal guardrail for new AI discretionary commitments.
    ///
    /// C3 found that voluntary sovereign borrowing was funding long-horizon
    /// programmes after the books had already become fragile. This gate is
    /// deliberately about starting a new commitment, not servicing one already
    /// under way: governments may finish research and strategic preparation they
    /// have already authorized, and the player remains free to debt-finance the
    /// same choices deliberately.
    /// </summary>
    public static class AIFiscalDiscipline
    {
        /// <summary>
        /// Above this debt-to-GDP level an AI government stops adding new
        /// discretionary programmes. The C3 audit found the interest/growth
        /// crossover becoming self-reinforcing around this point.
        /// </summary>
        public const float NewProgrammeDebtCeiling = 75f;

        /// <summary>
        /// Whether this country may take on a new research or strategic-
        /// preparation commitment. Player choices are never restricted here.
        /// </summary>
        public static bool CanStartNewDiscretionaryProgramme(GameState state, CountryState country)
        {
            if (state == null || country == null) return false;
            if (country.isPlayer || country.id == state.playerCountryId) return true;
            if (FiscalSystem.DebtToGdp(country) >= NewProgrammeDebtCeiling) return false;

            return FiscalSystem.ConditionOf(state, country) < FiscalCondition.DebtStressed;
        }
    }
}

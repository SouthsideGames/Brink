using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase B operator verbs live in StrategySystem rather than GameController,
    /// so they are kept beside ActionCatalog without pretending they are controller
    /// commands. ACTIONS merges this reference list into the permanent index.
    /// </summary>
    public static class StrategyActionCatalog
    {
        public static List<ActionEntry> All(GameState state)
        {
            var result = new List<ActionEntry>();
            var plan = StrategySystem.Ensure(state);
            bool hasPlan = plan != null;

            result.Add(new ActionEntry
            {
                pillar = Pillar.Government,
                viewId = "OPERATOR",
                label = "Set standing doctrine",
                cost = plan != null && plan.doctrineChosen ? "2 INF to revise" : "Free first adoption",
                description = "Set the posture unattended government should favour. Explicit Cabinet orders and Direct Control still override it.",
                available = hasPlan,
                blockedReason = hasPlan ? "" : "No posting mandate is active."
            });

            var policies = StrategySystem.AvailablePolicies(state);
            result.Add(new ActionEntry
            {
                pillar = Pillar.Government,
                viewId = "OPERATOR",
                label = "Adopt national policy",
                cost = "Free first adoption",
                description = "Choose the country-shaped strategic trade-off that steers favoured and strained autonomous desks.",
                available = policies.Length > 0,
                blockedReason = policies.Length > 0 ? "" : "No authored national policy is available for this posting."
            });

            bool room = plan != null && plan.objectives.Count < StrategySystem.MaxObjectives;
            result.Add(new ActionEntry
            {
                pillar = Pillar.Government,
                viewId = "OPERATOR",
                label = "Write a standing objective",
                cost = "Free",
                description = "Define what success means for this posting. Self-authored objectives are measurements only and never award XP or grade points.",
                available = room,
                blockedReason = room ? "" : $"Maximum {StrategySystem.MaxObjectives} standing objectives already on file."
            });

            return result;
        }

        public static int AvailableCount(GameState state)
        {
            int count = 0;
            foreach (var action in All(state)) if (action.available) count++;
            return count;
        }
    }
}
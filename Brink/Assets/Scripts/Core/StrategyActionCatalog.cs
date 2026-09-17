using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Posting-level strategy verbs kept beside ActionCatalog. ACTIONS merges
    /// this list into the permanent command reference without pretending these
    /// planning verbs are GameController simulation commands.
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
                label = "Set plan frame",
                cost = "Free",
                description = "Name the long-term plan and choose a 1, 3, 5 or 10-year horizon. The horizon organises decisions; it is not a deadline or scoring rule.",
                available = hasPlan,
                blockedReason = hasPlan ? "" : "No posting mandate is active."
            });

            result.Add(new ActionEntry
            {
                pillar = Pillar.Government,
                viewId = "OPERATOR",
                label = "Run a strategic what-if",
                cost = "Free",
                description = "Preview current-course arithmetic and the exact Cabinet intent of another doctrine without advancing the simulation or changing the save.",
                available = hasPlan,
                blockedReason = hasPlan ? "" : "No posting mandate is active."
            });

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
            bool policyHeld = policies.Length > 0
                && StrategySystem.PolicyInSlot(plan, policies[0].slotId) != null;
            result.Add(new ActionEntry
            {
                pillar = Pillar.Government,
                viewId = "OPERATOR",
                label = "Adopt national policy",
                cost = policyHeld
                    ? $"{StrategySystem.PolicyRevisionInfluence} INF to revise"
                    : "Free first adoption",
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
                description = "Define measurable success or record self-assessed freeform intent. Neither awards XP or grade points.",
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

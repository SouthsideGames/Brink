using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase E outcome-first navigation. The command index answers "what can I do?";
    /// this answers "what can I do about the thing that is getting worse?" It is
    /// read-only and deliberately returns catalog entries rather than executing them.
    /// </summary>
    public static class ActionFinderSystem
    {
        public sealed class Recommendation
        {
            public CausalMetric metric;
            public string outcome;
            public string viewId;
            public string label;
            public string cost;
            public bool available;
            public string blockedReason;
        }

        public static List<Recommendation> ForMetric(GameState state, CausalMetric metric, int limit = 4)
        {
            var result = new List<Recommendation>();
            if (state == null || metric == CausalMetric.None || limit <= 0) return result;

            var entries = new List<ActionEntry>();
            entries.AddRange(ActionCatalog.All(state));
            entries.AddRange(StrategyActionCatalog.All(state));
            var preferred = PreferredPillars(metric);

            foreach (var pillar in preferred)
            {
                foreach (var entry in entries)
                {
                    if (entry == null || entry.pillar != pillar) continue;
                    if (AlreadyAdded(result, entry)) continue;
                    result.Add(new Recommendation
                    {
                        metric = metric,
                        outcome = CausalReasons.MetricLabel(metric),
                        viewId = entry.viewId,
                        label = entry.label,
                        cost = entry.cost,
                        available = entry.available,
                        blockedReason = entry.blockedReason
                    });
                    if (result.Count >= limit) return result;
                }
            }
            return result;
        }

        public static string Render(GameState state, CausalMetric metric, int limit = 4)
        {
            var items = ForMetric(state, metric, limit);
            if (items.Count == 0) return "NO RELEVANT COMMANDS INDEXED FOR THIS OUTCOME.";
            var sb = new System.Text.StringBuilder();
            sb.Append("OPTIONS FOR ").Append(CausalReasons.MetricLabel(metric)).AppendLine();
            foreach (var item in items)
            {
                sb.Append("  ").Append(item.available ? "▸ " : "· ")
                  .Append(item.label.ToUpperInvariant()).Append("  [")
                  .Append(item.cost).Append("]  → ").Append(item.viewId);
                if (!item.available) sb.Append("  — ").Append(item.blockedReason);
                sb.AppendLine();
            }
            sb.Append("OPTIONS ARE NAVIGATION, NOT PROMISES OF OUTCOME.");
            return sb.ToString();
        }

        static bool AlreadyAdded(List<Recommendation> result, ActionEntry entry)
        {
            foreach (var existing in result)
                if (existing.viewId == entry.viewId && existing.label == entry.label) return true;
            return false;
        }

        static Pillar[] PreferredPillars(CausalMetric metric)
        {
            switch (metric)
            {
                case CausalMetric.GovernmentApproval:
                case CausalMetric.SocialUnrest:
                case CausalMetric.PublicGrievance:
                    return new[] { Pillar.Government, Pillar.Economy, Pillar.Diplomacy };
                case CausalMetric.LivingStandards:
                case CausalMetric.MarketIndex:
                case CausalMetric.Treasury:
                case CausalMetric.SovereignDebt:
                    return new[] { Pillar.Economy, Pillar.Government, Pillar.Diplomacy };
                case CausalMetric.WarExhaustion:
                    return new[] { Pillar.Military, Pillar.Diplomacy, Pillar.Government };
                default:
                    return new[] { Pillar.Government, Pillar.Economy, Pillar.Military, Pillar.Diplomacy, Pillar.Intelligence };
            }
        }
    }
}

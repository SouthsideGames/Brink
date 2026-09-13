using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase E legibility layer: one read-only board that answers "what deserves
    /// my attention, what changed, and what do we know about why?" It consumes
    /// existing authoritative state and the causal disclosure gate; it never
    /// creates facts, advances time, spends resources, or bypasses fog.
    /// </summary>
    public static class StrategicBoardSystem
    {
        public sealed class Item
        {
            public CausalMetric metric;
            public string label;
            public float value;
            public float delta;
            public int urgency;
            public string direction;
            public string driver;
            public bool incomplete;
        }

        public static List<Item> Build(GameState state)
        {
            var result = new List<Item>();
            if (state == null || state.causal == null || state.causal.records == null) return result;

            foreach (CausalMetric metric in Enum.GetValues(typeof(CausalMetric)))
            {
                if (metric == CausalMetric.None) continue;
                var record = Latest(state, metric);
                if (record == null) continue;
                var disclosed = CausalDisclosure.Disclose(state, record);
                if (disclosed == null) continue;

                result.Add(new Item
                {
                    metric = metric,
                    label = CausalReasons.MetricLabel(metric),
                    value = record.resulting,
                    delta = record.delta,
                    urgency = Urgency(metric, record.resulting, record.delta),
                    direction = Direction(metric, record.delta),
                    driver = DominantDriver(disclosed),
                    incomplete = disclosed.withheld > 0 || disclosed.Opaque
                });
            }

            result.Sort((a, b) =>
            {
                int urgent = b.urgency.CompareTo(a.urgency);
                return urgent != 0 ? urgent : ((int)a.metric).CompareTo((int)b.metric);
            });
            return result;
        }

        public static string Render(GameState state, int maxItems = 5)
        {
            var items = Build(state);
            if (items.Count == 0) return "STRATEGIC BOARD — NO RESOLVED-MONTH ANALYSIS YET.";
            maxItems = Math.Max(1, Math.Min(maxItems, items.Count));

            var lines = new System.Text.StringBuilder();
            lines.AppendLine("STRATEGIC BOARD — ATTENTION FIRST");
            for (int i = 0; i < maxItems; i++)
            {
                var item = items[i];
                string flag = item.urgency >= 3 ? "!!" : item.urgency == 2 ? "! " : "  ";
                lines.Append(flag).Append(' ').Append(item.label)
                     .Append("  ").Append(item.value.ToString("F1"))
                     .Append("  ").Append(item.delta >= 0f ? "+" : "").Append(item.delta.ToString("F1"))
                     .Append("  ").Append(item.direction).AppendLine();
                lines.Append("     DRIVER: ").Append(item.driver);
                if (item.incomplete) lines.Append("  [REPORTING INCOMPLETE]");
                lines.AppendLine();
            }
            lines.Append("READOUT RANKS ATTENTION; IT DOES NOT CHOOSE FOR YOU.");
            return lines.ToString();
        }

        static CausalRecord Latest(GameState state, CausalMetric metric)
        {
            for (int i = state.causal.records.Count - 1; i >= 0; i--)
            {
                var record = state.causal.records[i];
                if (record != null && record.countryId == state.playerCountryId && record.metric == metric)
                    return record;
            }
            return null;
        }

        static string DominantDriver(DisclosedExplanation disclosed)
        {
            DisclosedCause best = null;
            float magnitude = -1f;
            foreach (var cause in disclosed.causes)
            {
                if (cause == null || CausalReasons.IsStructural(cause.reason)) continue;
                float current = cause.sized ? Math.Abs(cause.value) : 0.0001f;
                if (current <= magnitude) continue;
                best = cause;
                magnitude = current;
            }
            if (best != null) return CausalReasons.Label(best.reason);
            return disclosed.withheld > 0 ? "CAUSE NOT AVAILABLE TO THIS DESK" : "NO DOMINANT REPORTED DRIVER";
        }

        static int Urgency(CausalMetric metric, float value, float delta)
        {
            float adverse = AdverseDelta(metric, delta);
            int score = adverse > 4f ? 2 : adverse > 1f ? 1 : 0;
            switch (metric)
            {
                case CausalMetric.GovernmentApproval:
                case CausalMetric.LivingStandards:
                    if (value < 25f) score += 2; else if (value < 40f) score += 1;
                    break;
                case CausalMetric.SocialUnrest:
                case CausalMetric.PublicGrievance:
                case CausalMetric.WarExhaustion:
                    if (value > 75f) score += 2; else if (value > 55f) score += 1;
                    break;
                case CausalMetric.MarketIndex:
                    if (value < 45f) score += 2; else if (value < 70f) score += 1;
                    break;
                case CausalMetric.Treasury:
                    if (value < 0f) score += 2;
                    break;
                case CausalMetric.SovereignDebt:
                    if (delta > 0f) score += 1;
                    break;
            }
            return Math.Min(4, score);
        }

        static float AdverseDelta(CausalMetric metric, float delta)
        {
            switch (metric)
            {
                case CausalMetric.GovernmentApproval:
                case CausalMetric.LivingStandards:
                case CausalMetric.MarketIndex:
                case CausalMetric.Treasury:
                    return -delta;
                default:
                    return delta;
            }
        }

        static string Direction(CausalMetric metric, float delta)
        {
            if (Math.Abs(delta) < 0.01f) return "STABLE";
            return AdverseDelta(metric, delta) > 0f ? "DETERIORATING" : "IMPROVING";
        }
    }
}
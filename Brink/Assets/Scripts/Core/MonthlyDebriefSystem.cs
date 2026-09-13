using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Read-only rollover model for the month that just resolved. It turns the
    /// existing causal ledger into a small consequence report without creating
    /// new facts, bypassing fog, spending resources, or advancing time.
    /// </summary>
    public static class MonthlyDebriefSystem
    {
        public sealed class Consequence
        {
            public CausalMetric metric;
            public string label;
            public float resulting;
            public float delta;
            public string direction;
            public string driver;
            public string sourceActionId;
            public bool playerLinked;
            public bool dominantPlayerLinked;
            public bool autonomousDominant;
            public bool incomplete;
            public int importance;
        }

        public sealed class Report
        {
            public int year;
            public int month;
            public readonly List<Consequence> consequences = new List<Consequence>();
            public int PlayerLinkedCount
            {
                get { int n = 0; foreach (var c in consequences) if (c.playerLinked) n++; return n; }
            }
            public int AutonomousDominantCount
            {
                get { int n = 0; foreach (var c in consequences) if (c.autonomousDominant) n++; return n; }
            }
        }

        public static Report Build(GameState state)
        {
            var report = new Report();
            if (state == null || state.causal == null || state.causal.records == null) return report;

            int latest = int.MinValue;
            foreach (var record in state.causal.records)
            {
                if (record == null || record.countryId != state.playerCountryId || !record.HasContent) continue;
                if (record.MonthIndex > latest) { latest = record.MonthIndex; report.year = record.year; report.month = record.month; }
            }
            if (latest == int.MinValue) return report;

            foreach (CausalMetric metric in Enum.GetValues(typeof(CausalMetric)))
            {
                if (metric == CausalMetric.None) continue;
                CausalRecord record = null;
                for (int i = state.causal.records.Count - 1; i >= 0; i--)
                {
                    var candidate = state.causal.records[i];
                    if (candidate != null && candidate.countryId == state.playerCountryId && candidate.metric == metric && candidate.MonthIndex == latest)
                    { record = candidate; break; }
                }
                if (record == null || !record.HasContent) continue;

                var disclosed = CausalDisclosure.Disclose(state, record);
                if (disclosed == null) continue;
                var best = Dominant(disclosed);
                var playerCause = DominantPlayerCause(disclosed);
                bool playerLinked = playerCause != null;
                bool dominantPlayerLinked = best != null && best.category == CausalCategory.PlayerDecision && !string.IsNullOrEmpty(best.sourceActionId);

                report.consequences.Add(new Consequence
                {
                    metric = metric,
                    label = CausalReasons.MetricLabel(metric),
                    resulting = record.resulting,
                    delta = record.delta,
                    direction = Direction(metric, record.delta),
                    driver = best != null ? CausalReasons.Label(best.reason) : (disclosed.withheld > 0 ? "CAUSE NOT AVAILABLE TO THIS DESK" : "NO DOMINANT REPORTED DRIVER"),
                    sourceActionId = playerLinked ? playerCause.sourceActionId : "",
                    playerLinked = playerLinked,
                    dominantPlayerLinked = dominantPlayerLinked,
                    autonomousDominant = best != null && !dominantPlayerLinked,
                    incomplete = disclosed.withheld > 0 || disclosed.Opaque,
                    importance = Importance(metric, record.resulting, record.delta, playerLinked)
                });
            }

            report.consequences.Sort((a, b) =>
            {
                int byImportance = b.importance.CompareTo(a.importance);
                return byImportance != 0 ? byImportance : ((int)a.metric).CompareTo((int)b.metric);
            });
            return report;
        }

        static DisclosedCause Dominant(DisclosedExplanation disclosed)
        {
            DisclosedCause best = null;
            float magnitude = -1f;
            foreach (var cause in disclosed.causes)
            {
                if (cause == null || CausalReasons.IsStructural(cause.reason)) continue;
                float current = cause.sized ? Math.Abs(cause.value) : 0.0001f;
                if (current <= magnitude) continue;
                best = cause; magnitude = current;
            }
            return best;
        }

        static DisclosedCause DominantPlayerCause(DisclosedExplanation disclosed)
        {
            DisclosedCause best = null;
            float magnitude = -1f;
            foreach (var cause in disclosed.causes)
            {
                if (cause == null || cause.category != CausalCategory.PlayerDecision || string.IsNullOrEmpty(cause.sourceActionId)) continue;
                float current = cause.sized ? Math.Abs(cause.value) : 0.0001f;
                if (current <= magnitude) continue;
                best = cause; magnitude = current;
            }
            return best;
        }

        static int Importance(CausalMetric metric, float value, float delta, bool playerLinked)
        {
            float adverse = AdverseDelta(metric, delta);
            int score = playerLinked ? 1 : 0;
            if (Math.Abs(delta) >= 1f) score++;
            if (Math.Abs(delta) >= 4f) score++;
            if (adverse > 0f) score++;
            switch (metric)
            {
                case CausalMetric.GovernmentApproval:
                case CausalMetric.LivingStandards: if (value < 40f) score++; break;
                case CausalMetric.SocialUnrest:
                case CausalMetric.PublicGrievance:
                case CausalMetric.WarExhaustion: if (value > 55f) score++; break;
                case CausalMetric.MarketIndex: if (value < 70f) score++; break;
                case CausalMetric.Treasury: if (value < 0f) score += 2; break;
                case CausalMetric.SovereignDebt: if (delta > 0f) score++; break;
            }
            return score;
        }

        static float AdverseDelta(CausalMetric metric, float delta)
        {
            switch (metric)
            {
                case CausalMetric.GovernmentApproval:
                case CausalMetric.LivingStandards:
                case CausalMetric.MarketIndex:
                case CausalMetric.Treasury: return -delta;
                default: return delta;
            }
        }

        static string Direction(CausalMetric metric, float delta)
        {
            if (Math.Abs(delta) < 0.01f) return "STABLE";
            return AdverseDelta(metric, delta) > 0f ? "DETERIORATED" : "IMPROVED";
        }
    }
}
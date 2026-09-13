using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Reads setbacks and recoveries without creating a game-over state. Durable
    /// losses come from existing records; recent recovery evidence comes from the
    /// causal ledger, whose twelve-month horizon is stated explicitly.
    /// </summary>
    public static class RecoveryHistorySystem
    {
        public sealed class Reading
        {
            public int warsWon;
            public int warsLost;
            public int administrationsServed;
            public int improvingRecentMetrics;
            public int deterioratingRecentMetrics;
            public string status;
        }

        public static Reading Read(GameState state)
        {
            if (state == null || state.PlayerCountry == null) return null;
            var r = new Reading
            {
                warsWon = state.PlayerCountry.warsWon,
                warsLost = state.PlayerCountry.warsLost,
                administrationsServed = state.administrationsServed
            };
            var latest = new Dictionary<CausalMetric, CausalRecord>();
            if (state.causal?.records != null)
                foreach (var record in state.causal.records)
                    if (record != null && record.countryId == state.playerCountryId && record.metric != CausalMetric.None)
                    {
                        CausalRecord prior;
                        if (!latest.TryGetValue(record.metric, out prior) || record.MonthIndex > prior.MonthIndex) latest[record.metric] = record;
                    }
            foreach (var pair in latest)
            {
                float d = pair.Value.delta;
                bool badWhenRising = pair.Key == CausalMetric.SocialUnrest || pair.Key == CausalMetric.PublicGrievance || pair.Key == CausalMetric.SovereignDebt || pair.Key == CausalMetric.WarExhaustion;
                if (Math.Abs(d) < 0.0001f) continue;
                bool improving = badWhenRising ? d < 0 : d > 0;
                if (improving) r.improvingRecentMetrics++; else r.deterioratingRecentMetrics++;
            }
            if (r.warsLost > 0 && r.improvingRecentMetrics > r.deterioratingRecentMetrics) r.status = "RECOVERY AFTER SETBACK";
            else if (r.administrationsServed > 1 && r.improvingRecentMetrics >= r.deterioratingRecentMetrics) r.status = "INSTITUTIONAL CONTINUITY";
            else if (r.improvingRecentMetrics > r.deterioratingRecentMetrics) r.status = "RECOVERY UNDERWAY";
            else if (r.deterioratingRecentMetrics > r.improvingRecentMetrics) r.status = "PRESSURE STILL BUILDING";
            else r.status = "NO CLEAR RECENT DIRECTION";
            return r;
        }

        public static string Render(GameState state)
        {
            var r = Read(state);
            if (r == null) return "RECOVERY FILE — NO PLAYER GOVERNMENT.";
            var sb = new StringBuilder("RECOVERY FILE — FAILURE IS HISTORY, NOT GAME OVER\n");
            sb.Append("wars won/lost ").Append(r.warsWon).Append('/').Append(r.warsLost)
              .Append("  administrations served ").Append(r.administrationsServed).AppendLine();
            sb.Append("recent improving/deteriorating metrics ").Append(r.improvingRecentMetrics).Append('/')
              .Append(r.deterioratingRecentMetrics).Append("  [12-MONTH CAUSAL WINDOW]").AppendLine();
            sb.Append("READING: ").AppendLine(r.status);
            sb.Append("A SETBACK REMAINS ON THE RECORD EVEN WHEN THE COUNTRY RECOVERS. RECOVERY DOES NOT ERASE CONSEQUENCE.");
            return sb.ToString();
        }
    }
}

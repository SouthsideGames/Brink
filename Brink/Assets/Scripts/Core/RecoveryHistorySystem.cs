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
            public int observedRecentMetrics;
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
                    if (record != null && record.countryId == state.playerCountryId && record.metric != CausalMetric.None
                        && record.month >= 1 && record.month <= 12
                        && state.date.year * 12 + state.date.month - record.MonthIndex >= 0
                        && state.date.year * 12 + state.date.month - record.MonthIndex < CausalLedger.MonthsKept)
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
            r.observedRecentMetrics = latest.Count;
            if (r.observedRecentMetrics == 0) r.status = "NO RECENT EVIDENCE";
            else if (r.warsLost > 0 && r.improvingRecentMetrics > r.deterioratingRecentMetrics) r.status = "RECOVERY AFTER SETBACK";
            else if (r.administrationsServed > 1 && r.improvingRecentMetrics >= r.deterioratingRecentMetrics) r.status = "INSTITUTIONAL CONTINUITY";
            else if (r.improvingRecentMetrics > r.deterioratingRecentMetrics) r.status = "RECOVERY UNDERWAY";
            else if (r.deterioratingRecentMetrics > r.improvingRecentMetrics) r.status = "PRESSURE STILL BUILDING";
            else r.status = "NO CLEAR RECENT DIRECTION";
            return r;
        }

        /// <summary>
        /// Own-state facts and routes to existing desks, not orders or promises
        /// of acceptance. Recovery can improve a rump without restoring its map.
        /// </summary>
        public static string Options(GameState state)
        {
            var c = state?.PlayerCountry;
            if (c == null) return "";
            var sb = new StringBuilder("THIS OFFICE CONTINUES — END MONTH REMAINS AVAILABLE\n");
            sb.AppendLine("These are options, not free repairs. The destination desk checks authority, cost and acceptance. Delegation remains available through CABINET.");
            int held = 0, occupied = 0;
            foreach (var site in state.locations)
            {
                if (site.ownerId == c.id) held++;
                if (site.originalOwnerId == c.id && site.ownerId != c.id) occupied++;
            }
            sb.AppendLine($"GROUND: {held} controlled; {occupied} titled sites held by others. Title already ceded or annexed is not counted as an occupation.");
            if (held == 0)
                sb.AppendLine("NO CONTROLLED GROUND — the diminished state and this posting survive. There is no automatic restoration of territory, resources or forces.");
            if (state.IsAtWar(c.id))
                sb.AppendLine("WAR CONTINUES — MILITARY: PROPOSE TERMS can seek an end to the fighting; a demand can be refused. Ending a war does not refund its losses.");
            if (c.warsLost > 0 || occupied > 0 || held == 0)
                sb.AppendLine("AFTER DEFEAT — MILITARY: review posture and existing procurement before buying more. Rebuilding takes funding and time; another war is not required to continue.");
            if (c.government.inCivilConflict || c.stability < 35f || c.nationalUnity < 35f)
                sb.AppendLine("DOMESTIC BREAKDOWN — GOVERNMENT: review public pressure, civic posture and institutional reform. CABINET can be directed toward domestic stability. Order and public consent have different costs; no action guarantees recovery.");
            if (c.government.coupsExperienced > 0 || state.administrationsServed > 1)
                sb.AppendLine($"GOVERNMENT TURNOVER — {state.administrationsServed} administrations served; {c.government.coupsExperienced} coups recorded. The operator remains. GOVERNMENT and CABINET show the new authority and appointments; old trust and treaty losses are not undone.");
            var child = SecessionSystem.BreakawayOf(state, c.id);
            bool childHasGround = false;
            if (child != null)
                foreach (var site in state.locations)
                    if (site.originalOwnerId == child.id) { childHasGround = true; break; }
            if (childHasGround)
                sb.AppendLine("SECESSION — the successor holds its own title; this office stays with the parent. DIPLOMACY: rebuild relations or coexist. Reunification is conditional, never a countdown or a free reset; it needs a settled, peaceful successor and sufficiently warm relations.");
            var fiscal = FiscalSystem.ConditionOf(state, c);
            sb.AppendLine("FINANCES: " + FiscalSystem.ConditionText(fiscal) + ".");
            if (fiscal != FiscalCondition.Sound || c.economy.growthRate < 0f)
                sb.AppendLine("ECONOMIC SETBACK — ECONOMY: review tax and budget posture, recurring programmes and debt restructuring. Austerity trades living standards for solvency; restructuring cuts debt but damages credit, confidence and creditor relationships. Borrowing is not income.");
            int measures = 0, agreements = 0;
            foreach (var sanction in state.sanctions) if (sanction.targetId == c.id) measures++;
            foreach (var treaty in state.treaties)
                if (treaty.Involves(c.id) && !treaty.broken)
                    foreach (TreatyCommitment commitment in Enum.GetValues(typeof(TreatyCommitment)))
                        if (treaty.HasActive(state, commitment)) { agreements++; break; }
            if (measures > 0)
                sb.AppendLine($"EXTERNAL PRESSURE — {measures} sanction regimes against us. DIPLOMACY: SEEK SANCTIONS RELIEF asks each sender; it does not guarantee consent. ECONOMY: inspect supply and trade dependencies.");
            if (agreements == 0)
                sb.AppendLine("NO ACTIVE TREATY COMMITMENTS — not proof that every state is hostile. DIPLOMACY: outreach, normalization and a limited agreement can rebuild ties; partners retain their own interests and memories.");
            sb.Append("COMMAND INDEX lists ordinary instruments and costs. A low score, failed mandate or catastrophic loss does not erase the save. Recovery may mean making a smaller country viable, not returning to its opening rank.");
            return sb.ToString();
        }

        public static string Render(GameState state)
        {
            var r = Read(state);
            if (r == null) return "RECOVERY FILE — NO PLAYER GOVERNMENT.";
            var sb = new StringBuilder("RECOVERY FILE — FAILURE IS HISTORY, NOT GAME OVER\n");
            sb.Append("wars won/lost ").Append(r.warsWon).Append('/').Append(r.warsLost)
              .Append("  administrations served ").Append(r.administrationsServed).AppendLine();
            sb.Append("latest observed metric changes: improving/deteriorating ").Append(r.improvingRecentMetrics).Append('/')
              .Append(r.deterioratingRecentMetrics).Append("; observed ").Append(r.observedRecentMetrics)
              .Append("  [12-MONTH CAUSAL WINDOW]").AppendLine();
            sb.Append("READING: ").AppendLine(r.status);
            sb.AppendLine("Direction is the latest observation per metric in this window, not a year-long trend or proof of success. Missing evidence is not improvement.");
            sb.AppendLine("A SETBACK REMAINS ON THE RECORD EVEN WHEN THE COUNTRY RECOVERS. RECOVERY DOES NOT ERASE CONSEQUENCE.");
            sb.Append(Options(state));
            return sb.ToString();
        }
    }
}

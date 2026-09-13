using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Reads durable precedent from the Chronicle. It deliberately does not add
    /// another save ledger: if Brink did not record an event, this system will
    /// not invent one after the fact. Precedent is narrative memory, not a buff.
    /// </summary>
    public static class PrecedentSystem
    {
        public sealed class Precedent
        {
            public GameDate date;
            public ChronicleCategory category;
            public string text;
            public string reading;
        }

        public static List<Precedent> Recent(GameState state, int limit = 8)
        {
            var result = new List<Precedent>();
            if (state == null) return result;
            for (int i = state.chronicle.Count - 1; i >= 0 && result.Count < Math.Max(1, limit); i--)
            {
                var entry = state.chronicle[i];
                if (entry == null || entry.countryId != state.playerCountryId) continue;
                string reading = Reading(entry);
                if (reading == null) continue;
                result.Add(new Precedent { date = entry.date, category = entry.category, text = entry.text, reading = reading });
            }
            return result;
        }

        public static string Render(GameState state, int limit = 6)
        {
            var items = Recent(state, limit);
            if (items.Count == 0) return "PRECEDENT FILE — NO MAJOR PLAYER-COUNTRY PRECEDENT IS YET RECORDED.";
            var sb = new StringBuilder("PRECEDENT FILE — WHAT LATER GOVERNMENTS CAN POINT TO\n");
            foreach (var item in items)
            {
                sb.Append(item.date.DisplayString).Append("  ").Append(item.category.ToString().ToUpperInvariant()).AppendLine();
                sb.Append("  ").AppendLine(item.text);
                sb.Append("  PRECEDENT: ").AppendLine(item.reading);
            }
            sb.Append("PRECEDENT IS MEMORY, NOT LAW. THE OPERATOR MAY REPEAT IT, REVERSE IT, OR BREAK WITH IT.");
            return sb.ToString();
        }

        static string Reading(ChronicleEntry entry)
        {
            string text = (entry.text ?? "").ToLowerInvariant();
            if (entry.category == ChronicleCategory.Military)
                return "The state has a recorded military choice that future leaders can cite as proof of what it was once willing to do.";
            if (entry.category == ChronicleCategory.Diplomatic)
                return "The state has a recorded diplomatic position against which later promises and reversals will be judged.";
            if (entry.category == ChronicleCategory.Political)
                return "The political order has survived or chosen a course that later governments inherit as institutional memory.";
            if (entry.category == ChronicleCategory.Economic && (text.Contains("sanction") || text.Contains("debt") || text.Contains("trade") || text.Contains("budget")))
                return "The state has an economic precedent that can frame later arguments about risk, dependence, and restraint.";
            return null;
        }
    }
}

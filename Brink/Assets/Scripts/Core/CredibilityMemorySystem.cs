using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Makes the diplomacy model's existing durable memory legible. Relationships
    /// already remember what passed between states and treaties already remember
    /// who broke them; this reader deliberately creates no second credibility
    /// score and applies no new modifier.
    /// </summary>
    public static class CredibilityMemorySystem
    {
        public sealed class PartnerMemory
        {
            public string partnerId;
            public string partnerName;
            public float trust;
            public float memoryWeight;
            public int memories;
            public int activeCommitments;
            public int brokenByUs;
            public int brokenByThem;
            public string latestMemory;
        }

        public static List<PartnerMemory> Build(GameState state)
        {
            var result = new List<PartnerMemory>();
            if (state == null || state.PlayerCountry == null) return result;
            foreach (var relationship in state.relationships)
            {
                if (relationship == null || !relationship.Involves(state.playerCountryId)) continue;
                string partnerId = relationship.PartnerOf(state.playerCountryId);
                var partner = state.FindCountry(partnerId);
                var item = new PartnerMemory
                {
                    partnerId = partnerId,
                    partnerName = partner?.displayName ?? partnerId,
                    trust = relationship.trust,
                    memoryWeight = relationship.memoryWeight,
                    memories = relationship.memory?.Count ?? 0,
                    latestMemory = relationship.memory != null && relationship.memory.Count > 0 ? relationship.memory[relationship.memory.Count - 1] : ""
                };
                foreach (var treaty in state.treaties)
                {
                    if (treaty == null || !treaty.Involves(state.playerCountryId) || treaty.PartnerOf(state.playerCountryId) != partnerId) continue;
                    if (!treaty.broken) item.activeCommitments += treaty.commitments?.Count ?? 0;
                    else if (treaty.brokenBy == state.playerCountryId) item.brokenByUs++;
                    else if (treaty.brokenBy == partnerId) item.brokenByThem++;
                }
                if (item.memories > 0 || item.activeCommitments > 0 || item.brokenByUs > 0 || item.brokenByThem > 0)
                    result.Add(item);
            }
            result.Sort((a, b) => Math.Abs(b.memoryWeight).CompareTo(Math.Abs(a.memoryWeight)));
            return result;
        }

        public static string Render(GameState state, int limit = 6)
        {
            var items = Build(state);
            if (items.Count == 0) return "CREDIBILITY FILE — NO DURABLE BILATERAL COMMITMENT HISTORY YET.";
            var sb = new StringBuilder("CREDIBILITY FILE — PROMISES LEAVE A RECORD\n");
            for (int i = 0; i < items.Count && i < Math.Max(1, limit); i++)
            {
                var p = items[i];
                sb.Append(p.partnerName.ToUpperInvariant()).Append("  trust ").Append(Math.Round(p.trust)).Append("  memory ")
                  .Append(p.memoryWeight >= 0 ? "+" : "").Append(Math.Round(p.memoryWeight, 1)).AppendLine();
                sb.Append("  commitments ").Append(p.activeCommitments).Append("  broken by us ").Append(p.brokenByUs)
                  .Append("  broken by them ").Append(p.brokenByThem).AppendLine();
                if (!string.IsNullOrEmpty(p.latestMemory)) sb.Append("  latest: ").AppendLine(p.latestMemory);
            }
            sb.Append("TRUST IS ALREADY PART OF DIPLOMACY. THIS FILE EXPLAINS THE RECORD; IT DOES NOT ADD A SECOND SCORE.");
            return sb.ToString();
        }
    }
}

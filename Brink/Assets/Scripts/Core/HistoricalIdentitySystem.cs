using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase G historical identity. Reads the history Brink already records and
    /// turns it into a compact answer to: what kind of state have we become?
    /// It is interpretive only; history never becomes a hidden national bonus.
    /// </summary>
    public static class HistoricalIdentitySystem
    {
        public sealed class Identity
        {
            public string name;
            public string basis;
            public int evidence;
        }

        public static List<Identity> Build(GameState state)
        {
            var result = new List<Identity>();
            var country = state?.PlayerCountry;
            if (country == null) return result;

            int military = 0, economic = 0, diplomatic = 0, political = 0;
            foreach (var entry in state.chronicle)
            {
                if (entry == null || entry.countryId != state.playerCountryId) continue;
                switch (entry.category)
                {
                    case ChronicleCategory.Military: military++; break;
                    case ChronicleCategory.Economic: economic++; break;
                    case ChronicleCategory.Diplomatic: diplomatic++; break;
                    case ChronicleCategory.Political: political++; break;
                }
            }

            Add(result, "SECURITY STATE", military, "military events repeatedly define the public record");
            Add(result, "COMMERCIAL POWER", economic, "economic decisions repeatedly define the public record");
            Add(result, "BROKER STATE", diplomatic, "diplomatic choices repeatedly define the public record");
            Add(result, "CONTESTED ORDER", political, "domestic and constitutional events repeatedly define the public record");

            if (country.pillars.military >= 65f) Add(result, "ARMED TRADITION", 2, "the state carries unusually deep military capacity");
            if (country.resources.industrialCapacity >= 65f) Add(result, "INDUSTRIAL TRADITION", 2, "industrial capacity remains a defining national asset");
            if (country.socialUnrest >= 65f) Add(result, "POLITICS UNDER STRAIN", 2, "high unrest has become part of the inherited political context");

            result.Sort((a, b) => b.evidence != a.evidence ? b.evidence.CompareTo(a.evidence) : string.CompareOrdinal(a.name, b.name));
            return result;
        }

        public static string Render(GameState state)
        {
            var identities = Build(state);
            if (identities.Count == 0) return "HISTORICAL IDENTITY — THE RECORD HAS NOT YET SETTLED INTO A CLEAR PATTERN.";
            var sb = new StringBuilder("HISTORICAL IDENTITY — WHAT THE STATE REMEMBERS ITSELF AS\n");
            int shown = Math.Min(4, identities.Count);
            for (int i = 0; i < shown; i++)
                sb.Append(identities[i].evidence >= 4 ? "!! " : "!  ").Append(identities[i].name).Append(" — ").AppendLine(identities[i].basis + ".");
            sb.Append("IDENTITY DESCRIBES THE RECORD. IT DOES NOT GRANT POWER OR LOCK FUTURE CHOICES.");
            return sb.ToString();
        }

        static void Add(List<Identity> list, string name, int evidence, string basis)
        {
            if (evidence < 2) return;
            list.Add(new Identity { name = name, evidence = evidence, basis = basis });
        }
    }
}

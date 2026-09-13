using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase F strategic-surprise reading. This is deliberately observational:
    /// it identifies where the player's own government is exposed to uncertainty
    /// or contradictory pressure without rolling events, changing odds, or
    /// revealing foreign truth the player has not collected.
    /// </summary>
    public static class StrategicSurpriseSystem
    {
        public sealed class Exposure
        {
            public Pillar pillar;
            public int severity;
            public string title;
            public string known;
            public string unknown;
            public string implication;
        }

        public static List<Exposure> Assess(GameState state)
        {
            var result = new List<Exposure>();
            var country = state?.PlayerCountry;
            if (country == null) return result;

            AddMilitary(state, country, result);
            AddEconomy(state, country, result);
            AddIntelligence(state, result);
            AddDiplomacy(state, result);
            AddGovernment(country, result);

            result.Sort((a, b) =>
            {
                int severity = b.severity.CompareTo(a.severity);
                return severity != 0 ? severity : ((int)a.pillar).CompareTo((int)b.pillar);
            });
            return result;
        }

        public static string Render(GameState state, int maxItems = 4)
        {
            var exposures = Assess(state);
            if (exposures.Count == 0)
                return "STRATEGIC SURPRISE — NO MATERIAL EXPOSURE IDENTIFIED FROM OUR CURRENT RECORD.";

            var sb = new StringBuilder("STRATEGIC SURPRISE — WHERE OUR PICTURE MAY BREAK\n");
            int shown = Math.Min(Math.Max(1, maxItems), exposures.Count);
            for (int i = 0; i < shown; i++)
            {
                var item = exposures[i];
                sb.Append(item.severity >= 4 ? "!! " : item.severity >= 2 ? "!  " : "   ")
                  .Append(item.pillar.ToString().ToUpperInvariant()).Append(" — ")
                  .AppendLine(item.title.ToUpperInvariant());
                sb.Append("   KNOWN: ").AppendLine(item.known);
                sb.Append("   UNKNOWN: ").AppendLine(item.unknown);
                sb.Append("   IMPLICATION: ").AppendLine(item.implication);
            }
            sb.Append("UNCERTAINTY IS NOT A SECRET REVEALED. IT IS A LIMIT ON WHAT THIS GOVERNMENT CAN SAFELY ASSUME.");
            return sb.ToString();
        }

        static void AddMilitary(GameState state, CountryState country, List<Exposure> result)
        {
            if (!state.IsAtWar(country.id) && country.warExhaustion < 55f) return;
            int severity = country.warExhaustion >= 75f ? 4 : state.IsAtWar(country.id) ? 3 : 2;
            result.Add(new Exposure
            {
                pillar = Pillar.Military,
                severity = severity,
                title = "commitment endurance",
                known = "Our own war exhaustion is " + country.warExhaustion.ToString("F0") + "/100.",
                unknown = "Enemy willingness and escalation choices remain estimates, not promises.",
                implication = "A plan that assumes the present tempo continues cleanly may fail before the battlefield does."
            });
        }

        static void AddEconomy(GameState state, CountryState country, List<Exposure> result)
        {
            if (!state.treasuryTrendSeeded || state.treasuryTrend >= -2f) return;
            int severity = state.treasuryTrend < -8f ? 4 : state.treasuryTrend < -4f ? 3 : 2;
            result.Add(new Exposure
            {
                pillar = Pillar.Economy,
                severity = severity,
                title = "fiscal room",
                known = "Our fiscal trend is " + state.treasuryTrend.ToString("F0") + " per month.",
                unknown = "Future market and foreign policy reactions are not locked to the current trend.",
                implication = "A new commitment can become unaffordable even if today's account can pay for it."
            });
        }

        static void AddIntelligence(GameState state, List<Exposure> result)
        {
            int owned = 0, compromised = 0;
            foreach (var network in state.networks)
            {
                if (network.ownerId != state.playerCountryId) continue;
                owned++;
                if (network.compromised) compromised++;
            }
            if (owned > 0 && compromised == 0) return;
            int severity = owned == 0 ? 3 : compromised >= Math.Max(1, owned / 2) ? 4 : 2;
            result.Add(new Exposure
            {
                pillar = Pillar.Intelligence,
                severity = severity,
                title = "collection blind spot",
                known = owned == 0
                    ? "We have no standing foreign collection network."
                    : compromised + " of our " + owned + " network(s) are compromised.",
                unknown = "Missing collection cannot tell us which unseen development matters most.",
                implication = "Confidence should fall before policy becomes more specific."
            });
        }

        static void AddDiplomacy(GameState state, List<Exposure> result)
        {
            int sanctions = 0;
            foreach (var sanction in state.sanctions)
                if (sanction.targetId == state.playerCountryId) sanctions++;
            if (sanctions == 0) return;
            result.Add(new Exposure
            {
                pillar = Pillar.Diplomacy,
                severity = sanctions >= 3 ? 4 : 2,
                title = "external coalition pressure",
                known = sanctions + " sanction regime(s) are publicly directed at us.",
                unknown = "Further alignment by other governments remains contingent on their own interests.",
                implication = "Treat today's diplomatic isolation as a pressure vector, not a fixed ceiling."
            });
        }

        static void AddGovernment(CountryState country, List<Exposure> result)
        {
            if (country.socialUnrest < 50f && country.governmentApproval >= 40f) return;
            int severity = country.socialUnrest >= 75f || country.governmentApproval < 25f ? 4 : 2;
            result.Add(new Exposure
            {
                pillar = Pillar.Government,
                severity = severity,
                title = "domestic consent",
                known = "Approval is " + country.governmentApproval.ToString("F0") + "; unrest is " + country.socialUnrest.ToString("F0") + ".",
                unknown = "Public tolerance for the next shock cannot be reduced to one current score.",
                implication = "Policies that are individually sustainable can become jointly unsustainable after a shock."
            });
        }
    }
}

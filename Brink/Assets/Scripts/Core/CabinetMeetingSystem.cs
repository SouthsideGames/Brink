using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase F institutional personality. A cabinet meeting is a read-only clash
    /// of the government's existing officials: who is pressing for attention,
    /// where they disagree, and whose advice carries institutional weight.
    /// It does not invent hidden facts or create a second decision economy.
    /// </summary>
    public static class CabinetMeetingSystem
    {
        public sealed class Position
        {
            public Pillar pillar;
            public string officialName;
            public string title;
            public string position;
            public string concern;
            public int pressure;
        }

        public static List<Position> Build(GameState state)
        {
            var result = new List<Position>();
            var country = state?.PlayerCountry;
            if (country?.cabinet == null) return result;

            foreach (var official in country.cabinet)
            {
                if (official == null) continue;
                result.Add(new Position
                {
                    pillar = official.office,
                    officialName = official.displayName ?? "UNNAMED OFFICIAL",
                    title = official.title ?? official.office.ToString(),
                    position = PositionText(state, country, official),
                    concern = ConcernText(state, country, official.office),
                    pressure = Pressure(state, country, official)
                });
            }

            result.Sort((a, b) =>
            {
                int p = b.pressure.CompareTo(a.pressure);
                return p != 0 ? p : ((int)a.pillar).CompareTo((int)b.pillar);
            });
            return result;
        }

        public static string Render(GameState state)
        {
            var positions = Build(state);
            if (positions.Count == 0) return "CABINET MEETING — NO GOVERNMENT FORMED.";
            var sb = new StringBuilder();
            sb.AppendLine("CABINET MEETING — COMPETING PRIORITIES");
            foreach (var item in positions)
            {
                sb.Append(item.pressure >= 3 ? "!! " : item.pressure == 2 ? "!  " : "   ")
                  .Append(item.title.ToUpperInvariant()).Append(" — ")
                  .Append(item.officialName.ToUpperInvariant()).AppendLine();
                sb.Append("   ").Append(item.position).AppendLine();
                sb.Append("   CONCERN: ").Append(item.concern).AppendLine();
            }
            sb.Append("THIS IS ADVICE, NOT CONSENSUS. THE GOVERNMENT MAY WANT INCOMPATIBLE THINGS.");
            return sb.ToString();
        }

        static string PositionText(GameState state, CountryState country, Official official)
        {
            if (official.mode == ControlMode.Directed && !string.IsNullOrEmpty(official.directiveId))
                return "Working to your standing instruction: " + official.directiveId + ".";

            switch (official.office)
            {
                case Pillar.Military:
                    return country.warExhaustion > 60f
                        ? "The force can keep acting, but political endurance is becoming the constraint."
                        : "Preserve readiness and room to respond before accepting new commitments.";
                case Pillar.Economy:
                    return state.treasuryTrendSeeded && state.treasuryTrend < -4f
                        ? "The current fiscal path is consuming strategic freedom faster than it creates it."
                        : "Protect growth and reserves; every other pillar eventually sends us a bill.";
                case Pillar.Intelligence:
                    return "Do not convert uncertainty into confidence merely because a decision is urgent.";
                case Pillar.Diplomacy:
                    return "Spend credibility deliberately; partners remember demands as well as promises.";
                default:
                    return country.socialUnrest > 55f
                        ? "Domestic consent is becoming an operational constraint on the entire government."
                        : "Preserve authority and public tolerance for decisions that may become necessary later.";
            }
        }

        static string ConcernText(GameState state, CountryState country, Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military: return "War exhaustion " + country.warExhaustion.ToString("F0") + "/100.";
                case Pillar.Economy:
                    return state.treasuryTrendSeeded
                        ? "Treasury trend " + (state.treasuryTrend >= 0 ? "+" : "") + state.treasuryTrend.ToString("F0") + "/month."
                        : "Fiscal trend not yet established.";
                case Pillar.Intelligence:
                    int networks = 0;
                    foreach (var n in state.networks) if (n.ownerId == state.playerCountryId && !n.compromised) networks++;
                    return networks + " uncompromised foreign network(s).";
                case Pillar.Diplomacy:
                    int sanctions = 0;
                    foreach (var s in state.sanctions) if (s.targetId == state.playerCountryId) sanctions++;
                    return sanctions + " sanction regime(s) currently directed at us.";
                default: return "Approval " + country.governmentApproval.ToString("F0") + "; unrest " + country.socialUnrest.ToString("F0") + ".";
            }
        }

        static int Pressure(GameState state, CountryState country, Official official)
        {
            int score = 0;
            switch (official.office)
            {
                case Pillar.Military:
                    if (country.warExhaustion > 55f) score++;
                    if (country.warExhaustion > 75f) score++;
                    if (state.ActiveConfrontation != null && !state.ActiveConfrontation.resolved) score++;
                    break;
                case Pillar.Economy:
                    if (state.treasuryTrendSeeded && state.treasuryTrend < -4f) score += 2;
                    if (country.resources.treasury < 0f) score++;
                    break;
                case Pillar.Intelligence:
                    foreach (var n in state.networks)
                        if (n.ownerId == state.playerCountryId && n.compromised) { score += 2; break; }
                    break;
                case Pillar.Diplomacy:
                    foreach (var s in state.sanctions)
                        if (s.targetId == state.playerCountryId) { score += 2; break; }
                    break;
                default:
                    if (country.governmentApproval < 40f) score++;
                    if (country.socialUnrest > 55f) score += 2;
                    break;
            }
            if (official.trust < 35f) score++;
            return Math.Min(4, score);
        }
    }
}

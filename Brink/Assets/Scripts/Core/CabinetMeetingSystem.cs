using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase F cabinet meeting: the same five offices now arrive with stable
    /// institutional identities, relationship posture and visible disagreements.
    /// Read-only: this explains the government the player already has.
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
            public string identity;
            public string relationship;
            public int resistance;
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
                var profile = InstitutionalPersonalitySystem.ProfileFor(state, official);
                result.Add(new Position
                {
                    pillar = official.office,
                    officialName = official.displayName ?? "UNNAMED OFFICIAL",
                    title = official.title ?? official.office.ToString(),
                    position = PositionText(state, country, official),
                    concern = ConcernText(state, country, official.office),
                    identity = profile?.identity ?? "institutional voice",
                    relationship = profile?.relationship ?? "relationship unknown",
                    resistance = profile?.resistance ?? 0,
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
                sb.Append("   ").Append(item.identity.ToUpperInvariant()).Append(" | ").Append(item.relationship).AppendLine();
                sb.Append("   ").Append(item.position).AppendLine();
                sb.Append("   CONCERN: ").Append(item.concern).AppendLine();
                if (item.resistance >= 2)
                    sb.Append("   FRICTION: This office is increasingly protective of its own judgement.").AppendLine();
            }

            var frictions = InstitutionalPersonalitySystem.Frictions(state);
            if (frictions.Count > 0)
            {
                sb.AppendLine("CABINET FAULT LINES");
                int shown = Math.Min(3, frictions.Count);
                for (int i = 0; i < shown; i++)
                    sb.Append(frictions[i].intensity >= 4 ? "!! " : "!  ").AppendLine(frictions[i].summary);
            }
            sb.Append("THIS IS ADVICE, NOT CONSENSUS. THE GOVERNMENT MAY WANT INCOMPATIBLE THINGS.");
            return sb.ToString();
        }

        static string PositionText(GameState state, CountryState country, Official official)
        {
            if (official.mode == ControlMode.Directed && !string.IsNullOrEmpty(official.directiveId))
                return "Working to your standing instruction: " + InstitutionalPersonalitySystem.DirectiveLabel(official) + ".";

            var profile = InstitutionalPersonalitySystem.ProfileFor(state, official);
            if (profile != null) return char.ToUpperInvariant(profile.instinct[0]) + profile.instinct.Substring(1) + ".";

            switch (official.office)
            {
                case Pillar.Military: return "Preserve readiness and room to respond before accepting new commitments.";
                case Pillar.Economy: return "Protect growth and reserves; every other pillar eventually sends us a bill.";
                case Pillar.Intelligence: return "Do not convert uncertainty into confidence merely because a decision is urgent.";
                case Pillar.Diplomacy: return "Spend credibility deliberately; partners remember demands as well as promises.";
                default: return "Preserve authority and public tolerance for decisions that may become necessary later.";
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

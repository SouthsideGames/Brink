using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Reads important player-country choices through the Cabinet's existing
    /// personalities. This does not change action resolution or minister stats;
    /// it tells the operator which offices are likely to support or resist a
    /// contemplated strategic direction and why.
    /// </summary>
    public static class CabinetChoiceReadingSystem
    {
        public enum ChoiceKind
        {
            EscalateWar,
            SeekPeace,
            ExpandSpending,
            FiscalRestraint,
            CovertRisk,
            DiplomaticCompromise,
            RestrictDomesticSpace,
            LiberalizeDomesticSpace
        }

        public sealed class Voice
        {
            public Pillar pillar;
            public string officialName;
            public int stance; // -2 resist .. +2 support
            public string reason;
        }

        public static List<Voice> Read(GameState state, ChoiceKind choice)
        {
            var result = new List<Voice>();
            var cabinet = state?.PlayerCountry?.cabinet;
            if (cabinet == null) return result;

            foreach (var official in cabinet)
            {
                if (official == null) continue;
                int stance = BaseStance(official.office, choice);
                if (IsRiskSeeking(choice))
                {
                    if (official.riskTolerance >= 70f) stance++;
                    else if (official.riskTolerance <= 30f) stance--;
                }
                else if (IsRiskReducing(choice))
                {
                    if (official.riskTolerance <= 30f) stance++;
                    else if (official.riskTolerance >= 75f) stance--;
                }

                var profile = InstitutionalPersonalitySystem.ProfileFor(state, official);
                if (official.trust < 30f && Math.Abs(stance) > 0)
                    stance += stance > 0 ? -1 : 1; // strained offices are less willing to become cheerleaders.

                stance = Math.Max(-2, Math.Min(2, stance));
                result.Add(new Voice
                {
                    pillar = official.office,
                    officialName = official.displayName ?? official.office.ToString(),
                    stance = stance,
                    reason = Reason(profile, official, choice, stance)
                });
            }

            result.Sort((a, b) =>
            {
                int strength = Math.Abs(b.stance).CompareTo(Math.Abs(a.stance));
                return strength != 0 ? strength : ((int)a.pillar).CompareTo((int)b.pillar);
            });
            return result;
        }

        public static string Render(GameState state, ChoiceKind choice)
        {
            var voices = Read(state, choice);
            if (voices.Count == 0) return "CABINET READING — NO GOVERNMENT FORMED.";
            var sb = new StringBuilder("CABINET READING — ")
                .Append(choice.ToString().ToUpperInvariant()).AppendLine();
            foreach (var voice in voices)
            {
                string mark = voice.stance >= 2 ? "++" : voice.stance == 1 ? "+ " : voice.stance <= -2 ? "--" : voice.stance == -1 ? "- " : "= ";
                sb.Append(mark).Append(' ').Append(voice.pillar.ToString().ToUpperInvariant())
                  .Append(" — ").Append(voice.officialName.ToUpperInvariant()).AppendLine();
                sb.Append("   ").AppendLine(voice.reason);
            }
            sb.Append("THIS IS EXPECTED INSTITUTIONAL REACTION, NOT A VOTE AND NOT A VETO.");
            return sb.ToString();
        }

        static int BaseStance(Pillar pillar, ChoiceKind choice)
        {
            switch (choice)
            {
                case ChoiceKind.EscalateWar:
                    if (pillar == Pillar.Military) return 1;
                    if (pillar == Pillar.Economy || pillar == Pillar.Government) return -1;
                    break;
                case ChoiceKind.SeekPeace:
                    if (pillar == Pillar.Diplomacy || pillar == Pillar.Economy) return 1;
                    break;
                case ChoiceKind.ExpandSpending:
                    if (pillar == Pillar.Government || pillar == Pillar.Military) return 1;
                    if (pillar == Pillar.Economy) return -2;
                    break;
                case ChoiceKind.FiscalRestraint:
                    if (pillar == Pillar.Economy) return 2;
                    if (pillar == Pillar.Government || pillar == Pillar.Military) return -1;
                    break;
                case ChoiceKind.CovertRisk:
                    if (pillar == Pillar.Intelligence) return 1;
                    if (pillar == Pillar.Diplomacy) return -1;
                    break;
                case ChoiceKind.DiplomaticCompromise:
                    if (pillar == Pillar.Diplomacy || pillar == Pillar.Economy) return 1;
                    if (pillar == Pillar.Military) return -1;
                    break;
                case ChoiceKind.RestrictDomesticSpace:
                    if (pillar == Pillar.Government) return 1;
                    if (pillar == Pillar.Diplomacy) return -1;
                    break;
                case ChoiceKind.LiberalizeDomesticSpace:
                    if (pillar == Pillar.Diplomacy || pillar == Pillar.Government) return 1;
                    break;
            }
            return 0;
        }

        static bool IsRiskSeeking(ChoiceKind choice)
            => choice == ChoiceKind.EscalateWar || choice == ChoiceKind.ExpandSpending || choice == ChoiceKind.CovertRisk || choice == ChoiceKind.RestrictDomesticSpace;

        static bool IsRiskReducing(ChoiceKind choice)
            => choice == ChoiceKind.SeekPeace || choice == ChoiceKind.FiscalRestraint || choice == ChoiceKind.DiplomaticCompromise;

        static string Reason(InstitutionalPersonalitySystem.Profile profile, Official official, ChoiceKind choice, int stance)
        {
            string posture = profile?.identity ?? "institutional voice";
            string direction = stance > 0 ? "leans toward" : stance < 0 ? "pushes against" : "does not take a strong institutional position on";
            return posture + "; " + direction + " " + Human(choice) + ". " +
                   (official.trust < 35f ? "The relationship with the operator is already strained." : "The relationship does not itself dominate the advice.");
        }

        static string Human(ChoiceKind choice)
        {
            switch (choice)
            {
                case ChoiceKind.EscalateWar: return "accepting more military risk";
                case ChoiceKind.SeekPeace: return "reducing the current military commitment";
                case ChoiceKind.ExpandSpending: return "accepting a larger fiscal commitment";
                case ChoiceKind.FiscalRestraint: return "protecting fiscal room";
                case ChoiceKind.CovertRisk: return "accepting covert escalation risk";
                case ChoiceKind.DiplomaticCompromise: return "trading position for diplomatic room";
                case ChoiceKind.RestrictDomesticSpace: return "using more restrictive domestic authority";
                default: return "opening more domestic political space";
            }
        }
    }
}

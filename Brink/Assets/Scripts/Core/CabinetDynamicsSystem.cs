using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase F cabinet dynamics: interprets the five existing officials as a group.
    /// Coalitions are descriptive, not a new faction resource, and never veto an
    /// operator command. They tell the player where support or resistance is likely
    /// to form before choosing to intervene.
    /// </summary>
    public static class CabinetDynamicsSystem
    {
        public sealed class Alignment
        {
            public Pillar first;
            public Pillar second;
            public int strength;
            public string basis;
        }

        public sealed class InterventionReading
        {
            public Pillar pillar;
            public int resistance;
            public string reaction;
            public string cabinetEffect;
        }

        public static List<Alignment> Alignments(GameState state)
        {
            var result = new List<Alignment>();
            var cabinet = state?.PlayerCountry?.cabinet;
            if (cabinet == null) return result;

            for (int i = 0; i < cabinet.Count; i++)
            for (int j = i + 1; j < cabinet.Count; j++)
            {
                var a = cabinet[i]; var b = cabinet[j];
                if (a == null || b == null) continue;
                int strength = AlignmentStrength(a, b);
                if (strength < 2) continue;
                result.Add(new Alignment
                {
                    first = a.office,
                    second = b.office,
                    strength = strength,
                    basis = AlignmentBasis(a, b)
                });
            }

            result.Sort((a, b) => b.strength != a.strength
                ? b.strength.CompareTo(a.strength)
                : ((int)a.first).CompareTo((int)b.first));
            return result;
        }

        public static InterventionReading ReadIntervention(GameState state, Pillar pillar)
        {
            var official = state?.PlayerCountry?.FindOfficial(pillar);
            var profile = InstitutionalPersonalitySystem.ProfileFor(state, official);
            if (official == null || profile == null) return null;

            int allies = 0;
            foreach (var alignment in Alignments(state))
                if (alignment.first == pillar || alignment.second == pillar) allies++;

            string reaction;
            if (profile.resistance >= 4) reaction = "expects another bypass and will defend the office's ground";
            else if (profile.resistance >= 2) reaction = "will comply, but the intervention deepens an already strained relationship";
            else if (official.trust >= 70f) reaction = "is likely to treat intervention as exceptional rather than hostile";
            else reaction = "will comply without treating the intervention as routine";

            string effect = allies >= 2
                ? "This office has multiple natural allies in Cabinet; bypassing it will be noticed beyond one desk."
                : allies == 1
                    ? "One other office is naturally aligned with this minister's instincts."
                    : "The dispute is likely to remain concentrated in this office.";

            return new InterventionReading
            {
                pillar = pillar,
                resistance = profile.resistance,
                reaction = reaction,
                cabinetEffect = effect
            };
        }

        public static string Render(GameState state)
        {
            var alignments = Alignments(state);
            if (alignments.Count == 0) return "CABINET ALIGNMENTS — NO DURABLE ALIGNMENT IS DOMINANT.";
            var sb = new StringBuilder("CABINET ALIGNMENTS — NATURAL ALLIES\n");
            int count = Math.Min(4, alignments.Count);
            for (int i = 0; i < count; i++)
            {
                var a = alignments[i];
                sb.Append(a.strength >= 4 ? "!! " : "!  ")
                  .Append(a.first.ToString().ToUpperInvariant()).Append(" + ")
                  .Append(a.second.ToString().ToUpperInvariant()).Append(" — ")
                  .Append(a.basis).AppendLine();
            }
            sb.Append("ALIGNMENT SHAPES ADVICE AND REACTION; IT DOES NOT REMOVE YOUR AUTHORITY.");
            return sb.ToString();
        }

        static int AlignmentStrength(Official a, Official b)
        {
            int score = 0;
            float riskGap = Math.Abs(a.riskTolerance - b.riskTolerance);
            if (riskGap <= 20f) score++;
            if (riskGap <= 10f) score++;
            if (a.trust >= 55f && b.trust >= 55f) score++;
            if (a.mode == b.mode) score++;
            if (NaturalPair(a.office, b.office)) score++;
            return Math.Min(4, score);
        }

        static bool NaturalPair(Pillar a, Pillar b)
        {
            return Pair(a, b, Pillar.Military, Pillar.Intelligence)
                || Pair(a, b, Pillar.Economy, Pillar.Government)
                || Pair(a, b, Pillar.Diplomacy, Pillar.Intelligence)
                || Pair(a, b, Pillar.Diplomacy, Pillar.Economy);
        }

        static bool Pair(Pillar a, Pillar b, Pillar x, Pillar y)
            => (a == x && b == y) || (a == y && b == x);

        static string AlignmentBasis(Official a, Official b)
        {
            if (NaturalPair(a.office, b.office) && Math.Abs(a.riskTolerance - b.riskTolerance) <= 20f)
                return "shared institutional interests and a similar appetite for risk.";
            if (Math.Abs(a.riskTolerance - b.riskTolerance) <= 10f)
                return "a similar appetite for risk makes their advice converge.";
            if (a.mode == ControlMode.Directed && b.mode == ControlMode.Directed)
                return "both offices are currently working inside operator-set priorities.";
            return "their present working posture is unusually compatible.";
        }
    }
}

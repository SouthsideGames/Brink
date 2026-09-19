using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase F: turns existing cabinet traits and relationship state into stable,
    /// legible institutional voices. This layer is read-only: it interprets facts
    /// already owned by the player's government and never creates a second resource
    /// economy or reaches through fog into foreign cabinets.
    /// </summary>
    public static class InstitutionalPersonalitySystem
    {
        public sealed class Profile
        {
            public Pillar pillar;
            public string identity;
            public string temperament;
            public string relationship;
            public string instinct;
            public string continuity;
            public int resistance;
        }

        public sealed class Friction
        {
            public Pillar first;
            public Pillar second;
            public string summary;
            public int intensity;
        }

        public static Profile ProfileFor(GameState state, Official official)
        {
            if (state == null || official == null || state.PlayerCountry?.FindOfficial(official.office) != official)
                return null;

            return new Profile
            {
                pillar = official.office,
                identity = Identity(official),
                temperament = Temperament(official),
                relationship = Relationship(official),
                instinct = Instinct(state, official),
                continuity = ContinuityFor(official),
                resistance = Resistance(official)
            };
        }

        /// <summary>
        /// How deeply the current officeholder's habits have become the
        /// institution's habits. Derived from the tenure already in the save:
        /// no second meter, migration or hidden bonus.
        /// </summary>
        public static string ContinuityFor(Official official)
        {
            int months = Math.Max(0, official?.monthsInOffice ?? 0);
            if (months < 12) return "newly appointed — the office is still forming around them";
            if (months < 48) return "settled in office — routines are taking hold";
            if (months < 96) return "established command — the institution knows their methods";
            return "entrenched command — the office carries their imprint";
        }

        public static List<Friction> Frictions(GameState state)
        {
            var result = new List<Friction>();
            var cabinet = state?.PlayerCountry?.cabinet;
            if (cabinet == null) return result;

            for (int i = 0; i < cabinet.Count; i++)
            for (int j = i + 1; j < cabinet.Count; j++)
            {
                var a = cabinet[i]; var b = cabinet[j];
                if (a == null || b == null) continue;
                int intensity = FrictionScore(a, b);
                if (intensity < 2) continue;
                result.Add(new Friction
                {
                    first = a.office,
                    second = b.office,
                    intensity = intensity,
                    summary = FrictionText(a, b, intensity)
                });
            }

            result.Sort((a, b) => b.intensity != a.intensity
                ? b.intensity.CompareTo(a.intensity)
                : ((int)a.first).CompareTo((int)b.first));
            return result;
        }

        public static string DirectiveLabel(Official official)
        {
            if (official == null || string.IsNullOrEmpty(official.directiveId)) return "standing instruction";
            foreach (var d in CabinetSystem.GetDirectives(official.office))
                if (d.id == official.directiveId) return d.label;
            return official.directiveId.Replace('_', ' ');
        }

        static string Identity(Official o)
        {
            if (o.competence >= 78f && o.riskTolerance >= 62f) return "activist institution-builder";
            if (o.competence >= 78f && o.riskTolerance < 45f) return "technocratic steward";
            if (o.loyalty >= 72f && o.riskTolerance < 55f) return "administration loyalist";
            if (o.riskTolerance >= 72f) return "strategic gambler";
            if (o.loyalty < 38f) return "independent institutionalist";
            return "pragmatic operator";
        }

        static string Temperament(Official o)
        {
            if (o.riskTolerance >= 70f) return "pushes before certainty";
            if (o.riskTolerance <= 32f) return "demands margin before commitment";
            if (o.competence >= 75f) return "confident in the institution's own judgement";
            return "prefers bounded commitments";
        }

        static string Relationship(Official o)
        {
            if (o.trust >= 75f) return "trusts the operator";
            if (o.trust >= 50f) return "working relationship is sound";
            if (o.trust >= 30f) return "relationship is strained";
            return "expects intervention and protects institutional ground";
        }

        static int Resistance(Official o)
        {
            int score = 0;
            if (o.trust < 50f) score++;
            if (o.trust < 30f) score++;
            if (o.loyalty < 45f) score++;
            if (o.competence > 75f && o.trust < 45f) score++;
            if (o.mode == ControlMode.DirectControl) score++;
            if (o.monthsInOffice >= 96 && o.trust < 50f) score++;
            return Math.Min(4, score);
        }

        static string Instinct(GameState state, Official o)
        {
            var c = state.PlayerCountry;
            switch (o.office)
            {
                case Pillar.Military:
                    return c.warExhaustion > 60f ? "reduce commitments before endurance breaks" : "preserve readiness and freedom of action";
                case Pillar.Economy:
                    return state.treasuryTrendSeeded && state.treasuryTrend < -4f ? "restore fiscal room before accepting new bills" : "protect growth and reserves";
                case Pillar.Intelligence:
                    return "buy information before buying certainty";
                case Pillar.Diplomacy:
                    return "preserve credibility and optionality";
                default:
                    return c.socialUnrest > 55f ? "restore domestic consent before expanding commitments" : "preserve authority for the next crisis";
            }
        }

        static int FrictionScore(Official a, Official b)
        {
            int score = 0;
            float riskGap = Math.Abs(a.riskTolerance - b.riskTolerance);
            if (riskGap >= 30f) score++;
            if (riskGap >= 50f) score++;
            if (a.trust < 40f || b.trust < 40f) score++;
            if (a.mode == ControlMode.Directed ^ b.mode == ControlMode.Directed) score++;
            if ((a.office == Pillar.Military && b.office == Pillar.Economy) || (b.office == Pillar.Military && a.office == Pillar.Economy)) score++;
            return Math.Min(4, score);
        }

        static string FrictionText(Official a, Official b, int intensity)
        {
            string edge = Math.Abs(a.riskTolerance - b.riskTolerance) >= 30f
                ? "disagree on how much risk the government should carry"
                : "are protecting different institutional priorities";
            return a.office + " and " + b.office + " " + edge + (intensity >= 4 ? "; the split is becoming structural." : ".");
        }
    }
}

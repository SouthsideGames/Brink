using System;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Names the current strategic era from the operator's standing doctrine and
    /// the conditions under which it is being practiced. Era names are narrative
    /// handles for the Chronicle, not modifiers.
    /// </summary>
    public static class StrategicEraSystem
    {
        public sealed class Era
        {
            public string name;
            public string doctrine;
            public string character;
            public GameDate since;
        }

        public static Era Current(GameState state)
        {
            var strategy = state?.mandate?.strategy;
            if (state?.PlayerCountry == null || strategy == null || !strategy.doctrineChosen) return null;
            string prefix = PressurePrefix(state);
            return new Era
            {
                name = prefix + DoctrineName(strategy.doctrine),
                doctrine = strategy.doctrine.ToString(),
                character = Character(strategy.doctrine, prefix),
                since = strategy.doctrineAdopted
            };
        }

        public static string Render(GameState state)
        {
            var era = Current(state);
            if (era == null) return "STRATEGIC ERA — NO STANDING DOCTRINE HAS YET DEFINED THIS POSTING.";
            var sb = new StringBuilder("STRATEGIC ERA — ").AppendLine(era.name.ToUpperInvariant());
            sb.Append("DOCTRINE: ").AppendLine(era.doctrine.ToUpperInvariant());
            sb.Append("CHARACTER: ").AppendLine(era.character);
            sb.Append("SINCE: ").Append(era.since.ToString());
            return sb.ToString();
        }

        static string PressurePrefix(GameState state)
        {
            var c = state.PlayerCountry;
            if (state.IsAtWar(c.id) || c.warExhaustion >= 65f) return "Wartime ";
            if (c.socialUnrest >= 65f || c.governmentApproval < 30f) return "Crisis ";
            if (state.treasuryTrendSeeded && state.treasuryTrend < -6f) return "Austerity ";
            return "";
        }

        static string DoctrineName(StrategicDoctrine doctrine)
        {
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence: return "Shield Era";
                case StrategicDoctrine.Prosperity: return "Growth Era";
                case StrategicDoctrine.Influence: return "Reach Era";
                case StrategicDoctrine.Resilience: return "Hardening Era";
                case StrategicDoctrine.Transformation: return "Reconstruction Era";
                default: return "Stewardship Era";
            }
        }

        static string Character(StrategicDoctrine doctrine, string prefix)
        {
            string baseText;
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence: baseText = "security and readiness organize delegated government attention"; break;
                case StrategicDoctrine.Prosperity: baseText = "growth and economic room organize delegated government attention"; break;
                case StrategicDoctrine.Influence: baseText = "external leverage and relationships organize delegated government attention"; break;
                case StrategicDoctrine.Resilience: baseText = "redundancy and endurance organize delegated government attention"; break;
                case StrategicDoctrine.Transformation: baseText = "institutional change organizes delegated government attention"; break;
                default: baseText = "balanced stewardship organizes delegated government attention"; break;
            }
            return string.IsNullOrEmpty(prefix) ? baseText + "." : baseText + " under sustained national pressure.";
        }
    }
}

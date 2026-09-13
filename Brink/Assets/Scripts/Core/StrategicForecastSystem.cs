using System;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Read-only strategic forecast. It never advances the simulation and never
    /// rolls RNG. Arithmetic extrapolations are labelled as such; everything the
    /// independent world can change is described as exposure rather than promise.
    /// </summary>
    public static class StrategicForecastSystem
    {
        public static string Render(GameState state, StrategicDoctrine doctrine,
            int horizonMonths, int width)
        {
            var country = state?.PlayerCountry;
            if (country == null) return "NO FORECAST AVAILABLE.";
            horizonMonths = ClampHorizon(horizonMonths);
            width = Math.Max(40, width);

            var sb = new StringBuilder();
            sb.AppendLine($"WHAT-IF: {StrategySystem.DoctrineLabel(doctrine).ToUpperInvariant()} — {horizonMonths} MONTHS");
            sb.AppendLine("MODEL: current-course arithmetic + exact Cabinet intent; world reactions are not simulated here.");
            sb.AppendLine();

            if (state.treasuryTrendSeeded)
            {
                float projectedTreasury = country.resources.treasury + state.treasuryTrend * horizonMonths;
                sb.AppendLine($"TREASURY  {country.resources.treasury:F0} → ~{projectedTreasury:F0}  " +
                              $"at current {state.treasuryTrend:+0;-0;0}/month balance trend");
                if (state.treasuryTrend < -1f && projectedTreasury < 0f)
                    sb.AppendLine("  YES, BUT: current spending crosses the cash line inside this horizon.");
            }
            else sb.AppendLine("TREASURY  insufficient resolved-month history for a trend projection.");

            float years = horizonMonths / 12f;
            double growthBase = Math.Max(0.01, 1.0 + country.economy.growthRate / 100.0);
            double projectedGdp = country.economy.gdp * Math.Pow(growthBase, years);
            sb.AppendLine($"GDP       {country.economy.gdp:F0} → ~{projectedGdp:F0}  " +
                          $"if the current {country.economy.growthRate:+0.0;-0.0;0.0}% annualized rate held");

            sb.AppendLine();
            sb.AppendLine("DELEGATED INTENT UNDER THIS DOCTRINE");
            foreach (Pillar pillar in Enum.GetValues(typeof(Pillar)))
            {
                string label = StrategyCabinetBridge.PreviewLabel(doctrine, pillar);
                sb.AppendLine($"  {pillar.ToString().ToUpperInvariant(),-12} {label}");
            }

            sb.AppendLine();
            sb.AppendLine("KNOWN EXPOSURES");
            int wars = 0;
            foreach (var confrontation in state.confrontations)
                if (!confrontation.resolved && confrontation.Involves(state.playerCountryId)) wars++;
            int sanctions = 0;
            foreach (var sanction in state.sanctions)
                if (sanction.senderId == state.playerCountryId || sanction.targetId == state.playerCountryId) sanctions++;
            int openCrises = state.activeCrises.Count;
            sb.AppendLine($"  ACTIVE FRONTS {wars}   SANCTIONS INVOLVING US {sanctions}   OPEN CRISES {openCrises}");

            string tradeoff = Tradeoff(doctrine);
            if (!string.IsNullOrEmpty(tradeoff)) sb.AppendLine("  YES, BUT: " + tradeoff);

            var plan = StrategySystem.Ensure(state);
            if (plan != null && plan.objectives.Count > 0)
            {
                int met = 0;
                foreach (var objective in plan.objectives) if (objective.achieved) met++;
                sb.AppendLine($"  STANDING OBJECTIVES CURRENTLY MET {met}/{plan.objectives.Count}");
            }

            sb.AppendLine();
            sb.Append("CONFIDENCE: HIGH on our own arithmetic; MEDIUM on delegated intent; " +
                      "LOW on the independent world's response. This is a staff estimate, not a future save-state preview.");
            return Brink.UI.AsciiChart.WrapBlock(sb.ToString(), width);
        }

        public static int ClampHorizon(int months)
        {
            if (months <= 12) return 12;
            if (months <= 36) return 36;
            if (months <= 60) return 60;
            return 120;
        }

        static string Tradeoff(StrategicDoctrine doctrine)
        {
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence:
                    return "force shortfalls are filled through real procurement, which consumes treasury and gives the economy less room.";
                case StrategicDoctrine.Prosperity:
                    return "growth receives the margin while military expansion is deliberately conserved.";
                case StrategicDoctrine.Influence:
                    return "diplomacy and collection receive attention while independent force growth is restrained.";
                case StrategicDoctrine.Resilience:
                    return "internal stability and hardening take precedence over external diplomatic expansion.";
                case StrategicDoctrine.Transformation:
                    return "economic and intelligence modernization is favoured while the government's attention is tied up managing change.";
                default:
                    return "no desk gets a standing strategic preference; the Cabinet's ordinary judgement dominates.";
            }
        }
    }
}
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

            var plan = state.mandate?.strategy;
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

        /// <summary>Staff judgment from public commitments and collected reports, never a future-state simulation.</summary>
        public static string SanctionsAssessment(GameState state, string targetId, SanctionSeverity severity, int width)
        {
            if (state?.PlayerCountry == null || targetId == state.playerCountryId
                || state.FindCountry(targetId) == null || !Enum.IsDefined(typeof(SanctionSeverity), severity))
                return "NO SANCTIONS ASSESSMENT AVAILABLE.";
            var relation = state.FindRelationship(state.playerCountryId, targetId);
            var sb = new StringBuilder("CABINET ASSESSMENT — PROPOSED " + severity.ToString().ToUpperInvariant() + " MEASURES\n");
            if (!EconomySystem.CanImposeSanctions(state, targetId, severity, out string reason))
                sb.AppendLine("UNAVAILABLE: " + reason);
            if (relation?.sanctionsTruceMonths > 0)
                sb.AppendLine("DÉTENTE: imposition is refused while the bilateral truce runs.");
            if (state.FindSanction(state.playerCountryId, targetId) != null)
            {
                sb.AppendLine("Existing measures are not replaced or stacked by this order.");
                return Brink.UI.AsciiChart.WrapBlock(sb.ToString(), Math.Max(20, width));
            }
            var proposed = new Sanction { senderId = state.playerCountryId, targetId = targetId, severity = severity };
            float blowback = EconomySystem.SanctionBlowbackTerm(state, state.playerCountryId, proposed);
            var economic = IntelligenceSystem.GetEstimate(state, state.playerCountryId, targetId, IntelDomain.Economic);
            var military = IntelligenceSystem.GetEstimate(state, state.playerCountryId, targetId, IntelDomain.Military);
            sb.AppendLine("TARGET ECONOMIC DAMAGE: " + Judgment(state, Pillar.Economy, targetId, proposed.Weight)
                + " pressure assessment, not a GDP-loss prediction.");
            sb.AppendLine("Initial sanction-pressure weight " + proposed.Weight.ToString("F2")
                + "; adaptation reduces this term over time. Other economic effects remain world-dependent.");
            sb.AppendLine("TARGET CAPACITY: " + Report(economic));
            sb.AppendLine("DOMESTIC BLOWBACK: " + Judgment(state, Pillar.Economy, targetId, blowback)
                + "; current own-cost term " + blowback.ToString("F2") + " (not a treasury bill).");
            var link = state.FindTrade(state.playerCountryId, targetId);
            bool closed = link == null || link.embargoed || state.FindSanction(targetId, state.playerCountryId) != null;
            sb.AppendLine(link == null ? "TRADE: no bilateral link to close."
                : link.focus == TradeFocus.General ? "TRADE: this general link carries no commodity supply; trade-health effects are separate."
                : closed ? "TRADE: commodity supply is already closed; do not count a second supply loss."
                : "TRADE: bilateral commodity supply closes in both directions, even below Severe. This is additional to pressure and blowback.");
            sb.AppendLine("RETALIATION: " + (military == null || military.confidence == ConfidenceGrade.None
                ? "UNCERTAIN — no collected military assessment."
                : Judgment(state, Pillar.Intelligence, targetId,
                    (relation != null && relation.relations < EconomySystem.SanctionHostilityLine ? 1.5f : 0.5f)
                    + military.reportedValue / 100f) + " staff concern, not a probability; reported military " + Report(military)));
            sb.AppendLine("ALLIED SUPPORT: " + (CouncilSystem.SanctionsMandated(state, targetId)
                ? "a current chamber mandate reduces our modeled blowback. No further participation promised."
                : "UNCERTAIN — no chamber mandate. Alliances do not automatically join this order."));
            var front = state.ActiveConfrontationFor(state.playerCountryId);
            sb.AppendLine("ESCALATION: " + (front != null && !front.resolved && front.Involves(targetId)
                ? "adds pressure to the existing confrontation; no next escalation level is guaranteed."
                : "this order does not itself open a confrontation. Hostility can shape later choices."));
            sb.Append("CONFIDENCE: exact on stated current-rule terms; staff bands can be wrong. "
                + "Foreign reactions are not simulated. Collection dates and bands describe reports, not hidden truth.");
            return Brink.UI.AsciiChart.WrapBlock(sb.ToString(), Math.Max(20, width));
        }

        static string Report(IntelEstimate estimate)
            => estimate == null || estimate.confidence == ConfidenceGrade.None ? "NO ASSESSMENT"
                : estimate.RangeText + " / " + estimate.confidence + " as of " + estimate.asOf.DisplayString;

        static string Judgment(GameState state, Pillar pillar, string targetId, float pressure)
        {
            var official = state.PlayerCountry.FindOfficial(pillar);
            if (official == null) return "UNCERTAIN (desk vacant)";
            // Stable staff misreading of known inputs, not a calibrated outcome model.
            float noise = ((Hash.Of(official.id + ":" + targetId) & 65535) / 65535f * 2f - 1f)
                * (1f - Math.Max(0f, Math.Min(1f, official.competence / 100f)));
            float reading = pressure + noise;
            return reading < 0.8f ? "LOW" : reading < 2f ? "MODERATE" : "HIGH";
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

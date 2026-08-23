using Brink.Core;
using Brink.Data;

namespace Brink.UI
{
    /// <summary>
    /// Renders foreign capability the way the operator actually receives it —
    /// as a range with a confidence grade, or "NO ASSESSMENT" (GDD §14).
    /// Views must never print a foreign country's true pillar value.
    /// </summary>
    public static class IntelReadout
    {
        /// <summary>Maps a pillar to the collection domain that assesses it, if any.</summary>
        public static bool TryDomainFor(Pillar pillar, out IntelDomain domain)
        {
            switch (pillar)
            {
                case Pillar.Military: domain = IntelDomain.Military; return true;
                case Pillar.Economy: domain = IntelDomain.Economic; return true;
                case Pillar.Diplomacy: domain = IntelDomain.Diplomatic; return true;
                case Pillar.Government: domain = IntelDomain.Political; return true;
                default: domain = IntelDomain.Military; return false; // intelligence itself is opaque
            }
        }

        public static string ForPillar(GameState state, string targetId, Pillar pillar)
        {
            if (!TryDomainFor(pillar, out var domain))
                return "OPAQUE — RIVAL SERVICES DO NOT REPORT ON THEMSELVES";
            return ForDomain(state, targetId, domain);
        }

        public static string ForDomain(GameState state, string targetId, IntelDomain domain)
        {
            var estimate = IntelligenceSystem.GetEstimate(state, state.playerCountryId, targetId, domain);
            if (estimate == null || estimate.confidence == ConfidenceGrade.None)
                return "NO ASSESSMENT";

            int monthsStale = state.date.MonthsSince(estimate.asOf);
            string staleness = monthsStale > 3 ? $"  (AS OF {estimate.asOf.DisplayString})" : "";
            return $"EST {estimate.RangeText,-9} CONF {estimate.confidence.ToString().ToUpperInvariant(),-9}{staleness}";
        }

        /// <summary>
        /// A foreign inventory count as the operator receives it (GDD §14, §19).
        ///
        /// **Never the true number.** The rule the whole intelligence pillar
        /// rests on is that a view must not print a foreign country's real value,
        /// and an equipment count is exactly the sort of concrete figure it would
        /// be tempting to leak. So the count is banded by collection quality: with
        /// no reporting you get nothing, with poor reporting a wide range, and
        /// only with confirmed access something close to the number.
        ///
        /// The band is derived from the estimate's own confidence, so improving
        /// collection genuinely sharpens the picture — which is the argument for
        /// spending on intelligence before spending on a war.
        /// </summary>
        public static string ForeignAssetCount(GameState state, string targetId, AssetKind kind)
        {
            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, targetId, IntelDomain.Military);
            if (estimate == null || estimate.confidence == ConfidenceGrade.None)
                return "NO ASSESSMENT";

            var target = state.FindCountry(targetId);
            var profile = AssetCatalog.For(kind);
            if (target == null || profile == null) return "NO ASSESSMENT";

            float actual = target.military.Get(profile.branch).inventory.CountOf(kind);

            // The band widens as confidence falls. The centre is deliberately
            // *not* the true value either — it is nudged by the same reported
            // figure the estimate carries, so deception bends equipment counts
            // exactly as it bends everything else.
            float bandwidth = BandFor(estimate.confidence);
            float bias = estimate.reportedValue > 0f && target.pillars.military > 0f
                ? estimate.reportedValue / System.Math.Max(1f, target.pillars.military)
                : 1f;
            if (bias < 0.4f) bias = 0.4f;
            if (bias > 1.6f) bias = 1.6f;

            float centre = actual * bias;
            float low = System.Math.Max(0f, centre * (1f - bandwidth));
            float high = centre * (1f + bandwidth);

            return $"{AssetCatalog.Format(low)}–{AssetCatalog.Format(high)}"
                   + $" ({estimate.confidence.ToString().ToUpperInvariant()})";
        }

        static float BandFor(ConfidenceGrade grade)
        {
            switch (grade)
            {
                case ConfidenceGrade.Confirmed: return 0.06f;
                case ConfidenceGrade.High: return 0.16f;
                case ConfidenceGrade.Moderate: return 0.32f;
                case ConfidenceGrade.Low: return 0.55f;
                default: return 0.85f;
            }
        }
    }
}

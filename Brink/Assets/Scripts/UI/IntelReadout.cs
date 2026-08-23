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

        /// <summary>
        /// A foreign branch's fighting weight, as a band.
        ///
        /// Reports `EffectivePower` rather than raw strength, because that is the
        /// number that decides an operation — readiness, supply and now
        /// experience included. A comparison built on paper strength would tell
        /// the operator the wrong thing about a large force that cannot move.
        ///
        /// Never prints the true value (GDD §14): width comes from the estimate's
        /// own confidence and the centre is bent by the same deception bias as
        /// every other foreign figure.
        /// </summary>
        public static string ForeignBranchStrength(GameState state, string targetId, ForceBranch branch)
        {
            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, targetId, IntelDomain.Military);
            if (estimate == null || estimate.confidence == ConfidenceGrade.None)
                return "NO ASSESSMENT";

            var target = state.FindCountry(targetId);
            if (target == null) return "NO ASSESSMENT";

            float actual = target.military.Get(branch).EffectivePower;
            if (actual <= 0.01f) return "NONE";

            float bandwidth = BandFor(estimate.confidence);
            float bias = estimate.reportedValue > 0f && target.pillars.military > 0f
                ? estimate.reportedValue / System.Math.Max(1f, target.pillars.military)
                : 1f;
            if (bias < 0.4f) bias = 0.4f;
            if (bias > 1.6f) bias = 1.6f;

            float centre = actual * bias;
            return $"{System.Math.Max(0f, centre * (1f - bandwidth)):F1}–{centre * (1f + bandwidth):F1}";
        }

        /// <summary>
        /// Our own dead, stated plainly. A government counts its own casualties.
        ///
        /// The internal figure is in the same abstract units as branch strength,
        /// so it is scaled to people — the point of showing it at all is that
        /// "attackerLosses 31.4" is not something an operator can feel, and
        /// "≈125,600 dead and wounded" is.
        /// </summary>
        public static string OwnCasualties(float losses)
        {
            float people = losses * 4000f;
            if (people < 1000f) return $"{people:F0}";
            if (people < 1000000f) return $"{people / 1000f:F0}k";
            return $"{people / 1000000f:F1}M";
        }

        /// <summary>
        /// Their dead, as a band. We count our own and estimate theirs, which is
        /// both correct fog discipline (GDD §14 — never print a foreign true
        /// value) and an honest description of what a government at war knows.
        ///
        /// Without collection this is deliberately vague rather than absent:
        /// wars produce casualty claims whether or not anybody can verify them.
        /// </summary>
        public static string ForeignCasualties(GameState state, string targetId, float losses)
        {
            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, targetId, IntelDomain.Military);

            float bandwidth = estimate == null || estimate.confidence == ConfidenceGrade.None
                ? 1.1f                                   // press reports and claims
                : BandFor(estimate.confidence);

            float centre = losses * 4000f;
            float low = System.Math.Max(0f, centre * (1f - bandwidth));
            float high = centre * (1f + bandwidth);

            string Scale(float v) => v < 1000f ? $"{v:F0}"
                : v < 1000000f ? $"{v / 1000f:F0}k"
                : $"{v / 1000000f:F1}M";

            return $"{Scale(low)}–{Scale(high)}";
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

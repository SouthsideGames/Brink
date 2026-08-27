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
        /// <summary>
        /// How much of a foreign government's personnel our collection reaches.
        ///
        /// Extracted here because two screens now render the same dossier — the
        /// INTELLIGENCE panel and the country dossier — and two copies of a
        /// threshold are two thresholds. The same reason `OperationCatalog.CanOrder`
        /// is shared between the order screen and the launch path: what is offered
        /// and what is true cannot be allowed to disagree.
        /// </summary>
        public enum PersonnelAccess
        {
            /// <summary>We do not know who runs their ministries.</summary>
            None,

            /// <summary>Names and offices, and nothing about how good they are.</summary>
            Identities,

            /// <summary>Names, and a band on their competence.</summary>
            Assessed
        }

        /// <summary>Penetration needed to put names to their offices.</summary>
        public const float NameThreshold = 20f;

        /// <summary>Penetration needed to judge how good they are at the job.</summary>
        public const float AssessThreshold = 55f;

        public static PersonnelAccess PersonnelAccessOf(GameState state, string targetId)
        {
            if (state == null || string.IsNullOrEmpty(targetId)) return PersonnelAccess.None;
            if (targetId == state.playerCountryId) return PersonnelAccess.Assessed;

            var network = state.FindNetwork(state.playerCountryId, targetId);
            float penetration = network != null && !network.compromised ? network.penetration : 0f;

            if (penetration >= AssessThreshold) return PersonnelAccess.Assessed;
            if (penetration >= NameThreshold) return PersonnelAccess.Identities;
            return PersonnelAccess.None;
        }

        /// <summary>
        /// A band, never a number. An estimate that printed 63.4 would be
        /// claiming a precision collection does not have.
        /// </summary>
        public static string CompetenceBand(float competence)
        {
            if (competence >= 70f) return "CAPABLE";
            if (competence >= 50f) return "ADEQUATE";
            if (competence >= 35f) return "WEAK";
            return "OUT OF DEPTH";
        }

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

            // One vocabulary for one situation. This said "NO ASSESSMENT" while
            // the military readouts had been split into UNTASKED / COLLECTING /
            // BURNED, which would have shown an operator two different words for
            // the same state on two adjacent screens. A network covers every
            // domain, so the reason is the same whichever one is being asked
            // about.
            if (estimate == null || estimate.confidence == ConfidenceGrade.None)
                return WhyNoAssessment(state, targetId, domain) ?? "UNTASKED";

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
            string absence = WhyNoAssessment(state, targetId);
            if (absence != null) return absence;

            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, targetId, IntelDomain.Military);
            var target = state.FindCountry(targetId);
            var profile = AssetCatalog.For(kind);
            if (estimate == null || target == null || profile == null) return "UNTASKED";

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
        /// Why we have no figure for this state — in words the operator can act on.
        ///
        /// Returns null when an assessment does exist.
        ///
        /// **"NO ASSESSMENT" was three different situations wearing one label.**
        /// Reported from play: an operator established a network against a rival,
        /// opened MILITARY, and read NO ASSESSMENT — which looks exactly like a
        /// broken feature. It was not: collection runs on END MONTH, so a network
        /// bought this month reports next month. Saying so is the difference
        /// between "this is broken" and "this is coming".
        ///
        /// The same three-way distinction the after-action reports draw: an
        /// absence should say what would change it.
        /// </summary>
        public static string WhyNoAssessment(GameState state, string targetId,
            IntelDomain domain = IntelDomain.Military)
        {
            // Domain-aware: a network reports on every domain, but at different
            // rates, so a state can be assessed militarily and not economically.
            // Defaulting to Military keeps the military readouts reading the way
            // they were written.
            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, targetId, domain);
            if (estimate != null && estimate.confidence != ConfidenceGrade.None) return null;

            var network = state.FindNetwork(state.playerCountryId, targetId);

            if (network == null)
                return "UNTASKED";              // nothing has ever been tasked here

            if (network.compromised)
                return "BURNED";                // rolled up; it will recover slowly

            // A network exists and has simply not reported yet. Collection is a
            // monthly tick, so this clears itself.
            return "COLLECTING";
        }

        /// <summary>
        /// The military figure our reporting believes, for ordering a ranking.
        ///
        /// Deliberately the *reported* value rather than the truth, so a state
        /// running a deception programme is ranked where it has persuaded us it
        /// belongs. Returns -1 when we have nothing, so unassessed states sort
        /// below everything rather than to the top.
        /// </summary>
        public static float EstimatedMilitary(GameState state, string targetId)
        {
            var estimate = IntelligenceSystem.GetEstimate(
                state, state.playerCountryId, targetId, IntelDomain.Military);
            return estimate == null || estimate.confidence == ConfidenceGrade.None
                ? -1f
                : estimate.reportedValue;
        }

        /// <summary>
        /// One line explaining the labels above, for the foot of a readout that
        /// contains any of them.
        /// </summary>
        public static string AssessmentLegend
            => "UNTASKED — nothing collecting against them.  "
             + "COLLECTING — tasked; first report arrives at END MONTH.  "
             + "BURNED — network rolled up; rebuild it or wait.";

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

            // Say which kind of nothing this is — see WhyNoAssessment.
            string absence = WhyNoAssessment(state, targetId);
            if (absence != null) return absence;

            var target = state.FindCountry(targetId);
            if (target == null) return "UNTASKED";

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

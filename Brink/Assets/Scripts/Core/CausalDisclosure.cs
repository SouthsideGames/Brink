using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>One cause, as a given reader is entitled to see it.</summary>
    public sealed class DisclosedCause
    {
        public CausalReason reason;
        public CausalCategory category;
        public CausalKind kind;
        public CausalVisibility visibility;
        public string sourceCountryId = "";
        public string sourceActionId = "";
        public float confidence = 1f;

        /// <summary>Signed contribution. Meaningless unless `sized` is true.</summary>
        public float value;

        /// <summary>
        /// Whether the reader may be given a figure at all. A suspected factor
        /// is named without one; an unknown factor is not even named.
        /// </summary>
        public bool sized;
    }

    /// <summary>One explanation, filtered for a reader.</summary>
    public sealed class DisclosedExplanation
    {
        public CausalMetric metric;
        public string countryId = "";
        public int year;
        public int month;
        public float previous;
        public float resulting;
        public float delta;
        public CausalReconciliation reconciliation;
        public List<DisclosedCause> causes = new List<DisclosedCause>();

        /// <summary>
        /// How many causes were withheld from this reader entirely. Surfaced,
        /// not hidden — "there is something here you cannot see" is itself
        /// information an operator is entitled to, and is what makes buying
        /// collection worth doing.
        /// </summary>
        public int withheld;

        /// <summary>True when the net change is real but nothing can be said about it.</summary>
        public bool Opaque => causes.Count == 0 && withheld > 0;
    }

    /// <summary>
    /// The single gate between a recorded explanation and a reader (spec 26 §4).
    ///
    /// **This is the reason the explainability layer is not an omniscience
    /// exploit.** Everything else in the framework records what actually
    /// happened, in full, because the simulation needs to be able to explain
    /// itself to itself. What a particular government is entitled to *read* is
    /// decided here and nowhere else — one gate, shared by every renderer, the
    /// same discipline `OperationCatalog.CanOrder` and
    /// `IntelReadout.PersonnelAccessOf` already follow. Two screens applying
    /// their own fog rules is two fog rules, and one of them will be wrong.
    ///
    /// Three existing systems are deferred to rather than re-implemented:
    ///
    /// - **Intelligence.** A foreign country's internal reasons are visible only
    ///   to the degree our estimate of that country is, and never better than
    ///   the estimate's own confidence grade. No collection, no explanation.
    /// - **Cabinet reporting (spec 15 §28.1).** A poor desk *buries* detail: the
    ///   smallest causes drop off and are counted as withheld. It never restates
    ///   a figure — "missed or buried, never distorted" is the standing rule, and
    ///   an explanation layer that reported a wrong number would repeal it. What
    ///   the operator runs under Direct Control they see in full, exactly as the
    ///   advice rule already works.
    /// - **Classification.** A `Classified` cause is dropped for every reader at
    ///   every width — and **leaves no existence side-channel**: it is never
    ///   named, never counted into the withheld line, and never degrades the
    ///   explanation. Its magnitude is merged into the record's OTHER line
    ///   *before* any reader-specific rule runs (`WithoutClassified`), so every
    ///   reader — the country's own government, a foreign one with confirmed
    ///   collection, one with none — sees exactly what they would have seen had
    ///   the movement simply gone unattributed. The net movement is on the
    ///   operator's screen regardless, so hiding the magnitude would protect
    ///   nothing and a column that fell short of it would itself be the tell;
    ///   what is protected is that the movement *had a cause at all*. Distinct
    ///   from `Unknown`, which is *counted*: "something is here you cannot see"
    ///   is what collection can fix; classified is what no reader may learn
    ///   exists.
    ///
    /// The honesty rule on arithmetic: if anything was withheld or could not be
    /// sized, the explanation is downgraded to `Qualitative`, because a column
    /// of figures that silently omits a term is a lie told with correct numbers.
    /// The *net* change is always disclosable — the operator can read the value
    /// itself on any screen, so concealing its movement would protect nothing.
    /// </summary>
    public static class CausalDisclosure
    {
        public static DisclosedExplanation Disclose(GameState state, CausalRecord record)
            => Disclose(state, record, state?.playerCountryId);

        public static DisclosedExplanation Disclose(GameState state, CausalRecord record, string viewerId)
        {
            if (record == null) return null;

            var view = new DisclosedExplanation
            {
                metric = record.metric,
                countryId = record.countryId,
                year = record.year,
                month = record.month,
                previous = record.previous,
                resulting = record.resulting,
                delta = record.delta,
                reconciliation = record.reconciliation,
            };

            bool own = !string.IsNullOrEmpty(viewerId) && viewerId == record.countryId;
            float foreignQuality = own ? 1f : ForeignInsight(state, viewerId, record.countryId);
            float deskQuality = own ? DeskQuality(state, record.metric) : 1f;

            bool degraded = false;

            // A classified cause must not reveal that it exists (see the class
            // comment). Before any reader-specific rule runs, every Classified
            // contribution is merged into the record's OTHER line — so what
            // every reader sees, at every width and every collection grade, is
            // exactly what they would see had the movement simply gone
            // unattributed. Done up front rather than inside the loop, because
            // each of the loop's own rules (counting withheld, degrading to
            // bands, sizing by collection, burying by desk) was a place it could
            // leak: the first version counted it as withheld, and a foreign
            // reader with confirmed collection would have been handed a column
            // that no longer summed to the net change — a tell either way.
            var contributions = WithoutClassified(record.contributions);

            for (int i = 0; i < contributions.Count; i++)
            {
                var c = contributions[i];

                var visibility = c.visibility;
                float confidence = c.confidence;

                if (!own)
                {
                    // Somebody else's internal politics. Our reading of it is
                    // never better than our collection against them.
                    if (foreignQuality <= 0f) { view.withheld++; degraded = true; continue; }

                    if (visibility < CausalVisibility.Estimated) visibility = CausalVisibility.Estimated;
                    if (foreignQuality < 0.55f && visibility < CausalVisibility.Suspected)
                        visibility = CausalVisibility.Suspected;
                    confidence = Math.Min(confidence, foreignQuality);
                }

                // A weak desk loses the small stuff. Bury, never distort: the
                // figures that survive are the ones the simulation recorded.
                if (own && deskQuality < 1f && !CausalReasons.IsStructural(c.reason)
                    && Math.Abs(c.value) < BuriedBelow(deskQuality, record))
                {
                    view.withheld++;
                    degraded = true;
                    continue;
                }

                bool sized = visibility == CausalVisibility.Known
                             || visibility == CausalVisibility.Estimated;
                if (!sized) degraded = true;

                if (visibility == CausalVisibility.Unknown) { view.withheld++; degraded = true; continue; }

                view.causes.Add(new DisclosedCause
                {
                    reason = c.reason,
                    category = c.category,
                    kind = c.kind,
                    visibility = visibility,
                    // A source we have not identified is not named. The cause
                    // may be reported; who is behind it is a separate fact and
                    // needs its own collection.
                    sourceCountryId = own || foreignQuality >= 0.55f ? c.sourceCountryId : "",
                    sourceActionId = c.sourceActionId,
                    confidence = confidence,
                    value = c.value,
                    sized = sized,
                });
            }

            if (degraded) view.reconciliation = CausalReconciliation.Qualitative;
            return view;
        }

        /// <summary>
        /// The record's contributions with every `Classified` one merged into
        /// its OTHER line. Returns the original list untouched when there is
        /// nothing to merge, and otherwise a **copy** — the record is the
        /// simulation's own complete account of itself, and disclosure reads it,
        /// never writes it.
        ///
        /// The magnitude is kept rather than dropped because dropping it would
        /// itself be the side-channel: the net change is on the operator's
        /// screen regardless, so a column that fell short of it would announce
        /// that something was missing. What classification protects is that the
        /// movement *had a cause at all*; folded into OTHER it reads as the
        /// ordinary unattributed remainder every other month already carries.
        /// </summary>
        static List<CausalContribution> WithoutClassified(List<CausalContribution> source)
        {
            float classified = 0f;
            bool any = false;
            for (int i = 0; i < source.Count; i++)
            {
                if (source[i].visibility != CausalVisibility.Classified) continue;
                classified += source[i].value;
                any = true;
            }
            if (!any) return source;

            var result = new List<CausalContribution>(source.Count);
            bool merged = false;
            for (int i = 0; i < source.Count; i++)
            {
                var c = source[i];
                if (c.visibility == CausalVisibility.Classified) continue;

                if (!merged && c.reason == CausalReason.Unattributed)
                {
                    result.Add(new CausalContribution(
                        c.reason, c.value + classified, c.category, c.kind, c.visibility)
                    {
                        sourceCountryId = c.sourceCountryId,
                        sourceActionId = c.sourceActionId,
                        confidence = c.confidence,
                    });
                    merged = true;
                    continue;
                }
                result.Add(c);
            }

            if (!merged && Math.Abs(classified) > Causal.EpsilonValue)
                result.Add(new CausalContribution(
                    CausalReason.Unattributed, classified, CausalCategory.Other, CausalKind.Indirect));

            return result;
        }

        /// <summary>
        /// How small a cause has to be before a mediocre desk loses it. Scaled
        /// against the month's own movement rather than an absolute figure, so
        /// the rule means the same thing for a metric that moves in tenths and
        /// one that moves in thousands.
        /// </summary>
        static float BuriedBelow(float quality, CausalRecord record)
        {
            float scale = Math.Abs(record.delta);
            if (scale < 0.01f) return 0f;
            return scale * 0.25f * (1f - quality);
        }

        /// <summary>
        /// Reporting quality for the desk that owns a metric, 0..1. A pillar the
        /// operator runs themselves has no intermediary and so no filter — the
        /// Direct Control rule from spec 15 §12, reused rather than restated.
        /// </summary>
        static float DeskQuality(GameState state, CausalMetric metric)
        {
            if (state == null || ReportingSystem.Disabled) return 1f;

            var pillar = PillarFor(metric);
            if (pillar == null) return 1f;

            var official = CabinetAdvice.OfficialFor(state, pillar.Value);
            if (official == null) return 1f;
            if (official.mode == ControlMode.DirectControl) return 1f;

            float quality = ReportingSystem.ReportingQualityFor(state, ReportingSystem.DeskFor(pillar.Value));
            return Math.Max(0f, Math.Min(1f, quality / 100f));
        }

        static Pillar? PillarFor(CausalMetric metric)
        {
            switch (metric)
            {
                case CausalMetric.GovernmentApproval:
                case CausalMetric.SocialUnrest:
                case CausalMetric.PublicGrievance:
                    return Pillar.Government;
                case CausalMetric.MarketIndex:
                case CausalMetric.Treasury:
                case CausalMetric.SovereignDebt:
                case CausalMetric.LivingStandards:
                    return Pillar.Economy;
                case CausalMetric.WarExhaustion:
                    return Pillar.Military;
                default:
                    return null;
            }
        }

        /// <summary>
        /// 0..1 reading of how well we understand a foreign country's internal
        /// condition. Government domain, because that is the domain that covers
        /// why a government's own public is behaving as it is.
        /// </summary>
        static float ForeignInsight(GameState state, string viewerId, string targetId)
        {
            if (state == null || string.IsNullOrEmpty(viewerId) || string.IsNullOrEmpty(targetId))
                return 0f;

            var estimate = IntelligenceSystem.GetEstimate(
                state, viewerId, targetId, IntelDomain.Political);
            if (estimate == null) return 0f;

            switch (estimate.confidence)
            {
                case ConfidenceGrade.Confirmed: return 1f;
                case ConfidenceGrade.High: return 0.85f;
                case ConfidenceGrade.Moderate: return 0.6f;
                case ConfidenceGrade.Low: return 0.3f;
                default: return 0f;
            }
        }
    }
}

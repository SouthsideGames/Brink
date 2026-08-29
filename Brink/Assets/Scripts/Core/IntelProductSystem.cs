using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Finished intelligence: a commissioned answer to one question about one
    /// state (spec 03 §10, spec 25 §5.3).
    ///
    /// **This is what collection is for.** Networks and estimates buy sharper
    /// numbers about foreign *capability*; nothing in the game told the operator
    /// what a rival was trying to achieve, whether it would honour a pact, or how
    /// it read them — despite `AIStrategy.StrategicPath`,
    /// `AIPrediction.OpponentModel` and `EndgameSystem.KnownPreparation` being
    /// computed every month for every government. The largest body of unread
    /// state in the codebase, in the one pillar whose subject is knowing things.
    ///
    /// Three rules:
    ///
    /// 1. **It can be wrong, and wrong plausibly.** A poor service does not
    ///    return noise; it returns a different defensible conclusion, stated with
    ///    the same confidence as a good one. Noise would be obviously worthless
    ///    and therefore free to ignore.
    /// 2. **The answer is fixed when it is delivered.** Deterministic per
    ///    (observer, target, question, month), stored, and never recomputed — a
    ///    judgement that flickered on refresh would be unusable, and averaging
    ///    repeated reads would leak the truth. The `MilitaryAdvice` precedent.
    /// 3. **It needs collection.** Commissioning requires a network on the
    ///    target: this is a product of intelligence, not a free oracle, and the
    ///    grade it comes back with is set by how well that network reports.
    /// </summary>
    public static class IntelProductSystem
    {
        public const int CommissionCost = 2;      // CP

        /// <summary>Months of analysis before the judgement lands.</summary>
        public const int BaseMonths = 3;

        /// <summary>At most this many outstanding at once, per service.</summary>
        public const int MaxOutstanding = 2;

        // ---------- commissioning ----------

        /// <summary>
        /// Whether this service can be set this question, and why not. One gate,
        /// shared by the order screen and the verb (the `OperationCatalog.CanOrder`
        /// precedent), so what is offered and what is accepted cannot differ.
        /// </summary>
        public static bool CanCommission(GameState state, string observerId, string targetId,
            EstimateQuestion question, out string reason)
        {
            reason = "";
            if (observerId == targetId) { reason = "WE DO NOT COLLECT ON OURSELVES."; return false; }

            var target = state.FindCountry(targetId);
            if (target == null) { reason = "NO SUCH STATE."; return false; }

            var network = state.FindNetwork(observerId, targetId);
            if (network == null)
            {
                reason = "NO NETWORK IN PLACE. Analysis is a product of collection, not a "
                         + "substitute for it.";
                return false;
            }

            // **A capability that unlocks a question** (spec 13 §6). Tracing a
            // weapon or a payment back to whoever sent it is a forensic
            // apparatus, not an opinion — without it the service can report that
            // there is a rising, and nothing about who is behind it.
            var observer = state.FindCountry(observerId);
            if (question == EstimateQuestion.SubversionSponsorship
                && !TechnologySystem.Has(observer, "CAP_FORENSICS"))
            {
                reason = "WE CANNOT TRACE IT BACK. Requires the Attribution Forensics programme.";
                return false;
            }

            int outstanding = 0;
            foreach (var product in state.intelProducts)
            {
                if (product.observerId != observerId || product.delivered) continue;
                if (product.question == question && product.targetId == targetId)
                {
                    reason = "THAT QUESTION IS ALREADY WITH THE ANALYSTS.";
                    return false;
                }
                outstanding++;
            }

            if (outstanding >= MaxOutstanding)
            {
                reason = $"THE SHOP IS FULL ({outstanding} ASSESSMENTS OUTSTANDING).";
                return false;
            }

            return true;
        }

        /// <summary>Actor-generic. The player wrapper spends CP and delegates.</summary>
        public static IntelProduct CommissionBy(GameState state, string observerId,
            string targetId, EstimateQuestion question)
        {
            if (!CanCommission(state, observerId, targetId, question, out _)) return null;

            var product = new IntelProduct
            {
                observerId = observerId,
                targetId = targetId,
                question = question,
                commissioned = state.date,
                monthsRemaining = BaseMonths
            };
            state.intelProducts.Add(product);
            return product;
        }

        // ---------- delivery ----------

        public static void MonthlyUpdate(GameState state)
        {
            foreach (var product in state.intelProducts)
            {
                if (product.delivered) continue;
                product.monthsRemaining--;
                if (product.monthsRemaining > 0) continue;
                Deliver(state, product);
            }

            // The record is not kept forever. A delivered assessment is a
            // snapshot of a month that has passed; keeping decades of them would
            // make the save grow without bound and the readout unreadable.
            for (int i = state.intelProducts.Count - 1; i >= 0; i--)
            {
                var product = state.intelProducts[i];
                if (!product.delivered) continue;
                if (state.date.MonthsSince(product.commissioned) > 60)
                    state.intelProducts.RemoveAt(i);
            }
        }

        static void Deliver(GameState state, IntelProduct product)
        {
            product.delivered = true;

            var target = state.FindCountry(product.targetId);
            if (target == null)
            {
                product.answer = "The subject no longer exists.";
                product.confidence = ConfidenceGrade.Confirmed;
                product.accurate = true;
                return;
            }

            float confidence = AISystem.EstimateConfidence(
                state, product.observerId, product.targetId, DomainFor(product.question));
            product.confidence = GradeFor(confidence);

            // **Deterministic, and drawn once.** Seeded from the pair, the
            // question and the month it was commissioned, so the same
            // commission always resolves the same way — re-reading it cannot
            // shake a different answer loose, and neither can reloading.
            var rng = new Random(unchecked(
                state.rngSeed * 1500450271
                + Hash.Of(product.observerId) * 31
                + Hash.Of(product.targetId) * 17
                + (int)product.question * 7919
                + product.commissioned.MonthsSince(state.startDate) * 131));

            // A service with nothing to go on is a coin flip; one with deep,
            // uncompromised access is nearly always right. Never certain either
            // way — a grade of Confirmed is a claim about confidence, not about
            // correctness, and the operator is meant to weigh it.
            float accuracyChance = 0.30f + 0.62f * Clamp01(confidence);
            product.accurate = rng.NextDouble() < accuracyChance;

            product.answer = Answer(state, product, target, rng);

            if (product.observerId == state.playerCountryId)
            {
                state.AddNotification(NotificationClass.Priority, "ASSESSMENT DELIVERED",
                    $"{QuestionText(product.question)} — {target.displayName}: {product.answer}",
                    target.id, desk: ReportingDesk.Intelligence);
                ProgressionSystem.AwardXP(state, 14, "Assessment delivered");
            }
        }

        static IntelDomain DomainFor(EstimateQuestion question)
        {
            switch (question)
            {
                case EstimateQuestion.StrategicProgramme: return IntelDomain.Military;
                case EstimateQuestion.TreatyReliability: return IntelDomain.Diplomatic;
                case EstimateQuestion.SubversionSponsorship: return IntelDomain.Political;
                default: return IntelDomain.Political;
            }
        }

        static ConfidenceGrade GradeFor(float confidence)
        {
            if (confidence >= 0.85f) return ConfidenceGrade.Confirmed;
            if (confidence >= 0.65f) return ConfidenceGrade.High;
            if (confidence >= 0.45f) return ConfidenceGrade.Moderate;
            if (confidence >= 0.25f) return ConfidenceGrade.Low;
            return ConfidenceGrade.None;
        }

        // ---------- the judgements ----------

        static string Answer(GameState state, IntelProduct product, CountryState target, Random rng)
        {
            switch (product.question)
            {
                case EstimateQuestion.StrategicIntent: return IntentAnswer(state, product, target, rng);
                case EstimateQuestion.StrategicProgramme: return ProgrammeAnswer(state, product, target, rng);
                case EstimateQuestion.TreatyReliability: return ReliabilityAnswer(state, product, target, rng);
                case EstimateQuestion.TheirReadOfUs: return ReadOfUsAnswer(state, product, target, rng);
                default: return SponsorshipAnswer(state, product, target, rng);
            }
        }

        /// <summary>
        /// What they are building toward. The first time `AIStrategy.StrategicPath`
        /// has ever been visible to the operator.
        /// </summary>
        static string IntentAnswer(GameState state, IntelProduct product, CountryState target, Random rng)
        {
            var ai = state.FindAI(target.id);
            if (ai == null) return "No coherent strategy is discernible.";

            var believed = ai.path;
            if (!product.accurate)
            {
                // **Plausibly wrong**: a different real path, not noise. A wrong
                // assessment has to be usable — an operator acts on it, and finds
                // out later.
                var paths = (StrategicPath[])Enum.GetValues(typeof(StrategicPath));
                var alternative = paths[rng.Next(paths.Length)];
                if (alternative == believed)
                    alternative = paths[(Array.IndexOf(paths, believed) + 1) % paths.Length];
                believed = alternative;
            }

            return $"They are pursuing {AIStrategy.Describe(believed)}";
        }

        static string ProgrammeAnswer(GameState state, IntelProduct product, CountryState target, Random rng)
        {
            EndgameType? found = null;
            float best = 0f;
            foreach (EndgameType type in Enum.GetValues(typeof(EndgameType)))
            {
                float known = EndgameSystem.KnownPreparation(state, product.observerId, target.id, type);
                if (known > best) { best = known; found = type; }
            }

            bool reportPreparing = found.HasValue;
            if (!product.accurate) reportPreparing = !reportPreparing;

            if (!reportPreparing)
                return "We find no evidence of work toward a decisive instrument.";

            var reported = found ?? (EndgameType)rng.Next(
                Enum.GetValues(typeof(EndgameType)).Length);
            return $"They are working toward {Phrase.Of(reported)}.";
        }

        static string ReliabilityAnswer(GameState state, IntelProduct product, CountryState target, Random rng)
        {
            var relationship = state.FindRelationship(product.observerId, target.id);
            float weight = relationship?.trust ?? 50f;

            // Breaking a treaty is a matter of public record, so a history of it
            // is the strongest evidence there is.
            int broken = 0;
            foreach (var treaty in state.treaties)
                if (treaty.broken && treaty.brokenBy == target.id) broken++;

            bool reliable = broken == 0 && weight >= 45f;
            if (!product.accurate) reliable = !reliable;

            return reliable
                ? "They will hold to what they have signed while it serves them, and we judge "
                  + "the terms still serve them."
                : "We judge they would set aside their commitments if pressed.";
        }

        static string ReadOfUsAnswer(GameState state, IntelProduct product, CountryState target, Random rng)
        {
            var ai = state.FindAI(target.id);
            var model = ai?.ModelOf(product.observerId);
            if (model == null || model.observations < AIPrediction.SettledAfter)
                return "They have not formed a settled view of us.";

            var expected = model.predictedMove;
            if (!product.accurate)
            {
                var moves = (PredictedMove[])Enum.GetValues(typeof(PredictedMove));
                var alternative = moves[rng.Next(moves.Length)];
                if (alternative == expected)
                    alternative = moves[(Array.IndexOf(moves, expected) + 1) % moves.Length];
                expected = alternative;
            }

            return $"They expect us to {Phrase.Of(expected)}, and are positioned accordingly.";
        }

        /// <summary>
        /// Who is arming a rising on our own ground. Reads the same public record
        /// the world's expectations are built from, so a sponsor who has been
        /// caught before is easier to name.
        /// </summary>
        static string SponsorshipAnswer(GameState state, IntelProduct product, CountryState target, Random rng)
        {
            string sponsor = null;
            foreach (var insurgency in state.insurgencies)
            {
                var location = state.FindLocation(insurgency.locationId);
                if (location == null || location.ownerId != product.observerId) continue;
                if (string.IsNullOrEmpty(insurgency.sponsorId)) continue;
                sponsor = insurgency.sponsorId;
                break;
            }

            if (sponsor == null && product.accurate)
                return "No foreign hand is behind the fighting on our ground.";

            bool blameTarget = sponsor == target.id;
            if (!product.accurate) blameTarget = !blameTarget;

            return blameTarget
                ? $"{target.displayName} is arming the rising on our ground."
                : $"We find no evidence that {target.displayName} is behind it.";
        }

        // ---------- presentation ----------

        public static string QuestionText(EstimateQuestion question)
        {
            switch (question)
            {
                case EstimateQuestion.StrategicIntent: return "WHAT ARE THEY BUILDING TOWARD";
                case EstimateQuestion.StrategicProgramme: return "ARE THEY PREPARING AN INSTRUMENT";
                case EstimateQuestion.TreatyReliability: return "WILL THEY HONOUR THEIR COMMITMENTS";
                case EstimateQuestion.TheirReadOfUs: return "HOW DO THEY READ US";
                default: return "WHO IS BEHIND THE RISING";
            }
        }

        /// <summary>Everything this service has been told to find out.</summary>
        public static List<IntelProduct> For(GameState state, string observerId)
        {
            var mine = new List<IntelProduct>();
            foreach (var product in state.intelProducts)
                if (product.observerId == observerId) mine.Add(product);
            return mine;
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}

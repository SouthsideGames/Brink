using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Which value an explanation is about (spec 26).
    ///
    /// **Append-only, persisted by ordinal.** A saved `CausalRecord` stores this
    /// as an int, so inserting a member in the middle silently relabels every
    /// record already written — the same rule `CivicPosture` and `TradeFocus`
    /// follow, for the same reason.
    /// </summary>
    public enum CausalMetric
    {
        None = 0,
        GovernmentApproval,
        SocialUnrest,
        LivingStandards,
        PublicGrievance,
        MarketIndex,
        Treasury,
        SovereignDebt,
        WarExhaustion,
    }

    /// <summary>
    /// What kind of thing a cause is, so the reader can group without the
    /// renderer knowing anything about individual systems. Append-only.
    /// </summary>
    public enum CausalCategory
    {
        Other = 0,
        Economic,
        Fiscal,
        Social,
        Political,
        Military,
        Diplomatic,
        Intelligence,
        PlayerDecision,
    }

    /// <summary>
    /// How much the operator's government actually knows about a cause.
    ///
    /// **This is the whole information-security contract of the framework.**
    /// Brink is a fog-of-war game: an explanation layer that reported the true
    /// reason for everything would be a free intelligence service, and would
    /// quietly repeal spec 06 §7b's counter-play and every reason to buy
    /// collection. So a contribution carries what is *known about it*, and
    /// `CausalDisclosure` is the one place that decides what a reader is shown.
    ///
    /// Append-only, and deliberately ordered from most to least disclosed so a
    /// comparison like `visibility >= CausalVisibility.Estimated` reads naturally.
    /// </summary>
    public enum CausalVisibility
    {
        /// <summary>Our own government's own act. Reported exactly.</summary>
        Known = 0,

        /// <summary>We have a figure and it carries a confidence, not a fact.</summary>
        Estimated,

        /// <summary>We know something is acting here; we cannot size or name it.</summary>
        Suspected,

        /// <summary>We can see the hole and nothing about what made it.</summary>
        Unknown,

        /// <summary>
        /// Our own government knows and the desk did not pass it on, or it is
        /// somebody else's secret entirely. Never rendered, at any width.
        /// </summary>
        Classified,
    }

    /// <summary>
    /// Whether a cause acted on the metric itself or reached it through
    /// something else. Phase A keeps the chain shallow — one hop — but the
    /// field is what lets a later phase walk
    /// `doctrine → decision → policy → economic effect → political consequence`
    /// without the record shape changing.
    /// </summary>
    public enum CausalKind
    {
        Direct = 0,
        Indirect,
    }

    /// <summary>
    /// How honestly the listed contributions add up to the observed change.
    ///
    /// **The presentation must match the mathematics.** Brink's monthly ticks
    /// are mostly `Approach(value, target, rate)`, which is linear in the
    /// target, so a decomposition of the target scales exactly into a
    /// decomposition of the delta — that is `Exact`. Where a value is clamped,
    /// or where a genuinely non-linear step sits between the causes and the
    /// result, saying "these five numbers sum to −3.8" would be a fabrication,
    /// and the renderer drops to ranked words instead.
    /// </summary>
    public enum CausalReconciliation
    {
        /// <summary>Contributions sum to the net delta. Figures may be shown.</summary>
        Exact = 0,

        /// <summary>
        /// Contributions are exact in their own space (a target, a pressure sum)
        /// and have been scaled into delta space by a linear transfer. Still
        /// additive, still printable, but the record says so.
        /// </summary>
        Scaled,

        /// <summary>
        /// The relationship is not additive. Rank and direction are honest;
        /// figures are not, and the renderer prints bands instead.
        /// </summary>
        Qualitative,
    }

    /// <summary>
    /// One reason a value moved (spec 26 §2).
    ///
    /// Kept deliberately small and free of prose: `reason` is a stable enum
    /// ordinal and the player-facing wording is resolved at render time by
    /// `CausalReasons.Label`. Storing sentences here would put presentation
    /// text into authoritative, persisted simulation state and multiply save
    /// size by the length of the English language.
    /// </summary>
    [Serializable]
    public class CausalContribution
    {
        /// <summary>Stable identity of the cause. See `CausalReason`.</summary>
        public CausalReason reason;

        /// <summary>
        /// Signed contribution, in the units of the metric, in the same space
        /// as the owning record's `reconciliation` says.
        /// </summary>
        public float value;

        public CausalCategory category;
        public CausalKind kind;
        public CausalVisibility visibility;

        /// <summary>
        /// Which country this cause came from, when that is meaningful and
        /// disclosable — a sanctioning state, the sponsor of a rising. Empty
        /// means "us" or "not attributable". A stable country id, never a name.
        /// </summary>
        public string sourceCountryId = "";

        /// <summary>
        /// The operator decision this traces back to, when one does
        /// (§10 provenance). A stable id — an `ActionCatalog` verb name via
        /// `nameof`, a `CrisisOption` id, a directive id — never a display
        /// string, because matching on display strings is how the XP
        /// diminishing-returns counter was defeated twice.
        /// </summary>
        public string sourceActionId = "";

        /// <summary>
        /// 0..1 for an `Estimated` cause; ignored otherwise. Carries the same
        /// meaning as an intelligence estimate's grade: how much the figure
        /// beside it should be trusted.
        /// </summary>
        public float confidence = 1f;

        public CausalContribution() { }

        public CausalContribution(CausalReason reason, float value, CausalCategory category,
                                  CausalKind kind = CausalKind.Direct,
                                  CausalVisibility visibility = CausalVisibility.Known)
        {
            this.reason = reason;
            this.value = value;
            this.category = category;
            this.kind = kind;
            this.visibility = visibility;
        }
    }

    /// <summary>
    /// What happened to one metric, for one country, in one month (spec 26 §1).
    ///
    /// Recorded where the simulation actually applies the change, never
    /// reconstructed afterwards from the result — a value recomputed after the
    /// fact can only ever be a guess at its own history, and this project has
    /// already shipped that mistake as `OperationAnalysis`' predecessor.
    /// </summary>
    [Serializable]
    public class CausalRecord
    {
        public CausalMetric metric;
        public string countryId = "";

        /// <summary>
        /// The month this describes, stored as the calendar pair rather than an
        /// index so a record renders its own heading ("MAR 2041") without
        /// needing the epoch, and reads correctly in a save whose start date
        /// differs.
        /// </summary>
        public int year;
        public int month;

        /// <summary>Ordering key. Not serialized — derived from the pair above.</summary>
        public int MonthIndex => year * 12 + month;

        public float previous;
        public float resulting;

        /// <summary>
        /// The observed net change. Stored rather than derived so a record read
        /// back from an old save cannot disagree with itself if the metric's
        /// own clamping rules change later.
        /// </summary>
        public float delta;

        public CausalReconciliation reconciliation;

        /// <summary>
        /// Ordered, and the order is deterministic: contributions are appended
        /// in the order the simulation applied them, never sorted by magnitude
        /// at record time. Ranking is a *presentation* decision and belongs to
        /// the renderer, which sorts a copy.
        /// </summary>
        public List<CausalContribution> contributions = new List<CausalContribution>();

        /// <summary>
        /// How much of `delta` the listed contributions do not account for —
        /// clamping, or a term nobody instrumented. Rendered as OTHER rather
        /// than hidden, because a decomposition that quietly fails to add up is
        /// worse than one that admits a remainder.
        /// </summary>
        public float unexplained;

        public bool HasContent => contributions.Count > 0 || Math.Abs(delta) > 0.0001f;
    }

    /// <summary>
    /// The bounded store of recent explanations (spec 26 §5).
    ///
    /// **Bounded by construction, and the bound is the design.** An explanation
    /// layer is the most natural place in this codebase to grow an unbounded
    /// log — every system writes to it every month — and `GameState` is a single
    /// JSON object loaded whole. So: the player's country only, a fixed number
    /// of months, a fixed number of contributions per record, oldest pruned
    /// first.
    ///
    /// Empty on an old save is **correct, not merely blank** — a world that
    /// predates this genuinely has no recorded reasons, and inventing them for
    /// months that resolved before the feature existed would be fabricating
    /// history. That is the same reasoning that let `warsWon` and `Bloc.commitments`
    /// ship without a migration step, so this needs none either and
    /// `SaveSystem.CurrentSaveVersion` is unchanged.
    /// </summary>
    [Serializable]
    public class CausalLedger
    {
        /// <summary>
        /// How many months of explanation are kept per metric. Twelve is a
        /// playable horizon — "why has approval been sliding all year" — and at
        /// eight instrumented metrics bounds the ledger at ~96 records.
        /// </summary>
        public const int MonthsKept = 12;

        /// <summary>
        /// Hard ceiling on contributions inside one record. The monthly social
        /// tick has around a dozen terms; anything past this is noise the
        /// operator will not read, and an unbounded list here would defeat the
        /// record cap above.
        /// </summary>
        public const int MaxContributions = 14;

        /// <summary>Flat, newest last. Flat rather than keyed because
        /// `JsonUtility` does not serialize dictionaries.</summary>
        public List<CausalRecord> records = new List<CausalRecord>();

        /// <summary>
        /// Append and prune. Pruning is per (country, metric) rather than
        /// global, or a chatty metric would evict a quiet one and the quiet
        /// one's history would be the first thing an operator went looking for.
        /// </summary>
        public void Add(CausalRecord record)
        {
            if (record == null) return;

            if (record.contributions.Count > MaxContributions)
                record.contributions.RemoveRange(
                    MaxContributions, record.contributions.Count - MaxContributions);

            records.Add(record);

            int seen = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var r = records[i];
                if (r.metric != record.metric || r.countryId != record.countryId) continue;
                seen++;
                if (seen > MonthsKept) records.RemoveAt(i);
            }
        }

        /// <summary>Newest first, bounded by `count`. Never returns null.</summary>
        public List<CausalRecord> History(string countryId, CausalMetric metric, int count)
        {
            var found = new List<CausalRecord>();
            for (int i = records.Count - 1; i >= 0 && found.Count < count; i--)
            {
                var r = records[i];
                if (r.metric == metric && r.countryId == countryId) found.Add(r);
            }
            return found;
        }

        /// <summary>The most recent record for a metric, or null.</summary>
        public CausalRecord Latest(string countryId, CausalMetric metric)
        {
            for (int i = records.Count - 1; i >= 0; i--)
            {
                var r = records[i];
                if (r.metric == metric && r.countryId == countryId) return r;
            }
            return null;
        }
    }
}

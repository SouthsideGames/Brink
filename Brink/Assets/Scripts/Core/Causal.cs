using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Recording side of the causal explainability framework (spec 26).
    ///
    /// **The rule this class exists to enforce: an explanation is captured where
    /// the simulation applies the change, never reconstructed afterwards.** A
    /// value re-derived after the fact can only guess at its own history, and a
    /// guess presented as an explanation is worse than no explanation — the
    /// operator would learn the wrong lesson and play to it. So systems hand
    /// their own already-computed terms to a builder, and the builder does
    /// arithmetic only on what it was given.
    ///
    /// **It must not be able to change the game.** Every entry point is inert
    /// when `Enabled` is false, nothing here draws from an RNG, nothing here
    /// writes to any simulation field, and no recorded figure is ever read back
    /// into the simulation. `CausalityTests` asserts a decade resolves
    /// identically with recording on and off, because an observability layer
    /// that moves the world it observes is not observability.
    /// </summary>
    public static class Causal
    {
        /// <summary>
        /// Master switch. Exists for the determinism test above and for a
        /// headless balance run that wants no allocation at all — not as a
        /// player setting.
        /// </summary>
        public static bool Enabled = true;

        /// <summary>
        /// Whether foreign countries' movements are recorded as well as the
        /// player's.
        ///
        /// **Off, and off for a size reason rather than a secrecy one.** The
        /// ledger is persisted inside a single-object save and the monthly tick
        /// runs for every country, so recording a full world multiplies the
        /// store by the roster. Disclosure is a separate concern and is handled
        /// by `CausalDisclosure` whatever this is set to — turning this on would
        /// not leak anything, it would simply cost save size that Phase A has no
        /// use for.
        /// </summary>
        public static bool RecordForeign = false;

        const float Epsilon = 0.0005f;

        /// <summary>
        /// Whether this country's changes are being recorded at all. Call this
        /// *before* building a decomposition — the terms a system would hand to
        /// a builder are already in locals, so the whole recording block can be
        /// skipped for the fifteen countries nobody is going to ask about, and
        /// the monthly tick allocates nothing for them.
        /// </summary>
        public static bool Records(GameState state, string countryId)
        {
            if (!Enabled || state == null || string.IsNullOrEmpty(countryId)) return false;
            if (RecordForeign) return true;
            return countryId == state.playerCountryId;
        }

        /// <summary>
        /// Open a decomposition. Only call when `Records` is true.
        /// </summary>
        public static CausalBuilder Begin(GameState state, string countryId,
                                          CausalMetric metric, float previous)
            => new CausalBuilder(state, countryId, metric, previous);

        /// <summary>
        /// Record one discrete cause against a metric that accumulates from many
        /// scattered sites rather than settling toward a target — war exhaustion
        /// being the pilot case, with eleven write sites across six systems.
        ///
        /// Finds or creates this month's record and appends, so a month in which
        /// fighting, an operation, an occupation and a rising all pushed the same
        /// value produces **one** explanation listing four causes rather than
        /// four explanations each claiming the whole change. That is the
        /// difference between an explanation and a misleading partial one.
        ///
        /// `previous` and `resulting` are the values actually observed around the
        /// assignment, so the contribution is the real post-clamp movement rather
        /// than the intended one.
        /// </summary>
        public static void Note(GameState state, string countryId, CausalMetric metric,
                                CausalReason reason, float previous, float resulting,
                                CausalCategory category,
                                CausalKind kind = CausalKind.Direct,
                                CausalVisibility visibility = CausalVisibility.Known,
                                string sourceCountryId = null,
                                string sourceActionId = null,
                                float confidence = 1f)
        {
            if (!Records(state, countryId)) return;

            float change = resulting - previous;
            if (Math.Abs(change) < Epsilon) return;

            var record = OpenRecord(state, countryId, metric, previous);
            if (record == null) return;

            // The month's running total. `previous` stays what the month opened
            // at; a later site's `previous` is an intermediate and must never
            // overwrite it.
            record.resulting = resulting;
            record.delta = record.resulting - record.previous;

            Append(record, new CausalContribution(reason, change, category, kind, visibility)
            {
                sourceCountryId = sourceCountryId ?? "",
                sourceActionId = sourceActionId ?? "",
                confidence = confidence,
            });
        }

        /// <summary>
        /// This month's record for a metric, created on first touch.
        ///
        /// `previous` comes from the month-open snapshot when there is one, so a
        /// record describes **the whole month** rather than the slice between
        /// the first instrumented site and the last. Without that, a value moved
        /// by a cabinet action before the government tick and by a crisis after
        /// it reported a change that did not match what the operator could read
        /// on the screen — which is the one thing this framework may not do.
        /// </summary>
        internal static CausalRecord OpenRecord(GameState state, string countryId,
                                                CausalMetric metric, float fallbackPrevious)
        {
            var ledger = state.causal;
            if (ledger == null) return null;

            var record = FindOpen(ledger, countryId, metric, state.date);
            if (record != null) return record;

            float previous = ledger.TryOpening(countryId, metric, out var opened)
                ? opened
                : fallbackPrevious;

            record = new CausalRecord
            {
                metric = metric,
                countryId = countryId,
                year = state.date.year,
                month = state.date.month,
                previous = previous,
                resulting = previous,
                delta = 0f,
                reconciliation = CausalReconciliation.Exact,
            };
            ledger.Add(record);
            return record;
        }

        /// <summary>
        /// Append a cause, folding past the cap into OTHER rather than dropping
        /// — listed causes that stop adding up without saying why are worse than
        /// a remainder that admits itself. `FoldToCap` keeps the largest named
        /// causes and merges the smallest, so a big late arrival displaces a
        /// small early one instead of vanishing into OTHER itself.
        /// </summary>
        internal static void Append(CausalRecord record, CausalContribution contribution)
        {
            record.contributions.Add(contribution);
            CausalLedger.FoldToCap(record);
        }

        /// <summary>
        /// Convenience for the `Note` pattern at a site that assigns in one
        /// expression. Reads the field, assigns the value the caller computed,
        /// and records the movement — **the arithmetic stays in the caller's
        /// expression, untouched**, which is what keeps instrumentation from
        /// being a rewrite of the simulation.
        /// </summary>
        public static void Apply(GameState state, string countryId, CausalMetric metric,
                                 CausalReason reason, ref float field, float value,
                                 CausalCategory category,
                                 CausalKind kind = CausalKind.Direct,
                                 CausalVisibility visibility = CausalVisibility.Known,
                                 string sourceCountryId = null,
                                 string sourceActionId = null)
        {
            float before = field;
            field = value;
            Note(state, countryId, metric, reason, before, value, category, kind, visibility,
                 sourceCountryId, sourceActionId);
        }

        static void Fold(CausalRecord record, float amount)
        {
            for (int i = 0; i < record.contributions.Count; i++)
            {
                if (record.contributions[i].reason != CausalReason.Unattributed) continue;
                record.contributions[i].value += amount;
                return;
            }
            record.contributions.Add(new CausalContribution(
                CausalReason.Unattributed, amount, CausalCategory.Other));
            CausalLedger.FoldToCap(record);
        }

        static CausalRecord FindOpen(CausalLedger ledger, string countryId,
                                     CausalMetric metric, GameDate date)
        {
            int monthIndex = date.year * 12 + date.month;
            for (int i = ledger.records.Count - 1; i >= 0; i--)
            {
                var r = ledger.records[i];
                if (r.MonthIndex != monthIndex) break; // appended in month order
                if (r.metric == metric && r.countryId == countryId) return r;
            }
            return null;
        }

        /// <summary>
        /// Every value this framework reconciles over a whole month. Adding a
        /// metric here without a reader below is caught by
        /// `CausalityTests.EveryReconciledMetricCanBeRead`.
        /// </summary>
        public static readonly CausalMetric[] Reconciled =
        {
            CausalMetric.GovernmentApproval,
            CausalMetric.SocialUnrest,
            CausalMetric.LivingStandards,
            CausalMetric.PublicGrievance,
            CausalMetric.MarketIndex,
            CausalMetric.SovereignDebt,
            CausalMetric.WarExhaustion,
        };

        public static bool TryRead(CountryState country, CausalMetric metric, out float value)
        {
            value = 0f;
            if (country == null) return false;
            switch (metric)
            {
                case CausalMetric.GovernmentApproval: value = country.governmentApproval; return true;
                case CausalMetric.SocialUnrest: value = country.socialUnrest; return true;
                case CausalMetric.LivingStandards: value = country.livingStandards; return true;
                case CausalMetric.PublicGrievance: value = country.publicGrievance; return true;
                case CausalMetric.MarketIndex: value = country.economy.marketIndex; return true;
                case CausalMetric.SovereignDebt: value = country.fiscal.sovereignDebt; return true;
                case CausalMetric.WarExhaustion: value = country.warExhaustion; return true;
                default: return false;
            }
        }

        /// <summary>
        /// **The causal month boundary (spec 26 §3a).** A causal month is the
        /// interval between two consecutive completions of
        /// `TurnManager.EndMonth`: from the moment the operator's turn begins —
        /// the previous resolution has finished, its year-end evaluation
        /// included, and the screen shows the values the operator reads — to
        /// the moment this resolution finishes. Everything inside belongs to
        /// the month being resolved: the operator's own verbs, a crisis they
        /// answered or let lapse (`CrisisSystem.LapseUnanswered` runs before
        /// `ResolveMonth` fires), the whole pipeline, and whatever `YearEnded`
        /// does in a December.
        ///
        /// Snapshotting at pipeline start instead left every pre-resolution
        /// mutation outside the record — the measured symptom was approval
        /// endpoints disagreeing with the screen in exactly the months a crisis
        /// lapsed (13 of 60 on one seed, 6 of 60 on another).
        ///
        /// So the snapshot is taken **when the month closes**, for the month
        /// that follows (`CloseMonth`, wired to `TurnManager.MonthResolved`);
        /// seeded at `SimulationPipeline.Wire` for a new world or a save that
        /// predates it (`OpenMonth`, which fills gaps and never overwrites); and
        /// **persisted**, because the game autosaves after every player verb, so
        /// a reload mid-turn must resume the bracket the turn began in rather
        /// than open a new one on the post-verb values.
        ///
        /// This method clears and re-takes it. A fixture that rewrites the world
        /// after wiring is describing "the world as given", not a month, and
        /// should call this to say so.
        /// </summary>
        public static void SnapshotOpenings(GameState state)
        {
            if (!Enabled || state == null || state.causal == null) return;

            state.causal.openings?.Clear();

            for (int i = 0; i < state.countries.Count; i++)
            {
                var country = state.countries[i];
                if (!Records(state, country.id)) continue;
                for (int m = 0; m < Reconciled.Length; m++)
                    if (TryRead(country, Reconciled[m], out var value))
                        state.causal.OpenMetric(country.id, Reconciled[m], value);
            }
        }

        /// <summary>
        /// Fill any missing opening from the current value, **never overwriting
        /// one that exists**. Called at `SimulationPipeline.Wire` — a new world,
        /// an old save with no snapshot, a country whose recording was just
        /// switched on — and again as the first system of the pipeline as a
        /// fallback for a hand-wired fixture. The real snapshot was taken when
        /// the previous month closed (see `SnapshotOpenings` for the boundary
        /// rule); overwriting it here would push the boundary back to pipeline
        /// start and re-open the pre-resolution gap.
        /// </summary>
        public static void OpenMonth(GameState state)
        {
            if (!Enabled || state == null || state.causal == null) return;

            for (int i = 0; i < state.countries.Count; i++)
            {
                var country = state.countries[i];
                if (!Records(state, country.id)) continue;
                for (int m = 0; m < Reconciled.Length; m++)
                {
                    if (state.causal.TryOpening(country.id, Reconciled[m], out _)) continue;
                    if (TryRead(country, Reconciled[m], out var value))
                        state.causal.OpenMetric(country.id, Reconciled[m], value);
                }
            }
        }

        /// <summary>
        /// The close of the causal month: make every record describe the month
        /// that actually happened, then open the next one where this one
        /// closed. Wired to `TurnManager.MonthResolved`, which fires after the
        /// pipeline *and* after `YearEnded`, so a December's record includes
        /// the annual evaluation's consequences rather than closing before them.
        ///
        /// Anything the listed causes do not account for is booked as
        /// `Unattributed` and shown as OTHER — a crisis effect, a coup, a peace
        /// term. **Admitting a remainder is the honest reading**; silently
        /// reporting a change smaller than the one on screen is not.
        /// </summary>
        public static void CloseMonth(GameState state)
        {
            if (!Enabled || state == null || state.causal == null) return;

            for (int i = 0; i < state.countries.Count; i++)
            {
                var country = state.countries[i];
                if (!Records(state, country.id)) continue;

                for (int m = 0; m < Reconciled.Length; m++)
                {
                    var metric = Reconciled[m];
                    if (!TryRead(country, metric, out var now)) continue;
                    if (!state.causal.TryOpening(country.id, metric, out var opened)) continue;

                    var existing = FindOpen(state.causal, country.id, metric, state.date);
                    if (existing == null && Math.Abs(now - opened) < Epsilon) continue;

                    var record = existing ?? OpenRecord(state, country.id, metric, opened);
                    if (record == null) continue;

                    record.resulting = now;
                    record.delta = now - record.previous;

                    float explained = 0f;
                    for (int c = 0; c < record.contributions.Count; c++)
                        explained += record.contributions[c].value;

                    float remainder = record.delta - explained;
                    if (Math.Abs(remainder) <= Epsilon) continue;

                    record.unexplained += remainder;
                    Fold(record, remainder);
                }
            }

            // The next causal month opens *here*, where this one closed — not at
            // the next pipeline start. Anything that happens in between (the
            // operator's turn, a lapsing crisis) belongs to the month it
            // precedes. See `SnapshotOpenings` for the boundary rule.
            SnapshotOpenings(state);
        }

        /// <summary>
        /// Whether a metric's opening is on record for a country — what the
        /// boundary tests read to prove the bracket survived a save.
        /// </summary>
        public static bool TryOpening(GameState state, string countryId, CausalMetric metric, out float value)
        {
            value = 0f;
            return state?.causal != null && state.causal.TryOpening(countryId, metric, out value);
        }

        internal static float EpsilonValue => Epsilon;
    }

    /// <summary>
    /// Accumulates the named terms of one value's movement and closes them into
    /// a `CausalRecord` (spec 26 §3).
    ///
    /// Two shapes of Brink arithmetic are supported, and the distinction is the
    /// whole reason the builder exists rather than a plain list:
    ///
    /// - **A target approached at a rate** — `Approach(v, target, rate)`, which is
    ///   the idiom this codebase pushed almost every recurring pressure into
    ///   after the value-versus-target bug family. It is *linear in the target*,
    ///   so a decomposition of the target scales exactly into a decomposition of
    ///   the month's delta. `CommitApproach` does that scaling and the figures
    ///   stay honest.
    /// - **A sum applied directly** — `CommitAdditive`.
    ///
    /// Multipliers are handled without lying about them: for a target of the
    /// form `M x (t1 + t2 + ...)`, the effect of `M` is exactly
    /// `(sum so far) x (M - 1)`, which is itself an additive term. So a damping
    /// factor appears as its own line with its own signed figure, and the
    /// listed causes still sum to the target.
    /// </summary>
    public sealed class CausalBuilder
    {
        readonly GameState state;
        readonly string countryId;
        readonly CausalMetric metric;
        readonly float previous;
        readonly List<CausalContribution> terms = new List<CausalContribution>();

        internal CausalBuilder(GameState state, string countryId, CausalMetric metric, float previous)
        {
            this.state = state;
            this.countryId = countryId;
            this.metric = metric;
            this.previous = previous;
        }

        /// <summary>
        /// Add one named term of the target. Zero-valued terms are dropped —
        /// a country with no war has no business being told that war exhaustion
        /// contributed nothing.
        /// </summary>
        public CausalBuilder Add(CausalReason reason, float value, CausalCategory category,
                                 CausalKind kind = CausalKind.Direct,
                                 CausalVisibility visibility = CausalVisibility.Known,
                                 string sourceCountryId = null,
                                 string sourceActionId = null,
                                 float confidence = 1f)
        {
            if (Math.Abs(value) < Causal.EpsilonValue) return this;

            terms.Add(new CausalContribution(reason, value, category, kind, visibility)
            {
                sourceCountryId = sourceCountryId ?? "",
                sourceActionId = sourceActionId ?? "",
                confidence = confidence,
            });
            return this;
        }

        /// <summary>
        /// Fold in a multiplicative modifier applied to everything added so far,
        /// as its own exact additive line. See the class comment.
        /// </summary>
        public CausalBuilder Multiply(CausalReason reason, float factor, CausalCategory category,
                                      CausalKind kind = CausalKind.Indirect,
                                      CausalVisibility visibility = CausalVisibility.Known,
                                      string sourceActionId = null)
        {
            float running = Sum();
            float effect = running * (factor - 1f);
            return Add(reason, effect, category, kind, visibility, null, sourceActionId);
        }

        float Sum()
        {
            float total = 0f;
            for (int i = 0; i < terms.Count; i++) total += terms[i].value;
            return total;
        }

        /// <summary>
        /// Close a value that approached `target` at `rate` and ended at
        /// `resulting`.
        ///
        /// `delta = rate x (target - previous)`, so every term of the target
        /// contributes `rate x term` and the value's own level contributes
        /// `-rate x previous`. That last one is bookkeeping rather than a world
        /// event, which is why it is filed under `Reversion` and the renderer
        /// suppresses it unless it is carrying the change.
        ///
        /// Any difference between that prediction and what actually happened —
        /// a clamped target, a clamped result — is booked honestly rather than
        /// hidden.
        /// </summary>
        public void CommitApproach(float target, float rate, float resulting, float baseline = 0f)
        {
            if (!Causal.Records(state, countryId)) return;

            float modelled = Sum();

            // A target that was clamped is not the sum of its terms any more.
            float clampedAway = target - (baseline + modelled);
            if (Math.Abs(clampedAway) > Causal.EpsilonValue)
                Add(CausalReason.Bounds, clampedAway, CausalCategory.Other, CausalKind.Indirect);

            for (int i = 0; i < terms.Count; i++) terms[i].value *= rate;

            // The anchor the value settles toward when nothing is pressing on
            // it, net of where the value already stands. Folded together on
            // purpose: "the baseline is 52 and you are at 61" is one fact about
            // where this is heading, not two causes, and splitting it would put
            // a constant on screen as though the world had done something.
            Add(CausalReason.Reversion, rate * (baseline - previous),
                CausalCategory.Other, CausalKind.Indirect);

            Close(resulting, CausalReconciliation.Scaled);
        }

        /// <summary>Close a value whose terms were applied directly to it.</summary>
        public void CommitAdditive(float resulting,
                                   CausalReconciliation reconciliation = CausalReconciliation.Exact)
        {
            if (!Causal.Records(state, countryId)) return;
            Close(resulting, reconciliation);
        }

        void Close(float resulting, CausalReconciliation reconciliation)
        {
            var ledger = state.causal;
            if (ledger == null) return;

            float actual = resulting - previous;
            float explained = Sum();
            float remainder = actual - explained;

            if (terms.Count == 0 && Math.Abs(actual) < Causal.EpsilonValue) return;

            // **One record per metric per month**, shared with every `Note` site.
            // A decomposition and a scattered episodic cause both describing the
            // same value in the same month have to be the same explanation, or
            // the operator is shown two partial accounts of one movement.
            var record = Causal.OpenRecord(state, countryId, metric, previous);
            if (record == null) return;

            record.reconciliation = reconciliation;
            for (int i = 0; i < terms.Count; i++) Causal.Append(record, terms[i]);

            record.resulting = resulting;
            record.delta = record.resulting - record.previous;

            if (Math.Abs(remainder) > Causal.EpsilonValue)
            {
                record.unexplained += remainder;
                Causal.Append(record, new CausalContribution(
                    CausalReason.Unattributed, remainder, CausalCategory.Other, CausalKind.Indirect));
            }
        }
    }
}

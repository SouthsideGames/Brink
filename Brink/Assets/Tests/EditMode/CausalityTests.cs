using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The causal explainability framework (spec 26).
    ///
    /// Two properties carry the whole feature, and they pull against each other:
    /// an explanation has to be **true** — the listed causes are the ones the
    /// simulation actually applied, and where figures are printed they add up —
    /// and it has to be **no more true than the operator's government is
    /// entitled to**, because a layer that explains everything is a free
    /// intelligence service and would quietly repeal the fog every other system
    /// is built around.
    ///
    /// The third property is that it changes nothing. An observability layer
    /// that moves the world it observes is not observability, so the last two
    /// tests run the same seed with recording on and off and demand the same
    /// world out.
    /// </summary>
    public class CausalityTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            Causal.Enabled = true;
            Causal.RecordForeign = false;

            // Cabinet filtering is a real part of disclosure and has its own
            // test below. It is off for the rest of them because the seeded
            // cabinet's competence is a *roll*: leaving it on would make every
            // rendering assertion depend on how good a minister the world
            // happened to appoint, which is how a test ends up passing on a coin
            // flip.
            ReportingSystem.Disabled = true;
            state = WorldFactory.CreateDebugWorld(seed: 7731);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            Causal.Enabled = true;
            Causal.RecordForeign = false;
            ReportingSystem.Disabled = false;
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState Player => state.PlayerCountry;

        void RunMonths(int count)
        {
            for (int i = 0; i < count; i++) turns.EndMonth();
        }

        /// <summary>
        /// Put the country under real strain.
        ///
        /// Unrest's pressure terms are **zero by construction** in an untroubled
        /// country — that is the design, not a gap — so a test that needs unrest
        /// to have causes has to give it some. Without this the decomposition is
        /// legitimately empty and an assertion about it proves nothing.
        /// </summary>
        void Stress()
        {
            Player.livingStandards = 24f;
            Player.socialUnrest = 30f;
            Player.publicGrievance = 35f;
            Player.warExhaustion = 40f;

            // Arrangement is "the world as given", not something that happened
            // during a month. The causal month opens where the previous one
            // closed (spec 26 §3a), so a fixture that rewrites the world after
            // wiring must re-take the month-open snapshot or the first month's
            // records would truthfully — and uselessly — explain the fixture.
            Causal.SnapshotOpenings(state);
        }

        // ---- 1. the record describes the movement it was taken from ----

        [Test]
        public void ARecordCarriesPreviousResultingAndNetDelta()
        {
            RunMonths(3);

            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record, "approval was never recorded");

            Assert.AreEqual(record.resulting - record.previous, record.delta, 0.0001f,
                "the stored delta disagrees with its own endpoints");
            Assert.AreEqual(Player.governmentApproval, record.resulting, 0.0001f,
                "the record's resulting value is not the value the country actually holds");
        }

        // ---- 2 + the central correctness property: the figures add up ----

        [Test]
        public void ListedCausesSumToTheObservedChange()
        {
            // A country under real strain, so every metric below actually has
            // something to explain. On an untroubled world the hardship terms
            // are zero by construction and unrest records nothing at all —
            // correctly, but this test would then be asserting reconciliation
            // over an empty decomposition.
            Stress();

            RunMonths(6);

            var metrics = new[]
            {
                CausalMetric.GovernmentApproval,
                CausalMetric.SocialUnrest,
                CausalMetric.LivingStandards,
                CausalMetric.PublicGrievance,
                CausalMetric.MarketIndex,
            };

            foreach (var metric in metrics)
            {
                var record = state.causal.Latest(Player.id, metric);
                Assert.IsNotNull(record, $"{metric} was never recorded");
                Assert.Greater(record.contributions.Count, 1,
                    $"{metric} recorded a single cause — the decomposition is not being built");
                Assert.AreNotEqual(0f, record.delta,
                    $"{metric} did not move, so its reconciliation proves nothing");

                float sum = 0f;
                foreach (var c in record.contributions) sum += c.value;

                // The remainder is booked as a contribution rather than dropped,
                // so a complete decomposition must reconcile to the observed
                // change within rounding.
                Assert.AreEqual(record.delta, sum, 0.01f,
                    $"{metric}: listed causes sum to {sum} but the value moved {record.delta}");
            }
        }

        [Test]
        public void AMultiplierIsRecordedAsItsOwnExactLine()
        {
            // National unity damps the hardship terms of unrest by a factor.
            // Recorded as a signed line worth (sum so far) x (factor - 1), which
            // is exactly what the multiplication did — so the column still adds
            // up and the operator can see the damping as its own cause.
            Player.livingStandards = 10f;   // real hardship, so the terms are non-zero
            Player.nationalUnity = 90f;     // strong damping

            // NARROW PIPELINE: this asserts one month of the government tick's
            // own arithmetic. No other system is needed — and running the world
            // would let a crisis or a war add terms this assertion does not
            // model.
            GovernmentSystem.MonthlyUpdate(state);

            var record = state.causal.Latest(Player.id, CausalMetric.SocialUnrest);
            Assert.IsNotNull(record);

            var unity = record.contributions.Find(c => c.reason == CausalReason.NationalUnity);
            Assert.IsNotNull(unity, "the unity damping was not recorded as a cause");
            Assert.Less(unity.value, 0f, "strong unity should have removed unrest, not added it");

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f, "the multiplier broke reconciliation");
        }

        [Test]
        public void AMonthsRecordDescribesTheWholeMonthNotJustOnePartOfIt()
        {
            // The defect this guards, found by running a real month and reading
            // the panel against the values: a record anchored on the first
            // *instrumented* site reports the movement between that site and the
            // last one, not the movement the operator can see. Approval is moved
            // by a cabinet action before the government tick; the market index is
            // moved again afterwards by a crisis. Both reported a monthly change
            // that did not match the screen — which is the one thing this
            // framework may not do.
            Stress();

            var before = new Dictionary<CausalMetric, float>();
            foreach (var metric in Causal.Reconciled)
                if (Causal.TryRead(Player, metric, out var v)) before[metric] = v;
            Assert.AreEqual(Causal.Reconciled.Length, before.Count,
                "a reconciled metric has no reader, so it can never be reconciled");

            turns.EndMonth();

            int proven = 0;
            foreach (var metric in Causal.Reconciled)
            {
                Assert.IsTrue(Causal.TryRead(Player, metric, out var after));
                float moved = after - before[metric];
                var record = state.causal.Latest(Player.id, metric);

                if (record == null)
                {
                    Assert.AreEqual(0f, moved, 0.0005f,
                        $"{metric} moved {moved} and recorded nothing at all");
                    continue;
                }

                Assert.AreEqual(before[metric], record.previous, 0.0005f,
                    $"{metric}: the record does not open where the month opened");
                Assert.AreEqual(after, record.resulting, 0.0005f,
                    $"{metric}: the record does not close where the month closed");
                Assert.AreEqual(moved, record.delta, 0.0005f,
                    $"{metric}: the reported change is not the change that happened");

                float sum = 0f;
                foreach (var c in record.contributions) sum += c.value;
                Assert.AreEqual(record.delta, sum, 0.01f,
                    $"{metric}: listed causes sum to {sum} against a move of {record.delta}");
                proven++;
            }

            Assert.Greater(proven, 3,
                "too few metrics recorded anything for this to prove a thing");
        }

        [Test]
        public void ACrisisLapseBelongsToTheMonthItLapsed()
        {
            // `TurnManager.EndMonth` lapses unanswered crises *before* the
            // resolution hook fires. The causal month opens where the previous
            // one closed (spec 26 §3a), so the lapse penalty must land inside
            // this month's record — the measured defect was approval endpoints
            // disagreeing with the screen in exactly the months a crisis lapsed.
            state.activeCrises.Add(CrisisSystem.Create(state, EventCatalog.Definitions[0].id));

            float before = Player.governmentApproval;
            turns.EndMonth();
            float after = Player.governmentApproval;

            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record, "the lapse month recorded nothing for approval");
            Assert.AreEqual(before, record.previous, 0.0005f,
                "the lapse penalty fell outside the record — the month did not open where the screen did");
            Assert.AreEqual(after, record.resulting, 0.0005f,
                "the record does not close where the month closed");

            var lapsed = record.contributions.Find(c => c.reason == CausalReason.CrisisLapsed);
            Assert.IsNotNull(lapsed, "letting a crisis lapse was not named as a cause");
            Assert.Less(lapsed.value, -3f, "the lapse penalty lost its magnitude");

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f, "the lapse month does not reconcile");
        }

        [Test]
        public void ABetweenMonthsMutationBelongsToTheMonthItPrecedes()
        {
            // Anything that happens on the operator's turn — here standing in
            // for any episodic, uninstrumented write — belongs to the month
            // about to resolve, and lands in its record as OTHER rather than
            // falling into a gap between two records.
            RunMonths(1);

            float opened = Player.governmentApproval;   // what the screen shows
            Player.governmentApproval = opened + 3f;    // an uninstrumented act

            turns.EndMonth();
            float closed = Player.governmentApproval;

            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            Assert.AreEqual(opened, record.previous, 0.0005f,
                "the record does not open where the operator's turn began");
            Assert.AreEqual(closed, record.resulting, 0.0005f);

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f,
                "the between-months movement was lost rather than booked");
        }

        [Test]
        public void ADebtRestructureIsAttributedAndItsArithmeticIsUntouched()
        {
            Player.fiscal.sovereignDebt = 800f;
            Causal.SnapshotOpenings(state);   // the world as given

            float before = Player.fiscal.sovereignDebt;
            Assert.IsTrue(FiscalSystem.RestructureDebtBy(state, Player.id),
                "the restructure was refused, so this test proves nothing");

            // 1. The debt changed exactly as it always did.
            Assert.AreEqual(before * 0.5f, Player.fiscal.sovereignDebt, 0f,
                "instrumentation changed the write-down arithmetic");

            turns.EndMonth();

            var record = state.causal.Latest(Player.id, CausalMetric.SovereignDebt);
            Assert.IsNotNull(record, "a restructure month recorded nothing for debt");

            // 2. The action is named, with provenance.
            var restructure = record.contributions.Find(
                c => c.reason == CausalReason.DebtRestructured);
            Assert.IsNotNull(restructure, "the write-down is not attributed");
            Assert.AreEqual(-before * 0.5f, restructure.value, 0.01f,
                "the attributed figure is not the write-down");
            Assert.AreEqual(nameof(GameController.RestructureDebt), restructure.sourceActionId,
                "provenance is not the stable verb id");

            // 3. The whole month still reconciles, service and deficit included.
            Assert.AreEqual(before, record.previous, 0.0005f,
                "the record does not open at the pre-restructure stock");
            Assert.AreEqual(Player.fiscal.sovereignDebt, record.resulting, 0.0005f);
            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f, "the restructure month does not reconcile");
        }

        [Test]
        public void AnAnsweredCrisisBelongsToTheMonthItWasAnsweredIn()
        {
            // The real verb, not a raw write: answering a crisis moves approval
            // on the operator's turn through an uninstrumented site. The
            // movement must land in the month about to resolve, with the record
            // opening at the value the operator saw when their turn began.
            RunMonths(1);
            float opened = Player.governmentApproval;

            ActiveCrisis crisis = null;
            int option = -1;
            foreach (var definition in EventCatalog.Definitions)
            {
                var candidate = CrisisSystem.Create(state, definition.id);
                for (int i = 0; i < candidate.options.Count && option < 0; i++)
                    if (Math.Abs(candidate.options[i].approvalDelta) >= 2f) { crisis = candidate; option = i; }
                if (crisis != null) break;
            }
            Assert.IsNotNull(crisis, "no authored crisis option moves approval, so this test cannot run");

            state.activeCrises.Add(crisis);
            CrisisSystem.Resolve(state, crisis, option);
            Assert.AreNotEqual(opened, Player.governmentApproval,
                "the answer did not move approval, so the fixture proves nothing");

            int resolving = state.date.year * 12 + state.date.month;
            turns.EndMonth();
            float closed = Player.governmentApproval;

            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            Assert.AreEqual(resolving, record.MonthIndex, "the answer was booked to the wrong month");
            Assert.AreEqual(opened, record.previous, 0.0005f,
                "the record does not open where the operator's turn began");
            Assert.AreEqual(closed, record.resulting, 0.0005f);

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f, "the answered month does not reconcile");
        }

        [Test]
        public void AYearEndConsequenceIsInsideDecembersBracket()
        {
            // `YearEnded` fires after every `ResolveMonth` handler. If the causal
            // month closed as the last of those, anything the annual evaluation
            // did to a metric would fall after December's record closed and
            // before January's opened. The bracket is `EndMonth` itself, so a
            // year-end consequence belongs to December.
            RunMonths(11);
            Assert.IsTrue(state.date.IsYearEnd, "the clock should read December");

            float opened = Player.governmentApproval;
            bool fired = false;
            turns.YearEnded += _ =>
            {
                fired = true;
                Player.governmentApproval = Math.Min(100f, Player.governmentApproval + 3f);
            };

            int december = state.date.year * 12 + state.date.month;
            turns.EndMonth();
            Assert.IsTrue(fired, "the year-end hook never ran, so this test proves nothing");
            float closed = Player.governmentApproval;

            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            Assert.AreEqual(december, record.MonthIndex);
            Assert.AreEqual(opened, record.previous, 0.0005f);
            Assert.AreEqual(closed, record.resulting, 0.0005f,
                "December's record closed before the year-end consequence landed");

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f);

            // And January opens where December closed — after the evaluation.
            Assert.IsTrue(Causal.TryOpening(state, Player.id, CausalMetric.GovernmentApproval, out var next));
            Assert.AreEqual(closed, next, 0.0005f, "the next month did not open where this one closed");
        }

        [Test]
        public void AMidTurnSaveResumesTheSameCausalMonth()
        {
            // The game autosaves after every player verb, so a save can be taken
            // after the operator acted and before they ended the month. The
            // snapshot travels with the save, or the reload would open the
            // causal month on the post-verb values and lose the verb.
            RunMonths(1);
            float opened = Player.governmentApproval;

            Player.governmentApproval = opened + 3f;   // an uninstrumented act on the operator's turn
            Assert.AreNotEqual(opened, Player.governmentApproval, "the write did not take");

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.IsTrue(Causal.TryOpening(loaded, loaded.playerCountryId, CausalMetric.GovernmentApproval, out var carried),
                "the month-open snapshot did not survive the save");
            Assert.AreEqual(opened, carried, 0.0005f, "the snapshot came back as a different value");

            var resumed = new TurnManager(loaded);
            SimulationPipeline.Wire(resumed, loaded);
            Assert.IsTrue(Causal.TryOpening(loaded, loaded.playerCountryId, CausalMetric.GovernmentApproval, out var afterWire));
            Assert.AreEqual(opened, afterWire, 0.0005f, "wiring overwrote the carried snapshot");

            resumed.EndMonth();

            var record = loaded.causal.Latest(loaded.playerCountryId, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            Assert.AreEqual(opened, record.previous, 0.0005f,
                "the resumed month did not open where the operator's turn began");
            Assert.AreEqual(loaded.PlayerCountry.governmentApproval, record.resulting, 0.0005f);

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.delta, sum, 0.01f);
        }

        [Test]
        public void OneRecordPerMetricPerMonthEvenWhenManySystemsTouchIt()
        {
            // War exhaustion is written at eleven sites across six systems. Two
            // explanations of one month's movement is two partial accounts, and
            // the operator would be shown whichever one a screen happened to
            // fetch.
            Stress();
            RunMonths(3);

            var seen = new Dictionary<string, int>();
            foreach (var r in state.causal.records)
            {
                string key = r.countryId + "|" + r.metric + "|" + r.MonthIndex;
                seen[key] = seen.TryGetValue(key, out var n) ? n + 1 : 1;
            }
            foreach (var pair in seen)
                Assert.AreEqual(1, pair.Value, $"{pair.Key} has {pair.Value} records for one month");
        }

        // ---- 3. signs survive into the rendering ----

        [Test]
        public void PositiveAndNegativeCausesRenderWithTheirSign()
        {
            var record = Fabricate(new[]
            {
                (CausalReason.CostOfLiving, -2.1f),
                (CausalReason.EconomicGrowth, +0.8f),
            });

            string text = CausalExplanation.Render(
                CausalDisclosure.Disclose(state, record), 52);

            StringAssert.Contains("-2.1", text);
            StringAssert.Contains("+0.8", text);
            StringAssert.Contains("COST OF LIVING", text);
            StringAssert.Contains("ECONOMIC GROWTH", text);
        }

        // ---- 4. ordering is deterministic ----

        [Test]
        public void CauseOrderingIsDeterministicAcrossRenders()
        {
            // Two causes of identical magnitude: without a stable tiebreak these
            // could swap between renders and the panel would flicker.
            var record = Fabricate(new[]
            {
                (CausalReason.Unemployment, -1.5f),
                (CausalReason.CostOfLiving, -1.5f),
                (CausalReason.EconomicGrowth, +3.0f),
            });

            string first = CausalExplanation.Render(CausalDisclosure.Disclose(state, record), 52);
            string second = CausalExplanation.Render(CausalDisclosure.Disclose(state, record), 52);
            Assert.AreEqual(first, second, "the same record rendered two different ways");

            int growth = first.IndexOf("ECONOMIC GROWTH", StringComparison.Ordinal);
            int living = first.IndexOf("COST OF LIVING", StringComparison.Ordinal);
            Assert.Less(growth, living, "causes are not ranked by size of effect");
        }

        // ---- 5. hidden causes stay hidden ----

        [Test]
        public void AClassifiedCauseIsNeverRendered()
        {
            var record = Fabricate(new[] { (CausalReason.EconomicGrowth, +1.0f) });
            record.contributions.Add(new CausalContribution(
                CausalReason.CovertAction, -8.2f, CausalCategory.Intelligence,
                CausalKind.Direct, CausalVisibility.Classified)
            { sourceCountryId = "CHN" });
            // The record itself is complete — the simulation explains itself to
            // itself in full — so its endpoints include the classified movement.
            record.delta = -7.2f;
            record.resulting = record.previous + record.delta;

            var view = CausalDisclosure.Disclose(state, record);
            string text = CausalExplanation.Render(view, 52);

            // Never named, and never *counted* — a withheld tally that ticks up
            // when a classified cause is present is an existence flag, which is
            // exactly what classification must not produce (spec 26 §4).
            Assert.AreEqual(0, view.withheld,
                "a classified cause was counted — the count is an existence side-channel");
            StringAssert.DoesNotContain("COVERT", text);
            StringAssert.DoesNotContain("CHN", text);
            StringAssert.DoesNotContain("NOT REPORTED", text,
                "the reader was told something is missing, which reveals that it exists");

            // The magnitude rides inside OTHER so the column still adds up —
            // the net movement is on the operator's screen regardless.
            float sum = 0f;
            foreach (var cause in view.causes) { Assert.IsTrue(cause.sized); sum += cause.value; }
            Assert.AreEqual(view.delta, sum, 0.01f,
                "hiding the classified cause broke the column");
        }

        [Test]
        public void ClassifiedLeavesNoExistenceSideChannel()
        {
            // The strong form: a record whose movement is partly classified must
            // be *indistinguishable* from one with the same ordinary
            // unattributed movement. If any pixel differs, the difference is a
            // side-channel.
            var classified = Fabricate(new[] { (CausalReason.EconomicGrowth, +1.0f) });
            classified.contributions.Add(new CausalContribution(
                CausalReason.CovertAction, -8.2f, CausalCategory.Intelligence,
                CausalKind.Direct, CausalVisibility.Classified)
            { sourceCountryId = "CHN" });
            classified.delta = -7.2f;
            classified.resulting = classified.previous + classified.delta;

            var mundane = Fabricate(new[] { (CausalReason.EconomicGrowth, +1.0f) });
            mundane.contributions.Add(new CausalContribution(
                CausalReason.Unattributed, -8.2f, CausalCategory.Other));
            mundane.delta = -7.2f;
            mundane.resulting = mundane.previous + mundane.delta;

            var viewClassified = CausalDisclosure.Disclose(state, classified);
            var viewMundane = CausalDisclosure.Disclose(state, mundane);

            Assert.AreEqual(viewMundane.withheld, viewClassified.withheld);
            Assert.AreEqual(viewMundane.reconciliation, viewClassified.reconciliation);
            Assert.AreEqual(
                CausalExplanation.Render(viewMundane, 52),
                CausalExplanation.Render(viewClassified, 52),
                "a classified cause rendered differently from ordinary unattributed movement");
        }

        [Test]
        public void AForeignReaderWithConfirmedCollectionCannotTellClassifiedFromUnattributed()
        {
            // The case the first fix missed: a foreign reader with confirmed
            // collection is handed sized figures and an additive total. Dropping
            // the classified cause there would leave a column that no longer
            // summed to the net change — the existence flag by another route.
            var rival = state.countries.Find(c => c.id != state.playerCountryId);
            Assert.IsNotNull(rival);
            state.estimates.Add(new IntelEstimate
            {
                observerId = Player.id,
                targetId = rival.id,
                domain = IntelDomain.Political,
                confidence = ConfidenceGrade.Confirmed,
                everCollected = true,
                reportedValue = 50f,
            });

            var classified = Fabricate(new[] { (CausalReason.Deprivation, -1.0f) });
            classified.countryId = rival.id;
            classified.contributions.Add(new CausalContribution(
                CausalReason.CovertAction, -8.2f, CausalCategory.Intelligence,
                CausalKind.Direct, CausalVisibility.Classified)
            { sourceCountryId = Player.id });
            classified.delta = -9.2f;
            classified.resulting = classified.previous + classified.delta;

            var mundane = Fabricate(new[] { (CausalReason.Deprivation, -1.0f) });
            mundane.countryId = rival.id;
            mundane.contributions.Add(new CausalContribution(
                CausalReason.Unattributed, -8.2f, CausalCategory.Other));
            mundane.delta = -9.2f;
            mundane.resulting = mundane.previous + mundane.delta;

            var viewClassified = CausalDisclosure.Disclose(state, classified, Player.id);
            var viewMundane = CausalDisclosure.Disclose(state, mundane, Player.id);

            // Non-vacuity: the reader really is being shown figures.
            Assert.Greater(viewMundane.causes.Count, 0, "confirmed collection showed the reader nothing");
            Assert.AreNotEqual(CausalReconciliation.Qualitative, viewMundane.reconciliation,
                "the reader was not handed an additive column, so a short column could not have leaked");

            Assert.AreEqual(0, viewClassified.withheld);
            Assert.AreEqual(viewMundane.reconciliation, viewClassified.reconciliation);
            Assert.AreEqual(
                CausalExplanation.Render(viewMundane, 52),
                CausalExplanation.Render(viewClassified, 52),
                "a foreign reader could tell a classified cause from unattributed movement");

            float sum = 0f;
            foreach (var cause in viewClassified.causes) sum += cause.value;
            Assert.AreEqual(viewClassified.delta, sum, 0.01f, "the disclosed column fell short of the net change");
        }

        [Test]
        public void AnUnattributedForeignHandIsNamedButNotSized()
        {
            var record = Fabricate(new[] { (CausalReason.EconomicGrowth, +1.0f) });
            record.contributions.Add(new CausalContribution(
                CausalReason.ForeignInterference, -4.4f, CausalCategory.Intelligence,
                CausalKind.Direct, CausalVisibility.Suspected)
            { sourceCountryId = "" });

            var view = CausalDisclosure.Disclose(state, record);
            string text = CausalExplanation.Render(view, 52);

            StringAssert.Contains("FOREIGN INTERFERENCE", text);
            StringAssert.DoesNotContain("-4.4", text);
            Assert.AreEqual(CausalReconciliation.Qualitative, view.reconciliation,
                "an unsized cause must stop the panel claiming an additive total");
            StringAssert.DoesNotContain("NET CHANGE", text);
        }

        [Test]
        public void ForeignInternalCausesNeedCollection()
        {
            // A rival's own politics, with nothing collected against them.
            var rival = state.countries.Find(c => c.id != state.playerCountryId);
            Assert.IsNotNull(rival);

            var record = Fabricate(new[] { (CausalReason.Deprivation, -6.0f) });
            record.countryId = rival.id;

            var view = CausalDisclosure.Disclose(state, record, state.playerCountryId);

            Assert.AreEqual(0, view.causes.Count,
                "a foreign government's internal reasons were readable with no collection");
            Assert.Greater(view.withheld, 0);
            Assert.IsTrue(view.Opaque);
            StringAssert.DoesNotContain("DEPRIVATION",
                CausalExplanation.Render(view, 52));
        }

        [Test]
        public void APoorDeskLosesSmallCausesAndNeverRestatesAFigure()
        {
            ReportingSystem.Disabled = false;

            var official = CabinetAdvice.OfficialFor(state, Pillar.Government);
            Assert.IsNotNull(official, "no government desk to degrade");
            official.mode = ControlMode.Autonomous;
            official.competence = 5f;

            var record = Fabricate(new[]
            {
                (CausalReason.CostOfLiving, -4.0f),
                (CausalReason.Unemployment, -0.02f),
            });

            var view = CausalDisclosure.Disclose(state, record);

            // The small one is buried...
            Assert.Greater(view.withheld, 0, "a barely-competent desk lost nothing at all");

            // ...and every figure that survived is the one the simulation
            // recorded. Missed or buried, never distorted (spec 15).
            foreach (var cause in view.causes)
            {
                var original = record.contributions.Find(c => c.reason == cause.reason);
                Assert.IsNotNull(original);
                Assert.AreEqual(original.value, cause.value, 0f,
                    "a reported figure was altered rather than merely withheld");
            }
        }

        [Test]
        public void DirectControlRemovesTheReportingFilter()
        {
            ReportingSystem.Disabled = false;

            var official = CabinetAdvice.OfficialFor(state, Pillar.Government);
            Assert.IsNotNull(official);
            official.competence = 5f;
            official.mode = ControlMode.DirectControl;

            var record = Fabricate(new[]
            {
                (CausalReason.CostOfLiving, -4.0f),
                (CausalReason.Unemployment, -0.02f),
            });

            var view = CausalDisclosure.Disclose(state, record);
            Assert.AreEqual(0, view.withheld,
                "what the operator runs themselves, they should see in full");
        }

        // ---- 6. estimates carry their confidence ----

        [Test]
        public void AnEstimatedCauseKeepsAConfidence()
        {
            var record = Fabricate(new[] { (CausalReason.EconomicGrowth, +1.0f) });
            record.contributions.Add(new CausalContribution(
                CausalReason.SanctionPressure, -3.0f, CausalCategory.Diplomatic,
                CausalKind.Direct, CausalVisibility.Estimated)
            { confidence = 0.4f });

            var view = CausalDisclosure.Disclose(state, record);
            var estimated = view.causes.Find(c => c.reason == CausalReason.SanctionPressure);

            Assert.IsNotNull(estimated, "an estimated cause should still be reported");
            Assert.IsTrue(estimated.sized, "an estimate carries a figure, with a confidence beside it");
            Assert.AreEqual(0.4f, estimated.confidence, 0.001f);
        }

        // ---- 7. old saves ----

        [Test]
        public void ASaveWithNoCausalHistoryLoadsAndStartsRecording()
        {
            // Non-vacuity first: a save that never had any history would pass
            // the emptiness assertion below for the wrong reason.
            RunMonths(2);
            Assert.Greater(state.causal.records.Count, 0,
                "nothing was recorded, so this test would prove nothing");

            // A save written before the ledger existed simply has no key for it.
            string json = SaveSystem.ToJson(state).Replace("\"causal\"", "\"causal_absent\"");
            StringAssert.DoesNotContain("\"causal\":", json, "the ledger key was not actually removed");

            var loaded = SaveSystem.FromJson(json);
            Assert.IsNotNull(loaded, "an old save failed to load");
            Assert.IsNotNull(loaded.causal, "the ledger should default rather than come back null");
            Assert.AreEqual(0, loaded.causal.records.Count,
                "history was invented for months that resolved before the feature existed");

            var resumed = new TurnManager(loaded);
            SimulationPipeline.Wire(resumed, loaded);
            resumed.EndMonth();

            Assert.Greater(loaded.causal.records.Count, 0,
                "an old save did not begin accumulating explanations");
        }

        [Test]
        public void ALedgerSurvivesASaveAndReload()
        {
            RunMonths(2);
            int before = state.causal.records.Count;
            Assert.Greater(before, 0);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(before, loaded.causal.records.Count);

            var record = loaded.causal.Latest(loaded.playerCountryId, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record, "the approval explanation did not survive the round trip");
            Assert.Greater(record.contributions.Count, 0, "contributions were lost in serialization");
        }

        // ---- 8. bounded ----

        [Test]
        public void HistoryIsBoundedAndKeepsTheNewest()
        {
            RunMonths(CausalLedger.MonthsKept + 8);

            var approval = state.causal.History(Player.id, CausalMetric.GovernmentApproval, 999);
            Assert.LessOrEqual(approval.Count, CausalLedger.MonthsKept,
                "the ledger grew past its own bound");
            Assert.Greater(approval.Count, 0);

            // Newest first, and the newest is the month that was last *resolved*.
            // `TurnManager.EndMonth` raises `ResolveMonth` and only then advances
            // `State.date`, so a record is stamped with the month it describes
            // and the clock afterwards reads the next, still-unresolved month.
            // That is the behaviour we want — "MAR 2041, approval fell 3.8" has
            // to name March — so the expectation is one behind the clock.
            var resolved = new GameDate(state.date.year, state.date.month);
            int lastResolved = resolved.year * 12 + resolved.month - 1;
            Assert.AreEqual(lastResolved, approval[0].MonthIndex,
                "the most recent resolved month is not at the head of the history");

            for (int i = 1; i < approval.Count; i++)
                Assert.Less(approval[i].MonthIndex, approval[i - 1].MonthIndex,
                    "history is not in strict newest-first order");
        }

        [Test]
        public void OneMetricCannotEvictAnother()
        {
            RunMonths(CausalLedger.MonthsKept + 6);

            Assert.Greater(state.causal.History(Player.id, CausalMetric.MarketIndex, 999).Count, 0,
                "a chatty metric evicted a quiet one");
            Assert.Greater(state.causal.History(Player.id, CausalMetric.PublicGrievance, 999).Count, 0);
        }

        [Test]
        public void ContributionsWithinARecordAreCapped()
        {
            int planted = CausalLedger.MaxContributions + 10;
            var record = new CausalRecord
            { metric = CausalMetric.Treasury, countryId = Player.id, previous = 100f };
            for (int i = 0; i < planted; i++)
                record.contributions.Add(new CausalContribution(
                    CausalReason.GovernmentSpending, -1f, CausalCategory.Fiscal));
            record.delta = -planted;
            record.resulting = record.previous + record.delta;

            var ledger = new CausalLedger();
            ledger.Add(record);

            Assert.LessOrEqual(record.contributions.Count, CausalLedger.MaxContributions);

            // Capped by folding, never by dropping: the magnitude is all there.
            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.resulting, record.previous + sum, 0.001f,
                "the cap threw contributions away instead of folding them");
        }

        [Test]
        public void FoldingKeepsTheLargestNamedCausesInOrderAndOneOtherRow()
        {
            // Twenty distinct named causes of distinct size, alternating sign.
            // The thirteen largest must survive, in the order they were
            // recorded, with exactly one OTHER row carrying the sum of the seven
            // smallest — and folding again must change nothing.
            int planted = CausalLedger.MaxContributions + 6;
            var reasons = NamedReasons(planted);
            var record = new CausalRecord
            { metric = CausalMetric.GovernmentApproval, countryId = Player.id, previous = 50f };
            float total = 0f;
            for (int i = 0; i < planted; i++)
            {
                float value = (i + 1) * 0.5f * (i % 2 == 0 ? 1f : -1f);
                total += value;
                record.contributions.Add(new CausalContribution(reasons[i], value, CausalCategory.Other));
            }
            record.delta = total;
            record.resulting = record.previous + total;

            CausalLedger.FoldToCap(record);

            Assert.AreEqual(CausalLedger.MaxContributions, record.contributions.Count);

            int folded = planted - (CausalLedger.MaxContributions - 1);
            float expectedOther = 0f;
            for (int i = 0; i < folded; i++) expectedOther += (i + 1) * 0.5f * (i % 2 == 0 ? 1f : -1f);

            int others = 0;
            int expectedNext = folded;   // the first surviving named cause
            for (int i = 0; i < record.contributions.Count; i++)
            {
                var c = record.contributions[i];
                if (c.reason == CausalReason.Unattributed)
                {
                    others++;
                    Assert.AreEqual(expectedOther, c.value, 0.001f, "OTHER does not carry the folded magnitude");
                    continue;
                }
                Assert.AreEqual(reasons[expectedNext], c.reason,
                    "a smaller cause survived while a larger one was folded, or the order was rewritten");
                expectedNext++;
            }
            Assert.AreEqual(1, others, "folding minted more than one OTHER row");
            Assert.AreEqual(planted, expectedNext, "a large named cause went missing");

            float sum = 0f;
            foreach (var c in record.contributions) sum += c.value;
            Assert.AreEqual(record.resulting, record.previous + sum, 0.001f);

            // Idempotent, and therefore deterministic across repeated folds.
            var before = new List<(CausalReason, float)>();
            foreach (var c in record.contributions) before.Add((c.reason, c.value));
            CausalLedger.FoldToCap(record);
            Assert.AreEqual(before.Count, record.contributions.Count);
            for (int i = 0; i < before.Count; i++)
            {
                Assert.AreEqual(before[i].Item1, record.contributions[i].reason);
                Assert.AreEqual(before[i].Item2, record.contributions[i].value, 0f);
            }
        }

        [Test]
        public void AnOverflowingMonthFoldsIntoOtherAndStillReconciles()
        {
            // The live path: more named causes in one month than a record may
            // hold, through `Causal.Apply`, then the pipeline adds its own on
            // top. `previous + Σ contributions == resulting` has to survive it.
            RunMonths(1);
            float opened = Player.governmentApproval;

            int planted = CausalLedger.MaxContributions + 6;
            var reasons = NamedReasons(planted);
            for (int i = 0; i < planted; i++)
            {
                float step = 0.1f * (i + 1) * (i % 2 == 0 ? 1f : -1f);
                Causal.Apply(state, Player.id, CausalMetric.GovernmentApproval, reasons[i],
                    ref Player.governmentApproval, Player.governmentApproval + step, CausalCategory.Other);
            }

            turns.EndMonth();

            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            Assert.LessOrEqual(record.contributions.Count, CausalLedger.MaxContributions,
                "the record grew past its bound");

            int others = 0;
            float sum = 0f;
            foreach (var c in record.contributions)
            {
                sum += c.value;
                if (c.reason == CausalReason.Unattributed) others++;
            }
            Assert.AreEqual(1, others, "a busy month should carry exactly one OTHER row");
            Assert.AreEqual(opened, record.previous, 0.0005f);
            Assert.AreEqual(Player.governmentApproval, record.resulting, 0.0005f);
            Assert.AreEqual(record.resulting, record.previous + sum, 0.01f,
                "an overflowing month stopped adding up");

            // The largest planted cause is the one worth keeping, and it was kept.
            Assert.IsNotNull(record.contributions.Find(c => c.reason == reasons[planted - 1]),
                "the largest named cause was folded away while smaller ones survived");
        }

        /// <summary>`count` distinct, non-structural reasons, in enum order.</summary>
        static CausalReason[] NamedReasons(int count)
        {
            var picked = new List<CausalReason>();
            foreach (CausalReason reason in Enum.GetValues(typeof(CausalReason)))
            {
                if (reason == CausalReason.Unspecified || CausalReasons.IsStructural(reason)) continue;
                picked.Add(reason);
                if (picked.Count == count) break;
            }
            Assert.AreEqual(count, picked.Count, "not enough distinct reasons exist for this test");
            return picked.ToArray();
        }

        // ---- 9. provenance ----

        [Test]
        public void APlayerDecisionIsStillNamedInALaterConsequence()
        {
            // A standing decision, taken once, in a country with a street to
            // suppress — the suppression is worth `pressure x (0.45 - 1)`, so
            // with no pressure there is correctly nothing to attribute.
            Stress();
            Player.government.civicPosture = CivicPosture.Restrictive;

            // ...and still explaining the street several months later.
            RunMonths(4);

            var record = state.causal.Latest(Player.id, CausalMetric.SocialUnrest);
            Assert.IsNotNull(record);

            var posture = record.contributions.Find(
                c => c.reason == CausalReason.CivicPosture);
            Assert.IsNotNull(posture, "the posture decision stopped being named as a cause");
            Assert.AreEqual(nameof(GameController.SetCivicPosture), posture.sourceActionId,
                "provenance is not a stable verb id");
            Assert.AreEqual(CausalCategory.PlayerDecision, posture.category);
            Assert.Less(posture.value, 0f,
                "a restrictive posture suppresses the expression of unrest");
        }

        [Test]
        public void ProvenanceIsAStableVerbIdNotADisplayString()
        {
            RunMonths(2);
            var record = state.causal.Latest(Player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);

            foreach (var c in record.contributions)
            {
                if (string.IsNullOrEmpty(c.sourceActionId)) continue;

                // `nameof` on a real verb, so renaming the verb breaks the build
                // here rather than orphaning the provenance — the ActionCatalog
                // discipline, reused.
                Assert.IsNotNull(
                    typeof(GameController).GetMethod(c.sourceActionId),
                    $"provenance '{c.sourceActionId}' names no GameController verb");
            }
        }

        // ---- 10. the panel fits the terminal ----

        [Test]
        public void TheExplanationFitsEveryTerminalWidth()
        {
            RunMonths(3);
            var view = CausalDisclosure.Disclose(
                state, state.causal.Latest(Player.id, CausalMetric.GovernmentApproval));

            // 40 is narrower than any real phone panel; 96 is wider than any
            // tablet. Nothing between may overflow.
            for (int width = 40; width <= 96; width += 4)
            {
                foreach (string line in CausalExplanation.Render(view, width).Split('\n'))
                    Assert.LessOrEqual(line.Length, width,
                        $"a row ran off a {width}-column panel: '{line}'");
            }
        }

        [Test]
        public void TheHistoryBlockFitsEveryTerminalWidth()
        {
            Stress();
            RunMonths(5);

            var records = state.causal.History(Player.id, CausalMetric.SocialUnrest, 6);
            var views = new List<DisclosedExplanation>();
            foreach (var r in records) views.Add(CausalDisclosure.Disclose(state, r));

            for (int width = 40; width <= 96; width += 4)
                foreach (string line in CausalExplanation.RenderHistory(views, width).Split('\n'))
                    Assert.LessOrEqual(line.Length, width,
                        $"a history row ran off a {width}-column panel: '{line}'");
        }

        [Test]
        public void AVeryLongCauseLabelIsTruncatedRatherThanOverflowing()
        {
            var record = Fabricate(new[] { (CausalReason.DisplacementAtSource, -1.2f) });
            record.contributions[0].sourceCountryId = "AVERYLONGCOUNTRYIDENTIFIER";

            foreach (string line in CausalExplanation.Render(
                         CausalDisclosure.Disclose(state, record), 40).Split('\n'))
                Assert.LessOrEqual(line.Length, 40, $"overflowing row: '{line}'");
        }

        // ---- 11 + 12. it changes nothing ----

        [Test]
        public void RecordingDoesNotChangeTheInstrumentedMetrics()
        {
            var withRecording = Play(seed: 4242, months: 24, recording: true);
            var without = Play(seed: 4242, months: 24, recording: false);

            Assert.AreEqual(without.approval, withRecording.approval, 0f,
                "approval moved because recording was switched on");
            Assert.AreEqual(without.unrest, withRecording.unrest, 0f, "unrest moved");
            Assert.AreEqual(without.standards, withRecording.standards, 0f, "living standards moved");
            Assert.AreEqual(without.grievance, withRecording.grievance, 0f, "grievance moved");
            Assert.AreEqual(without.market, withRecording.market, 0f, "the market index moved");
            Assert.AreEqual(without.exhaustion, withRecording.exhaustion, 0f, "war exhaustion moved");
            Assert.AreEqual(without.debt, withRecording.debt, 0f, "sovereign debt moved");
        }

        [Test]
        public void TheWorldAsAWholeResolvesIdenticallyWithRecordingOff()
        {
            var withRecording = Play(seed: 9184, months: 24, recording: true);
            var without = Play(seed: 9184, months: 24, recording: false);

            // A world-wide fingerprint rather than the player's own figures: an
            // RNG drawn one extra time anywhere would show up here and nowhere
            // else, which is the failure mode an observability layer is most
            // likely to introduce.
            Assert.AreEqual(without.fingerprint, withRecording.fingerprint,
                "the world diverged when recording was enabled");
        }

        [Test]
        public void TwoIdenticalRunsWithRecordingOnStayIdentical()
        {
            var first = Play(seed: 5150, months: 18, recording: true);
            var second = Play(seed: 5150, months: 18, recording: true);
            Assert.AreEqual(first.fingerprint, second.fingerprint,
                "recording made the simulation non-deterministic");
        }

        [Test]
        public void NothingIsRecordedForForeignCountriesByDefault()
        {
            RunMonths(3);

            foreach (var record in state.causal.records)
                Assert.AreEqual(state.playerCountryId, record.countryId,
                    "a foreign country's movements are being stored in the player's save");
        }

        // ---- helpers ----

        struct Outcome
        {
            public float approval, unrest, standards, grievance, market, exhaustion, debt;
            public double fingerprint;
        }

        static Outcome Play(int seed, int months, bool recording)
        {
            bool previous = Causal.Enabled;
            Causal.Enabled = recording;
            try
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var manager = new TurnManager(world);
                SimulationPipeline.Wire(manager, world);
                for (int i = 0; i < months; i++) manager.EndMonth();

                var player = world.PlayerCountry;
                double fingerprint = 0d;
                foreach (var country in world.countries)
                    fingerprint += country.governmentApproval + country.socialUnrest * 3d
                                   + country.livingStandards * 7d + country.publicGrievance * 11d
                                   + country.economy.marketIndex * 13d + country.warExhaustion * 17d
                                   + country.resources.treasury * 0.001d
                                   + country.fiscal.sovereignDebt * 0.0001d;

                return new Outcome
                {
                    approval = player.governmentApproval,
                    unrest = player.socialUnrest,
                    standards = player.livingStandards,
                    grievance = player.publicGrievance,
                    market = player.economy.marketIndex,
                    exhaustion = player.warExhaustion,
                    debt = player.fiscal.sovereignDebt,
                    fingerprint = fingerprint,
                };
            }
            finally
            {
                Causal.Enabled = previous;
            }
        }

        /// <summary>
        /// A record built by hand, for the disclosure and rendering claims that
        /// need a specific shape rather than whatever the world happened to do.
        /// </summary>
        CausalRecord Fabricate((CausalReason reason, float value)[] causes)
        {
            var record = new CausalRecord
            {
                metric = CausalMetric.GovernmentApproval,
                countryId = state.playerCountryId,
                year = state.date.year,
                month = state.date.month,
                previous = 50f,
                reconciliation = CausalReconciliation.Exact,
            };

            float sum = 0f;
            foreach (var (reason, value) in causes)
            {
                record.contributions.Add(new CausalContribution(
                    reason, value, CausalCategory.Economic));
                sum += value;
            }

            record.delta = sum;
            record.resulting = record.previous + sum;
            return record;
        }
    }
}

using System;
using System.IO;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Production-side provenance guards for operator decisions that feed the
    /// monthly debrief. These tests assert the causal record at its source rather
    /// than the presentation layer, so a renderer cannot manufacture authorship
    /// that the simulation never recorded.
    /// </summary>
    public class ProductionProvenanceTests
    {
        GameState state;
        TurnManager turns;
        string saveDir;
        string previousSaveDir;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            Causal.Enabled = true;
            Causal.RecordForeign = false;
            ReportingSystem.Disabled = true;

            // The operator wrappers autosave. Redirect the slot so exercising a
            // real production path cannot touch the developer's own saves.
            saveDir = Path.Combine(Path.GetTempPath(), "brink_provenance_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveDir);
            previousSaveDir = SaveSystem.SaveDirectoryOverride;
            SaveSystem.SaveDirectoryOverride = saveDir;

            state = WorldFactory.CreateDebugWorld(seed: 7731);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.SaveDirectoryOverride = previousSaveDir;
            try { Directory.Delete(saveDir, true); } catch { }
            Causal.Enabled = true;
            Causal.RecordForeign = false;
            ReportingSystem.Disabled = false;
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>
        /// The live controller, attached to this fixture's world, with the
        /// constitutional and fiscal room an operator verb needs. Tests that go
        /// through here are exercising the real player boundary rather than a
        /// reconstruction of it.
        /// </summary>
        GameController Operator()
        {
            var controller = GameController.Instance;
            controller.NewGame(7731);
            state = controller.State;
            turns = controller.Turns;
            state.PlayerCountry.government.legislativeSupport = 90f;
            state.politicalCapital = 20f;
            state.commandPoints.current = 20;
            return controller;
        }

        static CausalContribution ContributionOf(GameState state, CausalMetric metric, CausalReason reason)
        {
            var record = state.causal.Latest(state.playerCountryId, metric);
            return record?.contributions.Find(c => c.reason == reason);
        }

        [Test]
        public void CrisisLapseCarriesPlayerDecisionProvenance()
        {
            state.activeCrises.Add(CrisisSystem.Create(state, EventCatalog.Definitions[0].id));

            turns.EndMonth();

            var record = state.causal.Latest(state.playerCountryId, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record, "the lapse month recorded no approval movement");

            var lapsed = record.contributions.Find(c => c.reason == CausalReason.CrisisLapsed);
            Assert.IsNotNull(lapsed, "the lapse penalty was not recorded as a causal contribution");
            Assert.AreEqual(CausalCategory.PlayerDecision, lapsed.category,
                "an unanswered operator crisis must remain a player decision");
            Assert.AreEqual("CrisisLapsed", lapsed.sourceActionId,
                "the player decision has no production action provenance");
        }

        [Test]
        public void LapsedMarketCrisisIsNotMisreportedAsAnAnsweredDecision()
        {
            state.activeCrises.Add(CrisisSystem.Create(state, "MARKET_PANIC"));

            turns.EndMonth();

            var contribution = ContributionOf(state, CausalMetric.MarketIndex,
                CausalReason.MarketConditions);
            Assert.IsNotNull(contribution, "the unanswered panic left no market cause");
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category);
            Assert.AreEqual("CrisisLapsed", contribution.sourceActionId,
                "silence was incorrectly reported as an answered Crisis Turn");
        }

        [Test]
        public void AnsweredCrisisApprovalNamesTheDecisionAndReachesTheDebrief()
        {
            ActiveCrisis crisis = null;
            int optionIndex = -1;
            foreach (var definition in EventCatalog.Definitions)
            {
                var candidate = CrisisSystem.Create(state, definition.id);
                for (int i = 0; i < candidate.options.Count; i++)
                {
                    if (Math.Abs(candidate.options[i].approvalDelta) < 2f) continue;
                    crisis = candidate;
                    optionIndex = i;
                    break;
                }
                if (crisis != null) break;
            }
            Assert.IsNotNull(crisis, "no authored crisis option moves approval");

            float before = state.PlayerCountry.governmentApproval;
            state.activeCrises.Add(crisis);
            CrisisSystem.Resolve(state, crisis, optionIndex);

            var contribution = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.CrisisDecision);
            Assert.IsNotNull(contribution, "the answered crisis was left under OTHER");
            Assert.AreEqual(state.PlayerCountry.governmentApproval - before,
                contribution.value, 0.0005f, "the named cause does not equal the applied movement");
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category);
            Assert.AreEqual(nameof(GameController.ResolveCrisis), contribution.sourceActionId);

            turns.EndMonth();
            var consequence = MonthlyDebriefSystem.Build(state).consequences.Find(
                c => c.metric == CausalMetric.GovernmentApproval);
            Assert.IsNotNull(consequence, "the answered crisis disappeared at rollover");
            Assert.IsTrue(consequence.playerLinked,
                "the operator's crisis response reads as world-driven");
            Assert.AreEqual(nameof(GameController.ResolveCrisis), consequence.sourceActionId);
        }

        [Test]
        public void AnsweredCrisisMarketShockKeepsItsCauseAndNamesTheDecision()
        {
            var crisis = new ActiveCrisis { defId = "PROVENANCE_TEST", title = "Market test" };
            crisis.options.Add(new CrisisOption
            {
                label = "ACT",
                resultText = "The decision is taken.",
                effectId = CrisisEffects.MarketShock,
                effectMagnitude = -12f
            });
            state.activeCrises.Add(crisis);

            CrisisSystem.Resolve(state, crisis, 0);

            var contribution = ContributionOf(state, CausalMetric.MarketIndex,
                CausalReason.MarketConditions);
            Assert.IsNotNull(contribution, "the market shock was not recorded");
            Assert.Less(contribution.value, 0f, "the adverse shock did not lower the index");
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category,
                "the operator's selected response reads as an autonomous market movement");
            Assert.AreEqual(nameof(GameController.ResolveCrisis), contribution.sourceActionId);

            turns.EndMonth();
            var consequence = MonthlyDebriefSystem.Build(state).consequences.Find(
                c => c.metric == CausalMetric.MarketIndex);
            Assert.IsNotNull(consequence);
            Assert.IsTrue(consequence.playerLinked,
                "the debrief lost the operator's hand in the market consequence");
            Assert.AreEqual(nameof(GameController.ResolveCrisis), consequence.sourceActionId);
        }

        [Test]
        public void OperatorPeaceTermsNameEachApprovalEffect()
        {
            var controller = Operator();
            var war = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            Assert.IsNotNull(war);
            state.FindCountry("CHN").warSupport = 5f;
            state.FindCountry("CHN").pillars.government = 10f;
            war.defenderWarExhaustion = 85f;
            war.momentum = 55f;

            Assert.IsTrue(controller.ProposeTerms(PeaceProposal.Of(PeaceTerm.PrisonerExchange)),
                "the other side refused a concession-only settlement, so this test proves nothing");

            var exchange = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.PrisonerExchange);
            var settlement = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.PeaceSettlement);
            Assert.IsNotNull(exchange, "the prisoner exchange remained under OTHER");
            Assert.IsNotNull(settlement, "the settlement dividend remained under OTHER");
            Assert.AreEqual(3f, exchange.value, 0.0005f);
            Assert.AreEqual(6f, settlement.value, 0.0005f);
            Assert.AreEqual(CausalCategory.PlayerDecision, exchange.category);
            Assert.AreEqual(nameof(GameController.ProposeTerms), exchange.sourceActionId);
            Assert.AreEqual(nameof(GameController.ProposeTerms), settlement.sourceActionId);

            turns.EndMonth();
            var consequence = MonthlyDebriefSystem.Build(state).consequences.Find(
                c => c.metric == CausalMetric.GovernmentApproval);
            Assert.IsNotNull(consequence, "the settlement disappeared at rollover");
            Assert.IsTrue(consequence.playerLinked);
            Assert.AreEqual(nameof(GameController.ProposeTerms), consequence.sourceActionId);
        }

        [Test]
        public void AcceptedForeignTermsNameTheirDomesticCosts()
        {
            var controller = Operator();
            var war = ConfrontationSystem.BeginBy(state, "CHN", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            Assert.IsNotNull(war);
            var offer = PeaceProposal.Of(PeaceTerm.Reparations,
                PeaceTerm.PoliticalConcessions, PeaceTerm.PrisonerExchange);
            Assert.IsTrue(ConfrontationSystem.OfferConstructedTermsToPlayer(
                state, war, "CHN", offer));

            var crisis = state.activeCrises[state.activeCrises.Count - 1];
            controller.ResolveCrisis(crisis, 0);

            var reparations = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.Reparations);
            var concessions = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.PoliticalConcessions);
            var exchange = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.PrisonerExchange);
            Assert.AreEqual(-5f, reparations.value, 0.0005f);
            Assert.AreEqual(-12f, concessions.value, 0.0005f);
            Assert.AreEqual(3f, exchange.value, 0.0005f);
            foreach (var contribution in new[] { reparations, concessions, exchange })
            {
                Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category);
                Assert.AreEqual(nameof(GameController.ResolveCrisis), contribution.sourceActionId);
            }
        }

        [Test]
        public void ActorGenericPeaceDoesNotInventOperatorProvenance()
        {
            var war = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            state.FindCountry("CHN").warSupport = 5f;
            state.FindCountry("CHN").pillars.government = 10f;
            war.defenderWarExhaustion = 85f;
            war.momentum = 55f;
            Assert.IsTrue(PeaceSystem.ProposeTerms(state, war, state.playerCountryId,
                PeaceProposal.Of(PeaceTerm.PrisonerExchange)));

            var exchange = ContributionOf(state, CausalMetric.GovernmentApproval,
                CausalReason.PrisonerExchange);
            Assert.IsNotNull(exchange);
            Assert.AreEqual(CausalCategory.Diplomatic, exchange.category);
            Assert.IsEmpty(exchange.sourceActionId,
                "the player country acting autonomously was misreported as an operator decision");
        }

        [Test]
        public void PlayerDecisionWithoutActionIdDoesNotInventProvenance()
        {
            var player = state.PlayerCountry;
            float before = player.governmentApproval;

            Causal.Note(state, player.id, CausalMetric.GovernmentApproval,
                CausalReason.CrisisLapsed, before, before - 1f,
                CausalCategory.PlayerDecision);

            var record = state.causal.Latest(player.id, CausalMetric.GovernmentApproval);
            Assert.IsNotNull(record);
            var contribution = record.contributions.Find(c => c.reason == CausalReason.CrisisLapsed);
            Assert.IsNotNull(contribution);
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category);
            Assert.AreEqual("", contribution.sourceActionId,
                "the causal layer must not infer operator provenance from category alone");
        }

        [Test]
        public void NonPlayerCategoryWithActionIdDoesNotBecomePlayerDecision()
        {
            var player = state.PlayerCountry;
            float before = player.fiscal.sovereignDebt;

            Causal.Note(state, player.id, CausalMetric.SovereignDebt,
                CausalReason.DebtRestructured, before, before + 1f,
                CausalCategory.Fiscal,
                sourceActionId: "RestructureDebt");

            var record = state.causal.Latest(player.id, CausalMetric.SovereignDebt);
            Assert.IsNotNull(record);
            var contribution = record.contributions.Find(c => c.reason == CausalReason.DebtRestructured);
            Assert.IsNotNull(contribution);
            Assert.AreEqual(CausalCategory.Fiscal, contribution.category,
                "an action id is metadata; it must never promote an autonomous/system cause to player authorship");
            Assert.AreEqual("RestructureDebt", contribution.sourceActionId,
                "the causal layer should preserve explicit metadata and leave authorship classification to the caller");
        }

        // ---------- debt restructuring ----------

        [Test]
        public void OperatorRestructuringCarriesPlayerProvenance()
        {
            var controller = Operator();
            state.PlayerCountry.fiscal.sovereignDebt = 900f;

            Assert.IsTrue(controller.RestructureDebt(),
                "the operator's restructure was refused, so this test proves nothing");

            var contribution = ContributionOf(state, CausalMetric.SovereignDebt, CausalReason.DebtRestructured);
            Assert.IsNotNull(contribution, "the operator's write-down was not attributed");
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category,
                "an order given at the terminal must read as a player decision");
            Assert.AreEqual(nameof(GameController.RestructureDebt), contribution.sourceActionId,
                "the operator boundary must supply the stable verb id");
        }

        [Test]
        public void AutonomousRestructuringCarriesNoPlayerProvenance()
        {
            state.PlayerCountry.fiscal.sovereignDebt = 900f;

            // The same verb the AI and the finance ministry reach, called as
            // they call it — no explicit provenance offered.
            Assert.IsTrue(FiscalSystem.RestructureDebtBy(state, state.playerCountryId),
                "the generic restructure was refused, so this test proves nothing");

            var contribution = ContributionOf(state, CausalMetric.SovereignDebt, CausalReason.DebtRestructured);
            Assert.IsNotNull(contribution, "the autonomous write-down was not recorded at all");
            Assert.AreEqual(CausalCategory.Fiscal, contribution.category,
                "a write-down nobody ordered must not read as a player decision");
            Assert.IsEmpty(contribution.sourceActionId,
                "the generic verb must not stamp the operator's action id on autonomous work");
        }

        [Test]
        public void TheSameRestructuringVerbServesBothActorsInOneWorld()
        {
            // Player country: ordered. Foreign country: autonomous. One verb.
            Causal.RecordForeign = true;
            var foreign = state.FindCountry("CHN");
            state.PlayerCountry.fiscal.sovereignDebt = 700f;
            foreign.fiscal.sovereignDebt = 700f;

            Assert.IsTrue(FiscalSystem.RestructureDebtBy(state, state.playerCountryId,
                CausalCategory.PlayerDecision, nameof(GameController.RestructureDebt)));
            Assert.IsTrue(FiscalSystem.RestructureDebtBy(state, foreign.id));

            var ordered = ContributionOf(state, CausalMetric.SovereignDebt, CausalReason.DebtRestructured);
            var autonomous = state.causal.Latest(foreign.id, CausalMetric.SovereignDebt)
                ?.contributions.Find(c => c.reason == CausalReason.DebtRestructured);

            Assert.AreEqual(CausalCategory.PlayerDecision, ordered.category);
            Assert.AreEqual(nameof(GameController.RestructureDebt), ordered.sourceActionId);
            Assert.IsNotNull(autonomous, "the foreign write-down was not recorded");
            Assert.AreEqual(CausalCategory.Fiscal, autonomous.category,
                "a foreign government's write-down is not the operator's decision");
            Assert.IsEmpty(autonomous.sourceActionId);
            Assert.AreEqual(350f, state.PlayerCountry.fiscal.sovereignDebt, 0.01f,
                "attribution changed the write-down arithmetic");
            Assert.AreEqual(350f, foreign.fiscal.sovereignDebt, 0.01f,
                "attribution changed the write-down arithmetic");
        }

        // ---------- sovereign debt issuance ----------

        [Test]
        public void OperatorIssuanceCarriesPlayerProvenance()
        {
            var controller = Operator();
            state.PlayerCountry.fiscal.creditStanding = 80f;
            state.PlayerCountry.fiscal.sovereignDebt = 0f;

            Assert.IsTrue(controller.IssueSovereignDebt(),
                "the operator's issue was refused, so this test proves nothing");

            var contribution = ContributionOf(state, CausalMetric.SovereignDebt, CausalReason.BondIssue);
            Assert.IsNotNull(contribution, "the operator's issue was not attributed");
            Assert.AreEqual(CausalCategory.PlayerDecision, contribution.category);
            Assert.AreEqual(nameof(GameController.IssueSovereignDebt), contribution.sourceActionId);
        }

        [Test]
        public void AutonomousIssuanceCarriesNoPlayerProvenance()
        {
            // Reachable autonomously: AISystem.ManageTheBooks borrows before a
            // government's treasury runs dry.
            state.PlayerCountry.fiscal.creditStanding = 80f;
            state.PlayerCountry.fiscal.sovereignDebt = 0f;

            Assert.IsTrue(FiscalSystem.IssueSovereignDebtBy(state, state.playerCountryId),
                "the generic issue was refused, so this test proves nothing");

            var contribution = ContributionOf(state, CausalMetric.SovereignDebt, CausalReason.BondIssue);
            Assert.IsNotNull(contribution, "the autonomous issue was not recorded at all");
            Assert.AreEqual(CausalCategory.Fiscal, contribution.category,
                "borrowing nobody ordered must not read as a player decision");
            Assert.IsEmpty(contribution.sourceActionId);
        }

        // ---------- sanction blowback ----------

        /// <summary>
        /// Resolve one month of the real economy tick and return the market
        /// index's sanction-blowback contributions in the order recorded.
        /// </summary>
        System.Collections.Generic.List<CausalContribution> BlowbackContributions()
        {
            turns.EndMonth();
            var record = state.causal.Latest(state.playerCountryId, CausalMetric.MarketIndex);
            Assert.IsNotNull(record, "the month recorded no market movement");
            return record.contributions.FindAll(c => c.reason == CausalReason.SanctionBlowback);
        }

        [Test]
        public void OperatorSanctionBlowbackCarriesPlayerProvenance()
        {
            var controller = Operator();
            Assert.IsTrue(controller.ImposeSanctions("CHN", SanctionSeverity.Severe),
                "the operator's sanctions were refused, so this test proves nothing");

            var blowback = BlowbackContributions();
            Assert.AreEqual(1, blowback.Count, "only the operator's own measures are in force");
            Assert.AreEqual(CausalCategory.PlayerDecision, blowback[0].category);
            Assert.AreEqual(CausalKind.Indirect, blowback[0].kind);
            Assert.AreEqual(nameof(GameController.ImposeSanctions), blowback[0].sourceActionId);
        }

        [Test]
        public void AutonomousSanctionsFromThePlayerCountryAreNotPlayerAuthored()
        {
            // Same sender — the player's own country — but nobody ordered it at
            // the terminal: this is the government acting on its own account.
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN",
                SanctionSeverity.Severe, "CRISIS"));

            var blowback = BlowbackContributions();
            Assert.AreEqual(1, blowback.Count);
            Assert.AreEqual(CausalCategory.Diplomatic, blowback[0].category,
                "the player country being the sender is not proof the operator ordered it");
            Assert.AreEqual(CausalKind.Indirect, blowback[0].kind);
            Assert.IsEmpty(blowback[0].sourceActionId);
        }

        [Test]
        public void UnknownSanctionProvenanceDefaultsToTheWorld()
        {
            // An old save, or a marker a later version writes.
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN",
                SanctionSeverity.Severe));
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "RUS",
                SanctionSeverity.Severe, "SOME_FUTURE_CAUSE"));

            var blowback = BlowbackContributions();
            foreach (var contribution in blowback)
            {
                Assert.AreNotEqual(CausalCategory.PlayerDecision, contribution.category,
                    "unknown provenance must never be promoted to player authorship");
                Assert.IsEmpty(contribution.sourceActionId);
            }
        }

        [Test]
        public void TheOperatorCommandPersistsTheAuthorshipMarker()
        {
            // The link in the chain the arithmetic test below relies on: what the
            // player-facing wrapper actually writes to the save.
            var controller = Operator();
            Assert.IsTrue(controller.ImposeSanctions("CHN", SanctionSeverity.Severe));

            var sanction = state.FindSanction(state.playerCountryId, "CHN");
            Assert.IsNotNull(sanction, "the operator's regime was not recorded");
            Assert.AreEqual(EconomySystem.OperatorSanctionCause, sanction.cause,
                "the operator boundary no longer marks its own regimes, so provenance cannot be recovered");
        }

        [Test]
        public void MixedSanctionOriginsSplitWithoutChangingTheArithmetic()
        {
            // One ordered at the terminal (the marker the wrapper writes, proven
            // by the test above), one the government's own. Imposed through the
            // generic verb so this world and the control below are identical in
            // every respect except the authorship marker.
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN",
                SanctionSeverity.Severe, EconomySystem.OperatorSanctionCause));
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "RUS",
                SanctionSeverity.Severe, "CRISIS"));

            float total = EconomySystem.SanctionBlowbackFor(state, state.playerCountryId);
            float ordered = EconomySystem.PlayerSanctionBlowbackFor(state, state.playerCountryId);
            Assert.Greater(ordered, 0f, "the ordered regime contributed nothing, so the split is untested");
            Assert.Greater(total, ordered, "the autonomous regime contributed nothing, so the split is untested");

            var blowback = BlowbackContributions();
            Assert.AreEqual(2, blowback.Count, "a mixed month must show both origins");

            var player = blowback.Find(c => c.category == CausalCategory.PlayerDecision);
            var world = blowback.Find(c => c.category == CausalCategory.Diplomatic);
            Assert.IsNotNull(player, "the operator's own measures are missing from the explanation");
            Assert.IsNotNull(world, "the government's own measures are missing from the explanation");
            Assert.AreEqual(nameof(GameController.ImposeSanctions), player.sourceActionId);
            Assert.IsEmpty(world.sourceActionId);

            // The two together are exactly the single line they replaced.
            // Measured against a control world built from the same seed and given
            // the same two regimes, differing only in that both are autonomous —
            // so its single world line carries the whole aggregate. Comparing to
            // a control rather than re-deriving the month's approach scale, which
            // would be a second copy of the formula under test.
            var control = WorldFactory.CreateDebugWorld(seed: 7731);
            var controlTurns = new TurnManager(control);
            SimulationPipeline.Wire(controlTurns, control);
            EconomySystem.ImposeSanctionsBy(control, control.playerCountryId, "CHN", SanctionSeverity.Severe, "CRISIS");
            EconomySystem.ImposeSanctionsBy(control, control.playerCountryId, "RUS", SanctionSeverity.Severe, "CRISIS");
            controlTurns.EndMonth();

            var controlRecord = control.causal.Latest(control.playerCountryId, CausalMetric.MarketIndex);
            float aggregate = 0f;
            foreach (var contribution in controlRecord.contributions)
                if (contribution.reason == CausalReason.SanctionBlowback) aggregate += contribution.value;
            Assert.AreNotEqual(0f, aggregate, "the control world recorded no blowback, so there is nothing to compare");
            Assert.AreEqual(aggregate, player.value + world.value, 0.0005f,
                "the split does not add back to the aggregate the simulation used");

            // And the month still reconciles with two lines where there was one.
            var record = state.causal.Latest(state.playerCountryId, CausalMetric.MarketIndex);
            float sum = 0f;
            foreach (var contribution in record.contributions) sum += contribution.value;
            Assert.AreEqual(record.delta, sum, 0.0005f,
                "splitting the blowback line broke the month's reconciliation");
        }

        [Test]
        public void SplittingBlowbackLeavesTheEconomyBitIdentical()
        {
            // The split is presentation of an existing term, so a world with
            // recording off — no contributions built at all — must resolve to the
            // same market index as one with it on.
            var recorded = WorldFactory.CreateDebugWorld(seed: 7731);
            var recordedTurns = new TurnManager(recorded);
            SimulationPipeline.Wire(recordedTurns, recorded);
            EconomySystem.ImposeSanctionsBy(recorded, recorded.playerCountryId, "CHN", SanctionSeverity.Severe, "PLAYER");
            EconomySystem.ImposeSanctionsBy(recorded, recorded.playerCountryId, "RUS", SanctionSeverity.Severe, "CRISIS");

            var silent = WorldFactory.CreateDebugWorld(seed: 7731);
            var silentTurns = new TurnManager(silent);
            SimulationPipeline.Wire(silentTurns, silent);
            EconomySystem.ImposeSanctionsBy(silent, silent.playerCountryId, "CHN", SanctionSeverity.Severe, "PLAYER");
            EconomySystem.ImposeSanctionsBy(silent, silent.playerCountryId, "RUS", SanctionSeverity.Severe, "CRISIS");

            Causal.Enabled = false;
            for (int i = 0; i < 6; i++) silentTurns.EndMonth();
            Causal.Enabled = true;
            for (int i = 0; i < 6; i++) recordedTurns.EndMonth();

            Assert.AreNotEqual(100f, recorded.PlayerCountry.economy.marketIndex,
                "the sanctions never moved the index, so this test proves nothing");
            Assert.AreEqual(silent.PlayerCountry.economy.marketIndex,
                recorded.PlayerCountry.economy.marketIndex, 0f,
                "attribution changed the market index the simulation produces");
            Assert.AreEqual(silent.PlayerCountry.economy.growthRate,
                recorded.PlayerCountry.economy.growthRate, 0f,
                "attribution changed growth");
            Assert.AreEqual(silent.PlayerCountry.resources.treasury,
                recorded.PlayerCountry.resources.treasury, 0f,
                "attribution changed the treasury");
        }

        // ---------- what the operator is actually told ----------

        static MonthlyDebriefSystem.Consequence DebriefFor(GameState state, CausalMetric metric)
            => MonthlyDebriefSystem.Build(state).consequences.Find(c => c.metric == metric);

        [Test]
        public void OperatorRestructuringReachesPlayerAttributionInTheDebrief()
        {
            var controller = Operator();
            state.PlayerCountry.fiscal.sovereignDebt = 900f;
            Assert.IsTrue(controller.RestructureDebt());
            turns.EndMonth();

            var consequence = DebriefFor(state, CausalMetric.SovereignDebt);
            Assert.IsNotNull(consequence, "the debrief lost the restructure month entirely");
            Assert.IsTrue(consequence.playerLinked, "the operator's own order reads as world-driven");
            Assert.AreNotEqual(MonthlyDebriefSystem.Involvement.World, consequence.involvement);
            Assert.AreEqual(nameof(GameController.RestructureDebt), consequence.sourceActionId);
            Assert.IsNotEmpty(MonthlyDebriefPresentation.ProvenanceLabel(consequence));
        }

        [Test]
        public void AutonomousRestructuringNeverReachesPlayerAttributionInTheDebrief()
        {
            state.PlayerCountry.fiscal.sovereignDebt = 900f;
            Assert.IsTrue(FiscalSystem.RestructureDebtBy(state, state.playerCountryId));
            turns.EndMonth();

            var consequence = DebriefFor(state, CausalMetric.SovereignDebt);
            Assert.IsNotNull(consequence, "the debrief lost the restructure month entirely");
            Assert.IsFalse(consequence.playerLinked,
                "a write-down the finance ministry made on its own must not be reported as the operator's order");
            Assert.IsFalse(consequence.playerDominant);
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.World, consequence.involvement);
            Assert.IsEmpty(consequence.sourceActionId);
            Assert.AreEqual("", MonthlyDebriefPresentation.ProvenanceLabel(consequence));
            Assert.AreEqual("WORLD-DRIVEN", MonthlyDebriefPresentation.InvolvementLabel(consequence));
        }

        [Test]
        public void OperatorSanctionsReachPlayerAttributionAndAutonomousOnesDoNot()
        {
            var controller = Operator();
            Assert.IsTrue(controller.ImposeSanctions("CHN", SanctionSeverity.Severe));
            turns.EndMonth();

            var ordered = DebriefFor(state, CausalMetric.MarketIndex);
            Assert.IsNotNull(ordered, "the debrief lost the market month");
            Assert.IsTrue(ordered.playerLinked, "the operator's own sanctions read as world-driven");
            Assert.AreEqual(nameof(GameController.ImposeSanctions), ordered.sourceActionId);

            // A second world, same seed, same measures — imposed by the
            // government rather than ordered at the terminal.
            var autonomousWorld = WorldFactory.CreateDebugWorld(seed: 7731);
            var autonomousTurns = new TurnManager(autonomousWorld);
            SimulationPipeline.Wire(autonomousTurns, autonomousWorld);
            Assert.IsTrue(EconomySystem.ImposeSanctionsBy(autonomousWorld,
                autonomousWorld.playerCountryId, "CHN", SanctionSeverity.Severe, "CRISIS"));
            autonomousTurns.EndMonth();

            var autonomous = DebriefFor(autonomousWorld, CausalMetric.MarketIndex);
            Assert.IsNotNull(autonomous);
            Assert.IsFalse(autonomous.playerLinked,
                "sanctions the government imposed itself must not be reported as the operator's order");
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.World, autonomous.involvement);
            Assert.IsEmpty(autonomous.sourceActionId);
        }

        [Test]
        public void ClassifiedOperatorSanctionsLeakNothingToTheDebrief()
        {
            var controller = Operator();
            Assert.IsTrue(controller.ImposeSanctions("CHN", SanctionSeverity.Severe));
            turns.EndMonth();

            var open = DebriefFor(state, CausalMetric.MarketIndex);
            Assert.IsTrue(open.playerLinked, "the disclosed case is not set up, so the comparison is empty");

            var record = state.causal.Latest(state.playerCountryId, CausalMetric.MarketIndex);
            foreach (var contribution in record.contributions)
                if (contribution.category == CausalCategory.PlayerDecision)
                    contribution.visibility = CausalVisibility.Classified;

            var closed = DebriefFor(state, CausalMetric.MarketIndex);
            Assert.IsNotNull(closed);
            Assert.IsFalse(closed.playerLinked, "classified player involvement leaked through playerLinked");
            Assert.IsFalse(closed.playerDominant, "classified player involvement leaked through playerDominant");
            Assert.AreEqual(MonthlyDebriefSystem.Involvement.World, closed.involvement);
            Assert.IsEmpty(closed.sourceActionId, "a classified cause leaked its action id");
            Assert.IsFalse(closed.incomplete,
                "a classified cause must not reveal its existence through an incompleteness marker");
            Assert.AreEqual("", MonthlyDebriefPresentation.ProvenanceLabel(closed));
            Assert.AreEqual("WORLD-DRIVEN", MonthlyDebriefPresentation.InvolvementLabel(closed));
        }
    }
}

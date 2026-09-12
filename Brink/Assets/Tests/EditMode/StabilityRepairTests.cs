using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The debt / sanction / collapse ratchet, and the fiscal signal under it
    /// (core stability repair, 2026-09; spec 02 §9a, §5).
    ///
    /// The audit measured every unattended 40-year world converging on the
    /// same ruin: sovereign debt at 300–700% of GDP, a sanction count that only
    /// grew, ~100 coups, 13–15 of 16 states with a market index under 20. Each
    /// edge of that loop was a value with no restoring force or a signal that
    /// had been blinded. These pin the repairs: deficit financing answers to
    /// creditworthiness and leaves visible arrears when nobody will lend; one
    /// fiscal condition is read by the grade, the mandate and both finance
    /// ministries; austerity is not prescribed into a depression; a sanction
    /// chills a relationship to a target rather than draining it to zero and
    /// lapses when its cause is gone; conspiracy has a resting point.
    /// </summary>
    public class StabilityRepairTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7272);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        // ---------- helpers ----------

        CountryState Country(string id) => state.FindCountry(id);

        static void MakeSound(CountryState c)
        {
            c.resources.treasury = 800f;
            c.fiscal.sovereignDebt = c.economy.gdp * 0.2f;
            c.fiscal.creditStanding = 70f;
            c.fiscal.deficitFinancedMonths = 0;
            c.fiscal.arrearsMonths = 0;
            c.fiscal.restructuringMemoryMonths = 0;
        }

        // ---------- 1. the fiscal condition ----------

        [Test]
        public void TheConditionReadsTheRepresentativeProfiles()
        {
            var c = Country("CHN");

            MakeSound(c);
            Assert.AreEqual(FiscalCondition.Sound, FiscalSystem.ConditionOf(state, c));

            // A shortfall covered by a creditworthy state: cash-negative, fine.
            c.fiscal.deficitFinancedMonths = 2;
            Assert.AreEqual(FiscalCondition.CashNegativeButCreditworthy, FiscalSystem.ConditionOf(state, c));

            // Living on credit for half a year.
            c.fiscal.deficitFinancedMonths = 8;
            Assert.AreEqual(FiscalCondition.DeficitFinanced, FiscalSystem.ConditionOf(state, c));

            // A heavy stock, or wary markets.
            MakeSound(c);
            c.fiscal.sovereignDebt = c.economy.gdp * 1.5f;
            Assert.AreEqual(FiscalCondition.DebtStressed, FiscalSystem.ConditionOf(state, c));
            MakeSound(c);
            c.fiscal.creditStanding = 25f;
            Assert.AreEqual(FiscalCondition.DebtStressed, FiscalSystem.ConditionOf(state, c));

            // Nobody will lend and the account is deep in the red.
            MakeSound(c);
            c.fiscal.creditStanding = 5f;
            c.resources.treasury = -FiscalSystem.AnnualIncome(c) * 0.5f;
            Assert.AreEqual(FiscalCondition.Crisis, FiscalSystem.ConditionOf(state, c));

            // The debt was just written down.
            MakeSound(c);
            c.fiscal.restructuringMemoryMonths = FiscalSystem.RestructuringMemoryMonths;
            Assert.AreEqual(FiscalCondition.Crisis, FiscalSystem.ConditionOf(state, c));

            // A heavy debt nobody will lend into, with the account in the black,
            // is stress rather than crisis: the ordinary budget's problem.
            MakeSound(c);
            c.fiscal.creditStanding = 5f;
            c.fiscal.sovereignDebt = c.economy.gdp * 1.6f;
            Assert.AreEqual(FiscalCondition.DebtStressed, FiscalSystem.ConditionOf(state, c));
        }

        [Test]
        public void TheSolvencyPenaltyRanksTheProfiles_AndReadsMoreThanTheBalance()
        {
            var c = Country("CHN");

            MakeSound(c);
            float sound = ProgressionSystem.SolvencyPenalty(state, c);

            c.fiscal.deficitFinancedMonths = 2;
            float cash = ProgressionSystem.SolvencyPenalty(state, c);

            c.fiscal.deficitFinancedMonths = 12;
            float financed = ProgressionSystem.SolvencyPenalty(state, c);

            MakeSound(c);
            c.fiscal.sovereignDebt = c.economy.gdp * 2.0f;
            float stressed = ProgressionSystem.SolvencyPenalty(state, c);

            MakeSound(c);
            c.fiscal.creditStanding = 5f;
            c.resources.treasury = -FiscalSystem.AnnualIncome(c);
            float crisis = ProgressionSystem.SolvencyPenalty(state, c);

            Assert.AreEqual(0f, sound, 0.001f);
            Assert.Greater(cash, sound);
            Assert.Greater(financed, cash);
            Assert.Greater(stressed, financed);
            Assert.Greater(crisis, stressed);
            Assert.AreEqual(20f, crisis, 0.001f);

            // The case the audit found: 300% of GDP, account zeroed by financing.
            MakeSound(c);
            c.resources.treasury = 0f;
            c.fiscal.sovereignDebt = c.economy.gdp * 3f;
            c.fiscal.deficitFinancedMonths = 40;
            Assert.Greater(ProgressionSystem.SolvencyPenalty(state, c), 10f,
                "a posting three times its GDP in debt graded penalty-free because the balance read zero");
        }

        [Test]
        public void ADeficitFinancedStateIsNotSolventForTheMandate()
        {
            var player = state.PlayerCountry;
            var objective = new MandateObjective { kind = MandateObjectiveKind.Solvent, text = "Solvent." };

            MakeSound(player);
            Assert.IsTrue(MandateSystem.IsMet(state, objective));

            player.fiscal.deficitFinancedMonths = 2;
            Assert.IsTrue(MandateSystem.IsMet(state, objective), "a month or two of borrowing is not insolvency");

            player.resources.treasury = 0f;
            player.fiscal.sovereignDebt = player.economy.gdp * 3f;
            player.fiscal.deficitFinancedMonths = 30;
            Assert.IsFalse(MandateSystem.IsMet(state, objective),
                "the Solvent objective was a free tick for a government living on credit at 300% of GDP");
        }

        // ---------- 2. deficits answer to creditworthiness ----------

        [Test]
        public void NobodyLendsToAStateWithNoCredit_SoTheShortfallIsArrears()
        {
            var c = Country("CHN");
            MakeSound(c);
            c.fiscal.sovereignDebt = c.economy.gdp * 0.5f;
            c.fiscal.creditStanding = 5f;
            c.resources.treasury = -100f;
            float debtBefore = c.fiscal.sovereignDebt;

            // NARROW PIPELINE: one fiscal tick; nothing else may touch the
            // account between the shortfall and the reading.
            FiscalSystem.MonthlyUpdate(state);

            Assert.Less(c.resources.treasury, 0f, "with no lender the shortfall stays unpaid — and visible");
            Assert.AreEqual(debtBefore, c.fiscal.sovereignDebt, 0.001f, "arrears are not borrowing");
            Assert.AreEqual(1, c.fiscal.arrearsMonths);

            // The same shortfall with credit is financed, as before.
            MakeSound(c);
            c.fiscal.creditStanding = 60f;
            c.resources.treasury = -100f;
            debtBefore = c.fiscal.sovereignDebt;
            FiscalSystem.MonthlyUpdate(state);
            Assert.AreEqual(0f, c.resources.treasury, 0.001f);
            Assert.Greater(c.fiscal.sovereignDebt, debtBefore);
            Assert.AreEqual(1, c.fiscal.deficitFinancedMonths);
        }

        [Test]
        public void ArrearsReachConfidenceThroughItsTarget_AndLiftWhenTheyClear()
        {
            var c = Country("CHN");
            MakeSound(c);
            c.fiscal.arrearsMonths = 3;
            c.resources.treasury = -50f;
            float pulledDown = c.economy.confidence;
            EconomySystem.MonthlyUpdate(state);
            float inArrears = c.economy.confidence;

            MakeSound(c);
            c.economy.confidence = pulledDown;
            EconomySystem.MonthlyUpdate(state);
            float clear = c.economy.confidence;

            Assert.Less(inArrears, clear, "arrears should weigh on confidence");
        }

        // ---------- 3. the finance ministry answers a crisis ----------

        [Test]
        public void AForeignMinistryTightensInACrisis_ButNotIntoADepression()
        {
            var c = Country("CHN");
            MakeSound(c);
            c.fiscal.creditStanding = 5f;
            c.resources.treasury = -FiscalSystem.AnnualIncome(c) * 0.3f;
            c.fiscal.budgetPosture = BudgetPosture.Balanced;
            c.fiscal.taxRate = 35f;
            c.economy.marketIndex = 85f;
            Assume.That(FiscalSystem.ConditionOf(state, c), Is.EqualTo(FiscalCondition.Crisis));

            FiscalSystem.MonthlyUpdate(state);
            Assert.AreEqual(BudgetPosture.Austerity, c.fiscal.budgetPosture, "a crisis in a working economy gets austerity");
            Assert.Greater(c.fiscal.taxRate, 35f, "and revenue is raised");

            // The same crisis in a depression: cuts would deepen it.
            MakeSound(c);
            c.fiscal.creditStanding = 5f;
            c.resources.treasury = -FiscalSystem.AnnualIncome(c) * 0.3f;
            c.fiscal.budgetPosture = BudgetPosture.Austerity;
            c.fiscal.taxRate = 46f;
            c.economy.marketIndex = 30f;
            FiscalSystem.MonthlyUpdate(state);
            Assert.AreEqual(BudgetPosture.Balanced, c.fiscal.budgetPosture,
                "austerity in a depression is abandoned, not prescribed");
            Assert.Less(c.fiscal.taxRate, 46f, "and the tax rate comes back toward what a slump can bear");
        }

        [Test]
        public void AStateThatCannotCarryItsDebtWritesItDown()
        {
            var c = Country("CHN");
            MakeSound(c);
            c.fiscal.creditStanding = 5f;
            c.fiscal.sovereignDebt = c.economy.gdp * 2.5f;
            c.resources.treasury = 100f;
            float before = c.fiscal.sovereignDebt;

            FiscalSystem.MonthlyUpdate(state);

            Assert.Less(c.fiscal.sovereignDebt, before * 0.6f,
                "a state nobody will lend to at 250% of GDP defaults rather than carrying it forever");
            Assert.IsTrue(c.fiscal.HasRestructured);
        }

        [Test]
        public void ThePlayersOwnMinistryIsNotOverruled()
        {
            var player = state.PlayerCountry;
            MakeSound(player);
            player.fiscal.creditStanding = 5f;
            player.resources.treasury = -FiscalSystem.AnnualIncome(player) * 0.3f;
            player.fiscal.budgetPosture = BudgetPosture.Balanced;
            player.economy.marketIndex = 85f;

            // The fiscal tick steadies foreign books only.
            FiscalSystem.MonthlyUpdate(state);
            Assert.AreEqual(BudgetPosture.Balanced, player.fiscal.budgetPosture,
                "the fiscal tick must not run the operator's economy for them");
        }

        [Test]
        public void ADelegatedEconomyDeskSteadiesThePlayersBooks_ADirectedOneDoesNot()
        {
            var player = state.PlayerCountry;
            var desk = CabinetAdvice.OfficialFor(state, Pillar.Economy);
            Assume.That(desk, Is.Not.Null);

            void PutInCrisis()
            {
                MakeSound(player);
                player.fiscal.creditStanding = 5f;
                player.resources.treasury = -FiscalSystem.AnnualIncome(player) * 0.3f;
                player.fiscal.budgetPosture = BudgetPosture.Balanced;
                player.economy.marketIndex = 85f;
            }

            PutInCrisis();
            desk.mode = ControlMode.Autonomous;
            CabinetSystem.MonthlyAct(state);
            Assert.AreEqual(BudgetPosture.Austerity, player.fiscal.budgetPosture,
                "a delegating operator's country used to have no fiscal response at all");

            PutInCrisis();
            desk.mode = ControlMode.Directed;
            CabinetSystem.MonthlyAct(state);
            Assert.AreEqual(BudgetPosture.Balanced, player.fiscal.budgetPosture,
                "a directed desk leaves the budget to the operator who is directing it");
        }

        [Test]
        public void TheAIRaisesRevenueWhenTheBooksAreInTrouble()
        {
            // Run the world; some state will end up leaning on its tax rate.
            // The claim is only that the lever is reachable at all — it had no
            // AI caller before this.
            for (int m = 0; m < 120; m++) turns.EndMonth();
            bool anyMoved = false;
            foreach (var c in state.countries)
                if (!c.isPlayer && Math.Abs(c.fiscal.taxRate - FiscalState.BaselineTaxRate) > 0.5f) anyMoved = true;
            Assert.IsTrue(anyMoved, "no foreign government touched its tax rate in ten years");
        }

        // ---------- 4. sanctions: a target, a cause, a lapse ----------

        [Test]
        public void ASanctionChillsARelationshipToATarget_NotToZero()
        {
            var pair = state.FindRelationship("RUS", "CHN");
            pair.relations = 60f;
            pair.trust = 50f;
            pair.strategicAlignment = 60f;
            state.sanctions.Add(new Sanction
            {
                senderId = "RUS", targetId = "CHN", severity = SanctionSeverity.Pressure, imposedDate = state.date
            });

            // NARROW PIPELINE: the bilateral tick only, so nothing lifts the
            // sanction or moves alignment for reasons this test does not model.
            for (int m = 0; m < 120; m++) DiplomacySystem.MonthlyUpdate(state);

            Assert.Greater(pair.relations, EconomySystem.SanctionHostilityLine,
                "a sanctioned pair used to be driven to zero, which guaranteed the regime could never lapse");
            Assert.AreEqual(pair.strategicAlignment - DiplomacySystem.SanctionChill, pair.relations, 6f,
                "the chill is a target below alignment, and the relationship settles there");
            Assert.GreaterOrEqual(pair.trust, DiplomacySystem.SanctionTrustFloor - 0.01f,
                "trust erodes under sanctions toward a floor, not to nothing");
        }

        [Test]
        public void ASanctionLapsesWhenItsCauseIsGone_AndStandsWhileItStands()
        {
            Sanction Fresh()
            {
                state.sanctions.Clear();
                var s = new Sanction
                {
                    senderId = "RUS", targetId = "CHN", severity = SanctionSeverity.Pressure,
                    imposedDate = state.date, monthsActive = EconomySystem.SanctionReviewMonths - 1
                };
                state.sanctions.Add(s);
                return s;
            }
            var pair = state.FindRelationship("RUS", "CHN");

            // Cause gone: relations recovered.
            Fresh();
            pair.relations = 40f;
            EconomySystem.AgeSanctions(state);
            Assert.IsNull(state.FindSanction("RUS", "CHN"), "a regime whose cause is gone lapses at review");

            // Cause stands: still cold.
            Fresh();
            pair.relations = 20f;
            EconomySystem.AgeSanctions(state);
            Assert.IsNotNull(state.FindSanction("RUS", "CHN"), "a regime against a cold state stands");

            // Cause stands: at war, whatever the relations figure says.
            Fresh();
            pair.relations = 40f;
            var war = ConfrontationSystem.BeginBy(state, "RUS", "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            war.escalation = EscalationState.LimitedConflict;
            EconomySystem.AgeSanctions(state);
            Assert.IsNotNull(state.FindSanction("RUS", "CHN"), "nobody lifts measures on a state they are fighting");

            // Threat perception alone no longer keeps a regime alive: it tracks
            // capability and never fades, so measures against any strong state
            // could never lapse.
            war.resolved = true;
            Fresh();
            pair.relations = 40f;
            pair.SetThreatPerceivedBy("RUS", 90f);
            EconomySystem.AgeSanctions(state);
            Assert.IsNull(state.FindSanction("RUS", "CHN"),
                "a strong neighbour is a fact, not a cause; the regime lapses once relations are decent");
        }

        [Test]
        public void ImpositionAndReviewShareOneDefinitionOfHostile()
        {
            var pair = state.FindRelationship("RUS", "CHN");
            pair.relations = 45f;
            Assert.IsFalse(EconomySystem.SanctionCauseStands(state, "RUS", "CHN"),
                "a government does not sanction a state it is on decent terms with");
            pair.relations = 20f;
            Assert.IsTrue(EconomySystem.SanctionCauseStands(state, "RUS", "CHN"));
        }

        // ---------- 5. conspiracy has a resting point ----------

        [Test]
        public void ConspiracySettlesUnderSteadyPressure_AndStillPeaksUnderSevere()
        {
            var c = Country("BRA");
            var gov = c.government;

            void Moderate()
            {
                c.stability = 40f; c.governmentApproval = 35f; c.nationalUnity = 45f;
                c.warExhaustion = 20f; c.economy.inflation = 6f; c.economy.unemployment = 8f;
                gov.legislativeSupport = 40f; gov.eliteCohesion = 40f; gov.militaryLoyalty = 60f;
            }

            // NARROW PIPELINE: the regime tick alone, with the pressure terms
            // held, so the claim is about conspiracy's own dynamics.
            // Proportional decay has a time constant of ~100 months, so the
            // resting level is judged over the second decade, not the first.
            gov.conspiracyLevel = 10f;
            float at180 = 0f, at240 = 0f, peak = 0f;
            for (int m = 0; m < 240; m++)
            {
                Moderate();
                RegimeSystem.MonthlyUpdate(state);
                peak = Math.Max(peak, gov.conspiracyLevel);
                if (m == 179) at180 = gov.conspiracyLevel;
                if (m == 239) at240 = gov.conspiracyLevel;
            }
            Assert.Less(peak, RegimeSystem.CoupThreshold,
                $"moderate, steady pressure used to accumulate to a coup with no sink (peak {peak:F1})");
            Assert.AreEqual(at180, at240, 3f,
                $"conspiracy under steady pressure should settle, not climb ({at180:F1} -> {at240:F1})");

            // Severe pressure still reaches the threshold: coups remain possible.
            gov.conspiracyLevel = 10f;
            peak = 0f;
            for (int m = 0; m < 48; m++)
            {
                c.stability = 5f; c.governmentApproval = 5f; c.nationalUnity = 10f;
                c.warExhaustion = 80f; c.economy.inflation = 20f; c.economy.unemployment = 25f;
                gov.legislativeSupport = 5f; gov.eliteCohesion = 5f; gov.militaryLoyalty = 15f;
                RegimeSystem.MonthlyUpdate(state);
                peak = Math.Max(peak, gov.conspiracyLevel);
            }
            Assert.GreaterOrEqual(peak, RegimeSystem.CoupThreshold,
                "severe failure must still be able to bring a government down");
        }

        // ---------- 6. the quiet decade, the whole world ----------

        [Test]
        public void ThirtyYearsUnattended_TheWorldIsNotAUniformRuin()
        {
            // Not a balance number: a distribution. The audit's 40-year worlds
            // ended with 13–15 of 16 states under a market index of 20 on every
            // seed. A healthy world has some states prospering and some in
            // trouble, and is not the same story everywhere.
            for (int m = 0; m < 360; m++) turns.EndMonth();

            int ruined = 0, prospering = 0;
            foreach (var c in state.countries)
            {
                if (c.economy.marketIndex < 20f) ruined++;
                if (c.economy.marketIndex >= 80f) prospering++;
            }
            Assert.Less(ruined, state.countries.Count / 2,
                $"{ruined} of {state.countries.Count} states ruined after thirty unattended years");
            Assert.Greater(prospering, 0, "nobody prospered in thirty years — the world has one story");
        }
    }
}

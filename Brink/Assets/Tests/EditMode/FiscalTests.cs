using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Public finance (spec 02 §9, spec 25 Tranche A).
    ///
    /// The economy pillar had six verbs and no instrument of public finance at
    /// all: `debtToGdp` was written in one place, read in three, and moved by
    /// nothing the operator could do. The claims here are the four the layer
    /// rests on — borrowing is never free capacity, a deficit becomes debt that
    /// is felt, the defaults are revenue-neutral so the measured balance table
    /// still means something, and every verb is available to the AI at the same
    /// price.
    /// </summary>
    public class FiscalTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6301);
            state.commandPoints.current = 60;
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState Player => state.PlayerCountry;

        // ---------- the defaults change nothing ----------

        [Test]
        public void TheAuthoredDefaultsAreRevenueNeutral()
        {
            // The whole layer is bolted onto the term every balance figure was
            // measured through. If the untouched multipliers were not exactly
            // 1.0, every number in the table would have moved for a reason
            // nobody chose — which is how a "harmless" addition silently becomes
            // a rebalance.
            foreach (var country in state.countries)
            {
                Assert.AreEqual(FiscalState.BaselineTaxRate, country.fiscal.taxRate, 0.001f,
                    $"{country.id} did not start at the revenue-neutral tax rate.");
                Assert.AreEqual(BudgetPosture.Balanced, country.fiscal.budgetPosture,
                    $"{country.id} started on a posture nobody chose.");
                Assert.AreEqual(1f, FiscalSystem.TaxMultiplier(country), 0.005f);
                Assert.AreEqual(1f, FiscalSystem.PostureIncomeMultiplier(country.fiscal.budgetPosture), 0.001f);
                Assert.AreEqual(0f, FiscalSystem.TaxGrowthDrag(country), 0.001f);
                Assert.AreEqual(0f, FiscalSystem.TaxApprovalDrag(country), 0.001f);
            }
        }

        [Test]
        public void ServicingTheDebtCostsWhatTheOldHaircutCost()
        {
            // Revenue-neutral applies to the cost that was *replaced*, not only
            // to the two multipliers that were added. Treasury income used to be
            // haircut by (1 − debtToGdp/400); charging the stock explicitly is
            // what lets credit standing price it, but if the rates are not
            // calibrated against the thing they replaced then every government
            // in the world silently changes how rich it is.
            //
            // That is not a hypothetical: the first pass charged roughly 40% of
            // the old haircut, and it showed up two partitions away as a colder
            // world — AI-vs-AI wars below the floor `WorldHeatTests` guards, and
            // wars running longer because states could afford them.
            foreach (var country in state.countries)
            {
                float legacy = FiscalSystem.LegacyDebtHaircut(country);
                float service = FiscalSystem.MonthlyDebtService(country);

                Assert.Greater(service, legacy * 0.7f,
                    $"{country.id}: servicing costs {service:F1}/mo against the {legacy:F1}/mo the "
                    + "income haircut used to take. Cheaper debt makes the whole world richer.");
                Assert.Less(service, legacy * 1.5f,
                    $"{country.id}: servicing costs {service:F1}/mo against {legacy:F1}/mo before. "
                    + "Dearer debt makes the whole world poorer.");
            }
        }

        [Test]
        public void EveryCountryStartsCarryingTheDebtItWasAuthoredWith()
        {
            // `debtToGdp` is authored; the stock is derived from it at creation.
            // If world generation forgot, every government in the world would
            // begin debt-free and the whole layer would be inert until somebody
            // borrowed.
            foreach (var country in state.countries)
            {
                Assert.Greater(country.fiscal.sovereignDebt, 0f,
                    $"{country.id} was created with no debt at all.");
                Assert.AreEqual(country.economy.debtToGdp,
                    FiscalSystem.DebtToGdp(country), 0.5f,
                    $"{country.id}: the stock and the ratio disagree at month zero.");
            }
        }

        // ---------- borrowing is not free capacity ----------

        [Test]
        public void BorrowingRaisesMoneyAndCostsStandingImmediately()
        {
            float treasury = Player.resources.treasury;
            float credit = Player.fiscal.creditStanding;
            float debt = Player.fiscal.sovereignDebt;

            Assert.IsTrue(FiscalSystem.IssueSovereignDebtBy(state, state.playerCountryId));

            Assert.Greater(Player.resources.treasury, treasury, "The issue raised nothing.");
            Assert.Greater(Player.fiscal.sovereignDebt, debt, "Money appeared without debt behind it.");
            Assert.Less(Player.fiscal.creditStanding, credit,
                "Asking cost nothing, so an operator could raise four rounds in a year at the "
                + "price of the first.");
        }

        [Test]
        public void DebtIsServicedEveryMonthAndTheRateFollowsStanding()
        {
            Player.fiscal.sovereignDebt = 4000f;

            Player.fiscal.creditStanding = 95f;
            float cheap = FiscalSystem.MonthlyDebtService(Player);

            Player.fiscal.creditStanding = 10f;
            float dear = FiscalSystem.MonthlyDebtService(Player);

            Assert.Greater(cheap, 0f, "A debt of 4000 was serviced for nothing.");
            Assert.Greater(dear, cheap * 1.5f,
                "A distrusted government borrowed at almost the same rate as a sound one, so "
                + "credit standing is a readout rather than a price.");
        }

        [Test]
        public void NobodyLendsToAGovernmentNobodyTrusts()
        {
            Player.fiscal.creditStanding = 5f;
            Assert.IsFalse(FiscalSystem.CanIssueDebt(state, state.playerCountryId, out string reason));
            Assert.IsNotEmpty(reason, "A refusal the operator cannot see is a broken button.");
            Assert.IsFalse(FiscalSystem.IssueSovereignDebtBy(state, state.playerCountryId),
                "The gate and the verb disagreed — what is offered and what is accepted must be "
                + "the same function.");
        }

        [Test]
        public void DebtCannotBeStackedForever()
        {
            Player.fiscal.creditStanding = 100f;
            Player.fiscal.sovereignDebt = Player.economy.gdp * 3f;   // 300% of GDP

            Assert.IsFalse(FiscalSystem.CanIssueDebt(state, state.playerCountryId, out _),
                "A state at 300% of GDP was still able to raise more, so borrowing is a printer.");
        }

        // ---------- a deficit is felt ----------

        [Test]
        public void ADeficitBecomesDebtRatherThanANegativeNumberNobodyFeels()
        {
            // Before this layer, treasury simply went negative and *nothing
            // happened*: the playtest measured a passive belligerent several
            // thousand in the red over a decade with no consequence the
            // simulation could express.
            Player.resources.treasury = 0f;
            Player.fiscal.sovereignDebt = 0f;
            float debtBefore = Player.fiscal.sovereignDebt;

            // Something expensive that the treasury cannot cover.
            Player.resources.treasury = -800f;
            FiscalSystem.MonthlyUpdate(state);

            Assert.GreaterOrEqual(Player.resources.treasury, 0f,
                "The treasury stayed negative, so the deficit was never financed.");
            Assert.Greater(Player.fiscal.sovereignDebt, debtBefore,
                "Spending money the state did not have created no debt — the deficit vanished.");
        }

        [Test]
        public void ASurplusRetiresDebtSoTheStockIsNotOneWay()
        {
            // The rule this project has broken more than any other: a value that
            // recurring conditions decrement needs a reachable recovery path.
            Player.fiscal.sovereignDebt = 3000f;
            Player.resources.treasury = 40000f;

            float before = Player.fiscal.sovereignDebt;
            for (int month = 0; month < 24; month++) FiscalSystem.MonthlyUpdate(state);

            Assert.Less(Player.fiscal.sovereignDebt, before,
                "Two years of large surpluses paid down none of the debt, so the stock only ever "
                + "grows and every government eventually drowns.");
        }

        // ---------- the standing choices do something ----------

        [Test]
        public void AusterityAndExpansionPullOppositeWays()
        {
            Assert.Greater(FiscalSystem.PostureIncomeMultiplier(BudgetPosture.Austerity),
                FiscalSystem.PostureIncomeMultiplier(BudgetPosture.Expansionary),
                "Austerity did not improve the balance relative to spending through it.");
            Assert.Less(FiscalSystem.PostureGrowthShift(BudgetPosture.Austerity),
                FiscalSystem.PostureGrowthShift(BudgetPosture.Expansionary),
                "Austerity did not cost growth.");
            Assert.Less(FiscalSystem.PostureStandardsShift(BudgetPosture.Austerity),
                FiscalSystem.PostureStandardsShift(BudgetPosture.Expansionary),
                "Austerity cost the public nothing, so it is free solvency.");
        }

        [Test]
        public void RaisingTaxBuysRevenueAndCostsGrowthAndStanding()
        {
            float baseline = FiscalSystem.TaxMultiplier(Player);

            Assert.IsTrue(FiscalSystem.SetTaxRateBy(state, state.playerCountryId, 75f));

            Assert.Greater(FiscalSystem.TaxMultiplier(Player), baseline, "A tax rise raised nothing.");
            Assert.Greater(FiscalSystem.TaxGrowthDrag(Player), 0f, "A tax rise cost no growth.");
            Assert.Greater(FiscalSystem.TaxApprovalDrag(Player), 0f, "Nobody minded the tax rise.");

            Assert.IsTrue(FiscalSystem.SetTaxRateBy(state, state.playerCountryId, 10f));
            Assert.Less(FiscalSystem.TaxMultiplier(Player), baseline);
            Assert.Less(FiscalSystem.TaxGrowthDrag(Player), 0f,
                "Cutting tax below the baseline bought no growth, so the lever only ever hurts.");
        }

        // ---------- reserves ----------

        [Test]
        public void AReserveRaisesTheFloorPressureCanGrindUsTo()
        {
            float without = FiscalSystem.ReserveFloorBonus(Player, TradeFocus.Energy);
            Assert.AreEqual(0f, without, 0.0001f, "A country holding no reserve had a floor bonus.");

            Player.resources.treasury = 100000f;
            Assert.IsTrue(FiscalSystem.BuildReservesBy(state, state.playerCountryId, TradeFocus.Energy));

            Assert.Greater(FiscalSystem.ReserveFloorBonus(Player, TradeFocus.Energy), 0f,
                "Buying a reserve bought nothing.");
            Assert.AreEqual(0f, FiscalSystem.ReserveFloorBonus(Player, TradeFocus.Food), 0.0001f,
                "An energy reserve covered grain, so the three are one pool wearing three names.");
        }

        [Test]
        public void AReserveIsDrawnOnUnderPressureAndKeptOtherwise()
        {
            Player.resources.treasury = 100000f;
            FiscalSystem.BuildReservesBy(state, state.playerCountryId, TradeFocus.Energy);
            float quiet = Player.fiscal.energyReserve;

            for (int month = 0; month < 6; month++) FiscalSystem.MonthlyUpdate(state);
            Assert.AreEqual(quiet, Player.fiscal.energyReserve, 0.001f,
                "A reserve nobody needed was consumed anyway.");

            // Now put the country under real pressure.
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                EconomySystem.ImposeSanctionsBy(state, country.id, state.playerCountryId,
                    SanctionSeverity.Existential);
            }

            float underPressure = Player.fiscal.energyReserve;
            for (int month = 0; month < 6; month++) FiscalSystem.MonthlyUpdate(state);

            Assert.Less(Player.fiscal.energyReserve, underPressure,
                "A blockaded country never touched its own strategic reserve, so holding one is "
                + "free and spending it is impossible.");
        }

        // ---------- restructuring ----------

        [Test]
        public void RestructuringWorksAndIsRemembered()
        {
            Player.fiscal.sovereignDebt = 5000f;
            Player.fiscal.creditStanding = 60f;

            Assert.IsTrue(FiscalSystem.RestructureDebtBy(state, state.playerCountryId));

            Assert.Less(Player.fiscal.sovereignDebt, 5000f, "The write-down wrote nothing down.");
            Assert.Less(Player.fiscal.creditStanding, 60f, "Defaulting cost no standing.");
            Assert.Greater(Player.fiscal.restructuringMemoryMonths, 0,
                "The default was forgotten immediately, which makes it a free reset.");

            // And it must be forgiven eventually — a permanent mark would make
            // the instrument unusable rather than expensive.
            for (int month = 0; month < FiscalSystem.RestructuringMemoryMonths + 2; month++)
                FiscalSystem.MonthlyUpdate(state);

            Assert.IsFalse(Player.fiscal.HasRestructured,
                "Five years on, the restructuring was still being held against them forever.");
        }

        // ---------- symmetry ----------

        [Test]
        public void ForeignGovernmentsFundThemselvesTheSameWay()
        {
            // The most-repeated bug in this codebase is an AI state locked out of
            // a player verb. Every fiscal instrument is actor-generic; this
            // proves the AI actually *reaches* them, which is the second half
            // that routine restocking failed for thirty measured years.
            var other = state.countries.Find(c => !c.isPlayer);
            other.resources.treasury = 0f;
            other.fiscal.creditStanding = 80f;
            float debtBefore = other.fiscal.sovereignDebt;

            for (int month = 0; month < 60; month++) turns.EndMonth();

            Assert.Greater(other.fiscal.sovereignDebt, debtBefore,
                $"{other.id} ran its treasury dry for five years and never borrowed a penny. A "
                + "verb the AI can call but never will is the same bug as one it cannot call.");
        }

        [Test]
        public void ADecadeOfOrdinaryPlayDoesNotBuryTheWorldInDebt()
        {
            // The mirror of `DisplacementTests.ADecadeOfDoingNothingDoesNotEndInTheRed`,
            // which can no longer detect a leak now that deficits finance
            // themselves: the red shows up as debt instead, so that is where the
            // leak detector has to look.
            for (int month = 0; month < 120; month++) turns.EndMonth();

            foreach (var country in state.countries)
                Assert.Less(FiscalSystem.DebtToGdp(country), 220f,
                    $"{country.id} ended a quiet decade at {FiscalSystem.DebtToGdp(country):F0}% "
                    + "of GDP. Any new recurring cost must be sized against "
                    + "`gdp × TreasuryIncomeRate`.");
        }

        [Test]
        public void CreditStandingRespondsToTheDebtRatherThanBeingSetByHand()
        {
            Player.fiscal.sovereignDebt = Player.economy.gdp * 1.8f;
            float stretched = FiscalSystem.CreditTarget(state, Player);

            Player.fiscal.sovereignDebt = Player.economy.gdp * 0.2f;
            float sound = FiscalSystem.CreditTarget(state, Player);

            Assert.Greater(sound, stretched + 10f,
                "Carrying nine times as much debt made almost no difference to what lenders "
                + "think, so the ratio is decorative.");
        }
    }
}

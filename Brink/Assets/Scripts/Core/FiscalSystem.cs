using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Public finance (spec 02 §9, spec 25 Tranche A).
    ///
    /// Everything here is **actor-generic**: a government funds itself the same
    /// way whoever is standing behind it. The player wrappers on
    /// `GameController` spend the operator's resource and then delegate, which
    /// is the pattern that stops this becoming the seventh instance of an AI
    /// state locked out of a player verb.
    ///
    /// The design rule the whole layer hangs from: **borrowing must never be
    /// free capacity.** Debt is a stock that is serviced every month at a rate
    /// set by credit standing, credit standing is derived from the debt, and a
    /// deficit finances itself into more debt rather than into a negative number
    /// nobody feels. That loop is what makes the levers a decision instead of a
    /// button that says yes.
    /// </summary>
    public static class FiscalSystem
    {
        // ---- costs ----
        public const int SetTaxRateCost = 2;          // PC
        public const int SetBudgetPostureCost = 2;    // CP
        public const int IssueDebtCost = 1;           // CP
        public const int SubsidiseCost = 1;           // CP
        public const int ReservesCost = 1;            // CP
        public const int RestructureCost = 4;         // PC

        /// <summary>Treasury a single reserve order buys, and what it costs.</summary>
        public const float ReserveOrderPoints = 12f;
        public const float ReserveCostPerPoint = 26f;

        /// <summary>How much one order of debt raises, as a share of GDP.</summary>
        public const float IssueShareOfGdp = 0.12f;

        /// <summary>Below this, no lender will take the paper at any price.</summary>
        public const float MinimumCreditToIssue = 18f;

        /// <summary>Debt-to-GDP past which further issuance is refused outright.</summary>
        public const float IssueDebtCeiling = 200f;

        public const int RestructuringMemoryMonths = 60;

        // ---------- derived readings ----------

        /// <summary>
        /// Debt as a share of GDP. **Derived, never stored** — `sovereignDebt`
        /// is the authority and `EconomyState.debtToGdp` is kept in step with it
        /// each month so the ~6 existing readers keep working rather than being
        /// rewritten against a second source of truth.
        /// </summary>
        public static float DebtToGdp(CountryState country)
        {
            if (country == null || country.economy.gdp <= 0f) return 0f;
            return Clamp(country.fiscal.sovereignDebt / country.economy.gdp * 100f, 0f, 400f);
        }

        /// <summary>
        /// Revenue multiplier on `TreasuryIncomeRate`. 1.0 at the baseline rate,
        /// so a world nobody has touched raises exactly what it raised before
        /// this system existed — the measured balance table stays comparable.
        /// </summary>
        public static float TaxMultiplier(CountryState country)
            => country == null ? 1f : Math.Max(0.5f, 0.5f + country.fiscal.taxRate / 70f);

        /// <summary>
        /// What a posture does to the monthly balance. Austerity is not extra
        /// revenue in reality — it is spending forgone — but the simulation has
        /// no general outlay model, so the net effect on the treasury is the
        /// honest equivalent and is documented as such rather than pretended
        /// otherwise.
        /// </summary>
        public static float PostureIncomeMultiplier(BudgetPosture posture)
        {
            switch (posture)
            {
                case BudgetPosture.Austerity: return 1.14f;
                case BudgetPosture.Expansionary: return 0.84f;
                default: return 1f;
            }
        }

        /// <summary>Growth a posture buys or forgoes, in annualized points.</summary>
        public static float PostureGrowthShift(BudgetPosture posture)
        {
            switch (posture)
            {
                case BudgetPosture.Austerity: return -0.7f;
                case BudgetPosture.Expansionary: return 0.8f;
                default: return 0f;
            }
        }

        /// <summary>What a posture does to what people can afford.</summary>
        public static float PostureStandardsShift(BudgetPosture posture)
        {
            switch (posture)
            {
                case BudgetPosture.Austerity: return -5f;
                case BudgetPosture.Expansionary: return 4f;
                default: return 0f;
            }
        }

        /// <summary>
        /// Tax above the baseline suppresses growth and costs the government
        /// standing; below it does the reverse and starves the treasury. Zero by
        /// construction at `BaselineTaxRate`, so this term does nothing at all in
        /// a world where the operator has not touched it.
        /// </summary>
        public static float TaxGrowthDrag(CountryState country)
            => country == null ? 0f
                : (country.fiscal.taxRate - FiscalState.BaselineTaxRate) * 0.030f;

        public static float TaxApprovalDrag(CountryState country)
            => country == null ? 0f
                : (country.fiscal.taxRate - FiscalState.BaselineTaxRate) * 0.060f;

        /// <summary>
        /// Monthly interest on the stock. Cheap money for a sound government,
        /// punitive for one nobody trusts — which is what makes credit standing
        /// worth protecting rather than a number on a screen.
        /// </summary>
        /// <summary>
        /// Monthly interest on the stock. Cheap money for a sound government,
        /// punitive for one nobody trusts.
        ///
        /// **Calibrated to be revenue-neutral against what it replaced**, which
        /// is the rule the tax and posture multipliers already follow and which
        /// this missed on the first pass. Treasury income used to be haircut by
        /// `(1 − debtToGdp/400)`; servicing the stock explicitly is what lets
        /// credit standing price it, but the first rates charged roughly 40% of
        /// the old haircut, so **every government in the world quietly got
        /// richer** — measured as a colder world: AI-vs-AI wars fell below the
        /// floor `WorldHeatTests` guards, and wars ran longer because states
        /// could afford them.
        ///
        /// The neutral point: `service = income × d/400` at a typical standing,
        /// which with `TreasuryIncomeRate = 0.03` works out at ~0.0075/month
        /// (≈9%/yr) — high by modern sovereign standards and unremarkable for
        /// the era this game is set in. The spread either side is what makes the
        /// standing worth protecting: ≈5.4%/yr trusted, ≈14.4%/yr distrusted.
        /// </summary>
        public static float MonthlyInterestRate(CountryState country)
        {
            float credit = country == null ? 50f : country.fiscal.creditStanding;
            return 0.0045f + (100f - Clamp(credit, 0f, 100f)) / 100f * 0.0075f;
        }

        /// <summary>
        /// What the old income haircut would have cost this country this month.
        /// Kept as the calibration reference for `MonthlyInterestRate` — a
        /// constant that replaces an existing cost has to be checkable against
        /// it, or "revenue-neutral" is an assertion nobody can test.
        /// </summary>
        public static float LegacyDebtHaircut(CountryState country)
            => country == null ? 0f
                : country.economy.gdp * EconomySystem.TreasuryIncomeRate
                  * (DebtToGdp(country) / 400f);

        public static float MonthlyDebtService(CountryState country)
            => country == null ? 0f : country.fiscal.sovereignDebt * MonthlyInterestRate(country);

        /// <summary>
        /// Where credit standing settles. Driven by the debt, whether the
        /// economy is growing into it, whether anybody is lending into a war,
        /// and whether this government has written debt down before.
        /// </summary>
        public static float CreditTarget(GameState state, CountryState country)
        {
            var eco = country.economy;
            float target = 78f
                           - Math.Max(0f, DebtToGdp(country) - 55f) * 0.55f
                           + eco.growthRate * 2.2f
                           + (eco.confidence - 50f) * 0.16f
                           - (state != null && state.IsAtWar(country.id) ? 9f : 0f);

            // A restructuring is remembered for five years and then forgiven, on
            // the same reasoning as every other reputational memory here: a
            // permanent mark would make the instrument unusable rather than
            // expensive.
            if (country.fiscal.HasRestructured)
                target -= 34f * (country.fiscal.restructuringMemoryMonths
                                 / (float)RestructuringMemoryMonths);

            return Clamp(target, 0f, 100f);
        }

        /// <summary>
        /// How much of a resource's ceiling a reserve will hold up against
        /// pressure, 0..0.35. Read by `EconomySystem`'s resource drift, where
        /// sanctions and war push the *target* down — a reserve raises the floor
        /// they can push it to, and depletes while it is doing that work.
        /// </summary>
        public static float ReserveFloorBonus(CountryState country, TradeFocus resource)
        {
            if (country == null) return 0f;
            float reserve;
            switch (resource)
            {
                case TradeFocus.Energy: reserve = country.fiscal.energyReserve; break;
                case TradeFocus.Materials: reserve = country.fiscal.materialsReserve; break;
                case TradeFocus.Food: reserve = country.fiscal.foodReserve; break;
                default: return 0f;
            }
            return Clamp(reserve, 0f, 100f) / 100f * 0.35f;
        }

        /// <summary>Health a standing subsidy buys in one sector.</summary>
        public static float SubsidyHealthBonus(CountryState country, EconomicSector sector)
            => country == null ? 0f : country.fiscal.SubsidyFor(sector) * 0.22f;

        // ---------- the monthly tick ----------

        /// <summary>
        /// Runs for every country. Ordered after `EconomySystem` in
        /// `SimulationPipeline` so it services the debt against the income that
        /// month actually produced.
        /// </summary>
        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries) Tick(state, country);

            // Sampled here rather than in `EconomySystem` so it reads the
            // *financed* position: the deficit has become debt by this point,
            // and the readout measures the balance rather than the account.
            EconomySystem.TrackTreasuryTrend(state);
        }

        static void Tick(GameState state, CountryState country)
        {
            var fiscal = country.fiscal;

            // Debt service, before anything else can spend the money.
            country.resources.treasury -= MonthlyDebtService(country);

            // Subsidies are a standing bill and they fade without renewal, so a
            // sector propped up once does not stay propped up for a decade.
            for (int i = fiscal.subsidies.Count - 1; i >= 0; i--)
            {
                var subsidy = fiscal.subsidies[i];
                country.resources.treasury -= subsidy.level * 0.9f;
                subsidy.level *= 0.97f;
                if (subsidy.level < 1f) fiscal.subsidies.RemoveAt(i);
            }

            // Reserves are drawn on when the resource they cover is under real
            // pressure, and only then. A reserve nobody needs keeps.
            DepleteReserve(state, country, TradeFocus.Energy);
            DepleteReserve(state, country, TradeFocus.Materials);
            DepleteReserve(state, country, TradeFocus.Food);

            // **A deficit finances itself.** Governments do not stop paying the
            // army because the account is empty; they borrow, and the borrowing
            // is what the next decade argues about. Before this, treasury simply
            // went negative and *nothing happened* — a passive belligerent ended
            // a decade several thousand in the red with no consequence the
            // simulation could express, which is why the playtest's fiscal
            // finding could only be recorded as an open question.
            if (country.resources.treasury < 0f)
            {
                // Indirect on purpose: the deficit is the *mechanism*, but what
                // caused it is whatever this government spent the month doing.
                // Phase A records the hop; the spending decisions upstream of it
                // are Phase B's chain to complete (spec 26 §8).
                Causal.Apply(state, country.id, CausalMetric.SovereignDebt,
                    CausalReason.FiscalDeficit, ref fiscal.sovereignDebt,
                    fiscal.sovereignDebt + -country.resources.treasury,
                    CausalCategory.Fiscal, CausalKind.Indirect);
                country.resources.treasury = 0f;
            }

            // A surplus retires debt before it piles up, so a well-run country
            // climbs out on its own and the stock is not a one-way value.
            else if (fiscal.sovereignDebt > 0f && country.resources.treasury > SurplusBuffer)
            {
                float repayment = Math.Min(fiscal.sovereignDebt,
                    (country.resources.treasury - SurplusBuffer) * 0.10f);
                Causal.Apply(state, country.id, CausalMetric.SovereignDebt,
                    CausalReason.FiscalSurplus, ref fiscal.sovereignDebt,
                    fiscal.sovereignDebt - repayment, CausalCategory.Fiscal);
                country.resources.treasury -= repayment;
            }

            if (fiscal.restructuringMemoryMonths > 0) fiscal.restructuringMemoryMonths--;

            fiscal.creditStanding = Approach(fiscal.creditStanding, CreditTarget(state, country), 0.08f);

            // Keep the derived reading in step for the six sites that read it.
            country.economy.debtToGdp = DebtToGdp(country);
        }

        /// <summary>
        /// Treasury a government keeps back rather than throwing at the debt.
        /// Mirrors `AISystem.DiscretionaryReserve`'s lesson: a state that spends
        /// its last coin cannot fund anything with a lead time.
        /// </summary>
        public const float SurplusBuffer = 400f;

        static void DepleteReserve(GameState state, CountryState country, TradeFocus resource)
        {
            var fiscal = country.fiscal;
            float held;
            switch (resource)
            {
                case TradeFocus.Energy: held = fiscal.energyReserve; break;
                case TradeFocus.Materials: held = fiscal.materialsReserve; break;
                default: held = fiscal.foodReserve; break;
            }
            if (held <= 0f) return;

            bool pressured = state.IsAtWar(country.id)
                             || EconomySystem.SanctionPressureOn(state, country.id) > 1.0f;
            if (!pressured) return;

            float drawn = Math.Min(held, 0.6f);
            switch (resource)
            {
                case TradeFocus.Energy: fiscal.energyReserve -= drawn; break;
                case TradeFocus.Materials: fiscal.materialsReserve -= drawn; break;
                default: fiscal.foodReserve -= drawn; break;
            }
        }

        // ---------- verbs, actor-generic ----------

        public static bool SetTaxRateBy(GameState state, string actorId, float rate)
        {
            var country = state.FindCountry(actorId);
            if (country == null) return false;

            float clamped = Clamp(rate, 0f, 100f);
            if (Math.Abs(clamped - country.fiscal.taxRate) < 0.5f) return false;

            country.fiscal.taxRate = clamped;
            state.AddChronicle(ChronicleCategory.Economic, actorId,
                $"Tax set to {clamped:F0}%.");
            return true;
        }

        public static bool SetBudgetPostureBy(GameState state, string actorId, BudgetPosture posture)
        {
            var country = state.FindCountry(actorId);
            if (country == null || country.fiscal.budgetPosture == posture) return false;

            country.fiscal.budgetPosture = posture;
            state.AddChronicle(ChronicleCategory.Economic, actorId,
                $"Budget posture: {PostureText(posture)}.", Publicity.Public);
            return true;
        }

        /// <summary>
        /// Whether this government can raise money on its paper, and why not.
        /// One gate, shared by the order screen and the verb, so what is offered
        /// and what is accepted cannot differ (the `OperationCatalog.CanOrder`
        /// precedent).
        /// </summary>
        public static bool CanIssueDebt(GameState state, string actorId, out string reason)
        {
            reason = "";
            var country = state.FindCountry(actorId);
            if (country == null) { reason = "No such state."; return false; }

            if (country.fiscal.creditStanding < MinimumCreditToIssue)
            {
                reason = $"NO LENDER WILL TAKE OUR PAPER (credit {country.fiscal.creditStanding:F0}).";
                return false;
            }
            if (DebtToGdp(country) >= IssueDebtCeiling)
            {
                reason = $"DEBT IS {DebtToGdp(country):F0}% OF THE ECONOMY. Nobody is lending more.";
                return false;
            }
            return true;
        }

        public static bool IssueSovereignDebtBy(GameState state, string actorId)
        {
            if (!CanIssueDebt(state, actorId, out _)) return false;
            var country = state.FindCountry(actorId);

            float raised = country.economy.gdp * IssueShareOfGdp;
            country.resources.treasury += raised;
            Causal.Apply(state, country.id, CausalMetric.SovereignDebt,
                CausalReason.BondIssue, ref country.fiscal.sovereignDebt,
                country.fiscal.sovereignDebt + raised, CausalCategory.Fiscal,
                CausalKind.Direct, CausalVisibility.Known, null,
                nameof(GameController.IssueSovereignDebt));

            // Issuing is itself a signal. The standing falls now rather than
            // only through next month's ratio, so a government cannot raise four
            // rounds in one year at the price of the first.
            country.fiscal.creditStanding = Clamp(country.fiscal.creditStanding - 6f, 0f, 100f);
            country.economy.debtToGdp = DebtToGdp(country);

            state.AddChronicle(ChronicleCategory.Economic, actorId,
                $"Sovereign issue raises {raised:F0}.", Publicity.Public);
            return true;
        }

        public static bool SubsidiseSectorBy(GameState state, string actorId, EconomicSector sector)
        {
            var country = state.FindCountry(actorId);
            if (country == null) return false;
            if (country.resources.treasury < SubsidyTreasury) return false;

            country.resources.treasury -= SubsidyTreasury;
            country.fiscal.AddSubsidy(sector, 22f);
            return true;
        }

        public const float SubsidyTreasury = 220f;

        public static bool BuildReservesBy(GameState state, string actorId, TradeFocus resource)
        {
            var country = state.FindCountry(actorId);
            if (country == null || resource == TradeFocus.General) return false;

            float cost = ReserveOrderPoints * ReserveCostPerPoint;
            if (country.resources.treasury < cost) return false;

            country.resources.treasury -= cost;
            switch (resource)
            {
                case TradeFocus.Energy:
                    country.fiscal.energyReserve =
                        Clamp(country.fiscal.energyReserve + ReserveOrderPoints, 0f, 100f); break;
                case TradeFocus.Materials:
                    country.fiscal.materialsReserve =
                        Clamp(country.fiscal.materialsReserve + ReserveOrderPoints, 0f, 100f); break;
                default:
                    country.fiscal.foodReserve =
                        Clamp(country.fiscal.foodReserve + ReserveOrderPoints, 0f, 100f); break;
            }
            return true;
        }

        /// <summary>
        /// Spend the buffer now rather than holding it. Converts a reserve
        /// directly into the resource, which is the emergency use — the monthly
        /// depletion above is the passive one.
        /// </summary>
        public static bool ReleaseReservesBy(GameState state, string actorId, TradeFocus resource)
        {
            var country = state.FindCountry(actorId);
            if (country == null) return false;

            float held;
            switch (resource)
            {
                case TradeFocus.Energy: held = country.fiscal.energyReserve; break;
                case TradeFocus.Materials: held = country.fiscal.materialsReserve; break;
                case TradeFocus.Food: held = country.fiscal.foodReserve; break;
                default: return false;
            }
            if (held < 5f) return false;

            float released = held * 0.5f;
            switch (resource)
            {
                case TradeFocus.Energy:
                    country.fiscal.energyReserve -= released;
                    country.resources.energy = Clamp(country.resources.energy + released * 0.5f, 0f, 100f);
                    break;
                case TradeFocus.Materials:
                    country.fiscal.materialsReserve -= released;
                    country.resources.strategicMaterials =
                        Clamp(country.resources.strategicMaterials + released * 0.5f, 0f, 100f);
                    break;
                default:
                    country.fiscal.foodReserve -= released;
                    country.resources.foodSecurity =
                        Clamp(country.resources.foodSecurity + released * 0.5f, 0f, 100f);
                    break;
            }
            return true;
        }

        /// <summary>
        /// Write the debt down. Effective, and remembered for five years by
        /// everyone who has to price this government's paper — plus the trading
        /// partners who were holding it.
        /// </summary>
        public static bool RestructureDebtBy(GameState state, string actorId)
        {
            var country = state.FindCountry(actorId);
            if (country == null || country.fiscal.sovereignDebt <= 0f) return false;

            // SovereignDebt is a pilot metric, so the write-down is recorded
            // where it is applied — an uninstrumented halving left a restructure
            // month's debt explanation silently short by half the stock (spec 26
            // §3). `Apply` assigns the caller's expression untouched, for the
            // player and the AI alike; recording itself is player-only as ever.
            Causal.Apply(state, actorId, CausalMetric.SovereignDebt,
                CausalReason.DebtRestructured, ref country.fiscal.sovereignDebt,
                country.fiscal.sovereignDebt * 0.5f, CausalCategory.Fiscal,
                sourceActionId: nameof(GameController.RestructureDebt));
            country.fiscal.creditStanding = Clamp(country.fiscal.creditStanding - 30f, 0f, 100f);
            country.fiscal.restructuringMemoryMonths = RestructuringMemoryMonths;
            country.economy.confidence = Clamp(country.economy.confidence - 14f, 0f, 100f);
            country.economy.debtToGdp = DebtToGdp(country);

            // Somebody was holding that paper. Standing, not capability — the
            // exposure-penalty lesson (spec 03): a cost with no recovery path in
            // the pillar it charges is a disqualification rather than a price.
            foreach (var link in state.trade)
            {
                if (!link.Involves(actorId)) continue;
                string other = link.countryA == actorId ? link.countryB : link.countryA;
                var relationship = state.FindRelationship(actorId, other);
                if (relationship == null) continue;
                relationship.relations = Clamp(relationship.relations - 8f, 0f, 100f);
                relationship.trust = Clamp(relationship.trust - 11f, 0f, 100f);
            }

            state.AddChronicle(ChronicleCategory.Economic, actorId,
                $"{country.displayName} restructures its debt.", Publicity.Public);
            return true;
        }

        public static string PostureText(BudgetPosture posture)
        {
            switch (posture)
            {
                case BudgetPosture.Austerity: return "AUSTERITY";
                case BudgetPosture.Expansionary: return "EXPANSIONARY";
                default: return "BALANCED";
            }
        }

        public static string CreditText(float standing)
        {
            if (standing >= 80f) return "UNQUESTIONED";
            if (standing >= 62f) return "SOUND";
            if (standing >= 44f) return "WATCHED";
            if (standing >= 26f) return "STRAINED";
            return "DISTRESSED";
        }

        static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        static float Approach(float current, float target, float rate)
            => current + (target - current) * rate;
    }
}

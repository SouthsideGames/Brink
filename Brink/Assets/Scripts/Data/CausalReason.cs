namespace Brink.Data
{
    /// <summary>
    /// The stable identity of a cause (spec 26 §2).
    ///
    /// **Append-only. Never reorder, never delete.** These ordinals are written
    /// into saves inside `CausalContribution.reason`, so moving one relabels
    /// every explanation already recorded — a save would start saying approval
    /// fell because of hunger when it actually fell because of a tax rise.
    ///
    /// An enum rather than a string because the reason is the one field written
    /// on every contribution of every metric of every recorded month, and
    /// because §15 of this phase's brief is explicit that stable ids belong in
    /// simulation state and prose does not. `CausalReasons.Label` is the only
    /// place the wording lives, so retitling a cause is a one-line change that
    /// cannot desynchronise from history.
    /// </summary>
    public enum CausalReason
    {
        Unspecified = 0,

        // ---- shared economic drivers ----
        EconomicGrowth,
        Inflation,
        Unemployment,
        MarketConditions,
        MarketConfidence,

        // ---- social ----
        Deprivation,
        CostOfLiving,
        Hunger,
        PublicMemory,
        OrganisedUnrest,
        OppositionCampaign,
        DisplacementHosting,
        DisplacementAtSource,

        // ---- political ----
        LivingStandardsLevel,
        PublicMessaging,
        ConstitutionalForm,
        CivicPosture,
        TaxBurden,
        BudgetPosture,

        // ---- military ----
        ActiveFighting,
        MilitaryOperations,
        Occupation,
        Insurgency,
        WarExhaustionLevel,
        Victory,
        PeacetimeRecovery,
        SettlementRelief,

        // ---- diplomatic / external ----
        SanctionPressure,
        SanctionBlowback,
        AtWar,

        // ---- fiscal ----
        DebtService,
        FiscalDeficit,
        TaxRevenue,
        GovernmentSpending,
        FiscalSurplus,
        BondIssue,

        // ---- national character and damping ----
        NationalUnity,
        NationalTemperament,

        // ---- structural bookkeeping, not world events ----
        /// <summary>
        /// The pull of the value's own current level toward its target. Not a
        /// world event and never presented as one — it is what makes a
        /// decomposition of an `Approach` tick add up, and the renderer folds it
        /// away unless it dominates.
        /// </summary>
        Reversion,

        /// <summary>The value hit 0 or 100 and the rest of the change was lost.</summary>
        Bounds,

        /// <summary>
        /// Real, measured, and not attributable to anything instrumented. Shown
        /// as OTHER. A decomposition that silently fails to add up is worse than
        /// one that admits what it cannot explain.
        /// </summary>
        Unattributed,

        // ---- covert and foreign action ----
        /// <summary>
        /// Somebody acted against us and we do not know who. Carries no
        /// `sourceCountryId` unless intelligence has actually named one.
        /// </summary>
        ForeignInterference,
        CovertAction,

        // ---- appended 2026-09 (Phase A closure). Append-only: new members go
        // ---- below this line, never between existing ones. ----

        /// <summary>A crisis the operator let lapse unanswered (GDD §23).</summary>
        CrisisLapsed,

        /// <summary>Sovereign debt written down by decision (spec 02 §9).</summary>
        DebtRestructured,

        /// <summary>An answered Crisis Turn changed a tracked domestic outcome.</summary>
        CrisisDecision,

        // ---- appended 2026-09 (episodic settlement attribution). ----
        Reparations,
        PoliticalConcessions,
        PrisonerExchange,
        PeaceSettlement,
    }

    /// <summary>
    /// Player-facing wording for a cause, and nothing else.
    ///
    /// Separate from the enum so that history recorded under an old label reads
    /// with the new one, and so the simulation never carries presentation text.
    /// Labels are terminal-style: short, uppercase, no sentence punctuation —
    /// they are column headings, not prose, and the readability rule reserves
    /// uppercase for exactly that.
    /// </summary>
    public static class CausalReasons
    {
        public static string Label(CausalReason reason)
        {
            switch (reason)
            {
                case CausalReason.EconomicGrowth: return "ECONOMIC GROWTH";
                case CausalReason.Inflation: return "INFLATION";
                case CausalReason.Unemployment: return "UNEMPLOYMENT";
                case CausalReason.MarketConditions: return "MARKET CONDITIONS";
                case CausalReason.MarketConfidence: return "INVESTOR CONFIDENCE";

                case CausalReason.Deprivation: return "DEPRIVATION";
                case CausalReason.CostOfLiving: return "COST OF LIVING";
                case CausalReason.Hunger: return "FOOD SHORTAGE";
                case CausalReason.PublicMemory: return "PUBLIC GRIEVANCE";
                case CausalReason.OrganisedUnrest: return "SOCIAL UNREST";
                case CausalReason.OppositionCampaign: return "OPPOSITION CAMPAIGN";
                case CausalReason.DisplacementHosting: return "REFUGEE BURDEN";
                case CausalReason.DisplacementAtSource: return "PEOPLE UNABLE TO LEAVE";

                case CausalReason.LivingStandardsLevel: return "LIVING STANDARDS";
                case CausalReason.PublicMessaging: return "PUBLIC MESSAGING";
                case CausalReason.ConstitutionalForm: return "CONSTITUTIONAL FORM";
                case CausalReason.CivicPosture: return "CIVIC POSTURE";
                case CausalReason.TaxBurden: return "TAX BURDEN";
                case CausalReason.BudgetPosture: return "BUDGET POSTURE";

                case CausalReason.ActiveFighting: return "ACTIVE FIGHTING";
                case CausalReason.MilitaryOperations: return "OPERATIONS";
                case CausalReason.Occupation: return "OCCUPATION DUTY";
                case CausalReason.Insurgency: return "ARMED INSURGENCY";
                case CausalReason.WarExhaustionLevel: return "WAR EXHAUSTION";
                case CausalReason.Victory: return "VICTORY";
                case CausalReason.PeacetimeRecovery: return "PEACETIME RECOVERY";
                case CausalReason.SettlementRelief: return "SETTLEMENT";

                case CausalReason.SanctionPressure: return "SANCTIONS ON US";
                case CausalReason.SanctionBlowback: return "OUR OWN SANCTIONS";
                case CausalReason.AtWar: return "STATE OF WAR";

                case CausalReason.DebtService: return "DEBT SERVICE";
                case CausalReason.FiscalDeficit: return "BUDGET DEFICIT";
                case CausalReason.TaxRevenue: return "TAX REVENUE";
                case CausalReason.GovernmentSpending: return "SPENDING";
                case CausalReason.FiscalSurplus: return "BUDGET SURPLUS";
                case CausalReason.BondIssue: return "BORROWING";

                case CausalReason.NationalUnity: return "NATIONAL UNITY";
                case CausalReason.NationalTemperament: return "NATIONAL TEMPERAMENT";

                case CausalReason.Reversion: return "SETTLING TOWARD LEVEL";
                case CausalReason.Bounds: return "AT THE LIMIT";
                case CausalReason.Unattributed: return "OTHER";

                case CausalReason.ForeignInterference: return "FOREIGN INTERFERENCE";
                case CausalReason.CovertAction: return "COVERT ACTION";

                case CausalReason.CrisisLapsed: return "CRISIS LEFT UNANSWERED";
                case CausalReason.DebtRestructured: return "DEBT RESTRUCTURED";
                case CausalReason.CrisisDecision: return "CRISIS DECISION";
                case CausalReason.Reparations: return "REPARATIONS";
                case CausalReason.PoliticalConcessions: return "POLITICAL CONCESSIONS";
                case CausalReason.PrisonerExchange: return "PRISONER EXCHANGE";
                case CausalReason.PeaceSettlement: return "PEACE SETTLEMENT";

                default: return "UNSPECIFIED";
            }
        }

        /// <summary>
        /// The heading a metric is explained under. Kept beside the reason
        /// labels because they are read together and drift apart if separated.
        /// </summary>
        public static string MetricLabel(CausalMetric metric)
        {
            switch (metric)
            {
                case CausalMetric.GovernmentApproval: return "APPROVAL";
                case CausalMetric.SocialUnrest: return "SOCIAL UNREST";
                case CausalMetric.LivingStandards: return "LIVING STANDARDS";
                case CausalMetric.PublicGrievance: return "PUBLIC GRIEVANCE";
                case CausalMetric.MarketIndex: return "MARKET INDEX";
                case CausalMetric.Treasury: return "TREASURY";
                case CausalMetric.SovereignDebt: return "SOVEREIGN DEBT";
                case CausalMetric.WarExhaustion: return "WAR EXHAUSTION";
                default: return "VALUE";
            }
        }

        /// <summary>
        /// A compact name for a button in a command row. The full metric label
        /// is a heading and is too wide to put five of on a 49-column phone,
        /// which is the width the whole rail has to survive.
        /// </summary>
        public static string ShortLabel(CausalMetric metric)
        {
            switch (metric)
            {
                case CausalMetric.GovernmentApproval: return "APPROVAL";
                case CausalMetric.SocialUnrest: return "UNREST";
                case CausalMetric.LivingStandards: return "STANDARDS";
                case CausalMetric.PublicGrievance: return "GRIEVANCE";
                case CausalMetric.MarketIndex: return "MARKET";
                case CausalMetric.Treasury: return "TREASURY";
                case CausalMetric.SovereignDebt: return "DEBT";
                case CausalMetric.WarExhaustion: return "EXHAUSTION";
                default: return "VALUE";
            }
        }

        /// <summary>
        /// Structural entries are bookkeeping rather than things that happened
        /// in the world. The renderer suppresses them unless they carry the
        /// change, so an operator is not told that approval fell because of
        /// "settling toward level".
        /// </summary>
        public static bool IsStructural(CausalReason reason) =>
            reason == CausalReason.Reversion
            || reason == CausalReason.Bounds
            || reason == CausalReason.Unattributed;
    }
}

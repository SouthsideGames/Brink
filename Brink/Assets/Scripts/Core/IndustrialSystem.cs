using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Investment in the economy's own capacity (GDD §20 amendment).
    ///
    /// **The pillar that earns the money could not spend a penny of it.** A verb
    /// audit found zero treasury spends anywhere in `EconomySystem`, `TradeSystem`
    /// or `DiplomacySystem`, against six in the military. Every economy control
    /// was a one-shot or a toggle — trade agreements are once per partner,
    /// sanctions are a standing regime, tariffs are a binary flip — so after
    /// roughly year two of a save the ECONOMY screen had **nothing left to press**
    /// while the treasury climbed every month.
    ///
    /// That is the mechanical reason for "I'm just clicking next month."
    ///
    /// The seven sectors were already there and already load-bearing: each has
    /// independent health and output, Energy tracks the energy stock, Defense
    /// surges at war, Finance decays past 90% debt-to-GDP, and
    /// `industrialCapacity` feeds growth, acquisition throughput *and* research.
    /// All of it was a readout. This gives it a verb.
    ///
    /// Deliberately shaped like `AcquisitionSystem` rather than like a stat boost:
    ///
    /// - **It takes years.** Money now, capacity later, so the decision is about
    ///   what the country will need rather than what it needs today.
    /// - **It is a commitment.** Programmes run to completion and can be
    ///   cancelled at a loss, which is what makes starting one a real choice.
    /// - **It is bounded.** Three at once, so investing everywhere is not an
    ///   option and the operator has to say what this country is *for*.
    /// - **Actor-generic.** `BeginBy` takes an actorId, because a verb the world
    ///   cannot use is this codebase's most-repeated bug.
    /// </summary>
    public static class IndustrialSystem
    {
        /// <summary>Programmes one state may run at once.</summary>
        public const int MaxProgrammes = 3;

        /// <summary>Command capacity to start one. Player only — see `Begin`.</summary>
        public const int CpCost = 2;

        /// <summary>
        /// Treasury cost per month of a programme, by scale. Charged monthly
        /// rather than up front so a programme is a standing commitment the
        /// operator keeps choosing to honour, and so losing an economy mid-build
        /// genuinely threatens what is being built.
        /// </summary>
        public static float MonthlyCostFor(IndustrialScale scale)
        {
            switch (scale)
            {
                case IndustrialScale.Modernisation: return 340f;
                case IndustrialScale.Expansion: return 190f;
                default: return 95f;
            }
        }

        /// <summary>How long it runs, in months.</summary>
        public static int MonthsFor(IndustrialScale scale)
        {
            switch (scale)
            {
                case IndustrialScale.Modernisation: return 36;
                case IndustrialScale.Expansion: return 24;
                default: return 12;
            }
        }

        /// <summary>What one month of it is worth to the sector, once it completes.</summary>
        static float YieldFor(IndustrialScale scale)
        {
            switch (scale)
            {
                case IndustrialScale.Modernisation: return 26f;
                case IndustrialScale.Expansion: return 15f;
                default: return 7f;
            }
        }

        /// <summary>Plain description, for the order screen.</summary>
        public static string Describe(IndustrialScale scale)
        {
            switch (scale)
            {
                case IndustrialScale.Modernisation:
                    return "Rebuild it to a modern standard. Three years and expensive, "
                         + "and it changes what the sector is capable of.";
                case IndustrialScale.Expansion:
                    return "Build more of what already works. Two years at moderate cost.";
                default:
                    return "Repair and maintain. A year, cheap, and it holds the line "
                         + "rather than moving it.";
            }
        }

        // ---------- ordering ----------

        public static bool CanBegin(GameState state, string actorId, out string reason)
        {
            var country = state.FindCountry(actorId);
            if (country == null) { reason = "No such state."; return false; }

            if (country.economy.programmes.Count >= MaxProgrammes)
            {
                reason = $"Already running {MaxProgrammes} programmes. "
                       + "The ministry cannot supervise another.";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>Actor-generic. Does not spend Command Points — see `Begin`.</summary>
        public static bool BeginBy(GameState state, string actorId,
            EconomicSector sector, IndustrialScale scale)
        {
            if (!CanBegin(state, actorId, out _)) return false;

            var country = state.FindCountry(actorId);

            // One programme per sector at a time. Two overlapping builds in the
            // same place would be the operator paying twice for one thing.
            foreach (var existing in country.economy.programmes)
                if (existing.sector == sector) return false;

            country.economy.programmes.Add(new IndustrialProgramme
            {
                sector = sector,
                scale = scale,
                monthsRemaining = MonthsFor(scale),
                started = state.date
            });

            if (country.isPlayer)
                state.AddNotification(NotificationClass.Advisory, "PROGRAMME BEGUN",
                    $"{Phrase.Of(sector)} — {scale.ToString().ToUpperInvariant()}. "
                    + $"{MonthsFor(scale)} months at {MonthlyCostFor(scale):F0} a month.",
                    actorId, desk: ReportingDesk.Economy);

            return true;
        }

        /// <summary>Player order: spends CP and records the initiative.</summary>
        public static bool Begin(GameState state, TurnManager turns,
            EconomicSector sector, IndustrialScale scale)
        {
            if (!AuthoritySystem.EnsureAuthority(state, Pillar.Economy)) return false;
            if (!CanBegin(state, state.playerCountryId, out string reason))
            {
                GameLog.Warn("ECON", reason);
                return false;
            }

            if (!turns.SpendCommandPoints(CpCost, $"Industrial programme: {sector}")) return false;
            if (!BeginBy(state, state.playerCountryId, sector, scale)) return false;

            ProgressionSystem.AwardXP(state, 14, "Industrial programme begun");
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        /// <summary>
        /// Stop a programme early. The money already spent is gone — which is
        /// what makes committing to one a decision rather than a reservation.
        /// </summary>
        public static bool Cancel(GameState state, string actorId, EconomicSector sector)
        {
            var country = state.FindCountry(actorId);
            if (country == null) return false;

            for (int i = country.economy.programmes.Count - 1; i >= 0; i--)
            {
                if (country.economy.programmes[i].sector != sector) continue;
                country.economy.programmes.RemoveAt(i);

                if (country.isPlayer)
                    state.AddNotification(NotificationClass.Advisory, "PROGRAMME CANCELLED",
                        $"{Phrase.Of(sector)} work stops. What has been spent is spent.",
                        actorId, desk: ReportingDesk.Economy);
                return true;
            }
            return false;
        }

        // ---------- the monthly tick ----------

        public static void MonthlyUpdate(GameState state)
        {
            foreach (var country in state.countries)
            {
                var eco = country.economy;

                for (int i = eco.programmes.Count - 1; i >= 0; i--)
                {
                    var programme = eco.programmes[i];
                    float cost = MonthlyCostFor(programme.scale);

                    // **A programme you cannot pay for stops.** Without this the
                    // treasury would go negative and the build would continue,
                    // which would make the cost decorative — the same trap that
                    // made war footing meaningless until it could lapse.
                    if (country.resources.treasury < cost)
                    {
                        eco.programmes.RemoveAt(i);
                        if (country.isPlayer)
                            state.AddNotification(NotificationClass.Priority, "PROGRAMME LAPSED",
                                $"{Phrase.Of(programme.sector)} work has stopped: the treasury "
                                + "cannot carry it. What was spent is spent.",
                                country.id, desk: ReportingDesk.Economy);
                        continue;
                    }

                    country.resources.treasury -= cost;
                    programme.monthsRemaining--;

                    if (programme.monthsRemaining > 0) continue;

                    Complete(state, country, programme);
                    eco.programmes.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// What a finished programme leaves behind.
        ///
        /// Output and health both move, because building a thing and keeping it
        /// running are different — and `industrialCapacity` only rises for the
        /// sectors that actually make things, so investing in Finance does not
        /// quietly buy tanks.
        /// </summary>
        static void Complete(GameState state, CountryState country, IndustrialProgramme programme)
        {
            float yield = YieldFor(programme.scale);
            var sector = country.economy.Sector(programme.sector);

            if (sector != null)
            {
                sector.output = Clamp(sector.output + yield);
                sector.health = Clamp(sector.health + yield * 0.5f);
            }

            // The two sectors that are physically productive feed the country's
            // ability to build. Technology feeds it less directly and Finance not
            // at all — a bigger bank does not make more aircraft.
            // Through `BuildIndustry`, so what was built also raises what the
            // country can hold — otherwise the monthly drift toward the
            // endowment would take the programme's yield straight back.
            if (programme.sector == EconomicSector.Industry)
                EconomySystem.BuildIndustry(country, yield * 0.45f);
            else if (programme.sector == EconomicSector.Technology)
                EconomySystem.BuildIndustry(country, yield * 0.20f);

            // Energy work raises what the country can hold, which is the one
            // authored vulnerability several states are built around.
            if (programme.sector == EconomicSector.Energy)
                country.resources.energyEndowment =
                    Clamp(country.resources.energyEndowment + yield * 0.35f);

            country.pillars.economy = Growth.Apply(country.pillars.economy, yield * 0.16f);

            state.AddChronicle(ChronicleCategory.Economic, country.id,
                $"{country.displayName} completes {programme.scale.ToString().ToLowerInvariant()} "
                + $"of its {Phrase.Of(programme.sector).ToLowerInvariant()} sector.", Publicity.Public);

            if (country.isPlayer)
            {
                state.AddNotification(NotificationClass.Priority, "PROGRAMME COMPLETE",
                    $"{Phrase.Of(programme.sector)} — {programme.scale.ToString().ToUpperInvariant()} "
                    + "finished. The capacity is there now, and so is the upkeep it implies.",
                    country.id, desk: ReportingDesk.Economy);
                ProgressionSystem.AwardXP(state, 30, "Industrial programme completed");
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

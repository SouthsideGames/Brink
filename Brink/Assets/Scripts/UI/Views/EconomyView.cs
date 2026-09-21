using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Economic command (GDD Phase 5, §20): macro readout, sector layer, the
    /// ASCII National Market Index, trade dependencies and economic warfare.
    /// </summary>
    public class EconomyView : TerminalView
    {
        public override string Id => "ECONOMY";
        public override string ShortCode => "ECO";

        /// <summary>Orders here are Economy pillar orders — see TerminalView.GateOnAuthority.</summary>
        protected override Pillar? CommandPillar => Pillar.Economy;

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string selectedTargetId;

        // Trade negotiation selection — kept on the view, not the save: which
        // partner is highlighted is not part of the world.
        string tradePartnerId;
        TradeFocus tradeFocus = TradeFocus.Energy;
        SanctionSeverity selectedSeverity = SanctionSeverity.Pressure;
        IndustrialScale investmentScale = IndustrialScale.Expansion;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();
            AddAuthorityBadge(state, Pillar.Economy);
            AddPillarArt(state, Pillar.Economy);
            AddCabinetAdvice(state, Pillar.Economy);
            BuildMacro(state, player);
            BuildFiscal(state, player);
            BuildMarketIndex(state, player);
            BuildSectors(player);
            BuildTrade(state);
            BuildEconomicWarfare(state);
        }

        void BuildMacro(GameState state, CountryState player)
        {
            var eco = player.economy;
            float pressure = EconomySystem.SanctionPressureOn(state, player.id);
            float blowback = EconomySystem.SanctionBlowbackFor(state, player.id);

            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("MACROECONOMIC CONDITION", W));
            sb.AppendLine(" " + AsciiChart.Row("GDP", $"{eco.gdp:F0}", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("GROWTH (ANNUALIZED)", $"{eco.growthRate:+0.00;-0.00;0.00}%", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("INFLATION", $"{eco.inflation:F2}%", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("UNEMPLOYMENT", $"{eco.unemployment:F1}%", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("DEBT / GDP", $"{eco.debtToGdp:F1}%", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("TREASURY", $"{player.resources.treasury:F0}", W - 2));
            sb.AppendLine();
            sb.AppendLine("  " + AsciiChart.LabeledBar("CONFIDENCE", eco.confidence, 100, 12, 20));
            if (pressure > 0f) sb.AppendLine($"  UNDER SANCTION PRESSURE: {pressure:F2}");
            if (blowback > 0f) sb.AppendLine($"  SELF-INFLICTED BLOWBACK:  {blowback:F2}");
            if (eco.InRecession) sb.AppendLine("  ** ECONOMY IN CONTRACTION **");
            text.text = sb.ToString();

            AddWhyPanel(state, CausalMetric.MarketIndex, CausalMetric.SovereignDebt);
        }

        /// <summary>
        /// Public finance (spec 02 §9). The readouts come first and the orders
        /// after, because the whole point of the layer is that borrowing is a
        /// trade — an operator who cannot see the service bill and the standing
        /// is being asked to guess.
        /// </summary>
        void BuildFiscal(GameState state, CountryState player)
        {
            var fiscal = player.fiscal;

            // Text rows, not a figure: long values must wrap without losing information.
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("PUBLIC FINANCE", W));
            sb.AppendLine(" " + AsciiChart.Row("TAX RATE", $"{fiscal.taxRate:F0}%", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("BUDGET POSTURE",
                FiscalSystem.PostureText(fiscal.budgetPosture), W - 2));
            sb.AppendLine(" " + AsciiChart.Row("SOVEREIGN DEBT",
                $"{fiscal.sovereignDebt:F0} ({FiscalSystem.DebtToGdp(player):F0}% GDP)", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("DEBT SERVICE",
                $"{FiscalSystem.MonthlyDebtService(player):F0}/MO", W - 2));
            sb.AppendLine(" " + AsciiChart.Row("CREDIT STANDING",
                $"{FiscalSystem.CreditText(fiscal.creditStanding)} ({fiscal.creditStanding:F0})", W - 2));

            if (fiscal.energyReserve > 0f || fiscal.materialsReserve > 0f || fiscal.foodReserve > 0f)
                sb.AppendLine(" " + AsciiChart.Row("RESERVES (ENR/MAT/FOOD)",
                    $"{fiscal.energyReserve:F0} / {fiscal.materialsReserve:F0} / {fiscal.foodReserve:F0}",
                    W - 2));

            if (fiscal.HasRestructured)
                sb.AppendLine($"  ** DEBT RESTRUCTURED — remembered for "
                              + $"{fiscal.restructuringMemoryMonths} more months **");

            text.text = sb.ToString();

            // ---- orders ----
            var postures = MakeRow();
            foreach (BudgetPosture posture in System.Enum.GetValues(typeof(BudgetPosture)))
            {
                var captured = posture;
                var button = AddButton(postures,
                    FiscalSystem.PostureText(posture) + $" [{FiscalSystem.SetBudgetPostureCost} CP]",
                    null, () => { GameController.Instance.SetBudgetPosture(captured); Refresh(); });
                if (fiscal.budgetPosture == posture)
                    Block(button, "ALREADY THE STANDING POSTURE");
            }

            var taxes = MakeRow();
            foreach (float step in new[] { -10f, -5f, 5f, 10f })
            {
                float target = fiscal.taxRate + step;
                var button = AddButton(taxes,
                    $"TAX {step:+0;-0}% [{FiscalSystem.SetTaxRateCost} PC]", null,
                    () => { GameController.Instance.SetTaxRate(target); Refresh(); });
                if (target < 0f || target > 100f) Block(button, "TAX RATE IS 0–100%");
            }

            var money = MakeRow();
            var issue = AddButton(money, $"ISSUE DEBT [{FiscalSystem.IssueDebtCost} CP]", null,
                () => { GameController.Instance.IssueSovereignDebt(); Refresh(); });
            if (!FiscalSystem.CanIssueDebt(state, player.id, out string debtBlock))
                Block(issue, debtBlock);

            var restructure = AddButton(money, $"RESTRUCTURE [{FiscalSystem.RestructureCost} PC]", null,
                () => { GameController.Instance.RestructureDebt(); Refresh(); });
            if (fiscal.sovereignDebt <= 0f) Block(restructure, "WE CARRY NO DEBT");

            var reserves = MakeRow();
            foreach (var resource in new[] { TradeFocus.Energy, TradeFocus.Materials, TradeFocus.Food })
            {
                var captured = resource;
                float cost = FiscalSystem.ReserveOrderPoints * FiscalSystem.ReserveCostPerPoint;
                var button = AddButton(reserves,
                    $"STOCKPILE {resource.ToString().ToUpperInvariant()} [{FiscalSystem.ReservesCost} CP]",
                    null, () => { GameController.Instance.BuildReserves(captured); Refresh(); });
                if (player.resources.treasury < cost)
                    Block(button, $"NEEDS {cost:F0} TREASURY");
            }

            ExplainBlockedCommands(postures);
            ExplainBlockedCommands(taxes);
            ExplainBlockedCommands(money);
            ExplainBlockedCommands(reserves);
        }

        void BuildMarketIndex(GameState state, CountryState player)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("NATIONAL MARKET INDEX", W);

            var history = player.economy.marketHistory;
            if (history.Count >= 2)
            {
                // Its own figure label: a chart is contiguous ASCII art, so it
                // must keep its rows touching and be built to the measured width
                // rather than a hardcoded 48 columns.
                AddFigure().text = AsciiChart.LineChart(
                    history.ToArray(), System.Math.Max(24, W - 6), 8);
            }

            var text = AddText();
            var sb = new StringBuilder();

            if (history.Count >= 2)
            {
                float latest = history[history.Count - 1];
                float prior = history[history.Count - 2];
                float change = (latest - prior) / prior * 100f;
                sb.AppendLine($"  INDEX {latest:F1}   MONTH CHANGE {change:+0.00;-0.00;0.00}%");
            }
            else
            {
                sb.AppendLine("  INSUFFICIENT HISTORY. END A MONTH TO BEGIN CHARTING.");
            }

            sb.AppendLine("\n  COMPARATIVE INDEXES");
            foreach (var country in state.countries)
            {
                string marker = country.isPlayer ? "►" : " ";
                sb.AppendLine($"  {marker}{AsciiChart.Cell(country.displayName.ToUpperInvariant(), AsciiChart.NameWidth(W))} {country.economy.marketIndex,8:F1}   " +
                              AsciiChart.Sparkline(country.economy.marketHistory.ToArray(), MaxOf(country.economy.marketHistory)));
            }
            text.text = sb.ToString();
        }

        static float MaxOf(System.Collections.Generic.List<float> values)
        {
            float max = 1f;
            foreach (float v in values) if (v > max) max = v;
            return max;
        }

        void BuildSectors(CountryState player)
        {
            var text = AddText();
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("SECTOR LAYER", W));
            foreach (var sector in player.economy.sectors)
            {
                string building = "";
                foreach (var programme in player.economy.programmes)
                    if (programme.sector == sector.sector)
                        building = $"  ({programme.scale.ToString().ToUpperInvariant()}, "
                                 + $"{programme.monthsRemaining} MO)";

                sb.AppendLine($"  {sector.sector.ToString().ToUpperInvariant(),-12} OUT {sector.output,5:F1}  " +
                              AsciiChart.LabeledBar("HEALTH", sector.health, 100, 6, 16) + building);
            }
            text.text = sb.ToString();

            BuildInvestment(player);
        }

        /// <summary>
        /// The economy's recurring decision, and its only treasury sink
        /// (GDD §20 amendment).
        ///
        /// The pillar that earns the money could not spend it: a verb audit found
        /// **zero treasury spends** in the economy, trade or diplomacy systems
        /// against six in the military, and every economy control was a one-shot
        /// or a toggle. After about year two this screen had nothing left to press
        /// while the balance climbed every month — which is the mechanical
        /// explanation for a player clicking END MONTH with nothing to do.
        ///
        /// Shaped like procurement because that shape is proven here: money now,
        /// capacity in years, bounded to three at once so investing everywhere is
        /// not an option and the operator has to decide what the country is for.
        /// </summary>
        void BuildInvestment(CountryState player)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("NATIONAL PROJECTS", W);

            var running = player.economy.programmes;
            AddText("terminal-text-dim").text =
                $"  {running.Count} of {IndustrialSystem.MaxProgrammes} under way."
                + (running.Count > 0
                    ? $"  Committed: {TotalMonthlyCost(player):F0} a month."
                    : "  Money spent here becomes capacity in years, not months.");

            foreach (var programme in running)
            {
                AddText("terminal-text-bright").text = IndustrialSystem.ProjectName(programme);
                AddText("terminal-text-dim").text = IndustrialSystem.ProjectProgress(player, programme);
                var captured = programme;
                var cancelRow = new VisualElement();
                cancelRow.AddToClassList("button-row");
                Root.Add(cancelRow);

                AddButton(cancelRow,
                    $"STOP {captured.sector.ToString().ToUpperInvariant()} WORK", "danger", () =>
                {
                    IndustrialSystem.Cancel(GameController.Instance.State,
                        GameController.Instance.State.playerCountryId, captured.sector);
                    Refresh();
                });
            }

            var record = new StringBuilder("RECENT PROJECT RECORD (LATEST 5)\n");
            int shown = 0;
            var chronicle = GameController.Instance.State.chronicle;
            for (int i = chronicle.Count - 1; i >= 0 && shown < 5; i--)
            {
                var entry = chronicle[i];
                if (entry.countryId != player.id || entry.category != ChronicleCategory.Economic
                    || entry.text == null || !entry.text.StartsWith("PROJECT ", System.StringComparison.Ordinal)) continue;
                record.AppendLine($"{entry.date.DisplayString}: {entry.text}");
                shown++;
            }
            if (shown == 0) record.AppendLine("No recorded project events yet. Older projects may have only a general history entry.");
            AddText("terminal-text-dim").text = record.ToString();

            if (!IndustrialSystem.CanBegin(GameController.Instance.State,
                    GameController.Instance.State.playerCountryId, out string blocked))
            {
                AddText("sig-hostile").text = "  " + blocked;
                return;
            }

            AddText("terminal-text-dim").text = "  " + IndustrialSystem.Describe(investmentScale);

            var scaleRow = new VisualElement();
            scaleRow.AddToClassList("button-row");
            Root.Add(scaleRow);
            foreach (IndustrialScale scale in System.Enum.GetValues(typeof(IndustrialScale)))
            {
                var captured = scale;
                bool current = investmentScale == scale;
                var button = new Button(() => { investmentScale = captured; Refresh(); })
                {
                    text = (current ? "► " : "") + scale.ToString().ToUpperInvariant()
                           + $" ({IndustrialSystem.MonthsFor(scale)}MO, "
                           + $"{IndustrialSystem.MonthlyCostFor(scale):F0}/MO)"
                };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                button.SetEnabled(!current);
                scaleRow.Add(button);
            }

            var sectorRow = new VisualElement();
            sectorRow.AddToClassList("button-row");
            Root.Add(sectorRow);
            foreach (EconomicSector sector in System.Enum.GetValues(typeof(EconomicSector)))
            {
                var captured = sector;

                bool alreadyBuilding = false;
                foreach (var programme in running)
                    if (programme.sector == sector) alreadyBuilding = true;
                if (alreadyBuilding) continue;

                AddButton(sectorRow,
                    $"{sector.ToString().ToUpperInvariant()} [{IndustrialSystem.CpCost} CP]", null, () =>
                {
                    GameController.Instance.BeginIndustrialProgramme(captured, investmentScale);
                    Refresh();
                });
            }
        }

        static float TotalMonthlyCost(CountryState player)
        {
            float total = 0f;
            foreach (var programme in player.economy.programmes)
                total += IndustrialSystem.MonthlyCostFor(programme.scale);
            return total;
        }

        void BuildTrade(GameState state)
        {
            var text = AddText();
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("TRADE & DEPENDENCIES", W));
            sb.AppendLine($"  EFFECTIVE TRADE HEALTH: {EconomySystem.TradeHealth(state, state.playerCountryId):F1}");
            foreach (var link in state.trade)
            {
                if (!link.Involves(state.playerCountryId)) continue;
                var partner = state.FindCountry(link.PartnerOf(state.playerCountryId));
                string status = link.embargoed ? "EMBARGOED" : $"TARIFF {link.tariff:F0}%";
                string supplies = link.focus == TradeFocus.General
                    ? ""
                    : $"   {Phrase.Caps(link.focus)}";
                sb.AppendLine($"  {AsciiChart.Cell(partner?.displayName.ToUpperInvariant(), AsciiChart.NameWidth(W))} VOL {link.volume,5:F1}   {status}{supplies}");
            }

            float energy = TradeSystem.Supply(state, state.playerCountryId, TradeFocus.Energy);
            float materials = TradeSystem.Supply(state, state.playerCountryId, TradeFocus.Materials);
            float food = TradeSystem.Supply(state, state.playerCountryId, TradeFocus.Food);
            if (energy > 0.5f || materials > 0.5f || food > 0.5f)
            {
                sb.AppendLine();
                sb.AppendLine("  SUPPLIED BY TRADE");
                if (energy > 0.5f) sb.AppendLine($"    ENERGY     +{energy:F0} to our ceiling");
                if (materials > 0.5f) sb.AppendLine($"    MATERIALS  +{materials:F0} to our ceiling");
                if (food > 0.5f) sb.AppendLine($"    FOOD       +{food:F0} to our ceiling");
            }

            text.text = sb.ToString();
            BuildTradeNegotiation(state);
        }

        /// <summary>
        /// The negotiating table for trade (GDD §20).
        ///
        /// Until this existed, trade links could only be created at world
        /// creation — so the action index advertised "open a trade link" and the
        /// Cabinet advised buying your way out of an energy dependency, and
        /// neither pointed at anything. This is the third answer to a shortfall,
        /// beside inventing your way out and taking what you need, and the only
        /// one that leaves you relying on somebody else.
        /// </summary>
        void BuildTradeNegotiation(GameState state)
        {
            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("TRADE NEGOTIATION", W);

            if (tradePartnerId == null)
                foreach (var country in state.countries)
                    if (!country.isPlayer) { tradePartnerId = country.id; break; }

            var partnerRow = MakeRow();
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var captured = country;
                bool current = tradePartnerId == country.id;
                var button = new Button(() => { tradePartnerId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + country.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                partnerRow.Add(button);
            }

            var focusRow = MakeRow();
            foreach (TradeFocus focus in System.Enum.GetValues(typeof(TradeFocus)))
            {
                var captured = focus;
                bool current = tradeFocus == focus;
                var button = new Button(() => { tradeFocus = captured; Refresh(); })
                { text = (current ? "► " : "") + Phrase.Caps(focus) };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                focusRow.Add(button);
            }

            var partnerState = state.FindCountry(tradePartnerId);
            var offer = TradeSystem.BestAcceptableDeal(
                state, state.playerCountryId, tradePartnerId, tradeFocus);

            var readout = AddText("terminal-text-dim");
            if (offer == null)
            {
                readout.text =
                    $"  {partnerState?.displayName} will not come to the table on " +
                    $"{Phrase.Of(tradeFocus).ToLowerInvariant()} terms at present. Improve relations, " +
                    "lift any sanctions between us, or approach a different partner.";
                return;
            }

            // **Our reading of them, not their answer.**
            //
            // This printed "THEY WOULD SIGN: volume 60, tariff 10%" straight from
            // `BestAcceptableDeal`, which is built on `WouldAccept` — ground truth
            // about a foreign government's decision. `TradeSystem.Assess` exists
            // specifically to fog that through `IntelligenceSystem.GetEstimate`,
            // and had **no callers anywhere in the codebase**.
            //
            // Same class as the settlement screen that leaked the opponent's
            // acceptance test, fixed once already in the audit sweep. The terms
            // themselves are what our negotiators would put on the table, so those
            // are ours to know; whether they would *sign* is a judgement about
            // them, and now reads as one.
            var outlook = TradeSystem.Assess(state, state.playerCountryId,
                new TradeDeal
                {
                    partnerId = tradePartnerId,
                    volume = offer.volume,
                    tariff = offer.tariff,
                    focus = tradeFocus,
                    preferentialTerms = offer.preferentialTerms
                });

            string reading;
            switch (outlook)
            {
                case TradeOutlook.Likely: reading = "OUR READING: they would likely sign"; break;
                case TradeOutlook.Unlikely: reading = "OUR READING: they would likely refuse"; break;
                case TradeOutlook.NoTerms: reading = "OUR READING: there are no terms to put"; break;
                default: reading = "OUR READING: uncertain — we do not have the access to say"; break;
            }

            readout.text =
                $"  TERMS WE WOULD OFFER: volume {offer.volume:F0}, tariff {offer.tariff:F0}%" +
                (offer.preferentialTerms ? ", on terms favourable to them" : "") + ".\n" +
                $"  {reading}.\n" +
                (tradeFocus == TradeFocus.General
                    ? "  A general link supports growth."
                    : $"  A {Phrase.Of(tradeFocus).ToLowerInvariant()} agreement raises our ceiling for it — " +
                      "and leaves us relying on them for it.");

            var actionRow = MakeRow();
            AddButton(actionRow, $"CONCLUDE AGREEMENT [{TradeSystem.ProposalCost} CP]", "primary", () =>
            {
                GameController.Instance.ProposeTrade(offer);
                Refresh();
            });

            if (state.FindTrade(state.playerCountryId, tradePartnerId) != null)
                AddButton(actionRow, $"WITHDRAW [{TradeSystem.WithdrawalCost} CP]", "danger", () =>
                {
                    GameController.Instance.WithdrawFromTrade(tradePartnerId);
                    Refresh();
                });
        }

        void BuildEconomicWarfare(GameState state)
        {
            AddText("terminal-text-bright").text = "\n" + AsciiChart.BoxHeader("ECONOMIC WARFARE", W);

            if (selectedTargetId == null)
                foreach (var country in state.countries)
                    if (!country.isPlayer) { selectedTargetId = country.id; break; }

            var targetRow = new VisualElement();
            targetRow.AddToClassList("button-row");
            Root.Add(targetRow);
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var captured = country;
                bool current = selectedTargetId == country.id;
                var button = new Button(() => { selectedTargetId = captured.id; Refresh(); })
                { text = (current ? "► " : "") + country.displayName.ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                targetRow.Add(button);
            }

            var severityRow = new VisualElement();
            severityRow.AddToClassList("button-row");
            Root.Add(severityRow);
            foreach (SanctionSeverity severity in System.Enum.GetValues(typeof(SanctionSeverity)))
            {
                var captured = severity;
                bool current = selectedSeverity == severity;
                var button = new Button(() => { selectedSeverity = captured; Refresh(); })
                { text = (current ? "► " : "") + severity.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                severityRow.Add(button);
            }

            var actionRow = new VisualElement();
            actionRow.AddToClassList("button-row");
            Root.Add(actionRow);

            var existing = state.FindSanction(state.playerCountryId, selectedTargetId);
            if (existing == null)
            {
                bool allowed = EconomySystem.CanImposeSanctions(state, selectedTargetId,
                    selectedSeverity, out string blocked);

                if (allowed)
                {
                    AddButton(actionRow, $"IMPOSE SANCTIONS [{EconomySystem.SanctionCost} CP]", "danger", () =>
                    {
                        GameController.Instance.ImposeSanctions(selectedTargetId, selectedSeverity);
                        Refresh();
                    });
                }
                else
                {
                    // Say why, rather than offering a control that does nothing.
                    AddText("terminal-text-dim").text = $"  UNAVAILABLE: {blocked}";
                }
            }
            else
            {
                AddButton(actionRow, "LIFT SANCTIONS [1 CP]", null, () =>
                {
                    GameController.Instance.LiftSanctions(selectedTargetId);
                    Refresh();
                });
            }

            AddButton(actionRow, "TARIFF 25% [1 CP]", null, () =>
            {
                GameController.Instance.SetTariff(selectedTargetId, 25f);
                Refresh();
            });
            AddButton(actionRow, "TARIFF 0% [1 CP]", null, () =>
            {
                GameController.Instance.SetTariff(selectedTargetId, 0f);
                Refresh();
            });

            var hint = AddText("terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine("  Sanctions damage the target and blow back on the sender through");
            sb.AppendLine("  inflation and supply disruption. Sanctioning a major trade partner");
            sb.AppendLine("  costs you far more than sanctioning a marginal one.");
            if (state.sanctions.Count > 0)
            {
                sb.AppendLine("\n  ACTIVE REGIMES");
                foreach (var sanction in state.sanctions)
                {
                    var sender = state.FindCountry(sanction.senderId);
                    var target = state.FindCountry(sanction.targetId);
                    sb.AppendLine($"   {sender?.displayName.ToUpperInvariant()} → {target?.displayName.ToUpperInvariant()}  " +
                                  $"{sanction.severity.ToString().ToUpperInvariant()}  {sanction.monthsActive} MO");
                }
            }
            hint.text = sb.ToString();
        }
    }
}

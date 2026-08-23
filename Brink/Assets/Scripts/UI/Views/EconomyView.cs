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

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string selectedTargetId;

        // Trade negotiation selection — kept on the view, not the save: which
        // partner is highlighted is not part of the world.
        string tradePartnerId;
        TradeFocus tradeFocus = TradeFocus.Energy;
        SanctionSeverity selectedSeverity = SanctionSeverity.Pressure;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();
            AddAuthorityBadge(state, Pillar.Economy);
            AddCabinetAdvice(state, Pillar.Economy);
            BuildMacro(state, player);
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
                sb.AppendLine($"  {sector.sector.ToString().ToUpperInvariant(),-12} OUT {sector.output,5:F1}  " +
                              AsciiChart.LabeledBar("HEALTH", sector.health, 100, 6, 16));
            text.text = sb.ToString();
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
            if (energy > 0.5f || materials > 0.5f)
            {
                sb.AppendLine();
                sb.AppendLine("  SUPPLIED BY TRADE");
                if (energy > 0.5f) sb.AppendLine($"    ENERGY     +{energy:F0} to our ceiling");
                if (materials > 0.5f) sb.AppendLine($"    MATERIALS  +{materials:F0} to our ceiling");
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

            readout.text =
                $"  THEY WOULD SIGN: volume {offer.volume:F0}, tariff {offer.tariff:F0}%" +
                (offer.preferentialTerms ? ", on terms favourable to them" : "") + ".\n" +
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

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
        }

        void AddButton(VisualElement row, string text, string extraClass, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cmd-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            row.Add(button);
        }
    }
}

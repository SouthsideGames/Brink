using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Research and capability (GDD §11). Programmes are funded across the five
    /// pillars; what they deliver is the ability to do something, never the
    /// thing itself. Foreign holdings are shown as assessments, not facts.
    /// </summary>
    public class TechnologyView : TerminalView
    {
        public override string Id => "RESEARCH";
        public override string ShortCode => "RES";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        Pillar selectedPillar = Pillar.Military;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();
            BuildRunningProgrammes(state, player);
            BuildPillarSelector();
            BuildCatalog(state, player);
            BuildForeignAssessment(state);
        }

        void BuildRunningProgrammes(GameState state, CountryState player)
        {
            var text = AddText("terminal-text-bright");
            var sb = new StringBuilder();
            sb.AppendLine(AsciiChart.BoxHeader("RESEARCH PROGRAMMES", W));
            sb.AppendLine($" RUNNING: {player.technology.programs.Count}/{TechnologySystem.MaxPrograms}");

            foreach (var program in player.technology.programs)
                sb.AppendLine($"   {AsciiChart.Cell(program.label.ToUpperInvariant(), AsciiChart.NameWidth(W, 0.45f))} " +
                              $"{program.monthsRemaining,3} MO REMAINING AT {program.monthlyCost:F0}/MO");
            if (player.technology.programs.Count == 0)
                sb.AppendLine("   Nothing under way. Capability takes years and costs every month of them.");

            sb.AppendLine();
            sb.AppendLine(" CAPABILITIES HELD");
            if (player.technology.capabilities.Count == 0)
            {
                sb.AppendLine("   NONE.");
            }
            else
            {
                foreach (var held in player.technology.capabilities)
                {
                    var definition = CapabilityCatalog.Find(held.capabilityId);
                    sb.AppendLine($"   {AsciiChart.Cell(definition?.name.ToUpperInvariant(), AsciiChart.NameWidth(W, 0.45f))} " +
                                  $"{held.source.ToString().ToUpperInvariant(),-10} " +
                                  AsciiChart.Bar(held.maturity, 100, 12) + $" {held.maturity,5:F0}");
                }
                sb.AppendLine("   Maturity is how well we actually understand it. What we developed we");
                sb.AppendLine("   understand; what we took, we merely possess until we have used it.");
            }
            text.text = sb.ToString();
        }

        void BuildPillarSelector()
        {
            AddText().text = "\n PROGRAMMES AVAILABLE";
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);

            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                var captured = pillar;
                bool current = selectedPillar == pillar;
                var button = new Button(() => { selectedPillar = captured; Refresh(); })
                { text = (current ? "► " : "") + pillar.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }
        }

        void BuildCatalog(GameState state, CountryState player)
        {
            foreach (var definition in CapabilityCatalog.ForPillar(selectedPillar))
            {
                bool held = player.technology.Has(definition.id);
                bool running = player.technology.IsResearching(definition.id);
                bool available = TechnologySystem.CanResearch(state, player, definition.id, out string reason);

                var text = AddText(held ? "terminal-text-bright" : "terminal-text-dim");
                var sb = new StringBuilder();
                sb.AppendLine($" [{(held ? "X" : running ? "~" : " ")}] {definition.name.ToUpperInvariant()}   " +
                              $"{definition.researchMonths} MO @ {definition.monthlyCost:F0}/MO");
                sb.AppendLine($"     {definition.description}");
                if (!held && !running && !available)
                    sb.AppendLine($"     UNAVAILABLE: {reason}");
                text.text = sb.ToString();

                if (held || running || !available) continue;

                var row = new VisualElement();
                row.AddToClassList("button-row");
                Root.Add(row);
                var button = new Button(() =>
                {
                    GameController.Instance.BeginResearch(definition.id);
                    Refresh();
                })
                { text = $"AUTHORIZE [{TechnologySystem.StartResearchCost} CP]" };
                button.AddToClassList("cmd-button");
                button.AddToClassList("primary");
                row.Add(button);
            }
        }

        void BuildForeignAssessment(GameState state)
        {
            var text = AddText("terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.BoxHeader("FOREIGN CAPABILITY (ASSESSED)", W));

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;

                // Knowing what a rival has fielded is an intelligence problem.
                var estimate = IntelligenceSystem.GetEstimate(state, state.playerCountryId,
                    country.id, IntelDomain.Military);
                bool canSee = estimate != null && estimate.confidence >= ConfidenceGrade.Moderate;

                if (!canSee)
                {
                    sb.AppendLine($"  {country.displayName.ToUpperInvariant(),-18} NO RELIABLE ASSESSMENT");
                    continue;
                }

                var names = new System.Collections.Generic.List<string>();
                foreach (var held in country.technology.capabilities)
                    names.Add(CapabilityCatalog.Find(held.capabilityId)?.name ?? held.capabilityId);

                sb.AppendLine($"  {country.displayName.ToUpperInvariant(),-18} " +
                              (names.Count == 0 ? "NOTHING NOTABLE FIELDED" : string.Join(", ", names)));
            }
            text.text = sb.ToString();
        }
    }
}

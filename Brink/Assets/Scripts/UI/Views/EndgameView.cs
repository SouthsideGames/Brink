using System.Text;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// Strategic instruments (GDD §21). Deliberately austere and slightly
    /// unwelcoming: these are the decisive options, they take years to prepare,
    /// and using one permanently changes how the world regards us.
    /// </summary>
    public class EndgameView : TerminalView
    {
        // Named for what the panel holds, not for how important it is.
        // "STRATEGIC/STG" sat next to "STRATEGIST/STR" in the nav rail and the
        // two were indistinguishable at a glance — reported from play as a
        // straight question about which was which.
        public override string Id => "ENDGAME";
        public override string ShortCode => "EGM";

        /// <summary>Terminal width, measured from the real panel (see TerminalMetrics).</summary>
        static int W => TerminalMetrics.Columns;

        string selectedTargetId;

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            var state = gc.State;
            var player = state.PlayerCountry;
            if (player == null) return;

            Root.Clear();

            var header = AddText("terminal-text-bright");
            header.text =
                AsciiChart.BoxHeader("STRATEGIC INSTRUMENTS", W) + "\n" +
                " The state's decisive options — not your record, which is in OPERATOR.\n" +
                " Each pillar can reach an effect capable of breaking an opponent.\n" +
                " None of them is a button. Each needs a capability we command, years\n" +
                " of preparation, and conditions that permit it — and each leaves a\n" +
                " mark on how every other state regards us.\n";

            BuildTargetSelector(state);

            foreach (EndgameType type in System.Enum.GetValues(typeof(EndgameType)))
                BuildInstrument(state, player, type);

            BuildForeignProgrammes(state);
            BuildHistory(state);
        }

        /// <summary>
        /// What we believe others are building. This is collection, not fact:
        /// a programme we have no coverage of simply does not appear here.
        /// </summary>
        void BuildForeignProgrammes(GameState state)
        {
            var sb = new StringBuilder();
            int found = 0;

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                foreach (EndgameType type in System.Enum.GetValues(typeof(EndgameType)))
                {
                    float known = EndgameSystem.KnownPreparation(state, state.playerCountryId, country.id, type);
                    if (known < 0f) continue;
                    found++;
                    string band = known >= 100f ? "READY"
                                : known >= 60f ? "ADVANCED"
                                : "UNDER WAY";
                    sb.AppendLine($"  {AsciiChart.Cell(country.displayName, AsciiChart.NameWidth(W, 0.28f))} {AsciiChart.Cell(EndgameSystem.NameOf(type), AsciiChart.NameWidth(W, 0.40f))} {band}");
                }
            }

            var text = AddText("terminal-text-dim");
            sb.Insert(0, "\n" + AsciiChart.BoxHeader("FOREIGN PROGRAMMES", W) + "\n");
            if (found == 0)
                sb.AppendLine("  Nothing reported. This is not the same as nothing existing.");
            text.text = sb.ToString();
        }

        void BuildTargetSelector(GameState state)
        {
            if (selectedTargetId == null)
                foreach (var country in state.countries)
                    if (!country.isPlayer) { selectedTargetId = country.id; break; }

            AddText("terminal-text-dim").text = " TARGET";
            var row = MakeRow();
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                var captured = country.id;
                bool current = selectedTargetId == country.id;
                var button = new Button(() => { selectedTargetId = captured; Refresh(); })
                { text = (current ? "► " : "") + (WorldFactory.FindProfile(country.id)?.mapCode ?? country.id) };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }
        }

        void BuildInstrument(GameState state, CountryState player, EndgameType type)
        {
            float progress = player.endgames.ProgressFor(type);
            bool canPrepare = EndgameSystem.CanPrepare(state, player, type, out string prepareReason);
            bool canExecute = EndgameSystem.CanExecute(state, type, selectedTargetId, out string executeReason);
            bool ready = progress >= 100f;

            var text = AddText(ready ? "terminal-text-bright" : "terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($" {EndgameSystem.NameOf(type).ToUpperInvariant()}   " +
                          $"[{EndgameSystem.PillarOf(type).ToString().ToUpperInvariant()}]   " +
                          $"SEVERITY {EndgameSystem.SeverityOf(type).ToString().ToUpperInvariant()}");
            sb.AppendLine("   " + AsciiChart.LabeledBar("PREPARATION", progress, 100, 12, 22));
            sb.AppendLine($"   REQUIRES: {CapabilityCatalog.Find(EndgameSystem.RequiredCapability(type))?.name}");
            if (!canPrepare) sb.AppendLine($"   UNAVAILABLE: {prepareReason}");
            else if (ready && !canExecute) sb.AppendLine($"   HELD IN READINESS: {executeReason}");
            text.text = sb.ToString();

            var row = MakeRow();

            if (canPrepare && !ready)
            {
                AddButton(row, $"PREPARE [{EndgameSystem.PreparationCost} CP]", null, () =>
                {
                    GameController.Instance.PrepareEndgame(type);
                    Refresh();
                });
            }

            if (!ready || !canExecute) return;

            var execute = new Button(() =>
            {
                GameController.Instance.ExecuteEndgame(type, selectedTargetId);
                Refresh();
            })
            { text = $"EXECUTE [{EndgameSystem.ExecutionCost} CP]" };
            execute.AddToClassList("cmd-button");
            execute.AddToClassList("danger");
            row.Add(execute);
        }

        void BuildHistory(GameState state)
        {
            if (state.endgameRecords.Count == 0) return;

            var text = AddText("terminal-text-dim");
            var sb = new StringBuilder();
            sb.AppendLine("\n" + AsciiChart.BoxHeader("ON THE RECORD", W));
            foreach (var record in state.endgameRecords)
                sb.AppendLine($"  {record.date.SortKey}  {record.summary}");
            sb.AppendLine("  These are remembered. They do not fade from the record.");
            text.text = sb.ToString();
        }
    }
}

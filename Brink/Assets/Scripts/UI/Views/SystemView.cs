using Brink.Core;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// SYSTEM console: debug/test controls (GDD §34.1 — every significant system
    /// exposes debug controls) plus the live log. Developer-facing by design.
    /// </summary>
    public class SystemView : TerminalView
    {
        public override string Id => "SYSTEM";
        public override string ShortCode => "SYS";

        readonly ScrollView logScroll;
        readonly Label saveInfo;

        public SystemView()
        {
            saveInfo = AddText("terminal-text-dim");

            var row0 = MakeRow();
            AddButton(row0, "DBG QUICK START", "debug", () =>
                GameController.Instance.NewGame(UnityEngine.Random.Range(int.MinValue, int.MaxValue)));

            var row1 = MakeRow();
            AddButton(row1, "SPEND 1 CP", null, () =>
                GameController.Instance.Turns.SpendCommandPoints(1, "Manual test intervention"));
            AddButton(row1, "DBG +12 MO", "debug", () =>
            {
                for (int i = 0; i < 12; i++)
                    if (!GameController.Instance.EndMonth()) break;
            });
            AddButton(row1, "FORCE CRISIS", "debug", () =>
            {
                var state = GameController.Instance.State;
                CrisisSystem.Trigger(state, CrisisSystem.CatalogIds[
                    UnityEngine.Random.Range(0, CrisisSystem.CatalogIds.Length)]);
            });
            AddButton(row1, "DBG AGE CABINET", "debug", () =>
            {
                var state = GameController.Instance.State;
                foreach (var official in state.cabinet) official.age += 5f;
                GameLog.Info("CABINET", "Cabinet aged five years.");
            });
            AddButton(row1, "DBG FREEZE AGING", "debug", () =>
            {
                CabinetLifecycle.Frozen = !CabinetLifecycle.Frozen;
                GameLog.Info("CABINET", CabinetLifecycle.Frozen
                    ? "Cabinet aging frozen — nobody retires or dies."
                    : "Cabinet aging resumed.");
            });
            AddButton(row1, "DBG FULL REPORTING", "debug", () =>
            {
                ReportingSystem.Disabled = !ReportingSystem.Disabled;
                GameLog.Info("REPORT", ReportingSystem.Disabled
                    ? "Cabinet reporting filter OFF — every item reaches the desk."
                    : "Cabinet reporting filter ON.");
            });

            var row2 = MakeRow();
            AddButton(row2, "SAVE SLOT 1", null, () => GameController.Instance.SaveToSlot(1));
            AddButton(row2, "LOAD SLOT 1", null, () =>
            {
                if (SaveSystem.SaveExists(1)) GameController.Instance.LoadFromSlot(1);
                else GameLog.Warn("SAVE", "No manual save in slot 1.");
            });
            AddButton(row2, "FULL RESET", "danger", () => GameController.Instance.ResetGame());

            var row3 = MakeRow();
            foreach (Data.Difficulty difficulty in System.Enum.GetValues(typeof(Data.Difficulty)))
            {
                var captured = difficulty;
                AddButton(row3, $"AI: {difficulty.ToString().ToUpperInvariant()}", "debug", () =>
                {
                    var gc = GameController.Instance;
                    if (!gc.IsRunning) return;
                    gc.State.difficulty = captured;
                    GameLog.Info("AI", $"Difficulty set to {captured} " +
                                       $"({AISystem.ActionBudget(captured)} actions/mo per state).");
                });
            }

            BuildSessionReview();

            var logTitle = AddText("terminal-text-bright");
            logTitle.text = "SYSTEM LOG";

            logScroll = new ScrollView(ScrollViewMode.Vertical)
            {
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            logScroll.style.flexGrow = 1;
            logScroll.style.minHeight = 120;
            Root.Add(logScroll);

            foreach (var entry in GameLog.Entries)
                AppendLogLine(entry);
            GameLog.OnLog += AppendLogLine;
        }

        /// <summary>
        /// What the session recorder makes of the play so far (GDD §34.1).
        ///
        /// The point of showing it here rather than only writing a file: with a
        /// single tester, the fastest loop is noticing something looks wrong
        /// while still sitting in the save that produced it.
        /// </summary>
        void BuildSessionReview()
        {
            var gc = GameController.Instance;

            var title = AddText("terminal-text-bright");
            title.text = "\n" + AsciiChart.BoxHeader("SESSION REVIEW", TerminalMetrics.Columns);

            var row = MakeRow();
            AddButton(row, TelemetryFile.Enabled ? "► RECORDING ON" : "RECORDING OFF", "debug",
                () => { TelemetryFile.Enabled = !TelemetryFile.Enabled; Refresh(); });

            AddButton(row, "WRITE SESSION FILE", "debug", () =>
            {
                string path = TelemetryFile.Write(gc.IsRunning ? gc.State : null);
                GameLog.Info("TELEMETRY", string.IsNullOrEmpty(path)
                    ? "Nothing written."
                    : $"Written: {path}");
            });

            if (!TelemetryFile.Enabled)
            {
                AddText("terminal-text-dim").text =
                    "   Not recording. Nothing leaves this device either way — the recorder writes " +
                    "a file next to the saves and makes no network call of any kind.";
                return;
            }

            if (!gc.IsRunning) return;

            foreach (var finding in TelemetryAnalysis.Findings(gc.State))
            {
                string style = finding.severity == FindingSeverity.Suspect ? "sig-hostile"
                    : finding.severity == FindingSeverity.Warning ? "terminal-text-bright"
                    : "terminal-text-dim";
                AddText(style).text = $"   {finding.headline}";
                AddText("terminal-text-dim").text = $"     {finding.detail}";
            }
        }

        VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
        }

        static void AddButton(VisualElement row, string text, string extraClass, System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cmd-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            row.Add(button);
        }

        protected override void Build()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning) return;
            saveInfo.text =
                $"SAVE DIR: {SaveSystem.SaveDirectory}\n" +
                $"AUTOSAVE: SLOT {GameController.AutosaveSlot}   SCHEMA v{SaveSystem.CurrentSaveVersion}   SEED: {gc.State.rngSeed}\n" +
                $"AI DIFFICULTY: {gc.State.difficulty.ToString().ToUpperInvariant()} " +
                $"({AISystem.ActionBudget(gc.State.difficulty)} actions/mo)   " +
                $"AI STATES: {gc.State.aiStates.Count}";
        }

        void AppendLogLine(LogEntry entry)
        {
            var line = new Label($"[{entry.category}] {entry.message}");
            line.AddToClassList("log-line");
            if (entry.level == LogLevel.Warning) line.AddToClassList("log-line-warning");
            if (entry.level == LogLevel.Error) line.AddToClassList("log-line-error");
            logScroll.Add(line);

            while (logScroll.childCount > 200)
                logScroll.RemoveAt(0);

            logScroll.schedule.Execute(() =>
                logScroll.scrollOffset = new UnityEngine.Vector2(0, float.MaxValue));
        }
    }
}

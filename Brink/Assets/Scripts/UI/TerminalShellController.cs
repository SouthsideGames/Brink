using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using Brink.UI.Views;
using UnityEngine;
using UnityEngine.UIElements;

namespace Brink.UI
{
    /// <summary>
    /// Command terminal shell (GDD Phase 1): status bar, responsive nav rail and
    /// view host. Layout adapts by size class — tablets get a full left rail,
    /// foldables a narrow rail, phones a horizontal tab strip — and respects
    /// device safe areas (notches/cutouts) in landscape.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TerminalShellController : MonoBehaviour
    {
        VisualElement root;
        VisualElement contentHost;
        ScrollView navRail;
        Label classificationLabel;
        Label dateLabel;
        Label cpLabel;
        Label crisisIndicator;
        VisualElement crisisOverlay;
        CrisisPanel crisisPanel;
        VisualElement scanlines;
        Label measureProbe;
        int lastScreenWidth;
        int lastScreenHeight;
        DisplaySettingsPanel settingsPanel;
        bool pulseOn;
        readonly List<TerminalView> views = new List<TerminalView>();
        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        TerminalView activeView;
        AssessmentScreen assessmentScreen;
        TutorialPanel tutorialPanel;
        SizeClass sizeClass = SizeClass.Large;
        bool cursorOn = true;

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement.Q<VisualElement>("terminal-root");
            contentHost = root.Q<VisualElement>("content-host");
            navRail = root.Q<ScrollView>("nav-rail");
            classificationLabel = root.Q<Label>("classification-label");
            dateLabel = root.Q<Label>("date-label");
            cpLabel = root.Q<Label>("cp-label");
            crisisOverlay = root.Q<VisualElement>("crisis-overlay");
            if (crisisOverlay != null) { crisisOverlay.Clear(); crisisPanel = new CrisisPanel(); crisisOverlay.Add(crisisPanel.Root); }
            crisisIndicator = new Label("■ CRISIS"); crisisIndicator.AddToClassList("status-crisis");
            var statusRight = root.Q<VisualElement>(className: "status-right"); statusRight?.Insert(0, crisisIndicator);
            var endMonth = root.Q<Button>("end-month-button");
            if (endMonth != null) endMonth.clicked += () =>
            {
                if (!GameController.Instance.EndMonth()) { Brink.Audio.AudioDirector.Play(Brink.Audio.SfxId.CommandRejected); return; }
                Brink.Audio.AudioDirector.Play(Brink.Audio.SfxId.EndMonth);
                Brink.Audio.AudioDirector.Sync(GameController.Instance.State);
                RefreshAll();
                if (!ShowRecordClosedIfDue()) { SelectView("COMMAND CENTER"); ShowMonthlyBriefing(); }
            };
            settingsPanel = new DisplaySettingsPanel(ApplyDisplaySettingsAndRefresh);
            var displayButton = new Button(() => settingsPanel.Toggle()) { text = "DSP" }; displayButton.AddToClassList("cmd-button"); statusRight?.Insert(0, displayButton);
            measureProbe = new Label("M"); measureProbe.AddToClassList("terminal-text"); measureProbe.style.position = Position.Absolute; measureProbe.style.opacity = 0f; measureProbe.pickingMode = PickingMode.Ignore; contentHost.Add(measureProbe);
            contentHost.Add(settingsPanel.Root);
            BuildScanlines(); BuildViews();
            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            GameLog.OnLog += OnAnyLog;
            GameController.Instance.StateReplaced += Views.TerminalView.ResetExplanations;
            GameController.Instance.StateReplaced += RefreshAll;
            tutorialPanel = new TutorialPanel(RefreshAll); contentHost.Add(tutorialPanel.Root);
            assessmentScreen = new AssessmentScreen(() => { UpdateSessionMode(); SelectView("COMMAND CENTER"); RefreshAll(); }); contentHost.Add(assessmentScreen.Root);
            GameController.Instance.EnsureStarted(); UpdateSessionMode(); SelectView("COMMAND CENTER"); ApplyDisplaySettings(); RefreshAll();
            classificationLabel.schedule.Execute(BlinkCursor).Every(530); root.schedule.Execute(PulseEndMonth).Every(700);
        }

        void OnDisable() { GameLog.OnLog -= OnAnyLog; GameController.Instance.StateReplaced -= Views.TerminalView.ResetExplanations; GameController.Instance.StateReplaced -= RefreshAll; }

        public static List<TerminalView> BuildPanels(bool includeDebugConsoles)
        {
            var panels = new List<TerminalView> { new CommandCenterView(), new BriefingView(), new ActionsView(), new WorldMapView(), new DossierView(), new CabinetView(), new MilitaryView(), new EconomyView(), new IntelligenceView(), new DiplomacyView(), new GovernmentView(), new TechnologyView(), new EndgameView(), new StrategistView(), new ChronicleView() };
            if (includeDebugConsoles) { panels.Add(new SystemView()); panels.Add(new AudioDebugView()); }
            return panels;
        }

        void BuildViews() { views.AddRange(BuildPanels(Debug.isDebugBuild || Application.isEditor)); foreach (var view in views) { contentHost.Add(view.Root); var captured = view; var button = new Button(() => SelectView(captured.Id)) { text = captured.Id }; button.AddToClassList("nav-button"); navRail.Add(button); navButtons[view.Id] = button; } }
        void SelectView(string id) { if (GameController.Instance.AwaitingAssessment) return; bool changed = activeView == null || activeView.Id != id; foreach (var view in views) { bool target = view.Id == id; view.SetVisible(target); if (target) activeView = view; navButtons[view.Id].EnableInClassList("nav-button-active", target); } if (changed) { Brink.Audio.AudioDirector.Play(Brink.Audio.SfxId.PanelOpen); Brink.Audio.AudioDirector.SetContext(Brink.Audio.AudioCues.ContextFor(id)); } }
        void UpdateSessionMode() { bool awaiting = GameController.Instance.AwaitingAssessment; assessmentScreen.Root.style.display = awaiting ? DisplayStyle.Flex : DisplayStyle.None; navRail.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex; if (awaiting) Brink.Audio.AudioDirector.SetMusic(Brink.Audio.MusicState.MainMenu); else Brink.Audio.AudioDirector.Sync(GameController.Instance.State, silentAlerts: true); if (tutorialPanel != null) tutorialPanel.Root.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex; var end = root.Q<Button>("end-month-button"); if (end != null) end.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex; dateLabel.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex; cpLabel.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex; if (awaiting) { foreach (var view in views) view.SetVisible(false); assessmentScreen.Restart(); } }
        void RefreshAll() { if (GameController.Instance.AwaitingAssessment) { UpdateSessionMode(); return; } UpdateStatusBar(); UpdateCrisisOverlay(); tutorialPanel?.Refresh(); activeView?.Refresh(); ApplyTextPolicy(root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns); }
        void ApplyDisplaySettingsAndRefresh() { ApplyDisplaySettings(); UpdateMetrics(); RefreshAll(); }
        void OnAnyLog(LogEntry entry) { UpdateStatusBar(); UpdateCrisisOverlay(); }
        void UpdateStatusBar() { var gc = GameController.Instance; if (!gc.IsRunning) { classificationLabel.text = BannerText(); return; } dateLabel.text = gc.State.date.DisplayString; bool spent = gc.State.CommandCapacitySpent; bool roomy = sizeClass == SizeClass.Large; cpLabel.text = spent && roomy ? $"CP 0 — MONTH SPENT   INF {gc.State.influence}  PC {gc.State.politicalCapital:F0}" : $"CP {gc.State.commandPoints.current}  INF {gc.State.influence}  PC {gc.State.politicalCapital:F0}"; cpLabel.EnableInClassList("status-stat-spent", spent); crisisIndicator.style.display = gc.State.HasOpenCrisis ? DisplayStyle.Flex : DisplayStyle.None; UpdateAttention(gc.State); }
        void UpdateAttention(GameState state) { ApplyNavLabels(); var items = AttentionSystem.Collect(state); var end = root.Q<Button>("end-month-button"); if (end == null) return; int decisions = AttentionSystem.DecisionCount(items); end.text = decisions > 0 ? $"END MONTH ({decisions})" : "END MONTH"; end.EnableInClassList("end-month-unanswered", decisions > 0); }

        void ShowMonthlyBriefing()
        {
            if (!DisplaySettings.MonthlyBriefing) return; var gc = GameController.Instance; if (!gc.IsRunning) return;
            var overlay = root.Q<VisualElement>("rollover-overlay"); var body = root.Q<ScrollView>("rollover-body"); var actions = root.Q<VisualElement>("rollover-actions"); var title = root.Q<Label>("rollover-title"); if (overlay == null || body == null || actions == null) return;
            var state = gc.State; body.Clear(); actions.Clear(); if (title != null) title.text = $"MONTHLY BRIEFING — {state.date.DisplayString}";
            void Section(string heading) { var l = new Label(heading); l.AddToClassList("terminal-text-bright"); body.Add(l); }
            void Line(string text, string cls = "terminal-text") { var l = new Label(text); l.AddToClassList("terminal-text"); if (cls != "terminal-text") l.AddToClassList(cls); body.Add(l); }

            var debrief = MonthlyDebriefSystem.Build(state);
            Section(" LAST MONTH — WHAT HAPPENED / WHY");
            if (debrief.consequences.Count == 0)
            {
                Line("  No resolved-month consequence record is available yet.", "terminal-text-dim");
            }
            else
            {
                Line($"  {new GameDate(debrief.year, debrief.month).DisplayString} — {debrief.consequences.Count} tracked consequence{(debrief.consequences.Count == 1 ? "" : "s")}.", "terminal-text-dim");
                int shown = sizeClass == SizeClass.Compact ? 3 : sizeClass == SizeClass.Medium ? 4 : 5;
                for (int i = 0; i < debrief.consequences.Count && i < shown; i++)
                {
                    var c = debrief.consequences[i];
                    string marker = c.direction == "DETERIORATED" ? "!" : c.direction == "IMPROVED" ? "+" : ".";
                    string cls = c.direction == "DETERIORATED" ? "sig-hostile" : "terminal-text";
                    Line($"  {marker} {c.label.ToUpperInvariant()} {c.delta:+0.0;-0.0;0.0} — {c.direction}", cls);
                    Line($"     WHY: {c.driver}{(c.incomplete ? " [REPORTING INCOMPLETE]" : "")}", "terminal-text-dim");
                    if (c.playerLinked) Line($"     YOUR ORDER: {c.sourceActionId}", "terminal-text-bright");
                }
                if (debrief.consequences.Count > shown) Line($"  + {debrief.consequences.Count - shown} lower-priority consequence{(debrief.consequences.Count - shown == 1 ? "" : "s")} remain on record.", "terminal-text-dim");
            }

            if (DisplaySettings.WorldWire) { var wire = WorldWire.LastMonth(state); Section("\n WORLD WIRE — LAST MONTH"); if (wire.Count == 0) Line("  A quiet month. Nothing of note reached the wire.", "terminal-text-dim"); else foreach (var item in wire) Line("  " + WorldWire.Format(state, item), item.involvesUs ? "sig-rival" : "terminal-text"); }
            var attention = AttentionSystem.Collect(state); Section("\n NEW MONTH — YOUR DESK"); if (attention.Count == 0) Line("  Nothing is waiting on you. Ending the month without intervention is a valid choice.", "terminal-text-dim"); else foreach (var item in attention) Line($"  {(item.level == AttentionLevel.Decision ? "!" : ".")} [{item.viewId}] {item.summary}", item.level == AttentionLevel.Decision ? "sig-hostile" : "terminal-text-dim");
            if (state.cabinetReport.Count > 0) { Section("\n YOUR CABINET"); foreach (var line in state.cabinetReport) Line($"  {line.pillar.ToString().ToUpperInvariant()} — {line.officialName} {line.summary}", line.ownJudgement ? "terminal-text" : "terminal-text-dim"); }
            var directives = DirectiveSystem.Collect(state); if (directives.Count > 0) { Section("\n CABINET RECOMMENDS"); foreach (var d in directives) { Line($"  ▸ {d.title.ToUpperInvariant()}   → {d.viewId}"); Line($"     {d.suggestion}", "terminal-text-dim"); } }
            var dismiss = new Button(() => overlay.style.display = DisplayStyle.None) { text = "ENTER NEW MONTH" }; dismiss.AddToClassList("cmd-button"); dismiss.AddToClassList("primary"); actions.Add(dismiss); ApplyTextPolicy(overlay, DisplaySettings.ParagraphSpacing, TerminalMetrics.OverlayColumns); overlay.style.display = DisplayStyle.Flex;
        }

        string lastVerdictShownFor = ""; bool tenureShown;
        bool ShowRecordClosedIfDue() { var gc = GameController.Instance; if (!gc.IsRunning) return false; var state = gc.State; if (state.mandateRecord != null && lastVerdictShownFor != state.mandate?.title) { lastVerdictShownFor = state.mandate?.title ?? ""; string headline = state.mandateRecord.verdict.ToString().ToUpperInvariant(); ShowRecordClosed($"MANDATE REVIEW — {headline}", (state.mandate?.title ?? "").ToUpperInvariant(), state.mandateRecord.summary, "The posting continues. The record is closed."); return true; } if (state.tenureReviewed && !tenureShown) { tenureShown = true; Notification review = null; for (int i = state.notifications.Count - 1; i >= 0; i--) if (state.notifications[i].title.StartsWith("TENURE")) { review = state.notifications[i]; break; } ShowRecordClosed("TENURE REVIEW", "FORTY YEARS AT THIS TERMINAL", review?.body ?? "The record is closed.", ""); return true; } return false; }
        void ShowRecordClosed(string heading, string subheading, string bodyText, string footer) { var overlay = root.Q<VisualElement>("rollover-overlay"); var body = root.Q<ScrollView>("rollover-body"); var actions = root.Q<VisualElement>("rollover-actions"); var title = root.Q<Label>("rollover-title"); if (overlay == null || body == null || actions == null) return; body.Clear(); actions.Clear(); if (title != null) title.text = heading; void Line(string text, string cls = "terminal-text") { var l = new Label(text); l.AddToClassList("terminal-text"); if (cls != "terminal-text") l.AddToClassList(cls); body.Add(l); } Line(" " + subheading, "terminal-text-bright"); Line(""); foreach (var line in bodyText.Split('\n')) Line(" " + line); if (!string.IsNullOrEmpty(footer)) { Line(""); Line(" " + footer, "terminal-text-dim"); } var dismiss = new Button(() => { overlay.style.display = DisplayStyle.None; SelectView("COMMAND CENTER"); ShowMonthlyBriefing(); }) { text = "ACKNOWLEDGE" }; dismiss.AddToClassList("cmd-button"); dismiss.AddToClassList("primary"); actions.Add(dismiss); ApplyTextPolicy(overlay, DisplaySettings.ParagraphSpacing, TerminalMetrics.OverlayColumns); overlay.style.display = DisplayStyle.Flex; }
        void UpdateCrisisOverlay() { var gc = GameController.Instance; var crises = gc.IsRunning ? gc.State.activeCrises : null; if (crisisOverlay == null) return; bool active = crises != null && crises.Count > 0; crisisOverlay.style.display = active ? DisplayStyle.Flex : DisplayStyle.None; if (!active) return; var crisis = crises[0]; crisisPanel?.Show(crisis, index => { GameController.Instance.ResolveCrisis(crisis, index); RefreshAll(); }); ApplyTextPolicy(crisisOverlay, DisplaySettings.ParagraphSpacing, TerminalMetrics.OverlayColumns); }
        void PulseEndMonth() { var end = root.Q<Button>("end-month-button"); if (end == null) return; var gc = GameController.Instance; bool spent = gc.IsRunning && !gc.AwaitingAssessment && gc.State.CommandCapacitySpent; end.EnableInClassList("end-month-spent", spent); if (!spent) { end.RemoveFromClassList("end-month-spent-on"); pulseOn = false; return; } pulseOn = !pulseOn; end.EnableInClassList("end-month-spent-on", pulseOn); }
        public static string BannerFor(SizeClass size, bool awaiting) { switch (size) { case SizeClass.Large: return awaiting ? "STRATEGIC APTITUDE ASSESSMENT // CLASSIFIED" : "UNKNOWN GAME // CLASSIFIED"; case SizeClass.Medium: return awaiting ? "ASSESSMENT // CLASSIFIED" : "CLASSIFIED"; default: return string.Empty; } }
        string BannerText() => BannerFor(sizeClass, GameController.Instance.AwaitingAssessment);
        void BlinkCursor() { cursorOn = !cursorOn; classificationLabel.text = BannerText() + (cursorOn ? "█" : " "); }
        void OnGeometryChanged(GeometryChangedEvent evt) { float width = root.resolvedStyle.width; if (float.IsNaN(width) || width <= 0) return; ApplyPanelScale(); ApplySafeArea(); UpdateMetrics(); ApplyShortScreenClass(); }
        void ApplySizeClass(SizeClass newSize) { if (newSize != sizeClass || !root.ClassListContains(Breakpoints.ToUssClass(newSize))) { root.RemoveFromClassList(Breakpoints.ToUssClass(sizeClass)); sizeClass = newSize; root.AddToClassList(Breakpoints.ToUssClass(sizeClass)); ApplyNavLabels(); classificationLabel.text = BannerText() + (cursorOn ? "█" : " "); UpdateStatusBar(); } }
        void ApplyPanelScale(bool force = false) { if (Screen.width <= 0 || Screen.height <= 0) return; if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) return; lastScreenWidth = Screen.width; lastScreenHeight = Screen.height; var doc = GetComponent<UIDocument>(); var settings = doc != null ? doc.panelSettings : null; if (settings == null || settings.scaleMode != PanelScaleMode.ConstantPixelSize) return; settings.scale = TerminalScale.ScaleFor(Screen.width, Screen.height, DisplaySettings.ScaleMultiplier); }
        void ApplyDisplaySettings() { foreach (var theme in DisplaySettings.AllThemeClasses) root.EnableInClassList(theme, theme == DisplaySettings.ThemeClass); if (scanlines != null) scanlines.style.display = DisplaySettings.Atmosphere ? DisplayStyle.Flex : DisplayStyle.None; ApplyTextPolicy(root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns); ApplyPanelScale(force: true); }
        void BuildScanlines() { scanlines = new VisualElement(); scanlines.AddToClassList("scanline-overlay"); scanlines.pickingMode = PickingMode.Ignore; scanlines.style.overflow = Overflow.Hidden; for (int i = 0; i < 220; i++) { var line = new VisualElement(); line.AddToClassList(i % 2 == 0 ? "scanline-row" : "scanline-gap"); line.pickingMode = PickingMode.Ignore; scanlines.Add(line); } root.Insert(0, scanlines); }
        public static bool IsReadout(Label label) => label.ClassListContains("terminal-text") || label.ClassListContains("terminal-text-dim") || label.ClassListContains("terminal-text-bright");
        public static void ApplyTextPolicy(VisualElement element, float spacing, int columns) { if (element == null) return; if (element is Label label && IsReadout(label)) { bool figure = label.ClassListContains("terminal-figure"); label.style.unityParagraphSpacing = figure ? 0f : spacing; if (!figure && !string.IsNullOrEmpty(label.text)) label.text = AsciiChart.WrapBlock(label.text, columns); } foreach (var child in element.Children()) ApplyTextPolicy(child, spacing, columns); }
        void ApplyShortScreenClass() { bool isShort = root.resolvedStyle.height > 0f && root.resolvedStyle.height < TerminalMetrics.ShortScreenHeight; root.EnableInClassList("bp-short", isShort); }
        void UpdateMetrics() { if (measureProbe == null || contentHost == null) return; float contentWidth = contentHost.resolvedStyle.width; if (float.IsNaN(contentWidth) || contentWidth <= 0f) return; float pl = contentHost.resolvedStyle.paddingLeft, pr = contentHost.resolvedStyle.paddingRight; if (!float.IsNaN(pl)) contentWidth -= pl; if (!float.IsNaN(pr)) contentWidth -= pr; const float fallback = 18f; var viewRoot = activeView?.Root; float chrome = fallback; if (viewRoot != null) { float border = viewRoot.resolvedStyle.borderLeftWidth + viewRoot.resolvedStyle.borderRightWidth; float padding = viewRoot.resolvedStyle.paddingLeft + viewRoot.resolvedStyle.paddingRight; if (!float.IsNaN(border) && !float.IsNaN(padding)) chrome = border + padding; } contentWidth -= chrome; const int sample = 40; var measured = measureProbe.MeasureTextSize(new string('M', sample), 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined); float charWidth = measured.x / sample; if (float.IsNaN(charWidth) || charWidth <= .01f) { float fs = measureProbe.resolvedStyle.fontSize; charWidth = (float.IsNaN(fs) || fs <= 0 ? 13f : fs) * .6f; } int columns = (int)(contentWidth / charWidth) - 1; ApplySizeClass(Breakpoints.FromColumns(columns)); if (TerminalMetrics.Update(contentWidth, charWidth, root.resolvedStyle.height, sizeClass)) RefreshAll(); }
        void ApplyNavLabels() { bool full = sizeClass == SizeClass.Large; var items = GameController.Instance.IsRunning ? AttentionSystem.Collect(GameController.Instance.State) : new List<AttentionItem>(); foreach (var view in views) { var level = AttentionSystem.LevelFor(items, view.Id); var button = navButtons[view.Id]; string marker = level == AttentionLevel.Decision ? "!" : level == AttentionLevel.Information ? "." : ""; string label = full ? view.Id : view.ShortCode; button.text = marker.Length == 0 ? label : full ? $"{label} {marker}" : $"{label}{marker}"; button.EnableInClassList("nav-attention-decision", level == AttentionLevel.Decision); button.EnableInClassList("nav-attention-info", level == AttentionLevel.Information); } }
        void ApplySafeArea() { if (Screen.width <= 0 || Screen.height <= 0) return; var safe = Screen.safeArea; float sx = root.resolvedStyle.width / Screen.width, sy = root.resolvedStyle.height / Screen.height; const float pad = 6f; root.style.paddingLeft = pad + safe.xMin * sx; root.style.paddingRight = pad + (Screen.width - safe.xMax) * sx; root.style.paddingTop = pad + (Screen.height - safe.yMax) * sy; root.style.paddingBottom = pad + safe.yMin * sy; }
    }
}

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
        Label crisisTitle;
        Label crisisBody;
        VisualElement crisisOptions;
        VisualElement scanlines;

        /// <summary>Off-screen label used only to measure the monospace advance width.</summary>
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
            crisisTitle = root.Q<Label>("crisis-title");
            crisisBody = root.Q<Label>("crisis-body");
            crisisOptions = root.Q<VisualElement>("crisis-options");

            crisisIndicator = new Label("■ CRISIS");
            crisisIndicator.AddToClassList("status-crisis");
            var statusRight = root.Q<VisualElement>(className: "status-right");
            statusRight?.Insert(0, crisisIndicator);

            var endMonth = root.Q<Button>("end-month-button");
            if (endMonth != null)
                endMonth.clicked += () =>
                {
                    if (!GameController.Instance.EndMonth()) return;

                    // **Refresh before showing the briefing.** `EndMonth` does not
                    // raise `StateReplaced`, and the log handler only touches the
                    // status bar and the crisis overlay — so nothing called
                    // `activeView.Refresh()` on this path. The screen behind the
                    // briefing kept last month's numbers until the operator
                    // happened to tap a nav button, which reads as the game not
                    // having resolved the turn.
                    RefreshAll();
                    ShowMonthlyBriefing();
                };

            // Display preferences ship in every build — unlike the debug console.
            settingsPanel = new DisplaySettingsPanel(ApplyDisplaySettingsAndRefresh);
            var displayButton = new Button(() => { settingsPanel.Toggle(); }) { text = "DSP" };
            displayButton.AddToClassList("cmd-button");
            statusRight?.Insert(0, displayButton);

            // Measures the terminal font's advance width. Kept in the tree so it
            // inherits the same styling the views use, but never visible.
            measureProbe = new Label("M");
            measureProbe.AddToClassList("terminal-text");
            measureProbe.style.position = Position.Absolute;
            measureProbe.style.opacity = 0f;
            measureProbe.pickingMode = PickingMode.Ignore;
            contentHost.Add(measureProbe);

            contentHost.Add(settingsPanel.Root);

            BuildScanlines();
            BuildViews();

            root.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
            GameLog.OnLog += OnAnyLog;
            GameController.Instance.StateReplaced += RefreshAll;

            tutorialPanel = new TutorialPanel(RefreshAll);
            contentHost.Add(tutorialPanel.Root);

            assessmentScreen = new AssessmentScreen(() =>
            {
                UpdateSessionMode();
                SelectView("BRIEFING");
                RefreshAll();
            });
            contentHost.Add(assessmentScreen.Root);

            GameController.Instance.EnsureStarted();
            UpdateSessionMode();
            SelectView("BRIEFING");
            ApplyDisplaySettings();
            RefreshAll();

            classificationLabel.schedule.Execute(BlinkCursor).Every(530);
            root.schedule.Execute(PulseEndMonth).Every(700);
        }

        void OnDisable()
        {
            GameLog.OnLog -= OnAnyLog;
            GameController.Instance.StateReplaced -= RefreshAll;
        }

        void BuildViews()
        {
            views.Add(new BriefingView());
            // Second in the rail deliberately: "what can I do?" is the question a
            // new operator has after the briefing, and it should be the next
            // thing their eye lands on.
            views.Add(new ActionsView());
            views.Add(new WorldMapView());
            views.Add(new CabinetView());
            views.Add(new MilitaryView());
            views.Add(new EconomyView());
            views.Add(new IntelligenceView());
            views.Add(new DiplomacyView());
            views.Add(new GovernmentView());
            views.Add(new TechnologyView());
            views.Add(new EndgameView());
            views.Add(new StrategistView());
            views.Add(new ChronicleView());

            // The SYSTEM console carries save/load slots, FORCE CRISIS, month
            // skipping and live difficulty toggles. Shipping it would hand the
            // player exactly the "reload the turn I disliked" loop GDD §30
            // forbids, so it exists only in the editor and development builds.
            if (Debug.isDebugBuild || Application.isEditor)
            {
                views.Add(new SystemView());

                // Temporary bench for the audio foundation. Ships beside SYSTEM
                // and disappears with it in a player build.
                views.Add(new AudioDebugView());
            }

            foreach (var view in views)
            {
                contentHost.Add(view.Root);
                var captured = view;
                var button = new Button(() => SelectView(captured.Id)) { text = captured.Id };
                button.AddToClassList("nav-button");
                navRail.Add(button);
                navButtons[view.Id] = button;
            }
        }

        void SelectView(string id)
        {
            if (GameController.Instance.AwaitingAssessment) return;
            foreach (var view in views)
            {
                bool isTarget = view.Id == id;
                view.SetVisible(isTarget);
                if (isTarget) activeView = view;
                navButtons[view.Id].EnableInClassList("nav-button-active", isTarget);
            }
        }

        /// <summary>
        /// Show either the first-launch assessment or the command terminal
        /// (GDD §5). A full reset returns here.
        /// </summary>
        void UpdateSessionMode()
        {
            bool awaiting = GameController.Instance.AwaitingAssessment;

            assessmentScreen.Root.style.display = awaiting ? DisplayStyle.Flex : DisplayStyle.None;
            navRail.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex;

            // **Orientation has nothing to say during the assessment.**
            //
            // `RefreshAll` returns early while awaiting it, so `TutorialPanel.Refresh`
            // never ran — and the panel, created visible and never populated, sat
            // on top of the questionnaire as an empty green box with two buttons
            // and no text, eating a third of a landscape phone's height.
            //
            // Hiding it here rather than relying on Refresh is deliberate: the
            // panel is only *hidden* by a call this path skips, so leaving it to
            // Refresh is what produced the bug. The assessment is itself the
            // onboarding — orientation begins once there is a terminal to orient
            // somebody around.
            if (tutorialPanel != null)
                tutorialPanel.Root.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex;

            var endMonth = root.Q<Button>("end-month-button");
            if (endMonth != null) endMonth.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex;
            dateLabel.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex;
            cpLabel.style.display = awaiting ? DisplayStyle.None : DisplayStyle.Flex;

            if (awaiting)
            {
                foreach (var view in views) view.SetVisible(false);
                assessmentScreen.Restart();
            }
        }

        void RefreshAll()
        {
            if (GameController.Instance.AwaitingAssessment)
            {
                UpdateSessionMode();
                return;
            }
            UpdateStatusBar();
            UpdateCrisisOverlay();
            tutorialPanel?.Refresh();
            activeView?.Refresh();

            // Views rebuild their labels on every refresh, so leading has to be
            // re-applied to the new ones.
            //
            // Walk `root`, not `contentHost`. The overlays — the monthly
            // briefing, the crisis modal, the tutorial panel — are siblings of
            // the content host, not children of it, so a policy applied to the
            // content host alone never reached a single line of any of them. The
            // briefing's wire lines ran off the right edge for exactly this
            // reason.
            ApplyTextPolicy(root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);
        }

        void ApplyDisplaySettingsAndRefresh()
        {
            ApplyDisplaySettings();
            UpdateMetrics();
            RefreshAll();
        }

        void OnAnyLog(LogEntry entry)
        {
            UpdateStatusBar();
            UpdateCrisisOverlay();
        }

        void UpdateStatusBar()
        {
            var gc = GameController.Instance;
            if (!gc.IsRunning)
            {
                // Through BannerText, or this writes the full-length banner back
                // over the shortened one every time anything is logged.
                classificationLabel.text = BannerText();
                return;
            }
            dateLabel.text = gc.State.date.DisplayString;

            // "MONTH SPENT" is eleven characters that the pulsing END MONTH
            // button already communicates, and on a narrow bar those columns are
            // the difference between the resources fitting and not.
            bool spent = gc.State.CommandCapacitySpent;
            bool roomy = sizeClass == SizeClass.Large;
            cpLabel.text = spent && roomy
                ? $"CP 0 — MONTH SPENT   INF {gc.State.influence}  PC {gc.State.politicalCapital:F0}"
                : $"CP {gc.State.commandPoints.current}  INF {gc.State.influence}  PC {gc.State.politicalCapital:F0}";

            // The readout and the pulsing button should say the same thing.
            cpLabel.EnableInClassList("status-stat-spent", spent);

            crisisIndicator.style.display =
                gc.State.HasOpenCrisis ? DisplayStyle.Flex : DisplayStyle.None;

            UpdateAttention(gc.State);
        }

        /// <summary>
        /// Mark the panels that have something waiting on them (GDD §28.1).
        ///
        /// The whole problem in a game made of text is that a consequence
        /// nobody was told about is indistinguishable from a bug. So every panel
        /// carries its own marker, and END MONTH says how many decisions are
        /// still open.
        ///
        /// **Nothing here refuses anything.** The operator is running a
        /// government and is entitled to ignore a warning and live with it. The
        /// job is to make sure that when they do, it was a choice.
        /// </summary>
        void UpdateAttention(GameState state)
        {
            // Markers are applied in ApplyNavLabels, which owns the label form
            // (full name versus short code). Two writers for one label is how
            // the short code got clobbered.
            ApplyNavLabels();

            var items = AttentionSystem.Collect(state);
            var endMonth = root.Q<Button>("end-month-button");
            if (endMonth == null) return;

            // Short, because this lives in the status bar next to the
            // classification, the date and three resource readouts. The long form
            // ("END MONTH — 3 UNANSWERED") pushed the whole row off the right of
            // a landscape phone. The count plus the border carries the meaning;
            // the briefing spells it out.
            int decisions = AttentionSystem.DecisionCount(items);
            endMonth.text = decisions > 0 ? $"END MONTH ({decisions})" : "END MONTH";
            endMonth.EnableInClassList("end-month-unanswered", decisions > 0);
        }

        /// <summary>
        /// The monthly briefing (GDD §28).
        ///
        /// Three sections, in the order the operator needs them: **what the world
        /// saw**, **what is waiting on this desk**, and **what the Cabinet thinks
        /// is worth doing**. It ends by pointing back into the game rather than
        /// simply closing, which is the difference between a summary and a
        /// starting point.
        ///
        /// The simulation was already running sixteen countries through
        /// elections, coups, wars and treaties every month and almost none of it
        /// reached the player, because the briefing only ever showed what
        /// happened to us. The world was alive and invisible.
        ///
        /// Always dismissable, and switchable off entirely — off, the same
        /// material is still on the BRIEFING panel. Nothing is lost but the
        /// interruption.
        /// </summary>
        void ShowMonthlyBriefing()
        {
            if (!DisplaySettings.MonthlyBriefing) return;

            var gc = GameController.Instance;
            if (!gc.IsRunning) return;

            var overlay = root.Q<VisualElement>("rollover-overlay");
            var body = root.Q<ScrollView>("rollover-body");
            var actions = root.Q<VisualElement>("rollover-actions");
            var title = root.Q<Label>("rollover-title");
            if (overlay == null || body == null || actions == null) return;

            var state = gc.State;
            body.Clear();
            actions.Clear();

            if (title != null) title.text = $"MONTHLY BRIEFING — {state.date.DisplayString}";

            void Section(string heading)
            {
                var label = new Label(heading);
                label.AddToClassList("terminal-text-bright");
                body.Add(label);
            }

            void Line(string text, string ussClass = "terminal-text")
            {
                var label = new Label(text);

                // **Always `terminal-text` as well as the signal class.** This
                // added only the one class, so a line built as `sig-rival` failed
                // `IsReadout` and escaped `ApplyTextPolicy` entirely — while a
                // plain line carried `white-space: pre` and could not soft-wrap
                // either. That is why *some* words ran off the edge and others did
                // not: two different failure modes on adjacent lines of the same
                // briefing. Every other builder in the shell adds both.
                label.AddToClassList("terminal-text");
                if (ussClass != "terminal-text") label.AddToClassList(ussClass);
                body.Add(label);
            }

            // ---- what the world saw ----
            if (DisplaySettings.WorldWire)
            {
                var wire = WorldWire.LastMonth(state);
                Section(" WORLD WIRE");
                if (wire.Count == 0)
                    Line("  A quiet month. Nothing of note reached the wire.", "terminal-text-dim");
                else
                    foreach (var item in wire)
                        Line("  " + WorldWire.Format(state, item),
                            item.involvesUs ? "sig-rival" : "terminal-text");
            }

            // ---- what is waiting on us ----
            var attention = AttentionSystem.Collect(state);
            Section("\n YOUR DESK");
            if (attention.Count == 0)
                Line("  Nothing is waiting on you.", "terminal-text-dim");
            else
                foreach (var item in attention)
                    Line($"  {(item.level == AttentionLevel.Decision ? "!" : ".")} " +
                         $"[{item.viewId}] {item.summary}",
                        item.level == AttentionLevel.Decision ? "sig-hostile" : "terminal-text-dim");

            // ---- what our own government did without us ----
            //
            // The other half of delegation. An official who acts in the
            // operator's name and never reports is indistinguishable from a
            // pillar that has been switched off, which is not what handing
            // something to a competent person should feel like.
            if (state.cabinetReport.Count > 0)
            {
                Section("\n YOUR CABINET");
                foreach (var line in state.cabinetReport)
                    Line($"  {line.pillar.ToString().ToUpperInvariant()} — "
                         + $"{line.officialName} {line.summary}",
                        line.ownJudgement ? "terminal-text" : "terminal-text-dim");
            }

            // ---- what is worth doing ----
            var directives = DirectiveSystem.Collect(state);
            if (directives.Count > 0)
            {
                Section("\n CABINET RECOMMENDS");
                foreach (var directive in directives)
                {
                    Line($"  ▸ {directive.title.ToUpperInvariant()}   → {directive.viewId}");
                    Line($"     {directive.suggestion}", "terminal-text-dim");
                }
            }

            var dismiss = new Button(() => overlay.style.display = DisplayStyle.None)
            { text = "CONTINUE" };
            dismiss.AddToClassList("cmd-button");
            dismiss.AddToClassList("primary");
            actions.Add(dismiss);

            // Built outside any shell refresh, so it wraps itself. Every wire
            // line here names a country and runs long.
            //
            // **OverlayColumns, not Columns.** This panel is 80% of the content
            // width and is not a child of the content host, so wrapping to the
            // full width ran a fifth of every long line off the screen.
            ApplyTextPolicy(overlay, DisplaySettings.ParagraphSpacing, TerminalMetrics.OverlayColumns);

            overlay.style.display = DisplayStyle.Flex;
        }

        /// <summary>Crisis Turn interrupt (GDD §6): modal decision panel over the shell.</summary>
        void UpdateCrisisOverlay()
        {
            var gc = GameController.Instance;
            var crises = gc.IsRunning ? gc.State.activeCrises : null;
            if (crisisOverlay == null) return;
            bool active = crises != null && crises.Count > 0;
            crisisOverlay.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (!active) return;

            var crisis = crises[0];
            crisisTitle.text = crisis.title;
            crisisBody.text = crisis.body;

            crisisOptions.Clear();
            for (int i = 0; i < crisis.options.Count; i++)
            {
                var option = crisis.options[i];
                int index = i;
                var button = new Button(() =>
                {
                    GameController.Instance.ResolveCrisis(crisis, index);
                    RefreshAll();
                })
                { text = $"{i + 1}. {option.label}" };
                button.AddToClassList("crisis-option-button");
                crisisOptions.Add(button);

                var hint = new Label(option.description);
                hint.AddToClassList("crisis-option-hint");
                crisisOptions.Add(hint);
            }

            // An overlay built outside a shell refresh has to wrap itself. The
            // body and the option hints are the longest prose in the game.
            ApplyTextPolicy(crisisOverlay, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);
        }

        /// <summary>
        /// Draw the eye to END MONTH once the month's command capacity is spent.
        ///
        /// A player with no Command Points left has no way of knowing that from
        /// the screen — every button simply stops working, which reads as the
        /// game breaking rather than the month being over. Buttons now dim
        /// individually (see `TerminalView.GateOnAffordability`); this is the
        /// positive half of the same message: here is the thing to press.
        /// </summary>
        void PulseEndMonth()
        {
            var endMonth = root.Q<Button>("end-month-button");
            if (endMonth == null) return;

            var gc = GameController.Instance;
            bool spent = gc.IsRunning
                         && !gc.AwaitingAssessment
                         && gc.State.CommandCapacitySpent;

            endMonth.EnableInClassList("end-month-spent", spent);
            if (!spent)
            {
                endMonth.RemoveFromClassList("end-month-spent-on");
                pulseOn = false;
                return;
            }

            pulseOn = !pulseOn;
            endMonth.EnableInClassList("end-month-spent-on", pulseOn);
        }

        /// <summary>
        /// The classification banner, sized to the panel it has to share.
        ///
        /// The status bar carries the banner, the display toggle, the date,
        /// three resource readouts and END MONTH on one row. At full length the
        /// banner is the single widest element, and on a landscape phone it
        /// pushed everything to its right off the edge of the screen — the date
        /// and the turn control among them.
        ///
        /// So the flavour gives way to the instrument, in stages: the full
        /// banner on a large panel, a shortened one below that, and on a cover
        /// display just the cursor. Clipping (see `.classification` in USS) is
        /// the backstop, not the plan.
        /// </summary>
        public static string BannerFor(SizeClass size, bool awaiting)
        {
            switch (size)
            {
                case SizeClass.Large:
                    return awaiting
                        ? "STRATEGIC APTITUDE ASSESSMENT // CLASSIFIED"
                        : "UNKNOWN GAME // CLASSIFIED";
                case SizeClass.Medium:
                    return awaiting ? "ASSESSMENT // CLASSIFIED" : "CLASSIFIED";
                default:
                    // Compact and short screens: the readouts need every column.
                    return string.Empty;
            }
        }

        string BannerText()
            => BannerFor(sizeClass, GameController.Instance.AwaitingAssessment);

        void BlinkCursor()
        {
            cursorOn = !cursorOn;

            // The block and the space have the same advance in a monospace font,
            // so the cursor blinks without the banner reflowing around it.
            classificationLabel.text = BannerText() + (cursorOn ? "█" : " ");
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            float width = root.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0) return;

            ApplyPanelScale();
            ApplySafeArea();

            // Columns must be measured before the size class, because the class
            // is now expressed in characters across rather than panel points.
            UpdateMetrics();
            ApplyShortScreenClass();
        }

        void ApplySizeClass(SizeClass newSize)
        {
            if (newSize != sizeClass || !root.ClassListContains(Breakpoints.ToUssClass(newSize)))
            {
                root.RemoveFromClassList(Breakpoints.ToUssClass(sizeClass));
                sizeClass = newSize;
                root.AddToClassList(Breakpoints.ToUssClass(sizeClass));
                ApplyNavLabels();

                // The banner length is chosen by size class, so folding a phone
                // open or closed has to re-pick it. Left to the blink tick it
                // would be up to half a second stale — long enough to see the
                // bar overflow and then correct itself.
                classificationLabel.text = BannerText() + (cursorOn ? "█" : " ");
                UpdateStatusBar();
            }
        }

        /// <summary>
        /// Keep the panel scale matched to the current resolution. Folding a
        /// phone open or closed changes the screen without restarting the app,
        /// so a scale computed once at boot would be wrong for the rest of the
        /// session.
        /// </summary>
        void ApplyPanelScale(bool force = false)
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            if (!force && Screen.width == lastScreenWidth && Screen.height == lastScreenHeight) return;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;

            var document = GetComponent<UIDocument>();
            var settings = document != null ? document.panelSettings : null;
            if (settings == null || settings.scaleMode != PanelScaleMode.ConstantPixelSize) return;

            settings.scale = TerminalScale.ScaleFor(
                Screen.width, Screen.height, DisplaySettings.ScaleMultiplier);
        }

        /// <summary>
        /// Apply the operator's palette and line spacing.
        ///
        /// Leading is set here rather than in the stylesheet because
        /// `unityParagraphSpacing` in code is a compile-time guarantee — a USS
        /// property the parser did not recognise would fail silently, and a
        /// silently-ignored readability setting is worse than none.
        /// </summary>
        void ApplyDisplaySettings()
        {
            foreach (var themeClass in DisplaySettings.AllThemeClasses)
                root.EnableInClassList(themeClass, themeClass == DisplaySettings.ThemeClass);

            if (scanlines != null)
                scanlines.style.display = DisplaySettings.Atmosphere
                    ? DisplayStyle.Flex : DisplayStyle.None;

            ApplyTextPolicy(root, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);
            ApplyPanelScale(force: true);
        }

        /// <summary>
        /// The scanline field (GDD §28 presentation).
        ///
        /// **Inserted at index 0, so it sits behind everything.** Contrast in this
        /// project is a property of the text colours against the panel background,
        /// and every AAA guarantee would be void if an overlay were drawn *over*
        /// the glyphs. Darkening rows behind the content leaves the foreground
        /// untouched and only deepens the background, which is the safe direction
        /// — a lightening overlay would eat the headroom on `terminal-text-dim`,
        /// where the margin is thinnest.
        ///
        /// `PickingMode.Ignore` is load-bearing rather than tidy: this covers the
        /// whole shell, and without it every tap on the terminal would land here
        /// instead.
        ///
        /// Built once at a fixed count rather than rebuilt per resize. 220 rows of
        /// 2px line plus 2px gap covers ~880pt, taller than any handset in
        /// landscape, and the overlay clips what it does not need. Rebuilding this
        /// on every `GeometryChangedEvent` would mean hundreds of element
        /// allocations during a rotation.
        /// </summary>
        void BuildScanlines()
        {
            scanlines = new VisualElement();
            scanlines.AddToClassList("scanline-overlay");
            scanlines.pickingMode = PickingMode.Ignore;
            scanlines.style.overflow = Overflow.Hidden;

            for (int i = 0; i < 220; i++)
            {
                var line = new VisualElement();
                line.AddToClassList(i % 2 == 0 ? "scanline-row" : "scanline-gap");
                line.pickingMode = PickingMode.Ignore;
                scanlines.Add(line);
            }

            root.Insert(0, scanlines);
        }

        /// <summary>
        /// Walk the view tree applying the two things that have to be true of
        /// every block of terminal text, wherever it came from:
        ///
        /// 1. **It fits.** Under `white-space: pre` nothing wraps, so an
        ///    over-long line runs off the right edge. Sizing each view's boxes to
        ///    the panel was not enough — prose written into a notification or a
        ///    hint is arbitrary length. Doing it here rather than at each call
        ///    site means a view added later cannot forget.
        /// 2. **It has leading**, unless it is an ASCII figure, where a gap
        ///    between rows would break the vertical strokes that make the picture.
        /// </summary>
        /// <summary>
        /// Every class that marks a label as prose the shell must wrap.
        ///
        /// This used to test `terminal-text` alone, which quietly excluded
        /// `terminal-text-dim` and `terminal-text-bright` — so any label built
        /// with only a dim or bright class ran straight off the right edge. The
        /// briefing's attention note, the Cabinet's rationale lines and its
        /// closing note were all built that way and all overflowed on device.
        ///
        /// The trap is that the *dim* variant is exactly what secondary
        /// explanation uses, and secondary explanation is the longest prose in
        /// the game. Adding the base class at each call site would have fixed the
        /// three known cases and left the trap armed for the next one.
        /// </summary>
        public static bool IsReadout(Label label)
            => label.ClassListContains("terminal-text")
               || label.ClassListContains("terminal-text-dim")
               || label.ClassListContains("terminal-text-bright");

        public static void ApplyTextPolicy(VisualElement element, float spacing, int columns)
        {
            if (element == null) return;

            if (element is Label label && IsReadout(label))
            {
                bool isFigure = label.ClassListContains("terminal-figure");
                label.style.unityParagraphSpacing = isFigure ? 0f : spacing;

                // Figures are already built to the exact grid they were given;
                // wrapping one would corrupt it.
                if (!isFigure && !string.IsNullOrEmpty(label.text))
                    label.text = AsciiChart.WrapBlock(label.text, columns);
            }

            foreach (var child in element.Children())
                ApplyTextPolicy(child, spacing, columns);
        }

        /// <summary>
        /// A folding phone's cover display is wide and very short. There, height
        /// is the scarce resource: the nav rail goes back to being a narrow
        /// vertical strip (spending abundant width) instead of a horizontal band
        /// across the top (spending scarce height).
        /// </summary>
        void ApplyShortScreenClass()
        {
            bool isShort = root.resolvedStyle.height > 0f
                           && root.resolvedStyle.height < TerminalMetrics.ShortScreenHeight;
            root.EnableInClassList("bp-short", isShort);
        }

        /// <summary>
        /// Measure how many characters actually fit and let the views rebuild to
        /// that width. Measured rather than assumed, because the effective font
        /// size changes with the breakpoint and the panel scales with device DPI.
        /// </summary>
        void UpdateMetrics()
        {
            if (measureProbe == null || contentHost == null) return;

            float contentWidth = contentHost.resolvedStyle.width;
            if (float.IsNaN(contentWidth) || contentWidth <= 0f) return;

            float padLeft = contentHost.resolvedStyle.paddingLeft;
            float padRight = contentHost.resolvedStyle.paddingRight;
            if (!float.IsNaN(padLeft)) contentWidth -= padLeft;
            if (!float.IsNaN(padRight)) contentWidth -= padRight;

            const int sample = 40;
            var measured = measureProbe.MeasureTextSize(
                new string('M', sample), 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);

            float charWidth = measured.x / sample;
            if (float.IsNaN(charWidth) || charWidth <= 0.01f)
            {
                // JetBrains Mono advances 0.6em; good enough if measurement fails.
                float fontSize = measureProbe.resolvedStyle.fontSize;
                charWidth = (float.IsNaN(fontSize) || fontSize <= 0f ? 13f : fontSize) * 0.6f;
            }

            // Derive the size class from the columns we just measured, then let
            // the metrics carry it. Points were the wrong unit: the panel scale
            // is chosen to hit a column target, so panel width in points is
            // roughly constant whatever the device, and the wider classes were
            // simply unreachable.
            int columns = (int)(contentWidth / charWidth) - 1;
            ApplySizeClass(Breakpoints.FromColumns(columns));

            if (TerminalMetrics.Update(contentWidth, charWidth, root.resolvedStyle.height, sizeClass))
                RefreshAll();
        }

        /// <summary>
        /// Nav labels: full names on a large panel, three-letter codes below it.
        ///
        /// The attention markers are appended here rather than anywhere else,
        /// because this is the only place that knows which form the label is in.
        /// Writing the label from two places meant the marker pass overwrote the
        /// short code with the full name on every refresh — which on a folding
        /// phone's cover screen puts "INTELLIGENCE" into a 52-pixel rail.
        /// </summary>
        void ApplyNavLabels()
        {
            bool full = sizeClass == SizeClass.Large;
            var items = GameController.Instance.IsRunning
                ? AttentionSystem.Collect(GameController.Instance.State)
                : new System.Collections.Generic.List<AttentionItem>();

            foreach (var view in views)
            {
                var level = AttentionSystem.LevelFor(items, view.Id);
                var button = navButtons[view.Id];

                // The glyph carries the meaning; colour only reinforces it, so
                // the reading survives any palette and any colour vision.
                string marker = level == AttentionLevel.Decision ? "!"
                    : level == AttentionLevel.Information ? "." : "";

                // No space before the marker on a narrow rail — three characters
                // plus a space plus a glyph is what overflows 52 pixels.
                string label = full ? view.Id : view.ShortCode;
                button.text = marker.Length == 0 ? label
                    : full ? $"{label} {marker}" : $"{label}{marker}";

                button.EnableInClassList("nav-attention-decision", level == AttentionLevel.Decision);
                button.EnableInClassList("nav-attention-info", level == AttentionLevel.Information);
            }
        }

        /// <summary>Pad the shell so content clears notches/cutouts on landscape phones.</summary>
        void ApplySafeArea()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var safe = Screen.safeArea;
            float scaleX = root.resolvedStyle.width / Screen.width;
            float scaleY = root.resolvedStyle.height / Screen.height;
            const float basePad = 6f;

            root.style.paddingLeft = basePad + safe.xMin * scaleX;
            root.style.paddingRight = basePad + (Screen.width - safe.xMax) * scaleX;
            root.style.paddingTop = basePad + (Screen.height - safe.yMax) * scaleY;
            root.style.paddingBottom = basePad + safe.yMin * scaleY;
        }
    }
}

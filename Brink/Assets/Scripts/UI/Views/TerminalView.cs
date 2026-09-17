using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// One screen of the command terminal (GDD §28.1 information hierarchy).
    /// Views build their own element tree and refresh from GameController state.
    /// </summary>
    public abstract class TerminalView
    {
        /// <summary>Full nav label, e.g. "INTELLIGENCE".</summary>
        public abstract string Id { get; }

        /// <summary>Three-letter nav code for compact layouts, e.g. "INT".</summary>
        public abstract string ShortCode { get; }

        public VisualElement Root { get; }

        protected TerminalView()
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                // Terminal aesthetic: no scrollbar chrome; wheel/touch scrolling still works.
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                horizontalScrollerVisibility = ScrollerVisibility.Hidden
            };
            Root = scroll;
            Root.AddToClassList("view-scroll");
            Root.style.display = DisplayStyle.None;
        }

        public void SetVisible(bool visible)
        {
            Root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (visible) Refresh();
        }

        /// <summary>
        /// Rebuild the view and then apply the rules that must hold of every
        /// block of terminal text.
        ///
        /// This is deliberately not virtual. The formatting used to be applied by
        /// the shell after `RefreshAll`, but views rebuild themselves from ~60
        /// button handlers and from `SetVisible`, and every one of those paths
        /// skipped it — so a screen reached by tapping a tab, or by pressing a
        /// button, kept its unwrapped text and ran off the right edge.
        /// </summary>
        public void Refresh()
        {
            Build();
            FormatText(Root);
            GateOnAffordability(Root);
            GateOnAuthority(Root, CommandPillar);
            ExplainBlockedCommands(Root);
        }

        /// <summary>
        /// Print, under each row of commands, why any of them are refused.
        ///
        /// The refusal itself is carried on the button by <see cref="Block"/>,
        /// but a greyed button on a phone says only "no" — there is no hover, so
        /// the tooltip explaining it may as well not exist. An absence has to say
        /// what would change it, the same rule that turned a bare "NO ASSESSMENT"
        /// into UNTASKED / COLLECTING / BURNED.
        ///
        /// Done here rather than in each panel so a view added later cannot
        /// forget, and after both gates so it reports the reason that actually
        /// stuck.
        /// </summary>
        public static void ExplainBlockedCommands(VisualElement element)
        {
            if (element == null) return;

            // Snapshot: we insert siblings while walking.
            var rows = new System.Collections.Generic.List<VisualElement>();
            Collect(element);

            foreach (var row in rows)
            {
                var reasons = new System.Collections.Generic.List<string>();
                foreach (var child in row.Children())
                {
                    string reason = BlockedReason(child as Button);
                    if (reason != null && !reasons.Contains(reason)) reasons.Add(reason);
                }
                if (reasons.Count == 0) continue;

                var parent = row.parent;
                if (parent == null) continue;

                var note = new Label();
                note.AddToClassList("terminal-text");
                note.AddToClassList("terminal-text-dim");
                note.text = AsciiChart.WrapBlock(
                    "   UNAVAILABLE: " + string.Join("  ", reasons), TerminalMetrics.Columns);
                parent.Insert(parent.IndexOf(row) + 1, note);
            }

            void Collect(VisualElement node)
            {
                if (node.ClassListContains("button-row")) rows.Add(node);
                foreach (var child in node.Children()) Collect(child);
            }
        }

        /// <summary>
        /// The pillar this view issues orders in, or null for a readout.
        ///
        /// Declared here so `Refresh` can gate on it and a view added later
        /// cannot forget — the same reasoning that put the text policy and the
        /// affordability gate in one place.
        /// </summary>
        protected virtual Pillar? CommandPillar => null;

        /// <summary>
        /// Disable commands the constitution does not put in the operator's hands
        /// (GDD §3).
        ///
        /// **These buttons used to look live and do nothing.** Under a
        /// ParliamentaryRepublic the Government pillar is `AdvisoryOnly`, so
        /// `AuthoritySystem.EnsureAuthority` returned false and the press spent
        /// nothing, changed nothing and reported nothing — just a `GameLog.Warn`
        /// the player never sees. Nine of GOVERNMENT's thirteen controls behaved
        /// that way for anyone posted to such a state, and INTELLIGENCE does the
        /// same under a Monarchy. An entire pillar reads as a dead screen, which
        /// is indistinguishable from the game being broken.
        ///
        /// `AddAuthorityBadge` explained the situation in words at the top of the
        /// view, which is not the same thing: a paragraph does not stop a button
        /// looking pressable.
        ///
        /// Only `AdvisoryOnly` is disabled. `RequiresApproval` stays live on
        /// purpose — the operator *can* act, it costs Political Capital and the
        /// legislature may refuse, and refusing is a real outcome rather than an
        /// absence of one.
        ///
        /// Uses the same `[N CP]` cost-tag convention as the affordability gate,
        /// so it disables the controls that *spend* and leaves selection and
        /// navigation alone.
        /// </summary>
        public static void GateOnAuthority(VisualElement element, Pillar? pillar)
            => GateOnAuthority(element, pillar,
                GameController.Instance.IsRunning ? GameController.Instance.State : null);

        /// <summary>The authority gate against an explicit state. See the overload above.</summary>
        public static void GateOnAuthority(VisualElement element, Pillar? pillar, GameState state)
        {
            if (element == null || pillar == null || state == null) return;
            if (AuthoritySystem.AuthorityOver(state, pillar.Value)
                != AuthoritySystem.AuthorityLevel.AdvisoryOnly) return;

            Walk(element);

            void Walk(VisualElement node)
            {
                if (node is Button button && !string.IsNullOrEmpty(button.text)
                    && TryReadCost(button.text, out _, out _))
                    Block(button, "Not ours to command under this constitution.");

                foreach (var child in node.Children()) Walk(child);
            }
        }

        /// <summary>
        /// Disable any command the operator cannot currently pay for.
        ///
        /// Costs are already written into every button's label by a strict
        /// convention — `[2 CP]`, `[1 INF]`, `[3 PC]` — so reading them back is
        /// both reliable and automatically correct for buttons added later. This
        /// is an affordance only: `TurnManager.SpendCommandPoints` and the
        /// political-capital checks remain the authority, and a button that slips
        /// through is still refused there.
        ///
        /// Pressing a command you cannot afford previously spent nothing and said
        /// nothing, which reads as a broken control rather than an empty account.
        /// </summary>
        public static void GateOnAffordability(VisualElement element)
            => GateOnAffordability(element,
                GameController.Instance.IsRunning ? GameController.Instance.State : null);

        /// <summary>
        /// The gate against an explicit state, so it can be exercised without a
        /// running session. Taking the state as an argument is also the honest
        /// signature: nothing here needs the controller, only the three balances.
        /// </summary>
        public static void GateOnAffordability(VisualElement element, GameState state)
        {
            if (element == null || state == null) return;

            if (element is Button button && !string.IsNullOrEmpty(button.text))
            {
                if (TryReadCost(button.text, out int amount, out string resource))
                {
                    bool affordable;
                    switch (resource)
                    {
                        case "CP": affordable = state.commandPoints.current >= amount; break;
                        case "INF": affordable = state.influence >= amount; break;
                        case "PC": affordable = state.politicalCapital >= amount; break;
                        default: affordable = true; break;
                    }

                    // Only ever *disables*. `SetEnabled(affordable)` here used to
                    // re-enable everything a view had already blocked for its own
                    // reasons, because this gate runs after `Build`. That is why
                    // CONDUCT EXERCISE stayed bright during its cooldown, why a
                    // patronage button the treasury could not fund stayed
                    // pressable, and why pressing either spent nothing and said
                    // nothing. A view rebuilds its buttons from scratch every
                    // refresh, so there is never anything legitimate to re-enable.
                    if (!affordable) Block(button, $"Requires {amount} {resource}.");
                }
            }

            foreach (var child in element.Children())
                GateOnAffordability(child, state);
        }

        /// <summary>
        /// Refuse a command, and say why — the one way a control is taken away.
        ///
        /// **Three rules, and they are the whole point of routing every refusal
        /// through here.**
        ///
        /// 1. **A blocked button stays blocked.** The reason is recorded on the
        ///    button itself, so a later gate cannot quietly hand it back. Both
        ///    gates run after `Build`, and the affordability one used to call
        ///    `SetEnabled(affordable)` — undoing every precondition a view had
        ///    applied for its own reasons. The operator was then offered a
        ///    control that spent nothing and reported nothing when pressed,
        ///    which is indistinguishable from the game being broken.
        /// 2. **The first reason is the one shown.** Whichever gate refuses
        ///    first has the most specific answer; "requires 2 CP" is a worse
        ///    thing to be told than "we exercised with them last month".
        /// 3. **The reason is legible without hovering.** A tooltip is dead
        ///    weight on a phone, so the reason is also readable back through
        ///    <see cref="BlockedReason"/> and printed by the panels that offer
        ///    the command.
        /// </summary>
        public static void Block(Button button, string reason)
        {
            if (button == null) return;

            // Already refused for a more specific reason — leave it standing.
            if (button.userData is BlockedCommand) return;

            // A gate that refuses without saying why is the thing this replaced.
            // Never leave the operator with a dead control and a blank line.
            if (string.IsNullOrWhiteSpace(reason)) reason = "NOT AVAILABLE.";

            button.userData = new BlockedCommand { reason = reason };
            button.SetEnabled(false);
            button.AddToClassList("cmd-button-unaffordable");
            button.tooltip = reason;
        }

        /// <summary>Why this command is refused, or null if it is available.</summary>
        public static string BlockedReason(Button button)
            => button?.userData is BlockedCommand blocked ? blocked.reason : null;

        /// <summary>Marker carried by a refused command. See <see cref="Block"/>.</summary>
        sealed class BlockedCommand { public string reason; }

        /// <summary>
        /// Parse a trailing `[N CP]` / `[N INF]` / `[N PC]` cost tag.
        /// Public so the parser can be tested directly — a false positive here
        /// disables a command the operator can actually afford.
        /// </summary>
        public static bool TryReadCost(string label, out int amount, out string resource)
        {
            amount = 0;
            resource = null;

            int close = label.LastIndexOf(']');
            if (close < 0) return false;
            int open = label.LastIndexOf('[', close);
            if (open < 0 || close - open < 4) return false;

            string[] parts = label.Substring(open + 1, close - open - 1).Trim().Split(' ');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], out amount)) return false;

            resource = parts[1].ToUpperInvariant();
            return resource == "CP" || resource == "INF" || resource == "PC";
        }

        /// <summary>Build the view's contents. Called by <see cref="Refresh"/>.</summary>
        protected abstract void Build();

        /// <summary>
        /// Wrap readout text to the measured terminal width and give it leading.
        ///
        /// **One implementation, shared with the shell.** This used to be a
        /// second copy of `TerminalShellController.ApplyTextPolicy`, and the two
        /// drifted: the shell's was widened to cover `terminal-text-dim` and
        /// `terminal-text-bright`, and this one was not. Since a view rebuilds
        /// itself on every button press and *this* is the copy that runs then,
        /// every line of secondary explanation in the game ran off the right edge
        /// until something happened to trigger a shell-level refresh — which is
        /// why folding the phone and reopening it appeared to "fix" the text.
        ///
        /// Same class of bug as the monthly system list living in two places.
        /// Do not reintroduce a local copy of this rule.
        /// </summary>
        public static void FormatText(VisualElement element)
            => TerminalShellController.ApplyTextPolicy(
                element, DisplaySettings.ParagraphSpacing, TerminalMetrics.Columns);

        /// <summary>
        /// The official's counsel for this pillar, if they are advising rather
        /// than acting (GDD §7.2, §8).
        ///
        /// A single call so a view added later cannot forget it — the same
        /// reasoning that put the text policy in one place and the monthly system
        /// list in `SimulationPipeline`. Says nothing when the pillar is
        /// delegated, because then the official is acting and the operator reads
        /// about it in the briefing instead.
        /// </summary>
        protected void AddCabinetAdvice(GameState state, Pillar pillar)
        {
            var official = CabinetAdvice.OfficialFor(state, pillar);
            if (official == null) return;

            if (!CabinetAdvice.ShouldAdvise(state, pillar))
            {
                // Say why there is no counsel, or its absence reads as a missing
                // feature rather than as a consequence of delegating.
                AddText("terminal-text-dim").text =
                    $" {official.displayName} is running this pillar. Take DIRECT CONTROL in "
                    + "CABINET for their counsel; otherwise their work is in the monthly briefing.";
                return;
            }

            var advice = CabinetAdvice.For(state, pillar);
            if (advice == null) return;

            AddText("sig-advice").text = " " + CabinetAdvice.Header(state, advice);
            if (advice.reliability < CabinetAdvice.Unreliable / 100f)
                AddText("sig-hostile").text =
                    "   This desk has been wrong before. Weigh it accordingly.";
        }

        /// <summary>
        /// State the operator's writ over this pillar, at the top of its view.
        ///
        /// Authority binds ~21 verbs. Without saying so, a player under a
        /// constitution that withholds a pillar just finds buttons that refuse —
        /// which reads as a broken game rather than as the central premise of
        /// being an operator inside an institution you do not own (GDD §3).
        /// </summary>
        protected void AddAuthorityBadge(GameState state, Pillar pillar)
        {
            switch (AuthoritySystem.AuthorityOver(state, pillar))
            {
                case AuthoritySystem.AuthorityLevel.Direct:
                    return; // The ordinary case needs no explanation.

                case AuthoritySystem.AuthorityLevel.RequiresApproval:
                    AddText(AuthoritySystem.HoldsAuthority(state, pillar)
                            ? "terminal-text-dim" : "sig-rival").text =
                        AuthoritySystem.HoldsAuthority(state, pillar)
                            ? $" AUTHORITY: GRANTED for this administration."
                            : $" AUTHORITY: REQUIRES APPROVAL — {AuthoritySystem.ApprovalCost:F0} PC, " +
                              "and the legislature may refuse.";
                    return;

                default:
                    AddText("sig-hostile").text =
                        $" AUTHORITY: ADVISORY ONLY under a {state.PlayerCountry.government.TypeText}. " +
                        "Direct the official instead, or declare emergency powers.";
                    return;
            }
        }

        /// <summary>
        /// A row of command buttons. `button-row` is what
        /// <see cref="ExplainBlockedCommands"/> looks for, so a row built any
        /// other way will not get its refusals explained.
        /// </summary>
        protected VisualElement MakeRow()
        {
            var row = new VisualElement();
            row.AddToClassList("button-row");
            Root.Add(row);
            return row;
        }

        /// <summary>
        /// A command button in a row.
        ///
        /// **Returns the button** so the caller can refuse it with
        /// <see cref="Block"/>. Six views carried a byte-identical private copy
        /// of this that returned void, which is why a precondition the caller
        /// knew about had nowhere to go.
        /// </summary>
        protected static Button AddButton(VisualElement row, string text, string extraClass,
            System.Action onClick)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("cmd-button");
            if (extraClass != null) button.AddToClassList(extraClass);
            row.Add(button);
            return button;
        }

        protected Label AddText(string ussClass = "terminal-text")
        {
            var label = new Label();
            label.AddToClassList("terminal-text");
            if (ussClass != "terminal-text") label.AddToClassList(ussClass);
            Root.Add(label);
            return label;
        }

        /// <summary>
        /// A block of ASCII art — a map, a chart, a bar. Rendered without the
        /// leading that readout text gets, because a gap between rows breaks the
        /// vertical strokes that make the picture.
        /// </summary>
        protected Label AddFigure(string ussClass = "terminal-text")
        {
            var label = AddText(ussClass);
            label.AddToClassList("terminal-figure");
            return label;
        }

        /// <summary>
        /// The live institutional signature shared by the five command pillars.
        /// It is presentation only: the renderer reads the current save and
        /// writes no state.
        /// </summary>
        protected void AddPillarArt(GameState state, Pillar pillar)
        {
            AddFigure("terminal-text-dim").text =
                AsciiPillarArt.Render(state, pillar, TerminalMetrics.Columns);
        }

        /// <summary>
        /// Which metric's explanation is currently open, and whether its history
        /// is unrolled.
        ///
        /// **UI state, so it lives in the UI** — not in `GameState`, for the same
        /// reason `DisplaySettings` does not: which panel an operator has open is
        /// ergonomics, not a fact about the world, and putting it in the save
        /// would make it a thing that has to migrate. One metric at a time, so an
        /// answer to "why?" cannot itself become the permanent wall of panels
        /// this feature is supposed to replace.
        /// </summary>
        static CausalMetric openExplanation = CausalMetric.None;
        static bool openExplanationHistory;

        /// <summary>Forget any open explanation. Called when the world is replaced.</summary>
        public static void ResetExplanations()
        {
            openExplanation = CausalMetric.None;
            openExplanationHistory = false;
        }

        /// <summary>
        /// The on-demand "WHY?" affordance for one metric (spec 26 §6).
        ///
        /// Any view can call this under any readout it shows; nothing about it
        /// is specific to approval or to any one screen. It renders **nothing**
        /// but a button until asked, which is the whole design brief — an
        /// explanation is something an operator reaches for, not a second
        /// dashboard bolted under the first.
        ///
        /// Everything shown goes through `CausalDisclosure` first, so this is
        /// not a route around the fog: a view calling it on a foreign country
        /// gets what our collection supports and nothing more.
        /// </summary>
        protected void AddWhyPanel(GameState state, params CausalMetric[] metrics)
        {
            if (state == null || metrics == null || metrics.Length == 0) return;

            // One row for the whole readout above, not one row per figure. Four
            // separate WHY? buttons under one panel is the permanent explanation
            // wall this feature exists to avoid.
            var row = MakeRow();
            CausalMetric metric = CausalMetric.None;

            for (int i = 0; i < metrics.Length; i++)
            {
                var candidate = metrics[i];
                if (candidate == CausalMetric.None) continue;
                if (openExplanation == candidate) metric = candidate;

                bool isOpen = openExplanation == candidate;
                AddButton(row, "WHY " + CausalReasons.ShortLabel(candidate), null, () =>
                {
                    openExplanation = isOpen ? CausalMetric.None : candidate;
                    openExplanationHistory = false;
                    Refresh();
                });
            }

            if (metric == CausalMetric.None) return;

            // Derived from the real panel every time. A hardcoded column count
            // here is the END MONTH overflow waiting to happen again.
            int width = TerminalMetrics.Inset();

            var latest = state.causal?.Latest(state.playerCountryId, metric);
            AddFigure().text = CausalExplanation.Render(
                CausalDisclosure.Disclose(state, latest), width);

            if (latest == null) return;

            var historyRow = MakeRow();
            AddButton(historyRow, openExplanationHistory ? "HIDE HISTORY" : "VIEW HISTORY", null, () =>
            {
                openExplanationHistory = !openExplanationHistory;
                Refresh();
            });

            if (!openExplanationHistory) return;

            var records = state.causal.History(state.playerCountryId, metric, 6);
            var disclosed = new List<DisclosedExplanation>();
            for (int i = 0; i < records.Count; i++)
                disclosed.Add(CausalDisclosure.Disclose(state, records[i]));

            AddFigure().text = CausalExplanation.RenderHistory(disclosed, width);
        }
    }
}

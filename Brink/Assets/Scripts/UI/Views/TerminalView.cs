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
        {
            var gc = GameController.Instance;
            if (element == null || pillar == null || !gc.IsRunning) return;
            if (AuthoritySystem.AuthorityOver(gc.State, pillar.Value)
                != AuthoritySystem.AuthorityLevel.AdvisoryOnly) return;

            Walk(element);

            void Walk(VisualElement node)
            {
                if (node is Button button && !string.IsNullOrEmpty(button.text)
                    && TryReadCost(button.text, out _, out _))
                {
                    button.SetEnabled(false);
                    button.AddToClassList("cmd-button-unaffordable");
                    button.tooltip = "Not ours to command under this constitution.";
                }

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
        {
            var gc = GameController.Instance;
            if (element == null || !gc.IsRunning) return;

            if (element is Button button && !string.IsNullOrEmpty(button.text))
            {
                if (TryReadCost(button.text, out int amount, out string resource))
                {
                    bool affordable;
                    switch (resource)
                    {
                        case "CP": affordable = gc.State.commandPoints.current >= amount; break;
                        case "INF": affordable = gc.State.influence >= amount; break;
                        case "PC": affordable = gc.State.politicalCapital >= amount; break;
                        default: affordable = true; break;
                    }

                    button.SetEnabled(affordable);
                    button.EnableInClassList("cmd-button-unaffordable", !affordable);
                    if (!affordable) button.tooltip = $"Requires {amount} {resource}.";
                }
            }

            foreach (var child in element.Children())
                GateOnAffordability(child);
        }

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
    }
}

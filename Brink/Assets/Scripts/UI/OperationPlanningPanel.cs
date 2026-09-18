using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using UnityEngine.UIElements;

namespace Brink.UI
{
    public sealed class OperationPlanningPanel
    {
        readonly VisualElement root;
        readonly Action refresh;
        string selectedLocationId;
        OperationType selectedOperation = OperationType.Assault;
        OperationDomain selectedDomain = OperationDomain.Ground;

        public VisualElement Root => root;

        public OperationPlanningPanel(Action refresh)
        {
            this.refresh = refresh;
            root = new VisualElement { name = "operation-planning-panel" };
        }

        public void Build(GameState state, Confrontation confrontation)
        {
            root.Clear();
            if (state == null || confrontation == null || confrontation.resolved || !confrontation.Involves(state.playerCountryId)) return;

            Add(AsciiChart.BoxHeader("CAMPAIGN PLAN", TerminalMetrics.Columns), "terminal-text-bright");
            var plan = OperationPlanningSystem.For(state, confrontation.id);
            if (plan == null)
            {
                Add(" STAFF PLANNING IS FREE. A PLAN ALONE DOES NOT LAUNCH OPERATIONS, CHANGE ESCALATION, OR SPEND CP.", "terminal-text-dim");
                var title = new TextField("PLAN NAME") { value = "Campaign plan" }; title.AddToClassList("terminal-input"); root.Add(title);
                var row = Row();
                Button create = new Button(() => { OperationPlanningSystem.Create(state, confrontation.id, title.value); refresh?.Invoke(); }) { text = "CREATE CAMPAIGN PLAN" };
                create.AddToClassList("cmd-button"); create.AddToClassList("primary"); row.Add(create);
                return;
            }

            Add(" " + OperationPlanningSystem.StatusText(state, confrontation.id), "terminal-text");
            string pending = plan.standingOrder
                ? OperationPlanningSystem.StandingOrderPendingReason(state, confrontation.id)
                : "";
            Add(!plan.standingOrder
                ? " PLAN IS INTENT UNTIL A STANDING ORDER IS ISSUED. MANUAL EXECUTION REMAINS AVAILABLE."
                : string.IsNullOrEmpty(pending)
                    ? " STANDING ORDER IS READY. THE NEXT STEP WILL ATTEMPT ONCE AFTER MONTHLY CP REFRESH AND PAY ITS NORMAL COST."
                    : " STANDING ORDER REMAINS AUTHORIZED BUT WILL WAIT — " + pending.ToUpperInvariant(), "terminal-text-dim");
            var standingRow = Row();
            var standing = new Button(() =>
            {
                GameController.Instance.SetStandingOrder(confrontation.id, !plan.standingOrder);
                refresh?.Invoke();
            }) { text = plan.standingOrder ? "CANCEL STANDING ORDER" : "ISSUE STANDING ORDER" };
            standing.AddToClassList("cmd-button");
            if (plan.standingOrder) standing.AddToClassList("primary");
            if (!plan.standingOrder)
            {
                string blocked = OperationPlanningSystem.StandingOrderIssueBlockReason(state, confrontation.id);
                if (!string.IsNullOrEmpty(blocked))
                {
                    standing.SetEnabled(false);
                    standing.tooltip = blocked;
                    Add(" STANDING ORDER UNAVAILABLE — " + blocked.ToUpperInvariant(), "terminal-text-dim");
                }
            }
            standingRow.Add(standing);

            if (plan.steps != null && plan.steps.Count > 0)
            {
                Add(" SEQUENCE", "terminal-text-bright");
                for (int i = 0; i < plan.steps.Count; i++)
                {
                    var step = plan.steps[i]; if (step == null) continue;
                    var stepLocation = state.FindLocation(step.locationId);
                    string marker = step.completed ? "[X]" : OperationPlanningSystem.Next(state, confrontation.id) == step ? "[>]" : "[ ]";
                    Add($"  {i + 1}. {marker} {step.operationType.ToUpperInvariant()} — {(stepLocation?.displayName ?? step.locationId).ToUpperInvariant()}" + (step.completed ? $"  {step.completedDate.DisplayString}" : ""), step.completed ? "sig-friendly" : "terminal-text");
                    if (!step.completed)
                    {
                        string captured = step.id;
                        var removeRow = Row();
                        var remove = new Button(() => { OperationPlanningSystem.RemoveStep(state, confrontation.id, captured); refresh?.Invoke(); }) { text = "REMOVE STEP" };
                        remove.AddToClassList("cmd-button"); removeRow.Add(remove);
                    }
                }
            }

            if (plan.steps != null && plan.steps.Count >= OperationPlanningSystem.MaxSteps)
            {
                Add($" PLAN FULL — MAXIMUM {OperationPlanningSystem.MaxSteps} STEPS.", "terminal-text-dim");
                return;
            }

            var targets = new List<StrategicLocation>();
            foreach (var loc in state.locations)
                if (loc.ownerId == state.playerCountryId || confrontation.Involves(loc.ownerId)) targets.Add(loc);
            if (targets.Count == 0) return;
            if (string.IsNullOrEmpty(selectedLocationId) || state.FindLocation(selectedLocationId) == null) selectedLocationId = targets[0].id;

            Add(" ADD INTENDED STEP", "terminal-text-bright");
            var targetRow = Row();
            foreach (var loc in targets)
            {
                var captured = loc; bool current = selectedLocationId == loc.id;
                var b = new Button(() => { selectedLocationId = captured.id; refresh?.Invoke(); }) { text = (current ? "► " : "") + loc.displayName.ToUpperInvariant() };
                b.AddToClassList("cmd-button"); if (current) b.AddToClassList("primary"); targetRow.Add(b);
            }

            var domainRow = Row();
            foreach (OperationDomain domain in Enum.GetValues(typeof(OperationDomain)))
            {
                var captured = domain; bool current = selectedDomain == domain;
                var b = new Button(() => { selectedDomain = captured; var ops = OperationCatalog.InDomain(captured); if (ops.Count > 0) selectedOperation = ops[0].type; refresh?.Invoke(); }) { text = (current ? "► " : "") + Phrase.Caps(domain) };
                b.AddToClassList("cmd-button"); if (current) b.AddToClassList("primary"); domainRow.Add(b);
            }

            var selectedLocation = state.FindLocation(selectedLocationId);
            var opRow = Row();
            foreach (var profile in OperationCatalog.InDomain(selectedDomain))
            {
                var captured = profile.type; bool current = selectedOperation == captured;
                bool possible = OperationCatalog.CanOrder(state, state.playerCountryId, selectedLocation, captured, out string blocked);
                var b = new Button(() => { selectedOperation = captured; refresh?.Invoke(); }) { text = (current ? "► " : "") + profile.displayName };
                b.AddToClassList("cmd-button"); if (current) b.AddToClassList("primary");
                if (!possible)
                {
                    b.SetEnabled(false);
                    b.tooltip = blocked;
                    Add(" BLOCKED — " + profile.displayName.ToUpperInvariant() + ": " + blocked, "terminal-text-dim");
                }
                opRow.Add(b);
            }

            bool canAdd = OperationCatalog.CanOrder(state, state.playerCountryId, selectedLocation, selectedOperation, out string whyNot);
            var addRow = Row();
            var add = new Button(() => { OperationPlanningSystem.AddStep(state, confrontation.id, selectedLocationId, selectedOperation); refresh?.Invoke(); }) { text = "ADD TO PLAN [NO CP]" };
            add.AddToClassList("cmd-button"); add.AddToClassList("primary");
            if (!canAdd) { add.SetEnabled(false); add.tooltip = whyNot; }
            addRow.Add(add);
            if (!canAdd) Add(" UNAVAILABLE FOR THIS PLAN: " + whyNot, "terminal-text-dim");
        }

        VisualElement Row() { var row = new VisualElement(); row.AddToClassList("button-row"); root.Add(row); return row; }
        void Add(string text, string cls) { var label = new Label(text); label.AddToClassList("terminal-text"); if (cls != "terminal-text") label.AddToClassList(cls); root.Add(label); }
    }
}

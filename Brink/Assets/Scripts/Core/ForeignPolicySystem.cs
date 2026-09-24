using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Target-specific intent delegates existing paid commands, never a parallel resolver.</summary>
    public static class ForeignPolicySystem
    {
        public const int RevisionCost = 1;

        // Return values, not a live saved entry; UI queries cannot mutate the policy ledger.
        public static ForeignPolicyIntent IntentFor(GameState state, string targetId)
            => Find(state, targetId)?.intent ?? ForeignPolicyIntent.Unset;
        public static bool IsDelegated(GameState state, string targetId)
            => Find(state, targetId)?.delegated == true;

        static ForeignPolicy Find(GameState state, string targetId)
            => state?.mandate?.strategy?.foreignPolicies?.Find(p => p != null && p.targetId == targetId);

        public static int CostToSet(GameState state, string targetId, ForeignPolicyIntent intent)
        {
            var existing = Find(state, targetId);
            return existing == null || existing.intent == intent ? 0 : RevisionCost;
        }

        public static bool Set(GameState state, string targetId, ForeignPolicyIntent intent)
        {
            if (state?.PlayerCountry == null || targetId == state.playerCountryId
                || state.FindCountry(targetId) == null || !Enum.IsDefined(typeof(ForeignPolicyIntent), intent)) return false;
            var existing = Find(state, targetId);
            if (existing == null && intent == ForeignPolicyIntent.Unset) return true;
            if (existing != null && existing.intent == intent) return true;
            int cost = CostToSet(state, targetId, intent);
            if (state.influence < cost) return false;
            var plan = StrategySystem.Ensure(state);
            if (plan == null) return false;
            if (plan.foreignPolicies == null) plan.foreignPolicies = new List<ForeignPolicy>();
            if (existing == null)
            {
                existing = new ForeignPolicy { targetId = targetId };
                plan.foreignPolicies.Add(existing);
            }
            state.influence -= cost;
            existing.intent = intent;
            // A changed policy never inherits permission to execute a different action.
            existing.delegated = false;
            state.AddNotification(NotificationClass.Advisory, "FOREIGN POLICY",
                state.FindCountry(targetId).displayName + ": " + intent + ". " + Description(intent)
                + " Delegation is off; changing intent is not authorization.", state.playerCountryId);
            return true;
        }

        public static bool SetDelegated(GameState state, string targetId, bool enabled)
        {
            var policy = Find(state, targetId);
            if (policy == null) return false;
            if (enabled && (policy.intent == ForeignPolicyIntent.Unset || policy.intent == ForeignPolicyIntent.Ignore)) return false;
            policy.delegated = enabled;
            return true;
        }

        public static string Description(ForeignPolicyIntent intent)
        {
            switch (intent)
            {
                case ForeignPolicyIntent.Cooperate: return "Paid outreach until relations and trust both reach 70; no automatic treaty.";
                case ForeignPolicyIntent.Contain: return "Impose Pressure sanctions if none exist; no automatic escalation. Measures can lapse under ordinary review.";
                case ForeignPolicyIntent.Isolate: return "Impose Severe sanctions if none exist; embargo, blowback and escalation pressure are real. Existing measures are not upgraded.";
                case ForeignPolicyIntent.Reconcile: return "Lift our non-mandated sanctions first, then paid outreach to relations and trust 70; their measures and wars are not erased.";
                case ForeignPolicyIntent.Observe: return "Establish military collection, then expand our network to penetration 60. Exposure and upkeep remain ordinary; no covert attack.";
                case ForeignPolicyIntent.Ignore: return "No delegated action toward this state. Existing commitments, measures, networks and consequences remain.";
                default: return "No target-specific instruction.";
            }
        }

        enum Action { None, Outreach, Sanction, Lift, Establish, Expand }

        static Action Next(GameState state, string targetId, out Pillar pillar, out int cost, out string reason)
        {
            pillar = Pillar.Diplomacy; cost = 0; reason = "";
            var policy = Find(state, targetId);
            if (policy == null || !policy.delegated) { reason = "Delegation is off."; return Action.None; }
            if (state.FindCountry(targetId) == null || targetId == state.playerCountryId)
            { reason = "No foreign partner."; return Action.None; }
            var relationship = state.FindRelationship(state.playerCountryId, targetId);
            if (relationship == null) { reason = "No relationship record."; return Action.None; }
            var sanction = state.FindSanction(state.playerCountryId, targetId);
            Action action;
            switch (policy.intent)
            {
                case ForeignPolicyIntent.Contain:
                case ForeignPolicyIntent.Isolate:
                    pillar = Pillar.Economy;
                    if (sanction != null) { reason = "Our measures are already in force."; return Action.None; }
                    if (relationship.sanctionsTruceMonths > 0) { reason = "A sanctions detente still holds."; return Action.None; }
                    action = Action.Sanction;
                    cost = ProgressionSystem.DiscountedCost(state, EconomySystem.SanctionCost, SkillEffect.CoercionEfficiency);
                    break;
                case ForeignPolicyIntent.Reconcile:
                case ForeignPolicyIntent.Cooperate:
                    if (policy.intent == ForeignPolicyIntent.Reconcile && sanction != null)
                    {
                        pillar = Pillar.Economy;
                        if (CouncilSystem.SanctionsMandated(state, targetId))
                        { reason = "Chamber-mandated measures are not ours to trade away."; return Action.None; }
                        action = Action.Lift; cost = 1; break;
                    }
                    if (relationship.relations >= 70f && relationship.trust >= 70f)
                    { reason = "Outreach target reached; no further order needed."; return Action.None; }
                    action = Action.Outreach;
                    cost = ProgressionSystem.DiscountedCost(state, DiplomacySystem.DiplomaticOutreachCost, SkillEffect.OutreachEfficiency, minimum: 0);
                    break;
                case ForeignPolicyIntent.Observe:
                    pillar = Pillar.Intelligence;
                    var network = state.FindNetwork(state.playerCountryId, targetId);
                    if (network != null && network.compromised)
                    { reason = "Network compromised; waiting for ordinary recovery."; return Action.None; }
                    if (network != null && network.penetration >= 60f)
                    { reason = "Collection target reached; routine collection continues."; return Action.None; }
                    action = network == null ? Action.Establish : Action.Expand;
                    cost = network == null ? IntelligenceSystem.EstablishNetworkCost : IntelligenceSystem.ExpandNetworkCost;
                    break;
                default: reason = "No delegated action for this intent."; return Action.None;
            }
            var official = state.PlayerCountry.FindOfficial(pillar);
            if (official == null || official.mode != ControlMode.Autonomous)
            { reason = pillar + " must be staffed and Autonomous; explicit control takes precedence."; return Action.None; }
            if (!AuthoritySystem.HoldsAuthority(state, pillar))
            { reason = "Current " + pillar + " authority is required; no automatic approval request."; return Action.None; }
            if (state.commandPoints.current < cost) { reason = "Needs " + cost + " CP at execution."; return Action.None; }
            return action;
        }

        public static string PendingReason(GameState state, string targetId)
        {
            if (state?.mandate?.strategy != null && state.mandate.strategy.lastForeignPolicyAction.Equals(state.date)
                && IsDelegated(state, targetId)) return "Monthly policy slot used; resumes next month if still eligible.";
            var action = Next(state, targetId, out _, out int cost, out string reason);
            return action == Action.None ? reason : "Ready: " + action + " [" + cost + " CP]; shares the monthly policy slot.";
        }

        public static bool Execute(GameState state, TurnManager turns)
        {
            var plan = state?.mandate?.strategy;
            if (turns == null || !ReferenceEquals(turns.State, state) || plan?.foreignPolicies == null || plan.foreignPolicies.Count == 0
                || plan.lastForeignPolicyAction.Equals(state.date)) return false;
            // Rotate the starting country, not the saved list. Blocked policies never starve later ones.
            int count = state.countries.Count;
            if (count == 0) return false;
            int first = Math.Abs(state.date.MonthsSince(state.startDate) % count);
            for (int offset = 0; offset < count; offset++)
            {
                string targetId = state.countries[(first + offset) % count].id;
                var action = Next(state, targetId, out _, out _, out _);
                bool ok = false;
                switch (action)
                {
                    case Action.Outreach: ok = DiplomacySystem.Outreach(state, turns, targetId); break;
                    case Action.Sanction: ok = EconomySystem.ImposeSanctions(state, turns, targetId,
                        IntentFor(state, targetId) == ForeignPolicyIntent.Isolate ? SanctionSeverity.Severe : SanctionSeverity.Pressure); break;
                    case Action.Lift: ok = EconomySystem.LiftSanctions(state, turns, targetId); break;
                    case Action.Establish: ok = IntelligenceSystem.EstablishNetwork(state, turns, targetId, IntelDomain.Military); break;
                    case Action.Expand: ok = IntelligenceSystem.ExpandNetwork(state, turns, targetId); break;
                }
                if (!ok) continue;
                plan.lastForeignPolicyAction = state.date;
                state.AddNotification(NotificationClass.Advisory, "FOREIGN POLICY EXECUTED",
                    state.FindCountry(targetId).displayName + ": " + IntentFor(state, targetId) + " — " + action
                    + ". Ordinary command costs and consequences applied.", state.playerCountryId);
                return true;
            }
            return false;
        }
    }
}

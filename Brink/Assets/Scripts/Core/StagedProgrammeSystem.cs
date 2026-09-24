using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Ordered objectives with an explicit shared CP ceiling, not a second action resolver.</summary>
    public static class StagedProgrammeSystem
    {
        public const int MaxStages = 6;
        public static StagedProgramme For(GameState state) => state?.mandate?.strategy?.stagedProgramme;

        public static bool Add(GameState state, ProgrammeAction action, string targetId,
            float threshold, int earliestMonth, int deadlineMonth, ProgrammeCondition condition = ProgrammeCondition.Always)
        {
            if (state?.PlayerCountry == null || !Enum.IsDefined(typeof(ProgrammeAction), action)
                || action == ProgrammeAction.None || float.IsNaN(threshold) || float.IsInfinity(threshold)
                || threshold < 1f || threshold > 95f || earliestMonth < 0
                || deadlineMonth < earliestMonth || deadlineMonth > 120
                || !Enum.IsDefined(typeof(ProgrammeCondition), condition)) return false;
            var plan = For(state);
            if (plan?.abandoned == true || (plan?.stages?.Count ?? 0) >= MaxStages) return false;
            if (action == ProgrammeAction.Collection || action == ProgrammeAction.Outreach || action == ProgrammeAction.Assessment)
            {
                if (targetId == state.playerCountryId || state.FindCountry(targetId) == null) return false;
            }
            if (action == ProgrammeAction.EnergyWorks)
            {
                var site = state.FindLocation(targetId);
                if (site == null || site.type != LocationType.EnergyRegion || site.ownerId != state.playerCountryId) return false;
            }
            var strategy = StrategySystem.Ensure(state);
            if (strategy == null) return false;
            if (plan == null) strategy.stagedProgramme = plan = new StagedProgramme { adopted = state.date };
            if (plan.stages == null) plan.stages = new List<ProgrammeStage>();
            // Unity may materialize a missing nested record as a default object.
            if (plan.stages.Count == 0 && plan.adopted.Equals(default(GameDate))) plan.adopted = state.date;
            plan.stages.Add(new ProgrammeStage { action = action, targetId = targetId,
                threshold = threshold, earliestMonth = earliestMonth, deadlineMonth = deadlineMonth, condition = condition });
            plan.authorized = false; // editing intent never inherits permission
            return true;
        }

        public static bool Authorize(GameState state, bool enabled, int totalBudget)
        {
            var plan = For(state);
            if (plan == null) return false;
            if (!enabled) { plan.authorized = false; return true; }
            if (plan.abandoned || Next(plan) == null || totalBudget < Spent(plan) || totalBudget > 120) return false;
            foreach (var stage in plan.stages)
                if (!stage.completed && !AuthoritySystem.HoldsAuthority(state, PillarFor(stage.action))) return false;
            plan.commandPointBudget = totalBudget;
            plan.authorized = true;
            return true;
        }

        public static bool Remove(GameState state, int index)
        {
            var plan = For(state);
            if (plan?.stages == null || index < 0 || index >= plan.stages.Count
                || plan.stages[index].started || plan.stages[index].completed) return false;
            plan.stages.RemoveAt(index); plan.authorized = false;
            return true;
        }

        public static bool Abandon(GameState state)
        {
            var plan = For(state);
            if (plan == null || plan.abandoned) return false;
            plan.abandoned = true; plan.authorized = false;
            return true; // existing projects and costs are not undone
        }

        public static bool StartNew(GameState state)
        {
            var plan = For(state);
            if (plan != null && !plan.abandoned && Next(plan) != null) return false;
            var strategy = StrategySystem.Ensure(state);
            if (strategy == null) return false;
            if (plan != null && plan.stages != null && plan.stages.Count > 0)
            {
                if (strategy.programmeHistory == null) strategy.programmeHistory = new List<StagedProgramme>();
                strategy.programmeHistory.Add(plan);
            }
            // Do not reset the last-action date: restarting is not another monthly slot.
            strategy.stagedProgramme = new StagedProgramme { adopted = state.date,
                lastAction = plan == null ? default : plan.lastAction };
            return true;
        }

        public static bool MoveEarlier(GameState state, int index)
        {
            var plan = For(state);
            if (plan?.stages == null || plan.abandoned || index < 1 || index >= plan.stages.Count) return false;
            var current = plan.stages[index]; var prior = plan.stages[index - 1];
            if (current.started || current.completed || prior.started || prior.completed) return false;
            plan.stages[index - 1] = current; plan.stages[index] = prior;
            plan.authorized = false;
            return true;
        }

        public static string Status(GameState state)
        {
            var plan = For(state);
            if (plan == null) return "No staged programme. Planning spends nothing; authorization is separate.";
            var text = new System.Text.StringBuilder();
            text.AppendLine("STAGED PROGRAMME — " + (plan.abandoned ? "ABANDONED" : plan.authorized ? "AUTHORIZED" : "PAUSED"));
            text.AppendLine($"CP used {Spent(plan)} / authorized {plan.commandPointBudget}. Ordinary treasury costs continue separately.");
            if (plan.stages != null) for (int i = 0; i < plan.stages.Count; i++)
            {
                var stage = plan.stages[i]; if (stage == null) continue;
                string condition = stage.completed ? "COMPLETE" : state.date.MonthsSince(plan.adopted) > stage.deadlineMonth ? "DELAYED" : "PENDING";
                text.AppendLine($"{i + 1}. {stage.action} {stage.targetId} — {condition}; months {stage.earliestMonth}–{stage.deadlineMonth}, target {stage.threshold:F0}, gate {stage.condition}, spent {stage.commandPointsSpent} CP.");
            }
            string reason = PendingReason(state);
            text.AppendLine(reason.Length == 0 ? "Ready for one ordinary action after monthly CP refresh." : reason);
            text.Append("Completed stages record attainment, not permanent guarantees. Energy works require real completion; funding can lapse. Abandoning stops delegation, not existing projects.");
            return text.ToString();
        }

        public static int Spent(StagedProgramme plan)
        {
            int spent = 0;
            if (plan?.stages != null) foreach (var stage in plan.stages) if (stage != null) spent += stage.commandPointsSpent;
            return spent;
        }

        public static ProgrammeStage Next(StagedProgramme plan)
            => plan?.stages?.Find(s => s != null && !s.completed);

        public static Pillar PillarFor(ProgrammeAction action)
            => action == ProgrammeAction.Collection || action == ProgrammeAction.Assessment ? Pillar.Intelligence
                : action == ProgrammeAction.Outreach ? Pillar.Diplomacy
                : action == ProgrammeAction.Logistics ? Pillar.Military : Pillar.Economy;

        static bool Met(GameState state, ProgrammeStage stage)
        {
            switch (stage.action)
            {
                case ProgrammeAction.Assessment:
                    return stage.started && state.intelProducts.Exists(p => p.observerId == state.playerCountryId
                        && p.targetId == stage.targetId && p.question == EstimateQuestion.StrategicIntent
                        && p.commissioned.Equals(stage.commissioned) && p.delivered);
                case ProgrammeAction.Collection:
                    var network = state.FindNetwork(state.playerCountryId, stage.targetId);
                    return network != null && !network.compromised && network.penetration >= stage.threshold;
                case ProgrammeAction.Outreach:
                    var relation = state.FindRelationship(state.playerCountryId, stage.targetId);
                    return relation != null && relation.relations >= stage.threshold && relation.trust >= stage.threshold;
                case ProgrammeAction.Logistics: return state.PlayerCountry.military.logistics >= stage.threshold;
                case ProgrammeAction.EnergyWorks:
                    var site = state.FindLocation(stage.targetId);
                    return site != null && site.ownerId == state.playerCountryId && site.energyWorks;
                default: return false;
            }
        }

        static int Cost(GameState state, ProgrammeStage stage)
        {
            switch (stage.action)
            {
                case ProgrammeAction.Assessment: return IntelProductSystem.CommissionCost;
                case ProgrammeAction.Collection: return state.FindNetwork(state.playerCountryId, stage.targetId) == null
                    ? IntelligenceSystem.EstablishNetworkCost : IntelligenceSystem.ExpandNetworkCost;
                case ProgrammeAction.Outreach: return ProgressionSystem.DiscountedCost(state,
                    DiplomacySystem.DiplomaticOutreachCost, SkillEffect.OutreachEfficiency, minimum: 0);
                case ProgrammeAction.Logistics: return MilitarySystem.LogisticsInvestmentCost;
                default: return IndustrialSystem.CpCost;
            }
        }

        public static string PendingReason(GameState state)
        {
            var plan = For(state); var stage = Next(plan);
            if (plan == null || stage == null) return "No incomplete stage.";
            if (plan.abandoned) return "Abandoned; previous spending and projects remain.";
            if (!plan.authorized) return "Authorization is off.";
            if (plan.lastAction.Equals(state.date)) return "Monthly programme slot used.";
            if (state.date.MonthsSince(plan.adopted) < stage.earliestMonth) return "Waiting for the scheduled start.";
            if (!Enum.IsDefined(typeof(ProgrammeAction), stage.action) || stage.action == ProgrammeAction.None) return "Unrecognized action; revise the plan.";
            if (Met(state, stage)) return "Objective met; observation will advance the stage.";
            if (!Enum.IsDefined(typeof(ProgrammeCondition), stage.condition)) return "Unrecognized condition; revise the plan.";
            if (stage.condition == ProgrammeCondition.NonnegativeTreasury && state.PlayerCountry.resources.treasury < 0)
                return "Condition pending: nonnegative treasury.";
            if (stage.condition == ProgrammeCondition.NoActiveFront && state.confrontations.Exists(w => !w.resolved && w.Involves(state.playerCountryId)))
                return "Condition pending: no active front.";
            var pillar = PillarFor(stage.action);
            var official = state.PlayerCountry.FindOfficial(pillar);
            if (official == null || official.mode != ControlMode.Autonomous) return pillar + " must be staffed and Autonomous.";
            if (!AuthoritySystem.HoldsAuthority(state, pillar)) return "Current " + pillar + " authority required.";
            if (stage.action == ProgrammeAction.Collection || stage.action == ProgrammeAction.Outreach || stage.action == ProgrammeAction.Assessment)
            {
                if (stage.targetId == state.playerCountryId || state.FindCountry(stage.targetId) == null) return "Target no longer exists; revise the plan.";
                if (ForeignPolicySystem.IsDelegated(state, stage.targetId)) return "Pause the target's foreign policy before using this programme.";
                if (stage.action == ProgrammeAction.Outreach && state.FindRelationship(state.playerCountryId, stage.targetId) == null) return "No diplomatic channel.";
                if (stage.action == ProgrammeAction.Collection && state.FindNetwork(state.playerCountryId, stage.targetId)?.compromised == true) return "Collection is compromised.";
            }
            if (stage.action == ProgrammeAction.Assessment)
            {
                if (stage.started) return "Waiting for the commissioned assessment; delivery is not proof of truth.";
                if (!IntelProductSystem.CanCommission(state, state.playerCountryId, stage.targetId, EstimateQuestion.StrategicIntent, out string issue)) return issue;
            }
            if (stage.action == ProgrammeAction.Logistics && !MilitarySystem.CanInvestInLogistics(state, out string logistics)) return logistics;
            if (stage.action == ProgrammeAction.EnergyWorks)
            {
                var site = state.FindLocation(stage.targetId);
                if (site == null || site.ownerId != state.playerCountryId) return "Site no longer held; revise the plan.";
                if (stage.started) return "Waiting for the real energy works; a lapsed project requires a new plan, not automatic reordering.";
                if (!IndustrialSystem.CanBeginSite(state, state.playerCountryId, stage.targetId, out string issue)) return issue;
            }
            int cost = Cost(state, stage);
            if (Spent(plan) + cost > plan.commandPointBudget) return "CP budget exhausted; increase authorization or abandon.";
            if (state.commandPoints.current < cost) return "Waiting for ordinary Command Points.";
            return "";
        }

        public static void Observe(GameState state)
        {
            var plan = For(state); var stage = Next(plan);
            if (plan == null || plan.abandoned || stage == null
                || state.date.MonthsSince(plan.adopted) < stage.earliestMonth || !Met(state, stage)) return;
            stage.completed = true; stage.completedDate = state.date;
            if (Next(plan) == null) plan.authorized = false;
            state.AddNotification(NotificationClass.Advisory, "PROGRAMME STAGE COMPLETE",
                stage.action + ": measured target reached. No completion bonus.", state.playerCountryId);
        }

        public static bool Execute(GameState state, TurnManager turns)
        {
            if (state?.PlayerCountry == null || turns == null || !ReferenceEquals(turns.State, state)
                || PendingReason(state) != "") return false;
            var plan = For(state); var stage = Next(plan);
            int before = state.commandPoints.current;
            bool ok;
            switch (stage.action)
            {
                case ProgrammeAction.Assessment:
                    var product = IntelProductSystem.Commission(state, turns, stage.targetId, EstimateQuestion.StrategicIntent);
                    ok = product != null;
                    if (ok) stage.commissioned = product.commissioned;
                    break;
                case ProgrammeAction.Collection:
                    ok = state.FindNetwork(state.playerCountryId, stage.targetId) == null
                        ? IntelligenceSystem.EstablishNetwork(state, turns, stage.targetId, IntelDomain.Military)
                        : IntelligenceSystem.ExpandNetwork(state, turns, stage.targetId); break;
                case ProgrammeAction.Outreach: ok = DiplomacySystem.Outreach(state, turns, stage.targetId); break;
                case ProgrammeAction.Logistics: ok = MilitarySystem.InvestInLogistics(state, turns); break;
                case ProgrammeAction.EnergyWorks: ok = IndustrialSystem.BeginSite(state, turns, stage.targetId); break;
                default: return false;
            }
            int spent = Math.Max(0, before - state.commandPoints.current);
            stage.commandPointsSpent += spent;
            if (ok || spent > 0) { plan.lastAction = state.date; stage.started = true; }
            return ok;
        }
    }
}

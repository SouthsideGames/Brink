using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    public sealed class StrategicPolicyDef
    {
        public string id, slotId, label, description, countryId;
        public Pillar favours, strains;
    }

    /// <summary>
    /// Standing strategic intent for the player's posting. Strategy never grants
    /// national power directly: it is translated into existing Cabinet
    /// instructions, and explicit direction or Direct Control still wins.
    /// </summary>
    public static class StrategySystem
    {
        public const int MaxObjectives = 3;
        public const int DoctrineRevisionInfluence = 2;
        public const int PolicyRevisionInfluence = 1;

        static readonly StrategicPolicyDef[] Policies =
        {
            P("USA_ALLIANCE_FIRST","NATIONAL","Alliance First","Put coalition maintenance ahead of unilateral freedom of action.","USA",Pillar.Diplomacy,Pillar.Military),
            P("USA_INDUSTRIAL_RENEWAL","NATIONAL","Industrial Renewal","Rebuild the domestic production base while accepting less diplomatic reach.","USA",Pillar.Economy,Pillar.Diplomacy),
            P("CHN_INDUSTRIAL_SECURITY","NATIONAL","Industrial Security","Protect the industrial base even when diplomacy has to absorb the friction.","CHN",Pillar.Economy,Pillar.Diplomacy),
            P("CHN_STABLE_PERIPHERY","NATIONAL","Stable Periphery","Invest in diplomatic room around the frontier at the cost of economic tempo.","CHN",Pillar.Diplomacy,Pillar.Economy),
            P("RUS_STRATEGIC_DEPTH","NATIONAL","Strategic Depth","Prioritize military depth over economic openness.","RUS",Pillar.Military,Pillar.Economy),
            P("RUS_ECONOMIC_SOVEREIGNTY","NATIONAL","Economic Sovereignty","Strengthen the economic base while accepting less military readiness.","RUS",Pillar.Economy,Pillar.Military),
            P("IND_STRATEGIC_AUTONOMY","NATIONAL","Strategic Autonomy","Preserve room to manoeuvre between blocs, accepting slower alignment gains.","IND",Pillar.Government,Pillar.Diplomacy),
            P("IND_DEVELOPMENT_FIRST","NATIONAL","Development First","Put economic expansion ahead of institutional consolidation.","IND",Pillar.Economy,Pillar.Government),
            P("DEU_EXPORT_ORDER","NATIONAL","Export Order","Put economic integration ahead of coercive leverage.","DEU",Pillar.Economy,Pillar.Military),
            P("DEU_CONTINENTAL_SECURITY","NATIONAL","Continental Security","Reinforce deterrence while accepting a slower commercial agenda.","DEU",Pillar.Military,Pillar.Economy),
            P("JPN_MARITIME_SECURITY","NATIONAL","Maritime Security","Weight sea-lane security above domestic fiscal comfort.","JPN",Pillar.Military,Pillar.Economy),
            P("JPN_ECONOMIC_RESILIENCE","NATIONAL","Economic Resilience","Harden the economic base while accepting less military preparedness.","JPN",Pillar.Economy,Pillar.Military),
            P("BRA_REGIONAL_BROKER","NATIONAL","Regional Broker","Spend diplomatic effort to preserve regional room for compromise.","BRA",Pillar.Diplomacy,Pillar.Intelligence),
            P("BRA_STATE_RENEWAL","NATIONAL","State Renewal","Strengthen domestic institutions while accepting less external brokerage.","BRA",Pillar.Government,Pillar.Diplomacy),
            P("TUR_BALANCING_POWER","NATIONAL","Balancing Power","Keep strategic options open at the cost of institutional predictability.","TUR",Pillar.Diplomacy,Pillar.Government),
            P("TUR_INTERNAL_CONSOLIDATION","NATIONAL","Internal Consolidation","Prioritize cohesion while accepting less diplomatic freedom of action.","TUR",Pillar.Government,Pillar.Diplomacy),
            P("NGA_STATE_CAPACITY","NATIONAL","State Capacity","Put internal institutions ahead of external reach.","NGA",Pillar.Government,Pillar.Diplomacy),
            P("NGA_RESOURCE_LEVERAGE","NATIONAL","Resource Leverage","Convert resource strength into growth while accepting slower institutional repair.","NGA",Pillar.Economy,Pillar.Government),
            P("SAU_ENERGY_STATE","NATIONAL","Energy State","Protect economic leverage even when diversification moves more slowly.","SAU",Pillar.Economy,Pillar.Government),
            P("SAU_STATE_RESILIENCE","NATIONAL","State Resilience","Strengthen institutions while accepting less immediate economic leverage.","SAU",Pillar.Government,Pillar.Economy),
            P("AUS_FORWARD_PARTNERSHIPS","NATIONAL","Forward Partnerships","Invest in diplomatic access over independent military mass.","AUS",Pillar.Diplomacy,Pillar.Military),
            P("AUS_SELF_RELIANCE","NATIONAL","Strategic Self-Reliance","Build independent readiness while accepting less diplomatic reach.","AUS",Pillar.Military,Pillar.Diplomacy),
            P("KOR_TECH_EDGE","NATIONAL","Technology Edge","Weight economic and technical capacity above diplomatic flexibility.","KOR",Pillar.Economy,Pillar.Diplomacy),
            P("KOR_DETERRENCE_LINE","NATIONAL","Deterrence Line","Maintain military readiness while accepting less economic emphasis.","KOR",Pillar.Military,Pillar.Economy),
            P("MEX_DOMESTIC_FOUNDATION","NATIONAL","Domestic Foundation","Put state capacity ahead of foreign-policy ambition.","MEX",Pillar.Government,Pillar.Diplomacy),
            P("MEX_MARKET_DIVERSIFICATION","NATIONAL","Market Diversification","Broaden economic room while accepting slower institutional consolidation.","MEX",Pillar.Economy,Pillar.Government),
            P("IDN_ARCHIPELAGIC_BALANCE","NATIONAL","Archipelagic Balance","Protect internal cohesion while maintaining maritime reach.","IDN",Pillar.Government,Pillar.Military),
            P("IDN_MARITIME_GATEWAY","NATIONAL","Maritime Gateway","Prioritize maritime readiness while accepting greater domestic strain.","IDN",Pillar.Military,Pillar.Government),
            P("POL_FRONTLINE_DETERRENCE","NATIONAL","Frontline Security","Accept economic strain to keep the armed forces prepared.","POL",Pillar.Military,Pillar.Economy),
            P("POL_ECONOMIC_DEPTH","NATIONAL","Economic Depth","Build economic resilience while accepting less immediate readiness.","POL",Pillar.Economy,Pillar.Military),
            P("KAZ_MULTI_VECTOR","NATIONAL","Multi-Vector Policy","Prioritize diplomatic room between stronger neighbours over force concentration.","KAZ",Pillar.Diplomacy,Pillar.Military),
            P("KAZ_INTERNAL_DEPTH","NATIONAL","Internal Depth","Strengthen domestic institutions while accepting less diplomatic reach.","KAZ",Pillar.Government,Pillar.Diplomacy),
            P("GBR_GLOBAL_NETWORK","NATIONAL","Global Network","Preserve diplomatic reach while accepting less independent military concentration.","GBR",Pillar.Diplomacy,Pillar.Military),
            P("GBR_MARITIME_READINESS","NATIONAL","Maritime Readiness","Sustain deployable force at the cost of diplomatic bandwidth.","GBR",Pillar.Military,Pillar.Diplomacy),
            P("FRA_STRATEGIC_AUTONOMY","NATIONAL","Strategic Autonomy","Preserve independent military options while accepting less diplomatic alignment.","FRA",Pillar.Military,Pillar.Diplomacy),
            P("FRA_CONTINENTAL_LEADERSHIP","NATIONAL","Continental Leadership","Invest in diplomatic influence while accepting less military concentration.","FRA",Pillar.Diplomacy,Pillar.Military),
            P("ITA_MEDITERRANEAN_BROKER","NATIONAL","Mediterranean Broker","Prioritize regional diplomacy while accepting slower domestic consolidation.","ITA",Pillar.Diplomacy,Pillar.Government),
            P("ITA_DOMESTIC_RENEWAL","NATIONAL","Domestic Renewal","Strengthen institutions while accepting less external reach.","ITA",Pillar.Government,Pillar.Diplomacy),
            P("CAN_COALITION_ANCHOR","NATIONAL","Coalition Anchor","Invest in dependable partnerships while accepting less independent readiness.","CAN",Pillar.Diplomacy,Pillar.Military),
            P("CAN_NORTHERN_READINESS","NATIONAL","Northern Readiness","Build military capacity while accepting less diplomatic emphasis.","CAN",Pillar.Military,Pillar.Diplomacy),
            P("EGY_STATE_COHESION","NATIONAL","State Cohesion","Put domestic resilience ahead of regional influence.","EGY",Pillar.Government,Pillar.Diplomacy),
            P("EGY_REGIONAL_GATEWAY","NATIONAL","Regional Gateway","Prioritize diplomatic leverage while accepting slower institutional repair.","EGY",Pillar.Diplomacy,Pillar.Government),
            P("ZAF_INSTITUTIONAL_REPAIR","NATIONAL","Institutional Repair","Strengthen the state while accepting less regional activism.","ZAF",Pillar.Government,Pillar.Diplomacy),
            P("ZAF_REGIONAL_CONVENING","NATIONAL","Regional Convening","Invest in diplomatic leadership while accepting slower domestic repair.","ZAF",Pillar.Diplomacy,Pillar.Government),
            P("ARG_ECONOMIC_STABILIZATION","NATIONAL","Economic Stabilization","Put recovery ahead of external influence.","ARG",Pillar.Economy,Pillar.Diplomacy),
            P("ARG_REGIONAL_VOICE","NATIONAL","Regional Voice","Prioritize diplomatic room while accepting slower economic repair.","ARG",Pillar.Diplomacy,Pillar.Economy),
            P("VNM_STRATEGIC_HEDGING","NATIONAL","Strategic Hedging","Preserve diplomatic options while accepting less force concentration.","VNM",Pillar.Diplomacy,Pillar.Military),
            P("VNM_COASTAL_DEFENCE","NATIONAL","Coastal Defence","Build military readiness while accepting less diplomatic flexibility.","VNM",Pillar.Military,Pillar.Diplomacy)
        };

        static StrategicPolicyDef P(string id,string slot,string label,string description,string country,Pillar favours,Pillar strains)
            => new StrategicPolicyDef { id=id, slotId=slot, label=label, description=description, countryId=country, favours=favours, strains=strains };

        public static StrategicPlan Ensure(GameState state)
        {
            if (state.mandate == null) MandateSystem.Assign(state);
            if (state.mandate == null) return null;
            if (state.mandate.strategy == null) state.mandate.strategy = new StrategicPlan();
            return state.mandate.strategy;
        }

        public static StrategicPolicyDef[] AvailablePolicies(GameState state)
        {
            var list = new List<StrategicPolicyDef>();
            string id = state.PlayerCountry?.id;
            foreach (var p in Policies) if (p.countryId == id) list.Add(p);
            return list.ToArray();
        }

        /// <summary>
        /// The choice currently occupying a policy slot, or null.
        ///
        /// One definition, so what a panel prices and what <see cref="SetPolicy"/>
        /// actually charges cannot disagree — the same discipline as the single
        /// `OperationCatalog.CanOrder` gate shared by the order screen and the
        /// launch path.
        /// </summary>
        public static StrategicPolicyChoice PolicyInSlot(StrategicPlan plan, string slotId)
        {
            StrategicPolicyChoice held = null;
            if (plan != null) foreach (var p in plan.policies) if (p.slotId == slotId) held = p;
            return held;
        }

        public static bool SetDoctrine(GameState state, StrategicDoctrine doctrine)
        {
            var plan = Ensure(state); if (plan == null) return false;
            if (plan.doctrineChosen && plan.doctrine == doctrine) return true;
            int cost = plan.doctrineChosen ? DoctrineRevisionInfluence : 0;
            if (state.influence < cost) return false;
            state.influence -= cost;
            plan.doctrine = doctrine;
            plan.doctrineChosen = true;
            plan.doctrineAdopted = state.date;
            plan.revisionCount++;
            state.AddNotification(NotificationClass.Priority, "STRATEGY ADOPTED",
                $"Standing doctrine: {DoctrineLabel(doctrine)}. Delegated desks will weight routine choices accordingly.", state.playerCountryId);
            state.AddChronicle(ChronicleCategory.Political, state.playerCountryId,
                $"Strategic doctrine adopted: {DoctrineLabel(doctrine)}.");
            return true;
        }

        public static bool SetPolicy(GameState state, string policyId)
        {
            var plan = Ensure(state); if (plan == null) return false;
            StrategicPolicyDef def = null;
            foreach (var p in AvailablePolicies(state)) if (p.id == policyId) def = p;
            if (def == null) return false;
            var existing = PolicyInSlot(plan, def.slotId);
            if (existing != null && existing.policyId == policyId) return true;
            int cost = existing == null ? 0 : PolicyRevisionInfluence;
            if (state.influence < cost) return false;
            state.influence -= cost;
            if (existing == null) plan.policies.Add(new StrategicPolicyChoice { slotId=def.slotId, policyId=def.id, adopted=state.date });
            else { existing.policyId=def.id; existing.adopted=state.date; }
            plan.revisionCount++;
            state.AddNotification(NotificationClass.Priority, "NATIONAL POLICY",
                $"{def.label}: {def.description}", state.playerCountryId);
            return true;
        }

        /// <summary>
        /// Give the standing strategy a human name and planning horizon. This is
        /// organisation only: no Influence, initiative, XP or national effect.
        /// </summary>
        public static bool SetPlanFrame(GameState state, string title, int horizonMonths)
        {
            var plan = Ensure(state);
            if (plan == null || string.IsNullOrWhiteSpace(title)) return false;
            plan.planTitle = title.Trim();
            plan.horizonMonths = StrategicForecastSystem.ClampHorizon(horizonMonths);
            plan.horizonSet = state.date;
            return true;
        }

        public static bool AddObjective(GameState state, string title, MandateObjective condition)
        {
            var plan = Ensure(state); if (plan == null || condition == null || string.IsNullOrWhiteSpace(title)) return false;
            if (plan.objectives.Count >= MaxObjectives || !AllowedPlayerObjective(condition.kind)) return false;
            var objective = new PlayerObjective
            {
                id = "OBJ_" + state.NextActionSequence(), title = title.Trim(), condition = condition,
                created = state.date
            };
            plan.objectives.Add(objective);
            return true;
        }

        /// <summary>
        /// Record intent the simulation cannot honestly reduce to a numeric
        /// condition. Freeform objectives are self-assessed, unrewarded, and
        /// share the same limit and history as measured objectives.
        /// </summary>
        public static bool AddFreeformObjective(GameState state, string title)
        {
            var plan = Ensure(state); if (plan == null || string.IsNullOrWhiteSpace(title)) return false;
            if (plan.objectives.Count >= MaxObjectives) return false;
            plan.objectives.Add(new PlayerObjective
            {
                id = "OBJ_" + state.NextActionSequence(), title = title.Trim(), freeform = true,
                created = state.date
            });
            return true;
        }

        public static bool SetFreeformObjectiveMet(GameState state, string id, bool met)
        {
            var plan = Ensure(state); if (plan == null) return false;
            foreach (var objective in plan.objectives)
            {
                if (objective.id != id || !objective.freeform) continue;
                objective.achieved = met;
                if (met) RecordFirstAttainment(state, objective);
                return true;
            }
            return false;
        }

        public static bool RemoveObjective(GameState state, string id)
        {
            var plan = Ensure(state); if (plan == null) return false;
            for (int i=0;i<plan.objectives.Count;i++)
                if (plan.objectives[i].id==id) { plan.objectives.RemoveAt(i); return true; }
            return false;
        }

        static bool AllowedPlayerObjective(MandateObjectiveKind kind)
        {
            switch (kind)
            {
                case MandateObjectiveKind.PillarAtLeast: case MandateObjectiveKind.TreatiesAtLeast:
                case MandateObjectiveKind.StabilityAtLeast: case MandateObjectiveKind.ApprovalAtLeast:
                case MandateObjectiveKind.UnityAtLeast: case MandateObjectiveKind.EnergyAtLeast:
                case MandateObjectiveKind.FoodAtLeast: case MandateObjectiveKind.MaterialsAtLeast:
                case MandateObjectiveKind.IndustryAtLeast: case MandateObjectiveKind.RelationsAtLeast:
                case MandateObjectiveKind.RelationsAtMost: case MandateObjectiveKind.CapabilitiesAtLeast:
                case MandateObjectiveKind.Solvent: return true;
                default: return false;
            }
        }

        public static void MonthlyUpdate(GameState state)
        {
            var plan = Ensure(state); if (plan == null) return;
            foreach (var o in plan.objectives)
            {
                if (o.freeform) continue;
                bool met = MandateSystem.IsMet(state, o.condition);
                bool wasMet = o.achieved;
                o.achieved = met;
                if (!met || wasMet) continue;
                RecordFirstAttainment(state, o);
            }
        }

        static void RecordFirstAttainment(GameState state, PlayerObjective objective)
        {
            if (objective.everAchieved) return;
            objective.everAchieved = true;
            objective.achievedDate = state.date;
            state.AddNotification(NotificationClass.Advisory, "OBJECTIVE REACHED", objective.title, state.playerCountryId);
            state.AddChronicle(ChronicleCategory.System, state.playerCountryId,
                $"Player-authored objective first reached: {objective.title}.");
        }

        public static void ClarifyCabinetReport(GameState state)
        {
            var country = state.PlayerCountry;
            var plan = Ensure(state);
            if (country == null || plan == null) return;
            foreach (var line in state.cabinetReport)
            {
                var official = country.FindOfficial(line.pillar);
                if (official == null || official.mode != ControlMode.Autonomous || string.IsNullOrEmpty(official.directiveId)) continue;
                var def = CabinetSystem.FindDirective(official.office, official.directiveId);
                string label = def?.label ?? official.directiveId;
                line.ownJudgement = false;
                line.summary = $"worked under standing strategy ({label.ToLowerInvariant()}) — " + QualityTail(line.summary);
            }
        }

        static string QualityTail(string summary)
        {
            int dash = summary == null ? -1 : summary.LastIndexOf(" — ", StringComparison.Ordinal);
            return dash >= 0 ? summary.Substring(dash + 3) : "month resolved";
        }

        public static string StatusText(GameState state)
        {
            var plan = Ensure(state); if (plan == null) return "NO STRATEGIC PLAN ON FILE.";
            var sb = new StringBuilder();
            sb.AppendLine("STANDING STRATEGY");
            sb.AppendLine($"PLAN: {plan.planTitle.ToUpperInvariant()}   HORIZON: {plan.horizonMonths} MONTHS");
            sb.AppendLine("DOCTRINE: " + (plan.doctrineChosen ? DoctrineLabel(plan.doctrine).ToUpperInvariant() : "UNSET"));
            foreach (var c in plan.policies) { var d=FindPolicy(c.policyId); if(d!=null) sb.AppendLine("POLICY: " + d.label.ToUpperInvariant()); }
            sb.AppendLine("PLAYER OBJECTIVES:");
            if (plan.objectives.Count==0) sb.AppendLine("  NONE — define what success means for this posting.");
            foreach (var o in plan.objectives)
                sb.AppendLine((o.achieved ? "  [MET] " : o.freeform ? "  [OPEN] " : "  [   ] ") + o.title + (o.everAchieved && !o.achieved ? "  [PREVIOUSLY MET]" : ""));
            return sb.ToString().TrimEnd();
        }

        static StrategicPolicyDef FindPolicy(string id) { foreach(var p in Policies) if(p.id==id) return p; return null; }
        public static string DoctrineLabel(StrategicDoctrine d) => d.ToString();
    }
}

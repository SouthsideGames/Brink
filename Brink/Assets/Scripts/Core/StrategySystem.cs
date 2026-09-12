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
            P("CHN_INDUSTRIAL_SECURITY","NATIONAL","Industrial Security","Protect the industrial base even when diplomacy has to absorb the friction.","CHN",Pillar.Economy,Pillar.Diplomacy),
            P("RUS_STRATEGIC_DEPTH","NATIONAL","Strategic Depth","Prioritize military depth over economic openness.","RUS",Pillar.Military,Pillar.Economy),
            P("IND_STRATEGIC_AUTONOMY","NATIONAL","Strategic Autonomy","Preserve room to manoeuvre between blocs, accepting slower alignment gains.","IND",Pillar.Government,Pillar.Diplomacy),
            P("DEU_EXPORT_ORDER","NATIONAL","Export Order","Put economic integration ahead of coercive leverage.","DEU",Pillar.Economy,Pillar.Military),
            P("JPN_MARITIME_SECURITY","NATIONAL","Maritime Security","Weight sea-lane security above domestic fiscal comfort.","JPN",Pillar.Military,Pillar.Economy),
            P("BRA_REGIONAL_BROKER","NATIONAL","Regional Broker","Spend diplomatic effort to preserve regional room for compromise.","BRA",Pillar.Diplomacy,Pillar.Intelligence),
            P("TUR_BALANCING_POWER","NATIONAL","Balancing Power","Keep strategic options open at the cost of institutional predictability.","TUR",Pillar.Diplomacy,Pillar.Government),
            P("NGA_STATE_CAPACITY","NATIONAL","State Capacity","Put internal institutions ahead of external reach.","NGA",Pillar.Government,Pillar.Diplomacy),
            P("SAU_ENERGY_STATE","NATIONAL","Energy State","Protect economic leverage even when diversification moves more slowly.","SAU",Pillar.Economy,Pillar.Government),
            P("AUS_FORWARD_PARTNERSHIPS","NATIONAL","Forward Partnerships","Invest in diplomatic access over independent military mass.","AUS",Pillar.Diplomacy,Pillar.Military),
            P("KOR_TECH_EDGE","NATIONAL","Technology Edge","Weight economic and technical capacity above diplomatic flexibility.","KOR",Pillar.Economy,Pillar.Diplomacy),
            P("MEX_DOMESTIC_FOUNDATION","NATIONAL","Domestic Foundation","Put state capacity ahead of foreign-policy ambition.","MEX",Pillar.Government,Pillar.Diplomacy),
            P("IDN_ARCHIPELAGIC_BALANCE","NATIONAL","Archipelagic Balance","Protect internal cohesion while maintaining maritime reach.","IDN",Pillar.Government,Pillar.Military),
            P("POL_FRONTLINE_DETERRENCE","NATIONAL","Frontline Security","Accept economic strain to keep the armed forces prepared.","POL",Pillar.Military,Pillar.Economy),
            P("KAZ_MULTI_VECTOR","NATIONAL","Multi-Vector Policy","Prioritize diplomatic room between stronger neighbours over force concentration.","KAZ",Pillar.Diplomacy,Pillar.Military)
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
            // Strategy authoring is not annual-evaluation initiative. Otherwise a
            // player can toggle doctrine/policy/objectives for free grade points,
            // the same exploit Cabinet mode switching already had to close.
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
            StrategicPolicyChoice existing = null;
            foreach (var p in plan.policies) if (p.slotId == def.slotId) existing = p;
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
                if (o.condition == null) continue;
                bool met = MandateSystem.IsMet(state, o.condition);
                bool wasMet = o.achieved;
                o.achieved = met;
                if (!met || wasMet) continue;

                // First attainment is history; later regain is just current
                // status. This keeps a standing objective from becoming a noisy
                // quest that fires every time a threshold oscillates.
                if (!o.everAchieved)
                {
                    o.everAchieved = true;
                    o.achievedDate = state.date;
                    state.AddNotification(NotificationClass.Advisory, "OBJECTIVE REACHED", o.title, state.playerCountryId);
                    state.AddChronicle(ChronicleCategory.System, state.playerCountryId,
                        $"Player-authored objective first reached: {o.title}.");
                }
            }
        }

        /// <summary>
        /// CabinetSystem is older than standing strategy and labels every
        /// Autonomous desk as "own judgement". After the month, correct only the
        /// player's strategy-steered report lines so authorship remains truthful.
        /// This changes reporting, never simulation state.
        /// </summary>
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
            sb.AppendLine("DOCTRINE: " + (plan.doctrineChosen ? DoctrineLabel(plan.doctrine).ToUpperInvariant() : "UNSET"));
            foreach (var c in plan.policies) { var d=FindPolicy(c.policyId); if(d!=null) sb.AppendLine("POLICY: " + d.label.ToUpperInvariant()); }
            sb.AppendLine("PLAYER OBJECTIVES:");
            if (plan.objectives.Count==0) sb.AppendLine("  NONE — define what success means for this posting.");
            foreach (var o in plan.objectives)
                sb.AppendLine((o.achieved ? "  [MET] " : "  [   ] ") + o.title + (o.everAchieved && !o.achieved ? "  [PREVIOUSLY MET]" : ""));
            return sb.ToString().TrimEnd();
        }

        static StrategicPolicyDef FindPolicy(string id) { foreach(var p in Policies) if(p.id==id) return p; return null; }
        public static string DoctrineLabel(StrategicDoctrine d) => d.ToString();
    }
}
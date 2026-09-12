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
        public float favour = 0.08f, strain = 0.06f;
    }

    /// <summary>
    /// Phase B strategic intent. This sits above Cabinet directives: doctrine is
    /// the standing answer to "what matters when I am not looking?"; national
    /// policies are country-shaped trade-offs; authored objectives let the player
    /// define success without turning Brink into a quest log.
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
            P("POL_FRONTLINE_DETERRENCE","NATIONAL","Frontline Deterrence","Accept economic strain to keep military readiness high.","POL",Pillar.Military,Pillar.Economy),
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
            ProgressionSystem.RecordInitiative(state);
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
            ProgressionSystem.RecordInitiative(state);
            state.AddNotification(NotificationClass.Priority, "NATIONAL POLICY",
                $"{def.label}: {def.description}", state.playerCountryId);
            return true;
        }

        /// <summary>
        /// Delegated-work weighting only. It never changes Direct Control output,
        /// resources, or a pillar by itself; it changes which unattended desks
        /// receive marginal institutional attention.
        /// </summary>
        public static float DelegatedMultiplier(GameState state, Pillar pillar)
        {
            var plan = Ensure(state); if (plan == null || !plan.doctrineChosen) return 1f;
            float m = DoctrineMultiplier(plan.doctrine, pillar);
            foreach (var choice in plan.policies)
            {
                var def = FindPolicy(choice.policyId);
                if (def == null) continue;
                if (def.favours == pillar) m += def.favour;
                if (def.strains == pillar) m -= def.strain;
            }
            return Math.Max(0.75f, Math.Min(1.25f, m));
        }

        static float DoctrineMultiplier(StrategicDoctrine d, Pillar p)
        {
            if (d == StrategicDoctrine.Balanced) return 1f;
            if (d == StrategicDoctrine.Deterrence) return p == Pillar.Military ? 1.12f : p == Pillar.Economy ? 0.94f : 1f;
            if (d == StrategicDoctrine.Prosperity) return p == Pillar.Economy ? 1.12f : p == Pillar.Military ? 0.94f : 1f;
            if (d == StrategicDoctrine.Influence) return p == Pillar.Diplomacy ? 1.10f : p == Pillar.Intelligence ? 1.06f : p == Pillar.Military ? 0.95f : 1f;
            if (d == StrategicDoctrine.Resilience) return p == Pillar.Government ? 1.10f : p == Pillar.Economy ? 1.05f : p == Pillar.Diplomacy ? 0.96f : 1f;
            return p == Pillar.Economy || p == Pillar.Intelligence ? 1.07f : p == Pillar.Government ? 0.96f : 1f;
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
            ProgressionSystem.RecordInitiative(state);
            return true;
        }

        public static bool RemoveObjective(GameState state, string id)
        {
            var plan = Ensure(state); if (plan == null) return false;
            for (int i=0;i<plan.objectives.Count;i++) if (plan.objectives[i].id==id) { plan.objectives.RemoveAt(i); return true; }
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
                if (o.achieved || o.condition == null) continue;
                if (!MandateSystem.IsMet(state, o.condition)) continue;
                o.achieved = true; o.achievedDate = state.date;
                state.AddNotification(NotificationClass.Advisory, "OBJECTIVE REACHED", o.title, state.playerCountryId);
                state.AddChronicle(ChronicleCategory.System, state.playerCountryId, $"Player-authored objective reached: {o.title}.");
            }
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
            foreach (var o in plan.objectives) sb.AppendLine((o.achieved ? "  [MET] " : "  [   ] ") + o.title);
            return sb.ToString().TrimEnd();
        }

        static StrategicPolicyDef FindPolicy(string id) { foreach(var p in Policies) if(p.id==id) return p; return null; }
        public static string DoctrineLabel(StrategicDoctrine d) => d.ToString();
    }
}
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Maps strategic intent onto existing delegated Cabinet instructions.</summary>
    public static class StrategyCabinetBridge
    {
        public static void Prepare(GameState state)
        {
            var plan=StrategySystem.Ensure(state); var country=state.PlayerCountry;
            if(plan==null||country==null)return;
            foreach(var official in country.cabinet)
            {
                if(official.mode!=ControlMode.Autonomous)continue;
                official.directiveId=DefaultDirective(plan.doctrineChosen?plan.doctrine:StrategicDoctrine.Balanced,official.office);
            }
            foreach(var choice in plan.policies)
            {
                StrategicPolicyDef def=null;
                foreach(var candidate in StrategySystem.AvailablePolicies(state))if(candidate.id==choice.policyId)def=candidate;
                if(def==null)continue;
                var favoured=country.FindOfficial(def.favours);
                if(favoured!=null&&favoured.mode==ControlMode.Autonomous)favoured.directiveId=FavouredDirective(def.favours);
                var strained=country.FindOfficial(def.strains);
                if(strained!=null&&strained.mode==ControlMode.Autonomous)strained.directiveId=StrainedDirective(def.strains);
            }
        }

        static string DefaultDirective(StrategicDoctrine d,Pillar p)
        {
            switch(d)
            {
                case StrategicDoctrine.Deterrence: if(p==Pillar.Military)return "MIL_READINESS";if(p==Pillar.Economy)return "ECO_AUSTERITY";if(p==Pillar.Diplomacy)return "DIP_PRESSURE";break;
                case StrategicDoctrine.Prosperity: if(p==Pillar.Economy)return "ECO_GROWTH";if(p==Pillar.Military)return "MIL_CONSERVE";if(p==Pillar.Diplomacy)return "DIP_OUTREACH";break;
                case StrategicDoctrine.Influence: if(p==Pillar.Diplomacy)return "DIP_OUTREACH";if(p==Pillar.Intelligence)return "INT_COLLECTION";if(p==Pillar.Military)return "MIL_CONSERVE";break;
                case StrategicDoctrine.Resilience: if(p==Pillar.Government)return "GOV_STABILITY";if(p==Pillar.Intelligence)return "INT_COUNTERINTEL";if(p==Pillar.Economy)return "ECO_AUSTERITY";break;
                case StrategicDoctrine.Transformation: if(p==Pillar.Economy)return "ECO_GROWTH";if(p==Pillar.Intelligence)return "INT_COLLECTION";if(p==Pillar.Government)return "GOV_STABILITY";break;
            }
            return "";
        }

        static string FavouredDirective(Pillar p)
        {
            switch(p){case Pillar.Military:return "MIL_READINESS";case Pillar.Economy:return "ECO_GROWTH";case Pillar.Intelligence:return "INT_COLLECTION";case Pillar.Diplomacy:return "DIP_OUTREACH";default:return "GOV_STABILITY";}
        }

        static string StrainedDirective(Pillar p)
        {
            // A national policy must cost strategic room somewhere. Use existing
            // conservative/low-attention instructions where they exist; an empty
            // id means the desk receives no special strategic instruction.
            switch(p){case Pillar.Military:return "MIL_CONSERVE";case Pillar.Economy:return "ECO_AUSTERITY";case Pillar.Diplomacy:return "DIP_PRESSURE";default:return "";}
        }
    }
}
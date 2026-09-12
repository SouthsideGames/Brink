using Brink.Data;

namespace Brink.Core
{
    /// <summary>Maps strategic intent onto existing delegated Cabinet instructions.</summary>
    public static class StrategyCabinetBridge
    {
        public static void Prepare(GameState state)
        {
            var plan = StrategySystem.Ensure(state);
            var country = state.PlayerCountry;
            if (plan == null || country == null) return;

            foreach (var official in country.cabinet)
            {
                if (official.mode != ControlMode.Autonomous) continue;
                official.directiveId = DefaultDirective(plan.doctrineChosen ? plan.doctrine : StrategicDoctrine.Balanced, official.office);
            }

            foreach (var choice in plan.policies)
            {
                StrategicPolicyDef def = null;
                foreach (var candidate in StrategySystem.AvailablePolicies(state))
                    if (candidate.id == choice.policyId) def = candidate;
                if (def == null) continue;

                var favoured = country.FindOfficial(def.favours);
                if (favoured != null && favoured.mode == ControlMode.Autonomous)
                    favoured.directiveId = FavouredDirective(def.favours);

                var strained = country.FindOfficial(def.strains);
                if (strained != null && strained.mode == ControlMode.Autonomous)
                    strained.directiveId = StrainedDirective(def.strains);
            }
        }

        static string DefaultDirective(StrategicDoctrine doctrine, Pillar pillar)
        {
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence:
                    // Autonomous MIL_READINESS paid the directive's Cabinet cost
                    // while MilitarySystem withheld its readiness-target benefit
                    // from non-Directed officials. PREPARE FOR WAR is an actual
                    // existing delegated path: it fills force shortfalls and then
                    // naturally stops buying when establishment is met.
                    if (pillar == Pillar.Military) return MilitaryAdvice.PrepareForWar;
                    if (pillar == Pillar.Economy) return "ECO_AUSTERITY";
                    if (pillar == Pillar.Diplomacy) return "DIP_PRESSURE";
                    break;
                case StrategicDoctrine.Prosperity:
                    if (pillar == Pillar.Economy) return "ECO_GROWTH";
                    if (pillar == Pillar.Military) return "MIL_CONSERVE";
                    if (pillar == Pillar.Diplomacy) return "DIP_OUTREACH";
                    break;
                case StrategicDoctrine.Influence:
                    if (pillar == Pillar.Diplomacy) return "DIP_OUTREACH";
                    if (pillar == Pillar.Intelligence) return "INT_COLLECTION";
                    if (pillar == Pillar.Military) return "MIL_CONSERVE";
                    break;
                case StrategicDoctrine.Resilience:
                    if (pillar == Pillar.Government) return "GOV_STABILITY";
                    if (pillar == Pillar.Intelligence) return "INT_COUNTERINTEL";
                    if (pillar == Pillar.Economy) return "ECO_AUSTERITY";
                    break;
                case StrategicDoctrine.Transformation:
                    if (pillar == Pillar.Economy) return "ECO_GROWTH";
                    if (pillar == Pillar.Intelligence) return "INT_COLLECTION";
                    if (pillar == Pillar.Government) return "GOV_STABILITY";
                    break;
            }
            return "";
        }

        static string FavouredDirective(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military: return MilitaryAdvice.PrepareForWar;
                case Pillar.Economy: return "ECO_GROWTH";
                case Pillar.Intelligence: return "INT_COLLECTION";
                case Pillar.Diplomacy: return "DIP_OUTREACH";
                default: return "GOV_STABILITY";
            }
        }

        static string StrainedDirective(Pillar pillar)
        {
            // Strain is expressed as a competing use of the same ministry, not a
            // hidden negative modifier. Every Standard policy therefore changes
            // both sides of its declared trade-off.
            switch (pillar)
            {
                case Pillar.Military: return "MIL_CONSERVE";
                case Pillar.Economy: return "ECO_AUSTERITY";
                case Pillar.Intelligence: return "INT_COUNTERINTEL";
                case Pillar.Diplomacy: return "DIP_PRESSURE";
                default: return "GOV_APPROVAL";
            }
        }
    }
}
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Narrow bridge between the posting's standing strategy and the existing
    /// Cabinet machinery. Strategy never performs a national action itself: it
    /// supplies the default instruction to Autonomous player desks. Directed
    /// officials keep the operator's explicit order; Direct Control remains
    /// personal command. This preserves the delegation hierarchy instead of
    /// inventing a second simulation path.
    /// </summary>
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

            // The country's authored policy gets the last word on the favoured
            // desk only. It is deliberately expressed through existing cabinet
            // directives, so the trade-off is visible in the same systems the
            // player already understands rather than as a hidden numeric buff.
            foreach (var choice in plan.policies)
            {
                StrategicPolicyDef def = null;
                foreach (var candidate in StrategySystem.AvailablePolicies(state))
                    if (candidate.id == choice.policyId) def = candidate;
                if (def == null) continue;
                var official = country.FindOfficial(def.favours);
                if (official != null && official.mode == ControlMode.Autonomous)
                    official.directiveId = FavouredDirective(def.favours);
            }
        }

        static string DefaultDirective(StrategicDoctrine doctrine, Pillar pillar)
        {
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence:
                    if (pillar == Pillar.Military) return "MIL_READINESS";
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
                case Pillar.Military: return "MIL_READINESS";
                case Pillar.Economy: return "ECO_GROWTH";
                case Pillar.Intelligence: return "INT_COLLECTION";
                case Pillar.Diplomacy: return "DIP_OUTREACH";
                default: return "GOV_STABILITY";
            }
        }
    }
}
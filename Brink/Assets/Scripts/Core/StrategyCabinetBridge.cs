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

            foreach (var objective in plan.objectives)
            {
                if (objective.id != plan.programmeObjectiveId || objective.achieved) continue;
                if (!TryProgrammeInstruction(objective, out var pillar, out var directive)) break;
                var official = country.FindOfficial(pillar);
                if (official != null && official.mode == ControlMode.Autonomous) official.directiveId = directive;
                break;
            }
        }

        public static bool TryProgrammeInstruction(PlayerObjective objective, out Pillar pillar, out string directive)
        {
            pillar = Pillar.Government; directive = "";
            if (objective == null || objective.freeform || objective.condition == null) return false;
            switch (objective.condition.kind)
            {
                case MandateObjectiveKind.StabilityAtLeast:
                    directive = "GOV_STABILITY"; return true;
                case MandateObjectiveKind.ApprovalAtLeast:
                    directive = "GOV_APPROVAL"; return true;
                case MandateObjectiveKind.Solvent:
                    pillar = Pillar.Economy; directive = "ECO_AUSTERITY"; return true;
                case MandateObjectiveKind.PillarAtLeast:
                    if (!System.Enum.TryParse(objective.condition.param, true, out pillar)) return false;
                    if (pillar == Pillar.Government) return false;
                    directive = FavouredDirective(pillar); return true;
                default: return false;
            }
        }

        /// <summary>
        /// Read-only preview used by the forecast UI. It returns the exact same
        /// id Prepare would write, so what-if text cannot drift away from runtime
        /// behaviour.
        /// </summary>
        public static string PreviewDirective(StrategicDoctrine doctrine, Pillar pillar)
            => DefaultDirective(doctrine, pillar);

        public static string PreviewLabel(StrategicDoctrine doctrine, Pillar pillar)
        {
            string id = DefaultDirective(doctrine, pillar);
            if (string.IsNullOrEmpty(id)) return "ordinary ministerial judgement";
            return CabinetSystem.FindDirective(pillar, id)?.label ?? id;
        }

        static string DefaultDirective(StrategicDoctrine doctrine, Pillar pillar)
        {
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence:
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

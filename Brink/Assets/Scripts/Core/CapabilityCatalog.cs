using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// One researchable capability. Distributed across the five pillars rather
    /// than forming a sixth pillar or a single tree (GDD §11).
    /// </summary>
    public class CapabilityDef
    {
        public string id;
        public string name;
        public Pillar pillar;
        public string description;

        /// <summary>Months of funded work to develop it ourselves.</summary>
        public int researchMonths;

        /// <summary>Treasury per month while the programme runs.</summary>
        public float monthlyCost;

        /// <summary>Capability ids that must be held first.</summary>
        public string[] prerequisites = new string[0];

        /// <summary>
        /// Minimum institutional base to attempt it at all — a state without the
        /// underlying economy or institutions cannot simply buy the future.
        /// </summary>
        public float requiredIndustry;
        public float requiredPillar;
    }

    /// <summary>
    /// The authored capability catalog (GDD §11).
    ///
    /// Every entry unlocks an option or an efficiency. None grant force
    /// structure, trained personnel, infrastructure or money — those must still
    /// be built, recruited, financed and maintained.
    /// </summary>
    public static class CapabilityCatalog
    {
        static List<CapabilityDef> definitions;

        public static IReadOnlyList<CapabilityDef> Definitions => definitions ?? (definitions = Build());

        public static CapabilityDef Find(string id)
        {
            foreach (var definition in Definitions)
                if (definition.id == id) return definition;
            return null;
        }

        public static List<CapabilityDef> ForPillar(Pillar pillar)
        {
            var result = new List<CapabilityDef>();
            foreach (var definition in Definitions)
                if (definition.pillar == pillar) result.Add(definition);
            return result;
        }

        static List<CapabilityDef> Build()
        {
            var list = new List<CapabilityDef>();

            void Add(string id, string name, Pillar pillar, int months, float cost,
                float industry, float pillarFloor, string description, params string[] prerequisites)
            {
                list.Add(new CapabilityDef
                {
                    id = id, name = name, pillar = pillar,
                    researchMonths = months, monthlyCost = cost,
                    requiredIndustry = industry, requiredPillar = pillarFloor,
                    description = description, prerequisites = prerequisites
                });
            }

            // ---------------- Military ----------------
            Add("CAP_PRECISION", "Precision Munitions", Pillar.Military, 24, 40f, 55f, 45f,
                "Guided weapons and targeting. Operations do less collateral harm for the same effect.");

            Add("CAP_LIFT", "Strategic Lift", Pillar.Military, 30, 45f, 60f, 50f,
                "Heavy transport and prepositioning. Sustainment holds up under a standing posture.");

            Add("CAP_ISR", "Integrated ISR", Pillar.Military, 30, 50f, 62f, 55f,
                "Reconnaissance fused to fires. Operations resolve on better information.",
                "CAP_PRECISION");

            // ---------------- Economy ----------------
            Add("CAP_ADVMFG", "Advanced Manufacturing", Pillar.Economy, 30, 45f, 58f, 55f,
                "Automation and process control. Industrial capacity compounds faster.");

            Add("CAP_ENERGY", "Domestic Energy Programme", Pillar.Economy, 36, 55f, 50f, 45f,
                "Generation and storage at national scale. Energy security improves year on year.");

            Add("CAP_FINANCE", "Financial Infrastructure", Pillar.Economy, 24, 40f, 45f, 60f,
                "Clearing, settlement and reach. Economic coercion costs us less to sustain.",
                "CAP_ADVMFG");

            // ---------------- Intelligence ----------------
            Add("CAP_SIGINT", "Signals Architecture", Pillar.Intelligence, 24, 40f, 55f, 50f,
                "National collection infrastructure. Networks deepen considerably faster.");

            Add("CAP_SECCOMMS", "Secure Communications", Pillar.Intelligence, 20, 35f, 50f, 45f,
                "Hardened state communications. Foreign services find us markedly harder to read.");

            Add("CAP_ANALYTICS", "Analytic Computing", Pillar.Intelligence, 30, 45f, 60f, 60f,
                "Processing at scale. Our estimates carry tighter margins.",
                "CAP_SIGINT");

            // ---------------- Diplomacy ----------------
            Add("CAP_CONVENING", "Convening Infrastructure", Pillar.Diplomacy, 20, 30f, 35f, 55f,
                "Standing institutions others want to be inside. Treaties are easier to conclude.");

            Add("CAP_VERIFICATION", "Verification Regimes", Pillar.Diplomacy, 26, 35f, 45f, 60f,
                "Monitoring that lets rivals believe each other. Settlements come sooner.",
                "CAP_CONVENING");

            // ---------------- Government ----------------
            Add("CAP_CIVADMIN", "Civil Administration Reform", Pillar.Government, 24, 35f, 35f, 50f,
                "A state apparatus that executes. Political capital accrues faster.");

            Add("CAP_CONTINUITY", "National Resilience Planning", Pillar.Government, 28, 40f, 45f, 55f,
                "Contingency and continuity. Crises do less damage when they land.",
                "CAP_CIVADMIN");

            return list;
        }
    }
}

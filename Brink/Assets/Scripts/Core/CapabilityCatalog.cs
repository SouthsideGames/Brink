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

            // ================= Tranche E (spec 13 §6, spec 25 §8) =================
            //
            // **Some of these unlock an option rather than an efficiency**, which
            // is the idea the catalogue was missing. The distinction already
            // exists elsewhere and was only ever used in one direction: a *skill*
            // is operator capability and gates operator verbs (spec 07), a
            // *capability* is national ability — and until now every capability
            // granted a multiplier and none gated anything except the five
            // strategic instruments. The parallel was there and unused.
            //
            // Every entry below is read somewhere. `TechnologyTests` fails the
            // build on a capability with no read site, because twenty unread ids
            // would be the "written but never read" family at scale.

            // ---------------- Military ----------------
            Add("CAP_AIRDEFENSE", "Integrated Air Defence", Pillar.Military, 26, 42f, 55f, 48f,
                "Layered interception. Missile-defence programmes over our own ground hold far "
                + "more of what is thrown at them.");

            Add("CAP_UNDERSEA", "Undersea Warfare", Pillar.Military, 32, 48f, 60f, 55f,
                "Quiet hulls and the sensors to hunt them. Naval operations resolve on our terms "
                + "rather than on the size of the fleet.");

            Add("CAP_AUTONOMY", "Autonomous Systems", Pillar.Military, 28, 44f, 62f, 52f,
                "Machines where people used to be. Our own casualties fall for the same effect.",
                "CAP_PRECISION");

            // **Unlocks an operation.** Nothing else in the catalogue does this.
            Add("CAP_HYPERSONIC", "Hypersonic Strike", Pillar.Military, 40, 62f, 70f, 62f,
                "A weapon nothing currently fielded can intercept. Unlocks the deep-strike "
                + "order, and there is no defence against it yet.",
                "CAP_PRECISION", "CAP_ISR");

            // ---------------- Economy ----------------
            // Closes a standing open item: a food-poor state had *no* route to
            // raise its own ceiling — only authored trade links and the player's
            // own deals, neither of which an AI government can reach for.
            Add("CAP_AGRI", "Agricultural Science", Pillar.Economy, 26, 38f, 40f, 45f,
                "Yield, storage and distribution. The land feeds more of us than it did.");

            Add("CAP_SUBSTITUTION", "Materials Substitution", Pillar.Economy, 28, 42f, 55f, 50f,
                "Alloys and recycling. Industry needs less of what we have to import.");

            Add("CAP_LOGNET", "Logistics Network", Pillar.Economy, 24, 36f, 50f, 48f,
                "Ports, rail and the paperwork between them. Trade carries more for the same "
                + "relationships.");

            Add("CAP_RESERVECURR", "Reserve Currency Standing", Pillar.Economy, 36, 58f, 60f, 70f,
                "Everyone settles in our paper. Coercion aimed at us lands softer, and ours "
                + "lands harder.",
                "CAP_FINANCE");

            // ---------------- Intelligence ----------------
            // **Unlocks a covert verb.** Tranche B wrote `CyberOperation`
            // expecting this gate to exist.
            Add("CAP_CYBER", "Offensive Cyber Capability", Pillar.Intelligence, 28, 45f, 58f, 55f,
                "Reaching into somebody else's systems. Unlocks the cyber operation — damage "
                + "with no hand to shake.",
                "CAP_SIGINT");

            // **Unlocks estimates without a network**, which is the one thing
            // that changes what the map looks like rather than how sharp it is.
            Add("CAP_OVERHEAD", "Overhead Reconnaissance", Pillar.Intelligence, 34, 52f, 62f, 58f,
                "We can see them whether or not we have anybody there. Thin, unreliable "
                + "reporting on every state — but no longer nothing.",
                "CAP_SIGINT");

            // **Unlocks a question.** The sponsorship Special Estimate is
            // unanswerable without it.
            Add("CAP_FORENSICS", "Attribution Forensics", Pillar.Intelligence, 26, 40f, 55f, 60f,
                "Tracing a weapon, a payment or a fingerprint back to whoever sent it. Unlocks "
                + "asking who is really behind a rising.",
                "CAP_ANALYTICS");

            // ---------------- Diplomacy ----------------
            // **Unlocks a treaty commitment.**
            Add("CAP_ARMSCONTROL", "Arms Control Regime", Pillar.Diplomacy, 30, 38f, 40f, 62f,
                "Inspection protocols nobody has to take on trust. Unlocks limitation treaties.",
                "CAP_VERIFICATION");

            Add("CAP_DEVAID", "Development Assistance", Pillar.Diplomacy, 24, 42f, 45f, 55f,
                "Money that arrives as help and stays as leverage. Dependence grows where we "
                + "spend it.");

            Add("CAP_BROADCAST", "External Broadcasting", Pillar.Diplomacy, 22, 32f, 40f, 50f,
                "Speaking past a government to the people it governs. Being disliked abroad "
                + "costs us less than it did.");

            // ---------------- Government ----------------
            Add("CAP_STATISTICS", "National Statistical Service", Pillar.Government, 20, 30f, 35f, 45f,
                "Knowing our own condition before it becomes news. A weak desk loses less of "
                + "what should have reached the terminal.");

            Add("CAP_EMERGENCY", "Emergency Powers Framework", Pillar.Government, 24, 34f, 35f, 55f,
                "Extraordinary authority with a legal shape. It costs the state less legitimacy "
                + "to use, and less to give back.");

            Add("CAP_CIVILDEF", "Civil Defence", Pillar.Government, 26, 36f, 42f, 50f,
                "Shelters, stockpiles and a plan. What war and displacement do to the public "
                + "lands softer.",
                "CAP_CONTINUITY");

            // ---------------- Dual-use ----------------
            //
            // **Prerequisites in two pillars.** The catalogue was five separate
            // ladders; these are the rungs that need both, and they are the
            // reason to build breadth rather than depth in one place.
            Add("CAP_SPACE", "Space Access", Pillar.Military, 42, 65f, 70f, 62f,
                "Our own launch. Everything overhead gets better, and so does everything that "
                + "reaches a long way.",
                "CAP_ISR", "CAP_OVERHEAD");

            Add("CAP_STRATLOG", "Strategic Logistics", Pillar.Military, 32, 50f, 60f, 58f,
                "Moving an army the way we move cargo. Sustainment holds up a very long way "
                + "from home.",
                "CAP_LIFT", "CAP_LOGNET");

            Add("CAP_TECHTRANSFER", "Technology Transfer Regime", Pillar.Economy, 28, 44f, 55f, 58f,
                "Selling what we know, on our terms. Knowledge we hold spreads to partners "
                + "faster, and theirs to us.",
                "CAP_ADVMFG", "CAP_CONVENING");

            return list;
        }
    }
}

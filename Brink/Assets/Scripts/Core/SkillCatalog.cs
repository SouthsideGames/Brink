using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The five branching Strategist trees plus cross-pillar hybrids (GDD §25.3).
    ///
    /// Every node expands what the operator can know, choose or afford. None
    /// raise national statistics — country capability is still developed through
    /// investment, officials, technology, policy, conflict and world events.
    /// Costs rise with tier so specialization stays meaningful over long saves.
    /// </summary>
    public static class SkillCatalog
    {
        static List<SkillNode> nodes;

        public static IReadOnlyList<SkillNode> Nodes => nodes ?? (nodes = Build());

        public static SkillNode Find(string id)
        {
            foreach (var node in Nodes)
                if (node.id == id) return node;
            return null;
        }

        public static List<SkillNode> ForPillar(Pillar pillar)
        {
            var result = new List<SkillNode>();
            foreach (var node in Nodes)
                if (node.pillar == pillar) result.Add(node);
            return result;
        }

        static List<SkillNode> Build()
        {
            var list = new List<SkillNode>();

            void Add(string id, string name, Pillar pillar, int tier, SkillEffect effect,
                float magnitude, string description, params string[] prerequisites)
            {
                bool hybrid = false;
                foreach (var prerequisite in prerequisites)
                {
                    var parent = FindIn(list, prerequisite);
                    if (parent != null && parent.pillar != pillar) hybrid = true;
                }

                list.Add(new SkillNode
                {
                    id = id,
                    name = name,
                    pillar = pillar,
                    tier = tier,
                    cost = tier, // rising cost by tier (GDD §25.3)
                    effect = effect,
                    magnitude = magnitude,
                    description = description,
                    prerequisites = prerequisites,
                    isHybrid = hybrid
                });
            }

            // ---------------- Military ----------------
            Add("MIL_1", "Operational Planning", Pillar.Military, 1,
                SkillEffect.OperationEfficiency, 1f,
                "Planning staff prepare operations in advance. Operations cost 1 less CP.");
            Add("MIL_2", "Escalation Discipline", Pillar.Military, 2,
                SkillEffect.EscalationDiscipline, 1f,
                "Contingencies are pre-authorized. Abrupt escalation premiums are reduced.",
                "MIL_1");
            Add("MIL_3", "Coercive Credibility", Pillar.Military, 3,
                SkillEffect.SettlementLeverage, 8f,
                "Your threats are believed. Opponents accept terms sooner.",
                "MIL_2");

            // ---------------- Economy ----------------
            Add("ECO_1", "Sanctions Architecture", Pillar.Economy, 1,
                SkillEffect.CoercionEfficiency, 1f,
                "Prepared legal instruments. Imposing sanctions costs 1 less CP.");
            Add("ECO_2", "Exposure Management", Pillar.Economy, 2,
                SkillEffect.SanctionPrecision, 0.35f,
                "Carve-outs and substitutes. Your own sanctions blow back 35% less.",
                "ECO_1");
            Add("ECO_3", "Financial Statecraft", Pillar.Economy, 3,
                SkillEffect.PoliticalOperator, 0.4f,
                "Economic results translate into standing. +0.4 Political Capital per month.",
                "ECO_2");

            // ---------------- Intelligence ----------------
            Add("INT_1", "Analytical Rigor", Pillar.Intelligence, 1,
                SkillEffect.AnalyticalPrecision, 0.2f,
                "Better tradecraft in analysis. Your estimate margins narrow by 20%.");
            Add("INT_2", "Collection Tradecraft", Pillar.Intelligence, 2,
                SkillEffect.CollectionTradecraft, 1.2f,
                "Networks deepen faster. +1.2 penetration per month.",
                "INT_1");
            Add("INT_3", "Compartmentation", Pillar.Intelligence, 3,
                SkillEffect.Compartmentation, 0.4f,
                "Cells are isolated. Your operations are 40% less likely to be attributed.",
                "INT_2");
            Add("INT_4", "Covert Infrastructure", Pillar.Intelligence, 4,
                SkillEffect.CovertEfficiency, 1f,
                "Standing capability. Covert operations cost 1 less CP.",
                "INT_3");

            // ---------------- Diplomacy ----------------
            Add("DIP_1", "Standing Channels", Pillar.Diplomacy, 1,
                SkillEffect.OutreachEfficiency, 1f,
                "Permanent working groups. Diplomatic outreach is effectively free.");
            Add("DIP_2", "Negotiating Craft", Pillar.Diplomacy, 2,
                SkillEffect.TreatyPersuasion, 10f,
                "Terms drafted to suit the other side. Treaties are accepted more readily.",
                "DIP_1");
            Add("DIP_3", "Coalition Building", Pillar.Diplomacy, 3,
                SkillEffect.CoalitionPersuasion, 12f,
                "Burden-sharing arrangements. Partners join your coalitions more readily.",
                "DIP_2");

            // ---------------- Government ----------------
            Add("GOV_1", "Command Capacity", Pillar.Government, 1,
                SkillEffect.CommandCapacity, 1f,
                "Your office is properly staffed. +1 Command Point per month.");
            Add("GOV_2", "Strategic Reserve", Pillar.Government, 2,
                SkillEffect.StrategicReserve, 1f,
                "Unused capacity is banked. +1 to the Command Point reserve cap.",
                "GOV_1");
            Add("GOV_3", "Delegation Doctrine", Pillar.Government, 3,
                SkillEffect.DelegationBandwidth, 1f,
                "Standing guidance reaches the Cabinet. +1 Influence per month.",
                "GOV_2");

            // ---------------- Strategic verbs (GDD §25.3) ----------------
            // These unlock instruments the operator simply cannot reach without
            // the expertise — not discounts on things already available.
            Add("MIL_BASING", "Forward Basing", Pillar.Military, 3,
                SkillEffect.ForwardBasing, 1f,
                "Access agreements, prepositioned stocks and host-nation arrangements. " +
                "UNLOCKS the Forward posture.",
                "MIL_2");

            Add("ECO_STRATEGIC", "Strategic Industrial Base", Pillar.Economy, 4,
                SkillEffect.StrategicIndustry, 1f,
                "Yards, lines and skills that take a decade to build. " +
                "UNLOCKS Transformative procurement programs.",
                "ECO_2");

            Add("ECO_EXISTENTIAL", "Financial Blockade", Pillar.Economy, 5,
                SkillEffect.ExistentialMeasures, 1f,
                "Clearing systems, insurance and secondary sanctions. " +
                "UNLOCKS Existential economic measures.",
                "ECO_STRATEGIC", "DIP_2");

            Add("INT_DEEPCOVER", "Deep Cover Program", Pillar.Intelligence, 4,
                SkillEffect.DeepCoverProgram, 1f,
                "Legends built over years, held in reserve. UNLOCKS deception operations.",
                "INT_2");

            // ---------------- Additional depth ----------------
            Add("MIL_SUSTAINMENT", "Expeditionary Sustainment", Pillar.Military, 4,
                SkillEffect.OperationEfficiency, 1f,
                "Standing logistics for operations away from home. Operations cost 1 less CP again.",
                "MIL_BASING");

            Add("ECO_RESILIENCE", "Supply Resilience", Pillar.Economy, 3,
                SkillEffect.SanctionPrecision, 0.2f,
                "Stockpiles and alternate suppliers. Coercion costs us less to sustain.",
                "ECO_1");

            Add("INT_ALL_SOURCE", "All-Source Fusion", Pillar.Intelligence, 5,
                SkillEffect.AnalyticalPrecision, 0.15f,
                "Collection disciplines read against each other. Our margins narrow further.",
                "INT_4");

            Add("DIP_BACKCHANNEL", "Standing Backchannels", Pillar.Diplomacy, 4,
                SkillEffect.SettlementLeverage, 6f,
                "Quiet lines to people who can actually decide. Settlements come sooner.",
                "DIP_3");

            Add("GOV_MACHINERY", "Machinery of Government", Pillar.Government, 4,
                SkillEffect.PoliticalOperator, 0.5f,
                "The apparatus works for you rather than around you. +0.5 Political Capital per month.",
                "GOV_3");

            Add("GOV_CONTINUITY", "Continuity of Government", Pillar.Government, 5,
                SkillEffect.StrategicReserve, 1f,
                "Plans that survive a change of administration. +1 to the Command Point reserve cap.",
                "GOV_MACHINERY");

            // ---------------- Cross-pillar hybrids (GDD §25.3) ----------------
            Add("HYB_DETERRENCE", "Deterrence Doctrine", Pillar.Military, 4,
                SkillEffect.SettlementLeverage, 10f,
                "Force and diplomacy speak with one voice. Opponents concede far sooner.",
                "MIL_3", "DIP_2");

            Add("HYB_ECOWAR", "Economic Warfare", Pillar.Economy, 4,
                SkillEffect.SanctionPrecision, 0.3f,
                "Intelligence-guided targeting. Coercion is far cheaper to sustain.",
                "ECO_2", "INT_2");

            Add("HYB_POLWAR", "Political Warfare", Pillar.Intelligence, 5,
                SkillEffect.Compartmentation, 0.25f,
                "Covert and political instruments coordinate. Attribution is rarer still.",
                "INT_3", "GOV_2");

            Add("HYB_GRAND", "Grand Strategy", Pillar.Government, 5,
                SkillEffect.CommandCapacity, 1f,
                "Every instrument answers to one plan. +1 Command Point per month.",
                "GOV_3", "MIL_2", "ECO_1");

            return list;
        }

        static SkillNode FindIn(List<SkillNode> list, string id)
        {
            foreach (var node in list)
                if (node.id == id) return node;
            return null;
        }
    }
}

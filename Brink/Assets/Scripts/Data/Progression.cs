using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// What a Strategist skill actually does. Every effect expands the
    /// operator's knowledge, options or efficiency — none grant raw national
    /// power, which must still be built through investment, officials,
    /// technology, policy and events (GDD §25.3).
    /// </summary>
    public enum SkillEffect
    {
        CommandCapacity,          // +CP per month
        StrategicReserve,         // +CP reserve cap
        DelegationBandwidth,      // +Influence per month
        PoliticalOperator,        // +Political Capital per month

        OperationEfficiency,      // -CP per military operation
        EscalationDiscipline,     // -CP premium for abrupt escalation
        SettlementLeverage,       // opponents accept our terms sooner

        CoercionEfficiency,       // -CP to impose sanctions
        SanctionPrecision,        // -blowback from our own sanctions

        AnalyticalPrecision,      // tighter estimate margins for us
        CollectionTradecraft,     // faster network penetration growth
        CovertEfficiency,         // -CP for covert operations
        Compartmentation,         // -chance our operations are exposed

        OutreachEfficiency,       // -CP for diplomatic outreach
        TreatyPersuasion,         // treaties accepted more readily
        CoalitionPersuasion,      // partners join our coalitions more readily

        // ---- strategic verbs: instruments unavailable without the expertise ----
        ForwardBasing,            // unlocks Forward posture
        StrategicIndustry,        // unlocks Transformative procurement programs
        ExistentialMeasures,      // unlocks Existential sanctions
        DeepCoverProgram          // unlocks deception operations
    }

    /// <summary>One node in a branching Strategist tree (GDD §25.3).</summary>
    [Serializable]
    public class SkillNode
    {
        public string id;
        public string name;
        public string description;
        public Pillar pillar;
        public int tier;
        public int cost;
        public SkillEffect effect;
        public float magnitude;

        /// <summary>Node ids that must be unlocked first. Cross-pillar entries create hybrids.</summary>
        public string[] prerequisites = new string[0];

        /// <summary>True when this node requires nodes from another pillar's tree.</summary>
        public bool isHybrid;
    }

    /// <summary>Letter grade awarded by the annual evaluation (GDD §25.2).</summary>
    public enum EvaluationGrade
    {
        F,
        D,
        C,
        B,
        A,
        S
    }

    /// <summary>An archived annual evaluation.</summary>
    [Serializable]
    public class EvaluationRecord
    {
        public int year;
        public EvaluationGrade grade;
        public float score;
        public int skillPointsAwarded;
        public string summary;

        // Component scores, surfaced so the grade is never a black box.
        public float trajectoryScore;
        public float economyScore;
        public float stabilityScore;
        public float positionScore;
        public float crisisScore;
        public float initiativeScore;

        /// <summary>What the year cost relative to what it delivered (GDD §25.2).</summary>
        public float efficiencyScore;
    }

    /// <summary>
    /// Snapshot taken at the start of each in-game year. The annual evaluation
    /// grades movement relative to where the nation actually started, not
    /// against a conquest checklist (GDD §25.2).
    /// </summary>
    [Serializable]
    public class YearSnapshot
    {
        public int year;
        public float pillarTotal;
        public float gdp;
        public float treasury;
        public float approval;
        public float stability;
        public float unity;
        public float marketIndex;
        public int locationsHeld;
        public float relationsTotal;
        public int treatiesHeld;
        public int defensePactsHeld;
        public int crisesFaced;
        public int crisesResolved;
    }
}

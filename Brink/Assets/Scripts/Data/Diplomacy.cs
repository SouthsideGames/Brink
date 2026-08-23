using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Emergent relationship status (GDD §15.1). Never set directly — derived
    /// from the full relationship state, not from a single threshold.
    /// </summary>
    public enum RelationshipStatus
    {
        Hostile,
        Rival,
        Neutral,
        Cooperative,
        Friendly,
        StrategicPartner,
        Ally
    }

    /// <summary>Explicit commitments a treaty can contain (GDD §15.2).</summary>
    public enum TreatyCommitment
    {
        MutualDefense,
        IntelligenceSharing,
        Transit,
        JointPlanning,
        NonAggression,
        TradePreference
    }

    /// <summary>
    /// The six-dimension relationship between two countries (GDD §15.1).
    /// Stored once per unordered pair; dependence is asymmetric so it is
    /// tracked per direction.
    /// </summary>
    [Serializable]
    public class Relationship
    {
        public string countryA;
        public string countryB;

        public float relations = 50f;          // broad diplomatic temperature
        public float trust = 50f;              // belief commitments will be honored
        public float dependenceAOnB;           // asymmetric need
        public float dependenceBOnA;
        public float threatPerceptionOfA;      // how dangerous A looks to B
        public float threatPerceptionOfB;      // how dangerous B looks to A
        public float strategicAlignment = 50f; // do current interests point the same way

        /// <summary>Ability to operate together, built by joint exercises (GDD §15.3).</summary>
        public float interoperability;

        /// <summary>
        /// Knowledge of the other side's doctrine and procedures. Earned through
        /// exercises and retained even if the relationship later turns hostile.
        /// </summary>
        public float doctrineFamiliarity;

        /// <summary>Historical memory: durable record of what has passed between them.</summary>
        public List<string> memory = new List<string>();

        /// <summary>Weighted memory pressure; positive is goodwill, negative is grievance.</summary>
        public float memoryWeight;

        public bool Involves(string id) => countryA == id || countryB == id;
        public string PartnerOf(string id) => id == countryA ? countryB : countryA;

        public float DependenceOf(string id) => id == countryA ? dependenceAOnB : dependenceBOnA;
        public float ThreatPerceivedBy(string id) => id == countryA ? threatPerceptionOfB : threatPerceptionOfA;

        /// <summary>
        /// Set how much <paramref name="id"/> depends on the other party. Prefer
        /// this over the raw fields — which country is A is an implementation
        /// detail of world generation order, and getting it backwards is silent.
        /// </summary>
        public void SetDependenceOf(string id, float value)
        {
            if (id == countryA) dependenceAOnB = value;
            else dependenceBOnA = value;
        }

        /// <summary>Set how dangerous the other party looks to <paramref name="id"/>.</summary>
        public void SetThreatPerceivedBy(string id, float value)
        {
            if (id == countryA) threatPerceptionOfB = value;
            else threatPerceptionOfA = value;
        }

        public void AddMemory(GameDate date, string text, float weight)
        {
            memory.Add($"{date.SortKey} {text}");
            if (memory.Count > 30) memory.RemoveRange(0, memory.Count - 30);
            memoryWeight += weight;
        }
    }

    /// <summary>
    /// A treaty between two states. Commitments are explicit, and breaking them
    /// is possible but damages trustworthiness and future diplomacy (GDD §15.2).
    /// </summary>
    [Serializable]
    public class Treaty
    {
        public string id;
        public string countryA;
        public string countryB;
        public List<TreatyCommitment> commitments = new List<TreatyCommitment>();
        public GameDate signedDate;
        public bool broken;
        public string brokenBy = "";

        public bool Involves(string id) => countryA == id || countryB == id;
        public string PartnerOf(string id) => id == countryA ? countryB : countryA;
        public bool Has(TreatyCommitment commitment) => commitments.Contains(commitment);
    }

    /// <summary>
    /// A coalition assembled around a confrontation (GDD §15.2). Members join
    /// for their own reasons and can leave when their interests change.
    /// </summary>
    [Serializable]
    public class Coalition
    {
        public string id;
        public string leaderId;
        public string confrontationId;
        public string targetId;
        public List<string> memberIds = new List<string>();
        public bool dissolved;
    }
}

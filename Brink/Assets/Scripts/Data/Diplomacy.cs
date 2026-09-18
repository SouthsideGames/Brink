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
        TradePreference,

        /// <summary>
        /// Caps what either side may field and how far either may escalate
        /// against the other (spec 04 §5f). Appended — ordinals are persisted.
        /// </summary>
        ArmsControl = 6
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

        /// <summary>
        /// Whether this pair have recognised each other as sovereign (spec 04
        /// §5b). Only ever read for a **successor state** — one founded after
        /// world creation by `SecessionSystem`, which until now was the single
        /// thing in the game that creates a country and had no verb about it.
        ///
        /// `false` by default, and that is correct rather than merely empty: a
        /// breakaway starts unrecognised by everybody, which is the whole
        /// situation it has to work its way out of. Nothing asks the question
        /// about a state that was there at world creation, so no migration.
        /// </summary>
        public bool recognised;

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

        /// <summary>
        /// Months remaining of a negotiated détente: neither side imposes new
        /// sanctions on the other while it runs (spec 02 §4a). Opening a
        /// confrontation between the pair voids it — a détente does not survive
        /// a declaration of war. Zero on old saves is correct: no truce was
        /// ever negotiated.
        /// </summary>
        public int sanctionsTruceMonths;

        /// <summary>
        /// Months remaining in which neither side may open a new confrontation
        /// against the other, set by any settlement between them
        /// (`ConfrontationSystem.SettlementTruceMonths`). Zero on old saves is
        /// correct: nothing was settled.
        /// </summary>
        public int settlementTruceMonths;

        /// <summary>
        /// Months of preparation left before a convened summit meets (spec 04
        /// §5g), or zero.
        ///
        /// A summit is **preparation, not a button**: the months are the point,
        /// they are visible to anyone collecting on us, and the world can move
        /// underneath them — which is what makes convening one a commitment
        /// rather than an instant relationship purchase. Zero on old saves is
        /// correct; no migration.
        /// </summary>
        public int summitMonthsRemaining;

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
    /// <summary>
    /// Who carries a commitment (GDD §15.1 amendment).
    ///
    /// A treaty used to be a flat list that both sides implicitly received, so
    /// every agreement was symmetrical by construction and there was nothing to
    /// negotiate — pick a partner, pick commitments, signed. Real agreements are
    /// a back-and-forth in which each side is trying to receive more than it
    /// gives, and only genuinely friendly states settle for even.
    ///
    /// Append-only: persisted by ordinal, `Mutual` first so an old save's clauses
    /// read as reciprocal, which is what they were.
    /// </summary>
    public enum ClauseSide
    {
        /// <summary>Both sides carry it. The even bargain.</summary>
        Mutual = 0,

        /// <summary>They carry it for us. We receive.</summary>
        TheyProvide = 1,

        /// <summary>We carry it for them. We give.</summary>
        WeProvide = 2
    }

    /// <summary>
    /// The circumstance under which a treaty clause applies. Append-only: the
    /// zero value keeps every clause written before conditional agreements
    /// permanent and unconditional.
    /// </summary>
    public enum TreatyClauseTrigger
    {
        Always = 0,
        ConflictWithCountry = 1
    }

    /// <summary>One commitment in a treaty, and which side actually bears it.</summary>
    [Serializable]
    public class TreatyClause
    {
        public TreatyCommitment commitment;
        public ClauseSide side = ClauseSide.Mutual;

        /// <summary>Optional live condition; see <see cref="Treaty.ClauseIsActive"/>.</summary>
        public TreatyClauseTrigger trigger = TreatyClauseTrigger.Always;

        /// <summary>Named third state for <see cref="TreatyClauseTrigger.ConflictWithCountry"/>.</summary>
        public string triggerCountryId = "";

        /// <summary>Months from signature; zero means the clause does not expire.</summary>
        public int durationMonths;
    }

    [Serializable]
    public class Treaty
    {
        public string id;
        public string countryA;
        public string countryB;
        public List<TreatyCommitment> commitments = new List<TreatyCommitment>();

        /// <summary>
        /// The same commitments with their side recorded. Kept alongside the flat
        /// list rather than replacing it: ~20 call sites ask `Has(commitment)` and
        /// do not care who carries it, and rewriting them all to satisfy a data
        /// model change would be a large diff for no behaviour.
        ///
        /// Sides are always written relative to <see cref="countryA"/>.
        /// </summary>
        public List<TreatyClause> clauses = new List<TreatyClause>();

        public GameDate signedDate;
        public bool broken;
        public string brokenBy = "";

        public bool Involves(string id) => countryA == id || countryB == id;
        public string PartnerOf(string id) => id == countryA ? countryB : countryA;
        public bool Has(TreatyCommitment commitment) => commitments.Contains(commitment);

        /// <summary>
        /// Whether this country carries the commitment. Old treaties have no
        /// clause sides and therefore remain mutual through <see cref="SideFor"/>.
        /// </summary>
        public bool Carries(string countryId, TreatyCommitment commitment)
        {
            if (!Involves(countryId) || !Has(commitment)) return false;
            ClauseSide side = SideFor(countryId, commitment);
            return side == ClauseSide.Mutual || side == ClauseSide.WeProvide;
        }

        /// <summary>Whether this country presently carries an active commitment.</summary>
        public bool Carries(GameState state, string countryId, TreatyCommitment commitment)
            => ClauseIsActive(state, commitment) && Carries(countryId, commitment);

        /// <summary>Whether this country receives the commitment's benefit.</summary>
        public bool Receives(string countryId, TreatyCommitment commitment)
        {
            if (!Involves(countryId) || !Has(commitment)) return false;
            ClauseSide side = SideFor(countryId, commitment);
            return side == ClauseSide.Mutual || side == ClauseSide.TheyProvide;
        }

        /// <summary>Whether this country presently receives an active commitment.</summary>
        public bool Receives(GameState state, string countryId, TreatyCommitment commitment)
            => ClauseIsActive(state, commitment) && Receives(countryId, commitment);

        /// <summary>
        /// One authoritative activation rule for every mechanical consumer.
        /// Missing clause records are legacy mutual promises and remain active.
        /// A conflict trigger means either signatory is presently fighting the
        /// named third state; expiry is evaluated independently, so both compose.
        /// </summary>
        public bool ClauseIsActive(GameState state, TreatyCommitment commitment)
        {
            if (state == null || broken || !Has(commitment)) return false;

            TreatyClause clause = null;
            foreach (var candidate in clauses)
                if (candidate.commitment == commitment) { clause = candidate; break; }
            if (clause == null) return true;

            if (clause.durationMonths > 0
                && state.date.MonthsSince(signedDate) >= clause.durationMonths) return false;

            if (clause.trigger == TreatyClauseTrigger.Always) return true;
            if (string.IsNullOrEmpty(clause.triggerCountryId)
                || clause.triggerCountryId == countryA || clause.triggerCountryId == countryB)
                return false;

            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || confrontation.escalation < EscalationState.LimitedConflict)
                    continue;
                if (!confrontation.Involves(clause.triggerCountryId)) continue;
                if (confrontation.Involves(countryA) || confrontation.Involves(countryB)) return true;
            }
            return false;
        }

        public GameDate ClauseExpiry(TreatyCommitment commitment)
        {
            foreach (var clause in clauses)
            {
                if (clause.commitment != commitment || clause.durationMonths <= 0) continue;
                int total = signedDate.year * 12 + signedDate.month - 1 + clause.durationMonths;
                return new GameDate(total / 12, total % 12 + 1);
            }
            return default;
        }

        /// <summary>Which side carries a commitment, as seen by <paramref name="viewerId"/>.</summary>
        public ClauseSide SideFor(string viewerId, TreatyCommitment commitment)
        {
            foreach (var clause in clauses)
            {
                if (clause.commitment != commitment) continue;
                if (clause.side == ClauseSide.Mutual || viewerId == countryA) return clause.side;
                return clause.side == ClauseSide.TheyProvide
                    ? ClauseSide.WeProvide : ClauseSide.TheyProvide;
            }
            return ClauseSide.Mutual;
        }
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

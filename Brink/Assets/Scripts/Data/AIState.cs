using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Difficulty changes AI *reasoning quality* — planning horizon, cross-pillar
    /// coordination, opportunity recognition — not hidden stat bonuses (GDD §24.3).
    /// </summary>
    public enum Difficulty
    {
        Standard,
        Challenging,
        Ruthless
    }

    /// <summary>Strategic objectives an AI government can pursue (GDD §24.1).</summary>
    public enum AIObjectiveType
    {
        BuildCapability,   // invest in our own pillars
        SecureResources,   // fix a dependency or shortage
        CounterRival,      // collect on, coerce, or contain a threatening state
        ExpandInfluence,   // treaties and diplomatic reach
        AssertClaim,       // press a confrontation for a concrete objective
        ConsolidateHome,   // stabilize domestic politics

        // Appended, never reordered — JsonUtility persists enums by ordinal, so
        // inserting above would silently reinterpret every saved objective.
        PreemptProgramme,  // a rival is close to an instrument that ends us

        // Counter-play. These exist because the AI's read of another government
        // was computed every month and then acted on by almost nothing: a player
        // could be assessed as maximally dangerous and every state would carry on
        // exactly as before. A prediction that changes no decision is not a
        // prediction, it is telemetry.
        HardenDefenses,    // we expect to be attacked
        InsulateEconomy,   // we expect to be squeezed
        HardenSecurity     // we expect to be penetrated
    }

    /// <summary>
    /// A government's theory of how it ends up on top (GDD §24.1).
    ///
    /// Without one, an AI is a list of month-to-month reactions and its behaviour
    /// over a decade averages out to nothing in particular. A path makes a
    /// government legible — China building for economic primacy behaves
    /// differently from China building for military dominance, and the player can
    /// work out which they are facing and play against it.
    ///
    /// Appended, never reordered.
    /// </summary>
    public enum StrategicPath
    {
        /// <summary>Out-produce and out-trade everyone. Wars are interruptions.</summary>
        EconomicPrimacy,

        /// <summary>Be the strongest, and let everyone know it.</summary>
        MilitaryDominance,

        /// <summary>Own the neighbourhood. Global standing is somebody else's problem.</summary>
        RegionalHegemony,

        /// <summary>Win on capability rather than mass.</summary>
        TechnologicalEdge,

        /// <summary>Be indispensable. Treaties, coalitions, everyone's second choice.</summary>
        InstitutionalWeight,

        /// <summary>Outlast. For the weak, the surrounded and the recently beaten.</summary>
        Survival
    }

    /// <summary>
    /// What one government expects another to do next (GDD §24.2).
    ///
    /// Deliberately coarse. The AI is not simulating the player's turn; it is
    /// forming the kind of expectation a real staff forms — "they are going to
    /// come at us", "they are going to squeeze us" — and acting on it early
    /// enough to matter.
    /// </summary>
    public enum PredictedMove
    {
        Unknown,
        Hold,      // no particular move expected
        Attack,    // military pressure, probably on us
        Coerce,    // sanctions, tariffs, economic pressure
        Subvert,   // covert action against us
        Court,     // building a coalition or courting our partners
        Build      // heads down, growing capability
    }

    /// <summary>
    /// One government's read of another (GDD §24.2).
    ///
    /// Built only from what the observer could actually see — public acts, and
    /// covert acts that were exposed — so a state with poor collection forms poor
    /// expectations. That is what keeps intelligence worth buying and deception
    /// worth running: this is the thing they operate on.
    ///
    /// Not machine learning, and deliberately exploitable. A player who
    /// establishes a pattern and then breaks it should catch the world out, which
    /// is the whole point of having the model be observational.
    /// </summary>
    [Serializable]
    public class OpponentModel
    {
        public string countryId;

        // 0..100 exponentially-weighted reads of observed behaviour, so recent
        // conduct outweighs ancient history without ever fully erasing it.
        public float aggression;
        public float economicCoercion;
        public float covertActivity;
        public float diplomaticActivity;

        /// <summary>What we think they are trying to achieve.</summary>
        public StrategicPath predictedPath;

        /// <summary>What we think they will do next.</summary>
        public PredictedMove predictedMove;

        /// <summary>0..1 how much we trust the reading, from collection quality.</summary>
        public float confidence;

        /// <summary>Months of observation behind this model.</summary>
        public int observations;
    }

    /// <summary>
    /// Personality that shapes how a government reasons. Leaders can make
    /// mistakes through aggression, caution, or ideology (GDD §24.1), and the
    /// spread across countries keeps the AI from converging into one optimizer.
    /// </summary>
    [Serializable]
    public class AIProfile
    {
        public float aggression;   // willingness to coerce and escalate
        public float caution;      // reluctance to act on thin information
        public float opportunism;  // appetite for exploiting weakness
        public float patience;     // tolerance for long, slow strategies
    }

    /// <summary>An objective the AI is currently pursuing.</summary>
    [Serializable]
    public class AIObjective
    {
        public AIObjectiveType type;
        public string targetId;
        public float priority;
        public int monthsPursued;
    }

    /// <summary>
    /// Systemic pattern recognition of the player's behavior (GDD §24.2).
    /// Counts of observed actions — not machine learning, and deliberately
    /// exploitable: a player can establish a pattern and then break it.
    /// </summary>
    [Serializable]
    public class PlayerAssessment
    {
        public int observedAggressiveActs;
        public int observedCovertActs;
        public int observedEconomicCoercion;
        public int observedCooperativeActs;

        /// <summary>0..100 read of how dangerous the player's pattern looks.</summary>
        public float perceivedAggression;
    }

    /// <summary>
    /// How long a government has regarded another state as a rival. Objectives
    /// churn every few months as priorities are re-scored; a rivalry is the
    /// slower thing underneath them, and it is what justifies decade-long
    /// commitments like a strategic programme.
    /// </summary>
    [Serializable]
    public class Rivalry
    {
        public string countryId;

        /// <summary>Months this state has been treated as a rival, net of relief.</summary>
        public int months;

        /// <summary>Set each month the rivalry is still being actively pursued.</summary>
        public bool stillRival;

        /// <summary>Quiet months accumulated toward forgetting a month of rivalry.</summary>
        public int coolOff;
    }

    /// <summary>Per-country AI reasoning state.</summary>
    [Serializable]
    public class AIState
    {
        public string countryId;
        public AIProfile profile = new AIProfile();
        public List<AIObjective> objectives = new List<AIObjective>();
        public PlayerAssessment playerAssessment = new PlayerAssessment();
        public List<Rivalry> rivalries = new List<Rivalry>();

        /// <summary>Actions taken this month, capped by reasoning quality.</summary>
        public int actionsThisMonth;

        /// <summary>
        /// This government's domestic political budget — the AI's half of the
        /// Political Capital economy the player runs from `GameState`.
        ///
        /// It exists because the alternative was worse: AI governments used to
        /// buy stability, approval, energy and materials by writing the numbers
        /// directly, every month, for nothing. That is the mirror of the bug
        /// class this project has already fixed once (AI states locked *out* of
        /// player verbs) — here they had verbs the player does not, at a price
        /// the player cannot match, which is the same asymmetry pointing the
        /// other way.
        ///
        /// Stored here rather than on `CountryState` so the player's existing
        /// `GameState.politicalCapital` and its save schema stay untouched. Both
        /// pools are filled by the same income formula
        /// (<c>GovernmentSystem.PoliticalCapitalIncomeFor</c>), so a government
        /// with strong approval and a cohesive elite is well funded no matter who
        /// is running it.
        /// </summary>
        public float politicalCapital;

        /// <summary>
        /// This government's theory of how it wins (GDD §24.1). Chosen from its
        /// own endowments, and revisited when the world stops cooperating.
        /// </summary>
        public StrategicPath path;

        /// <summary>Months on the current path. Governments do not pivot lightly.</summary>
        public int monthsOnPath;

        /// <summary>0..100 how well the path is actually going. Sustained failure forces a rethink.</summary>
        public float pathProgress = 50f;

        /// <summary>
        /// A hidden per-game shift in how this government weighs the paths.
        ///
        /// Two countries with similar endowments would otherwise always reach the
        /// same conclusion, and a player who learned "Germany always goes
        /// institutional" would be right in every playthrough forever. This is not
        /// the main defence against that — counter-play is — but it stops the
        /// opening of every game from being identical.
        /// </summary>
        public float disposition;

        /// <summary>What this government believes about everyone else (GDD §24.2).</summary>
        public List<OpponentModel> opponentModels = new List<OpponentModel>();

        public int RivalryMonths(string otherId)
        {
            for (int i = 0; i < rivalries.Count; i++)
                if (rivalries[i].countryId == otherId) return rivalries[i].months;
            return 0;
        }

        public OpponentModel ModelOf(string otherId)
        {
            for (int i = 0; i < opponentModels.Count; i++)
                if (opponentModels[i].countryId == otherId) return opponentModels[i];
            return null;
        }
    }
}

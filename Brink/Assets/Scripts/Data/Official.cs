using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>Ministry-owned experience, independent of whoever holds the seat.</summary>
    [Serializable]
    public class InstitutionalMemory
    {
        public Pillar office;
        public int successes;
        public int setbacks;
        public float riskImprint;
        public GameDate lastOutcome;
    }

    /// <summary>Control modes for a pillar official (GDD §7.2).</summary>
    /// <summary>
    /// One line of what a delegated official did this month (GDD §8, §28.1).
    ///
    /// Delegation used to be silent: an official on Autonomous applied their
    /// effect and the operator saw only that some numbers had moved. That makes
    /// handing a pillar over feel like switching it off rather than like
    /// employing somebody. If they are acting in your name, you should be able to
    /// read what they did.
    /// </summary>
    [Serializable]
    public class CabinetReportLine
    {
        public Pillar pillar;
        public string officialName = "";
        public string summary = "";

        /// <summary>True when the official chose it; false when working to your instruction.</summary>
        public bool ownJudgement;
    }

    public enum ControlMode
    {
        Autonomous,   // official chooses priorities/actions; 0 CP
        Directed,     // player states the outcome; official executes; costs Influence
        DirectControl // player acts personally; costs Command Points
    }

    /// <summary>
    /// A pillar leader (GDD §8): advisor and optional autonomous manager.
    /// Phase 3 models the headline personality axes; traits, relationships,
    /// aging and succession arrive with later phases.
    /// </summary>
    [Serializable]
    public class Official
    {
        public string id;
        public string displayName;

        /// <summary>Office title appropriate to the government system, e.g. "Secretary of Defense".</summary>
        public string title;

        public Pillar office;

        public float competence;    // 0..100 — quality of autonomous execution
        public float loyalty;       // 0..100 — softens trust damage from overrides
        public float riskTolerance; // 0..100 — variance and event appetite

        /// <summary>Relationship state, not spendable currency (GDD §7.3).</summary>
        public float trust;

        public int monthsInOffice;

        /// <summary>
        /// Years. Officials age, retire and occasionally die in office
        /// (GDD §7.3), so a cabinet is something that turns over on its own
        /// rather than a fixed roster you only change by firing someone.
        /// </summary>
        public float age;

        public ControlMode mode = ControlMode.Autonomous;

        /// <summary>Active directive id when mode == Directed; empty otherwise.</summary>
        public string directiveId = "";

        /// <summary>
        /// A state this official is posted to as standing envoy (spec 04 §5e),
        /// or empty.
        ///
        /// Only the diplomatic desk is ever posted. It is what makes
        /// `competence` matter in a *fifth* place — until now it decided
        /// autonomous execution, what reached the terminal, the advice a
        /// minister gives, and how much a directive achieved, all of which are
        /// about the pillar rather than about a relationship.
        ///
        /// Empty on an old save is correct: nobody was posted anywhere in a
        /// world with no way to post them. No migration.
        /// </summary>
        public string envoyToCountryId = "";

        /// <summary>
        /// Whether we have already told the operator this instruction was met.
        ///
        /// Without it the completion notice would fire every month the goal is
        /// still satisfied, which turns "the force is ready" from news into
        /// wallpaper. Cleared whenever the directive changes, so re-issuing an
        /// instruction can genuinely complete again.
        /// </summary>
        public bool directiveCompletionReported;

        /// <summary>
        /// Who, if anyone, this official is quietly working for (GDD §14
        /// amendment). Empty for the overwhelming majority.
        ///
        /// The foreign cabinet dossier already renders every rival minister's
        /// name and a competence band, gated by penetration, with a comment
        /// explaining why a rival's incompetent economy minister "is a real,
        /// exploitable fact about them". Nothing in the game could act on it — a
        /// surface with no verb. `IntelligenceSystem` can now reach the person.
        ///
        /// Kept on the official rather than in a separate roster so it travels
        /// with them: a recruited minister who is reshuffled into another office
        /// is still ours, and one who retires takes the access with them. That is
        /// the whole risk of running an agent inside somebody else's cabinet.
        /// </summary>
        public string recruitedById = "";

        /// <summary>
        /// How far along an approach is, 0..100. Recruitment is not a dice roll
        /// on a stranger — it is months of cultivation that can be abandoned or
        /// discovered before it ever pays.
        /// </summary>
        public float cultivation;
    }

    /// <summary>Why a cabinet seat came open. Shapes how it reads and what it costs.</summary>
    public enum VacancyReason
    {
        Retirement,
        Death,
        Dismissal
    }

    /// <summary>
    /// One name on a shortlist for a vacant office (GDD §7.3).
    ///
    /// Candidates exist so that appointing is a **decision with a trade-off**
    /// rather than a dice roll. The GDD is explicit that the technically
    /// strongest candidate may be politically or strategically incompatible, so
    /// the pool is generated to contain genuine tensions — the brilliant
    /// operator nobody trusts, the safe pair of hands who will never excel —
    /// instead of three samples from one distribution.
    /// </summary>
    [Serializable]
    public class OfficialCandidate
    {
        public string displayName;
        public float competence;
        public float loyalty;
        public float riskTolerance;
        public float age;

        /// <summary>One line of dossier that tells the player what they are choosing.</summary>
        public string background;
    }

    /// <summary>An office standing empty, with its shortlist.</summary>
    [Serializable]
    public class CabinetVacancy
    {
        public Pillar office;
        public VacancyReason reason;
        public int monthsOpen;
        public List<OfficialCandidate> candidates = new List<OfficialCandidate>();
    }
}

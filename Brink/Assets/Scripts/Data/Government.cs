using System;

namespace Brink.Data
{
    /// <summary>
    /// Structural government types (GDD §13). These are institutional
    /// descriptions that change *how power works* — who can be dismissed, how
    /// leaders are replaced, what emergency authority costs — not flavor labels
    /// or judgments about any real government.
    /// </summary>
    public enum GovernmentType
    {
        PresidentialRepublic,  // fixed terms, separate legislature
        ParliamentaryRepublic, // government falls with confidence, early elections possible
        DominantPartyState,    // internal party succession, high institutional continuity
        CentralizedRepublic,   // executive-dominant, elite-brokered succession
        Monarchy               // hereditary succession
    }

    /// <summary>
    /// How the state holds its own society (GDD §12, §13).
    ///
    /// The government pillar's standing choice, and the thing it most obviously
    /// lacked: the military has posture and doctrine, the economy has trade
    /// policy, and government had only one-off interventions. A posture is a
    /// position you hold and pay for every month, which is what makes it a
    /// different kind of decision from an action you take once.
    ///
    /// `Standard` is declared first so that its ordinal is zero: an old save
    /// deserializes to the neutral setting rather than to whichever option
    /// happened to be written at the top. Display order is chosen by the view.
    /// </summary>
    public enum CivicPosture
    {
        /// <summary>The ordinary settlement between the state and the public.</summary>
        Standard,

        /// <summary>
        /// Open civic space. Unity and approval run higher; dissent organises
        /// more freely, and so does conspiracy.
        /// </summary>
        Open,

        /// <summary>
        /// Managed civic space. Order and command loyalty run higher and plots
        /// struggle to form; legitimacy erodes, and the apparatus costs
        /// political authority every month to maintain.
        /// </summary>
        Restrictive
    }

    /// <summary>
    /// The national strategic priority a leadership pursues (GDD §13: major
    /// policy settings influence autonomous officials).
    /// </summary>
    public enum NationalPriority
    {
        Security,
        Prosperity,
        Influence,
        Cohesion
    }

    /// <summary>
    /// A head of government. Leaders change while the save — and the player's
    /// tenure as strategic operator — continues (GDD §13).
    /// </summary>
    [Serializable]
    public class Leader
    {
        public string name;
        public string faction;
        public NationalPriority priority;
        public float competence;
        public float age;
        public int monthsInOffice;

        /// <summary>Completed terms. Long incumbency breeds anti-incumbent sentiment.</summary>
        public int termsServed;
    }

    /// <summary>
    /// Government institutions and political condition for one country.
    /// </summary>
    [Serializable]
    public class GovernmentState
    {
        public GovernmentType type;
        public Leader leader = new Leader();

        /// <summary>Full term length in months (elective systems).</summary>
        public int termLengthMonths = 48;

        /// <summary>Scheduled election (elective systems only).</summary>
        public GameDate nextElectionDate;

        /// <summary>
        /// Consecutive terms one leader may serve; 0 means no limit. A structural
        /// property of the system, not a modifier — a presidential republic
        /// changes leaders on schedule however popular the incumbent is.
        /// </summary>
        public int consecutiveTermLimit;

        /// <summary>Legislative/coalition backing, 0..100 (elective systems).</summary>
        public float legislativeSupport = 55f;

        /// <summary>Cohesion of the ruling elite, 0..100 (non-elective systems).</summary>
        public float eliteCohesion = 65f;

        public bool emergencyPowers;
        public int emergencyPowersMonthsRemaining;

        /// <summary>How the state currently holds its own society (GDD §12).</summary>
        public CivicPosture civicPosture;

        /// <summary>
        /// 0..100 support bought rather than earned — bargained out of the
        /// legislature or brokered with the elite.
        ///
        /// This exists because `legislativeSupport` and `eliteCohesion` both
        /// *drift toward a computed target* every month, so a verb that added to
        /// them directly would be erased before the player could feel it. That is
        /// the same trap that made occupation's readiness cost dead code. This
        /// field moves the **target**, and decays, so support has to be
        /// maintained rather than bought once.
        /// </summary>
        public float brokeredSupport;

        /// <summary>
        /// 0..100 how ready a successor is to take over (GDD §13).
        ///
        /// Administrations come and go while the operator persists, and until now
        /// a transition was purely something that happened *to* the player.
        /// Preparing for one is the most characteristically governmental thing
        /// there is. Consumed at the transition it was built for.
        /// </summary>
        public float successorReadiness;

        /// <summary>
        /// Pillars whose constitutional authority the operator has permanently
        /// raised, as a bitmask over <see cref="Pillar"/>.
        ///
        /// Zero is correct for an old save — nobody had bought one — so this
        /// needs no migration. Unlike everything else in this class it describes
        /// the *operator's* standing rather than the state's, which is why it is
        /// player-facing only: what a state can do is national power, and what
        /// the operator may personally command is not.
        /// </summary>
        public int authorityUpgradeMask;

        // ---- regime security (GDD §22) ----

        /// <summary>0..100 loyalty of the officer corps to the civil authority.</summary>
        public float militaryLoyalty = 65f;

        /// <summary>
        /// 0..100 accumulated conspiratorial organization. Grows from genuine
        /// domestic grievance; foreign action can accelerate it but cannot
        /// manufacture it (GDD §22).
        /// </summary>
        public float conspiracyLevel;

        /// <summary>Country id backing the conspiracy, if any. Empty for purely domestic plots.</summary>
        public string conspiracyBackerId = "";

        /// <summary>Set while the state is fighting itself.</summary>
        public bool inCivilConflict;
        public int civilConflictMonthsRemaining;

        /// <summary>
        /// Months this civil conflict has run. Needed because fragmentation
        /// depends on how long a state has been failing to win, and the
        /// remaining-months countdown cannot answer that.
        /// </summary>
        public int civilConflictMonthsElapsed;

        /// <summary>How many times leadership has been seized by force in this save.</summary>
        public int coupsExperienced;

        /// <summary>True when leadership is chosen at the ballot box.</summary>
        public bool IsElective =>
            type == GovernmentType.PresidentialRepublic || type == GovernmentType.ParliamentaryRepublic;

        /// <summary>Parliamentary governments can fall or go to the country early.</summary>
        public bool AllowsEarlyElection => type == GovernmentType.ParliamentaryRepublic;

        public string TypeText
        {
            get
            {
                switch (type)
                {
                    case GovernmentType.PresidentialRepublic: return "PRESIDENTIAL REPUBLIC";
                    case GovernmentType.ParliamentaryRepublic: return "PARLIAMENTARY REPUBLIC";
                    case GovernmentType.DominantPartyState: return "DOMINANT-PARTY STATE";
                    case GovernmentType.CentralizedRepublic: return "CENTRALIZED REPUBLIC";
                    default: return "MONARCHY";
                }
            }
        }
    }
}

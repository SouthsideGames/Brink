using System.Collections.Generic;
using System;

namespace Brink.Data
{
    /// <summary>
    /// What an opposition is campaigning on (GDD §13).
    ///
    /// `Drift` is declared first so its ordinal is zero and a save written
    /// before oppositions existed deserializes to the vaguest, least consequential
    /// case rather than to an accusation nobody made.
    /// </summary>
    public enum OppositionTheme
    {
        /// <summary>"They have run out of ideas." The residual case, always available.</summary>
        Drift = 0,

        /// <summary>"People cannot afford to live." The material case.</summary>
        Hardship = 1,

        /// <summary>"End it." Against a war that is costing more than it is winning.</summary>
        War = 2,

        /// <summary>"They have hollowed out the state." Against how power is being used.</summary>
        Corruption = 3,

        /// <summary>"They are governing by fiat." Against the boot, not the budget.</summary>
        Liberty = 4
    }

    /// <summary>
    /// Structural government types (GDD §13). These are institutional
    /// descriptions that change *how power works* — who can be dismissed, how
    /// leaders are replaced, what emergency authority costs — not flavor labels
    /// or judgments about any real government.
    /// </summary>
    /// <summary>
    /// One bloc inside the government's own coalition (spec 05 §2g).
    ///
    /// The point of naming them is that **they do not all want the same thing**:
    /// a bloc's `theme` says what would win it over, and it is the same
    /// vocabulary the opposition campaigns in, so the case being made against a
    /// government and the constituencies inside it are described in one language.
    /// </summary>
    [Serializable]
    public class Faction
    {
        public string name;

        /// <summary>What this bloc cares about. Drives which instrument moves it.</summary>
        public OppositionTheme theme;

        /// <summary>Share of the chamber or the elite, 0..1. Sums to ~1 across the list.</summary>
        public float share;

        /// <summary>How well disposed to the government, 0..100.</summary>
        public float disposition = 50f;
    }

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
        /// 0..100 how strong the argument against this government has become
        /// (GDD §13, §27).
        ///
        /// **Faction was arithmetic and the opposition still did nothing.** A
        /// turnover government took a support penalty, a chamber could withdraw
        /// confidence, and an election was a roll against incumbency fatigue —
        /// but nobody was ever *campaigning*. Nothing accumulated a case, nothing
        /// chose an issue, and there was no way to answer one.
        ///
        /// Drifts toward `OppositionSystem.CaseTargetFor`, so it is built out of
        /// the government's actual record and comes apart when the record
        /// improves. Zero on an old save is right: a government that has not been
        /// campaigned against has nothing standing against it yet.
        /// </summary>
        public float oppositionCase;

        /// <summary>
        /// What they are campaigning on. Decides which answer works — the whole
        /// point of storing it rather than deriving a number.
        /// </summary>
        public OppositionTheme oppositionTheme;

        /// <summary>Consecutive months this case has stood above the noise.</summary>
        public int oppositionMonths;

        /// <summary>
        /// Standing built by addressing the public directly, 0..100.
        ///
        /// **The sixth instance of this codebase's most persistent bug family**,
        /// and the most damaging one, because it sat on the pillar's cheapest and
        /// most repeatable verb. `PublicMessagingBy` wrote `+4` straight into
        /// `governmentApproval` and `+1.5` into `nationalUnity` — both of which
        /// `Approach` a computed target every month, and neither target contained
        /// any messaging term.
        ///
        /// **It failed in both directions at once, which is why it survived so
        /// long.** `Approach` pulls back a fixed *fraction of the gap*, so what
        /// the verb did depended entirely on how often it was used:
        /// - Used occasionally — the intended way — a campaign half-lifed in about
        ///   eleven months and left nothing behind. Reported from play as "is
        ///   there any way I can boost these things?", which was a fair reading,
        ///   because functionally there was not.
        /// - Used every month, a gain of ~5.6 against a 6% pull-back reaches
        ///   equilibrium ninety-odd points above target — i.e. **clamped at 100**.
        ///   Spamming the cheapest verb in the pillar pinned approval at maximum
        ///   permanently.
        ///
        /// A lever that is worthless used sensibly and degenerate when spammed is
        /// the exact opposite of a decision, and both halves have the same cause.
        /// A saturating reservoir removes both: frequency now changes how quickly
        /// you approach a bounded benefit rather than how far past it you can go.
        ///
        /// Same shape as <see cref="brokeredSupport"/>: a decaying reservoir that
        /// is a *term in the target*. Decays faster than brokered support does —
        /// rhetoric fades quicker than patronage, so this is a campaign a
        /// government keeps running rather than a speech it gives once.
        ///
        /// Defaults to zero, which is correct rather than merely empty: a state
        /// that has run no campaign carries no residue. No save migration needed.
        /// </summary>
        public float publicMessaging;

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
        /// <summary>
        /// How much of the state runs on favours rather than on rules, 0..100
        /// (spec 05 §2e).
        ///
        /// `DistributePatronage` has always said in its own comment that it
        /// "hollows the state out if it becomes the habitual instrument", and
        /// nothing recorded that it had. `OppositionTheme.Corruption` existed and
        /// was proxied by a weak government pillar plus conspiracy plus poor
        /// cohesion — which measures *the state being feeble*, not the state
        /// being bought.
        ///
        /// A store with **proportional decay**, like `publicGrievance`: every
        /// level of it has a resting point, so a government that stops buying
        /// support recovers and one that never starts is unaffected. Zero on an
        /// old save is correct — nothing had been recorded — so no migration.
        /// </summary>
        public float corruption;

        /// <summary>
        /// The blocs whose support this government actually rests on (spec 05
        /// §2g).
        ///
        /// **A lens on `legislativeSupport`, not a replacement for it.** That
        /// field has 36 read and write sites across eleven systems; turning it
        /// into a derived sum would break all eight writers for no behaviour.
        /// Instead the ledger contributes a term to the *support target* the way
        /// `brokeredSupport` already does, so courting a particular bloc is what
        /// moves the number rather than an abstract "build support" that lands
        /// nowhere in particular.
        ///
        /// Empty on an old save is correct — `GovernmentSystem.EnsureFactions`
        /// seeds them lazily on first use, so there is no migration.
        /// </summary>
        public List<Faction> factions = new List<Faction>();

        // ---- constitutional change (spec 05 §2f) ----
        //
        // `GovernmentType` decides how power works — elective or internal
        // succession, term limits, what emergency powers cost, whether an early
        // election is even possible — and it was fixed for the whole of a
        // decades-long save. The pillar's largest missing verb, and the natural
        // large purchase for a Political Capital economy whose only sink above
        // 7 was `ConsolidateAuthority` at 12.

        /// <summary>What we are trying to become, or the current type if nothing is under way.</summary>
        public GovernmentType constitutionalTarget;

        /// <summary>Months left in the attempt. Zero when nothing is under way.</summary>
        public int constitutionalMonthsRemaining;

        /// <summary>
        /// Accumulated backing for the change, 0..100. Built by the monthly
        /// spend and eroded by whoever loses from it; the attempt succeeds or
        /// fails on where this stands when the clock runs out.
        /// </summary>
        public float constitutionalSupport;

        public bool ChangingConstitution => constitutionalMonthsRemaining > 0;

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

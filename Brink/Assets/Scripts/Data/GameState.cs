using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Root persistent object for one save. Everything the simulation needs to
    /// resume must live here (or be derivable from here) so save/load stays a
    /// single-object serialization (GDD §30, §36 Save/Data Schema).
    /// </summary>
    [Serializable]
    public class GameState
    {
        /// <summary>
        /// Schema version for migration. Bump on breaking changes.
        ///
        /// Defaults to 1 — the oldest schema — deliberately: a JSON blob with no
        /// version field at all is by definition from before versioning, and
        /// must migrate forward rather than be trusted as current. A freshly
        /// created world is stamped with the build's version by `WorldFactory`.
        /// </summary>
        public int saveVersion = 1;

        /// <summary>Deterministic seed for controlled randomness within this save.</summary>
        public int rngSeed;

        public GameDate date = new GameDate(1984, 1);
        public GameDate startDate = new GameDate(1984, 1);

        public string playerCountryId;
        public List<CountryState> countries = new List<CountryState>();

        /// <summary>
        /// How much of the authored world this save opened with (GDD §31.2
        /// amendment). Fixed at creation — composition is a fact about the
        /// playthrough. `Standard`'s ordinal is zero, so a save written before
        /// the field existed deserializes to the world it was actually built in.
        /// </summary>
        public WorldSize worldSize;

        public CommandPointsState commandPoints = new CommandPointsState();

        /// <summary>
        /// The player's Cabinet (GDD §8).
        ///
        /// A **view** onto the player country's own cabinet, not a separate
        /// list. Every government has five officials now, so a cabinet belongs
        /// to a country; keeping the player's in a second place would be the
        /// same concept living in two homes, which is how they drift apart.
        ///
        /// Deliberately a property: JsonUtility serializes fields, so this is
        /// persisted exactly once, on the country that owns it.
        /// </summary>
        public List<Official> cabinet => PlayerCountry?.cabinet ?? EmptyCabinet;

        static readonly List<Official> EmptyCabinet = new List<Official>();

        /// <summary>
        /// Where the player's Cabinet used to live. Saves written before
        /// cabinets belonged to countries still carry it; `SaveMigration` moves
        /// the contents onto the player country and leaves this empty forever
        /// after. Never write to it.
        /// </summary>
        public List<Official> legacyCabinet = new List<Official>();

        /// <summary>Influence steers officials without micromanagement (GDD §7.3).</summary>
        public int influence;
        public const int InfluenceCap = 6;
        public const int InfluencePerMonth = 3;

        /// <summary>
        /// Political Capital pays for difficult appointments, dismissals, reforms
        /// and institutional changes (GDD §7.3).
        /// </summary>
        public float politicalCapital = 4f;
        public const float PoliticalCapitalCap = 20f;

        /// <summary>Administrations the player has served under in this save.</summary>
        public int administrationsServed = 1;

        /// <summary>
        /// Whether the forty-year career review has been delivered (GDD §9
        /// amendment). False on old saves is correct — a save crossing the line
        /// after loading simply receives its review at the next year-end.
        /// </summary>
        public bool tenureReviewed;

        /// <summary>
        /// What this posting is for (GDD §25 amendment, 2026-08): the mandate the
        /// operator was given on arrival and the verdict delivered on it at the
        /// ten-year review. Null on an old save; `MandateSystem` assigns one at
        /// the next month.
        /// </summary>
        public Mandate mandate;
        public MandateRecord mandateRecord;

        /// <summary>Optional strategic directives (GDD §29, spec 24). Empty on an old save is correct.</summary>
        public List<StandingDirective> standingDirectives = new List<StandingDirective>();
        public int directivesCompletedThisYear;
        public int lastDirectiveOfferMonth = -100;

        /// <summary>
        /// Smoothed month-over-month change in the player's treasury (EWMA,
        /// ~5-month memory), and the bookkeeping that seeds it. Written by
        /// `EconomySystem.MonthlyUpdate`; read by the briefing's treasury line
        /// and by `AttentionSystem`.
        ///
        /// Playtested into existence: deficit spending punishes on a lag of
        /// *years* (debt → confidence → markets → living standards), so an
        /// operator can bankrupt a healthy country and first hear about it a
        /// decade later — a 20-year test campaign did exactly that, ending at
        /// −4,905 with no warning ever shown. A consequence nobody was told
        /// about is not a consequence, it is a bug report.
        /// </summary>
        public float treasuryTrend;
        public float lastMonthTreasury;
        public bool treasuryTrendSeeded;

        /// <summary>World Chronicle — automatic historical archive (GDD §31.3).</summary>
        public List<ChronicleEntry> chronicle = new List<ChronicleEntry>();

        /// <summary>
        /// Why important values moved (spec 26). Bounded, player-country only,
        /// and **empty is correct on an old save** — a world that resolved its
        /// months before this existed has no recorded reasons, and manufacturing
        /// them after the fact would be inventing history rather than reporting
        /// it. That is the same reasoning `warsWon` and `Bloc.commitments`
        /// shipped under, so this needs no migration step and
        /// `SaveSystem.CurrentSaveVersion` is unchanged: an old save simply
        /// starts accumulating explanations from the first month it resolves.
        /// </summary>
        public CausalLedger causal = new CausalLedger();

        /// <summary>Briefing traffic (GDD §28.2), newest last. Trimmed to MaxNotifications.</summary>
        public List<Notification> notifications = new List<Notification>();

        /// <summary>Unresolved Crisis Turns (GDD §6, §23).</summary>
        public List<ActiveCrisis> activeCrises = new List<ActiveCrisis>();

        /// <summary>
        /// Commissioned finished assessments (spec 03 §10). Empty on an old save
        /// is correct — nothing had been commissioned in a world with no way to
        /// commission it — so this needs **no migration step**, on the same
        /// reasoning as `displacement` and the war verdicts.
        /// </summary>
        public List<IntelProduct> intelProducts = new List<IntelProduct>();

        /// <summary>When each event definition last fired (GDD §23 cooldowns).</summary>
        public List<EventCooldown> eventCooldowns = new List<EventCooldown>();

        /// <summary>
        /// How the player's recent crises ended (spec 11 §7). Feeds crisis
        /// chains; pruned by `CrisisSystem` once too old for any chain to read.
        /// </summary>
        public List<CrisisOutcome> crisisOutcomes = new List<CrisisOutcome>();

        /// <summary>Strategic map (GDD §16, §19).</summary>
        public List<StrategicLocation> locations = new List<StrategicLocation>();

        /// <summary>Active and resolved confrontations (GDD §18).</summary>
        public List<Confrontation> confrontations = new List<Confrontation>();

        /// <summary>
        /// Armed movements on the map (GDD §12, §17.1, §19).
        ///
        /// The first thing in this list that is not a government. Empty is
        /// correct for a save written before they existed — a world that
        /// predates them has no *recorded* movements, and inventing some from
        /// its unrest figures would be guessing at a history nobody played.
        /// Same reasoning as the war-verdict counters, and the same conclusion:
        /// no migration step.
        /// </summary>
        public List<Insurgency> insurgencies = new List<Insurgency>();

        /// <summary>Bilateral trade links (GDD §20).</summary>
        public List<TradeRelation> trade = new List<TradeRelation>();

        /// <summary>Active sanctions regimes (GDD §20).</summary>
        public List<Sanction> sanctions = new List<Sanction>();

        /// <summary>Collection networks, keyed by owner (GDD §14).</summary>
        public List<IntelNetwork> networks = new List<IntelNetwork>();

        /// <summary>Analytical estimates held by each observer (GDD §14).</summary>
        public List<IntelEstimate> estimates = new List<IntelEstimate>();

        /// <summary>Bilateral relationships, one per unordered pair (GDD §15.1).</summary>
        public List<Relationship> relationships = new List<Relationship>();

        /// <summary>Signed treaties (GDD §15.2).</summary>
        public List<Treaty> treaties = new List<Treaty>();

        /// <summary>Active coalitions (GDD §15.2).</summary>
        public List<Coalition> coalitions = new List<Coalition>();

        /// <summary>
        /// Standing alignments with names (GDD §15.2).
        ///
        /// Distinct from `coalitions`, which are raised for one confrontation and
        /// dissolve after it. A bloc outlives the reason it was founded, which is
        /// the whole difference. Empty on an old save is correct — no migration.
        /// </summary>
        public List<Bloc> blocs = new List<Bloc>();

        /// <summary>
        /// The multilateral chamber (GDD §15.2, §28).
        ///
        /// Its permanent seats are filled lazily by `CouncilSystem.EnsureSeated`
        /// from the capability the world opened with, so an existing save picks
        /// up a chamber on load without a migration step and always seats the
        /// same five.
        /// </summary>
        public CouncilState council = new CouncilState();

        /// <summary>Joint exercise after-action records (GDD §15.3).</summary>
        public List<ExerciseRecord> exercises = new List<ExerciseRecord>();

        /// <summary>Concluded settlements and their terms (GDD §26).</summary>
        public List<SettlementRecord> settlements = new List<SettlementRecord>();

        /// <summary>Strategic instruments that have actually been used (GDD §21).</summary>
        public List<EndgameRecord> endgameRecords = new List<EndgameRecord>();

        /// <summary>
        /// Standing efforts to absorb another country by persuasion (GDD §15.1).
        /// Slow, deniable, and paid for in the friendship that made them possible.
        /// </summary>
        public List<AccessionCampaign> accessions = new List<AccessionCampaign>();

        /// <summary>
        /// What the player's delegated officials did last month (GDD §8, §28.1).
        ///
        /// Rebuilt every month rather than accumulated — this is a report on the
        /// month just resolved, not an archive. The permanent record is the
        /// chronicle, which is where a player goes to find out what their
        /// government did rather than what it is doing.
        /// </summary>
        public List<CabinetReportLine> cabinetReport = new List<CabinetReportLine>();

        /// <summary>Per-country AI reasoning state (GDD §24).</summary>
        public List<AIState> aiStates = new List<AIState>();

        /// <summary>Difficulty changes AI reasoning quality only (GDD §24.3).</summary>
        public Difficulty difficulty = Difficulty.Standard;

        public AIState FindAI(string countryId)
        {
            for (int i = 0; i < aiStates.Count; i++)
                if (aiStates[i].countryId == countryId) return aiStates[i];
            return null;
        }

        // Strategist progression (GDD §25). Erased entirely by a full reset (§5.1).
        public int strategistXP;
        public int skillPoints;
        public int strategistLevel = 1;

        /// <summary>Unlocked skill node ids.</summary>
        public List<string> unlockedSkills = new List<string>();

        /// <summary>Archive of annual evaluations.</summary>
        public List<EvaluationRecord> evaluations = new List<EvaluationRecord>();

        /// <summary>Baseline for the current year's evaluation.</summary>
        public YearSnapshot yearSnapshot = new YearSnapshot();

        /// <summary>Crisis counters for the current evaluation year.</summary>
        public int crisesFacedThisYear;
        public int crisesResolvedThisYear;

        /// <summary>Significant operator decisions taken this evaluation year.</summary>
        public int initiativesThisYear;

        /// <summary>
        /// How many times each kind of action has already paid XP this year
        /// (GDD §25.1). Parallel lists rather than a dictionary because
        /// JsonUtility cannot serialize one.
        /// </summary>
        public List<string> xpReasons = new List<string>();
        public List<int> xpReasonCounts = new List<int>();

        /// <summary>
        /// Monotonic counter mixed into the seed of any draw that can happen more
        /// than once in the same month. Without it, two covert operations run in
        /// one month rebuilt an identical stream and returned identical rolls —
        /// which made a successful operation infinitely repeatable. Persisted, so
        /// a save resumes the sequence rather than replaying it.
        /// </summary>
        public int actionSequence;

        /// <summary>
        /// Bitmask of pillars the operator has obtained direct authority over
        /// under the current administration (`1 &lt;&lt; (int)Pillar`).
        ///
        /// Authority that must be granted is granted **once**, not re-bought for
        /// every action — a legislature that has ceded the economic brief does
        /// not re-litigate each tariff. It is cleared when the administration
        /// changes, because the grant was personal to the government that made
        /// it (spec 05 §1a).
        /// </summary>
        public int authorizedPillarMask;

        /// <summary>Consume the next value in the per-action draw sequence.</summary>
        public int NextActionSequence() => ++actionSequence;

        /// <summary>
        /// Per-month memo for "how much subversion has this state publicly been
        /// caught at", read by `IntelligenceSystem.DirectedHardening`.
        ///
        /// **Not state, and deliberately not saved.** The value is a pure
        /// function of the chronicle, but computing it means scanning every
        /// entry, and it is wanted once per network per month against a record
        /// that reaches a few thousand lines over a long save. Uncached it made
        /// the twenty- and thirty-year fixtures measurably slower, and several
        /// of those already sit against the clock.
        ///
        /// Keyed to nothing but this instance and this month: `[NonSerialized]`
        /// keeps it out of the save, and it must never be keyed on the seed —
        /// two worlds built from the same seed and played differently are
        /// exactly what the counter-play tests compare, and a shared cache
        /// would hand one arm's answer to the other.
        /// </summary>
        [NonSerialized] public Dictionary<string, float> subversionMemo;
        [NonSerialized] public int subversionMemoMonth = int.MinValue;

        /// <summary>
        /// The player's debt stock as of the last trend sample. Paired with
        /// `lastMonthTreasury` so the readout measures the **fiscal balance**
        /// rather than the balance of the account.
        ///
        /// Needed because a deficit now finances itself into debt: the treasury
        /// stops falling, so a treasury-delta reading goes quiet at exactly the
        /// moment the government starts living on borrowed money. Seeded lazily
        /// with the treasury figure, so no migration.
        /// </summary>
        public float lastMonthSovereignDebt;

        public bool HasSkill(string nodeId) => unlockedSkills.Contains(nodeId);

        /// <summary>Result of the first-launch assessment (GDD §5).</summary>
        public AssessmentResult assessment;

        /// <summary>First-posting orientation progress (GDD §5).</summary>
        public TutorialState tutorial = new TutorialState();

        public CountryState PlayerCountry => FindCountry(playerCountryId);

        public Relationship FindRelationship(string a, string b)
        {
            for (int i = 0; i < relationships.Count; i++)
            {
                var r = relationships[i];
                if ((r.countryA == a && r.countryB == b) || (r.countryA == b && r.countryB == a))
                    return r;
            }
            return null;
        }

        public Treaty FindTreaty(string a, string b)
        {
            for (int i = 0; i < treaties.Count; i++)
            {
                var t = treaties[i];
                if (t.broken) continue;
                if ((t.countryA == a && t.countryB == b) || (t.countryA == b && t.countryB == a))
                    return t;
            }
            return null;
        }

        /// <summary>
        /// The coalition led by a specific country. Both sides of a confrontation
        /// can field one, so a leader must always be specified.
        /// </summary>
        public Coalition FindCoalitionLedBy(string confrontationId, string leaderId)
        {
            for (int i = 0; i < coalitions.Count; i++)
            {
                var coalition = coalitions[i];
                if (!coalition.dissolved && coalition.confrontationId == confrontationId
                    && coalition.leaderId == leaderId)
                    return coalition;
            }
            return null;
        }

        /// <summary>The player's coalition for a confrontation, if any.</summary>
        public Coalition FindCoalition(string confrontationId)
            => FindCoalitionLedBy(confrontationId, playerCountryId);

        public IntelNetwork FindNetwork(string ownerId, string targetId)
        {
            for (int i = 0; i < networks.Count; i++)
                if (networks[i].ownerId == ownerId && networks[i].targetId == targetId)
                    return networks[i];
            return null;
        }

        public IntelEstimate FindEstimate(string observerId, string targetId, IntelDomain domain)
        {
            for (int i = 0; i < estimates.Count; i++)
            {
                var e = estimates[i];
                if (e.observerId == observerId && e.targetId == targetId && e.domain == domain)
                    return e;
            }
            return null;
        }

        public TradeRelation FindTrade(string a, string b)
        {
            for (int i = 0; i < trade.Count; i++)
            {
                var link = trade[i];
                if ((link.countryA == a && link.countryB == b) || (link.countryA == b && link.countryB == a))
                    return link;
            }
            return null;
        }

        public Sanction FindSanction(string senderId, string targetId)
        {
            for (int i = 0; i < sanctions.Count; i++)
                if (sanctions[i].senderId == senderId && sanctions[i].targetId == targetId)
                    return sanctions[i];
            return null;
        }

        public StrategicLocation FindLocation(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < locations.Count; i++)
                if (locations[i].id == id) return locations[i];
            return null;
        }

        /// <summary>
        /// Which confrontation the operator is currently commanding.
        ///
        /// A state can now be committed on more than one front (GDD §16), so
        /// "the" active confrontation is a choice rather than a fact. Empty means
        /// the first one, which keeps every existing caller correct.
        /// </summary>
        public string commandingConfrontationId = "";

        /// <summary>The confrontation the operator is commanding, if any.</summary>
        public Confrontation ActiveConfrontation
        {
            get
            {
                if (!string.IsNullOrEmpty(commandingConfrontationId))
                    foreach (var confrontation in confrontations)
                        if (confrontation.id == commandingConfrontationId && !confrontation.resolved
                            && confrontation.Involves(playerCountryId))
                            return confrontation;

                return ActiveConfrontationFor(playerCountryId);
            }
        }

        /// <summary>Every unresolved confrontation this country is party to.</summary>
        /// <summary>An unresolved confrontation by id, or null.</summary>
        public Confrontation FindConfrontation(string confrontationId)
        {
            if (string.IsNullOrEmpty(confrontationId)) return null;
            for (int i = 0; i < confrontations.Count; i++)
                if (confrontations[i].id == confrontationId) return confrontations[i];
            return null;
        }

        public List<Confrontation> ActiveConfrontationsFor(string countryId)
        {
            var active = new List<Confrontation>();
            for (int i = 0; i < confrontations.Count; i++)
                if (!confrontations[i].resolved && confrontations[i].Involves(countryId))
                    active.Add(confrontations[i]);
            return active;
        }

        /// <summary>Any country's active confrontation. AI states use the same lifecycle.</summary>
        public Confrontation ActiveConfrontationFor(string countryId)
        {
            for (int i = 0; i < confrontations.Count; i++)
                if (!confrontations[i].resolved && confrontations[i].Involves(countryId))
                    return confrontations[i];
            return null;
        }

        /// <summary>True when this country is in an active shooting conflict.</summary>
        public bool IsAtWar(string countryId)
        {
            for (int i = 0; i < confrontations.Count; i++)
            {
                var c = confrontations[i];
                if (!c.resolved && c.Involves(countryId) && c.escalation >= EscalationState.LimitedConflict)
                    return true;
            }
            return false;
        }

        public Official FindOfficial(Pillar office)
        {
            for (int i = 0; i < cabinet.Count; i++)
                if (cabinet[i].office == office) return cabinet[i];
            return null;
        }

        public CountryState FindCountry(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < countries.Count; i++)
                if (countries[i].id == id) return countries[i];
            return null;
        }

        public const int MaxNotifications = 200;

        /// <summary>
        /// True when the operator has spent this month's command capacity and
        /// nothing is standing in the way of ending it.
        ///
        /// Command Points are the operator's attention for the month (GDD §7.1),
        /// so an empty pool means every remaining decision is somebody else's to
        /// take. The shell uses this to draw the eye to END MONTH rather than
        /// leaving the player hunting for an action that is no longer available.
        ///
        /// Still suppressed while a crisis is open, but for a different reason
        /// now that nothing is refused: "you have spent your capacity, move on"
        /// is the wrong thing to say to someone who has an unanswered decision
        /// in front of them. `AttentionSystem` handles that case, and says
        /// something more useful.
        /// </summary>
        public bool CommandCapacitySpent
            => commandPoints.current <= 0 && !HasOpenCrisis;

        /// <summary>
        /// True when a crisis is open and unanswered.
        ///
        /// It does **not** block anything. This used to be `HasBlockingCrisis`,
        /// backed by a per-crisis `blocksEndMonth` flag, and the turn refused to
        /// advance while it was set. The operator is running a government and is
        /// allowed to fail to decide — that is a real outcome and a more
        /// interesting one than a wall. Ending the month with this true lapses
        /// the crisis instead (`CrisisSystem.LapseUnanswered`).
        /// </summary>
        public bool HasOpenCrisis => activeCrises.Count > 0;

        public void AddNotification(NotificationClass priority, string title, string body,
            string countryId = null, ReportingDesk desk = ReportingDesk.Command)
        {
            notifications.Add(new Notification
            {
                date = date,
                priority = priority,
                title = title,
                body = body ?? string.Empty,
                countryId = countryId ?? string.Empty,
                desk = desk
            });
            if (notifications.Count > MaxNotifications)
                notifications.RemoveRange(0, notifications.Count - MaxNotifications);
        }

        /// <summary>
        /// Whether the same line for the same state is already in the record
        /// within the last <paramref name="months"/>. A campaign that repeats
        /// every month is one entry, not one per month — the two noisiest lines
        /// in a thirty-year world were a crackdown (1,279×) and a blown network
        /// (883×) and between them were half the chronicle.
        /// </summary>
        public bool ChronicledWithin(string countryId, string text, int months)
        {
            for (int i = chronicle.Count - 1; i >= 0; i--)
            {
                var entry = chronicle[i];
                if (date.MonthsSince(entry.date) > months) return false;
                if (entry.countryId == countryId && entry.text == text) return true;
            }
            return false;
        }

        public void AddChronicle(ChronicleCategory category, string countryId, string text,
            Publicity publicity = Publicity.Secret)
        {
            chronicle.Add(new ChronicleEntry
            {
                date = date,
                category = category,
                countryId = countryId ?? string.Empty,
                text = text,
                publicity = publicity
            });
        }
    }
}

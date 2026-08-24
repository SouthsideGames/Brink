using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Persistent state for one country. Phase 0 keeps this minimal but
    /// forward-compatible: pillars, resources, and social pressure headline values.
    /// Cabinet, capabilities, relationships and traits arrive in later phases.
    /// </summary>
    [Serializable]
    public class CountryState
    {
        public string id;          // stable machine id, e.g. "USA"
        public string displayName; // e.g. "United States"
        public bool isPlayer;

        /// <summary>
        /// When this state came into existence. Year 0 means it was there at
        /// world creation; a seceded state carries the date it declared, which is
        /// what reunification counts from.
        /// </summary>
        public GameDate foundedDate;

        /// <summary>
        /// This government's five officials, one per pillar (GDD §8).
        ///
        /// Every country has one. The player's is the one they command through
        /// control modes; a foreign one is the machinery that actually runs that
        /// state — and an intelligence target, because who runs a rival's
        /// economy is exactly the sort of thing collection is for.
        /// </summary>
        public List<Official> cabinet = new List<Official>();

        public Official FindOfficial(Pillar office)
        {
            for (int i = 0; i < cabinet.Count; i++)
                if (cabinet[i].office == office) return cabinet[i];
            return null;
        }

        /// <summary>
        /// Offices standing empty and awaiting an appointment (GDD §7.3).
        ///
        /// Only ever populated for the player: a foreign government fills its own
        /// seat the month it opens, because there is no operator to consult and
        /// a shortlist nobody reads is not a decision.
        /// </summary>
        public List<CabinetVacancy> vacancies = new List<CabinetVacancy>();

        /// <summary>
        /// What this country is, beyond its numbers (GDD §10, §17.1). Authored
        /// per nation and stable across saves, so a player's read on a state
        /// carries from one playthrough to the next.
        /// </summary>
        public List<NationalTrait> traits = new List<NationalTrait>();

        public PillarScores pillars = new PillarScores();
        public NationalResources resources = new NationalResources();
        public MilitaryState military = new MilitaryState();
        public EconomyState economy = new EconomyState();
        public CounterIntelState counterIntel = new CounterIntelState();
        public GovernmentState government = new GovernmentState();
        public TechnologyState technology = new TechnologyState();
        public EndgameState endgames = new EndgameState();

        // Social pressure headline values (GDD §12), 0..100.
        public float governmentApproval;
        public float stability;
        public float nationalUnity;
        public float warSupport = 50f;
        public float warExhaustion;

        /// <summary>
        /// What this country's wars came to (GDD §20 amendment).
        ///
        /// Zero is genuinely correct for a save that predates this — such a world
        /// has no *recorded* history, and inventing one from the archived
        /// confrontations would be guessing at verdicts nobody measured. The
        /// record starts now, which is why this needs no migration step.
        ///
        /// Kept per country rather than only for the player: a rival with four
        /// wars won is a different proposition from one that has lost three, and
        /// that is exactly the sort of thing an operator should be able to learn
        /// about the world.
        /// </summary>
        public int warsWon;
        public int warsLost;
        public int warsDrawn;

        /// <summary>Plain record, e.g. "3–1–2". Reads the same for us and for them.</summary>
        public string WarRecordText => $"{warsWon}–{warsLost}–{warsDrawn}";

        /// <summary>
        /// 0..100 how well the public actually lives (GDD §12).
        ///
        /// The missing link between the economy and the politics. Approval was
        /// computed straight from growth, inflation and unemployment, which meant
        /// a bad quarter moved the polls immediately and a decade of prosperity
        /// left no trace. Living standards are the slow accumulation underneath:
        /// they follow the economy over years, and approval follows *them*. That
        /// is what makes a long boom worth something politically and a long
        /// depression something a single good year cannot fix.
        ///
        /// Zero would be wrong for an old save — a country is not destitute
        /// because the field is new — so `SaveMigration` seeds it from the
        /// economy that produced it.
        /// </summary>
        public float livingStandards = 55f;

        /// <summary>
        /// 0..100 organised public anger (GDD §12).
        ///
        /// Distinct from low approval, which is an opinion. Unrest is people in
        /// the street: it accumulates from hardship the government does not
        /// address, it suppresses rather than resolves under a restrictive civic
        /// posture, and it feeds the conspiracy that ends governments. A state
        /// can be widely disliked and perfectly calm; it can also be quietly
        /// approved of and coming apart in three cities.
        /// </summary>
        public float socialUnrest;

        /// <summary>
        /// 0..100 what the public has not forgotten (GDD §12: "public opinion
        /// has historical memory; long wars, depressions, victories and
        /// betrayals leave lasting political effects").
        ///
        /// The domestic counterpart to `Relationship.memory`, which was the only
        /// memory in the game and pointed outward. High grievance makes every
        /// later hardship land harder and every recovery count for less — a
        /// country that has been put through something does not respond to the
        /// next thing the way a fresh one would. It decays over decades, not
        /// years, and never quite to zero.
        /// </summary>
        public float publicGrievance;
    }

    /// <summary>Chronicle categories for the world archive (GDD §31.3).</summary>
    public enum ChronicleCategory
    {
        System,
        Political,
        Military,
        Economic,
        Diplomatic,
        Intelligence
    }

    /// <summary>
    /// Whether the world can see an event happen (GDD §14, §28).
    ///
    /// The world wire reports only what anyone with a radio would know. A
    /// treaty is signed in public; a coup is on television; an army moving to a
    /// border is observed. A covert operation, a research programme or a
    /// strategic preparation is not — those stay behind the fog, and collection
    /// remains the only way to see them.
    ///
    /// **The default is `Secret`, deliberately.** A new chronicle entry that
    /// nobody classified should fail closed: a missing wire item is a visible
    /// content gap somebody will notice and fix, while a leak silently guts the
    /// intelligence pillar and looks like the game working.
    ///
    /// (Note this is the opposite default from `ReportingDesk.Command`, and for
    /// the opposite reason — there the risk was traffic silently vanishing, so
    /// the safe default was maximum visibility. Same principle, mirrored: make
    /// the *harmful* case the one you have to ask for.)
    /// </summary>
    public enum Publicity
    {
        Secret,
        Public
    }

    /// <summary>One archived historical event. Long saves become an alternate-history chronicle.</summary>
    [Serializable]
    public class ChronicleEntry
    {
        public GameDate date;
        public ChronicleCategory category;
        public string countryId; // empty = global
        public string text;

        /// <summary>Whether the world saw this happen. See <see cref="Publicity"/>.</summary>
        public Publicity publicity;
    }

    /// <summary>
    /// Command Point pool (GDD §7.1). Baseline ~5 CP/month; a limited Strategic
    /// Reserve preserves some unused capacity between months.
    /// </summary>
    [Serializable]
    public class CommandPointsState
    {
        public int baselinePerMonth = 5;
        public int reserveCap = 2;
        public int current;
        public int reserve;

        /// <summary>Bank leftover CP into the reserve (capped), then refill for a new month.</summary>
        public void BeginMonth()
        {
            reserve = Math.Min(reserveCap, reserve + current);
            current = baselinePerMonth + reserve;
            reserve = 0;
        }

        /// <summary>Bank leftover CP, then refill including any temporary bonus.</summary>
        public void BeginMonth(int bonusPerMonth, int reserveCapBonus)
        {
            int cap = reserveCap + Math.Max(0, reserveCapBonus);
            reserve = Math.Min(cap, reserve + current);
            current = baselinePerMonth + Math.Max(0, bonusPerMonth) + reserve;
            reserve = 0;
        }

        public bool CanSpend(int amount) => amount > 0 && current >= amount;

        public bool Spend(int amount)
        {
            if (!CanSpend(amount)) return false;
            current -= amount;
            return true;
        }
    }
}

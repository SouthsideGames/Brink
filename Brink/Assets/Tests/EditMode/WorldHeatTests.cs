using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The world generates its own pressure (GDD §24 amendment, user decision:
    /// "noticeably hotter").
    ///
    /// Reported from a phone playtest: as the United States, the operator
    /// befriended every nation, signed treaties with every nation, and ran out
    /// of things to do. Measured, both halves were real: **15 of 15 warm
    /// relationships** achievable in twenty years with zero resistance, and
    /// **0–1 AI-vs-AI wars in thirty years** across three seeds — a world at
    /// peace with itself while sanctioning itself into depression, offering an
    /// operator at the top nothing to push against.
    ///
    /// These tests pin the two fixes: bloc politics makes universal friendship
    /// structurally impossible, and the AI world fights its own wars.
    /// </summary>
    public class WorldHeatTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static GameState RunPassive(int seed, int months)
        {
            var state = WorldFactory.CreateDebugWorld(seed);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int m = 0; m < months; m++) turns.EndMonth();
            return state;
        }

        // ---------- the AI world fights its own wars ----------

        // Six 30-year worlds on the full pipeline — 180 world-years, against
        // Unity's 180s default. Widened from two seeds for the sampling reason
        // below; the clock has to follow the sample.
        [Test, Timeout(900000)]
        public void TheWorldFightsItsOwnWars()
        {
            // **Six seeds, not two.** This asserted ≥3 wars across two worlds,
            // and the measured rate has a standard deviation larger than its
            // mean: eight worlds sampled 1, 1, 4, 0, 0, 3, 1, 0. A two-sample
            // floor on a quantity that varies that much measures which seeds it
            // happened to pick — the fragility this codebase already recognised
            // when it converted several single-seed decade comparisons to
            // multi-seed averages.
            //
            // **The old ≥3-per-two-worlds figure is superseded, and not by
            // tuning.** It was measured on a world whose economy could collapse
            // permanently: `AISystem.ResourcePrize` only makes a neighbour's
            // ground worth taking when the claimant's energy or materials are
            // below 40, and before the sector-capacity and stagnation-floor
            // fixes (spec 02 §2) states were routinely ground to nothing and
            // coveted each other accordingly. Some of that heat was the bug.
            // Measured mean afterwards: ~1.25 wars per 30-year world.
            //
            // So this now asserts the *failure mode* rather than the old number:
            // the world must not be scenery, and it must not be in flames.
            //
            // **A war and a front are counted separately (2026-09).** Since the
            // multilateral alliance work, honouring a guarantee opens a
            // satellite front of the war it was joined for — "three states and
            // three states become *one war* between six", in the design's own
            // words — and those fronts close with it. Counting every satellite
            // as a war of its own reported a single bloc war as six, which is
            // the flames-versus-peace question asked of the wrong quantity.
            // Wars a government *chose* are held to the old ceiling; the fronts
            // those wars pull in are bounded separately, generously, so a
            // world that swarms every aggressor still cannot become one where
            // every state is always fighting.
            int[] seeds = { 4242, 9090, 8686, 5171, 6301, 2468 };
            int aiWars = 0, aiFronts = 0;
            int worldsWithAWar = 0;
            var perWorld = new System.Text.StringBuilder();

            foreach (int seed in seeds)
            {
                var state = RunPassive(seed, 360);
                int here = 0, fronts = 0;
                foreach (var confrontation in state.confrontations)
                {
                    if (confrontation.Involves(state.playerCountryId)) continue;
                    if (confrontation.escalation < EscalationState.LimitedConflict) continue;
                    if (confrontation.IsObligationEntry) fronts++;
                    else here++;
                }
                aiWars += here;
                aiFronts += fronts;
                if (here + fronts > 0) worldsWithAWar++;
                perWorld.Append($" {seed}:{here}+{fronts}");
            }

            Assert.GreaterOrEqual(aiWars + aiFronts, 4,
                $"Six 30-year worlds produced {aiWars} AI-vs-AI wars and {aiFronts} alliance fronts "
                + $"between them ({perWorld}). The world has gone quiet — an operator at the top "
                + "has nothing to push against, alliance obligations never fire, and the wire "
                + "carries no news.");
            Assert.GreaterOrEqual(worldsWithAWar, 3,
                $"Only {worldsWithAWar} of {seeds.Length} worlds saw a single AI war in thirty "
                + $"years ({perWorld}). A handful of loud worlds hiding a majority of silent "
                + "ones is still a world that is scenery for most players.");
            Assert.LessOrEqual(aiWars, 40,
                $"{aiWars} AI-vs-AI wars in {seeds.Length * 30} world-years ({perWorld}) — the "
                + "world is in flames, which is as flat as a world at peace.");
            Assert.LessOrEqual(aiFronts, 72,
                $"{aiFronts} alliance fronts in {seeds.Length * 30} world-years ({perWorld}) — "
                + "every war is a world war, which is the cascade the 2026-09 repair damped.");
        }

        [Test]
        public void AnEngagedStateCanBeConfronted()
        {
            // The overstretch contradiction: `IsOverstretched` fed "attack the
            // busy" into AssertClaim's weight while a guard clause made
            // attacking anyone busy impossible — the opening was computed every
            // month and structurally unreachable. The guard is gone; a state
            // already at war elsewhere is attackable, exactly as the theatre
            // design promises.
            var state = WorldFactory.CreateDebugWorld(seed: 7777);

            var first = ConfrontationSystem.BeginBy(state, "RUS", "KAZ",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(first, "fixture: the first confrontation failed to open");

            var second = ConfrontationSystem.BeginBy(state, "CHN", "RUS",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(second,
                "A state already fighting one war cannot be attacked by a third party. " +
                "Overstretch is priced as an opening and then made unreachable — the " +
                "written-but-never-read family, wearing a guard clause.");
        }

        // ---------- universal friendship is structurally resisted ----------

        [Test]
        public void UniversalFriendshipIsStructurallyImpossible()
        {
            // The reported playthrough, replayed exactly: outreach to the
            // coldest every month, treaties to the warm, forever.
            //
            // **What this asserts is the rule, not a headcount (2026-09).** It
            // used to require at most 13 warm friendships at month 240. That
            // tally sits on a knife-edge: adding one unit of treasury to the AI
            // states in a single month flipped it between 13 and 14 in 4 of 13
            // sampled base trajectories, so it passed or failed on which
            // trajectory a runtime happened to follow, not on whether the
            // structure worked. It also looked at the wrong moment: measured
            // across 28 base and C4 trajectories, this bot holds all fifteen for
            // stretches of 38–109 months in the middle of the run, and the rule
            // that breaks the map apart is what the old end-of-run count was
            // indirectly — and fragilely — observing.
            //
            // The structure is the friend-of-my-enemy ceiling
            // (`DiplomacySystem.RivalGravity`, spec 04 §9a): committed alignment
            // with a state's genuine enemy caps how close the pair may
            // *functionally* be at 100 − gravity × 85.
            //
            // **Restated for the disposition / permission split (2026-09).** Rival
            // gravity used to enforce that cap by writing `relations`, `trust` and
            // `strategicAlignment` downward, so stored warmth *was* permitted
            // warmth and a stored-value predicate measured the rule exactly. It no
            // longer writes them: the locked rule is that countries may genuinely
            // like everyone but may not functionally stand beside everyone, so the
            // question "is this a friend" is now `FunctionalCloseness`, and
            // `relations > 65 && trust > 50` is a pre-split proxy for it. The
            // thresholds below are unchanged; only what they are read against is.
            // So:
            //   1. a friendship under committed rival gravity cannot persist —
            //      at 0.5 the permitted warmth is 57.5 and `CeilingBand` puts that
            //      at Cooperative, below the friendship line, and no partner may
            //      stay a *functional* friend under it for half a year;
            //   2. that gravity genuinely engages against this playthrough, or
            //      rule 1 is vacuous (measured: 76–173 months of 240);
            //   3. universal *functional* friendship is not a held outcome — it
            //      does not survive into the final two years;
            //   4. the world refuses treaties by structure, not by pacing;
            //   5. **and the constraint does not damage the relationship.** At
            //      least one partner must be warm by disposition while bloc
            //      politics holds it below functional friendship. That pairing is
            //      impossible under the old write-back — where stored and permitted
            //      were one number — so this is the assertion that fails if gravity
            //      ever starts manufacturing coldness again.
            const float CommittedRivalGravity = 0.5f;
            const int MonthsAFriendshipMaySurviveIt = 6;
            const int MonthsGravityMustEngage = 60;
            const int FinalMonthsWithoutUniversalFriendship = 24;

            var state = WorldFactory.CreateDebugWorld(seed: 1212);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            var pact = new List<TreatyCommitment>
                { TreatyCommitment.NonAggression, TreatyCommitment.MutualDefense };

            int rejections = 0;
            var survivingUnderGravity = new Dictionary<string, int>();
            int longestSurvival = 0, monthsGravityEngaged = 0, lastUniversalMonth = 0;
            int monthsWithAConstrainedFriendship = 0;
            string longestSurvivor = "none";
            for (int m = 0; m < 240; m++)
            {
                while (state.HasOpenCrisis)
                    CrisisSystem.Resolve(state, state.activeCrises[0], 0);

                var mine = new List<Relationship>();
                foreach (var r in state.relationships)
                    if (r.Involves(state.playerCountryId)) mine.Add(r);
                mine.Sort((a, b) => a.relations.CompareTo(b.relations));

                foreach (var r in mine)
                {
                    if (state.commandPoints.current <= 1) break;
                    string other = r.PartnerOf(state.playerCountryId);
                    if (r.relations < 70f) DiplomacySystem.Outreach(state, turns, other);
                }
                foreach (var r in mine)
                {
                    if (state.commandPoints.current <= 2) break;
                    string other = r.PartnerOf(state.playerCountryId);
                    if (state.FindTreaty(state.playerCountryId, other) != null) continue;
                    if (r.relations > 55f
                        && !DiplomacySystem.ProposeTreaty(state, turns, other, pact)) rejections++;
                }

                turns.EndMonth();

                int partners = 0, functionalFriends = 0;
                bool engaged = false;
                foreach (var r in state.relationships)
                {
                    if (!r.Involves(state.playerCountryId)) continue;
                    string other = r.PartnerOf(state.playerCountryId);

                    // Disposition: what they actually think of us.
                    bool warmDisposition = r.relations > 65f && r.trust > 50f;
                    // Permission: how close bloc politics presently lets us be.
                    bool functionalFriend = DiplomacySystem.FunctionalCloseness(
                        state, state.playerCountryId, other) >= RelationshipStatus.Friendly;
                    float gravity = DiplomacySystem.RivalGravity(state, state.playerCountryId, other);
                    partners++;
                    if (functionalFriend) functionalFriends++;
                    if (gravity >= CommittedRivalGravity) engaged = true;
                    if (warmDisposition && !functionalFriend) monthsWithAConstrainedFriendship++;

                    survivingUnderGravity.TryGetValue(other, out int survived);
                    survived = functionalFriend && gravity >= CommittedRivalGravity ? survived + 1 : 0;
                    survivingUnderGravity[other] = survived;
                    if (survived > longestSurvival)
                    {
                        longestSurvival = survived;
                        longestSurvivor = $"{other} (month {m + 1}, relations {r.relations:F1}, gravity {gravity:F2})";
                    }
                }
                if (engaged) monthsGravityEngaged++;
                if (partners > 0 && functionalFriends == partners) lastUniversalMonth = m + 1;
            }

            Assert.LessOrEqual(longestSurvival, MonthsAFriendshipMaySurviveIt,
                $"A functional friendship survived {longestSurvival} consecutive months under committed rival " +
                $"gravity ≥ {CommittedRivalGravity} — {longestSurvivor}. The friend of my enemy cannot also be " +
                "my friend: permitted warmth is supposed to sit below the friendship line within " +
                "months, or bloc politics decorates the thing it exists to prevent.");
            Assert.GreaterOrEqual(monthsGravityEngaged, MonthsGravityMustEngage,
                $"Bloc gravity reached {CommittedRivalGravity} against this playthrough in only {monthsGravityEngaged} " +
                "of 240 months. Nothing structural is resisting universal friendship — the befriend-everyone " +
                "bot is simply meeting a world with no committed sides.");
            Assert.LessOrEqual(lastUniversalMonth, 240 - FinalMonthsWithoutUniversalFriendship,
                $"All {state.relationships.FindAll(r => r.Involves(state.playerCountryId)).Count} states were still " +
                $"functional friends at month {lastUniversalMonth}. Universal friendship is supposed to be " +
                "structurally impossible to hold — or a diplomatic playthrough solves itself and the game ends " +
                "in boredom, as reported.");
            Assert.Greater(rejections, 30,
                "The world never refused anything — friendship is being resisted by " +
                "accident of pacing rather than by structure.");
            Assert.Greater(monthsWithAConstrainedFriendship, 0,
                "Not once in 240 months was a partner warm by disposition while bloc politics held it below " +
                "functional friendship. Either gravity never bit, or — the failure this guards — it bit by " +
                "writing the relationship down instead of by limiting what can be built on it. Rival gravity " +
                "may prevent warmth; it may not manufacture coldness.");
        }

        [Test]
        public void DeepAlignmentWithARivalCapsTheRelationship()
        {
            // The pure mechanism, isolated: hold deep alignment with a state's
            // genuine enemy, and the relationship with that state cannot
            // functionally stay warm however hard it is courted — while the
            // courtship itself is not undone. Before the disposition / permission
            // split this asserted `relations < 66` directly, because gravity closed
            // the stored value onto the cap; it now asserts the same 66 against the
            // permitted warmth, and adds the other half of the rule.
            // NARROW PIPELINE: DiplomacySystem.MonthlyUpdate alone — every other
            // system also writes relations, and this asserts one system's
            // arithmetic against preconditions it pins itself.
            var state = WorldFactory.CreateDebugWorld(seed: 6161);
            string us = state.playerCountryId;

            var withAlly = state.FindRelationship(us, "RUS");
            var allyEnemy = state.FindRelationship("RUS", "POL");
            var withVictim = state.FindRelationship(us, "POL");

            // The courtship is set once; the *sources* of gravity are held. Held
            // sources, decaying subject — re-pinning the subject each month
            // would measure the pin, not the mechanic.
            withVictim.relations = 80f;
            withVictim.trust = 70f;

            for (int m = 0; m < 48; m++)
            {
                withAlly.strategicAlignment = 90f;   // committed bloc partner
                allyEnemy.relations = 8f;            // genuine enmity
                DiplomacySystem.MonthlyUpdate(state);
            }

            Assert.Less(DiplomacySystem.PermittedWarmth(state, us, "POL"), 66f,
                $"Four years of deep alignment with their enemy left them permitted " +
                $"{DiplomacySystem.PermittedWarmth(state, us, "POL"):F1} warmth — the ceiling never bit, and " +
                "both sides of a rivalry can be held at once.");
            Assert.Less((int)DiplomacySystem.FunctionalCloseness(state, us, "POL"),
                       (int)RelationshipStatus.Friendly,
                $"They are still functionally " +
                $"{DiplomacySystem.FunctionalCloseness(state, us, "POL")} while we are deeply aligned with " +
                "their enemy. The cap has to cross the friendship line, or it decorates the thing it " +
                "exists to prevent.");
            Assert.GreaterOrEqual(withVictim.relations, 66f,
                $"The courtship itself was undone: relations fell to {withVictim.relations:F1}. Rival gravity " +
                "constrains what a relationship can be built into; it must not write the relationship down.");

            float gravity = DiplomacySystem.RivalGravity(state, us, "POL");
            Assert.Greater(gravity, 0.3f, "the configured rivalry produced almost no gravity");
        }

        [Test]
        public void AnAllianceWebMakesTheRestOfTheWorldWary()
        {
            var state = WorldFactory.CreateDebugWorld(seed: 6161);
            Assert.AreEqual(0f, DiplomacySystem.PactAnxiety(state, state.playerCountryId), 0.001f,
                "a state with no pacts frightens nobody");

            int signed = 0;
            foreach (var country in state.countries)
            {
                if (country.isPlayer || signed >= 8) continue;
                var treaty = new Treaty
                {
                    id = $"T{signed}",
                    countryA = state.playerCountryId,
                    countryB = country.id,
                    signedDate = state.date
                };
                treaty.commitments.Add(TreatyCommitment.MutualDefense);
                state.treaties.Add(treaty);
                signed++;
            }

            Assert.Greater(DiplomacySystem.PactAnxiety(state, state.playerCountryId), 0.5f,
                "eight defence pacts read as no more threatening than none — hegemony " +
                "is a finish line rather than a held position.");
        }

        // ---------- the career arc ----------

        [Test]
        public void TenureReviewArrivesAtFortyYears()
        {
            var state = WorldFactory.CreateDebugWorld(seed: 3131);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            // Warp to December of year forty rather than simulating 480 months —
            // the review is keyed on months served and fires at a year-end.
            state.date = new GameDate(state.startDate.year + 40, 12);
            turns.EndMonth();

            Assert.IsTrue(state.tenureReviewed, "forty years of service passed unreviewed");

            bool notified = false;
            foreach (var notification in state.notifications)
                if (notification.title != null && notification.title.Contains("TENURE REVIEW"))
                    notified = true;
            Assert.IsTrue(notified, "the review was recorded but never reached the operator");

            // The world does not stop: the review is an arc, not an ending.
            for (int m = 0; m < 6; m++) turns.EndMonth();
            Assert.IsNotNull(state.PlayerCountry);

            // And it is delivered exactly once.
            int reviews = 0;
            foreach (var entry in state.chronicle)
                if (entry.text != null && entry.text.Contains("Tenure review")) reviews++;
            Assert.AreEqual(1, reviews, "the review repeats — an arc delivered twice is a nag");
        }
    }
}

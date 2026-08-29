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

        [Test]
        public void TheWorldFightsItsOwnWars()
        {
            // Summed across two seeds so one quiet world does not fail the
            // claim and one loud world does not hide a regression.
            int aiWars = 0;
            foreach (int seed in new[] { 4242, 9090 })
            {
                var state = RunPassive(seed, 360);
                foreach (var confrontation in state.confrontations)
                {
                    if (confrontation.Involves(state.playerCountryId)) continue;
                    if (confrontation.escalation >= EscalationState.LimitedConflict) aiWars++;
                }
            }

            Assert.GreaterOrEqual(aiWars, 3,
                $"Two 30-year worlds produced {aiWars} AI-vs-AI wars between them. The world " +
                "has gone quiet again — an operator at the top has nothing to push against, " +
                "alliance obligations never fire, and the wire carries no news.");
            Assert.LessOrEqual(aiWars, 24,
                $"{aiWars} AI-vs-AI wars in sixty world-years — the world is in flames, " +
                "which is as flat as a world at peace.");
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
            var state = WorldFactory.CreateDebugWorld(seed: 1212);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            var pact = new List<TreatyCommitment>
                { TreatyCommitment.NonAggression, TreatyCommitment.MutualDefense };

            int rejections = 0;
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
            }

            int friends = 0;
            foreach (var r in state.relationships)
                if (r.Involves(state.playerCountryId) && r.relations > 65f && r.trust > 50f)
                    friends++;

            Assert.LessOrEqual(friends, 13,
                $"Twenty years of doing nothing but diplomacy produced {friends}/15 warm " +
                "friendships. Universal friendship is supposed to be structurally impossible — " +
                "the friend of my enemy cannot also be my friend — or a diplomatic " +
                "playthrough solves itself and the game ends in boredom, as reported.");
            Assert.Greater(rejections, 30,
                "The world never refused anything — friendship is being resisted by " +
                "accident of pacing rather than by structure.");
        }

        [Test]
        public void DeepAlignmentWithARivalCapsTheRelationship()
        {
            // The pure mechanism, isolated: hold deep alignment with a state's
            // genuine enemy, and the relationship with that state cannot stay
            // warm however hard it is courted.
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

            Assert.Less(withVictim.relations, 66f,
                $"Four years of holding {withVictim.relations:F1} relations with a state " +
                "while deeply aligned with its enemy — the ceiling never bit, and both " +
                "sides of a rivalry can be held at once.");

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

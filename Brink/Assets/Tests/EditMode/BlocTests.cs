using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Sides with names (GDD §15.2, §24).
    ///
    /// Blocs existed only as a term in `RivalGravity`: there was nothing to join,
    /// lead, be excluded from or walk out of. The claims here are that a bloc is
    /// bought rather than declared, that leading it costs, that it is worth
    /// something in the one room where states act together, and that it comes
    /// apart when the states in it stop wanting to be in it.
    /// </summary>
    public class BlocTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3121);
            state.commandPoints.current = 60;
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState Other(params string[] excluding)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                bool skip = false;
                foreach (string id in excluding) if (country.id == id) skip = true;
                if (!skip) return country;
            }
            return null;
        }

        void MakeThemLikeUs(string targetId)
        {
            var relationship = state.FindRelationship(state.playerCountryId, targetId);
            relationship.relations = 88f;
            relationship.trust = 85f;
            relationship.strategicAlignment = 85f;
            relationship.SetThreatPerceivedBy(targetId, 10f);
        }

        /// <summary>
        /// A second world identical to the fixture's, for measuring a delta
        /// against a control. `pillars.diplomacy` is raised to match `OurBloc`
        /// so the only difference between the two runs is the bloc itself.
        /// </summary>
        (GameState state, TurnManager turns) Fresh()
        {
            var other = WorldFactory.CreateDebugWorld(seed: 3121);
            other.commandPoints.current = 60;
            other.politicalCapital = GameState.PoliticalCapitalCap;
            other.authorizedPillarMask = ~0;
            other.PlayerCountry.pillars.diplomacy = 80f;

            var otherTurns = new TurnManager(other);
            SimulationPipeline.Wire(otherTurns, other);
            return (other, otherTurns);
        }

        Bloc OurBloc()
        {
            state.PlayerCountry.pillars.diplomacy = 80f;
            return BlocSystem.FoundBy(state, state.playerCountryId, "THE TEST UNDERSTANDING");
        }

        // ---------- founding ----------

        [Test]
        public void NobodyWithoutStandingCanLeadOne()
        {
            state.PlayerCountry.pillars.diplomacy = 20f;

            Assert.IsFalse(BlocSystem.CanFound(state, state.playerCountryId, out string reason),
                "A state nobody regards was allowed to found a bloc.");
            Assert.IsNotEmpty(reason, "a refusal with no reason is indistinguishable from a bug");
        }

        [Test]
        public void TheWorldWillNotCarryAFourthSide()
        {
            state.PlayerCountry.pillars.diplomacy = 90f;

            for (int i = 0; i < BlocSystem.MaxBlocs; i++)
            {
                var founder = state.countries[i + 1];
                founder.pillars.diplomacy = 90f;
                Assert.IsNotNull(BlocSystem.FoundBy(state, founder.id, $"BLOC {i}"));
            }

            Assert.IsFalse(BlocSystem.CanFound(state, state.playerCountryId, out _),
                $"A {BlocSystem.MaxBlocs + 1}th bloc was allowed. A world of one-member blocs "
                + "is a list, not a set of sides.");
        }

        // ---------- membership is bought, not declared ----------

        [Test]
        public void AStateThatDoesNotKnowUsWillNotJoin()
        {
            var bloc = OurBloc();
            var target = Other();

            var relationship = state.FindRelationship(state.playerCountryId, target.id);
            relationship.relations = 30f;
            relationship.trust = 25f;
            relationship.strategicAlignment = 30f;

            Assert.IsFalse(BlocSystem.InviteBy(state, state.playerCountryId, target.id),
                "A state we barely deal with joined our bloc for the asking.");
            Assert.IsFalse(bloc.Has(target.id));
        }

        [Test]
        public void APartnerOfLongStandingJoins()
        {
            var bloc = OurBloc();
            var target = Other();
            MakeThemLikeUs(target.id);

            Assert.IsTrue(BlocSystem.InviteBy(state, state.playerCountryId, target.id),
                $"willingness was {BlocSystem.JoinWillingness(state, bloc, target.id):F0}");
            Assert.IsTrue(bloc.Has(target.id));
        }

        [Test]
        public void NobodyJoinsALeaderTheyAreFrightenedOf()
        {
            var bloc = OurBloc();
            var target = Other();
            MakeThemLikeUs(target.id);

            float trusted = BlocSystem.JoinWillingness(state, bloc, target.id);

            var relationship = state.FindRelationship(state.playerCountryId, target.id);
            relationship.SetThreatPerceivedBy(target.id, 95f);

            float frightened = BlocSystem.JoinWillingness(state, bloc, target.id);

            Assert.Less(frightened, trusted - 20f,
                "A state was as willing to line up behind a power it fears as behind one it "
                + "does not. This is the term that stops a large power collecting the map.");
        }

        // ---------- it is worth something ----------

        [Test]
        public void ABlocVotesAsABloc()
        {
            var bloc = OurBloc();
            var partner = Other();
            MakeThemLikeUs(partner.id);
            Assert.IsTrue(BlocSystem.InviteBy(state, state.playerCountryId, partner.id));

            var subject = Other(partner.id);
            var motion = new CouncilMotion
            {
                id = "TEST", kind = MotionKind.Condemnation,
                moverId = state.playerCountryId, subjectId = subject.id,
                raised = state.date, summary = "Test."
            };

            float inBloc = CouncilSystem.VoteScore(state, motion, partner.id);

            BlocSystem.LeaveBy(state, partner.id);
            float outOfBloc = CouncilSystem.VoteScore(state, motion, partner.id);

            Assert.Greater(inBloc, outOfBloc,
                "A bloc partner voted with us no more readily than a state that had just "
                + "walked out. The chamber is the room a bloc exists for.");
        }

        [Test]
        public void MembershipPullsMembersIntoAlignment()
        {
            var bloc = OurBloc();
            var partner = Other();
            MakeThemLikeUs(partner.id);
            BlocSystem.InviteBy(state, state.playerCountryId, partner.id);

            var relationship = state.FindRelationship(state.playerCountryId, partner.id);
            relationship.strategicAlignment = 50f;
            bloc.cohesion = 80f;

            for (int month = 0; month < 12; month++)
            {
                bloc.cohesion = 80f;
                turns.EndMonth();
                if (!bloc.Has(partner.id)) Assert.Fail("the partner left mid-test");
            }

            Assert.Greater(relationship.strategicAlignment, 50f,
                "A year inside the same bloc left two states no more aligned than strangers.");
        }

        // ---------- and it costs ----------

        [Test]
        public void LeadingOneIsAMonthlyBill()
        {
            // A/B against a control, not a before/after on one run. Upkeep is
            // 0.35 PC for a two-member bloc and the same month credits PC
            // income, so a net measurement asks whether income happens to be
            // smaller than upkeep — and starting at the cap, as this fixture
            // originally did, hides the charge completely because the credit
            // clamps straight back to 20. The claim is a delta, so measure one.
            var control = Fresh();

            var bloc = OurBloc();
            var partner = Other();
            MakeThemLikeUs(partner.id);
            Assert.IsTrue(BlocSystem.InviteBy(state, state.playerCountryId, partner.id),
                "the fixture's partner declined, so there was no bloc to pay for");
            Assert.AreEqual(2, bloc.memberIds.Count, "the bloc never reached two members");

            // Both runs start from the same point, below the cap so a credit has
            // somewhere to go and cannot mask the debit.
            float start = GameState.PoliticalCapitalCap * 0.5f;
            control.state.politicalCapital = start;
            state.politicalCapital = start;

            control.turns.EndMonth();
            turns.EndMonth();

            Assert.Less(state.politicalCapital, control.state.politicalCapital,
                "Holding a bloc of governments together cost the leader nothing. A free "
                + "bloc is a free alliance, which is the thing this codebase keeps deleting.");
        }

        [Test]
        public void WalkingOutCostsTrustWithEveryoneStillInIt()
        {
            var bloc = OurBloc();
            var partner = Other();
            MakeThemLikeUs(partner.id);
            BlocSystem.InviteBy(state, state.playerCountryId, partner.id);

            var relationship = state.FindRelationship(state.playerCountryId, partner.id);
            float trust = relationship.trust;

            Assert.IsTrue(BlocSystem.LeaveBy(state, partner.id));

            Assert.Less(relationship.trust, trust,
                "A state walked out of a bloc and nobody in it thought less of them. A bloc "
                + "you can leave for nothing is a bloc that means nothing.");
        }

        [Test]
        public void ABlocOfStatesThatDislikeEachOtherComesApart()
        {
            var bloc = OurBloc();
            var partner = Other();
            MakeThemLikeUs(partner.id);
            BlocSystem.InviteBy(state, state.playerCountryId, partner.id);

            // The friendship curdles. Nothing touches cohesion directly.
            for (int month = 0; month < 60; month++)
            {
                var relationship = state.FindRelationship(state.playerCountryId, partner.id);
                if (relationship != null)
                {
                    relationship.relations = 4f;
                    relationship.strategicAlignment = 5f;
                    relationship.trust = 5f;
                }
                turns.EndMonth();
                if (!bloc.Has(partner.id) || bloc.dissolved) break;
            }

            Assert.IsFalse(bloc.Has(partner.id) && !bloc.dissolved,
                $"Five years of mutual dislike left the bloc intact at cohesion "
                + $"{bloc.cohesion:F0}. Cohesion is supposed to be what the members' own "
                + "relations will hold.");
        }

        // ---------- the world builds its own ----------

        [Test]
        public void TheWorldFormsBlocsWithoutThePlayer()
        {
            bool formed = false;
            for (int month = 0; month < 240 && !formed; month++)
            {
                turns.EndMonth();
                foreach (var bloc in state.blocs)
                    if (!bloc.dissolved && bloc.leaderId != state.playerCountryId
                        && bloc.memberIds.Count >= BlocSystem.MinimumMembers)
                        formed = true;
            }

            Assert.IsTrue(formed,
                "In twenty unattended years no foreign government founded a bloc anybody "
                + "joined. A verb the world will not reach for is a verb the world does "
                + "not have.");
        }

        [Test]
        public void BlocsSurviveASaveAndReload()
        {
            var bloc = OurBloc();
            var partner = Other();
            MakeThemLikeUs(partner.id);
            BlocSystem.InviteBy(state, state.playerCountryId, partner.id);
            bloc.cohesion = 63f;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = BlocSystem.BlocOf(loaded, loaded.playerCountryId);

            Assert.IsNotNull(restored, "the bloc did not survive the save");
            Assert.AreEqual(bloc.name, restored.name);
            Assert.AreEqual(63f, restored.cohesion, 0.01f);
            Assert.IsTrue(restored.Has(partner.id));
        }
    }
}

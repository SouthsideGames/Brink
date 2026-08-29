using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The multilateral chamber (GDD §15.2, §20, §28).
    ///
    /// The tests are the four claims the system rests on: votes are a live read
    /// of the diplomatic model rather than a second copy of it, the veto works
    /// and is not free, a lost vote costs the mover, and a carried motion changes
    /// what other verbs cost — because a resolution that only printed a line in
    /// the chronicle would be this codebase's oldest bug with a gavel.
    /// </summary>
    public class CouncilTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3390);
            state.commandPoints.current = 60;
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            CouncilSystem.EnsureSeated(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState SomeoneElse(params string[] excluding)
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

        /// <summary>Make the world want to condemn one state, and able to.</summary>
        void MakeAPariah(CountryState pariah)
        {
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(pariah.id)) continue;
                relationship.relations = 5f;
                relationship.trust = 5f;
                relationship.strategicAlignment = 10f;
                relationship.SetThreatPerceivedBy(relationship.PartnerOf(pariah.id), 85f);
                if (relationship.countryA == pariah.id) relationship.dependenceBOnA = 0f;
                else relationship.dependenceAOnB = 0f;
            }

            // Nobody is under an obligation to them.
            for (int i = state.treaties.Count - 1; i >= 0; i--)
                if (state.treaties[i].Involves(pariah.id)) state.treaties.RemoveAt(i);

            // And everyone likes the mover.
            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(state.playerCountryId)) continue;
                if (relationship.PartnerOf(state.playerCountryId) == pariah.id) continue;
                relationship.relations = 85f;
                relationship.trust = 80f;
                relationship.strategicAlignment = 85f;
            }
        }

        CouncilMotion Motion(MotionKind kind, string subjectId)
            => new CouncilMotion
            {
                id = "TEST_MOTION",
                kind = kind,
                moverId = state.playerCountryId,
                subjectId = subjectId,
                raised = state.date,
                summary = "Test motion."
            };

        // ---------- seats ----------

        [Test]
        public void TheChamberSeatsTheSameFiveEveryTime()
        {
            var first = new List<string>(state.council.permanentMembers);

            var again = WorldFactory.CreateDebugWorld(seed: 3390);
            CouncilSystem.EnsureSeated(again);

            CollectionAssert.AreEqual(first, again.council.permanentMembers,
                "The same world seated a different chamber twice. Permanent membership is a "
                + "fact about the world, and a save that reseats itself on load is a save "
                + "whose vetoes move.");
        }

        [Test]
        public void SeatingIsStableAcrossADecade()
        {
            var first = new List<string>(state.council.permanentMembers);
            for (int month = 0; month < 120; month++) turns.EndMonth();

            CollectionAssert.AreEqual(first, state.council.permanentMembers,
                "The chamber reshuffled itself as the league table moved. A permanent seat "
                + "that tracks this year's capability has no grievance in it, and the "
                + "grievance is the interesting part.");
        }

        // ---------- votes are a read of the world ----------

        [Test]
        public void AStateDoesNotVoteToSanctionItsOwnSupplier()
        {
            var subject = SomeoneElse();
            var voter = SomeoneElse(subject.id);
            MakeAPariah(subject);

            var motion = Motion(MotionKind.SanctionsMandate, subject.id);
            float hostileVote = CouncilSystem.VoteScore(state, motion, voter.id);

            // Now make the voter depend on them heavily and nothing else change.
            var relationship = state.FindRelationship(voter.id, subject.id);
            if (relationship.countryA == voter.id) relationship.dependenceAOnB = 90f;
            else relationship.dependenceBOnA = 90f;

            float dependentVote = CouncilSystem.VoteScore(state, motion, voter.id);

            Assert.Less(dependentVote, hostileVote - 30f,
                "Heavy dependence on the state being sanctioned did not move the vote. "
                + "Trade dependence is supposed to be a diplomatic asset, not only an "
                + "economic one.");
        }

        [Test]
        public void ADefencePactHoldsAVoteDown()
        {
            var subject = SomeoneElse();
            var voter = SomeoneElse(subject.id);
            MakeAPariah(subject);

            var motion = Motion(MotionKind.Condemnation, subject.id);
            float before = CouncilSystem.VoteScore(state, motion, voter.id);

            state.treaties.Add(new Treaty
            {
                id = "TEST_PACT",
                countryA = voter.id,
                countryB = subject.id,
                commitments = new List<TreatyCommitment> { TreatyCommitment.MutualDefense }
            });

            float after = CouncilSystem.VoteScore(state, motion, voter.id);

            Assert.Less(after, before - 30f,
                "A state voted to condemn a country it is pledged to defend as readily as "
                + "one it has signed nothing with.");
        }

        [Test]
        public void TheSubjectAlwaysVotesAgainstBeingCondemned()
        {
            var subject = SomeoneElse();
            var motion = Motion(MotionKind.Condemnation, subject.id);

            Assert.Less(CouncilSystem.VoteScore(state, motion, subject.id), 0f,
                "A state voted for its own condemnation.");
        }

        // ---------- the veto ----------

        [Test]
        public void APermanentMemberCanStopAnythingAndPaysForIt()
        {
            // Pick a subject that holds a permanent seat: the chamber cannot
            // censure a great power, which is the first true thing about one.
            string seatedId = state.council.permanentMembers[0];
            if (seatedId == state.playerCountryId && state.council.permanentMembers.Count > 1)
                seatedId = state.council.permanentMembers[1];

            var seated = state.FindCountry(seatedId);
            if (seated == null || seated.isPlayer) Assert.Ignore("this world seated no foreign major");

            MakeAPariah(seated);

            var supporter = SomeoneElse(seated.id);
            var beforeWithSupporter = state.FindRelationship(seated.id, supporter.id).relations;

            var motion = CouncilSystem.RaiseBy(state, state.playerCountryId,
                Motion(MotionKind.Condemnation, seated.id));

            Assert.AreEqual(MotionOutcome.Vetoed, motion.outcome,
                "A permanent member was censured by the chamber it sits on.");
            Assert.AreEqual(seated.id, motion.vetoedById);
            Assert.IsFalse(CouncilSystem.IsCensured(state, seated.id));

            Assert.Less(state.FindRelationship(seated.id, supporter.id).relations,
                beforeWithSupporter,
                "Blocking a motion everybody else supported cost the blocker nothing. "
                + "A free veto is a defensive superpower.");
        }

        // ---------- what a carried motion is worth ----------

        [Test]
        public void ACarriedCondemnationIsWeightAtTheTable()
        {
            var subject = SomeoneElse();
            if (state.council.IsPermanent(subject.id))
                subject = SomeoneElse(subject.id);
            if (subject == null || state.council.IsPermanent(subject.id))
                Assert.Ignore("this world gave every candidate a permanent seat");

            MakeAPariah(subject);

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId,
                subject.id, ConfrontationObjective.PolicyReversal, "", PrimaryStrategy.Diplomatic);
            Assert.IsNotNull(confrontation, "the fixture could not open a confrontation");

            float before = ConfrontationSystem.StrategicPressure(
                state, confrontation, state.playerCountryId);

            var motion = CouncilSystem.RaiseBy(state, state.playerCountryId,
                Motion(MotionKind.Condemnation, subject.id));
            Assert.AreEqual(MotionOutcome.Passed, motion.outcome,
                $"the fixture's motion did not carry ({motion.yes}-{motion.no}-{motion.abstain})");

            float after = ConfrontationSystem.StrategicPressure(
                state, confrontation, state.playerCountryId);

            Assert.Greater(after, before,
                "A carried condemnation changed nothing at the table it was supposed to "
                + "change. A resolution with no mechanical weight is a line in the chronicle.");
        }

        [Test]
        public void AMandateMakesCoercionCheaperAndHarderToEscape()
        {
            var subject = SomeoneElse();
            var player = state.PlayerCountry;

            state.sanctions.Add(new Sanction
            {
                senderId = player.id,
                targetId = subject.id,
                severity = SanctionSeverity.Coercive
            });

            float unmandated = EconomySystem.SanctionBlowbackFor(state, player.id);
            Assert.Greater(unmandated, 0f, "the fixture's sanction cost the sender nothing");

            state.council.mandates.Add(new CouncilMandate
            { subjectId = subject.id, monthsRemaining = 12 });

            float mandated = EconomySystem.SanctionBlowbackFor(state, player.id);

            Assert.Less(mandated, unmandated,
                "Multilateral authorisation did not reduce the sender's blowback, which is "
                + "the entire reason to spend a month assembling one.");

            Assert.IsFalse(EconomySystem.SeekSanctionsReliefBy(state, subject.id, player.id),
                "A sanctioned state talked its way out of measures the chamber had "
                + "authorised. A mandate that comes apart one relationship at a time is "
                + "worth less than a bilateral regime.");
        }

        [Test]
        public void LosingAVoteYouCalledCostsTheMover()
        {
            var subject = SomeoneElse();

            // Nobody agrees with us: warm to the subject, cold to us.
            foreach (var relationship in state.relationships)
            {
                if (relationship.Involves(subject.id))
                {
                    relationship.relations = 90f;
                    relationship.trust = 90f;
                }
                if (relationship.Involves(state.playerCountryId))
                {
                    relationship.relations = 12f;
                    relationship.strategicAlignment = 10f;
                    relationship.trust = 15f;
                }
            }

            var withSubject = state.FindRelationship(state.playerCountryId, subject.id);
            float before = withSubject.relations;
            float reciprocity = state.PlayerCountry.reciprocity;

            var motion = CouncilSystem.RaiseBy(state, state.playerCountryId,
                Motion(MotionKind.Condemnation, subject.id));

            Assert.AreNotEqual(MotionOutcome.Passed, motion.outcome,
                "the fixture's hopeless motion carried anyway");
            Assert.Less(withSubject.relations, before,
                "Naming a state in a motion that then failed cost nothing. The correct play "
                + "would be to table one every month and see what sticks.");
            Assert.Less(state.PlayerCountry.reciprocity, reciprocity);
        }

        // ---------- the agenda is scarce ----------

        [Test]
        public void TheChamberWillNotSitTwiceInAMonth()
        {
            var subject = SomeoneElse();
            MakeAPariah(subject);

            CouncilSystem.RaiseBy(state, state.playerCountryId,
                Motion(MotionKind.Condemnation, subject.id));

            Assert.IsFalse(CouncilSystem.CanRaise(state, state.playerCountryId, out string reason),
                "The chamber accepted a second motion in the same month. A body that "
                + "resolves four things a month is a ticker.");
            Assert.IsNotEmpty(reason, "a refusal with no reason is indistinguishable from a bug");
        }

        [Test]
        public void MotionsComeOnlyFromThingsStatesAreActuallyDoing()
        {
            // A world at peace, nobody occupying anybody, nobody sanctioned,
            // nobody destitute: there is nothing to put to a vote.
            foreach (var confrontation in state.confrontations) confrontation.resolved = true;
            foreach (var location in state.locations) location.ownerId = location.originalOwnerId;
            state.sanctions.Clear();
            state.insurgencies.Clear();
            foreach (var country in state.countries) country.livingStandards = 60f;

            var motions = CouncilSystem.AvailableMotions(state, state.playerCountryId);

            Assert.IsEmpty(motions,
                "The chamber found something to vote on in a world where nothing was "
                + "happening. It has no agenda of its own and must not invent a grievance.");
        }

        [Test]
        public void TheChamberSurvivesASaveAndReload()
        {
            var subject = SomeoneElse();
            state.council.censures.Add(new CouncilCensure
            { subjectId = subject.id, monthsRemaining = 9 });
            state.council.mandates.Add(new CouncilMandate
            { subjectId = subject.id, monthsRemaining = 14 });

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.IsTrue(CouncilSystem.IsCensured(loaded, subject.id));
            Assert.IsTrue(CouncilSystem.SanctionsMandated(loaded, subject.id));
            CollectionAssert.AreEqual(state.council.permanentMembers,
                loaded.council.permanentMembers);
        }
    }
}

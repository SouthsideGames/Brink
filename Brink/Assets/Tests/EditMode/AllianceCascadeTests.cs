using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The alliance cascade, held to its design (GDD §15.2, spec 04 §8).
    ///
    /// The audit measured ~11 AI wars per 30-year world against a design
    /// baseline of ~1.25, with 21 of 22 being obligation entries and single
    /// states carrying eight fronts. The mechanism was correct and the
    /// decision was near-unconditional: two indifferent states honoured a pact
    /// at the world's neutral defaults, a defensive guarantee was invoked on
    /// the aggressor's behalf as soon as anyone came to the victim's aid, and a
    /// front opened for an ally had no exit. These pin the repairs: warmth is
    /// counted relative to neutral, load and distance and recovery weigh, an
    /// offensive call is a choice with a soft price, and a guarantor's front
    /// closes with the war it was joined for.
    /// </summary>
    public class AllianceCascadeTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6161);
            foreach (var country in state.countries)
            {
                country.warExhaustion = 0f;
                country.stability = 70f;
                country.warSupport = 60f;
            }
        }

        // ---------- helpers ----------

        Bloc MakeBloc(string leaderId, params string[] memberIds)
        {
            var bloc = new Bloc
            {
                id = $"BLOC_{leaderId}",
                name = $"THE {leaderId} PACT",
                leaderId = leaderId,
                founded = state.date,
                cohesion = 70f,
                commitments = new List<TreatyCommitment> { TreatyCommitment.MutualDefense }
            };
            bloc.memberIds.Add(leaderId);
            foreach (string id in memberIds) bloc.memberIds.Add(id);
            state.blocs.Add(bloc);
            return bloc;
        }

        Treaty MakePact(string a, string b)
        {
            var treaty = new Treaty
            {
                id = $"T_{a}_{b}",
                countryA = a,
                countryB = b,
                commitments = new List<TreatyCommitment> { TreatyCommitment.MutualDefense }
            };
            state.treaties.Add(treaty);
            return treaty;
        }

        void Feel(string allyId, string towardId, float relations, float trust, float threatOfOther = -1f)
        {
            var pair = state.FindRelationship(allyId, towardId);
            pair.relations = relations;
            pair.trust = trust;
            pair.memoryWeight = 0f;
            if (threatOfOther >= 0f) pair.SetThreatPerceivedBy(allyId, threatOfOther);
        }

        /// <summary>
        /// Open the war at Tension only. Willingness is read here, *before*
        /// the escalation that invokes the guarantee: once the call-in has run,
        /// a repudiation has already chilled the relationship the formula reads.
        /// </summary>
        Confrontation OpenTension(string aggressorId, string defenderId)
            => ConfrontationSystem.BeginBy(state, aggressorId, defenderId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

        void Escalate(Confrontation war)
            => ConfrontationSystem.SetEscalationBy(state, war, EscalationState.LimitedConflict, war.initiatorId);

        Confrontation OpenWar(string aggressorId, string defenderId)
        {
            var war = OpenTension(aggressorId, defenderId);
            Escalate(war);
            return war;
        }

        static bool AtWar(GameState s, string a, string b)
        {
            var c = ConfrontationSystem.ExistingBetween(s, a, b);
            return c != null && c.escalation >= EscalationState.LimitedConflict;
        }

        // ---------- 1. indifference is not an alliance ----------

        [Test]
        public void AnIndifferentSignatoryDoesNotFight_AndPaysForIt()
        {
            // The world's neutral defaults: relations 50, trust 50, no shared
            // enemy. The old formula honoured here with ten points to spare.
            var treaty = MakePact("RUS", "IND");
            Feel("RUS", "IND", 50f, 50f);
            Feel("RUS", "CHN", 50f, 50f, threatOfOther: 30f);

            var war = OpenTension("CHN", "IND");
            Assert.Less(AllianceSystem.HonorWillingness(state, war, "RUS"), 50f,
                "two states with no history and no shared enemy should not go to war for each other");

            Escalate(war);
            Assert.IsFalse(AtWar(state, "RUS", "CHN"));

            // A defensive call declined is a repudiation, and it costs.
            Assert.IsTrue(treaty.broken, "walking away from a defensive guarantee breaks the treaty");
            Assert.IsNull(state.FindTreaty("RUS", "IND"), "a repudiated pact is no longer in force");
        }

        [Test]
        public void AWarmPartnerHonoursWhenFree_AndHesitatesUnderLoad()
        {
            MakePact("RUS", "IND");
            Feel("RUS", "IND", 78f, 72f);
            Feel("RUS", "CHN", 30f, 30f, threatOfOther: 55f);
            // No economic dependence on the aggressor: that is its own reason to
            // hesitate (HonorWillingness_WeighsDependenceOnTheAggressor) and
            // this test is about warmth, threat and load alone.
            state.FindRelationship("RUS", "CHN").SetDependenceOf("RUS", 0f);

            var war = OpenTension("CHN", "IND");
            float free = AllianceSystem.HonorWillingness(state, war, "RUS");
            Assert.GreaterOrEqual(free, 50f, $"a warm, threatened partner should honour when idle ({free})");

            // The same partner already carrying two limited wars.
            foreach (var other in new[] { "BRA", "TUR" })
            {
                var extra = ConfrontationSystem.BeginBy(state, "RUS", other,
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                Assume.That(extra, Is.Not.Null);
                extra.escalation = EscalationState.LimitedConflict;
            }
            float loaded = AllianceSystem.HonorWillingness(state, war, "RUS");
            Assert.Less(loaded, free - 20f, "two fronts already open should weigh heavily on a third");
        }

        [Test]
        public void ABlocMemberHonoursThroughASecondFront_NotAFourth()
        {
            MakeBloc("RUS", "IND", "BRA");
            Feel("RUS", "IND", 75f, 70f);
            Feel("RUS", "CHN", 30f, 30f, threatOfOther: 50f);

            var war = OpenTension("CHN", "IND");
            float idle = AllianceSystem.HonorWillingness(state, war, "RUS");
            Assert.GreaterOrEqual(idle, 50f, $"a bloc member honours when idle ({idle})");

            // One front already: still comes.
            var one = ConfrontationSystem.BeginBy(state, "RUS", "TUR",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            one.escalation = EscalationState.LimitedConflict;
            Assert.GreaterOrEqual(AllianceSystem.HonorWillingness(state, war, "RUS"), 50f,
                "one existing war should not stop a bloc member honouring");

            // Three fronts: the army is elsewhere.
            foreach (var other in new[] { "KAZ", "SAU" })
            {
                var extra = ConfrontationSystem.BeginBy(state, "RUS", other,
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                Assume.That(extra, Is.Not.Null);
                extra.escalation = EscalationState.LimitedConflict;
            }
            Assert.Less(AllianceSystem.HonorWillingness(state, war, "RUS"), 50f,
                "three existing wars should make even a bloc member decline");
        }

        // ---------- 2. distance and recovery weigh ----------

        [Test]
        public void ADistantGuarantorIsLessWillingThanANeighbour()
        {
            // Same warmth toward the victim, same view of the aggressor; the only
            // difference is where the guarantor sits.
            MakePact("RUS", "IND");
            MakePact("BRA", "IND");
            Feel("RUS", "IND", 78f, 72f);
            Feel("BRA", "IND", 78f, 72f);
            Feel("RUS", "CHN", 30f, 30f, threatOfOther: 55f);
            Feel("BRA", "CHN", 30f, 30f, threatOfOther: 55f);
            state.FindRelationship("RUS", "IND").interoperability = 0f;
            state.FindRelationship("BRA", "IND").interoperability = 0f;
            state.FindRelationship("RUS", "CHN").SetDependenceOf("RUS", 0f);
            state.FindRelationship("BRA", "CHN").SetDependenceOf("BRA", 0f);

            var war = OpenTension("CHN", "IND");
            float near = GeographySystem.ReachFactorTo(state, "RUS", "CHN");
            float far = GeographySystem.ReachFactorTo(state, "BRA", "CHN");
            Assume.That(far, Is.LessThan(near), "the fixture needs one guarantor genuinely further away");

            Assert.Less(AllianceSystem.HonorWillingness(state, war, "BRA"),
                AllianceSystem.HonorWillingness(state, war, "RUS"),
                "a guarantee across an ocean should be harder to honour than one on the border");
        }

        [Test]
        public void AGuarantorJustOutOfAWarIsLessWilling()
        {
            MakePact("RUS", "IND");
            Feel("RUS", "IND", 78f, 72f);
            Feel("RUS", "CHN", 30f, 30f, threatOfOther: 55f);

            var war = OpenTension("CHN", "IND");
            float rested = AllianceSystem.HonorWillingness(state, war, "RUS");

            // A war of Russia's own, just settled.
            var past = ConfrontationSystem.BeginBy(state, "RUS", "TUR",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            past.resolved = true;
            past.monthsActive = 0;
            Assume.That(AllianceSystem.RecentlyAtWar(state, "RUS"), Is.True);

            Assert.Less(AllianceSystem.HonorWillingness(state, war, "RUS"), rested,
                "a state whose own war ended last month should be less eager for another");
        }

        // ---------- 3. offensive calls are a choice ----------

        [Test]
        public void AnAggressorsGuarantorIsAskedIndirectly_AndDecliningIsNotABetrayal()
        {
            // Two blocs. BRA will defend IND. RUS is warm to CHN but only
            // ordinarily so — under the old rule it came in regardless.
            MakeBloc("IND", "BRA");
            MakeBloc("CHN", "RUS");
            Feel("BRA", "IND", 90f, 90f);
            Feel("BRA", "CHN", 20f, 20f, threatOfOther: 80f);
            Feel("RUS", "CHN", 62f, 58f);
            Feel("RUS", "BRA", 45f, 45f, threatOfOther: 35f);

            OpenWar("CHN", "IND");

            Assert.IsTrue(AtWar(state, "BRA", "CHN"), "the defender's guarantor comes in");
            var braFront = ConfrontationSystem.ExistingBetween(state, "BRA", "CHN");
            Assert.IsFalse(AllianceSystem.IsDirectCall(state, braFront),
                "the call to the aggressor's guarantor is an offensive one");

            Assert.IsFalse(AtWar(state, "RUS", "BRA"), "an ordinarily warm partner does not join an attack");

            // And nothing was betrayed: the bloc stands, the treaty stands, no
            // sanctions, only the asker's opinion.
            Assert.IsTrue(BlocSystem.SameBloc(state, "CHN", "RUS"), "declining an offensive call must not expel");
            Assert.IsNull(state.FindSanction("CHN", "RUS"), "declining an offensive call must not draw sanctions");
            Assert.Less(state.FindRelationship("RUS", "CHN").relations, 62f, "the asker remembers");
        }

        [Test]
        public void AnAggressorsGuarantorMayStillChooseToJoin()
        {
            MakeBloc("IND", "BRA");
            MakeBloc("CHN", "RUS");
            Feel("BRA", "IND", 90f, 90f);
            Feel("BRA", "CHN", 20f, 20f, threatOfOther: 80f);
            Feel("RUS", "CHN", 95f, 95f);
            Feel("RUS", "BRA", 10f, 10f, threatOfOther: 85f);
            state.FindRelationship("RUS", "CHN").memoryWeight = 5f;

            OpenWar("CHN", "IND");

            Assert.IsTrue(AtWar(state, "BRA", "CHN"));
            Assert.IsTrue(AtWar(state, "RUS", "BRA"),
                "a state that fears the other side and stands with its ally may still choose to join");
        }

        [Test]
        public void AnOffensiveCallIsRefusedAtTheCeiling_WhereADefensiveOneIsNot()
        {
            MakeBloc("IND", "BRA");
            MakeBloc("CHN", "RUS");
            Feel("BRA", "IND", 90f, 90f);
            Feel("BRA", "CHN", 20f, 20f, threatOfOther: 80f);
            Feel("RUS", "CHN", 95f, 95f);
            Feel("RUS", "BRA", 10f, 10f, threatOfOther: 85f);
            state.FindRelationship("RUS", "CHN").memoryWeight = 5f;

            // Russia already fighting to its ceiling.
            foreach (var other in new[] { "TUR", "KAZ" })
            {
                var extra = ConfrontationSystem.BeginBy(state, "RUS", other,
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                Assume.That(extra, Is.Not.Null);
                ConfrontationSystem.SetEscalationBy(state, extra, EscalationState.TotalWar, "RUS");
            }
            Assume.That(ConfrontationSystem.CanOpenAnother(state, "RUS", out _), Is.False);

            OpenWar("CHN", "IND");
            Assert.IsFalse(AtWar(state, "RUS", "BRA"),
                "joining an ally's offensive war is a war of choice, and the ceiling applies to those");
        }

        // ---------- 4. the front closes with the war it was joined for ----------

        [Test]
        public void AGuarantorsFrontClosesWhenTheWarItJoinedEnds()
        {
            MakePact("BRA", "IND");
            Feel("BRA", "IND", 90f, 90f);
            Feel("BRA", "CHN", 20f, 20f, threatOfOther: 80f);

            var root = OpenWar("CHN", "IND");
            var satellite = ConfrontationSystem.ExistingBetween(state, "BRA", "CHN");
            Assume.That(satellite, Is.Not.Null, "the fixture needs the guarantor to come in");
            Assert.AreEqual(root.id, satellite.obligationRootId);
            Assert.AreEqual("IND", satellite.obligationOnBehalfOfId);

            // India settles with China.
            root.defenderWarExhaustion = 95f;
            state.FindCountry("CHN").warSupport = 5f;
            root.monthsActive = 6;
            Assume.That(ConfrontationSystem.ProposeSettlementBy(state, root, "IND", concedeInstead: true), Is.True);
            Assume.That(root.resolved, Is.True);

            Assert.IsTrue(satellite.resolved,
                "a front opened in defence of an ally has no purpose once the ally has settled");
            StringAssert.Contains("has ended", satellite.outcomeSummary);
        }

        [Test]
        public void AChosenWarDoesNotCloseWhenSomebodyElsesDoes()
        {
            var a = OpenWar("CHN", "IND");
            var b = OpenWar("RUS", "TUR");
            a.defenderWarExhaustion = 95f;
            state.FindCountry("CHN").warSupport = 5f;
            a.monthsActive = 6;
            Assume.That(ConfrontationSystem.ProposeSettlementBy(state, a, "IND", concedeInstead: true), Is.True);
            Assert.IsFalse(b.resolved, "an unrelated war is not a satellite of anything");
        }

        // ---------- 5. a government seeks terms on every front ----------

        [Test]
        public void AGovernmentSeeksTermsOnEveryFront_NotOnlyItsFirst()
        {
            // Russia in two wars, both of which it wants out of, both opponents
            // willing. The old manager looked at the first front only.
            var first = OpenWar("RUS", "TUR");
            var second = OpenWar("RUS", "KAZ");
            foreach (var war in new[] { first, second })
            {
                war.monthsActive = 6;
                war.initiatorWarExhaustion = 90f;
                war.defenderWarExhaustion = 95f;
            }
            state.FindCountry("RUS").warSupport = 5f;
            state.FindCountry("TUR").warSupport = 5f;
            state.FindCountry("KAZ").warSupport = 5f;

            // Two months of AI thinking: one proposal per month at most.
            AISystem.MonthlyThink(state);
            AISystem.MonthlyThink(state);

            Assert.IsTrue(first.resolved, "the first front should have been settled");
            Assert.IsTrue(second.resolved,
                "the second front was never examined for terms — an obligation front would have had no exit");
        }

        // ---------- 6. the settlement truce is kept, not erased ----------

        [Test]
        public void HonouringDoesNotEraseAStandingPeace()
        {
            MakePact("BRA", "IND");
            Feel("BRA", "IND", 90f, 90f);
            Feel("BRA", "CHN", 20f, 20f, threatOfOther: 80f);
            var pair = state.FindRelationship("BRA", "CHN");
            pair.settlementTruceMonths = 9;

            OpenWar("CHN", "IND");
            Assume.That(AtWar(state, "BRA", "CHN"), Is.True);

            Assert.AreEqual(9, pair.settlementTruceMonths,
                "a peace two states made last year is not erased by a call-in; it stops them choosing a new war with each other, which this is not");
        }
    }
}

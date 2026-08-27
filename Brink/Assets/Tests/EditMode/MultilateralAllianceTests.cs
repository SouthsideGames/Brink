using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Multi-party alliances and the wars they cause (GDD §15.2, §18; user
    /// decision 2026-08-27).
    ///
    /// Three claims, one per thing that was missing:
    ///
    /// 1. **A bloc can carry commitments**, so an alliance of eight states is one
    ///    signature rather than twenty-eight bilateral treaties, and joining is
    ///    priced on what the bloc actually asks.
    /// 2. **Honouring opens a real war.** The old behaviour added the ally to a
    ///    coalition and stopped, so the operator was told they had entered a
    ///    conflict they had no front in and could give no orders about.
    /// 3. **It cascades.** Each entry is itself an attack, so the newly-attacked
    ///    party's own guarantors are asked — which is how three states and three
    ///    states become one war between six.
    ///
    /// And the fourth, which is the reason the decision is interesting at all:
    /// **walking away costs more than a relations number** — sanctions, cancelled
    /// preferential trade, expulsion from the bloc, and a grievance the AI's own
    /// rivalry reasoning can carry to a war later.
    /// </summary>
    public class MultilateralAllianceTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4477);
            turns = new TurnManager(state);
            // NARROW PIPELINE: the AI, economy, government and social ticks are omitted.
            // No test here advances more than a single month; every treaty, bloc,
            // relationship and escalation is set directly, and the assertions are
            // AllianceSystem's own invocation, cascade and repudiation arithmetic plus
            // BlocSystem's acceptance maths. Neither needs world evolution, and running
            // sixteen AI governments would let an unrelated foreign decision open a
            // confrontation that changes what the cascade finds.
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            state.commandPoints.current = 60;
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
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

        /// <summary>Make this state certain to answer a call, so the test measures the
        /// cascade rather than one government's mood.</summary>
        void WillFight(string allyId, string defenderId, string aggressorId)
        {
            var ally = state.FindCountry(allyId);
            ally.warExhaustion = 0f;
            ally.stability = 90f;
            ally.warSupport = 90f;

            var toDefender = state.FindRelationship(allyId, defenderId);
            toDefender.relations = 95f;
            toDefender.trust = 95f;
            toDefender.memoryWeight = 5f;

            var toAggressor = state.FindRelationship(allyId, aggressorId);
            toAggressor.SetDependenceOf(allyId, 0f);
            toAggressor.SetThreatPerceivedBy(allyId, 85f);
        }

        void WillNotFight(string allyId, string defenderId, string aggressorId)
        {
            var ally = state.FindCountry(allyId);
            ally.warExhaustion = 95f;
            ally.stability = 20f;
            ally.warSupport = 10f;

            var toDefender = state.FindRelationship(allyId, defenderId);
            toDefender.relations = 10f;
            toDefender.trust = 10f;
            toDefender.interoperability = 0f;
            toDefender.memoryWeight = -5f;

            var toAggressor = state.FindRelationship(allyId, aggressorId);
            toAggressor.SetDependenceOf(allyId, 95f);
            toAggressor.SetThreatPerceivedBy(allyId, 0f);
        }

        Confrontation OpenWar(string aggressorId, string defenderId)
        {
            var confrontation = ConfrontationSystem.BeginBy(state, aggressorId, defenderId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            Assert.IsNotNull(confrontation,
                $"the fixture could not open {aggressorId} vs {defenderId}");
            ConfrontationSystem.SetEscalationBy(
                state, confrontation, EscalationState.LimitedConflict, aggressorId);
            return confrontation;
        }

        static bool AtWar(GameState state, string a, string b)
            => ConfrontationSystem.ExistingBetween(state, a, b) != null;

        // ---------- 1. a bloc is an alliance ----------

        [Test]
        public void ABlocCanCarryAMutualDefenseCommitment()
        {
            var bloc = MakeBloc("RUS", "IND");

            Assert.IsTrue(bloc.Guarantees(TreatyCommitment.MutualDefense));
            CollectionAssert.AreEquivalent(new[] { "IND" }, bloc.PartnersOf("RUS"),
                "a bloc member's partners are everyone else in it");
        }

        [Test]
        public void BlocMembershipGuaranteesEveryOtherMember_WithNoBilateralTreaty()
        {
            MakeBloc("RUS", "IND", "BRA");
            Assert.IsNull(state.FindTreaty("RUS", "IND"),
                "the fixture must prove the guarantee comes from the bloc, not a treaty");

            var guarantors = AllianceSystem.GuarantorsOf(state, "IND", "CHN");

            var ids = new List<string>();
            foreach (var guarantor in guarantors) ids.Add(guarantor.countryId);
            CollectionAssert.Contains(ids, "RUS");
            CollectionAssert.Contains(ids, "BRA");
        }

        [Test]
        public void ABlocWithoutTheCommitmentGuaranteesNobody()
        {
            var bloc = MakeBloc("RUS", "IND");
            bloc.commitments.Clear();
            bloc.commitments.Add(TreatyCommitment.TradePreference);

            Assert.AreEqual(0, AllianceSystem.GuarantorsOf(state, "IND", "CHN").Count,
                "an economic union is not a defence pact");
        }

        [Test]
        public void ADefenceBlocIsAHarderSellThanAnAlignment()
        {
            var alignment = MakeBloc("RUS", "IND");
            alignment.commitments.Clear();

            var target = state.FindCountry("BRA");
            Assert.IsNotNull(target);

            float asAlignment = BlocSystem.JoinWillingness(state, alignment, target.id);

            alignment.commitments.Add(TreatyCommitment.MutualDefense);
            float asPact = BlocSystem.JoinWillingness(state, alignment, target.id);

            Assert.Less(asPact, asAlignment,
                "a bloc that can get you killed must be harder to recruit into than one "
                + "that cannot — otherwise the commitment is free");
        }

        [Test]
        public void AStateThatWalkedOutIsNotReadmittedOnAnApology()
        {
            var bloc = MakeBloc("RUS", "IND");
            var target = state.FindCountry("BRA");

            float before = BlocSystem.JoinWillingness(state, bloc, target.id);
            bloc.repudiatedBy.Add(target.id);
            float after = BlocSystem.JoinWillingness(state, bloc, target.id);

            Assert.Less(after, before, "the door does not simply reopen");
        }

        [Test]
        public void ABlocPactCountsTowardEncirclementJustAsTreatiesDo()
        {
            // Six states guaranteed bilaterally, versus the same six in one bloc.
            // The anxiety they cause the rest of the world must be the same: what
            // frightens an outsider is how many governments would come, not how
            // the promise was papered.
            var partners = new List<string>();
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                partners.Add(country.id);
                if (partners.Count == 6) break;
            }
            Assert.AreEqual(6, partners.Count, "the fixture needs six partners");

            string playerId = state.playerCountryId;
            foreach (string id in partners)
                state.treaties.Add(new Treaty
                {
                    id = $"T_{playerId}_{id}",
                    countryA = playerId,
                    countryB = id,
                    commitments = new List<TreatyCommitment> { TreatyCommitment.MutualDefense }
                });

            float bilateral = DiplomacySystem.PactAnxiety(state, playerId);
            Assert.Greater(bilateral, 0f, "six pacts must read as encirclement");

            state.treaties.Clear();
            Assert.AreEqual(0f, DiplomacySystem.PactAnxiety(state, playerId), 0.001f);

            MakeBloc(playerId, partners.ToArray());

            Assert.AreEqual(bilateral, DiplomacySystem.PactAnxiety(state, playerId), 0.001f,
                "a bloc that paid no encirclement anxiety would strictly dominate the "
                + "bilateral route and let the operator quietly collect the map — the exact "
                + "failure the world-heat work closed");
        }

        // ---------- 2. honouring opens a real war ----------

        [Test]
        public void HonoringOpensARealFront_NotOnlyACoalitionSeat()
        {
            MakeBloc("RUS", "IND");
            WillFight("RUS", "IND", "CHN");

            var war = OpenWar("CHN", "IND");

            Assert.IsTrue(AtWar(state, "RUS", "CHN"),
                "honouring a defence commitment must produce a front the ally can actually "
                + "fight on — a coalition seat alone is a war you cannot give an order in");

            var coalition = state.FindCoalitionLedBy(war.id, "IND");
            CollectionAssert.Contains(coalition?.memberIds, "RUS",
                "and it must still coordinate on the defender's own front");
        }

        [Test]
        public void TheNewFrontOpensAtLimitedConflict()
        {
            MakeBloc("RUS", "IND");
            WillFight("RUS", "IND", "CHN");
            OpenWar("CHN", "IND");

            var front = ConfrontationSystem.ExistingBetween(state, "RUS", "CHN");
            Assert.IsNotNull(front);
            Assert.GreaterOrEqual((int)front.escalation, (int)EscalationState.LimitedConflict,
                "entering a war already being fought is not a period of tension");
        }

        [Test]
        public void ACommitmentIsHonoredEvenWhenTheForceIsAlreadyAtItsCeiling()
        {
            MakeBloc("RUS", "IND");
            WillFight("RUS", "IND", "CHN");

            // Commit Russia elsewhere until it can no longer *choose* another war.
            foreach (var country in state.countries)
            {
                if (country.id == "RUS" || country.id == "CHN" || country.id == "IND") continue;
                var extra = ConfrontationSystem.BeginBy(state, "RUS", country.id,
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                if (extra == null) continue;
                ConfrontationSystem.SetEscalationBy(
                    state, extra, EscalationState.TotalWar, "RUS");
                if (!ConfrontationSystem.CanOpenAnother(state, "RUS", out _)) break;
            }

            Assert.IsFalse(ConfrontationSystem.CanOpenAnother(state, "RUS", out _),
                "the fixture must actually reach the commitment ceiling");

            OpenWar("CHN", "IND");

            Assert.IsTrue(AtWar(state, "RUS", "CHN"),
                "MaxCommitment stops a state CHOOSING another war; it has no business "
                + "refusing one an ally's attacker started. A ceiling that can block a "
                + "call-in makes the game forbid the operator from keeping their word.");
        }

        // ---------- 3. the cascade ----------

        [Test]
        public void TwoBlocsCollide_AndEveryoneEndsUpInTheWar()
        {
            // Our side and theirs, with no bilateral treaty anywhere: the whole
            // war is produced by two multilateral guarantees meeting.
            MakeBloc("IND", "BRA");     // the defender's alliance
            MakeBloc("CHN", "RUS");     // the aggressor's alliance

            WillFight("BRA", "IND", "CHN");
            WillFight("RUS", "CHN", "BRA");

            OpenWar("CHN", "IND");

            Assert.IsTrue(AtWar(state, "BRA", "CHN"),
                "the defender's guarantor comes in against the aggressor");
            Assert.IsTrue(AtWar(state, "RUS", "BRA"),
                "and that entry is itself an attack, so the AGGRESSOR's guarantor is asked "
                + "in turn — the cascade that made a bloc war impossible before");
        }

        [Test]
        public void TheCascadeTerminates()
        {
            MakeBloc("IND", "BRA");
            MakeBloc("CHN", "RUS");

            foreach (var country in state.countries)
                foreach (var other in state.countries)
                    if (country.id != other.id)
                    {
                        var pair = state.FindRelationship(country.id, other.id);
                        if (pair != null) pair.memoryWeight = 5f;
                    }

            foreach (var country in state.countries)
            {
                country.warExhaustion = 0f;
                country.stability = 95f;
                country.warSupport = 95f;
            }

            Assert.DoesNotThrow(() => OpenWar("CHN", "IND"),
                "an alliance graph must not be able to recurse without bound");

            Assert.LessOrEqual(state.confrontations.Count, state.countries.Count * state.countries.Count,
                "the cascade is bounded by the number of pairs");
        }

        [Test]
        public void AStateAlreadyFightingTheAggressorIsNotAskedAgain()
        {
            MakeBloc("RUS", "IND");
            WillFight("RUS", "IND", "CHN");

            var existing = OpenWar("RUS", "CHN");
            OpenWar("CHN", "IND");

            int count = 0;
            foreach (var confrontation in state.confrontations)
                if (!confrontation.resolved && confrontation.Involves("RUS")
                    && confrontation.Involves("CHN")) count++;

            Assert.AreEqual(1, count,
                "the obligation is discharged by the war they are already in");
            Assert.IsFalse(existing.resolved);
        }

        // ---------- 4. what walking away costs ----------

        [Test]
        public void RepudiationCostsStanding_NotNationalCapability()
        {
            MakeBloc("RUS", "IND");
            WillNotFight("RUS", "IND", "CHN");

            var russia = state.FindCountry("RUS");
            float diplomacyBefore = russia.pillars.diplomacy;

            OpenWar("CHN", "IND");

            Assert.IsFalse(AtWar(state, "RUS", "CHN"), "the fixture must actually repudiate");
            Assert.AreEqual(diplomacyBefore, russia.pillars.diplomacy, 0.001f,
                "a capability hit has no recovery path for the operator who incurred it, so "
                + "it is a slow disqualification rather than a price — the same fix this "
                + "project already made for intelligence exposure");

            var pair = state.FindRelationship("RUS", "IND");
            Assert.Less(pair.trust, 20f, "the cost lands in standing, where it is recoverable");
        }

        [Test]
        public void RepudiationDrawsSanctionsFromThePeopleWhoWereCountingOnUs()
        {
            MakeBloc("RUS", "IND", "BRA");
            WillNotFight("RUS", "IND", "CHN");

            OpenWar("CHN", "IND");

            Assert.IsNotNull(state.FindSanction("IND", "RUS"),
                "the state that was let down answers with more than a relations number");
        }

        [Test]
        public void RepudiationWithdrawsPreferentialTrade()
        {
            MakeBloc("RUS", "IND");
            WillNotFight("RUS", "IND", "CHN");

            state.treaties.Add(new Treaty
            {
                id = "T_RUS_IND",
                countryA = "RUS",
                countryB = "IND",
                commitments = new List<TreatyCommitment> { TreatyCommitment.TradePreference },
                clauses = new List<TreatyClause>
                {
                    new TreatyClause { commitment = TreatyCommitment.TradePreference }
                }
            });

            OpenWar("CHN", "IND");

            var treaty = state.FindTreaty("RUS", "IND");
            Assert.IsFalse(treaty.Has(TreatyCommitment.TradePreference),
                "a state that will not fight for you does not keep the trade you gave it "
                + "for being an ally");
            Assert.AreEqual(0, treaty.clauses.Count,
                "and the sided record must not keep a clause the flat list no longer has");
        }

        [Test]
        public void RepudiationGetsYouPutOutOfTheBloc()
        {
            var bloc = MakeBloc("IND", "RUS");
            WillNotFight("RUS", "IND", "CHN");

            OpenWar("CHN", "IND");

            Assert.IsFalse(bloc.Has("RUS"),
                "a member who would not come when the bloc was called is not a member");
            CollectionAssert.Contains(bloc.repudiatedBy, "RUS");
        }

        [Test]
        public void ABetrayedStateComesToRegardTheAbandonerAsAThreat()
        {
            MakeBloc("RUS", "IND");
            WillNotFight("RUS", "IND", "CHN");

            var pair = state.FindRelationship("IND", "RUS");
            float threatBefore = pair.ThreatPerceivedBy("IND");

            OpenWar("CHN", "IND");

            Assert.Greater(pair.ThreatPerceivedBy("IND"), threatBefore,
                "abandonment must move the inputs the AI's own rivalry reasoning reads, so "
                + "a betrayed ally can become an enemy in its own time — not by script");
        }

        [Test]
        public void HonoringInABlocIsSeenByEveryMemberOfIt()
        {
            var bloc = MakeBloc("IND", "RUS", "BRA");
            WillFight("RUS", "IND", "CHN");

            var withThirdParty = state.FindRelationship("RUS", "BRA");
            float trustBefore = withThirdParty.trust;
            float cohesionBefore = bloc.cohesion;

            OpenWar("CHN", "IND");

            Assert.Greater(withThirdParty.trust, trustBefore,
                "a public commitment kept is kept in front of everyone in the room");
            Assert.Greater(bloc.cohesion, cohesionBefore);
        }

        // ---------- 5. the operator can see the war ----------

        [Test]
        public void TheRosterNamesEveryBelligerentOnBothSides()
        {
            string playerId = state.playerCountryId;

            var war = OpenWar("CHN", playerId);
            var coalition = new Coalition
            {
                id = "COAL_TEST",
                leaderId = "CHN",
                confrontationId = war.id,
                targetId = playerId
            };
            coalition.memberIds.Add("CHN");
            coalition.memberIds.Add("RUS");
            state.coalitions.Add(coalition);

            var enemies = BelligerentRoster.EnemiesOf(state, playerId);

            var ids = new List<string>();
            foreach (var entry in enemies) ids.Add(entry.countryId);

            CollectionAssert.Contains(ids, "CHN", "the state we face directly");
            CollectionAssert.Contains(ids, "RUS",
                "and the state standing behind them, which is shooting at us whether or not "
                + "a confrontation names the pair");

            foreach (var entry in enemies)
                Assert.IsNotEmpty(entry.because,
                    $"{entry.countryId} appears in the roster with no account of why");
        }

        [Test]
        public void TheRosterListsEachBelligerentOnce()
        {
            string playerId = state.playerCountryId;

            var war = OpenWar("CHN", playerId);
            var coalition = new Coalition
            {
                id = "COAL_TEST",
                leaderId = "CHN",
                confrontationId = war.id,
                targetId = playerId
            };
            coalition.memberIds.Add("CHN");
            state.coalitions.Add(coalition);

            var enemies = BelligerentRoster.EnemiesOf(state, playerId);

            int chinaRows = 0;
            foreach (var entry in enemies) if (entry.countryId == "CHN") chinaRows++;
            Assert.AreEqual(1, chinaRows,
                "the operator needs a roster, not a spreadsheet");
        }

        [Test]
        public void TheRosterShowsWhoWeWouldHaveToDefend()
        {
            MakeBloc(state.playerCountryId, "IND");

            CollectionAssert.Contains(
                BelligerentRoster.ObligationsOwedBy(state, state.playerCountryId), "IND",
                "the other half of the ledger: how much of the world's trouble is ours");
        }

        [Test]
        public void TheRosterIsEmptyInPeacetime()
        {
            Assert.AreEqual(0, BelligerentRoster.EnemiesOf(state, state.playerCountryId).Count);
            Assert.AreEqual(0, BelligerentRoster.PartnersOf(state, state.playerCountryId).Count);
        }

        // ---------- 6. the player is asked, not told ----------

        [Test]
        public void ThePlayerIsGivenTheChoiceAndTheWarWaitsForIt()
        {
            string playerId = state.playerCountryId;
            MakeBloc(playerId, "IND");

            OpenWar("CHN", "IND");

            ActiveCrisis obligation = null;
            foreach (var crisis in state.activeCrises)
                if (crisis.defId == AllianceSystem.PlayerObligationCrisisId) obligation = crisis;

            Assert.IsNotNull(obligation, "the operator must be asked, never auto-committed");
            Assert.IsFalse(AtWar(state, playerId, "CHN"),
                "and nothing may be decided until they answer");
            Assert.AreEqual(2, obligation.options.Count);
            Assert.IsNotEmpty(obligation.contextId,
                "the crisis must name the war it is about — a cascade can put two "
                + "obligations in front of the operator at once");
        }

        [Test]
        public void AnsweringYesPutsUsInTheWar()
        {
            string playerId = state.playerCountryId;
            MakeBloc(playerId, "IND");
            OpenWar("CHN", "IND");

            ActiveCrisis obligation = null;
            foreach (var crisis in state.activeCrises)
                if (crisis.defId == AllianceSystem.PlayerObligationCrisisId) obligation = crisis;
            Assert.IsNotNull(obligation);

            CrisisSystem.Resolve(state, obligation, 0);

            Assert.IsTrue(AtWar(state, playerId, "CHN"),
                "honouring must give the operator a front they can give orders on");
        }

        [Test]
        public void AnsweringNoCostsUsTheBlocAndDrawsAReply()
        {
            string playerId = state.playerCountryId;
            var bloc = MakeBloc(playerId, "IND");
            OpenWar("CHN", "IND");

            ActiveCrisis obligation = null;
            foreach (var crisis in state.activeCrises)
                if (crisis.defId == AllianceSystem.PlayerObligationCrisisId) obligation = crisis;
            Assert.IsNotNull(obligation);

            CrisisSystem.Resolve(state, obligation, 1);

            Assert.IsFalse(AtWar(state, playerId, "CHN"));
            Assert.IsFalse(bloc.Has(playerId), "we are put out of the alliance we would not honour");
            Assert.IsNotNull(state.FindSanction("IND", playerId),
                "and the state we abandoned answers");
        }

        [Test]
        public void ALapsedObligationIsARepudiation()
        {
            string playerId = state.playerCountryId;
            var bloc = MakeBloc(playerId, "IND");
            OpenWar("CHN", "IND");

            Assert.IsTrue(bloc.Has(playerId), "the fixture must start with us in the bloc");

            turns.EndMonth();

            Assert.IsFalse(bloc.Has(playerId),
                "saying nothing to a partner who asked for help is an answer");
        }
    }
}

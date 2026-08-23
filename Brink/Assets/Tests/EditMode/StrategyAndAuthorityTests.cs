using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Two design promises that had no implementation: that a confrontation can
    /// be won through a pillar other than the military (GDD §18.2, §20), and
    /// that constitutional authority governs what the operator may personally
    /// command (GDD §3, a non-negotiable rule).
    /// </summary>
    public class StrategyAndAuthorityTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 9090);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        Confrontation Open(PrimaryStrategy strategy)
            => ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.PolicyReversal, null, strategy);

        // ---------- primary strategy ----------

        [Test]
        public void EconomicPressure_MovesAnOpponentTowardTerms()
        {
            var confrontation = Open(PrimaryStrategy.Economic);
            float before = ConfrontationSystem.SettlementWillingnessFor(
                state, confrontation, state.playerCountryId);

            var opponent = state.FindCountry("CHN");
            opponent.economy.growthRate = -6f;
            opponent.economy.confidence = 15f;
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", SanctionSeverity.Severe);

            float after = ConfrontationSystem.SettlementWillingnessFor(
                state, confrontation, state.playerCountryId);

            Assert.Greater(after, before + 5f,
                "An economic campaign must be able to coerce a government (GDD §20). "
                + "Willingness previously read only war exhaustion, momentum, war "
                + "support and the government pillar — none of which sanctions touch.");
        }

        [Test]
        public void PoliticalPressure_MovesAnOpponentTowardTerms()
        {
            var confrontation = Open(PrimaryStrategy.IntelligencePolitical);
            float before = ConfrontationSystem.SettlementWillingnessFor(
                state, confrontation, state.playerCountryId);

            var opponent = state.FindCountry("CHN");
            opponent.stability = 20f;
            opponent.governmentApproval = 15f;
            opponent.government.conspiracyLevel = 55f;

            float after = ConfrontationSystem.SettlementWillingnessFor(
                state, confrontation, state.playerCountryId);

            Assert.Greater(after, before + 5f,
                "A government fracturing at home should be readier to settle abroad.");
        }

        [Test]
        public void DiplomaticIsolation_MovesAnOpponentTowardTerms()
        {
            var confrontation = Open(PrimaryStrategy.Diplomatic);
            float before = ConfrontationSystem.SettlementWillingnessFor(
                state, confrontation, state.playerCountryId);

            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves("CHN")) continue;
                if (relationship.PartnerOf("CHN") == state.playerCountryId) continue;
                relationship.relations = 2f;
            }

            float after = ConfrontationSystem.SettlementWillingnessFor(
                state, confrontation, state.playerCountryId);

            Assert.Greater(after, before + 3f,
                "A state with no friends left should be readier to come to terms.");
        }

        [Test]
        public void CommittingToAStrategy_ConcentratesItsEffect()
        {
            // Identical world pressure, different declared strategy.
            float PressureUnder(PrimaryStrategy strategy)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 9090);
                var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "CHN",
                    ConfrontationObjective.PolicyReversal, null, strategy);

                var opponent = world.FindCountry("CHN");
                opponent.economy.growthRate = -6f;
                opponent.economy.confidence = 10f;
                EconomySystem.ImposeSanctionsBy(world, world.playerCountryId, "CHN", SanctionSeverity.Severe);

                return ConfrontationSystem.StrategicPressure(world, confrontation, world.playerCountryId);
            }

            Assert.Greater(PressureUnder(PrimaryStrategy.Economic), PressureUnder(PrimaryStrategy.Military),
                "Choosing a Primary Strategy must mean concentrating effort in it, "
                + "or the choice is a label rather than a decision.");
        }

        [Test]
        public void APivotIsAlwaysPossible_AndNeverFree()
        {
            var confrontation = Open(PrimaryStrategy.Military);
            state.commandPoints.current = 10;
            float momentumBefore = confrontation.momentum;

            Assert.IsTrue(ConfrontationSystem.Pivot(state, turns, confrontation, PrimaryStrategy.Economic));
            Assert.AreEqual(PrimaryStrategy.Economic, confrontation.primaryStrategy);
            Assert.AreEqual(10 - ConfrontationSystem.PivotCost, state.commandPoints.current);
            Assert.Less(confrontation.momentum, momentumBefore,
                "Effort spent on the old approach does not carry over (GDD §18.2).");

            Assert.IsFalse(ConfrontationSystem.Pivot(state, turns, confrontation, PrimaryStrategy.Economic),
                "Pivoting to the strategy we already hold is not a pivot.");
        }

        // ---------- escalation describes, it does not gate ----------

        [Test]
        public void AnOperationBelowLimitedConflict_IsPricedNotForbidden()
        {
            var confrontation = Open(PrimaryStrategy.Military);
            Assume.That(confrontation.escalation, Is.LessThan(EscalationState.LimitedConflict));

            state.commandPoints.current = 20;
            var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                "CONTESTED_LANE", OperationType.Assault, new OperationDirective());

            Assert.IsNotNull(record,
                "Escalation states describe the situation; they must not hard-gate "
                + "orders (GDD §18.1).");
            Assert.GreaterOrEqual(confrontation.escalation, EscalationState.LimitedConflict,
                "An overt operation IS limited conflict — the act carries the "
                + "confrontation with it.");
        }

        [Test]
        public void WithdrawingFromCapturedGround_GivesItBack()
        {
            var confrontation = Open(PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            var location = state.FindLocation("CONTESTED_LANE");
            string original = location.originalOwnerId;
            location.ownerId = state.playerCountryId;
            Assume.That(location.IsOccupied, Is.True);

            state.commandPoints.current = 20;
            var record = ConfrontationSystem.LaunchOperation(state, turns, confrontation,
                location.id, OperationType.Withdraw, new OperationDirective());

            Assert.IsNotNull(record, "Withdraw is the one operation conducted on ground we hold.");
            Assert.AreEqual(original, location.ownerId,
                "Abandoning a captured position must actually relinquish it.");
        }

        // ---------- constitutional authority ----------

        [Test]
        public void AParliamentaryOperator_CannotSimplyCommandTheEconomy()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.ParliamentaryRepublic;
            government.emergencyPowers = false;

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.AdvisoryOnly,
                AuthoritySystem.AuthorityOver(state, Pillar.Economy));

            var official = state.cabinet.Find(o => o.office == Pillar.Economy);
            Assert.IsFalse(CabinetSystem.SetMode(state, official, ControlMode.DirectControl),
                "GDD §3: where authority does not permit, the operator influences "
                + "and recommends — they do not command.");
            Assert.AreNotEqual(ControlMode.DirectControl, official.mode);

            // But directing and advising remain available.
            state.influence = 5;
            Assert.IsTrue(CabinetSystem.SetMode(state, official, ControlMode.Directed));
        }

        [Test]
        public void AuthorityThatMustBeObtained_CostsPoliticalCapital()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.PresidentialRepublic;
            government.emergencyPowers = false;
            government.legislativeSupport = 70f;
            state.politicalCapital = 12f;

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.RequiresApproval,
                AuthoritySystem.AuthorityOver(state, Pillar.Economy));

            var official = state.cabinet.Find(o => o.office == Pillar.Economy);
            Assert.IsTrue(CabinetSystem.SetMode(state, official, ControlMode.DirectControl));
            Assert.AreEqual(12f - AuthoritySystem.ApprovalCost, state.politicalCapital, 0.01f);
        }

        [Test]
        public void AnInstitutionThatDoesNotBackUs_CanRefuse()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.PresidentialRepublic;
            government.emergencyPowers = false;
            government.legislativeSupport = 10f;
            state.politicalCapital = 12f;

            var official = state.cabinet.Find(o => o.office == Pillar.Economy);
            Assert.IsFalse(CabinetSystem.SetMode(state, official, ControlMode.DirectControl),
                "A legislature that does not support us can withhold consent.");
            Assert.Less(state.politicalCapital, 12f,
                "The capital is spent in the asking, whatever the answer.");
        }

        [Test]
        public void ExecutiveAuthority_IsDirectAndFree()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.PresidentialRepublic;
            government.emergencyPowers = false;
            state.politicalCapital = 12f;

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.Direct,
                AuthoritySystem.AuthorityOver(state, Pillar.Military));

            var official = state.cabinet.Find(o => o.office == Pillar.Military);
            Assert.IsTrue(CabinetSystem.SetMode(state, official, ControlMode.DirectControl));
            Assert.AreEqual(12f, state.politicalCapital, 0.01f,
                "Commanding the forces is the executive's own authority.");
        }

        // ---------- authority actually binds the verbs ----------

        [Test]
        public void AuthorityIsGrantedOncePerAdministration_NotBoughtPerAction()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.PresidentialRepublic; // Economy = RequiresApproval
            government.emergencyPowers = false;
            government.legislativeSupport = 70f;
            state.politicalCapital = 20f;

            Assert.IsFalse(AuthoritySystem.HoldsAuthority(state, Pillar.Economy));

            Assert.IsTrue(AuthoritySystem.EnsureAuthority(state, Pillar.Economy));
            float afterFirst = state.politicalCapital;
            Assert.AreEqual(20f - AuthoritySystem.ApprovalCost, afterFirst, 0.01f);

            // Second and third actions in the same pillar are free: a legislature
            // that has ceded the brief does not re-litigate every tariff.
            Assert.IsTrue(AuthoritySystem.EnsureAuthority(state, Pillar.Economy));
            Assert.IsTrue(AuthoritySystem.EnsureAuthority(state, Pillar.Economy));
            Assert.AreEqual(afterFirst, state.politicalCapital, 0.01f,
                "Charging per action would make a whole playstyle unaffordable "
                + "rather than constitutionally awkward.");
        }

        [Test]
        public void AGrantLapsesWithTheAdministrationThatMadeIt()
        {
            state.authorizedPillarMask = 0;
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.PresidentialRepublic;
            government.emergencyPowers = false;
            government.legislativeSupport = 70f;
            state.politicalCapital = 20f;

            AuthoritySystem.EnsureAuthority(state, Pillar.Economy);
            Assert.IsTrue(AuthoritySystem.HoldsAuthority(state, Pillar.Economy));

            AuthoritySystem.ClearGrantedAuthority(state);
            Assert.IsFalse(AuthoritySystem.HoldsAuthority(state, Pillar.Economy),
                "The grant was personal to the government that made it.");
        }

        [Test]
        public void AnAdvisoryPillar_CannotBeCommandedNoMatterHowMuchCapitalWeHave()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.ParliamentaryRepublic; // Economy = AdvisoryOnly
            government.emergencyPowers = false;
            state.politicalCapital = 20f;

            Assert.IsFalse(AuthoritySystem.EnsureAuthority(state, Pillar.Economy));
            Assert.AreEqual(20f, state.politicalCapital, 0.01f,
                "There is nothing to buy — it is outside the writ, not expensive.");
        }

        /// <summary>
        /// The deadlock this design could easily have created: emergency powers
        /// suspend the authority distribution, but declaring them is a Government
        /// action — and a Parliamentary operator is advisory over Government. If
        /// that verb were gated, the one instrument designed to break the impasse
        /// would sit behind the impasse.
        /// </summary>
        [Test]
        public void EmergencyPowers_AreReachableEvenWhereGovernmentIsAdvisoryOnly()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.ParliamentaryRepublic;
            government.emergencyPowers = false;
            state.politicalCapital = 20f;

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.AdvisoryOnly,
                AuthoritySystem.AuthorityOver(state, Pillar.Government));

            Assert.IsTrue(GovernmentSystem.DeclareEmergencyPowers(state),
                "Claiming authority beyond the ordinary distribution cannot require "
                + "the authority it grants.");

            // And now everything is commandable.
            Assert.IsTrue(AuthoritySystem.HoldsAuthority(state, Pillar.Economy));
        }

        [Test]
        public void EmergencyPowers_SuspendTheOrdinaryDistributionOfAuthority()
        {
            var government = state.PlayerCountry.government;
            government.type = GovernmentType.ParliamentaryRepublic;

            Assert.AreEqual(AuthoritySystem.AuthorityLevel.AdvisoryOnly,
                AuthoritySystem.AuthorityOver(state, Pillar.Economy));

            government.emergencyPowers = true;
            Assert.AreEqual(AuthoritySystem.AuthorityLevel.Direct,
                AuthoritySystem.AuthorityOver(state, Pillar.Economy),
                "This is what makes emergency powers worth their political price.");
        }

        [Test]
        public void DifferentConstitutions_PutAuthorityInDifferentPlaces()
        {
            var government = state.PlayerCountry.government;
            government.emergencyPowers = false;

            var seen = new System.Collections.Generic.List<string>();
            foreach (GovernmentType type in System.Enum.GetValues(typeof(GovernmentType)))
            {
                government.type = type;
                var shape = "";
                foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                    shape += (int)AuthoritySystem.AuthorityOver(state, pillar);
                seen.Add(shape);
            }

            // Not every constitution need be unique, but they must not all be the
            // same — otherwise government type is decoration.
            CollectionAssert.AllItemsAreUnique(seen,
                "Each constitution should distribute authority differently (GDD §13).");
        }
    }
}

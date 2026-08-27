using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// One test per bug found in the audit sweep. Each names the wrong behaviour
    /// it forbids, so a regression reads as a sentence rather than a number.
    /// </summary>
    public class BugRegressionTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 777);
            turns = new TurnManager(state);
            state.commandPoints.current = 40;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- resources and stats ----------

        [Test]
        public void Manpower_NeverGoesNegativeAndRecoversFromAWar()
        {
            var player = state.PlayerCountry;
            player.resources.manpowerBaseline = player.resources.manpower;
            float baseline = player.resources.manpower;

            player.resources.manpower = 5f;
            MilitarySystem.ResolveOperation(state,
                ConfrontationSystem.BeginBy(state, player.id, "CHN",
                    ConfrontationObjective.Deterrence, "CONTESTED_LANE", PrimaryStrategy.Military),
                player.id, state.FindLocation("CONTESTED_LANE"),
                OperationType.Assault, new OperationDirective(), new Random(7));

            Assert.GreaterOrEqual(player.resources.manpower, 0f,
                "A country cannot spend more people than it has.");

            player.resources.manpower = baseline * 0.5f;
            var economy = new TurnManager(state);
            // NARROW PIPELINE: only the economy tick is wired because the assertion is
            // EconomySystem's own manpower restoring force against a directly-set 50%
            // baseline; a live AI or confrontation could spend manpower again and mask it.
            economy.ResolveMonth += EconomySystem.MonthlyUpdate;
            for (int i = 0; i < 60; i++) { state.commandPoints.current = 6; economy.EndMonth(); }

            Assert.Greater(player.resources.manpower, baseline * 0.5f,
                "A population recovers; losses are not permanent for the rest of the save.");
        }

        [Test]
        public void WarExhaustion_FadesInPeacetime()
        {
            var player = state.PlayerCountry;
            player.warExhaustion = 70f;

            // NARROW PIPELINE: AISystem and ConfrontationSystem are deliberately omitted —
            // the assertion is that GovernmentSystem's own decay reaches a level *in
            // peacetime*, and a live world would start a war and refill exhaustion.
            var manager = new TurnManager(state);
            manager.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            for (int i = 0; i < 60; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            Assert.Less(player.warExhaustion, 30f,
                "Exhaustion had only one decrement in the whole simulation, so it "
                + "ratcheted up forever and drove a permanent coup cycle.");
        }

        [Test]
        public void WarSupport_RecoversInPeacetime()
        {
            var player = state.PlayerCountry;
            player.warSupport = 5f;

            // NARROW PIPELINE: AISystem and ConfrontationSystem are deliberately omitted —
            // the assertion is GovernmentSystem's own recovery toward a peacetime level,
            // and an actual war would suppress war support and hide the restoring force.
            var manager = new TurnManager(state);
            manager.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            for (int i = 0; i < 60; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            Assert.Greater(player.warSupport, 25f,
                "Below 20 an AI government sues for terms immediately and can never "
                + "escalate; without recovery the world stops being dangerous.");
        }

        [Test]
        public void Approval_DoesNotSaturateInAHealthyCountry()
        {
            var player = state.PlayerCountry;
            player.economy.growthRate = 5f;
            player.economy.inflation = 2f;

            // NARROW PIPELINE: EconomySystem is deliberately omitted so the pinned
            // growth/inflation figures above stay pinned; the assertion is only that
            // GovernmentSystem's approval term approaches a level instead of integrating.
            var manager = new TurnManager(state);
            manager.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            for (int i = 0; i < 240; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            Assert.Less(player.governmentApproval, 99f,
                "Approval integrated a rate instead of approaching a level, so it "
                + "pinned at 100 and made elections a formality.");
        }

        [Test]
        public void TradeHealth_IsAScoreNotASumOfLinks()
        {
            foreach (var country in state.countries)
            {
                float health = EconomySystem.TradeHealth(state, country.id);
                Assert.LessOrEqual(health, 100f,
                    $"{country.id} trade health {health:F0} — a hub with many links "
                    + "must not out-scale the 0..100 range its consumer centres on 50.");
                Assert.GreaterOrEqual(health, 0f);
            }
        }

        // ---------- player actions ----------

        [Test]
        public void TogglingACabinetModeForFree_EarnsNoInitiative()
        {
            var official = state.cabinet[0];
            state.influence = 0; // cannot afford Directed
            int before = state.initiativesThisYear;

            for (int i = 0; i < 8; i++)
            {
                CabinetSystem.SetMode(state, official, ControlMode.DirectControl);
                CabinetSystem.SetMode(state, official, ControlMode.Autonomous);
            }

            Assert.AreEqual(before, state.initiativesThisYear,
                "Free mode toggles banked the whole initiative component of the "
                + "annual evaluation without spending anything.");
        }

        [Test]
        public void ConfrontingAStateAlreadyAtWar_CostsNothing()
        {
            // Two other powers are already committed to each other.
            ConfrontationSystem.BeginBy(state, "CHN", "RUS",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            state.commandPoints.current = 5;
            var opened = ConfrontationSystem.Begin(state, turns, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            Assert.IsNull(opened);
            Assert.AreEqual(5, state.commandPoints.current,
                "The defender check ran after the spend, so the player paid 2 CP "
                + "to be told no, with no message.");
        }

        [Test]
        public void PreparingAnInstrumentWeCannotFund_CostsNothingAndCreditsNothing()
        {
            var player = state.PlayerCountry;
            player.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = EndgameSystem.RequiredCapability(EndgameType.StrategicIsolation),
                source = CapabilitySource.Developed,
                maturity = 100f
            });
            player.pillars.diplomacy = 90f;
            player.resources.treasury = 10f; // cannot fund the 120 cost

            state.commandPoints.current = 5;
            int initiativeBefore = state.initiativesThisYear;

            Assert.IsFalse(EndgameSystem.Prepare(state, turns, EndgameType.StrategicIsolation));
            Assert.AreEqual(5, state.commandPoints.current, "CP was spent on nothing.");
            Assert.AreEqual(initiativeBefore, state.initiativesThisYear,
                "Initiative and XP were credited for work that did not happen.");
            Assert.AreEqual(0f, player.endgames.ProgressFor(EndgameType.StrategicIsolation));
        }

        [Test]
        public void LiftingSanctions_CountsAsStatecraft()
        {
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", SanctionSeverity.Pressure);
            state.commandPoints.current = 10;
            int before = state.initiativesThisYear;

            Assert.IsTrue(EconomySystem.LiftSanctions(state, turns, "CHN"));
            Assert.Greater(state.initiativesThisYear, before,
                "De-escalation is statecraft; crediting only escalation graded the "
                + "Economy pillar below a player who never lifts anything.");
        }

        [Test]
        public void LogisticsInvestmentWithNoMoney_IsRefused()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 5f;
            state.commandPoints.current = 5;

            Assert.IsFalse(MilitarySystem.InvestInLogistics(state, turns));
            Assert.AreEqual(5, state.commandPoints.current);
            Assert.GreaterOrEqual(player.resources.treasury, 0f,
                "One click could strand the player, which then cancels procurement "
                + "and suspends research.");
        }

        // ---------- fog of war and skills ----------

        [Test]
        public void ACoalitionLedByAnotherState_DoesNotScoreOnRelationsWithUs()
        {
            // India leads; Russia is the candidate; China is the target.
            var indRus = state.FindRelationship("IND", "RUS");
            indRus.relations = 95f; indRus.trust = 95f; indRus.strategicAlignment = 95f;
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.relations = 5f;

            // Our own relations with Russia are terrible and must not matter here.
            var usaRus = state.FindRelationship(state.playerCountryId, "RUS");
            usaRus.relations = 0f; usaRus.trust = 0f; usaRus.strategicAlignment = 0f;

            float ledByIndia = DiplomacySystem.CoalitionWillingness(state, "IND", "RUS", "CHN");
            float ledByUs = DiplomacySystem.CoalitionWillingness(state, state.playerCountryId, "RUS", "CHN");

            Assert.Greater(ledByIndia, ledByUs,
                "Coalition cohesion was scored against the player regardless of who "
                + "actually led it.");
        }

        [Test]
        public void APlayerJoiningSomeoneElsesCoalition_IsNotEvictedByAMissingSelfRelationship()
        {
            float willingness = DiplomacySystem.CoalitionWillingness(
                state, "IND", state.playerCountryId, "CHN");

            Assert.Greater(willingness, 30f,
                "Scoring the player against a relationship with themselves returned "
                + "0 and dropped them from any alliance they honoured.");
        }

        [Test]
        public void CoalitionPersuasion_DoesNotHoldTogetherForeignAlliances()
        {
            var indRus = state.FindRelationship("IND", "RUS");
            indRus.relations = 60f;

            float before = DiplomacySystem.CoalitionWillingness(state, "IND", "RUS", "CHN");
            foreach (var node in SkillCatalog.Nodes) state.unlockedSkills.Add(node.id);
            float after = DiplomacySystem.CoalitionWillingness(state, "IND", "RUS", "CHN");

            Assert.AreEqual(before, after, 0.001f,
                "Operator training must not make the alliances fielded against us "
                + "more cohesive (skills grant operator capability only).");
        }

        [Test]
        public void GarrisonEstimates_MoveWhenWeAreDeceived()
        {
            var location = state.FindLocation("CONTESTED_LANE");
            var owner = state.FindCountry(location.ownerId);
            Assume.That(owner.id, Is.Not.EqualTo(state.playerCountryId));

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = owner.id,
                focus = IntelDomain.Military,
                penetration = 60f
            });
            // NARROW PIPELINE: collection alone, with no AI or decay, so the honest
            // estimate is measured against a world that is holding still; the
            // deception values below are set by hand, so nothing else needs to run.
            var manager = new TurnManager(state);
            manager.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            state.commandPoints.current = 6;
            manager.EndMonth();

            Assert.IsTrue(IntelligenceSystem.TryEstimateGarrison(state, state.playerCountryId,
                location, out float honestLow, out float honestHigh, out _));

            // Now they run a deception that inflates our read of their military.
            owner.counterIntel.deceptionStrength = 90f;
            owner.counterIntel.deceptionDomain = IntelDomain.Military;
            owner.counterIntel.deceptionBias = 1f;
            owner.counterIntel.counterIntelligence = 95f;
            for (int i = 0; i < 6; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            IntelligenceSystem.TryEstimateGarrison(state, state.playerCountryId,
                location, out float bentLow, out float bentHigh, out _);

            float honestCentre = (honestLow + honestHigh) / 2f;
            float bentCentre = (bentLow + bentHigh) / 2f;
            Assert.Greater(Math.Abs(bentCentre - honestCentre), 0.5f,
                "The garrison band was centred on the true value, so deception "
                + "changed the INTELLIGENCE screen and nothing the player acts on.");
        }

        [Test]
        public void TheSettlementTable_IsNotAnOracleWithoutCollection()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);

            var proposal = new PeaceProposal();
            proposal.terms.Add(PeaceTerm.TerritorialCession);

            Assert.AreEqual(SettlementOutlook.Unknown,
                PeaceSystem.Assess(state, confrontation, state.playerCountryId, proposal),
                "With no political collection on them we must not be told whether "
                + "they would sign (GDD §26).");
        }

        [Test]
        public void TheFogRunsBothWays_AIStatesCanDeceiveUsToo()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 5150);
            var manager = new TurnManager(world);
            // MUST use the real pipeline. This runs a decade and asserts that some
            // AI government *eventually* chooses to deceive us — an emergent
            // decision, not arithmetic. An AI reasoning inside a partial world is
            // not the AI that ships.
            SimulationPipeline.Wire(manager, world);

            // We are watching them closely, and they are hostile to us.
            foreach (var country in world.countries)
            {
                if (country.isPlayer) continue;
                country.pillars.intelligence = 80f;

                var relationship = world.FindRelationship(world.playerCountryId, country.id);
                relationship.relations = 5f;
                relationship.SetThreatPerceivedBy(country.id, 90f);

                world.networks.Add(new IntelNetwork
                {
                    ownerId = world.playerCountryId,
                    targetId = country.id,
                    focus = IntelDomain.Military,
                    penetration = 55f
                });
            }

            bool anyoneLied = false;
            for (int i = 0; i < 120 && !anyoneLied; i++)
            {
                world.commandPoints.current = 6;
                manager.EndMonth();
                foreach (var country in world.countries)
                    if (!country.isPlayer && country.counterIntel.deceptionStrength > 0f)
                        anyoneLied = true;
            }

            Assert.IsTrue(anyoneLied,
                "Deception was a player-only verb, so the player could be deceived "
                + "by nobody while deceiving everyone (GDD §14).");
        }

        [Test]
        public void AFullReset_LeavesNothingLoadable()
        {
            // A manual save in another slot must not survive a reset that is
            // supposed to erase the nation and all progression (GDD §5.1).
            SaveSystem.Save(state, 0);
            SaveSystem.Save(state, 1);
            SaveSystem.Save(state, 3);
            Assume.That(SaveSystem.SaveExists(1), Is.True, "Precondition: a manual save exists.");

            SaveSystem.DeleteAll();

            for (int slot = 0; slot <= SaveSystem.MaxSlot; slot++)
                Assert.IsFalse(SaveSystem.SaveExists(slot),
                    $"Slot {slot} survived a full reset — the erased world can be restored.");
        }

        [Test]
        public void ADeceptionProgramme_BendsTheDomainItWasAimedAt()
        {
            var player = state.PlayerCountry;
            foreach (var node in SkillCatalog.Nodes) state.unlockedSkills.Add(node.id);
            state.commandPoints.current = 40;

            Assert.IsTrue(IntelligenceSystem.RunCovertOperation(state, turns, "CHN",
                CovertOperation.Deception, 1f, IntelDomain.Economic));

            Assert.AreEqual(IntelDomain.Economic, player.counterIntel.deceptionDomain,
                "The domain was never assigned, so every deception the player ran "
                + "distorted the Military estimate — a programme aimed at hiding "
                + "economic weakness did nothing at all.");
        }

        [Test]
        public void ARecession_ReachesTheHistoricalRecord()
        {
            var player = state.PlayerCountry;
            // NARROW PIPELINE: only the economy tick — everything else is omitted so the
            // six-month window contains no competing chronicle traffic and the forced
            // contraction below is not repaired by a cabinet or an AI mid-test.
            var manager = new TurnManager(state);
            manager.ResolveMonth += EconomySystem.MonthlyUpdate;

            // Drive the economy into sustained contraction.
            player.pillars.economy = 5f;
            player.economy.growthRate = -6f;
            player.economy.confidence = 5f;

            int before = state.chronicle.Count;
            for (int i = 0; i < 6; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            Assert.IsTrue(player.economy.InRecession, "Precondition: the economy is contracting.");

            bool recorded = false;
            for (int i = before; i < state.chronicle.Count; i++)
                if (state.chronicle[i].category == ChronicleCategory.Economic
                    && state.chronicle[i].text.Contains("recession")) recorded = true;

            Assert.IsTrue(recorded,
                "Recessions were the one category on GDD §31.3's list of eight that "
                + "never reached the chronicle, so a thirty-year history showed every "
                + "war and no depression.");
        }

        [Test]
        public void ASingleBadMonth_IsNotARecession()
        {
            var eco = state.PlayerCountry.economy;
            eco.contractionMonths = 1;
            Assert.IsFalse(eco.InRecession, "One negative month is noise, not a downturn.");

            eco.contractionMonths = 2;
            Assert.IsTrue(eco.InRecession);

            eco.contractionMonths = 12;
            Assert.IsTrue(eco.InDepression, "A full year of contraction is no longer a downturn.");
        }

        // ---------- determinism ----------

        [Test]
        public void TwoCovertOperationsInOneMonth_AreTwoIndependentGambles()
        {
            var player = state.PlayerCountry;
            player.pillars.intelligence = 80f;

            foreach (string target in new[] { "CHN", "RUS" })
                state.networks.Add(new IntelNetwork
                {
                    ownerId = player.id,
                    targetId = target,
                    focus = IntelDomain.Military,
                    penetration = 55f
                });

            // Same operation, same month, two targets: the seeds must differ.
            state.commandPoints.current = 40;
            int sequenceBefore = state.actionSequence;
            IntelligenceSystem.RunCovertOperation(state, turns, "CHN", CovertOperation.Sabotage);
            int afterFirst = state.actionSequence;
            IntelligenceSystem.RunCovertOperation(state, turns, "RUS", CovertOperation.Sabotage);

            Assert.Greater(afterFirst, sequenceBefore,
                "Each drawing action must advance the sequence, or repeated actions "
                + "in one month replay the same rolls and become farmable.");
            Assert.Greater(state.actionSequence, afterFirst);
        }

        [Test]
        public void TheActionSequence_SurvivesASave()
        {
            state.actionSequence = 41;
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.AreEqual(41, loaded.actionSequence,
                "If the sequence resets on load, a save/reload replays the same draws.");
        }

        [Test]
        public void TheStableHash_NeverChanges()
        {
            // Pinned so a runtime or platform change fails here rather than
            // silently forking every existing save's random streams.
            Assert.AreEqual(0, Hash.Of(""));
            Assert.AreEqual(0, Hash.Of(null));
            Assert.AreEqual(unchecked((int)0x966BD5C8), Hash.Of("USA"));
            Assert.AreEqual(unchecked((int)0x315F9680), Hash.Of("CHN"));
            Assert.AreEqual(unchecked((int)0xB2A7D289), Hash.Of("RUS"));
            Assert.AreNotEqual(Hash.Of("USA"), Hash.Of("RUS"));
        }

        // ---------- the UI cannot offer what the rules refuse ----------

        [Test]
        public void EverySkillGatedVerb_TellsUsWhyItIsLocked()
        {
            // No skills unlocked: each strategic verb must report a reason rather
            // than presenting a control that silently does nothing when clicked.
            Assert.IsFalse(MilitarySystem.CanSetPosture(state, MilitaryPosture.Forward, out string posture));
            StringAssert.Contains("Forward Basing", posture);

            Assert.IsFalse(EconomySystem.CanImposeSanctions(state, "CHN",
                SanctionSeverity.Existential, out string sanctions));
            StringAssert.Contains("Existential Measures", sanctions);

            Assert.IsFalse(IntelligenceSystem.CanRunCovertOperation(state, "CHN",
                CovertOperation.Deception, out string deception));
            StringAssert.Contains("Deep Cover", deception);
        }

        [Test]
        public void TheGuardsAgree_WithWhatTheActionsActuallyEnforce()
        {
            // A predicate that says yes must be followed by an action that works,
            // and vice versa — otherwise the UI either hides a legal move or
            // offers an illegal one.
            state.commandPoints.current = 40;
            state.PlayerCountry.resources.treasury = 5000f;

            Assert.IsFalse(MilitarySystem.CanSetPosture(state, MilitaryPosture.Forward, out _));
            Assert.IsFalse(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Forward));

            Assert.IsTrue(MilitarySystem.CanSetPosture(state, MilitaryPosture.Alert, out _));
            Assert.IsTrue(MilitarySystem.SetPosture(state, turns, MilitaryPosture.Alert));

            Assert.IsTrue(EconomySystem.CanImposeSanctions(state, "CHN", SanctionSeverity.Pressure, out _));
            Assert.IsTrue(EconomySystem.ImposeSanctions(state, turns, "CHN", SanctionSeverity.Pressure));

            // Now it is in force, so the predicate must stop offering it.
            Assert.IsFalse(EconomySystem.CanImposeSanctions(state, "CHN", SanctionSeverity.Pressure, out _));
        }

        // ---------- posture ----------

        [Test]
        public void SettlingAWar_DoesNotDiscardAForwardPosture()
        {
            var player = state.PlayerCountry;
            player.military.posture = MilitaryPosture.Forward;

            var confrontation = ConfrontationSystem.BeginBy(state, player.id, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.CloseWithSettlement(state, confrontation, player.id, "settled");

            Assert.AreEqual(MilitaryPosture.Forward, player.military.posture,
                "Forward is a skill-gated verb costing CP and real upkeep; ending a "
                + "war silently reset it to Peacetime.");
        }

        [Test]
        public void SettlingAWar_StandsDownAWartimeAlert()
        {
            var player = state.PlayerCountry;
            player.military.posture = MilitaryPosture.Alert;

            var confrontation = ConfrontationSystem.BeginBy(state, player.id, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.CloseWithSettlement(state, confrontation, player.id, "settled");

            Assert.AreEqual(MilitaryPosture.Peacetime, player.military.posture);
        }

        // ---------- the AI can maintain itself ----------

        [Test]
        public void AIStates_CanRebuildAForceTheyHaveLost()
        {
            var china = state.FindCountry("CHN");
            china.government.leader.priority = NationalPriority.Security;
            china.resources.treasury = 20000f;
            china.military.ground.strength = 20f;
            float before = china.military.ground.strength;

            // NARROW PIPELINE: the AI's decision plus the upkeep tick that advances the
            // programme it authorizes is the entire mechanism under test. Confrontations
            // are omitted on purpose — a war would attrit the force being rebuilt.
            var manager = new TurnManager(state);
            manager.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            manager.ResolveMonth += AISystem.MonthlyThink;
            for (int i = 0; i < 60; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            Assert.Greater(china.military.ground.strength, before,
                "Procurement was player-only, so a beaten AI army could never be "
                + "rebuilt and the player could permanently disarm every rival.");
        }

        [Test]
        public void AIStates_MaintainTheirLogistics()
        {
            var china = state.FindCountry("CHN");
            china.government.leader.priority = NationalPriority.Security;
            china.resources.treasury = 20000f;
            china.military.logistics = 20f;

            // NARROW PIPELINE: as above — the AI choosing to invest and the upkeep tick
            // that applies it are the whole mechanism. Omitting the economy keeps the
            // hand-set 20000 treasury available so the test measures reachability, not funding.
            var manager = new TurnManager(state);
            manager.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            manager.ResolveMonth += AISystem.MonthlyThink;
            for (int i = 0; i < 60; i++) { state.commandPoints.current = 6; manager.EndMonth(); }

            Assert.Greater(china.military.logistics, 20f,
                "Logistics decayed monthly for everyone with a player-only recovery "
                + "path, so every AI's sustainment fell to zero over a long save.");
        }

        [Test]
        public void ForeignSanctions_DoNotLockTwoStatesInPermanentHostility()
        {
            EconomySystem.ImposeSanctionsBy(state, "CHN", "IND", SanctionSeverity.Pressure);

            // They reconcile.
            var relationship = state.FindRelationship("CHN", "IND");
            relationship.relations = 70f;
            relationship.SetThreatPerceivedBy("CHN", 10f);

            // NARROW PIPELINE: only the sanction ageing tick, because the reconciled
            // relations set just above must stay reconciled for the review to lift them;
            // a live DiplomacySystem or AI would move them back under the threshold.
            var manager = new TurnManager(state);
            manager.ResolveMonth += EconomySystem.AgeSanctions;
            for (int i = 0; i < EconomySystem.SanctionReviewMonths + 4; i++)
            {
                state.commandPoints.current = 6;
                manager.EndMonth();
            }

            Assert.IsNull(state.FindSanction("CHN", "IND"),
                "Nothing ever lifted an AI's sanctions, and a sanctioned pair skips "
                + "the relations-recovery branch entirely — so they stayed hostile "
                + "for the rest of the save.");
        }

        [Test]
        public void AReputation_CanBeOutlived()
        {
            var ai = state.FindAI("CHN");

            // A burst of early wars.
            for (int i = 0; i < 6; i++)
                state.confrontations.Add(new Confrontation
                {
                    id = $"OLD_{i}",
                    initiatorId = state.playerCountryId,
                    defenderId = "RUS",
                    startDate = state.date,
                    resolved = true
                });

            AISystem.MonthlyThink(state);
            float immediately = ai.playerAssessment.perceivedAggression;
            Assert.Greater(immediately, 40f, "Precondition: we look aggressive now.");

            // Twenty quiet years later.
            for (int i = 0; i < 240; i++) state.date = state.date.NextMonth();
            AISystem.MonthlyThink(state);

            Assert.Less(ai.playerAssessment.perceivedAggression, immediately,
                "Every war ever fought counted at full weight forever, so a player "
                + "who changed course was read as maximally aggressive regardless "
                + "(GDD §24.2 is explicitly about a pattern that can be broken).");
        }

        // ---------- settlements (2026-08 playtest) ----------

        Confrontation OpenLaneWar()
        {
            return ConfrontationSystem.BeginBy(state, "USA", "CHN",
                ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
        }

        static void ExhaustPlayerSide(GameState state, Confrontation confrontation)
        {
            var usa = state.PlayerCountry;
            usa.warSupport = 0f;
            usa.pillars.government = 10f;
            confrontation.initiatorWarExhaustion = 90f;
            confrontation.momentum = -60f;
        }

        [Test]
        public void Settlement_ProposedByTheDefender_WithdrawsTheClaimAndCedesNothing()
        {
            var confrontation = OpenLaneWar();
            var lane = state.FindLocation("CONTESTED_LANE");

            ExhaustPlayerSide(state, confrontation);
            Assert.IsFalse(ConfrontationSystem.ProposeSettlementBy(state, confrontation, "CHN"),
                "A foreign offer must not close the player's war by itself.");
            Assert.IsFalse(confrontation.resolved);
            Assert.IsTrue(state.HasOpenCrisis, "The offer should reach the operator as a decision.");
            var offer = state.activeCrises[state.activeCrises.Count - 1];
            Assert.AreEqual(ConfrontationSystem.TermsOfferedCrisisId, offer.defId);

            CrisisSystem.Resolve(state, offer, 0); // accept

            Assert.IsTrue(confrontation.resolved);
            Assert.AreEqual("CHN", lane.ownerId,
                "The defender proposed. The defender's terms are the status quo — it "
                + "used to 'cede' ground it already held, recorded as the *initiator* ceding it.");
            Assert.AreEqual("CHN", lane.originalOwnerId);
            StringAssert.DoesNotContain("United States cedes", confrontation.outcomeSummary);
        }

        [Test]
        public void Settlement_RefusedTerms_KeepTheWarOpenAndWaitBeforeAskingAgain()
        {
            var confrontation = OpenLaneWar();
            ExhaustPlayerSide(state, confrontation);
            ConfrontationSystem.ProposeSettlementBy(state, confrontation, "CHN");
            var offer = state.activeCrises[state.activeCrises.Count - 1];

            CrisisSystem.Resolve(state, offer, 1); // refuse

            Assert.IsFalse(confrontation.resolved);
            Assert.IsFalse(state.HasOpenCrisis);
            Assert.IsFalse(ConfrontationSystem.ProposeSettlementBy(state, confrontation, "CHN"));
            Assert.IsFalse(state.HasOpenCrisis,
                "They asked again the same month. A refusal has to buy the operator some quiet.");
        }

        [Test]
        public void Settlement_CannotCedeGroundHeldByAThirdParty()
        {
            var confrontation = OpenLaneWar();
            var lane = state.FindLocation("CONTESTED_LANE");
            lane.ownerId = "IND"; // taken by somebody else mid-war

            var china = state.FindCountry("CHN");
            china.warSupport = 0f;
            china.pillars.government = 10f;
            confrontation.defenderWarExhaustion = 90f;
            confrontation.momentum = 60f;

            Assert.IsTrue(ConfrontationSystem.ProposeSettlement(state, confrontation));
            Assert.AreEqual("IND", lane.ownerId,
                "A settlement with China transferred India's ground.");
            Assert.AreEqual("CHN", lane.originalOwnerId);
        }

        [Test]
        public void Confrontation_CannotDemandGroundTheDefenderDoesNotHold()
        {
            state.FindLocation("CONTESTED_LANE").ownerId = "IND";
            Assert.IsNull(OpenLaneWar(),
                "A war with China over a place India holds is not a war with China.");
        }

        [Test]
        public void Settlement_BindsBothSidesForAYear()
        {
            var confrontation = OpenLaneWar();
            Assert.IsTrue(ConfrontationSystem.ProposeSettlement(state, confrontation, concedeInstead: true));

            Assert.IsNull(OpenLaneWar(),
                "The same war was re-declared the month after it was settled. The harness "
                + "did this eighteen times in a decade.");
            Assert.IsNull(ConfrontationSystem.BeginBy(state, "CHN", "USA",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military),
                "The truce binds the other side too.");

            var pair = state.FindRelationship("USA", "CHN");
            pair.settlementTruceMonths = 0;
            Assert.NotNull(OpenLaneWar(), "And it expires.");
        }

        [Test]
        public void WinningAWar_RalliesTheCountryAndCountsInTheEvaluation()
        {
            var confrontation = OpenLaneWar();
            confrontation.escalation = EscalationState.LimitedConflict;
            var lane = state.FindLocation("CONTESTED_LANE");
            var usa = state.PlayerCountry;
            var china = state.FindCountry("CHN");
            china.warSupport = 0f; china.pillars.government = 10f;
            confrontation.defenderWarExhaustion = 90f; confrontation.momentum = 60f;
            lane.ownerId = "USA";

            usa.governmentApproval = 40f; usa.warExhaustion = 60f;
            float approval = usa.governmentApproval, exhaustion = usa.warExhaustion;
            float chinaApproval = china.governmentApproval;
            ProgressionSystem.CaptureYearSnapshot(state);

            Assert.IsTrue(ConfrontationSystem.ProposeSettlement(state, confrontation));
            Assert.AreEqual(WarVerdict.InitiatorVictory, confrontation.verdict);

            Assert.Greater(usa.governmentApproval, approval,
                "A won war used to change a counter and nothing else — the winner paid every "
                + "month of the war and got no rally for it.");
            Assert.Less(usa.warExhaustion, exhaustion, "Victory should let the country breathe.");
            Assert.Less(china.governmentApproval, chinaApproval, "And defeat should cost the loser.");

            var record = ProgressionSystem.EvaluateYear(state, 1984);
            Assert.Greater(record.positionScore, 50f + 8f,
                "The evaluation credited the ground and not the verdict.");
        }

        [Test]
        public void Evaluation_ADeepDeficitCostsTheEconomyGrade()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 500f;
            float solvent = ProgressionSystem.EvaluateYear(state, 1984).economyScore;

            player.resources.treasury = -5000f;
            float broke = ProgressionSystem.EvaluateYear(state, 1985).economyScore;

            Assert.Less(broke, solvent - 10f,
                "The evaluation never looked at the balance: every posting was tens of "
                + "thousands in the red and graded B, which is how a cost 70× income passed "
                + "every balance measurement this project had taken.");
            player.resources.treasury = 0f;
            Assert.AreEqual(0f, ProgressionSystem.SolvencyPenalty(player), "Solvent is free.");
        }
    }
}

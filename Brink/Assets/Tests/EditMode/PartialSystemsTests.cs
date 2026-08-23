using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Systems that were documented but only half-wired: a field written and
    /// never read, a cost applied and then undone by the same tick, a
    /// notification class with no producer, a priority level that meant nothing
    /// because everything used it, and XP that measured repeatability instead of
    /// judgement.
    ///
    /// Each test asserts the behaviour the design already claimed, so a
    /// regression reads as the feature going back to being decorative.
    /// </summary>
    public class PartialSystemsTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4242);
            turns = new TurnManager(state);
            state.commandPoints.current = 60;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- escalation pressure (GDD §18.1) ----------

        [Test]
        public void Sanctions_WindUpAConfrontationTheyAreAimedAt()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Economic);
            Assert.IsNotNull(confrontation);

            float before = confrontation.escalationPressure;
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "CHN", SanctionSeverity.Severe);

            Assert.Greater(confrontation.escalationPressure, before,
                "Coercion during a confrontation must wind it up even though nobody fired.");
        }

        [Test]
        public void Pressure_DoesNotLeakIntoUnrelatedConfrontations()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Economic);
            float before = confrontation.escalationPressure;

            // Sanctioning a third party has nothing to do with this standoff.
            EconomySystem.ImposeSanctionsBy(state, state.playerCountryId, "IND", SanctionSeverity.Severe);

            Assert.AreEqual(before, confrontation.escalationPressure, 0.001f,
                "Pressure was added to a confrontation the target is not part of.");
        }

        [Test]
        public void HighPressure_EventuallyEscalatesWithoutAnyoneChoosingTo()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            confrontation.escalation = EscalationState.Tension;
            confrontation.escalationPressure = 95f;

            ConfrontationSystem.MonthlyTick(state);

            Assert.Greater((int)confrontation.escalation, (int)EscalationState.Tension,
                "Pressure past the boil point must carry the situation up a level on its own.");
        }

        [Test]
        public void Boilover_StopsShortOfWiderWar()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            confrontation.escalation = EscalationState.LimitedConflict;
            confrontation.escalationPressure = 100f;

            ConfrontationSystem.MonthlyTick(state);

            Assert.AreEqual(EscalationState.LimitedConflict, confrontation.escalation,
                "Crossing beyond limited conflict is a decision, never an accident of pressure.");
        }

        [Test]
        public void Boilover_DoesNotRunAwayInASingleTick()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            confrontation.escalation = EscalationState.Tension;
            confrontation.escalationPressure = 200f;

            ConfrontationSystem.MonthlyTick(state);

            Assert.LessOrEqual((int)confrontation.escalation, (int)EscalationState.Tension + 1,
                "One tick must move the situation at most one level, however wound up it is.");
        }

        [Test]
        public void AStandingCrisis_EventuallyBoilsOverOnItsOwn()
        {
            // The mechanic has to fire from ordinary play, not only when a test
            // forces the number. With one-off inputs against a standing decay it
            // could never reach the boil point at all, so it was implemented,
            // tested, and inert.
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.Crisis, "CHN");

            for (int i = 0; i < 36 && confrontation.escalation < EscalationState.LimitedConflict; i++)
                ConfrontationSystem.MonthlyTick(state);

            Assert.AreEqual(EscalationState.LimitedConflict, confrontation.escalation,
                "A crisis left open for three years never became anything. " +
                "Leaving a situation unresolved has to carry a risk of its own.");
        }

        [Test]
        public void ADefusedStandoff_DoesNotBoilOver()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            confrontation.escalation = EscalationState.Tension;
            confrontation.escalationPressure = 0f;

            for (int i = 0; i < 60; i++) ConfrontationSystem.MonthlyTick(state);

            Assert.AreEqual(EscalationState.Tension, confrontation.escalation,
                "A standoff held below crisis must be able to sit there indefinitely — " +
                "de-escalation has to be worth something.");
        }

        [Test]
        public void Pressure_MakesASituationHarderToClose()
        {
            var calm = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            calm.escalationPressure = 0f;
            float relaxed = ConfrontationSystem.SettlementWillingnessFor(state, calm, "CHN");

            calm.escalationPressure = 80f;
            float wound = ConfrontationSystem.SettlementWillingnessFor(state, calm, "CHN");

            Assert.Less(wound, relaxed,
                "Nobody settles while the situation is still winding up.");
        }

        // ---------- occupation actually costs readiness (GDD §19) ----------

        [Test]
        public void Occupation_LowersReadinessAndSurvivesTheMonthlyTick()
        {
            var player = state.PlayerCountry;

            float clean = MeasureSettledReadiness();

            // Seize two foreign locations and hold them.
            int taken = 0;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId == player.id) continue;
                if (location.type == LocationType.Capital) continue;
                location.ownerId = player.id;
                if (++taken == 2) break;
            }
            Assert.AreEqual(2, taken, "Test needs two occupiable foreign locations.");

            float occupying = MeasureSettledReadiness();

            Assert.Less(occupying, clean,
                "Garrison duty must still cost readiness after the tick that restores it. " +
                "A flat monthly subtraction was erased by the same tick's drift back to target.");
        }

        /// <summary>Run enough months for readiness to settle at its target, then read it.</summary>
        float MeasureSettledReadiness()
        {
            var player = state.PlayerCountry;
            for (int i = 0; i < 24; i++)
            {
                MilitarySystem.MonthlyUpkeep(state);
                TerritorySystem.MonthlyUpdate(state);
            }
            return player.military.ground.readiness;
        }

        [Test]
        public void Occupation_DegradesButDoesNotDisarm()
        {
            var player = state.PlayerCountry;
            foreach (var location in state.locations)
                if (location.originalOwnerId != player.id) location.ownerId = player.id;

            for (int i = 0; i < 36; i++)
            {
                MilitarySystem.MonthlyUpkeep(state);
                TerritorySystem.MonthlyUpdate(state);
            }

            Assert.Greater(player.military.ground.readiness, 5f,
                "Occupying the world should degrade a force, not dissolve it.");
        }

        // ---------- a directive the player pays for must do something ----------

        [Test]
        public void RaiseReadinessDirective_ActuallyRaisesReadiness()
        {
            // MIL_READINESS fell through to the no-directive default in
            // ApplyPillarEffect, so the player spent 1 Influence on a directive
            // that was bit-identical to leaving the minister alone.
            var player = state.PlayerCountry;
            var official = state.FindOfficial(Pillar.Military);

            official.mode = ControlMode.Autonomous;
            for (int i = 0; i < 24; i++) MilitarySystem.MonthlyUpkeep(state);
            float undirected = player.military.ground.readiness;

            official.mode = ControlMode.Directed;
            official.directiveId = "MIL_READINESS";
            for (int i = 0; i < 24; i++) MilitarySystem.MonthlyUpkeep(state);
            float directed = player.military.ground.readiness;

            Assert.Greater(directed, undirected,
                "A directive the operator pays Influence for has to change something.");
        }

        [Test]
        public void RaiseReadinessDirective_IsPaidForInTreasury()
        {
            var player = state.PlayerCountry;
            var official = state.FindOfficial(Pillar.Military);
            official.mode = ControlMode.Directed;
            official.directiveId = "MIL_READINESS";
            official.competence = 80f;

            float before = player.resources.treasury;
            for (int i = 0; i < 12; i++) CabinetSystem.MonthlyAct(state);

            Assert.Less(player.resources.treasury, before,
                "'Prioritize force readiness over budget' has to cost the budget.");
        }

        [Test]
        public void ReadinessDirective_DoesNotLeakToForeignArmies()
        {
            var official = state.FindOfficial(Pillar.Military);
            official.mode = ControlMode.Directed;
            official.directiveId = "MIL_READINESS";

            var foreignCountry = state.FindCountry("CHN");
            Assert.AreEqual(0f, MilitarySystem.ReadinessDirectiveBonus(state, foreignCountry),
                "The cabinet is the operator's. Directing our defence ministry must not " +
                "raise the readiness of every army on the map.");
        }

        [Test]
        public void EveryDirectiveChangesSomething()
        {
            // The class of bug, not the instance. MIL_READINESS shipped as a
            // branchless fall-through to the Autonomous default: the player paid
            // Influence for a directive that was bit-identical to leaving the
            // minister alone, and nothing anywhere would have noticed. Any new
            // directive added to the catalog must earn its cost.
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                string baseline = RunWithDirective(pillar, null);
                foreach (var directive in CabinetSystem.GetDirectives(pillar))
                {
                    string directed = RunWithDirective(pillar, directive.id);
                    Assert.AreNotEqual(baseline, directed,
                        $"{directive.id} produces an outcome identical to leaving the " +
                        $"{pillar} minister autonomous. It costs Influence and buys nothing.");
                }
            }
        }

        /// <summary>
        /// Play a fixed stretch of months with one official set as described and
        /// fingerprint what it did to the country. Same seed each time, so any
        /// difference is the directive.
        /// </summary>
        static string RunWithDirective(Pillar pillar, string directiveId)
        {
            var world = WorldFactory.CreateDebugWorld(seed: 31337);
            var official = world.FindOfficial(pillar);
            official.competence = 70f;
            official.riskTolerance = 0f; // remove variance so only the directive differs

            if (directiveId == null)
            {
                official.mode = ControlMode.Autonomous;
                official.directiveId = "";
            }
            else
            {
                official.mode = ControlMode.Directed;
                official.directiveId = directiveId;
            }

            for (int i = 0; i < 12; i++)
            {
                CabinetSystem.MonthlyAct(world);
                MilitarySystem.MonthlyUpkeep(world);
            }

            var player = world.PlayerCountry;
            var p = player.pillars;
            return $"{p.military:F3}|{p.economy:F3}|{p.intelligence:F3}|{p.diplomacy:F3}|{p.government:F3}"
                   + $"|{player.resources.treasury:F3}|{player.stability:F3}|{player.governmentApproval:F3}"
                   + $"|{player.military.ground.readiness:F3}|{player.counterIntel.counterIntelligence:F3}";
        }

        // ---------- efficiency: what the year's spending bought (GDD §25.2) ----------

        [Test]
        public void DoingNothingIsNotEfficient()
        {
            // The trap this component fell into on the first attempt: subtracting
            // spending from delivery handed a perfect score to an operator who
            // did nothing, and measurement showed passive play rising while every
            // engaged playstyle's margin collapsed.
            var quiet = ProgressionSystem.EvaluateYear(state, state.date.year);

            Assert.AreEqual(50f, quiet.efficiencyScore, 0.01f,
                "A year with nothing spent has nothing to judge, and must score neutral " +
                "rather than perfect. Inaction must never be the efficient strategy.");
        }

        [Test]
        public void SpendingWellScoresBetterThanSpendingBadly()
        {
            float ScoreFor(float pillarGain)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 8642);
                ProgressionSystem.CaptureYearSnapshot(world);

                var player = world.PlayerCountry;
                player.resources.treasury -= 1000f;          // same outlay both times
                player.pillars.economy += pillarGain;        // different result

                return ProgressionSystem.EvaluateYear(world, world.date.year).efficiencyScore;
            }

            Assert.Greater(ScoreFor(12f), ScoreFor(0f),
                "The same money spent to better effect has to grade better, " +
                "or the component measures spending rather than efficiency.");
        }

        [Test]
        public void BurningTheReservesForNothingIsPunished()
        {
            ProgressionSystem.CaptureYearSnapshot(state);
            var player = state.PlayerCountry;
            player.resources.treasury -= 2000f;
            player.pillars.economy -= 5f;   // went backwards

            var wasteful = ProgressionSystem.EvaluateYear(state, state.date.year);

            Assert.Less(wasteful.efficiencyScore, 50f,
                "Reserves out of the door with nothing to show must grade below neutral.");
        }

        // ---------- an order can limit itself (GDD §19) ----------

        [Test]
        public void ADegradeOrderBreaksThePositionWithoutTakingIt()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            StrategicLocation target = null;
            foreach (var location in state.locations)
                if (location.ownerId == "CHN" && location.type != LocationType.Capital)
                { target = location; break; }
            target.defenseValue = 0f;
            target.garrison = 0f;

            ConfrontationSystem.LaunchOperationBy(state, confrontation, state.playerCountryId,
                target.id, OperationType.Assault,
                new OperationDirective { territorialIntent = TerritorialIntent.Degrade });

            Assert.AreEqual("CHN", target.ownerId,
                "Ordered to break the position, not hold it — without this every successful " +
                "assault annexed and a limited war was impossible.");
        }

        [Test]
        public void AnEscalationLimitRefusesTheOrderItWouldExceed()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            // Still at Tension: the operation would carry us to Limited Conflict.

            StrategicLocation target = null;
            foreach (var location in state.locations)
                if (location.ownerId == "CHN") { target = location; break; }

            var record = ConfrontationSystem.LaunchOperationBy(state, confrontation,
                state.playerCountryId, target.id, OperationType.Assault,
                new OperationDirective { escalationLimit = EscalationState.Crisis });

            Assert.IsNull(record, "The order exceeded the limit it was given.");
            Assert.AreEqual(EscalationState.Tension, confrontation.escalation,
                "Committing forces must not hand over the decision to widen the war.");
        }

        // ---------- ARCHIVE has a producer (GDD §28.2) ----------

        [Test]
        public void YearEnd_FilesArchiveTraffic()
        {
            ProgressionSystem.EvaluateYear(state, state.date.year);

            bool filed = false;
            foreach (var notification in state.notifications)
                if (notification.priority == NotificationClass.Archive) filed = true;

            Assert.IsTrue(filed,
                "ARCHIVE existed in the enum, the styling and the briefing filter with no producer anywhere.");
        }

        // ---------- FLASH means a decision is required (GDD §28.2) ----------

        [Test]
        public void ResolvedOperations_AreReportsNotFlashTraffic()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);

            string target = null;
            foreach (var location in state.locations)
                if (location.ownerId == "CHN" && location.type != LocationType.Capital) { target = location.id; break; }
            Assert.IsNotNull(target);

            state.notifications.Clear();
            var record = ConfrontationSystem.LaunchOperationBy(state, confrontation, state.playerCountryId,
                target, OperationType.Assault, new OperationDirective());

            Assert.IsNotNull(record, "Operation did not run — the assertion below would prove nothing.");
            Assert.IsNotEmpty(state.notifications, "No traffic at all — nothing was checked.");

            foreach (var notification in state.notifications)
                Assert.AreNotEqual(NotificationClass.Flash, notification.priority,
                    $"'{notification.title}' has already happened — there is nothing to decide. " +
                    "A war was generating a FLASH every single month.");
        }

        [Test]
        public void ForeignLeadershipStruggle_DoesNotFlashOurBriefing()
        {
            state.notifications.Clear();

            // Succession only runs where leadership is not chosen at the ballot
            // box, so the fragile state has to be a non-elective one.
            GovernmentState foreignGov = null;
            foreach (var country in state.countries)
            {
                if (country.isPlayer || country.government.IsElective) continue;
                foreignGov = country.government;
                break;
            }
            Assert.IsNotNull(foreignGov, "Test needs a non-elective foreign government.");

            bool sawOne = false;
            for (int i = 0; i < 240; i++)
            {
                // The month must actually advance: the succession draw is seeded
                // from the month index, so ticking in place redraws one identical
                // roll forever.
                state.date = state.date.NextMonth();
                GovernmentSystem.MonthlyUpdate(state);

                // Keep the foreign state permanently fragile so a struggle is
                // certain to occur — otherwise this test proves nothing.
                foreignGov.eliteCohesion = 10f;
                foreignGov.leader.age = 88;
            }

            foreach (var notification in state.notifications)
                if (notification.title.Contains("CONTESTED SUCCESSION"))
                {
                    sawOne = true;
                    Assert.AreNotEqual(NotificationClass.Flash, notification.priority,
                        "A leadership fight in another country is not a decision on our desk.");
                }

            Assert.IsTrue(sawOne, "No contested succession ever fired — the assertion above never ran.");
        }

        // ---------- XP measures judgement, not repetition (GDD §25.1) ----------

        [Test]
        public void RepeatingOneAction_PaysProgressivelyLess()
        {
            int first = XPFrom(() => ProgressionSystem.AwardXP(state, 100, "Trade adjustment"));
            for (int i = 0; i < 20; i++) ProgressionSystem.AwardXP(state, 100, "Trade adjustment");
            int later = XPFrom(() => ProgressionSystem.AwardXP(state, 100, "Trade adjustment"));

            Assert.Less(later, first,
                "The same lever pulled twenty times should teach less than the first pull.");
        }

        [Test]
        public void RepetitionDiscount_NeverReachesZero()
        {
            for (int i = 0; i < 200; i++) ProgressionSystem.AwardXP(state, 100, "Trade adjustment");
            int veryLate = XPFrom(() => ProgressionSystem.AwardXP(state, 100, "Trade adjustment"));

            Assert.Greater(veryLate, 0,
                "A repeated action should be worth less, not worthless.");
        }

        [Test]
        public void DifferentActions_DoNotDiscountEachOther()
        {
            for (int i = 0; i < 30; i++) ProgressionSystem.AwardXP(state, 100, "Trade adjustment");
            int fresh = XPFrom(() => ProgressionSystem.AwardXP(state, 100, "Treaty concluded"));

            Assert.AreEqual(100, fresh,
                "Wearing out one lever must not devalue a different kind of decision.");
        }

        [Test]
        public void RepetitionDiscount_ResetsEachYear()
        {
            for (int i = 0; i < 30; i++) ProgressionSystem.AwardXP(state, 100, "Trade adjustment");
            int worn = XPFrom(() => ProgressionSystem.AwardXP(state, 100, "Trade adjustment"));

            ProgressionSystem.EvaluateYear(state, state.date.year);

            int renewed = XPFrom(() => ProgressionSystem.AwardXP(state, 100, "Trade adjustment"));

            Assert.Greater(renewed, worn,
                "A lever worn out last year is worth learning from again after a year of something else.");
        }

        [Test]
        public void MonthlyBaseline_IsNotDiscounted()
        {
            int first = XPFrom(() => ProgressionSystem.MonthlyXP(state));
            for (int i = 0; i < 30; i++) ProgressionSystem.MonthlyXP(state);
            int later = XPFrom(() => ProgressionSystem.MonthlyXP(state));

            Assert.AreEqual(first, later,
                "The passive floor is not a repeated decision — the operator did not do anything twice.");
        }

        int XPFrom(System.Action award)
        {
            int before = state.strategistXP;
            award();
            return state.strategistXP - before;
        }
    }
}

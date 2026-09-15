using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The post-war exit from occupation (C5, spec 01 §3c, spec 06 §6b).
    ///
    /// Captured ground used to have no way out once its war was over: a
    /// settlement cedes only the objective, closing a war never touches ground,
    /// and `Withdraw` needs a live confrontation — so the holder paid upkeep and
    /// insurgency bills for the rest of the save. These tests hold the three
    /// things that must stay true of the fix: the exit is **reachable** and
    /// correct, it is a **decision** (the AI weighs value and burden; the player
    /// is never decided for), and it is **not an act of war** or a fiscal verb.
    /// </summary>
    public class OccupationExitTests
    {
        GameState state;
        TurnManager turns;
        string saveDir;
        string previousSaveDir;

        const string Holder = "RUS";
        const string Victim = "CHN";
        const string VictimPort = "CHN_PRT";
        const string VictimWorks = "CHN_IND";

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;

            // Operator wrappers autosave; keep them off the developer's slots.
            saveDir = Path.Combine(Path.GetTempPath(), "brink_occupation_exit_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(saveDir);
            previousSaveDir = SaveSystem.SaveDirectoryOverride;
            SaveSystem.SaveDirectoryOverride = saveDir;

            state = NewWorld();
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.SaveDirectoryOverride = previousSaveDir;
            try { Directory.Delete(saveDir, true); } catch { }
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- helpers ----------

        static GameState NewWorld()
        {
            var world = WorldFactory.CreateDebugWorld(seed: 5171);
            world.commandPoints.current = 60;
            world.politicalCapital = GameState.PoliticalCapitalCap;
            world.authorizedPillarMask = ~0;
            return world;
        }

        static GameDate MonthsBefore(GameDate date, int months)
        {
            int total = date.year * 12 + (date.month - 1) - months;
            return new GameDate(total / 12, total % 12 + 1);
        }

        /// <summary>A war between two states that ended <paramref name="endedMonthsAgo"/> months ago.</summary>
        static Confrontation PastWar(GameState world, string a, string b, int endedMonthsAgo, int length = 12)
        {
            var war = new Confrontation
            {
                id = $"TEST_WAR_{a}_{b}_{world.confrontations.Count}",
                initiatorId = a,
                defenderId = b,
                objective = ConfrontationObjective.Deterrence,
                escalation = EscalationState.LimitedConflict,
                startDate = MonthsBefore(world.date, endedMonthsAgo + length),
                monthsActive = length,
                resolved = true
            };
            world.confrontations.Add(war);
            return war;
        }

        static StrategicLocation Occupy(GameState world, string holderId, string locationId)
        {
            var location = world.FindLocation(locationId);
            Assert.IsNotNull(location, $"the fixture has no location {locationId}");
            location.ownerId = holderId;
            location.pacification = 0f;
            location.garrison = 40f;
            Assert.IsTrue(location.IsOccupied, "the fixture did not actually occupy anything");
            return location;
        }

        /// <summary>Deep arrears: a fiscal crisis by `ConditionOf`'s own definition.</summary>
        static void Distress(GameState world, string countryId)
        {
            var country = world.FindCountry(countryId);
            country.resources.treasury = -50000f;
            Assert.AreEqual(FiscalCondition.Crisis, FiscalSystem.ConditionOf(world, country),
                "the fixture did not put the holder into a fiscal crisis");
        }

        /// <summary>Sound books and a treasury that carries any holding bill for decades.</summary>
        static void Enrich(GameState world, string countryId, float treasury = 1000000f)
        {
            var country = world.FindCountry(countryId);
            country.resources.treasury = treasury;
            country.fiscal.sovereignDebt = 0f;
            country.fiscal.creditStanding = 80f;
            country.fiscal.deficitFinancedMonths = 0;
            country.fiscal.arrearsMonths = 0;
            country.fiscal.restructuringMemoryMonths = 0;
            Assert.LessOrEqual(FiscalSystem.ConditionOf(world, country), FiscalCondition.CashNegativeButCreditworthy,
                "the fixture did not leave the holder fiscally sound");
        }

        static void Decide(GameState world, string countryId)
        {
            var country = world.FindCountry(countryId);
            var ai = world.aiStates.Find(a => a.countryId == countryId);
            AISystem.ConsiderRelinquishment(world, ai, country);
        }

        /// <summary>A burdensome post-war occupation the rule should give up.</summary>
        StrategicLocation BurdensomePostWarHolding(string locationId = VictimPort)
        {
            var location = Occupy(state, Holder, locationId);
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Distress(state, Holder);
            return location;
        }

        // ---------- 1. reachable ----------

        [Test]
        public void PostWarOccupiedGround_HasAReachableExit_WhileWithdrawStillNeedsAWar()
        {
            string us = state.playerCountryId;
            var port = Occupy(state, us, VictimPort);
            PastWar(state, us, Victim, endedMonthsAgo: 30);

            Assert.IsTrue(TerritorySystem.CanRelinquish(state, us, port.id, out string why),
                $"Post-war occupied ground had no exit: {why}");

            // The wartime verb is unchanged: it still needs a confrontation.
            Assert.IsTrue(ConfrontationSystem.RequiresConfrontation(OperationType.Withdraw));
            Assert.IsNull(ConfrontationSystem.LaunchOperation(state, turns, null, port.id,
                OperationType.Withdraw, new OperationDirective()),
                "Withdraw was accepted without a confrontation — C5 must not have repurposed it.");
            Assert.AreEqual(us, port.ownerId);
        }

        [Test]
        public void CanRelinquish_RefusesGroundWeDoNotHoldOrThatIsOurOwn()
        {
            var home = state.locations.First(l => l.originalOwnerId == Holder && l.ownerId == Holder);
            Assert.IsFalse(TerritorySystem.CanRelinquish(state, Holder, home.id, out _),
                "A state was offered its own ground to hand back.");
            Assert.IsFalse(TerritorySystem.CanRelinquish(state, Holder, VictimPort, out _),
                "A state was offered ground it does not hold.");
            Assert.IsFalse(TerritorySystem.CanRelinquish(state, Holder, "NO_SUCH_PLACE", out _));
        }

        // ---------- 2. correct ----------

        [Test]
        public void Relinquishing_ReturnsControl_AndEveryOwnershipReadingFollows()
        {
            var port = Occupy(state, Holder, VictimPort);
            port.garrison = 5f;
            port.pacification = 30f;
            PastWar(state, Holder, Victim, endedMonthsAgo: 30);
            var holder = state.FindCountry(Holder);
            holder.warSupport = 50f;
            var pair = state.FindRelationship(Holder, Victim);
            float memoryBefore = pair.memoryWeight;
            float occupiedBefore = TerritorySystem.OccupiedValue(state, Holder);
            float lostBefore = TerritorySystem.LostValue(state, Victim);

            Assert.IsTrue(TerritorySystem.RelinquishBy(state, Holder, port.id));

            Assert.AreEqual(Victim, port.ownerId, "Control did not return to the original owner.");
            Assert.IsFalse(port.IsOccupied);
            Assert.AreEqual(Victim, port.originalOwnerId, "A relinquishment must not rewrite title.");
            Assert.GreaterOrEqual(port.garrison, 20f, "The returned position was left undefended.");
            Assert.AreEqual(0f, port.pacification, 0.001f);
            Assert.AreEqual(occupiedBefore - port.strategicValue, TerritorySystem.OccupiedValue(state, Holder), 0.01f);
            Assert.AreEqual(lostBefore - port.strategicValue, TerritorySystem.LostValue(state, Victim), 0.01f);
            Assert.AreEqual(50f - TerritorySystem.RelinquishWarSupportCost, holder.warSupport, 0.001f);
            Assert.AreEqual(memoryBefore + TerritorySystem.RelinquishMemoryWeight, pair.memoryWeight, 0.001f);
            Assert.IsTrue(state.chronicle.Any(e => e.countryId == Holder && (e.text ?? "").Contains("relinquished")),
                "A public act left nothing in the record.");
        }

        [Test]
        public void SettlementCededObjective_IsNotEligible()
        {
            string us = state.playerCountryId;
            var war = ConfrontationSystem.BeginBy(state, us, Victim,
                ConfrontationObjective.TerritorialConcession, VictimPort, PrimaryStrategy.Military);
            Assert.IsNotNull(war, "the fixture could not open the war");

            var opponent = state.FindCountry(Victim);
            opponent.warSupport = 5f;
            opponent.pillars.government = 10f;
            war.defenderWarExhaustion = 100f;
            war.momentum = 100f;
            Assert.IsTrue(PeaceSystem.ProposeTerms(state, war, us, PeaceProposal.Of(PeaceTerm.TerritorialCession)),
                "the fixture's settlement was refused");

            var port = state.FindLocation(VictimPort);
            Assert.AreEqual(us, port.ownerId, "Precondition: the objective was ceded to us.");
            Assert.IsFalse(port.IsOccupied, "Ceded ground is recognised title, not occupation.");
            Assert.IsFalse(TerritorySystem.CanRelinquish(state, us, port.id, out _),
                "Ground ceded in a settlement was offered back — C5 would undo objective cession.");

            // And a foreign government never hands back what a settlement gave it.
            var works = state.FindLocation(VictimWorks);
            TerritorySystem.Cede(state, works, Holder);
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Distress(state, Holder);
            for (int month = 0; month < 3; month++) Decide(state, Holder);
            Assert.AreEqual(Holder, works.ownerId);
        }

        [Test]
        public void LiveWarOccupation_CannotBeRelinquished_AndWartimeWithdrawStillWorks()
        {
            string us = state.playerCountryId;
            var war = ConfrontationSystem.BeginBy(state, us, Victim,
                ConfrontationObjective.Deterrence, "", PrimaryStrategy.Military);
            Assert.IsNotNull(war);
            war.escalation = EscalationState.LimitedConflict;
            var port = Occupy(state, us, VictimPort);

            Assert.IsFalse(TerritorySystem.CanRelinquish(state, us, port.id, out string why));
            StringAssert.Contains("STILL BEING FOUGHT", why);

            // A foreign holder in a live war keeps it too, however broke.
            var works = Occupy(state, Holder, VictimWorks);
            Assert.IsNotNull(ConfrontationSystem.BeginBy(state, Holder, Victim,
                ConfrontationObjective.Deterrence, "", PrimaryStrategy.Military));
            Distress(state, Holder);
            Decide(state, Holder);
            Assert.AreEqual(Holder, works.ownerId, "Live-war occupation was relinquished by the new rule.");

            // The wartime verb still does what it always did.
            var record = ConfrontationSystem.LaunchOperation(state, turns, war, port.id,
                OperationType.Withdraw, new OperationDirective());
            Assert.IsNotNull(record, "Wartime Withdraw was refused.");
            Assert.AreEqual(Victim, port.ownerId, "Wartime Withdraw no longer hands the position back.");
        }

        // ---------- 3. a decision ----------

        [Test]
        public void SustainableAI_KeepsItsOccupation()
        {
            var port = Occupy(state, Holder, VictimPort);
            var works = Occupy(state, Holder, VictimWorks);
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Enrich(state, Holder);

            for (int month = 0; month < 6; month++) Decide(state, Holder);

            Assert.AreEqual(Holder, port.ownerId, "A government that can carry the bill gave up ground it chose to hold.");
            Assert.AreEqual(Holder, works.ownerId);
        }

        [Test]
        public void DistressedAI_KeepsUncontestedGroundAnsweringItsOwnShortfall_ButNotContestedGround()
        {
            var field = state.locations.Find(l => l.type == LocationType.EnergyRegion
                                                  && l.originalOwnerId != Holder
                                                  && l.originalOwnerId != state.playerCountryId
                                                  && l.ownerId == l.originalOwnerId);
            Assert.IsNotNull(field, "the fixture found no foreign energy region");
            Occupy(state, Holder, field.id);
            PastWar(state, Holder, field.originalOwnerId, endedMonthsAgo: 60);
            state.FindCountry(Holder).resources.energy = 20f;
            Distress(state, Holder);
            Assert.IsTrue(TerritorySystem.AnswersOwnShortfall(state.FindCountry(Holder), field));
            Assert.IsFalse(InsurgencySystem.Denies(state, field));

            Decide(state, Holder);
            Assert.AreEqual(Holder, field.ownerId,
                "A distressed government handed back the oilfield it is short of — the ground a war was fought for.");

            // Contested ground produces nothing for anybody, so it answers nothing.
            var rising = InsurgencySystem.Open(state, field, InsurgencyCause.Occupation, 70f);
            rising.strength = InsurgencySystem.ContestThreshold + 10f;
            Assert.IsTrue(InsurgencySystem.Denies(state, field));
            Decide(state, Holder);
            Assert.AreEqual(field.originalOwnerId, field.ownerId,
                "Contested ground that yields nothing was kept as though it answered a shortfall.");
        }

        [Test]
        public void ThinReservesAlone_MakeBurdensomeGroundACandidate()
        {
            // Fiscal condition is not the whole rule: sound books with no money to
            // carry the bill still shed ground that is not worth it.
            var port = Occupy(state, Holder, VictimPort);
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Enrich(state, Holder, treasury: 100f);
            Assert.Less(state.FindCountry(Holder).resources.treasury,
                TerritorySystem.HoldingBill(state, Holder) * TerritorySystem.HoldingRunwayMonths);

            Decide(state, Holder);
            Assert.AreEqual(Victim, port.ownerId);
        }

        [Test]
        public void BurdensomePostWarGround_BecomesEligibleAtTheTruceHorizon_NotBefore()
        {
            var port = Occupy(state, Holder, VictimPort);
            var war = PastWar(state, Holder, Victim, endedMonthsAgo: ConfrontationSystem.SettlementTruceMonths - 1);
            Distress(state, Holder);

            Decide(state, Holder);
            Assert.AreEqual(Holder, port.ownerId, "Ground was handed back before the war's truce horizon.");

            war.startDate = MonthsBefore(state.date, ConfrontationSystem.SettlementTruceMonths + war.monthsActive);
            Decide(state, Holder);
            Assert.AreEqual(Victim, port.ownerId, "Burdensome ground was still held at the truce horizon.");
        }

        [Test]
        public void AtMostOneLocationAMonth_TheLargestBillFirst()
        {
            var port = Occupy(state, Holder, VictimPort);
            var works = Occupy(state, Holder, VictimWorks);
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Distress(state, Holder);

            // The port is worth less on paper, but a movement on it makes it the
            // costlier to hold — the rule reads the bill, not the value.
            var rising = InsurgencySystem.Open(state, port, InsurgencyCause.Occupation, 60f);
            rising.strength = 40f;
            Assert.Greater(TerritorySystem.HoldingBillFor(state, port), TerritorySystem.HoldingBillFor(state, works));
            Assert.Less(port.strategicValue, works.strategicValue);

            Decide(state, Holder);
            Assert.AreEqual(Victim, port.ownerId, "The costliest holding was not the one given up.");
            Assert.AreEqual(Holder, works.ownerId, "More than one location was relinquished in a month.");

            Decide(state, Holder);
            Assert.AreEqual(Victim, works.ownerId);
        }

        [Test]
        public void EqualBills_GoToTheFirstInAuthoredOrder()
        {
            var port = Occupy(state, Holder, VictimPort);
            var works = Occupy(state, Holder, VictimWorks);
            port.strategicValue = 70f;
            works.strategicValue = 70f;
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Distress(state, Holder);

            var first = state.locations.IndexOf(port) < state.locations.IndexOf(works) ? port : works;
            var second = first == port ? works : port;

            Decide(state, Holder);
            Assert.AreEqual(Victim, first.ownerId, "An equal-bill tie was not broken by authored order.");
            Assert.AreEqual(Holder, second.ownerId);
        }

        [Test]
        public void TheRuleRunsInTheMonthlyAIFlow()
        {
            SimulationPipeline.Wire(turns, state);
            var port = BurdensomePostWarHolding();
            state.FindRelationship(Holder, Victim).settlementTruceMonths = 60;

            turns.EndMonth();

            Assert.AreEqual(Victim, port.ownerId,
                "A burdensome post-war holding survived a month of the live AI flow.");
        }

        // ---------- 4. the player decides ----------

        [Test]
        public void PlayerGround_IsNeverAutomaticallyRelinquished_EvenInSevereDistress()
        {
            SimulationPipeline.Wire(turns, state);
            string us = state.playerCountryId;
            var port = Occupy(state, us, VictimPort);
            PastWar(state, us, Victim, endedMonthsAgo: 60);
            state.FindRelationship(us, Victim).settlementTruceMonths = 60;
            state.PlayerCountry.resources.treasury = -50000f;

            // The rule itself refuses the player's country outright...
            AISystem.ConsiderRelinquishment(state, null, state.PlayerCountry);
            Assert.AreEqual(us, port.ownerId);

            // ...and three years of the live world, broke, change nothing.
            for (int month = 0; month < 36; month++) turns.EndMonth();

            Assert.IsFalse(state.chronicle.Any(e => e.countryId == us && (e.text ?? "").Contains("relinquished")),
                "The player's country relinquished ground nobody ordered it to.");
            Assert.AreEqual(us, port.ownerId, "Player-held ground changed hands without the operator.");
        }

        [Test]
        public void PlayerCommand_SpendsOneCommandPoint_AndRelinquishes()
        {
            var controller = GameController.Instance;
            controller.NewGame(5171);
            state = controller.State;
            state.PlayerCountry.government.legislativeSupport = 90f;
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;
            string us = state.playerCountryId;

            var port = Occupy(state, us, VictimPort);
            PastWar(state, us, Victim, endedMonthsAgo: 30);
            state.commandPoints.current = 5;

            Assert.IsTrue(controller.RelinquishLocation(port.id), "The operator's order was refused.");
            Assert.AreEqual(4, state.commandPoints.current, "Relinquishing did not cost exactly one Command Point.");
            Assert.AreEqual(Victim, port.ownerId);

            // A refused order spends nothing.
            var works = Occupy(state, us, VictimWorks);
            Assert.IsNotNull(ConfrontationSystem.BeginBy(state, us, Victim,
                ConfrontationObjective.Deterrence, "", PrimaryStrategy.Military));
            int before = state.commandPoints.current;
            Assert.IsFalse(controller.RelinquishLocation(works.id));
            Assert.AreEqual(before, state.commandPoints.current, "A refused relinquishment still spent capacity.");
            Assert.AreEqual(us, works.ownerId);
        }

        // ---------- 5. allied captures ----------

        [Test]
        public void AlliedCapture_BecomesEligibleOnceItsRootWarCloses()
        {
            string us = state.playerCountryId;
            const string Ally = "JPN";
            var root = ConfrontationSystem.BeginBy(state, Victim, us,
                ConfrontationObjective.Deterrence, "", PrimaryStrategy.Military);
            Assert.IsNotNull(root, "the fixture could not open the root war");
            root.escalation = EscalationState.LimitedConflict;
            var satellite = ConfrontationSystem.BeginObligationBy(state, Ally, Victim, us, root.id);
            Assert.IsNotNull(satellite, "the fixture could not open the ally's front");
            Assert.IsTrue(satellite.IsObligationEntry);

            var port = Occupy(state, Ally, VictimPort);
            Assert.IsFalse(TerritorySystem.CanRelinquish(state, Ally, port.id, out _),
                "The ally's capture was relinquishable while its front was still open.");

            ConfrontationSystem.CloseWithSettlement(state, root, us, "Fixture settlement.");
            Assert.IsTrue(satellite.resolved, "Precondition: the ally's front closes with its root.");

            Assert.IsTrue(TerritorySystem.CanRelinquish(state, Ally, port.id, out string why),
                $"An ally's capture had no exit after the war it was joined for ended: {why}");
            Assert.IsTrue(TerritorySystem.RelinquishBy(state, Ally, port.id));
            Assert.AreEqual(Victim, port.ownerId);
        }

        // ---------- 6. not an act of war ----------

        [Test]
        public void Relinquishing_CreatesNoConfrontation()
        {
            BurdensomePostWarHolding();
            int confrontations = state.confrontations.Count;
            int unresolved = state.confrontations.Count(c => !c.resolved);

            Decide(state, Holder);

            Assert.AreEqual(Victim, state.FindLocation(VictimPort).ownerId, "Precondition: the rule relinquished.");
            Assert.AreEqual(confrontations, state.confrontations.Count, "Relinquishing opened a confrontation.");
            Assert.AreEqual(unresolved, state.confrontations.Count(c => !c.resolved));
        }

        [Test]
        public void Relinquishing_LeavesTrucesSanctionsEscalationAndThreatAlone()
        {
            BurdensomePostWarHolding();
            EconomySystem.ImposeSanctionsBy(state, Victim, Holder, SanctionSeverity.Coercive);
            var live = ConfrontationSystem.BeginBy(state, Holder, "IND",
                ConfrontationObjective.Deterrence, "", PrimaryStrategy.Military);

            string Snapshot() => string.Join("|",
                state.relationships.Select(r => $"{r.countryA}{r.countryB}:{r.settlementTruceMonths}:{r.sanctionsTruceMonths}:{r.threatPerceptionOfA:F4}:{r.threatPerceptionOfB:F4}"))
                + "#" + string.Join("|", state.sanctions.Select(s => $"{s.senderId}>{s.targetId}:{s.severity}"))
                + "#" + string.Join("|", state.confrontations.Select(c => $"{c.id}:{c.escalation}:{c.escalationPressure:F4}:{c.resolved}"));

            string before = Snapshot();
            Assert.IsTrue(TerritorySystem.RelinquishBy(state, Holder, VictimPort));
            Assert.AreEqual(before, Snapshot(),
                "Relinquishing moved a truce, a sanction, an escalation or a threat perception.");
            Assert.IsNotNull(live, "the fixture's unrelated war did not open");
        }

        [Test]
        public void TheFormerHolderStopsPaying_UpkeepAndInsurgencyBill()
        {
            var port = BurdensomePostWarHolding();
            var rising = InsurgencySystem.Open(state, port, InsurgencyCause.Occupation, 70f);
            rising.strength = 60f;
            var holder = state.FindCountry(Holder);

            float OneMonthsCharge()
            {
                float before = holder.resources.treasury;
                TerritorySystem.MonthlyUpdate(state);
                InsurgencySystem.MonthlyUpdate(state);
                return before - holder.resources.treasury;
            }

            float holding = OneMonthsCharge();
            Assert.Greater(holding, port.strategicValue * TerritorySystem.OccupationUpkeepPerValue,
                "the fixture's holder was not paying upkeep and an insurgency bill");

            Assert.IsTrue(TerritorySystem.RelinquishBy(state, Holder, port.id));
            float after = OneMonthsCharge();

            Assert.AreEqual(0f, after, 0.01f,
                "The former holder was still billed for ground it handed back.");
        }

        [Test]
        public void TheOccupationMovement_IsLeftToFadeByItsOwnRules()
        {
            var port = BurdensomePostWarHolding();
            var rising = InsurgencySystem.Open(state, port, InsurgencyCause.Occupation, 70f);
            rising.strength = 60f;

            Assert.IsTrue(TerritorySystem.RelinquishBy(state, Holder, port.id));

            Assert.IsTrue(state.insurgencies.Contains(rising),
                "Relinquishing deleted the movement — it must fade through the existing lifecycle.");
            Assert.AreEqual(0f, InsurgencySystem.SupportTargetFor(state, rising), 0.01f,
                "An occupation movement kept a cause after the occupier left.");

            for (int month = 0; month < 60 && state.insurgencies.Contains(rising); month++)
                InsurgencySystem.MonthlyUpdate(state);

            Assert.IsFalse(state.insurgencies.Contains(rising),
                "The movement never faded after its cause was removed.");
        }

        // ---------- 7. safe edges ----------

        [Test]
        public void MissingOriginalOwner_FailsSafely()
        {
            var port = Occupy(state, Holder, VictimPort);
            port.originalOwnerId = "NO_SUCH_STATE";
            Distress(state, Holder);

            Assert.IsFalse(TerritorySystem.CanRelinquish(state, Holder, port.id, out string why));
            Assert.IsNotEmpty(why);
            Assert.IsFalse(TerritorySystem.RelinquishBy(state, Holder, port.id));
            Assert.DoesNotThrow(() => Decide(state, Holder));
            Assert.AreEqual(Holder, port.ownerId);
        }

        [Test]
        public void TheRule_DrawsNoRandomNumbers_AndChangesNothingWhenNothingIsEligible()
        {
            var method = typeof(AISystem).GetMethod(nameof(AISystem.ConsiderRelinquishment));
            Assert.IsNotNull(method);
            Assert.IsFalse(method.GetParameters().Any(p => p.ParameterType == typeof(Random)),
                "The relinquishment rule takes a random source; it must be deterministic.");

            // Nothing occupied anywhere: every government runs the rule and the
            // world is byte-identical afterwards.
            string before = SaveSystem.ToJson(state);
            foreach (var ai in state.aiStates) AISystem.ConsiderRelinquishment(state, ai, state.FindCountry(ai.countryId));
            Assert.AreEqual(before, SaveSystem.ToJson(state), "The rule changed a world with nothing to decide.");

            // A holding the rule keeps is also left untouched.
            Occupy(state, Holder, VictimPort);
            PastWar(state, Holder, Victim, endedMonthsAgo: 60);
            Enrich(state, Holder);
            before = SaveSystem.ToJson(state);
            foreach (var ai in state.aiStates) AISystem.ConsiderRelinquishment(state, ai, state.FindCountry(ai.countryId));
            Assert.AreEqual(before, SaveSystem.ToJson(state), "The rule changed a world where every holding is kept.");
        }

        [Test]
        public void TheRule_IsNotAFiscalVerb_AndDoesNotReadRestructuringHistory()
        {
            string Outcome(int restructuringMemory)
            {
                var world = NewWorld();
                Occupy(world, Holder, VictimPort);
                Occupy(world, Holder, VictimWorks);
                PastWar(world, Holder, Victim, endedMonthsAgo: 60);
                Distress(world, Holder);
                var holder = world.FindCountry(Holder);
                holder.fiscal.restructuringMemoryMonths = restructuringMemory;
                float debt = holder.fiscal.sovereignDebt, credit = holder.fiscal.creditStanding;
                float treasury = holder.resources.treasury;

                Decide(world, Holder);

                Assert.AreEqual(debt, holder.fiscal.sovereignDebt, 0.001f, "Relinquishing touched the debt.");
                Assert.AreEqual(credit, holder.fiscal.creditStanding, 0.001f, "Relinquishing touched credit standing.");
                Assert.AreEqual(treasury, holder.resources.treasury, 0.001f, "Relinquishing touched the treasury.");
                Assert.AreEqual(restructuringMemory, holder.fiscal.restructuringMemoryMonths);
                Assert.IsFalse(world.chronicle.Any(e => (e.text ?? "").Contains("restructures its debt")),
                    "Relinquishing invoked a restructuring.");
                return string.Join(",", world.locations.Where(l => l.originalOwnerId == Victim).Select(l => l.ownerId));
            }

            Assert.AreEqual(Outcome(0), Outcome(40),
                "The decision changed with restructuring history — it belongs to territory, not to the books.");
        }

        [Test]
        public void SaveAndLoad_AfterRelinquishing_PreserveTheReturnedGround()
        {
            var port = BurdensomePostWarHolding();
            Decide(state, Holder);
            Assert.AreEqual(Victim, port.ownerId, "Precondition: the rule relinquished.");

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var reloaded = loaded.FindLocation(VictimPort);

            Assert.AreEqual(Victim, reloaded.ownerId);
            Assert.AreEqual(Victim, reloaded.originalOwnerId);
            Assert.IsFalse(reloaded.IsOccupied);
            Assert.AreEqual(port.garrison, reloaded.garrison, 0.001f);
            Assert.AreEqual(0f, reloaded.pacification, 0.001f);
            Assert.IsFalse(TerritorySystem.CanRelinquish(loaded, Holder, VictimPort, out _));
        }
    }
}

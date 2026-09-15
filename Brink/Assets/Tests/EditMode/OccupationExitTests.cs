using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The post-war occupation exit (C5, GDD §16, §19).
    ///
    /// Ground captured in a war could not be put down once the war ended. A
    /// settlement cedes only the objective; closing a confrontation releases
    /// nothing else that was taken; `Withdraw` is an operation and needs a live
    /// confrontation to be ordered in; and no AI government had any verb for it
    /// at all. So occupation upkeep and the insurgency bill ran forever against
    /// ground nobody could give back — a fiscal trap with no exit, which is what
    /// C4 left behind after it had fixed the fiscal *rules*.
    ///
    /// **This fixture covers a reconstruction of C5, whose original acceptance
    /// verdict was FAIL.** The mechanism below is the one that was measured; the
    /// strategic-containment defect it is known to carry is deliberately still
    /// here and belongs to C5B. Nothing in this file should be read as certifying
    /// the rule is right — only that it is the rule that was built.
    /// </summary>
    public class OccupationExitTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1212);
            state.commandPoints.current = 20;

            // The constitution is not what these tests are about; `AuthoritySystem`
            // has its own fixture. The one test here that *is* about authority
            // revokes it explicitly and asserts it did.
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;

            // NARROW PIPELINE: no monthly systems are wired at all. Every test in
            // this file sets ownership, fiscal condition and war history itself
            // and then calls exactly one thing — `TerritorySystem.RelinquishBy`,
            // `GameController.RelinquishLocation` or `AISystem.MonthlyThink`. The
            // omitted systems are precisely the ones that would move the ground,
            // the treasury or the war between the two ticks being compared:
            // `TerritorySystem.MonthlyUpdate` bills the occupation, `FiscalSystem`
            // would re-derive the condition the test just set, `InsurgencySystem`
            // would grow or fade the rising whose bill is being measured, and
            // `ConfrontationSystem` would resolve or reopen the war the 24-month
            // gate reads. The assertions survive because nothing outside the call
            // under test writes `ownerId`, `garrison`, `pacification`,
            // `warSupport` or `memoryWeight` in these fixtures.
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- fixture helpers ----------

        /// <summary>A country that is not the player and is not <paramref name="besides"/>.</summary>
        CountryState OtherCountry(string besides = null)
        {
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                if (country.id == besides) continue;
                return country;
            }
            Assert.Fail("the debug world has too few countries for this fixture");
            return null;
        }

        /// <summary>First location originally belonging to <paramref name="ownerId"/>.</summary>
        StrategicLocation GroundOf(string ownerId, LocationType? type = null,
            string besides = null)
        {
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != ownerId) continue;
                if (location.id == besides) continue;
                if (type.HasValue && location.type != type.Value) continue;
                return location;
            }
            return null;
        }

        /// <summary>Put <paramref name="holderId"/> on ground belonging to somebody else.</summary>
        StrategicLocation Occupy(StrategicLocation location, string holderId)
        {
            Assert.AreNotEqual(holderId, location.originalOwnerId,
                "the fixture asked a country to occupy its own ground");
            location.ownerId = holderId;
            Assert.IsTrue(location.IsOccupied, "the fixture failed to create an occupation");
            return location;
        }

        /// <summary>
        /// A war between the two that ended <paramref name="monthsAgo"/> months ago,
        /// written the way the production idiom reads it: months since it started,
        /// less the months it ran.
        /// </summary>
        Confrontation EndedWar(string initiatorId, string defenderId, int monthsAgo,
            int ranFor = 12)
        {
            int back = monthsAgo + ranFor;
            var start = new GameDate(state.date.year - (back / 12) - 1,
                                     ((state.date.month - 1 + 12 - (back % 12)) % 12) + 1);
            // Re-derive rather than trust the arithmetic above.
            while (state.date.MonthsSince(start) - ranFor > monthsAgo)
                start = start.NextMonth();

            var confrontation = new Confrontation
            {
                id = $"war-{initiatorId}-{defenderId}-{state.confrontations.Count}",
                initiatorId = initiatorId,
                defenderId = defenderId,
                startDate = start,
                monthsActive = ranFor,
                resolved = true,
                escalation = EscalationState.Peace
            };
            state.confrontations.Add(confrontation);

            Assert.AreEqual(monthsAgo, state.date.MonthsSince(start) - ranFor,
                "the fixture did not place the war where it meant to");
            return confrontation;
        }

        /// <summary>A live war between the two.</summary>
        Confrontation LiveWar(string initiatorId, string defenderId)
        {
            var confrontation = new Confrontation
            {
                id = $"live-{initiatorId}-{defenderId}",
                initiatorId = initiatorId,
                defenderId = defenderId,
                startDate = state.date,
                monthsActive = 3,
                resolved = false,
                escalation = EscalationState.LimitedConflict
            };
            state.confrontations.Add(confrontation);
            return confrontation;
        }

        /// <summary>Make this government plainly unable to fund its garrisons.</summary>
        void Broke(CountryState country)
        {
            country.resources.treasury = 0f;
            Assert.Less(country.resources.treasury,
                TerritorySystem.HoldingBillFor(state, country.id)
                    * TerritorySystem.HoldingRunwayMonths,
                "the fixture failed to make the holder unable to fund its occupations");
        }

        /// <summary>Make this government plainly able to fund them.</summary>
        void Flush(CountryState country)
        {
            country.resources.treasury = 1_000_000f;
            Assert.AreEqual(FiscalCondition.Sound, FiscalSystem.ConditionOf(state, country),
                "the fixture failed to make the holder solvent");
        }

        /// <summary>Stop the pair opening a fresh war inside a test that runs the AI.</summary>
        void KeepThePeace(string a, string b)
        {
            var relationship = state.FindRelationship(a, b);
            if (relationship == null) return;
            relationship.relations = 85f;
            relationship.trust = 85f;
            relationship.settlementTruceMonths = 240;
        }

        // ---------- CanRelinquish ----------

        [Test]
        public void CannotRelinquishOurOwnGround()
        {
            var ours = GroundOf(state.playerCountryId);
            Assert.NotNull(ours, "the player has no authored ground");
            Assert.IsFalse(ours.IsOccupied, "the fixture's control ground is already occupied");

            Assert.IsFalse(TerritorySystem.CanRelinquish(
                state, state.playerCountryId, ours.id, out string reason));
            Assert.IsNotEmpty(reason, "a refusal must say why");
        }

        [Test]
        public void CannotRelinquishGroundWeDoNotHold()
        {
            var theirs = GroundOf(OtherCountry().id);
            Assert.NotNull(theirs);
            Assert.AreNotEqual(state.playerCountryId, theirs.ownerId,
                "the fixture's ground is already ours");

            Assert.IsFalse(TerritorySystem.CanRelinquish(
                state, state.playerCountryId, theirs.id, out string reason));
            Assert.IsNotEmpty(reason);
        }

        [Test]
        public void CannotRelinquishWhileTheWarIsStillOn()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);

            // Eligible but for the war.
            Assert.IsTrue(TerritorySystem.CanRelinquish(
                state, state.playerCountryId, ground.id, out _),
                "the fixture was not eligible before the war was added");

            LiveWar(owner.id, state.playerCountryId);

            Assert.IsFalse(TerritorySystem.CanRelinquish(
                state, state.playerCountryId, ground.id, out string reason));
            StringAssert.Contains("WAR", reason.ToUpperInvariant());
        }

        [Test]
        public void CanRelinquishEligiblePostWarGround()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            EndedWar(state.playerCountryId, owner.id, monthsAgo: 30);

            Assert.IsTrue(TerritorySystem.CanRelinquish(
                state, state.playerCountryId, ground.id, out string reason), reason);
        }

        // ---------- what relinquishing does ----------

        [Test]
        public void TheOriginalOwnerGetsTheGroundBack()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);

            Assert.IsTrue(TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id));

            Assert.AreEqual(owner.id, ground.ownerId, "the ground did not go back to its owner");
            Assert.IsFalse(ground.IsOccupied, "it is still reading as occupied");
        }

        [Test]
        public void AReturnedProvinceIsLeftWithAGarrison()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            ground.garrison = 2f;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            Assert.GreaterOrEqual(ground.garrison, TerritorySystem.ReturnedGarrisonFloor,
                "handing ground back left it undefended");
        }

        [Test]
        public void ARelinquishedProvinceHasNoPacificationFigure()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            ground.pacification = 70f;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            Assert.AreEqual(0f, ground.pacification, 0.001f,
                "pacification measures an occupier settling a population; there is no occupier");
        }

        [Test]
        public void GivingGroundUpCostsWarSupportAtHome()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            var player = state.PlayerCountry;
            player.warSupport = 60f;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            Assert.AreEqual(60f - TerritorySystem.RelinquishWarSupportCost, player.warSupport, 0.001f);
        }

        [Test]
        public void TheReceivingStateRemembersIt()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);

            var relationship = state.FindRelationship(state.playerCountryId, owner.id);
            Assert.NotNull(relationship, "the fixture has no relationship to write to");
            float before = relationship.memoryWeight;
            int entries = relationship.memory.Count;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            Assert.AreEqual(before + TerritorySystem.RelinquishMemoryWeight,
                relationship.memoryWeight, 0.001f);
            Assert.AreEqual(entries + 1, relationship.memory.Count);
            StringAssert.Contains("Returned occupied ground",
                relationship.memory[relationship.memory.Count - 1]);
        }

        [Test]
        public void TheRecordSaysItHappened()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            int before = state.chronicle.Count;
            int notices = state.notifications.Count;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            Assert.Greater(state.chronicle.Count, before, "nothing was chronicled");
            var entry = state.chronicle[state.chronicle.Count - 1];
            StringAssert.Contains(ground.displayName, entry.text);
            Assert.AreEqual(Publicity.Public, entry.publicity,
                "giving territory back is not a secret");

            Assert.Greater(state.notifications.Count, notices,
                "the operator's own withdrawal did not reach the terminal");
        }

        [Test]
        public void RelinquishingStartsNoWar()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            int wars = state.confrontations.Count;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            Assert.AreEqual(wars, state.confrontations.Count,
                "handing ground back opened a confrontation");
        }

        [Test]
        public void RelinquishingIsNotACession()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            string title = ground.originalOwnerId;
            string basing = ground.foreignOperatorId;

            TerritorySystem.RelinquishBy(state, state.playerCountryId, ground.id);

            // `Cede` rewrites `originalOwnerId` and clears basing; this must not.
            // The title never moved — that is the whole difference between giving
            // ground back and signing it away.
            Assert.AreEqual(title, ground.originalOwnerId,
                "relinquishment rewrote the title, which is what Cede does");
            Assert.AreEqual(basing, ground.foreignOperatorId,
                "relinquishment touched basing rights");
        }

        // ---------- the player's command ----------

        [Test]
        public void ThePlayerCommandNeedsMilitaryAuthority()
        {
            var controller = GameController.Instance;
            controller.NewGame(1212);
            state = controller.State;
            turns = controller.Turns;
            state.commandPoints.current = 20;

            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);

            // No authored constitution leaves the military advisory-only — the
            // lowest it reaches is RequiresApproval, under a parliamentary
            // republic. So the refusal to reproduce is a chamber declining to
            // grant the brief, not a pillar the operator may never touch.
            var government = state.PlayerCountry.government;
            government.emergencyPowers = false;
            government.authorityUpgradeMask = 0;
            government.type = GovernmentType.ParliamentaryRepublic;
            state.authorizedPillarMask = 0;
            Assert.AreEqual(AuthoritySystem.AuthorityLevel.RequiresApproval,
                AuthoritySystem.AuthorityOver(state, Pillar.Military),
                "the fixture did not produce a military the operator must ask for");

            government.legislativeSupport = 10f;
            state.politicalCapital = GameState.PoliticalCapitalCap;

            int cp = state.commandPoints.current;
            Assert.IsFalse(controller.RelinquishLocation(ground.id),
                "the command ran without the authority to give it");
            Assert.AreEqual(state.playerCountryId, ground.ownerId, "the ground moved anyway");
            Assert.AreEqual(cp, state.commandPoints.current,
                "a command refused for want of authority still charged command points");

            // And with the chamber behind us the same command goes through, so
            // the refusal above is authority and not some other blockage.
            government.legislativeSupport = 90f;
            Assert.IsTrue(controller.RelinquishLocation(ground.id),
                "the command is blocked by something other than authority");
            Assert.AreEqual(owner.id, ground.ownerId);
        }

        [Test]
        public void ThePlayerCommandCostsOneCommandPoint()
        {
            var controller = GameController.Instance;
            controller.NewGame(1212);
            state = controller.State;
            turns = controller.Turns;
            state.authorizedPillarMask = ~0;
            state.PlayerCountry.government.emergencyPowers = true;
            state.commandPoints.current = 10;

            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);

            Assert.IsTrue(controller.RelinquishLocation(ground.id));

            Assert.AreEqual(10 - GameController.RelinquishCost, state.commandPoints.current);
            Assert.AreEqual(1, GameController.RelinquishCost,
                "the recovered cost is not the one C5 was measured with");
        }

        [Test]
        public void ThePlayerCommandRecordsInitiative()
        {
            var controller = GameController.Instance;
            controller.NewGame(1212);
            state = controller.State;
            turns = controller.Turns;
            state.authorizedPillarMask = ~0;
            state.PlayerCountry.government.emergencyPowers = true;
            state.commandPoints.current = 10;

            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            int before = state.initiativesThisYear;

            Assert.IsTrue(controller.RelinquishLocation(ground.id));

            Assert.AreEqual(before + 1, state.initiativesThisYear,
                "a pillar whose verbs record no initiative grades below doing nothing");
        }

        // ---------- the AI's decision ----------

        [Test]
        public void ASolventGovernmentKeepsWhatItTook()
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);
            var ground = Occupy(GroundOf(owner.id), holder.id);
            EndedWar(holder.id, owner.id, monthsAgo: 60);
            KeepThePeace(holder.id, owner.id);
            Flush(holder);

            AISystem.MonthlyThink(state);

            Assert.AreEqual(holder.id, ground.ownerId,
                "a government that can plainly pay for a garrison gave it up");
        }

        [Test]
        public void AGovernmentThatCannotPayForItLetsItGo()
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);
            var ground = Occupy(GroundOf(owner.id), holder.id);
            EndedWar(holder.id, owner.id, monthsAgo: 60);
            KeepThePeace(holder.id, owner.id);
            Broke(holder);

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, ground.ownerId,
                "an unaffordable post-war occupation was still not released");
        }

        [Test]
        public void TheGroundIsHeldUntilTheWarIsWellOver()
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);
            var ground = Occupy(GroundOf(owner.id), holder.id);
            KeepThePeace(holder.id, owner.id);
            Broke(holder);

            // Inside the window: kept, even though it cannot be afforded.
            EndedWar(holder.id, owner.id,
                monthsAgo: AISystem.PostWarRelinquishmentMonths - 12);
            AISystem.MonthlyThink(state);
            Assert.AreEqual(holder.id, ground.ownerId,
                "the ground went back before the peace had settled");

            // Past it: released. Same world, same holder, same bill.
            state.confrontations.Clear();
            EndedWar(holder.id, owner.id,
                monthsAgo: AISystem.PostWarRelinquishmentMonths + 6);
            AISystem.MonthlyThink(state);
            Assert.AreEqual(owner.id, ground.ownerId,
                "past the window the ground was still not released");
        }

        [Test]
        public void GroundThatAnswersAShortfallIsWorthTheBill()
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);

            var prize = GroundOf(owner.id, LocationType.EnergyRegion);
            if (prize == null) Assert.Ignore("this world authors no energy region to test with");
            var spare = GroundOf(owner.id, besides: prize.id);
            Assert.NotNull(spare, "the fixture needs a second location to prove the routine ran");

            Occupy(prize, holder.id);
            Occupy(spare, holder.id);
            EndedWar(holder.id, owner.id, monthsAgo: 60);
            KeepThePeace(holder.id, owner.id);
            Broke(holder);

            holder.resources.energy = TerritorySystem.ShortfallThreshold - 10f;
            Assert.IsTrue(TerritorySystem.AnswersShortfall(state, holder, prize),
                "the fixture failed to create a shortfall the ground answers");
            Assert.IsFalse(InsurgencySystem.Denies(state, prize),
                "the fixture's prize is contested, which is the other half of the rule");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(holder.id, prize.ownerId,
                "gave up the oilfield it is short of oil without");
            // Non-vacuity: the routine did run, and chose the other one.
            Assert.AreEqual(owner.id, spare.ownerId,
                "nothing was released at all, so the retention proves nothing");
        }

        [Test]
        public void NoMoreThanOneWithdrawalAMonth()
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);

            var first = GroundOf(owner.id);
            var second = GroundOf(owner.id, besides: first.id);
            Assert.NotNull(second, "the fixture needs two locations");

            Occupy(first, holder.id);
            Occupy(second, holder.id);
            EndedWar(holder.id, owner.id, monthsAgo: 60);
            KeepThePeace(holder.id, owner.id);
            Broke(holder);

            AISystem.MonthlyThink(state);

            int returned = 0;
            if (first.ownerId == owner.id) returned++;
            if (second.ownerId == owner.id) returned++;
            Assert.AreEqual(1, returned,
                "a government withdrawing from everything at once reads as a collapse");
        }

        [Test]
        public void TheMostExpensiveGarrisonGoesFirst()
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);

            var cheap = GroundOf(owner.id);
            var dear = GroundOf(owner.id, besides: cheap.id);
            Assert.NotNull(dear, "the fixture needs two locations");

            Occupy(cheap, holder.id);
            Occupy(dear, holder.id);
            cheap.strategicValue = 20f;
            dear.strategicValue = 90f;
            Assert.Greater(TerritorySystem.HoldingBill(state, dear),
                TerritorySystem.HoldingBill(state, cheap),
                "the fixture failed to make one garrison dearer than the other");

            EndedWar(holder.id, owner.id, monthsAgo: 60);
            KeepThePeace(holder.id, owner.id);
            Broke(holder);

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, dear.ownerId, "it let the cheap one go first");
            Assert.AreEqual(holder.id, cheap.ownerId);
        }

        [Test]
        public void TheOperatorsOwnOccupationsAreNeverGivenUpForThem()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            EndedWar(state.playerCountryId, owner.id, monthsAgo: 60);
            KeepThePeace(state.playerCountryId, owner.id);
            Broke(state.PlayerCountry);

            // The routine is eligible in every respect except whose country it is.
            Assert.IsTrue(TerritorySystem.CanRelinquish(
                state, state.playerCountryId, ground.id, out _),
                "the fixture is not otherwise eligible, so this proves nothing");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(state.playerCountryId, ground.ownerId,
                "an occupation the operator chose to hold was handed back by the AI");

            foreach (var ai in state.aiStates)
                Assert.AreNotEqual(state.playerCountryId, ai.countryId,
                    "the player has an AIState, so the isolation rests on the guard alone");
        }

        // ---------- C5B: strategic containment ----------

        /// <summary>
        /// A war the guarantor opened by honouring a guarantee: the owner
        /// attacked `ally`, and `holder` entered on the ally's behalf. Returns
        /// the holder's own front, which is the "producing confrontation".
        /// </summary>
        Confrontation DefensiveEntry(string holderId, string ownerId, string allyId,
            int monthsAgo, int ranFor = 12)
        {
            var root = EndedWar(ownerId, allyId, monthsAgo, ranFor);
            var front = EndedWar(holderId, ownerId, monthsAgo, ranFor);
            front.obligationRootId = root.id;
            front.obligationOnBehalfOfId = allyId;

            Assert.IsTrue(front.IsObligationEntry, "the fixture did not build an obligation entry");
            Assert.IsTrue(AllianceSystem.IsDefensiveEntry(state, front),
                "the fixture's entry is not defensive, which is the thing under test");
            return front;
        }

        /// <summary>The same shape, but the ally was the aggressor — an offensive entry.</summary>
        Confrontation OffensiveEntry(string holderId, string ownerId, string allyId,
            int monthsAgo, int ranFor = 12)
        {
            var root = EndedWar(allyId, ownerId, monthsAgo, ranFor);
            var front = EndedWar(holderId, ownerId, monthsAgo, ranFor);
            front.obligationRootId = root.id;
            front.obligationOnBehalfOfId = allyId;

            Assert.IsTrue(front.IsObligationEntry);
            Assert.IsFalse(AllianceSystem.IsDefensiveEntry(state, front),
                "the fixture built a defensive entry when it meant to build an offensive one");
            return front;
        }

        /// <summary>Set a distressed AI holder up on ground it could otherwise release.</summary>
        (CountryState holder, CountryState owner, StrategicLocation ground) Occupier(
            string besides = null)
        {
            var holder = OtherCountry();
            var owner = OtherCountry(besides: holder.id);
            var ground = Occupy(GroundOf(owner.id), holder.id);
            KeepThePeace(holder.id, owner.id);
            Broke(holder);
            return (holder, owner, ground);
        }

        [Test]
        public void RecentDirectAggressionKeepsTheGround()
        {
            var (holder, owner, ground) = Occupier();
            EndedWar(holder.id, owner.id, monthsAgo: 30);
            // The owner attacked us, inside the window.
            EndedWar(owner.id, holder.id, monthsAgo: 20);
            Assert.IsTrue(AllianceSystem.RecentlyAttacked(
                state, owner.id, holder.id, AISystem.ContainmentWindowMonths),
                "the fixture failed to record a recent attack on the holder");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(holder.id, ground.ownerId,
                "gave ground back to a state that attacked us twenty months ago");
        }

        [Test]
        public void GroundTakenDefendingAnAllyIsKeptWhileThatAllyIsStillBeingAttacked()
        {
            var (holder, owner, ground) = Occupier();
            var ally = OtherCountry(besides: holder.id);
            if (ally.id == owner.id) ally = state.countries.Find(
                c => !c.isPlayer && c.id != holder.id && c.id != owner.id);
            Assert.NotNull(ally, "the fixture needs a third state");

            DefensiveEntry(holder.id, owner.id, ally.id, monthsAgo: 30);
            // ...and the owner went back at that same ally inside the window.
            EndedWar(owner.id, ally.id, monthsAgo: 10);

            Assert.IsFalse(AllianceSystem.RecentlyAttacked(
                state, owner.id, holder.id, AISystem.ContainmentWindowMonths),
                "the fixture also created a direct attack, so this proves nothing about the ally clause");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(holder.id, ground.ownerId,
                "handed back the ground we took defending an ally the owner keeps attacking");
        }

        [Test]
        public void GroundTakenJoiningAnAlliesOwnWarIsNotContained()
        {
            var (holder, owner, ground) = Occupier();
            var ally = state.countries.Find(
                c => !c.isPlayer && c.id != holder.id && c.id != owner.id);
            Assert.NotNull(ally);

            OffensiveEntry(holder.id, owner.id, ally.id, monthsAgo: 30);

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, ground.ownerId,
                "ground taken joining an ally's war of choice is ordinary conquest "
                + "and must follow the ordinary rule");
        }

        [Test]
        public void OurOwnWarStillFollowsTheOrdinaryRule()
        {
            var (holder, owner, ground) = Occupier();
            EndedWar(holder.id, owner.id, monthsAgo: 30);

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, ground.ownerId,
                "a war we chose ourselves produced no containment claim, so C5 should apply");
        }

        [Test]
        public void ContainmentExpires()
        {
            var (holder, owner, ground) = Occupier();
            EndedWar(holder.id, owner.id, monthsAgo: 30);
            // Older than the window: the record has aged out.
            EndedWar(owner.id, holder.id,
                monthsAgo: AISystem.ContainmentWindowMonths + 12);

            Assert.IsFalse(AllianceSystem.RecentlyAttacked(
                state, owner.id, holder.id, AISystem.ContainmentWindowMonths),
                "the fixture's aggression is still inside the window");
            Assert.IsTrue(AllianceSystem.RecentlyAttacked(
                state, owner.id, holder.id, AISystem.ContainmentWindowMonths + 24),
                "the fixture recorded no aggression at all, so ageing out proves nothing");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, ground.ownerId,
                "containment never expired — that is a permanent occupation, which C5B refuses");
        }

        [Test]
        public void AQuietOriginalOwnerGetsItsGroundBack()
        {
            var (holder, owner, ground) = Occupier();
            EndedWar(holder.id, owner.id, monthsAgo: 30);
            foreach (var c in state.confrontations)
                Assert.AreNotEqual(owner.id, c.initiatorId,
                    "the fixture's owner started a war, so it is not quiet");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, ground.ownerId,
                "a state that has attacked nobody was still refused its territory");
        }

        [Test]
        public void TheNeverFoughtPathIsUnchanged()
        {
            var (holder, owner, ground) = Occupier();
            // No confrontation between them at all.
            foreach (var c in state.confrontations)
                Assert.IsFalse(c.Involves(holder.id) && c.Involves(owner.id),
                    "the fixture has a war between the pair, which is not the never-fought path");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(owner.id, ground.ownerId,
                "C5B changed the never-fought path, which is a separate recovery question");
        }

        [Test]
        public void TheOperatorMayStillHandBackContainedGround()
        {
            var controller = GameController.Instance;
            controller.NewGame(1212);
            state = controller.State;
            turns = controller.Turns;
            state.authorizedPillarMask = ~0;
            state.PlayerCountry.government.emergencyPowers = true;
            state.commandPoints.current = 10;

            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            EndedWar(state.playerCountryId, owner.id, monthsAgo: 30);
            EndedWar(owner.id, state.playerCountryId, monthsAgo: 6);

            Assert.IsTrue(AllianceSystem.RecentlyAttacked(
                state, owner.id, state.playerCountryId, AISystem.ContainmentWindowMonths),
                "the fixture is not contained, so this proves nothing");

            Assert.IsTrue(controller.RelinquishLocation(ground.id),
                "containment is an AI judgement and must never bind the operator");
            Assert.AreEqual(owner.id, ground.ownerId);
        }

        [Test]
        public void ContainmentDoesNotChangeWhatTheVerbPermits()
        {
            var (holder, owner, ground) = Occupier();
            EndedWar(holder.id, owner.id, monthsAgo: 30);
            EndedWar(owner.id, holder.id, monthsAgo: 6);

            // The gate is judgement, not permission. `CanRelinquish` must be
            // blind to it, or the player's screen would start refusing too.
            Assert.IsTrue(TerritorySystem.CanRelinquish(
                state, holder.id, ground.id, out string reason), reason);

            AISystem.MonthlyThink(state);
            Assert.AreEqual(holder.id, ground.ownerId, "the AI should still have kept it");
        }

        [Test]
        public void ContainmentIsDeterministic()
        {
            string Play(int seed)
            {
                var s = WorldFactory.CreateDebugWorld(seed);
                var t = new TurnManager(s);
                SimulationPipeline.Wire(t, s);
                for (int i = 0; i < 90; i++) { s.commandPoints.current = 6; t.EndMonth(); }
                var sb = new System.Text.StringBuilder();
                foreach (var l in s.locations) sb.Append(l.id).Append('=').Append(l.ownerId).Append(';');
                sb.Append("wars=").Append(s.confrontations.Count);
                return sb.ToString();
            }

            string a = Play(9090), b = Play(9090);
            Assert.AreEqual(a, b, "containment introduced non-determinism");
            // Non-vacuity: two runs that both did nothing are also equal.
            StringAssert.DoesNotContain("wars=0", a, "the world was inert, so equality proves nothing");
        }

        [Test]
        public void RecentAndAgedAggressionProduceDifferentDecisions()
        {
            // A/B on one fixture: the ONLY difference is how long ago the owner
            // attacked. Measuring one arm alone cannot separate containment from
            // any other reason the ground might not move.
            bool Released(int aggressionMonthsAgo)
            {
                state = WorldFactory.CreateDebugWorld(seed: 1212);
                state.commandPoints.current = 20;
                turns = new TurnManager(state);

                var holder = OtherCountry();
                var owner = OtherCountry(besides: holder.id);
                var ground = Occupy(GroundOf(owner.id), holder.id);
                KeepThePeace(holder.id, owner.id);
                Broke(holder);
                EndedWar(holder.id, owner.id, monthsAgo: 30);
                EndedWar(owner.id, holder.id, monthsAgo: aggressionMonthsAgo);

                AISystem.MonthlyThink(state);
                return ground.ownerId == owner.id;
            }

            bool recent = Released(12);
            bool aged = Released(AISystem.ContainmentWindowMonths + 24);

            Assert.IsFalse(recent, "recent aggression did not hold the ground");
            Assert.IsTrue(aged, "aged-out aggression still held the ground — containment is permanent");
            Assert.AreNotEqual(recent, aged);
        }

        // ---------- reachability ----------

        [Test]
        public void TheCommandIndexKnowsAboutIt()
        {
            var owner = OtherCountry();
            Occupy(GroundOf(owner.id), state.playerCountryId);

            ActionEntry found = null;
            foreach (var entry in ActionCatalog.All(state))
            {
                if (System.Array.IndexOf(entry.verbs,
                        nameof(GameController.RelinquishLocation)) < 0) continue;
                found = entry;
                break;
            }

            Assert.NotNull(found, "the operator cannot find the verb in the COMMAND INDEX");
            Assert.AreEqual(Pillar.Military, found.pillar);
            Assert.AreEqual("MILITARY", found.viewId, "an entry must name the panel it lives on");
            Assert.IsTrue(found.available, "eligible occupied ground still read as unavailable");
        }

        [Test]
        public void TheIndexSaysWhyWhenItCannotBeUsed()
        {
            // No occupation anywhere: the entry still appears, with a reason.
            foreach (var location in state.locations)
                if (location.IsOccupied) location.ownerId = location.originalOwnerId;

            ActionEntry found = null;
            foreach (var entry in ActionCatalog.All(state))
            {
                if (System.Array.IndexOf(entry.verbs,
                        nameof(GameController.RelinquishLocation)) < 0) continue;
                found = entry;
                break;
            }

            Assert.NotNull(found, "an unavailable verb was hidden rather than explained");
            Assert.IsFalse(found.available);
            Assert.IsNotEmpty(found.blockedReason, "unavailable entries must say why");
        }

        // ---------- the holding bill ----------

        [Test]
        public void TheHoldingBillIsTheOneTheTreasuryActuallyPays()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            ground.strategicValue = 50f;

            float expected = 50f * TerritorySystem.OccupationUpkeepPerValue;
            Assert.AreEqual(expected, TerritorySystem.HoldingBill(state, ground), 0.001f,
                "a second definition of what holding costs would drift from the real one");

            // Our own ground is not an occupation and costs nothing to hold.
            var ours = GroundOf(state.playerCountryId);
            Assert.AreEqual(0f, TerritorySystem.HoldingBill(state, ours), 0.001f);
        }

        [Test]
        public void ARisingOnOccupiedGroundIsPartOfTheBill()
        {
            var owner = OtherCountry();
            var ground = Occupy(GroundOf(owner.id), state.playerCountryId);
            ground.strategicValue = 50f;
            float quiet = TerritorySystem.HoldingBill(state, ground);

            state.insurgencies.Add(new Insurgency
            {
                locationId = ground.id,
                strength = 100f
            });

            float contested = TerritorySystem.HoldingBill(state, ground);
            Assert.AreEqual(quiet + InsurgencySystem.InsurgencyBillPerMonth, contested, 0.001f,
                "the insurgency bill is not counted in what the ground costs to hold");
        }
    }
}

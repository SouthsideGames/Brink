using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The military verb list and the arithmetic under it (GDD §19, §15.2).
    ///
    /// Two problems reported from play: three interchangeable ground verbs made
    /// force composition irrelevant, and recruiting a coalition appeared to do
    /// nothing. The second turned out to be a scale bug — `TotalPower` is 0..3
    /// and `garrison` is 0..100, and they were added together, so the entire
    /// national army was about four percent of the defence figure and every
    /// multiplier on it was noise.
    /// </summary>
    public class OperationVerbTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4646);
            state.commandPoints.current = 60;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        Confrontation OpenWar(string targetId)
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, targetId,
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            Assert.IsNotNull(confrontation, $"Could not open a confrontation with {targetId}.");
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);
            return confrontation;
        }

        StrategicLocation TargetIn(string countryId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type != LocationType.Capital)
                    return location;
            Assert.Fail($"{countryId} has no non-capital location to attack.");
            return null;
        }

        /// <summary>Success rate over many independent draws.</summary>
        float SuccessRate(OperationType type, int trials = 200,
            Action<BranchForce> prepare = null)
        {
            int wins = 0;
            for (int i = 0; i < trials; i++)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 4646);

                if (prepare != null)
                    foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
                        prepare(world.PlayerCountry.military.Get(branch));

                var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "MEX",
                    ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(world, confrontation,
                    EscalationState.LimitedConflict, world.playerCountryId);

                StrategicLocation target = null;
                foreach (var location in world.locations)
                    if (location.originalOwnerId == "MEX" && location.type != LocationType.Capital)
                    { target = location; break; }

                var record = MilitarySystem.ResolveOperation(world, confrontation,
                    world.playerCountryId, target, type, new OperationDirective(),
                    new System.Random(i), 0f);
                if (record.success) wins++;
            }
            return wins / (float)trials;
        }

        // ---------- the scale bug ----------

        [Test]
        public void AStrongPowerCanActuallyWinAnAssault()
        {
            // The reported symptom. At the old scale a typical assault sat at
            // roughly 8% odds *no matter what the attacker had built* — TotalPower
            // is 0..3 and garrison is 0..100, and the two were added directly.
            //
            // What is asserted here is the thing that was actually broken: that
            // what a country builds reaches the battlefield. A cold assault is
            // deliberately not a good plan — the army goes in at its authored
            // readiness (~65) with no suppression, no air preparation, no doctrine
            // and no partners, and the whole point of the other verbs is that this
            // is the expensive way to do it. So the floor here is the *scale*, and
            // the ceiling is checked where it belongs: on a force that prepared.
            float cold = SuccessRate(OperationType.Assault);

            Assert.Greater(cold, 0.25f,
                $"A major power assaulting a weaker neighbour won {cold:P0} of the time. " +
                "At three times the broken 8% this is still the army failing to be the " +
                "dominant term in its own battle.");
            Assert.Less(cold, 0.60f,
                $"A cold frontal assault succeeded {cold:P0} of the time. If going in " +
                "unprepared is this good, SUPPRESS DEFENSES and the air verbs are decoration.");

            // The other half, and the half that was never asserted: a force that
            // is ready and supplied has to be able to win outright. A game where
            // preparation moves the number from 32% to 38% has priced the whole
            // military pillar as a rounding error.
            float ready = SuccessRate(OperationType.Assault, prepare: force =>
            {
                force.readiness = 90f;
                force.supply = 90f;
            });

            Assert.Greater(ready, 0.55f,
                $"A fully ready, fully supplied army won only {ready:P0} of the time. " +
                "Readiness and supply are the two things a player spends years on.");
            Assert.Less(ready, 0.95f, "Attacking must not be a formality either.");
        }

        [Test]
        public void ABiggerArmyWinsMoreOften()
        {
            float RateWithStrength(float strength)
            {
                int wins = 0;
                for (int i = 0; i < 150; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 4646);
                    var player = world.PlayerCountry;
                    foreach (ForceBranch branch in System.Enum.GetValues(typeof(ForceBranch)))
                    {
                        var force = player.military.Get(branch);
                        force.strength = strength;
                        force.readiness = 80f;
                        force.supply = 80f;
                    }

                    var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "MEX",
                        ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(world, confrontation,
                        EscalationState.LimitedConflict, world.playerCountryId);

                    StrategicLocation target = null;
                    foreach (var location in world.locations)
                        if (location.originalOwnerId == "MEX" && location.type != LocationType.Capital)
                        { target = location; break; }

                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, OperationType.Assault,
                        new OperationDirective(), new System.Random(i), 0f);
                    if (record.success) wins++;
                }
                return wins / 150f;
            }

            Assert.Greater(RateWithStrength(90f), RateWithStrength(25f) + 0.15f,
                "Building an army has to visibly change what it can do.");
        }

        [Test]
        public void CoalitionSupportChangesOutcomes()
        {
            // The other reported symptom: partners joined and nothing happened.
            float RateWithSupport(float coalitionSupport)
            {
                int wins = 0;
                for (int i = 0; i < 200; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 4646);
                    var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "CHN",
                        ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(world, confrontation,
                        EscalationState.LimitedConflict, world.playerCountryId);

                    StrategicLocation target = null;
                    foreach (var location in world.locations)
                        if (location.originalOwnerId == "CHN" && location.type != LocationType.Capital)
                        { target = location; break; }

                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, OperationType.Assault,
                        new OperationDirective(), new System.Random(i), coalitionSupport);
                    if (record.success) wins++;
                }
                return wins / 200f;
            }

            float alone = RateWithSupport(0f);
            float supported = RateWithSupport(1.2f); // roughly one strong partner

            Assert.Greater(supported, alone + 0.08f,
                $"Alone {alone:P0}, with partners {supported:P0}. Recruiting a coalition has to " +
                "be worth the diplomacy it costs.");
        }

        // ---------- force composition matters ----------

        [Test]
        public void EachOperationDrawsOnTheBranchesItShould()
        {
            var mil = state.PlayerCountry.military;
            mil.ground.strength = 100f; mil.ground.readiness = 100f; mil.ground.supply = 100f;
            mil.air.strength = 0f;
            mil.naval.strength = 0f;

            Assert.Greater(MilitarySystem.BranchPowerFor(mil, OperationType.Assault), 0.3f,
                "An army with only ground forces can still assault.");
            Assert.AreEqual(0f, MilitarySystem.BranchPowerFor(mil, OperationType.AirStrike), 0.001f,
                "No air force, no air strike.");
            Assert.AreEqual(0f, MilitarySystem.BranchPowerFor(mil, OperationType.NavalBlockade), 0.001f,
                "No fleet, no blockade. Build a lopsided military and whole categories of " +
                "operation stop being available to you.");
        }

        [Test]
        public void OnlyAnAssaultCanHoldTheObjective()
        {
            // Two verbs put people on the ground and keep them there. Everything
            // else breaks, degrades or denies — which is what stops the list
            // becoming twenty-three ways to conquer.
            foreach (OperationType type in System.Enum.GetValues(typeof(OperationType)))
                if (type != OperationType.Assault && type != OperationType.AmphibiousAssault)
                    Assert.IsFalse(MilitarySystem.CanTakeGround(type),
                        $"{type} must not end with us owning the ground.");

            Assert.IsTrue(MilitarySystem.CanTakeGround(OperationType.Assault));
            Assert.IsTrue(MilitarySystem.CanTakeGround(OperationType.AmphibiousAssault));
        }

        [Test]
        public void EveryOperationExplainsItself()
        {
            foreach (OperationType type in System.Enum.GetValues(typeof(OperationType)))
                Assert.IsNotEmpty(MilitarySystem.DescribeOperation(type),
                    $"{type} appears as a button with no explanation of what it does.");
        }

        // ---------- the new verbs do what they say ----------

        [Test]
        public void SuppressingDefencesMakesTheNextOperationEasier()
        {
            // The setup move the three-verb list had no way to express.
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");
            target.defenseValue = 80f;
            float before = target.defenseValue;

            for (int i = 0; i < 30 && target.defenseValue >= before; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.SuppressDefenses, new OperationDirective(),
                    new System.Random(i), 0f);

            Assert.Less(target.defenseValue, before,
                "Suppression must permanently lower what the next operation fights through, " +
                "or sequencing operations is pointless.");
        }

        [Test]
        public void ABlockadeHurtsTheCountryNotTheObjective()
        {
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");
            float garrisonBefore = target.garrison;
            float tradeBefore = 0f;
            foreach (var link in state.trade)
                if (link.Involves("MEX")) tradeBefore += link.volume;

            for (int i = 0; i < 30; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.NavalBlockade, new OperationDirective(),
                    new System.Random(i), 0f);

            float tradeAfter = 0f;
            foreach (var link in state.trade)
                if (link.Involves("MEX")) tradeAfter += link.volume;

            Assert.Less(tradeAfter, tradeBefore, "A blockade strangles their trade.");
            Assert.AreEqual(garrisonBefore, target.garrison, 0.01f,
                "It is strategic, not tactical — it must not touch the objective's garrison.");
        }

        [Test]
        public void ASpecialOperationIsCheapInLivesAndInHarm()
        {
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");

            float specialLosses = 0f, specialHarm = 0f, assaultLosses = 0f, assaultHarm = 0f;
            for (int i = 0; i < 20; i++)
            {
                var special = MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.SpecialOperation, new OperationDirective(),
                    new System.Random(i), 0f);
                specialLosses += special.attackerLosses;
                specialHarm += special.civilianHarm;

                var assault = MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.Assault, new OperationDirective(),
                    new System.Random(i), 0f);
                assaultLosses += assault.attackerLosses;
                assaultHarm += assault.civilianHarm;
            }

            Assert.Less(specialLosses, assaultLosses,
                "A handful of people is not an army.");
            Assert.Less(specialHarm, assaultHarm,
                "Precision is the whole point of the instrument.");
        }

        [Test]
        public void ASpecialOperationIsStoppedBySecurityNotByArmies()
        {
            float RateAgainstCounterintel(float level)
            {
                int wins = 0;
                for (int i = 0; i < 120; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 4646);
                    world.FindCountry("MEX").counterIntel.counterIntelligence = level;

                    var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "MEX",
                        ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(world, confrontation,
                        EscalationState.LimitedConflict, world.playerCountryId);

                    StrategicLocation target = null;
                    foreach (var location in world.locations)
                        if (location.originalOwnerId == "MEX" && location.type != LocationType.Capital)
                        { target = location; break; }

                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, OperationType.SpecialOperation,
                        new OperationDirective(), new System.Random(i), 0f);
                    if (record.success) wins++;
                }
                return wins / 120f;
            }

            Assert.Greater(RateAgainstCounterintel(0f), RateAgainstCounterintel(95f),
                "A good security service is the real defence against this, not a big army.");
        }

        [Test]
        public void AnAirStrikeIsTheBluntInstrument()
        {
            // The order screen warns about "heavy civilian risk". Before the
            // per-verb factor that warning was false: a strike commits less
            // weight than an assault, and harm scaled with weight alone, so the
            // stand-off option was quietly the *kindest* one available.
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");
            var directive = new OperationDirective();

            float strikeHarm = 0f, specialHarm = 0f;
            for (int i = 0; i < 20; i++)
            {
                strikeHarm += MilitarySystem.ResolveOperation(state, confrontation,
                    state.playerCountryId, target, OperationType.AirStrike, directive,
                    new System.Random(i), 0f).civilianHarm;
                specialHarm += MilitarySystem.ResolveOperation(state, confrontation,
                    state.playerCountryId, target, OperationType.SpecialOperation, directive,
                    new System.Random(i), 0f).civilianHarm;
            }

            Assert.Greater(strikeHarm, specialHarm * 3f,
                "Bombing a position has to cost civilians far more than a precise action, " +
                "or the warning attached to it is a lie.");
        }

        [Test]
        public void LossesComeOffTheForceThatActuallyFought()
        {
            // Every operation used to charge the ground force and the garrison,
            // which made blockade and suppression into slow assaults.
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");
            var us = state.PlayerCountry;
            var them = state.FindCountry("MEX");

            float ourGroundBefore = us.military.ground.strength;
            float theirNavyBefore = them.military.naval.strength;

            for (int i = 0; i < 15; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.NavalBlockade, new OperationDirective(),
                    new System.Random(i), 0f);

            Assert.AreEqual(ourGroundBefore, us.military.ground.strength, 0.01f,
                "A blockade must not cost us soldiers we never deployed.");
            Assert.Less(them.military.naval.strength, theirNavyBefore,
                "It is fought against their fleet, so their fleet is what it wears down.");
        }

        [Test]
        public void SofteningFirstIsAChoiceRatherThanTheAnswer()
        {
            // The risk the wider list creates: if suppress-then-assault simply
            // beats assaulting, the twenty-three verbs collapse into one opening
            // and the choice is fake. It has to buy real odds *and* cost enough
            // command capacity that spending it is a judgement call.
            float OddsOfAssault(float defenseValue)
            {
                int wins = 0;
                const int trials = 250;
                for (int i = 0; i < trials; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 4646);
                    var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "CHN",
                        ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(world, confrontation,
                        EscalationState.LimitedConflict, world.playerCountryId);

                    StrategicLocation target = null;
                    foreach (var location in world.locations)
                        if (location.originalOwnerId == "CHN" && location.type != LocationType.Capital)
                        { target = location; break; }
                    target.defenseValue = defenseValue;

                    if (MilitarySystem.ResolveOperation(world, confrontation, world.playerCountryId,
                            target, OperationType.Assault, new OperationDirective(),
                            new System.Random(i), 0f).success) wins++;
                }
                return wins / (float)trials;
            }

            float cold = OddsOfAssault(80f);
            float softened = OddsOfAssault(80f - 18f); // one successful suppression

            Assert.Greater(softened, cold,
                $"Suppression left the assault at {softened:P0} against {cold:P0} cold. " +
                "Sequencing has to actually buy something.");

            // And it must not be free. Two operations cost more command capacity
            // than one, which is the whole reason not to always do it.
            Assert.Greater(
                MilitarySystem.OperationCost(OperationType.SuppressDefenses)
                + MilitarySystem.OperationCost(OperationType.Assault),
                MilitarySystem.OperationCost(OperationType.Assault),
                "If softening is free it is always correct, and the choice is not a choice.");
        }

        [Test]
        public void OperationsAreNotAllPricedTheSame()
        {
            // If every verb costs the same, one of them is strictly best and the
            // rest are decoration.
            Assert.Less(MilitarySystem.OperationCost(OperationType.SpecialOperation),
                MilitarySystem.OperationCost(OperationType.Assault),
                "The cheap probe has to be cheaper than the full commitment.");

            int sequenced = MilitarySystem.OperationCost(OperationType.SuppressDefenses)
                            + MilitarySystem.OperationCost(OperationType.Assault);
            Assert.Greater(sequenced, MilitarySystem.OperationCost(OperationType.Assault),
                "Softening the objective first has to cost command capacity, or it is " +
                "free and therefore always correct.");
        }

        [Test]
        public void TheWorldCanUseTheVerbsTheOperatorCan()
        {
            // The failure this codebase keeps repeating: a verb is added for the
            // player, the AI's selection is never touched, and half the military
            // system becomes something only one country in the world can do.
            var used = new System.Collections.Generic.HashSet<string>();
            int operationsSeen = 0;

            for (int seed = 0; seed < 6; seed++)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 7000 + seed);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world); // a bare TurnManager never thinks
                for (int month = 0; month < 180; month++) turns.EndMonth();

                // AI-vs-AI wars only, so nothing here can have been ordered by
                // the operator. OperationRecord does not carry an attacker id.
                foreach (var confrontation in world.confrontations)
                {
                    if (confrontation.Involves(world.playerCountryId)) continue;
                    foreach (var op in confrontation.operations)
                    {
                        used.Add(op.operationType);
                        operationsSeen++;
                    }
                }
            }

            Assert.Greater(operationsSeen, 0,
                "No AI-vs-AI operations ran at all, so this proves nothing.");
            Assert.Greater(used.Count, 2,
                $"Across 90 simulated years and {operationsSeen} operations, the world's " +
                $"governments only ever reached for {used.Count} kind(s): " +
                $"{string.Join(", ", used)}. The wider verb list has to be available to every " +
                "state, not just the operator.");
        }

        [Test]
        public void AnAirStrikeCannotTakeGround()
        {
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");
            string ownerBefore = target.ownerId;

            for (int i = 0; i < 20; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.AirStrike, new OperationDirective(),
                    new System.Random(i), 0f);

            Assert.AreEqual(ownerBefore, target.ownerId,
                "Air power breaks things; it does not hold them.");
        }
    }
}

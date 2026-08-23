using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class SaveMigrationTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void CurrentSave_NeedsNoMigration()
        {
            var state = WorldFactory.CreateDebugWorld(1);
            Assert.AreEqual(SaveSystem.CurrentSaveVersion, SaveMigration.TargetVersion);
            Assert.IsFalse(SaveMigration.NeedsMigration(state));
            Assert.IsFalse(SaveMigration.IsFromFuture(state));

            Assert.AreSame(state, SaveMigration.Migrate(state), "A current save passes straight through.");
        }

        [Test]
        public void FutureSave_IsRefusedRatherThanMangled()
        {
            var state = WorldFactory.CreateDebugWorld(2);
            state.saveVersion = SaveSystem.CurrentSaveVersion + 5;

            Assert.IsTrue(SaveMigration.IsFromFuture(state));
            var error = Assert.Throws<InvalidOperationException>(() => SaveMigration.Migrate(state));
            StringAssert.Contains("newer build", error.Message,
                "The player should be told to update, not shown a broken world.");
        }

        [Test]
        public void MissingMigrationStep_FailsLoudly()
        {
            // Simulate a breaking schema change made without adding a step.
            //
            // Version 0, not `CurrentSaveVersion - 1`: now that a real migration
            // exists, the version immediately below current is precisely the one
            // that *does* have a step, so the old expression tested the opposite
            // of what it claims. Any version with no step at all will do.
            var state = WorldFactory.CreateDebugWorld(3);
            state.saveVersion = 0;

            var error = Assert.Throws<InvalidOperationException>(() => SaveMigration.Migrate(state));
            StringAssert.Contains("No migration step", error.Message);
        }

        [Test]
        public void RoundTripThroughJson_RunsMigrationAndValidation()
        {
            var state = WorldFactory.CreateDebugWorld(4);
            var turns = new TurnManager(state);
            for (int i = 0; i < 6; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(SaveSystem.CurrentSaveVersion, loaded.saveVersion);
            Assert.AreEqual(state.date, loaded.date);
            Assert.NotNull(loaded.PlayerCountry);
        }

        // ---------- validation catches states the simulation cannot run ----------

        [Test]
        public void Validation_RejectsAMissingPlayerCountry()
        {
            var state = WorldFactory.CreateDebugWorld(5);
            state.playerCountryId = "NOWHERE";

            var error = Assert.Throws<InvalidOperationException>(() => SaveMigration.Validate(state));
            StringAssert.Contains("playerCountryId", error.Message);
        }

        [Test]
        public void Validation_RejectsAnEmptyWorld()
        {
            var state = WorldFactory.CreateDebugWorld(6);
            state.countries.Clear();

            Assert.Throws<InvalidOperationException>(() => SaveMigration.Validate(state));
        }

        [Test]
        public void Validation_RejectsOrphanedTerritory()
        {
            var state = WorldFactory.CreateDebugWorld(7);
            state.locations[0].ownerId = "GONE";

            var error = Assert.Throws<InvalidOperationException>(() => SaveMigration.Validate(state));
            StringAssert.Contains("unknown country", error.Message);
        }

        [Test]
        public void Validation_AcceptsAHealthyWorld()
        {
            var state = WorldFactory.CreateDebugWorld(8);
            Assert.DoesNotThrow(() => SaveMigration.Validate(state));
        }

        [Test]
        public void Validation_AcceptsAWorldAfterALongRun()
        {
            var state = WorldFactory.CreateDebugWorld(9);
            var turns = new TurnManager(state);
            turns.ResolveMonth += CabinetSystem.MonthlyAct;
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;
            turns.ResolveMonth += AISystem.MonthlyThink;
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;

            for (int i = 0; i < 240; i++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            Assert.DoesNotThrow(() => SaveMigration.Validate(state),
                "Twenty years of simulation must not produce an unloadable state.");
            Assert.DoesNotThrow(() => SaveSystem.FromJson(SaveSystem.ToJson(state)));
        }

        [Test]
        public void ChainDescription_IsAvailableForSupport()
        {
            Assert.IsNotEmpty(SaveMigration.DescribeChain());
        }
    }
}

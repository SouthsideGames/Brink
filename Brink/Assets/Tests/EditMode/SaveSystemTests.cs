using System.IO;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class SaveSystemTests
    {
        string tempDir;
        string previousDirectory;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            tempDir = Path.Combine(Path.GetTempPath(), "BrinkTests_" + Path.GetRandomFileName());
            previousDirectory = SaveSystem.SaveDirectoryOverride;
            SaveSystem.SaveDirectoryOverride = tempDir;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.SaveDirectoryOverride = previousDirectory;
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }

        [Test]
        public void SaveThenLoad_RoundTripsFullState()
        {
            var state = WorldFactory.CreateDebugWorld(seed: 777);
            var turns = new TurnManager(state);
            for (int i = 0; i < 15; i++) turns.EndMonth();
            turns.SpendCommandPoints(3, "test");
            state.AddChronicle(ChronicleCategory.Military, "CHN", "Border exercise observed.");

            SaveSystem.Save(state, 0);
            var loaded = SaveSystem.Load(0);

            Assert.AreEqual(state.saveVersion, loaded.saveVersion);
            Assert.AreEqual(state.rngSeed, loaded.rngSeed);
            Assert.AreEqual(state.date, loaded.date);
            Assert.AreEqual(state.commandPoints.current, loaded.commandPoints.current);
            Assert.AreEqual(state.countries.Count, loaded.countries.Count);
            Assert.AreEqual(state.playerCountryId, loaded.playerCountryId);
            Assert.AreEqual(state.chronicle.Count, loaded.chronicle.Count);
            Assert.AreEqual("Border exercise observed.", loaded.chronicle[loaded.chronicle.Count - 1].text);

            var player = loaded.PlayerCountry;
            Assert.NotNull(player);
            Assert.AreEqual(state.PlayerCountry.resources.treasury, player.resources.treasury);
            Assert.AreEqual(state.PlayerCountry.pillars.military, player.pillars.military);
        }

        [Test]
        public void SaveExists_ReflectsDiskState()
        {
            Assert.IsFalse(SaveSystem.SaveExists(0));
            SaveSystem.Save(WorldFactory.CreateDebugWorld(1), 0);
            Assert.IsTrue(SaveSystem.SaveExists(0));
            SaveSystem.Delete(0);
            Assert.IsFalse(SaveSystem.SaveExists(0));
        }

        [Test]
        public void Save_OverwritesPreviousSlotContent()
        {
            var state = WorldFactory.CreateDebugWorld(2);
            SaveSystem.Save(state, 0);
            new TurnManager(state).EndMonth();
            SaveSystem.Save(state, 0);

            var loaded = SaveSystem.Load(0);
            Assert.AreEqual(state.date, loaded.date);
            Assert.IsFalse(File.Exists(SaveSystem.SlotPath(0) + ".tmp"), "Temp file should not remain after save.");
        }

        [Test]
        public void Load_MissingSlot_Throws()
        {
            Assert.Throws<FileNotFoundException>(() => SaveSystem.Load(9));
        }

        [Test]
        public void Slots_AreIndependent()
        {
            var a = WorldFactory.CreateDebugWorld(10);
            var b = WorldFactory.CreateDebugWorld(20);
            new TurnManager(b).EndMonth();

            SaveSystem.Save(a, 0);
            SaveSystem.Save(b, 1);

            Assert.AreEqual(a.date, SaveSystem.Load(0).date);
            Assert.AreEqual(b.date, SaveSystem.Load(1).date);
        }

        [Test]
        public void DeterministicSeed_ProducesIdenticalWorlds()
        {
            var a = WorldFactory.CreateDebugWorld(42);
            var b = WorldFactory.CreateDebugWorld(42);
            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(b));
        }

        [Test]
        public void SaveBoundaryCoversTheWholeAssemblyAndIsNotParallel()
        {
            var type = typeof(TestSaveIsolation);
            Assert.IsNull(type.Namespace, "A namespace-scoped boundary misses sibling namespaces.");
            Assert.IsTrue(System.Attribute.IsDefined(type, typeof(SetUpFixtureAttribute)));
            var scheduling = (ParallelizableAttribute)System.Attribute.GetCustomAttribute(
                type, typeof(ParallelizableAttribute));
            Assert.IsNotNull(scheduling);
            Assert.AreEqual(ParallelScope.None, scheduling.Properties.Get("ParallelScope"),
                "SaveDirectoryOverride is process-global, not thread-local.");
        }

        [Test]
        public void ControllerAutosaveUsesTheBoundaryInstalledBeforeFixtureSetup()
        {
            // Check BEFORE calling any saving verb: even a broken boundary must
            // fail this test rather than use the player's real autosave.
            Assert.IsNotNull(previousDirectory);
            StringAssert.StartsWith("brink-test-run-", Path.GetFileName(previousDirectory));
            Assert.IsTrue(Directory.Exists(previousDirectory));
            var controller = GameController.Instance;
            var oldState = controller.State;
            var oldTurns = controller.Turns;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public;
            try
            {
                SaveSystem.SaveDirectoryOverride = previousDirectory;
                controller.NewGame(1212);
                Assert.AreEqual(Path.Combine(previousDirectory, "slot_0.json"), SaveSystem.SlotPath(0));
                Assert.IsTrue(File.Exists(SaveSystem.SlotPath(0)));
                Assert.AreEqual(1212, SaveSystem.Load(0).rngSeed);
            }
            finally
            {
                SaveSystem.SaveDirectoryOverride = tempDir;
                typeof(GameController).GetProperty("State", flags).SetValue(controller, oldState);
                typeof(GameController).GetProperty("Turns", flags).SetValue(controller, oldTurns);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void InnerFixtureRestoresItsEnclosingOverride(bool career)
        {
            string enclosing = SaveSystem.SaveDirectoryOverride;
            var saves = new SaveSystemTests();
            var records = new CareerRecordTests();
            string inner = null;
            try
            {
                if (career) records.SetUp(); else saves.SetUp();
                inner = SaveSystem.SaveDirectoryOverride;
                Assert.AreNotEqual(enclosing, inner);
                Directory.CreateDirectory(inner);
                File.WriteAllText(Path.Combine(inner, "sentinel"), "test only");
            }
            finally
            {
                try { if (career) records.TearDown(); else saves.TearDown(); }
                finally
                {
                    string restored = SaveSystem.SaveDirectoryOverride;
                    SaveSystem.SaveDirectoryOverride = enclosing;
                    Assert.AreEqual(enclosing, restored, "Teardown must not reopen the real save directory.");
                }
            }
            Assert.IsFalse(Directory.Exists(inner));
        }

        [Test]
        public void RunBoundaryRestoresItsCallerAndDeletesOnlyItsOwnDirectory()
        {
            Directory.CreateDirectory(tempDir);
            string sentinel = Path.Combine(tempDir, "sentinel");
            File.WriteAllText(sentinel, "keep");
            string first = null;
            for (int i = 0; i < 2; i++)
            {
                var boundary = new TestSaveIsolation();
                string inner = null;
                try
                {
                    boundary.Begin();
                    inner = SaveSystem.SaveDirectoryOverride;
                    Assert.AreNotEqual(tempDir, inner);
                    Assert.AreNotEqual(first, inner, "Each run needs a fresh directory.");
                    Assert.IsTrue(Directory.Exists(inner));
                    File.WriteAllText(Path.Combine(inner, "sentinel"), "discard");
                }
                finally { boundary.End(); }
                Assert.AreEqual(tempDir, SaveSystem.SaveDirectoryOverride);
                Assert.IsFalse(Directory.Exists(inner));
                Assert.AreEqual("keep", File.ReadAllText(sentinel));
                first = inner;
            }
        }
    }
}

using System.IO;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class SaveSystemTests
    {
        string tempDir;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            tempDir = Path.Combine(Path.GetTempPath(), "BrinkTests_" + Path.GetRandomFileName());
            SaveSystem.SaveDirectoryOverride = tempDir;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.SaveDirectoryOverride = null;
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
    }
}

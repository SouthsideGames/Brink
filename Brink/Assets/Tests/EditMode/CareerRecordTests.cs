using System.IO;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The operator's record across saves (2026-08): written when a posting is
    /// judged, readable from any later game, and never an input to one.
    /// </summary>
    public class CareerRecordTests
    {
        string directory;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            directory = Path.Combine(Path.GetTempPath(), "brink-career-" + System.Guid.NewGuid().ToString("N"));
            SaveSystem.SaveDirectoryOverride = directory;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.SaveDirectoryOverride = null;
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void APosting_IsRecordedOnceAndUpdatedInPlace()
        {
            var state = WorldFactory.CreateDebugWorld(seed: 4);
            var first = CareerRecord.Record(state);
            Assert.NotNull(first);
            Assert.AreEqual("USA", first.countryId);
            Assert.AreEqual("Pending", first.verdict);

            state.mandateRecord = new MandateRecord { verdict = MandateVerdict.Held, met = 3, total = 4, date = state.date };
            CareerRecord.Record(state);

            var file = CareerRecord.Load();
            Assert.AreEqual(1, file.postings.Count, "The same posting was recorded twice.");
            Assert.AreEqual("Held", file.postings[0].verdict);
            Assert.AreEqual(3, file.postings[0].met);
        }

        [Test]
        public void TheRecord_OutlivesTheSave()
        {
            var first = WorldFactory.CreateWorld(11, "DEU");
            CareerRecord.Record(first);
            var second = WorldFactory.CreateWorld(12, "JPN");
            CareerRecord.Record(second);

            var file = CareerRecord.Load();
            Assert.AreEqual(2, file.postings.Count);
            StringAssert.Contains("GERMANY", CareerRecord.StatusText(second));
            StringAssert.Contains("► JAPAN", CareerRecord.StatusText(second));
        }

        [Test]
        public void AnUnreadableFile_IsAnEmptyRecordNotACrash()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(CareerRecord.FilePath, "{ not json");
            var file = CareerRecord.Load();
            Assert.AreEqual(0, file.postings.Count);
            Assert.AreEqual("NO PRIOR POSTINGS ON FILE.", CareerRecord.StatusText(null));
        }
    }
}

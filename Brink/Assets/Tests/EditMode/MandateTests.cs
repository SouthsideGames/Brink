using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The posting's mandate (GDD §25 amendment, 2026-08): every posting opens
    /// with one, it is judged from the world at ten years, the save continues,
    /// and it never asks for foreign ground.
    /// </summary>
    public class MandateTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4242);
            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void EveryPosting_OpensWithAMandate()
        {
            foreach (string posting in WorldFactory.RosterFor(WorldSize.Full))
            {
                var world = WorldFactory.CreateWorld(7, posting, WorldSize.Full);
                Assert.NotNull(world.mandate, $"{posting} opened without a mandate.");
                Assert.GreaterOrEqual(world.mandate.objectives.Count, 3, $"{posting}'s mandate is too thin.");
                Assert.IsNotEmpty(world.mandate.title);
                foreach (var objective in world.mandate.objectives)
                    Assert.IsNotEmpty(objective.text, $"{posting} has an objective with no line for the operator.");
            }
        }

        [Test]
        public void AMandate_NeverAsksForForeignGround()
        {
            // No objective kind can be satisfied by taking somebody else's
            // location: the only territorial claim is holding our own.
            foreach (string posting in WorldFactory.RosterFor(WorldSize.Standard))
            {
                var world = WorldFactory.CreateWorld(7, posting);
                foreach (var objective in world.mandate.objectives)
                    Assert.AreNotEqual("", objective.text);
                Assert.IsFalse(world.mandate.objectives.Exists(o =>
                        o.text.ToLowerInvariant().Contains("take") || o.text.ToLowerInvariant().Contains("seize")
                        || o.text.ToLowerInvariant().Contains("conquer")),
                    $"{posting}'s mandate reads as a conquest checklist (GDD §25).");
            }
        }

        [Test]
        public void TheVerdict_ArrivesAtTenYearsAndTheSaveContinues()
        {
            for (int month = 0; month < MandateSystem.ReviewMonths + 1; month++)
            {
                while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                turns.EndMonth();
            }

            Assert.NotNull(state.mandateRecord, "No verdict after ten years.");
            Assert.AreNotEqual(MandateVerdict.Pending, state.mandateRecord.verdict);
            Assert.AreEqual(state.mandate.objectives.Count, state.mandateRecord.total);
            Assert.IsTrue(state.notifications.Exists(n => n.title == "MANDATE REVIEW"),
                "The verdict never reached the terminal.");

            var before = state.date;
            while (state.HasOpenCrisis) CrisisSystem.Resolve(state, state.activeCrises[0], 0);
            turns.EndMonth();
            Assert.AreEqual(1, state.date.MonthsSince(before), "The game stopped at the verdict. The save always continues.");
        }

        [Test]
        public void TheVerdict_IsJudgedFromTheWorld()
        {
            Assert.AreEqual(MandateVerdict.Fulfilled, MandateSystem.VerdictFor(4, 4));
            Assert.AreEqual(MandateVerdict.Held, MandateSystem.VerdictFor(2, 4));
            Assert.AreEqual(MandateVerdict.Failed, MandateSystem.VerdictFor(1, 4));

            var player = state.PlayerCountry;
            var objective = new MandateObjective { kind = MandateObjectiveKind.StabilityAtLeast, threshold = 60f, text = "x" };
            player.stability = 70f;
            Assert.IsTrue(MandateSystem.IsMet(state, objective));
            player.stability = 50f;
            Assert.IsFalse(MandateSystem.IsMet(state, objective));

            var ground = new MandateObjective { kind = MandateObjectiveKind.HoldOriginalGround, text = "x" };
            Assert.IsTrue(MandateSystem.IsMet(state, ground));
            state.FindLocation(state.mandate.startLocationIds[0]).ownerId = "CHN";
            Assert.IsFalse(MandateSystem.IsMet(state, ground), "Lost ground should fail the holding objective.");
        }

        [Test]
        public void AMandate_SurvivesASaveAndAnOldSaveGetsOne()
        {
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            Assert.NotNull(loaded.mandate);
            Assert.AreEqual(state.mandate.title, loaded.mandate.title);
            Assert.AreEqual(state.mandate.objectives.Count, loaded.mandate.objectives.Count);

            loaded.mandate = null;
            var resumed = new TurnManager(loaded);
            SimulationPipeline.Wire(resumed, loaded);
            while (loaded.HasOpenCrisis) CrisisSystem.Resolve(loaded, loaded.activeCrises[0], 0);
            resumed.EndMonth();
            Assert.NotNull(loaded.mandate, "A save from before mandates existed should receive one at the next month.");
        }

        [Test]
        public void ReloadBeforeReview_DoesNotInventAPendingVerdict()
        {
            Assert.IsNull(state.mandateRecord, "Fresh posting should not already have a verdict record.");

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.IsNull(loaded.mandateRecord,
                "JsonUtility's default MandateRecord must not turn an unfinished mandate into a closed verdict.");
            StringAssert.Contains("REVIEW IN", MandateSystem.StatusText(loaded));
            StringAssert.DoesNotContain("VERDICT: PENDING", MandateSystem.StatusText(loaded));
        }

        [Test]
        public void ReloadAfterReview_PreservesTheRealVerdictRecord()
        {
            state.mandateRecord = new MandateRecord
            {
                date = state.date,
                verdict = MandateVerdict.Held,
                met = 2,
                total = 4,
                summary = "MANDATE HELD — regression record."
            };

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.NotNull(loaded.mandateRecord);
            Assert.AreEqual(MandateVerdict.Held, loaded.mandateRecord.verdict);
            Assert.AreEqual(2, loaded.mandateRecord.met);
            Assert.AreEqual(4, loaded.mandateRecord.total);
            Assert.AreEqual("MANDATE HELD — regression record.", loaded.mandateRecord.summary);
        }

        [Test]
        public void StatusText_ReadsInTheTerminalsVoice()
        {
            string text = MandateSystem.StatusText(state);
            StringAssert.Contains("REVIEW IN", text);
            StringAssert.Contains("[", text);
            Assert.AreEqual(state.mandate.objectives.Count, text.Split('\n').Length - 2);
        }
    }
}

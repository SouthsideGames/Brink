using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Crisis chains (spec 11 §7). An unanswered crisis seeds a follow-up months
    /// later; a crisis somebody actually dealt with does not. The chain is the
    /// cheapest way to make the world feel causal — and it is also the first
    /// mechanic that makes *answering* a crisis worth something beyond the
    /// option's own deltas, because every option is somebody taking
    /// responsibility and the sequel belongs to nobody having done so.
    /// </summary>
    public class CrisisChainTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4242);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static EventDefinition Chain(string id)
        {
            var definition = EventCatalog.Find(id);
            Assert.IsNotNull(definition, $"{id} is not in the catalog.");
            Assert.IsFalse(string.IsNullOrEmpty(definition.followsFrom),
                $"{id} is expected to be a chained event.");
            return definition;
        }

        int MonthIndex() => state.date.MonthsSince(state.startDate);

        void AdvanceMonths(int months)
        {
            for (int i = 0; i < months; i++) state.date = state.date.NextMonth();
        }

        void LapseParentOf(EventDefinition chained)
        {
            CrisisSystem.Trigger(state, chained.followsFrom);
            CrisisSystem.LapseUnanswered(state);
            Assert.AreEqual(0, state.activeCrises.Count, "the lapse did not close the crisis");
        }

        // ---------- the chain itself ----------

        [Test]
        public void ALapsedParentMakesTheFollowUpEligible()
        {
            var chained = Chain("HUNGER_RIOTS");
            LapseParentOf(chained);

            AdvanceMonths(chained.followUpDelayMonths);

            Assert.IsTrue(CrisisSystem.ChainEligible(state, chained, MonthIndex()),
                "the parent lapsed inside the window and the follow-up is still not possible — " +
                "the chain never connects and the field is decoration.");
        }

        [Test]
        public void AResolvedParentSeedsNoSequel()
        {
            var chained = Chain("HUNGER_RIOTS");
            var crisis = CrisisSystem.Trigger(state, chained.followsFrom);
            CrisisSystem.Resolve(state, crisis, 0);

            AdvanceMonths(chained.followUpDelayMonths + 2);

            Assert.IsFalse(CrisisSystem.ChainEligible(state, chained, MonthIndex()),
                "a crisis the operator answered produced a sequel anyway. If dealing with a " +
                "situation and ignoring it chain identically, answering one is pointless.");
        }

        [Test]
        public void TheFollowUpDoesNotArriveTheNextMorning()
        {
            var chained = Chain("HUNGER_RIOTS");
            LapseParentOf(chained);

            AdvanceMonths(chained.followUpDelayMonths - 1);

            Assert.IsFalse(CrisisSystem.ChainEligible(state, chained, MonthIndex()),
                "the follow-up is possible immediately after the lapse. A consequence landing " +
                "the next month reads as the same event still happening, not the world remembering.");
        }

        [Test]
        public void TheWindowCloses()
        {
            var chained = Chain("HUNGER_RIOTS");
            LapseParentOf(chained);

            AdvanceMonths(chained.followUpWindowMonths + 1);

            Assert.IsFalse(CrisisSystem.ChainEligible(state, chained, MonthIndex()),
                "a lapse from years ago still spawns sequels. A chain with no horizon turns one " +
                "mistake into a permanent tax rather than a consequence.");
        }

        [Test]
        public void AChainSurvivesSaveAndLoad()
        {
            var chained = Chain("HUNGER_RIOTS");
            LapseParentOf(chained);
            AdvanceMonths(chained.followUpDelayMonths);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.IsTrue(CrisisSystem.ChainEligible(
                    loaded, chained, loaded.date.MonthsSince(loaded.startDate)),
                "the outcome record did not survive the round trip — saving mid-window " +
                "silently pardons the lapse.");
        }

        [Test]
        public void AnOldSaveSimplyHasNoHistory()
        {
            // Empty is correct, not merely tolerated: a world that predates the
            // record has no measured outcomes, so no chain fires from guessed
            // history. Same reasoning as war verdicts.
            state.crisisOutcomes.Clear();

            foreach (var definition in EventCatalog.Definitions)
                if (!string.IsNullOrEmpty(definition.followsFrom))
                    Assert.IsFalse(CrisisSystem.ChainEligible(state, definition, MonthIndex()),
                        $"{definition.id} is eligible in a world with no recorded outcomes.");
        }

        // ---------- it actually reaches the player ----------

        [Test]
        public void TheSequelActuallyFires()
        {
            var chained = Chain("HUNGER_RIOTS");
            var player = state.PlayerCountry;

            // Keep the parent's condition true so the riot's own eligibility
            // holds, and keep the rest of the world quiet so the draw is not
            // diluted into flakiness.
            player.resources.foodSecurity = 30f;
            player.resources.foodEndowment = 30f;

            LapseParentOf(chained);

            // The monthly draw is 8% and the eligible pool is shared, so a single
            // window can miss honestly. Ten years, re-lapsing the parent whenever
            // the window closes, cannot — if it does, the chain is too thin to
            // exist in real play. Deterministic for the fixed seed.
            bool fired = false;
            for (int month = 0; month < 120 && !fired; month++)
            {
                AdvanceMonths(1);
                player.resources.foodSecurity = 30f;

                if (!CrisisSystem.ChainEligible(state, chained, MonthIndex())
                    && state.activeCrises.Count == 0)
                    LapseParentOf(chained);

                CrisisSystem.SystemicCheck(state);

                for (int i = state.activeCrises.Count - 1; i >= 0; i--)
                {
                    if (state.activeCrises[i].defId == chained.id) { fired = true; break; }
                    // Some other situation arose; answer it and keep waiting.
                    CrisisSystem.Resolve(state, state.activeCrises[i], 0);
                }
            }

            Assert.IsTrue(fired,
                "an eligible follow-up never arrived across ten years of open windows " +
                "(seed 4242). Either the gate is wrong or the weight is too thin to matter.");
        }

        [Test]
        public void ForeignStatesNeverDrawAChainedEvent()
        {
            // A chained event reads the player's outcome record; a foreign
            // government's situations resolve in the same tick and leave no lapse
            // to chain from. If one ever appears in the foreign record, the skip
            // in ForeignCrisisSystem.PickFor has been lost.
            var chainedTitles = new List<string>();
            foreach (var definition in EventCatalog.Definitions)
                if (!string.IsNullOrEmpty(definition.followsFrom))
                    chainedTitles.Add(definition.title.ToLowerInvariant());

            for (int month = 0; month < 240; month++)
            {
                foreach (var country in state.countries)
                {
                    if (country.isPlayer) continue;
                    country.resources.foodSecurity = 25f;
                    country.socialUnrest = 70f;
                    country.nationalUnity = 25f;
                    country.livingStandards = 25f;
                }
                ForeignCrisisSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }

            foreach (var entry in state.chronicle)
            {
                if (entry.countryId == state.playerCountryId) continue;
                foreach (var title in chainedTitles)
                    Assert.IsFalse(entry.text.ToLowerInvariant().Contains(title),
                        $"a chained event reached a foreign state: \"{entry.text}\"");
            }
        }

        // ---------- housekeeping ----------

        [Test]
        public void OutcomesAreRecordedForBothEndings()
        {
            var crisis = CrisisSystem.Trigger(state, "FOOD_SHORTAGE");
            CrisisSystem.Resolve(state, crisis, 0);

            AdvanceMonths(25); // past FOOD_SHORTAGE's cooldown
            CrisisSystem.Trigger(state, "FOOD_SHORTAGE");
            CrisisSystem.LapseUnanswered(state);

            int resolved = 0, lapsed = 0;
            foreach (var outcome in state.crisisOutcomes)
            {
                if (outcome.defId != "FOOD_SHORTAGE") continue;
                if (outcome.lapsed) lapsed++; else resolved++;
            }

            Assert.AreEqual(1, resolved, "the resolved ending was not recorded");
            Assert.AreEqual(1, lapsed, "the lapsed ending was not recorded");
        }

        [Test]
        public void TheRecordDoesNotGrowForever()
        {
            var chained = Chain("HUNGER_RIOTS");
            LapseParentOf(chained);

            // Far past every authored window. SystemicCheck prunes as it runs.
            AdvanceMonths(60);
            CrisisSystem.SystemicCheck(state);

            Assert.AreEqual(0, state.crisisOutcomes.Count,
                "outcomes no chain could still read are being carried in the save forever.");
        }
    }
}

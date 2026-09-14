using System.Collections.Generic;
using System.IO;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Phase B1B: one pre-emption at a time.
    ///
    /// `PreemptProgramme` is raised once per detected foreign programme and
    /// scores from a floor of 84, so a government that had detected several
    /// filled its whole action budget with copies of one objective. Measured
    /// over 8 seeds × 360 months it won 90.2% of the budget cuts that discarded
    /// a live `AssertClaim`, and only 26 claims survived the cut in 240
    /// world-years.
    ///
    /// These tests drive the real planning cycle — `AISystem.MonthlyThink` —
    /// rather than a reconstruction of it, so they fail if the production path
    /// stops behaving as the repair intends.
    /// </summary>
    public class ActionLadderTests
    {
        GameState state;

        /// <summary>
        /// **The duplicate rule can only bind where there is more than one slot.**
        /// `GameState.difficulty` defaults to Standard and `ActionBudget(Standard)`
        /// is 1, so a Standard world keeps exactly one objective and "at most one
        /// pre-emption" is already true of it. The behaviour under test therefore
        /// has to be exercised at Challenging (2) and Ruthless (3), which is what
        /// a real posting runs at — the assessment defaults to Challenging.
        /// Standard is asserted separately, as the degenerate case.
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4242);
            state.difficulty = Difficulty.Challenging;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>
        /// Make `targetId`'s instrument programme visible to everyone.
        /// `EndgameSystem.KnownPreparation` discloses at
        /// `penetration + progress × 0.45 >= 45`, so a completed programme is
        /// visible with no collection at all — which is the authored "the world
        /// gets exactly one warning" rule, not a test shortcut.
        /// </summary>
        static void PlantVisibleProgramme(GameState state, string targetId, EndgameType type)
        {
            var country = state.FindCountry(targetId);
            Assert.IsNotNull(country, $"no such country {targetId}");
            country.endgames.preparations.Add(new EndgamePreparation { type = type, progress = 100f });
        }

        static AIState Observer(GameState state, string id)
        {
            var ai = state.FindAI(id);
            Assert.IsNotNull(ai, $"no AI state for {id}");
            return ai;
        }

        static int CountOf(List<AIObjective> objectives, AIObjectiveType type)
        {
            int n = 0;
            foreach (var o in objectives) if (o.type == type) n++;
            return n;
        }

        // ---------- 1. at most one survives ----------

        [Test]
        public void SeveralDetectedProgrammesYieldAtMostOneSelectedPreemption()
        {
            // Three foreign programmes, all visible to the observer.
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            PlantVisibleProgramme(state, "IND", EndgameType.StrategicDestruction);
            PlantVisibleProgramme(state, "DEU", EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            var ai = Observer(state, "CHN");
            Assert.LessOrEqual(CountOf(ai.objectives, AIObjectiveType.PreemptProgramme), 1,
                "a government filled its action budget with duplicate pre-emptions");
        }

        [Test]
        public void NoGovernmentAnywhereSelectsTwoPreemptionsInOneCycle()
        {
            foreach (var country in state.countries)
                if (country.id != state.playerCountryId)
                    PlantVisibleProgramme(state, country.id, EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            foreach (var ai in state.aiStates)
                Assert.LessOrEqual(CountOf(ai.objectives, AIObjectiveType.PreemptProgramme), 1,
                    $"{ai.countryId} selected more than one pre-emption in a single planning cycle");
        }

        // ---------- 2. the survivor is the strongest ----------

        [Test]
        public void TheSurvivingPreemptionIsTheHighestPriorityCandidate()
        {
            // Different progress levels: the score is `60 + programme × 1.6`,
            // so the most advanced visible programme must be the one kept.
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            PlantVisibleProgramme(state, "IND", EndgameType.StrategicDestruction);
            PlantVisibleProgramme(state, "DEU", EndgameType.StrategicDestruction);
            state.FindCountry("RUS").endgames.Find(EndgameType.StrategicDestruction).progress = 100f;
            state.FindCountry("IND").endgames.Find(EndgameType.StrategicDestruction).progress = 92f;
            state.FindCountry("DEU").endgames.Find(EndgameType.StrategicDestruction).progress = 88f;

            AISystem.MonthlyThink(state);

            foreach (var ai in state.aiStates)
            {
                AIObjective kept = null;
                foreach (var o in ai.objectives)
                    if (o.type == AIObjectiveType.PreemptProgramme) kept = o;
                if (kept == null) continue;
                if (kept.targetId == ai.countryId) continue;

                // Whatever it kept must be at least as alarming as every other
                // programme that government can see.
                float keptAlarm = Alarm(state, ai.countryId, kept.targetId);
                foreach (var other in state.countries)
                {
                    if (other.id == ai.countryId || other.id == kept.targetId) continue;
                    float alarm = Alarm(state, ai.countryId, other.id);
                    Assert.LessOrEqual(alarm, keptAlarm + 0.001f,
                        $"{ai.countryId} kept the pre-emption against {kept.targetId} "
                        + $"while {other.id} was the more advanced programme");
                }
            }
        }

        static float Alarm(GameState state, string observerId, string targetId)
        {
            float worst = 0f;
            foreach (EndgameType type in System.Enum.GetValues(typeof(EndgameType)))
            {
                if (type == EndgameType.TotalMobilization) continue;
                float known = EndgameSystem.KnownPreparation(state, observerId, targetId, type);
                if (known < 0f) continue;
                float weight = EndgameSystem.SeverityOf(type) == StrategicSeverity.Existential ? 0.60f : 0.35f;
                if (known * weight > worst) worst = known * weight;
            }
            return worst;
        }

        // ---------- 3. the freed slots go back to the ladder ----------

        [Test]
        public void FreedSlotsAreOccupiedByOtherObjectiveTypes()
        {
            foreach (var country in state.countries)
                if (country.id != state.playerCountryId)
                    PlantVisibleProgramme(state, country.id, EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            int budget = AISystem.ActionBudget(state.difficulty);
            int fullBudgets = 0, withOtherTypes = 0;
            foreach (var ai in state.aiStates)
            {
                if (ai.objectives.Count < budget) continue;
                fullBudgets++;
                var types = new HashSet<AIObjectiveType>();
                foreach (var o in ai.objectives) types.Add(o.type);
                if (types.Count > 1) withOtherTypes++;
            }

            Assert.Greater(budget, 1, "a one-slot budget cannot demonstrate freed slots");
            Assert.Greater(fullBudgets, 0, "no government filled its budget, so this proves nothing");
            Assert.AreEqual(fullBudgets, withOtherTypes,
                "a government filled a full budget with a single objective type — "
                + "the slots freed by the duplicate rule were not returned to the ladder");
        }

        /// <summary>
        /// The degenerate case, stated rather than assumed: at Standard there is
        /// one slot, so the duplicate rule has nothing to do and the strongest
        /// single candidate still takes it.
        /// </summary>
        [Test]
        public void AStandardWorldKeepsOneObjectiveAndIsUnaffectedByTheDuplicateRule()
        {
            state.difficulty = Difficulty.Standard;
            foreach (var country in state.countries)
                if (country.id != state.playerCountryId)
                    PlantVisibleProgramme(state, country.id, EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            foreach (var ai in state.aiStates)
            {
                Assert.LessOrEqual(ai.objectives.Count, 1,
                    $"{ai.countryId} kept more than the single Standard slot");
                Assert.LessOrEqual(CountOf(ai.objectives, AIObjectiveType.PreemptProgramme), 1);
            }
        }

        /// <summary>Ruthless has three slots; still only one may be a pre-emption.</summary>
        [Test]
        public void ARuthlessWorldStillSelectsAtMostOnePreemption()
        {
            state.difficulty = Difficulty.Ruthless;
            foreach (var country in state.countries)
                if (country.id != state.playerCountryId)
                    PlantVisibleProgramme(state, country.id, EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            int full = 0;
            foreach (var ai in state.aiStates)
            {
                Assert.LessOrEqual(CountOf(ai.objectives, AIObjectiveType.PreemptProgramme), 1,
                    $"{ai.countryId} selected more than one pre-emption at Ruthless");
                if (ai.objectives.Count == 3) full++;
            }
            Assert.Greater(full, 0, "no government filled the three-slot budget, so this proves nothing");
        }

        // ---------- 4. a single candidate is unaffected ----------

        [Test]
        public void ASingleDetectedProgrammeStillSelectsItsPreemption()
        {
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            bool anyoneSelectedIt = false;
            foreach (var ai in state.aiStates)
                if (CountOf(ai.objectives, AIObjectiveType.PreemptProgramme) == 1)
                    anyoneSelectedIt = true;

            Assert.IsTrue(anyoneSelectedIt,
                "a lone visible programme no longer raises a pre-emption anywhere — "
                + "the duplicate rule has suppressed the first candidate, not the copies");
        }

        [Test]
        public void AWorldWithNoProgrammesSelectsNoPreemptions()
        {
            AISystem.MonthlyThink(state);

            foreach (var ai in state.aiStates)
                Assert.AreEqual(0, CountOf(ai.objectives, AIObjectiveType.PreemptProgramme),
                    $"{ai.countryId} pre-empted a programme that does not exist");
        }

        // ---------- 5. budget size is untouched ----------

        [Test]
        public void ActionBudgetSizeIsUnchangedAndStillBounds()
        {
            Assert.AreEqual(1, AISystem.ActionBudget(Difficulty.Standard));
            Assert.AreEqual(2, AISystem.ActionBudget(Difficulty.Challenging));
            Assert.AreEqual(3, AISystem.ActionBudget(Difficulty.Ruthless));

            foreach (var country in state.countries)
                if (country.id != state.playerCountryId)
                    PlantVisibleProgramme(state, country.id, EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            int budget = AISystem.ActionBudget(state.difficulty);
            foreach (var ai in state.aiStates)
                Assert.LessOrEqual(ai.objectives.Count, budget,
                    $"{ai.countryId} kept more objectives than the action budget allows");
        }

        // ---------- 6. the score formulas are untouched ----------

        [Test]
        public void ScoreFormulasAreUnchangedInSource()
        {
            string ai = File.ReadAllText(Path.Combine(
                UnityEngine.Application.dataPath, "Scripts/Core/AISystem.cs"));

            StringAssert.Contains("priority = 60f + programme * 1.6f", ai,
                "the PreemptProgramme score formula changed; B1B is a selection repair only");
            StringAssert.Contains("+ ResourcePrize(state, country, other.id) * 22f", ai,
                "the AssertClaim score formula changed; B1B is a selection repair only");
            StringAssert.Contains(
                "float commitChance = 0.10f + ai.profile.aggression / 300f + ai.profile.opportunism / 400f",
                ai, "the AssertClaim commit probability changed; B1B must not touch it");
        }

        // ---------- 7. no RNG was added, removed or reordered ----------

        [Test]
        public void TheRepairDrawsNoRandomNumbers()
        {
            // The whole planning cycle is deterministic per seed, so the proof
            // that the repair consumes no RNG is that a world reaches an
            // identical state whether or not the duplicate rule had anything to
            // do: plant programmes in one world and not the other, and the
            // *number of draws* must still leave both replayable.
            string first = Fingerprint(4242);
            string second = Fingerprint(4242);
            Assert.AreEqual(first, second, "the planning cycle is no longer deterministic per seed");
        }

        static string Fingerprint(int seed)
        {
            var world = WorldFactory.CreateDebugWorld(seed);
            var turns = new TurnManager(world);
            SimulationPipeline.Wire(turns, world);
            foreach (var country in world.countries)
                if (country.id != world.playerCountryId)
                    country.endgames.preparations.Add(new EndgamePreparation
                    { type = EndgameType.StrategicDestruction, progress = 100f });
            for (int m = 0; m < 24; m++) turns.EndMonth();

            var sb = new System.Text.StringBuilder();
            sb.Append(world.date.SortKey).Append('|').Append(world.actionSequence).Append('|');
            foreach (var c in world.countries)
                sb.Append(c.id).Append(':').Append(c.pillars.military.ToString("F4"))
                  .Append(',').Append(c.stability.ToString("F4"))
                  .Append(',').Append(c.resources.treasury.ToString("F4")).Append('|');
            foreach (var f in world.confrontations)
                sb.Append(f.id).Append(':').Append(f.escalation).Append('|');
            return sb.ToString();
        }

        // ---------- 8. detection and programme state are untouched ----------

        [Test]
        public void ProgrammeDetectionAndStateAreUnchangedBySelection()
        {
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            PlantVisibleProgramme(state, "IND", EndgameType.StrategicDestruction);
            PlantVisibleProgramme(state, "DEU", EndgameType.StrategicDestruction);

            var before = new Dictionary<string, float>();
            foreach (var c in state.countries)
                before[c.id] = c.endgames.ProgressFor(EndgameType.StrategicDestruction);

            // What every government can see, before and after planning.
            var visibleBefore = new Dictionary<string, float>();
            foreach (var ai in state.aiStates)
                foreach (var c in state.countries)
                    visibleBefore[ai.countryId + ">" + c.id] =
                        EndgameSystem.KnownPreparation(state, ai.countryId, c.id, EndgameType.StrategicDestruction);

            AISystem.MonthlyThink(state);

            foreach (var c in state.countries)
                Assert.AreEqual(before[c.id], c.endgames.ProgressFor(EndgameType.StrategicDestruction), 0f,
                    $"{c.id}'s programme progress moved during objective selection");

            foreach (var ai in state.aiStates)
                foreach (var c in state.countries)
                    Assert.AreEqual(visibleBefore[ai.countryId + ">" + c.id],
                        EndgameSystem.KnownPreparation(state, ai.countryId, c.id, EndgameType.StrategicDestruction), 0f,
                        $"what {ai.countryId} can see of {c.id} changed during objective selection");
        }
    }
}

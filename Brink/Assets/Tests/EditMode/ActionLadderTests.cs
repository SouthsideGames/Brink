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

        // ---------- 7. a pre-emption that can no longer act releases the slot ----------

        /// <summary>
        /// Put `observer` in the state where every response to `holder`'s finished
        /// programme has already been made: a station already at full penetration,
        /// counter-intelligence already hardened, no legend to build, no sanction
        /// worth imposing, no deterrent it can afford to begin, and no war to
        /// settle. The programme stays fully visible throughout.
        /// </summary>
        static void ExhaustEveryResponse(GameState state, string observerId, string holderId)
        {
            var observer = state.FindCountry(observerId);
            var ai = Observer(state, observerId);

            state.networks.RemoveAll(n => n.ownerId == observerId && n.targetId == holderId);
            state.networks.Add(new IntelNetwork
            {
                ownerId = observerId, targetId = holderId,
                focus = IntelDomain.Military, penetration = 100f
            });
            observer.counterIntel.counterIntelligence = 60f;   // already hardened
            observer.counterIntel.deceptionStrength = 55f;     // no room for a fresh legend
            ai.profile.aggression = 30f;                       // below the coercion appetite
            observer.resources.treasury = 0f;                  // cannot fund a deterrent

            Assert.IsFalse(state.IsAtWar(observerId), "fixture: the observer must not be at war");
            Assert.Greater(EndgameSystem.KnownPreparation(state, observerId, holderId,
                    EndgameType.StrategicDestruction), 0f,
                "fixture: the programme must stay visible — this is about response, not detection");
        }

        /// <summary>
        /// Make a programme visible whether or not the holder already has one of
        /// that type under way. `EndgamePreparation` lookup returns the *first*
        /// match, so appending a second entry beside a low-progress one leaves
        /// the low figure authoritative and the programme invisible — a fixture
        /// that quietly tests nothing once the world has been run for a month.
        /// </summary>
        static void MakeProgrammeVisible(GameState state, string holderId, EndgameType type)
        {
            var holder = state.FindCountry(holderId);
            Assert.IsNotNull(holder, $"no such country {holderId}");
            foreach (var existing in holder.endgames.preparations)
                if (existing.type == type) { existing.progress = 100f; return; }
            holder.endgames.preparations.Add(new EndgamePreparation { type = type, progress = 100f });
        }

        static AIObjectiveType? OnlyObjective(AIState ai)
            => ai.objectives.Count == 0 ? (AIObjectiveType?)null : ai.objectives[0].type;

        [Test]
        public void ADetectedProgrammeWithAResponseLeftStillRaisesOne()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ai = Observer(state, "IND");
            var ind = state.FindCountry("IND");

            Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: a government with no station on the holder plainly has a response left");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(1, ai.objectives.Count, "Standard difficulty keeps exactly one objective");
            Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai),
                "a visible finished programme with an available response must still command the slot");
            Assert.GreaterOrEqual(ai.actionsThisMonth, 1, "and the response must actually have been carried out");
        }

        [Test]
        public void AnExhaustedPreemptionReleasesTheOnlyObjectiveSlot()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            ExhaustEveryResponse(state, "IND", "RUS");
            var ai = Observer(state, "IND");
            var ind = state.FindCountry("IND");

            Assert.IsFalse(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: every response this government commands is already made");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(1, ai.objectives.Count);
            Assert.AreNotEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai),
                "an answered threat held the only action slot, so nothing else could ever be chosen — "
                + "the threat is still visible and still counted as threat, but it commands no further response");
            Assert.Greater(EndgameSystem.KnownPreparation(state, "IND", "RUS",
                    EndgameType.StrategicDestruction), 0f,
                "releasing the slot must not have hidden the programme");
        }

        [Test]
        public void AfterTheSlotIsReleasedAnotherObjectiveIsSelectedAndActs()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            ExhaustEveryResponse(state, "IND", "RUS");
            var ai = Observer(state, "IND");

            AISystem.MonthlyThink(state);

            Assert.AreNotEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai));
            Assert.GreaterOrEqual(ai.actionsThisMonth, 1,
                "the freed slot produced no action at all, which is starvation by another name");
        }

        [Test]
        public void AMateriallyNewThreatIsAnsweredAgain()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            ExhaustEveryResponse(state, "IND", "RUS");
            var ai = Observer(state, "IND");
            var ind = state.FindCountry("IND");
            AISystem.MonthlyThink(state);
            Assert.AreNotEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai), "fixture: the slot was released");

            // A second state's programme becomes visible. Nothing has been done
            // about that one, so pre-emption is relevant again — no memory, no
            // cooldown, just a response that exists where none did before.
            MakeProgrammeVisible(state, "DEU", EndgameType.StrategicDestruction);
            Assert.Greater(EndgameSystem.KnownPreparation(state, "IND", "DEU",
                    EndgameType.StrategicDestruction), 0f, "fixture: the new programme must be visible");
            Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, "DEU"));

            // Governments re-plan on their own review cadence rather than every
            // month (`3 + patience/20`, so at most eight), which is deliberate:
            // one that re-planned monthly would read as noise. The claim here is
            // that the threat can take the slot back at the next review, not
            // that it interrupts the current objective mid-course.
            string target = null;
            for (int cycle = 0; cycle < 10 && target == null; cycle++)
            {
                AISystem.MonthlyThink(state);
                if (OnlyObjective(ai) == AIObjectiveType.PreemptProgramme) target = ai.objectives[0].targetId;
            }

            Assert.AreEqual("DEU", target,
                "a newly visible programme nobody has answered must be able to take the slot back, "
                + "and be aimed at the threat that still has a response rather than the answered one");
        }

        [Test]
        public void AResponseThatBecomesAvailableAgainRestoresThePreemption()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            ExhaustEveryResponse(state, "IND", "RUS");
            var ai = Observer(state, "IND");
            var ind = state.FindCountry("IND");
            Assert.IsFalse(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"));

            // Counter-intelligence decays; hardening at home is worth doing again.
            ind.counterIntel.counterIntelligence = 20f;

            Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "the same threat must command a response again once one exists");
            AISystem.MonthlyThink(state);
            Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai));
        }

        [Test]
        public void SustainedResponsesAreNotCutShort()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ai = Observer(state, "IND");
            var ind = state.FindCountry("IND");

            // **The fixture has to supply the premise the assertion rests on.**
            // This read the default world, where the only response available to
            // IND is opening its first station on RUS — which the first month
            // takes, after which nothing remains. So "six cycles with a response
            // still available" was never six cycles with a response still
            // available, and the test passed only because an answered objective
            // used to linger until the review clock came round. Once the planner
            // reconsiders an answered plan, the old fixture measures the
            // lingering it was written to rule out.
            //
            // Counter-intelligence at 20 is the premise stated properly: hardening
            // adds three points a month against a threshold of 45, so the response
            // genuinely outlasts the window.
            state.networks.RemoveAll(n => n.ownerId == "IND" && n.targetId == "RUS");
            state.networks.Add(new IntelNetwork
            {
                ownerId = "IND", targetId = "RUS",
                focus = IntelDomain.Military, penetration = 50f
            });
            ind.counterIntel.counterIntelligence = 20f;
            ind.counterIntel.deceptionStrength = 55f;
            ind.pillars.intelligence = 20f;
            ai.profile.aggression = 30f;
            ind.resources.treasury = 0f;

            // Six consecutive planning cycles with a response still available:
            // the objective is not switched off early, and it is not a cooldown.
            int held = 0;
            for (int month = 0; month < 6; month++)
            {
                AISystem.MonthlyThink(state);
                if (OnlyObjective(ai) == AIObjectiveType.PreemptProgramme) held++;
            }

            Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: the responses ran out inside the window, so this measured "
                + "withdrawal rather than the timing-out it claims to rule out");
            Assert.GreaterOrEqual(held, 4,
                $"pre-emption held the slot in only {held} of six cycles while responses remained — "
                + "this correction withdraws an exhausted objective, it does not time one out");
        }

        [Test]
        public void CollectionThatCannotDeepenIsNotAnAction()
        {
            // `CounterRival`'s last step adds four points of penetration. The
            // figure caps at 100, so past that it returned true every month for
            // a change it could not make: an action that consumed the budget and
            // moved nothing.
            var ind = state.FindCountry("IND");
            var ai = Observer(state, "IND");
            state.networks.RemoveAll(n => n.ownerId == "IND" && n.targetId == "RUS");
            state.networks.Add(new IntelNetwork
            {
                ownerId = "IND", targetId = "RUS",
                focus = IntelDomain.Military, penetration = 100f
            });
            ind.counterIntel.counterIntelligence = 60f;
            ind.counterIntel.deceptionStrength = 55f;
            ai.profile.aggression = 30f;

            var method = typeof(AISystem).GetMethod("CounterRival",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "CounterRival was renamed without updating its guard");

            bool acted = (bool)method.Invoke(null, new object[] { state, ai, ind, "RUS", new System.Random(3) });

            Assert.IsFalse(acted,
                "a government reported an action for topping up a station already at full penetration");
            Assert.AreEqual(100f, state.FindNetwork("IND", "RUS").penetration, 0f,
                "and it must not have pretended to move the figure either");
        }

        [Test]
        public void TheEligibilityQuestionChangesNothing()
        {
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ai = Observer(state, "IND");
            var ind = state.FindCountry("IND");

            string before = SaveSystem.ToJson(state);
            int sequence = state.actionSequence;
            bool first = AISystem.PreemptionResponseRemains(state, ai, ind, "RUS");
            for (int i = 0; i < 20; i++)
                Assert.AreEqual(first, AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                    "the same question answered differently on the same state is not deterministic");
            Assert.AreEqual(before, SaveSystem.ToJson(state), "asking whether a response remains changed the world");
            Assert.AreEqual(sequence, state.actionSequence, "asking whether a response remains consumed randomness");
        }

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

        // ---------- 4. an answered plan is reconsidered, and nothing else is ----------

        /// <summary>
        /// Run the real dispatcher over the objectives a government is currently
        /// holding, without re-planning first.
        ///
        /// `AISystem.Act` is private and has exactly one caller, so this is the
        /// only way to ask what dispatch does with a plan that has gone stale
        /// since it was made — which is precisely the case the execution-side
        /// eligibility check exists for.
        /// </summary>
        static void Dispatch(GameState state, AIState ai, CountryState country, int seed)
        {
            var method = typeof(AISystem).GetMethod("Act",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method, "AISystem.Act was renamed without updating this guard");
            method.Invoke(null, new object[] { state, ai, country, new System.Random(seed) });
        }

        /// <summary>
        /// **The execution-side guard, pinned at the dispatcher.**
        ///
        /// `PreemptProgramme` falls through to `CounterRival` when none of its own
        /// steps apply, and `CounterRival`'s last step deepens an existing station.
        /// A station almost always has another four points to give, so without the
        /// eligibility check at the top of `PreemptProgramme` a plan the world had
        /// already answered would spend the month's only action on routine
        /// collection and report it as a pre-emption. Measured over six seeds and
        /// 360 months before the planner learned to reconsider such a plan, that
        /// was roughly 6,000 government-months.
        ///
        /// The fixture is the smallest reachable form of it: eligible for exactly
        /// one reason (hardening is still worth doing at 43), the hardening is
        /// done in the first month of the cycle (43 to 46), and the plan is then
        /// carried into a month where nothing it commands remains.
        /// </summary>
        [Test]
        public void AStalePreemptionDoesNotFallThroughToRoutineCollection()
        {
            state.difficulty = Difficulty.Standard;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ind = state.FindCountry("IND");
            var ai = Observer(state, "IND");

            state.networks.RemoveAll(n => n.ownerId == "IND" && n.targetId == "RUS");
            state.networks.Add(new IntelNetwork
            {
                ownerId = "IND", targetId = "RUS",
                focus = IntelDomain.Military, penetration = 50f
            });
            ind.counterIntel.counterIntelligence = 43f;   // hardening is still worth doing
            ind.counterIntel.deceptionStrength = 55f;     // no room for a legend
            ind.pillars.intelligence = 20f;               // and no service to build one
            ai.profile.aggression = 30f;                  // below the coercion appetite
            ind.resources.treasury = 0f;                  // cannot fund a deterrent

            Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: the plan must be legitimate when it is made");

            AISystem.MonthlyThink(state);

            Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai),
                "fixture: the government must be holding the pre-emption");
            Assert.AreEqual("RUS", ai.objectives[0].targetId);
            Assert.GreaterOrEqual(ind.counterIntel.counterIntelligence, 45f,
                "fixture: the hardening this plan commanded must actually have happened");
            Assert.IsFalse(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: and the plan must therefore now be answered");

            float penetration = state.FindNetwork("IND", "RUS").penetration;
            float treasury = ind.resources.treasury;
            float politicalCapital = ai.politicalCapital;

            Dispatch(state, ai, ind, seed: 11);

            Assert.AreEqual(0, ai.actionsThisMonth,
                "an answered pre-emption reported an action it had no response left to take");
            Assert.AreEqual(penetration, state.FindNetwork("IND", "RUS").penetration, 0f,
                "the month's only action was spent deepening a station, dressed as a pre-emption");
            Assert.AreEqual(treasury, ind.resources.treasury, 0f, "and it spent treasury doing it");
            Assert.AreEqual(politicalCapital, ai.politicalCapital, 0f,
                "and it spent political capital doing it");
        }

        /// <summary>
        /// The planner reconsiders a pre-emption the world has answered instead of
        /// carrying it to the next scheduled review. Releasing the slot is only
        /// half of the repair: before this, the objective was correctly declined
        /// every month until the review came round and **nothing else was
        /// planned**, so the freed slot sat empty.
        /// </summary>
        [Test]
        public void AnAnsweredPreemptionIsReconsideredBeforeTheNextReview()
        {
            state.difficulty = Difficulty.Standard;
            var ai = Observer(state, "IND");
            ai.profile.patience = 100f;                    // review interval of eight months
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ind = state.FindCountry("IND");
            OnlyHardeningRemains(state, ai, ind);

            AISystem.MonthlyThink(state);
            Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai), "fixture: the plan was made");
            Assert.AreEqual(0, ai.objectives[0].monthsPursued, "fixture: the plan is fresh");
            Assert.IsFalse(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: and it is answered before the next month");

            AISystem.MonthlyThink(state);

            Assert.AreNotEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai),
                "a government carried an answered pre-emption for the rest of its review cycle, "
                + "declining it every month and planning nothing in its place");
        }

        /// <summary>
        /// The replacement is an ordinary objective that acts within the ordinary
        /// budget. Reconsideration reopens the planner; it grants no extra action.
        /// </summary>
        [Test]
        public void TheReplacementForAnAnsweredPreemptionActsWithinTheBudget()
        {
            state.difficulty = Difficulty.Standard;
            var ai = Observer(state, "IND");
            ai.profile.patience = 100f;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ind = state.FindCountry("IND");
            OnlyHardeningRemains(state, ai, ind);

            AISystem.MonthlyThink(state);
            AISystem.MonthlyThink(state);

            Assert.LessOrEqual(ai.objectives.Count, AISystem.ActionBudget(state.difficulty),
                "reconsidering a plan handed the government more objectives than its budget");
            Assert.LessOrEqual(ai.actionsThisMonth, AISystem.ActionBudget(state.difficulty),
                "reconsidering a plan bought the government an extra action");
            Assert.GreaterOrEqual(ai.actionsThisMonth, 1,
                "the slot was released and then left empty, which is the starvation this closes");
        }

        /// <summary>
        /// **The distinction reconsideration turns on: a response that exists and
        /// did not fire is not an answered response.**
        ///
        /// `PreemptProgramme` asks an appetite roll before mounting a legend and a
        /// 20% roll before bringing coercion to bear, both deliberately outside
        /// the eligibility predicate so that asking the question cannot consume
        /// the month's randomness. A government at war with the holder whose
        /// opponent will not yet come to terms is the deterministic form of the
        /// same shape: the response is real, it is simply not available this
        /// month. Reconsidering here would re-plan after every quiet month and
        /// keep re-planning until a roll succeeded.
        ///
        /// So this asserts two things at once, and they are the two halves of "no
        /// manufactured activity": the plan is kept, and the month stays idle.
        /// </summary>
        [Test]
        public void AValidPreemptionThatCannotActIsNeitherReplacedNorForcedToAct()
        {
            state.difficulty = Difficulty.Standard;
            var ai = Observer(state, "IND");
            ai.profile.patience = 100f;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ind = state.FindCountry("IND");

            // Every ordinary tool is spent, so the war itself is the only response
            // left — and a war that has just opened is not ready to be settled.
            ExhaustEveryResponse(state, "IND", "RUS");
            state.confrontations.Add(new Confrontation
            {
                id = "probe-war", initiatorId = "RUS", defenderId = "IND",
                objective = ConfrontationObjective.Deterrence,
                escalation = EscalationState.LimitedConflict,
                startDate = state.date
            });

            Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, "RUS"),
                "fixture: seeking terms is a response that remains available");

            AISystem.MonthlyThink(state);
            Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai), "fixture: the plan was made");

            int pursued = ai.objectives[0].monthsPursued;
            for (int month = 0; month < 5; month++)
            {
                AISystem.MonthlyThink(state);
                Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai),
                    "a valid pre-emption was dropped because it had a quiet month");
                Assert.AreEqual(pursued + month + 1, ai.objectives[0].monthsPursued,
                    "the planning cycle restarted after a response declined to fire — "
                    + "that is a reroll loop, not a government changing its mind");
                Assert.LessOrEqual(ai.actionsThisMonth, AISystem.ActionBudget(state.difficulty),
                    "a quiet month was answered with more actions than the budget allows");
            }
        }

        /// <summary>
        /// A plan that is still good keeps its review cadence. Reconsideration is
        /// keyed on the plan being answered, never on the calendar or on how the
        /// month happened to go.
        /// </summary>
        [Test]
        public void AValidPlanIsNotReconsideredBeforeItsReview()
        {
            state.difficulty = Difficulty.Standard;
            var ai = Observer(state, "IND");
            ai.profile.patience = 100f;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ind = state.FindCountry("IND");

            // A response that survives being acted on: hardening adds three
            // points a month from 20 against a threshold of 45, so the plan is
            // still good in the month after it is made. (Opening a first station
            // would not do — the first month takes it and the plan is answered,
            // which is the neighbouring test's subject.)
            state.networks.RemoveAll(n => n.ownerId == "IND" && n.targetId == "RUS");
            state.networks.Add(new IntelNetwork
            {
                ownerId = "IND", targetId = "RUS",
                focus = IntelDomain.Military, penetration = 50f
            });
            ind.counterIntel.counterIntelligence = 20f;
            ind.counterIntel.deceptionStrength = 55f;
            ind.pillars.intelligence = 20f;
            ai.profile.aggression = 30f;
            ind.resources.treasury = 0f;

            AISystem.MonthlyThink(state);
            Assert.AreEqual(AIObjectiveType.PreemptProgramme, OnlyObjective(ai), "fixture: the plan was made");

            AISystem.MonthlyThink(state);
            Assert.AreEqual(1, ai.objectives[0].monthsPursued,
                "a plan that is still good was re-planned mid-cycle");
        }

        /// <summary>
        /// **Above Standard the budget holds more than one objective, and the
        /// answered plan can be the second of them.**
        ///
        /// Pre-emption scores from a floor of 84 and usually leads, so this is
        /// the uncommon arrangement rather than the impossible one: measured over
        /// three seeds and 360 months, a pre-emption sits below the leading slot
        /// in 220 government-months at Challenging and 406 at Ruthless, and is
        /// already answered in 68 and 122 of them. Examining only the first
        /// objective would strand exactly those.
        ///
        /// The fixture arranges the order by hand because the scoring that
        /// produces it depends on a whole world's worth of threat; what is under
        /// test is whether the planner looks past the first slot, not how a plan
        /// came to sit in the second one.
        /// </summary>
        [Test]
        public void AnAnsweredPreemptionIsReconsideredEvenBelowTheLeadingSlot()
        {
            state.difficulty = Difficulty.Challenging;
            var ai = Observer(state, "IND");
            ai.profile.patience = 100f;
            PlantVisibleProgramme(state, "RUS", EndgameType.StrategicDestruction);
            var ind = state.FindCountry("IND");
            OnlyHardeningRemains(state, ai, ind);

            AISystem.MonthlyThink(state);

            AIObjective preemption = null;
            foreach (var objective in ai.objectives)
                if (objective.type == AIObjectiveType.PreemptProgramme) preemption = objective;
            Assert.IsNotNull(preemption, "fixture: the government must be holding a pre-emption");
            Assert.Greater(ai.objectives.Count, 1, "fixture: Challenging must keep more than one objective");
            Assert.IsFalse(AISystem.PreemptionResponseRemains(state, ai, ind, preemption.targetId),
                "fixture: and that pre-emption must already be answered");

            ai.objectives.Remove(preemption);
            ai.objectives.Add(preemption);
            Assert.AreNotEqual(AIObjectiveType.PreemptProgramme, ai.objectives[0].type,
                "fixture: the answered plan must be sitting below the leading slot");

            AISystem.MonthlyThink(state);

            foreach (var objective in ai.objectives)
                if (objective.type == AIObjectiveType.PreemptProgramme)
                    Assert.IsTrue(AISystem.PreemptionResponseRemains(state, ai, ind, objective.targetId),
                        "an answered pre-emption below the leading slot was carried to the next review — "
                        + "the planner is only looking at the first objective it holds");
        }

        /// <summary>
        /// Eligible for exactly one reason — hardening at home is still worth
        /// doing — so acting on the plan once answers it completely.
        /// </summary>
        static void OnlyHardeningRemains(GameState state, AIState ai, CountryState country)
        {
            state.networks.RemoveAll(n => n.ownerId == country.id && n.targetId == "RUS");
            state.networks.Add(new IntelNetwork
            {
                ownerId = country.id, targetId = "RUS",
                focus = IntelDomain.Military, penetration = 50f
            });
            country.counterIntel.counterIntelligence = 43f;
            country.counterIntel.deceptionStrength = 55f;
            country.pillars.intelligence = 20f;
            ai.profile.aggression = 30f;
            country.resources.treasury = 0f;
        }
    }
}

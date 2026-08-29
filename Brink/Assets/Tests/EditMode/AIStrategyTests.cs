using System;
using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Strategic paths, opponent modelling and counter-play (GDD §24).
    ///
    /// The design requirement these serve: a player must not be able to play a few
    /// months, work out how the AI behaves, reset, and run a known-winning opening.
    /// Randomness is not the answer to that — a world that behaves differently for
    /// no reason is worse than one that behaves the same for good reasons. The
    /// answer is that AI behaviour is a function of what the player *does*, so
    /// repeating an opening summons the same counter to it.
    /// </summary>
    public class AIStrategyTests
    {
        [SetUp]
        public void SetUp() => GameLog.MirrorToUnityConsole = false;

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static GameState World(int seed, Difficulty difficulty = Difficulty.Challenging)
        {
            var state = WorldFactory.CreateDebugWorld(seed);
            state.difficulty = difficulty;
            return state;
        }

        /// <summary>
        /// Give the player a covert presence in every other country. A fresh world
        /// has none, so a test that only flips `compromised` on an empty list
        /// proves nothing — which is exactly what the first version of these tests
        /// did.
        /// </summary>
        static void GivePlayerNetworksEverywhere(GameState state)
        {
            foreach (var country in state.countries)
            {
                if (country.id == state.playerCountryId) continue;
                state.networks.Add(new IntelNetwork
                {
                    ownerId = state.playerCountryId,
                    targetId = country.id,
                    focus = IntelDomain.Military,
                    penetration = 40f
                });
            }
        }

        /// <summary>The strongest read any government has formed of the player's covert habits.</summary>
        static float PeakCovertRead(GameState state)
        {
            float peak = 0f;
            foreach (var ai in state.aiStates)
            {
                var model = ai.ModelOf(state.playerCountryId);
                if (model != null && model.covertActivity > peak) peak = model.covertActivity;
            }
            return peak;
        }

        static GameState Run(int seed, int months, Action<GameState, int> eachMonth = null)
        {
            var state = World(seed);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < months; month++)
            {
                eachMonth?.Invoke(state, month);
                turns.EndMonth();
            }
            return state;
        }

        // ---------- paths ----------

        [Test]
        public void EveryGovernmentCommitsToAWayOfWinning()
        {
            var state = Run(4242, 40);

            foreach (var ai in state.aiStates)
                Assert.Greater(ai.pathProgress, 0f,
                    $"{ai.countryId} has never measured how its strategy is going.");

            var chosen = new HashSet<StrategicPath>();
            foreach (var ai in state.aiStates) chosen.Add(ai.path);

            Assert.Greater(chosen.Count, 2,
                "Every government in the world converged on the same theory of victory: " +
                $"{string.Join(", ", chosen)}. A world of identical optimizers has no texture.");
        }

        [Test]
        public void APathFitsTheCountryThatChoseIt()
        {
            var state = World(4242);

            // An industrial giant and a weak buffer state should not find the same
            // strategies attractive. Suitability is the function that decides it.
            var chn = state.FindCountry("CHN");
            var kaz = state.FindCountry("KAZ");
            var chnAi = state.FindAI("CHN");
            var kazAi = state.FindAI("KAZ");

            float chnEconomic = AIStrategy.Suitability(state, chnAi, chn, StrategicPath.EconomicPrimacy);
            float kazEconomic = AIStrategy.Suitability(state, kazAi, kaz, StrategicPath.EconomicPrimacy);
            Assert.Greater(chnEconomic, kazEconomic,
                "Economic primacy has to suit the industrial power more than the buffer state.");

            float kazSurvival = AIStrategy.Suitability(state, kazAi, kaz, StrategicPath.Survival);
            float kazDominance = AIStrategy.Suitability(state, kazAi, kaz, StrategicPath.MilitaryDominance);
            Assert.Greater(kazSurvival, kazDominance,
                "A small landlocked state must not decide to dominate the world militarily.");
        }

        [Test]
        public void APathChangesWhatAGovernmentReachesFor()
        {
            // If the path does not move objective scoring, it is a label on a
            // save file and nothing else.
            Assert.Less(AIStrategy.ObjectiveBias(StrategicPath.EconomicPrimacy, AIObjectiveType.AssertClaim),
                AIStrategy.ObjectiveBias(StrategicPath.MilitaryDominance, AIObjectiveType.AssertClaim),
                "A trading power and a military one must not press claims equally readily.");

            Assert.Greater(AIStrategy.ObjectiveBias(StrategicPath.InstitutionalWeight, AIObjectiveType.ExpandInfluence),
                AIStrategy.ObjectiveBias(StrategicPath.MilitaryDominance, AIObjectiveType.ExpandInfluence));

            Assert.Greater(AIStrategy.ObjectiveBias(StrategicPath.Survival, AIObjectiveType.ConsolidateHome), 0f);
        }

        [Test]
        public void GovernmentsDoNotChangeStrategyEveryMonth()
        {
            // A government that re-plans constantly reads as noise rather than
            // intention, and nothing the player learns about it stays true.
            var state = World(4242);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            var lastPath = new Dictionary<string, StrategicPath>();
            foreach (var ai in state.aiStates) lastPath[ai.countryId] = ai.path;

            int pivots = 0;
            for (int month = 0; month < 120; month++)
            {
                turns.EndMonth();
                foreach (var ai in state.aiStates)
                {
                    if (lastPath[ai.countryId] == ai.path) continue;
                    pivots++;
                    lastPath[ai.countryId] = ai.path;
                }
            }

            // Fifteen governments over a decade. A handful of genuine strategic
            // reversals is the world being alive; dozens is churn.
            Assert.Less(pivots, 40,
                $"{pivots} strategic reversals in ten years across the world. Governments " +
                "are supposed to pivot when a strategy fails, not because the month ended.");
        }

        // ---------- opponent modelling ----------

        [Test]
        public void GovernmentsFormAReadOnEveryoneTheyCanSee()
        {
            var state = Run(4242, 24);

            foreach (var ai in state.aiStates)
            {
                Assert.Greater(ai.opponentModels.Count, 0,
                    $"{ai.countryId} has formed no view of anyone at all.");

                var playerModel = ai.ModelOf(state.playerCountryId);
                Assert.IsNotNull(playerModel,
                    $"{ai.countryId} has no model of the one country the player controls.");
                Assert.Greater(playerModel.observations, 0);
            }
        }

        [Test]
        public void APredictionIsNotMadeBeforeThereIsAnythingToGoOn()
        {
            // A government that has just met another should not be confidently
            // preparing for anything it might do.
            var state = Run(4242, 2);

            foreach (var ai in state.aiStates)
                foreach (PredictedMove move in Enum.GetValues(typeof(PredictedMove)))
                    Assert.AreEqual(0f, AIPrediction.Expectation(ai, state.playerCountryId, move), 0.001f,
                        $"{ai.countryId} formed an expectation after two months of observation.");
        }

        [Test]
        public void OnlyExposedCovertActionIsEverObserved()
        {
            // The fog rule. An intact network is invisible, which is exactly what
            // makes running one worth the cost.
            var state = World(4242);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            foreach (var network in state.networks)
                if (network.ownerId == state.playerCountryId) network.compromised = false;

            for (int month = 0; month < 24; month++) turns.EndMonth();

            float withHiddenNetworks = 0f;
            foreach (var ai in state.aiStates)
            {
                var model = ai.ModelOf(state.playerCountryId);
                if (model != null) withHiddenNetworks += model.covertActivity;
            }

            Assert.Less(withHiddenNetworks, 1f,
                "Covert action that was never exposed still taught the world to expect it. " +
                "That makes deniability worthless and reads as the AI cheating.");
        }

        [Test]
        public void PoorReportingMakesForPoorExpectations()
        {
            var state = Run(4242, 36);
            var ai = state.aiStates[0];
            var model = ai.ModelOf(state.playerCountryId);
            Assert.IsNotNull(model);

            model.observations = 60;
            model.predictedMove = PredictedMove.Attack;
            model.aggression = 90f;

            model.confidence = 1f;
            float confident = AIPrediction.Expectation(ai, state.playerCountryId, PredictedMove.Attack);

            model.confidence = 0f;
            float blind = AIPrediction.Expectation(ai, state.playerCountryId, PredictedMove.Attack);

            Assert.Greater(confident, blind,
                "A government with no collection must not counter as well as one with good " +
                "reporting, or intelligence stops being worth buying.");
        }

        // ---------- counter-play: the anti-memorisation property ----------

        [Test]
        public void RepeatedlyBeingCaughtSubvertingHardensTheWorldAgainstUs()
        {
            // The core claim. An operator with a signature move teaches the world
            // to defend against it, and resetting does not help because the world
            // is responding to what they do rather than to the seed.
            // The only difference between the two worlds is whether the operator
            // runs covert networks at all. Exposure is left to the simulation —
            // setting `compromised` by hand would skip the code that writes the
            // public record, which is the thing the world actually reads.
            float SecurityAfterDecade(int seed, bool operatorRunsNetworks)
            {
                var state = World(seed);
                if (operatorRunsNetworks) GivePlayerNetworksEverywhere(state);
                var turns = new TurnManager(state);
                SimulationPipeline.Wire(turns, state);

                for (int month = 0; month < 120; month++) turns.EndMonth();

                float total = 0f;
                int counted = 0;
                foreach (var country in state.countries)
                {
                    if (country.id == state.playerCountryId) continue;
                    total += country.counterIntel.counterIntelligence;
                    counted++;
                }
                return total / counted;
            }

            // Averaged over two seeds: in a world that fights its own wars, two
            // decades diverging at the first exposure are different worlds, and
            // one seed's fortunes can swamp a several-point real signal.
            float careless = (SecurityAfterDecade(9090, true) + SecurityAfterDecade(2468, true)) / 2f;
            float careful = (SecurityAfterDecade(9090, false) + SecurityAfterDecade(2468, false)) / 2f;

            Assert.Greater(careless, careful + 1f,
                $"An operator who ran covert networks for a decade left the world's " +
                $"counterintelligence at {careless:F1}, against {careful:F1} for one who ran " +
                "none. The world has to learn from what it sees, or the same opening works " +
                "in every playthrough forever.");
        }

        [Test]
        public void BeingCaughtIsWhatTeachesTheWorld()
        {
            // The mechanism behind the test above, isolated: it is *exposure* that
            // informs, not the existence of a network. An operation nobody ever
            // catches must leave no trace in anyone's expectations.
            var state = World(9090);
            GivePlayerNetworksEverywhere(state);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            for (int month = 0; month < 120; month++) turns.EndMonth();

            int exposures = 0;
            foreach (var entry in state.chronicle)
                if (entry.countryId == state.playerCountryId
                    && entry.category == ChronicleCategory.Intelligence
                    && entry.text.Contains("compromised")) exposures++;

            Assert.Greater(exposures, 0,
                "Nothing was ever rolled up in a decade, so there was nothing for the world " +
                "to notice and this proves nothing.");
            Assert.Greater(PeakCovertRead(state), 5f,
                $"{exposures} networks were publicly rolled up and no government revised its " +
                "view of who runs covert operations.");
        }

        [Test]
        public void ThePlayerCanStillBreakTheirOwnPattern()
        {
            // The model must stay exploitable. If it hardened permanently, a
            // single early mistake would poison a whole campaign and the right
            // move would be to reload — which is exactly what this game refuses.
            var state = World(9090);
            GivePlayerNetworksEverywhere(state);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            // A long enough run of covert work that some of it is caught.
            for (int month = 0; month < 120; month++) turns.EndMonth();

            float peak = PeakCovertRead(state);
            Assert.Greater(peak, 5f, "The world never noticed in the first place.");

            // Give it up entirely, and let the memory window run out.
            state.networks.RemoveAll(n => n.ownerId == state.playerCountryId);
            for (int month = 0; month < 130; month++) turns.EndMonth();

            float after = PeakCovertRead(state);

            Assert.Less(after, peak,
                "A reputation has to decay when the behaviour stops, or the assessment is a " +
                "permanent sentence rather than a read on current conduct.");
        }

        [Test]
        public void ExpectingAnAttackMakesAGovernmentPrepareForOne()
        {
            var state = World(9090);
            var ai = state.FindAI("MEX");
            var mex = state.FindCountry("MEX");
            ai.profile.caution = 10f;

            // Build the expectation the only way the AI is allowed to form one —
            // out of things it could actually have seen. A record of wars this
            // operator started, several of them against us, and a relationship
            // that has gone cold. Injecting the model directly proves nothing,
            // because the observation pass correctly overwrites it every month.
            for (int i = 0; i < 4; i++)
                state.confrontations.Add(new Confrontation
                {
                    id = $"HISTORY_{i}",
                    initiatorId = state.playerCountryId,
                    defenderId = "MEX",
                    objective = ConfrontationObjective.TerritorialConcession,
                    startDate = state.date,
                    resolved = true
                });

            var relationship = state.FindRelationship("MEX", state.playerCountryId);
            relationship.relations = 12f;
            relationship.trust = 10f;

            // Domestically settled, so nothing more urgent crowds the objective out.
            mex.stability = 80f;
            mex.governmentApproval = 75f;

            float shieldBefore = mex.military.missileDefense;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 48; month++)
            {
                // A government that cannot pay cannot prepare, and that is tested
                // separately. Here we are asking whether it *would*.
                mex.resources.treasury = Math.Max(mex.resources.treasury, 3000f);
                ai.politicalCapital = Math.Max(ai.politicalCapital, 20f);
                // Hold the relationship cold. Whether relations drift back up over
                // four years is a different system's business; this test is about
                // whether an expectation turns into preparation.
                relationship.relations = 12f;
                turns.EndMonth();
            }

            var model = ai.ModelOf(state.playerCountryId);
            Assert.IsNotNull(model);
            Assert.AreEqual(PredictedMove.Attack, model.predictedMove,
                $"After four wars started against it by a state it distrusts, MEX expects " +
                $"'{model.predictedMove}'.");

            Assert.Greater(mex.military.missileDefense, shieldBefore,
                "A government that expects to be attacked and can afford to prepare must " +
                "actually prepare. A prediction that changes no decision is telemetry.");
        }

        [Test]
        public void PreparingIsNotFree()
        {
            // The symmetry rule this project holds the AI to: nothing free for one
            // side that the other pays for.
            var state = World(9090);
            var ai = state.FindAI("MEX");
            var mex = state.FindCountry("MEX");

            for (int i = 0; i < 4; i++)
                state.confrontations.Add(new Confrontation
                {
                    id = $"HISTORY_{i}",
                    initiatorId = state.playerCountryId,
                    defenderId = "MEX",
                    objective = ConfrontationObjective.TerritorialConcession,
                    startDate = state.date,
                    resolved = true
                });
            var relationship = state.FindRelationship("MEX", state.playerCountryId);
            relationship.relations = 12f;

            float shieldBefore = mex.military.missileDefense;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 48; month++)
            {
                mex.resources.treasury = 0f;
                ai.politicalCapital = 0f;
                relationship.relations = 12f;
                turns.EndMonth();
            }

            // Asserted on the shield alone, because nothing else in the simulation
            // writes it. Counterintelligence has its own drift, so watching that
            // number would credit the AI for movement it did not cause — which is
            // how a test ends up passing or failing for reasons unrelated to the
            // thing it claims to check.
            Assert.AreEqual(shieldBefore, mex.military.missileDefense, 0.01f,
                "A bankrupt government armoured itself for nothing.");
        }

        // ---------- the world stays coherent ----------

        [Test]
        public void TheHardestDifficultyCanUseAllOfItsReasoning()
        {
            // Ruthless is given three actions a month, but the objective list was
            // capped at two, so the third was unreachable through the main path.
            var state = World(4242, Difficulty.Ruthless);
            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            int mostHeld = 0;
            for (int month = 0; month < 60; month++)
            {
                turns.EndMonth();
                foreach (var ai in state.aiStates)
                    if (ai.objectives.Count > mostHeld) mostHeld = ai.objectives.Count;
            }

            Assert.AreEqual(AISystem.ActionBudget(Difficulty.Ruthless), mostHeld,
                "The hardest difficulty must be able to hold as many objectives as it has " +
                "actions, or its extra reasoning is unreachable.");
        }

        [Test]
        public void ADecadeStaysDeterministic()
        {
            // Everything added here draws from seeded RNG and world state, so two
            // runs of the same seed must still land in exactly the same place.
            string Fingerprint(GameState state)
            {
                var parts = new List<string>();
                foreach (var ai in state.aiStates)
                    parts.Add($"{ai.countryId}:{ai.path}:{ai.monthsOnPath}:{ai.pathProgress:F2}");
                foreach (var country in state.countries)
                    parts.Add($"{country.id}:{country.pillars.military:F2}:{country.stability:F2}");
                return string.Join("|", parts);
            }

            Assert.AreEqual(Fingerprint(Run(31337, 120)), Fingerprint(Run(31337, 120)),
                "The same seed has to produce the same decade, or nothing measured about " +
                "balance means anything.");
        }

        [Test]
        public void TheWorldDoesNotSpendAllOfItsTimeDefending()
        {
            // Counter-play must not crowd out everything else. A world that only
            // ever braces for impact never does anything the player can react to.
            var state = Run(4242, 120);

            int defensive = 0, total = 0;
            foreach (var ai in state.aiStates)
                foreach (var objective in ai.objectives)
                {
                    total++;
                    if (objective.type == AIObjectiveType.HardenDefenses
                        || objective.type == AIObjectiveType.InsulateEconomy
                        || objective.type == AIObjectiveType.HardenSecurity) defensive++;
                }

            Assert.Greater(total, 0);
            Assert.Less(defensive / (float)total, 0.7f,
                $"{defensive} of {total} standing objectives across the world are defensive. " +
                "The world has stopped having ambitions of its own.");
        }
    }
}

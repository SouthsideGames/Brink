using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class EventCatalogTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4400);
            turns = new TurnManager(state);
            // NARROW PIPELINE: the long-running tests on this turn manager
            // (QuietWorld / TroubledWorld / Cooldown) are controlled
            // experiments that re-pin every value their eligibility functions
            // read at the top of each month, so the omitted systems — economy,
            // government, intelligence, AI — would only overwrite the
            // conditions being tested rather than change what is asserted.
            turns.ResolveMonth += CrisisSystem.SystemicCheck;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Catalog_IsWellFormed()
        {
            Assert.GreaterOrEqual(EventCatalog.Definitions.Count, 8,
                "The catalog needs enough breadth that a decade is not repetitive.");

            var ids = new HashSet<string>();
            foreach (var definition in EventCatalog.Definitions)
            {
                Assert.IsTrue(ids.Add(definition.id), $"Duplicate event id {definition.id}.");
                Assert.IsNotEmpty(definition.title);
                Assert.NotNull(definition.body);
                Assert.NotNull(definition.isEligible);
                Assert.NotNull(definition.options);
                Assert.Greater(definition.cooldownMonths, 0);

                var options = definition.options(state);
                Assert.GreaterOrEqual(options.Count, 2, $"{definition.id} needs a real choice.");
                foreach (var option in options)
                {
                    Assert.IsNotEmpty(option.label);
                    Assert.IsNotEmpty(option.description, "Every option needs a consequence hint.");
                    Assert.IsNotEmpty(option.resultText);
                }
            }
        }

        [Test]
        public void EveryDefinition_CanBuildAgainstAFreshWorld()
        {
            // Body text reads live state; none of it may throw on a clean save.
            foreach (var definition in EventCatalog.Definitions)
            {
                var crisis = CrisisSystem.Create(state, definition.id);
                Assert.IsNotEmpty(crisis.body, $"{definition.id} produced empty situation text.");
            }
        }

        [Test]
        public void Eligibility_RespondsToWorldState()
        {
            var energy = EventCatalog.Find("ENERGY_CRISIS");
            state.PlayerCountry.resources.energy = 90f;
            Assert.IsFalse(energy.isEligible(state), "A well-supplied state has no energy crisis.");

            state.PlayerCountry.resources.energy = 20f;
            Assert.IsTrue(energy.isEligible(state));
            Assert.Greater(energy.weight(state), 1f, "A deeper shortfall should be likelier.");

            var protests = EventCatalog.Find("INFLATION_PROTESTS");
            state.PlayerCountry.economy.inflation = 2f;
            state.PlayerCountry.governmentApproval = 80f;
            Assert.IsFalse(protests.isEligible(state));

            state.PlayerCountry.economy.inflation = 14f;
            state.PlayerCountry.governmentApproval = 40f;
            Assert.IsTrue(protests.isEligible(state));
        }

        [Test]
        public void WarWeariness_RequiresAnActualWar()
        {
            var weariness = EventCatalog.Find("WAR_WEARINESS");
            state.PlayerCountry.warExhaustion = 80f;
            Assert.IsFalse(weariness.isEligible(state), "Exhaustion without a war is not war weariness.");

            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, state.playerCountryId);

            Assert.IsTrue(weariness.isEligible(state));
        }

        [Test]
        public void IntelligenceScandal_RequiresACompromisedNetwork()
        {
            var scandal = EventCatalog.Find("INTELLIGENCE_SCANDAL");
            Assert.IsFalse(scandal.isEligible(state));

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = "CHN",
                focus = IntelDomain.Military,
                penetration = 40f,
                compromised = true
            });

            Assert.IsTrue(scandal.isEligible(state), "A rolled-up network is exactly the trigger.");
        }

        [Test]
        public void CabinetDissent_NamesTheActualOfficial()
        {
            var official = state.FindOfficial(Pillar.Diplomacy);
            official.trust = 10f;
            foreach (var other in state.cabinet)
                if (other != official) other.trust = 90f;

            var definition = EventCatalog.Find("CABINET_DISSENT");
            Assert.IsTrue(definition.isEligible(state));

            var crisis = CrisisSystem.Create(state, "CABINET_DISSENT");
            StringAssert.Contains(official.displayName, crisis.body,
                "The situation should name the official it is actually about.");
        }

        [Test]
        public void Cooldown_PreventsImmediateRepeats()
        {
            CrisisSystem.Trigger(state, "FOOD_SHORTAGE");
            CrisisSystem.Resolve(state, state.activeCrises[0], 0);

            Assert.AreEqual(1, state.eventCooldowns.Count);
            Assert.AreEqual("FOOD_SHORTAGE", state.eventCooldowns[0].defId);

            // Force the situation to be maximally eligible, then run a stretch of
            // months: the same event must not recur while it is resting.
            state.PlayerCountry.resources.foodSecurity = 5f;
            for (int i = 0; i < 12; i++)
            {
                while (state.HasOpenCrisis)
                {
                    Assert.AreNotEqual("FOOD_SHORTAGE", state.activeCrises[0].defId,
                        "A resting event must not fire again during its cooldown.");
                    CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                }
                turns.EndMonth();
            }
        }

        [Test]
        public void QuietWorld_ProducesNoCrises()
        {
            // A quiet world, not merely a quiet player: several events key off
            // foreign instability, which is correct — trouble abroad reaches us.
            //
            // **Every dimension an eligibility function reads has to be settled,
            // or the invariant is only proven against whichever ones happened to
            // be listed.** This helper used to pin nine values and the catalog
            // reads twenty-eight. The gap was not theoretical: SUCCESSION_QUESTION
            // (an ageing leader with no successor) and DEFECTION (a foreign
            // service running a network against us) were both live in a world this
            // helper called settled — and both are real problems, so a world with
            // them in it was never quiet to begin with.
            //
            // Derived from the catalog rather than guessed: grep `isEligible` for
            // everything it touches. If you add an event that reads a new field,
            // add it here too, or this test quietly stops covering it.
            void SettleTheWorld()
            {
                foreach (var country in state.countries)
                {
                    country.resources.foodSecurity = 95f;
                    country.resources.energy = 95f;
                    country.resources.industrialCapacity = 80f;
                    country.economy.inflation = 2f;
                    country.economy.confidence = 85f;
                    country.economy.debtToGdp = 30f;
                    country.governmentApproval = 85f;
                    country.stability = 85f;
                    country.warExhaustion = 0f;
                    foreach (var sector in country.economy.sectors) sector.health = 95f;

                    // The social layer. Unrest, grievance and living standards are
                    // read by four events and were entirely unpinned.
                    country.livingStandards = 70f;
                    country.socialUnrest = 5f;
                    country.publicGrievance = 0f;
                    country.nationalUnity = 80f;

                    // A settled succession. An ageing leader with no heir is a
                    // problem the world has, not one it invents.
                    country.government.leader.age = 52f;
                    country.government.successorReadiness = 80f;
                    country.government.coupsExperienced = 0;

                    // Nobody is mobilising.
                    country.military.alertPosture = false;
                }

                // No foreign service is running against us. Ordinary espionage is
                // not "manufactured adversity", but it is adversity, and a world
                // where it is happening is not the world this test describes.
                for (int n = state.networks.Count - 1; n >= 0; n--)
                    if (state.networks[n].targetId == state.playerCountryId)
                        state.networks.RemoveAt(n);

                foreach (var official in state.cabinet) official.trust = 90f;
                foreach (var relationship in state.relationships) relationship.relations = 85f;
            }

            SettleTheWorld();

            for (int i = 0; i < 60; i++)
            {
                SettleTheWorld(); // hold the calm against drift
                turns.EndMonth();

                // Opportunities are allowed — a settled, prosperous, capable
                // country still makes discoveries, and it makes them *because*
                // things are going well. What a calm world must not do is invent
                // adversity, which is the thing this invariant exists to catch.
                if (!state.HasOpenCrisis) continue;

                var open = state.activeCrises[0];
                var definition = EventCatalog.Find(open.defId);
                Assert.AreEqual(EventNature.Opportunity, definition?.nature ?? EventNature.Adversity,
                    $"A genuinely stable world manufactured '{open.title}'.");

                CrisisSystem.Resolve(state, open, 0);   // clear it and keep measuring
            }
        }

        [Test]
        public void TroubledWorld_ProducesCrises()
        {
            var player = state.PlayerCountry;
            player.resources.foodSecurity = 30f;
            player.resources.energy = 25f;
            player.economy.inflation = 15f;
            player.economy.confidence = 30f;
            player.governmentApproval = 35f;

            int fired = 0;
            for (int i = 0; i < 120; i++)
            {
                player.resources.foodSecurity = 30f;
                player.resources.energy = 25f;
                player.economy.inflation = 15f;
                player.economy.confidence = 30f;
                player.governmentApproval = 35f;

                while (state.HasOpenCrisis)
                {
                    fired++;
                    CrisisSystem.Resolve(state, state.activeCrises[0], 0);
                }
                turns.EndMonth();
            }

            Assert.Greater(fired, 2, "A failing state should generate real crises.");
        }

        [Test]
        public void Selection_IsDeterministicPerSeed()
        {
            List<string> Run(int seed)
            {
                var sim = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(sim);
                // Must use the real pipeline: unlike the controlled tests above
                // this one lets the world run for 200 months, and selection is
                // only meaningfully deterministic if it is deterministic against
                // the same evolving world the player gets.
                SimulationPipeline.Wire(simTurns, sim);

                var fired = new List<string>();
                for (int i = 0; i < 200; i++)
                {
                    while (sim.HasOpenCrisis)
                    {
                        fired.Add($"{sim.date.SortKey}:{sim.activeCrises[0].defId}");
                        CrisisSystem.Resolve(sim, sim.activeCrises[0], 0);
                    }
                    simTurns.EndMonth();
                }
                return fired;
            }

            Assert.AreEqual(Run(777), Run(777));
        }

        [Test]
        public void Cooldowns_SurviveSaveRoundTrip()
        {
            CrisisSystem.Trigger(state, "MARKET_PANIC");
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(state.eventCooldowns.Count, loaded.eventCooldowns.Count);
            Assert.AreEqual("MARKET_PANIC", loaded.eventCooldowns[0].defId);
            Assert.AreEqual(state.eventCooldowns[0].lastFiredMonth, loaded.eventCooldowns[0].lastFiredMonth);
        }
    }
}

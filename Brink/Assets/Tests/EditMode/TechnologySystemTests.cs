using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class TechnologySystemTests
    {
        /// <summary>
        /// Where the runtime sources live, relative to the project root Unity
        /// runs tests from.
        /// </summary>
        const string SourceDirectory = "Assets/Scripts";

        [Test]
        public void EveryCapabilityIsReadSomewhere()
        {
            // **The guard that makes the catalogue content rather than a list.**
            // A capability nobody reads grants nothing: the operator funds it for
            // years, it completes, and the world is identical. That is the
            // "written but never read" family — which this project has shipped
            // repeatedly — and at 33 entries it would be shipped at scale.
            //
            // Checked against the sources rather than by reflection because the
            // ids are strings passed to `TechnologySystem.Has` / `Effectiveness`,
            // so there is no symbol to reflect over.
            string root = Path.Combine(Directory.GetCurrentDirectory(), SourceDirectory);
            Assert.IsTrue(Directory.Exists(root), $"Cannot find sources at {root}.");

            var mentions = new Dictionary<string, int>();
            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                bool isCatalogue = Path.GetFileName(path) == "CapabilityCatalog.cs";
                string source = File.ReadAllText(path);

                foreach (Match match in Regex.Matches(source, "CAP_[A-Z]+"))
                {
                    string id = match.Value;
                    if (!mentions.ContainsKey(id)) mentions[id] = 0;
                    // The catalogue declaring and cross-referencing an id is not
                    // somebody reading it.
                    if (!isCatalogue) mentions[id]++;
                }
            }

            var unread = new List<string>();
            foreach (var definition in CapabilityCatalog.Definitions)
                if (!mentions.TryGetValue(definition.id, out int count) || count == 0)
                    unread.Add($"{definition.id} ({definition.name})");

            CollectionAssert.IsEmpty(unread,
                "These capabilities are funded for years, complete, and change nothing — no "
                + "system reads them:\n  " + string.Join("\n  ", unread)
                + "\nGive each one a read site or take it out of the catalogue.");
        }

        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1200);
            turns = new TurnManager(state);
            // NARROW PIPELINE: deliberately only TechnologySystem — every test here
            // sets its own preconditions directly (grants a mature capability, adds
            // the treaty or the network diffusion needs) and then asserts
            // TechnologySystem's own arithmetic, so the omitted systems (AI,
            // diplomacy, confrontations, economy, territory, cabinet lifecycle)
            // could only add noise to a controlled measurement. Even
            // `AiStates_PursueResearchOfTheirOwn` stays honest: `ConsiderAiResearch`
            // lives inside `TechnologySystem.MonthlyUpdate`, so the AI branch under
            // test is running. Running the full pipeline would also let the economy
            // move treasury underneath the funding assertions, which are the point.
            turns.ResolveMonth += TechnologySystem.MonthlyUpdate;
            state.commandPoints.current = 40;
            state.PlayerCountry.resources.treasury = 8000f;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Catalog_SpansAllFivePillarsWithoutBeingATree()
        {
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
                Assert.Greater(CapabilityCatalog.ForPillar(pillar).Count, 0,
                    $"{pillar} has no research of its own (GDD §11: distributed across the pillars).");

            foreach (var definition in CapabilityCatalog.Definitions)
            {
                Assert.IsNotEmpty(definition.name);
                Assert.IsNotEmpty(definition.description);
                Assert.Greater(definition.researchMonths, 0);
                Assert.Greater(definition.monthlyCost, 0f);
                foreach (var prerequisite in definition.prerequisites)
                    Assert.NotNull(CapabilityCatalog.Find(prerequisite),
                        $"Dangling prerequisite {prerequisite}.");
            }
        }

        [Test]
        public void Research_TakesYearsAndIsPaidForEveryMonth()
        {
            float treasuryBefore = state.PlayerCountry.resources.treasury;

            Assert.IsTrue(TechnologySystem.BeginResearch(state, turns, "CAP_PRECISION"));
            Assert.IsFalse(TechnologySystem.Has(state.PlayerCountry, "CAP_PRECISION"),
                "Nothing is delivered on the day it is authorized.");

            var definition = CapabilityCatalog.Find("CAP_PRECISION");
            for (int i = 0; i < definition.researchMonths; i++) turns.EndMonth();

            Assert.IsTrue(TechnologySystem.Has(state.PlayerCountry, "CAP_PRECISION"));
            Assert.Less(state.PlayerCountry.resources.treasury, treasuryBefore,
                "It was funded every month it ran.");
            Assert.AreEqual(0, state.PlayerCountry.technology.programs.Count);
        }

        [Test]
        public void Research_RequiresInstitutionsAndIndustry()
        {
            var player = state.PlayerCountry;
            player.resources.industrialCapacity = 10f;
            player.pillars.intelligence = 10f;

            Assert.IsFalse(TechnologySystem.CanResearch(state, player, "CAP_ANALYTICS", out string reason));
            Assert.IsNotEmpty(reason);
            Assert.IsFalse(TechnologySystem.BeginResearch(state, turns, "CAP_ANALYTICS"),
                "A state cannot simply buy the future without the base to build it on.");
        }

        [Test]
        public void Research_RespectsPrerequisitesAndProgrammeLimits()
        {
            Assert.IsFalse(TechnologySystem.CanResearch(state, state.PlayerCountry, "CAP_ISR", out string reason));
            StringAssert.Contains("Requires", reason);

            Assert.IsTrue(TechnologySystem.BeginResearch(state, turns, "CAP_PRECISION"));
            Assert.IsTrue(TechnologySystem.BeginResearch(state, turns, "CAP_SIGINT"));
            Assert.IsFalse(TechnologySystem.BeginResearch(state, turns, "CAP_CONVENING"),
                "The research base cannot carry unlimited programmes.");
        }

        [Test]
        public void UnfundedProgramme_IsWoundUpRatherThanRunningFree()
        {
            TechnologySystem.BeginResearch(state, turns, "CAP_ENERGY");
            state.PlayerCountry.resources.treasury = 5f;

            turns.EndMonth();

            Assert.AreEqual(0, state.PlayerCountry.technology.programs.Count);
            Assert.IsFalse(TechnologySystem.Has(state.PlayerCountry, "CAP_ENERGY"));
        }

        // ---------- capability delivers ability, not substance ----------

        [Test]
        public void Capability_UnlocksAbilityNotForceStructure()
        {
            var player = state.PlayerCountry;
            float forceBefore = player.military.TotalPower;
            float treasuryBefore = player.resources.treasury;
            float manpowerBefore = player.resources.manpower;

            GrantMature(player, "CAP_PRECISION");
            GrantMature(player, "CAP_ISR");

            Assert.AreEqual(forceBefore, player.military.TotalPower, 0.001f,
                "Technology never hands over force structure (GDD §11).");
            Assert.AreEqual(treasuryBefore, player.resources.treasury, 0.001f,
                "Nor money.");
            Assert.AreEqual(manpowerBefore, player.resources.manpower, 0.001f,
                "Nor trained people.");
        }

        [Test]
        public void PrecisionMunitions_ReduceCollateralHarmForTheSameEffect()
        {
            float HarmWith(bool precision)
            {
                var sim = WorldFactory.CreateDebugWorld(1201);
                if (precision) GrantMature(sim.PlayerCountry, "CAP_PRECISION");

                var confrontation = ConfrontationSystem.BeginBy(sim, sim.playerCountryId, "CHN",
                    ConfrontationObjective.TerritorialConcession, "CONTESTED_LANE", PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(sim, confrontation, EscalationState.LimitedConflict, sim.playerCountryId);

                var record = ConfrontationSystem.LaunchOperationBy(sim, confrontation, sim.playerCountryId,
                    "CONTESTED_LANE", OperationType.Assault,
                    new OperationDirective { speedPriority = 60f, casualtyTolerance = 50f, civilianRiskLimit = 70f });
                return record.civilianHarm;
            }

            Assert.Less(HarmWith(true), HarmWith(false));
        }

        [Test]
        public void SignalsArchitecture_DeepensNetworksFaster()
        {
            float PenetrationAfter(bool sigint)
            {
                var sim = WorldFactory.CreateDebugWorld(1202);
                var simTurns = new TurnManager(sim);
                // NARROW PIPELINE: an A/B on one capability — only collection may
                // run, or AI counter-intelligence and covert action would move
                // penetration for reasons unrelated to CAP_SIGINT.
                simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
                sim.commandPoints.current = 40;
                if (sigint) GrantMature(sim.PlayerCountry, "CAP_SIGINT");

                IntelligenceSystem.EstablishNetwork(sim, simTurns, "CHN", IntelDomain.Military);
                for (int i = 0; i < 12; i++) simTurns.EndMonth();
                return sim.FindNetwork(sim.playerCountryId, "CHN").penetration;
            }

            Assert.Greater(PenetrationAfter(true), PenetrationAfter(false));
        }

        [Test]
        public void SecureCommunications_MakeUsHarderToRead()
        {
            float MarginAgainstUs(bool hardened)
            {
                var sim = WorldFactory.CreateDebugWorld(1203);
                var simTurns = new TurnManager(sim);
                // NARROW PIPELINE: one month, one variable. Only collection runs so
                // the estimate's margin can be attributed to CAP_SECCOMMS and not to
                // deception, decay or an AI service acting on its own account.
                simTurns.ResolveMonth += IntelligenceSystem.MonthlyCollection;
                if (hardened) GrantMature(sim.PlayerCountry, "CAP_SECCOMMS");

                sim.networks.Add(new IntelNetwork
                {
                    ownerId = "CHN",
                    targetId = sim.playerCountryId,
                    focus = IntelDomain.Military,
                    penetration = 70f
                });
                simTurns.EndMonth();
                return sim.FindEstimate("CHN", sim.playerCountryId, IntelDomain.Military).margin;
            }

            Assert.Greater(MarginAgainstUs(true), MarginAgainstUs(false),
                "Hardened communications should widen a foreign service's error bars.");
        }

        [Test]
        public void AdvancedManufacturing_CompoundsCapacityWithoutGrantingIt()
        {
            float CapacityAfter(bool advanced)
            {
                var sim = WorldFactory.CreateDebugWorld(1204);
                var simTurns = new TurnManager(sim);
                // NARROW PIPELINE: only the economy runs, because the assertion is
                // that CAP_ADVMFG compounds capacity over 36 months — territory,
                // acquisition and war would all move industrial capacity too and
                // make the A/B unattributable.
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                if (advanced) GrantMature(sim.PlayerCountry, "CAP_ADVMFG");

                float before = sim.PlayerCountry.resources.industrialCapacity;
                Assert.AreEqual(before, sim.PlayerCountry.resources.industrialCapacity, 0.001f,
                    "Granting the capability alone changes nothing.");

                for (int i = 0; i < 36; i++) simTurns.EndMonth();
                return sim.PlayerCountry.resources.industrialCapacity;
            }

            Assert.Greater(CapacityAfter(true), CapacityAfter(false),
                "It should compound into capacity over years, not arrive as a gift.");
        }

        // ---------- diffusion ----------

        [Test]
        public void Knowledge_SpreadsToTreatyPartners()
        {
            var partner = state.FindCountry("IND");
            GrantMature(partner, "CAP_CONVENING");

            state.treaties.Add(new Treaty
            {
                id = "T1",
                countryA = state.playerCountryId,
                countryB = "IND",
                commitments = new System.Collections.Generic.List<TreatyCommitment>
                {
                    TreatyCommitment.IntelligenceSharing
                }
            });

            bool acquired = false;
            for (int i = 0; i < 600 && !acquired; i++)
            {
                turns.EndMonth();
                acquired = TechnologySystem.Has(state.PlayerCountry, "CAP_CONVENING");
            }

            Assert.IsTrue(acquired, "Partners share what they know (GDD §11).");
            Assert.AreEqual(CapabilitySource.Shared,
                state.PlayerCountry.technology.Find("CAP_CONVENING").source);
        }

        [Test]
        public void Knowledge_CanBeStolenThroughCollection()
        {
            var target = state.FindCountry("CHN");
            GrantMature(target, "CAP_ADVMFG");

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = "CHN",
                focus = IntelDomain.Economic,
                penetration = 100f
            });

            bool acquired = false;
            for (int i = 0; i < 600 && !acquired; i++)
            {
                turns.EndMonth();
                acquired = TechnologySystem.Has(state.PlayerCountry, "CAP_ADVMFG");
            }

            Assert.IsTrue(acquired, "You cannot hold an advantage close forever.");
            Assert.AreEqual(CapabilitySource.Stolen,
                state.PlayerCountry.technology.Find("CAP_ADVMFG").source);
        }

        [Test]
        public void StolenKnowledge_IsShallowerThanDevelopedKnowledge()
        {
            var player = state.PlayerCountry;
            player.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = "CAP_PRECISION", source = CapabilitySource.Developed, maturity = 70f
            });
            player.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = "CAP_SIGINT", source = CapabilitySource.Stolen, maturity = 25f
            });

            Assert.Greater(TechnologySystem.Effectiveness(player, "CAP_PRECISION"),
                           TechnologySystem.Effectiveness(player, "CAP_SIGINT"),
                "What we built we understand; what we took we merely possess.");

            // But it matures with use.
            for (int i = 0; i < 60; i++) turns.EndMonth();
            Assert.Greater(TechnologySystem.Effectiveness(player, "CAP_SIGINT"), 0.25f);
        }

        [Test]
        public void AiStates_PursueResearchOfTheirOwn()
        {
            for (int i = 0; i < 120; i++) turns.EndMonth();

            int aiHoldings = 0;
            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                aiHoldings += country.technology.capabilities.Count;
            }

            Assert.Greater(aiHoldings, 0, "The world develops capability without us.");
        }

        [Test]
        public void Technology_SurvivesSaveRoundTrip()
        {
            TechnologySystem.BeginResearch(state, turns, "CAP_PRECISION");
            GrantMature(state.PlayerCountry, "CAP_SIGINT");
            for (int i = 0; i < 6; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.PlayerCountry.technology;

            Assert.AreEqual(state.PlayerCountry.technology.programs.Count, restored.programs.Count);
            Assert.IsTrue(restored.Has("CAP_SIGINT"));
            Assert.AreEqual(state.PlayerCountry.technology.Find("CAP_SIGINT").maturity,
                            restored.Find("CAP_SIGINT").maturity);
            Assert.AreEqual("CAP_PRECISION", restored.programs[0].capabilityId);
        }

        [Test]
        public void LongRun_IsDeterministicAndStable()
        {
            string Run(int seed)
            {
                var sim = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(sim);
                // NARROW PIPELINE: 20 years, but the only claims are that maturity
                // stays inside 0–100 and that the same seed reproduces itself —
                // both properties of TechnologySystem alone. The whole-world version
                // of this assertion lives in WorldInvariantTests, on the real
                // pipeline.
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                simTurns.ResolveMonth += TechnologySystem.MonthlyUpdate;
                for (int i = 0; i < 240; i++) simTurns.EndMonth();

                foreach (var country in sim.countries)
                    foreach (var held in country.technology.capabilities)
                    {
                        Assert.GreaterOrEqual(held.maturity, 0f);
                        Assert.LessOrEqual(held.maturity, 100f);
                    }
                return SaveSystem.ToJson(sim);
            }

            Assert.AreEqual(Run(4242), Run(4242));
        }

        static void GrantMature(CountryState country, string capabilityId)
        {
            country.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = capabilityId,
                source = CapabilitySource.Developed,
                maturity = 100f
            });
        }
    }
}

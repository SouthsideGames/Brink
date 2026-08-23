using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class RegimeSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6600);
            turns = new TurnManager(state);
            // NARROW PIPELINE: the regime tick alone, here and in the per-test
            // worlds below, and the omission is the experiment rather than an
            // oversight — every case re-pins stability, approval, unity,
            // military loyalty, cohesion and the economy every single month to
            // hold conditions fixed, so any other system moving those inputs
            // would destroy the control that the coup-rate, cannot-topple-a-
            // stable-state and thirty-years-without-collapse claims are
            // measured against, and RegimeSystem.MonthlyUpdate is the only
            // producer of coups, so those negative claims are not vacuous (the
            // successful-coup cases in this file fire under the same wiring).
            turns.ResolveMonth += RegimeSystem.MonthlyUpdate;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>Drive a country into the conditions that breed a conspiracy.</summary>
        static void MakeFailingState(CountryState country)
        {
            country.stability = 15f;
            country.governmentApproval = 15f;
            country.nationalUnity = 20f;
            country.warExhaustion = 70f;
            country.economy.inflation = 25f;
            country.economy.unemployment = 25f;
            country.government.militaryLoyalty = 20f;
            country.government.eliteCohesion = 15f;
            country.government.legislativeSupport = 15f;
        }

        static void MakeHealthyState(CountryState country)
        {
            country.stability = 85f;
            country.governmentApproval = 75f;
            country.nationalUnity = 80f;
            country.warExhaustion = 0f;
            country.economy.inflation = 2f;
            country.economy.unemployment = 5f;
            country.government.militaryLoyalty = 90f;
            country.government.eliteCohesion = 85f;
            country.government.legislativeSupport = 85f;
        }

        /// <summary>
        /// Re-apply the fixture's conditions every month and advance.
        ///
        /// **The `setup(country)` call must stay *before* `EndMonth`.** Several of
        /// these fixtures inject values that a fuller pipeline would erase within
        /// a month or two — `MakeFailingState` sets `inflation = 25` and
        /// `unemployment = 25`, which `EconomySystem` approaches back toward
        /// ~2 and ~7 at 30% and 25% a month. Re-pinning first is what makes the
        /// conditions actually hold while the regime tick reads them; flipping the
        /// order would leave the tests measuring a recovery and calling it a
        /// failing state.
        ///
        /// This is also why the narrow wiring above is load-bearing rather than
        /// merely economical. If `EconomySystem` or `GovernmentSystem` is ever
        /// added to this fixture, check that every value these setups inject is
        /// still at its intended level at the point the assertion runs.
        /// </summary>
        void HoldConditions(CountryState country, System.Action<CountryState> setup, int months)
        {
            for (int i = 0; i < months; i++)
            {
                setup(country);
                turns.EndMonth();
            }
        }

        // ---------- conspiracy accumulates from conditions ----------

        [Test]
        public void Conspiracy_GrowsInAFailingStateAndDecaysInAHealthyOne()
        {
            var player = state.PlayerCountry;

            // Measured against a healthy control rather than an absolute number,
            // so this tests the design claim and not one RNG realization.
            var control = WorldFactory.CreateDebugWorld(seed: 6600);
            var controlTurns = new TurnManager(control);
            controlTurns.ResolveMonth += RegimeSystem.MonthlyUpdate;
            var healthy = control.PlayerCountry;
            for (int i = 0; i < 24; i++)
            {
                MakeHealthyState(healthy);
                control.commandPoints.current = 6;
                controlTurns.EndMonth();
            }

            HoldConditions(player, MakeFailingState, 24);
            float underFailure = player.government.conspiracyLevel;

            Assert.Greater(underFailure, healthy.government.conspiracyLevel + 10f,
                "Sustained misgovernment should breed conspiracy that competent "
                + "government does not.");
            Assert.Greater(underFailure, 15f, "Sustained misgovernment should breed conspiracy.");

            player.government.conspiracyLevel = 50f;
            HoldConditions(player, MakeHealthyState, 24);

            Assert.Less(player.government.conspiracyLevel, 50f,
                "Governing well should starve a conspiracy of oxygen.");
        }

        [Test]
        public void MilitaryLoyalty_TracksTheStateOfTheState()
        {
            var player = state.PlayerCountry;
            player.government.militaryLoyalty = 50f;

            HoldConditions(player, MakeHealthyState, 24);
            float loyal = player.government.militaryLoyalty;

            var other = WorldFactory.CreateDebugWorld(6600);
            var otherTurns = new TurnManager(other);
            otherTurns.ResolveMonth += RegimeSystem.MonthlyUpdate;
            var failing = other.PlayerCountry;
            failing.government.militaryLoyalty = 50f;
            for (int i = 0; i < 24; i++)
            {
                MakeFailingState(failing);
                failing.government.militaryLoyalty = System.Math.Min(failing.government.militaryLoyalty, 50f);
                otherTurns.EndMonth();
            }

            Assert.Greater(loyal, 50f, "A functioning state keeps its officer corps.");
            Assert.Less(failing.government.militaryLoyalty, 50f);
        }

        [Test]
        public void NoCoupIsPossibleBelowTheConspiracyThreshold()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            string incumbent = gov.leader.name;
            gov.conspiracyLevel = RegimeSystem.CoupThreshold - 1f;
            gov.militaryLoyalty = 5f; // maximally disloyal, but nobody is organized

            for (int i = 0; i < 120; i++)
            {
                gov.conspiracyLevel = RegimeSystem.CoupThreshold - 1f;
                gov.militaryLoyalty = 5f;
                turns.EndMonth();
            }

            Assert.AreEqual(0, gov.coupsExperienced, "Disloyalty without organization is not a coup.");
            Assert.AreEqual(incumbent, gov.leader.name);
        }

        // ---------- foreign action accelerates but cannot manufacture ----------

        [Test]
        public void ForeignSubversion_CannotTopplaAStableState()
        {
            var target = state.FindCountry("IND");
            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = "IND",
                focus = IntelDomain.Political,
                penetration = 100f
            });

            for (int i = 0; i < 240; i++) // twenty years of maximum subversion
            {
                MakeHealthyState(target);
                turns.EndMonth();
            }

            Assert.AreEqual(0, target.government.coupsExperienced,
                "A legitimate, stable state cannot be toppled by clicking at it (GDD §22).");
            Assert.Less(target.government.conspiracyLevel, RegimeSystem.CoupThreshold);
        }

        [Test]
        public void ForeignSubversion_AcceleratesAnExistingVulnerability()
        {
            float ConspiracyAfter(bool subverted)
            {
                var sim = WorldFactory.CreateDebugWorld(6601);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += RegimeSystem.MonthlyUpdate;
                var target = sim.FindCountry("IND");

                if (subverted)
                    sim.networks.Add(new IntelNetwork
                    {
                        ownerId = sim.playerCountryId,
                        targetId = "IND",
                        focus = IntelDomain.Political,
                        penetration = 100f
                    });

                for (int i = 0; i < 18; i++)
                {
                    MakeFailingState(target);
                    target.government.conspiracyLevel =
                        System.Math.Min(target.government.conspiracyLevel, 55f); // stay below the threshold
                    simTurns.EndMonth();
                }
                return target.government.conspiracyLevel;
            }

            Assert.Greater(ConspiracyAfter(true), ConspiracyAfter(false),
                "Backing plotters in an already-fracturing state should speed things up.");
        }

        [Test]
        public void CovertPoliticalInfluence_FeedsConspiracyProportionallyToGrievance()
        {
            float ConspiracyGain(float targetStability)
            {
                var sim = WorldFactory.CreateDebugWorld(6602);
                var simTurns = new TurnManager(sim);
                sim.commandPoints.current = 40;
                IntelligenceSystem.EstablishNetwork(sim, simTurns, "IND", IntelDomain.Political);
                sim.FindNetwork(sim.playerCountryId, "IND").penetration = 100f;

                var target = sim.FindCountry("IND");
                target.counterIntel.counterIntelligence = 0f;
                target.stability = targetStability;
                float before = target.government.conspiracyLevel;

                IntelligenceSystem.RunCovertOperation(sim, simTurns, "IND", CovertOperation.PoliticalInfluence);
                return target.government.conspiracyLevel - before;
            }

            Assert.Greater(ConspiracyGain(10f), ConspiracyGain(90f),
                "Support to opposition works where there is grievance, not where there is not.");
        }

        // ---------- outcomes ----------

        [Test]
        public void SuccessfulCoup_InstallsANewOrderWithoutEndingTheGame()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            string incumbent = gov.leader.name;
            state.strategistXP = 900;
            state.skillPoints = 4;

            // Overwhelming conspiracy against a hollow state.
            for (int i = 0; i < 240 && gov.coupsExperienced == 0; i++)
            {
                MakeFailingState(player);
                gov.conspiracyLevel = 95f;
                gov.militaryLoyalty = 5f;
                turns.EndMonth();
            }

            Assert.AreEqual(1, gov.coupsExperienced, "This state should have fallen.");
            Assert.AreNotEqual(incumbent, gov.leader.name);
            Assert.AreEqual("MILITARY COUNCIL", gov.leader.faction);
            Assert.AreEqual(GovernmentType.CentralizedRepublic, gov.type,
                "Power centralizes after a seizure.");
            Assert.IsFalse(gov.IsElective);
            Assert.Less(gov.conspiracyLevel, RegimeSystem.CoupThreshold, "The plot has spent itself.");

            // The save continues and the operator keeps their post and progression.
            Assert.AreEqual(900, state.strategistXP);
            Assert.AreEqual(4, state.skillPoints);
            Assert.AreEqual(2, state.administrationsServed);
            Assert.NotNull(state.PlayerCountry);
            Assert.AreEqual(5, state.cabinet.Count);
        }

        [Test]
        public void SuccessfulCoup_RepudiatesDefenseCommitmentsAndCostsTrust()
        {
            var player = state.PlayerCountry;
            var gov = player.government;

            state.treaties.Add(new Treaty
            {
                id = "T1",
                countryA = state.playerCountryId,
                countryB = "IND",
                commitments = new System.Collections.Generic.List<TreatyCommitment>
                {
                    TreatyCommitment.MutualDefense
                }
            });
            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            relationship.trust = 80f;

            for (int i = 0; i < 240 && gov.coupsExperienced == 0; i++)
            {
                MakeFailingState(player);
                gov.conspiracyLevel = 95f;
                gov.militaryLoyalty = 5f;
                turns.EndMonth();
            }

            Assert.AreEqual(1, gov.coupsExperienced);
            Assert.IsNull(state.FindTreaty(state.playerCountryId, "IND"),
                "A government that no longer exists cannot guarantee anyone's defense.");
            Assert.Less(relationship.trust, 80f, "The world discounts a state that changes hands by force.");
        }

        [Test]
        public void ForeignBackedCoup_ProducesASovereignGovernmentNotAPuppet()
        {
            var target = state.FindCountry("IND");
            var gov = target.government;
            gov.conspiracyBackerId = state.playerCountryId;

            var toBacker = state.FindRelationship("IND", state.playerCountryId);
            float alignmentBefore = toBacker.strategicAlignment;

            for (int i = 0; i < 240 && gov.coupsExperienced == 0; i++)
            {
                MakeFailingState(target);
                gov.conspiracyLevel = 95f;
                gov.militaryLoyalty = 5f;
                gov.conspiracyBackerId = state.playerCountryId;
                turns.EndMonth();
            }

            Assert.AreEqual(1, gov.coupsExperienced);
            Assert.AreEqual(alignmentBefore, toBacker.strategicAlignment, 0.001f,
                "A sponsored successor is sovereign — it does not inherit our interests (GDD §22).");
            Assert.IsNotEmpty(gov.leader.name);
            Assert.NotNull(state.FindAI("IND"), "It keeps making its own decisions.");
        }

        [Test]
        public void FailedCoup_PurgesTheOfficerCorps()
        {
            var player = state.PlayerCountry;
            var gov = player.government;

            // High conspiracy against a state that can still defend itself.
            float militaryBefore = player.pillars.military;
            bool purged = false;

            for (int i = 0; i < 240 && !purged; i++)
            {
                gov.conspiracyLevel = 95f;
                gov.militaryLoyalty = 95f;
                player.stability = 90f;
                player.pillars.government = 95f;
                turns.EndMonth();
                purged = player.pillars.military < militaryBefore - 0.001f;
            }

            Assert.IsTrue(purged, "An attempt should eventually be made against a state this penetrated.");
            Assert.Less(gov.conspiracyLevel, 30f, "The plot was rolled up.");
            Assert.Less(player.pillars.military, militaryBefore,
                "A purge costs the army capability it does not get back quickly.");
        }

        /// <summary>
        /// A rate, over many independent worlds, rather than a single draw. The
        /// previous form asserted the first resolved attempt against a maximal
        /// state was a failure — which was true only of one RNG realization, and
        /// concealed that such a state was in fact losing two attempts in five.
        /// </summary>
        [Test]
        public void AStrongState_DefeatsTheOverwhelmingMajorityOfAttempts()
        {
            int attempts = 0, survived = 0;

            for (int seed = 0; seed < 60; seed++)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 3000 + seed);
                var worldTurns = new TurnManager(world);
                worldTurns.ResolveMonth += RegimeSystem.MonthlyUpdate;
                var country = world.PlayerCountry;

                // One plot, resolved on its own terms — not re-pinned every month,
                // which would force attempt after attempt against a state that had
                // already rolled the first one up.
                country.government.conspiracyLevel = 95f;
                country.government.militaryLoyalty = 95f;
                country.stability = 90f;
                country.pillars.government = 95f;

                for (int month = 0; month < 60; month++)
                {
                    if (country.government.conspiracyLevel < RegimeSystem.CoupThreshold) break;
                    if (country.government.coupsExperienced > 0) break;
                    world.commandPoints.current = 6;
                    worldTurns.EndMonth();
                }

                if (country.government.coupsExperienced > 0) attempts++;
                else survived++;
            }

            Assert.Greater(survived, attempts * 4,
                $"A maximally loyal, stable, well-governed state lost {attempts} of "
                + $"{attempts + survived} runs to a coup. A stable state must not be "
                + "topplable by accumulating conspiracy alone (GDD §22).");
        }

        [Test]
        public void SecureMilitaryLoyalty_TradesStandingForSafety()
        {
            var player = state.PlayerCountry;
            player.government.militaryLoyalty = 40f;
            player.government.conspiracyLevel = 40f;
            float approvalBefore = player.governmentApproval;
            state.politicalCapital = 20f;

            Assert.IsTrue(RegimeSystem.SecureMilitaryLoyalty(state));

            Assert.Greater(player.government.militaryLoyalty, 40f);
            Assert.Less(player.government.conspiracyLevel, 40f);
            Assert.Less(player.governmentApproval, approvalBefore, "Patronage has a price.");
            Assert.AreEqual(20f - RegimeSystem.SecureLoyaltyCost, state.politicalCapital, 0.001f);
        }

        [Test]
        public void SecureMilitaryLoyalty_RequiresPoliticalCapital()
        {
            state.politicalCapital = 1f;
            float loyaltyBefore = state.PlayerCountry.government.militaryLoyalty;
            Assert.IsFalse(RegimeSystem.SecureMilitaryLoyalty(state));
            Assert.AreEqual(loyaltyBefore, state.PlayerCountry.government.militaryLoyalty);
        }

        [Test]
        public void CivilConflict_DegradesTheStateThenResolves()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            gov.inCivilConflict = true;
            gov.civilConflictMonthsRemaining = 6;
            player.pillars.economy = 70f;
            float economyBefore = player.pillars.economy;

            for (int i = 0; i < 6; i++) turns.EndMonth();

            Assert.IsFalse(gov.inCivilConflict, "Civil conflict must end in something, not run forever.");
            Assert.Less(player.pillars.economy, economyBefore, "It leaves the country poorer.");
            Assert.Greater(player.stability, 0f, "The state survives — this is not a game over.");
        }

        [Test]
        public void ConspiracyDetection_DependsOnOurOwnCounterintelligence()
        {
            bool Warned(float counterIntel)
            {
                var sim = WorldFactory.CreateDebugWorld(6603);
                var simTurns = new TurnManager(sim);
                simTurns.ResolveMonth += RegimeSystem.MonthlyUpdate;
                var player = sim.PlayerCountry;
                player.counterIntel.counterIntelligence = counterIntel;
                player.government.conspiracyLevel = 70f;
                simTurns.EndMonth();

                foreach (var notification in sim.notifications)
                    if (notification.title == "CONSPIRACY DETECTED") return true;
                return false;
            }

            Assert.IsTrue(Warned(80f), "Competent services should see a plot this far advanced.");
            Assert.IsFalse(Warned(10f), "Blind services see nothing coming.");
        }

        [Test]
        public void Regime_SurvivesSaveRoundTrip()
        {
            var gov = state.PlayerCountry.government;
            gov.militaryLoyalty = 42f;
            gov.conspiracyLevel = 58f;
            gov.conspiracyBackerId = "CHN";
            gov.inCivilConflict = true;
            gov.civilConflictMonthsRemaining = 7;
            gov.coupsExperienced = 2;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.PlayerCountry.government;

            Assert.AreEqual(42f, restored.militaryLoyalty);
            Assert.AreEqual(58f, restored.conspiracyLevel);
            Assert.AreEqual("CHN", restored.conspiracyBackerId);
            Assert.IsTrue(restored.inCivilConflict);
            Assert.AreEqual(7, restored.civilConflictMonthsRemaining);
            Assert.AreEqual(2, restored.coupsExperienced);
        }

        [Test]
        public void StableWorld_DoesNotSpontaneouslyCollapse()
        {
            // Thirty years of competent government anywhere should not produce coups.
            var sim = WorldFactory.CreateDebugWorld(6604);
            var simTurns = new TurnManager(sim);
            simTurns.ResolveMonth += RegimeSystem.MonthlyUpdate;

            for (int i = 0; i < 360; i++)
            {
                foreach (var country in sim.countries) MakeHealthyState(country);
                simTurns.EndMonth();
            }

            foreach (var country in sim.countries)
                Assert.AreEqual(0, country.government.coupsExperienced,
                    $"{country.id} fell despite being governed well.");
        }
    }
}

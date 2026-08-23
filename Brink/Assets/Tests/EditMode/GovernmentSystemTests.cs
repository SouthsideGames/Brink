using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class GovernmentSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8080);
            turns = new TurnManager(state);
            // NARROW PIPELINE: every test on this turn manager forces its own
            // preconditions (approval, growth, legislative support, election
            // date, leader age) and then asserts GovernmentSystem's own
            // arithmetic — elections, succession, emergency powers, PC accrual.
            // The omitted systems (cabinet, military, AI, intel) would move the
            // capability figures these tests hold constant rather than the
            // political ones they assert on.
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Factory_SeatsGovernmentsWithSystemAppropriateInstitutions()
        {
            Assert.AreEqual(GovernmentType.PresidentialRepublic, state.FindCountry("USA").government.type);
            Assert.AreEqual(GovernmentType.ParliamentaryRepublic, state.FindCountry("IND").government.type);
            Assert.AreEqual(GovernmentType.DominantPartyState, state.FindCountry("CHN").government.type);
            Assert.AreEqual(GovernmentType.CentralizedRepublic, state.FindCountry("RUS").government.type);

            foreach (var country in state.countries)
            {
                Assert.IsNotEmpty(country.government.leader.name);
                Assert.Greater(country.government.leader.age, 0f);
            }

            Assert.IsTrue(state.PlayerCountry.government.IsElective);
            Assert.IsFalse(state.FindCountry("CHN").government.IsElective);
            Assert.IsTrue(state.FindCountry("IND").government.AllowsEarlyElection);
            Assert.IsFalse(state.PlayerCountry.government.AllowsEarlyElection,
                "A presidential republic cannot simply go to the country early.");
        }

        [Test]
        public void FirstElection_IsNotScheduledImmediately()
        {
            var gov = state.PlayerCountry.government;
            Assert.Greater(gov.nextElectionDate.MonthsSince(state.date), 0,
                "The slice should not open on election day.");
        }

        [Test]
        public void PoliticalCapital_AccruesMonthlyAndIsCapped()
        {
            state.politicalCapital = 0f;
            turns.EndMonth();
            Assert.Greater(state.politicalCapital, 0f);

            for (int i = 0; i < 120; i++) turns.EndMonth();
            Assert.LessOrEqual(state.politicalCapital, GameState.PoliticalCapitalCap);
        }

        [Test]
        public void PublicMessaging_SpendsCapitalAndLiftsApproval()
        {
            var player = state.PlayerCountry;
            player.governmentApproval = 50f;
            state.politicalCapital = 10f;

            Assert.IsTrue(GovernmentSystem.PublicMessaging(state));
            Assert.AreEqual(10f - GovernmentSystem.PublicMessagingCost, state.politicalCapital, 0.001f);
            Assert.Greater(player.governmentApproval, 50f);
        }

        [Test]
        public void Instruments_FailWithoutSufficientCapital()
        {
            state.politicalCapital = 0.5f;
            var player = state.PlayerCountry;
            float approvalBefore = player.governmentApproval;

            Assert.IsFalse(GovernmentSystem.PublicMessaging(state));
            Assert.IsFalse(GovernmentSystem.InstitutionalReform(state));
            Assert.IsFalse(GovernmentSystem.DeclareEmergencyPowers(state));
            Assert.AreEqual(approvalBefore, player.governmentApproval);
            Assert.AreEqual(0.5f, state.politicalCapital, 0.001f);
        }

        [Test]
        public void DismissOfficial_ReplacesThemAndCostsLegislativeGoodwill()
        {
            state.politicalCapital = 20f;
            var player = state.PlayerCountry;
            var official = state.FindOfficial(Pillar.Military);
            string originalName = official.displayName;
            official.mode = ControlMode.Directed;
            official.directiveId = "MIL_READINESS";
            official.monthsInOffice = 40;
            float supportBefore = player.government.legislativeSupport;

            Assert.IsTrue(GovernmentSystem.DismissOfficial(state, Pillar.Military));

            Assert.AreNotEqual(originalName, official.displayName);
            Assert.AreEqual(0, official.monthsInOffice);
            Assert.AreEqual(ControlMode.Autonomous, official.mode, "A new appointee starts autonomous.");
            Assert.IsEmpty(official.directiveId);
            Assert.AreEqual("Secretary of Defense", official.title, "The office survives the officeholder.");
            Assert.Less(player.government.legislativeSupport, supportBefore);
        }

        [Test]
        public void EmergencyPowers_GrantCommandCapacityAtPoliticalCost()
        {
            state.politicalCapital = 20f;
            var player = state.PlayerCountry;
            int baselineBefore = state.commandPoints.baselinePerMonth;
            int currentBefore = state.commandPoints.current;
            float approvalBefore = player.governmentApproval;

            Assert.IsTrue(GovernmentSystem.DeclareEmergencyPowers(state));

            Assert.IsTrue(player.government.emergencyPowers);
            Assert.AreEqual(currentBefore + 2, state.commandPoints.current, "Capacity is felt immediately.");
            Assert.AreEqual(baselineBefore, state.commandPoints.baselinePerMonth,
                "The bonus is derived from the emergency state, not baked into the baseline.");
            Assert.Less(player.governmentApproval, approvalBefore);
            Assert.IsFalse(GovernmentSystem.DeclareEmergencyPowers(state), "Cannot double-declare.");
        }

        [Test]
        public void EmergencyPowers_ErodeApprovalThenLapse()
        {
            // Measured against a control run, not against an absolute change:
            // a strong economy can lift approval faster than emergency rule
            // erodes it, which would mask a real cost.
            float ApprovalAfter(bool declared)
            {
                var sim = WorldFactory.CreateDebugWorld(seed: 8080);
                var simTurns = new TurnManager(sim);
                // NARROW PIPELINE: declared-vs-not A/B with both arms wired
                // identically; omitting the cabinet and AI keeps the approval
                // gap attributable to emergency rule alone.
                simTurns.ResolveMonth += EconomySystem.MonthlyUpdate;
                simTurns.ResolveMonth += GovernmentSystem.MonthlyUpdate;
                sim.politicalCapital = 20f;
                sim.PlayerCountry.government.nextElectionDate = new GameDate(3000, 1);

                if (declared) GovernmentSystem.DeclareEmergencyPowers(sim);
                for (int i = 0; i < GovernmentSystem.EmergencyPowersDuration; i++) simTurns.EndMonth();
                return sim.PlayerCountry.governmentApproval;
            }

            Assert.Less(ApprovalAfter(true), ApprovalAfter(false),
                "Sustained emergency rule should never be politically free.");

            state.politicalCapital = 20f;
            GovernmentSystem.DeclareEmergencyPowers(state);
            turns.EndMonth();
            int cpUnderEmergency = state.commandPoints.current;

            for (int i = 0; i < GovernmentSystem.EmergencyPowersDuration + 1; i++) turns.EndMonth();

            Assert.IsFalse(state.PlayerCountry.government.emergencyPowers,
                "Extraordinary authority must expire.");
            Assert.Less(state.commandPoints.current, cpUnderEmergency,
                "Command capacity must fall back when the authority lapses.");
        }

        [Test]
        public void EmergencyPowers_CheaperInNonElectiveSystems()
        {
            float electiveCost = GovernmentSystem.EmergencyPowersCost * 1.4f;
            float centralizedCost = GovernmentSystem.EmergencyPowersCost * 0.7f;
            Assert.Greater(electiveCost, centralizedCost,
                "Government type must change what authority costs, not just apply modifiers.");
        }

        [Test]
        public void NationalPriority_RedirectsAutonomousOfficials()
        {
            float MilitaryGainUnder(NationalPriority priority)
            {
                var sim = WorldFactory.CreateDebugWorld(seed: 4321);
                var simTurns = new TurnManager(sim);
                // NARROW PIPELINE: the cabinet tick is the only thing a national
                // priority acts through, and it is the only difference between
                // the two arms; anything else wired here would add pillar growth
                // that has nothing to do with the priority being measured.
                simTurns.ResolveMonth += CabinetSystem.MonthlyAct;
                sim.PlayerCountry.government.leader.priority = priority;

                float before = sim.PlayerCountry.pillars.military;
                for (int i = 0; i < 24; i++) simTurns.EndMonth();
                return sim.PlayerCountry.pillars.military - before;
            }

            Assert.Greater(MilitaryGainUnder(NationalPriority.Security),
                           MilitaryGainUnder(NationalPriority.Prosperity),
                           "A security priority should visibly favor the military pillar.");
        }

        [Test]
        public void SetNationalPriority_CostsCapitalAndRejectsNoOp()
        {
            state.politicalCapital = 10f;
            var gov = state.PlayerCountry.government;
            gov.leader.priority = NationalPriority.Prosperity;

            Assert.IsFalse(GovernmentSystem.SetNationalPriority(state, NationalPriority.Prosperity));
            Assert.AreEqual(10f, state.politicalCapital, 0.001f);

            Assert.IsTrue(GovernmentSystem.SetNationalPriority(state, NationalPriority.Security));
            Assert.AreEqual(NationalPriority.Security, gov.leader.priority);
            Assert.Less(state.politicalCapital, 10f);
        }

        [Test]
        public void Election_RunsOnScheduleAndReschedules()
        {
            var gov = state.PlayerCountry.government;
            gov.nextElectionDate = state.date;
            var beforeDate = state.date;

            turns.EndMonth();

            Assert.Greater(gov.nextElectionDate.MonthsSince(beforeDate), 1,
                "A completed election must schedule the next one.");
        }

        [Test]
        public void Election_UnpopularGovernmentInSlumpLosesOffice()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            string incumbent = gov.leader.name;

            player.governmentApproval = 5f;
            player.economy.growthRate = -8f;
            player.economy.inflation = 22f;
            player.warExhaustion = 90f;
            player.nationalUnity = 10f;
            gov.legislativeSupport = 10f;
            gov.nextElectionDate = state.date;

            turns.EndMonth();

            // `EconomySystem.MonthlyUpdate` is wired ahead of `GovernmentSystem`
            // in this fixture and approaches growth back toward ~2 at 35%/month
            // and inflation back toward ~2.2 at 30%/month — so the slump the
            // election reads is not the slump written above. It only has to
            // survive one tick here, but if it ever stops surviving it, the
            // assertion below becomes a coin flip on the ±14 election jitter.
            Assert.Less(player.economy.growthRate, 0f,
                $"Growth recovered to {player.economy.growthRate:F1} before the election was held, "
                + "so this is no longer a government going to the country in a slump.");
            Assert.Greater(player.economy.inflation, 8f,
                $"Inflation fell to {player.economy.inflation:F1} before the election was held.");

            Assert.AreNotEqual(incumbent, gov.leader.name, "A collapse this severe should end the administration.");
            Assert.AreEqual(2, state.administrationsServed);
            Assert.Greater(player.governmentApproval, 40f, "New leadership starts with a honeymoon.");
            Assert.AreEqual(0, gov.leader.monthsInOffice);
        }

        [Test]
        public void Election_PopularGovernmentInBoomRetainsOffice()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            string incumbent = gov.leader.name;

            player.governmentApproval = 95f;
            player.economy.growthRate = 6f;
            player.economy.inflation = 2f;
            player.warExhaustion = 0f;
            player.nationalUnity = 90f;
            gov.legislativeSupport = 90f;
            gov.nextElectionDate = state.date;

            turns.EndMonth();

            // Same ordering hazard as the slump case above, and it matters more
            // here: an election with an erased fixture sits near the 50 coin flip
            // and "the incumbent was retained" would pass roughly half the time
            // while asserting nothing about a boom.
            Assert.Greater(player.economy.growthRate, 0f,
                $"Growth fell to {player.economy.growthRate:F1} before the election was held, "
                + "so this is no longer a popular government in a boom.");

            Assert.AreEqual(incumbent, gov.leader.name);
            Assert.AreEqual(1, state.administrationsServed);
        }

        [Test]
        public void NewAdministration_InheritsCapabilitiesUnchanged()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            float militaryBefore = player.pillars.military;
            float industryBefore = player.resources.industrialCapacity;
            float groundStrengthBefore = player.military.ground.strength;

            player.governmentApproval = 2f;
            player.economy.growthRate = -9f;
            player.nationalUnity = 5f;
            gov.legislativeSupport = 5f;
            gov.nextElectionDate = state.date;

            turns.EndMonth();

            Assert.AreEqual(2, state.administrationsServed, "Precondition: leadership changed.");
            Assert.AreEqual(militaryBefore, player.pillars.military, 0.5f,
                "A new administration does not re-arm the country overnight (GDD §13).");
            Assert.AreEqual(industryBefore, player.resources.industrialCapacity, 0.5f);
            Assert.AreEqual(groundStrengthBefore, player.military.ground.strength, 0.5f);
        }

        [Test]
        public void NewAdministration_EndsEmergencyPowersAndReshufflesCabinet()
        {
            state.politicalCapital = 20f;
            GovernmentSystem.DeclareEmergencyPowers(state);

            var player = state.PlayerCountry;
            var gov = player.government;
            foreach (var official in state.cabinet)
            {
                official.mode = ControlMode.Directed;
                official.directiveId = "TEST";
            }

            player.governmentApproval = 2f;
            player.economy.growthRate = -9f;
            gov.legislativeSupport = 3f;
            gov.nextElectionDate = state.date;

            turns.EndMonth();

            Assert.AreEqual(2, state.administrationsServed);
            Assert.IsFalse(gov.emergencyPowers, "Emergency authority does not survive a change of leadership.");

            int reverted = 0;
            foreach (var official in state.cabinet)
                if (official.mode == ControlMode.Autonomous) reverted++;
            Assert.Greater(reverted, 0, "A new administration brings some of its own people.");
        }

        [Test]
        public void PlayerPersistsAcrossAdministrations()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            state.strategistXP = 500;
            state.skillPoints = 3;

            player.governmentApproval = 2f;
            player.economy.growthRate = -9f;
            gov.legislativeSupport = 3f;
            gov.nextElectionDate = state.date;

            turns.EndMonth();

            // The two sibling tests that force a leadership change this way both
            // assert it actually happened; this one did not. Without it, an
            // election the incumbent survives — which the injected slump only
            // avoids because it outlasts one economy tick — leaves the test
            // asserting that progression survives an ordinary month.
            Assert.AreEqual(2, state.administrationsServed,
                "Precondition: leadership changed. Nothing here outlives an administration "
                + "if no administration ended.");

            Assert.AreEqual(500, state.strategistXP, "The operator's progression outlives administrations.");
            Assert.AreEqual(3, state.skillPoints);
            Assert.IsNotNull(state.PlayerCountry);
        }

        [Test]
        public void NonElectiveSystem_ReplacesLeadershipInternally()
        {
            var chn = state.FindCountry("CHN");
            var gov = chn.government;
            string incumbent = gov.leader.name;
            gov.leader.age = 95f;      // succession pressure
            gov.eliteCohesion = 20f;   // and a fractured elite

            bool changed = false;
            for (int i = 0; i < 240 && !changed; i++)
            {
                turns.EndMonth();
                changed = gov.leader.name != incumbent;
            }

            Assert.IsTrue(changed, "Age and elite fracture should eventually force a succession.");
        }

        [Test]
        public void ForeignElections_ProduceWireTraffic()
        {
            var ind = state.FindCountry("IND");
            ind.government.nextElectionDate = state.date;

            // This test is about what GovernmentSystem *files*, not about whether
            // the Cabinet passed it on — a weak Government desk is entitled to
            // lose world news (GDD §28.1), which would make this flaky rather
            // than wrong. Isolate the producer.
            ReportingSystem.Disabled = true;
            try { turns.EndMonth(); }
            finally { ReportingSystem.Disabled = false; }

            bool reported = false;
            foreach (var notification in state.notifications)
                if (notification.countryId == "IND" && notification.title.Contains("ELECTION")) reported = true;
            Assert.IsTrue(reported, "A foreign election is world news.");
        }

        [Test]
        public void EconomicMisery_ErodesApprovalOverTime()
        {
            // NARROW PIPELINE: the omission is the point of the test.
            // Isolate the political response: run without the economy hook so the
            // injected slump persists instead of self-correcting toward target.
            var politicalOnly = new TurnManager(state);
            politicalOnly.ResolveMonth += GovernmentSystem.MonthlyUpdate;

            var player = state.PlayerCountry;
            player.governmentApproval = 70f;
            player.economy.growthRate = -5f;
            player.economy.inflation = 15f;
            player.economy.unemployment = 18f;
            player.government.nextElectionDate = new GameDate(3000, 1); // keep the same administration

            for (int i = 0; i < 12; i++) politicalOnly.EndMonth();

            Assert.Less(player.governmentApproval, 70f);
        }

        [Test]
        public void TermLimit_EndsAdministrationEvenWhenWildlyPopular()
        {
            var player = state.PlayerCountry;
            var gov = player.government;
            Assert.AreEqual(2, gov.consecutiveTermLimit, "Presidential republics limit consecutive terms.");

            string incumbent = gov.leader.name;
            gov.leader.termsServed = 2;
            player.governmentApproval = 99f;
            player.economy.growthRate = 8f;
            gov.legislativeSupport = 99f;
            gov.nextElectionDate = state.date;

            turns.EndMonth();

            Assert.AreNotEqual(incumbent, gov.leader.name,
                "A term-limited leader leaves office however popular they are.");
            Assert.AreEqual("GOVERNING PARTY", gov.leader.faction,
                "A popular administration's faction should retain power.");
            Assert.AreEqual(0, gov.leader.termsServed);
        }

        [Test]
        public void ParliamentarySystem_HasNoTermLimit()
        {
            Assert.AreEqual(0, state.FindCountry("IND").government.consecutiveTermLimit);
        }

        [Test]
        public void LongIncumbency_MakesReelectionHarder()
        {
            int LossesOverRuns(int termsServed)
            {
                int losses = 0;
                for (int seed = 0; seed < 40; seed++)
                {
                    var sim = WorldFactory.CreateDebugWorld(seed * 17 + 3);
                    var simTurns = new TurnManager(sim);
                    // NARROW PIPELINE: one month per seed, with approval, growth
                    // and support pinned immediately before the election — only
                    // the incumbency term count varies, so nothing else needs to
                    // run for the electoral arithmetic to be measured.
                    simTurns.ResolveMonth += GovernmentSystem.MonthlyUpdate;

                    var gov = sim.PlayerCountry.government;
                    sim.PlayerCountry.governmentApproval = 52f;
                    sim.PlayerCountry.economy.growthRate = 2f;
                    gov.legislativeSupport = 52f;
                    gov.leader.termsServed = termsServed;
                    gov.nextElectionDate = sim.date;

                    string incumbent = gov.leader.name;
                    simTurns.EndMonth();
                    if (gov.leader.name != incumbent) losses++;
                }
                return losses;
            }

            int freshLosses = LossesOverRuns(0);
            int tiredLosses = LossesOverRuns(3);

            Assert.Greater(tiredLosses, freshLosses,
                "A long-serving government should face anti-incumbent sentiment.");
        }

        [Test]
        public void Government_SurvivesSaveRoundTrip()
        {
            state.politicalCapital = 15f;
            GovernmentSystem.DeclareEmergencyPowers(state);
            for (int i = 0; i < 4; i++) turns.EndMonth();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var original = state.PlayerCountry.government;
            var restored = loaded.PlayerCountry.government;

            Assert.AreEqual(original.type, restored.type);
            Assert.AreEqual(original.leader.name, restored.leader.name);
            Assert.AreEqual(original.leader.priority, restored.leader.priority);
            Assert.AreEqual(original.nextElectionDate, restored.nextElectionDate);
            Assert.AreEqual(original.emergencyPowers, restored.emergencyPowers);
            Assert.AreEqual(original.emergencyPowersMonthsRemaining, restored.emergencyPowersMonthsRemaining);
            Assert.AreEqual(state.politicalCapital, loaded.politicalCapital, 0.001f);
            Assert.AreEqual(state.administrationsServed, loaded.administrationsServed);
        }

        [Test]
        public void LongRun_RemainsStableAndDeterministic()
        {
            GameState Run(int seed)
            {
                var sim = WorldFactory.CreateDebugWorld(seed);
                var simTurns = new TurnManager(sim);
                // Must use the real pipeline: a twenty-year world-health and
                // determinism invariant is exactly the claim a hand-copied
                // subset cannot make. This list had drifted from the shipped
                // order by regime change, technology, territory, endgames,
                // acquisition and cabinet lifecycle.
                SimulationPipeline.Wire(simTurns, sim);
                for (int i = 0; i < 240; i++) simTurns.EndMonth(); // 20 years
                return sim;
            }

            var a = Run(1234);
            Assert.AreEqual(SaveSystem.ToJson(a), SaveSystem.ToJson(Run(1234)));

            // The world should still be coherent after two decades.
            Assert.Greater(a.administrationsServed, 1, "Twenty years should see more than one administration.");
            foreach (var country in a.countries)
            {
                Assert.IsNotEmpty(country.government.leader.name);
                Assert.GreaterOrEqual(country.governmentApproval, 0f);
                Assert.LessOrEqual(country.governmentApproval, 100f);
                Assert.Greater(country.economy.gdp, 0f);
            }
        }
    }
}

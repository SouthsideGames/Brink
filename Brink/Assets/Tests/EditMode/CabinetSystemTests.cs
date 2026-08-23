using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class CabinetSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 2024);
            turns = new TurnManager(state);
            turns.ResolveMonth += CabinetSystem.MonthlyAct;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        [Test]
        public void Factory_CreatesOneOfficialPerPillar_Deterministic()
        {
            Assert.AreEqual(5, state.cabinet.Count);
            foreach (Pillar pillar in System.Enum.GetValues(typeof(Pillar)))
            {
                var official = state.FindOfficial(pillar);
                Assert.NotNull(official, $"Missing official for {pillar}");
                Assert.AreEqual(ControlMode.Autonomous, official.mode);
                Assert.IsNotEmpty(official.displayName);
            }

            var twin = WorldFactory.CreateDebugWorld(seed: 2024);
            for (int i = 0; i < 5; i++)
                Assert.AreEqual(twin.cabinet[i].displayName, state.cabinet[i].displayName);
        }

        [Test]
        public void MonthlyAct_AutonomousOfficialsImproveTheNation()
        {
            var player = state.PlayerCountry;
            float militaryBefore = player.pillars.military;
            float economyBefore = player.pillars.economy;

            for (int i = 0; i < 24; i++)
                turns.EndMonth();

            Assert.Greater(player.pillars.military, militaryBefore);
            Assert.Greater(player.pillars.economy, economyBefore);
            Assert.AreEqual(24, state.FindOfficial(Pillar.Military).monthsInOffice);
        }

        [Test]
        public void MonthlyAct_IsDeterministicPerSeed()
        {
            var stateB = WorldFactory.CreateDebugWorld(seed: 2024);
            var turnsB = new TurnManager(stateB);
            turnsB.ResolveMonth += CabinetSystem.MonthlyAct;

            for (int i = 0; i < 36; i++) { turns.EndMonth(); turnsB.EndMonth(); }

            Assert.AreEqual(SaveSystem.ToJson(stateB), SaveSystem.ToJson(state));
        }

        [Test]
        public void Directive_ShiftsOfficialFocus()
        {
            float StabilityGainWithDirective(string directiveId)
            {
                var simState = WorldFactory.CreateDebugWorld(seed: 7);
                var simTurns = new TurnManager(simState);
                simTurns.ResolveMonth += CabinetSystem.MonthlyAct;
                var official = simState.FindOfficial(Pillar.Government);
                official.mode = ControlMode.Directed;
                official.directiveId = directiveId;

                float before = simState.PlayerCountry.stability;
                for (int i = 0; i < 24; i++) simTurns.EndMonth();
                return simState.PlayerCountry.stability - before;
            }

            Assert.Greater(StabilityGainWithDirective("GOV_STABILITY"),
                           StabilityGainWithDirective("GOV_APPROVAL"));
        }

        [Test]
        public void SetMode_DirectedCostsInfluence_FailsWhenExhausted()
        {
            var official = state.FindOfficial(Pillar.Economy);
            state.influence = 1;

            Assert.IsTrue(CabinetSystem.SetMode(state, official, ControlMode.Directed));
            Assert.AreEqual(0, state.influence);
            Assert.AreEqual(ControlMode.Directed, official.mode);
            Assert.IsNotEmpty(official.directiveId);

            var second = state.FindOfficial(Pillar.Military);
            Assert.IsFalse(CabinetSystem.SetMode(state, second, ControlMode.Directed));
            Assert.AreEqual(ControlMode.Autonomous, second.mode);
        }

        [Test]
        public void SetDirective_CostsInfluence_NoChargeForSameDirective()
        {
            var official = state.FindOfficial(Pillar.Economy);
            state.influence = 3;
            CabinetSystem.SetMode(state, official, ControlMode.Directed); // -1

            Assert.IsTrue(CabinetSystem.SetDirective(state, official, "ECO_AUSTERITY")); // -1
            Assert.AreEqual(1, state.influence);
            Assert.IsFalse(CabinetSystem.SetDirective(state, official, "ECO_AUSTERITY"), "Same directive should not recharge.");
            Assert.AreEqual(1, state.influence);
        }

        [Test]
        public void Influence_RegeneratesMonthlyWithCap()
        {
            state.influence = 0;
            turns.EndMonth();
            Assert.AreEqual(GameState.InfluencePerMonth, state.influence);

            for (int i = 0; i < 5; i++) turns.EndMonth();
            Assert.AreEqual(GameState.InfluenceCap, state.influence);
        }

        [Test]
        public void DirectControl_SidelinedOfficialDoesNotActAndTrustDecays()
        {
            var official = state.FindOfficial(Pillar.Military);
            official.mode = ControlMode.DirectControl;
            float trustBefore = official.trust;
            float militaryBefore = state.PlayerCountry.pillars.military;

            for (int i = 0; i < 12; i++) turns.EndMonth();

            Assert.AreEqual(militaryBefore, state.PlayerCountry.pillars.military, 0.001f,
                "No one should be advancing the military pillar while sidelined.");
            Assert.Less(official.trust, trustBefore);
        }

        [Test]
        public void DirectAction_SpendsCPAppliesEffectAndErodesTrust()
        {
            var official = state.FindOfficial(Pillar.Military);
            float trustBefore = official.trust;
            float militaryBefore = state.PlayerCountry.pillars.military;
            int cpBefore = state.commandPoints.current;

            Assert.IsTrue(CabinetSystem.TryDirectAction(state, turns, Pillar.Military));

            Assert.AreEqual(cpBefore - 2, state.commandPoints.current);
            Assert.Greater(state.PlayerCountry.pillars.military, militaryBefore);
            Assert.Less(official.trust, trustBefore);
        }

        [Test]
        public void DirectAction_FailsWithoutCommandPoints()
        {
            state.commandPoints.current = 1;
            float militaryBefore = state.PlayerCountry.pillars.military;

            Assert.IsFalse(CabinetSystem.TryDirectAction(state, turns, Pillar.Military));
            Assert.AreEqual(militaryBefore, state.PlayerCountry.pillars.military);
            Assert.AreEqual(1, state.commandPoints.current);
        }

        [Test]
        public void Cabinet_SurvivesSaveRoundTrip()
        {
            var official = state.FindOfficial(Pillar.Diplomacy);
            CabinetSystem.SetMode(state, official, ControlMode.Directed);
            CabinetSystem.SetDirective(state, official, "DIP_PRESSURE");

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(5, loaded.cabinet.Count);
            var loadedOfficial = loaded.FindOfficial(Pillar.Diplomacy);
            Assert.AreEqual(ControlMode.Directed, loadedOfficial.mode);
            Assert.AreEqual("DIP_PRESSURE", loadedOfficial.directiveId);
            Assert.AreEqual(official.trust, loadedOfficial.trust);
            Assert.AreEqual(state.influence, loaded.influence);
        }
    }
}

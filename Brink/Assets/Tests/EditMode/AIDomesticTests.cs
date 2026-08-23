using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The AI's domestic half (GDD §13, §27).
    ///
    /// The foreign side of the AI has matched the player's verbs for a while.
    /// The domestic side did not: `ConsolidateHome` and `SecureResources` wrote
    /// stability, approval, energy and materials directly, every month, at no
    /// cost, through verbs the player has no access to. That is the same
    /// asymmetry this project already fixed once pointing the other way — AI
    /// states locked *out* of player verbs — and it had two consequences worth
    /// naming: a rival could ride out any domestic damage the player inflicted,
    /// and an energy-poor archetype could stop being energy-poor by wanting to.
    /// </summary>
    public class AIDomesticTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6161);
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        CountryState Rival => state.FindCountry("CHN");
        AIState RivalAI => state.FindAI("CHN");

        // ---------- the political capital economy is shared ----------

        [Test]
        public void AIGovernmentsAccruePoliticalCapital()
        {
            RivalAI.politicalCapital = 0f;

            for (int i = 0; i < 6; i++) GovernmentSystem.MonthlyUpdate(state);

            Assert.Greater(RivalAI.politicalCapital, 0f,
                "A government with no political budget cannot be made to pay for anything.");
        }

        [Test]
        public void PoliticalCapitalIncomeUsesOneFormulaForEveryone()
        {
            var player = state.PlayerCountry;
            var rival = Rival;

            // Same standing, same income — the formula must not know who is
            // running the government.
            rival.governmentApproval = player.governmentApproval;
            rival.pillars.government = player.pillars.government;
            rival.government.type = player.government.type;
            rival.government.legislativeSupport = player.government.legislativeSupport;
            rival.government.eliteCohesion = player.government.eliteCohesion;
            rival.government.emergencyPowers = player.government.emergencyPowers;

            Assert.AreEqual(GovernmentSystem.PoliticalCapitalIncomeFor(player),
                            GovernmentSystem.PoliticalCapitalIncomeFor(rival), 0.001f);
        }

        [Test]
        public void PoliticalCapitalIsCapped()
        {
            for (int i = 0; i < 400; i++) GovernmentSystem.MonthlyUpdate(state);

            Assert.LessOrEqual(RivalAI.politicalCapital, GameState.PoliticalCapitalCap,
                "An AI government must not bank unlimited authority over a long save.");
        }

        // ---------- domestic action costs something ----------

        [Test]
        public void ABankruptGovernmentCannotTalkItsWayOutOfTrouble()
        {
            RivalAI.politicalCapital = 0f;
            var rival = Rival;
            float approvalBefore = rival.governmentApproval;
            float stabilityBefore = rival.stability;

            Assert.IsFalse(GovernmentSystem.PublicMessagingBy(state, "CHN"));
            Assert.IsFalse(GovernmentSystem.InstitutionalReformBy(state, "CHN"));

            Assert.AreEqual(approvalBefore, rival.governmentApproval, 0.001f);
            Assert.AreEqual(stabilityBefore, rival.stability, 0.001f);
        }

        [Test]
        public void DomesticRepairSpendsTheBudget()
        {
            RivalAI.politicalCapital = 20f;
            float before = RivalAI.politicalCapital;

            Assert.IsTrue(GovernmentSystem.PublicMessagingBy(state, "CHN"));

            Assert.Less(RivalAI.politicalCapital, before,
                "The point of the budget is that spending it depletes it.");
        }

        [Test]
        public void ReformCostsAForeignGovernmentItsGoodwillToo()
        {
            RivalAI.politicalCapital = 40f;
            var rival = Rival;
            float cohesionBefore = rival.government.eliteCohesion;
            float supportBefore = rival.government.legislativeSupport;

            Assert.IsTrue(GovernmentSystem.InstitutionalReformBy(state, "CHN"));

            bool paid = rival.government.IsElective
                ? rival.government.legislativeSupport < supportBefore
                : rival.government.eliteCohesion < cohesionBefore;

            Assert.IsTrue(paid,
                "Reform disturbs entrenched interests wherever it happens, not only at home.");
        }

        // ---------- the endowment model applies to everyone ----------

        [Test]
        public void AResourcePoorStateStaysResourcePoor()
        {
            // The endowment model's own comment says a country can invest past
            // its endowment "only through capability". The AI's resource action
            // used to add energy uncapped, which repealed that for every
            // non-player state and stopped energyDrag biting for anyone.
            var rival = Rival;
            rival.resources.energyEndowment = 25f;
            rival.resources.energy = 25f;
            rival.resources.materialsEndowment = 20f;
            rival.resources.strategicMaterials = 20f;
            rival.resources.treasury = 500000f;

            var simulation = new TurnManager(state);
            simulation.ResolveMonth += AISystem.MonthlyThink;

            for (int i = 0; i < 120; i++) simulation.EndMonth();

            Assert.LessOrEqual(rival.resources.energy,
                EconomySystem.EnergyCeilingFor(state, rival) + 1f,
                "A decade of investment must not turn an energy-poor archetype into an energy-rich one.");
            Assert.LessOrEqual(rival.resources.strategicMaterials,
                EconomySystem.MaterialsCeilingFor(state, rival) + 1f);
        }

        [Test]
        public void SecuringResourcesCostsMoney()
        {
            var rival = Rival;
            rival.resources.energyEndowment = 95f;
            rival.resources.energy = 20f;   // a real gap to close
            rival.resources.treasury = 4000f;
            float before = rival.resources.treasury;

            var simulation = new TurnManager(state);
            simulation.ResolveMonth += AISystem.MonthlyThink;
            for (int i = 0; i < 24; i++) simulation.EndMonth();

            Assert.Less(rival.resources.treasury, before,
                "Closing a resource gap is an investment, not a wish.");
        }

        [Test]
        public void ABrokeGovernmentCannotBuyResources()
        {
            var rival = Rival;
            rival.resources.energyEndowment = 95f;
            rival.resources.energy = 20f;
            rival.resources.treasury = 0f;
            float energyBefore = rival.resources.energy;

            AISystem.MonthlyThink(state);

            Assert.LessOrEqual(rival.resources.energy, energyBefore + 0.01f,
                "With an empty treasury there is nothing to invest.");
        }

        // ---------- the headline rule ----------

        [Test]
        public void NoGovernmentBUYSDomesticRepairForFree()
        {
            // The line this test draws is between *governance* and
            // *intervention*, not between "moves" and "does not move".
            //
            // Ordinary governance is legitimately free on both sides: the
            // player's Autonomous Cabinet raises pillars every month at no CP
            // cost, and `InvestInPillars` is the AI's equivalent at a comparable
            // magnitude. Asserting that nothing at all may move would forbid the
            // AI from having a functioning state apparatus while the player has
            // one.
            //
            // What must never be free is the discretionary repair — buying
            // stability, approval, unity, energy and materials on demand. Those
            // are what `ConsolidateHome` and `SecureResources` used to grant for
            // nothing, and they are the stats this snapshot covers.
            foreach (var country in state.countries)
            {
                country.resources.treasury = 0f;
                var ai = state.FindAI(country.id);
                if (ai != null) ai.politicalCapital = 0f;
            }

            var before = Snapshot();
            AISystem.MonthlyThink(state);
            var after = Snapshot();

            Assert.AreEqual(before, after,
                "With no treasury and no political capital anywhere, no government may buy " +
                "itself stability, approval, unity or resources. Anything that moves here is " +
                "being granted for free.");
        }

        /// <summary>
        /// The stats a government can only obtain by *spending*.
        ///
        /// Pillars and `nationalUnity` are deliberately excluded: both sides get
        /// them from ordinary governance. The player's Autonomous Cabinet grants
        /// free approval, stability and Government pillar every month; the AI's
        /// `InvestInPillars` grants a Government pillar and a little unity to a
        /// stability-minded government. Different shapes, comparable size, and
        /// if anything the player's baseline is the stronger of the two — so
        /// neither is the free lunch this test is hunting for.
        /// </summary>
        string Snapshot()
        {
            var builder = new System.Text.StringBuilder();
            foreach (var country in state.countries)
                builder.Append($"{country.id}:{country.stability:F3},")
                       .Append($"{country.governmentApproval:F3},")
                       .Append($"{country.resources.energy:F3},")
                       .Append($"{country.resources.strategicMaterials:F3};");
            return builder.ToString();
        }

        // ---------- pre-emption ----------

        [Test]
        public void ADetectedProgrammeProducesDistinctBehaviour()
        {
            // Detection used to raise threat and change nothing about what the
            // government actually did — the most consequential thing
            // intelligence can report was behaviourally inert.
            // A completed programme crosses the visibility threshold on its own
            // signature, with no collection required — the world gets exactly one
            // warning, and it is that the thing is ready (`KnownPreparation`).
            Rival.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StrategicDestruction,
                progress = 100f
            });

            AISystem.MonthlyThink(state);

            var ai = state.FindAI("IND");
            bool preempting = false;
            foreach (var objective in ai.objectives)
                if (objective.type == AIObjectiveType.PreemptProgramme) preempting = true;

            Assert.IsTrue(preempting,
                "A government that can see a rival approaching an existential instrument " +
                "must have a distinct objective about it, not merely a higher threat number.");
        }
    }
}

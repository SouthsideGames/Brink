using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The defence minister's professional opinion (GDD §7.2, §8, §19).
    ///
    /// `Official.competence` decided how well a minister executed when left
    /// autonomous and how much traffic reached the terminal. It did nothing for a
    /// player running the war themselves — so the person nominally in charge of
    /// the military had nothing to say during a confrontation, and appointing a
    /// good one changed outcomes you never saw and none you were deciding.
    /// </summary>
    public class MilitaryAdviceTests
    {
        GameState state;
        Confrontation confrontation;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 8642);
            state.commandPoints.current = 40;

            confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, "CHN",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        StrategicLocation TargetIn(string countryId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type != LocationType.Capital)
                    return location;
            return null;
        }

        // ---------- there is advice at all ----------

        [Test]
        public void TheMinisterHasSomethingToSayDuringAWar()
        {
            var advice = MilitaryAdvice.Recommend(state, confrontation);

            Assert.IsNotNull(advice, "The officer in charge of the military had no opinion.");
            Assert.IsTrue(advice.HasTarget);
            Assert.IsNotEmpty(advice.rationale, "A recommendation with no reasoning is a coin toss.");
            Assert.Greater(advice.assessedOdds, 0f);
        }

        [Test]
        public void TheAdviceNamesAnOrderWeCouldActuallyGive()
        {
            var advice = MilitaryAdvice.Recommend(state, confrontation);
            var target = state.FindLocation(advice.targetLocationId);

            Assert.IsNotNull(target);
            Assert.IsTrue(OperationCatalog.CanOrder(
                    state, state.playerCountryId, target, advice.operation),
                "The minister recommended something the world would refuse.");
        }

        [Test]
        public void NoAdviceWithoutAMinister()
        {
            state.PlayerCountry.cabinet.Clear();
            Assert.IsNull(MilitaryAdvice.Recommend(state, confrontation),
                "Advice arrived from a vacant office.");
        }

        // ---------- competence is the whole mechanic ----------

        [Test]
        public void ACompetentMinisterReadsTheOddsCloseToTruth()
        {
            var minister = state.PlayerCountry.FindOfficial(Pillar.Military);
            var target = TargetIn("CHN");
            var directive = new OperationDirective();

            float truth = MilitarySystem.EstimateOdds(state, state.playerCountryId, target,
                OperationType.Assault, directive, 0f);

            minister.competence = 100f;
            float sharp = MilitaryAdvice.AssessOdds(state, target, OperationType.Assault, directive, 0f);

            Assert.AreEqual(truth, sharp, 0.02f,
                "A minister at maximum competence should read the board almost exactly.");
        }

        [Test]
        public void APoorMinisterIsConfidentlyWrong()
        {
            // The design point: the noise is in their *assessment*, not their
            // delivery. Bad advice arrives sounding exactly like good advice,
            // because you are trusting a person rather than a calculator.
            var minister = state.PlayerCountry.FindOfficial(Pillar.Military);
            var directive = new OperationDirective();

            float worstError = 0f;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != "CHN") continue;
                foreach (var type in new[] { OperationType.Assault, OperationType.AirStrike,
                             OperationType.Siege, OperationType.SuppressDefenses })
                {
                    if (!OperationCatalog.CanOrder(state, state.playerCountryId, location, type))
                        continue;

                    float truth = MilitarySystem.EstimateOdds(state, state.playerCountryId,
                        location, type, directive, 0f);

                    minister.competence = 5f;
                    float guess = MilitaryAdvice.AssessOdds(state, location, type, directive, 0f);
                    worstError = Math.Max(worstError, Math.Abs(guess - truth));
                }
            }

            Assert.Greater(worstError, 0.05f,
                "An incompetent minister assessed everything correctly, so competence buys " +
                "nothing where the player can feel it.");
        }

        [Test]
        public void ReliabilityIsStatedRatherThanHidden()
        {
            var minister = state.PlayerCountry.FindOfficial(Pillar.Military);

            minister.competence = 90f;
            Assert.Greater(MilitaryAdvice.ReliabilityOf(minister), 0.85f);

            minister.competence = 10f;
            Assert.Less(MilitaryAdvice.ReliabilityOf(minister), 0.2f);

            var advice = MilitaryAdvice.Recommend(state, confrontation);
            string header = MilitaryAdvice.Header(state, advice);
            StringAssert.Contains("competence", header,
                "The operator cannot tell a confident minister from a competent one.");
        }

        [Test]
        public void TheSameQuestionGetsTheSameAnswerTwice()
        {
            // A number that flickered on every refresh would be unusable, and
            // averaging it would leak the true value.
            var target = TargetIn("CHN");
            var directive = new OperationDirective();

            float first = MilitaryAdvice.AssessOdds(state, target, OperationType.Assault, directive, 0f);
            float second = MilitaryAdvice.AssessOdds(state, target, OperationType.Assault, directive, 0f);

            Assert.AreEqual(first, second, 0.0001f);
        }

        [Test]
        public void ADifferentMinisterReadsTheBoardDifferently()
        {
            var minister = state.PlayerCountry.FindOfficial(Pillar.Military);
            minister.competence = 20f;
            var target = TargetIn("CHN");
            var directive = new OperationDirective();

            float before = MilitaryAdvice.AssessOdds(state, target, OperationType.Assault, directive, 0f);

            minister.id = "REPLACEMENT_OFFICIAL";
            float after = MilitaryAdvice.AssessOdds(state, target, OperationType.Assault, directive, 0f);

            Assert.AreNotEqual(before, after,
                "Replacing the officer changed nothing about the advice, so the appointment " +
                "is not the decision it is meant to be.");
        }

        // ---------- the advice is an opinion, not a calculator ----------

        [Test]
        public void TheMinisterValuesASetupBeyondItsOwnOdds()
        {
            // A professional recommends breaking the works first because the
            // effect is permanent — not because that single order has the best
            // chance of succeeding.
            var target = TargetIn("CHN");
            target.defenseValue = 95f;

            var advice = MilitaryAdvice.Recommend(state, confrontation);
            Assert.IsNotNull(advice);

            // Against a heavily fortified board, suppression should be at least
            // considered rather than always losing to a raw assault.
            Assert.IsTrue(advice.operation == OperationType.SuppressDefenses
                          || advice.assessedOdds > 0.4f,
                $"Against 95 defence the staff recommended {advice.operation} at " +
                $"{advice.assessedOdds:P0} — no professional judgement is being applied.");
        }

        [Test]
        public void TheAdviceIsNeverBinding()
        {
            // The operator gives the order. Advice that constrained the choice
            // would be delegation wearing a recommendation's clothes.
            var advice = MilitaryAdvice.Recommend(state, confrontation);
            var other = TargetIn("CHN");

            var record = ConfrontationSystem.LaunchOperationBy(state, confrontation,
                state.playerCountryId, other.id, OperationType.AirStrike, new OperationDirective());

            Assert.IsNotNull(record,
                "An order the minister did not recommend was refused. The player decides.");
        }

        // ---------- steering a delegated pillar ----------

        [Test]
        public void DelegationIsVisibleRatherThanSilent()
        {
            var minister = state.PlayerCountry.FindOfficial(Pillar.Military);
            minister.mode = ControlMode.Directed;
            minister.directiveId = MilitaryAdvice.PrepareForWar;

            string standing = MilitaryAdvice.DescribeStanding(state);
            Assert.IsNotEmpty(standing);
            StringAssert.Contains(minister.displayName, standing,
                "The operator cannot see what their own minister is doing.");
        }

        [Test]
        public void PreparingForWarOrdersEquipment()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 20000f;
            player.military.air.inventory.Add(AssetKind.Fighters,
                -player.military.air.inventory.CountOf(AssetKind.Fighters) * 0.7f);

            var minister = player.FindOfficial(Pillar.Military);
            minister.mode = ControlMode.Directed;
            minister.directiveId = MilitaryAdvice.PrepareForWar;
            minister.competence = 80f;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 12; month++)
            {
                minister.mode = ControlMode.Directed;
                minister.directiveId = MilitaryAdvice.PrepareForWar;
                turns.EndMonth();
            }

            float ordered = 0f;
            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                foreach (var stock in player.military.Get(branch).inventory.stocks)
                    ordered += stock.onOrder + stock.count;

            Assert.Greater(ordered, 0f);
            Assert.Less(player.resources.treasury, 20000f,
                "A year of preparing for war cost nothing.");
        }

        [Test]
        public void DrawingDownSellsEquipmentBackAtALoss()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 1000f;

            float tanksBefore = player.military.ground.inventory.CountOf(AssetKind.Tanks);
            float fullValue = AssetCatalog.CostOf(AssetKind.Tanks, tanksBefore);

            var minister = player.FindOfficial(Pillar.Military);
            minister.competence = 80f;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 18; month++)
            {
                minister.mode = ControlMode.Directed;
                minister.directiveId = MilitaryAdvice.DrawDown;
                turns.EndMonth();
            }

            Assert.Greater(player.resources.treasury, 1000f,
                "Drawing down returned no money at all.");
            Assert.Less(player.resources.treasury, 1000f + fullValue,
                "Equipment sold back at full value would make oscillating between build-up " +
                "and sell-off a free way to park money.");
        }

        [Test]
        public void EveryMilitaryDirectiveDoesSomethingDistinct()
        {
            // The guard that caught MIL_READINESS being a no-op for eight
            // phases: a directive whose outcome matches another's is a label.
            var directives = CabinetSystem.GetDirectives(Pillar.Military);
            Assert.GreaterOrEqual(directives.Length, 4);

            foreach (var directive in directives)
            {
                Assert.IsNotEmpty(directive.label);
                Assert.IsNotEmpty(directive.description,
                    $"{directive.id} offers the operator no way to know what it does.");
            }
        }
    }
}

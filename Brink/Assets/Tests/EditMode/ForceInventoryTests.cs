using System;
using Brink.Core;
using Brink.Data;
using Brink.UI;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Counted force structure, procurement, and the after-action analysis that
    /// makes a failure informative (GDD §19 amended, §28.1).
    /// </summary>
    public class ForceInventoryTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 5150);
            state.commandPoints.current = 40;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- the inventory is the truth ----------

        [Test]
        public void EveryCountryOpensWithARealForce()
        {
            foreach (var country in state.countries)
            {
                foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                {
                    var force = country.military.Get(branch);
                    if (force.strength < 1f) continue;   // landlocked navies, correctly empty

                    Assert.IsFalse(force.inventory.IsSpent,
                        $"{country.displayName}'s {branch} has strength {force.strength:F0} and " +
                        "nothing behind it.");
                }
            }
        }

        [Test]
        public void StrengthIsAMirrorOfWhatIsHeld()
        {
            // The direction of authority matters. If strength were authoritative
            // and counts cosmetic, buying two hundred fighters could leave the
            // force exactly as strong — the "written but never read" bug wearing
            // an expensive costume.
            var air = state.PlayerCountry.military.air;
            float before = air.strength;

            air.inventory.Add(AssetKind.Fighters, 400f);
            air.SyncStrength();

            Assert.Greater(air.strength, before,
                "Four hundred more fighters did not make the air force any stronger.");
        }

        [Test]
        public void LosingStrengthLosesActualEquipment()
        {
            var ground = state.PlayerCountry.military.ground;
            float tanksBefore = ground.inventory.CountOf(AssetKind.Tanks);
            Assert.Greater(tanksBefore, 0f);

            ground.SetStrength(ground.strength * 0.5f);

            Assert.Less(ground.inventory.CountOf(AssetKind.Tanks), tanksBefore,
                "Halving the army destroyed no tanks. Counts and strength have drifted apart.");
        }

        [Test]
        public void ALandlockedStateHoldsNoShips()
        {
            var kaz = state.FindCountry("KAZ");
            Assert.AreEqual(0f, kaz.military.naval.inventory.CountOf(AssetKind.Carriers), 0.01f);
            Assert.AreEqual(0f, kaz.military.naval.inventory.CountOf(AssetKind.Submarines), 0.01f);
        }

        [Test]
        public void CountsAreOnAScaleAPersonCanRead()
        {
            // The entire reason counts exist: "air strength 68" is not a fact you
            // can reason about; "912 fighters" is.
            var usa = state.FindCountry("USA");
            float fighters = usa.military.air.inventory.CountOf(AssetKind.Fighters);
            float carriers = usa.military.naval.inventory.CountOf(AssetKind.Carriers);

            Assert.Greater(fighters, 300f, "A major power's fighter count reads as a squadron.");
            Assert.Less(fighters, 3000f);
            Assert.Greater(carriers, 2f);
            Assert.Less(carriers, 30f, "Carrier counts have to stay in single or low double digits.");
        }

        // ---------- fog ----------

        [Test]
        public void AForeignInventoryIsNeverPrintedAsTruth()
        {
            // The rule the whole intelligence pillar rests on. An equipment count
            // is exactly the sort of concrete figure it would be tempting to leak.
            var chn = state.FindCountry("CHN");
            float actual = chn.military.air.inventory.CountOf(AssetKind.Fighters);

            string readout = IntelReadout.ForeignAssetCount(state, "CHN", AssetKind.Fighters);

            Assert.IsFalse(readout.Contains($"{actual:F0}") && !readout.Contains("–"),
                $"The readout printed the true count: {readout}");
            Assert.IsTrue(readout.Contains("–") || readout.Contains("NO ASSESSMENT"),
                $"A foreign count must be a band or nothing. Got: {readout}");
        }

        [Test]
        public void OurOwnInventoryIsExact()
        {
            var player = state.PlayerCountry;
            float fighters = player.military.air.inventory.CountOf(AssetKind.Fighters);
            Assert.Greater(fighters, 0f, "We should know our own order of battle exactly.");
        }

        // ---------- procurement ----------

        [Test]
        public void OrderingCostsMoneyAndArrivesLater()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 10000f;

            float before = player.military.air.inventory.CountOf(AssetKind.Fighters);
            float treasuryBefore = player.resources.treasury;

            var turns = new TurnManager(state);
            Assert.IsTrue(AcquisitionSystem.Order(state, turns, AssetKind.Fighters, 100f));

            Assert.Less(player.resources.treasury, treasuryBefore, "An order has to be paid for.");
            Assert.AreEqual(before, player.military.air.inventory.CountOf(AssetKind.Fighters), 0.01f,
                "Equipment arrived the instant it was ordered. Lead time is the whole reason " +
                "force structure is a strategic decision rather than a tactical one.");
            Assert.Greater(player.military.air.inventory.OnOrderOf(AssetKind.Fighters), 0f);
        }

        [Test]
        public void OrderedEquipmentEventuallyArrivesAndMakesUsStronger()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 40000f;

            float strengthBefore = player.military.air.strength;
            AcquisitionSystem.OrderBy(state, player.id, AssetKind.Fighters, 600f);

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 36; month++) turns.EndMonth();

            Assert.Greater(player.military.air.strength, strengthBefore,
                "Six hundred fighters were bought and delivered and the air force is no stronger.");
        }

        [Test]
        public void AnOrderTheTreasuryCannotCoverIsRefused()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 0f;

            Assert.IsFalse(AcquisitionSystem.OrderBy(state, player.id, AssetKind.Carriers, 4f));
            Assert.AreEqual(0f, player.military.naval.inventory.OnOrderOf(AssetKind.Carriers), 0.01f);
        }

        [Test]
        public void SteelTakesLongerThanPeople()
        {
            Assert.Greater(AssetCatalog.For(AssetKind.Carriers).leadMonths,
                AssetCatalog.For(AssetKind.Soldiers).leadMonths,
                "A carrier and a recruit intake cannot take the same time to arrive.");
        }

        // ---------- war footing ----------

        [Test]
        public void AWarFootingNeedsPoliticalBackingAndAWar()
        {
            var player = state.PlayerCountry;
            player.government.legislativeSupport = 80f;
            state.politicalCapital = 20f;

            Assert.IsFalse(AcquisitionSystem.CanDeclareWarFooting(state, player.id, out string peace));
            StringAssert.Contains("CONFRONTATION", peace,
                "A country at peace has no case to make for an emergency budget.");

            ConfrontationSystem.BeginBy(state, player.id, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, state.ActiveConfrontation,
                EscalationState.LimitedConflict, player.id);

            Assert.IsTrue(AcquisitionSystem.CanDeclareWarFooting(state, player.id, out _));

            player.government.legislativeSupport = 10f;
            Assert.IsFalse(AcquisitionSystem.CanDeclareWarFooting(state, player.id, out string thin));
            StringAssert.Contains("CHAMBER", thin,
                "The gate is political backing, and the refusal has to say so.");
        }

        [Test]
        public void AWarFootingSpeedsDelivery()
        {
            var player = state.PlayerCountry;
            float normal = AcquisitionSystem.DeliveryRateFor(player, AssetKind.Tanks);

            player.military.warFooting = true;
            float surged = AcquisitionSystem.DeliveryRateFor(player, AssetKind.Tanks);

            Assert.Greater(surged, normal * 1.5f,
                "A war footing has to visibly change how fast things arrive, or it is a label.");
        }

        [Test]
        public void AWarFootingLapsesWhenItCannotBePaidFor()
        {
            var player = state.PlayerCountry;
            player.government.legislativeSupport = 80f;
            state.politicalCapital = 20f;

            ConfrontationSystem.BeginBy(state, player.id, "CHN",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, state.ActiveConfrontation,
                EscalationState.LimitedConflict, player.id);
            Assert.IsTrue(AcquisitionSystem.SetWarFooting(state, true));

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 12; month++)
            {
                state.politicalCapital = 0f;
                turns.EndMonth();
            }

            Assert.IsFalse(player.military.warFooting,
                "A government that cannot pay the political cost kept its emergency budget " +
                "running anyway.");
        }

        // ---------- a stripped position falls ----------

        [Test]
        public void AnEmptyPositionIsEasyToTake()
        {
            // The reward for a successful siege has to be an open door, not a
            // slightly cheaper assault.
            var target = FirstTargetIn("MEX");
            var defender = state.FindCountry("MEX");

            target.garrison = 60f;
            target.defenseValue = 60f;
            float held = MilitarySystem.DepletionFactor(defender, target);

            target.garrison = 2f;
            target.defenseValue = 2f;
            float open = MilitarySystem.DepletionFactor(defender, target);

            Assert.Less(open, held * 0.5f,
                "A position with nothing left in it resisted almost as well as a full one.");
        }

        [Test]
        public void AnEmptyPositionIsStillNotAFormality()
        {
            var target = FirstTargetIn("MEX");
            target.garrison = 0f;
            target.defenseValue = 0f;

            Assert.Greater(MilitarySystem.DepletionFactor(state.FindCountry("MEX"), target), 0.05f,
                "A guaranteed capture makes the last step of a campaign a formality rather " +
                "than a decision.");
        }

        // ---------- the after-action explanation ----------

        [Test]
        public void AFailedOperationSaysWhy()
        {
            // The whole point. An outcome with no explanation leaves the operator
            // exactly one strategy: try again until the dice land.
            var confrontation = OpenWar("CHN");
            var target = FirstTargetIn("CHN");
            target.defenseValue = 90f;
            target.garrison = 90f;

            OperationRecord failure = null;
            for (int i = 0; i < 30 && failure == null; i++)
            {
                var record = MilitarySystem.ResolveOperation(state, confrontation,
                    state.playerCountryId, target, OperationType.Assault,
                    new OperationDirective(), new System.Random(i), 0f);
                if (!record.success) failure = record;
            }

            Assert.IsNotNull(failure, "Nothing failed, so this proves nothing.");
            Assert.IsNotEmpty(failure.explanation, "A failure with no explanation.");
            Assert.Greater(failure.oddsAtOrder, 0f, "The assessed odds were not recorded.");

            StringAssert.Contains("%", failure.explanation,
                "The report has to state what the odds actually were — a 23% attempt that " +
                "failed is not the same event as a 71% attempt that failed.");
            StringAssert.Contains("WHAT WOULD CHANGE IT", failure.explanation,
                "Without this line a retry is the only available response.");
        }

        [Test]
        public void TheExplanationNamesTheThingThatActuallyStoppedUs()
        {
            var confrontation = OpenWar("CHN");
            var target = FirstTargetIn("CHN");

            // Overwhelming works, everything else neutral.
            target.defenseValue = 100f;
            target.garrison = 100f;

            OperationRecord failure = null;
            for (int i = 0; i < 40 && failure == null; i++)
            {
                var record = MilitarySystem.ResolveOperation(state, confrontation,
                    state.playerCountryId, target, OperationType.Assault,
                    new OperationDirective(), new System.Random(i), 0f);
                if (!record.success) failure = record;
            }

            Assert.IsNotNull(failure);
            StringAssert.Contains("WHAT DECIDED IT", failure.explanation);
            Assert.IsTrue(failure.explanation.Contains("garrison")
                          || failure.explanation.Contains("fortifications"),
                $"A heavily defended position failed to name its own defences: {failure.explanation}");
        }

        [Test]
        public void AnUnluckyFailureIsNamedAsUnlucky()
        {
            // The difference between "this plan is wrong" and "this plan was
            // fine" is the difference between changing approach and persisting.
            var analysis = new OperationAnalysis { odds = 0.8f };
            string text = analysis.Explain(false, true, "Somewhere", OperationType.Assault);

            StringAssert.Contains("unlucky", text,
                "A plan assessed at 80% that failed must not read the same as one assessed at 15%.");
        }

        [Test]
        public void AHopelessAttemptSaysSoRatherThanEncouragingARetry()
        {
            var analysis = new OperationAnalysis { odds = 0.12f };
            string text = analysis.Explain(false, true, "Somewhere", OperationType.Assault);

            StringAssert.Contains("Repeating it unchanged will fail again", text);
        }

        [Test]
        public void WeLearnFromAttacksAgainstUsToo()
        {
            // What held here is what to build at the next position along.
            var analysis = new OperationAnalysis { odds = 0.3f };
            analysis.Record(OperationAnalysis.Labels.Fortifications, 2.4f, true, "defence value 80");

            string text = analysis.Explain(false, attackerIsUs: false, "Norfolk", OperationType.Assault);
            StringAssert.Contains("WHAT HELD", text);
            StringAssert.DoesNotContain("WHAT WOULD CHANGE IT", text,
                "We should not be advising the enemy on their next attempt.");
        }

        [Test]
        public void EveryFactorLabelHasAdviceBehindIt()
        {
            // `Advice` switches on the label text, so a renamed label silently
            // falls through to the generic line — which is the failure this whole
            // class exists to prevent.
            var labels = new[]
            {
                OperationAnalysis.Labels.Fortifications, OperationAnalysis.Labels.Garrison,
                OperationAnalysis.Labels.EnemyAir, OperationAnalysis.Labels.EnemyNavy,
                OperationAnalysis.Labels.CounterIntelligence, OperationAnalysis.Labels.Reach,
                OperationAnalysis.Labels.Commitment, OperationAnalysis.Labels.Doctrine,
                OperationAnalysis.Labels.MissileDefence, OperationAnalysis.Labels.Insurgency
            };

            string generic = new OperationAnalysis().Advice(OperationType.Assault);

            foreach (var label in labels)
            {
                var analysis = new OperationAnalysis();
                analysis.Record(label, 2f, true, "");
                string advice = analysis.Advice(OperationType.Assault);

                Assert.IsNotEmpty(advice, $"'{label}' produces no advice.");
                Assert.AreNotEqual(generic, advice,
                    $"'{label}' falls through to the generic line — a renamed constant.");
            }
        }

        // ---------- the world uses it too ----------

        [Test]
        public void ForeignGovernmentsBuyEquipmentAndSurgeTheirBudgets()
        {
            bool anyOrdered = false;
            bool anyFooting = false;

            foreach (int seed in new[] { 5150, 7373 })
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);

                for (int month = 0; month < 180; month++)
                {
                    turns.EndMonth();
                    foreach (var country in world.countries)
                    {
                        if (country.id == world.playerCountryId) continue;
                        if (country.military.warFooting) anyFooting = true;
                        foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                            foreach (var stock in country.military.Get(branch).inventory.stocks)
                                if (stock.onOrder > 0.5f) anyOrdered = true;
                    }
                }
            }

            Assert.IsTrue(anyOrdered,
                "No foreign government ordered a single piece of equipment in thirty years.");
            Assert.IsTrue(anyFooting,
                "No foreign government ever moved money to its military, however hard pressed.");
        }

        [Test]
        public void ADecadeStaysDeterministic()
        {
            string Fingerprint(int seed)
            {
                var world = WorldFactory.CreateDebugWorld(seed);
                var turns = new TurnManager(world);
                SimulationPipeline.Wire(turns, world);
                for (int month = 0; month < 120; month++) turns.EndMonth();

                float total = 0f;
                foreach (var country in world.countries)
                    foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                        foreach (var stock in country.military.Get(branch).inventory.stocks)
                            total += stock.count;
                return total.ToString("F2");
            }

            Assert.AreEqual(Fingerprint(4242), Fingerprint(4242));
        }

        // ---------- helpers ----------

        Confrontation OpenWar(string targetId)
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, targetId,
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);
            return confrontation;
        }

        StrategicLocation FirstTargetIn(string countryId)
        {
            foreach (var location in state.locations)
                if (location.originalOwnerId == countryId && location.type != LocationType.Capital)
                    return location;
            return null;
        }
    }
}

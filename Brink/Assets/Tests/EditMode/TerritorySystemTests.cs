using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Territory has to be worth something (GDD §16, §19). Nothing outside the
    /// annual evaluation used to read `ownerId`, so an energy region could change
    /// hands without a single unit of energy changing with it — which is why war
    /// could never pay for itself.
    /// </summary>
    public class TerritorySystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1212);
            turns = new TurnManager(state);
            // NARROW PIPELINE: deliberately three systems — territory plus the two
            // it feeds. Every test here is a controlled before/after on ownership
            // the test sets itself (`SeizeAll`), so the omitted `AISystem`,
            // `ConfrontationSystem`, `CrisisSystem`, `SecessionSystem` and
            // `RegimeSystem` are precisely the things that would take the ground
            // back, hand over more of it, or invent a country to own it, and the
            // measurement would stop attributing anything to territory. The
            // assertions survive because nothing outside these three reads or
            // writes the resource/readiness terms being compared.
            //
            // !! DO NOT ADD GovernmentSystem.MonthlyUpdate HERE. !!
            //
            // Three tests in this file would then pass *without territory doing
            // anything at all*, because their injected starting values drift the
            // direction the assertion looks for:
            //   OccupationIsNotFreeIncome         stability   90 -> target ~65 (falls)
            //   ACountryUnderOccupation_Hardens   warSupport  40 -> target ~50 (rises)
            //   (occupation is corrosive)         unity       80 -> target ~78 (falls)
            // A false pass is worse than a failure, and this one is a single line
            // away. If this fixture ever does need the government tick, convert
            // these three to A/B comparisons against an unoccupied control first,
            // so the drift cancels and the difference is still attributable.
            turns.ResolveMonth += EconomySystem.MonthlyUpdate;
            turns.ResolveMonth += MilitarySystem.MonthlyUpkeep;
            turns.ResolveMonth += TerritorySystem.MonthlyUpdate;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void Run(int months)
        {
            for (int i = 0; i < months; i++) { state.commandPoints.current = 6; turns.EndMonth(); }
        }

        /// <summary>Hand the player every location of a type owned by someone else.</summary>
        int SeizeAll(LocationType type)
        {
            int seized = 0;
            foreach (var location in state.locations)
            {
                if (location.type != type) continue;
                if (location.ownerId == state.playerCountryId) continue;
                location.ownerId = state.playerCountryId;
                seized++;
            }
            return seized;
        }

        [Test]
        public void TakingEnergyRegions_ActuallySuppliesEnergy()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 40f;
            player.resources.energyEndowment = 40f;
            Run(24);
            float withoutConquest = player.resources.energy;

            Assume.That(SeizeAll(LocationType.EnergyRegion), Is.GreaterThan(0),
                "Precondition: there are foreign energy regions to take.");
            player.resources.treasury = 500000f; // occupation upkeep is not what we are measuring
            Run(36);

            Assert.Greater(player.resources.energy, withoutConquest + 5f,
                "Capturing every energy region on the map supplied no energy.");
        }

        [Test]
        public void LosingEnergyRegions_CostsEnergy()
        {
            var player = state.PlayerCountry;
            player.resources.energy = 90f;
            player.resources.energyEndowment = 90f;

            int lost = 0;
            foreach (var location in state.locations)
            {
                if (location.type != LocationType.EnergyRegion) continue;
                if (location.originalOwnerId != player.id) continue;
                location.ownerId = "CHN";
                lost++;
            }
            Assume.That(lost, Is.GreaterThan(0), "Precondition: the player owns energy regions.");

            Run(36);
            Assert.Less(player.resources.energy, 90f,
                "Ground taken from us must cost us what it was producing.");
        }

        [Test]
        public void TakingIndustrialCentres_BuildsCapacity()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 500000f;
            Run(12);
            float before = player.resources.industrialCapacity;

            Assume.That(SeizeAll(LocationType.IndustrialCenter), Is.GreaterThan(0));
            Run(36);

            Assert.Greater(player.resources.industrialCapacity, before,
                "Industrial centres are industry — holding them must show up in capacity.");
        }

        /// <summary>
        /// Measured on air readiness rather than ground: occupation deliberately
        /// ties down the *army*, so ground readiness nets out against the basing
        /// gain. Air reach is what airbases actually buy.
        /// </summary>
        [Test]
        public void HoldingAirbases_ExtendsTheReadinessWeCanSustain()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 500000f;
            player.military.posture = MilitaryPosture.Alert;
            Run(24);
            float before = player.military.air.readiness;

            Assume.That(SeizeAll(LocationType.Airbase), Is.GreaterThan(0),
                "Precondition: the world authors airbases at all.");
            Run(24);

            Assert.Greater(player.military.air.readiness, before,
                "Basing is reach — holding it should raise what the force can sustain.");
        }

        [Test]
        public void OccupyingGround_TiesDownTheArmyThatHoldsIt()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 500000f;
            player.military.posture = MilitaryPosture.Alert;
            Run(24);
            float before = player.military.ground.readiness;

            Assume.That(SeizeAll(LocationType.IndustrialCenter), Is.GreaterThan(0));
            Run(24);

            Assert.Less(player.military.ground.readiness, before,
                "An army sitting on hostile ground is not an army available elsewhere.");
        }

        [Test]
        public void OccupationIsNotFreeIncome_ItCostsMoneyAndUnrest()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 20000f;
            player.stability = 90f;
            float stabilityBefore = player.stability;

            Assume.That(SeizeAll(LocationType.IndustrialCenter), Is.GreaterThan(0));
            float treasuryBefore = player.resources.treasury;
            Run(24);

            Assert.Less(player.resources.treasury, treasuryBefore,
                "Garrisoning hostile ground has to cost something.");
            Assert.Less(player.stability, stabilityBefore,
                "Occupied populations do not consent.");
        }

        [Test]
        public void ACountryUnderOccupation_HardensRatherThanFolds()
        {
            var victim = state.FindCountry("CHN");
            victim.warSupport = 40f;
            victim.nationalUnity = 80f;

            int taken = 0;
            foreach (var location in state.locations)
            {
                if (location.originalOwnerId != "CHN") continue;
                if (location.type == LocationType.Capital) continue;
                location.ownerId = state.playerCountryId;
                taken++;
            }
            Assume.That(taken, Is.GreaterThan(0));

            Run(24);
            Assert.Greater(victim.warSupport, 40f,
                "A country with foreign troops on its soil does not lose the will to fight.");
            Assert.Less(victim.nationalUnity, 80f,
                "But occupation is corrosive to the state that suffers it.");
        }

        [Test]
        public void SeizingGround_CostsUsStandingWithEveryone()
        {
            var player = state.PlayerCountry;
            float diplomacyBefore = player.pillars.diplomacy;
            float indiaTrustBefore = state.FindRelationship(player.id, "IND").trust;

            var target = state.FindLocation("CONTESTED_LANE");
            Assume.That(target.ownerId, Is.Not.EqualTo(player.id));
            TerritorySystem.RecordSeizure(state, target, player.id);

            Assert.Less(player.pillars.diplomacy, diplomacyBefore,
                "Annexation is not diplomatically free.");
            Assert.Less(state.FindRelationship(player.id, "IND").threatPerceptionOfA
                        + state.FindRelationship(player.id, "IND").threatPerceptionOfB,
                        200f);
            Assert.LessOrEqual(state.FindRelationship(player.id, "IND").trust, indiaTrustBefore);
        }

        [Test]
        public void RecoveringOurOwnGround_IsNotAnAnnexation()
        {
            var player = state.PlayerCountry;

            StrategicLocation ours = null;
            foreach (var location in state.locations)
                if (location.originalOwnerId == player.id && location.type != LocationType.Capital)
                { ours = location; break; }
            Assume.That(ours, Is.Not.Null);

            ours.ownerId = "CHN";
            float diplomacyBefore = player.pillars.diplomacy;
            TerritorySystem.RecordSeizure(state, ours, player.id);

            Assert.AreEqual(diplomacyBefore, player.pillars.diplomacy, 0.001f,
                "Taking back what was ours must not be judged as conquest.");
        }

        [Test]
        public void EveryLocationType_ExistsSomewhereInTheAuthoredWorld()
        {
            foreach (LocationType type in System.Enum.GetValues(typeof(LocationType)))
            {
                int count = 0;
                foreach (var location in state.locations)
                    if (location.type == type) count++;

                Assert.Greater(count, 0,
                    $"No authored location uses {type}. A type nothing instantiates "
                    + "is a mechanic that can never be played — airbases were in "
                    + "exactly this state for the whole project.");
            }
        }

        [Test]
        public void TheSwingIsSymmetric_WhatWeGainIsWhatTheyLose()
        {
            var location = state.FindLocation("CONTESTED_LANE");
            string original = location.originalOwnerId;
            string taker = state.playerCountryId;
            Assume.That(original, Is.Not.EqualTo(taker));

            float takerBefore = TerritorySystem.Swing(state, taker, location.type);
            float loserBefore = TerritorySystem.Swing(state, original, location.type);

            location.ownerId = taker;

            float takerGain = TerritorySystem.Swing(state, taker, location.type) - takerBefore;
            float loserLoss = loserBefore - TerritorySystem.Swing(state, original, location.type);

            Assert.AreEqual(takerGain, loserLoss, 0.001f);
            Assert.Greater(takerGain, 0f);
        }
    }
}

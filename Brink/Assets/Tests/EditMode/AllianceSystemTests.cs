using System.Collections.Generic;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    public class AllianceSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 3300);
            turns = new TurnManager(state);
            turns.ResolveMonth += ConfrontationSystem.MonthlyTick;
            turns.ResolveMonth += DiplomacySystem.MonthlyUpdate;
            state.commandPoints.current = 60;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static Treaty DefensePact(string a, string b) => new Treaty
        {
            id = $"T_{a}_{b}",
            countryA = a,
            countryB = b,
            commitments = new List<TreatyCommitment> { TreatyCommitment.MutualDefense }
        };

        /// <summary>CHN attacks IND, who has a defense pact with the given ally.</summary>
        Confrontation OpenWarAgainstIndia()
        {
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.TerritorialConcession, "IND_IND", PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.LimitedConflict, "CHN");
            return confrontation;
        }

        [Test]
        public void Obligations_FireOnlyWhenWarActuallyBegins()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));

            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.Crisis, "CHN");

            Assert.IsFalse(confrontation.obligationsInvoked,
                "Tension and crisis do not invoke a defense commitment.");

            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.LimitedConflict, "CHN");
            Assert.IsTrue(confrontation.obligationsInvoked);
        }

        [Test]
        public void Obligations_AreInvokedOnlyOnce()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));
            var confrontation = OpenWarAgainstIndia();

            var coalition = state.FindCoalitionLedBy(confrontation.id, "IND");
            int membersAfterFirst = coalition?.memberIds.Count ?? 0;

            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, "CHN");

            var after = state.FindCoalitionLedBy(confrontation.id, "IND");
            Assert.AreEqual(membersAfterFirst, after?.memberIds.Count ?? 0,
                "Escalating further must not re-invoke the same obligation.");
        }

        [Test]
        public void WillingAlly_HonorsAndJoinsTheDefendersCoalition()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));

            // Russia is close to India and fears China: it will fight.
            var rusInd = state.FindRelationship("RUS", "IND");
            rusInd.relations = 90f; rusInd.trust = 90f; rusInd.interoperability = 60f;
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.threatPerceptionOfB = 90f;
            rusChn.dependenceAOnB = 0f; rusChn.dependenceBOnA = 0f;
            var rus = state.FindCountry("RUS");
            rus.warExhaustion = 0f; rus.stability = 80f; rus.warSupport = 70f;

            var confrontation = OpenWarAgainstIndia();

            var coalition = state.FindCoalitionLedBy(confrontation.id, "IND");
            Assert.NotNull(coalition, "Honoring should create the defender's coalition.");
            CollectionAssert.Contains(coalition.memberIds, "RUS");
            CollectionAssert.Contains(coalition.memberIds, "IND");
            Assert.IsTrue(rus.military.alertPosture, "An ally at war goes to alert posture.");

            Assert.IsFalse(state.FindTreaty("RUS", "IND").broken);
            Assert.Greater(rusInd.trust, 90f - 0.01f, "Honoring should not damage trust.");
            Assert.Less(rusChn.relations, 50f, "They are now at odds with the aggressor.");
        }

        [Test]
        public void UnwillingAlly_RepudiatesAndPaysReputationally()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));

            // Russia is exhausted, unstable, and dependent on the aggressor.
            var rusInd = state.FindRelationship("RUS", "IND");
            rusInd.relations = 20f; rusInd.trust = 20f;
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.dependenceAOnB = 100f; rusChn.dependenceBOnA = 100f;
            rusChn.threatPerceptionOfB = 0f;
            var rus = state.FindCountry("RUS");
            rus.warExhaustion = 90f; rus.stability = 20f; rus.warSupport = 10f;
            float diplomacyBefore = rus.pillars.diplomacy;

            var usaRus = state.FindRelationship("USA", "RUS");
            float thirdPartyTrustBefore = usaRus.trust;

            OpenWarAgainstIndia();

            Assert.IsNull(state.FindTreaty("RUS", "IND"), "A repudiated pact is no longer in force.");
            Assert.Less(rusInd.trust, 20f);
            Assert.Less(rus.pillars.diplomacy, diplomacyBefore);
            Assert.Less(usaRus.trust, thirdPartyTrustBefore,
                "Every state discounts a guarantee that was not honored.");
        }

        [Test]
        public void HonorWillingness_WeighsDependenceOnTheAggressor()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "IND",
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);

            var rusInd = state.FindRelationship("RUS", "IND");
            rusInd.relations = 70f; rusInd.trust = 70f;
            var rusChn = state.FindRelationship("RUS", "CHN");

            rusChn.SetDependenceOf("RUS", 0f);
            float independent = AllianceSystem.HonorWillingness(state, confrontation, "RUS");

            rusChn.SetDependenceOf("RUS", 100f);
            float dependent = AllianceSystem.HonorWillingness(state, confrontation, "RUS");

            Assert.Greater(independent, dependent,
                "A state that depends on the aggressor is far less willing to fight it.");
        }

        [Test]
        public void PlayerObligation_BecomesABlockingCrisisTurn()
        {
            state.treaties.Add(DefensePact("USA", "IND"));

            OpenWarAgainstIndia();

            Assert.IsTrue(state.HasOpenCrisis, "An alliance call must reach the operator as a decision.");
            var crisis = state.activeCrises[0];
            Assert.AreEqual(AllianceSystem.PlayerObligationCrisisId, crisis.defId);
            Assert.AreEqual(2, crisis.options.Count);
            // The turn is never refused. Saying nothing to a partner who asked for
            // help is itself an answer, and it is the repudiating one.
            Assert.IsTrue(turns.EndMonth(), "The month must advance even with the call unanswered.");
            Assert.IsFalse(state.HasOpenCrisis, "An unanswered call lapses rather than persisting.");
        }

        [Test]
        public void PlayerHonoring_JoinsTheWar()
        {
            state.treaties.Add(DefensePact("USA", "IND"));
            var confrontation = OpenWarAgainstIndia();
            var usaInd = state.FindRelationship("USA", "IND");
            float trustBefore = usaInd.trust;

            CrisisSystem.Resolve(state, state.activeCrises[0], 0); // honor

            var coalition = state.FindCoalitionLedBy(confrontation.id, "IND");
            Assert.NotNull(coalition);
            CollectionAssert.Contains(coalition.memberIds, "USA");
            Assert.Greater(usaInd.trust, trustBefore);
            Assert.IsFalse(state.FindTreaty("USA", "IND").broken);
            Assert.IsTrue(state.PlayerCountry.military.alertPosture);
        }

        [Test]
        public void PlayerRepudiating_CostsStandingEverywhere()
        {
            state.treaties.Add(DefensePact("USA", "IND"));
            OpenWarAgainstIndia();

            var usaInd = state.FindRelationship("USA", "IND");
            var usaChn = state.FindRelationship("USA", "CHN");
            float diplomacyBefore = state.PlayerCountry.pillars.diplomacy;
            float chnTrustBefore = usaChn.trust;

            CrisisSystem.Resolve(state, state.activeCrises[0], 1); // repudiate

            Assert.IsNull(state.FindTreaty("USA", "IND"));
            Assert.Less(usaInd.trust, 30f);
            Assert.Less(state.PlayerCountry.pillars.diplomacy, diplomacyBefore);
            Assert.Less(usaChn.trust, chnTrustBefore, "Even our rivals revise their view of us.");
            Assert.IsTrue(turns.EndMonth(), "The month proceeds once we have answered.");
        }

        [Test]
        public void AggressorIsNeverCalledToDefendAgainstItself()
        {
            // A pact between the two belligerents must not drag the aggressor in.
            state.treaties.Add(DefensePact("CHN", "IND"));
            var confrontation = OpenWarAgainstIndia();

            var coalition = state.FindCoalitionLedBy(confrontation.id, "IND");
            if (coalition != null) CollectionAssert.DoesNotContain(coalition.memberIds, "CHN");
            Assert.IsFalse(state.HasOpenCrisis);
        }

        [Test]
        public void AlliedSupport_AddsWeightToTheDefendersOperations()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));
            var rusInd = state.FindRelationship("RUS", "IND");
            rusInd.relations = 95f; rusInd.trust = 95f; rusInd.interoperability = 80f;
            var rusChn = state.FindRelationship("RUS", "CHN");
            rusChn.threatPerceptionOfB = 95f;
            rusChn.dependenceAOnB = 0f;
            var rus = state.FindCountry("RUS");
            rus.warExhaustion = 0f; rus.stability = 85f; rus.warSupport = 80f;

            var confrontation = OpenWarAgainstIndia();
            Assert.NotNull(state.FindCoalitionLedBy(confrontation.id, "IND"),
                "Precondition: Russia honored the pact.");

            Assert.Greater(DiplomacySystem.CoalitionStrength(state, confrontation, "IND"), 0f,
                "The defender now fights with allied weight behind it.");
            Assert.AreEqual(0f, DiplomacySystem.CoalitionStrength(state, confrontation, "CHN"),
                "The aggressor has no coalition of its own.");
        }

        [Test]
        public void Obligations_SurviveSaveRoundTrip()
        {
            state.treaties.Add(DefensePact("RUS", "IND"));
            var confrontation = OpenWarAgainstIndia();

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.ActiveConfrontationFor("IND");

            Assert.NotNull(restored);
            Assert.IsTrue(restored.obligationsInvoked, "A war must not re-invoke obligations on load.");
            Assert.AreEqual(state.coalitions.Count, loaded.coalitions.Count);
        }
    }
}

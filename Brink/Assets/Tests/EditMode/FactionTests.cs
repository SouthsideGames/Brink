using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Faction arithmetic (GDD §13, spec 05 §2b).
    ///
    /// `Leader.faction` was a display string no rule read: a government of
    /// continuity and one that had just thrown the old party out faced identical
    /// chambers, a junta's grip had nothing to do with the army it stood on, and
    /// `GovernmentType.ParliamentaryRepublic`'s own declaration — "government
    /// falls with confidence" — was implemented by nothing.
    ///
    /// NARROW PIPELINE: these tests run `GovernmentSystem.MonthlyUpdate` alone.
    /// Each pins its own preconditions and asserts this system's arithmetic;
    /// nothing here claims anything about the emergent world, which the
    /// full-pipeline suites cover.
    /// </summary>
    public class FactionTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 6161);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void RunGovernmentMonths(int months, System.Action beforeEachMonth = null)
        {
            for (int i = 0; i < months; i++)
            {
                beforeEachMonth?.Invoke();
                GovernmentSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }
        }

        static void PushElectionsFarOut(GovernmentState gov, GameDate now)
            => gov.nextElectionDate = new GameDate(now.year + 30, now.month);

        // ---------- the chamber ----------

        [Test]
        public void ANewGoverningFactionInheritsAThinnerChamber()
        {
            float SupportAfterSixMonths(string faction)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 6161);
                var gov = world.PlayerCountry.government;
                string leaderBefore = gov.leader.name;

                gov.leader.faction = faction;
                gov.leader.monthsInOffice = 0;
                PushElectionsFarOut(gov, world.date);

                for (int i = 0; i < 6; i++)
                {
                    GovernmentSystem.MonthlyUpdate(world);
                    world.date = world.date.NextMonth();
                }

                Assert.AreEqual(leaderBefore, gov.leader.name,
                    "the fixture did not hold — a leadership change reset the chamber " +
                    "and the comparison measures the honeymoon instead");
                return gov.legislativeSupport;
            }

            float turnover = SupportAfterSixMonths("OPPOSITION");
            float continuity = SupportAfterSixMonths("GOVERNING PARTY");

            Assert.Less(turnover, continuity - 2f,
                $"a government that just threw the old party out holds the chamber " +
                $"({turnover:F1}) as firmly as one of continuity ({continuity:F1}) — " +
                "the faction string is still decoration.");
        }

        [Test]
        public void TheChamberComesAroundWithTime()
        {
            var gov = state.PlayerCountry.government;
            gov.leader.faction = "OPPOSITION";

            gov.leader.monthsInOffice = 0;
            float fresh = GovernmentSystem.FactionSupportShift(gov);

            gov.leader.monthsInOffice = GovernmentSystem.FactionConsolidationMonths;
            float consolidated = GovernmentSystem.FactionSupportShift(gov);

            Assert.Less(fresh, -8f, "a fresh turnover government faces no real penalty");
            Assert.AreEqual(0f, consolidated, 0.01f,
                "the penalty never expires — a term in office and the chamber is still " +
                "somebody else's, which turns a transition into a permanent tax.");
        }

        [Test]
        public void BrokeredSupportRemainsTheWayThrough()
        {
            // The penalty must be answerable with the pillar's own verbs, or a
            // turnover government is a death spiral rather than a hard start.
            var gov = state.PlayerCountry.government;
            gov.leader.faction = "OPPOSITION";
            gov.leader.monthsInOffice = 0;

            float unaided = GovernmentSystem.FactionSupportShift(gov);
            gov.brokeredSupport = 30f;

            // brokeredSupport enters the same target at 0.45 — more than the
            // whole penalty at full strength.
            Assert.Greater(gov.brokeredSupport * 0.45f, -unaided,
                "the workhorse verb cannot outbid the faction penalty at full strength.");
        }

        // ---------- the men with guns, and the committee ----------

        [Test]
        public void AJuntaStandsOnTheOfficerCorps()
        {
            var gov = state.FindCountry("CHN").government;
            gov.leader.faction = "MILITARY COUNCIL";

            gov.militaryLoyalty = 90f;
            float loyal = GovernmentSystem.FactionCohesionShift(gov);
            gov.militaryLoyalty = 30f;
            float mutinous = GovernmentSystem.FactionCohesionShift(gov);

            Assert.Greater(loyal, 0f, "a loyal officer corps lends the junta nothing");
            Assert.Less(mutinous, -5f,
                "the army has turned and the council's grip is unaffected — undermining " +
                "the military is supposed to be the specific way at a coup-born government.");
        }

        [Test]
        public void AProvisionalAuthorityConsolidates()
        {
            var gov = state.FindCountry("CHN").government;
            gov.leader.faction = "PROVISIONAL AUTHORITY";

            gov.leader.monthsInOffice = 0;
            float fresh = GovernmentSystem.FactionCohesionShift(gov);
            gov.leader.monthsInOffice = GovernmentSystem.FactionConsolidationMonths + 6;
            float settled = GovernmentSystem.FactionCohesionShift(gov);

            Assert.Less(fresh, -5f, "a state still deciding whether it is one pays nothing for it");
            Assert.AreEqual(0f, settled, 0.01f, "the provisional discount never expires");
        }

        [Test]
        public void OrdinaryFactionsCarryNoArithmetic()
        {
            // Every string the game writes must either have a rule or cost
            // nothing — a label that penalises by accident of wording is worse
            // than a label that does nothing.
            var electiveGov = state.PlayerCountry.government;
            electiveGov.leader.faction = "GOVERNING PARTY";
            electiveGov.leader.monthsInOffice = 0;
            Assert.AreEqual(0f, GovernmentSystem.FactionSupportShift(electiveGov), 0.01f);

            var internalGov = state.FindCountry("CHN").government;
            internalGov.leader.faction = "PARTY LEADERSHIP";
            internalGov.leader.monthsInOffice = 0;
            Assert.AreEqual(0f, GovernmentSystem.FactionCohesionShift(internalGov), 0.01f);
        }

        // ---------- confidence (the parliamentary promise) ----------

        [Test]
        public void AParliamentaryGovernmentCanFall()
        {
            var germany = state.FindCountry("DEU");
            var gov = germany.government;
            Assert.IsTrue(gov.AllowsEarlyElection, "DEU is expected to be parliamentary");

            string leaderBefore = gov.leader.name;
            PushElectionsFarOut(gov, state.date);

            RunGovernmentMonths(60, () =>
            {
                gov.legislativeSupport = 10f;
                if (gov.leader.name == leaderBefore) PushElectionsFarOut(gov, state.date);
            });

            Assert.AreNotEqual(leaderBefore, gov.leader.name,
                "five years at legislative support 10 and the government stands (seed 6161) — " +
                "the type's own declaration, \"government falls with confidence\", is still " +
                "implemented by nothing.");

            bool chronicled = false;
            foreach (var entry in state.chronicle)
                if (entry.countryId == "DEU" && entry.text.Contains("confidence"))
                    chronicled = true;
            Assert.IsTrue(chronicled, "the fall left no public record");
        }

        [Test]
        public void APresidentialSystemRidesItOut()
        {
            var usa = state.FindCountry("USA");
            var gov = usa.government;
            Assert.IsFalse(gov.AllowsEarlyElection, "USA is expected to be presidential");
            PushElectionsFarOut(gov, state.date);

            RunGovernmentMonths(60, () =>
            {
                gov.legislativeSupport = 10f;
                PushElectionsFarOut(gov, state.date);
            });

            foreach (var entry in state.chronicle)
                if (entry.countryId == "USA")
                    Assert.IsFalse(entry.text.Contains("confidence vote"),
                        "a presidential government fell to a confidence vote — the fixed term " +
                        "is the thing that makes the two elective types different, and it is gone.");
        }

        [Test]
        public void AHealthyChamberNeverBringsTheGovernmentDown()
        {
            var germany = state.FindCountry("DEU");
            var gov = germany.government;
            PushElectionsFarOut(gov, state.date);

            RunGovernmentMonths(60, () =>
            {
                gov.legislativeSupport = 60f;
                PushElectionsFarOut(gov, state.date);
            });

            foreach (var entry in state.chronicle)
                if (entry.countryId == "DEU")
                    Assert.IsFalse(entry.text.Contains("confidence"),
                        "a government with a working majority fell anyway — the mechanic is " +
                        "random rather than conditional.");
        }
    }
}

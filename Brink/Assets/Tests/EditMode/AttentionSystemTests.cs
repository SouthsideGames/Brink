using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Telling the operator what needs looking at (GDD §28.1).
    ///
    /// In a game made entirely of text the hardest problem is not simulating a
    /// decision — it is making sure the player *knows* one is waiting. A
    /// consequence nobody was told about is not a consequence, it is a bug
    /// report.
    ///
    /// The load-bearing rule is that none of it blocks. The operator is running
    /// a government and is allowed to ignore a warning and live with it; a
    /// mistake they chose is the point. What they must never do is make that
    /// mistake without being told.
    /// </summary>
    public class AttentionSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7373);
            turns = new TurnManager(state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- the rule that matters most ----------

        [Test]
        public void AnUnansweredCrisisNeverBlocksTheMonth()
        {
            CrisisSystem.Trigger(state, CrisisSystem.CatalogIds[0]);
            Assert.IsTrue(state.HasOpenCrisis);

            Assert.IsTrue(turns.EndMonth(),
                "The turn must never be refused. The operator is allowed to fail to decide — " +
                "that is a real outcome and a more interesting one than a wall.");
        }

        [Test]
        public void FailingToDecideCostsStandingRatherThanTheTurn()
        {
            var player = state.PlayerCountry;
            CrisisSystem.Trigger(state, CrisisSystem.CatalogIds[0]);

            float approvalBefore = player.governmentApproval;
            float stabilityBefore = player.stability;

            turns.EndMonth();

            Assert.Less(player.governmentApproval, approvalBefore,
                "Inaction has to cost something, or ignoring a crisis is free.");
            Assert.Less(player.stability, stabilityBefore);
            Assert.IsFalse(state.HasOpenCrisis, "An unanswered crisis lapses rather than persisting.");
        }

        [Test]
        public void DriftingIsWorseThanDecidingBadly()
        {
            float Approval(bool decide)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 7373);
                var clock = new TurnManager(world);
                var crisis = CrisisSystem.Trigger(world, CrisisSystem.CatalogIds[0]);

                if (decide)
                {
                    // The worst option on the table — someone still took
                    // responsibility for it.
                    int worst = 0;
                    for (int i = 1; i < crisis.options.Count; i++)
                        if (crisis.options[i].approvalDelta < crisis.options[worst].approvalDelta) worst = i;
                    CrisisSystem.Resolve(world, crisis, worst);
                }

                clock.EndMonth();
                return world.PlayerCountry.governmentApproval;
            }

            Assert.Less(Approval(decide: false), Approval(decide: true),
                "Not deciding must be worse than any option, or the crisis screen is optional.");
        }

        [Test]
        public void AnUnansweredCrisisIsSurvivable()
        {
            // It should hurt, not end the save.
            var player = state.PlayerCountry;
            player.stability = 50f;
            player.governmentApproval = 50f;

            for (int i = 0; i < 12; i++)
            {
                CrisisSystem.Trigger(state, CrisisSystem.CatalogIds[0]);
                turns.EndMonth();
            }

            Assert.Greater(player.stability, 0f,
                "A mistake the player chose should hurt, never be unrecoverable.");
        }

        // ---------- it points at the right panel ----------

        [Test]
        public void AVacantMinistryPointsAtCabinet()
        {
            var player = state.PlayerCountry;
            player.cabinet.Remove(player.FindOfficial(Pillar.Economy));
            player.vacancies.Add(new CabinetVacancy { office = Pillar.Economy });

            Assert.AreEqual(AttentionLevel.Decision,
                AttentionSystem.LevelFor(AttentionSystem.Collect(state), "CABINET"));
        }

        [Test]
        public void UnspentSkillPointsPointAtStrategist()
        {
            state.skillPoints = 3;
            Assert.AreEqual(AttentionLevel.Decision,
                AttentionSystem.LevelFor(AttentionSystem.Collect(state), "STRATEGIST"));
        }

        [Test]
        public void AnOpenCrisisPointsAtTheBriefing()
        {
            CrisisSystem.Trigger(state, CrisisSystem.CatalogIds[0]);
            Assert.AreEqual(AttentionLevel.Decision,
                AttentionSystem.LevelFor(AttentionSystem.Collect(state), "BRIEFING"));
        }

        [Test]
        public void EverySummaryNamesAPanelThatExists()
        {
            state.skillPoints = 2;
            CrisisSystem.Trigger(state, CrisisSystem.CatalogIds[0]);

            var known = new System.Collections.Generic.List<string>
            {
                "BRIEFING", "MAP", "CABINET", "MILITARY", "ECONOMY", "INTELLIGENCE",
                "DIPLOMACY", "GOVERNMENT", "RESEARCH", "STRATEGIC", "STRATEGIST",
                "CHRONICLE", "SYSTEM"
            };

            foreach (var item in AttentionSystem.Collect(state))
            {
                Assert.Contains(item.viewId, known,
                    $"'{item.viewId}' is not a panel — the operator would be sent hunting.");
                Assert.IsNotEmpty(item.summary, "A marker with no explanation is just an alarm.");
            }
        }

        // ---------- markers stop shouting once read ----------

        [Test]
        public void ReadingTheBriefingQuietsUnreadTraffic()
        {
            state.AddNotification(NotificationClass.Priority, "SOMETHING", "body");
            Assert.AreEqual(AttentionLevel.Decision,
                AttentionSystem.LevelFor(AttentionSystem.Collect(state), "BRIEFING"));

            AttentionSystem.MarkBriefingSeen(state);

            Assert.AreEqual(AttentionLevel.None,
                AttentionSystem.LevelFor(AttentionSystem.Collect(state), "BRIEFING"),
                "An item that keeps shouting after it has been read turns every marker " +
                "into wallpaper, and then a real one goes unnoticed.");
        }

        [Test]
        public void AQuietWorldHasNothingToSay()
        {
            AttentionSystem.MarkBriefingSeen(state);
            state.skillPoints = 0;
            var player = state.PlayerCountry;
            player.technology.programs.Add(new ResearchProgram());

            int decisions = AttentionSystem.DecisionCount(AttentionSystem.Collect(state));

            Assert.AreEqual(0, decisions,
                "A month with nothing pending must show no decision markers, or they " +
                "stop meaning anything.");
        }

        // ---------- counting, for the End Month affordance ----------

        [Test]
        public void DecisionsAreCountedSoTheOperatorChoosesKnowingly()
        {
            AttentionSystem.MarkBriefingSeen(state);
            state.skillPoints = 1;
            var player = state.PlayerCountry;
            player.cabinet.Remove(player.FindOfficial(Pillar.Military));
            player.vacancies.Add(new CabinetVacancy { office = Pillar.Military });

            var items = AttentionSystem.Collect(state);

            Assert.GreaterOrEqual(AttentionSystem.DecisionCount(items), 2);
            Assert.AreEqual(AttentionLevel.Decision, AttentionSystem.HighestLevel(items));
        }

        [Test]
        public void CollectIsSafeOnAWorldThatHasNotStarted()
        {
            Assert.IsEmpty(AttentionSystem.Collect(null));
            Assert.DoesNotThrow(() => AttentionSystem.MarkBriefingSeen(null));
        }
    }
}

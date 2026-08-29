using System;
using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// What an army learns by doing (GDD §19, amended).
    ///
    /// The military pillar was the only one that did not compound. The economy
    /// grows, networks deepen, treaties accumulate, institutions strengthen — a
    /// decade of engagement leaves every other playstyle further ahead than it
    /// started. A decade of war left the army where it began, minus the losses,
    /// which is the structural reason militarism graded lowest of the five rather
    /// than anything about conquest yields.
    ///
    /// These tests exist mostly to stop it becoming a free ratchet, which is the
    /// bug family this codebase has now shipped seven times.
    /// </summary>
    public class VeterancyTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7412);
            state.commandPoints.current = 90;
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

        Confrontation OpenWar(string targetId)
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, targetId,
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, state.playerCountryId);
            return confrontation;
        }

        // ---------- fighting teaches ----------

        [Test]
        public void FightingMakesAForceMoreExperienced()
        {
            var confrontation = OpenWar("MEX");
            var target = TargetIn("MEX");
            var ground = state.PlayerCountry.military.ground;
            float before = ground.experience;

            for (int i = 0; i < 6; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.Assault, new OperationDirective(), new Random(i), 0f);

            Assert.Greater(ground.experience, before,
                "Six assaults taught the army nothing. Fighting is the one thing that " +
                "has to build experience, or the whole mechanic has no source.");
        }

        [Test]
        public void OnlyTheBranchesThatFoughtLearn()
        {
            // A blockade is the fleet's work. The army was not there.
            var confrontation = OpenWar("CHN");
            var target = TargetIn("CHN");
            var mil = state.PlayerCountry.military;
            float groundBefore = mil.ground.experience;

            for (int i = 0; i < 6; i++)
                MilitarySystem.ResolveOperation(state, confrontation, state.playerCountryId,
                    target, OperationType.NavalBlockade, new OperationDirective(), new Random(i), 0f);

            Assert.Greater(mil.naval.experience, groundBefore,
                "The fleet ran six blockades and learned nothing.");
            Assert.AreEqual(groundBefore, mil.ground.experience, 0.01f,
                "The army gained experience from a naval blockade it took no part in.");
        }

        [Test]
        public void AHardFightTeachesMoreThanAWalkover()
        {
            // Otherwise the optimal way to build a veteran army is to attack the
            // weakest thing on the map forever, which is not a lesson about war.
            string trace = "";

            float LearnedAgainst(float garrison, float works)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 7412);
                world.commandPoints.current = 90;
                var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId, "MEX",
                    ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
                ConfrontationSystem.SetEscalationBy(world, confrontation,
                    EscalationState.LimitedConflict, world.playerCountryId);

                StrategicLocation target = null;
                foreach (var location in world.locations)
                    if (location.originalOwnerId == "MEX" && location.type != LocationType.Capital)
                    { target = location; break; }

                target.garrison = garrison;
                target.defenseValue = works;

                var ground = world.PlayerCountry.military.ground;
                float before = ground.experience;
                for (int i = 0; i < 5; i++)
                {
                    target.garrison = garrison;      // hold the fight at this difficulty
                    target.defenseValue = works;

                    // **And keep it theirs.** Without this the easy arm captured
                    // the position on the first assault and then "attacked" ground
                    // we already held four more times, banking four free wins. The
                    // walkover was not teaching more because easy fights teach
                    // more — it was running a different experiment.
                    target.ownerId = "MEX";

                    float expBefore = ground.experience;
                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, OperationType.Assault,
                        new OperationDirective(), new Random(i), 0f);

                    trace += $"\n   g{garrison:F0}/w{works:F0} op{i}: odds {record.oddsAtOrder:F2}"
                             + $" {(record.success ? "WON " : "lost")}"
                             + $" exp +{ground.experience - expBefore:F2}";
                }
                return ground.experience - before;
            }

            float hard = LearnedAgainst(95f, 90f);
            float easy = LearnedAgainst(6f, 4f);

            Assert.Greater(hard, easy,
                $"A grinding assault on a defended position taught {hard:F1} and walking into "
                + $"an empty one taught {easy:F1}.{trace}");
        }

        [Test]
        public void LosingTeachesToo()
        {
            // A failed assault is where an army learns what it cannot do. Zero on
            // defeat would make the mechanic a reward for already being strong.
            var confrontation = OpenWar("CHN");
            var target = TargetIn("CHN");
            target.garrison = 100f;
            target.defenseValue = 100f;

            var mil = state.PlayerCountry.military;
            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                mil.Get(branch).SetStrength(8f);   // certain to fail

            float before = mil.ground.experience;
            bool anyWon = false;
            for (int i = 0; i < 8; i++)
            {
                target.garrison = 100f;
                var record = MilitarySystem.ResolveOperation(state, confrontation,
                    state.playerCountryId, target, OperationType.Assault,
                    new OperationDirective(), new Random(i), 0f);
                if (record.success) anyWon = true;
            }

            Assert.IsFalse(anyWon, "The fixture was meant to be unwinnable; it was not.");
            Assert.Greater(mil.ground.experience, before,
                "Eight failed assaults taught the army nothing at all.");
        }

        // ---------- and it is not a ratchet ----------

        [Test]
        public void ExperienceDecaysWithoutUse()
        {
            // The restoring force. Without it a country could fight one war and
            // stay elite for the rest of the save — the one-way-value bug this
            // project has shipped seven times.
            var ground = state.PlayerCountry.military.ground;
            ground.experience = 95f;

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);

            // **Peacetime has to be enforced, not assumed.** The training
            // ceiling only applies when the country is not at war, and it tops
            // out at 48 (`18 + readiness × 0.22 + logistics × 0.08`) — so an army
            // still at 88 after five years means it spent them fighting, not that
            // the decay is broken. Running the live pipeline for sixty months and
            // hoping nobody declares war is a fact about the AI's mood on this
            // seed, which is not what this test is named after.
            for (int month = 0; month < 60; month++)
            {
                foreach (var confrontation in state.confrontations)
                    if (confrontation.Involves(state.playerCountryId)) confrontation.resolved = true;
                turns.EndMonth();
            }

            Assert.IsFalse(state.IsAtWar(state.playerCountryId),
                "the fixture failed to hold the peace, so this measured a war");
            Assert.Less(ground.experience, 80f,
                $"Five peacetime years left the army at {ground.experience:F0}. An army that " +
                "neither fights nor trains has to forget, or veterancy is permanent.");
        }

        [Test]
        public void ReplacementsDiluteWhatTheSurvivorsKnow()
        {
            // This is what makes attrition genuinely costly: a force back to full
            // strength on paper is measurably worse in the field.
            var ground = state.PlayerCountry.military.ground;
            ground.SetStrength(60f);
            ground.experience = 80f;

            ground.AbsorbReplacements(60f);   // as many replacements as survivors

            Assert.Less(ground.experience, 45f,
                $"Doubling the force with green replacements left experience at " +
                $"{ground.experience:F0}. Half the army has never been anywhere.");
            Assert.Greater(ground.experience, 35f,
                "It should be diluted, not erased — the survivors are still there.");
        }

        [Test]
        public void ABloodyVictoryCanLeaveAForceLessExperienced()
        {
            // The whole point of the dilution rule, end to end through the real
            // delivery path rather than by calling AbsorbReplacements directly.
            var player = state.PlayerCountry;
            var ground = player.military.ground;
            ground.experience = 70f;
            player.resources.treasury = 900000f;

            float before = ground.experience;

            // Heavy losses, then buy the force back.
            ground.SetStrength(ground.strength * 0.55f);
            AcquisitionSystem.OrderBy(state, player.id, AssetKind.Soldiers, 400000f);
            AcquisitionSystem.OrderBy(state, player.id, AssetKind.Tanks, 3000f);

            var turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
            for (int month = 0; month < 30; month++) turns.EndMonth();

            Assert.Less(ground.experience, before,
                $"The army took 45% casualties, replaced them, and came out at " +
                $"{ground.experience:F0} against {before:F0}. Buying the bodies back cannot " +
                "buy back what they knew.");
        }

        [Test]
        public void ExercisesAreThePeacetimePathToIt()
        {
            // War games had no effect on capability beyond readiness and trust,
            // which made their exposure cost hard to justify.
            var player = state.PlayerCountry;
            var relationship = state.FindRelationship(player.id, "DEU");
            if (relationship != null) { relationship.relations = 80f; relationship.trust = 75f; }

            float before = player.military.ground.experience;
            state.commandPoints.current = 40;

            var exercise = ExerciseSystem.Conduct(state, new TurnManager(state), "DEU",
                ExerciseScale.Full, ExerciseFocus.Ground);

            Assert.IsNotNull(exercise, "The exercise did not happen, so this measures nothing.");
            Assert.Greater(player.military.ground.experience, before,
                "A full-scale ground exercise taught the army nothing.");
        }

        // ---------- symmetry and effect ----------

        [Test]
        public void ForeignArmiesGainItToo()
        {
            // Anything the player has and the world does not is this codebase's
            // most-repeated bug.
            var chn = state.FindCountry("CHN");
            var rus = state.FindCountry("RUS");
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", "RUS",
                ConfrontationObjective.TerritorialConcession, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation,
                EscalationState.LimitedConflict, "CHN");

            StrategicLocation target = null;
            foreach (var location in state.locations)
                if (location.originalOwnerId == "RUS" && location.type != LocationType.Capital)
                { target = location; break; }

            float attackerBefore = chn.military.ground.experience;
            float defenderBefore = rus.military.ground.experience;

            for (int i = 0; i < 6; i++)
                MilitarySystem.ResolveOperation(state, confrontation, "CHN",
                    target, OperationType.Assault, new OperationDirective(), new Random(i), 0f);

            Assert.Greater(chn.military.ground.experience, attackerBefore,
                "A foreign attacker learned nothing from six assaults.");
            Assert.Greater(rus.military.ground.experience, defenderBefore,
                "A foreign defender learned nothing from being attacked six times.");
        }

        [Test]
        public void AVeteranForceOutfightsAGreenOneOfTheSameSize()
        {
            float WinRate(float experience)
            {
                int wins = 0;
                for (int i = 0; i < 120; i++)
                {
                    var world = WorldFactory.CreateDebugWorld(seed: 7412);
                    foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                    {
                        var force = world.PlayerCountry.military.Get(branch);
                        force.readiness = 75f;
                        force.supply = 75f;
                        force.experience = experience;
                    }

                    var confrontation = ConfrontationSystem.BeginBy(world, world.playerCountryId,
                        "MEX", ConfrontationObjective.TerritorialConcession, null,
                        PrimaryStrategy.Military);
                    ConfrontationSystem.SetEscalationBy(world, confrontation,
                        EscalationState.LimitedConflict, world.playerCountryId);

                    StrategicLocation target = null;
                    foreach (var location in world.locations)
                        if (location.originalOwnerId == "MEX" && location.type != LocationType.Capital)
                        { target = location; break; }

                    var record = MilitarySystem.ResolveOperation(world, confrontation,
                        world.playerCountryId, target, OperationType.Assault,
                        new OperationDirective(), new Random(i), 0f);
                    if (record.success) wins++;
                }
                return wins / 120f;
            }

            float veteran = WinRate(95f);
            float green = WinRate(5f);

            Assert.Greater(veteran, green,
                $"A veteran force won {veteran:P0} and a green one {green:P0} at identical " +
                "strength, readiness and supply. Experience has to be worth something.");

            // But not so much that it beats having more of an army — the factor is
            // deliberately the smallest of the three multipliers on EffectivePower.
            Assert.Less(veteran - green, 0.45f,
                $"Experience swung the win rate by {(veteran - green):P0}. It must matter " +
                "without dominating strength, readiness or supply.");
        }

        [Test]
        public void TheAfterActionReportNamesExperience()
        {
            // The whole direction of this pillar is that a failure explains itself.
            var confrontation = OpenWar("CHN");
            var target = TargetIn("CHN");
            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                state.PlayerCountry.military.Get(branch).experience = 2f;

            var record = MilitarySystem.ResolveOperation(state, confrontation,
                state.playerCountryId, target, OperationType.Assault,
                new OperationDirective(), new Random(3), 0f);

            StringAssert.Contains("unblooded", record.explanation.ToLowerInvariant(),
                "An operation carried out by a force that had never fought did not say so in "
                + $"its after-action report:\n{record.explanation}");
        }

        // ---------- a crash the stochastic harness found first ----------

        [Test]
        public void AStateFracturingMidTickDoesNotCrashTheRegimeSweep()
        {
            // `RegimeSystem.MonthlyUpdate` enumerated `state.countries` while,
            // three calls down, `SecessionSystem.Fracture` appended a breakaway to
            // that same list — InvalidOperationException on the next step.
            //
            // Latent since SecessionSystem landed. Reaching it needs a successful
            // coup followed by an eight-month collapse that neither unity nor
            // military loyalty recovers from, so nothing exercised it
            // deterministically and a thirty-year stochastic run was the first
            // thing to walk the path. This forces the exact conditions instead,
            // which is what makes it a regression test rather than a lottery.
            var target = state.FindCountry("MEX");
            var gov = target.government;

            gov.inCivilConflict = true;
            gov.civilConflictMonthsElapsed = 10;
            gov.militaryLoyalty = 20f;
            target.nationalUnity = 15f;
            target.stability = 12f;

            int before = state.countries.Count;

            Assert.DoesNotThrow(() => RegimeSystem.MonthlyUpdate(state),
                "The roster was modified while the regime sweep was walking it.");

            Assert.GreaterOrEqual(state.countries.Count, before,
                "The sweep lost a country.");
        }

        // ---------- the save ----------

        [Test]
        public void ExperienceSurvivesASaveRoundTrip()
        {
            state.PlayerCountry.military.ground.experience = 63.5f;
            state.PlayerCountry.military.naval.experience = 41.25f;

            var restored = SaveSystem.FromJson(SaveSystem.ToJson(state));

            Assert.AreEqual(63.5f, restored.PlayerCountry.military.ground.experience, 0.01f);
            Assert.AreEqual(41.25f, restored.PlayerCountry.military.naval.experience, 0.01f);
        }

        [Test]
        public void AnOldSaveIsBackfilledRatherThanLeftGreen()
        {
            // Zero is wrong rather than empty here: EffectivePower multiplies by a
            // factor that bottoms out at 0.88, so a migrated world with no
            // experience would quietly weaken every army on earth by ~9%.
            var old = SaveSystem.ToJson(state).Replace("\"saveVersion\":6", "\"saveVersion\":5");

            foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                old = old.Replace($"\"experience\":{state.PlayerCountry.military.Get(branch).experience}", "\"experience\":0");

            var migrated = SaveSystem.FromJson(old);

            foreach (var country in migrated.countries)
                foreach (ForceBranch branch in Enum.GetValues(typeof(ForceBranch)))
                    Assert.Greater(migrated.FindCountry(country.id).military.Get(branch).experience, 5f,
                        $"{country.id}'s {branch} came back from an old save completely green.");
        }
    }
}

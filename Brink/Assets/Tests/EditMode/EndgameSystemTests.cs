using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Strategic endgames (GDD §21). The design rule under test throughout:
    /// these are not generic ultimate buttons. They require capability,
    /// preparation and conditions, and they create systemic consequences.
    /// </summary>
    public class EndgameSystemTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 1500);
            turns = new TurnManager(state);
            turns.ResolveMonth += EndgameSystem.MonthlyUpdate;
            state.commandPoints.current = 90;
            state.PlayerCountry.resources.treasury = 9000f;
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        /// <summary>Give the player everything an instrument needs except conditions.</summary>
        void EnableInstrument(EndgameType type)
        {
            var player = state.PlayerCountry;
            player.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = EndgameSystem.RequiredCapability(type),
                source = CapabilitySource.Developed,
                maturity = 100f
            });
            player.pillars.Set(EndgameSystem.PillarOf(type), 90f);
        }

        void PrepareFully(EndgameType type)
        {
            EnableInstrument(type);
            for (int i = 0; i < 12 && state.PlayerCountry.endgames.ProgressFor(type) < 100f; i++)
            {
                state.commandPoints.current = 90;
                EndgameSystem.Prepare(state, turns, type);
            }
            Assert.AreEqual(100f, state.PlayerCountry.endgames.ProgressFor(type),
                "Precondition: the instrument is prepared.");
        }

        Confrontation OpenWarWith(string targetId, EscalationState escalation)
        {
            var confrontation = ConfrontationSystem.BeginBy(state, state.playerCountryId, targetId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, escalation, state.playerCountryId);
            return confrontation;
        }

        // ---------- gating ----------

        [Test]
        public void Instruments_RequireACapabilityWeActuallyCommand()
        {
            foreach (EndgameType type in System.Enum.GetValues(typeof(EndgameType)))
            {
                Assert.IsFalse(EndgameSystem.CanPrepare(state, state.PlayerCountry, type, out string reason),
                    $"{EndgameSystem.NameOf(type)} should not be available to a state with no capability.");
                StringAssert.Contains("Requires", reason);
            }
        }

        [Test]
        public void HoldingACapabilityShallowly_IsNotCommandingIt()
        {
            var player = state.PlayerCountry;
            player.pillars.military = 90f;
            player.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = EndgameSystem.RequiredCapability(EndgameType.StrategicDestruction),
                source = CapabilitySource.Stolen,
                maturity = 25f
            });

            Assert.IsFalse(EndgameSystem.CanPrepare(state, player, EndgameType.StrategicDestruction, out string reason));
            StringAssert.Contains("do not yet command", reason);
        }

        [Test]
        public void Instruments_RequireAPillarEqualToThem()
        {
            var player = state.PlayerCountry;
            player.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = EndgameSystem.RequiredCapability(EndgameType.SystemicCollapse),
                source = CapabilitySource.Developed,
                maturity = 100f
            });
            player.pillars.economy = 30f;

            Assert.IsFalse(EndgameSystem.CanPrepare(state, player, EndgameType.SystemicCollapse, out _));

            player.pillars.economy = 90f;
            Assert.IsTrue(EndgameSystem.CanPrepare(state, player, EndgameType.SystemicCollapse, out _));
        }

        [Test]
        public void Preparation_TakesRepeatedEffortAndTreasury()
        {
            EnableInstrument(EndgameType.StrategicIsolation);
            float treasuryBefore = state.PlayerCountry.resources.treasury;

            Assert.IsTrue(EndgameSystem.Prepare(state, turns, EndgameType.StrategicIsolation));
            Assert.Less(state.PlayerCountry.endgames.ProgressFor(EndgameType.StrategicIsolation), 100f,
                "One authorization does not prepare a decisive instrument.");
            Assert.Less(state.PlayerCountry.resources.treasury, treasuryBefore);
        }

        [Test]
        public void UnpreparedInstruments_CannotBeUsed()
        {
            EnableInstrument(EndgameType.StrategicIsolation);
            OpenWarWith("CHN", EscalationState.Crisis);

            Assert.IsFalse(EndgameSystem.CanExecute(state, EndgameType.StrategicIsolation, "CHN", out string reason));
            StringAssert.Contains("Preparation", reason);
            Assert.IsFalse(EndgameSystem.Execute(state, turns, EndgameType.StrategicIsolation, "CHN"));
        }

        [Test]
        public void Instruments_RequireAConfrontationNotJustDislike()
        {
            PrepareFully(EndgameType.StrategicIsolation);

            Assert.IsFalse(EndgameSystem.CanExecute(state, EndgameType.StrategicIsolation, "CHN", out string reason));
            StringAssert.Contains("confrontation", reason.ToLowerInvariant());
        }

        [Test]
        public void StrategicDestruction_RequiresTotalWar()
        {
            PrepareFully(EndgameType.StrategicDestruction);
            var confrontation = OpenWarWith("CHN", EscalationState.LimitedConflict);

            Assert.IsFalse(EndgameSystem.CanExecute(state, EndgameType.StrategicDestruction, "CHN", out string reason));
            StringAssert.Contains("total war", reason.ToLowerInvariant());

            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, state.playerCountryId);
            Assert.IsTrue(EndgameSystem.CanExecute(state, EndgameType.StrategicDestruction, "CHN", out _));
        }

        // ---------- consequences ----------

        [Test]
        public void StrategicDestruction_DevastatesThemAndIndictsUs()
        {
            PrepareFully(EndgameType.StrategicDestruction);
            OpenWarWith("CHN", EscalationState.TotalWar);

            var player = state.PlayerCountry;
            var target = state.FindCountry("CHN");
            float targetMilitaryBefore = target.pillars.military;
            float ourDiplomacyBefore = player.pillars.diplomacy;
            float indiaTrustBefore = state.FindRelationship(player.id, "IND").trust;
            float targetWarSupportBefore = target.warSupport;

            Assert.IsTrue(EndgameSystem.Execute(state, turns, EndgameType.StrategicDestruction, "CHN"));

            Assert.Less(target.pillars.military, targetMilitaryBefore - 20f);
            Assert.Greater(target.warSupport, targetWarSupportBefore,
                "They do not fold — they harden.");
            Assert.Less(player.pillars.diplomacy, ourDiplomacyBefore - 10f);
            Assert.Less(state.FindRelationship(player.id, "IND").trust, indiaTrustBefore - 20f,
                "Every state on earth revises its assessment of us, not just the target.");
            Assert.AreEqual(1, state.endgameRecords.Count);
        }

        [Test]
        public void SystemicCollapse_SpreadsThroughEveryoneExposedToThem()
        {
            PrepareFully(EndgameType.SystemicCollapse);
            OpenWarWith("CHN", EscalationState.Crisis);

            var target = state.FindCountry("CHN");
            // Someone else who trades heavily with the target.
            var bystander = state.FindCountry("KOR");
            float bystanderConfidenceBefore = bystander.economy.confidence;
            float targetIndexBefore = target.economy.marketIndex;

            Assert.IsTrue(EndgameSystem.Execute(state, turns, EndgameType.SystemicCollapse, "CHN"));

            Assert.Less(target.economy.marketIndex, targetIndexBefore * 0.6f);
            Assert.Less(bystander.economy.confidence, bystanderConfidenceBefore,
                "Contagion reaches everyone exposed to them (GDD §21).");
        }

        [Test]
        public void StateDestabilization_DoesNotGuaranteeAFriendlySuccessor()
        {
            PrepareFully(EndgameType.StateDestabilization);
            OpenWarWith("IND", EscalationState.Crisis);

            var target = state.FindCountry("IND");
            var relationship = state.FindRelationship(state.playerCountryId, "IND");
            float alignmentBefore = relationship.strategicAlignment;
            float stabilityBefore = target.stability;

            Assert.IsTrue(EndgameSystem.Execute(state, turns, EndgameType.StateDestabilization, "IND"));

            Assert.Less(target.stability, stabilityBefore - 20f);
            Assert.Greater(target.government.conspiracyLevel, 30f, "We have lit the fuse.");
            Assert.AreEqual(alignmentBefore, relationship.strategicAlignment, 0.001f,
                "Fracturing a state does not make its successor ours (GDD §22).");
            Assert.Less(relationship.relations, 50f);
        }

        [Test]
        public void StrategicIsolation_StripsPartnersAccessAndStanding()
        {
            PrepareFully(EndgameType.StrategicIsolation);
            OpenWarWith("RUS", EscalationState.Crisis);

            // The target has a partner and a trade link to lose.
            state.treaties.Add(new Treaty
            {
                id = "T_RUS_KAZ",
                countryA = "RUS",
                countryB = "KAZ",
                commitments = new System.Collections.Generic.List<TreatyCommitment>
                {
                    TreatyCommitment.MutualDefense
                }
            });
            var target = state.FindCountry("RUS");
            var link = state.FindTrade("RUS", "KAZ");
            float volumeBefore = link.volume;
            float diplomacyBefore = target.pillars.diplomacy;

            Assert.IsTrue(EndgameSystem.Execute(state, turns, EndgameType.StrategicIsolation, "RUS"));

            Assert.IsNull(state.FindTreaty("RUS", "KAZ"), "Their partners walk away.");
            Assert.Less(link.volume, volumeBefore, "Their access is cut.");
            Assert.Less(target.pillars.diplomacy, diplomacyBefore - 20f, "Their standing collapses.");
        }

        [Test]
        public void TotalMobilization_IsExtraordinaryEffortAtExtraordinaryCost()
        {
            PrepareFully(EndgameType.TotalMobilization);
            var player = state.PlayerCountry;
            player.nationalUnity = 70f;

            Assert.IsTrue(EndgameSystem.Execute(state, turns, EndgameType.TotalMobilization, null));
            Assert.IsTrue(player.endgames.totalMobilization);
            Assert.AreEqual(MilitaryPosture.Forward, player.military.posture);

            float treasuryBefore = player.resources.treasury;
            float approvalBefore = player.governmentApproval;
            float militaryBefore = player.pillars.military;

            for (int i = 0; i < 6; i++) turns.EndMonth();

            Assert.Less(player.resources.treasury, treasuryBefore, "It is being paid for every month.");
            Assert.Less(player.governmentApproval, approvalBefore, "And it wears on the country.");
            Assert.Greater(player.pillars.military, militaryBefore, "But it does deliver.");

            for (int i = 0; i < 14; i++) turns.EndMonth();
            Assert.IsFalse(player.endgames.totalMobilization, "A country cannot stay mobilized forever.");
        }

        [Test]
        public void TotalMobilization_NeedsACountryThatWillFollow()
        {
            PrepareFully(EndgameType.TotalMobilization);
            state.PlayerCountry.nationalUnity = 20f;

            Assert.IsFalse(EndgameSystem.CanExecute(state, EndgameType.TotalMobilization, null, out string reason));
            StringAssert.Contains("divided", reason.ToLowerInvariant());
        }

        [Test]
        public void UsingAnInstrument_SpendsIt()
        {
            PrepareFully(EndgameType.StrategicIsolation);
            OpenWarWith("CHN", EscalationState.Crisis);

            Assert.IsTrue(EndgameSystem.Execute(state, turns, EndgameType.StrategicIsolation, "CHN"));
            Assert.Less(state.PlayerCountry.endgames.ProgressFor(EndgameType.StrategicIsolation), 100f,
                "The instrument is spent and must be rebuilt.");
            Assert.IsTrue(state.PlayerCountry.endgames.Find(EndgameType.StrategicIsolation).everUsed);
        }

        [Test]
        public void ExistentialInstruments_JustifyAMilitaryResponse()
        {
            Assert.IsTrue(EndgameSystem.JustifiesMilitaryResponse(EndgameType.StrategicDestruction));
            Assert.IsTrue(EndgameSystem.JustifiesMilitaryResponse(EndgameType.SystemicCollapse),
                "An existential economic attack can justify a military answer (GDD §21).");
            Assert.IsFalse(EndgameSystem.JustifiesMilitaryResponse(EndgameType.TotalMobilization),
                "Mobilizing ourselves is not an attack on anyone.");
        }

        // ---------- the world can do this to us too ----------

        [Test]
        public void OtherStates_AnswerToTheSameConditionsWeDo()
        {
            var china = state.FindCountry("CHN");
            china.pillars.military = 95f;
            china.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StrategicDestruction,
                progress = 100f
            });

            // Prepared, but no confrontation: still not usable.
            Assert.IsFalse(EndgameSystem.CanExecuteBy(state, "CHN", EndgameType.StrategicDestruction,
                state.playerCountryId, out string reason));
            StringAssert.Contains("confrontation", reason.ToLowerInvariant());

            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.TotalWar, "CHN");

            Assert.IsTrue(EndgameSystem.CanExecuteBy(state, "CHN", EndgameType.StrategicDestruction,
                state.playerCountryId, out _));
        }

        [Test]
        public void AnInstrumentUsedOnUs_HurtsUsAndCostsThemStanding()
        {
            var china = state.FindCountry("CHN");
            china.pillars.economy = 95f;
            china.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.SystemicCollapse,
                progress = 100f
            });
            ConfrontationSystem.BeginBy(state, "CHN", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Economic);

            var player = state.PlayerCountry;
            float confidenceBefore = player.economy.confidence;
            float theirDiplomacyBefore = china.pillars.diplomacy;

            Assert.IsTrue(EndgameSystem.ExecuteBy(state, "CHN", EndgameType.SystemicCollapse,
                state.playerCountryId));

            Assert.Less(player.economy.confidence, confidenceBefore - 30f);
            Assert.Less(china.pillars.diplomacy, theirDiplomacyBefore);
            Assert.AreEqual("CHN", state.endgameRecords[0].actorId);
        }

        [Test]
        public void AnExistentialAttack_UnsealsTheHighestResponse()
        {
            var china = state.FindCountry("CHN");
            china.pillars.economy = 95f;
            china.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.SystemicCollapse,
                progress = 100f
            });
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Economic);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.Crisis, "CHN");

            EndgameSystem.ExecuteBy(state, "CHN", EndgameType.SystemicCollapse, state.playerCountryId);

            Assert.AreEqual(EscalationState.TotalWar, confrontation.escalation,
                "An existential non-military attack justifies a military answer (GDD §21).");
        }

        [Test]
        public void ASevereButNonExistentialAct_DoesNotAutomaticallyMeanWar()
        {
            var china = state.FindCountry("CHN");
            china.pillars.diplomacy = 95f;
            china.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StrategicIsolation,
                progress = 100f
            });
            var confrontation = ConfrontationSystem.BeginBy(state, "CHN", state.playerCountryId,
                ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);
            ConfrontationSystem.SetEscalationBy(state, confrontation, EscalationState.Crisis, "CHN");

            EndgameSystem.ExecuteBy(state, "CHN", EndgameType.StrategicIsolation, state.playerCountryId);

            Assert.AreEqual(EscalationState.Crisis, confrontation.escalation);
        }

        [Test]
        public void ForeignPreparations_AreNotPublicInformation()
        {
            var china = state.FindCountry("CHN");
            china.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StateDestabilization,
                progress = 30f
            });

            Assert.AreEqual(-1f, EndgameSystem.KnownPreparation(state, state.playerCountryId,
                "CHN", EndgameType.StateDestabilization),
                "With no collection against them, an early programme is invisible.");

            state.networks.Add(new IntelNetwork
            {
                ownerId = state.playerCountryId,
                targetId = "CHN",
                focus = IntelDomain.Political,
                penetration = 70f
            });

            Assert.AreEqual(30f, EndgameSystem.KnownPreparation(state, state.playerCountryId,
                "CHN", EndgameType.StateDestabilization), 0.01f);
        }

        [Test]
        public void AProgrammeNearingReadiness_LeaksEvenWithoutCollection()
        {
            var china = state.FindCountry("CHN");
            china.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StateDestabilization,
                progress = 100f
            });

            Assert.Greater(EndgameSystem.KnownPreparation(state, state.playerCountryId,
                "CHN", EndgameType.StateDestabilization), 0f,
                "Movement, spending and people make a finished programme hard to hide.");
        }

        [Test]
        public void AIPrepares_OnlyAgainstARivalItHasFacedForYears()
        {
            var china = state.FindCountry("CHN");
            china.pillars.intelligence = 90f;
            china.resources.treasury = 5000f;
            china.technology.capabilities.Add(new HeldCapability
            {
                capabilityId = EndgameSystem.RequiredCapability(EndgameType.StateDestabilization),
                source = CapabilitySource.Developed,
                maturity = 100f
            });

            var ai = state.FindAI("CHN");
            ai.profile.patience = 90f;
            ai.objectives.Clear();
            ai.objectives.Add(new AIObjective
            {
                type = AIObjectiveType.CounterRival,
                targetId = state.playerCountryId,
                priority = 90f
            });

            for (int i = 0; i < 6; i++) AISystem.MonthlyThink(state);
            Assert.AreEqual(0f, china.endgames.ProgressFor(EndgameType.StateDestabilization),
                "A rivalry a few months old does not justify building this.");
        }

        [Test]
        public void RivalryOutlivesTheObjectivesThatExpressIt()
        {
            // Real, sustained hostility — not a hand-placed objective, which
            // FormObjectives would simply re-score away.
            var relationship = state.FindRelationship("CHN", state.playerCountryId);
            relationship.relations = 5f;
            relationship.trust = 0f;
            relationship.SetThreatPerceivedBy("CHN", 95f);

            var ai = state.FindAI("CHN");
            for (int i = 0; i < 30; i++) AISystem.MonthlyThink(state);

            Assert.Greater(ai.RivalryMonths(state.playerCountryId), 12,
                "Objectives churn every few months; the rivalry underneath them must not.");
        }

        [Test]
        public void RivalryFadesWhenThePressureComesOff()
        {
            var relationship = state.FindRelationship("CHN", state.playerCountryId);
            relationship.relations = 5f;
            relationship.trust = 0f;
            relationship.SetThreatPerceivedBy("CHN", 95f);

            var ai = state.FindAI("CHN");
            for (int i = 0; i < 24; i++) AISystem.MonthlyThink(state);
            int peak = ai.RivalryMonths(state.playerCountryId);
            Assert.Greater(peak, 0, "Precondition: a rivalry formed.");

            // Détente.
            relationship.relations = 85f;
            relationship.trust = 80f;
            relationship.SetThreatPerceivedBy("CHN", 0f);
            for (int i = 0; i < 36; i++) AISystem.MonthlyThink(state);

            Assert.Less(ai.RivalryMonths(state.playerCountryId), peak,
                "Enmity fades — slowly — once nobody is feeding it.");
        }

        [Test]
        public void OverALongGame_SomeGovernmentBuildsAnInstrument()
        {
            // The player must not be the only actor who can reach these.
            var world = WorldFactory.CreateDebugWorld(seed: 90210);
            world.difficulty = Difficulty.Ruthless;
            var manager = new TurnManager(world);
            manager.ResolveMonth += TechnologySystem.MonthlyUpdate;
            manager.ResolveMonth += EndgameSystem.MonthlyUpdate;
            manager.ResolveMonth += AISystem.MonthlyThink;

            foreach (var country in world.countries)
            {
                if (country.isPlayer) continue;
                country.resources.treasury = 30000f;
            }

            bool anyPrepared = false;
            for (int month = 0; month < 240 && !anyPrepared; month++)
            {
                world.commandPoints.current = 20;
                manager.EndMonth();
                foreach (var country in world.countries)
                {
                    if (country.isPlayer) continue;
                    if (country.endgames.preparations.Count > 0) anyPrepared = true;
                }
            }

            Assert.IsTrue(anyPrepared,
                "Across twenty years of a hostile world, someone should reach for a decisive instrument.");
        }

        [Test]
        public void ASecondUse_IsJudgedAsAPatternNotAnIncident()
        {
            float FirstUseTrustLoss()
            {
                var world = WorldFactory.CreateDebugWorld(seed: 4242);
                var manager = new TurnManager(world);
                var actor = world.PlayerCountry;
                actor.pillars.diplomacy = 95f;
                actor.endgames.preparations.Add(new EndgamePreparation
                {
                    type = EndgameType.StrategicIsolation,
                    progress = 100f
                });
                ConfrontationSystem.BeginBy(world, actor.id, "CHN",
                    ConfrontationObjective.Deterrence, null, PrimaryStrategy.Diplomatic);

                float before = actor.pillars.diplomacy;
                EndgameSystem.ExecuteBy(world, actor.id, EndgameType.StrategicIsolation, "CHN");
                return before - actor.pillars.diplomacy;
            }

            float firstCost = FirstUseTrustLoss();

            // Same act, by a state that has already done this twice.
            var player = state.PlayerCountry;
            player.pillars.diplomacy = 95f;
            for (int i = 0; i < 2; i++)
                state.endgameRecords.Add(new EndgameRecord
                {
                    date = state.date,
                    type = EndgameType.StrategicIsolation,
                    actorId = player.id,
                    targetId = "RUS",
                    severity = StrategicSeverity.Severe,
                    summary = "prior use"
                });
            player.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StrategicIsolation,
                progress = 100f
            });
            OpenWarWith("CHN", EscalationState.Crisis);

            float diplomacyBefore = player.pillars.diplomacy;
            EndgameSystem.ExecuteBy(state, player.id, EndgameType.StrategicIsolation, "CHN");
            float repeatCost = diplomacyBefore - player.pillars.diplomacy;

            Assert.Greater(repeatCost, firstCost,
                "A state known for reaching for these pays more each time (GDD §21).");
            Assert.AreEqual(1f, EndgameSystem.Recidivism(state, "IND"), 0.001f,
                "A state that has never done this is judged on the incident alone.");
        }

        [Test]
        public void ADetectedProgramme_ReordersHowARivalIsSeen()
        {
            var china = state.FindCountry("CHN");
            var ai = state.FindAI("CHN");

            // Baseline: a placid world produces no rivalry with us.
            for (int i = 0; i < 8; i++) AISystem.MonthlyThink(state);
            int quiet = ai.RivalryMonths(state.playerCountryId);

            // Now we finish something that can destroy them, and they see it.
            state.PlayerCountry.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.StrategicDestruction,
                progress = 100f
            });
            state.networks.Add(new IntelNetwork
            {
                ownerId = "CHN",
                targetId = state.playerCountryId,
                focus = IntelDomain.Military,
                penetration = 80f
            });
            ai.objectives.Clear();

            for (int i = 0; i < 8; i++) AISystem.MonthlyThink(state);

            Assert.Greater(ai.RivalryMonths(state.playerCountryId), quiet,
                "A state that can see an existential programme aimed its way treats us as a rival.");
        }

        [Test]
        public void Endgames_SurviveSaveRoundTrip()
        {
            PrepareFully(EndgameType.TotalMobilization);
            state.PlayerCountry.nationalUnity = 70f;
            EndgameSystem.Execute(state, turns, EndgameType.TotalMobilization, null);

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.PlayerCountry.endgames;

            Assert.IsTrue(restored.totalMobilization);
            Assert.AreEqual(state.PlayerCountry.endgames.mobilizationMonthsRemaining,
                            restored.mobilizationMonthsRemaining);
            Assert.AreEqual(1, loaded.endgameRecords.Count);
            Assert.AreEqual(EndgameType.TotalMobilization, loaded.endgameRecords[0].type);
            Assert.IsTrue(restored.Find(EndgameType.TotalMobilization).everUsed);
        }
    }
}

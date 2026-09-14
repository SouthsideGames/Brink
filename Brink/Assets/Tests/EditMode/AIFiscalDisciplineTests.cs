using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Phase C4: an AI government may finish discretionary programmes it has
    /// already authorized, but it may not add new research or strategic
    /// preparation once sovereign debt reaches the C3 lock-in threshold or the
    /// shared fiscal signal has become DebtStressed/Crisis. The player is
    /// deliberately exempt: debt-financing a programme remains an operator
    /// choice.
    /// </summary>
    public class AIFiscalDisciplineTests
    {
        GameState state;
        CountryState ai;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 4242);
            state.difficulty = Difficulty.Challenging;

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                ai = country;
                break;
            }
            Assert.IsNotNull(ai, "debug world has no AI country");
            MakeSound(ai, 0f);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        static void MakeSound(CountryState country, float debtRatio)
        {
            country.resources.treasury = 1000f;
            country.fiscal.sovereignDebt = country.economy.gdp * debtRatio / 100f;
            country.fiscal.creditStanding = 80f;
            country.fiscal.deficitFinancedMonths = 0;
            country.fiscal.restructuringMemoryMonths = 0;
        }

        static CapabilityDef FreshBaseCapability(CountryState country)
        {
            country.technology.programs.Clear();
            foreach (var definition in CapabilityCatalog.Definitions)
            {
                if (definition.prerequisites.Length != 0) continue;
                if (country.technology.Has(definition.id)) continue;
                return definition;
            }
            Assert.Fail("no unheld prerequisite-free capability available for test");
            return null;
        }

        static void MeetResearchRequirements(CountryState country, CapabilityDef definition)
        {
            country.resources.treasury = 1000f;
            country.resources.industrialCapacity = 100f;
            switch (definition.pillar)
            {
                case Pillar.Military: country.pillars.military = 100f; break;
                case Pillar.Economy: country.pillars.economy = 100f; break;
                case Pillar.Diplomacy: country.pillars.diplomacy = 100f; break;
                case Pillar.Intelligence: country.pillars.intelligence = 100f; break;
                default: country.pillars.government = 100f; break;
            }
        }

        static void MeetMobilizationRequirements(CountryState country)
        {
            country.resources.treasury = 1000f;
            country.pillars.government = 100f;
            var held = country.technology.Find("CAP_CIVADMIN");
            if (held == null)
            {
                country.technology.capabilities.Add(new HeldCapability
                {
                    capabilityId = "CAP_CIVADMIN",
                    source = CapabilitySource.Developed,
                    maturity = 100f
                });
            }
            else
            {
                held.maturity = 100f;
            }
        }

        [Test]
        public void AiMayStartBelowDebtCeilingWhenOtherwiseSound()
        {
            MakeSound(ai, AIFiscalDiscipline.NewProgrammeDebtCeiling - 0.1f);
            Assert.IsTrue(AIFiscalDiscipline.CanStartNewDiscretionaryProgramme(state, ai));
        }

        [Test]
        public void AiIsBlockedAtExactDebtCeiling()
        {
            MakeSound(ai, AIFiscalDiscipline.NewProgrammeDebtCeiling);
            Assert.IsFalse(AIFiscalDiscipline.CanStartNewDiscretionaryProgramme(state, ai));
        }

        [Test]
        public void SharedDebtStressSignalBlocksBelowDebtCeiling()
        {
            MakeSound(ai, 40f);
            ai.fiscal.creditStanding = 20f;
            Assert.AreEqual(FiscalCondition.DebtStressed, FiscalSystem.ConditionOf(state, ai));
            Assert.IsFalse(AIFiscalDiscipline.CanStartNewDiscretionaryProgramme(state, ai));
        }

        [Test]
        public void PlayerIsNeverBlockedByAiFiscalGate()
        {
            var player = state.PlayerCountry;
            MakeSound(player, 180f);
            player.fiscal.creditStanding = 5f;
            Assert.IsTrue(AIFiscalDiscipline.CanStartNewDiscretionaryProgramme(state, player));
        }

        [Test]
        public void PlayerResearchEligibilityIgnoresAiFiscalGate()
        {
            var player = state.PlayerCountry;
            var definition = FreshBaseCapability(player);
            MakeSound(player, 180f);
            player.fiscal.creditStanding = 5f;
            MeetResearchRequirements(player, definition);

            Assert.IsTrue(TechnologySystem.CanResearch(state, player, definition.id, out string reason), reason);
        }

        [Test]
        public void ActorGenericResearchRefusesFreshAiProgrammeAtCeiling()
        {
            var definition = FreshBaseCapability(ai);
            MeetResearchRequirements(ai, definition);
            MakeSound(ai, AIFiscalDiscipline.NewProgrammeDebtCeiling);
            MeetResearchRequirements(ai, definition);

            Assert.IsFalse(TechnologySystem.BeginResearchBy(state, ai.id, definition.id));
            Assert.IsFalse(ai.technology.IsResearching(definition.id));
        }

        [Test]
        public void ActorGenericResearchStillAllowsSoundAiProgramme()
        {
            var definition = FreshBaseCapability(ai);
            MakeSound(ai, AIFiscalDiscipline.NewProgrammeDebtCeiling - 0.1f);
            MeetResearchRequirements(ai, definition);

            Assert.IsTrue(TechnologySystem.BeginResearchBy(state, ai.id, definition.id));
            Assert.IsTrue(ai.technology.IsResearching(definition.id));
        }

        [Test]
        public void ExistingResearchContinuesAfterCountryCrossesDebtCeiling()
        {
            var definition = FreshBaseCapability(ai);
            MakeSound(ai, AIFiscalDiscipline.NewProgrammeDebtCeiling - 0.1f);
            MeetResearchRequirements(ai, definition);
            Assert.IsTrue(TechnologySystem.BeginResearchBy(state, ai.id, definition.id));

            var programme = ai.technology.programs[0];
            int before = programme.monthsRemaining;
            ai.fiscal.sovereignDebt = ai.economy.gdp * 1.25f;
            ai.resources.treasury = 1000f;

            TechnologySystem.MonthlyUpdate(state);

            Assert.AreEqual(before - 1, programme.monthsRemaining,
                "C4 must not cancel or freeze research already under way");
        }

        [Test]
        public void FreshAiStrategicPreparationIsBlockedAtCeiling()
        {
            MakeSound(ai, AIFiscalDiscipline.NewProgrammeDebtCeiling);
            MeetMobilizationRequirements(ai);

            Assert.IsFalse(EndgameSystem.PrepareBy(state, ai.id, EndgameType.TotalMobilization));
            Assert.AreEqual(0f, ai.endgames.ProgressFor(EndgameType.TotalMobilization), 0.001f);
        }

        [Test]
        public void ExistingAiStrategicPreparationMayContinueWhileStressed()
        {
            MakeSound(ai, 125f);
            MeetMobilizationRequirements(ai);
            ai.endgames.preparations.Add(new EndgamePreparation
            {
                type = EndgameType.TotalMobilization,
                progress = 10f
            });

            Assert.IsTrue(EndgameSystem.PrepareBy(state, ai.id, EndgameType.TotalMobilization));
            Assert.Greater(ai.endgames.ProgressFor(EndgameType.TotalMobilization), 10f,
                "C4 must allow an already-authorized strategic programme to finish");
        }

        [Test]
        public void PlayerMayStartStrategicPreparationDespiteHighDebt()
        {
            var player = state.PlayerCountry;
            MakeSound(player, 180f);
            player.fiscal.creditStanding = 5f;
            MeetMobilizationRequirements(player);

            Assert.IsTrue(EndgameSystem.PrepareBy(state, player.id, EndgameType.TotalMobilization));
            Assert.Greater(player.endgames.ProgressFor(EndgameType.TotalMobilization), 0f);
        }
    }
}

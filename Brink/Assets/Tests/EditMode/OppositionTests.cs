using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// The domestic antagonist (GDD §13, §27).
    ///
    /// The claim under test is that the *theme* is the decision: a case built on
    /// hardship or a war cannot be denied, a case built on mood can, and
    /// conceding always works and always costs. Everything else — that the case
    /// is built from the record, that it comes apart when the record improves,
    /// that it is read at the ballot and in the chamber — is the machinery that
    /// makes that decision matter.
    /// </summary>
    public class OppositionTests
    {
        GameState state;
        TurnManager turns;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 7742);
            state.commandPoints.current = 60;
            state.politicalCapital = GameState.PoliticalCapitalCap;
            state.authorizedPillarMask = ~0;

            turns = new TurnManager(state);
            SimulationPipeline.Wire(turns, state);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void MakeDestitute(CountryState country)
        {
            country.livingStandards = 8f;
            country.publicGrievance = 80f;
            country.economy.inflation = 22f;
            country.economy.unemployment = 24f;
            country.governmentApproval = 20f;
        }

        // ---------- it is built out of the record ----------

        [Test]
        public void AWellRunCountryFacesNoSeriousCase()
        {
            var player = state.PlayerCountry;
            player.livingStandards = 78f;
            player.publicGrievance = 4f;
            player.socialUnrest = 3f;
            player.governmentApproval = 70f;
            player.warExhaustion = 0f;
            player.pillars.government = 78f;
            player.economy.inflation = 2f;
            player.economy.unemployment = 4f;
            player.government.conspiracyLevel = 0f;
            player.government.eliteCohesion = 75f;
            player.government.civicPosture = CivicPosture.Standard;

            float target = OppositionSystem.CaseTargetFor(state, player, out OppositionTheme theme);

            Assert.Less(target, OppositionSystem.NoiseFloor,
                $"A prosperous, approved-of, peaceful government still faced a serious case "
                + $"({theme}, {target:F0}). An opposition must not be weather.");
        }

        [Test]
        public void HardshipIsTheCaseWhenThePeopleAreHungry()
        {
            var player = state.PlayerCountry;
            MakeDestitute(player);
            player.government.civicPosture = CivicPosture.Standard;

            float target = OppositionSystem.CaseTargetFor(state, player, out OppositionTheme theme);

            Assert.AreEqual(OppositionTheme.Hardship, theme,
                "A destitute country with 22% inflation produced some other grievance.");
            Assert.Greater(target, 45f, "the hardship case was not strong enough to be a campaign");
        }

        [Test]
        public void ACaseComesApartWhenTheRecordImproves()
        {
            var player = state.PlayerCountry;
            MakeDestitute(player);
            player.government.oppositionCase = 70f;
            player.government.oppositionTheme = OppositionTheme.Hardship;

            // Fix the country. Nothing here touches the case directly.
            player.livingStandards = 80f;
            player.publicGrievance = 3f;
            player.economy.inflation = 2f;
            player.economy.unemployment = 4f;
            player.governmentApproval = 72f;
            player.pillars.government = 75f;

            for (int month = 0; month < 60; month++)
            {
                turns.EndMonth();
                player.livingStandards = 80f;
                player.publicGrievance = 3f;
                player.economy.inflation = 2f;
                player.economy.unemployment = 4f;
                player.governmentApproval = 72f;
            }

            Assert.Less(player.government.oppositionCase, 30f,
                "Five years of a genuinely improved record left the case against the "
                + "government where it was. This is the one-way-value bug, and it has been "
                + "shipped enough times already.");
        }

        // ---------- the theme decides the answer ----------

        [Test]
        public void ShoutingAtAHungryCountryMakesItWorse()
        {
            var player = state.PlayerCountry;
            MakeDestitute(player);
            player.government.civicPosture = CivicPosture.Standard;
            player.government.oppositionTheme = OppositionTheme.Hardship;
            player.government.oppositionCase = 60f;

            float before = player.government.oppositionCase;
            Assert.IsTrue(OppositionSystem.ConfrontBy(state, player.id),
                "the confrontation was refused");

            Assert.GreaterOrEqual(player.government.oppositionCase, before,
                "Denying that people cannot afford to live reduced the argument that they "
                + "cannot afford to live. The country can see it from every kitchen.");
        }

        [Test]
        public void MoodCanBeAnsweredInPublic()
        {
            var player = state.PlayerCountry;
            player.government.oppositionTheme = OppositionTheme.Drift;
            player.government.oppositionCase = 60f;

            float before = player.government.oppositionCase;
            Assert.IsTrue(OppositionSystem.ConfrontBy(state, player.id));

            Assert.Less(player.government.oppositionCase, before - 15f,
                "A case made entirely of 'they have run out of ideas' survived being "
                + "answered in public, which is the one thing that should work on it.");
        }

        [Test]
        public void ConcedingAlwaysWorksAndAlwaysCosts()
        {
            var player = state.PlayerCountry;
            player.resources.treasury = 20000f;
            MakeDestitute(player);
            player.government.oppositionTheme = OppositionTheme.Hardship;
            player.government.oppositionCase = 70f;

            float treasury = player.resources.treasury;
            float before = player.government.oppositionCase;

            Assert.IsTrue(OppositionSystem.ConcedeBy(state, player.id));

            Assert.Less(player.government.oppositionCase, before - 20f,
                "Conceding ground did not weaken the case it conceded to.");
            Assert.Less(player.resources.treasury, treasury,
                "A hardship concession cost nothing. A promise with no programme behind it "
                + "is a speech, and speeches are the other verb.");
        }

        [Test]
        public void ConcedingOnLibertyGivesUpTheInstrument()
        {
            var player = state.PlayerCountry;
            player.government.civicPosture = CivicPosture.Restrictive;
            player.government.oppositionTheme = OppositionTheme.Liberty;
            player.government.oppositionCase = 60f;

            Assert.IsTrue(OppositionSystem.ConcedeBy(state, player.id));

            Assert.AreNotEqual(CivicPosture.Restrictive, player.government.civicPosture,
                "A government conceded to a campaign against governing by fiat and kept "
                + "governing by fiat. There is no way to grant this one and keep the "
                + "instrument.");
        }

        [Test]
        public void ConcedingOnAWarCostsTheWillToFightIt()
        {
            var player = state.PlayerCountry;
            player.warSupport = 70f;
            player.government.oppositionTheme = OppositionTheme.War;
            player.government.oppositionCase = 60f;

            Assert.IsTrue(OppositionSystem.ConcedeBy(state, player.id));

            Assert.Less(player.warSupport, 70f,
                "Promising to wind a war down was not heard by the people being asked to "
                + "keep fighting it.");
        }

        // ---------- it is read, not just written ----------

        [Test]
        public void ACampaignHoldsDownSupportInTheChamber()
        {
            var gov = state.PlayerCountry.government;

            gov.oppositionCase = 0f;
            float quiet = OppositionSystem.SupportDrag(gov);

            gov.oppositionCase = 80f;
            float loud = OppositionSystem.SupportDrag(gov);

            Assert.AreEqual(0f, quiet, 0.001f,
                "A government nobody is campaigning against was still losing support to one.");
            Assert.Greater(loud, 15f,
                "An 80-point campaign was worth almost nothing in the chamber, so the "
                + "opposition is a readout rather than a force.");
        }

        [Test]
        public void ACampaignIsWaitingAtTheElection()
        {
            var gov = state.PlayerCountry.government;

            gov.oppositionCase = 0f;
            float unopposed = OppositionSystem.ElectionDrag(gov);

            gov.oppositionCase = 85f;
            float opposed = OppositionSystem.ElectionDrag(gov);

            Assert.AreEqual(0f, unopposed, 0.001f);
            Assert.Greater(opposed, 25f,
                "A decade-long campaign against the government counted for nothing at the "
                + "ballot, which is where an election was already only a roll against "
                + "incumbency fatigue.");
        }

        // ---------- the world runs it too ----------

        [Test]
        public void ForeignGovernmentsAnswerTheirOwnOppositions()
        {
            var foreignState = state.countries.Find(c => !c.isPlayer);
            MakeDestitute(foreignState);
            foreignState.government.oppositionCase = 80f;
            foreignState.government.oppositionTheme = OppositionTheme.Hardship;
            foreignState.resources.treasury = 40000f;

            bool answered = false;
            for (int month = 0; month < 48 && !answered; month++)
            {
                float before = foreignState.government.oppositionCase;
                bool ended = turns.EndMonth();
                // Answering shows up as a fall the drift alone cannot produce in
                // one month against a target this high.
                if (foreignState.government.oppositionCase < before - 8f) answered = true;
                if (month < 6 || !ended)
                {
                    float pc = -1f; string objectives = ""; int acted = -1;
                    foreach (var ai in state.aiStates)
                        if (ai.countryId == foreignState.id)
                        {
                            pc = ai.politicalCapital; acted = ai.actionsThisMonth;
                            foreach (var o in ai.objectives) objectives += $"{o.type}:{o.targetId}:{o.priority:F1} ";
                        }
                    TestContext.WriteLine($"m{month} ended={ended} crises={state.activeCrises.Count} " +
                        $"{foreignState.id} case={foreignState.government.oppositionCase:F1} pc={pc:F1} acted={acted} " +
                        $"treasury={foreignState.resources.treasury:F0} msg={foreignState.government.publicMessaging:F1} " +
                        $"backing={(foreignState.government.IsElective ? foreignState.government.legislativeSupport : foreignState.government.eliteCohesion):F0} " +
                        $"theme={foreignState.government.oppositionTheme} type={foreignState.government.type} obj=[{objectives}]");
                }
            }

            Assert.IsTrue(answered,
                "No foreign government ever answered its own opposition in four years. "
                + "A verb the world cannot reach for is a verb the world does not have.");
        }

        [Test]
        public void ARestrictiveStateSuppressesTheCampaignWithoutFixingTheCause()
        {
            var player = state.PlayerCountry;
            MakeDestitute(player);

            player.government.civicPosture = CivicPosture.Standard;
            float open = OppositionSystem.CaseTargetFor(state, player, out _);

            player.government.civicPosture = CivicPosture.Restrictive;
            float closed = OppositionSystem.CaseTargetFor(state, player, out _);

            Assert.Less(closed, open,
                "A restrictive civic posture did not suppress the campaign at all.");
            Assert.Greater(player.publicGrievance, 50f,
                "The grievance underneath was cleared by the posture. Order bought that way "
                + "is rented, and the bill arrives when the posture relaxes.");
        }

        [Test]
        public void TheCaseSurvivesASaveAndReload()
        {
            var gov = state.PlayerCountry.government;
            gov.oppositionCase = 63f;
            gov.oppositionTheme = OppositionTheme.Corruption;
            gov.oppositionMonths = 14;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));
            var restored = loaded.PlayerCountry.government;

            Assert.AreEqual(63f, restored.oppositionCase, 0.01f);
            Assert.AreEqual(OppositionTheme.Corruption, restored.oppositionTheme);
            Assert.AreEqual(14, restored.oppositionMonths);
        }
    }
}

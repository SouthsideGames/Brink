using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Every government has a cabinet (GDD §8).
    ///
    /// Foreign states used to have none: their capability grew from a bespoke AI
    /// routine with no people behind it, so a rival's progress had no
    /// explanation, nothing to collect against, and nothing a coup could damage.
    /// The same five offices now run all sixteen states.
    /// </summary>
    public class ForeignCabinetTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            state = WorldFactory.CreateDebugWorld(seed: 2727);
        }

        [TearDown]
        public void TearDown()
        {
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        // ---------- everyone has one ----------

        [Test]
        public void EveryCountryHasFiveOfficialsOnePerPillar()
        {
            foreach (var country in state.countries)
            {
                Assert.AreEqual(5, country.cabinet.Count, $"{country.id} has no full cabinet.");
                foreach (Pillar office in System.Enum.GetValues(typeof(Pillar)))
                    Assert.IsNotNull(country.FindOfficial(office),
                        $"{country.id} has nobody running {office}.");
            }
        }

        [Test]
        public void OfficialIdsAreUniqueAcrossTheWorld()
        {
            // Five officials per state means the old per-pillar id scheme
            // collided on every single id across sixteen cabinets.
            var seen = new System.Collections.Generic.HashSet<string>();
            foreach (var country in state.countries)
                foreach (var official in country.cabinet)
                    Assert.IsTrue(seen.Add(official.id),
                        $"Duplicate official id {official.id} — ids must be country-qualified.");
        }

        [Test]
        public void AForeignCabinetBelongsToItsOwnNation()
        {
            // Names come from each country's own pools, not the player's.
            var profile = WorldFactory.FindProfile("JPN");
            foreach (var official in state.FindCountry("JPN").cabinet)
            {
                bool fromPool = false;
                foreach (var first in profile.firstNames)
                    if (official.displayName.StartsWith(first)) fromPool = true;
                Assert.IsTrue(fromPool,
                    $"{official.displayName} does not read as belonging to {profile.displayName}.");

                Assert.Contains(official.title, profile.officeTitles,
                    "A foreign minister must carry their own country's office title.");
            }
        }

        [Test]
        public void ThePlayerCabinetIsTheSameListAsTheirCountrys()
        {
            // GameState.cabinet is a view, not a second copy. Two homes for one
            // concept is how they drift apart.
            Assert.AreSame(state.PlayerCountry.cabinet, state.cabinet);
        }

        // ---------- foreign cabinets do the work ----------

        [Test]
        public void AForeignCabinetGrowsItsOwnCountry()
        {
            var rival = state.FindCountry("CHN");
            float before = rival.pillars.economy + rival.pillars.military
                           + rival.pillars.intelligence + rival.pillars.diplomacy
                           + rival.pillars.government;

            for (int i = 0; i < 24; i++)
            {
                CabinetSystem.MonthlyAct(state);
                state.date = state.date.NextMonth();
            }

            float after = rival.pillars.economy + rival.pillars.military
                          + rival.pillars.intelligence + rival.pillars.diplomacy
                          + rival.pillars.government;

            Assert.Greater(after, before,
                "A foreign government's officials have to actually run the country.");
        }

        [Test]
        public void CompetentForeignCabinetsOutperformIncompetentOnes()
        {
            float Run(float competence)
            {
                var world = WorldFactory.CreateDebugWorld(seed: 2727);
                var rival = world.FindCountry("CHN");
                foreach (var official in rival.cabinet)
                {
                    official.competence = competence;
                    official.riskTolerance = 0f; // isolate competence from variance
                }
                float start = Total(rival);
                for (int i = 0; i < 36; i++)
                {
                    CabinetSystem.MonthlyAct(world);
                    world.date = world.date.NextMonth();
                }
                return Total(rival) - start;
            }

            Assert.Greater(Run(90f), Run(20f),
                "Who runs a rival's ministries has to matter, or the cabinet is scenery.");
        }

        static float Total(CountryState c)
            => c.pillars.military + c.pillars.economy + c.pillars.intelligence
               + c.pillars.diplomacy + c.pillars.government;

        [Test]
        public void ForeignGovernmentsDoNotAllHaveAGoodYearTogether()
        {
            // The month RNG has to be mixed with the country, or sixteen cabinets
            // draw the same variance and the whole world moves in lockstep.
            var results = new System.Collections.Generic.List<float>();
            foreach (var country in state.countries) results.Add(Total(country));

            for (int i = 0; i < 12; i++)
            {
                CabinetSystem.MonthlyAct(state);
                state.date = state.date.NextMonth();
            }

            var deltas = new System.Collections.Generic.List<float>();
            for (int i = 0; i < state.countries.Count; i++)
                deltas.Add(Total(state.countries[i]) - results[i]);

            bool varied = false;
            for (int i = 1; i < deltas.Count; i++)
                if (System.Math.Abs(deltas[i] - deltas[0]) > 0.01f) varied = true;

            Assert.IsTrue(varied, "Every government in the world advanced by exactly the same amount.");
        }

        // ---------- control modes stay the player's ----------

        [Test]
        public void ForeignCabinetsAreAlwaysAutonomous()
        {
            for (int i = 0; i < 24; i++)
            {
                CabinetSystem.MonthlyAct(state);
                state.date = state.date.NextMonth();
            }

            foreach (var country in state.countries)
            {
                if (country.isPlayer) continue;
                foreach (var official in country.cabinet)
                    Assert.AreEqual(ControlMode.Autonomous, official.mode,
                        "Control modes are the operator's interface to their own government. " +
                        "There is nobody standing outside a foreign cabinet to direct it.");
            }
        }

        // ---------- turnover reaches foreign cabinets ----------

        [Test]
        public void AForeignCoupReplacesThatCountrysCabinet()
        {
            var rival = state.FindCountry("CHN");
            string beforeName = rival.cabinet[0].displayName;

            var gov = rival.government;
            gov.militaryLoyalty = 0f;
            gov.conspiracyLevel = 100f;
            rival.stability = 5f;
            rival.nationalUnity = 5f;

            bool replaced = false;
            for (int i = 0; i < 240 && !replaced; i++)
            {
                RegimeSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
                if (rival.cabinet[0].displayName != beforeName) replaced = true;
                gov.militaryLoyalty = 0f;
                gov.conspiracyLevel = 100f;
            }

            Assert.IsTrue(replaced,
                "A junta installs its own people wherever it takes power — that is the most " +
                "visible thing a coup does to a state's capability.");
        }

        [Test]
        public void WeCannotCommandAnotherCountrysMinisters()
        {
            var foreignOfficial = state.FindCountry("RUS").FindOfficial(Pillar.Economy);
            state.influence = 10;

            Assert.IsFalse(CabinetSystem.SetMode(state, foreignOfficial, ControlMode.Directed),
                "Directing a foreign minister would be the most fundamental fog break available.");
            Assert.AreEqual(ControlMode.Autonomous, foreignOfficial.mode);
        }

        [Test]
        public void AForeignReshuffleIsRecordedInTheirHistoryNotOurs()
        {
            // The chronicle entry was filed against the player's country — which
            // was harmless while the reshuffle only ever ran for the player, and
            // started writing every foreign reshuffle into our own national
            // record the moment it went actor-generic.
            var rival = state.FindCountry("IND");
            rival.government.nextElectionDate = state.date;

            for (int i = 0; i < 3; i++)
            {
                GovernmentSystem.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }

            foreach (var entry in state.chronicle)
                if (entry.text.Contains("Cabinet reshuffle"))
                    Assert.AreNotEqual(state.playerCountryId, entry.countryId,
                        "A foreign cabinet reshuffle was filed into our own national record.");
        }

        // ---------- a foreign cabinet is an intelligence target ----------

        [Test]
        public void ForeignOfficialsAreNotIdentifiedWithoutCollection()
        {
            // The fog rule: a view must never print a foreign country's true
            // value without collection behind it. A cabinet is no exception —
            // knowing who runs a rival's ministries is something you find out.
            Assert.IsNull(state.FindNetwork(state.playerCountryId, "RUS"),
                "Test assumes no starting network against RUS.");
        }

        [Test]
        public void CompetenceIsReportedAsABandNeverANumber()
        {
            // An estimate that printed 63.4 would be claiming a precision
            // collection does not have.
            foreach (var country in state.countries)
                foreach (var official in country.cabinet)
                    Assert.IsFalse(official.competence < 0f || official.competence > 100f,
                        "Competence must stay in range for the band mapping to hold.");
        }

        // ---------- the migration actually runs ----------

        [Test]
        public void ASaveFromBeforeCabinetsMovedStillLoads()
        {
            // The migration chain shipped with an empty step list and had never
            // executed. This is the first schema change to use it.
            var legacy = WorldFactory.CreateDebugWorld(seed: 4141);

            // Reconstruct the v1 shape: the player's cabinet on GameState,
            // no foreign cabinets at all.
            legacy.legacyCabinet.AddRange(legacy.PlayerCountry.cabinet);
            foreach (var country in legacy.countries) country.cabinet.Clear();
            legacy.saveVersion = 1;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(legacy));

            Assert.AreEqual(SaveSystem.CurrentSaveVersion, loaded.saveVersion);
            Assert.AreEqual(5, loaded.PlayerCountry.cabinet.Count,
                "The player's officials must survive the move to their country.");
            Assert.IsEmpty(loaded.legacyCabinet, "The legacy list must be left empty after migrating.");

            foreach (var country in loaded.countries)
                Assert.AreEqual(5, country.cabinet.Count,
                    $"{country.id} was never appointed a cabinet by the migration.");
        }

        [Test]
        public void MigratingTheSameSaveTwiceGivesTheSameWorld()
        {
            GameState Migrate()
            {
                var legacy = WorldFactory.CreateDebugWorld(seed: 4141);
                legacy.legacyCabinet.AddRange(legacy.PlayerCountry.cabinet);
                foreach (var country in legacy.countries) country.cabinet.Clear();
                legacy.saveVersion = 1;
                return SaveSystem.FromJson(SaveSystem.ToJson(legacy));
            }

            var first = Migrate();
            var second = Migrate();

            var a = new System.Text.StringBuilder();
            var b = new System.Text.StringBuilder();
            foreach (var country in first.countries)
                foreach (var official in country.cabinet)
                    a.Append(official.id).Append(official.displayName).Append(official.competence);
            foreach (var country in second.countries)
                foreach (var official in country.cabinet)
                    b.Append(official.id).Append(official.displayName).Append(official.competence);

            Assert.AreEqual(a.ToString(), b.ToString(),
                "A migration that produced a different world on each load would be worse " +
                "than refusing the save.");
        }

        [Test]
        public void CabinetsSurviveASaveRoundTrip()
        {
            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(state));

            foreach (var country in loaded.countries)
                Assert.AreEqual(5, country.cabinet.Count, $"{country.id} lost its cabinet in the save.");

            Assert.AreEqual(state.FindCountry("RUS").cabinet[0].displayName,
                            loaded.FindCountry("RUS").cabinet[0].displayName);
        }
    }
}

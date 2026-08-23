using Brink.Core;
using Brink.Data;
using NUnit.Framework;

namespace Brink.Tests
{
    /// <summary>
    /// Officials age, retire and occasionally die (GDD §7.3).
    ///
    /// A cabinet used to be a fixed roster that changed only when the player
    /// fired someone. Nobody grew old in it, so a well-appointed cabinet stayed
    /// perfect forever and the people in it read as sliders rather than a
    /// generation of officials who arrive, serve and leave.
    /// </summary>
    public class CabinetLifecycleTests
    {
        GameState state;

        [SetUp]
        public void SetUp()
        {
            GameLog.MirrorToUnityConsole = false;
            CabinetLifecycle.Frozen = false;
            state = WorldFactory.CreateDebugWorld(seed: 9191);
        }

        [TearDown]
        public void TearDown()
        {
            CabinetLifecycle.Frozen = false;
            GameLog.MirrorToUnityConsole = true;
            GameLog.Clear();
        }

        void RunMonths(int months)
        {
            for (int i = 0; i < months; i++)
            {
                CabinetLifecycle.MonthlyUpdate(state);
                state.date = state.date.NextMonth();
            }
        }

        // ---------- aging ----------

        [Test]
        public void EveryOfficialStartsWithAPlausibleAge()
        {
            foreach (var country in state.countries)
                foreach (var official in country.cabinet)
                    Assert.That(official.age, Is.InRange(45f, 72f),
                        $"{official.displayName} was appointed at an implausible age.");
        }

        [Test]
        public void OfficialsAgeOneYearPerTwelveMonths()
        {
            var official = state.cabinet[0];
            float before = official.age;

            RunMonths(12);

            // May have retired; only assert when the same person is still there.
            var after = state.FindOfficial(official.office);
            if (after != official) Assert.Pass("Incumbent left office — aging covered elsewhere.");
            Assert.AreEqual(before + 1f, official.age, 0.01f);
        }

        [Test]
        public void NobodyRetiresBeforeRetirementAge()
        {
            foreach (var country in state.countries)
                foreach (var official in country.cabinet)
                {
                    official.age = 50f;
                    Assert.AreEqual(0f, CabinetLifecycle.RetirementChance(official), 0.0001f);
                    Assert.AreEqual(0f, CabinetLifecycle.DeathChance(official), 0.0001f);
                }
        }

        [Test]
        public void RetirementBecomesLikelierWithAgeAndTenure()
        {
            var young = new Official { age = 67f, monthsInOffice = 12 };
            var old = new Official { age = 76f, monthsInOffice = 12 };
            var longServing = new Official { age = 67f, monthsInOffice = 240 };

            Assert.Greater(CabinetLifecycle.RetirementChance(old),
                           CabinetLifecycle.RetirementChance(young));
            Assert.Greater(CabinetLifecycle.RetirementChance(longServing),
                           CabinetLifecycle.RetirementChance(young),
                           "A long tenure wears people out independently of age.");
        }

        [Test]
        public void DeathInOfficeStaysRare()
        {
            // A shock that happens once or twice in a long save, not something
            // the player plans around.
            var elderly = new Official { age = 78f };
            Assert.Less(CabinetLifecycle.DeathChance(elderly), 0.02f,
                "Mortality must not become a mechanic the operator schedules around.");
        }

        [Test]
        public void ACabinetTurnsOverAcrossALongSave()
        {
            var original = new System.Collections.Generic.List<string>();
            foreach (var official in state.cabinet) original.Add(official.displayName);

            // Start them old enough that turnover is certain within the window.
            foreach (var official in state.cabinet) official.age = 68f;

            RunMonths(360);

            bool anyChanged = false;
            foreach (var official in state.cabinet)
                if (!original.Contains(official.displayName)) anyChanged = true;

            Assert.IsTrue(anyChanged,
                "Thirty years passed and the same five people were still in post.");
        }

        // ---------- the seat is never left empty ----------

        [Test]
        public void AVacancyIsFilledEvenIfTheOperatorIgnoresIt()
        {
            OpenPlayerVacancy(Pillar.Economy);
            Assert.AreEqual(1, state.PlayerCountry.vacancies.Count);

            RunMonths(CabinetLifecycle.MonthsBeforeGovernmentDecides + 1);

            Assert.IsEmpty(state.PlayerCountry.vacancies,
                "A ministry cannot stand empty forever because the operator did not click.");
            Assert.IsNotNull(state.FindOfficial(Pillar.Economy));
        }

        [Test]
        public void TheGovernmentsOwnPickIsTheSafeOne()
        {
            // Left to itself an establishment confirms the loyal, unremarkable
            // candidate — which is exactly why the operator should choose.
            var rng = new System.Random(4);
            var shortlist = CabinetLifecycle.Shortlist(state.PlayerCountry, Pillar.Military, rng);
            var safe = CabinetLifecycle.SafestOf(shortlist);

            foreach (var candidate in shortlist)
                if (candidate != safe)
                    Assert.GreaterOrEqual(safe.loyalty - safe.riskTolerance * 0.5f,
                                          candidate.loyalty - candidate.riskTolerance * 0.5f);
        }

        [Test]
        public void ForeignSeatsAreFilledImmediatelyAndNeverQueued()
        {
            var rival = state.FindCountry("CHN");
            foreach (var official in rival.cabinet) official.age = 95f; // force turnover

            RunMonths(120);

            Assert.AreEqual(5, rival.cabinet.Count,
                "A foreign government must always have a full cabinet.");
            Assert.IsEmpty(rival.vacancies,
                "A shortlist nobody reads is not a decision — foreign seats fill at once.");
        }

        [Test]
        public void NoCountryEverLosesAnOfficeForGood()
        {
            foreach (var country in state.countries)
                foreach (var official in country.cabinet) official.age = 90f;

            RunMonths(240);

            foreach (var country in state.countries)
            {
                // The player's may be briefly vacant and awaiting a decision.
                int held = country.cabinet.Count + country.vacancies.Count;
                Assert.AreEqual(5, held,
                    $"{country.id} has permanently lost an office.");
            }
        }

        // ---------- appointing is a real choice ----------

        [Test]
        public void TheShortlistContainsARealTradeOff()
        {
            // Three samples from one distribution would not be a decision. The
            // GDD is explicit that the strongest candidate may be the wrong one.
            var rng = new System.Random(11);
            var shortlist = CabinetLifecycle.Shortlist(state.PlayerCountry, Pillar.Intelligence, rng);

            Assert.AreEqual(CabinetLifecycle.ShortlistSize, shortlist.Count);

            OfficialCandidate best = shortlist[0];
            foreach (var candidate in shortlist)
                if (candidate.competence > best.competence) best = candidate;

            foreach (var candidate in shortlist)
                if (candidate != best)
                    Assert.Greater(candidate.loyalty, best.loyalty,
                        "The most capable candidate must not also be the most trusted, " +
                        "or there is nothing to weigh.");
        }

        [Test]
        public void AppointingInstallsTheChosenCandidate()
        {
            OpenPlayerVacancy(Pillar.Diplomacy);
            var vacancy = state.PlayerCountry.vacancies[0];
            string wanted = vacancy.candidates[0].displayName;

            Assert.IsTrue(CabinetLifecycle.Appoint(state, Pillar.Diplomacy, 0));

            Assert.AreEqual(wanted, state.FindOfficial(Pillar.Diplomacy).displayName);
            Assert.IsEmpty(state.PlayerCountry.vacancies);
        }

        [Test]
        public void AppointingToAFilledOfficeDoesNothing()
        {
            Assert.IsFalse(CabinetLifecycle.Appoint(state, Pillar.Military, 0),
                "There is no vacancy to fill.");
        }

        [Test]
        public void AppointingOutsideTheShortlistIsRejected()
        {
            OpenPlayerVacancy(Pillar.Government);
            Assert.IsFalse(CabinetLifecycle.Appoint(state, Pillar.Government, 99));
            Assert.IsNotEmpty(state.PlayerCountry.vacancies);
        }

        // ---------- housekeeping ----------

        [Test]
        public void TheDebugFreezeStopsTurnoverEntirely()
        {
            foreach (var official in state.cabinet) official.age = 95f;
            CabinetLifecycle.Frozen = true;

            RunMonths(120);

            Assert.AreEqual(5, state.cabinet.Count);
            Assert.IsEmpty(state.PlayerCountry.vacancies,
                "Every significant system exposes a debug control (GDD §34.1).");
        }

        [Test]
        public void LifecycleIsDeterministic()
        {
            string Run()
            {
                var world = WorldFactory.CreateDebugWorld(seed: 9191);
                foreach (var country in world.countries)
                    foreach (var official in country.cabinet) official.age = 69f;

                for (int i = 0; i < 120; i++)
                {
                    CabinetLifecycle.MonthlyUpdate(world);
                    world.date = world.date.NextMonth();
                }

                var sb = new System.Text.StringBuilder();
                foreach (var country in world.countries)
                    foreach (var official in country.cabinet)
                        sb.Append(official.displayName).Append(official.age.ToString("F2"));
                return sb.ToString();
            }

            Assert.AreEqual(Run(), Run(), "A reloaded save must produce the same officials.");
        }

        [Test]
        public void AgesSurviveASaveAndABackfillMigration()
        {
            var legacy = WorldFactory.CreateDebugWorld(seed: 5252);
            foreach (var country in legacy.countries)
                foreach (var official in country.cabinet) official.age = 0f; // the v2 shape
            legacy.saveVersion = 2;

            var loaded = SaveSystem.FromJson(SaveSystem.ToJson(legacy));

            foreach (var country in loaded.countries)
                foreach (var official in country.cabinet)
                    Assert.Greater(official.age, 0f,
                        "age defaults to 0, which would mean a cabinet of infants who never " +
                        "retire — the backfill is why this field needed a migration step.");
        }

        void OpenPlayerVacancy(Pillar office)
        {
            var player = state.PlayerCountry;
            player.cabinet.Remove(player.FindOfficial(office));
            player.vacancies.Add(new CabinetVacancy
            {
                office = office,
                reason = VacancyReason.Retirement,
                candidates = CabinetLifecycle.Shortlist(player, office, new System.Random(7))
            });
        }
    }
}

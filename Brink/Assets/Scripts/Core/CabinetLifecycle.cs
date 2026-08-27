using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Officials age, retire and occasionally die (GDD §7.3).
    ///
    /// A cabinet used to be a fixed roster that changed only when the player
    /// fired someone or an administration turned over. Nobody grew old in it.
    /// That made the people in it feel like sliders rather than a generation of
    /// officials who arrive, serve and leave — and it meant a well-appointed
    /// cabinet, once assembled, stayed perfect forever.
    ///
    /// Turnover is now something that happens *to* the operator. When a seat
    /// opens, a shortlist is drawn with real tensions in it: the GDD is explicit
    /// that the technically strongest candidate may be politically or
    /// strategically incompatible, so the pool is built to contain that choice
    /// rather than three samples from one distribution.
    ///
    /// **It never blocks.** A vacancy left unfilled is filled by the government
    /// itself after <see cref="MonthsBeforeGovernmentDecides"/> months, with the
    /// candidate the establishment would have picked. The operator can always
    /// decline to choose; declining is itself a choice, and it costs them the
    /// pick rather than the turn.
    /// </summary>
    public static class CabinetLifecycle
    {
        /// <summary>Age above which officials begin to consider retiring.</summary>
        public const float RetirementAge = 66f;

        /// <summary>Age above which mortality becomes a real monthly risk.</summary>
        public const float MortalityAge = 70f;

        /// <summary>Candidates on a shortlist.</summary>
        public const int ShortlistSize = 3;

        /// <summary>How long a seat may stand empty before the government fills it.</summary>
        public const int MonthsBeforeGovernmentDecides = 3;

        /// <summary>Set true to freeze aging entirely (GDD §34.1 debug control).</summary>
        public static bool Frozen;

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            foreach (var country in state.countries)
            {
                ResolvePendingVacancies(state, country, monthIndex);
                if (Frozen) continue;

                for (int i = country.cabinet.Count - 1; i >= 0; i--)
                {
                    var official = country.cabinet[i];
                    official.age += 1f / 12f;

                    var rng = new Random(unchecked(
                        state.rngSeed * 6421 + monthIndex * 577
                        + (int)official.office * 31 + Hash.Of(country.id) * 13));

                    if (rng.NextDouble() < DeathChance(official))
                    {
                        OpenVacancy(state, country, official, VacancyReason.Death, rng);
                        continue;
                    }

                    if (rng.NextDouble() < RetirementChance(official))
                        OpenVacancy(state, country, official, VacancyReason.Retirement, rng);
                }
            }
        }

        /// <summary>
        /// Monthly chance of stepping down. Rises with age and with time served —
        /// a long tenure wears people out independently of how old they are.
        /// </summary>
        public static float RetirementChance(Official official)
        {
            if (official.age < RetirementAge) return 0f;
            float overAge = official.age - RetirementAge;
            float tenure = official.monthsInOffice / 12f;
            return Math.Min(0.08f, overAge * 0.004f + Math.Max(0f, tenure - 8f) * 0.002f);
        }

        /// <summary>
        /// Monthly chance of dying in office. Deliberately small — this should be
        /// a shock that happens once or twice in a long save, not a mechanic the
        /// player plans around.
        /// </summary>
        public static float DeathChance(Official official)
        {
            if (official.age < MortalityAge) return 0f;
            return Math.Min(0.012f, (official.age - MortalityAge) * 0.0008f);
        }

        // ---------- vacancies ----------

        static void OpenVacancy(GameState state, CountryState country, Official official,
            VacancyReason reason, Random rng)
        {
            country.cabinet.Remove(official);

            string what = reason == VacancyReason.Death ? "has died in office" : "has stepped down";
            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"{official.title} {official.displayName} {what}.", Publicity.Public);

            // Also to the log: a chronicle entry is invisible to a headless run,
            // and "did this fire at all?" is the first question anyone asks of a
            // rare event.
            GameLog.Info("CABINET",
                $"{country.id}: {official.title} {official.displayName} {what} at {official.age:F0}.");

            if (!country.isPlayer)
            {
                // A foreign government simply appoints. No shortlist: nobody
                // would ever read it, and simulating a choice with no observer
                // is cost without consequence.
                Install(country, BestFor(Shortlist(country, official.office, rng), rng), official.office, rng);
                if (WorldWire.Watches(state, country.id))
                    state.AddNotification(NotificationClass.Wire, "FOREIGN CABINET CHANGE",
                        $"{official.displayName} {what} in {country.displayName}.",
                        country.id, desk: ReportingDesk.Intelligence);
                return;
            }

            var vacancy = new CabinetVacancy
            {
                office = official.office,
                reason = reason,
                candidates = Shortlist(country, official.office, rng)
            };
            country.vacancies.Add(vacancy);

            state.AddNotification(NotificationClass.Priority,
                $"{official.office.ToString().ToUpperInvariant()} OFFICE VACANT",
                $"{official.title} {official.displayName} {what}. " +
                $"A shortlist is on your desk. If you do not choose within " +
                $"{MonthsBeforeGovernmentDecides} months the government will appoint for you.",
                country.id);
        }

        static void ResolvePendingVacancies(GameState state, CountryState country, int monthIndex)
        {
            for (int i = country.vacancies.Count - 1; i >= 0; i--)
            {
                var vacancy = country.vacancies[i];
                vacancy.monthsOpen++;
                if (vacancy.monthsOpen < MonthsBeforeGovernmentDecides) continue;

                var rng = new Random(unchecked(
                    state.rngSeed * 3313 + monthIndex * 101 + (int)vacancy.office));

                // The establishment's pick, not the best one: left to itself, a
                // government appoints the safe, loyal, unremarkable candidate.
                var chosen = SafestOf(vacancy.candidates);
                Install(country, chosen, vacancy.office, rng);
                country.vacancies.RemoveAt(i);

                state.AddNotification(NotificationClass.Advisory, "APPOINTMENT MADE WITHOUT YOU",
                    $"{chosen.displayName} has been confirmed as " +
                    $"{TitleFor(country, vacancy.office)}. The office could not stand empty.",
                    country.id, desk: ReportingDesk.Government);
            }
        }

        /// <summary>Fill a vacancy with the operator's chosen candidate.</summary>
        public static bool Appoint(GameState state, Pillar office, int candidateIndex)
        {
            var country = state.PlayerCountry;
            if (country == null) return false;

            CabinetVacancy vacancy = null;
            foreach (var open in country.vacancies)
                if (open.office == office) { vacancy = open; break; }

            if (vacancy == null) return false;
            if (candidateIndex < 0 || candidateIndex >= vacancy.candidates.Count) return false;

            var rng = new Random(unchecked(
                state.rngSeed * 811 + state.NextActionSequence() * 104729 + (int)office));

            Install(country, vacancy.candidates[candidateIndex], office, rng);
            country.vacancies.Remove(vacancy);

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 14, "Cabinet appointment");
            state.AddChronicle(ChronicleCategory.Political, country.id,
                $"{vacancy.candidates[candidateIndex].displayName} appointed {TitleFor(country, office)}.",
                Publicity.Public);
            return true;
        }

        static void Install(CountryState country, OfficialCandidate candidate, Pillar office, Random rng)
        {
            country.cabinet.Add(new Official
            {
                id = $"OFF_{country.id}_{office.ToString().ToUpperInvariant()}_{rng.Next(100000)}",
                displayName = candidate.displayName,
                title = TitleFor(country, office),
                office = office,
                competence = candidate.competence,
                loyalty = candidate.loyalty,
                riskTolerance = candidate.riskTolerance,
                age = candidate.age,
                trust = 50f,
                monthsInOffice = 0,
                mode = ControlMode.Autonomous
            });
        }

        public static string TitleFor(CountryState country, Pillar office)
        {
            var profile = WorldFactory.FindProfile(country.id);
            return profile != null && profile.officeTitles.Length == 5
                ? profile.officeTitles[(int)office]
                : office.ToString();
        }

        // ---------- the shortlist ----------

        /// <summary>
        /// Three candidates built around a tension rather than sampled from one
        /// distribution: an outstanding official the establishment distrusts, a
        /// safe and loyal mediocrity, and one wild card. Which of those is the
        /// right answer depends on what the operator is trying to do, which is
        /// the whole point (GDD §7.3).
        /// </summary>
        public static List<OfficialCandidate> Shortlist(CountryState country, Pillar office, Random rng)
        {
            var profile = WorldFactory.FindProfile(country.id);
            var list = new List<OfficialCandidate>();

            float Roll(float min, float max) => (float)(min + (max - min) * rng.NextDouble());

            string Name()
            {
                if (profile == null) return "APPOINTEE";
                return $"{profile.firstNames[rng.Next(profile.firstNames.Length)]} " +
                       $"{profile.lastNames[rng.Next(profile.lastNames.Length)]}";
            }

            // The talent: excellent, and nobody's creature.
            list.Add(new OfficialCandidate
            {
                displayName = Name(),
                competence = Roll(72f, 92f),
                loyalty = Roll(20f, 42f),
                riskTolerance = Roll(45f, 80f),
                age = Roll(44f, 58f),
                background = "Exceptional record. Owes this office nothing and knows it."
            });

            // The safe pair of hands.
            list.Add(new OfficialCandidate
            {
                displayName = Name(),
                competence = Roll(42f, 58f),
                loyalty = Roll(68f, 90f),
                riskTolerance = Roll(15f, 38f),
                age = Roll(55f, 68f),
                background = "Career official. Will not surprise you in either direction."
            });

            // The wild card.
            list.Add(new OfficialCandidate
            {
                displayName = Name(),
                competence = Roll(48f, 80f),
                loyalty = Roll(35f, 70f),
                riskTolerance = Roll(70f, 95f),
                age = Roll(40f, 54f),
                background = "Unorthodox and well connected. Opinions differ sharply."
            });

            return list;
        }

        /// <summary>The candidate a government left to itself would confirm.</summary>
        public static OfficialCandidate SafestOf(List<OfficialCandidate> candidates)
        {
            OfficialCandidate best = null;
            float bestScore = float.MinValue;
            foreach (var candidate in candidates)
            {
                float score = candidate.loyalty - candidate.riskTolerance * 0.5f;
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best ?? candidates[0];
        }

        /// <summary>What a foreign government picks: the strongest available.</summary>
        static OfficialCandidate BestFor(List<OfficialCandidate> candidates, Random rng)
        {
            OfficialCandidate best = null;
            float bestScore = float.MinValue;
            foreach (var candidate in candidates)
            {
                float score = candidate.competence + candidate.loyalty * 0.35f
                              + (float)rng.NextDouble() * 8f;
                if (score > bestScore) { bestScore = score; best = candidate; }
            }
            return best ?? candidates[0];
        }
    }
}

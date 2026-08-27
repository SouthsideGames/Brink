using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// The world before the operator arrived (GDD §31.3).
    ///
    /// **The chronicle began empty.** A save opened in JAN 1984 with two lines in
    /// it — the terminal booting and a cabinet being sworn in — so CHRONICLE was
    /// blank for the first two years of every playthrough, and every system that
    /// reasons from the past started from nothing: relationship memory, the
    /// opponent model, `publicGrievance`. A world with no history is a world that
    /// began the moment you were posted to it, which is exactly the wrong feeling
    /// for a game about inheriting a situation.
    ///
    /// Three rules make this backstory rather than a second world generator:
    ///
    /// 1. **History explains the starting position; it never creates it.** Not one
    ///    line here moves a national statistic, and every relationship memory is
    ///    written at **weight zero**. `memoryWeight` is read in four places and
    ///    decays at 0.985/month, so seeding it would silently retune treaty
    ///    acceptance, sanctions relief and alliance willingness — and every
    ///    balance figure ever measured with it. The same line skills are held to:
    ///    operator capability only, never national power.
    /// 2. **It is derived, so it cannot contradict.** A grievance is written only
    ///    between states world generation actually made cold, a partnership only
    ///    between states it made warm. Authoring a 1974 quarrel between two
    ///    countries that open as allies would be worse than no history at all.
    /// 3. **It consumes no random draws.** Everything varies through `Hash.Of`,
    ///    not `rng`, so the Standard world stays bit-identical to the one every
    ///    balance figure was measured on (`WorldSizeTests`).
    ///
    /// On the content: these are **archetype events, not real history**. The
    /// roster uses real country names as gameplay archetypes, and this project's
    /// standing rule is to keep well away from modelling actual events and live
    /// claims. So a state with a large energy endowment gets "the revenue decade",
    /// not any particular one — the texture of a plausible past, with no assertion
    /// about a real one.
    /// </summary>
    public static class HistoryCatalog
    {
        /// <summary>Earliest year the record reaches back to.</summary>
        public const int FirstYear = 1971;

        /// <summary>Entries written per country, before pair history.</summary>
        public const int PerCountry = 3;

        /// <summary>
        /// Write the world's backstory. Called once, at the end of world
        /// creation, after relationships exist — the pair half reads them.
        /// </summary>
        public static void Seed(GameState state)
        {
            if (state == null || state.countries.Count == 0) return;

            foreach (var country in state.countries)
                SeedCountry(state, country);

            SeedPairs(state);

            // The record reads forward in time (ChronicleTests). Country by
            // country the lines are authored out of order — a 1974 line after a
            // 1977 one — and this system shipped without a test run, so the
            // chronicle opened scrambled. Stable: same-date lines keep their
            // authored order.
            var indexed = new List<(ChronicleEntry entry, int index)>();
            for (int i = 0; i < state.chronicle.Count; i++) indexed.Add((state.chronicle[i], i));
            indexed.Sort((a, b) =>
            {
                int byDate = a.entry.date.CompareTo(b.entry.date);
                return byDate != 0 ? byDate : a.index.CompareTo(b.index);
            });
            state.chronicle.Clear();
            foreach (var pair in indexed) state.chronicle.Add(pair.entry);
        }

        // ---------- one country's own decade ----------

        static void SeedCountry(GameState state, CountryState country)
        {
            var lines = new List<string>();

            // Economic character, from what the country was actually authored
            // with. Each line is a consequence of a number the operator can see
            // on the ECONOMY screen, so the past reads as an explanation of the
            // present rather than as decoration beside it.
            if (country.resources.energyEndowment >= 70f)
                lines.Add("a decade of energy revenue, and the habits that came with it");
            else if (country.resources.energyEndowment <= 30f)
                lines.Add("a decade spent buying energy it could not produce");

            if (country.resources.industrialCapacity >= 65f)
                lines.Add("sustained industrial expansion");
            else if (country.resources.industrialCapacity <= 35f)
                lines.Add("factories that closed faster than they were replaced");

            if (country.resources.foodSecurity <= 35f)
                lines.Add("an import bill for food that no government could reduce");

            if (country.resources.materialsEndowment >= 70f)
                lines.Add("extraction revenue that funded everything else");

            // Political character, from the constitution rather than from events.
            switch (country.government.type)
            {
                case GovernmentType.ParliamentaryRepublic:
                    lines.Add("four governments in eleven years, and no crisis in any of them");
                    break;
                case GovernmentType.PresidentialRepublic:
                    lines.Add("two administrations, and a constitution that survived both");
                    break;
                case GovernmentType.DominantPartyState:
                    lines.Add("one succession, managed internally and announced afterwards");
                    break;
                case GovernmentType.CentralizedRepublic:
                    lines.Add("an executive that accumulated authority faster than it was granted");
                    break;
                default:
                    lines.Add("a continuity nobody in office had to argue for");
                    break;
            }

            // Military character.
            if (country.pillars.military >= 65f)
                lines.Add("a rearmament programme that outlasted the government that began it");
            else if (country.pillars.military <= 35f)
                lines.Add("a defence budget that lost every argument it was in");

            if (lines.Count == 0) return;

            // Deterministic spread across the pre-start years, varied per country
            // through the id rather than through `rng`.
            // Masked rather than `Math.Abs`: an FNV hash can land on int.MinValue,
            // whose absolute value throws.
            int salt = Hash.Of(country.id) & 0x7fffffff;
            int written = 0;

            for (int i = 0; i < lines.Count && written < PerCountry; i++, written++)
            {
                int span = Math.Max(1, state.startDate.year - FirstYear);
                int year = FirstYear + (salt / (i + 1) + i * 5) % span;
                int month = 1 + (salt / (i + 3)) % 12;

                state.chronicle.Add(new ChronicleEntry
                {
                    date = new GameDate(year, month),
                    category = ChronicleCategory.Economic,
                    countryId = country.id,
                    text = $"{country.displayName}: {lines[i]}.",
                    publicity = Publicity.Public
                });
            }
        }

        // ---------- what has passed between them ----------

        static void SeedPairs(GameState state)
        {
            foreach (var relationship in state.relationships)
            {
                var a = state.FindCountry(relationship.countryA);
                var b = state.FindCountry(relationship.countryB);
                if (a == null || b == null) continue;

                string text = null;
                var category = ChronicleCategory.Diplomatic;

                // Only where world generation already made them cold or warm.
                // Anything else and the record would contradict the numbers.
                if (relationship.relations <= 28f)
                {
                    text = $"{a.displayName} and {b.displayName}: a quarrel neither government "
                         + "has been able to put down since";
                    category = ChronicleCategory.Political;
                }
                else if (relationship.relations >= 72f && relationship.trust >= 60f)
                {
                    text = $"{a.displayName} and {b.displayName}: an understanding that has "
                         + "outlived the people who signed it";
                }
                else if (Math.Max(relationship.dependenceAOnB, relationship.dependenceBOnA) >= 55f)
                {
                    string dependent = relationship.dependenceAOnB >= relationship.dependenceBOnA
                        ? a.displayName : b.displayName;
                    string supplier = relationship.dependenceAOnB >= relationship.dependenceBOnA
                        ? b.displayName : a.displayName;
                    text = $"{dependent} came to rely on {supplier}, and has been managing "
                         + "that ever since";
                    category = ChronicleCategory.Economic;
                }

                if (text == null) continue;

                int salt = Hash.Of(relationship.countryA + relationship.countryB) & 0x7fffffff;
                int year = FirstYear + salt % Math.Max(1, state.startDate.year - FirstYear);
                int month = 1 + (salt / 7) % 12;
                var date = new GameDate(year, month);

                state.chronicle.Add(new ChronicleEntry
                {
                    date = date,
                    category = category,
                    countryId = relationship.countryA,
                    text = text + ".",
                    publicity = Publicity.Public
                });

                // **Weight zero.** The text is the point: it gives the operator
                // something to read in the relationship detail and gives a
                // rivalry a date. Any weight at all would move treaty acceptance,
                // sanctions relief and alliance willingness before the first
                // month was played.
                relationship.AddMemory(date, Summarise(text), 0f);
            }
        }

        /// <summary>A memory line is read in a narrow column; the chronicle is not.</summary>
        static string Summarise(string text)
        {
            int colon = text.IndexOf(':');
            return colon > 0 && colon + 2 < text.Length ? text.Substring(colon + 2) : text;
        }
    }
}

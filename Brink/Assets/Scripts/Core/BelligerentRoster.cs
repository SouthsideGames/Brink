using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Who is fighting whom, and on whose account (GDD §18, §15.2).
    ///
    /// **A cascading alliance war is illegible without this.** Once honouring a
    /// pact can open a front, and that front can call in the aggressor's own
    /// guarantors, the operator is no longer in "a war" — they are in three, with
    /// partners who joined for their own reasons and enemies they never declared
    /// against. The MILITARY screen could name the opponent of whichever front was
    /// selected and nothing else, so the one question a coalition war actually
    /// raises — *who am I fighting, and who is with me?* — had no answer anywhere
    /// in the game.
    ///
    /// Two rules:
    ///
    /// 1. **Derived, never stored.** Sides are read from the live confrontations
    ///    and coalitions each time, on the `StatusOf` and `VoteScore` precedent.
    ///    A cached roster would be a second opinion about who is at war, and it
    ///    would be wrong within a month.
    ///
    /// 2. **Belligerency is public; strength is not.** Who has declared against
    ///    whom is an observable fact and is reported plainly. Nothing here returns
    ///    a capability figure — anything the operator reads about how strong these
    ///    states are still goes through `IntelReadout`, so this cannot become a
    ///    back door around the fog rule.
    /// </summary>
    public static class BelligerentRoster
    {
        /// <summary>Why a state is in this war, in the operator's language.</summary>
        public class Entry
        {
            public string countryId;

            /// <summary>The front they are on with us, if we face them directly.</summary>
            public string confrontationId = "";

            /// <summary>Plain-language account of how they came to be here.</summary>
            public string because = "";

            /// <summary>True when we are directly at war with them, not merely opposed.</summary>
            public bool direct;
        }

        /// <summary>
        /// Everyone <paramref name="countryId"/> is at war with, plus anyone
        /// fighting alongside those states against us.
        ///
        /// The second half is the point: a state that joined our enemy's coalition
        /// is shooting at us whether or not a confrontation names the pair, and an
        /// operator who reads only their own fronts will be surprised by exactly
        /// the states the cascade brought in.
        /// </summary>
        public static List<Entry> EnemiesOf(GameState state, string countryId)
        {
            var enemies = new List<Entry>();
            if (state == null || string.IsNullOrEmpty(countryId)) return enemies;

            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || !confrontation.Involves(countryId)) continue;

                string opponentId = confrontation.OpponentOf(countryId);
                var opponent = state.FindCountry(opponentId);
                if (opponent == null) continue;

                Add(enemies, new Entry
                {
                    countryId = opponentId,
                    confrontationId = confrontation.id,
                    direct = true,
                    because = confrontation.initiatorId == countryId
                        ? $"we opened this front ({TheatreSystem.Name(confrontation.theatre)})"
                        : $"they opened this front ({TheatreSystem.Name(confrontation.theatre)})"
                });

                // Anybody who lined up behind them.
                var theirCoalition = state.FindCoalitionLedBy(confrontation.id, opponentId);
                if (theirCoalition == null || theirCoalition.dissolved) continue;

                foreach (string memberId in theirCoalition.memberIds)
                {
                    if (memberId == opponentId || memberId == countryId) continue;
                    Add(enemies, new Entry
                    {
                        countryId = memberId,
                        confrontationId = confrontation.id,
                        direct = false,
                        because = $"stands with {opponent.displayName}"
                    });
                }
            }

            return enemies;
        }

        /// <summary>
        /// Everyone fighting on our side, across every front, and why they came.
        ///
        /// "Why" matters more here than in the enemy list: a partner who is in
        /// because a bloc obliged them behaves differently from one who chose it,
        /// and `DiplomacySystem.MonthlyUpdate` can take either of them back out
        /// mid-war.
        /// </summary>
        public static List<Entry> PartnersOf(GameState state, string countryId)
        {
            var partners = new List<Entry>();
            if (state == null || string.IsNullOrEmpty(countryId)) return partners;

            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || !confrontation.Involves(countryId)) continue;

                var ourCoalition = state.FindCoalitionLedBy(confrontation.id, countryId);
                if (ourCoalition != null && !ourCoalition.dissolved)
                    foreach (string memberId in ourCoalition.memberIds)
                    {
                        if (memberId == countryId) continue;
                        Add(partners, new Entry
                        {
                            countryId = memberId,
                            confrontationId = confrontation.id,
                            direct = true,
                            because = "in our coalition"
                        });
                    }

                // We may also be a guest in somebody else's coalition — which is
                // what honouring a pact makes us. Without this the operator who
                // came to a partner's defence reads a screen that says they are
                // fighting alone.
                foreach (var coalition in state.coalitions)
                {
                    if (coalition.dissolved || coalition.confrontationId != confrontation.id) continue;
                    if (coalition.leaderId == countryId) continue;
                    if (!coalition.memberIds.Contains(countryId)) continue;

                    foreach (string memberId in coalition.memberIds)
                    {
                        if (memberId == countryId) continue;
                        var leader = state.FindCountry(coalition.leaderId);
                        Add(partners, new Entry
                        {
                            countryId = memberId,
                            confrontationId = confrontation.id,
                            direct = true,
                            because = memberId == coalition.leaderId
                                ? "we came to their defence"
                                : $"with {leader?.displayName} in the same coalition"
                        });
                    }
                }
            }

            return partners;
        }

        /// <summary>
        /// States that would be obliged to come in if we were attacked — the
        /// guarantee we are holding but have not had to call.
        ///
        /// Shown because the value of an alliance is almost entirely in the war
        /// that does not happen, and an operator who cannot see what they have
        /// bought has no way to judge whether it was worth the price.
        /// </summary>
        public static List<AllianceSystem.Guarantor> GuaranteesHeldBy(GameState state, string countryId)
            => AllianceSystem.GuarantorsOf(state, countryId, null);

        /// <summary>
        /// States we would be obliged to defend. The other half of the ledger, and
        /// the one that decides how much of the world's trouble is ours.
        /// </summary>
        public static List<string> ObligationsOwedBy(GameState state, string countryId)
        {
            var owed = new List<string>();
            if (state == null || string.IsNullOrEmpty(countryId)) return owed;

            foreach (var country in state.countries)
            {
                if (country.id == countryId) continue;
                foreach (var guarantor in AllianceSystem.GuarantorsOf(state, country.id, null))
                {
                    if (guarantor.countryId != countryId) continue;
                    if (!owed.Contains(country.id)) owed.Add(country.id);
                    break;
                }
            }
            return owed;
        }

        /// <summary>
        /// Merge, keeping the first account of why somebody is here. A state that
        /// faces us directly on one front and stands behind an ally on another is
        /// listed once — the operator needs a roster, not a spreadsheet.
        /// </summary>
        static void Add(List<Entry> list, Entry entry)
        {
            foreach (var existing in list)
            {
                if (existing.countryId != entry.countryId) continue;
                // A direct war outranks a supporting role in the description.
                if (entry.direct && !existing.direct)
                {
                    existing.direct = true;
                    existing.because = entry.because;
                    existing.confrontationId = entry.confrontationId;
                }
                return;
            }
            list.Add(entry);
        }
    }
}

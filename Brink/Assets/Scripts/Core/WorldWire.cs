using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>One line of world news, as a wire service would carry it.</summary>
    public class WireItem
    {
        public GameDate date;
        public string countryId;
        public ChronicleCategory category;
        public string headline;

        /// <summary>True when we are party to it — our own affairs read differently.</summary>
        public bool involvesUs;
    }

    /// <summary>
    /// The world wire (GDD §28, §31.3).
    ///
    /// A strategy game made of text lives or dies on whether the player can tell
    /// what is going on. The simulation already runs sixteen countries through
    /// elections, coups, wars, treaties and sanctions every month — and almost
    /// none of it reached the operator, because the briefing only ever showed
    /// what happened to *us*. The world was alive and invisible.
    ///
    /// **It carries only what is publicly observable.** A treaty is signed in
    /// public. A coup is on television. An army at a border is seen. A covert
    /// operation, a research programme and a strategic preparation are not — and
    /// a wire that reported those would quietly destroy the intelligence pillar,
    /// because collection would tell the player nothing the news had not already
    /// given them for free.
    ///
    /// That leaves three distinct channels, which is the point:
    /// - the **wire** says what happened,
    /// - your **ministries** say what it means for us (`ReportingSystem`),
    /// - **intelligence** says what is underneath (`IntelligenceSystem`).
    ///
    /// It is deliberately **not** filtered by cabinet competence. This is public
    /// news, not your ministry's paperwork; a weak minister costs you your own
    /// reporting, not the ability to hear that a war has started.
    /// </summary>
    public static class WorldWire
    {
        /// <summary>Network penetration at which a state's domestic news reaches our wire.</summary>
        public const float WatchPenetration = 20f;

        /// <summary>
        /// Whether a foreign state's *domestic* news is ours to hear (2026-08).
        /// A network with real penetration, an unbroken treaty, or a live
        /// confrontation with them earns it; the rest of the world's cabinet
        /// reshuffles and ministerial stumbles stay in the chronicle, where they
        /// always were. Unfiltered, foreign cabinet gossip was a quarter of all
        /// terminal traffic.
        /// </summary>
        public static bool Watches(GameState state, string countryId)
        {
            if (string.IsNullOrEmpty(countryId) || countryId == state.playerCountryId) return true;
            var network = state.FindNetwork(state.playerCountryId, countryId);
            if (network != null && network.penetration >= WatchPenetration) return true;
            // A trade preference does not put our people in their ministries;
            // a defence pact or an intelligence-sharing clause does.
            var treaty = state.FindTreaty(state.playerCountryId, countryId);
            if (treaty != null && !treaty.broken
                && (treaty.HasActive(state, TreatyCommitment.MutualDefense)
                    || treaty.HasActive(state, TreatyCommitment.IntelligenceSharing)))
                return true;
            foreach (var confrontation in state.confrontations)
                if (!confrontation.resolved && confrontation.Involves(state.playerCountryId) && confrontation.Involves(countryId))
                    return true;
            return false;
        }

        /// <summary>
        /// Categories that reach the wire at all. Intelligence never does — it is
        /// the category of things done quietly, and is exactly what the fog is
        /// protecting.
        /// </summary>
        public static bool CarriesCategory(ChronicleCategory category)
            => category != ChronicleCategory.Intelligence
               && category != ChronicleCategory.System;

        /// <summary>
        /// Whether one chronicle entry may be shown to this observer.
        ///
        /// **Every surface that reads `GameState.chronicle` must go through
        /// here.** The chronicle is the world's *true* record — it contains
        /// covert operations, research programmes, sponsored coups and capability
        /// theft for every country — and printing it raw hands the player, for
        /// free, everything collection is supposed to buy.
        ///
        /// Two views did exactly that: the Briefing's recent-events block and the
        /// CHRONICLE screen's filter, which sorted by country and category and
        /// never looked at publicity at all. The rule existed, was tested, and
        /// was simply not applied at the point of use.
        ///
        /// Our own affairs are always legible to us — a government knows what it
        /// did.
        /// </summary>
        public static bool CanShow(GameState state, ChronicleEntry entry)
        {
            if (entry == null) return false;
            if (state != null && entry.countryId == state.playerCountryId) return true;
            if (entry.publicity != Publicity.Public) return false;
            return CarriesCategory(entry.category);
        }

        /// <summary>Everything the world saw happen in a given month.</summary>
        public static List<WireItem> ForMonth(GameState state, GameDate month)
        {
            var items = new List<WireItem>();
            if (state == null) return items;

            foreach (var entry in state.chronicle)
            {
                if (entry.date.year != month.year || entry.date.month != month.month) continue;
                if (entry.publicity != Publicity.Public) continue;
                if (!CarriesCategory(entry.category)) continue;

                items.Add(new WireItem
                {
                    date = entry.date,
                    countryId = entry.countryId,
                    category = entry.category,
                    headline = entry.text,
                    involvesUs = entry.countryId == state.playerCountryId
                });
            }

            return items;
        }

        /// <summary>The month just resolved — what the rollover briefing reports.</summary>
        public static List<WireItem> LastMonth(GameState state)
            => state == null ? new List<WireItem>() : ForMonth(state, PreviousMonth(state.date));

        /// <summary>
        /// A dateline for one item, in the terminal's voice.
        /// `RUS — Moscow confirms the treaty.`
        /// </summary>
        public static string Format(GameState state, WireItem item)
        {
            var country = state?.FindCountry(item.countryId);
            string source = country != null
                ? country.displayName.ToUpperInvariant()
                : "WORLD";
            return $"{source} — {item.headline}";
        }

        static GameDate PreviousMonth(GameDate date)
            => date.month == 1
                ? new GameDate(date.year - 1, 12)
                : new GameDate(date.year, date.month - 1);
    }
}

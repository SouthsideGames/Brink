using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>How loudly something is asking to be looked at.</summary>
    public enum AttentionLevel
    {
        None,
        Information, // worth knowing; nothing is waiting on you
        Decision     // something is sitting on a panel waiting for an answer
    }

    /// <summary>One thing asking for the operator's attention, and where it lives.</summary>
    public class AttentionItem
    {
        public string viewId;
        public AttentionLevel level;
        public string summary;
    }

    /// <summary>
    /// What needs looking at, and which panel it is on (GDD §28.1).
    ///
    /// This game is made entirely of text, so the hardest problem is not
    /// simulating a decision — it is making sure the operator *knows* one is
    /// waiting. A consequence nobody was told about is not a consequence, it is
    /// a bug report.
    ///
    /// **It never blocks, by design.** Nothing here prevents ending the month.
    /// The operator is an adult running a government; they are allowed to ignore
    /// a warning and live with it, and a mistake they chose is the point of the
    /// game. What they must never do is make that mistake *without being told* —
    /// so the job of this system is to be impossible to miss and trivial to
    /// overrule.
    ///
    /// Nothing here is stored. Attention is recomputed from the live world every
    /// refresh, so it cannot go stale, cannot be forgotten to clear, and cannot
    /// disagree with the panel it points at.
    /// </summary>
    public static class AttentionSystem
    {
        public static List<AttentionItem> Collect(GameState state)
        {
            var items = new List<AttentionItem>();
            if (state?.PlayerCountry == null) return items;

            var player = state.PlayerCountry;

            void Add(string viewId, AttentionLevel level, string summary)
                => items.Add(new AttentionItem { viewId = viewId, level = level, summary = summary });

            // ---- decisions genuinely waiting on the operator ----

            if (state.activeCrises.Count > 0)
                Add("BRIEFING", AttentionLevel.Decision,
                    state.activeCrises.Count == 1
                        ? $"Crisis unanswered: {state.activeCrises[0].title}"
                        : $"{state.activeCrises.Count} crises unanswered");

            if (player.vacancies.Count > 0)
                foreach (var vacancy in player.vacancies)
                    Add("CABINET", AttentionLevel.Decision,
                        $"{CabinetLifecycle.TitleFor(player, vacancy.office)} vacant — " +
                        $"{CabinetLifecycle.MonthsBeforeGovernmentDecides - vacancy.monthsOpen} month(s) " +
                        "before the government appoints");

            if (state.skillPoints > 0)
                Add("OPERATOR", AttentionLevel.Decision,
                    $"{state.skillPoints} skill point(s) unspent");

            var confrontation = state.ActiveConfrontation;
            if (confrontation != null && !confrontation.resolved)
            {
                // Our reporting's read, never the acceptance test itself: the
                // old nudge fired off true willingness and told the operator, with
                // no collection, the month the enemy became willing.
                if (PeaceSystem.AssessDisposition(state, confrontation, state.playerCountryId)
                    >= SettlementDisposition.PotentiallyReceptive)
                    Add("MILITARY", AttentionLevel.Decision,
                        "Our reporting reads them as open to terms — see NEGOTIATED SETTLEMENT");
                else
                    Add("MILITARY", AttentionLevel.Information,
                        $"Confrontation active — {Phrase.Of(confrontation.escalation)}");
            }

            // A treasury draining faster than it fills, while it still can be
            // fixed. The real consequences of deficit spending land years later
            // (debt → confidence → markets → living standards), which is
            // exactly the kind of lag an operator on a phone cannot be expected
            // to notice from a raw balance — a test campaign bankrupted a
            // healthy country to −4,905 without one warning. Decision when the
            // account is already dry; a heads-up while there is still runway.
            if (state.treasuryTrendSeeded && state.treasuryTrend < -4f)
            {
                if (player.resources.treasury <= 0f)
                    Add("ECONOMY", AttentionLevel.Decision,
                        $"Treasury in deficit and sinking ~{-state.treasuryTrend:F0}/month");
                else if (player.resources.treasury / -state.treasuryTrend < 36f)
                    Add("ECONOMY", AttentionLevel.Information,
                        $"Spending exceeds income by ~{-state.treasuryTrend:F0}/month — reserves " +
                        $"carry ~{player.resources.treasury / -state.treasuryTrend:F0} months");
            }

            // ---- worth knowing ----

            int unseen = 0, unseenUrgent = 0;
            foreach (var notification in state.notifications)
            {
                if (notification.seen) continue;
                unseen++;
                if (notification.priority <= NotificationClass.Priority) unseenUrgent++;
            }
            if (unseenUrgent > 0)
                Add("BRIEFING", AttentionLevel.Decision, $"{unseenUrgent} urgent item(s) unread");
            else if (unseen > 0)
                Add("BRIEFING", AttentionLevel.Information, $"{unseen} item(s) unread");

            if (EconomySystem.SanctionPressureOn(state, player.id) > 0.01f)
                Add("ECONOMY", AttentionLevel.Information, "We are under sanction");
            if (player.economy.InRecession)
                Add("ECONOMY", AttentionLevel.Information,
                    player.economy.InDepression ? "The economy is in depression" : "The economy is contracting");

            var gov = player.government;
            if (gov.IsElective)
            {
                int monthsToElection = MonthsBetween(state.date, gov.nextElectionDate);
                if (monthsToElection >= 0 && monthsToElection <= 3)
                    Add("GOVERNMENT", AttentionLevel.Information,
                        monthsToElection == 0 ? "Election this month" : $"Election in {monthsToElection} month(s)");
            }
            if (gov.emergencyPowers && gov.emergencyPowersMonthsRemaining <= 2)
                Add("GOVERNMENT", AttentionLevel.Information,
                    $"Emergency powers lapse in {gov.emergencyPowersMonthsRemaining} month(s)");

            foreach (var network in state.networks)
                if (network.ownerId == player.id && network.compromised)
                {
                    Add("INTELLIGENCE", AttentionLevel.Information,
                        "A collection network has been rolled up");
                    break;
                }

            foreach (EndgameType type in System.Enum.GetValues(typeof(EndgameType)))
            {
                if (player.endgames.ProgressFor(type) < 100f) continue;
                Add("ENDGAME", AttentionLevel.Decision,
                    $"{EndgameSystem.NameOf(type)} is prepared and available");
                break;
            }

            if (player.technology.programs.Count == 0)
                Add("RESEARCH", AttentionLevel.Information, "No research programme running");

            return items;
        }

        /// <summary>Highest attention level anywhere, for the End Month affordance.</summary>
        public static AttentionLevel HighestLevel(List<AttentionItem> items)
        {
            var highest = AttentionLevel.None;
            foreach (var item in items)
                if (item.level > highest) highest = item.level;
            return highest;
        }

        /// <summary>Highest attention level on one panel, for its nav marker.</summary>
        public static AttentionLevel LevelFor(List<AttentionItem> items, string viewId)
        {
            var highest = AttentionLevel.None;
            foreach (var item in items)
                if (item.viewId == viewId && item.level > highest) highest = item.level;
            return highest;
        }

        /// <summary>How many decisions are sitting unanswered.</summary>
        public static int DecisionCount(List<AttentionItem> items)
        {
            int count = 0;
            foreach (var item in items)
                if (item.level == AttentionLevel.Decision) count++;
            return count;
        }

        /// <summary>
        /// Mark this month's traffic as read. Called when the operator opens the
        /// briefing — an item that has been looked at must stop shouting, or the
        /// markers become wallpaper and stop meaning anything.
        /// </summary>
        public static void MarkBriefingSeen(GameState state)
        {
            if (state == null) return;
            for (int i = 0; i < state.notifications.Count; i++)
                state.notifications[i].seen = true;
        }

        static int MonthsBetween(GameDate from, GameDate to)
            => (to.year - from.year) * 12 + (to.month - from.month);
    }
}

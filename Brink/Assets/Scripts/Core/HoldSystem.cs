using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Holding — advancing several quiet months deliberately (GDD §6, §28.2).
    ///
    /// **There was one way to pass time and it was a tap.** An operator running a
    /// stable country through a quiet stretch — waiting for a procurement
    /// programme to deliver, a research capability to mature, a treaty to bed in —
    /// pressed END MONTH, read a briefing with nothing in it, and pressed END
    /// MONTH again, twenty times. That is not pacing, it is a lack of one, and it
    /// makes inattention feel like an accident rather than a choice.
    ///
    /// Three rules make this a decision rather than a fast-forward:
    ///
    /// 1. **It ends the moment the game needs the operator.** A crisis, FLASH
    ///    traffic, a war, a vacant office, an emptying treasury: any of them stops
    ///    the hold and says which one. The operator never wakes up to find
    ///    something was decided for them while the clock ran.
    /// 2. **The months are genuinely forgone.** Command Points are not banked
    ///    beyond the ordinary Strategic Reserve — `CommandPointsState.BeginMonth`
    ///    already caps that — so holding for six months means six months of
    ///    capacity nobody spent. It is cheaper in attention and more expensive in
    ///    everything else, which is exactly the trade it should be.
    /// 3. **It is graded as what it is.** Nothing here awards XP or records an
    ///    initiative, so the annual evaluation sees a held year the way it sees a
    ///    passive one — and the harness has already measured what that costs
    ///    (`DRIFTER`, −0.22 of a grade against the same routine that engages).
    ///
    /// Deliberately capped at <see cref="MaxMonths"/>. A hold is a decision to
    /// stand back from a quiet stretch, not a way to skip to the end of the save.
    /// </summary>
    public static class HoldSystem
    {
        /// <summary>The longest single hold. A year is a stretch; a decade is an exit.</summary>
        public const int MaxMonths = 12;

        /// <summary>Months of runway below which an emptying treasury interrupts a hold.</summary>
        public const int RunwayMonths = 24;

        /// <summary>Why a hold ended, in the terminal's voice.</summary>
        public struct Result
        {
            /// <summary>Months actually resolved.</summary>
            public int months;

            /// <summary>Short reason it stopped, or empty when it simply ran out.</summary>
            public string stopped;

            public bool RanToCompletion => string.IsNullOrEmpty(stopped);
        }

        /// <summary>
        /// Whether the operator may stand back at all. A hold that begins on top
        /// of an open decision would resolve that decision by lapsing it — which
        /// is a legitimate outcome of ending a month and an illegitimate side
        /// effect of asking for quiet.
        /// </summary>
        public static bool CanHold(GameState state, out string reason)
        {
            if (state == null) { reason = "No session."; return false; }

            if (state.HasOpenCrisis)
            {
                reason = "A CRISIS IS OPEN. Answer it, or end the month and let it lapse — "
                       + "either is a decision; holding is not.";
                return false;
            }

            var player = state.PlayerCountry;
            if (player == null) { reason = "No posting."; return false; }

            if (player.vacancies.Count > 0)
            {
                reason = "AN OFFICE IS VACANT. The shortlist is waiting.";
                return false;
            }

            if (state.IsAtWar(player.id))
            {
                reason = "WE ARE AT WAR. Nothing about this month is quiet.";
                return false;
            }

            reason = "";
            return true;
        }

        /// <summary>
        /// Advance up to <paramref name="months"/>, stopping the moment anything
        /// needs deciding.
        /// </summary>
        public static Result Hold(GameState state, TurnManager turns, int months)
        {
            var result = new Result { months = 0, stopped = "" };
            if (!CanHold(state, out string blocked))
            {
                result.stopped = blocked;
                return result;
            }

            int wanted = months < 1 ? 1 : (months > MaxMonths ? MaxMonths : months);
            var player = state.PlayerCountry;

            for (int i = 0; i < wanted; i++)
            {
                int trafficBefore = state.notifications.Count;

                if (!turns.EndMonth())
                {
                    result.stopped = "THE MONTH WOULD NOT RESOLVE.";
                    return result;
                }
                result.months++;

                string interruption = Interruption(state, player, trafficBefore);
                if (string.IsNullOrEmpty(interruption)) continue;

                result.stopped = interruption;
                return result;
            }

            return result;
        }

        /// <summary>
        /// What, if anything, in the month just resolved requires the operator.
        ///
        /// Ordered by how badly: an open crisis first, then anything filed FLASH,
        /// then the structural interruptions. FLASH is the right tripwire because
        /// §28.2 already defines it as traffic that cannot be left alone — one
        /// definition of "this needs you", read here rather than a second list
        /// that would drift away from it.
        /// </summary>
        static string Interruption(GameState state, CountryState player, int trafficBefore)
        {
            if (state.HasOpenCrisis) return "A CRISIS IS OPEN.";

            for (int i = trafficBefore; i < state.notifications.Count; i++)
                if (state.notifications[i].priority == NotificationClass.Flash)
                    return $"FLASH — {state.notifications[i].title}.";

            if (player == null) return "THE POSTING ENDED.";
            if (player.vacancies.Count > 0) return "AN OFFICE HAS FALLEN VACANT.";
            if (state.IsAtWar(player.id)) return "WE ARE AT WAR.";

            // The treasury warning the briefing already computes. A hold is
            // precisely the situation in which a slow fiscal collapse would go
            // unnoticed — which is the failure that put `treasuryTrend` in the
            // game in the first place.
            if (state.treasuryTrend < -4f && player.resources.treasury > 0f
                && player.resources.treasury / -state.treasuryTrend < RunwayMonths)
                return "THE TREASURY IS EMPTYING.";
            if (player.resources.treasury <= 0f) return "THE TREASURY IS EMPTY.";

            return "";
        }
    }
}

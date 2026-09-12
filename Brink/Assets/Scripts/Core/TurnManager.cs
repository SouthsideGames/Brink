using System;
using Brink.Data;
using SkillEffect = Brink.Data.SkillEffect;

namespace Brink.Core
{
    /// <summary>
    /// Drives the monthly loop (GDD §6). Phase 0 implements the skeleton:
    /// End Month advances the date, resolution hooks fire in order, CP refreshes,
    /// and year-end raises the annual evaluation hook (real evaluation is Phase 10).
    /// Plain C# — no Unity dependency — so it is fully edit-mode testable.
    /// </summary>
    public class TurnManager
    {
        public GameState State { get; }

        /// <summary>Fired at the start of each new month, after CP refresh.</summary>
        public event Action<GameDate> MonthStarted;

        /// <summary>
        /// Resolution hook: officials/countries/markets act here in later phases.
        /// Fired after the player ends the month, before the date advances.
        /// </summary>
        public event Action<GameState> ResolveMonth;

        /// <summary>Fired when a December resolves — annual evaluation entry point (GDD §25.2).</summary>
        public event Action<int> YearEnded;

        /// <summary>
        /// Fired once the resolved month is complete — after every
        /// <see cref="ResolveMonth"/> handler *and* after <see cref="YearEnded"/>
        /// — while the resolved date is still current. This is the causal month
        /// boundary (spec 26 §3a): an observer that needs to see the whole month
        /// as one interval closes here, and anything that happens after this
        /// point — the operator's turn, a crisis they let lapse at the top of
        /// the next <see cref="EndMonth"/> — belongs to the next month.
        /// Observational: nothing that moves the world should subscribe.
        /// </summary>
        public event Action<GameState> MonthResolved;

        public TurnManager(GameState state)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
        }

        /// <summary>
        /// Player ends the current month: world resolves, consequences apply,
        /// then the next month begins with refreshed Command Points.
        /// Returns false when an unresolved Crisis Turn blocks the month end.
        /// </summary>
        public bool EndMonth()
        {
            // The month always advances.
            //
            // An unresolved crisis used to refuse the turn outright. That made
            // the game argue with the operator instead of letting them be wrong:
            // a government that fails to decide is a real outcome, and replacing
            // it with a wall removed the most interesting mistake available.
            // Failing to answer now *lapses* — see CrisisSystem.LapseUnanswered —
            // which costs standing rather than the turn.
            CrisisSystem.LapseUnanswered(State);

            var resolvedDate = State.date;
            GameLog.Info("TURN", $"Resolving {resolvedDate.DisplayString}.");

            // Everything filed from here on is this month's traffic, and passes
            // through the Cabinet before the operator reads it (GDD §28.1).
            int trafficStart = State.notifications.Count;

            ResolveMonth?.Invoke(State);

            if (resolvedDate.IsYearEnd)
            {
                GameLog.Info("TURN", $"Year {resolvedDate.year} concluded. Annual evaluation pending.");
                YearEnded?.Invoke(resolvedDate.year);
            }

            // The month is now whole. Everything from here to the top of the
            // next EndMonth is the operator's turn.
            MonthResolved?.Invoke(State);

            ReportingSystem.FilterMonth(State, trafficStart);

            State.date = resolvedDate.NextMonth();

            // Command capacity = baseline + emergency authority + operator skill.
            int cpBonus = (int)ProgressionSystem.EffectValue(State, SkillEffect.CommandCapacity);
            var playerGov = State.PlayerCountry?.government;
            if (playerGov != null && playerGov.emergencyPowers) cpBonus += 2;
            int reserveBonus = (int)ProgressionSystem.EffectValue(State, SkillEffect.StrategicReserve);
            State.commandPoints.BeginMonth(cpBonus, reserveBonus);

            int influenceGain = GameState.InfluencePerMonth
                                + (int)ProgressionSystem.EffectValue(State, SkillEffect.DelegationBandwidth);
            State.influence = Math.Min(GameState.InfluenceCap + influenceGain - GameState.InfluencePerMonth,
                                       State.influence + influenceGain);

            // ARCHIVE, not ADVISORY (2026-08): the date and CP are on the status
            // bar every frame; as an advisory this was 120 items a decade of the
            // terminal telling the operator what the terminal already shows.
            State.AddNotification(NotificationClass.Archive, "MONTH START",
                $"{State.date.DisplayString}. Command capacity: {State.commandPoints.current} CP.");
            if (State.activeCrises.Count > 0)
                State.AddNotification(NotificationClass.Flash, "CRISIS AWAITING DECISION",
                    "A crisis is open. Ending the month without answering it will be recorded " +
                    "as this office having failed to decide.");

            GameLog.Info("TURN", $"Month started: {State.date.DisplayString}. CP available: {State.commandPoints.current}.");
            MonthStarted?.Invoke(State.date);
            return true;
        }

        /// <summary>Direct intervention spends CP (GDD §7.1). Returns false when capacity is exhausted.</summary>
        public bool SpendCommandPoints(int amount, string reason)
        {
            if (!State.commandPoints.Spend(amount))
            {
                GameLog.Warn("CP", $"Insufficient Command Points for: {reason} (need {amount}, have {State.commandPoints.current}).");

                // A refusal is data. An action the operator keeps reaching for
                // and keeps being denied is mispriced or badly explained, and
                // that only shows up if the failures are recorded too.
                Telemetry.Record(State, TelemetryKind.PlayerAction, State.playerCountryId,
                    Telemetry.Bucket(reason), reason, amount, success: false);
                return false;
            }

            GameLog.Info("CP", $"{amount} CP spent: {reason}. Remaining: {State.commandPoints.current}.");

            // Recorded here rather than at each verb: almost every player action
            // passes through this one function carrying a reason string, so a
            // verb added later is captured without anyone remembering to
            // instrument it.
            Telemetry.Record(State, TelemetryKind.PlayerAction, State.playerCountryId,
                Telemetry.Bucket(reason), reason, amount);
            return true;
        }
    }
}

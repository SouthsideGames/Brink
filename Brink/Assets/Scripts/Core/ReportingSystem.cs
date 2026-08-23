using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// What the operator is actually told (GDD §28.1).
    ///
    /// The operator does not observe the world; they read what their government
    /// sends up. Every item except their own command traffic passes through the
    /// official who runs that pillar, and a weak minister's desk reports badly:
    /// things arrive stripped of urgency, or never arrive at all.
    ///
    /// This is the clause that makes the Cabinet matter beyond execution
    /// quality. Before it, <c>Official.competence</c> was read at exactly one
    /// site — scaling autonomous outcomes — so appointing a capable minister
    /// changed how well the pillar performed and nothing about how well the
    /// operator could see it. Delegation was a throughput decision. It is now
    /// also an information decision, which is what §28.1 asks for.
    ///
    /// Three rules keep it fair rather than merely punishing:
    ///
    /// 1. **A decision is never withheld.** FLASH means an answer is required
    ///    this month; suppressing one would strand the player in front of a
    ///    turn they cannot take. Incompetence costs awareness, never agency.
    /// 2. **What you run yourself, you see yourself.** Direct Control removes
    ///    the intermediary, so it removes the filter — the cost of Direct
    ///    Control is CP and the official's trust, not blindness.
    /// 3. **The record is honest even when the briefing is not.** Nothing here
    ///    touches <c>GameState.chronicle</c>. A missed item still happened and
    ///    can still be found in CHRONICLE later, which is exactly the
    ///    experience of learning what your government did not tell you.
    /// </summary>
    public static class ReportingSystem
    {
        /// <summary>
        /// Competence at or above which a desk reports cleanly.
        ///
        /// Calibrated against the seeded cabinet, which rolls 40–78
        /// (`WorldFactory.MakeCabinet`, and the same range on reshuffle in
        /// `GovernmentSystem`): a *median* appointment must report nearly
        /// everything. Set at the top of that range instead, every default
        /// cabinet quietly lost about a quarter of the world's news from month
        /// one — which a new player reads as an empty game, not as a mediocre
        /// minister. Degradation has to be something they did, not the baseline.
        /// </summary>
        public const float ReliableCompetence = 60f;

        /// <summary>
        /// Competence at or below which a desk is barely reporting at all.
        /// Below the seeded floor, so reaching it takes a bad appointment, a
        /// purge or a coup rather than an unlucky roll at world creation.
        /// </summary>
        public const float FailingCompetence = 25f;

        /// <summary>Set true to bypass filtering entirely (GDD §34.1 debug control).</summary>
        public static bool Disabled;

        /// <summary>
        /// Probability that this desk mishandles any one item. Zero for a desk
        /// with no intermediary, and never certain even at the bottom — a
        /// hopeless minister still gets some of it through.
        /// </summary>
        public static float MishandleChanceFor(GameState state, ReportingDesk desk)
        {
            var official = OfficialFor(state, desk);
            if (official == null) return 0f;

            // Running it personally means reading it personally.
            if (official.mode == ControlMode.DirectControl) return 0f;

            float span = ReliableCompetence - FailingCompetence;
            float shortfall = (ReliableCompetence - official.competence) / span;
            if (shortfall <= 0f) return 0f;
            if (shortfall > 1f) shortfall = 1f;

            float chance = shortfall * 0.55f;

            // A stated priority focuses a desk on what the operator asked about,
            // so Directed reporting is better than Autonomous but still filtered.
            if (official.mode == ControlMode.Directed) chance *= 0.5f;

            // Trust is the willingness to bring the operator bad news.
            if (official.trust < 40f) chance += (40f - official.trust) * 0.004f;

            return chance < 0f ? 0f : (chance > 0.75f ? 0.75f : chance);
        }

        /// <summary>
        /// A 0..100 read of how well a desk is currently reporting, for the
        /// CABINET view. The player must be able to predict this — an invisible
        /// information penalty is indistinguishable from a bug.
        /// </summary>
        public static float ReportingQualityFor(GameState state, ReportingDesk desk)
            => 100f - MishandleChanceFor(state, desk) * 100f / 0.75f;

        public static Official OfficialFor(GameState state, ReportingDesk desk)
        {
            if (desk == ReportingDesk.Command) return null;
            var office = OfficeFor(desk);
            for (int i = 0; i < state.cabinet.Count; i++)
                if (state.cabinet[i].office == office) return state.cabinet[i];
            return null;
        }

        public static Pillar OfficeFor(ReportingDesk desk)
        {
            switch (desk)
            {
                case ReportingDesk.Military: return Pillar.Military;
                case ReportingDesk.Economy: return Pillar.Economy;
                case ReportingDesk.Intelligence: return Pillar.Intelligence;
                case ReportingDesk.Diplomacy: return Pillar.Diplomacy;
                default: return Pillar.Government;
            }
        }

        public static ReportingDesk DeskFor(Pillar pillar)
        {
            switch (pillar)
            {
                case Pillar.Military: return ReportingDesk.Military;
                case Pillar.Economy: return ReportingDesk.Economy;
                case Pillar.Intelligence: return ReportingDesk.Intelligence;
                case Pillar.Diplomacy: return ReportingDesk.Diplomacy;
                default: return ReportingDesk.Government;
            }
        }

        /// <summary>
        /// Filter the traffic generated since <paramref name="fromIndex"/>.
        /// Called once per month from <see cref="TurnManager"/>, after the world
        /// has resolved and before the operator reads the briefing.
        ///
        /// Returns the number of items that never reached the desk.
        /// </summary>
        public static int FilterMonth(GameState state, int fromIndex)
        {
            if (Disabled) return 0;
            if (state.cabinet == null || state.cabinet.Count == 0) return 0;

            int monthIndex = state.date.MonthsSince(state.startDate);
            int missed = 0;

            for (int i = state.notifications.Count - 1; i >= fromIndex; i--)
            {
                var notification = state.notifications[i];

                // Rule 1: a decision is never withheld.
                if (notification.priority == NotificationClass.Flash) continue;
                if (notification.desk == ReportingDesk.Command) continue;

                float chance = MishandleChanceFor(state, notification.desk);
                if (chance <= 0f) continue;

                // Deterministic per item: seeded from the month and the item's
                // position so a reloaded save is told exactly the same things.
                var rng = new Random(unchecked(
                    state.rngSeed * 15485863
                    + monthIndex * 6151
                    + (int)notification.desk * 97
                    + i * 389));

                if (rng.NextDouble() >= chance) continue;

                if (notification.priority == NotificationClass.Priority)
                {
                    // Important news survives, but arrives without its urgency —
                    // buried in the routine traffic rather than at the top.
                    notification.priority = NotificationClass.Advisory;
                    continue;
                }

                state.notifications.RemoveAt(i);
                missed++;
            }

            if (missed > 0)
                GameLog.Debug("REPORT", $"{missed} item(s) never reached the operator's desk.");

            return missed;
        }
    }
}

using Brink.Data;

namespace Brink.Audio
{
    /// <summary>
    /// What the world sounds like — pure functions from game state to cues, so
    /// the mapping is edit-mode testable and the MonoBehaviour side only plays.
    ///
    /// Added in the 2026-08 audio audit. The audio system had five music
    /// states, twenty sound ids, a mixer and a preferences model, and **no
    /// call site outside the debug console**: the game ran with ambience only.
    /// </summary>
    public static class AudioCues
    {
        /// <summary>The music state the player's situation calls for.</summary>
        public static MusicState MusicFor(GameState state)
        {
            if (state == null || state.PlayerCountry == null) return MusicState.MainMenu;

            string me = state.playerCountryId;
            if (state.HasOpenCrisis) return MusicState.Crisis;

            var worst = MusicState.Peace;
            foreach (var confrontation in state.confrontations)
            {
                if (confrontation.resolved || !confrontation.Involves(me)) continue;
                if (confrontation.escalation >= EscalationState.LimitedConflict) return MusicState.War;
                if (confrontation.escalation >= EscalationState.Crisis) worst = MusicState.Crisis;
                else if (worst < MusicState.Tension) worst = MusicState.Tension;
            }
            return worst;
        }

        /// <summary>The pillar a view id belongs to, for context-aware music.</summary>
        public static PillarContext ContextFor(string viewId)
        {
            switch (viewId)
            {
                case "MILITARY": return PillarContext.Military;
                case "ECONOMY": return PillarContext.Economy;
                case "INTELLIGENCE": return PillarContext.Intelligence;
                case "DIPLOMACY": return PillarContext.Diplomacy;
                case "GOVERNMENT": return PillarContext.Government;
                default: return PillarContext.World;
            }
        }

        /// <summary>The alert a notification class earns. Wire and Archive are silent.</summary>
        public static SfxId AlertFor(NotificationClass priority)
        {
            switch (priority)
            {
                case NotificationClass.Flash: return SfxId.FlashAlert;
                case NotificationClass.Priority: return SfxId.PriorityAlert;
                case NotificationClass.Advisory: return SfxId.AdvisoryAlert;
                default: return SfxId.None;
            }
        }

        /// <summary>
        /// The single loudest alert owed for notifications added after
        /// <paramref name="seenCount"/>. One cue per month, not one per item —
        /// a busy month is a busy month, not a siren test.
        /// </summary>
        public static SfxId LoudestNewAlert(GameState state, int seenCount)
        {
            var loudest = SfxId.None;
            for (int i = seenCount; i < state.notifications.Count; i++)
            {
                var cue = AlertFor(state.notifications[i].priority);
                if (cue > loudest) loudest = cue;
            }
            return loudest;
        }
    }
}

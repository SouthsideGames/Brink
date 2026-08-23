using System;

namespace Brink.Data
{
    /// <summary>Notification priority classes (GDD §28.2). Lower value = higher priority.</summary>
    public enum NotificationClass
    {
        Flash,    // immediate decision required
        Priority, // important this turn
        Advisory, // strategically relevant
        Wire,     // general world news
        Archive   // historical/informational
    }

    /// <summary>
    /// Which desk filed this item (GDD §28.1). Reporting passes through the
    /// official who runs that pillar, so what reaches the operator depends on
    /// who they delegated to.
    ///
    /// <c>Command</c> is the operator's own traffic — the turn structure,
    /// crises, obligations, their own orders coming back confirmed. It has no
    /// intermediary and is never filtered. It is also the **default**, so a
    /// notification added without a desk can never silently go missing; making
    /// something filterable is an explicit decision.
    /// </summary>
    public enum ReportingDesk
    {
        Command,
        Military,
        Economy,
        Intelligence,
        Diplomacy,
        Government
    }

    /// <summary>One briefing/notification item surfaced to the operator.</summary>
    [Serializable]
    public class Notification
    {
        public GameDate date;
        public NotificationClass priority;
        public string title;
        public string body;
        public string countryId;

        /// <summary>The desk that filed it (GDD §28.1). Command = unfiltered.</summary>
        public ReportingDesk desk;

        /// <summary>
        /// True once the operator has opened the briefing since this arrived.
        ///
        /// Attention markers are only useful while they mean something. An item
        /// that keeps shouting after it has been read turns every marker into
        /// wallpaper, and then a real one goes unnoticed.
        /// </summary>
        public bool seen;
    }
}

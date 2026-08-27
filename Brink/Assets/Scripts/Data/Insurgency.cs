using System;

namespace Brink.Data
{
    /// <summary>
    /// Why an armed movement exists. The cause decides what would end it, which
    /// is the whole reason it is stored rather than inferred: a garrison answers
    /// one of these and is beside the point for the other two.
    /// </summary>
    public enum InsurgencyCause
    {
        /// <summary>Foreign troops on ground that is not theirs. Ends when they leave.</summary>
        Occupation = 0,

        /// <summary>Hardship nobody in the capital is answering. Ends when it is.</summary>
        Deprivation = 1,

        /// <summary>A region that does not consider itself part of this state.</summary>
        Separatism = 2
    }

    /// <summary>
    /// An armed movement rooted in one place (GDD §12, §17.1, §19).
    ///
    /// **The first actor in this game that is not a state.** Everything else on
    /// the map is a government with a cabinet, a treasury and a seat at the
    /// table; this is none of those things. It cannot be sanctioned, it will not
    /// sign anything, and it does not care what the world thinks of it.
    ///
    /// It is deliberately *not* stored with a target country. The ground decides
    /// who it fights: whoever holds <see cref="locationId"/> this month is who it
    /// is shooting at. That is what makes an occupation insurgency end when the
    /// occupier leaves rather than following them home, and it is what lets a
    /// province change hands mid-campaign without the movement losing its thread.
    /// </summary>
    [Serializable]
    public class Insurgency
    {
        public string id;

        /// <summary>The ground it is rooted in. Its holder is who it fights.</summary>
        public string locationId;

        public InsurgencyCause cause;
        public GameDate began;

        /// <summary>
        /// 0..100 what it can actually do — fighters, weapons, organisation.
        ///
        /// Approaches a ceiling set by <see cref="support"/> and by whatever a
        /// sponsor is shipping in. **Never written directly by a monthly cost**:
        /// a value that only ratchets one way is this codebase's most-repeated
        /// bug, and a movement that could never be worn down would make garrison
        /// duty pointless.
        /// </summary>
        public float strength;

        /// <summary>
        /// 0..100 how much of the population is behind it.
        ///
        /// The one that matters. Strength can be shot at; support has to be
        /// answered, and it drifts toward whatever the conditions on that ground
        /// will hold. Kill every fighter in a province that still wants you gone
        /// and you have bought eighteen months.
        /// </summary>
        public float support;

        /// <summary>
        /// The state arming it, or empty. Secret until <see cref="sponsorExposed"/>.
        /// </summary>
        public string sponsorId = "";

        /// <summary>Months the current sponsor has been running it.</summary>
        public int sponsorMonths;

        /// <summary>Cumulative equipment shipped in, for the record and the verdict.</summary>
        public float armsSupplied;

        /// <summary>
        /// 0..100 how visible the sponsorship has become. Deniability is a
        /// wasting asset: every shipment is a chance to be traced, and a
        /// programme run long enough is a programme that will be found.
        /// </summary>
        public float exposure;

        /// <summary>True once the world has attributed it. There is no going back.</summary>
        public bool sponsorExposed;

        /// <summary>Consecutive months below the threshold at which it stops being a movement.</summary>
        public int fadingMonths;

        public bool HasSponsor => !string.IsNullOrEmpty(sponsorId);
    }
}

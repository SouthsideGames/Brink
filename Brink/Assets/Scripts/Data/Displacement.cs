using System;

namespace Brink.Data
{
    /// <summary>
    /// People who have had to leave, and the states carrying them (GDD §12, §27).
    ///
    /// **The missing externality.** Everything bad that happened to a country
    /// stayed inside its borders. A state could be bombed flat, starved by a
    /// blockade and torn apart by an insurgency, and its neighbours would notice
    /// nothing but a market number — so a war was a private arrangement between
    /// the two governments fighting it, and the region it was fought in had no
    /// opinion about that.
    ///
    /// Both values are 0..100 shares of the population rather than headcounts,
    /// for the same reason the rest of the model is: a number an operator can
    /// reason about beats a number that is merely precise.
    ///
    /// Zero on an old save is correct — nobody was displaced in a world that had
    /// no way to displace them — so this needs no migration step.
    /// </summary>
    [Serializable]
    public class DisplacementState
    {
        /// <summary>
        /// 0..100 share of this country's own people who have been displaced.
        /// Approaches a target set by war, insurgency and deprivation, so it
        /// recedes when the country becomes liveable again.
        /// </summary>
        public float displaced;

        /// <summary>0..100 share-equivalent this country is currently hosting.</summary>
        public float hosted;

        /// <summary>
        /// Whether this state is refusing arrivals.
        ///
        /// A **national** posture rather than a per-pair one, deliberately: a
        /// government either accepts people or it does not, and making it
        /// bilateral would turn one political decision into twenty-three
        /// administrative ones. It is the same shape as `CivicPosture` — an
        /// instrument with a standing price rather than a one-off act.
        /// </summary>
        public bool bordersClosed;

        /// <summary>Consecutive months the border has been shut.</summary>
        public int monthsClosed;
    }
}

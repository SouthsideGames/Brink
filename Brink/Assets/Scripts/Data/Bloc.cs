using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// A standing alignment with a leader and members (GDD §15.2).
    ///
    /// **Blocs existed only as a term in a formula.** `DiplomacySystem.RivalGravity`
    /// prices being deeply aligned with somebody's enemy, and it does that well —
    /// but there was nothing on the map a state could *join*, lead, be excluded
    /// from, or defect from. Coalitions are raised for one war and dissolve after
    /// it; treaties are bilateral; alliance obligations are a consequence of a
    /// signature rather than a structure. The world had sides and no side had a
    /// name.
    ///
    /// Deliberately a small object. It carries no commitments of its own — the
    /// defence pact, the trade preference and the transit right all still live in
    /// `Treaty`, where the acceptance logic and the reputational cost of breaking
    /// them already are. What a bloc adds is the thing none of those could
    /// express: that a group of states acts together, and that everyone can see
    /// which group.
    /// </summary>
    [Serializable]
    public class Bloc
    {
        public string id;

        /// <summary>In-universe name, e.g. "THE NORTHERN UNDERSTANDING".</summary>
        public string name;

        /// <summary>Who holds it together, and pays to.</summary>
        public string leaderId;

        /// <summary>Everyone in it, the leader included.</summary>
        public List<string> memberIds = new List<string>();

        /// <summary>
        /// 0..100 how much of a single thing this actually is.
        ///
        /// Drifts toward what the members' own relations and alignment will hold,
        /// so a bloc of states that dislike each other comes apart on its own and
        /// the leader has to keep buying it. Nothing here ratchets.
        /// </summary>
        public float cohesion = 50f;

        public GameDate founded;
        public bool dissolved;

        public bool Has(string countryId) => memberIds.Contains(countryId);
    }
}

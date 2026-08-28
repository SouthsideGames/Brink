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
    /// **A bloc now carries commitments** (user decision, 2026-08-27, supersedes
    /// this type's original "it carries no commitments" rule). That rule was
    /// written when a bloc was read in two places and was right about what it
    /// protected: acceptance logic and the reputational cost of breaking a
    /// promise live in `Treaty`, and duplicating them would have been a second
    /// treaty system.
    ///
    /// What it could not express is the thing an alliance actually is. A mutual
    /// defence pact between eight states is not twenty-eight bilateral treaties —
    /// it is one signature that obliges you to everybody at once, and everybody
    /// to you. Building NATO out of `Treaty` meant N² agreements *and* fought the
    /// game's own `PactAnxiety`, which makes every pact past the fourth harder to
    /// sign. The multilateral layer had to live somewhere, and this is the object
    /// that already had a leader, a membership, a cohesion figure and a way in
    /// and out.
    ///
    /// The division of labour is now: **`Treaty` is the bilateral bargain**, where
    /// two states trade clauses and each tries to receive more than it gives;
    /// **`Bloc` is the multilateral alliance**, where everybody carries the same
    /// terms because that is what makes it one thing rather than a list.
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

        /// <summary>
        /// What every member carries toward every other member.
        ///
        /// Uniform by construction — there is no `ClauseSide` here, because a
        /// bloc in which some members are guaranteed and others only guarantee is
        /// not an alliance, it is a protectorate with extra steps. Asymmetric
        /// bargains are exactly what `Treaty.clauses` is for; keep them there.
        ///
        /// Empty on an old save is correct rather than merely blank: blocs that
        /// predate this genuinely carried no commitments, so they load as the
        /// declarations of alignment they were and no migration is owed.
        /// </summary>
        public List<TreatyCommitment> commitments = new List<TreatyCommitment>();

        /// <summary>
        /// Members who were called upon and would not come, kept after they are
        /// out of <see cref="memberIds"/>.
        ///
        /// Without this the record of an abandonment lives only in the pairwise
        /// memory of the states that were let down, so a bloc could be walked out
        /// on and re-joined as though nothing had happened.
        /// </summary>
        public List<string> repudiatedBy = new List<string>();

        public GameDate founded;
        public bool dissolved;

        public bool Has(string countryId) => memberIds.Contains(countryId);

        /// <summary>Whether this bloc obliges its members to a given commitment.</summary>
        public bool Guarantees(TreatyCommitment commitment) => commitments.Contains(commitment);

        /// <summary>Everyone in it except <paramref name="countryId"/>.</summary>
        public List<string> PartnersOf(string countryId)
        {
            var partners = new List<string>();
            foreach (string memberId in memberIds)
                if (memberId != countryId) partners.Add(memberId);
            return partners;
        }
    }
}

using System;
using System.Collections.Generic;

namespace Brink.Data
{
    /// <summary>
    /// Terms a settlement can contain (GDD §26). A peace is negotiated from
    /// these rather than accepted wholesale, and a proposal can mix demands with
    /// concessions — which is what makes a hard term swallowable.
    /// </summary>
    /// <summary>
    /// How a draft settlement is likely to be received, as far as our own
    /// reporting can tell. Deliberately coarse: the exact acceptance test is
    /// ground truth and is never shown to the player (GDD §26).
    /// </summary>
    public enum SettlementOutlook
    {
        NoTerms,
        Unknown,    // we have no read on their politics at all
        Unlikely,
        Uncertain,
        Likely
    }

    public enum PeaceTerm
    {
        // ---- demands: things we ask of them ----
        TerritorialCession,   // they cede the objective
        Reparations,          // they pay
        Demilitarization,     // they stand their forces down
        ResourceAccess,       // they guarantee us access
        Recognition,          // they formally accept our position
        TreatyRevision,       // their commitments are rewritten in our favour

        // ---- concessions: things we offer ----
        Withdrawal,           // we hand back what we occupy
        SanctionsRelief,      // we lift economic measures
        PrisonerExchange,     // mutual, cheap, and expected
        SecurityGuarantee,    // we underwrite their security

        // Appended, never reordered — JsonUtility persists enums by ordinal, so
        // inserting above would silently reinterpret every archived settlement.
        /// <summary>
        /// They change course: the government that fought us has to be seen
        /// changing its policy, which costs it standing at home. The last of
        /// GDD §26's ten terms to be built — the other nine matched exactly.
        /// </summary>
        PoliticalConcessions
    }

    /// <summary>A settlement offer: what we demand, and what we are prepared to give.</summary>
    [Serializable]
    public class PeaceProposal
    {
        public List<PeaceTerm> terms = new List<PeaceTerm>();

        public bool Has(PeaceTerm term) => terms.Contains(term);

        public static PeaceProposal Of(params PeaceTerm[] terms)
        {
            var proposal = new PeaceProposal();
            proposal.terms.AddRange(terms);
            return proposal;
        }
    }

    /// <summary>An archived settlement, so the chronicle records what was agreed.</summary>
    [Serializable]
    public class SettlementRecord
    {
        public GameDate date;
        public string confrontationId;
        public string proposerId;
        public string accepterId;
        public List<PeaceTerm> terms = new List<PeaceTerm>();
        public string summary;
    }
}

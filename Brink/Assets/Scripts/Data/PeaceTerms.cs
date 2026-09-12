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

    /// <summary>
    /// How the other side is disposed toward ending the war at all, before any
    /// terms are priced — as far as our own reporting can tell (GDD §26,
    /// spec 01 §5a). The console used to print the true acceptance bit as
    /// "OPEN TO TERMS / RESISTING"; watching it flip told the operator exactly
    /// when hidden willingness crossed its threshold, with no collection.
    /// Ordered from least to most receptive so a comparison reads naturally;
    /// `Unknown` first because it is what no collection yields.
    /// </summary>
    public enum SettlementDisposition
    {
        Unknown = 0,
        HighlyResistant,
        Resistant,
        Uncertain,
        PotentiallyReceptive,
        LikelyReceptive
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
        PoliticalConcessions,

        /// <summary>
        /// **They** lift the economic measures they have imposed on us.
        ///
        /// Reported from play: the settlement screen could offer to lift *our*
        /// sanctions as a concession, but there was no way to demand relief from
        /// theirs — so a war fought while under embargo could be won and leave the
        /// embargo in place. Ending the coercion aimed at you is one of the most
        /// ordinary things a state actually negotiates for.
        ///
        /// The mirror of <see cref="SanctionsRelief"/>, and the pair is the point:
        /// a settlement where both sides stand their measures down is a real,
        /// balanced bargain the game previously could not express.
        /// </summary>
        SanctionsLifted
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

using System;

namespace Brink.Data
{
    /// <summary>
    /// What a trade link is actually for (GDD §20).
    ///
    /// A general link moves goods and helps growth. A link negotiated around a
    /// commodity does more: it raises the importer's ceiling for that resource,
    /// which is the only route besides research and conquest to fixing an
    /// authored energy or materials dependency.
    /// </summary>
    public enum TradeFocus
    {
        General,
        Energy,
        Materials,

        /// <summary>
        /// Grain and agricultural staples. Appended so existing saves keep
        /// their focus ordinals; declaring it earlier would silently relabel
        /// every stored link.
        /// </summary>
        Food
    }

    /// <summary>
    /// A bilateral trade relationship (GDD §20). Volume is the strategic weight
    /// of the link; tariffs and embargoes are the levers applied to it.
    /// </summary>
    [Serializable]
    public class TradeRelation
    {
        public string countryA;
        public string countryB;
        public float volume;   // 0..100 strategic trade weight
        public float tariff;   // 0..100 % applied by either side
        public bool embargoed;

        /// <summary>What the link is built around. Set when it is negotiated.</summary>
        public TradeFocus focus;

        /// <summary>
        /// Who sought the arrangement. The supplicant in a resource deal takes
        /// the dependence; the supplier takes the leverage.
        /// </summary>
        public string initiatedBy = "";

        /// <summary>Either partner can commission the costed freight detour. False in old saves.</summary>
        public bool avoidPassages;

        public bool Involves(string id) => countryA == id || countryB == id;
        public string PartnerOf(string id) => id == countryA ? countryB : countryA;
    }

    /// <summary>
    /// Terms on the table for a trade agreement (GDD §20).
    ///
    /// Deliberately shaped like <c>PeaceProposal</c>: the player has already
    /// learned that idiom at the negotiating table, and a deal is a deal.
    /// </summary>
    [Serializable]
    public class TradeDeal
    {
        public string partnerId;
        public TradeFocus focus = TradeFocus.General;

        /// <summary>Strategic weight sought, 0..100. More benefit, more dependence.</summary>
        public float volume = 30f;

        /// <summary>Tariff we accept, 0..100. Lower favours us and costs them.</summary>
        public float tariff = 20f;

        /// <summary>
        /// Terms favourable to them: we take the worse side of the bargain to
        /// get the arrangement signed. The concession that makes a hard ask
        /// possible, exactly as in a peace proposal.
        /// </summary>
        public bool preferentialTerms;
    }

    /// <summary>Severity scale for economic coercion (GDD §21 Strategic Severity).</summary>
    public enum SanctionSeverity
    {
        Routine,
        Pressure,
        Coercive,
        Severe,
        Existential
    }

    /// <summary>
    /// An active sanctions regime. Economic pressure can backfire on the sender
    /// through inflation, supply disruption and financial exposure (GDD §20).
    /// </summary>
    [Serializable]
    public class Sanction
    {
        public string senderId;
        public string targetId;
        public SanctionSeverity severity;
        public GameDate imposedDate;
        public int monthsActive;

        /// <summary>
        /// Why it was imposed — RIVALRY, REPUDIATION, CRISIS, PLAYER. Read by
        /// nothing in the simulation; it exists so a long-run probe can say
        /// where a world's standing regimes come from, which is the diagnostic
        /// the sanction ratchet needed and did not have. Empty on old saves.
        /// </summary>
        public string cause = "";

        /// <summary>Damage multiplier applied to the target.</summary>
        public float Weight
        {
            get
            {
                switch (severity)
                {
                    case SanctionSeverity.Routine: return 0.35f;
                    case SanctionSeverity.Pressure: return 0.8f;
                    case SanctionSeverity.Coercive: return 1.5f;
                    case SanctionSeverity.Severe: return 2.4f;
                    default: return 3.6f;
                }
            }
        }

        /// <summary>Blowback multiplier applied to the sender.</summary>
        public float Blowback => Weight * 0.4f;
    }
}

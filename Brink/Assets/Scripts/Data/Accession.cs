using System;

namespace Brink.Data
{
    /// <summary>
    /// Who is being persuaded to join (GDD §15, §22).
    ///
    /// Two different arguments, made to two different audiences, with opposite
    /// weaknesses. An elite accession is quick and resented; a popular one is
    /// slow and durable.
    /// </summary>
    public enum AccessionRoute
    {
        /// <summary>
        /// Buy the government. Works on a fractured elite that has more to gain
        /// from us than from their own state — and the population, who were
        /// never asked, know exactly what happened.
        /// </summary>
        Elite,

        /// <summary>
        /// Win the population. Needs a country whose people have stopped
        /// believing in it, and takes far longer — but what is agreed to
        /// willingly does not have to be held down afterwards.
        /// </summary>
        Popular
    }

    /// <summary>
    /// A standing effort to bring another country into ours by persuasion
    /// rather than force (GDD §15.1, §22).
    ///
    /// The premise is that friendship is an instrument. Every condition this
    /// needs — deep trust, heavy dependence, real access — is something you can
    /// only build by being a genuinely good partner for years. That is what
    /// makes it a betrayal rather than an attack, and why the reputational bill
    /// falls due among your *friends* rather than your enemies.
    /// </summary>
    [Serializable]
    public class AccessionCampaign
    {
        public string sponsorId;
        public string targetId;
        public AccessionRoute route;

        /// <summary>0..100. Reaching 100 completes the accession.</summary>
        public float progress;

        public int monthsRunning;

        /// <summary>
        /// True once the target has worked out what is being done to them. An
        /// exposed campaign can continue, but everyone can see it — which is
        /// usually fatal to the friendship it depends on.
        /// </summary>
        public bool exposed;

        /// <summary>Set when the effort has been abandoned or has collapsed.</summary>
        public bool ended;
    }
}

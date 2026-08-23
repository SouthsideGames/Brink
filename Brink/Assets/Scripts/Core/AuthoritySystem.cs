using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Constitutional authority (GDD §3, §7.3).
    ///
    /// A non-negotiable rule of the design: the operator "may directly direct a
    /// pillar when political and constitutional authority permits, and otherwise
    /// must influence or recommend." This had no implementation at all — Direct
    /// Control was available over any pillar, in any government type, at any
    /// time, for a flat command-point price, and government structure changed
    /// only how much Political Capital things cost.
    ///
    /// That is the hinge of the whole premise. An operator inside an institution
    /// he does not own is a different game from an operator with uniform access
    /// wearing a bureaucratic costume. Here, what you may personally command
    /// depends on the constitution you serve under.
    /// </summary>
    public static class AuthoritySystem
    {
        /// <summary>Political Capital an approved intervention costs to obtain.</summary>
        public const float ApprovalCost = 3f;

        /// <summary>What the operator may do with a given pillar under this government.</summary>
        public enum AuthorityLevel
        {
            /// <summary>The executive commands this directly. No approval needed.</summary>
            Direct,

            /// <summary>Command is possible, but must be obtained: Political Capital, and the institution may refuse.</summary>
            RequiresApproval,

            /// <summary>Outside the operator's writ. Influence and recommend only.</summary>
            AdvisoryOnly
        }

        /// <summary>
        /// Where authority over each pillar sits under each constitution.
        ///
        /// The shape of each government is what differs, not the size of a
        /// modifier: a presidential executive owns force and foreign affairs but
        /// must go to the legislature for the economy; a parliamentary one holds
        /// almost nothing without the cabinet's assent; a dominant-party state
        /// commands the apparatus but not the ministries of production; a
        /// monarch's writ runs to the army and the court but not the treasury.
        /// </summary>
        public static AuthorityLevel AuthorityOver(GameState state, Pillar pillar)
        {
            var government = state.PlayerCountry.government;

            // Emergency powers suspend the ordinary distribution of authority —
            // which is exactly what makes them worth their political price.
            if (government.emergencyPowers) return AuthorityLevel.Direct;

            // A change the operator bought outright and keeps. Unlike emergency
            // powers this is permanent, costs no legitimacy every month, and had
            // to be paid for once at a price the Political Capital economy had
            // nothing else at (GovernmentSystem.ConsolidateAuthority).
            if ((government.authorityUpgradeMask & (1 << (int)pillar)) != 0)
                return AuthorityLevel.Direct;

            switch (government.type)
            {
                case GovernmentType.PresidentialRepublic:
                    switch (pillar)
                    {
                        case Pillar.Military:
                        case Pillar.Diplomacy:
                        case Pillar.Intelligence:
                            return AuthorityLevel.Direct;
                        case Pillar.Economy:
                            return AuthorityLevel.RequiresApproval;
                        default:
                            return AuthorityLevel.RequiresApproval;
                    }

                case GovernmentType.ParliamentaryRepublic:
                    switch (pillar)
                    {
                        case Pillar.Diplomacy:
                            return AuthorityLevel.Direct;
                        case Pillar.Military:
                        case Pillar.Intelligence:
                            return AuthorityLevel.RequiresApproval;
                        default:
                            return AuthorityLevel.AdvisoryOnly;
                    }

                case GovernmentType.DominantPartyState:
                    switch (pillar)
                    {
                        case Pillar.Economy:
                            return AuthorityLevel.RequiresApproval;
                        default:
                            return AuthorityLevel.Direct;
                    }

                case GovernmentType.CentralizedRepublic:
                    switch (pillar)
                    {
                        case Pillar.Government:
                            return AuthorityLevel.RequiresApproval;
                        default:
                            return AuthorityLevel.Direct;
                    }

                default: // Monarchy
                    switch (pillar)
                    {
                        case Pillar.Military:
                        case Pillar.Diplomacy:
                        case Pillar.Government:
                            return AuthorityLevel.Direct;
                        case Pillar.Intelligence:
                            return AuthorityLevel.RequiresApproval;
                        default:
                            return AuthorityLevel.AdvisoryOnly;
                    }
            }
        }

        /// <summary>
        /// Whether the operator can take personal command of this pillar right
        /// now, and what standing in the way if not.
        /// </summary>
        public static bool CanDirectlyCommand(GameState state, Pillar pillar, out string reason)
        {
            switch (AuthorityOver(state, pillar))
            {
                case AuthorityLevel.Direct:
                    reason = "";
                    return true;

                case AuthorityLevel.RequiresApproval:
                    if (state.politicalCapital < ApprovalCost)
                    {
                        reason = $"Requires {ApprovalCost:F0} Political Capital to obtain authority.";
                        return false;
                    }
                    reason = "";
                    return true;

                default:
                    reason = $"{pillar} is outside the operator's writ under a " +
                             $"{state.PlayerCountry.government.TypeText}. Influence or recommend.";
                    return false;
            }
        }

        /// <summary>
        /// Whether the operator may act personally in this pillar *right now* —
        /// either because the constitution gives it to them outright, or because
        /// they have already obtained the grant under this administration.
        /// </summary>
        public static bool HoldsAuthority(GameState state, Pillar pillar)
        {
            switch (AuthorityOver(state, pillar))
            {
                case AuthorityLevel.Direct: return true;
                case AuthorityLevel.AdvisoryOnly: return false;
                default: return (state.authorizedPillarMask & (1 << (int)pillar)) != 0;
            }
        }

        /// <summary>
        /// Gate a player action on constitutional authority (GDD §3).
        ///
        /// Called by every verb in `GameController` that acts on a pillar. The
        /// grant is obtained **once per pillar per administration**, not paid for
        /// each action: a legislature that has ceded the economic brief does not
        /// re-litigate every tariff, and charging per action would make an
        /// entire playstyle unaffordable rather than constitutionally awkward.
        ///
        /// Where authority is refused the operator is not stuck: Directed mode
        /// still works, which is precisely the GDD's "otherwise must influence or
        /// recommend".
        /// </summary>
        public static bool EnsureAuthority(GameState state, Pillar pillar)
        {
            if (HoldsAuthority(state, pillar)) return true;

            if (AuthorityOver(state, pillar) == AuthorityLevel.AdvisoryOnly)
            {
                GameLog.Warn("AUTHORITY",
                    $"{pillar} is not ours to command under a " +
                    $"{state.PlayerCountry.government.TypeText}. Direct the official instead.");
                return false;
            }

            if (!ObtainAuthority(state, pillar)) return false;

            state.authorizedPillarMask |= 1 << (int)pillar;
            state.AddNotification(NotificationClass.Advisory, "AUTHORITY GRANTED",
                $"We now hold direct authority over {pillar.ToString().ToUpperInvariant()} " +
                "for the life of this administration.", state.playerCountryId);
            return true;
        }

        /// <summary>
        /// A new government re-decides what the operator may command. The grant
        /// was personal to the administration that made it.
        /// </summary>
        public static void ClearGrantedAuthority(GameState state)
        {
            if (state.authorizedPillarMask == 0) return;
            state.authorizedPillarMask = 0;
            GameLog.Info("AUTHORITY", "Delegated authority lapses with the administration.");
        }

        /// <summary>
        /// Obtain authority to command a pillar. Direct authority is free;
        /// delegated authority must be bought and can be refused by an
        /// institution that does not support us.
        /// </summary>
        public static bool ObtainAuthority(GameState state, Pillar pillar)
        {
            var level = AuthorityOver(state, pillar);
            if (level == AuthorityLevel.Direct) return true;

            if (level == AuthorityLevel.AdvisoryOnly)
            {
                GameLog.Warn("AUTHORITY",
                    $"{pillar} is not the operator's to command under a " +
                    $"{state.PlayerCountry.government.TypeText}.");
                return false;
            }

            if (!GovernmentSystem.SpendPoliticalCapital(state, ApprovalCost,
                    $"Authority over {pillar}"))
                return false;

            // A legislature that does not back us can withhold its consent, and
            // the capital is spent asking either way.
            var government = state.PlayerCountry.government;
            if (government.legislativeSupport < 30f)
            {
                GameLog.Warn("AUTHORITY",
                    $"Authority over {pillar} was refused. The legislature does not support us.");
                state.AddNotification(NotificationClass.Priority, "AUTHORITY REFUSED",
                    $"Our request for direct authority over {pillar.ToString().ToUpperInvariant()} " +
                    "was declined. We may advise, but we may not command.", state.playerCountryId);
                return false;
            }

            // Reaching over an institution's head costs standing with it.
            government.legislativeSupport = Clamp(government.legislativeSupport - 2.5f);
            return true;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

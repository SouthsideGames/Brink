using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// What a crisis decision actually does to the world (GDD §23).
    ///
    /// Until this existed, `CrisisOption` resolved to four scalars on the
    /// player's own country — treasury, stability, approval, unity — and nothing
    /// else. A crisis could not touch a relationship, a market, a foreign state
    /// or a war. The most dramatic moments in the simulation were stat pokes
    /// that left nothing behind, which contradicts §23's whole premise that
    /// events drive the world rather than decorate it.
    ///
    /// Effects are named with strings rather than delegates because a live
    /// crisis is persisted (see <see cref="CrisisOption.effectId"/>). The switch
    /// is exhaustive by test: `CrisisEffectTests` walks every option in the
    /// authored catalog and fails the build on an id this file does not handle,
    /// so a typo cannot become a silently inert choice — which is exactly the
    /// failure this system already had, one layer down.
    ///
    /// Every effect is written to be safe against a world that has moved on
    /// since the crisis fired: a target that no longer exists, a war that has
    /// already started, a trade link that has been severed. A crisis resolving
    /// into a contradiction must do nothing, never throw.
    /// </summary>
    public static class CrisisEffects
    {
        // ---- diplomatic ----
        public const string Relations = "RELATIONS";
        public const string Trust = "TRUST";
        public const string Threat = "THREAT";

        // ---- military ----
        public const string OpenConfrontation = "OPEN_CONFRONTATION";
        public const string SufferConfrontation = "SUFFER_CONFRONTATION";
        public const string Readiness = "READINESS";
        public const string WarSupport = "WAR_SUPPORT";

        // ---- economic ----
        public const string ImposeSanction = "IMPOSE_SANCTION";
        public const string SufferSanction = "SUFFER_SANCTION";
        public const string TradeShock = "TRADE_SHOCK";
        public const string MarketShock = "MARKET_SHOCK";

        // ---- intelligence ----
        public const string ExposeNetwork = "EXPOSE_NETWORK";

        /// <summary>
        /// A foreign official offers themselves to us (spec 03 §11). Deep access
        /// into one state, bought with that state's opinion of us — a defector
        /// is a windfall, not a free one.
        /// </summary>
        public const string AcceptDefector = "ACCEPT_DEFECTOR";

        // ---- government ----
        public const string ForeignUnrest = "FOREIGN_UNREST";
        public const string Conspiracy = "CONSPIRACY";

        /// <summary>Every id this file handles. The test walks the catalog against it.</summary>
        public static readonly string[] All =
        {
            Relations, Trust, Threat,
            OpenConfrontation, SufferConfrontation, Readiness, WarSupport,
            ImposeSanction, SufferSanction, TradeShock, MarketShock,
            ExposeNetwork, AcceptDefector,
            ForeignUnrest, Conspiracy
        };

        public static bool IsKnown(string effectId)
        {
            if (string.IsNullOrEmpty(effectId)) return true; // scalars only
            foreach (var known in All)
                if (known == effectId) return true;
            return false;
        }

        /// <summary>
        /// Apply one named effect. Returns a short line describing what happened
        /// in the world, or empty when nothing did — the caller appends it to the
        /// operator's report so a consequence beyond our borders is *visible*
        /// rather than something the player discovers three months later.
        /// </summary>
        public static string Apply(GameState state, string effectId, string targetId, float magnitude)
        {
            if (string.IsNullOrEmpty(effectId)) return "";

            var player = state.PlayerCountry;
            if (player == null) return "";

            var target = string.IsNullOrEmpty(targetId) ? null : state.FindCountry(targetId);

            switch (effectId)
            {
                // ---------------- diplomatic ----------------

                case Relations:
                {
                    if (target == null) return "";
                    var relationship = state.FindRelationship(player.id, target.id);
                    if (relationship == null) return "";
                    relationship.relations = Clamp(relationship.relations + magnitude);
                    return magnitude >= 0f
                        ? $"Relations with {target.displayName} have improved."
                        : $"Relations with {target.displayName} have deteriorated.";
                }

                case Trust:
                {
                    if (target == null) return "";
                    var relationship = state.FindRelationship(player.id, target.id);
                    if (relationship == null) return "";
                    relationship.trust = Clamp(relationship.trust + magnitude);
                    return magnitude >= 0f
                        ? $"{target.displayName} is more inclined to take us at our word."
                        : $"{target.displayName} trusts us less than it did.";
                }

                case Threat:
                {
                    if (target == null) return "";
                    var relationship = state.FindRelationship(player.id, target.id);
                    if (relationship == null) return "";
                    bool playerIsA = relationship.countryA == player.id;
                    if (playerIsA)
                        relationship.threatPerceptionOfA = Clamp(relationship.threatPerceptionOfA + magnitude);
                    else
                        relationship.threatPerceptionOfB = Clamp(relationship.threatPerceptionOfB + magnitude);
                    return magnitude >= 0f
                        ? $"{target.displayName} now regards us as more dangerous."
                        : $"{target.displayName} is less alarmed by us than it was.";
                }

                // ---------------- military ----------------

                case OpenConfrontation:
                case SufferConfrontation:
                {
                    if (target == null) return "";

                    // Either side already committed elsewhere cannot open this.
                    // A crisis resolving into an impossible war does nothing.
                    if (state.ActiveConfrontationFor(player.id) != null) return "";
                    if (state.ActiveConfrontationFor(target.id) != null) return "";

                    bool weInitiate = effectId == OpenConfrontation;
                    string initiator = weInitiate ? player.id : target.id;
                    string defender = weInitiate ? target.id : player.id;

                    var confrontation = ConfrontationSystem.BeginBy(state, initiator, defender,
                        ConfrontationObjective.Deterrence, null, PrimaryStrategy.Military);
                    if (confrontation == null) return "";

                    state.AddNotification(NotificationClass.Flash,
                        weInitiate ? "CONFRONTATION OPENED" : "CONFRONTATION FORCED UPON US",
                        weInitiate
                            ? $"We have moved against {target.displayName}. The situation is now a " +
                              "standing confrontation."
                            : $"{target.displayName} has moved against us. We are in a standing " +
                              "confrontation whether we wanted one or not.",
                        player.id, desk: ReportingDesk.Command);

                    return weInitiate
                        ? $"A confrontation with {target.displayName} is now open."
                        : $"{target.displayName} has opened a confrontation against us.";
                }

                case Readiness:
                {
                    var mil = player.military;
                    mil.ground.readiness = Clamp(mil.ground.readiness + magnitude);
                    mil.air.readiness = Clamp(mil.air.readiness + magnitude);
                    mil.naval.readiness = Clamp(mil.naval.readiness + magnitude);
                    return magnitude >= 0f
                        ? "The force has been brought to a higher state of readiness."
                        : "Readiness across the force has slipped.";
                }

                case WarSupport:
                    player.warSupport = Clamp(player.warSupport + magnitude);
                    return magnitude >= 0f
                        ? "Public willingness to fight has hardened."
                        : "Public willingness to fight has fallen away.";

                // ---------------- economic ----------------

                case ImposeSanction:
                {
                    if (target == null) return "";
                    var severity = SeverityFor(magnitude);
                    if (!EconomySystem.ImposeSanctionsBy(state, player.id, target.id, severity, "CRISIS")) return "";
                    return $"Sanctions imposed on {target.displayName}.";
                }

                case SufferSanction:
                {
                    if (target == null) return "";
                    var severity = SeverityFor(magnitude);
                    if (!EconomySystem.ImposeSanctionsBy(state, target.id, player.id, severity, "CRISIS")) return "";
                    return $"{target.displayName} has imposed sanctions on us.";
                }

                case TradeShock:
                {
                    // Named target hits that relationship; no target hits every
                    // link we have, which is what a general disruption means.
                    float moved = 0f;
                    foreach (var link in state.trade)
                    {
                        if (!link.Involves(player.id)) continue;
                        if (target != null && !link.Involves(target.id)) continue;
                        float before = link.volume;
                        link.volume = Clamp(link.volume + magnitude);
                        moved += Math.Abs(link.volume - before);
                    }
                    if (moved < 0.01f) return "";
                    return magnitude >= 0f
                        ? "Trade volumes have recovered."
                        : "Trade volumes have contracted.";
                }

                case MarketShock:
                {
                    var economy = player.economy;
                    economy.confidence = Clamp(economy.confidence + magnitude);
                    // Named rather than left to the month's OTHER line: a crisis
                    // is the single most explainable thing that happens to a
                    // market, and an operator who just answered one should see
                    // it by name (spec 26 §3).
                    Causal.Apply(state, player.id, CausalMetric.MarketIndex,
                        CausalReason.MarketConditions, ref economy.marketIndex,
                        Math.Max(1f, economy.marketIndex * (1f + magnitude / 100f)),
                        CausalCategory.Economic, CausalKind.Direct);
                    return magnitude >= 0f
                        ? "Markets have taken the news well."
                        : "Markets have taken the news badly.";
                }

                // ---------------- intelligence ----------------

                case AcceptDefector:
                {
                    if (target == null) return "";

                    // **A pull channel, not a push one.** Every other route into
                    // a foreign service is something we do to them; this is
                    // somebody walking in. It is a windfall the operator can
                    // refuse — and refusing costs nothing, which is what makes
                    // taking them a decision rather than a formality.
                    var network = state.FindNetwork(player.id, target.id);
                    if (network == null)
                    {
                        network = new IntelNetwork
                        {
                            ownerId = player.id,
                            targetId = target.id,
                            focus = IntelDomain.Political
                        };
                        state.networks.Add(network);
                    }
                    network.penetration = Clamp(network.penetration + magnitude);
                    network.compromised = false;

                    // They know who left and where they went.
                    var relationship = state.FindRelationship(player.id, target.id);
                    if (relationship != null)
                    {
                        relationship.relations = Clamp(relationship.relations - 12f);
                        relationship.trust = Clamp(relationship.trust - 15f);
                        relationship.AddMemory(state.date, "Took in one of ours.", -2f);
                    }

                    // And their service tightens up, which is the cost that
                    // outlives the windfall.
                    target.counterIntel.institutionalHardening =
                        Math.Min(20f, target.counterIntel.institutionalHardening + 3f);

                    return $"A defector from {target.displayName} is in our hands.";
                }

                case ExposeNetwork:
                {
                    foreach (var network in state.networks)
                    {
                        if (network.ownerId != player.id) continue;
                        if (target != null && network.targetId != target.id) continue;
                        if (network.compromised) continue;

                        network.compromised = true;
                        network.penetration = Clamp(network.penetration * 0.35f);

                        // Public, and on the record for everyone — this is what
                        // teaches the world we run covert operations (spec 06 §7b).
                        state.AddChronicle(ChronicleCategory.Intelligence, player.id,
                            $"Network in {network.targetId} compromised.", Publicity.Public);
                        return "A collection network has been rolled up and named publicly.";
                    }
                    return "";
                }

                // ---------------- government ----------------

                case ForeignUnrest:
                {
                    if (target == null) return "";
                    target.stability = Clamp(target.stability + magnitude);
                    if (magnitude < 0f)
                        target.government.conspiracyLevel =
                            Clamp(target.government.conspiracyLevel - magnitude * 0.4f);
                    return magnitude >= 0f
                        ? $"{target.displayName} has steadied."
                        : $"{target.displayName} is less stable than it was.";
                }

                case Conspiracy:
                    player.government.conspiracyLevel =
                        Clamp(player.government.conspiracyLevel + magnitude);
                    return magnitude >= 0f
                        ? "Elements within the state are organising against this government."
                        : "Plotting against this government has been set back.";

                default:
                    // Unreachable while the test holds, and deliberately silent
                    // rather than throwing: a save that somehow carries an
                    // unknown effect should still be playable.
                    GameLog.Warn("CRISIS", $"Unknown crisis effect '{effectId}' ignored.");
                    return "";
            }
        }

        static SanctionSeverity SeverityFor(float magnitude)
        {
            if (magnitude >= 4f) return SanctionSeverity.Existential;
            if (magnitude >= 3f) return SanctionSeverity.Severe;
            if (magnitude >= 2f) return SanctionSeverity.Coercive;
            if (magnitude >= 1f) return SanctionSeverity.Pressure;
            return SanctionSeverity.Routine;
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

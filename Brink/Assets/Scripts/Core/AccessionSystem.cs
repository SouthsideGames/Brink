using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Absorbing a country that trusts you (GDD §15.1, §22).
    ///
    /// A third route to another nation's territory, beside conquest and a
    /// sponsored coup, and the only one that costs you nothing militarily. You
    /// spend years being a good partner — trade they come to rely on, treaties
    /// you honour, trust you never break — and then you use every bit of it at
    /// once.
    ///
    /// The design rests on four things:
    ///
    /// 1. **It requires real friendship.** Every precondition — deep trust,
    ///    heavy dependence, political access — is something only a genuine
    ///    partner accumulates. You cannot fake your way to it, and you cannot
    ///    start it against someone who dislikes you.
    /// 2. **It requires their weakness.** A confident, cohesive country does not
    ///    dissolve itself. The elite route needs a fractured establishment; the
    ///    popular route needs a population that has stopped believing in the
    ///    state. Neither can be manufactured from outside quickly, exactly as
    ///    with `RegimeSystem`'s rule that foreign action accelerates rather than
    ///    creates.
    /// 3. **It is visible if they are watching.** A campaign can be detected by
    ///    the target's counterintelligence and by any third party with political
    ///    collection. Nothing here is an unstoppable ambush.
    /// 4. **The bill falls due among your friends.** Absorbing a partner tells
    ///    every other partner what friendship with you is worth. The
    ///    reputational cost is deliberately weighted toward states that *liked*
    ///    you — the ones who now have to reconsider. That is the answer to
    ///    "what stops this being strictly better than war".
    /// </summary>
    public static class AccessionSystem
    {
        /// <summary>CP to open a campaign.</summary>
        public const int OpenCost = 3;

        /// <summary>Political Capital drawn each month a campaign runs.</summary>
        public const float MonthlyPoliticalCost = 0.8f;

        /// <summary>Relations below which no country will hear the argument at all.</summary>
        public const float RequiredRelations = 70f;

        /// <summary>Trust below which the approach is simply refused.</summary>
        public const float RequiredTrust = 60f;

        /// <summary>How dependent on us they must already be.</summary>
        public const float RequiredDependence = 35f;

        // ---------- can we even begin ----------

        public static bool CanBegin(GameState state, string sponsorId, string targetId,
            AccessionRoute route, out string reason)
        {
            var sponsor = state.FindCountry(sponsorId);
            var target = state.FindCountry(targetId);

            if (sponsor == null || target == null) { reason = "No such state."; return false; }
            if (sponsorId == targetId) { reason = "We cannot accede to ourselves."; return false; }
            if (FindCampaign(state, sponsorId, targetId) != null)
            {
                reason = "An effort is already under way there.";
                return false;
            }

            var relationship = state.FindRelationship(sponsorId, targetId);
            if (relationship == null) { reason = "No relationship exists."; return false; }

            if (DiplomacySystem.Permitted(state, relationship, relationship.relations) < RequiredRelations)
            {
                reason = $"They are not close enough to us. Relations {relationship.relations:F0}, " +
                         $"need {RequiredRelations:F0}.";
                return false;
            }
            if (relationship.trust < RequiredTrust)
            {
                reason = $"They do not trust us enough. Trust {relationship.trust:F0}, " +
                         $"need {RequiredTrust:F0}.";
                return false;
            }
            if (relationship.DependenceOf(targetId) < RequiredDependence)
            {
                reason = "They do not rely on us enough for the argument to carry. " +
                         "Build trade and commitments they cannot easily replace.";
                return false;
            }

            // The target must be a country with something wrong with it.
            if (route == AccessionRoute.Elite && target.government.eliteCohesion > 55f)
            {
                reason = "Their establishment is united and has no reason to sell.";
                return false;
            }
            if (route == AccessionRoute.Popular && target.nationalUnity > 50f)
            {
                reason = "Their people still believe in their own state.";
                return false;
            }

            reason = "";
            return true;
        }

        // ---------- running one ----------

        public static bool Begin(GameState state, TurnManager turns, string targetId, AccessionRoute route)
        {
            if (!CanBegin(state, state.playerCountryId, targetId, route, out string blocked))
            {
                GameLog.Warn("ACCESSION", blocked);
                return false;
            }
            if (!turns.SpendCommandPoints(OpenCost, $"Open accession effort in {targetId}")) return false;

            ProgressionSystem.RecordInitiative(state);
            ProgressionSystem.AwardXP(state, 18, "Accession effort opened");
            return BeginBy(state, state.playerCountryId, targetId, route);
        }

        /// <summary>Actor-generic. Any government can try this on a friend.</summary>
        public static bool BeginBy(GameState state, string sponsorId, string targetId, AccessionRoute route)
        {
            if (!CanBegin(state, sponsorId, targetId, route, out _)) return false;

            state.accessions.Add(new AccessionCampaign
            {
                sponsorId = sponsorId,
                targetId = targetId,
                route = route
            });

            var target = state.FindCountry(targetId);
            if (sponsorId == state.playerCountryId)
                state.AddNotification(NotificationClass.Advisory, "ACCESSION EFFORT OPENED",
                    $"Quiet work has begun toward bringing {target.displayName} into our union " +
                    $"by {Phrase.Of(route).ToLowerInvariant()} consent. It will take years, it " +
                    "depends on their continued trust, and it will be noticed if we are careless.",
                    targetId, desk: ReportingDesk.Intelligence);

            GameLog.Info("ACCESSION", $"{sponsorId} opened a {route} accession effort in {targetId}.");
            return true;
        }

        /// <summary>Abandon an effort. The work is lost; the friendship survives.</summary>
        public static bool Abandon(GameState state, string sponsorId, string targetId)
        {
            var campaign = FindCampaign(state, sponsorId, targetId);
            if (campaign == null) return false;
            campaign.ended = true;
            state.accessions.Remove(campaign);
            return true;
        }

        public static AccessionCampaign FindCampaign(GameState state, string sponsorId, string targetId)
        {
            foreach (var campaign in state.accessions)
                if (campaign.sponsorId == sponsorId && campaign.targetId == targetId && !campaign.ended)
                    return campaign;
            return null;
        }

        // ---------- the monthly work ----------

        public static void MonthlyUpdate(GameState state)
        {
            int monthIndex = state.date.MonthsSince(state.startDate);

            for (int i = state.accessions.Count - 1; i >= 0; i--)
            {
                var campaign = state.accessions[i];
                var sponsor = state.FindCountry(campaign.sponsorId);
                var target = state.FindCountry(campaign.targetId);
                var relationship = state.FindRelationship(campaign.sponsorId, campaign.targetId);

                if (sponsor == null || target == null || relationship == null)
                {
                    state.accessions.RemoveAt(i);
                    continue;
                }

                campaign.monthsRunning++;

                // Sustained political work costs authority every month it runs.
                if (!GovernmentSystem.SpendPoliticalCapitalBy(
                        state, campaign.sponsorId, MonthlyPoliticalCost, "Accession effort"))
                {
                    Collapse(state, campaign, "could not be sustained");
                    state.accessions.RemoveAt(i);
                    continue;
                }

                // The whole thing rests on being their friend. Stop being one and
                // the argument stops working — this is what makes it a *betrayal*
                // rather than a slow-motion invasion.
                if (DiplomacySystem.Permitted(state, relationship, relationship.relations) < RequiredRelations - 15f
                    || relationship.trust < RequiredTrust - 15f)
                {
                    Collapse(state, campaign, "lost the goodwill it depended on");
                    state.accessions.RemoveAt(i);
                    continue;
                }

                var rng = new Random(unchecked(
                    state.rngSeed * 5099 + monthIndex * 331
                    + Hash.Of(campaign.sponsorId) * 7 + Hash.Of(campaign.targetId)));

                campaign.progress = Clamp(campaign.progress + MonthlyProgress(state, campaign));
                CheckExposure(state, campaign, rng);

                if (campaign.progress >= 100f)
                {
                    Complete(state, campaign);
                    state.accessions.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// How much ground a campaign makes in a month.
        ///
        /// Driven by their weakness and our hold on them, not by our own
        /// strength — this is an argument being won inside their country, and
        /// the only thing our size buys is the credibility of the offer.
        /// </summary>
        public static float MonthlyProgress(GameState state, AccessionCampaign campaign)
        {
            var target = state.FindCountry(campaign.targetId);
            var relationship = state.FindRelationship(campaign.sponsorId, campaign.targetId);
            if (target == null || relationship == null) return 0f;

            float dependence = relationship.DependenceOf(campaign.targetId);
            float access = PoliticalAccess(state, campaign.sponsorId, campaign.targetId);

            float weakness = campaign.route == AccessionRoute.Elite
                ? Math.Max(0f, 55f - target.government.eliteCohesion)
                : Math.Max(0f, 50f - target.nationalUnity);

            // Their counterintelligence is working against us throughout.
            float resistance = target.counterIntel.counterIntelligence * 0.012f;

            float rate = 0.25f
                         + weakness * 0.022f
                         + dependence * 0.012f
                         + access * 0.008f
                         + relationship.trust * 0.004f
                         - resistance;

            // Winning a population over is slower than buying a cabinet, and
            // that difference is the whole trade between the two routes.
            if (campaign.route == AccessionRoute.Popular) rate *= 0.6f;

            // Being caught does not end it, but it makes every further month
            // an argument against a country that now knows what we are doing.
            if (campaign.exposed) rate *= 0.35f;

            return Math.Max(0f, rate);
        }

        /// <summary>Depth of our political collection in the target.</summary>
        static float PoliticalAccess(GameState state, string sponsorId, string targetId)
        {
            float best = 0f;
            foreach (var network in state.networks)
            {
                if (network.ownerId != sponsorId || network.targetId != targetId) continue;
                if (network.compromised || network.focus != IntelDomain.Political) continue;
                if (network.penetration > best) best = network.penetration;
            }
            return best;
        }

        static void CheckExposure(GameState state, AccessionCampaign campaign, Random rng)
        {
            if (campaign.exposed) return;

            var target = state.FindCountry(campaign.targetId);
            float chance = 0.02f + target.counterIntel.counterIntelligence * 0.0018f
                                 + campaign.progress * 0.0006f;

            if (rng.NextDouble() >= chance) return;

            campaign.exposed = true;

            // Being found out is not a private embarrassment. The relationship it
            // was exploiting is the first casualty.
            var relationship = state.FindRelationship(campaign.sponsorId, campaign.targetId);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations - 25f);
                relationship.trust = Clamp(relationship.trust - 35f);
                relationship.SetThreatPerceivedBy(campaign.targetId,
                    Clamp(relationship.ThreatPerceivedBy(campaign.targetId) + 25f));
                relationship.AddMemory(state.date, "Attempted to absorb us from within", -4f);
            }

            var sponsor = state.FindCountry(campaign.sponsorId);
            state.AddNotification(
                campaign.sponsorId == state.playerCountryId
                    ? NotificationClass.Priority : NotificationClass.Flash,
                "ACCESSION EFFORT EXPOSED",
                campaign.sponsorId == state.playerCountryId
                    ? $"{target.displayName} has discovered what we have been doing inside their " +
                      "government. The friendship the effort relied on is badly damaged."
                    : $"{sponsor.displayName} has been working to absorb {target.displayName} " +
                      "from within. The approach was made under cover of friendship.",
                campaign.targetId, desk: ReportingDesk.Intelligence);

            state.AddChronicle(ChronicleCategory.Diplomatic, campaign.sponsorId,
                $"{sponsor.displayName}'s effort to absorb {target.displayName} was exposed.",
                Publicity.Public);
        }

        static void Collapse(GameState state, AccessionCampaign campaign, string why)
        {
            campaign.ended = true;
            if (campaign.sponsorId != state.playerCountryId) return;

            var target = state.FindCountry(campaign.targetId);
            state.AddNotification(NotificationClass.Advisory, "ACCESSION EFFORT ENDED",
                $"The effort in {target?.displayName} {why}. The work is lost.",
                campaign.targetId, desk: ReportingDesk.Intelligence);
        }

        // ---------- it succeeds ----------

        static void Complete(GameState state, AccessionCampaign campaign)
        {
            var sponsor = state.FindCountry(campaign.sponsorId);
            var target = state.FindCountry(campaign.targetId);

            // Territory and resources pass exactly as they do on conquest —
            // absorbing a country is absorbing a country, however it was agreed.
            ConquestSystem.Absorb(state, sponsor, target);

            // A union nobody was consulted about is held together by less.
            if (campaign.route == AccessionRoute.Elite)
            {
                sponsor.nationalUnity = Clamp(sponsor.nationalUnity - 10f);
                sponsor.stability = Clamp(sponsor.stability - 8f);
            }

            BetrayalCost(state, sponsor, target, campaign.route);

            state.AddNotification(
                sponsor.isPlayer ? NotificationClass.Priority : NotificationClass.Flash,
                "ACCESSION COMPLETE",
                sponsor.isPlayer
                    ? $"{target.displayName} has acceded to our union. Their territory and " +
                      "resources are ours, and not a shot was fired. Every state that considered " +
                      "itself our friend has watched how this was done."
                    : $"{target.displayName} has been absorbed into {sponsor.displayName} " +
                      "by political means.",
                campaign.targetId, desk: ReportingDesk.Diplomacy);

            state.AddChronicle(ChronicleCategory.Diplomatic, campaign.sponsorId,
                $"{target.displayName} acceded to {sponsor.displayName} " +
                $"by {Phrase.Of(campaign.route).ToLowerInvariant()} consent.", Publicity.Public);
            GameLog.Warn("ACCESSION", $"{campaign.sponsorId} absorbed {campaign.targetId}.");
        }

        /// <summary>
        /// What it costs among everyone who trusted us.
        ///
        /// The reputational hit is **scaled by how friendly each state was** —
        /// this is the mechanical heart of the whole feature. A rival who already
        /// assumed the worst learns little. A partner who thought they were safe
        /// has just watched what happens to partners, and reacts accordingly.
        ///
        /// So the price of the instrument is paid in the exact currency that
        /// made it possible: the more friends you had, the more it costs, and
        /// using it twice is close to impossible because nobody will be close
        /// enough to you again.
        /// </summary>
        static void BetrayalCost(GameState state, CountryState sponsor, CountryState target,
            AccessionRoute route)
        {
            sponsor.pillars.diplomacy = Clamp(sponsor.pillars.diplomacy - 22f);

            foreach (var relationship in state.relationships)
            {
                if (!relationship.Involves(sponsor.id)) continue;
                string other = relationship.PartnerOf(sponsor.id);
                if (other == target.id) continue;

                // Proportional to how much they liked us. Being close to a state
                // that does this is now the dangerous position to be in.
                float closeness = (relationship.relations + relationship.trust) * 0.5f;
                float shock = 8f + closeness * 0.45f;

                relationship.relations = Clamp(relationship.relations - shock);
                relationship.trust = Clamp(relationship.trust - shock * 1.2f);
                relationship.SetThreatPerceivedBy(other,
                    Clamp(relationship.ThreatPerceivedBy(other) + shock * 0.8f));
                relationship.AddMemory(state.date,
                    $"Absorbed {target.displayName} from within", -4.5f);

                // A defence pact with a state that eats its friends is not a
                // reassuring thing to hold.
                var treaty = state.FindTreaty(sponsor.id, other);
                if (treaty != null && !treaty.broken && closeness < 45f)
                {
                    treaty.broken = true;
                    treaty.brokenBy = other;
                    state.AddChronicle(ChronicleCategory.Diplomatic, other,
                        $"Treaty with {sponsor.displayName} repudiated after the absorption of " +
                        $"{target.displayName}.", Publicity.Public);
                }
            }
        }

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

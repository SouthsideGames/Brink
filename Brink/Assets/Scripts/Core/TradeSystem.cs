using System;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>How likely a partner is to sign, at the precision we can judge it.</summary>
    public enum TradeOutlook
    {
        NoTerms,
        Unlikely,
        Uncertain,
        Likely
    }

    /// <summary>
    /// Negotiated trade (GDD §20).
    ///
    /// Trade links existed, fed growth, and could **only be created at world
    /// creation** — there was no verb, for the player or anyone else. That left
    /// two promises broken: the action index advertised "open a trade link", and
    /// the Cabinet advised "build trade links with an energy exporter" to fix a
    /// dependency. Neither pointed at anything.
    ///
    /// Worse, trade supplied no resources at all, so even an authored link did
    /// nothing about an energy shortfall. Several countries are *defined* by
    /// that shortfall — Germany at 30, China and India at 38 — and the obvious
    /// answer to it did not work. Only research and conquest did.
    ///
    /// So a resource-focused agreement now raises the importer's ceiling for
    /// that commodity. That is the point of the system: **the third answer to a
    /// vulnerability, alongside inventing your way out and taking what you
    /// need** — and the only one that leaves you dependent on somebody.
    /// </summary>
    public static class TradeSystem
    {
        /// <summary>CP to put an agreement to another government.</summary>
        public const int ProposalCost = 1;

        /// <summary>CP to walk away from one.</summary>
        public const int WithdrawalCost = 1;

        /// <summary>Most of a partner's endowment a link can ever supply.</summary>
        public const float MaxSupplyShare = 0.45f;

        // ---------- what a deal is worth to each side ----------

        /// <summary>
        /// What we are asking them to give up, 0..100. Volume they must commit,
        /// tariff we refuse to pay, and the leverage a resource deal hands us.
        /// </summary>
        public static float CostToPartner(GameState state, string proposerId, TradeDeal deal)
        {
            var partner = state.FindCountry(deal.partnerId);
            if (partner == null) return 100f;

            float cost = deal.volume * 0.35f;

            // A low tariff is us taking the better end of it.
            cost += Math.Max(0f, 30f - deal.tariff) * 0.5f;

            // Handing over a commodity they are themselves short of is a real
            // sacrifice; selling a surplus is barely one.
            if (deal.focus == TradeFocus.Energy)
                cost += Math.Max(0f, 60f - partner.resources.energy) * 0.4f;
            if (deal.focus == TradeFocus.Materials)
                cost += Math.Max(0f, 60f - partner.resources.strategicMaterials) * 0.4f;
            if (deal.focus == TradeFocus.Food)
                cost += Math.Max(0f, 60f - partner.resources.foodSecurity) * 0.4f;

            if (deal.preferentialTerms) cost -= 22f;

            return cost;
        }

        /// <summary>
        /// How much they want an arrangement with us at all. Relations, standing,
        /// and the plain fact that trade is usually good for both sides.
        /// </summary>
        public static float PartnerWillingness(GameState state, string proposerId, string partnerId)
        {
            var relationship = state.FindRelationship(proposerId, partnerId);
            var proposer = state.FindCountry(proposerId);
            var partner = state.FindCountry(partnerId);
            if (relationship == null || proposer == null || partner == null) return 0f;

            float willingness = 20f
                                + (relationship.relations - 50f) * 0.8f
                                + relationship.trust * 0.25f
                                + proposer.pillars.diplomacy * 0.20f;

            // Nobody signs a commercial treaty with a state they are fighting,
            // and a sanctions regime is the opposite of an arrangement.
            if (state.IsAtWar(partnerId) && state.ActiveConfrontationFor(partnerId)?.Involves(proposerId) == true)
                willingness -= 80f;
            if (state.FindSanction(proposerId, partnerId) != null) willingness -= 45f;
            if (state.FindSanction(partnerId, proposerId) != null) willingness -= 45f;

            willingness += relationship.ThreatPerceivedBy(partnerId) * -0.3f;

            // Operator skill at persuasion is capability, not national power.
            if (proposerId == state.playerCountryId)
                willingness += ProgressionSystem.EffectValue(state, SkillEffect.TreatyPersuasion);

            return willingness;
        }

        /// <summary>The true test. Decides what happens, not what we are shown.</summary>
        public static bool WouldAccept(GameState state, string proposerId, TradeDeal deal)
        {
            if (deal == null || string.IsNullOrEmpty(deal.partnerId)) return false;
            if (deal.volume <= 0f) return false;

            // Some things are not a matter of price.
            //
            // A sanctions regime and an open war are not expensive conditions to
            // negotiate around — they are the absence of a commercial
            // relationship. Left as mere penalties they could be bought off with
            // a generous enough offer, which would have let a state sanction a
            // rival and go on buying its energy.
            if (state.FindSanction(proposerId, deal.partnerId) != null) return false;
            if (state.FindSanction(deal.partnerId, proposerId) != null) return false;

            var confrontation = state.ActiveConfrontationFor(proposerId);
            if (confrontation != null && !confrontation.resolved
                && confrontation.Involves(deal.partnerId)) return false;

            return PartnerWillingness(state, proposerId, deal.partnerId)
                   >= CostToPartner(state, proposerId, deal);
        }

        /// <summary>
        /// What our analysts expect, at the precision our political collection on
        /// them supports. Never the true test — reading another government's
        /// commercial position is exactly what intelligence is for.
        /// </summary>
        public static TradeOutlook Assess(GameState state, string proposerId, TradeDeal deal)
        {
            if (deal == null || deal.volume <= 0f) return TradeOutlook.NoTerms;

            float margin = PartnerWillingness(state, proposerId, deal.partnerId)
                           - CostToPartner(state, proposerId, deal);

            var estimate = IntelligenceSystem.GetEstimate(
                state, proposerId, deal.partnerId, IntelDomain.Political);
            var grade = estimate?.confidence ?? ConfidenceGrade.None;

            switch (grade)
            {
                case ConfidenceGrade.Confirmed:
                case ConfidenceGrade.High:
                    return margin >= 0f ? TradeOutlook.Likely : TradeOutlook.Unlikely;
                case ConfidenceGrade.Moderate:
                case ConfidenceGrade.Low:
                    if (margin > 18f) return TradeOutlook.Likely;
                    if (margin < -18f) return TradeOutlook.Unlikely;
                    return TradeOutlook.Uncertain;
                default:
                    return TradeOutlook.Uncertain;
            }
        }

        /// <summary>
        /// The best arrangement this partner would actually sign, or null when
        /// there is none. Gives ground the way a negotiator would: ask for less,
        /// pay more, and finally offer preferential terms.
        /// </summary>
        public static TradeDeal BestAcceptableDeal(GameState state, string proposerId,
            string partnerId, TradeFocus focus)
        {
            var deal = new TradeDeal { partnerId = partnerId, focus = focus, volume = 60f, tariff = 10f };
            if (WouldAccept(state, proposerId, deal)) return deal;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (deal.tariff < 40f) deal.tariff += 10f;
                else if (deal.volume > 20f) deal.volume -= 10f;
                else if (!deal.preferentialTerms) deal.preferentialTerms = true;
                else break;

                if (WouldAccept(state, proposerId, deal)) return deal;
            }

            return null;
        }

        // ---------- doing it ----------

        /// <summary>Player proposal. Spends CP, then delegates.</summary>
        public static bool ProposeAgreement(GameState state, TurnManager turns, TradeDeal deal)
        {
            if (deal == null) return false;
            if (!turns.SpendCommandPoints(ProposalCost, $"Trade proposal to {deal.partnerId}"))
                return false;

            ProgressionSystem.RecordInitiative(state);
            bool signed = ProposeAgreementBy(state, state.playerCountryId, deal);
            if (signed) ProgressionSystem.AwardXP(state, 12, "Trade agreement concluded");
            return signed;
        }

        /// <summary>
        /// Actor-generic. Every government negotiates trade on the same terms —
        /// an AI state short of energy should be out looking for a supplier for
        /// the same reasons the player is.
        /// </summary>
        public static bool ProposeAgreementBy(GameState state, string proposerId, TradeDeal deal)
        {
            var proposer = state.FindCountry(proposerId);
            var partner = state.FindCountry(deal?.partnerId);
            if (proposer == null || partner == null) return false;

            if (!WouldAccept(state, proposerId, deal))
            {
                if (proposerId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "TRADE PROPOSAL DECLINED",
                        $"{partner.displayName} will not sign on those terms. Ask for less, " +
                        "accept a higher tariff, or offer them the better side of it.",
                        partner.id, desk: ReportingDesk.Economy);
                return false;
            }

            var link = state.FindTrade(proposerId, deal.partnerId);
            if (link == null)
            {
                link = new TradeRelation { countryA = proposerId, countryB = deal.partnerId };
                state.trade.Add(link);
            }

            link.volume = Math.Max(link.volume, deal.volume);
            link.tariff = deal.tariff;
            link.embargoed = false;
            link.focus = deal.focus;
            link.initiatedBy = proposerId;

            // An arrangement is a relationship. It also makes the supplicant
            // dependent, which is leverage the other side now holds.
            var relationship = state.FindRelationship(proposerId, deal.partnerId);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations + 6f);
                relationship.trust = Clamp(relationship.trust + 3f);
                relationship.SetDependenceOf(proposerId, Clamp(
                    relationship.DependenceOf(proposerId) + deal.volume * 0.25f));
            }

            state.AddNotification(
                proposerId == state.playerCountryId ? NotificationClass.Priority : NotificationClass.Wire,
                "TRADE AGREEMENT SIGNED",
                $"{proposer.displayName} and {partner.displayName} have concluded a " +
                $"{Phrase.Of(deal.focus).ToLowerInvariant()} trade agreement.",
                partner.id, desk: ReportingDesk.Economy);

            state.AddChronicle(ChronicleCategory.Economic, proposerId,
                $"Trade agreement concluded with {partner.displayName}.", Publicity.Public);
            GameLog.Info("TRADE", $"{proposerId} ↔ {deal.partnerId}: {deal.focus} agreement signed.");
            return true;
        }

        /// <summary>
        /// Walk away from an arrangement. Cheap to do, and the partner remembers
        /// — a state that abandons its commercial commitments is a worse bet for
        /// everyone watching.
        /// </summary>
        public static bool Withdraw(GameState state, TurnManager turns, string partnerId)
        {
            var link = state.FindTrade(state.playerCountryId, partnerId);
            if (link == null) return false;
            if (!turns.SpendCommandPoints(WithdrawalCost, $"Withdraw from trade with {partnerId}"))
                return false;

            state.trade.Remove(link);
            ProgressionSystem.RecordInitiative(state);

            var relationship = state.FindRelationship(state.playerCountryId, partnerId);
            if (relationship != null)
            {
                relationship.relations = Clamp(relationship.relations - 10f);
                relationship.trust = Clamp(relationship.trust - 8f);
            }

            var partner = state.FindCountry(partnerId);
            state.AddChronicle(ChronicleCategory.Economic, state.playerCountryId,
                $"Trade with {partner?.displayName} discontinued.", Publicity.Public);
            return true;
        }

        // ---------- what trade actually supplies ----------

        /// <summary>
        /// How much of a commodity our trade links supply, in ceiling points.
        ///
        /// This is what makes trade an answer to a dependency rather than a
        /// growth modifier. A partner can only sell what they have, a tariff
        /// throttles it, and an embargo or a sanction stops it — so the supply
        /// is exactly as reliable as the relationship behind it, which is the
        /// interesting part.
        /// </summary>
        public static float Supply(GameState state, string countryId, TradeFocus focus)
            => SupplyIfLifted(state, countryId, focus, null, null);

        /// <summary>
        /// Delivered volume, not the stored agreement. Until named routes exist,
        /// mines on either holder's ground impose one national six-point drag.
        /// Multiple sites/endpoints never stack; expiry needs no refund or tick.
        /// </summary>
        public static float EffectiveVolume(GameState state, string aId, string bId, float volume)
        {
            foreach (var site in state.locations)
                if ((site.ownerId == aId || site.ownerId == bId)
                    && MilitarySystem.MineMonthsRemaining(state, site) > 0)
                    return Math.Max(0f, volume - 6f);
            return volume;
        }

        /// <summary>
        /// `Supply` as it would read if `liftSenderId`'s regime on
        /// `liftTargetId` were lifted the way `LiftSanctions` lifts it: that one
        /// record gone and the pair's link no longer embargoed. Every other
        /// closure — a regime the other way, a third state's regime, another
        /// embargo, focus, tariff, stock — is read exactly as it stands, by the
        /// one loop above. Read-only: nothing on the state moves, so an offer
        /// can be priced without a preview editing and restoring the live
        /// world. With no pair named this *is* `Supply`; only the sanctions
        /// exchange (`DiplomaticLeverage.SupplyReliefGain`) names one.
        /// </summary>
        public static float SupplyIfLifted(GameState state, string countryId, TradeFocus focus,
            string liftSenderId, string liftTargetId)
        {
            if (focus == TradeFocus.General) return 0f;
            bool lifting = liftSenderId != null && liftTargetId != null;

            float supplied = 0f;
            foreach (var link in state.trade)
            {
                if (!link.Involves(countryId)) continue;
                bool liftedLink = lifting && link.Involves(liftSenderId) && link.Involves(liftTargetId);
                if (link.embargoed && !liftedLink) continue;
                if (link.focus != focus) continue;

                var partner = state.FindCountry(link.PartnerOf(countryId));
                if (partner == null) continue;

                // A sanctions regime either way closes the tap.
                if (Stands(state, partner.id, countryId, liftSenderId, liftTargetId)) continue;
                if (Stands(state, countryId, partner.id, liftSenderId, liftTargetId)) continue;

                float theirs;
                switch (focus)
                {
                    case TradeFocus.Energy: theirs = partner.resources.energy; break;
                    case TradeFocus.Food: theirs = partner.resources.foodSecurity; break;
                    default: theirs = partner.resources.strategicMaterials; break;
                }

                float throughput = EffectiveVolume(state, link.countryA, link.countryB, link.volume)
                    / 100f * (1f - link.tariff / 150f);
                supplied += theirs * MaxSupplyShare * throughput;
            }

            return supplied;
        }

        /// <summary>A regime from sender to target stands — unless it is the one being lifted.</summary>
        static bool Stands(GameState state, string senderId, string targetId, string liftSenderId, string liftTargetId)
            => state.FindSanction(senderId, targetId) != null
               && !(senderId == liftSenderId && targetId == liftTargetId);

        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

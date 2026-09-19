using System;
using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Specific diplomatic leverage (roadmap #21, first slice; spec 04 §5h):
    /// offer a concrete concession this government needs from us in exchange
    /// for one commitment it carries for us.
    ///
    /// The concession is an ordinary resource supply link — a `TradeRelation` on
    /// the commodity they are short of and we hold in surplus, on concessionary
    /// terms. **Ordinary means ordinary**: what it delivers follows the trade
    /// rules (`TradeSystem.Supply` reads our live stock, the tariff, embargoes
    /// and sanctions every month), and we may change or withdraw it later
    /// through the trade verbs at their usual cost — which does not cancel the
    /// commitment they signed. The commitment is an ordinary `TreatyClause`
    /// they carry. Both
    /// halves already exist; what did not exist was a way to put them on the
    /// same table. A trade proposal was judged on its own commercial merits and
    /// a treaty on its own diplomatic ones, so "we will keep your lights on if
    /// you open your ports to us" could not be said.
    ///
    /// **No parallel currency.** The offer is priced by the systems that own its
    /// halves: `TreatyWillingness` decides how the commitment lands (dependence,
    /// rival gravity, encirclement, our reciprocity and the rest), and the
    /// supply's worth is the ceiling points `TradeSystem.Supply` would actually
    /// add for them — marginal over any link that already exists, so the same
    /// supply cannot be sold twice. One acceptance test decides both halves,
    /// and they are applied together or not at all.
    /// </summary>
    public static class DiplomaticLeverage
    {
        /// <summary>CP to put the offer to them. It is a treaty proposal, priced as one.</summary>
        public const int OfferCost = DiplomacySystem.TreatyProposalCost;

        /// <summary>
        /// Below this we have nothing to spare. The same line `TradeSystem` uses
        /// to call a partner "short" — one definition of scarce, not two.
        /// </summary>
        public const float SurplusFloor = 60f;

        /// <summary>Strategic weight the offered link carries, and the concessionary tariff it is opened at.</summary>
        public const float OfferVolume = 50f;
        public const float OfferTariff = 10f;

        /// <summary>
        /// Willingness per ceiling point the link adds for them. Scaled so a
        /// full offer from a rich supplier (~19 points) moves a modest ask
        /// across the line and a mutual-defence pact stays a matter of warmth.
        /// </summary>
        public const float WillingnessPerCeilingPoint = 1.5f;

        /// <summary>
        /// Ceiling points per unit of treaty value, for the reciprocity ledger.
        /// A real supply link is worth more to its receiver than a paper trade
        /// preference (2.5) and less than a guarantee to fight (5).
        /// </summary>
        public const float CeilingPointsPerValueUnit = 6f;

        /// <summary>
        /// Whether the offer can be put at all. Every reason here is a public
        /// fact — our own stocks, a signed link, a signed treaty, a sanctions
        /// regime, a war — so the refusal leaks nothing about their position.
        /// </summary>
        public static bool CanOffer(GameState state, string actorId, string targetId,
            TradeFocus focus, TreatyCommitment commitment, out string reason)
        {
            reason = null;
            var actor = state?.FindCountry(actorId);
            var target = state?.FindCountry(targetId);
            if (actor == null || target == null || actorId == targetId) { reason = "NO SUCH PARTNER."; return false; }
            if (focus == TradeFocus.General) { reason = "NAME A COMMODITY TO OFFER."; return false; }

            if (OwnStock(actor, focus) < SurplusFloor)
            {
                reason = $"WE HOLD NO {Phrase.Caps(focus)} SURPLUS TO OFFER.";
                return false;
            }

            if (state.FindSanction(actorId, targetId) != null || state.FindSanction(targetId, actorId) != null)
            {
                reason = "NO COMMERCE UNDER SANCTIONS.";
                return false;
            }
            var confrontation = state.ActiveConfrontationFor(actorId);
            if (confrontation != null && !confrontation.resolved && confrontation.Involves(targetId))
            {
                reason = "NOT WHILE WE ARE FIGHTING THEM.";
                return false;
            }

            // A link on another commodity is theirs to keep: converting it would
            // silently take that supply away. A `General` link supplies no
            // commodity (`TradeSystem.Supply` returns zero for it), so it may be
            // converted — its volume and tariff are preserved or improved, the
            // same re-focusing an ordinary trade proposal performs.
            var link = state.FindTrade(actorId, targetId);
            if (link != null && link.focus != focus && link.focus != TradeFocus.General && link.volume > 0f)
            {
                reason = $"OUR LINK WITH THEM ALREADY CARRIES {Phrase.Caps(link.focus)} — CHANGE IT THROUGH TRADE.";
                return false;
            }
            if (LinkGain(state, actorId, targetId, focus) <= 0.01f)
            {
                reason = $"THEY ALREADY DRAW OUR {Phrase.Caps(focus)} ON THESE TERMS — THERE IS NOTHING NEW TO OFFER.";
                return false;
            }

            var treaty = state.FindTreaty(actorId, targetId);
            if (treaty != null && treaty.broken)
            {
                reason = "THE STANDING TREATY IS BROKEN. NORMALISE BEFORE ASKING FOR MORE.";
                return false;
            }
            if (treaty != null && treaty.Carries(targetId, commitment) && !treaty.ClauseIsExpired(state, commitment))
            {
                reason = $"THEY ALREADY CARRY {Phrase.Caps(commitment)} FOR US.";
                return false;
            }
            if (commitment == TreatyCommitment.ArmsControl && !TechnologySystem.Has(actor, "CAP_ARMSCONTROL"))
            {
                reason = "ARMS CONTROL NEEDS A VERIFICATION REGIME WE DO NOT HAVE.";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Ceiling points the link would actually add for them: our link's
        /// marginal supply, bounded by the room they have to use it. Zero once
        /// they draw this from us on these terms — which is what stops one
        /// concession buying two commitments — and zero for a state that does
        /// not need it.
        /// </summary>
        public static float SupplyGain(GameState state, string actorId, string targetId, TradeFocus focus)
        {
            var target = state?.FindCountry(targetId);
            if (target == null) return 0f;
            float gain = LinkGain(state, actorId, targetId, focus);
            if (gain <= 0f) return 0f;

            // **Need is what makes it leverage.** The link is worth exactly
            // the ceiling it lifts: a state already at its ceiling gains
            // nothing however much we could ship, so our surplus alone buys
            // nothing from a state that does not need it. Their live figure is
            // read here, in the true test — never on the offer screen.
            float headroom = Math.Max(0f, 100f - CurrentCeiling(state, target, focus));
            return Math.Min(gain, headroom);
        }

        /// <summary>
        /// Ceiling points our link would add over what it already supplies —
        /// public arithmetic on our stock and our link, used by the validity
        /// gate. Zero once they draw this from us on these terms.
        /// </summary>
        public static float LinkGain(GameState state, string actorId, string targetId, TradeFocus focus)
        {
            var actor = state?.FindCountry(actorId);
            var target = state?.FindCountry(targetId);
            if (actor == null || target == null || focus == TradeFocus.General) return 0f;

            var link = state.FindTrade(actorId, targetId);
            float current = link == null || link.focus != focus || link.embargoed
                ? 0f : Throughput(link.volume, link.tariff);

            // The resulting link keeps the better of the existing terms and the
            // offered ones — for a same-commodity link *and* for a `General` link
            // being converted — so the marginal supply is priced off the link
            // acceptance would actually leave behind.
            bool keepsTerms = link != null && (link.focus == focus || link.focus == TradeFocus.General);
            float offered = Throughput(
                Math.Max(keepsTerms ? link.volume : 0f, OfferVolume),
                Math.Min(keepsTerms ? link.tariff : 100f, OfferTariff));

            // Exactly `TradeSystem.Supply`'s arithmetic for one link: what they
            // can draw is bounded by what we actually have.
            float gain = OwnStock(actor, focus) * TradeSystem.MaxSupplyShare * (offered - current);
            return Math.Max(0f, gain);
        }

        static float CurrentCeiling(GameState state, CountryState country, TradeFocus focus)
        {
            switch (focus)
            {
                case TradeFocus.Energy: return EconomySystem.EnergyCeilingFor(state, country);
                case TradeFocus.Materials: return EconomySystem.MaterialsCeilingFor(state, country);
                case TradeFocus.Food: return EconomySystem.FoodCeilingFor(state, country);
                default: return 100f;
            }
        }

        /// <summary>
        /// How the whole package lands: the commitment judged exactly as a
        /// negotiated treaty clause they carry, plus what the link is worth
        /// to them. The true test — decides what happens, never what is shown.
        /// </summary>
        public static float Willingness(GameState state, string actorId, string targetId,
            TradeFocus focus, TreatyCommitment commitment)
        {
            float treaty = DiplomacySystem.TreatyWillingness(state, actorId, targetId, Clauses(commitment));
            float gain = SupplyGain(state, actorId, targetId, focus);
            return treaty + gain * WillingnessPerCeilingPoint;
        }

        /// <summary>
        /// What our analysts expect, at the precision our political collection on
        /// them supports — the `TradeSystem.Assess` rule. Never the true test.
        /// </summary>
        public static TradeOutlook Assess(GameState state, string actorId, string targetId,
            TradeFocus focus, TreatyCommitment commitment)
        {
            if (!CanOffer(state, actorId, targetId, focus, commitment, out _)) return TradeOutlook.NoTerms;
            float margin = Willingness(state, actorId, targetId, focus, commitment) - 50f;

            var estimate = IntelligenceSystem.GetEstimate(state, actorId, targetId, IntelDomain.Political);
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
        /// Put the offer. Actor-generic and free of CP; the player wrapper on
        /// `GameController` spends. Accepted: the link and the clause are applied
        /// together through the paths a trade agreement and a negotiated treaty
        /// already use. Declined or invalid: nothing is delivered.
        /// </summary>
        public static bool OfferBy(GameState state, string actorId, string targetId,
            TradeFocus focus, TreatyCommitment commitment)
        {
            if (!CanOffer(state, actorId, targetId, focus, commitment, out string reason))
            {
                GameLog.Warn("DIPLO", reason ?? "Offer refused.");
                return false;
            }
            var actor = state.FindCountry(actorId);
            var target = state.FindCountry(targetId);
            var relationship = state.FindRelationship(actorId, targetId);
            if (actor == null || target == null || relationship == null) return false;

            float gain = SupplyGain(state, actorId, targetId, focus);
            float willingness = Willingness(state, actorId, targetId, focus, commitment);
            if (willingness < 50f)
            {
                relationship.AddMemory(state.date, "Rejected a supply-for-commitment offer", -0.5f);
                if (actorId == state.playerCountryId)
                    state.AddNotification(NotificationClass.Advisory, "OFFER DECLINED",
                        $"{target.displayName} will not carry {Phrase.Of(commitment).ToLowerInvariant()} "
                        + $"for {Phrase.Of(focus).ToLowerInvariant()} supply. "
                        + (DiplomacySystem.BlockedByRival(state, actorId, targetId)
                           ?? "What we can supply does not outweigh what we are asking; a lighter commitment, or more warmth first."),
                        targetId, desk: ReportingDesk.Diplomacy);
                GameLog.Info("DIPLO", $"{targetId} declined a {focus}-for-{commitment} offer from {actorId}.");
                return false;
            }

            // ---- the concession: an ordinary supply link on concessionary terms ----
            // Terms are only ever kept or improved: volume takes the larger,
            // tariff the smaller. A `General` link converts to the commodity on
            // the same rule; `CanOffer` has already refused any other commodity.
            var link = state.FindTrade(actorId, targetId);
            bool created = link == null;
            if (created)
            {
                link = new TradeRelation { countryA = actorId, countryB = targetId, tariff = OfferTariff };
                state.trade.Add(link);
            }
            link.focus = focus;
            link.volume = Math.Max(link.volume, OfferVolume);
            link.tariff = created ? OfferTariff : Math.Min(link.tariff, OfferTariff);
            link.embargoed = false;
            link.initiatedBy = actorId;

            // The importer becomes dependent on the supplier — leverage we now
            // hold, and the direction `TradeSystem` records for a proposer who
            // buys. Here they are the buyer.
            relationship.SetDependenceOf(targetId, Clamp(relationship.DependenceOf(targetId) + OfferVolume * 0.25f));
            relationship.relations = Clamp(relationship.relations + 6f);
            relationship.trust = Clamp(relationship.trust + 3f);

            // ---- the commitment: a clause they carry, through the treaty paths ----
            var clauses = Clauses(commitment);
            var standing = state.FindTreaty(actorId, targetId);
            bool concluded = standing == null
                ? DiplomacySystem.ConcludeNegotiatedTreaty(state, actorId, targetId, clauses)
                : DiplomacySystem.RecordDeepening(state, actorId, targetId,
                    new List<TreatyCommitment> { commitment }, null, clauses);
            if (!concluded)
            {
                // Cannot happen after CanOffer, but never leave half an exchange.
                GameLog.Error("DIPLO", "Supply-for-commitment: the commitment could not be recorded.");
                return false;
            }

            // What the terms say about us, on the ledger every other government
            // reads: the commitment's value against what the link is worth.
            float balance = DiplomacySystem.ValueOf(commitment) - gain / CeilingPointsPerValueUnit;
            DiplomacySystem.ApplyReciprocity(state, actor, target, balance);

            state.AddNotification(
                actorId == state.playerCountryId ? NotificationClass.Priority : NotificationClass.Wire,
                "LEVERAGE ACCEPTED",
                $"{target.displayName} accepts: {actor.displayName} opens "
                + $"{Phrase.Of(focus).ToLowerInvariant()} supply on concessionary terms; {target.displayName} carries "
                + $"{Phrase.Of(commitment).ToLowerInvariant()}. What the link delivers follows the trade rules and "
                + "our own stocks; it can be changed or withdrawn through TRADE at the usual cost, and doing so "
                + "does not cancel their commitment.",
                targetId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, actorId,
                $"{Phrase.Of(focus)} supply opened to {target.displayName} in exchange for "
                + $"{Phrase.Of(commitment).ToLowerInvariant()}.", Publicity.Public);
            GameLog.Info("DIPLO", $"{actorId} -> {targetId}: {focus} supply for {commitment} accepted "
                + $"(willingness {willingness:F1}, gain {gain:F1}).");
            return true;
        }

        /// <summary>Our own stock of a commodity — the only resource figure the offer screen may read.</summary>
        public static float OwnStock(CountryState country, TradeFocus focus)
        {
            switch (focus)
            {
                case TradeFocus.Energy: return country.resources.energy;
                case TradeFocus.Materials: return country.resources.strategicMaterials;
                case TradeFocus.Food: return country.resources.foodSecurity;
                default: return 0f;
            }
        }

        /// <summary>Commodities we could offer right now.</summary>
        public static List<TradeFocus> Surpluses(CountryState country)
        {
            var list = new List<TradeFocus>();
            foreach (TradeFocus focus in Enum.GetValues(typeof(TradeFocus)))
                if (focus != TradeFocus.General && OwnStock(country, focus) >= SurplusFloor) list.Add(focus);
            return list;
        }

        /// <summary>
        /// The authored endowment, which is public knowledge (spec 08: every
        /// state is written with a genuine vulnerability). Null for a state with
        /// no authored profile — a breakaway — because there is nothing public to
        /// read, and its live figure is not ours to print.
        /// </summary>
        public static float? AuthoredEndowment(string countryId, TradeFocus focus)
        {
            var profile = WorldFactory.FindProfile(countryId);
            if (profile == null) return null;
            switch (focus)
            {
                case TradeFocus.Energy: return profile.energy;
                case TradeFocus.Materials: return profile.materials;
                case TradeFocus.Food: return profile.food;
                default: return null;
            }
        }

        static List<TreatyClause> Clauses(TreatyCommitment commitment)
            => new List<TreatyClause> { new TreatyClause { commitment = commitment, side = ClauseSide.TheyProvide } };

        static float Throughput(float volume, float tariff) => volume / 100f * (1f - tariff / 150f);
        static float Clamp(float v) => v < 0f ? 0f : (v > 100f ? 100f : v);
    }
}

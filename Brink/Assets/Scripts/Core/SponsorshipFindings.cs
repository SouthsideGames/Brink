using System.Collections.Generic;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>Evidence and its publication reuse live sponsorship and its existing attribution bill.</summary>
    public static class SponsorshipFindings
    {
        public const int ExposureCost = 2;
        public const int ShareCost = 1;
        public const float RequiredPenetration = 65f;

        public static List<SponsorshipFinding> For(GameState state, string observerId)
            => state.sponsorshipFindings?.FindAll(f => f != null && f.observerId == observerId)
               ?? new List<SponsorshipFinding>();

        public static SponsorshipFinding Find(GameState state, string observerId, string movementId)
            => state.sponsorshipFindings?.FindLast(f => f != null && f.observerId == observerId
                && f.Key == movementId);

        public static bool Knows(GameState state, string observerId, Insurgency movement)
            => movement != null && state.sponsorshipFindings != null
               && state.sponsorshipFindings.Exists(f => f != null && f.observerId == observerId
                   && f.movementId == movement.id && f.locationId == movement.locationId
                   && f.sponsorId == movement.sponsorId);

        public static void Discover(GameState state)
        {
            var observer = state.PlayerCountry;
            if (observer == null || !TechnologySystem.Has(observer, "CAP_FORENSICS")) return;
            foreach (var movement in state.insurgencies)
            {
                var location = state.FindLocation(movement.locationId);
                if (location == null || location.ownerId != observer.id || !movement.HasSponsor
                    || movement.sponsorExposed || movement.sponsorId == observer.id
                    || state.FindCountry(movement.sponsorId) == null || Knows(state, observer.id, movement)) continue;
                var network = state.FindNetwork(observer.id, movement.sponsorId);
                if (network == null || network.compromised || network.penetration < RequiredPenetration) continue;
                if (state.sponsorshipFindings == null) state.sponsorshipFindings = new List<SponsorshipFinding>();
                var finding = new SponsorshipFinding { observerId = observer.id, movementId = movement.id,
                    locationId = movement.locationId, sponsorId = movement.sponsorId, discovered = state.date };
                state.sponsorshipFindings.Add(finding);
                string text = Describe(state, finding);
                state.AddNotification(NotificationClass.Priority, "CLASSIFIED FINDING", text,
                    finding.sponsorId, desk: ReportingDesk.Intelligence);
                state.AddChronicle(ChronicleCategory.Intelligence, observer.id, text, Publicity.Secret);
            }
        }

        public static bool CanExpose(GameState state, string movementId, out string reason)
        {
            var finding = Find(state, state.playerCountryId, movementId);
            if (finding == null) { reason = "NO PRIVATE FINDING."; return false; }
            if (finding.silencePromised) { reason = "WE SOLD OUR SILENCE ON THIS FINDING."; return false; }
            var movement = state.insurgencies.Find(i => i.id == finding.movementId);
            var location = state.FindLocation(finding.locationId);
            if (finding.exposed || movement == null || movement.sponsorExposed)
            { reason = "NO UNPUBLISHED CURRENT SPONSORSHIP."; return false; }
            if (movement.sponsorId != finding.sponsorId || movement.locationId != finding.locationId
                || location == null || location.ownerId != state.playerCountryId
                || state.FindCountry(finding.sponsorId) == null)
            { reason = "THE DATED FINDING NO LONGER DESCRIBES SPONSORSHIP ON OUR GROUND."; return false; }
            reason = "";
            return true;
        }

        /// <summary>Wrapper spends CP. Attribution applies exactly its existing bill, once.</summary>
        public static bool Expose(GameState state, string movementId)
        {
            if (!CanExpose(state, movementId, out _)) return false;
            var finding = Find(state, state.playerCountryId, movementId);
            var movement = state.insurgencies.Find(i => i.id == finding.movementId);
            InsurgencySystem.Attribute(state, movement, state.FindLocation(finding.locationId),
                state.PlayerCountry, state.FindCountry(finding.sponsorId));
            finding.exposed = true;
            return true;
        }

        public static bool File(GameState state, string movementId)
        {
            var finding = Find(state, state.playerCountryId, movementId);
            if (finding == null || finding.filed) return false;
            finding.filed = true;
            return true;
        }

        public static bool Reopen(GameState state, string key)
        {
            var finding = Find(state, state.playerCountryId, key);
            if (finding == null || !finding.filed) return false;
            finding.filed = false;
            return true;
        }

        public static bool CanShare(GameState state, string key, string recipientId, out string reason)
        {
            if (!CanExpose(state, key, out reason)) return false;
            var finding = Find(state, state.playerCountryId, key);
            var treaty = state.FindTreaty(state.playerCountryId, recipientId);
            if (recipientId == finding.sponsorId || recipientId == state.playerCountryId
                || state.FindCountry(recipientId) == null || treaty == null
                || !treaty.Carries(state, state.playerCountryId, TreatyCommitment.IntelligenceSharing))
            { reason = "AN ACTIVE INTELLIGENCE-SHARING PARTNER IS REQUIRED."; return false; }
            if (Find(state, recipientId, key) != null)
            { reason = "THEY ALREADY HAVE THIS FINDING."; return false; }
            reason = "";
            return true;
        }

        public static bool Share(GameState state, string key, string recipientId)
        {
            if (!CanShare(state, key, recipientId, out _)) return false;
            var finding = Find(state, state.playerCountryId, key);
            state.sponsorshipFindings.Add(new SponsorshipFinding { observerId = recipientId,
                movementId = finding.movementId, locationId = finding.locationId,
                sponsorId = finding.sponsorId, discovered = finding.discovered,
                sharedById = state.playerCountryId });
            state.AddChronicle(ChronicleCategory.Intelligence, state.playerCountryId,
                "Shared dated sponsorship evidence with " + state.FindCountry(recipientId).displayName + ".", Publicity.Secret);
            return true;
        }

        public static bool CanConfront(GameState state, string key, out string reason)
        {
            if (!CanExpose(state, key, out reason)) return false;
            var finding = Find(state, state.playerCountryId, key);
            if (finding.confronted) { reason = "WE HAVE ALREADY PUT THIS FINDING TO THEM."; return false; }
            if (state.FindRelationship(state.playerCountryId, finding.sponsorId) == null)
            { reason = "NO DIPLOMATIC CHANNEL."; return false; }
            reason = "";
            return true;
        }

        public static bool CanBargain(GameState state, string key, TreatyCommitment commitment, out string reason)
        {
            if (!CanExpose(state, key, out reason)) return false;
            var finding = Find(state, state.playerCountryId, key);
            if (finding.bargainAttempted) { reason = "THIS FINDING HAS ALREADY BEEN PUT ON THE TABLE."; return false; }
            if (commitment != TreatyCommitment.Transit && commitment != TreatyCommitment.IntelligenceSharing
                && commitment != TreatyCommitment.NonAggression)
            { reason = "ASK FOR TRANSIT, INTELLIGENCE SHARING OR NON-AGGRESSION."; return false; }
            if (state.sponsorshipFindings.Exists(f => f != null && f.Key == key && f.observerId != state.playerCountryId))
            { reason = "THE EVIDENCE IS ALREADY SHARED; WE CANNOT SELL EXCLUSIVE SILENCE."; return false; }
            foreach (var war in state.confrontations)
                if (!war.resolved && war.Involves(state.playerCountryId) && war.Involves(finding.sponsorId))
                { reason = "WE ARE FIGHTING THEM."; return false; }
            if (state.FindRelationship(state.playerCountryId, finding.sponsorId) == null)
            { reason = "NO DIPLOMATIC CHANNEL."; return false; }
            return DiplomaticLeverage.CommitmentGate(state, state.PlayerCountry, finding.sponsorId, commitment, null, out reason);
        }

        /// <summary>
        /// Valuation: mean trust at stake in our ordinary publication, capped12.
        /// Not a promise that automatic or third-party attribution cannot occur.
        /// Hidden relationship values are never printed as an outlook.
        /// </summary>
        public static float SilenceValue(GameState state, string key)
        {
            if (!CanExpose(state, key, out _)) return 0f;
            var finding = Find(state, state.playerCountryId, key);
            float total = 0f; int count = 0;
            foreach (var relation in state.relationships)
            {
                if (!relation.Involves(finding.sponsorId)) continue;
                float cost = relation.Involves(state.playerCountryId)
                    ? InsurgencySystem.AttributionBilateralTrustCost : InsurgencySystem.AttributionOtherTrustCost;
                total += relation.trust - System.Math.Max(0f, relation.trust - cost);
                count++;
            }
            return count == 0 ? 0f : System.Math.Min(12f, total / count);
        }

        public static bool Bargain(GameState state, string key, TreatyCommitment commitment)
        {
            if (!CanBargain(state, key, commitment, out _)) return false;
            var finding = Find(state, state.playerCountryId, key);
            float value = SilenceValue(state, key);
            var clauses = new List<TreatyClause> { DiplomaticLeverage.RequestedClause(commitment, null) };
            float willingness = DiplomacySystem.TreatyWillingness(state, state.playerCountryId,
                finding.sponsorId, clauses) + value;
            finding.bargainAttempted = true;
            if (willingness < 50f)
            {
                state.AddNotification(NotificationClass.Advisory, "SILENCE OFFER DECLINED",
                    "They refused the commitment. Our effort was spent; the finding remains private and usable for other responses.",
                    finding.sponsorId, desk: ReportingDesk.Diplomacy);
                return false;
            }
            bool extending = state.FindTreaty(state.playerCountryId, finding.sponsorId) != null;
            if (!DiplomaticLeverage.RecordCommitment(state, state.playerCountryId, finding.sponsorId, commitment, null)) return false;
            finding.silencePromised = true;
            if (extending)
            {
                ProgressionSystem.RecordInitiative(state);
                ProgressionSystem.AwardXP(state, 20, "Treaty deepened");
            }
            DiplomacySystem.ApplyReciprocity(state, state.PlayerCountry, state.FindCountry(finding.sponsorId),
                DiplomacySystem.ValueOf(commitment) - value / 6f);
            string terms = "We will neither publish nor share this finding. They carry " + Phrase.Of(commitment)
                + ". Their sponsorship may continue; other discovery and automatic attribution remain possible. The treaty is public, this bargain is secret.";
            state.AddNotification(NotificationClass.Priority, "SILENCE FOR A COMMITMENT", terms,
                finding.sponsorId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Intelligence, state.playerCountryId, terms, Publicity.Secret);
            return true;
        }

        /// <summary>One private request, judged by ordinary bilateral willingness; no publication bill.</summary>
        public static bool Confront(GameState state, string key)
        {
            if (!CanConfront(state, key, out _)) return false;
            var finding = Find(state, state.playerCountryId, key);
            finding.confronted = true;
            // The ask is to end a channel, not sign an invented treaty. The empty
            // commitment list reuses bilateral willingness without treaty burden.
            bool accepted = DiplomacySystem.TreatyWillingness(state, state.playerCountryId,
                finding.sponsorId, new List<TreatyCommitment>()) >= 50f;
            if (accepted) InsurgencySystem.WithdrawSupportBy(state, finding.sponsorId,
                state.insurgencies.Find(i => i.id == finding.movementId));
            string outcome = accepted
                ? "They closed this supply channel. Delivered weapons remain; no permanent restraint or public attribution was agreed."
                : "They refused to close this supply channel. The evidence remains private; our negotiating effort is spent.";
            state.AddNotification(NotificationClass.Advisory, "PRIVATE CONFRONTATION", outcome,
                finding.sponsorId, desk: ReportingDesk.Diplomacy);
            state.AddChronicle(ChronicleCategory.Diplomatic, state.playerCountryId, outcome, Publicity.Secret);
            return accepted;
        }

        public static string Describe(GameState state, SponsorshipFinding finding)
        {
            if (finding == null || finding.observerId != state.playerCountryId) return "";
            string sponsor = state.FindCountry(finding.sponsorId)?.displayName ?? finding.sponsorId;
            string place = state.FindLocation(finding.locationId)?.displayName ?? finding.locationId;
            var movement = state.insurgencies.Find(i => i.id == finding.movementId);
            bool published = finding.exposed || (movement != null && movement.sponsorId == finding.sponsorId && movement.sponsorExposed);
            return $"{finding.discovered.DisplayString}: forensic collection traced {sponsor}'s sponsorship "
                + $"of the movement at {place}. A dated finding, not a guarantee of present activity. "
                + (published ? "PUBLICLY ATTRIBUTED. " : "PRIVATE EVIDENCE. ")
                + (finding.silencePromised ? "OUR SILENCE IS COMMITTED. " : "")
                + "Publication applies the ordinary attribution consequences; it does not end the movement or recover equipment.";
        }
    }
}

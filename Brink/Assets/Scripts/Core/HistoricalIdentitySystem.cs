using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Phase G historical identity. Reads the history Brink already records and
    /// turns it into a compact answer to: what kind of state have we become?
    /// It is interpretive only; history never becomes a hidden national bonus.
    /// </summary>
    public static class HistoricalIdentitySystem
    {
        public sealed class Identity
        {
            public string name;
            public string basis;
            public int evidence;
        }

        public static List<Identity> Build(GameState state)
            => Build(state, state?.playerCountryId);

        public static List<Identity> Build(GameState state, string countryId)
        {
            var result = new List<Identity>();
            var country = state?.FindCountry(countryId);
            if (country == null) return result;

            int military = 0, economic = 0, diplomatic = 0, political = 0;
            foreach (var entry in state.chronicle)
            {
                if (entry == null || entry.countryId != countryId || !WorldWire.CanShow(state, entry)
                    || entry.date.CompareTo(state.startDate) < 0 || entry.date.CompareTo(state.date) > 0) continue;
                switch (entry.category)
                {
                    case ChronicleCategory.Military: military++; break;
                    case ChronicleCategory.Economic: economic++; break;
                    case ChronicleCategory.Diplomatic: diplomatic++; break;
                    case ChronicleCategory.Political: political++; break;
                }
            }

            Add(result, "SECURITY STATE", military, "archive emphasis: military entries, not proof that this state initiated them");
            Add(result, "COMMERCIAL POWER", economic, "archive emphasis: economic entries, not proof of prosperity");
            Add(result, "BROKER STATE", diplomatic, "archive emphasis: diplomatic entries, not proof of successful mediation");
            Add(result, "CONTESTED ORDER", political, "archive emphasis: political entries, not proof of instability");

            // Current strength is not historical evidence, and foreign true
            // values must never be laundered through a historical label.

            result.Sort((a, b) => b.evidence != a.evidence ? b.evidence.CompareTo(a.evidence) : string.CompareOrdinal(a.name, b.name));
            return result;
        }

        public static string Render(GameState state)
            => Render(state, state?.playerCountryId);

        public static string Render(GameState state, string countryId)
        {
            var country = state?.FindCountry(countryId);
            if (country == null) return "HISTORICAL IDENTITY — NO COUNTRY ON RECORD.";
            var identities = Build(state, countryId);
            var sb = new StringBuilder("HISTORICAL IDENTITY — ");
            sb.Append(country.displayName.ToUpperInvariant()).Append(" / ").AppendLine(state.date.year.ToString());
            sb.AppendLine("KNOWN FOR — SAVED EVIDENCE, NOT A FIXED NATIONAL CHARACTER");
            foreach (string line in KnownFor(state, countryId)) sb.Append("  ").AppendLine(line);
            sb.AppendLine("ARCHIVE EMPHASIS — VOLUME OF ENTRIES, NOT A STRATEGY VERDICT");
            if (identities.Count == 0) sb.AppendLine("  The record has not yet settled into a clear pattern.");
            int shown = Math.Min(4, identities.Count);
            for (int i = 0; i < shown; i++)
                sb.Append(identities[i].evidence >= 4 ? "!! " : "!  ").Append(identities[i].name).Append(" — ").AppendLine(identities[i].basis + ".");
            sb.AppendLine("HISTORICAL TURNING POINTS — LATEST EIGHT DATED RECORDS, NOT INVENTED TITLES");
            var points = TurningPoints(state, countryId);
            if (points.Count == 0) sb.AppendLine("  No identified turning points in the saved record yet.");
            foreach (var point in points) sb.Append("  ").Append(point.date.DisplayString).Append(" — ").AppendLine(point.text);
            sb.AppendLine("PUBLIC MEMORY — WHAT OTHER GOVERNMENTS CAN CITE");
            sb.AppendLine(CoercionPrecedent(state, countryId));
            if (countryId == state.playerCountryId) sb.AppendLine(StrategicEraSystem.RenderEarned(state, countryId));
            sb.Append("Labels grant no power. AI coercion assessment remembers distinct sanctioned partners for 120 months; the archive lasts. Missing old-save evidence is not reconstructed.");
            return sb.ToString();
        }

        public const int PublicMemoryMonths = 120;

        // One public observation definition for the historical reader and the
        // real AI consumer. No free read of secret actions or prose parsing.
        public static HashSet<string> CoercionTargets(GameState state, string countryId)
        {
            var targets = new HashSet<string>(StringComparer.Ordinal);
            if (state?.FindCountry(countryId) == null) return targets;
            foreach (var sanction in state.sanctions)
                if (sanction.senderId == countryId) targets.Add(sanction.targetId);
            foreach (var entry in state.chronicle)
                if (PublicSanction(state, entry, countryId)
                    && state.date.MonthsSince(entry.date) <= PublicMemoryMonths)
                    targets.Add(entry.counterpartyId);
            return targets;
        }

        static bool PublicSanction(GameState state, ChronicleEntry entry, string countryId)
            => entry != null && entry.countryId == countryId && entry.publicity == Publicity.Public
                && entry.historicalEvent == HistoricalEvent.SanctionsImposed
                && !string.IsNullOrEmpty(entry.counterpartyId) && entry.counterpartyId != countryId
                && entry.date.month >= 1 && entry.date.month <= 12
                && entry.date.CompareTo(state.startDate) >= 0 && entry.date.CompareTo(state.date) <= 0;

        public static string CoercionPrecedent(GameState state, string countryId)
        {
            if (state?.FindCountry(countryId) == null) return "No public coercion precedent on record.";
            ChronicleEntry latest = null;
            foreach (var entry in state.chronicle)
                if (PublicSanction(state, entry, countryId) && state.date.MonthsSince(entry.date) <= PublicMemoryMonths
                    && (latest == null || entry.date.CompareTo(latest.date) > 0)) latest = entry;
            if (latest == null) return "No dated coercion precedent in the 120-month window; live measures still count.";
            return latest.date.DisplayString + " — sanctions against " + Name(state, latest.counterpartyId)
                + "; " + CoercionTargets(state, countryId).Count + " distinct partner(s) in the current public coercion reading. Lifting ends the measure, not the recorded act.";
        }

        public static List<string> KnownFor(GameState state, string countryId)
        {
            var lines = new List<string>();
            var country = state?.FindCountry(countryId);
            if (country == null) return lines;
            int initiated = 0, economic = 0, intelPacts = 0, broken = 0;
            foreach (var front in state.confrontations)
                if (front.initiatorId == countryId && front.startDate.CompareTo(state.date) <= 0)
                { initiated++; if (front.primaryStrategy == PrimaryStrategy.Economic) economic++; }
            var partners = new HashSet<string>();
            foreach (var treaty in state.treaties)
                if (treaty.Involves(countryId) && treaty.signedDate.CompareTo(state.date) <= 0)
                {
                    if (treaty.Has(TreatyCommitment.IntelligenceSharing)) partners.Add(treaty.PartnerOf(countryId));
                    if (treaty.broken && treaty.brokenBy == countryId) broken++;
                }
            intelPacts = partners.Count;
            var sanctions = new HashSet<string>();
            foreach (var entry in state.chronicle)
                if (PublicSanction(state, entry, countryId)) sanctions.Add(entry.counterpartyId);
            if (initiated > 0) lines.Add(initiated + " initiated confrontation(s) in the saved record (not necessarily shooting wars).");
            if (economic > 0) lines.Add(economic + " recorded confrontation(s) currently or finally led by economic strategy; past pivots are not reconstructed.");
            if (sanctions.Count > 0) lines.Add("Economic coercion recorded against " + sanctions.Count + " distinct partner(s), including measures since lifted.");
            if (intelPacts > 0) lines.Add("Intelligence-sharing agreements with " + intelPacts + " distinct partner(s) on record; signing is not proof of current access.");
            if (broken > 0) lines.Add(broken + " agreement(s) broken by this state, still on the record.");
            var start = country.foundedDate.year > 0 && country.foundedDate.CompareTo(state.startDate) > 0 ? country.foundedDate : state.startDate;
            int years = Math.Max(0, state.date.MonthsSince(start) / 12);
            if (years >= 10 && initiated == 0) lines.Add("No initiated confrontation in " + years + " years of this save's record. Not proof of an absence of defensive wars, covert action or missing legacy events.");
            if (lines.Count == 0) lines.Add("Insufficient recorded conduct for a historical reputation.");
            return lines;
        }

        public sealed class TurningPoint
        {
            public GameDate date;
            public string text;
        }

        public static List<TurningPoint> TurningPoints(GameState state, string countryId, int limit = 8)
        {
            var points = new List<TurningPoint>();
            if (state?.FindCountry(countryId) == null || limit <= 0) return points;
            foreach (var front in state.confrontations)
                if (front.Involves(countryId)) points.Add(new TurningPoint { date = front.startDate,
                    text = Name(state, front.initiatorId) + " opened a confrontation with " + Name(state, front.defenderId) + " (" + front.objective + ")." });
            foreach (var treaty in state.treaties)
                if (treaty.Involves(countryId)) points.Add(new TurningPoint { date = treaty.signedDate,
                    text = "Agreement signed with " + Name(state, treaty.PartnerOf(countryId)) + "." });
            foreach (var entry in state.chronicle)
            {
                if (PublicSanction(state, entry, entry?.countryId)
                    && (entry.countryId == countryId || entry.counterpartyId == countryId))
                    points.Add(new TurningPoint { date = entry.date,
                        text = Name(state, entry.countryId) + " imposed sanctions against " + Name(state, entry.counterpartyId) + "." });
                if (entry != null && entry.historicalEvent == HistoricalEvent.TurningPoint
                    && (entry.countryId == countryId || entry.counterpartyId == countryId)
                    && WorldWire.CanShow(state, entry))
                    points.Add(new TurningPoint { date = entry.date, text = entry.text });
            }
            points.RemoveAll(p => p.date.year <= 0 || p.date.month < 1 || p.date.month > 12 || p.date.CompareTo(state.date) > 0);
            points.Sort((a,b) => { int order = b.date.CompareTo(a.date); return order != 0 ? order : string.CompareOrdinal(a.text,b.text); });
            var unique = new List<TurningPoint>();
            foreach (var point in points)
            {
                if (unique.Exists(p => p.date.CompareTo(point.date) == 0 && p.text == point.text)) continue;
                unique.Add(point);
                if (unique.Count == limit) break;
            }
            return unique;
        }

        static string Name(GameState state, string id) => state.FindCountry(id)?.displayName ?? id ?? "Unknown state";

        static void Add(List<Identity> list, string name, int evidence, string basis)
        {
            if (evidence < 2) return;
            list.Add(new Identity { name = name, evidence = evidence, basis = basis });
        }
    }
}

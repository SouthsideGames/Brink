using System;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Names the current strategic era from the operator's standing doctrine and
    /// the conditions under which it is being practiced. Era names are narrative
    /// handles for the Chronicle, not modifiers.
    /// </summary>
    public static class StrategicEraSystem
    {
        public sealed class Era
        {
            public string name;
            public string doctrine;
            public string character;
            public GameDate since;
        }

        public static Era Current(GameState state)
        {
            var strategy = state?.mandate?.strategy;
            if (state?.PlayerCountry == null || strategy == null || !strategy.doctrineChosen) return null;
            string prefix = PressurePrefix(state);
            return new Era
            {
                name = prefix + DoctrineName(strategy.doctrine),
                doctrine = strategy.doctrine.ToString(),
                character = Character(strategy.doctrine, prefix),
                since = strategy.doctrineAdopted
            };
        }

        public static string Render(GameState state)
        {
            var era = Current(state);
            if (era == null) return "STRATEGIC ERA — NO STANDING DOCTRINE HAS YET DEFINED THIS POSTING.";
            var sb = new StringBuilder("DECLARED STRATEGIC ERA — ").AppendLine(era.name.ToUpperInvariant());
            sb.Append("DOCTRINE: ").AppendLine(era.doctrine.ToUpperInvariant());
            sb.Append("CHARACTER: ").AppendLine(era.character);
            sb.Append("SINCE: ").Append(era.since.ToString());
            return sb.ToString();
        }

        public const int ReviewMonths = 60;
        public const int SustainedMonths = 36;
        static readonly string[] Names = { "Defence Partnership", "Economic Pressure", "Forward Presence", "Collection Network", "Commercial Partnership" };
        static readonly string[] Evidence = { "active defence agreement", "outgoing sanctions in force", "Forward military posture", "uncompromised collection station with access", "active trade-preference agreement" };

        /// <summary>
        /// Own-office classified history, sampled once at month close. A missing
        /// interval is not sustained conduct: start a fresh window, keep old names.
        /// No event prose is parsed and no current value fills unobserved months.
        /// </summary>
        public static void RecordMonth(GameState state)
        {
            var country = state?.PlayerCountry;
            if (country == null || state.date.year <= 0 || state.date.month < 1 || state.date.month > 12) return;
            var record = country.strategicConduct;
            if (record == null) country.strategicConduct = record = new StrategicConductRecord();
            if (record.lastObserved.year > 0 && state.date.CompareTo(record.lastObserved) <= 0) return;
            if (record.months == 0 || record.heldMonths == null || record.heldMonths.Length != Names.Length
                || record.lastObserved.year == 0 || state.date.MonthsSince(record.lastObserved) != 1)
            {
                record.since = state.date;
                record.months = 0;
                record.heldMonths = new int[Names.Length];
            }

            bool defence = false, commerce = false;
            foreach (var treaty in state.treaties)
            {
                if (!treaty.Involves(country.id) || treaty.signedDate.CompareTo(state.date) > 0) continue;
                defence |= treaty.HasActive(state, TreatyCommitment.MutualDefense);
                commerce |= treaty.HasActive(state, TreatyCommitment.TradePreference);
            }
            bool[] held = { defence, state.sanctions.Exists(s => s.senderId == country.id),
                country.military.posture == MilitaryPosture.Forward,
                state.networks.Exists(n => n.ownerId == country.id && n.targetId != country.id && n.penetration > 0f && !n.compromised), commerce };
            for (int i = 0; i < held.Length; i++) if (held[i]) record.heldMonths[i]++;
            record.lastObserved = state.date;
            record.months++;
            if (record.months < ReviewMonths) return;

            // Rank by observed duration; stable authored order resolves equal counts.
            int first = -1, second = -1;
            for (int i = 0; i < Names.Length; i++)
            {
                if (record.heldMonths[i] < SustainedMonths) continue;
                if (first < 0 || record.heldMonths[i] > record.heldMonths[first]) { second = first; first = i; }
                else if (second < 0 || record.heldMonths[i] > record.heldMonths[second]) second = i;
            }
            string name = first < 0 ? null : Names[first] + (second < 0 ? "" : " and " + Names[second]);
            var text = new StringBuilder("CONDUCT REVIEW ").Append(record.since.DisplayString).Append(" to ")
                .Append(state.date.DisplayString).Append(" — ");
            if (name == null) text.Append("No settled doctrine earned in this window. ");
            else text.Append(name).Append(" Doctrine / ").Append(name).Append(" Era. ");
            text.Append("Resolved-month observations (not spending totals, intent or uninterrupted duration): ");
            for (int i = 0; i < Names.Length; i++)
                text.Append(Evidence[i]).Append(' ').Append(record.heldMonths[i]).Append('/').Append(ReviewMonths).Append("; ");
            text.Append("Naming requires at least 36 of 60 months; up to two longest-held patterns, ties in listed order. ");
            if (!string.IsNullOrEmpty(record.lastEarnedName))
                text.Append("Looking back to ").Append(record.lastEarnedDate.DisplayString).Append(": ")
                    .Append(record.lastEarnedName).Append(" Doctrine remains in the archive; ")
                    .Append(name == record.lastEarnedName ? "this window sustains that reading. " : "this window does not repeat that definition. ");
            text.Append("A description of this government's conduct, including delegation and inherited commitments, not an operator achievement or a source of power.");
            state.AddChronicle(ChronicleCategory.System, country.id, text.ToString(), Publicity.Secret, HistoricalEvent.ConductReview);
            if (name != null && name != record.lastEarnedName)
            { record.lastEarnedName = name; record.lastEarnedDate = state.date; }
            record.months = 0;
        }

        public static string RenderEarned(GameState state, string countryId)
        {
            if (state?.PlayerCountry == null || countryId != state.playerCountryId) return "";
            var record = state.PlayerCountry.strategicConduct;
            var text = new StringBuilder("EARNED DOCTRINES AND ERAS — CLASSIFIED POSTING HISTORY\n");
            text.Append("Recording ").Append(record?.months ?? 0).Append("/60 months of the current window; no legacy backfill.\n");
            // Latest review is only a summary surface; every review stays in Chronicle.
            ChronicleEntry latest = null;
            foreach (var entry in state.chronicle)
                if (entry != null && entry.countryId == countryId && entry.historicalEvent == HistoricalEvent.ConductReview
                    && entry.date.CompareTo(state.date) <= 0 && (latest == null || entry.date.CompareTo(latest.date) > 0)) latest = entry;
            text.Append(latest?.text ?? "No five-year conduct review recorded yet. Choosing a doctrine does not earn a historical name.");
            return text.ToString();
        }

        static string PressurePrefix(GameState state)
        {
            var c = state.PlayerCountry;
            if (state.IsAtWar(c.id) || c.warExhaustion >= 65f) return "Wartime ";
            if (c.socialUnrest >= 65f || c.governmentApproval < 30f) return "Crisis ";
            if (state.treasuryTrendSeeded && state.treasuryTrend < -6f) return "Austerity ";
            return "";
        }

        static string DoctrineName(StrategicDoctrine doctrine)
        {
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence: return "Shield Era";
                case StrategicDoctrine.Prosperity: return "Growth Era";
                case StrategicDoctrine.Influence: return "Reach Era";
                case StrategicDoctrine.Resilience: return "Hardening Era";
                case StrategicDoctrine.Transformation: return "Reconstruction Era";
                default: return "Stewardship Era";
            }
        }

        static string Character(StrategicDoctrine doctrine, string prefix)
        {
            string baseText;
            switch (doctrine)
            {
                case StrategicDoctrine.Deterrence: baseText = "security and readiness organize delegated government attention"; break;
                case StrategicDoctrine.Prosperity: baseText = "growth and economic room organize delegated government attention"; break;
                case StrategicDoctrine.Influence: baseText = "external leverage and relationships organize delegated government attention"; break;
                case StrategicDoctrine.Resilience: baseText = "redundancy and endurance organize delegated government attention"; break;
                case StrategicDoctrine.Transformation: baseText = "institutional change organizes delegated government attention"; break;
                default: baseText = "balanced stewardship organizes delegated government attention"; break;
            }
            return string.IsNullOrEmpty(prefix) ? baseText + "." : baseText + " under sustained national pressure.";
        }
    }
}

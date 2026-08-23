using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>What kind of thing happened.</summary>
    public enum TelemetryKind
    {
        SessionStart,
        PlayerAction,
        AiAction,
        Crisis,
        MonthEnd,
        Note
    }

    /// <summary>
    /// One recorded moment. Deliberately flat and small: a decade of play is
    /// about 120 months, so the whole session fits comfortably in memory and in
    /// a file a person can read.
    /// </summary>
    public class TelemetryEvent
    {
        public int monthIndex;
        public string date;
        public TelemetryKind kind;

        /// <summary>Who did it. Empty for world-level records.</summary>
        public string actorId = "";

        /// <summary>What was done, as a stable bucket — not interpolated prose.</summary>
        public string action = "";

        /// <summary>Free detail for a human reading the log.</summary>
        public string detail = "";

        public float amount;
        public bool success = true;
    }

    /// <summary>
    /// Session recording for live play (GDD §34.1: every significant system must
    /// expose debug and test controls).
    ///
    /// **Why this exists.** Balance and correctness have been measured entirely by
    /// harness bots, which play the way the harness was written to play. They
    /// cannot tell us which of twenty-three military verbs a person actually
    /// reaches for, whether command capacity sits unspent, or which crisis is
    /// always ignored. A single operator playing for an evening is a different
    /// kind of evidence, and with one tester it is the only kind available.
    ///
    /// **Three design constraints, all deliberate.**
    ///
    /// 1. **Local only. No network, ever.** The game is offline-first and sold
    ///    once; a telemetry uploader would be an outward-facing act requiring
    ///    consent this project has not asked for. The recorder writes a file on
    ///    the device and nothing else. Do not add transmission.
    /// 2. **Hooked at the resource spends, not at forty call sites.** Almost
    ///    every player action already passes through `SpendCommandPoints`,
    ///    `SpendPoliticalCapital` or the Influence decrements, and each carries a
    ///    *reason string*. Recording there means a verb added later is captured
    ///    without anyone remembering to instrument it — the same reasoning that
    ///    put the monthly system list in `SimulationPipeline`.
    /// 3. **Reasons must be stable buckets.** `"Assault at Norfolk"` and
    ///    `"Assault at Shanghai"` are the same decision and must aggregate as
    ///    one. `Bucket()` strips the specifics. This is the identical trap that
    ///    defeated XP diminishing returns when two call sites interpolated a
    ///    target into the reason string.
    ///
    /// Plain C# with no scene dependencies, so the analysis below is edit-mode
    /// testable and a harness run can be inspected the same way a real session is.
    /// </summary>
    public static class Telemetry
    {
        /// <summary>Off by default. Turned on by the shell in development builds.</summary>
        public static bool Enabled;

        static readonly List<TelemetryEvent> events = new List<TelemetryEvent>();

        public static IReadOnlyList<TelemetryEvent> Events => events;

        public static int SessionSeed { get; private set; }
        public static string SessionCountryId { get; private set; } = "";

        // ---------- recording ----------

        public static void BeginSession(GameState state)
        {
            events.Clear();
            if (!Enabled || state == null) return;

            SessionSeed = state.rngSeed;
            SessionCountryId = state.playerCountryId;

            Record(state, TelemetryKind.SessionStart, state.playerCountryId, "SESSION",
                $"seed {state.rngSeed}, {state.playerCountryId}, {state.difficulty}");
        }

        public static void Record(GameState state, TelemetryKind kind, string actorId,
            string action, string detail = "", float amount = 0f, bool success = true)
        {
            if (!Enabled || state == null) return;

            events.Add(new TelemetryEvent
            {
                monthIndex = state.date.MonthsSince(state.startDate),
                date = state.date.ToString(),
                kind = kind,
                actorId = actorId ?? "",
                action = action ?? "",
                detail = detail ?? "",
                amount = amount,
                success = success
            });
        }

        /// <summary>
        /// Reduce a spend reason to a stable bucket.
        ///
        /// Reasons are written for a human reading a log — "Assault at Norfolk
        /// Naval Complex", "Set national priority: Security". Two spends on the
        /// same verb must aggregate as one, or every count is 1 and the whole
        /// exercise measures nothing.
        /// </summary>
        public static string Bucket(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "UNKNOWN";

            string trimmed = reason;

            // "Assault at Norfolk" -> "Assault";  "Directive: ECO_GROWTH" -> "Directive"
            int at = trimmed.IndexOf(" at ", StringComparison.OrdinalIgnoreCase);
            if (at > 0) trimmed = trimmed.Substring(0, at);

            int colon = trimmed.IndexOf(':');
            if (colon > 0) trimmed = trimmed.Substring(0, colon);

            // "Outreach to China" -> "Outreach"
            int to = trimmed.IndexOf(" to ", StringComparison.OrdinalIgnoreCase);
            if (to > 0) trimmed = trimmed.Substring(0, to);

            int on = trimmed.IndexOf(" on ", StringComparison.OrdinalIgnoreCase);
            if (on > 0) trimmed = trimmed.Substring(0, on);

            return trimmed.Trim().ToUpperInvariant();
        }

        // ---------- the monthly snapshot ----------

        /// <summary>
        /// One line per month of the things a ratchet would show up in.
        ///
        /// Recorded rather than derived because the analysis needs the *series*:
        /// the recurring bug in this project is a value that only ever moves one
        /// way, and that is invisible in a final snapshot.
        /// </summary>
        public static void RecordMonth(GameState state)
        {
            if (!Enabled || state == null) return;
            var player = state.PlayerCountry;
            if (player == null) return;

            var detail = new StringBuilder();
            detail.Append($"cp={state.commandPoints.current}");
            detail.Append($" pc={state.politicalCapital:F1}");
            detail.Append($" inf={state.influence}");
            detail.Append($" stab={player.stability:F1}");
            detail.Append($" unity={player.nationalUnity:F1}");
            detail.Append($" appr={player.governmentApproval:F1}");
            detail.Append($" mil={player.pillars.military:F1}");
            detail.Append($" eco={player.pillars.economy:F1}");
            detail.Append($" gdp={player.economy.gdp:F0}");
            detail.Append($" treas={player.resources.treasury:F0}");
            detail.Append($" wars={CountPlayerWars(state)}");

            Record(state, TelemetryKind.MonthEnd, player.id, "MONTH", detail.ToString());
        }

        static int CountPlayerWars(GameState state)
        {
            int count = 0;
            foreach (var confrontation in state.confrontations)
                if (!confrontation.resolved && confrontation.Involves(state.playerCountryId)) count++;
            return count;
        }

        // ---------- reading it back ----------

        /// <summary>How often each bucket was used, for one kind of event.</summary>
        public static Dictionary<string, int> CountsFor(TelemetryKind kind, bool playerOnly)
        {
            var counts = new Dictionary<string, int>();
            foreach (var e in events)
            {
                if (e.kind != kind) continue;
                if (playerOnly && e.actorId != SessionCountryId) continue;
                counts.TryGetValue(e.action, out int existing);
                counts[e.action] = existing + 1;
            }
            return counts;
        }

        /// <summary>The recorded month series for one field of the snapshot line.</summary>
        public static List<float> Series(string field)
        {
            var series = new List<float>();
            foreach (var e in events)
            {
                if (e.kind != TelemetryKind.MonthEnd) continue;
                float? value = ParseField(e.detail, field);
                if (value.HasValue) series.Add(value.Value);
            }
            return series;
        }

        static float? ParseField(string detail, string field)
        {
            string key = field + "=";
            int start = detail.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return null;
            start += key.Length;
            int end = detail.IndexOf(' ', start);
            if (end < 0) end = detail.Length;
            return float.TryParse(detail.Substring(start, end - start),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                ? parsed
                : (float?)null;
        }

        /// <summary>The whole session as readable lines, for the file and the screen.</summary>
        public static string ToText()
        {
            var sb = new StringBuilder();
            foreach (var e in events)
            {
                sb.Append(e.date).Append(" | ").Append(e.kind.ToString().ToUpperInvariant());
                if (!string.IsNullOrEmpty(e.actorId)) sb.Append(" | ").Append(e.actorId);
                sb.Append(" | ").Append(e.action);
                if (Math.Abs(e.amount) > 0.001f) sb.Append(" | ").Append(e.amount.ToString("F1"));
                if (!e.success) sb.Append(" | REFUSED");
                if (!string.IsNullOrEmpty(e.detail)) sb.Append(" | ").Append(e.detail);
                sb.AppendLine();
            }
            return sb.ToString();
        }

        public static void Clear() => events.Clear();
    }
}

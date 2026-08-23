using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    public enum FindingSeverity
    {
        /// <summary>Worth knowing. Probably fine.</summary>
        Note,

        /// <summary>Looks wrong. Check it.</summary>
        Warning,

        /// <summary>A known bug class in this project, matched.</summary>
        Suspect
    }

    public class TelemetryFinding
    {
        public FindingSeverity severity;
        public string headline;
        public string detail;

        public override string ToString()
            => $"[{severity.ToString().ToUpperInvariant()}] {headline}\n    {detail}";
    }

    /// <summary>
    /// Turns a played session into the same kind of signal the test harness
    /// gives — but from real play.
    ///
    /// This is the half that matters. A recorder on its own produces a pile of
    /// lines nobody reads. What a designer with one tester actually needs is
    /// *"here is what looked wrong in the evening you just spent"*, and in
    /// particular the failure modes this project has hit over and over:
    ///
    /// - **A value that only ever moves one way.** Approval, war exhaustion,
    ///   manpower, energy, stability and unity have each shipped as a ratchet at
    ///   some point. A recorded month series makes it visible in one pass.
    /// - **A verb nobody can reach.** Half-wired systems, options gated behind a
    ///   condition that never fires, AI states locked out of player verbs. If the
    ///   catalog has twenty-three operations and a decade of play used four, that
    ///   is worth knowing before shipping the other nineteen.
    /// - **A resource with nothing to buy.** Political Capital pinned at its cap
    ///   is exactly how the Government pillar's problem was diagnosed, and it was
    ///   only found because someone happened to look.
    /// - **A choice that never works.** An operation attempted repeatedly and
    ///   never succeeding is either a scale bug or a lie in the interface.
    ///
    /// Every finding names what to check rather than asserting a fault: this
    /// reads one session, and one session is evidence, not proof.
    /// </summary>
    public static class TelemetryAnalysis
    {
        /// <summary>Below this many months, a session is too short to conclude anything.</summary>
        public const int MinimumMonths = 12;

        public static List<TelemetryFinding> Findings(GameState state)
        {
            var findings = new List<TelemetryFinding>();
            var months = Telemetry.Series("cp");

            if (months.Count < MinimumMonths)
            {
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Note,
                    headline = $"Session too short to read ({months.Count} months)",
                    detail = $"At least {MinimumMonths} resolved months are needed before any of " +
                             "this means anything. Keep playing."
                });
                return findings;
            }

            CheckRatchets(findings);
            CheckUnspentCapacity(findings);
            CheckUnusedVerbs(findings, state);
            CheckFutileActions(findings);
            CheckWorldActivity(findings);
            CheckCrises(findings);

            if (findings.Count == 0)
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Note,
                    headline = "Nothing looked wrong in this session",
                    detail = $"{months.Count} months recorded. No stuck values, no wasted capacity, " +
                             "no dead options and no futile actions."
                });

            return findings;
        }

        // ---------- the recurring bug class ----------

        static void CheckRatchets(List<TelemetryFinding> findings)
        {
            // Each of these has shipped as a one-way value at some point in this
            // project's history. A live session is the cheapest place to catch
            // the next one.
            var watched = new (string field, string name)[]
            {
                ("stab", "Stability"),
                ("unity", "National unity"),
                ("appr", "Government approval"),
                ("mil", "Military pillar"),
                ("eco", "Economy pillar"),
                ("treas", "Treasury")
            };

            foreach (var (field, name) in watched)
            {
                var series = Telemetry.Series(field);
                if (series.Count < MinimumMonths) continue;

                int up = 0, down = 0;
                for (int i = 1; i < series.Count; i++)
                {
                    float delta = series[i] - series[i - 1];
                    if (delta > 0.01f) up++;
                    else if (delta < -0.01f) down++;
                }

                if (up + down < 6) continue; // barely moved; nothing to conclude

                // A value that moved a lot and never once went the other way is
                // the shape of every mean-reversion bug this project has had.
                if (down == 0)
                    findings.Add(new TelemetryFinding
                    {
                        severity = FindingSeverity.Suspect,
                        headline = $"{name} only ever went up",
                        detail = $"{up} increases, 0 decreases across {series.Count} months " +
                                 $"({series[0]:F1} → {series[series.Count - 1]:F1}). Check it has a " +
                                 "reachable path downward under the same conditions."
                    });
                else if (up == 0)
                    findings.Add(new TelemetryFinding
                    {
                        severity = FindingSeverity.Suspect,
                        headline = $"{name} only ever went down",
                        detail = $"{down} decreases, 0 increases across {series.Count} months " +
                                 $"({series[0]:F1} → {series[series.Count - 1]:F1}). This is the bug " +
                                 "class that has bitten this project five times: a value drained by " +
                                 "conditions that recur, with a recovery path that does not."
                    });
            }
        }

        // ---------- resources with nothing to buy ----------

        static void CheckUnspentCapacity(List<TelemetryFinding> findings)
        {
            // Command capacity refills each month rather than banking, so the
            // question is not "is it at a cap" but "was it left on the table".
            // Baseline income is the yardstick.
            var cp = Telemetry.Series("cp");
            if (cp.Count >= MinimumMonths)
            {
                int idle = 0;
                foreach (float value in cp) if (value >= 4f) idle++;
                if (idle > cp.Count * 0.5f)
                    findings.Add(new TelemetryFinding
                    {
                        severity = FindingSeverity.Warning,
                        headline = $"Command capacity was left unspent in {idle} of {cp.Count} months",
                        detail = "Four or more CP still in hand at month end. Either there was " +
                                 "nothing worth ordering, or what there was cost too little to " +
                                 "absorb a month's capacity."
                    });
            }

            CheckPool(findings, "pc", "Political Capital", GameState.PoliticalCapitalCap, 0.85f);
        }

        static void CheckPool(List<TelemetryFinding> findings, string field, string name,
            float cap, float pinnedFraction)
        {
            var series = Telemetry.Series(field);
            if (series.Count < MinimumMonths) return;

            int pinned = 0;
            float total = 0f;
            foreach (float value in series)
            {
                total += value;
                if (value >= cap * pinnedFraction) pinned++;
            }

            float share = pinned / (float)series.Count;
            if (share < 0.5f) return;

            findings.Add(new TelemetryFinding
            {
                severity = FindingSeverity.Warning,
                headline = $"{name} sat near its ceiling for {share:P0} of the session",
                detail = $"Mean {total / series.Count:F1} against a cap of {cap:F0}. Either there is " +
                         "nothing worth buying, or what there is costs too little. This is exactly " +
                         "how the Government pillar's problem was diagnosed."
            });
        }

        // ---------- verbs nobody reached ----------

        static void CheckUnusedVerbs(List<TelemetryFinding> findings, GameState state)
        {
            var used = Telemetry.CountsFor(TelemetryKind.PlayerAction, playerOnly: true);

            var missing = new List<string>();
            foreach (var profile in OperationCatalog.All)
            {
                string bucket = Telemetry.Bucket(profile.type.ToString());
                if (!used.ContainsKey(bucket)) missing.Add(profile.displayName);
            }

            if (missing.Count >= OperationCatalog.All.Count - 3)
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Note,
                    headline = $"{OperationCatalog.All.Count - missing.Count} of " +
                               $"{OperationCatalog.All.Count} military operations were used",
                    detail = "Not a fault on its own — one session cannot exercise everything. " +
                             "Worth watching across several: an option nobody ever reaches is " +
                             "either badly priced, badly explained, or unreachable. Unused: " +
                             string.Join(", ", missing.ToArray(), 0, Math.Min(8, missing.Count)) +
                             (missing.Count > 8 ? $" and {missing.Count - 8} more." : ".")
                });

            if (used.Count == 0)
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Warning,
                    headline = "No player actions were recorded at all",
                    detail = "Either the operator spent nothing all session, or the recorder is not " +
                             "hooked up to the spends. Check the second one first."
                });
        }

        // ---------- choices that never work ----------

        static void CheckFutileActions(List<TelemetryFinding> findings)
        {
            var attempts = new Dictionary<string, int>();
            var refusals = new Dictionary<string, int>();

            foreach (var e in Telemetry.Events)
            {
                if (e.kind != TelemetryKind.PlayerAction) continue;
                attempts.TryGetValue(e.action, out int a);
                attempts[e.action] = a + 1;
                if (e.success) continue;
                refusals.TryGetValue(e.action, out int r);
                refusals[e.action] = r + 1;
            }

            foreach (var pair in refusals)
            {
                if (pair.Value < 4) continue;
                attempts.TryGetValue(pair.Key, out int tried);
                if (pair.Value < tried * 0.6f) continue;

                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Warning,
                    headline = $"'{pair.Key}' was refused {pair.Value} times out of {tried}",
                    detail = "An action the operator keeps reaching for and keeps being denied is " +
                             "either mispriced or badly explained. The interface should say why " +
                             "before it is pressed, not after."
                });
            }
        }

        // ---------- is the world doing anything ----------

        static void CheckWorldActivity(List<TelemetryFinding> findings)
        {
            var aiActions = Telemetry.CountsFor(TelemetryKind.AiAction, playerOnly: false);
            var months = Telemetry.Series("cp");

            int total = 0;
            foreach (var pair in aiActions) total += pair.Value;

            if (total == 0)
            {
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Suspect,
                    headline = "No foreign government did anything all session",
                    detail = "Fifteen states took zero recorded actions. Either the world is inert " +
                             "or the recorder is not hooked into AI execution."
                });
                return;
            }

            if (aiActions.Count <= 2)
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Warning,
                    headline = $"The world only ever did {aiActions.Count} kind(s) of thing",
                    detail = $"{total} foreign actions across {months.Count} months, but only " +
                             $"{string.Join(", ", new List<string>(aiActions.Keys).ToArray())}. " +
                             "A world with one move is a world the player learns once."
                });
        }

        // ---------- crises ----------

        static void CheckCrises(List<TelemetryFinding> findings)
        {
            int fired = 0, resolved = 0, lapsed = 0;
            foreach (var e in Telemetry.Events)
            {
                if (e.kind != TelemetryKind.Crisis) continue;
                if (e.action == "FIRED") fired++;
                else if (e.action == "RESOLVED") resolved++;
                else if (e.action == "LAPSED") lapsed++;
            }

            if (fired == 0) return;

            if (lapsed >= 3 && lapsed > resolved)
                findings.Add(new TelemetryFinding
                {
                    severity = FindingSeverity.Warning,
                    headline = $"{lapsed} of {fired} crises were left unanswered",
                    detail = "Drifting is a legitimate choice, but if it is the *usual* one the " +
                             "options are not worth the command capacity, or the Crisis Turn is " +
                             "arriving somewhere the operator does not look."
                });
        }

        // ---------- presentation ----------

        public static string Report(GameState state)
        {
            var findings = Findings(state);
            var sb = new StringBuilder();

            sb.AppendLine($"SESSION REVIEW — seed {Telemetry.SessionSeed}, " +
                          $"{Telemetry.SessionCountryId}, {Telemetry.Series("cp").Count} months");
            sb.AppendLine();

            foreach (var finding in findings)
            {
                sb.AppendLine(finding.ToString());
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}

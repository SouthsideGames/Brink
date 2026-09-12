using System;
using System.Collections.Generic;
using System.Linq;
using Brink.Core;
using Brink.Data;

/// <summary>
/// Long-run distribution probe for the core stability repair.
///
/// Build with `C:\Temp\brink-audit\build-stability.sh` (same recipe as
/// `Tools/Wars.cs`: runtime sources + this file, compiled with Unity's Roslyn
/// against its UnityEngine reference set). Run:
///
///   dotnet stability.exe [months=360] [seed seed ...]
///
/// Prints, per seed, the figures the repair brief asks for — wars initiated,
/// obligation entries (defensive / offensive), simultaneous fronts,
/// confrontation-months, war duration, how many states saw no war / limited
/// war / repeated war, the player's treatment against the AI's — and the
/// world-health figures the ratchet audit measured: sanctions, debt/GDP,
/// restructurings, market health, living standards, approval, unrest, coups,
/// insurgencies, ruin and recovery, strategy churn. **The goal is a
/// distribution, not a number**: a healthy run has materially different
/// histories between states and between seeds.
/// </summary>
public static class Stability
{
    public static void Main(string[] args)
    {
        GameLog.MirrorToUnityConsole = false;
        int months = args.Length > 0 && int.TryParse(args[0], out var m) ? m : 360;
        var seeds = new List<int>();
        for (int i = 1; i < args.Length; i++) if (int.TryParse(args[i], out var s)) seeds.Add(s);
        if (seeds.Count == 0) seeds.AddRange(new[] { 4242, 9090, 8686, 5171, 6301, 2468, 1212, 3131 });

        var summaries = new List<string>();
        foreach (int seed in seeds) summaries.Add(Run(seed, months));

        Console.WriteLine();
        Console.WriteLine("=== SUMMARY (one line per seed) ===");
        Console.WriteLine("seed   wars  oblig(def/off)  maxFronts  confMo/dec  medDur  none/lim/rep  plyrWars  sanct  debt%(med/max)  restr  ruined  meanIdx  meanStd  meanAppr  meanUnr  coups  insurg  churn");
        foreach (var line in summaries) Console.WriteLine(line);
    }

    static string Run(int seed, int months)
    {
        var state = WorldFactory.CreateDebugWorld(seed);
        var turns = new TurnManager(state);
        SimulationPipeline.Wire(turns, state);
        string me = state.playerCountryId;

        int confMonths = 0;
        int maxFronts = 0;
        int coups = 0, restructurings = 0, lastChron = 0, pathChanges = 0;
        var lastPath = new Dictionary<string, StrategicPath>();
        var ruinedEver = new HashSet<string>();
        var recoveredFromRuin = new HashSet<string>();

        Console.WriteLine($"\n=== seed {seed}, {months} months, player {me} ===");
        Console.WriteLine("month  wars  ruined  meanIdx  sanct  meanPress  debt%med  debt%max  unrest  appr   coups  insurg  hosted");

        for (int month = 0; month <= months; month++)
        {
            // Sample.
            int wars = 0;
            foreach (var c in state.confrontations)
                if (!c.resolved && c.escalation >= EscalationState.LimitedConflict) { wars++; confMonths++; }

            foreach (var country in state.countries)
            {
                int fronts = state.ActiveConfrontationsFor(country.id)
                    .Count(c => c.escalation >= EscalationState.LimitedConflict);
                if (fronts > maxFronts) maxFronts = fronts;

                bool ruined = country.economy.marketIndex < 20f;
                if (ruined) ruinedEver.Add(country.id);
                else if (ruinedEver.Contains(country.id)) recoveredFromRuin.Add(country.id);

                var ai = state.FindAI(country.id);
                if (ai != null)
                {
                    if (lastPath.TryGetValue(country.id, out var previous) && previous != ai.path) pathChanges++;
                    lastPath[country.id] = ai.path;
                }
            }

            for (int i = lastChron; i < state.chronicle.Count; i++)
            {
                string text = state.chronicle[i].text?.ToUpperInvariant() ?? "";
                if (text.Contains("COUP")) coups++;
                if (text.Contains("RESTRUCTURES ITS DEBT")) restructurings++;
            }
            lastChron = state.chronicle.Count;

            if (month % 60 == 0)
            {
                var debts = state.countries.Select(c => c.economy.gdp > 1f ? 100f * c.fiscal.sovereignDebt / c.economy.gdp : 0f).OrderBy(x => x).ToList();
                float idx = state.countries.Average(c => c.economy.marketIndex);
                float press = state.countries.Average(c => EconomySystem.SanctionPressureOn(state, c.id));
                float unrest = state.countries.Average(c => c.socialUnrest);
                float appr = state.countries.Average(c => c.governmentApproval);
                float hosted = state.countries.Sum(c => c.displacement.hosted);
                int ruinedNow = state.countries.Count(c => c.economy.marketIndex < 20f);
                Console.WriteLine($"{month,5}  {wars,4}  {ruinedNow,6}  {idx,7:F1}  {state.sanctions.Count,5}  {press,9:F2}  {Median(debts),8:F0}  {debts.Last(),8:F0}  {unrest,6:F1}  {appr,5:F1}  {coups,5}  {state.insurgencies.Count,6}  {hosted,6:F0}");
            }

            if (month < months) turns.EndMonth();
        }

        // War accounting over the whole run.
        var fought = state.confrontations.Where(c => c.escalation >= EscalationState.LimitedConflict).ToList();
        int aiWars = fought.Count(c => !c.Involves(me));
        int initiated = fought.Count(c => !c.IsObligationEntry);
        int obligationEntries = fought.Count(c => c.IsObligationEntry);
        int offensiveEntries = fought.Count(c => c.IsObligationEntry && !AllianceSystem.IsDefensiveEntry(state, c));
        var durations = fought.Where(c => c.resolved).Select(c => (float)c.monthsActive).OrderBy(x => x).ToList();
        float medianDuration = durations.Count == 0 ? 0f : Median(durations);

        var warsPerState = state.countries.ToDictionary(c => c.id, c => fought.Count(f => f.Involves(c.id)));
        int none = warsPerState.Values.Count(v => v == 0);
        int limited = warsPerState.Values.Count(v => v == 1 || v == 2);
        int repeated = warsPerState.Values.Count(v => v >= 3);
        int playerWars = warsPerState[me];
        float aiMeanWars = (float)warsPerState.Where(kv => kv.Key != me).Average(kv => kv.Value);

        var debtsEnd = state.countries.Select(c => c.economy.gdp > 1f ? 100f * c.fiscal.sovereignDebt / c.economy.gdp : 0f).OrderBy(x => x).ToList();
        int ruinedEnd = state.countries.Count(c => c.economy.marketIndex < 20f);

        var byCause = state.sanctions.GroupBy(s => string.IsNullOrEmpty(s.cause) ? "?" : s.cause)
            .Select(g => $"{g.Key} {g.Count()}");
        Console.WriteLine($"standing sanctions by cause: {string.Join(", ", byCause)}");
        if (Environment.GetEnvironmentVariable("BRINK_SANCTION_DETAIL") == "1")
        {
            Console.WriteLine("  sender->target  cause        months  rel  align  trust  memory  threat  war  margin  mandated");
            foreach (var s in state.sanctions.OrderBy(s => s.senderId))
            {
                var r = state.FindRelationship(s.senderId, s.targetId);
                bool war = ConfrontationSystem.ExistingBetween(state, s.senderId, s.targetId) is Confrontation c
                           && c.escalation >= EscalationState.LimitedConflict;
                Console.WriteLine($"  {s.senderId,-4}->{s.targetId,-4}  {s.cause,-11}  {s.monthsActive,5}  {r?.relations ?? 0f,4:F0}  {r?.strategicAlignment ?? 0f,5:F0}  {r?.trust ?? 0f,5:F0}  {r?.memoryWeight ?? 0f,6:F1}  {r?.ThreatPerceivedBy(s.senderId) ?? 0f,6:F0}  {(war ? "Y" : "-"),3}  {EconomySystem.ReliefMargin(state, s.senderId, s.targetId),6:F0}  {(CouncilSystem.SanctionsMandated(state, s.targetId) ? "Y" : "-")}");
            }
        }
        int aiRoot = fought.Count(c => !c.Involves(me) && !c.IsObligationEntry);
        int aiSatellite = fought.Count(c => !c.Involves(me) && c.IsObligationEntry);
        Console.WriteLine($"AI-vs-AI: root wars {aiRoot}, satellite fronts {aiSatellite}");
        Console.WriteLine($"wars fought {fought.Count} (AI-vs-AI {aiWars}): initiated {initiated}, obligation entries {obligationEntries} ({offensiveEntries} offensive)");
        Console.WriteLine($"max simultaneous fronts {maxFronts}; confrontation-months {confMonths} ({confMonths * 120f / months:F0}/decade); median war {medianDuration:F0} months");
        Console.WriteLine($"states with no war {none}, limited (1-2) {limited}, repeated (3+) {repeated}; player {playerWars} wars vs AI mean {aiMeanWars:F1}");
        Console.WriteLine($"ruined at end {ruinedEnd}; ruined ever {ruinedEver.Count}; recovered from ruin {recoveredFromRuin.Count}; restructurings {restructurings}; coups {coups}; strategy changes {pathChanges}");
        Console.WriteLine("per-state end state: id  idx  std  appr  unrest  debt%  wars  sanctionsOn");
        foreach (var c in state.countries.OrderByDescending(c => c.economy.marketIndex))
        {
            int sanctionsOn = state.sanctions.Count(s => s.targetId == c.id);
            float debt = c.economy.gdp > 1f ? 100f * c.fiscal.sovereignDebt / c.economy.gdp : 0f;
            Console.WriteLine($"  {c.id,-4} {c.economy.marketIndex,5:F0} {c.livingStandards,5:F0} {c.governmentApproval,5:F0} {c.socialUnrest,6:F0} {debt,6:F0} {warsPerState[c.id],5} {sanctionsOn,4}{(c.id == me ? "  <- player" : "")}");
        }

        float meanIdx = state.countries.Average(c => c.economy.marketIndex);
        float meanStd = state.countries.Average(c => c.livingStandards);
        float meanAppr = state.countries.Average(c => c.governmentApproval);
        float meanUnr = state.countries.Average(c => c.socialUnrest);
        return $"{seed,-5} {fought.Count,5}  {obligationEntries,3}({obligationEntries - offensiveEntries}/{offensiveEntries})        {maxFronts,4}      {confMonths * 120f / months,6:F0}   {medianDuration,5:F0}   {none,2}/{limited,2}/{repeated,2}       {playerWars,3}     {state.sanctions.Count,4}   {Median(debtsEnd),4:F0}/{debtsEnd.Last(),-5:F0}  {restructurings,4}   {ruinedEnd,4}    {meanIdx,5:F1}   {meanStd,5:F1}   {meanAppr,5:F1}    {meanUnr,5:F1}  {coups,4}   {state.insurgencies.Count,4}   {pathChanges,4}";
    }

    static float Median(List<float> sorted)
    {
        if (sorted.Count == 0) return 0f;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2f;
    }
}

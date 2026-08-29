using System;
using Brink.Core;
using Brink.Data;

public static class Wars
{
    public static void Main(string[] args)
    {
        GameLog.MirrorToUnityConsole = false;
        int[] seeds = { 4242, 9090, 8686, 1212, 3390, 5171, 6301, 2468 };
        int total = 0;
        Console.WriteLine("seed   aiWars(>=LimitedConflict) per 30y");
        foreach (int seed in seeds)
        {
            int n = Count(seed, 360);
            total += n;
            Console.WriteLine($"{seed,5}  {n,5}");
        }
        Console.WriteLine($"\nmean {total / (float)seeds.Length:F2} wars per 30-year world "
            + $"({total} across {seeds.Length} worlds)");
        Console.WriteLine($"the test's floor is 3 across 2 worlds = 1.50/world");
    }

    static int Count(int seed, int months)
    {
        var state = WorldFactory.CreateDebugWorld(seed);
        var turns = new TurnManager(state);
        SimulationPipeline.Wire(turns, state);
        for (int m = 0; m < months; m++) turns.EndMonth();

        int wars = 0;
        foreach (var c in state.confrontations)
        {
            if (c.Involves(state.playerCountryId)) continue;
            if (c.escalation >= EscalationState.LimitedConflict) wars++;
        }
        return wars;
    }
}

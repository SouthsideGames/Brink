using System;
using Brink.Core;
using Brink.Data;

public static class Recovery
{
    public static void Main(string[] args)
    {
        GameLog.MirrorToUnityConsole = false;
        Console.WriteLine("month  ruined  meanIdx  sanctions  wars  meanPressure");
        var state = WorldFactory.CreateDebugWorld(5171);
        var turns = new TurnManager(state);
        SimulationPipeline.Wire(turns, state);

        for (int m = 0; m <= 480; m++)
        {
            if (m % 60 == 0)
            {
                int ruined = 0; float idx = 0f, pressure = 0f;
                foreach (var c in state.countries)
                {
                    if (c.economy.marketIndex < 20f) ruined++;
                    idx += c.economy.marketIndex;
                    pressure += EconomySystem.SanctionPressureOn(state, c.id);
                }
                int wars = 0;
                foreach (var cf in state.confrontations)
                    if (!cf.resolved && cf.escalation >= EscalationState.LimitedConflict) wars++;

                Console.WriteLine($"{m,5}  {ruined,6}  {idx / state.countries.Count,7:F1}"
                    + $"  {state.sanctions.Count,9}  {wars,4}  {pressure / state.countries.Count,12:F2}");
            }
            turns.EndMonth();
        }
    }
}

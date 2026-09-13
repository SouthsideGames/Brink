using System;
using System.Collections.Generic;
using System.Text;
using Brink.Data;

namespace Brink.Core
{
    /// <summary>
    /// Interprets doctrine revisions as historical turns. Uses only the current
    /// strategic plan and Chronicle evidence; no hidden continuity score exists.
    /// </summary>
    public static class StrategicReversalSystem
    {
        public sealed class Reading
        {
            public string headline;
            public string meaning;
            public int revisionCount;
        }

        public static Reading Read(GameState state)
        {
            var strategy = state?.mandate?.strategy;
            if (strategy == null || !strategy.doctrineChosen || strategy.revisionCount <= 0) return null;

            int publicPressure = 0;
            foreach (var entry in state.chronicle)
                if (entry != null && entry.countryId == state.playerCountryId && entry.category == ChronicleCategory.Political)
                    publicPressure++;

            string scale = strategy.revisionCount >= 3 ? "STRATEGIC BREAK" : strategy.revisionCount == 2 ? "SECOND TURN" : "COURSE REVISION";
            string meaning = "The operator has revised standing strategy " + strategy.revisionCount + " time(s). ";
            meaning += publicPressure >= 3
                ? "The change sits against an already crowded political record and will read as part of a larger period of adjustment."
                : "The change is visible as adaptation rather than proof that earlier policy never existed.";
            return new Reading { headline = scale, meaning = meaning, revisionCount = strategy.revisionCount };
        }

        public static string Render(GameState state)
        {
            var reading = Read(state);
            if (reading == null) return "STRATEGIC CONTINUITY — NO DOCTRINAL REVERSAL RECORDED IN THIS POSTING.";
            return new StringBuilder(reading.headline).Append(" — ").AppendLine(StrategicEraSystem.Current(state)?.name ?? "CURRENT ERA")
                .Append(reading.meaning).AppendLine()
                .Append("REVERSAL DOES NOT ERASE THE OLD COURSE; IT BECOMES PART OF THE RECORD.").ToString();
        }
    }
}

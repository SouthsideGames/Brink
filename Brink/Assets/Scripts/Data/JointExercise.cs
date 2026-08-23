using System;

namespace Brink.Data
{
    /// <summary>
    /// Depth of participation in a joint exercise (GDD §15.3). Greater depth
    /// buys more training and trust at the cost of greater exposure — the
    /// partner learns more about how we actually operate.
    /// </summary>
    public enum ExerciseScale
    {
        Limited,   // observers and staff talks
        Standard,  // formed units, scripted scenarios
        Full       // full-spectrum, free-play, real doctrine on display
    }

    /// <summary>What the exercise trains.</summary>
    public enum ExerciseFocus
    {
        Ground,
        Air,
        Naval,
        Combined
    }

    /// <summary>
    /// An after-action record. Losing still teaches — and can expose a weakness
    /// before a real conflict does (GDD §15.3).
    /// </summary>
    [Serializable]
    public class ExerciseRecord
    {
        public GameDate date;
        public string partnerId;
        public ExerciseScale scale;
        public ExerciseFocus focus;

        public bool weOutperformed;
        public float readinessGained;
        public float interoperabilityGained;
        public float exposureIncurred;
        public string lesson;
    }
}

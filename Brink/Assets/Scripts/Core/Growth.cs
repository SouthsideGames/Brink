using System;

namespace Brink.Core
{
    /// <summary>
    /// Shared growth curve for national capability.
    ///
    /// Capability gains taper sharply as a pillar approaches its ceiling, so a
    /// long save keeps meaningful headroom instead of every power pinning at
    /// 100 within a decade. Losses are never damped — decline is always fully
    /// felt.
    /// </summary>
    public static class Growth
    {
        /// <summary>Range over which gains taper to nothing near the ceiling.</summary>
        public const float TaperRange = 45f;

        /// <summary>Fraction of a nominal gain actually realized at this level.</summary>
        public static float DiminishingFactor(float current)
        {
            float headroom = (100f - current) / TaperRange;
            return headroom < 0f ? 0f : (headroom > 1f ? 1f : headroom);
        }

        /// <summary>Apply a capability change with diminishing returns on gains.</summary>
        public static float Apply(float current, float amount)
        {
            if (amount > 0f) amount *= DiminishingFactor(current);
            float result = current + amount;
            return result < 0f ? 0f : (result > 100f ? 100f : result);
        }
    }
}

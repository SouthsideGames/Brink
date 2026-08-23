namespace Brink.Core
{
    /// <summary>
    /// Stable string hashing for random seeds.
    ///
    /// Simulation seeds mix in a country or target id so that two actors drawing
    /// in the same month get independent streams. Those seeds were built from
    /// <c>string.GetHashCode()</c>, which .NET does not guarantee to be stable
    /// across processes — it happens to be on Unity's current backends, which is
    /// why nothing has broken, and every determinism test runs both halves in one
    /// process so none of them could ever notice.
    ///
    /// If that guarantee ever lapses, a loaded save would silently draw from
    /// different streams than the session that wrote it: the world would fork
    /// from the save and nothing would throw. This is a fixed, in-house FNV-1a so
    /// the seed depends only on the string's characters, forever.
    ///
    /// <b>Never replace this implementation.</b> Changing it changes every seed
    /// in every existing save. `HashTests` pins its output for that reason.
    /// </summary>
    public static class Hash
    {
        const int FnvOffsetBasis = unchecked((int)2166136261);
        const int FnvPrime = 16777619;

        /// <summary>Deterministic 32-bit hash of a string, stable across runs and platforms.</summary>
        public static int Of(string value)
        {
            if (string.IsNullOrEmpty(value)) return 0;

            int hash = FnvOffsetBasis;
            unchecked
            {
                for (int i = 0; i < value.Length; i++)
                {
                    hash ^= value[i];
                    hash *= FnvPrime;
                }
            }
            return hash;
        }
    }
}

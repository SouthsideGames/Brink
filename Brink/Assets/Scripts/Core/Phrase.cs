using System;
using System.Text;

namespace Brink.Core
{
    /// <summary>
    /// Turning an enum name into something a person would read.
    ///
    /// C# enum values are written for the compiler — `LimitedConflict`,
    /// `TerritorialConcession`, `IntelligencePolitical` — and interpolating one
    /// straight into player-facing prose puts a variable name on the operator's
    /// screen. "Confrontation active — TotalWar" is the tell: everything around
    /// it is written English and one word is code.
    ///
    /// This exists so the fix is a call rather than a judgement. Any enum
    /// interpolated into a notification, chronicle entry or view goes through
    /// here; anything written into `GameLog` deliberately does not, because logs
    /// are read by developers and a raw name is easier to grep for.
    /// </summary>
    public static class Phrase
    {
        /// <summary>
        /// `TotalWar` → `Total War`, `IntelligencePolitical` → `Intelligence
        /// Political`. Acronyms are left intact: `ISRProgramme` does not become
        /// `I S R Programme`.
        /// </summary>
        public static string Of(Enum value)
            => value == null ? string.Empty : Split(value.ToString());

        /// <summary>Upper case, for headers and labels.</summary>
        public static string Caps(Enum value) => Of(value).ToUpperInvariant();

        static string Split(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var builder = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char current = name[i];

                // A break belongs before an upper-case letter that starts a new
                // word: either the previous character was lower case, or this is
                // the last capital of a run and the next character is lower case
                // (so `ISRLift` splits as `ISR Lift`, not `I S R Lift`).
                bool startsWord = i > 0 && char.IsUpper(current)
                                  && (!char.IsUpper(name[i - 1])
                                      || (i + 1 < name.Length && char.IsLower(name[i + 1])));

                if (startsWord) builder.Append(' ');
                builder.Append(current);
            }

            return builder.ToString();
        }
    }
}

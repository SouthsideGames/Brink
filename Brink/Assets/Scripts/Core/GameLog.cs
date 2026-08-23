using System;
using System.Collections.Generic;

namespace Brink.Core
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public struct LogEntry
    {
        public LogLevel level;
        public string category;
        public string message;
    }

    /// <summary>
    /// Central game logger (GDD Phase 0: debug/logging). Keeps a ring buffer for
    /// the in-game debug console and mirrors to Unity's console when available.
    /// Plain C# so edit-mode tests and headless simulation can use it freely.
    /// </summary>
    public static class GameLog
    {
        public const int MaxEntries = 500;

        static readonly List<LogEntry> entries = new List<LogEntry>();
        public static IReadOnlyList<LogEntry> Entries => entries;

        public static event Action<LogEntry> OnLog;

        /// <summary>Mirror to UnityEngine.Debug. Disable in pure headless test runs if noisy.</summary>
        public static bool MirrorToUnityConsole = true;

        public static void Debug(string category, string message) => Write(LogLevel.Debug, category, message);
        public static void Info(string category, string message) => Write(LogLevel.Info, category, message);
        public static void Warn(string category, string message) => Write(LogLevel.Warning, category, message);
        public static void Error(string category, string message) => Write(LogLevel.Error, category, message);

        public static void Clear() => entries.Clear();

        static void Write(LogLevel level, string category, string message)
        {
            var entry = new LogEntry { level = level, category = category, message = message };
            entries.Add(entry);
            if (entries.Count > MaxEntries)
                entries.RemoveRange(0, entries.Count - MaxEntries);

            if (MirrorToUnityConsole)
            {
                string line = $"[{category}] {message}";
                switch (level)
                {
                    case LogLevel.Warning: UnityEngine.Debug.LogWarning(line); break;
                    case LogLevel.Error: UnityEngine.Debug.LogError(line); break;
                    default: UnityEngine.Debug.Log(line); break;
                }
            }

            OnLog?.Invoke(entry);
        }
    }
}

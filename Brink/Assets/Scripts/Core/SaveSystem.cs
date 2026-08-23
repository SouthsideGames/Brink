using System;
using System.IO;
using Brink.Data;
using UnityEngine;

namespace Brink.Core
{
    /// <summary>
    /// Save/load for the persistent simulation (GDD §30). JSON via JsonUtility,
    /// written atomically (temp file then replace) so a crash mid-write cannot
    /// corrupt the previous save. Slot-based; slot 0 is the autosave.
    /// </summary>
    public static class SaveSystem
    {
        public const int CurrentSaveVersion = 6;

        /// <summary>Override for tests; null = Application.persistentDataPath/saves.</summary>
        public static string SaveDirectoryOverride;

        public static string SaveDirectory =>
            SaveDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "saves");

        public static string SlotPath(int slot) => Path.Combine(SaveDirectory, $"slot_{slot}.json");

        public static string ToJson(GameState state) => JsonUtility.ToJson(state, prettyPrint: true);

        public static GameState FromJson(string json)
        {
            var state = JsonUtility.FromJson<GameState>(json);
            if (state == null)
                throw new InvalidDataException("Save data could not be parsed.");

            // Walk older saves forward to the current schema. Throws rather than
            // loading a mismatched world (see SaveMigration).
            return SaveMigration.Migrate(state);
        }

        public static void Save(GameState state, int slot = 0)
        {
            Directory.CreateDirectory(SaveDirectory);
            string finalPath = SlotPath(slot);
            string tempPath = finalPath + ".tmp";

            File.WriteAllText(tempPath, ToJson(state));
            if (File.Exists(finalPath))
                File.Delete(finalPath);
            File.Move(tempPath, finalPath);

            GameLog.Info("SAVE", $"State saved to slot {slot} ({state.date.DisplayString}).");
        }

        public static bool SaveExists(int slot = 0) => File.Exists(SlotPath(slot));

        public static GameState Load(int slot = 0)
        {
            string path = SlotPath(slot);
            if (!File.Exists(path))
                throw new FileNotFoundException($"No save in slot {slot}.", path);

            var state = FromJson(File.ReadAllText(path));
            GameLog.Info("SAVE", $"State loaded from slot {slot} ({state.date.DisplayString}).");
            return state;
        }

        /// <summary>
        /// Highest addressable save slot. A full reset must clear every one of
        /// them: erasing only the autosave left a manual save loadable, so the
        /// "erased" nation, world history and Strategist progression could be
        /// restored in two clicks — which GDD §5.1 requires to be impossible.
        /// </summary>
        public const int MaxSlot = 8;

        /// <summary>Erase every save. Used only by full reset (GDD §5.1).</summary>
        public static void DeleteAll()
        {
            for (int slot = 0; slot <= MaxSlot; slot++) Delete(slot);
        }

        public static void Delete(int slot = 0)
        {
            string path = SlotPath(slot);
            if (File.Exists(path))
            {
                File.Delete(path);
                GameLog.Info("SAVE", $"Slot {slot} deleted.");
            }
        }
    }
}

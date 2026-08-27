using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Brink.Data;

namespace Brink.Core
{
    [Serializable]
    public class CareerPosting
    {
        public string countryId;
        public string countryName;
        public int seed;
        public string worldSize;
        public string difficulty;
        public string mandateTitle;
        public string verdict;          // Pending / Fulfilled / Held / Failed
        public int met, total;
        public int yearsServed;
        public float careerGrade;       // mean annual grade, F=0 … S=5
        public int warsWon, warsLost;
        public string closedOn = "";    // in-game date of the last entry
    }

    [Serializable]
    public class CareerFile
    {
        public int version = 1;
        public List<CareerPosting> postings = new List<CareerPosting>();
    }

    /// <summary>
    /// The operator's record across saves (2026-08). Every posting used to end
    /// with the save and nothing else: a second game had no memory of the first.
    /// The career file lives beside the save slots and is written at the two
    /// moments a posting is judged — the ten-year mandate verdict and the
    /// forty-year tenure review — and refreshed on every autosave so an
    /// abandoned posting still shows what it was.
    ///
    /// It is a record, not progression: nothing in it changes a new game.
    /// </summary>
    public static class CareerRecord
    {
        public static string FilePath => Path.Combine(SaveSystem.SaveDirectory, "career.json");

        public static CareerFile Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new CareerFile();
                var file = JsonUtility.FromJson<CareerFile>(File.ReadAllText(FilePath));
                return file ?? new CareerFile();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[CAREER] Unreadable career file: {e.Message}");
                return new CareerFile();
            }
        }

        static void Save(CareerFile file)
        {
            Directory.CreateDirectory(SaveSystem.SaveDirectory);
            File.WriteAllText(FilePath, JsonUtility.ToJson(file, prettyPrint: true));
        }

        /// <summary>Write the posting this state describes into the record, replacing any earlier entry for the same posting.</summary>
        /// <summary>
        /// Recording is on for a player and off for the headless suites: the
        /// edit-mode tests play thousands of decades and would fill the real
        /// career file with bots. A test that wants the record sets
        /// <see cref="SaveSystem.SaveDirectoryOverride"/>, which opts it in.
        /// </summary>
        public static bool Enabled
        {
            get
            {
                if (SaveSystem.SaveDirectoryOverride != null) return true;
                // Outside a real Unity player (the headless harness) the engine
                // call itself is unavailable; there is no career to keep there.
                // The call sits in its own method: the failure is raised when
                // that method is compiled, which happens inside this try.
                try { return !InBatchMode(); }
                catch (Exception) { return false; }
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static bool InBatchMode() => Application.isBatchMode;

        public static CareerPosting Record(GameState state)
        {
            var player = state.PlayerCountry;
            if (player == null || !Enabled) return null;
            try { return RecordUnchecked(state); }
            catch (Exception e)
            {
                Debug.LogWarning($"[CAREER] Could not write the career file: {e.Message}");
                return null;
            }
        }

        static CareerPosting RecordUnchecked(GameState state)
        {
            var player = state.PlayerCountry;

            float gradeSum = 0f;
            foreach (var evaluation in state.evaluations) gradeSum += (int)evaluation.grade;

            var entry = new CareerPosting
            {
                countryId = player.id,
                countryName = player.displayName,
                seed = state.rngSeed,
                worldSize = state.worldSize.ToString(),
                difficulty = state.difficulty.ToString(),
                mandateTitle = state.mandate?.title ?? "",
                verdict = state.mandateRecord?.verdict.ToString() ?? "Pending",
                met = state.mandateRecord?.met ?? MandateSystem.MetCount(state),
                total = state.mandate?.objectives.Count ?? 0,
                yearsServed = state.evaluations.Count,
                careerGrade = state.evaluations.Count > 0 ? gradeSum / state.evaluations.Count : 0f,
                warsWon = player.warsWon,
                warsLost = player.warsLost,
                closedOn = state.date.DisplayString
            };

            var file = Load();
            int index = file.postings.FindIndex(p => p.seed == state.rngSeed && p.countryId == player.id);
            if (index >= 0) file.postings[index] = entry; else file.postings.Add(entry);
            Save(file);
            return entry;
        }

        /// <summary>Terminal-voice block for the STRATEGIST panel.</summary>
        public static string StatusText(GameState current)
        {
            var file = Load();
            if (file.postings.Count == 0) return "NO PRIOR POSTINGS ON FILE.";
            var sb = new System.Text.StringBuilder();
            int shown = 0;
            for (int i = file.postings.Count - 1; i >= 0 && shown < 6; i--, shown++)
            {
                var p = file.postings[i];
                bool thisOne = current != null && p.seed == current.rngSeed && p.countryId == current.playerCountryId;
                sb.AppendLine($"{(thisOne ? "► " : "  ")}{p.countryName.ToUpperInvariant(),-16} {p.yearsServed,2} yr  " +
                              $"grade {GradeLetter(p.careerGrade)}  mandate {p.verdict.ToUpperInvariant()} ({p.met}/{p.total})  " +
                              $"wars {p.warsWon}–{p.warsLost}  {p.difficulty.ToUpperInvariant()}");
            }
            if (file.postings.Count > shown) sb.AppendLine($"  … and {file.postings.Count - shown} earlier.");
            return sb.ToString().TrimEnd();
        }

        static string GradeLetter(float mean)
            => mean >= 4.5f ? "S" : mean >= 3.5f ? "A" : mean >= 2.5f ? "B" : mean >= 1.5f ? "C" : mean >= 0.5f ? "D" : "F";
    }
}

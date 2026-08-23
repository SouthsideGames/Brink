using System;
using System.IO;
using Brink.Data;
using UnityEngine;

namespace Brink.Core
{
    /// <summary>
    /// Writing a session to disk, and the setting that decides whether one is
    /// recorded at all.
    ///
    /// Split from <see cref="Telemetry"/> so the recorder and its analysis stay
    /// plain C# with no `UnityEngine` file or preference dependency — the same
    /// rule the rest of the simulation layer follows, and what lets a harness run
    /// be analysed in an edit-mode test exactly like a real session.
    ///
    /// **Local only.** This writes a file next to the saves and does nothing
    /// else. There is no upload, no identifier, no third-party SDK and no network
    /// call anywhere in this feature. The game is offline-first and sold once;
    /// transmitting play data would be an outward-facing act this project has not
    /// asked anyone's permission for. If that ever changes it must be an explicit,
    /// informed, opt-in decision — not a quiet addition here.
    /// </summary>
    public static class TelemetryFile
    {
        const string EnabledKey = "brink.telemetry.enabled";

        public static string Directory =>
            Path.Combine(Application.persistentDataPath, "sessions");

        /// <summary>
        /// Whether sessions are recorded. Defaults **on in a development build**
        /// and **off in a player build**, following the same reasoning that gated
        /// the SYSTEM debug console out of shipping builds.
        /// </summary>
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(EnabledKey, Debug.isDebugBuild ? 1 : 0) == 1;
            set
            {
                PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Telemetry.Enabled = value;
            }
        }

        /// <summary>Called once at boot, before any state is attached.</summary>
        public static void Initialize() => Telemetry.Enabled = Enabled;

        /// <summary>
        /// Write the session and its review. Returns the path, or empty on
        /// failure — a telemetry problem must never interrupt play.
        /// </summary>
        public static string Write(GameState state)
        {
            if (state == null) return "";

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                // Named from the world rather than the wall clock so two dumps of
                // the same session overwrite rather than accumulating, and so the
                // file name identifies the run it can be replayed from.
                string name = $"session_{Telemetry.SessionSeed}_{Telemetry.SessionCountryId}.txt";
                string path = Path.Combine(Directory, name);

                string contents = TelemetryAnalysis.Report(state)
                                  + Environment.NewLine
                                  + "---- SESSION LOG ----" + Environment.NewLine
                                  + Telemetry.ToText();

                File.WriteAllText(path, contents);
                GameLog.Info("TELEMETRY", $"Session written to {path}");
                return path;
            }
            catch (Exception e)
            {
                GameLog.Error("TELEMETRY", $"Could not write session: {e.Message}");
                return "";
            }
        }
    }
}

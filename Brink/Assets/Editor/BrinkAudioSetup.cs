using System.IO;
using Brink.Audio;
using UnityEditor;
using UnityEngine;

namespace Brink.EditorTools
{
    /// <summary>
    /// Builds the audio library asset and wires it to the imported clips.
    ///
    /// **In code, like `BrinkBuildSetup`, and for the same reason.** A hand-wired
    /// asset is a binary blob nobody can review, cannot be diffed, and is lost if
    /// the project is reset. This is a menu item whose mappings can be read,
    /// argued with, and re-run.
    ///
    /// Clip paths appear *here and nowhere else*. This is the only file in the
    /// project permitted to name an audio file; everything downstream goes
    /// through <see cref="SfxId"/> and <see cref="MusicState"/>.
    /// </summary>
    public static class BrinkAudioSetup
    {
        const string AudioRoot = "Assets/Audio";
        const string LibraryFolder = "Assets/Resources/Audio";
        const string LibraryPath = LibraryFolder + "/BrinkAudioLibrary.asset";

        const string Shapeforms =
            AudioRoot + "/Shapeforms Audio Free Sound Effects/Shapeforms Audio Free Sound Effects";

        // ---------- music ----------

        static readonly (MusicState state, string path, float volume)[] Music =
        {
            (MusicState.MainMenu,  AudioRoot + "/tinytunatunes_MainMenu.wav",    0.85f),
            (MusicState.Peace,     AudioRoot + "/tinytunatunes_Exploration.wav", 0.80f),
            (MusicState.Tension,   AudioRoot + "/tinytunatunes_Tension.wav",     0.85f),
            (MusicState.Crisis,    AudioRoot + "/tinytunatunes_DeepPillar.wav",  0.85f),
            (MusicState.War,       AudioRoot + "/tinytunatunes_Combat.wav",      0.85f)
        };

        /// <summary>
        /// The terminal itself. Deliberately quiet — this should register mainly
        /// when it stops.
        /// </summary>
        /// <summary>
        /// The Dystopia folder's name is mojibake, not an en-dash.
        ///
        /// The pack was zipped on macOS and unpacked with a different codepage, so
        /// what looks like "Dystopia – Ambience" is actually the three literal
        /// characters Γ Ç ô (0xCE93, 0x00C7, 0x00F4). Typing a real en-dash here
        /// compiles perfectly and finds nothing — a silent null clip and a silent
        /// game. Escaped so it cannot be "tidied up" by an editor or mangled by a
        /// file-encoding change.
        ///
        /// Renaming the folder would fix it properly, but that was explicitly out
        /// of scope for this pass.
        /// </summary>
        const string DystopiaFolder = "Dystopia ΓÇô Ambience and Drone Preview";

        const string Ambience =
            Shapeforms + "/" + DystopiaFolder + "/Audio/" +
            "AMBIENCE_UNDERGROUND_POWER_STATION_LOOP.wav";

        // ---------- sound effects ----------
        //
        // `isUi` routes chrome through the UI group so it can be balanced apart
        // from events. `maxDuration` truncates clips authored as loops.

        static readonly (SfxId id, string path, float volume, bool isUi, float maxDuration)[] Sfx =
        {
            (SfxId.UIConfirm,
                Shapeforms + "/Future UI Preview/Audio/FUI Button Beep Clean.wav",
                0.45f, true, 0f),

            (SfxId.UICancel,
                Shapeforms + "/Future UI Preview/Audio/Holographic Tap Interaction.wav",
                0.40f, true, 0f),

            (SfxId.PanelOpen,
                Shapeforms + "/Future UI Preview/Audio/Old Terminal Popup Appear Low.wav",
                0.45f, true, 0f),

            (SfxId.CommandAccepted,
                Shapeforms + "/Future UI Preview/Audio/High-Tech Gadget Activate.wav",
                0.55f, false, 0f),

            (SfxId.CommandRejected,
                Shapeforms + "/Future UI Preview/Audio/Error Triplet-5.wav",
                0.50f, false, 0f),

            (SfxId.IncomingReport,
                Shapeforms + "/Future UI Preview/Audio/Old Terminal Computing-3.wav",
                0.45f, false, 0f),

            (SfxId.AdvisoryAlert,
                Shapeforms + "/Future UI Preview/Audio/FUI Navigation Tone Stereo Flutter.wav",
                0.50f, false, 0f),

            (SfxId.PriorityAlert,
                Shapeforms + "/Future UI Preview/Audio/FUI Ping Triplet Echo.wav",
                0.60f, false, 0f),

            // Authored as a loop, so it is cut short. FLASH must be unmistakable
            // and must not run on under the panel it announced.
            (SfxId.FlashAlert,
                Shapeforms + "/Future UI Preview/Audio/Old Terminal Alarm Loop.wav",
                0.65f, false, 1.4f),

            (SfxId.IntelligenceTransmission,
                Shapeforms + "/Cassette Preview/Audio/EMF_TAPE_RECORDING_06.wav",
                0.50f, false, 2.5f),

            (SfxId.SaveComplete,
                Shapeforms + "/Floppy Disk Preview/Audio/Amiga Disk Drive Button Click-15.wav",
                0.35f, false, 0f),

            (SfxId.NegativeOutcome,
                Shapeforms + "/Glitch and Noise Preview/Audio/Electric Glitch_01.wav",
                0.50f, false, 0f),

            // A newsroom sting: information arriving at a terminal, which is the
            // whole audio perspective. The alternative candidates were weapons and
            // explosions — the battlefield the operator is explicitly not standing on.
            (SfxId.WarDeclared,
                AudioRoot + "/freesound_community-news-ting-6832.mp3",
                0.70f, false, 0f),

            // Pointer-only. `SfxPlayer` refuses to play this on a touch screen,
            // where there is no hover and firing it from a tap would double every
            // press.
            (SfxId.UIHover,
                Shapeforms + "/Future UI Preview/Audio/Holographic Interaction-32.wav",
                0.20f, true, 0f)
        };

        [MenuItem("Brink/Audio/Create or Update Audio Library")]
        public static void CreateOrUpdateLibrary()
        {
            Directory.CreateDirectory(LibraryFolder);

            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            bool created = library == null;
            if (created) library = ScriptableObject.CreateInstance<AudioLibrary>();

            library.music.Clear();
            library.sfx.Clear();

            int missing = 0;

            foreach (var (state, path, volume) in Music)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) { Debug.LogWarning($"[AUDIO] Music not found: {path}"); missing++; }
                library.music.Add(new MusicEntry { state = state, clip = clip, loop = true, volume = volume });
            }

            foreach (var (id, path, volume, isUi, maxDuration) in Sfx)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                if (clip == null) { Debug.LogWarning($"[AUDIO] SFX not found: {path}"); missing++; }
                library.sfx.Add(new SfxEntry
                {
                    id = id, clip = clip, volume = volume, isUi = isUi, maxDuration = maxDuration
                });
            }

            library.terminalAmbience = AssetDatabase.LoadAssetAtPath<AudioClip>(Ambience);
            if (library.terminalAmbience == null)
                Debug.LogWarning($"[AUDIO] Ambience not found: {Ambience}");

            if (created) AssetDatabase.CreateAsset(library, LibraryPath);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var unmapped = library.Unmapped();
            Debug.Log($"[AUDIO] Library {(created ? "created" : "updated")} at {LibraryPath}. "
                      + $"{library.music.Count} music, {library.sfx.Count} SFX, "
                      + $"{missing} file(s) not found, {unmapped.Count} id(s) with no clip.");

            if (unmapped.Count > 0)
                Debug.Log("[AUDIO] Deliberately unmapped: " + string.Join(", ", unmapped)
                          + "\n  These are known gaps, not failures — no clip in the imported "
                          + "libraries fits without defining the game's audio identity wrongly.");

            Selection.activeObject = library;
        }

        /// <summary>
        /// Report what the library currently maps, without changing anything.
        /// Mirrors `Brink → Check Android Readiness`.
        /// </summary>
        [MenuItem("Brink/Audio/Check Audio Readiness")]
        public static void CheckReadiness()
        {
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(LibraryPath);
            if (library == null)
            {
                Debug.LogWarning("[AUDIO] No library. Run Brink > Audio > Create or Update Audio Library.");
                return;
            }

            foreach (var entry in library.music)
                if (entry.clip == null) Debug.LogWarning($"[AUDIO] {entry.state} has no track.");

            if (library.mixer == null)
                Debug.Log("[AUDIO] No AudioMixer assigned. Volume is applied to the sources "
                          + "directly, which works — assign one for grouped routing.");

            Debug.Log($"[AUDIO] Ambience: "
                      + (library.terminalAmbience != null ? library.terminalAmbience.name : "NONE")
                      + $"  |  Unmapped SFX: {library.Unmapped().Count}");
        }
    }
}

using UnityEngine;

namespace Brink.Audio
{
    /// <summary>
    /// The one entry point. Gameplay talks to this and to nothing else in the
    /// audio layer.
    ///
    /// `AudioDirector.Play(SfxId.FlashAlert)` — never an AudioSource, never a
    /// fade, never a mixer parameter, never a filename. That boundary is the
    /// whole architecture: everything behind it can be rewritten (stems, ducking,
    /// pillar layers) without touching a single gameplay system.
    ///
    /// Bootstrapped from `GameBootstrap` rather than placed in a scene, because
    /// this project deliberately has no scene wiring — any scene boots the game,
    /// so audio must not depend on one either.
    /// </summary>
    public class AudioDirector : MonoBehaviour
    {
        /// <summary>Where `GameBootstrap` finds the configuration.</summary>
        public const string LibraryResourcePath = "Audio/BrinkAudioLibrary";

        static AudioDirector instance;

        /// <summary>Null in batch mode and before boot. Callers must tolerate that.</summary>
        public static AudioDirector Instance => instance;

        AudioLibrary library;
        MusicDirector music;
        SfxPlayer sfx;
        AudioSource ambience;

        public MusicState? MusicState => music != null ? music.Current : null;
        public PillarContext Context { get; private set; } = PillarContext.World;
        public bool IsCrossfading => music != null && music.IsCrossfading;
        public AudioClip CurrentMusicClip => music != null ? music.CurrentClip : null;
        public AudioLibrary Library => library;

        /// <summary>
        /// Create the audio system if it does not exist.
        ///
        /// **Returns immediately in batch mode.** The edit-mode suite runs
        /// hundreds of tests headless; instantiating AudioSources there would be
        /// pure cost and could well be the thing that makes a test hang.
        /// `GameBootstrap` guards itself the same way for the same reason.
        /// </summary>
        public static void EnsureExists()
        {
            if (instance != null || Application.isBatchMode) return;

            var library = Resources.Load<AudioLibrary>(LibraryResourcePath);
            if (library == null)
            {
                Debug.LogWarning($"[AUDIO] No library at Resources/{LibraryResourcePath}. " +
                                 "The game runs silent.");
                return;
            }

            var host = new GameObject("BrinkAudio");
            DontDestroyOnLoad(host);

            instance = host.AddComponent<AudioDirector>();
            instance.Build(library);
        }

        void Build(AudioLibrary configuration)
        {
            library = configuration;

            music = gameObject.AddComponent<MusicDirector>();
            music.Initialize(library);

            sfx = gameObject.AddComponent<SfxPlayer>();
            sfx.Initialize(library);

            ambience = CreateAmbience();

            AudioPreferences.Changed += ApplySettings;
            ApplySettings();

            if (library.terminalAmbience != null) StartAmbience();
        }

        void OnDestroy()
        {
            AudioPreferences.Changed -= ApplySettings;
            if (instance == this) instance = null;
        }

        AudioSource CreateAmbience()
        {
            var host = new GameObject("Ambience");
            host.transform.SetParent(transform, false);

            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = library.ambienceGroup;
            return source;
        }

        void StartAmbience()
        {
            ambience.clip = library.terminalAmbience;
            ambience.volume = library.ambienceVolume;
            ambience.Play();
        }

        /// <summary>
        /// Push settings to the mixer.
        ///
        /// When no mixer is assigned the levels are applied to the sources
        /// directly, so the system is fully usable before anyone opens the mixer
        /// window — a foundation that only works once an asset is wired by hand is
        /// a foundation nobody can test.
        /// </summary>
        void ApplySettings()
        {
            if (library.mixer != null)
            {
                AudioPreferences.Apply(library.mixer);
                return;
            }

            if (ambience != null)
                ambience.volume = library.ambienceVolume
                                  * AudioPreferences.EffectiveLevel(AudioPreferences.Ambience);
        }

        // ---------- what gameplay calls ----------

        /// <summary>Set the world's intensity. Safe to call repeatedly.</summary>
        public static void SetMusic(MusicState state)
        {
            if (instance == null || instance.music == null) return;
            instance.music.Play(state, instance.Context);
        }

        /// <summary>
        /// Set where the operator is looking.
        ///
        /// Tracked now, used later. Because `AudioLibrary.Resolve` ignores context,
        /// this provably cannot restart the music — which is the behaviour asked
        /// for, expressed as a property of the design rather than a promise.
        /// </summary>
        public static void SetContext(PillarContext context)
        {
            if (instance == null) return;
            if (instance.Context == context) return;

            instance.Context = context;
            if (instance.music != null && instance.music.Current.HasValue)
                instance.music.Play(instance.music.Current.Value, context);
        }

        public static void Play(SfxId id)
        {
            if (instance == null || instance.sfx == null) return;
            instance.sfx.Play(id);
        }

        /// <summary>Stop everything. Used by a full reset.</summary>
        public static void Silence()
        {
            if (instance == null) return;
            instance.music?.StopAll(immediate: true);
            instance.sfx?.StopAll();
            if (instance.ambience != null) instance.ambience.Stop();
        }

        // ---------- lifecycle ----------

        /// <summary>
        /// Mobile: stop cleanly when backgrounded and resume when returned to.
        ///
        /// Without this a streamed track can come back mid-buffer after a call or
        /// a lock screen, and the ambience loop can be left silently stopped while
        /// the system still believes it is playing.
        /// </summary>
        void OnApplicationPause(bool paused)
        {
            if (ambience == null) return;

            if (paused) ambience.Pause();
            else if (library.terminalAmbience != null && !AudioPreferences.Muted) ambience.UnPause();
        }
    }
}

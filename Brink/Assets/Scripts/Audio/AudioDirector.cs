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
            // The mixer path only counts if the mixer actually exposes the
            // parameters. It shipped with none exposed (2026-08 audit): every
            // SetFloat returned false, the code returned early, and no volume or
            // mute setting did anything. Now a mixer that cannot be driven is
            // treated as absent and the sources are scaled directly.
            if (library.mixer != null && AudioPreferences.Apply(library.mixer))
            {
                if (music != null) music.SetLevel(1f);
                if (sfx != null) sfx.SetLevel(1f);
                if (ambience != null) ambience.volume = library.ambienceVolume;
                return;
            }

            if (ambience != null)
                ambience.volume = library.ambienceVolume
                                  * AudioPreferences.EffectiveLevel(AudioPreferences.Ambience);
            if (music != null) music.SetLevel(AudioPreferences.EffectiveLevel(AudioPreferences.Music));
            if (sfx != null) sfx.SetLevel(AudioPreferences.EffectiveLevel(AudioPreferences.Sfx));
        }

        // ---------- the world, as sound ----------

        int notificationsSeen;
        int crisesSeen;
        bool everSynced;

        /// <summary>
        /// Make the audio match the game: the music the situation calls for, the
        /// loudest alert owed for traffic since the last sync, and the crisis
        /// sting when a new Crisis Turn has opened. Called by the shell after End
        /// Month and whenever the state is replaced; safe to call any time.
        /// </summary>
        public static void Sync(Brink.Data.GameState state, bool silentAlerts = false)
        {
            if (instance == null || state == null) return;
            instance.SyncTo(state, silentAlerts);
        }

        void SyncTo(Brink.Data.GameState state, bool silentAlerts)
        {
            SetMusic(AudioCues.MusicFor(state));

            int notifications = state.notifications.Count;
            int crises = state.activeCrises.Count;

            // A replaced state (new game, load) is a fresh baseline: nothing in
            // it is "new traffic", and a decade of old items must not fire.
            if (!everSynced || silentAlerts || notifications < notificationsSeen)
            {
                notificationsSeen = notifications;
                crisesSeen = crises;
                warsWonSeen = state.PlayerCountry.warsWon;
                warsLostSeen = state.PlayerCountry.warsLost;
                evaluationsSeen = state.evaluations.Count;
                verdictHeard = state.mandateRecord != null;
                everSynced = true;
                return;
            }

            // A war decided this month is an outcome before it is traffic.
            var player = state.PlayerCountry;
            if (player.warsWon > warsWonSeen) Play(SfxId.PositiveOutcome);
            else if (player.warsLost > warsLostSeen) Play(SfxId.NegativeOutcome);
            else if (state.mandateRecord != null && !verdictHeard)
                Play(state.mandateRecord.verdict == Brink.Data.MandateVerdict.Failed ? SfxId.NegativeOutcome : SfxId.PositiveOutcome);
            else if (crises > crisesSeen) Play(SfxId.CrisisStarted);
            else
            {
                var alert = AudioCues.LoudestNewAlert(state, notificationsSeen);
                if (alert != SfxId.None) Play(alert);
            }

            if (state.evaluations.Count > evaluationsSeen) Play(SfxId.AnnualEvaluation);

            notificationsSeen = notifications;
            crisesSeen = crises;
            warsWonSeen = player.warsWon;
            warsLostSeen = player.warsLost;
            evaluationsSeen = state.evaluations.Count;
            verdictHeard = state.mandateRecord != null;
        }

        int warsWonSeen, warsLostSeen, evaluationsSeen;
        bool verdictHeard;

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

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace Brink.Audio
{
    /// <summary>One music state and the track that carries it.</summary>
    [Serializable]
    public class MusicEntry
    {
        public MusicState state;
        public AudioClip clip;

        [Tooltip("Music loops by default. Clear only for a one-shot sting.")]
        public bool loop = true;

        [Range(0f, 1f)] public float volume = 1f;
    }

    /// <summary>One semantic sound and the clip that plays it. Clip may be null.</summary>
    [Serializable]
    public class SfxEntry
    {
        public SfxId id;
        public AudioClip clip;

        [Range(0f, 1f)] public float volume = 1f;

        [Tooltip("Route through the UI group rather than SFX. Chrome, not events.")]
        public bool isUi;

        [Tooltip("Seconds after which to stop. 0 plays the clip out.\n\n" +
                 "Exists because some good candidates are authored as loops — the " +
                 "terminal alarm chosen for FLASH is one — and a loop used as a " +
                 "one-shot would never stop.")]
        public float maxDuration;
    }

    /// <summary>
    /// The single place in the project where audio files are named
    /// (GDD §28 presentation).
    ///
    /// **Nothing else may reference an AudioClip.** Gameplay asks for
    /// `SfxId.FlashAlert` and this decides what that is, so re-sounding the game
    /// is an edit to one asset rather than a search across the codebase. It lives
    /// in `Resources/Audio/` because `GameBootstrap` already loads the whole UI
    /// from `Resources` and the project has deliberately no scene wiring —
    /// following that convention means the audio system needs no scene either.
    /// </summary>
    [CreateAssetMenu(fileName = "BrinkAudioLibrary", menuName = "Brink/Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        [Header("Mixer")]
        [Tooltip("Optional. Without it the system still works, controlling volume " +
                 "on the AudioSources directly — see AudioPreferences.Apply.")]
        public AudioMixer mixer;

        public AudioMixerGroup musicGroup;
        public AudioMixerGroup ambienceGroup;
        public AudioMixerGroup sfxGroup;
        public AudioMixerGroup uiGroup;

        [Header("Music")]
        public List<MusicEntry> music = new List<MusicEntry>();

        [Tooltip("Seconds. Long enough to feel deliberate, short enough that a " +
                 "war starting is not announced four seconds late.")]
        [Range(0.1f, 8f)] public float crossfadeSeconds = 2.5f;

        [Header("Ambience")]
        [Tooltip("The terminal itself. Should sit far enough back that the player " +
                 "notices it mainly when it stops.")]
        public AudioClip terminalAmbience;

        [Range(0f, 1f)] public float ambienceVolume = 0.15f;

        [Header("Sound effects")]
        public List<SfxEntry> sfx = new List<SfxEntry>();

        // ---------- lookup ----------

        /// <summary>
        /// Which track should be playing, given both halves of the audio state.
        ///
        /// **`context` is deliberately ignored today.** There is no pillar music,
        /// and inventing some by reusing tracks would be exactly the fake identity
        /// this was asked not to build. Taking the parameter now means that when
        /// pillar stems exist, this method is the only thing that changes —
        /// callers, the director and the debug panel are already passing it.
        ///
        /// It also guarantees the behaviour the brief asked for: since the answer
        /// cannot depend on context, changing panels *cannot* restart the music.
        /// </summary>
        public MusicEntry Resolve(MusicState state, PillarContext context)
        {
            foreach (var entry in music)
                if (entry.state == state) return entry;
            return null;
        }

        public SfxEntry Find(SfxId id)
        {
            foreach (var entry in sfx)
                if (entry.id == id) return entry;
            return null;
        }

        /// <summary>
        /// Ids that carry no clip. Surfaced in the debug panel so an unmapped
        /// sound reads as a known gap rather than as a broken button.
        /// </summary>
        public List<SfxId> Unmapped()
        {
            var missing = new List<SfxId>();
            foreach (SfxId id in Enum.GetValues(typeof(SfxId)))
            {
                if (id == SfxId.None) continue;
                var entry = Find(id);
                if (entry == null || entry.clip == null) missing.Add(id);
            }
            return missing;
        }
    }
}

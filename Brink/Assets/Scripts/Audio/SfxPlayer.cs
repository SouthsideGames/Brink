using System.Collections;
using UnityEngine;

namespace Brink.Audio
{
    /// <summary>
    /// A small fixed pool of AudioSources, reused.
    ///
    /// Not one source per sound: nineteen permanent sources for nineteen ids
    /// would be nineteen components initialised at boot to play a handful of
    /// clicks, and it scales with the *catalogue* rather than with how much is
    /// actually audible at once. This is a strategy game — a busy month resolving
    /// is a handful of alerts, not a firefight — so four voices is generous.
    ///
    /// Not a growing pool either: an unbounded pool hides the bug where something
    /// fires a sound every frame. Running out here means the newest sound steals
    /// the oldest voice, which is audible and therefore findable.
    /// </summary>
    public class SfxPlayer : MonoBehaviour
    {
        /// <summary>
        /// Simultaneous voices. Four covers a crisis arriving while a command
        /// resolves and the month ends, which is about as dense as this game gets.
        /// </summary>
        public const int Voices = 4;

        AudioSource[] pool;
        int next;
        AudioLibrary library;

        public void Initialize(AudioLibrary configuration)
        {
            library = configuration;
            pool = new AudioSource[Voices];

            for (int i = 0; i < Voices; i++)
            {
                var host = new GameObject($"SFX {i}");
                host.transform.SetParent(transform, false);

                var source = host.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 0f;
                pool[i] = source;
            }
        }

        /// <summary>
        /// Play a semantic sound. Silent and harmless when the id has no clip —
        /// which several deliberately do not.
        /// </summary>
        public void Play(SfxId id)
        {
            if (library == null || pool == null) return;

            var entry = library.Find(id);
            if (entry == null || entry.clip == null) return;

            // Hover is a pointer idea. On a touch screen there is no hover, and
            // firing it from a tap would double every button press.
            if (id == SfxId.UIHover && Application.isMobilePlatform) return;

            var source = Claim();
            source.outputAudioMixerGroup = entry.isUi ? library.uiGroup : library.sfxGroup;
            source.clip = entry.clip;
            source.volume = entry.volume;
            source.Play();

            // Some good candidates are authored as loops — the terminal alarm
            // chosen for FLASH is one. Without this it would never stop.
            if (entry.maxDuration > 0.01f) StartCoroutine(StopAfter(source, entry.maxDuration));
        }

        /// <summary>
        /// Next free voice, or the oldest if all are busy.
        ///
        /// Round-robin rather than "find a silent one" so that stealing is
        /// predictable: the sound that gets cut is always the longest-running,
        /// which is the one a listener has already heard most of.
        /// </summary>
        AudioSource Claim()
        {
            for (int i = 0; i < pool.Length; i++)
            {
                var candidate = pool[(next + i) % pool.Length];
                if (candidate.isPlaying) continue;
                next = (next + i + 1) % pool.Length;
                return candidate;
            }

            var stolen = pool[next];
            next = (next + 1) % pool.Length;
            stolen.Stop();
            return stolen;
        }

        IEnumerator StopAfter(AudioSource source, float seconds)
        {
            var clip = source.clip;
            yield return new WaitForSecondsRealtime(seconds);

            // Only stop it if it is still the same sound — the voice may have been
            // reused in the meantime, and stopping the wrong one would cut a sound
            // that had just started.
            if (source != null && source.clip == clip && source.isPlaying) source.Stop();
        }

        /// <summary>Silence every voice. Used on mute and on teardown.</summary>
        public void StopAll()
        {
            if (pool == null) return;
            foreach (var source in pool)
                if (source != null) source.Stop();
        }
    }
}

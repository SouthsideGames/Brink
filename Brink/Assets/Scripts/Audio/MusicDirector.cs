using System.Collections;
using UnityEngine;

namespace Brink.Audio
{
    /// <summary>
    /// Two AudioSources, one crossfade at a time.
    ///
    /// A/B rather than one source because a single source cannot fade out of one
    /// clip and into another simultaneously — it would have to cut, and a cut is
    /// the one thing the brief rules out.
    ///
    /// Three rules do most of the work, and each answers an acceptance test:
    ///
    /// 1. **The same track is never restarted.** Asking for War while War is
    ///    playing is a no-op, so a system that re-asserts state every month does
    ///    not stutter the music every month.
    /// 2. **Only one fade exists at a time.** A new request stops the running
    ///    coroutine and starts from wherever the volumes actually are, so
    ///    Peace → Tension → Crisis → War pressed rapidly ends on War with
    ///    everything else silent rather than four tracks layered.
    /// 3. **The idle source is always stopped.** Left playing at zero volume it
    ///    would hold a streamed clip open and come back audibly on the next fade.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        AudioSource a;
        AudioSource b;
        AudioSource active;      // the one currently carrying the music
        Coroutine fade;

        AudioLibrary library;

        /// <summary>What is playing, or has been requested. Null before the first call.</summary>
        public MusicState? Current { get; private set; }

        public bool IsCrossfading => fade != null;

        public AudioClip CurrentClip => active != null ? active.clip : null;

        public void Initialize(AudioLibrary configuration)
        {
            library = configuration;

            a = CreateSource("Music A");
            b = CreateSource("Music B");
            active = a;
        }

        AudioSource CreateSource(string sourceName)
        {
            var host = new GameObject(sourceName);
            host.transform.SetParent(transform, false);

            var source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.volume = 0f;
            source.spatialBlend = 0f;                 // 2D: this is not in the world
            source.outputAudioMixerGroup = library != null ? library.musicGroup : null;
            return source;
        }

        /// <summary>
        /// Move to a music state, crossfading. Safe to call every frame; safe to
        /// call with the state already playing.
        /// </summary>
        public void Play(MusicState state, PillarContext context, bool immediate = false)
        {
            if (library == null) return;

            // Rule 1. Note this compares *state*, not clip: two states sharing a
            // track would still be a real transition as far as the rest of the
            // system is concerned, and comparing clips would silently swallow it.
            if (Current == state && active != null && active.isPlaying) return;

            var entry = library.Resolve(state, context);
            Current = state;

            if (entry == null || entry.clip == null)
            {
                // A state with no track is silence, not the previous track left
                // running — otherwise an unmapped state would quietly inherit
                // whatever came before and look like it worked.
                StopFade();
                FadeOutEverything(immediate);
                return;
            }

            var incoming = active == a ? b : a;
            incoming.clip = entry.clip;
            incoming.loop = entry.loop;
            incoming.volume = 0f;
            incoming.Play();

            var outgoing = active;
            active = incoming;

            StopFade();
            float seconds = immediate ? 0f : Mathf.Max(0.01f, library.crossfadeSeconds);
            fade = StartCoroutine(Crossfade(outgoing, incoming, entry.volume, seconds));
        }

        /// <summary>Silence the music entirely, e.g. for Mute or a full reset.</summary>
        public void StopAll(bool immediate = false)
        {
            Current = null;
            StopFade();
            FadeOutEverything(immediate);
        }

        void FadeOutEverything(bool immediate)
        {
            if (immediate)
            {
                if (a != null) { a.Stop(); a.volume = 0f; }
                if (b != null) { b.Stop(); b.volume = 0f; }
                return;
            }
            fade = StartCoroutine(Crossfade(a, null, 0f, library.crossfadeSeconds));
        }

        void StopFade()
        {
            if (fade == null) return;
            StopCoroutine(fade);
            fade = null;
        }

        /// <summary>
        /// Fade `outgoing` down and `incoming` up over `seconds`.
        ///
        /// Starts from each source's *current* volume rather than from 0 and 1, so
        /// interrupting a half-finished fade continues smoothly from where it
        /// actually is instead of jumping.
        /// </summary>
        IEnumerator Crossfade(AudioSource outgoing, AudioSource incoming,
            float targetVolume, float seconds)
        {
            float fromOut = outgoing != null ? outgoing.volume : 0f;
            float fromIn = incoming != null ? incoming.volume : 0f;
            float elapsed = 0f;

            while (elapsed < seconds)
            {
                // Unscaled: a paused game should still fade, and this project has
                // no time scaling, so tying music to it would only create a bug
                // the first time something pauses.
                elapsed += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(elapsed / seconds);

                if (outgoing != null) outgoing.volume = Mathf.Lerp(fromOut, 0f, t);
                if (incoming != null) incoming.volume = Mathf.Lerp(fromIn, targetVolume, t);
                yield return null;
            }

            if (outgoing != null)
            {
                outgoing.volume = 0f;
                outgoing.Stop();          // rule 3
                outgoing.clip = null;     // let a streamed clip go
            }
            if (incoming != null) incoming.volume = targetVolume;

            fade = null;
        }
    }
}

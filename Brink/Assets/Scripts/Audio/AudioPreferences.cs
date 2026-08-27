using System;
using UnityEngine;
using UnityEngine.Audio;

namespace Brink.Audio
{
    /// <summary>
    /// Per-device audio preferences.
    ///
    /// Deliberately shaped exactly like `Brink.UI.DisplaySettings`: a static class
    /// over `PlayerPrefs` with a `Changed` event. How loud you like your game is a
    /// property of the person and the handset, not of the save — it should follow
    /// you across a full reset and should not travel to another device with a
    /// copied save.
    ///
    /// Two settings systems is a smell, and it is worth saying why this is not
    /// one: they cover genuinely separate concerns and share a pattern, so the
    /// settings panel can host both without special-casing either.
    /// </summary>
    public static class AudioPreferences
    {
        const string MasterKey = "brink.audio.master";
        const string MusicKey = "brink.audio.music";
        const string SfxKey = "brink.audio.sfx";
        const string AmbienceKey = "brink.audio.ambience";
        const string MutedKey = "brink.audio.muted";

        /// <summary>
        /// Exposed AudioMixer parameter names. These are strings by necessity —
        /// the mixer API takes them — so they live here once rather than being
        /// retyped at each call site where a typo would fail silently.
        /// </summary>
        public const string MasterParam = "MasterVol";
        public const string MusicParam = "MusicVol";
        public const string SfxParam = "SfxVol";
        public const string UiParam = "UiVol";
        public const string AmbienceParam = "AmbienceVol";

        /// <summary>Below this a channel is treated as off rather than very quiet.</summary>
        public const float Silence = 0.0001f;

        static bool loaded;
        static float master = 0.8f;
        static float music = 0.7f;
        static float sfx = 0.85f;
        static float ambience = 0.5f;
        static bool muted;

        /// <summary>Raised when any value changes, so the mixer can be re-applied.</summary>
        public static event Action Changed;

        public static float Master
        {
            get { Load(); return master; }
            set { Load(); if (Approximately(master, value)) return; master = Clamp01(value); Save(); }
        }

        public static float Music
        {
            get { Load(); return music; }
            set { Load(); if (Approximately(music, value)) return; music = Clamp01(value); Save(); }
        }

        public static float Sfx
        {
            get { Load(); return sfx; }
            set { Load(); if (Approximately(sfx, value)) return; sfx = Clamp01(value); Save(); }
        }

        public static float Ambience
        {
            get { Load(); return ambience; }
            set { Load(); if (Approximately(ambience, value)) return; ambience = Clamp01(value); Save(); }
        }

        /// <summary>
        /// Silence everything without losing the mix.
        ///
        /// **Mute does not zero the values.** It is a separate flag applied on top,
        /// so unmuting restores exactly what the player had rather than a default —
        /// which is what anyone muting for a phone call expects, and what they
        /// would lose if mute were implemented by writing zeros.
        /// </summary>
        public static bool Muted
        {
            get { Load(); return muted; }
            set { Load(); if (muted == value) return; muted = value; Save(); }
        }

        // ---------- decibels ----------

        /// <summary>
        /// Convert a 0..1 slider position to mixer decibels.
        ///
        /// **AudioMixer volume is logarithmic and mostly negative** — 0 dB is
        /// unity gain and −80 dB is silence — so assigning a 0..1 value directly
        /// produces a control that does nothing until the last percent and is
        /// wrong at every point in between. `log10(0)` is negative infinity, which
        /// is why anything at or below the silence floor is mapped explicitly
        /// rather than computed.
        /// </summary>
        public static float ToDecibels(float linear)
        {
            if (linear <= Silence) return -80f;
            return Mathf.Log10(Mathf.Clamp01(linear)) * 20f;
        }

        /// <summary>Effective 0..1 level for a channel, mute and master included.</summary>
        public static float EffectiveLevel(float channel)
        {
            Load();
            return muted ? 0f : Clamp01(channel) * Clamp01(master);
        }

        /// <summary>
        /// Push current values into the mixer.
        ///
        /// Master is applied as its own group rather than multiplied into each
        /// channel, so the mixer graph does the work and a future effect on the
        /// master bus behaves correctly.
        /// </summary>
        public static bool Apply(AudioMixer mixer)
        {
            if (mixer == null) return false;
            Load();

            // `SetFloat` returns false for a parameter the mixer does not expose.
            // The shipped mixer exposed none (2026-08 audit), so every call here
            // failed silently and no preference reached the ear. The result is
            // reported so the director can fall back to scaling the sources.
            float gate = muted ? 0f : 1f;
            bool ok = true;
            ok &= mixer.SetFloat(MasterParam, ToDecibels(master * gate));
            ok &= mixer.SetFloat(MusicParam, ToDecibels(music));
            ok &= mixer.SetFloat(SfxParam, ToDecibels(sfx));
            ok &= mixer.SetFloat(UiParam, ToDecibels(sfx));      // UI rides the SFX slider
            ok &= mixer.SetFloat(AmbienceParam, ToDecibels(ambience));
            return ok;
        }

        /// <summary>Every parameter the preferences drive. A mixer must expose all of them.</summary>
        public static readonly string[] MixerParameters =
            { MasterParam, MusicParam, SfxParam, UiParam, AmbienceParam };

        // ---------- persistence ----------

        static void Load()
        {
            if (loaded) return;
            loaded = true;

            master = PlayerPrefs.GetFloat(MasterKey, 0.8f);
            music = PlayerPrefs.GetFloat(MusicKey, 0.7f);
            sfx = PlayerPrefs.GetFloat(SfxKey, 0.85f);
            ambience = PlayerPrefs.GetFloat(AmbienceKey, 0.5f);
            muted = PlayerPrefs.GetInt(MutedKey, 0) != 0;
        }

        static void Save()
        {
            PlayerPrefs.SetFloat(MasterKey, master);
            PlayerPrefs.SetFloat(MusicKey, music);
            PlayerPrefs.SetFloat(SfxKey, sfx);
            PlayerPrefs.SetFloat(AmbienceKey, ambience);
            PlayerPrefs.SetInt(MutedKey, muted ? 1 : 0);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Back to defaults. Used by the debug panel and a full reset.</summary>
        public static void ResetToDefaults()
        {
            Load();
            master = 0.8f; music = 0.7f; sfx = 0.85f; ambience = 0.5f; muted = false;
            Save();
        }

        static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
        static bool Approximately(float a, float b) => Mathf.Abs(a - b) < 0.0005f;
    }
}

using System;
using Brink.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Brink.Tests
{
    /// <summary>
    /// The audio foundation's contract (GDD §28 presentation).
    ///
    /// Deliberately covers only what is true without a running audio device: the
    /// decibel conversion, the settings contract, and the two structural rules
    /// that stop the system misbehaving. Crossfading and mixer routing need a
    /// real AudioSource and are verified by hand through the AUDIO debug view —
    /// asserting them here would need a play-mode harness for very little, and a
    /// test that cannot actually hear anything proves less than it appears to.
    /// </summary>
    public class AudioSystemTests
    {
        [TearDown]
        public void TearDown() => AudioPreferences.ResetToDefaults();

        // ---------- decibels ----------

        /// <summary>
        /// **AudioMixer volume is logarithmic and mostly negative.** Assigning a
        /// 0..1 slider value straight into it produces a control that does nothing
        /// until the last percent — the single most common way a volume slider
        /// ships broken.
        /// </summary>
        [Test]
        public void FullVolumeIsUnityGainAndSilenceIsFloored()
        {
            Assert.AreEqual(0f, AudioPreferences.ToDecibels(1f), 0.01f,
                "Full volume must be 0 dB — unity gain, not +1.");

            Assert.LessOrEqual(AudioPreferences.ToDecibels(0f), -80f,
                "Zero must map to the silence floor. log10(0) is negative infinity, "
                + "so this has to be handled rather than computed.");

            Assert.LessOrEqual(AudioPreferences.ToDecibels(AudioPreferences.Silence * 0.5f), -80f,
                "Anything below the silence threshold is off, not very quiet.");
        }

        [Test]
        public void HalfVolumeIsNotHalfDecibels()
        {
            // The whole point of the conversion. A linear slider at 0.5 is about
            // −6 dB, not −40 and not −0.5.
            float half = AudioPreferences.ToDecibels(0.5f);

            Assert.Less(half, 0f);
            Assert.Greater(half, -12f,
                $"0.5 converted to {half:F1} dB. Half volume should sit near −6 dB; a "
                + "much lower value means the curve is wrong and the slider will feel dead.");
        }

        [Test]
        public void QuieterIsAlwaysQuieter()
        {
            float previous = float.MaxValue;
            for (float v = 1f; v >= 0f; v -= 0.05f)
            {
                float db = AudioPreferences.ToDecibels(v);
                Assert.LessOrEqual(db, previous + 0.0001f,
                    $"Volume {v:F2} was louder than the step above it. The curve must be monotonic.");
                previous = db;
            }
        }

        // ---------- settings ----------

        [Test]
        public void MuteRemembersTheMixRatherThanZeroingIt()
        {
            // Mute implemented by writing zeros loses the player's settings, which
            // is exactly what someone muting for a phone call does not expect.
            AudioPreferences.Master = 0.62f;
            AudioPreferences.Music = 0.33f;

            AudioPreferences.Muted = true;
            Assert.AreEqual(0f, AudioPreferences.EffectiveLevel(AudioPreferences.Music), 0.0001f,
                "Muted, nothing should be audible.");
            Assert.AreEqual(0.33f, AudioPreferences.Music, 0.001f,
                "Mute overwrote the stored value instead of gating it.");

            AudioPreferences.Muted = false;
            Assert.AreEqual(0.62f, AudioPreferences.Master, 0.001f);
            Assert.AreEqual(0.33f, AudioPreferences.Music, 0.001f,
                "Unmuting did not restore what the player had.");
        }

        [Test]
        public void MasterScalesEveryChannel()
        {
            AudioPreferences.Muted = false;
            AudioPreferences.Master = 0.5f;
            AudioPreferences.Music = 1f;
            AudioPreferences.Sfx = 1f;

            Assert.AreEqual(0.5f, AudioPreferences.EffectiveLevel(AudioPreferences.Music), 0.001f);
            Assert.AreEqual(0.5f, AudioPreferences.EffectiveLevel(AudioPreferences.Sfx), 0.001f);
        }

        [Test]
        public void ChannelsAreIndependent()
        {
            AudioPreferences.Muted = false;
            AudioPreferences.Master = 1f;
            AudioPreferences.Music = 0f;
            AudioPreferences.Sfx = 0.9f;

            Assert.AreEqual(0f, AudioPreferences.EffectiveLevel(AudioPreferences.Music), 0.001f);
            Assert.Greater(AudioPreferences.EffectiveLevel(AudioPreferences.Sfx), 0.8f,
                "Silencing the music silenced the sound effects too.");
        }

        [Test]
        public void LevelsAreClampedToTheAudibleRange()
        {
            AudioPreferences.Music = 5f;
            Assert.LessOrEqual(AudioPreferences.Music, 1f, "A level above 1 would clip.");

            AudioPreferences.Music = -3f;
            Assert.GreaterOrEqual(AudioPreferences.Music, 0f);
        }

        // ---------- the structural rules ----------

        /// <summary>
        /// Changing pillar **cannot** restart the music, because the resolver does
        /// not consider context. Asserted as a property of the design rather than
        /// as a promise about the director's behaviour: when pillar stems arrive,
        /// this test is the thing that should be revisited deliberately.
        /// </summary>
        [Test]
        public void ContextCannotChangeWhichTrackPlays()
        {
            var library = ScriptableObject.CreateInstance<AudioLibrary>();
            library.music.Add(new MusicEntry { state = MusicState.Peace, clip = null });

            var world = library.Resolve(MusicState.Peace, PillarContext.World);
            foreach (PillarContext context in Enum.GetValues(typeof(PillarContext)))
                Assert.AreSame(world, library.Resolve(MusicState.Peace, context),
                    $"{context} resolved to a different entry than World, so changing panels "
                    + "would interrupt the music.");

            ScriptableObject.DestroyImmediate(library);
        }

        [Test]
        public void AnUnmappedSoundIsReportedRatherThanGuessed()
        {
            // Several ids deliberately carry no clip. They must be visible as
            // known gaps — an unmapped sound that looks mapped is how a wrong
            // clip gets quietly substituted later.
            var library = ScriptableObject.CreateInstance<AudioLibrary>();
            library.sfx.Add(new SfxEntry { id = SfxId.UIConfirm, clip = null });

            var unmapped = library.Unmapped();
            CollectionAssert.Contains(unmapped, SfxId.UIConfirm,
                "An entry with no clip was not reported as unmapped.");
            CollectionAssert.Contains(unmapped, SfxId.WarDeclared,
                "An id with no entry at all was not reported as unmapped.");
            CollectionAssert.DoesNotContain(unmapped, SfxId.None,
                "None is not a sound and should never be listed as missing one.");

            ScriptableObject.DestroyImmediate(library);
        }

        [Test]
        public void EveryMusicStateIsDistinctlyAddressable()
        {
            // A duplicated state in the list would make Resolve return whichever
            // came first and silently strand the other.
            var seen = new System.Collections.Generic.HashSet<MusicState>();
            foreach (MusicState state in Enum.GetValues(typeof(MusicState)))
                Assert.IsTrue(seen.Add(state), $"{state} appears twice in the enum.");

            Assert.AreEqual(5, seen.Count,
                "MainMenu, Peace, Tension, Crisis and War are the states the brief asked for.");
        }

        [Test]
        public void TheDirectorStaysAbsentInBatchMode()
        {
            // The edit-mode suite runs headless. Instantiating AudioSources across
            // hundreds of tests would be pure cost, and `GameBootstrap` guards
            // itself the same way for the same reason.
            AudioDirector.EnsureExists();
            Assert.IsNull(AudioDirector.Instance,
                "The audio director was created in batch mode.");
        }
    }
}

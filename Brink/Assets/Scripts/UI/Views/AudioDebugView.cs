using System.Text;
using Brink.Audio;
using Brink.Core;
using UnityEngine.UIElements;

namespace Brink.UI.Views
{
    /// <summary>
    /// A bench for the audio system (temporary).
    ///
    /// Every acceptance test for this system needs a state change on demand — a
    /// war starting, a FLASH arriving, a rapid run through four music states —
    /// and waiting for gameplay to produce those would make the audio untestable
    /// in practice. This drives all of it directly.
    ///
    /// Ships only in development builds, alongside the SYSTEM console and for the
    /// same reason (GDD §30): handing a player buttons that reach past the
    /// simulation is exactly what the no-reloading rule exists to prevent.
    ///
    /// Expected to be deleted once audio is wired to real events and the Settings
    /// screen carries the volume controls.
    /// </summary>
    public class AudioDebugView : TerminalView
    {
        public override string Id => "AUDIO";
        public override string ShortCode => "AUD";

        static int W => TerminalMetrics.Columns;

        protected override void Build()
        {
            Root.Clear();

            var director = AudioDirector.Instance;
            if (director == null)
            {
                AddText("sig-hostile").text =
                    " AUDIO SYSTEM NOT RUNNING.\n"
                    + " Expected in batch mode. Otherwise the library is missing — run\n"
                    + " Brink > Audio > Create or Update Audio Library, then restart play mode.";
                return;
            }

            BuildStatus(director);
            BuildMusicControls();
            BuildContextControls(director);
            BuildVolumeControls();
            BuildSfxControls(director);
        }

        void BuildStatus(AudioDirector director)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("AUDIO STATE", W);

            var sb = new StringBuilder();
            sb.AppendLine($"  MUSIC STATE ... {(director.MusicState.HasValue ? director.MusicState.Value.ToString().ToUpperInvariant() : "NONE")}");
            sb.AppendLine($"  PLAYING ....... {(director.CurrentMusicClip != null ? director.CurrentMusicClip.name : "SILENT")}");
            sb.AppendLine($"  CONTEXT ....... {director.Context.ToString().ToUpperInvariant()}");
            sb.AppendLine($"  CROSSFADING ... {(director.IsCrossfading ? "YES" : "no")}");
            sb.AppendLine();
            sb.AppendLine($"  MASTER {AudioPreferences.Master:F2}   MUSIC {AudioPreferences.Music:F2}   "
                          + $"SFX {AudioPreferences.Sfx:F2}   AMBIENCE {AudioPreferences.Ambience:F2}");
            sb.AppendLine($"  MUTED ......... {(AudioPreferences.Muted ? "YES" : "no")}");
            sb.AppendLine($"  MIXER ......... {(director.Library != null && director.Library.mixer != null ? "assigned" : "none (levels applied to sources)")}");

            AddFigure().text = sb.ToString();

            // Refreshes are manual on purpose: a repeating schedule to animate the
            // crossfade flag would be exactly the per-frame polling this system is
            // meant not to have.
            var row = MakeRow();
            AddButton(row, "REFRESH READOUT", null, Refresh);
        }

        void BuildMusicControls()
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("MUSIC STATE", W);
            var row = MakeRow();

            foreach (MusicState state in System.Enum.GetValues(typeof(MusicState)))
            {
                var captured = state;
                AddButton(row, state.ToString().ToUpperInvariant(), null, () =>
                {
                    AudioDirector.SetMusic(captured);
                    Refresh();
                });
            }

            AddText("terminal-text-dim").text =
                "  Press the same state twice: the track must NOT restart. Press four in\n"
                + "  quick succession: exactly one track should survive.";
        }

        void BuildContextControls(AudioDirector director)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("PILLAR CONTEXT", W);
            var row = MakeRow();

            foreach (PillarContext context in System.Enum.GetValues(typeof(PillarContext)))
            {
                var captured = context;
                bool current = director.Context == context;
                var button = new Button(() => { AudioDirector.SetContext(captured); Refresh(); })
                { text = (current ? "► " : "") + context.ToString().ToUpperInvariant() };
                button.AddToClassList("cmd-button");
                if (current) button.AddToClassList("primary");
                row.Add(button);
            }

            AddText("terminal-text-dim").text =
                "  Context is tracked, not yet scored. Changing it must never restart the\n"
                + "  music — AudioLibrary.Resolve ignores context, so it cannot.";
        }

        void BuildVolumeControls()
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("LEVELS", W);

            Step("MASTER", () => AudioPreferences.Master, v => AudioPreferences.Master = v);
            Step("MUSIC", () => AudioPreferences.Music, v => AudioPreferences.Music = v);
            Step("SFX + UI", () => AudioPreferences.Sfx, v => AudioPreferences.Sfx = v);
            Step("AMBIENCE", () => AudioPreferences.Ambience, v => AudioPreferences.Ambience = v);

            var row = MakeRow();
            AddButton(row, AudioPreferences.Muted ? "UNMUTE ALL" : "MUTE ALL",
                AudioPreferences.Muted ? "primary" : "danger", () =>
                {
                    AudioPreferences.Muted = !AudioPreferences.Muted;
                    Refresh();
                });
            AddButton(row, "RESET LEVELS", null, () => { AudioPreferences.ResetToDefaults(); Refresh(); });

            AddText("terminal-text-dim").text =
                "  Mute does not zero the sliders — unmuting restores exactly these values.";
        }

        /// <summary>
        /// A labelled −/+ pair. UI Toolkit has a slider, but a 44px-tall drag
        /// target on a phone is fiddly and this is a debug bench: discrete steps
        /// are easier to hit and easier to reason about when checking a level.
        /// </summary>
        void Step(string label, System.Func<float> get, System.Action<float> set)
        {
            var row = MakeRow();
            AddButton(row, $"{label} −", null, () => { set(Round(get() - 0.1f)); Refresh(); });
            // A readout, not a control: no cost tag, so no gate touches it.
            AddButton(row, $"{get():F2}", "primary", null).SetEnabled(false);
            AddButton(row, $"{label} +", null, () => { set(Round(get() + 0.1f)); Refresh(); });
        }

        static float Round(float v)
        {
            v = v < 0f ? 0f : (v > 1f ? 1f : v);
            return UnityEngine.Mathf.Round(v * 100f) / 100f;
        }

        /// <summary>A debug control that is inert when nothing is mapped to it.</summary>
        void AddOptionalButton(VisualElement row, string text, string extraClass, System.Action onClick)
        {
            var button = AddButton(row, text, extraClass, () => onClick?.Invoke());
            if (onClick == null) Block(button, "NOTHING MAPPED");
        }

        void BuildSfxControls(AudioDirector director)
        {
            AddText("terminal-text-bright").text = AsciiChart.BoxHeader("SOUND EFFECTS", W);

            var mapped = MakeRow();
            var unmapped = new System.Collections.Generic.List<string>();

            foreach (SfxId id in System.Enum.GetValues(typeof(SfxId)))
            {
                if (id == SfxId.None) continue;

                var entry = director.Library != null ? director.Library.Find(id) : null;
                if (entry == null || entry.clip == null)
                {
                    unmapped.Add(id.ToString());
                    continue;
                }

                var captured = id;
                AddOptionalButton(mapped, id.ToString().ToUpperInvariant(), null,
                    () => AudioDirector.Play(captured));
            }

            if (unmapped.Count > 0)
                AddText("terminal-text-dim").text =
                    "  NO CLIP (deliberate — nothing in the imported libraries fits without\n"
                    + "  defining the game's audio identity wrongly):\n    "
                    + string.Join(", ", unmapped);
        }
    }
}

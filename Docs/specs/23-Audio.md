# 23 — Audio

**Status: as-built.** `Scripts/Audio/` (`AudioDirector`, `MusicDirector`,
`SfxPlayer`, `AudioPreferences`, `AudioLibrary`, `AudioCues`, `AudioTypes`),
`Resources/Audio/BrinkAudioLibrary.asset`, `Resources/Audio/BrinkMixer.mixer`,
`Assets/Tests/EditMode/AudioSystemTests.cs`. GDD §28 presentation.

## 1. What the 2026-08 audit found

The layer was well built and **never driven**. Five music states, twenty sound
ids, a two-source crossfader, a four-voice SFX pool, a mixer with five groups
and a persisted preferences model — and outside `GameBootstrap.EnsureExists`
and the AUDIO debug view, **no call site anywhere in the game**. A player heard
the terminal ambience loop and nothing else, ever.

Three further defects:

1. **The mixer exposed no parameters** (`m_ExposedParameters: []`). Every
   `SetFloat` in `AudioPreferences.Apply` returned false, and because a mixer
   *was* assigned the director returned early instead of scaling the sources —
   so no volume or mute setting did anything, in any build.
2. **No screen set any preference.** The model persisted five values that
   nothing could change.
3. **Seven of twenty sounds had no clip** (`PanelClose`, `EndMonth`,
   `PositiveOutcome`, `NegativeOutcome`, `CrisisStarted`, `AnnualEvaluation`,
   `SaveComplete`), and `MusicDirector.FadeOutEverything` faded source A
   unconditionally, so an unmapped state reached while B was active left B
   playing with `Current` reporting silence.

## 2. How gameplay drives it now

`AudioCues` is pure — state in, cue out — so the mapping is edit-mode tested;
`AudioDirector` only plays.

| Moment | Call | Cue |
|---|---|---|
| Nav rail: view changes | `Play(PanelOpen)`, `SetContext(AudioCues.ContextFor(id))` | UI click; pillar context tracked |
| END MONTH pressed | `Play(EndMonth)` (or `CommandRejected` if blocked) then `Sync(state)` | tape-load; then the month's traffic |
| `Sync(state)` | `SetMusic(AudioCues.MusicFor(state))` | **Crisis Turn open → Crisis; any player war at Limited Conflict+ → War; at Crisis → Crisis; at Tension → Tension; else Peace** |
| `Sync(state)` | `CrisisStarted` if a new Crisis Turn opened, else the **loudest** alert owed for traffic since the last sync (Flash > Priority > Advisory; Wire/Archive silent) | one cue per month, not one per item |
| Assessment screen | `SetMusic(MainMenu)` | |
| State replaced (new game, load) | `Sync(state, silentAlerts: true)` | re-baselines the traffic counter; old items never fire |

`AudioDirector.Sync` keeps `notificationsSeen` / `crisesSeen`; a count that goes
*down* (the notification cap, a load) resets the baseline rather than firing.

## 3. Volume

`AudioPreferences.Apply(mixer)` returns whether every parameter in
`MixerParameters` (`MasterVol`, `MusicVol`, `SfxVol`, `UiVol`, `AmbienceVol`)
took. The director uses the mixer only when it does; otherwise it scales the
sources directly through `MusicDirector.SetLevel`, `SfxPlayer.SetLevel` and the
ambience source, mute included. The shipped mixer now exposes all five and
`AudioSystemTests.TheShippedMixer_ExposesEveryPreferenceParameter` keeps it so.

Settings (the DSP panel) gained an AUDIO section: MASTER / MUSIC / EFFECTS /
TERMINAL HUM in five steps, and MUTE ALL. Buttons, not sliders — the shell
draws no sliders.

## 4. Library

Music: MainMenu, Peace, Tension, Crisis, War — one loop each. Ambience: the
underground-power-station loop at 0.15. Every `SfxId` is mapped;
`AudioSystemTests.TheShippedLibrary_MapsEverySound` refuses a gap. Loop-authored
clips used as one-shots carry `maxDuration`.

## 5. Open

- `SetContext` is tracked and cannot change the track (`Resolve` ignores
  context by design). Pillar-layered music is the obvious next step and needs
  nothing from gameplay.
- ~~`PositiveOutcome` / `NegativeOutcome` are mapped but no call site fires them.~~
  `Sync` now fires them for a war won or lost this month and for the mandate
  verdict, and `AnnualEvaluation` when a new evaluation lands. Operation-level
  results are still silent.
- Crossfade and routing are still verified by ear through the AUDIO debug view;
  a play-mode harness would be the only way to assert them.

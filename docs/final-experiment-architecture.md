# Final experiment architecture

## Fixed decisions

- Target platform: Android only.
- Scenarios: dental drill, toothbrush, and apple.
- Conditions currently executed: baseline and researcher haptics.
- Baseline always resolves to no haptic profile.
- Audio and animation are owned by `ScenarioProfile`, so they stay identical across conditions.
- Researcher haptics follow: sound-based generation → manual correction → calibrated profile lock.
- Participant-created haptics remain represented in the data model but are disabled in `ExperimentProtocol` until the tuning procedure is decided.
- Evaluation metrics are outside the current runner and can be added after the program flow stabilizes.

## Execution spaces

- `ExperimentScene`: participant-facing VR runner only. The legacy 360-degree search controller and development menus are not present.
- `MobileTestScene`: profile preview, controller status, mirror scenario synchronization, and the PC haptic link receiver.
- `PCScenarioTestScene`: non-XR scenario, animation-block, and sound-offset preview.
- Editor windows: Manual Haptic Generator, Sound-based Haptic Generator, and PC Scenario Synchronizer.

## Editor Tools menu

- `Tools/Experiment/PC Scenario Synchronizer`: edits and previews scenario timing without XR.
- `Tools/Experiment/Validate Final Experiment`: checks final sounds, playable/calibrated researcher haptics, animation blocks, protocol coverage, scene references, and a `MirrorRig` runtime smoke test.
- `Tools/Experiment/Build`: builds the participant experiment or mobile test Android APK. The participant build always runs the final validation first.
- `Tools/Experiment/Setup/Create Profiles and Scenes`: initial/regeneration command. It shows a destructive-reset confirmation before changing generated scenes and profiles.
- `Tools/Haptics`: contains only the Manual and Sound-based Haptic Generators.

Legacy rig probes, synthesized-audio preview, the old waveform launcher, and audio-converter regression checks are intentionally absent from the normal menu. The converter check remains callable in batch mode through `AudioHapticConverterChecks.RunBatch`.

## Profiles and audio

- `SoundProfile`: clip, trim/fade/volume, hash, and placeholder state.
- `HapticProfile`: unlimited logical duration, waveform, source settings/hash, offset, and calibration origin.
- `ScenarioProfile`: fixed sound plus shared animation blocks and condition-specific haptics.
- `ExperimentProtocol`: scenario/condition order; participant condition disabled by default.
- `ProfileCatalog`: central lookup for test tooling.

Existing audio was moved to `Assets/Audio/ScenarioSources`. All current sound profiles are marked as placeholders. The final Experiment Android build validator rejects missing/placeholder audio and missing or unlocked researcher profiles.

## Long profiles

Audio conversion reads one analysis window at a time rather than allocating the full PCM clip. Profiles have no ten-second or 256-segment total cap. A long profile is chunked only at the Android transport/playback boundary, and chunk starts use the absolute DSP clock to avoid cumulative coroutine delay.

## Block animation timeline

`ScenarioPlaybackController` owns the DSP-based scenario clock. The separated experiment path disables `MirrorRig`'s legacy self-advancing animation clock and evaluates one explicit primary-track block on every frame. Block duration changes therefore stretch, loop, play once, or hold the selected source action instead of only firing metadata callbacks.

- Apple: three explicit repetitions of plate wait → approach → bite → chew → rest.
- Dental Drill: independent drill approach → drill contact → drill retreat actions.
- Toothbrush: independent brush approach → brush contact → brush retreat actions.

Timeline validation rejects missing actions, duplicate IDs, invalid ranges, overlaps, action/scenario mismatches, and blocks that exceed the scenario duration. `Tools/Experiment/Validate Final Experiment` includes both the profile check and a `MirrorRig` runtime evaluation of every block. The individual checks remain available to batch automation through `ExperimentBuild.ValidateAnimationTimelines` and `ExperimentBuild.ValidateAnimationRuntime`, but are intentionally absent from the normal menu.

PC Scenario Synchronizer commits the selected profile before opening the test scene, repairs the animation-driver and camera references, validates the timeline, and then enters Play Mode. In the Game view, hold the right mouse button to look, use WASD/QE to move, 1–6 for view presets, and R to reset the PC camera.

## Still pending

- Final scenario audio files and licensing/source notes.
- Sound-based generation and manual calibration of each researcher profile.
- Participant tuning method and participant-condition runner.
- Trial order, repetitions, counterbalancing, and session navigation details.
- Audio output policy (device speaker/headphones and fixed volume).
- Evaluation and results schema.

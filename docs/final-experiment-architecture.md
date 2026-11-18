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

## Profiles and audio

- `SoundProfile`: clip, trim/fade/volume, hash, and placeholder state.
- `HapticProfile`: unlimited logical duration, waveform, source settings/hash, offset, and calibration origin.
- `ScenarioProfile`: fixed sound plus shared animation blocks and condition-specific haptics.
- `ExperimentProtocol`: scenario/condition order; participant condition disabled by default.
- `ProfileCatalog`: central lookup for test tooling.

Existing audio was moved to `Assets/Audio/ScenarioSources`. All current sound profiles are marked as placeholders. The final Experiment Android build validator rejects missing/placeholder audio and missing or unlocked researcher profiles.

## Long profiles

Audio conversion reads one analysis window at a time rather than allocating the full PCM clip. Profiles have no ten-second or 256-segment total cap. A long profile is chunked only at the Android transport/playback boundary, and chunk starts use the absolute DSP clock to avoid cumulative coroutine delay.

## Still pending

- Final scenario audio files and licensing/source notes.
- Sound-based generation and manual calibration of each researcher profile.
- Participant tuning method and participant-condition runner.
- Trial order, repetitions, counterbalancing, and session navigation details.
- Audio output policy (device speaker/headphones and fixed volume).
- Evaluation and results schema.

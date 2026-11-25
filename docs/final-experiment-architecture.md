# Final experiment architecture

## Research redesign (2026-11-25)

The enabled `ExperimentProtocol.research` runner supersedes the legacy conditions described below.
The D:/playground/XR-Interaction-final-experiment worktree uses `feature/experiment-redesign`.

- Factors: Toothbrush / Apple, Active / Passive, None / Simple / Sound-based (12 combinations).
- Active A toggles behavior repeatedly inside one observation window. B held for one second aborts.
- Passive starts the existing scenario once; it never automatically restarts. If the source ends before
  the observation deadline, the idle pose and logging remain until the deadline.
- Observation duration is edited in Experiment Configuration and shared by all six cells of a scenario.
  The current 60-second defaults are pilot values, not finalized experimental parameters.
- Deadline rejects new activation and requests a graceful finish. Apple continues its original audio,
  haptics and remaining chewing through the current iteration's rest; Brush plays its retreat.
  Recording ends only after the finishing transition. Requests and actual state changes are separate events.
- Original Sound-based assets are unchanged and protected by a stable waveform/settings fingerprint.
  Old personalization assets remain stored for compatibility but are not used or displayed by research UI.

### Active transitions and world lifetime

The research runner prepares one MirrorRig per trial. Its room origin and yaw are captured once;
repeated ON/OFF episodes retain the same room, character, materials and light objects. Head tracking
continues normally. Starting an episode schedules audio/haptics while the prepared idle scene stays
visible, then starts the existing v002 timeline at zero. OFF before animation zero cancels pending
audio/haptics and returns to Idle immediately (`start_cancelled`), including at the trial deadline.

For Brush OFF, media stops immediately. The displayed tool pose, nearby hand/tool pose and mouth
opening are captured and eased toward the idle pose over the remaining Retreat duration, with
`finishBlendSeconds` as a minimum transition duration. Interrupting an already running Retreat
continues its remaining portion. Continuous natural Retreat uses the same pose capture; direct
PC timeline sampling does not capture history and remains deterministic. Apple OFF still continues
the original synchronized timeline through the current chewing iteration's rest. Preparing the next
episode resets food/tool state without recreating the world or recentering it on the current view.

Treatment lighting is updated after head tracking, including while idle. Environment settings and
all three ambient colors are restored only when disposing the trial world. Trial completion/abort
and scene exit still stop media and release the world.

`ActiveSceneContinuityVerification` runs before the existing 12-condition runtime checks. It checks
scheduled cancellation, world/light identity and anchor retention after head turns, interrupted
approach/contact retreat poses and endpoints, natural completion, Apple chewing and ambient
restoration. Runtime cases also check that world and lighting stay stable between episodes.
These checks do not substitute for frame-time and perceived-brightness review on the Android device.

### PC settings and Simple generation

`Tools/Experiment/Experiment Configuration` edits the experiment's ScenarioProfile and Simple profile
assignments, observation times, controller requirement and IMU rate. Sound is explicitly shown as
read-only from ScenarioProfile; Sound-based remains fixed/read-only. None always maps to no haptic.
There are no generation controls or waveform graphs in Configuration. Asset paths/Inspect and
Controller & IMU are collapsed by default, with Save Configuration and Validate 12 Conditions at the end.

`Tools/Haptics/Simple Pulse Generator` is an independent window with its own draft source scenario,
optional analysis-settings reference, fixed amplitude, width/gap and advanced detection parameters.
Generate & Save New Profile creates a separately named asset and never overwrites an existing output
or assigns the result to the experiment. Last saved result provides waveform comparison, Inspect and
Profile Player links. To apply it, select the result in Configuration > Simple and Save Configuration.
Open Generator in a scenario card copies starting inputs only; later generator edits do not mutate
the protocol. Legacy serialized pulse seed fields are retained for initial setup, but validation uses
the selected Simple asset's actual constant levels, not those old generator parameters.
The generator thresholds the source audio envelope inside Contact blocks, removes short spans, merges
small contact gaps and splits long spans into pulses. Every nonzero pulse has one fixed amplitude;
duration resolution is the source analysis window. The initial amplitude 100, threshold .15, maximum
pulse 200 ms and silence 80 ms are editable pilot settings. Regenerate after changing pulse parameters.
Sound-based profiles are never regenerated by this command. Physical perceived-intensity matching is
still a research step, not an automatic numerical normalization guarantee.

`ResearchToolIsolationVerification.Run` checks independent generator drafts, new-output paths and IDs,
unchanged existing experiment/Simple/Sound-based assets, explicit alternate Simple assignment and
overwrite protection. It creates and removes only a uniquely named temporary test asset.

Both Tools Android builds and ordinary Build And Run validate the enabled research configuration.
Keep MobileTestScene first, ExperimentScene second, and Android Initialize XR on Startup disabled.
`Initialize Research Redesign` migrates legacy assignments without rebuilding scenes or touching source waveforms.

### Mobile workflow

1. Open Controller Check and verify A and B. Recognition means app-level Gamepad input, not OS Bluetooth pairing.
   Joystick-only/HID devices are reported as unsupported rather than falsely accepted as usable Gamepads.
2. Choose Scenario, Mode and Haptic in Experiment Setup; review the confirmation, then Start Trial.
3. One second of preparation precedes the common observation-start event. Complete/aborted trials return to Setup.
   A disconnected/reconfigured required controller aborts; there is no silent automatic resume.
4. Haptic Test has exactly two playback paths: Scenario Preview and PC Haptic Receiver (see below).
   Sensor / Logging Test now captures IMU only; use optional recording in the receiver for motor/noise tests.
5. Saved Logs / Export lists old and new recordings recursively and creates a ZIP in Downloads/XRExperiment
   on Android 10+. Older Android versions keep the ZIP at the displayed app storage path.

### Scenario Preview and saved-profile PC playback

Haptic Test > Scenario Preview selects the same ScenarioProfile, Active/Passive mode and None/Simple/
Sound-based assignment as the experiment. It reuses ResearchTrialController, including observation time,
single automatic passive start, repeated active ON/OFF, Apple chewing completion, and the same controller
readiness rules. Preview logging defaults OFF; no recorder files or native IMU registration are required
in that mode. Enabling logging tags it ScenarioPreview. Formal experiment recording remains mandatory.
Both completed and aborted previews return to Scenario Preview with the previous selection/logging option
retained; formal experiments return to Experiment Setup with selections reset.

For direct waveform playback, open Haptic Test > PC Haptic Receiver on the phone. On PC open
Tools/Haptics/Haptic Profile Player, select a saved HapticProfile, ADB Forward, Check Receiver, then
Play on Android. The player shows the asset path, ID/version/hash, waveform, scale, offset and loop.
Unsaved selected assets must be saved first. No Unity Play Mode is required. Simple, Sound-based and
Manual assets use the existing upload/chunk transport. The phone shows the last profile and playback
state and offers Stop and optional IMU recording. Recorded transport events include actual uploaded
scaled arrays plus source ID/version/hash and scheduling offset. The positive offset adds delay; the
negative-offset reference is shifted as in the scenario scheduler, preserving the full waveform.

The receiver's protocol=2 handshake prevents a saved-profile player from silently sending offset/identity
metadata to an older APK. Existing Generator ADB Forward and draft Play on Android remain functional;
their draft buttons are labeled explicitly and saved-profile buttons open the dedicated Player. The
Simple Generator also links to the Player. The receiver and VR scenario execution are mutually exclusive:
leaving the receiver stops playback and closes its listener before a scenario is launched.

PC direct preview sends temporary waveforms; it does not replace assets or protocol assignments in the
APK. ScenarioProfile changes and updated saved profiles used by Scenario Preview still require rebuilding
and reinstalling the APK. Runtime profile editing, intensity personalization and scenario streaming are
not part of these preview paths.

### Data and validation

Each recording has metadata.json, head.csv, imu.csv, events.csv and summary.json under
Application.persistentDataPath/ResearchLogs/<purpose>/<UTC timestamp + random ID>. Purpose is Experiment,
ScenarioPreview, DirectHapticTest or SensorTest, and is also explicit in schema-v2 metadata. Direct and
IMU-only capture have no Active/Passive mode (NotApplicable), instead of impersonating passive trials.
Metadata includes source
snapshots, selected condition, device and sampling configuration. Quaternion and raw IMU are preserved;
no online filtering, displacement integration or claim of objective presence is made.
Sensor callbacks use their boot-time nanosecond timestamps in native Android code, independent of Unity
frames. Clock-anchor events retain Unity monotonic midpoint and native boot time plus call uncertainty.
Events retain DSP time, episode IDs, ON/OFF requests, state changes, block boundaries, scheduled audio,
and haptic chunk commands/arrays. Command timestamps are not measured physical stimulus onset.
The sensor queue is bounded and reports dropped records; writes are buffered and flushed periodically.
App pause, storage failure, abort or unexpected unload are distinguished from completed trials.
Missing summary.json indicates incomplete/recoverable raw output after failure or forced termination.

Saved Logs / Export is a cached, paged browser with purpose filters, newest/oldest sorting, recording
status and managed byte counts (raw files plus associated internal ZIPs). Select individual rows or
all recordings in the current filter across pages, then confirm Delete Selected; each row also has
Export ZIP and Delete. Incomplete folders with raw files but no metadata, aborted recordings and
legacy root-level recordings are included. Refresh rescans storage; it is not scanned every GUI frame.
Deleting removes the selected app recording directory and its associated internal export ZIP caches.
Existing copies in Downloads/XRExperiment are kept. The confirmation shows count, size and target
recordings. Active recordings cannot be exported or deleted. Management rejects paths outside the
log root, root/category deletion and symbolic-link traversal. No automatic retention/deletion runs.
Recorders track their own output directory separately from the latest-recording shortcut.
`ResearchLogManagementVerification.Run` exercises these rules only in a unique temporary fixture,
including simultaneous recorder ownership, repeated exports, aborted/incomplete/legacy discovery
and preservation of unrelated recordings and exported copies.

`ResearchVerification.VerifyAssets` checks frozen profiles, constant Simple amplitudes and each Apple
finish boundary. `ResearchVerification.RunPlayMode` runs all 12 combinations with shorter temporary
observation windows, exercises repeated ON/OFF and passive single start, and verifies log/ZIP output.
These Editor checks do not validate physical IMU quality, motor timing, mobile controller mappings or
perceived haptic equivalence; those require the pre-experiment device tests.

`ResearchVerification.RunPreviewMode` runs the 12 combinations as previews, checks no-log mode and
purpose-separated files, then tests the real TCP receiver on Editor loopback: saved profile identity,
scale, offset, handshake and Stop, plus the original Generator draft transport. This validates the
transport implementation but does not claim physical USB/ADB or motor delivery on an Android device.

## Legacy architecture reference

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

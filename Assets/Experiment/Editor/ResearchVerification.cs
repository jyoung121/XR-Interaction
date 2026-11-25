using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Batch verification exercises real runtime states and exported records, not hardware claims.</summary>
[InitializeOnLoad]
public static class ResearchVerification
{
    private const string Flag = "XR.ResearchVerification";
    private static int index;
    private static GameObject runtime;
    private static ResearchTrialController trial;
    private static ScenarioPlaybackController player;
    private static double caseStarted, lastAction;
    private static int requestedStarts;
    private static ResearchMode mode;
    private static ScenarioId scenario;
    private static bool appleMediaAsserted;
    private static string beforeDirectory;
    private static bool previewRun;
    private static Task<string> transportTask;
    private static HapticLinkReceiver receiver;
    private static HapticProfile transportProfile;
    private static int transportStage;
    private static string receiverProfileEvent;
    private static string receiverChunkEvent;
    private static double receiverScheduled;
    private static MirrorScenarioAnimationDriver animationDriver;
    private static ActiveSceneContinuityVerification.WorldSnapshot world;
    static ResearchVerification()
    {
        EditorApplication.playModeStateChanged += state => {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Flag, false))
            {
                try { ActiveSceneContinuityVerification.Verify(); }
                catch (Exception exception) { Fail(exception); return; }
                ResearchLogRecorder.TestRoot = Path.Combine(Path.GetTempPath(), "XRResearchVerification", DateTime.UtcNow.ToString("yyyyMMdd_HHmmss"));
                index = 0;
                previewRun = SessionState.GetBool("XR.ResearchPreviewVerification", false);
                EditorApplication.update += Tick;
                Next();
            }
        };
    }
    public static void VerifyAssets()
    {
        ResearchExperimentSetup.ValidateResearch();
        foreach (var settings in ResearchExperimentSetup.Protocol.research.scenarios)
        {
            var profile = settings.scenario;
            foreach (var block in profile.animationBlocks)
            {
                if (profile.scenario != ScenarioId.Apple || block.kind != AnimationBlockKind.Contact) continue;
                double at = block.startSeconds + block.durationSeconds * .5;
                double end = ResearchTimelineRules.AppleFinishSeconds(profile, (float)at);
                var rest = profile.animationBlocks.Find(b => b.action == ScenarioAnimationAction.AppleRest && b.iteration == block.iteration);
                Require(rest != null && Math.Abs(end - rest.startSeconds - rest.durationSeconds) < .001,
                    "Apple finish must include all remaining chewing in the current iteration");
            }
            string before = ResearchLogRecorder.HapticHash(settings.soundBased);
            var copy = new ResearchScenarioSettings {
                scenario = settings.scenario, soundBased = settings.soundBased,
                simple = ScriptableObject.CreateInstance<HapticProfile>(),
                simpleAmplitude = settings.simpleAmplitude, pulseThreshold = settings.pulseThreshold,
                minimumPulseMs = settings.minimumPulseMs, mergeGapMs = settings.mergeGapMs,
                maximumPulseMs = settings.maximumPulseMs, interPulseMs = settings.interPulseMs
            };
            ResearchExperimentSetup.GenerateSimple(copy);
            Require(before == ResearchLogRecorder.HapticHash(settings.soundBased), "Generator modified frozen Sound-based");
            Require(copy.simple.Amplitudes.All(a => a == 0 || a == settings.simpleAmplitude), "Variable Simple amplitude");
            Require(copy.simple.DurationMs == settings.simple.DurationMs, "Non-deterministic Simple duration");
            UnityEngine.Object.DestroyImmediate(copy.simple);
        }
        Debug.Log("[ResearchVerification] Asset invariants and all Apple finish boundaries passed.");
    }
    public static void RunPlayMode()
    {
        VerifyAssets();
        SessionState.SetBool(Flag, true);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorApplication.isPlaying = true;
    }
    public static void RunPreviewMode()
    {
        SessionState.SetBool("XR.ResearchPreviewVerification", true);
        RunPlayMode();
    }
    public static void VerifyRecordingAndBuild()
    {
        VerifyAssets();
        ResearchLogRecorder.TestRoot = Path.Combine(Path.GetTempPath(), "XRResearchRecorderCheck", Guid.NewGuid().ToString("N"));
        var owner = new GameObject("RecorderCheck");
        try
        {
            var recorder = owner.AddComponent<ResearchLogRecorder>();
            Require(recorder.Begin(ResearchExperimentSetup.Protocol.research.scenarios[0], ResearchMode.Active, ResearchHaptic.None, 100, "editor_recorder_check"), "Recorder begin failed");
            recorder.Event("observation_start"); recorder.Event("on_request", "check"); recorder.Event("off_request", "check"); recorder.Event("nominal_end");
            owner.SendMessage("LateUpdate");
            recorder.End("completed", true);
            string directory = ResearchLogRecorder.LastDirectory;
            Require(string.IsNullOrEmpty(recorder.Error), "Recorder flush error");
            Require(File.ReadAllText(Path.Combine(directory, "head.csv")).Contains("recording_s,observation_s"), "Missing separate clock columns");
            Require(File.ReadAllText(Path.Combine(directory, "summary.json")).Contains("observedGyroHz"), "Missing native sampling summary");
            Require(File.ReadAllText(Path.Combine(directory, "metadata.json")).Contains("\"purpose\": \"Experiment\""), "Experiment log purpose missing");
            Require(ResearchLogRecorder.Export(directory).StartsWith("ZIP:"), "Export failed");
            Debug.Log("[ResearchVerification] Recorder clocks, final flush, sampling summary and ZIP checks passed.");
        }
        finally { UnityEngine.Object.DestroyImmediate(owner); ResearchLogRecorder.TestRoot = null; }
        ExperimentBuild.BuildUnifiedAndroidDevelopment();
    }
    private static void Next()
    {
        try
        {
            if (index >= 12)
            {
                if (previewRun) { StartTransportChecks(); return; }
                Debug.Log("[ResearchVerification] PASS: 12 runtime combinations, repeated active ON/OFF, one-shot passive, deadline + graceful finish, raw log files and ZIP export. Logs: " + ResearchLogRecorder.Root);
                SessionState.SetBool(Flag, false); EditorApplication.update -= Tick; EditorApplication.Exit(0); return;
            }
            scenario = index < 6 ? ScenarioId.Toothbrush : ScenarioId.Apple;
            int within = index % 6;
            mode = within < 3 ? ResearchMode.Active : ResearchMode.Passive;
            var haptic = (ResearchHaptic)(within % 3);
            var source = ResearchExperimentSetup.Protocol.research.Find(scenario);
            var settings = new ResearchScenarioSettings { scenario = source.scenario, simple = source.simple,
                soundBased = source.soundBased, frozenSoundBasedHash = source.frozenSoundBasedHash,
                observationSeconds = scenario == ScenarioId.Toothbrush && mode == ResearchMode.Active ? 5f : 1f };
            if (Camera.main == null) { var camera = new GameObject("VerificationCamera").AddComponent<Camera>(); camera.tag = "MainCamera"; camera.gameObject.AddComponent<AudioListener>(); }
            runtime = new GameObject("Verification " + index);
            runtime.AddComponent<AudioSource>(); runtime.AddComponent<LongHapticPlayer>();
            var driver = runtime.AddComponent<MirrorScenarioAnimationDriver>();
            animationDriver = driver;
            driver.viewer = Camera.main.transform;
            player = runtime.AddComponent<ScenarioPlaybackController>(); player.animationDriver = driver;
            trial = runtime.AddComponent<ResearchTrialController>();
            beforeDirectory = ResearchLogRecorder.LastDirectory;
            trial.Initialize(settings, mode, haptic, new ResearchExperimentSettings { requireController = false }, player,
                previewRun ? ResearchRecordingPurpose.ScenarioPreview : ResearchRecordingPurpose.Experiment, previewRun && index != 0);
            world = new ActiveSceneContinuityVerification.WorldSnapshot(driver);
            caseStarted = EditorApplication.timeSinceStartup; lastAction = -100; requestedStarts = 0; appleMediaAsserted = false;
            Debug.Log("[ResearchVerification] Case " + index + " " + scenario + "/" + mode + "/" + haptic);
        }
        catch (Exception exception) { Fail(exception); }
    }
    private static void Tick()
    {
        if (trial == null) return;
        try
        {
            if (EditorApplication.timeSinceStartup - caseStarted > 35) throw new Exception("Runtime case timed out: " + index);
            if (trial.Phase != ResearchTrialPhase.Complete && trial.Phase != ResearchTrialPhase.Aborted)
                world.AssertStable(animationDriver);
            if (mode == ResearchMode.Active && trial.Phase == ResearchTrialPhase.Idle && requestedStarts < 2 && trial.Remaining > .3)
            {
                Invoke("Activate", "verification_input"); requestedStarts++; lastAction = EditorApplication.timeSinceStartup;
            }
            if (mode == ResearchMode.Active && trial.Phase == ResearchTrialPhase.Acting && EditorApplication.timeSinceStartup - lastAction > .5)
            { Invoke("Deactivate", "verification_input"); lastAction = EditorApplication.timeSinceStartup; }
            if (scenario == ScenarioId.Apple && player.ResearchFinishing && !appleMediaAsserted)
            {
                Require(player.IsPlaying && player.State != ScenarioPlaybackState.Finishing,
                    "Apple must continue the original synchronized timeline after OFF");
                appleMediaAsserted = true;
            }
            if (trial.Phase == ResearchTrialPhase.Aborted) throw new Exception("Trial aborted: " + trial.Recorder.Status);
            if (trial.Phase != ResearchTrialPhase.Complete) return;
            if (previewRun && index == 0)
            {
                Require(ResearchLogRecorder.LastDirectory == beforeDirectory && !trial.Recorder.Recording && trial.Recorder.HeadSamples == 0,
                    "No-log preview created a recording");
                UnityEngine.Object.DestroyImmediate(runtime); index++; trial = null; Next(); return;
            }
            string directory = ResearchLogRecorder.LastDirectory;
            foreach (string name in new[] { "metadata.json", "head.csv", "imu.csv", "events.csv", "summary.json" })
                Require(File.Exists(Path.Combine(directory, name)), "Missing log " + name);
            string events = File.ReadAllText(Path.Combine(directory, "events.csv"));
            int starts = events.Split(new[] { ",\"on_request\"," }, StringSplitOptions.None).Length - 1;
            Require(starts == (mode == ResearchMode.Passive ? 1 : requestedStarts), "Unexpected number of ON requests");
            Require(events.Contains("nominal_end") && events.Contains("recording_end"), "Missing completion markers");
            Require(trial.Recorder.HeadSamples > 0, "No head samples");
            string purpose = previewRun ? "ScenarioPreview" : "Experiment";
            Require(File.ReadAllText(Path.Combine(directory, "metadata.json")).Contains("\"purpose\": \"" + purpose + "\""), "Incorrect log purpose");
            if (scenario == ScenarioId.Apple) Require(appleMediaAsserted, "Apple OFF path not exercised");
            if (scenario == ScenarioId.Toothbrush && mode == ResearchMode.Active) Require(requestedStarts == 2, "Repeated activation not exercised");
            if (index == 0) Require(ResearchLogRecorder.Export(directory).StartsWith("ZIP:"), "ZIP export failed");
            UnityEngine.Object.DestroyImmediate(runtime);
            index++; trial = null; Next();
        }
        catch (Exception exception) { Fail(exception); }
    }
    private static void Invoke(string method, string value) => typeof(ResearchTrialController)
        .GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(trial, new object[] { value });
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void StartTransportChecks()
    {
        EditorApplication.update -= Tick;
        runtime = new GameObject("TransportVerification");
        runtime.AddComponent<LongHapticPlayer>();
        receiver = runtime.AddComponent<HapticLinkReceiver>();
        receiver.PreviewEvent += (kind, value) => {
            if (kind == "pc_profile") receiverProfileEvent = value;
            if (kind == "pc_chunk_loaded") receiverChunkEvent = value;
            if (kind == "pc_profile_scheduled") receiverScheduled = double.Parse(value.Substring("start_dsp=".Length), System.Globalization.CultureInfo.InvariantCulture);
        };
        receiver.StartListening();
        Require(receiver.Running, "Local receiver did not open");
        transportProfile = ScriptableObject.CreateInstance<HapticProfile>();
        transportProfile.SetWaveform(new[] { 80, 40, 120 }, new[] { 100, 0, 100 }, HapticProfileOrigin.Manual, "transport-test", 3);
        transportProfile.defaultAmplitudeScale = .5f;
        transportProfile.hapticOffsetMs = 80;
        transportStage = 0; caseStarted = EditorApplication.timeSinceStartup;
        transportTask = Task.Run(HapticTransportClient.CheckReceiver);
        EditorApplication.update += TransportTick;
    }
    private static void TransportTick()
    {
        try
        {
            if (EditorApplication.timeSinceStartup - caseStarted > 30) throw new Exception("Transport test timed out");
            if (!transportTask.IsCompleted) return;
            string response = transportTask.GetAwaiter().GetResult();
            if (transportStage == 0)
            {
                Require(response.Contains("protocol=2"), "Receiver handshake failed");
                transportStage++; lastAction = AudioSettings.dspTime;
                transportTask = Task.Run(() => HapticTransportClient.Play(transportProfile)); return;
            }
            if (transportStage == 1)
            {
                Require(response.Contains("Playing profile"), "Saved profile did not play");
                Require(receiverProfileEvent.Contains("transport-test") && receiverProfileEvent.Contains("\"profileVersion\":3") &&
                    receiver.LastProfile.Contains(ResearchLogRecorder.HapticHash(transportProfile)), "Saved profile identity missing");
                Require(receiverScheduled >= lastAction + .32, "Positive offset not applied");
                Require(receiverChunkEvent.Contains("\"amplitudes\":[50,0,50]"), "Saved profile scale was not applied");
                Require(HapticPreviewTiming.StartDsp(10, -80) == 10.25, "Negative offset must preserve full waveform");
                transportStage++; transportTask = Task.Run(HapticTransportClient.Stop); return;
            }
            if (transportStage == 2)
            {
                Require(!receiver.Playing, "Stop did not stop receiver");
                transportStage++; transportTask = Task.Run(() => HapticTransportClient.Play("legacy-draft", new long[] { 50, 50 }, new[] { 100, 0 })); return;
            }
            Require(response.Contains("Playing profile") && receiver.LastProfile.Contains("legacy-draft"), "Existing generator draft playback regressed");
            receiver.StopListening();
            UnityEngine.Object.DestroyImmediate(runtime); UnityEngine.Object.DestroyImmediate(transportProfile);
            EditorApplication.update -= TransportTick;
            SessionState.SetBool(Flag, false); SessionState.SetBool("XR.ResearchPreviewVerification", false);
            Debug.Log("[PreviewVerification] PASS: 12 preview combinations, no-log preview, separated purpose, saved profile identity/offset, receiver handshake/stop and legacy draft transport.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { EditorApplication.update -= TransportTick; Fail(exception); }
    }
    private static void Fail(Exception exception)
    {
        Debug.LogException(exception); SessionState.SetBool(Flag, false); SessionState.SetBool("XR.ResearchPreviewVerification", false); EditorApplication.update -= Tick; EditorApplication.Exit(1);
    }
}

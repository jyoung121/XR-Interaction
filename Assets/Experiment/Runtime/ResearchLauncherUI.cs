using System;
using System.IO;
using UnityEngine;

/// <summary>Landscape research launcher; no participant tuning or runtime intensity controls.</summary>
public sealed class ResearchLauncherUI : MonoBehaviour
{
    public ExperimentProtocol protocol;
    private enum Page { Home, Setup, Confirm, Controller, Sensor, Logs, Link, Loading, HapticTest }
    private Page page;
    private ResearchScenarioSettings selected;
    private ResearchMode? mode;
    private ResearchHaptic? haptic;
    private string message = "";
    private Vector2 scroll;
    private GUIStyle title, text, button, box;
    private MobileAppFlowController flow;
    private ResearchLogRecorder testRecorder;
    private LongHapticPlayer testHaptics;
    private HapticLinkReceiver receiver;
    private bool scenarioPreview;
    private bool recordPreview;
    private readonly ResearchLogBrowser logBrowser = new ResearchLogBrowser();

    private void Start()
    {
        flow = MobileAppFlowController.GetOrCreate();
        message = flow.ConsumeLauncherMessage();
        var destination = flow.ConsumeLauncherDestination();
        scenarioPreview = destination == MobileLauncherDestination.ScenarioPreview;
        page = scenarioPreview || destination == MobileLauncherDestination.ExperimentSetup ? Page.Setup : Page.Home;
        if (scenarioPreview) RestorePreview();
        flow.PrepareLauncher();
        testHaptics = GetComponent<LongHapticPlayer>();
        receiver = GetComponent<HapticLinkReceiver>();
        testRecorder = gameObject.AddComponent<ResearchLogRecorder>();
        testRecorder.Failed += error => { testHaptics?.Stop(); receiver?.StopListening(); message = "LOG ERROR: " + error; };
        if (receiver != null) receiver.PreviewEvent += OnReceiverEvent;
    }
    private void Update()
    {
        ResearchController.Refresh();
        if (page == Page.Controller) ResearchController.TestButtons();
        if (page == Page.Loading && !flow.IsBusy && !flow.IsExperimentActive)
        { message = flow.ConsumeLauncherMessage(); page = Page.Setup; }
    }
    private void Styles()
    {
        if (text != null) return;
        text = new GUIStyle(GUI.skin.label) { fontSize = 25, wordWrap = true, fixedHeight = 0,
            padding = new RectOffset(8, 8, 7, 7), margin = new RectOffset(4, 4, 5, 5) };
        title = new GUIStyle(text) { fontSize = 34, fontStyle = FontStyle.Bold };
        button = new GUIStyle(GUI.skin.button) { fontSize = 25, wordWrap = true, fixedHeight = 0,
            padding = new RectOffset(16, 16, 14, 14), margin = new RectOffset(6, 6, 7, 7) };
        box = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 14, 14), fixedHeight = 0 };
    }
    private void Label(string value) => GUILayout.Label(value, text);
    private bool Button(string value) => GUILayout.Button(value, button, GUILayout.MinHeight(65));
    private void OnGUI()
    {
        if (protocol == null || !protocol.research.enabled) return;
        Styles();
        Rect safe = Screen.safeArea;
        float scale = Mathf.Max(.1f, safe.width / 1280f);
        Matrix4x4 previous = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(new Vector3(safe.x, Screen.height - safe.yMax, 0), Quaternion.identity, new Vector3(scale, scale, 1));
        GUILayout.BeginArea(new Rect(22, 20, 1236, safe.height / scale - 40), box);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label(page == Page.Home ? "MOBILE LAUNCHER" : page == Page.Setup && scenarioPreview ?
            "SCENARIO PREVIEW" : page == Page.Link ? "PC HAPTIC RECEIVER" : page == Page.Logs ? "SAVED LOGS / EXPORT" : page.ToString().ToUpperInvariant(), title);
        if (!string.IsNullOrEmpty(message)) Label(message);
        switch (page)
        {
            case Page.Home:
                if (Button("EXPERIMENT SETUP")) { scenarioPreview = false; mode = null; haptic = null; selected = null; Go(Page.Setup); }
                if (Button("HAPTIC TEST")) Go(Page.HapticTest);
                if (Button("CONTROLLER CHECK")) Go(Page.Controller);
                if (Button("SENSOR / LOGGING TEST")) Go(Page.Sensor);
                if (Button("SAVED LOGS / EXPORT")) Go(Page.Logs);
                break;
            case Page.Setup: Setup(); break;
            case Page.Confirm: Confirmation(); break;
            case Page.Controller: Controller(); break;
            case Page.Sensor: SensorTest(); break;
            case Page.Logs: Logs(); break;
            case Page.HapticTest:
                Label("Two playback paths: complete VR scenarios, or a waveform sent from the PC.");
                if (Button("SCENARIO PREVIEW - 12 CONDITIONS")) { scenarioPreview = true; RestorePreview(); Go(Page.Setup); }
                if (Button("PC HAPTIC RECEIVER - ADB FORWARD")) { Go(Page.Link); receiver?.StartListening(); }
                Back();
                break;
            case Page.Link: ReceiverPage(); break;
            case Page.Loading: Label(flow.Status); break;
        }
        GUILayout.EndScrollView(); GUILayout.EndArea(); GUI.matrix = previous;
    }
    private void Go(Page next)
    {
        if (page == Page.Logs) logBrowser.CancelConfirmation();
        page = next; scroll = Vector2.zero;
        if (next == Page.Logs) { message = ""; logBrowser.Refresh(); }
    }
    private void RestorePreview()
    {
        selected = flow.LastPreviewSettings;
        mode = selected == null ? (ResearchMode?)null : flow.LastPreviewMode;
        haptic = selected == null ? (ResearchHaptic?)null : flow.LastPreviewHaptic;
        recordPreview = selected != null && flow.LastPreviewRecord;
    }
    private void OnReceiverEvent(string kind, string value)
    {
        if (page == Page.Link) testRecorder?.Event(kind, value);
    }
    private void Back()
    {
        if (!Button("BACK TO LAUNCHER")) return;
        StopTest(); receiver?.StopListening(); Go(Page.Home);
    }
    private void ScenarioButtons()
    {
        foreach (ScenarioId id in new[] { ScenarioId.Toothbrush, ScenarioId.Apple })
        {
            var entry = protocol.research.Find(id);
            if (Button((selected == entry && entry != null ? "[SELECTED] " : "") + id)) selected = entry;
        }
    }
    private void HapticButtons()
    {
        foreach (ResearchHaptic value in Enum.GetValues(typeof(ResearchHaptic)))
            if (Button((haptic == value ? "[SELECTED] " : "") + HapticName(value))) haptic = value;
    }
    private static string HapticName(ResearchHaptic value) => value == ResearchHaptic.None ? "NO HAPTIC" :
        value == ResearchHaptic.Simple ? "SIMPLE PULSE" : "SOUND-BASED";
    private void Setup()
    {
        Label(scenarioPreview ? "Preview uses the same ScenarioProfile, playback and controller rules as the experiment." :
            "Choose all three factors for every trial. 2 scenarios x 2 modes x 3 haptics.");
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(box, GUILayout.Width(385)); Label("1. SCENARIO"); ScenarioButtons(); GUILayout.EndVertical();
        GUILayout.BeginVertical(box, GUILayout.Width(385)); Label("2. MODE");
        foreach (ResearchMode value in Enum.GetValues(typeof(ResearchMode)))
            if (Button((mode == value ? "[SELECTED] " : "") + value)) mode = value;
        GUILayout.EndVertical();
        GUILayout.BeginVertical(box, GUILayout.Width(385)); Label("3. HAPTIC"); HapticButtons(); GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        Label("Controller: " + (ResearchController.Ready ? "READY" : ResearchController.Description + "; check A/B before running"));
        Label(selected == null ? "Select a scenario." : "Observation: " + selected.observationSeconds + " s + graceful finish");
        if (selected?.scenario != null)
        {
            Label("ScenarioProfile: " + selected.scenario.name + " / v" + selected.scenario.Version);
            var profile = haptic.HasValue ? selected.Resolve(haptic.Value) : null;
            Label("HapticProfile: " + (profile != null ? profile.name + " / v" + profile.Version : "None"));
        }
        if (scenarioPreview && Button("PREVIEW LOGGING: " + (recordPreview ? "ON" : "OFF"))) recordPreview = !recordPreview;
        bool ready = SelectionReady(out string reason);
        Label(ready ? "Ready for confirmation." : reason);
        GUI.enabled = ready;
        if (Button("CONTINUE")) Go(Page.Confirm);
        GUI.enabled = true;
        if (Button("CONTROLLER CHECK")) Go(Page.Controller);
        Back();
    }
    private bool SelectionReady(out string reason)
    {
        if (selected == null || !mode.HasValue || !haptic.HasValue)
        { reason = "Select scenario, mode and haptic."; return false; }
        if (protocol.research.requireController && !ResearchController.Ready)
        { reason = "Open Controller Check and press A and B on the connected Gamepad."; return false; }
        if (!selected.Validate(out reason)) return false;
        if (!string.IsNullOrEmpty(selected.frozenSoundBasedHash) &&
            ResearchLogRecorder.HapticHash(selected.soundBased) != selected.frozenSoundBasedHash)
        { reason = "Frozen Sound-based profile has changed. Review configuration on PC."; return false; }
        try { if (!scenarioPreview || recordPreview) Directory.CreateDirectory(ResearchLogRecorder.Root); }
        catch (Exception exception) { reason = "Log storage unavailable: " + exception.Message; return false; }
        reason = ""; return true;
    }
    private void Confirmation()
    {
        Label(selected?.scenario == null ? "Missing scenario" : selected.scenario.scenario + " / " + mode + " / " + HapticName(haptic ?? ResearchHaptic.None));
        Label(scenarioPreview ? "PREVIEW ONLY. Logging: " + (recordPreview ? "ON - tagged ScenarioPreview" : "OFF - no IMU recording required") :
            "EXPERIMENT. Logs are recorded automatically.");
        Label("Observation time is fixed; current chewing finishes after timeout.");
        Label(mode == ResearchMode.Active ? "A: activate/deactivate repeatedly. B: hold 1 second to abort." :
            "Starts automatically once. No automatic restart. B: hold 1 second to abort.");
        bool ready = SelectionReady(out string reason);
        if (!ready) Label(reason);
        GUI.enabled = ready;
        if (Button(scenarioPreview ? "START SCENARIO PREVIEW" : "START TRIAL"))
        {
            StopTest(); receiver?.StopListening();
            if (flow.LaunchResearch(selected, mode.Value, haptic.Value,
                scenarioPreview ? ResearchRecordingPurpose.ScenarioPreview : ResearchRecordingPurpose.Experiment,
                !scenarioPreview || recordPreview, out string launchReason)) Go(Page.Loading);
            else message = launchReason;
        }
        GUI.enabled = true;
        if (Button("BACK TO SETUP")) Go(Page.Setup);
    }
    private void Controller()
    {
        Label(ResearchController.Description);
        Label("App-level input recognition, not Android Bluetooth pairing status.");
        Label("A / South: " + (ResearchController.AChecked ? "RECEIVED" : "press now"));
        Label("B / East: " + (ResearchController.BChecked ? "RECEIVED" : "press now"));
        Label("Buttons on this screen never start or abort a trial.");
        Label(ResearchController.Ready ? "READY FOR EXPERIMENT" : "NOT READY");
        if (Button("BACK TO SETUP")) Go(Page.Setup);
        Back();
    }
    private void SensorTest()
    {
        Label("IMU-only capture. For capture during vibration, open Haptic Test > PC Haptic Receiver and enable recording there.");
        GUI.enabled = !testRecorder.Recording;
        if (Button("START SENSOR RECORDING"))
        {
            if (testRecorder.Begin(null, ResearchMode.Passive, ResearchHaptic.None, protocol.research.requestedImuHz,
                    "sensor_test", ResearchRecordingPurpose.SensorTest)) testRecorder.Phase = "SensorTest";
            else message = testRecorder.Error;
        }
        GUI.enabled = true;
        RecordingStatus();
        if (Button("STOP AND SAVE")) StopTest();
        Back();
    }
    private void RecordingStatus()
    {
        Label(testRecorder.Status);
        Label("Elapsed " + testRecorder.Elapsed.ToString("F1") + " s / head " + testRecorder.HeadSamples + " / raw IMU " + testRecorder.ImuSamples);
        Label("Observed average: gyro " + testRecorder.GyroHz.ToString("F1") + " Hz / accel " + testRecorder.AccelHz.ToString("F1") + " Hz (Editor: no physical IMU)");
    }
    private void ReceiverPage()
    {
        Label("PC: Tools > Haptics > Haptic Profile Player (or existing Generator draft playback). ADB Forward > Play on Android.");
        Label(receiver != null ? receiver.Status : "Receiver unavailable");
        if (receiver != null)
        {
            Label((receiver.Playing ? "PLAYING / SCHEDULED: " : "IDLE / last profile: ") + receiver.LastProfile);
            if (!receiver.Running && Button("START RECEIVER")) receiver.StartListening();
        }
        if (Button("STOP HAPTICS")) testHaptics?.Stop();
        if (!testRecorder.Recording && Button("START OPTIONAL IMU RECORDING"))
        {
            testHaptics?.Stop();
            if (testRecorder.Begin(null, ResearchMode.Passive, ResearchHaptic.None, protocol.research.requestedImuHz,
                    "direct_haptic_test", ResearchRecordingPurpose.DirectHapticTest)) testRecorder.Phase = "PCReceiver";
            else message = testRecorder.Error;
        }
        RecordingStatus();
        if (testRecorder.Recording && Button("STOP RECORDING AND SAVE")) StopTest();
        if (Button("BACK TO HAPTIC TEST")) { StopTest(); receiver?.StopListening(); Go(Page.HapticTest); }
        Back();
    }
    private void StopTest()
    {
        testHaptics?.Stop();
        if (testRecorder != null && testRecorder.Recording)
            testRecorder.End(page == Page.Link ? "direct_haptic_test_complete" : "sensor_test_complete", true);
    }
    private void Logs()
    {
        logBrowser.Draw(text, button, box, value => message = value);
        Back();
    }
    private void OnDisable() { StopTest(); receiver?.StopListening(); if (receiver != null) receiver.PreviewEvent -= OnReceiverEvent; }
}

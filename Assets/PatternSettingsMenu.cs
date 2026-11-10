using System.Collections;
using System.Globalization;
using UnityEngine;

/// <summary>
/// 진동 값을 폰에서 직접 고치는 평면 화면.
///
/// 아이폰은 빌드 한 번에 30~40분이 걸린다. 값을 하나 바꿔 보려고 그 시간을 쓰는 대신,
/// 앱 안에서 숫자를 쳐 넣고 Preview 로 바로 느껴 보게 한다.
/// 조원이 안드로이드에 만들어 둔 ExperimentSettingsMenu 와 같은 생각이고,
/// 필드 이름만 이쪽 ExperimentController 에 맞췄다.
///
/// 이 화면이 떠 있는 동안에는 컨트롤러가 Cardboard XR 을 꺼 둔다. XR 모드에서는
/// 화면이 둘로 갈라져 글씨를 읽기 어렵고 터치·키보드가 제대로 먹지 않는다.
/// 그래서 여기서는 Api 트리거를 쓰지 않고 IMGUI 버튼만 쓴다 — XR 이 꺼져 있어도,
/// 다시 켜는 데 실패해도 버튼은 눌린다.
/// </summary>
public class PatternSettingsMenu : MonoBehaviour
{
    public enum SettingsPage { Home, PatternPreview, ControllerSetup, MirrorSceneSelect, PcHapticLink }

    public bool Visible { get; set; }
    public bool Done { get; private set; }
    public bool RunMirrorScene { get; private set; }
    public bool StartExperiment { get; private set; }
    public MirrorRig.Scenario SelectedMirrorScenario { get; private set; }
    public SettingsPage Page { get; private set; }

    private ExperimentController controller;
    private HapticLinkReceiver hapticLink;
    private VibrationPattern selected = VibrationPattern.SinglePulse;
    private Vector2 scroll;
    private string error = "";

    private string envDuration, envSample, envModulation;
    private string stepMs, gapMs, highAmp, lowAmp, pulseMs, repeatMs;
    private string previewRepeatCount = "1";
    private string minInterval, maxInterval, near, far;
    private float sharpness;             // 0~1. iOS 전용 — 안드로이드는 무시한다
    private Coroutine previewRoutine;

    public void Initialize(ExperimentController experiment)
    {
        controller = experiment;
        hapticLink = GetComponent<HapticLinkReceiver>();
        if (hapticLink == null) hapticLink = gameObject.AddComponent<HapticLinkReceiver>();
        RefreshFromController();
    }

    /// <summary>화면을 띄운다. 컨트롤러가 XR 을 끈 뒤에 부른다.</summary>
    public void Begin()
    {
        StopBuzz();
        RefreshFromController();
        Done = false;
        RunMirrorScene = false;
        StartExperiment = false;
        SelectedMirrorScenario = MirrorRig.Scenario.None;
        Page = SettingsPage.Home;
        Visible = true;
    }

    public void BeginMirrorSelection()
    {
        Begin();
        Page = SettingsPage.MirrorSceneSelect;
    }

    public void ShowError(string message)
    {
        error = message;
        Done = false;
        Visible = true;
    }

    public void RefreshFromController()
    {
        var ci = CultureInfo.InvariantCulture;
        selected = controller.previewPattern;
        envDuration = controller.envelopeDurationMs.ToString(ci);
        envSample = controller.envelopeSampleMs.ToString(ci);
        envModulation = controller.envelopeModulationHz.ToString(ci);
        stepMs = controller.waveStepMs.ToString(ci);
        gapMs = controller.waveGapMs.ToString(ci);
        highAmp = controller.waveHighAmplitude.ToString(ci);
        lowAmp = controller.waveLowAmplitude.ToString(ci);
        pulseMs = controller.pulseDurationMs.ToString(ci);
        repeatMs = ((int)controller.patternRepeatMs).ToString(ci);
        minInterval = ((int)controller.minIntervalMs).ToString(ci);
        maxInterval = ((int)controller.maxIntervalMs).ToString(ci);
        near = ((int)controller.intensityNear).ToString(ci);
        far = ((int)controller.intensityFar).ToString(ci);
        sharpness = Mathf.Clamp01(HapticManager.Sharpness);
        error = "";
    }

    private void StopBuzz()
    {
        StopPreviewPlayback();
    }

    private void OnDisable()
    {
        StopBuzz();
    }

    private void OnGUI()
    {
        if (!Visible || controller == null) return;

        Matrix4x4 oldMatrix = GUI.matrix;
        int oldLabel = GUI.skin.label.fontSize;
        int oldButton = GUI.skin.button.fontSize;
        int oldField = GUI.skin.textField.fontSize;

        float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 960f, Screen.height / 540f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.skin.label.fontSize = 17;
        GUI.skin.button.fontSize = 17;
        GUI.skin.textField.fontSize = 17;

        GUI.Box(new Rect(0, 0, width, height), "");
        GUILayout.BeginArea(new Rect(24, 14, width - 48, height - 28));

        DrawHeader();
        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
        switch (Page)
        {
            case SettingsPage.PatternPreview: DrawPatternPreview(); break;
            case SettingsPage.ControllerSetup: DrawControllerSetup(); break;
            case SettingsPage.MirrorSceneSelect: DrawMirrorSceneSelect(); break;
            case SettingsPage.PcHapticLink: DrawPcHapticLink(); break;
            default: DrawHome(); break;
        }
        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);
        if (Page != SettingsPage.Home && GUILayout.Button("Back", GUILayout.Height(44)))
        {
            StopBuzz();
            if (hapticLink != null) hapticLink.StopListening();
            Page = SettingsPage.Home;
            scroll = Vector2.zero;
        }

        GUILayout.EndArea();

        GUI.skin.label.fontSize = oldLabel;
        GUI.skin.button.fontSize = oldButton;
        GUI.skin.textField.fontSize = oldField;
        GUI.matrix = oldMatrix;
    }

    private void DrawHeader()
    {
        GUILayout.Label("XR Haptic Pilot");
        GUILayout.Label(Page == SettingsPage.Home ? "Settings and test tools" : Page.ToString());
    }

    private void DrawHome()
    {
        GUILayout.Space(12);
        if (GUILayout.Button("Preview Pattern", GUILayout.Height(54))) OpenPage(SettingsPage.PatternPreview);
        if (GUILayout.Button("Controller Setup", GUILayout.Height(54))) OpenPage(SettingsPage.ControllerSetup);
        if (GUILayout.Button("Mirror Scene", GUILayout.Height(54))) OpenPage(SettingsPage.MirrorSceneSelect);
        if (GUILayout.Button("Test Haptic Directly on PC", GUILayout.Height(54))) OpenPage(SettingsPage.PcHapticLink);

        GUILayout.Space(18);
        GUILayout.Label("Automatic startup vibration tests and Compare all 10 are disabled.");
        if (GUILayout.Button("Start Experiment", GUILayout.Height(58)) && Apply())
        {
            StopBuzz();
            StartExperiment = true;
            Done = true;
        }
    }

    private void DrawPatternPreview()
    {
        GUILayout.Label("Choose one of the 10 patterns, edit its parameters, then preview it.");
        GUILayout.Space(8);
        GUILayout.BeginHorizontal();
        Choice("Pulse", VibrationPattern.SinglePulse);
        Choice("Ramp up/down", VibrationPattern.RampUpDown);
        Choice("Bounce", VibrationPattern.Bounce);
        Choice("Wobble", VibrationPattern.Wobble);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Choice("Sine", VibrationPattern.SineEnvelope);
        Choice("Triangle", VibrationPattern.TriangleEnvelope);
        Choice("Sawtooth", VibrationPattern.SawtoothEnvelope);
        Choice("Square", VibrationPattern.SquareEnvelope);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Choice("Exp. decay", VibrationPattern.ExponentialDecay);
        Choice("Damped oscillation", VibrationPattern.DampedOscillation);
        GUILayout.EndHorizontal();

        GUILayout.Space(12);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Play this pattern", GUILayout.Height(48))) Preview();
        GUILayout.Space(12);
        GUILayout.Label("Repeat count  1-100", GUILayout.Width(180));
        previewRepeatCount = GUILayout.TextField(
            previewRepeatCount, GUILayout.Width(90), GUILayout.Height(40));
        GUILayout.EndHorizontal();

        GUILayout.Space(12);
        Field("Envelope duration (ms)  20-2000", ref envDuration);
        Field("Sample step (ms)  10-100", ref envSample);
        Field("Modulation (Hz)  0.5-50", ref envModulation);
        Field("Wave segment (ms)  1-2000", ref stepMs);
        Field("Silent gap (ms)  1-2000", ref gapMs);
        Field("Peak strength  1-255", ref highAmp);
        Field("Low strength  1-255", ref lowAmp);
        Field("Pulse duration (ms)  1-2000", ref pulseMs);
        Field("Post-pattern gap (ms)  20-5000", ref repeatMs);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Sharpness (iOS)  " + sharpness.ToString("0.00"), GUILayout.Width(220));
        sharpness = GUILayout.HorizontalSlider(sharpness, 0f, 1f, GUILayout.Width(260));
        GUILayout.EndHorizontal();
        GUILayout.Label("Android ignores sharpness. Intensity uses the 1-255 amplitude fields above.");
        GUILayout.Label("Haptics: " + HapticManager.Diagnostics);
    }

    private void DrawControllerSetup()
    {
        ControllerConnectionDiagnostics d = ControllerConnectionDiagnostics.Instance;
        GUILayout.Label(d != null && d.Connected ? "Controller: CONNECTED" : "Controller: NOT CONNECTED");
        GUILayout.Space(6);
        GUILayout.Label(d != null ? d.DeviceSummary : "Diagnostics service is starting...");
        if (d == null) return;

        Vector2 stick = d.StickValue;
        GUILayout.Label($"Left stick: ({stick.x:F2}, {stick.y:F2})");
        GUILayout.Label($"South [{Mark(d.SouthPressed)}]   North [{Mark(d.NorthPressed)}]   " +
                        $"West [{Mark(d.WestPressed)}]   East [{Mark(d.EastPressed)}]");
        GUILayout.Space(10);
        GUILayout.Label("Recent controller input");
        foreach (string entry in d.RecentLogs) GUILayout.Label(entry);
        GUILayout.Space(10);
        GUILayout.Label("Android mapping: South=play/pause, West=previous, North=next, East=exit.");
        GUILayout.Label("Only leftStick Vector2 is used; its synthetic up/down/left/right children are ignored.");
    }

    private void DrawMirrorSceneSelect()
    {
        GUILayout.Label("Choose one scene. The controller changes playback, pattern, and strength inside it.");
        GUILayout.Space(12);
        if (GUILayout.Button("Hair Clipper", GUILayout.Height(58))) StartMirror(MirrorRig.Scenario.Clipper);
        if (GUILayout.Button("Eating", GUILayout.Height(58))) StartMirror(MirrorRig.Scenario.Food);
        if (GUILayout.Button("Dental Drill", GUILayout.Height(58))) StartMirror(MirrorRig.Scenario.Dental);
        GUILayout.Space(10);
        GUILayout.Label("South: play/pause   West/North: pattern -/+   East: exit");
        GUILayout.Label("Left stick up/down: strength +/- 5");
        GUILayout.Label("Left stick left/right: post-pattern gap -/+ 20 ms");
    }

    private void DrawPcHapticLink()
    {
        GUILayout.Label("PC Haptic Link");
        GUILayout.Label(hapticLink == null ? "Receiver unavailable" : hapticLink.Status);
        GUILayout.Label(hapticLink != null && hapticLink.Connected ? "PC: CONNECTED" : "PC: waiting for a connection");
        GUILayout.Space(8);
        GUILayout.Label("ANDROID (USB): enable USB debugging, then press ADB Forward in Unity.");
        GUILayout.Label("IPHONE (Wi-Fi): turn on Personal Hotspot and join the PC to it.");
        GUILayout.Label("Then set Host in Unity to 172.20.10.1 (or the address above).");
        GUILayout.Space(4);
        GUILayout.Label("In Unity: Tools > Haptic Waveform Designer, then Play on Phone.");
        GUILayout.Label("The receiver stops when you leave this page.");
        // 이 화면의 글자는 반드시 ASCII 여야 한다 — 내장 글꼴에 한글 글리프가 없어
        // 한글은 빈 칸으로 보인다.
    }

    private void OpenPage(SettingsPage page)
    {
        if (hapticLink != null && Page == SettingsPage.PcHapticLink)
            hapticLink.StopListening();
        Page = page;
        if (hapticLink != null && Page == SettingsPage.PcHapticLink)
            hapticLink.StartListening();
        scroll = Vector2.zero;
        error = "";
    }

    private void StartMirror(MirrorRig.Scenario scenario)
    {
        if (!Apply()) return;
        StopBuzz();
        SelectedMirrorScenario = scenario;
        RunMirrorScene = true;
        Done = true;
    }

    private static string Mark(bool value) { return value ? "ON" : "--"; }

    private void Choice(string label, VibrationPattern value)
    {
        if (GUILayout.Button((selected == value ? "[x] " : "[ ] ") + label, GUILayout.Height(42)))
            selected = value;
    }

    private static void Field(string label, ref string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(330));
        value = GUILayout.TextField(value, GUILayout.Width(150), GUILayout.Height(34));
        GUILayout.EndHorizontal();
    }

    /// <summary>입력한 값을 컨트롤러에 쓴다. 하나라도 범위를 벗어나면 아무것도 쓰지 않는다.</summary>
    private bool Apply()
    {
        int duration, sample, step, gap, high, low, pulse, repeat, lo, hi, n, f;
        float modulation;

        if (!Read(envDuration, 20, 2000, out duration) ||
            !Read(envSample, 10, 100, out sample) ||
            !Read(envModulation, 0.5f, 50f, out modulation) ||
            !Read(stepMs, 1, 2000, out step) ||
            !Read(gapMs, 1, 2000, out gap) ||
            !Read(highAmp, 1, 255, out high) ||
            !Read(lowAmp, 1, 255, out low) || low > high ||
            !Read(pulseMs, 1, 2000, out pulse) ||
            !Read(repeatMs, 20, 5000, out repeat) ||
            !Read(minInterval, 1, 10000, out lo) ||
            !Read(maxInterval, 1, 10000, out hi) || hi < lo ||
            !Read(near, 1, 255, out n) ||
            !Read(far, 1, 255, out f) || f > n)
        {
            error = "Check the ranges printed next to each box. " +
                    "Also: low <= peak, min <= max, far <= near.";
            return false;
        }

        error = "";
        controller.previewPattern = selected;
        controller.envelopeDurationMs = duration;
        controller.envelopeSampleMs = sample;
        controller.envelopeModulationHz = modulation;
        controller.waveStepMs = step;
        controller.waveGapMs = gap;
        controller.waveHighAmplitude = high;
        controller.waveLowAmplitude = low;
        controller.pulseDurationMs = pulse;
        controller.patternRepeatMs = repeat;
        controller.minIntervalMs = lo;
        controller.maxIntervalMs = hi;
        controller.intensityNear = n;
        controller.intensityFar = f;

        // 슬라이더라 범위를 벗어날 수 없다. 패턴 재생 때 HapticManager 가 이 값을 읽는다
        // (ch_playWaveform / ch_transient / ch_beginContinuous 로 그대로 넘어간다).
        HapticManager.Sharpness = Mathf.Clamp01(sharpness);
        return true;
    }

    private void Preview()
    {
        if (!Apply()) return;
        int count;
        if (!Read(previewRepeatCount, 1, 100, out count))
        {
            error = "Repeat count must be an integer from 1 to 100.";
            return;
        }

        StopPreviewPlayback();
        previewRoutine = StartCoroutine(PreviewRepeated(count));
    }

    private IEnumerator PreviewRepeated(int count)
    {
        long patternDurationMs = HapticManager.GetPatternDurationMs(selected,
            controller.pulseDurationMs, controller.waveStepMs, controller.waveGapMs,
            controller.envelopeDurationMs, controller.envelopeSampleMs,
            controller.envelopeModulationHz);

        for (int i = 0; i < count; i++)
        {
            HapticManager.PlayPattern(selected, controller.pulseDurationMs,
                controller.waveStepMs, controller.waveGapMs,
                controller.waveHighAmplitude, controller.waveLowAmplitude, 255,
                controller.envelopeDurationMs, controller.envelopeSampleMs,
                controller.envelopeModulationHz);

            if (i + 1 < count)
                yield return new WaitForSecondsRealtime(
                    (patternDurationMs + controller.patternRepeatMs) / 1000f);
        }

        previewRoutine = null;
    }

    private void StopPreviewPlayback()
    {
        if (previewRoutine != null)
        {
            StopCoroutine(previewRoutine);
            previewRoutine = null;
        }
        HapticManager.StopWaveform();
        HapticManager.StopContinuous();
    }

    private static bool Read(string value, int minimum, int maximum, out int number)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            && number >= minimum && number <= maximum;
    }

    private static bool Read(string value, float minimum, float maximum, out float number)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && !float.IsNaN(number) && number >= minimum && number <= maximum;
    }
}

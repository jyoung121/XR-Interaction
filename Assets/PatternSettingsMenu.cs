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
    public bool Visible { get; set; }
    public bool Done { get; private set; }
    public bool RunPatternTest { get; private set; }

    private ExperimentController controller;
    private VibrationPattern selected = VibrationPattern.SinglePulse;
    private Vector2 scroll;
    private string error = "";

    private string envDuration, envSample, envModulation;
    private string stepMs, gapMs, highAmp, lowAmp, pulseMs, repeatMs;
    private string minInterval, maxInterval, near, far;
    private float sharpness;             // 0~1. iOS 전용 — 안드로이드는 무시한다

    // 시나리오 확인용 (파일럿). 바리깡·치과는 지속 진동이라 단발 Preview 로는 들을 수 없다.
    private bool buzzing;
    private bool loadDip;
    private float buzzSeconds;
    private float buzzSharpness;         // 버즈를 시작할 때 쓴 sharpness
    private int crunchSeed = 1;

    public void Initialize(ExperimentController experiment)
    {
        controller = experiment;
        RefreshFromController();
    }

    /// <summary>화면을 띄운다. 컨트롤러가 XR 을 끈 뒤에 부른다.</summary>
    public void Begin()
    {
        StopBuzz();
        RefreshFromController();
        Done = false;
        RunPatternTest = false;
        Visible = true;
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

    /// <summary>
    /// 지속 버즈를 돌린다. 바리깡·치과는 끊기지 않는 진동이라 단발 Preview 로는
    /// 들어볼 수 없어서, 파일럿에서 기준 패턴을 정하려면 이게 있어야 한다.
    ///
    /// SetContinuous 는 첫 호출에만 sharpness 를 넘기고 그 뒤에는 세기만 갱신한다.
    /// 그래서 돌아가는 중에 sharpness 를 바꿔도 안 먹는다. 바리깡과 치과를 가르는 축이
    /// 바로 sharpness 이므로, 값이 바뀌면 재생기를 다시 띄워 준다.
    /// </summary>
    private void Update()
    {
        if (!buzzing) return;

        if (!Visible || controller == null) { StopBuzz(); return; }

        if (!Mathf.Approximately(buzzSharpness, sharpness))
        {
            HapticManager.StopContinuous();
            HapticManager.Sharpness = Mathf.Clamp01(sharpness);
            buzzSharpness = sharpness;
        }

        buzzSeconds += Time.deltaTime;

        int peak;
        if (!Read(highAmp, 1, 255, out peak)) peak = 200;

        float load = loadDip ? ScenarioHaptics.ClipperLoad(buzzSeconds) : 1f;
        HapticManager.SetContinuous(Mathf.Clamp(Mathf.RoundToInt(peak * load), 1, 255));
    }

    private void StartBuzz()
    {
        HapticManager.StopWaveform();
        HapticManager.Sharpness = Mathf.Clamp01(sharpness);
        buzzSharpness = sharpness;
        buzzSeconds = 0f;
        buzzing = true;
    }

    private void StopBuzz()
    {
        if (!buzzing) return;
        buzzing = false;
        buzzSeconds = 0f;
        HapticManager.StopContinuous();
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

        GUILayout.Label("Haptic pattern settings");
        GUILayout.Label("Change a number, tap Preview, feel it. No rebuild needed.");

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));

        GUILayout.Space(10);
        GUILayout.Label("Pattern to preview");
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
        GUILayout.Label("Envelope shapes (sine / triangle / sawtooth / square / decay / damped)");
        Field("Envelope duration (ms)  20-2000", ref envDuration);
        Field("Sample step (ms)  10-100", ref envSample);
        Field("Modulation (Hz)  0.5-50", ref envModulation);
        GUILayout.Label("Usable ceiling is 1000 / (2 x sample step): 25Hz at 20ms, 50Hz at 10ms.");
        GUILayout.Label("duration x Hz / 1000 = cycles.  200ms x 5Hz = 1 cycle (one swell).");
        GUILayout.Label("600ms x 15Hz = 9 cycles, which feels like a rhythm instead.");

        GUILayout.Space(12);
        GUILayout.Label("Step shapes (ramp / bounce / wobble)");
        Field("Wave segment (ms)  1-2000", ref stepMs);
        Field("Silent gap (ms)  1-2000", ref gapMs);

        GUILayout.Space(12);
        GUILayout.Label("Strength and timing");
        Field("Peak strength  1-255", ref highAmp);
        Field("Low strength  1-255", ref lowAmp);
        Field("Pulse duration (ms)  1-2000", ref pulseMs);
        Field("Preview repeat (ms)  100-5000", ref repeatMs);

        GUILayout.Space(12);
        GUILayout.Label("Sharpness  (iPhone only - Android ignores it)");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Sharpness  " + sharpness.ToString("0.00"), GUILayout.Width(170));
        if (GUILayout.Button("-", GUILayout.Width(48), GUILayout.Height(34)))
            sharpness = Mathf.Clamp01(Mathf.Round((sharpness - 0.05f) * 100f) / 100f);
        sharpness = GUILayout.HorizontalSlider(sharpness, 0f, 1f, GUILayout.Width(180));
        if (GUILayout.Button("+", GUILayout.Width(48), GUILayout.Height(34)))
            sharpness = Mathf.Clamp01(Mathf.Round((sharpness + 0.05f) * 100f) / 100f);
        GUILayout.EndHorizontal();
        GUILayout.Label("0 = round and organic, 1 = crisp and precise. Apple does NOT call this");
        GUILayout.Label("frequency, and it also changes how strong the buzz feels - so it is not");
        GUILayout.Label("independent of the strength boxes above. Peak output sits near 0.7, not 1.");
        GUILayout.Label("Report the number itself, not a Hz value.");

        GUILayout.Space(12);
        GUILayout.Label("Scenario check  (pilot)");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(buzzing ? "Stop buzz" : "Hold buzz  (clipper / dental)",
                             GUILayout.Height(44)))
        {
            if (buzzing) StopBuzz();
            else if (Apply()) StartBuzz();
        }
        if (GUILayout.Button(loadDip ? "[x] load dip" : "[ ] load dip",
                             GUILayout.Width(160), GUILayout.Height(44)))
            loadDip = !loadDip;
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Crunch once  (eating)", GUILayout.Height(44)))
        {
            if (Apply())
            {
                StopBuzz();
                int peak;
                if (!Read(highAmp, 1, 255, out peak)) peak = 200;
                crunchSeed++;
                ScenarioHaptics.PlayCrunch(4, peak, crunchSeed);
            }
        }
        GUILayout.Label("Buzz uses Peak strength + Sharpness, and keeps running until you stop it.");
        GUILayout.Label("Clipper: sharpness 0.10-0.25, load dip ON.   Dental: sharpness 1.0, dip OFF.");
        GUILayout.Label("Crunch is random each press. Current seed " + crunchSeed + " - write it down");
        GUILayout.Label("when one feels right, so the same bite can be rebuilt later.");

        GUILayout.Space(12);
        GUILayout.Label("Experiment (case 2 interval, case 3 strength)");
        Field("Minimum interval (ms)  1-10000", ref minInterval);
        Field("Maximum interval (ms)  1-10000", ref maxInterval);
        Field("Strength near target  1-255", ref near);
        Field("Strength far from target  1-255", ref far);

        GUILayout.Space(12);
        GUILayout.Label("Haptics: " + HapticManager.Diagnostics);

        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);

        if (GUILayout.Button("Preview this pattern", GUILayout.Height(44))) Preview();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Compare all 10", GUILayout.Height(48)))
        {
            if (Apply()) { StopBuzz(); RunPatternTest = true; Done = true; }
        }
        if (GUILayout.Button("Start experiment", GUILayout.Height(48)))
        {
            if (Apply()) { StopBuzz(); RunPatternTest = false; Done = true; }
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();

        GUI.skin.label.fontSize = oldLabel;
        GUI.skin.button.fontSize = oldButton;
        GUI.skin.textField.fontSize = oldField;
        GUI.matrix = oldMatrix;
    }

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
            !Read(repeatMs, 100, 5000, out repeat) ||
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

        HapticManager.StopWaveform();
        HapticManager.PlayPattern(selected, controller.pulseDurationMs,
            controller.waveStepMs, controller.waveGapMs,
            controller.waveHighAmplitude, controller.waveLowAmplitude, 255,
            controller.envelopeDurationMs, controller.envelopeSampleMs,
            controller.envelopeModulationHz);
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

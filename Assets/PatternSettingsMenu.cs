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

    public void Initialize(ExperimentController experiment)
    {
        controller = experiment;
        RefreshFromController();
    }

    /// <summary>화면을 띄운다. 컨트롤러가 XR 을 끈 뒤에 부른다.</summary>
    public void Begin()
    {
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
        error = "";
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
        Field("Modulation (Hz)  0.5-20", ref envModulation);
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
            if (Apply()) { RunPatternTest = true; Done = true; }
        }
        if (GUILayout.Button("Start experiment", GUILayout.Height(48)))
        {
            if (Apply()) { RunPatternTest = false; Done = true; }
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
            !Read(envModulation, 0.5f, 20f, out modulation) ||
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

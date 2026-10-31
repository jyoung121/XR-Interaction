using UnityEngine;

public enum VibrationPattern
{
    SinglePulse,
    RampUpDown,
    Bounce,
    Wobble,
    SineEnvelope,
    TriangleEnvelope,
    SawtoothEnvelope,
    SquareEnvelope,
    ExponentialDecay,
    DampedOscillation
}

/// <summary>
/// Android Vibrator 래퍼. 세기(amplitude) 제어는 API 26+ 이고 기기가 hasAmplitudeControl()==true 일 때만 유효.
/// </summary>
public class AndroidVibrationManager : MonoBehaviour
{
    private static AndroidJavaObject vibrator;
    private static AndroidJavaClass vibrationEffectClass;
    private static bool isInitialized = false;

    /// <summary>기기가 진동 세기 조절을 지원하는지 (Case 3 실험 유효성 판단용)</summary>
    public static bool HasAmplitudeControl { get; private set; } = false;
    public static bool HasVibrator { get; private set; } = false;

    // Unity 는 코드에서 Handheld.Vibrate() 가 참조될 때만 android.permission.VIBRATE 를 자동으로 추가합니다.
    // 아래 참조는 실행되지 않지만(항상 false) 권한을 자동 추가시키는 역할을 합니다. 지우지 마세요.
    private static readonly bool forcePermission = false;

    private void Awake()
    {
        if (forcePermission) Handheld.Vibrate();
        InitializeVibrator();
    }

    /// <summary>HapticManager 에서 호출. 초기화만 보장한다.</summary>
    public static void EnsureInitialized() { InitializeVibrator(); }

    private static void InitializeVibrator()
    {
        if (isInitialized) return;

#if UNITY_ANDROID && !UNITY_EDITOR
        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                vibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (vibrator != null)
            {
                HasVibrator = vibrator.Call<bool>("hasVibrator");
                HasAmplitudeControl = vibrator.Call<bool>("hasAmplitudeControl");
            }

            vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
            isInitialized = true;
            Debug.Log($"[Vibration] init ok. hasVibrator={HasVibrator}, hasAmplitudeControl={HasAmplitudeControl}");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Vibration] 초기화 실패: {e.Message}");
        }
#else
        isInitialized = true;
#endif
    }

    /// <summary>정해진 시간(ms)과 세기(1~255)로 진동.</summary>
    public static void Vibrate(long milliseconds, int amplitude)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (vibrator == null) InitializeVibrator();
        if (vibrator == null || !HasVibrator) return;

        amplitude = Mathf.Clamp(amplitude, 1, 255);

        try
        {
            if (vibrationEffectClass != null)
            {
                using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                           "createOneShot", milliseconds, amplitude))
                {
                    vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                vibrator.Call("vibrate", milliseconds);
            }
        }
        catch (System.Exception e)
        {
            // VIBRATE 권한 누락(SecurityException) 등 — 매 프레임 예외 스팸 방지
            Debug.LogWarning($"[Vibration] vibrate 실패: {e.Message}");
        }
#else
        // 에디터 테스트용
        // Debug.Log($"[Editor Haptic] {milliseconds}ms / amp {amplitude}");
#endif
    }

    /// <summary>
    /// Plays one complete pattern. Timings are segment durations, not absolute timestamps.
    /// A later call may replace a pattern that is still playing; device testing will determine
    /// whether the chosen experiment intervals cause that to happen.
    /// </summary>
    public static void PlayPattern(VibrationPattern pattern, long pulseMs, long stepMs,
        long gapMs, int highAmplitude, int lowAmplitude, int intensity,
        int mathDurationMs = 200, int mathSampleMs = 20, float modulationHz = 5f)
    {
        int peak = Mathf.Clamp(Mathf.RoundToInt(
            Mathf.Clamp(highAmplitude, 1, 255) * Mathf.Clamp(intensity, 1, 255) / 255f), 1, 255);
        int low = Mathf.Clamp(Mathf.RoundToInt(
            Mathf.Clamp(lowAmplitude, 1, 255) * Mathf.Clamp(intensity, 1, 255) / 255f), 1, peak);

        if (pattern == VibrationPattern.SinglePulse)
        {
            Vibrate(Mathf.Max(1, (int)pulseMs), peak);
            return;
        }

        long[] timings;
        int[] amplitudes;
        if (IsMathPattern(pattern))
            BuildMathWaveform(pattern, mathDurationMs, mathSampleMs, modulationHz,
                peak, out timings, out amplitudes);
        else
            BuildWaveform(pattern, stepMs, gapMs, peak, low, out timings, out amplitudes);

#if UNITY_ANDROID && !UNITY_EDITOR
        if (vibrator == null) InitializeVibrator();
        if (vibrator == null || !HasVibrator || vibrationEffectClass == null) return;

        try
        {
            using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                "createWaveform", timings, amplitudes, -1))
            {
                vibrator.Call("vibrate", effect);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Vibration] waveform 실패: {e.Message}");
        }
#endif
    }

    /// <summary>timings / amplitudes 짝을 그대로 재생한다 (되할이 없음).
    /// HapticManager 가 iOS 와 공통 창구를 만들 수 있게 떼낸 것이다.</summary>
    public static void PlayWaveform(long[] timings, int[] amplitudes)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (timings == null || amplitudes == null) return;
        if (vibrator == null) InitializeVibrator();
        if (vibrator == null || !HasVibrator || vibrationEffectClass == null) return;

        try
        {
            using (AndroidJavaObject effect = vibrationEffectClass.CallStatic<AndroidJavaObject>(
                "createWaveform", timings, amplitudes, -1))
            {
                vibrator.Call("vibrate", effect);
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Vibration] waveform 실패: {e.Message}");
        }
#endif
    }

    /// <summary>Builds a single, non-repeating waveform and returns its duration in ms.</summary>
    public static long BuildWaveform(VibrationPattern pattern, long stepMs, long gapMs,
        int highAmplitude, int lowAmplitude, out long[] timings, out int[] amplitudes)
    {
        long step = System.Math.Max(1L, stepMs);
        long gap = System.Math.Max(1L, gapMs);
        int high = Mathf.Clamp(highAmplitude, 1, 255);
        int low = Mathf.Clamp(lowAmplitude, 1, high);
        int middle = (high + low) / 2;

        switch (pattern)
        {
            case VibrationPattern.RampUpDown:
                timings = new[] { step, step, step, step, step };
                amplitudes = new[] { low, middle, high, middle, low };
                break;
            case VibrationPattern.Bounce:
                timings = new[] { step, gap, step, gap, step };
                amplitudes = new[] { high, 0, middle, 0, low };
                break;
            case VibrationPattern.Wobble:
                timings = new[] { step, gap, step, gap, step, gap, step };
                amplitudes = new[] { low, 0, high, 0, low, 0, high };
                break;
            default:
                timings = new[] { step };
                amplitudes = new[] { high };
                break;
        }

        long total = 0;
        foreach (long duration in timings) total += duration;
        return total;
    }

    public static bool IsMathPattern(VibrationPattern pattern)
    {
        return pattern >= VibrationPattern.SineEnvelope &&
            pattern <= VibrationPattern.DampedOscillation;
    }

    /// <summary>
    /// Samples an amplitude envelope for createWaveform. modulationHz is the rate of
    /// strength changes, not the physical oscillation frequency of the motor.
    /// </summary>
    public static long BuildMathWaveform(VibrationPattern pattern, int durationMs,
        int sampleMs, float modulationHz, int peakAmplitude,
        out long[] timings, out int[] amplitudes)
    {
        int duration = Mathf.Clamp(durationMs, 20, 2000);
        int sample = Mathf.Clamp(sampleMs, 10, 100);
        float rate = Mathf.Clamp(modulationHz, 0.5f, 20f);
        int peak = Mathf.Clamp(peakAmplitude, 1, 255);
        int count = (duration + sample - 1) / sample;
        timings = new long[count];
        amplitudes = new int[count];

        for (int i = 0; i < count; i++)
        {
            int start = i * sample;
            int length = Mathf.Min(sample, duration - start);
            float progress = (start + length * 0.5f) / duration;
            float phase = Mathf.Repeat((start + length * 0.5f) * rate / 1000f, 1f);
            float value;

            switch (pattern)
            {
                case VibrationPattern.SineEnvelope:
                    value = 0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * phase);
                    break;
                case VibrationPattern.TriangleEnvelope:
                    value = 1f - Mathf.Abs(2f * phase - 1f);
                    break;
                case VibrationPattern.SawtoothEnvelope:
                    value = phase;
                    break;
                case VibrationPattern.SquareEnvelope:
                    value = phase < 0.5f ? 1f : 0f;
                    break;
                case VibrationPattern.ExponentialDecay:
                    value = Mathf.Exp(-4f * progress);
                    break;
                case VibrationPattern.DampedOscillation:
                    value = Mathf.Exp(-3f * progress) *
                        (0.5f - 0.5f * Mathf.Cos(2f * Mathf.PI * phase));
                    break;
                default:
                    value = 0f;
                    break;
            }

            timings[i] = length;
            amplitudes[i] = Mathf.Clamp(Mathf.RoundToInt(value * peak), 0, 255);
        }

        return duration;
    }

    public static void Cancel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (vibrator == null) return;
        try { vibrator.Call("cancel"); }
        catch (System.Exception e) { Debug.LogWarning($"[Vibration] cancel 실패: {e.Message}"); }
#endif
    }
}

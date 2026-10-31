using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 안드로이드 / 아이폰 공통 진동 창구.
///
///  Android : AndroidVibrationManager (VibrationEffect) 로 넘긴다.
///  iOS     : Plugins/iOS/CardboardHaptics.mm 의 CoreHaptics 로 넘긴다.
///
/// 실험 코드는 이 클래스만 부르면 되고, 플랫폼 분기는 전부 여기서 끝난다.
///
///  Pulse()            — 짧은 한 번 (Case 2: 주기 변조)
///  SetContinuous()    — 세기를 바꿔 가며 이어지는 진동 (Case 3: 세기 변조). 주기적으로 반복 호출한다.
///  StopContinuous()   — 시행이 끝나면 호출
///  PlayPattern()      — 진동 패턴 하나를 끝까지 (10가지 모양)
///
/// HasAmplitudeControl 이 false 면 Case 3 는 세기가 3단계로 뭉뚱그려진다.
/// Diagnostics 는 iOS 에서 왜 진동이 안 되는지 보기 위한 것 — 맥 없이는 로그를 볼 수 없다.
/// </summary>
public static class HapticManager
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int    ch_init();
    [DllImport("__Internal")] private static extern int    ch_supportsHaptics();
    [DllImport("__Internal")] private static extern void   ch_transient(float intensity, float sharpness);
    [DllImport("__Internal")] private static extern void   ch_beginContinuous(float intensity, float sharpness);
    [DllImport("__Internal")] private static extern void   ch_setContinuousIntensity(float intensity);
    [DllImport("__Internal")] private static extern void   ch_stopContinuous();
    [DllImport("__Internal")] private static extern void   ch_playWaveform(int[] durationsMs, int[] amplitudes, int count, float sharpness);
    [DllImport("__Internal")] private static extern void   ch_stopWaveform();
    [DllImport("__Internal")] private static extern void   ch_stopAll();
    [DllImport("__Internal")] private static extern void   ch_impact(float intensity);
    [DllImport("__Internal")] private static extern void   ch_systemVibrate();
    [DllImport("__Internal")] private static extern System.IntPtr ch_diagnostics();

    private static bool continuousRunning = false;
#endif

    private static bool initialized = false;

    /// <summary>진동 자체가 가능한가</summary>
    public static bool HasVibrator { get; private set; }

    /// <summary>세기를 자유롭게 지정할 수 있는가. false 면 Case 3 는 세기 3단계로만 가능.</summary>
    public static bool HasAmplitudeControl { get; private set; }

    /// <summary>Case 2 펄스의 날카로움(iOS 전용, 0~1). Android 에서는 무시된다.</summary>
    public static float Sharpness = 0.5f;

    /// <summary>iOS 진동 경로의 상태와 마지막 오류. 화면에 띄워 원인을 가린다.</summary>
    public static string Diagnostics
    {
        get
        {
#if UNITY_IOS && !UNITY_EDITOR
            System.IntPtr p = ch_diagnostics();
            return p == System.IntPtr.Zero ? "(없음)" : Marshal.PtrToStringAnsi(p);
#elif UNITY_ANDROID && !UNITY_EDITOR
            return string.Format("android vib={0} amp={1}", HasVibrator, HasAmplitudeControl);
#else
            return "에디터 — 진동 없음";
#endif
        }
    }

    public static void Init()
    {
        if (initialized) return;
        initialized = true;

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.EnsureInitialized();
        HasVibrator = AndroidVibrationManager.HasVibrator;
        HasAmplitudeControl = AndroidVibrationManager.HasAmplitudeControl;
        Debug.Log($"[Haptics] Android. hasVibrator={HasVibrator}, hasAmplitudeControl={HasAmplitudeControl}");

#elif UNITY_IOS && !UNITY_EDITOR
        int ok = ch_init();
        HasVibrator = true;                       // 아이폰은 Taptic Engine 이 기본 탑재
        HasAmplitudeControl = (ok == 1) && (ch_supportsHaptics() == 1);
        Debug.Log($"[Haptics] iOS. coreHaptics={HasAmplitudeControl} / {Diagnostics}");
        if (!HasAmplitudeControl)
            Debug.LogWarning("[Haptics] CoreHaptics 를 못 쓴다 — UIImpactFeedback(3단계)으로 대체한다. " +
                             "Case 3(세기 변조)의 해상도가 떨어지므로 Limitations 에 적을 것.");

#else
        HasVibrator = false;
        HasAmplitudeControl = false;
        Debug.Log("[Haptics] 에디터 — 진동 없음");
#endif
    }

    /// <summary>
    /// 엔진이 중간에 멈췄다 다시 켜졌을 수 있으므로 상태를 다시 읽는다.
    /// 자가진단과 블록 전환 시점에 부른다.
    /// </summary>
    public static void Refresh()
    {
#if UNITY_IOS && !UNITY_EDITOR
        HasAmplitudeControl = ch_supportsHaptics() == 1;
#endif
    }

    /// <summary>짧은 펄스 한 번. amplitude 는 Android 와 맞춰 1~255.</summary>
    public static void Pulse(long milliseconds, int amplitude)
    {
        if (!initialized) Init();
        amplitude = Mathf.Clamp(amplitude, 1, 255);

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.Vibrate(milliseconds, amplitude);

#elif UNITY_IOS && !UNITY_EDITOR
        // CoreHaptics 의 transient 는 길이를 지정할 수 없다(기기가 정한 짧은 탁 소리).
        // 길이 대신 세기로만 대응한다 — 보고서 Limitations 에 적을 것.
        // CoreHaptics 가 실패하면 네이티브 쪽에서 UIImpactFeedback 으로 자동 전환된다.
        ch_transient(amplitude / 255f, Sharpness);
#endif
    }

    /// <summary>
    /// 세기가 바뀌는 연속 진동. 100ms 간격처럼 주기적으로 계속 불러 주면 된다.
    /// Android 는 짧은 진동을 겹쳐 이어 붙이고, iOS 는 연속 이벤트의 세기만 갱신한다.
    /// </summary>
    public static void SetContinuous(int amplitude)
    {
        if (!initialized) Init();
        amplitude = Mathf.Clamp(amplitude, 1, 255);

#if UNITY_ANDROID && !UNITY_EDITOR
        // 90ms 울림을 100ms 마다 → 거의 끊김 없이 이어진다 (원본 Case 3 와 동일)
        AndroidVibrationManager.Vibrate(90, amplitude);

#elif UNITY_IOS && !UNITY_EDITOR
        if (!HasAmplitudeControl)
        {
            // CoreHaptics 가 없으면 연속 진동을 만들 수 없다.
            // 짧은 탁을 계속 찍어 세기 변화만이라도 전달한다(해상도는 3단계).
            ch_impact(amplitude / 255f);
            return;
        }

        if (!continuousRunning)
        {
            ch_beginContinuous(amplitude / 255f, Sharpness);
            continuousRunning = true;
        }
        else
        {
            ch_setContinuousIntensity(amplitude / 255f);
        }
#endif
    }

    public static void StopContinuous()
    {
#if UNITY_IOS && !UNITY_EDITOR
        if (continuousRunning) { ch_stopContinuous(); continuousRunning = false; }
#endif
    }

    /// <summary>
    /// 진동 패턴 하나를 끝까지 재생한다. 인자는 AndroidVibrationManager.PlayPattern 과 같다.
    ///
    ///  Android : VibrationEffect.createWaveform 으로 넘긴다 (원본 그대로).
    ///  iOS     : 같은 timings/amplitudes 를 만들어 ch_playWaveform 으로 넘긴다.
    ///            CoreHaptics 에 createWaveform 대응 API 가 없어, 구간마다
    ///            continuous 이벤트를 하나씩 놓아 같은 계단 모양을 만든다.
    ///
    /// 모양을 만드는 계산(BuildWaveform / BuildMathWaveform)은 플랫폼과 무관한
    /// 순수 수식이라 양쪽이 같은 함수를 쓴다 — 두 기기가 같은 파형을 받는다.
    /// </summary>
    public static void PlayPattern(VibrationPattern pattern, long pulseMs, long stepMs,
        long gapMs, int highAmplitude, int lowAmplitude, int intensity,
        int mathDurationMs = 200, int mathSampleMs = 20, float modulationHz = 5f)
    {
        if (!initialized) Init();

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.PlayPattern(pattern, pulseMs, stepMs, gapMs,
            highAmplitude, lowAmplitude, intensity, mathDurationMs, mathSampleMs, modulationHz);

#elif UNITY_IOS && !UNITY_EDITOR
        // 세기 환산은 안드로이드 쪽과 한 글자도 다르지 않게 맞춘다
        int peak = Mathf.Clamp(Mathf.RoundToInt(
            Mathf.Clamp(highAmplitude, 1, 255) * Mathf.Clamp(intensity, 1, 255) / 255f), 1, 255);
        int low = Mathf.Clamp(Mathf.RoundToInt(
            Mathf.Clamp(lowAmplitude, 1, 255) * Mathf.Clamp(intensity, 1, 255) / 255f), 1, peak);

        long[] timings;
        int[] amplitudes;

        if (pattern == VibrationPattern.SinglePulse)
        {
            // 안드로이드는 createOneShot(pulseMs, peak) — 길이가 있는 짧은 울림이다.
            // ch_transient 는 길이를 못 정하므로, 한 구간 파형으로 보내 길이를 살린다.
            timings = new long[] { System.Math.Max(1L, pulseMs) };
            amplitudes = new int[] { peak };
        }
        else if (AndroidVibrationManager.IsMathPattern(pattern))
        {
            AndroidVibrationManager.BuildMathWaveform(pattern, mathDurationMs, mathSampleMs,
                modulationHz, peak, out timings, out amplitudes);
        }
        else
        {
            AndroidVibrationManager.BuildWaveform(pattern, stepMs, gapMs, peak, low,
                out timings, out amplitudes);
        }

        PlayWaveform(timings, amplitudes);
#endif
    }

    /// <summary>
    /// timings(구간 길이 ms) / amplitudes(0~255) 짝을 그대로 재생한다.
    /// Android 의 createWaveform 과 같은 의미이고, 되풀이는 하지 않는다.
    /// </summary>
    public static void PlayWaveform(long[] timings, int[] amplitudes)
    {
        if (timings == null || amplitudes == null) return;
        int count = Mathf.Min(timings.Length, amplitudes.Length);
        if (count <= 0) return;

        if (!initialized) Init();

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.PlayWaveform(timings, amplitudes);

#elif UNITY_IOS && !UNITY_EDITOR
        int[] durations = new int[count];
        int[] levels = new int[count];
        for (int i = 0; i < count; i++)
        {
            durations[i] = (int)Mathf.Clamp(timings[i], 0f, 60000f);
            levels[i] = Mathf.Clamp(amplitudes[i], 0, 255);
        }
        ch_playWaveform(durations, levels, count, Sharpness);
#endif
    }

    /// <summary>재생 중인 패턴을 끊는다.</summary>
    public static void StopWaveform()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.Cancel();
#elif UNITY_IOS && !UNITY_EDITOR
        ch_stopWaveform();
#endif
    }

    /// <summary>세기 3단계 진동을 직접. 자가진단에서 경로를 나눠 확인할 때만 쓴다.</summary>
    public static void ImpactOnly(int amplitude)
    {
#if UNITY_IOS && !UNITY_EDITOR
        ch_impact(Mathf.Clamp(amplitude, 1, 255) / 255f);
#endif
    }

    /// <summary>세기 고정 진동 한 번. 기기가 진동 자체는 되는지 가리는 용도.</summary>
    public static void SystemVibrate()
    {
#if UNITY_IOS && !UNITY_EDITOR
        ch_systemVibrate();
#elif UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.Vibrate(400, 255);
#endif
    }

    /// <summary>진행 중인 모든 진동 중단.</summary>
    public static void Cancel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.Cancel();

#elif UNITY_IOS && !UNITY_EDITOR
        StopContinuous();
        ch_stopWaveform();
#endif
    }
}

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
///
/// HasAmplitudeControl 이 false 면 Case 3 는 그 기기에서 무의미하다.
/// </summary>
public static class HapticManager
{
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern int  ch_init();
    [DllImport("__Internal")] private static extern int  ch_supportsHaptics();
    [DllImport("__Internal")] private static extern void ch_transient(float intensity, float sharpness);
    [DllImport("__Internal")] private static extern void ch_beginContinuous(float intensity, float sharpness);
    [DllImport("__Internal")] private static extern void ch_setContinuousIntensity(float intensity);
    [DllImport("__Internal")] private static extern void ch_stopContinuous();
    [DllImport("__Internal")] private static extern void ch_stopAll();

    private static bool continuousRunning = false;
#endif

    private static bool initialized = false;

    /// <summary>진동 자체가 가능한가</summary>
    public static bool HasVibrator { get; private set; }

    /// <summary>세기 조절이 가능한가. false 면 Case 3 실험 불가.</summary>
    public static bool HasAmplitudeControl { get; private set; }

    /// <summary>Case 2 펄스의 날카로움(iOS 전용, 0~1). Android 에서는 무시된다.</summary>
    public static float Sharpness = 0.5f;

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
        Debug.Log($"[Haptics] iOS CoreHaptics. supported={HasAmplitudeControl}");
        if (!HasAmplitudeControl)
            Debug.LogWarning("[Haptics] 이 기기는 CoreHaptics 미지원(iPhone 8 미만 또는 iOS 13 미만). " +
                             "Case 3(세기 변조)는 측정이 무의미하다.");

#else
        HasVibrator = false;
        HasAmplitudeControl = false;
        Debug.Log("[Haptics] 에디터 — 진동 없음");
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

    /// <summary>진행 중인 모든 진동 중단.</summary>
    public static void Cancel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidVibrationManager.Cancel();

#elif UNITY_IOS && !UNITY_EDITOR
        StopContinuous();
#endif
    }
}

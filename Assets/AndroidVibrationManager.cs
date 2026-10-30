using UnityEngine;

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

    public static void Cancel()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (vibrator == null) return;
        try { vibrator.Call("cancel"); }
        catch (System.Exception e) { Debug.LogWarning($"[Vibration] cancel 실패: {e.Message}"); }
#endif
    }
}

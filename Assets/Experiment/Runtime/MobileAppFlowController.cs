using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

public enum MobileLauncherDestination
{
    Home,
    HapticTest,
    ExperimentSetup
}

/// <summary>
/// Persists across the Android launcher and VR experiment scenes. The launcher owns trial
/// selection; the experiment scene only consumes and plays the selected trial once.
/// </summary>
public sealed class MobileAppFlowController : MonoBehaviour
{
    public const string LauncherScenePath = "Assets/Scenes/MobileTestScene.unity";
    public const string ExperimentScenePath = "Assets/Scenes/ExperimentScene.unity";

    private static MobileAppFlowController instance;

    private ExperimentCondition selectedCondition;
    private ScenarioId selectedScenario;
    private ScenarioProfile selectedProfile;
    private float selectedRuntimeHapticOffset;
    private float selectedRuntimeAmplitudeScale = 1f;
    private bool experimentActive;
    private bool busy;
    private string status = "Launcher ready";
    private string launcherMessage = string.Empty;
    private MobileLauncherDestination launcherDestination = MobileLauncherDestination.Home;

    public static MobileAppFlowController Instance => instance;
    public bool IsExperimentActive => experimentActive;
    public bool IsBusy => busy;
    public string Status => status;

    public static MobileAppFlowController GetOrCreate()
    {
        if (instance != null) return instance;
        instance = FindAnyObjectByType<MobileAppFlowController>();
        if (instance != null) return instance;
        return new GameObject("Mobile App Flow").AddComponent<MobileAppFlowController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }
        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void PrepareLauncher()
    {
        if (busy || experimentActive) return;
        StartCoroutine(PrepareLauncherRoutine());
    }

    public bool LaunchExperiment(ExperimentProtocol protocol, ExperimentCondition condition,
                                 ScenarioId scenario, MobileLauncherDestination returnDestination,
                                 float runtimeHapticOffset, float runtimeAmplitudeScale,
                                 out string reason)
    {
        if (busy)
        {
            reason = "The app is already changing modes.";
            return false;
        }
        if (protocol == null)
        {
            reason = "Experiment Protocol is missing.";
            return false;
        }
        if (!protocol.TryResolveTrial(condition, scenario, out ScenarioProfile profile, out reason))
            return false;

        selectedCondition = condition;
        selectedScenario = scenario;
        selectedProfile = profile;
        selectedRuntimeHapticOffset = runtimeHapticOffset;
        selectedRuntimeAmplitudeScale = Mathf.Clamp(runtimeAmplitudeScale, 0f, 2f);
        launcherDestination = returnDestination;
        experimentActive = true;
        launcherMessage = string.Empty;
        StartCoroutine(LaunchExperimentRoutine());
        return true;
    }

    public bool TryGetSelectedTrial(out ScenarioProfile profile,
                                    out ExperimentCondition condition,
                                    out ScenarioId scenario,
                                    out float runtimeHapticOffset,
                                    out float runtimeAmplitudeScale)
    {
        profile = selectedProfile;
        condition = selectedCondition;
        scenario = selectedScenario;
        runtimeHapticOffset = selectedRuntimeHapticOffset;
        runtimeAmplitudeScale = selectedRuntimeAmplitudeScale;
        return experimentActive && profile != null;
    }

    public void ReturnToLauncher(string message = "")
    {
        if (busy && !experimentActive) return;
        launcherMessage = message ?? string.Empty;
        StartCoroutine(ReturnToLauncherRoutine());
    }

    public string ConsumeLauncherMessage()
    {
        string result = launcherMessage;
        launcherMessage = string.Empty;
        return result;
    }

    public MobileLauncherDestination ConsumeLauncherDestination()
    {
        MobileLauncherDestination result = launcherDestination;
        launcherDestination = MobileLauncherDestination.Home;
        return result;
    }

    private IEnumerator PrepareLauncherRoutine()
    {
        busy = true;
        status = "Preparing launcher...";
        yield return null;
        StopXR();
        ClearTrial();
        busy = false;
        status = "Launcher ready";
    }

    private IEnumerator LaunchExperimentRoutine()
    {
        busy = true;
        status = "Initializing VR...";
        XRManagerSettings manager = GetXRManager();
        if (manager == null)
        {
            FailLaunch("Android XR Manager is missing.");
            yield break;
        }

        if (!manager.isInitializationComplete)
            yield return manager.InitializeLoader();
        if (manager.activeLoader == null)
        {
            FailLaunch("Cardboard XR loader could not be initialized.");
            yield break;
        }

        manager.StartSubsystems();
        status = "Loading experiment...";
        AsyncOperation load = SceneManager.LoadSceneAsync(ExperimentScenePath,
            LoadSceneMode.Single);
        if (load == null)
        {
            StopXR();
            FailLaunch("ExperimentScene could not be loaded.");
            yield break;
        }
        while (!load.isDone) yield return null;
        busy = false;
        status = "Experiment running";
    }

    private IEnumerator ReturnToLauncherRoutine()
    {
        busy = true;
        status = "Returning to launcher...";
        StopXR();
        experimentActive = false;
        selectedProfile = null;
        selectedRuntimeHapticOffset = 0f;
        selectedRuntimeAmplitudeScale = 1f;
        AsyncOperation load = SceneManager.LoadSceneAsync(LauncherScenePath,
            LoadSceneMode.Single);
        if (load != null)
            while (!load.isDone) yield return null;
        busy = false;
        status = "Launcher ready";
    }

    private void FailLaunch(string reason)
    {
        launcherMessage = reason;
        ClearTrial();
        busy = false;
        status = reason;
        Debug.LogError("[MobileAppFlow] " + reason);
    }

    private void ClearTrial()
    {
        experimentActive = false;
        selectedProfile = null;
        selectedRuntimeHapticOffset = 0f;
        selectedRuntimeAmplitudeScale = 1f;
    }

    private static XRManagerSettings GetXRManager()
    {
        return XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
    }

    private static void StopXR()
    {
        XRManagerSettings manager = GetXRManager();
        if (manager == null || !manager.isInitializationComplete) return;
        manager.StopSubsystems();
        manager.DeinitializeLoader();
    }
}

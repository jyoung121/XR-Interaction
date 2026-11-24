using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using Google.XR.Cardboard;

/// <summary>
/// Shared participant input for interactive VR scenarios. South/A starts a prepared trial
/// and requests a graceful finish once the active contact block can be stopped.
/// </summary>
public sealed class ExperimentControllerInput : MonoBehaviour
{
    public ExperimentSessionController session;
    public bool allowCardboardTriggerFallback = true;
    public bool showVrPrompt = true;
    [Min(0.25f)] public float abortHoldSeconds = 1f;

    private float abortPressedAt = -1f;
    private bool abortSent;
    private string lastPrompt = string.Empty;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void InstallForExperimentScene()
    {
        ExperimentSessionController activeSession =
            FindAnyObjectByType<ExperimentSessionController>();
        if (activeSession == null ||
            FindAnyObjectByType<ExperimentControllerInput>() != null)
            return;

        ExperimentControllerInput input =
            activeSession.gameObject.AddComponent<ExperimentControllerInput>();
        input.session = activeSession;
    }

    private void Awake()
    {
        if (session == null) session = GetComponent<ExperimentSessionController>();
        if (session == null) session = FindAnyObjectByType<ExperimentSessionController>();
    }

    private void OnEnable()
    {
        if (session == null) session = FindAnyObjectByType<ExperimentSessionController>();
        if (session != null) session.StateChanged += HandleSessionStateChanged;
    }

    private void Start()
    {
        if (showVrPrompt && Camera.main != null)
            DebugHud.Create(Camera.main.transform);
        RefreshPrompt();
    }

    private void OnDisable()
    {
        if (session != null) session.StateChanged -= HandleSessionStateChanged;
        HidePrompt();
    }

    private void Update()
    {
        if (session == null) return;

        if (PrimaryPressedThisFrame())
        {
            if (session.State == ExperimentSessionState.ReadyToStart)
                session.StartPreparedTrial();
            else if (session.State == ExperimentSessionState.Running &&
                     session.CanFinishCurrentTrial)
                session.RequestFinishCurrentTrial();
        }

        UpdateAbortInput();
        RefreshPrompt();
    }

    private bool PrimaryPressedThisFrame()
    {
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame)
            return true;
#if UNITY_EDITOR || UNITY_STANDALONE
        if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            return true;
#endif
#endif
        return allowCardboardTriggerFallback && Api.IsTriggerPressed;
    }

    private void UpdateAbortInput()
    {
        bool held = false;
#if ENABLE_INPUT_SYSTEM
        held = Gamepad.current != null && Gamepad.current.buttonEast.isPressed;
#endif
        if (!held)
        {
            abortPressedAt = -1f;
            abortSent = false;
            return;
        }

        if (abortPressedAt < 0f) abortPressedAt = Time.unscaledTime;
        if (abortSent || Time.unscaledTime - abortPressedAt < abortHoldSeconds) return;
        abortSent = true;
        session.AbortCurrentTrial("Controller B held to abort");
    }

    private void HandleSessionStateChanged(ExperimentSessionState _)
    {
        RefreshPrompt(true);
    }

    private void RefreshPrompt(bool force = false)
    {
        if (!showVrPrompt || session == null || !session.IsInteractiveTrial)
        {
            HidePrompt();
            return;
        }

        string prompt = string.Empty;
        switch (session.State)
        {
            case ExperimentSessionState.ReadyToStart:
                prompt = "PRESS A TO START";
                break;
            case ExperimentSessionState.Running:
                prompt = session.CanFinishCurrentTrial
                    ? "PRESS A TO STOP"
                    : "STARTING...";
                break;
            case ExperimentSessionState.Finishing:
                prompt = "FINISHING...";
                break;
        }

        if (string.IsNullOrEmpty(prompt))
        {
            HidePrompt();
            return;
        }
        if (!force && prompt == lastPrompt) return;
        lastPrompt = prompt;
        DebugHud.Set(prompt);
        DebugHud.Show(true);
    }

    private void HidePrompt()
    {
        if (string.IsNullOrEmpty(lastPrompt)) return;
        lastPrompt = string.Empty;
        DebugHud.Set(string.Empty);
        DebugHud.Show(false);
    }
}

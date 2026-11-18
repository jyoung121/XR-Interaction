using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

public sealed class MobileTestController : MonoBehaviour
{
    public ProfileCatalog catalog; public ScenarioPlaybackController player; public LongHapticPlayer hapticPlayer;
    public HapticLinkReceiver linkReceiver; public HapticProfile previewProfile; private Vector2 scroll;
    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<ScenarioPlaybackController>();
        if (hapticPlayer == null) hapticPlayer = FindAnyObjectByType<LongHapticPlayer>();
        if (linkReceiver == null) linkReceiver = GetComponent<HapticLinkReceiver>() ?? gameObject.AddComponent<HapticLinkReceiver>();
        linkReceiver.StartListening();
    }
    private void Update()
    {
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current == null || player == null) return;
        if (Gamepad.current.dpad.left.wasPressedThisFrame) player.runtimeHapticOffsetSeconds -= .01f;
        if (Gamepad.current.dpad.right.wasPressedThisFrame) player.runtimeHapticOffsetSeconds += .01f;
        if (Gamepad.current.dpad.down.wasPressedThisFrame) player.runtimeAmplitudeScale = Mathf.Max(0f, player.runtimeAmplitudeScale - .05f);
        if (Gamepad.current.dpad.up.wasPressedThisFrame) player.runtimeAmplitudeScale = Mathf.Min(2f, player.runtimeAmplitudeScale + .05f);
#endif
    }
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(16, 16, Mathf.Min(520, Screen.width - 32), Screen.height - 32), GUI.skin.box);
        scroll = GUILayout.BeginScrollView(scroll); GUILayout.Label("MOBILE TEST SCENE"); GUILayout.Label("Preview Pattern");
        if (GUILayout.Button("Play selected profile") && previewProfile != null) hapticPlayer.PlayProfile(previewProfile, AudioSettings.dspTime + 0.1d);
        if (GUILayout.Button("Stop haptics")) hapticPlayer.Stop(); GUILayout.Space(10); GUILayout.Label("Controller Setup");
#if ENABLE_INPUT_SYSTEM
        GUILayout.Label(Gamepad.current == null ? "No gamepad detected" : $"Connected: {Gamepad.current.displayName}");
#else
        GUILayout.Label("Input System is not enabled");
#endif
        GUILayout.Space(10); GUILayout.Label("Mirror Scenario Synchronizer");
        if (player != null)
        {
            GUILayout.Label($"Haptic offset: {player.runtimeHapticOffsetSeconds * 1000f:0} ms  |  amplitude: {player.runtimeAmplitudeScale:0.00}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Offset -10 ms")) player.runtimeHapticOffsetSeconds -= .01f;
            if (GUILayout.Button("Offset +10 ms")) player.runtimeHapticOffsetSeconds += .01f;
            if (GUILayout.Button("Reset")) { player.runtimeHapticOffsetSeconds = 0f; player.runtimeAmplitudeScale = 1f; }
            GUILayout.EndHorizontal();
            GUILayout.Label("Controller: D-pad left/right = offset, down/up = amplitude");
        }
        if (catalog != null) foreach (var scenario in catalog.scenarios) if (scenario != null)
        { GUILayout.BeginHorizontal(); GUILayout.Label(scenario.scenario.ToString(), GUILayout.Width(140)); if (GUILayout.Button("Baseline")) player.Play(scenario, ExperimentCondition.Baseline); if (GUILayout.Button("Researcher")) player.Play(scenario, ExperimentCondition.ResearcherHaptic); GUILayout.EndHorizontal(); }
        GUILayout.Space(10); GUILayout.Label("PC Haptic Link Receiver"); GUILayout.Label(linkReceiver == null ? "Unavailable" : linkReceiver.Status);
        GUILayout.EndScrollView(); GUILayout.EndArea();
    }
}

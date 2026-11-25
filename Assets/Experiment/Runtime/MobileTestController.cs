using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Landscape, non-VR Android launcher. Direct ADB haptics stay in this scene, while
/// condition/scenario tests and formal trials are handed to the VR ExperimentScene.
/// </summary>
public sealed class MobileTestController : MonoBehaviour
{
    private enum Page { Home, HapticTest, ExperimentSetup, ParticipantProfile, Confirmation, Loading }

    public ProfileCatalog catalog;
    public ExperimentProtocol protocol;
    public ScenarioPlaybackController player;
    public LongHapticPlayer hapticPlayer;
    public HapticLinkReceiver linkReceiver;
    public HapticProfile previewProfile;

    private MobileAppFlowController flow;
    private Page page;
    private Page loadingFallbackPage;
    private Vector2 scroll;
    private ExperimentCondition? selectedCondition;
    private ScenarioId? selectedScenario;
    private ParticipantHapticSlot selectedParticipantSlot;
    private string status = string.Empty;
    private float controlHeight;
    private float contentWidth;
    private GUIStyle titleStyle;
    private GUIStyle panelStyle;

    private void Awake()
    {
#if UNITY_ANDROID
        Screen.autorotateToPortrait = false;
        Screen.autorotateToPortraitUpsideDown = false;
        Screen.autorotateToLandscapeLeft = true;
        Screen.autorotateToLandscapeRight = true;
        Screen.orientation = ScreenOrientation.AutoRotation;
#endif
        if (player == null) player = FindAnyObjectByType<ScenarioPlaybackController>();
        if (hapticPlayer == null) hapticPlayer = FindAnyObjectByType<LongHapticPlayer>();
        if (linkReceiver == null)
            linkReceiver = GetComponent<HapticLinkReceiver>() ?? gameObject.AddComponent<HapticLinkReceiver>();
        if (protocol == null && catalog != null && catalog.protocols != null)
            protocol = catalog.protocols.Find(candidate => candidate != null &&
                candidate.targetPlatform == RuntimePlatform.Android);

        linkReceiver.StopListening();
        flow = MobileAppFlowController.GetOrCreate();
    }

    private void Start()
    {
        if (protocol != null && protocol.research.enabled)
        {
            var research = gameObject.AddComponent<ResearchLauncherUI>();
            research.protocol = protocol;
            enabled = false;
            return;
        }
        flow.PrepareLauncher();
        status = flow.ConsumeLauncherMessage();
        ApplyLauncherDestination(flow.ConsumeLauncherDestination());
    }

    private void OnDisable()
    {
        linkReceiver?.StopListening();
        hapticPlayer?.Stop();
        player?.Stop();
    }

    private void Update()
    {
        if (page != Page.HapticTest || player == null) return;
#if ENABLE_INPUT_SYSTEM
        if (Gamepad.current == null) return;
        if (Gamepad.current.dpad.left.wasPressedThisFrame) player.runtimeHapticOffsetSeconds -= .01f;
        if (Gamepad.current.dpad.right.wasPressedThisFrame) player.runtimeHapticOffsetSeconds += .01f;
        if (Gamepad.current.dpad.down.wasPressedThisFrame)
            player.runtimeAmplitudeScale = Mathf.Max(0f, player.runtimeAmplitudeScale - .05f);
        if (Gamepad.current.dpad.up.wasPressedThisFrame)
            player.runtimeAmplitudeScale = Mathf.Min(2f, player.runtimeAmplitudeScale + .05f);
#endif
    }

    private void OnGUI()
    {
        Rect safe = Screen.safeArea;
        float horizontalMargin = Mathf.Max(20f, safe.width * .035f);
        float verticalMargin = Mathf.Max(18f, safe.height * .04f);
        Rect area = new Rect(safe.x + horizontalMargin,
            Screen.height - safe.yMax + verticalMargin,
            Mathf.Max(1f, safe.width - horizontalMargin * 2f),
            Mathf.Max(1f, safe.height - verticalMargin * 2f));
        contentWidth = area.width;

        int oldLabelSize = GUI.skin.label.fontSize;
        int oldButtonSize = GUI.skin.button.fontSize;
        bool oldLabelWrap = GUI.skin.label.wordWrap;
        bool oldButtonWrap = GUI.skin.button.wordWrap;
        TextClipping oldLabelClipping = GUI.skin.label.clipping;
        TextClipping oldButtonClipping = GUI.skin.button.clipping;
        RectOffset oldLabelPadding = GUI.skin.label.padding;
        RectOffset oldLabelMargin = GUI.skin.label.margin;
        RectOffset oldButtonPadding = GUI.skin.button.padding;
        RectOffset oldButtonMargin = GUI.skin.button.margin;
        GUI.skin.label.fontSize = Mathf.Clamp(Mathf.RoundToInt(area.height / 34f), 16, 27);
        GUI.skin.button.fontSize = Mathf.Clamp(Mathf.RoundToInt(area.height / 38f), 16, 25);
        GUI.skin.label.wordWrap = true;
        GUI.skin.button.wordWrap = true;
        GUI.skin.label.clipping = TextClipping.Overflow;
        GUI.skin.button.clipping = TextClipping.Clip;
        GUI.skin.label.padding = new RectOffset(7, 7, 5, 5);
        GUI.skin.label.margin = new RectOffset(4, 4, 3, 3);
        GUI.skin.button.padding = new RectOffset(10, 10, 7, 7);
        GUI.skin.button.margin = new RectOffset(5, 5, 5, 5);
        controlHeight = Mathf.Clamp(Mathf.Max(area.height * .11f,
            GUI.skin.button.fontSize * 2.8f), 54f, 96f);
        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Clamp(Mathf.RoundToInt(area.height / 25f), 22, 36),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft,
            wordWrap = true,
            clipping = TextClipping.Overflow
        };
        panelStyle = new GUIStyle(GUI.skin.box)
        {
            padding = new RectOffset(18, 18, 14, 14),
            margin = new RectOffset(6, 6, 6, 6)
        };

        GUILayout.BeginArea(area, panelStyle);
        scroll = GUILayout.BeginScrollView(scroll);
        switch (page)
        {
            case Page.Home: DrawHome(); break;
            case Page.HapticTest: DrawHapticTest(); break;
            case Page.ExperimentSetup: DrawExperimentSetup(); break;
            case Page.ParticipantProfile: DrawParticipantProfile(); break;
            case Page.Confirmation: DrawConfirmation(); break;
            case Page.Loading: DrawLoading(); break;
        }
        GUILayout.EndScrollView();
        GUILayout.EndArea();

        GUI.skin.label.fontSize = oldLabelSize;
        GUI.skin.button.fontSize = oldButtonSize;
        GUI.skin.label.wordWrap = oldLabelWrap;
        GUI.skin.button.wordWrap = oldButtonWrap;
        GUI.skin.label.clipping = oldLabelClipping;
        GUI.skin.button.clipping = oldButtonClipping;
        GUI.skin.label.padding = oldLabelPadding;
        GUI.skin.label.margin = oldLabelMargin;
        GUI.skin.button.padding = oldButtonPadding;
        GUI.skin.button.margin = oldButtonMargin;
    }

    private void ApplyLauncherDestination(MobileLauncherDestination destination)
    {
        selectedCondition = null;
        selectedScenario = null;
        selectedParticipantSlot = ParticipantHapticSlot.None;
        scroll = Vector2.zero;
        switch (destination)
        {
            case MobileLauncherDestination.HapticTest:
                page = Page.HapticTest;
                linkReceiver.StartListening();
                break;
            case MobileLauncherDestination.ExperimentSetup:
                page = Page.ExperimentSetup;
                linkReceiver.StopListening();
                break;
            default:
                page = Page.Home;
                linkReceiver.StopListening();
                break;
        }
    }

    private void Header(string title, string subtitle = null)
    {
        GUILayout.Label(title, titleStyle, GUILayout.MinHeight(titleStyle.lineHeight * 1.55f));
        if (!string.IsNullOrWhiteSpace(subtitle)) BodyLabel(subtitle, 1.25f);
        if (!string.IsNullOrWhiteSpace(status)) BodyLabel(status, 1.25f);
        GUILayout.Space(14f);
    }

    private void BodyLabel(string value, float lines = 1f)
    {
        float lineHeight = Mathf.Max(GUI.skin.label.lineHeight,
            GUI.skin.label.fontSize + GUI.skin.label.padding.vertical);
        GUILayout.Label(value, GUILayout.MinHeight(lineHeight * Mathf.Max(1f, lines)));
    }

    private void DrawHome()
    {
        Header("MOBILE LAUNCHER", "Choose haptic development or a formal experiment trial.");
        GUILayout.FlexibleSpace();
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("HAPTIC TEST", GUILayout.MinHeight(controlHeight * 2.2f))) EnterHapticTest();
        GUILayout.Space(16f);
        if (GUILayout.Button("EXPERIMENT SETUP", GUILayout.MinHeight(controlHeight * 2.2f)))
            EnterExperimentSetup();
        GUILayout.EndHorizontal();
        GUILayout.FlexibleSpace();
        if (flow != null && flow.IsBusy) BodyLabel(flow.Status);
    }

    private void EnterHapticTest()
    {
        page = Page.HapticTest;
        status = string.Empty;
        scroll = Vector2.zero;
        linkReceiver.StartListening();
    }

    private void DrawHapticTest()
    {
        Header("HAPTIC TEST", "Direct ADB haptics stay on this screen. Condition tests run in VR.");
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true));
        BodyLabel("PC HAPTIC LINK");
        BodyLabel(linkReceiver == null ? "Receiver unavailable" : linkReceiver.Status);
        BodyLabel("PC: ADB Forward → Play on Android");
        GUILayout.Space(8f);
        BodyLabel("Stored Profile Preview");
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previewProfile != null;
        if (GUILayout.Button("PLAY SELECTED PROFILE", GUILayout.MinHeight(controlHeight)))
            hapticPlayer.PlayProfile(previewProfile, AudioSettings.dspTime + .1d);
        GUI.enabled = previousEnabled;
        if (GUILayout.Button("STOP HAPTICS", GUILayout.MinHeight(controlHeight))) hapticPlayer.Stop();
        GUILayout.EndVertical();

        GUILayout.Space(14f);
        GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true));
        BodyLabel("VR CONDITION TEST");
        BodyLabel("The selected scenario opens in Cardboard VR and returns here when complete.", 1.4f);
        DrawRuntimeCalibration();
        DrawVrScenarioRow(ScenarioId.DentalDrill);
        DrawVrScenarioRow(ScenarioId.Toothbrush);
        DrawVrScenarioRow(ScenarioId.Apple);
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.Space(10f);
        if (GUILayout.Button("BACK TO LAUNCHER", GUILayout.MinHeight(controlHeight)))
        {
            player?.Stop();
            hapticPlayer?.Stop();
            linkReceiver?.StopListening();
            status = string.Empty;
            page = Page.Home;
        }
    }

    private void DrawRuntimeCalibration()
    {
        if (player == null) return;
        BodyLabel($"Offset {player.runtimeHapticOffsetSeconds * 1000f:0} ms  |  " +
                  $"Amplitude {player.runtimeAmplitudeScale:0.00}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("-10 ms")) player.runtimeHapticOffsetSeconds -= .01f;
        if (GUILayout.Button("+10 ms")) player.runtimeHapticOffsetSeconds += .01f;
        if (GUILayout.Button("AMP -"))
            player.runtimeAmplitudeScale = Mathf.Max(0f, player.runtimeAmplitudeScale - .05f);
        if (GUILayout.Button("AMP +"))
            player.runtimeAmplitudeScale = Mathf.Min(2f, player.runtimeAmplitudeScale + .05f);
        if (GUILayout.Button("RESET"))
        {
            player.runtimeHapticOffsetSeconds = 0f;
            player.runtimeAmplitudeScale = 1f;
        }
        GUILayout.EndHorizontal();
#if ENABLE_INPUT_SYSTEM
        BodyLabel(Gamepad.current == null ? "No gamepad detected" :
            $"Controller: {Gamepad.current.displayName}");
#endif
    }

    private void DrawVrScenarioRow(ScenarioId scenario)
    {
        GUILayout.BeginHorizontal(GUILayout.MinHeight(controlHeight * .72f));
        GUILayout.Label(ExperimentProtocol.DisplayName(scenario),
            GUILayout.Width(Mathf.Clamp(contentWidth * .16f, 130f, 260f)),
            GUILayout.MinHeight(controlHeight * .62f));
        DrawVrConditionButton("BASELINE", scenario, ExperimentCondition.Baseline);
        DrawVrConditionButton("RESEARCHER", scenario, ExperimentCondition.ResearcherHaptic);
        GUILayout.EndHorizontal();
        GUILayout.Space(5f);
    }

    private void DrawVrConditionButton(string label, ScenarioId scenario,
                                       ExperimentCondition condition)
    {
        string reason = protocol == null ? "Experiment Protocol is missing." : string.Empty;
        bool valid = protocol != null && protocol.TryResolveTrial(condition, scenario,
            out _, out reason);
        bool previousEnabled = GUI.enabled;
        GUI.enabled = valid;
        if (GUILayout.Button(valid ? label : label + "\nNOT READY",
                GUILayout.MinHeight(controlHeight * .62f)))
            StartVrConditionTest(scenario, condition);
        GUI.enabled = previousEnabled;
        if (!valid && string.IsNullOrWhiteSpace(status)) status = reason;
    }

    private void StartVrConditionTest(ScenarioId scenario, ExperimentCondition condition)
    {
        linkReceiver?.StopListening();
        hapticPlayer?.Stop();
        player?.Stop();
        float offset = player != null ? player.runtimeHapticOffsetSeconds : 0f;
        float amplitude = player != null ? player.runtimeAmplitudeScale : 1f;
        if (!flow.LaunchExperiment(protocol, condition, scenario, ParticipantHapticSlot.None,
                MobileLauncherDestination.HapticTest, offset, amplitude, out string reason))
        {
            status = reason;
            linkReceiver?.StartListening();
            return;
        }
        loadingFallbackPage = Page.HapticTest;
        page = Page.Loading;
    }

    private void EnterExperimentSetup()
    {
        linkReceiver?.StopListening();
        hapticPlayer?.Stop();
        player?.Stop();
        selectedCondition = null;
        selectedScenario = null;
        selectedParticipantSlot = ParticipantHapticSlot.None;
        status = protocol == null ? "Experiment Protocol is missing." : string.Empty;
        scroll = Vector2.zero;
        page = Page.ExperimentSetup;
    }

    private void DrawExperimentSetup()
    {
        Header("EXPERIMENT SETUP", "Select the condition again for every trial.");
        GUILayout.BeginHorizontal();

        GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true));
        BodyLabel("1. CONDITION");
        DrawConditionButton(ExperimentCondition.Baseline);
        DrawConditionButton(ExperimentCondition.ResearcherHaptic);
        DrawConditionButton(ExperimentCondition.ParticipantHaptic);
        GUILayout.EndVertical();

        GUILayout.Space(14f);
        GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true));
        BodyLabel("2. SCENARIO");
        DrawScenarioButton(ScenarioId.DentalDrill);
        DrawScenarioButton(ScenarioId.Toothbrush);
        DrawScenarioButton(ScenarioId.Apple);
        GUILayout.EndVertical();

        GUILayout.EndHorizontal();
        GUILayout.Space(10f);
        bool valid = TryResolveConfiguredSelection(out _, out string reason);
        BodyLabel(valid ? "Selected trial is ready." : reason, 1.25f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("BACK TO LAUNCHER", GUILayout.MinHeight(controlHeight)))
        {
            status = string.Empty;
            page = Page.Home;
        }
        GUI.enabled = valid;
        if (GUILayout.Button("CONTINUE", GUILayout.MinHeight(controlHeight)))
        {
            hapticPlayer?.Stop();
            page = selectedCondition == ExperimentCondition.ParticipantHaptic
                ? Page.ParticipantProfile : Page.Confirmation;
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
    }

    private void DrawConditionButton(ExperimentCondition condition)
    {
        ExperimentConditionPlan plan = protocol != null ? protocol.GetPlan(condition) : null;
        bool available = plan != null && plan.enabled;
        bool previousEnabled = GUI.enabled;
        GUI.enabled = available;
        string selected = selectedCondition == condition ? "  ✓" : string.Empty;
        string readiness = available ? string.Empty : "  ·  NOT READY";
        if (GUILayout.Button(ExperimentProtocol.DisplayName(condition).ToUpperInvariant() +
                readiness + selected, GUILayout.MinHeight(controlHeight)))
        {
            if (selectedCondition != condition)
                selectedParticipantSlot = ParticipantHapticSlot.None;
            selectedCondition = condition;
        }
        GUI.enabled = previousEnabled;
    }

    private void DrawScenarioButton(ScenarioId scenario)
    {
        string selected = selectedScenario == scenario ? "  ✓" : string.Empty;
        if (GUILayout.Button(ExperimentProtocol.DisplayName(scenario).ToUpperInvariant() + selected,
                GUILayout.MinHeight(controlHeight)))
        {
            if (selectedScenario != scenario)
                selectedParticipantSlot = ParticipantHapticSlot.None;
            selectedScenario = scenario;
        }
    }

    private void DrawParticipantProfile()
    {
        Header("SELECT HAPTIC PROFILE",
            "Preview the three vibrations and select one profile for this trial.");
        if (!TryGetParticipantAssignment(out ConditionScenarioAssignment assignment,
                out string reason))
        {
            BodyLabel(reason, 1.4f);
            if (GUILayout.Button("BACK", GUILayout.MinHeight(controlHeight)))
                page = Page.ExperimentSetup;
            return;
        }

        DrawParticipantOption(ParticipantHapticSlot.ProfileA, assignment.profileA);
        DrawParticipantOption(ParticipantHapticSlot.ProfileB, assignment.profileB);
        DrawParticipantOption(ParticipantHapticSlot.ProfileC, assignment.profileC);

        GUILayout.Space(10f);
        if (GUILayout.Button("STOP PREVIEW", GUILayout.MinHeight(controlHeight * .75f)))
            hapticPlayer?.Stop();
        BodyLabel(selectedParticipantSlot == ParticipantHapticSlot.None
            ? "Select Profile A, B, or C."
            : ExperimentProtocol.DisplayName(selectedParticipantSlot) + " selected.", 1.2f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("BACK", GUILayout.MinHeight(controlHeight)))
        {
            hapticPlayer?.Stop();
            page = Page.ExperimentSetup;
        }
        bool previousEnabled = GUI.enabled;
        GUI.enabled = selectedParticipantSlot != ParticipantHapticSlot.None;
        if (GUILayout.Button("CONTINUE", GUILayout.MinHeight(controlHeight)))
        {
            hapticPlayer?.Stop();
            page = Page.Confirmation;
        }
        GUI.enabled = previousEnabled;
        GUILayout.EndHorizontal();
    }

    private void DrawParticipantOption(ParticipantHapticSlot slot, HapticProfile profile)
    {
        GUILayout.BeginHorizontal(panelStyle, GUILayout.MinHeight(controlHeight));
        string label = ExperimentProtocol.DisplayName(slot);
        string selected = selectedParticipantSlot == slot ? "  ✓" : string.Empty;
        GUILayout.Label(label + selected,
            GUILayout.Width(Mathf.Clamp(contentWidth * .24f, 170f, 320f)),
            GUILayout.MinHeight(controlHeight * .72f));
        bool previousEnabled = GUI.enabled;
        GUI.enabled = profile != null && profile.IsPlayable(out _);
        if (GUILayout.Button("PREVIEW", GUILayout.MinHeight(controlHeight * .72f)))
        {
            hapticPlayer?.Stop();
            hapticPlayer?.PlayProfile(profile, AudioSettings.dspTime + .1d, 1f);
        }
        if (GUILayout.Button("SELECT", GUILayout.MinHeight(controlHeight * .72f)))
            selectedParticipantSlot = slot;
        GUI.enabled = previousEnabled;
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);
    }

    private void DrawConfirmation()
    {
        Header("CONFIRM EXPERIMENT SCENARIO", "Check the selection before placing the phone in Cardboard.");
        if (!TryResolveSelection(out ScenarioProfile profile, out HapticProfile haptic,
                out string reason))
        {
            BodyLabel(reason, 1.25f);
            if (GUILayout.Button("BACK", GUILayout.MinHeight(controlHeight))) page = Page.ExperimentSetup;
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true));
        BodyLabel("CONDITION");
        BodyLabel(ExperimentProtocol.DisplayName(selectedCondition.Value));
        GUILayout.Space(8f);
        BodyLabel("SCENARIO");
        BodyLabel(ExperimentProtocol.DisplayName(selectedScenario.Value));
        GUILayout.EndVertical();

        GUILayout.Space(14f);
        GUILayout.BeginVertical(panelStyle, GUILayout.ExpandWidth(true));
        BodyLabel("SCENARIO PROFILE");
        BodyLabel($"{profile.name}  ({profile.ProfileId} v{profile.Version})", 1.35f);
        GUILayout.Space(8f);
        BodyLabel("HAPTIC PROFILE");
        BodyLabel(haptic == null ? "None — Baseline" :
            selectedCondition == ExperimentCondition.ParticipantHaptic
                ? ExperimentProtocol.DisplayName(selectedParticipantSlot)
                : $"{haptic.name}  ({haptic.ProfileId} v{haptic.Version})", 1.35f);
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("BACK", GUILayout.MinHeight(controlHeight))) page = Page.ExperimentSetup;
        if (GUILayout.Button("START SCENARIO", GUILayout.MinHeight(controlHeight))) StartConfirmedTrial();
        GUILayout.EndHorizontal();
    }

    private void StartConfirmedTrial()
    {
        linkReceiver?.StopListening();
        hapticPlayer?.Stop();
        player?.Stop();
        if (!flow.LaunchExperiment(protocol, selectedCondition.Value, selectedScenario.Value,
                selectedParticipantSlot,
                MobileLauncherDestination.ExperimentSetup, 0f, 1f, out string reason))
        {
            status = reason;
            page = Page.ExperimentSetup;
            return;
        }
        loadingFallbackPage = Page.ExperimentSetup;
        page = Page.Loading;
    }

    private void DrawLoading()
    {
        GUILayout.FlexibleSpace();
        GUILayout.Label("INITIALIZING VR...", titleStyle,
            GUILayout.MinHeight(titleStyle.lineHeight * 1.55f));
        BodyLabel(flow != null ? flow.Status : "Preparing experiment...");
        BodyLabel("Insert the phone into the Cardboard viewer when the VR scene appears.", 1.4f);
        GUILayout.FlexibleSpace();

        if (flow != null && !flow.IsBusy && !flow.IsExperimentActive)
        {
            status = flow.Status;
            page = loadingFallbackPage;
            if (page == Page.HapticTest) linkReceiver?.StartListening();
        }
    }

    private bool TryResolveConfiguredSelection(out ScenarioProfile profile, out string reason)
    {
        profile = null;
        if (protocol == null)
        {
            reason = "Experiment Protocol is missing.";
            return false;
        }
        if (!selectedCondition.HasValue || !selectedScenario.HasValue)
        {
            reason = "Select both a condition and a scenario.";
            return false;
        }
        return protocol.TryResolveTrial(selectedCondition.Value, selectedScenario.Value,
            out profile, out reason);
    }

    private bool TryResolveSelection(out ScenarioProfile profile, out HapticProfile haptic,
                                     out string reason)
    {
        profile = null;
        haptic = null;
        if (protocol == null)
        {
            reason = "Experiment Protocol is missing.";
            return false;
        }
        if (!selectedCondition.HasValue || !selectedScenario.HasValue)
        {
            reason = "Select both a condition and a scenario.";
            return false;
        }
        ParticipantHapticSlot slot = selectedCondition == ExperimentCondition.ParticipantHaptic
            ? selectedParticipantSlot : ParticipantHapticSlot.None;
        return protocol.TryResolveTrial(selectedCondition.Value, selectedScenario.Value, slot,
            out profile, out haptic, out reason);
    }

    private bool TryGetParticipantAssignment(out ConditionScenarioAssignment assignment,
                                             out string reason)
    {
        assignment = null;
        if (protocol == null || !selectedScenario.HasValue)
        {
            reason = "Select a scenario first.";
            return false;
        }
        ExperimentConditionPlan plan = protocol.GetPlan(ExperimentCondition.ParticipantHaptic);
        assignment = plan != null ? plan.Find(selectedScenario.Value) : null;
        if (plan == null || !plan.enabled || assignment == null)
        {
            reason = "User Haptics is not configured for this scenario.";
            return false;
        }
        if (!protocol.TryResolveTrial(ExperimentCondition.ParticipantHaptic,
                selectedScenario.Value, out _, out reason)) return false;
        return true;
    }
}

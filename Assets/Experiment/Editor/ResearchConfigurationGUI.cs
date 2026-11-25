using System;
using UnityEditor;
using UnityEngine;

/// <summary>Assignments only. Generation parameters belong to SimplePulseGeneratorWindow.</summary>
public static class ResearchConfigurationGUI
{
    private static Vector2 scroll;
    private static string status;
    private static MessageType statusType;
    private static bool acquisitionExpanded;
    private static readonly bool[] pathsExpanded = new bool[2];

    public static void Draw(ExperimentProtocol protocol)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Experiment Configuration", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Toothbrush / Apple  x  Active / Passive  x  3 haptic conditions", EditorStyles.miniLabel);
        EditorGUILayout.Space(8);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        if (protocol.research.scenarios == null) protocol.research.scenarios = new System.Collections.Generic.List<ResearchScenarioSettings>();
        for (int i = 0; i < protocol.research.scenarios.Count; i++) DrawScenario(protocol, protocol.research.scenarios[i], i);

        EditorGUILayout.Space(6);
        acquisitionExpanded = EditorGUILayout.Foldout(acquisitionExpanded, "Controller & IMU", true);
        if (acquisitionExpanded)
        {
            EditorGUI.BeginChangeCheck();
            bool controller = EditorGUILayout.Toggle("Require checked A/B", protocol.research.requireController);
            int hz = EditorGUILayout.IntSlider("Requested IMU rate (Hz)", protocol.research.requestedImuHz, 20, 200);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(protocol, "Edit controller and IMU settings");
                protocol.research.requireController = controller; protocol.research.requestedImuHz = hz;
                EditorUtility.SetDirty(protocol);
            }
        }
        else EditorGUILayout.LabelField("Controller " + (protocol.research.requireController ? "required" : "optional") +
            "  |  IMU " + protocol.research.requestedImuHz + " Hz", EditorStyles.miniLabel);

        EditorGUILayout.Space(10);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Save Configuration", GUILayout.Height(30)))
            {
                AssetDatabase.SaveAssetIfDirty(protocol); status = "Configuration saved."; statusType = MessageType.Info;
            }
            if (GUILayout.Button("Validate 12 Conditions", GUILayout.Height(30)))
            {
                try { ResearchExperimentSetup.ValidateProtocol(protocol); status = "All 12 conditions are valid."; statusType = MessageType.Info; }
                catch (Exception exception) { status = exception.Message; statusType = MessageType.Error; }
            }
        }
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
        EditorGUILayout.EndScrollView();
    }

    private static void DrawScenario(ExperimentProtocol protocol, ResearchScenarioSettings settings, int index)
    {
        if (settings == null) { EditorGUILayout.HelpBox("Empty scenario assignment.", MessageType.Error); return; }
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField(settings.scenario != null ? settings.scenario.scenario.ToString() : "Scenario " + (index + 1), EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        var scenario = (ScenarioProfile)EditorGUILayout.ObjectField(new GUIContent("Scenario Profile", "Shared animation and sound for all six conditions of this scenario."), settings.scenario, typeof(ScenarioProfile), false);
        float duration = EditorGUILayout.FloatField(new GUIContent("Observation time (s)", "The same time applies to Active/Passive and all three haptic conditions."), settings.observationSeconds);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField(new GUIContent("Sound (from Scenario)", "Change the Sound field in the ScenarioProfile Inspector; no duplicate sound assignment is created here."),
                scenario != null ? scenario.sound : null, typeof(SoundProfile), false);
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("Haptic condition -> profile", EditorStyles.miniBoldLabel);
        EditorGUILayout.LabelField("No Haptic", "None");
        var simple = (HapticProfile)EditorGUILayout.ObjectField("Simple", settings.simple, typeof(HapticProfile), false);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField(new GUIContent("Sound-based (fixed)", "Existing Sound-based profile is preserved and fingerprint-checked."), settings.soundBased, typeof(HapticProfile), false);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(protocol, "Edit research profile assignments");
            settings.scenario = scenario; settings.observationSeconds = duration; settings.simple = simple;
            EditorUtility.SetDirty(protocol); status = "Unsaved configuration changes."; statusType = MessageType.Info;
        }
        if (simple != null)
            EditorGUILayout.LabelField("Simple pulse level", SimplePulseLevel(simple), EditorStyles.miniLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Open Generator")) SimplePulseGeneratorWindow.OpenFor(settings);
            using (new EditorGUI.DisabledScope(simple == null))
                if (GUILayout.Button("Play Simple...")) HapticProfilePlayerWindow.OpenProfile(simple);
            using (new EditorGUI.DisabledScope(settings.soundBased == null))
                if (GUILayout.Button("Play Sound-based...")) HapticProfilePlayerWindow.OpenProfile(settings.soundBased);
        }
        if (!settings.Validate(out string reason)) EditorGUILayout.HelpBox(reason, MessageType.Error);
        else if (simple.sourceScenario != null && simple.sourceScenario != scenario ||
                 simple.sourceSound != null && simple.sourceSound != scenario.sound)
            EditorGUILayout.HelpBox("Simple was generated from another source. Review the assignment or generate a new profile.", MessageType.Warning);
        else if (settings.soundBased.sourceSound != null && settings.soundBased.sourceSound != scenario.sound)
            EditorGUILayout.HelpBox("The fixed Sound-based profile references a different sound. Review the Scenario assignment.", MessageType.Warning);

        int foldoutIndex = Mathf.Clamp(index, 0, pathsExpanded.Length - 1);
        pathsExpanded[foldoutIndex] = EditorGUILayout.Foldout(pathsExpanded[foldoutIndex], "Asset paths / Inspect", true);
        if (pathsExpanded[foldoutIndex])
        {
            ResearchEditorGUI.AssetPath("Scenario", settings.scenario);
            ResearchEditorGUI.AssetPath("Sound", scenario != null ? scenario.sound : null);
            ResearchEditorGUI.AssetPath("Simple", settings.simple);
            ResearchEditorGUI.AssetPath("Sound-based", settings.soundBased);
        }
        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(8);
    }

    public static string SimplePulseLevel(HapticProfile profile)
    {
        int level = 0;
        foreach (int value in profile.Amplitudes)
        {
            if (value == 0) continue;
            if (level != 0 && value != level) return "Variable levels (invalid Simple)";
            level = value;
        }
        return level == 0 ? "No pulses" : level + " / 255 (same for every pulse)";
    }
}

public static class ResearchEditorGUI
{
    public static void AssetPath(string label, UnityEngine.Object value)
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel(label);
            EditorGUILayout.SelectableLabel(value != null ? AssetDatabase.GetAssetPath(value) : "None", EditorStyles.miniLabel, GUILayout.Height(18));
            using (new EditorGUI.DisabledScope(value == null))
                if (GUILayout.Button("Inspect", GUILayout.Width(60))) Selection.activeObject = value;
        }
    }
    public static void Waveform(string label, HapticProfile profile)
    {
        if (profile == null || profile.DurationMs <= 0) return;
        EditorGUILayout.LabelField(label + "  |  " + (profile.DurationMs / 1000d).ToString("F2") + " s", EditorStyles.miniBoldLabel);
        Rect rect = GUILayoutUtility.GetRect(100, 65, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.12f, .12f, .12f));
        double elapsed = 0;
        for (int i = 0; i < profile.SegmentCount; i++)
        {
            float x = rect.x + (float)(elapsed / profile.DurationMs) * rect.width;
            float width = (float)((double)profile.DurationsMs[i] / profile.DurationMs) * rect.width;
            float height = Mathf.Clamp01(profile.Amplitudes[i] * profile.defaultAmplitudeScale / 255f) * rect.height;
            EditorGUI.DrawRect(new Rect(x, rect.yMax - height, Mathf.Max(.5f, width), height), new Color(.2f, .75f, .8f));
            elapsed += profile.DurationsMs[i];
        }
    }
}

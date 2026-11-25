using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Window-local inputs, never a live reference to experiment assignments.</summary>
[Serializable]
public sealed class SimplePulseDraft
{
    public ScenarioProfile sourceScenario;
    public HapticProfile analysisTemplate;
    public int amplitude = 100;
    public float threshold = .15f;
    public int minimumSpanMs = 50;
    public int mergeGapMs = 60;
    public int maximumPulseMs = 200;
    public int silenceMs = 80;

    public static SimplePulseDraft FromAssignment(ResearchScenarioSettings source)
    {
        if (source == null) return new SimplePulseDraft();
        return new SimplePulseDraft {
            sourceScenario = source.scenario, analysisTemplate = source.soundBased,
            amplitude = source.simpleAmplitude, threshold = source.pulseThreshold,
            minimumSpanMs = source.minimumPulseMs, mergeGapMs = source.mergeGapMs,
            maximumPulseMs = source.maximumPulseMs, silenceMs = source.interPulseMs
        };
    }
    public ResearchScenarioSettings BuildSettings() => new ResearchScenarioSettings {
        scenario = sourceScenario, soundBased = analysisTemplate, simple = null,
        simpleAmplitude = amplitude, pulseThreshold = threshold, minimumPulseMs = minimumSpanMs,
        mergeGapMs = mergeGapMs, maximumPulseMs = maximumPulseMs, interPulseMs = silenceMs
    };
}

public sealed class SimplePulseGeneratorWindow : EditorWindow
{
    [SerializeField] private SimplePulseDraft draft = new SimplePulseDraft();
    [SerializeField] private HapticProfile generated;
    private Vector2 scroll;
    private bool advanced;
    private bool details;
    private string status;
    private MessageType statusType;

    [MenuItem("Tools/Haptics/Simple Pulse Generator", false, 25)]
    public static void OpenWindow() => GetWindow<SimplePulseGeneratorWindow>("Simple Pulses");
    public static void OpenFor(ResearchScenarioSettings assignment)
    {
        var window = GetWindow<SimplePulseGeneratorWindow>("Simple Pulses");
        window.draft = SimplePulseDraft.FromAssignment(assignment);
        window.generated = null; window.status = null; window.Show();
    }
    private void OnEnable() { minSize = new Vector2(520, 560); if (draft == null) draft = new SimplePulseDraft(); }

    /// <summary>Only the new output asset is saved; the experiment protocol is not touched.</summary>
    public static HapticProfile GenerateNew(SimplePulseDraft input, string outputPath)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        outputPath = (outputPath ?? "").Replace('\\', '/');
        if (!outputPath.StartsWith("Assets/", StringComparison.Ordinal) || outputPath.Contains("../") ||
            !outputPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a new .asset path inside Assets.");
        if (AssetDatabase.LoadMainAssetAtPath(outputPath) != null || File.Exists(outputPath))
            throw new ArgumentException("This asset already exists. Choose a new name; assigned profiles are not overwritten.");
        var temporarySettings = input.BuildSettings();
        ResearchExperimentSetup.GenerateSimple(temporarySettings, outputPath);
        temporarySettings.simple.SetIdentity(Path.GetFileNameWithoutExtension(outputPath), 1);
        EditorUtility.SetDirty(temporarySettings.simple);
        AssetDatabase.SaveAssetIfDirty(temporarySettings.simple);
        return temporarySettings.simple;
    }
    private void OnGUI()
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Simple Pulse Generator", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Create a new profile. Experiment assignments stay unchanged.", EditorStyles.miniLabel);
        EditorGUILayout.Space(8);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("1. Analysis source", EditorStyles.boldLabel);
        draft.sourceScenario = (ScenarioProfile)EditorGUILayout.ObjectField("Source Scenario", draft.sourceScenario, typeof(ScenarioProfile), false);
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Sound (from Scenario)", draft.sourceScenario != null ? draft.sourceScenario.sound : null, typeof(SoundProfile), false);
        draft.analysisTemplate = (HapticProfile)EditorGUILayout.ObjectField(new GUIContent("Analysis settings from", "Optional HapticProfile whose audio-conversion settings are read. Its waveform is never changed."), draft.analysisTemplate, typeof(HapticProfile), false);
        if (draft.analysisTemplate == null) EditorGUILayout.LabelField("Using default audio analysis settings.", EditorStyles.miniLabel);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.LabelField("2. Pulse shape", EditorStyles.boldLabel);
        draft.amplitude = EditorGUILayout.IntSlider("Fixed amplitude", draft.amplitude, 1, 255);
        draft.maximumPulseMs = EditorGUILayout.IntField("Pulse width limit (ms)", draft.maximumPulseMs);
        draft.silenceMs = EditorGUILayout.IntField("Silence between pulses (ms)", draft.silenceMs);
        advanced = EditorGUILayout.Foldout(advanced, "Detection settings", true);
        if (advanced)
        {
            draft.threshold = EditorGUILayout.Slider("Envelope threshold", draft.threshold, .01f, 1f);
            draft.minimumSpanMs = EditorGUILayout.IntField("Minimum detected span (ms)", draft.minimumSpanMs);
            draft.mergeGapMs = EditorGUILayout.IntField("Merge short gaps (ms)", draft.mergeGapMs);
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space(8);
        bool usable = draft.sourceScenario != null && draft.sourceScenario.sound != null && draft.sourceScenario.sound.clip != null;
        using (new EditorGUI.DisabledScope(!usable))
            if (GUILayout.Button("Generate & Save New Profile...", GUILayout.Height(34))) GenerateFromDialog();
        if (!usable) EditorGUILayout.HelpBox("Choose a ScenarioProfile with a SoundProfile and AudioClip.", MessageType.Info);
        if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);

        if (generated != null)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("3. Last saved result", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("HapticProfile", generated, typeof(HapticProfile), false);
            ResearchEditorGUI.AssetPath("Output", generated);
            EditorGUILayout.LabelField("Pulse level", ResearchConfigurationGUI.SimplePulseLevel(generated));
            ResearchEditorGUI.Waveform("Analysis reference", draft.analysisTemplate);
            ResearchEditorGUI.Waveform("Saved Simple", generated);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Play on Android...")) HapticProfilePlayerWindow.OpenProfile(generated);
                if (GUILayout.Button("Open Experiment Configuration")) EditorApplication.ExecuteMenuItem("Tools/Experiment/Experiment Configuration");
            }
            EditorGUILayout.LabelField("To apply: choose this asset in Configuration > Simple, then Save.", EditorStyles.miniLabel);
            details = EditorGUILayout.Foldout(details, "Generation details", true);
            if (details) EditorGUILayout.HelpBox(generated.calibrationNote, MessageType.None);
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndScrollView();
    }
    private void GenerateFromDialog()
    {
        try
        {
            ManualHapticGeneratorWindow.EnsureFolder(ResearchExperimentSetup.SimpleFolder);
            string suggestion = AssetDatabase.GenerateUniqueAssetPath(ResearchExperimentSetup.SimpleFolder + "/" + draft.sourceScenario.scenario + "_SimplePulse.asset");
            string path = EditorUtility.SaveFilePanelInProject("Save new Simple profile", Path.GetFileNameWithoutExtension(suggestion),
                "asset", "Generation does not assign this profile to the experiment.", ResearchExperimentSetup.SimpleFolder);
            if (string.IsNullOrEmpty(path)) return;
            generated = GenerateNew(draft, path);
            status = "Saved new profile. Experiment assignment unchanged."; statusType = MessageType.Info;
        }
        catch (Exception exception) { status = exception.Message; statusType = MessageType.Error; }
    }
}

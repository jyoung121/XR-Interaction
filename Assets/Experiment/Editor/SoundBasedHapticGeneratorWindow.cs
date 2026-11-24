using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class SoundBasedHapticGeneratorWindow : EditorWindow
{
    private const string SoundDirectory = "Assets/Profiles/Sounds", HapticDirectory = "Assets/Profiles/Haptics/SoundBased";
    [SerializeField] private AudioClip sourceAudio; [SerializeField] private SoundProfile soundProfile; [SerializeField] private HapticProfile hapticProfile;
    [SerializeField] private ScenarioProfile scenarioProfile;
    [SerializeField] private AudioHapticConversionSettings settings; [SerializeField] private int offsetMs;
    [SerializeField] private HapticStyleGenerationSettings styleSettings;
    private ScenarioId styleSettingsScenario;
    private AudioHapticConversionResult result;
    private ScenarioHapticStyleGenerationResult styleResult;
    private ScenarioHapticStyleSet generatedStyleSet;
    private Vector2 scroll; private string status = "Select scenario audio and analyze its envelope.";
    [MenuItem("Tools/Haptics/Sound-based Haptic Generator")] public static void OpenWindow() => GetWindow<SoundBasedHapticGeneratorWindow>("Sound-based Haptic");
    private void OnEnable()
    {
        if (settings.windowMs <= 0) settings = AudioHapticConversionSettings.Default;
        if (styleSettings.curveKeyIntervalMs <= 0)
            styleSettings = HapticStyleGenerationSettings.ForScenario(styleSettingsScenario);
    }
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll); EditorGUILayout.LabelField("Sound-based Haptic Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Create the initial waveform from audio, then open it in Manual Generator for researcher correction and locking.", MessageType.Info);
        ScenarioProfile nextScenario = (ScenarioProfile)EditorGUILayout.ObjectField(
            "Scenario Profile", scenarioProfile, typeof(ScenarioProfile), false);
        if (nextScenario != scenarioProfile)
        {
            scenarioProfile = nextScenario;
            if (scenarioProfile != null)
            {
                styleSettingsScenario = scenarioProfile.scenario;
                styleSettings = HapticStyleGenerationSettings.ForScenario(scenarioProfile.scenario);
                soundProfile = scenarioProfile.sound;
                sourceAudio = soundProfile != null ? soundProfile.clip : null;
                styleResult = null;
                generatedStyleSet = scenarioProfile.participantStyleSet;
            }
        }
        sourceAudio = (AudioClip)EditorGUILayout.ObjectField("Source Audio", sourceAudio, typeof(AudioClip), false);
        soundProfile = (SoundProfile)EditorGUILayout.ObjectField("Sound Profile", soundProfile, typeof(SoundProfile), false);
        hapticProfile = (HapticProfile)EditorGUILayout.ObjectField("Haptic Profile", hapticProfile, typeof(HapticProfile), false);
        settings.windowMs = EditorGUILayout.IntSlider("Window (ms)", settings.windowMs, 10, 200); settings.rmsWeight = EditorGUILayout.Slider("RMS weight", settings.rmsWeight, 0, 1);
        settings.gateDb = EditorGUILayout.Slider("Noise gate (dB)", settings.gateDb, -80, -10); settings.gamma = EditorGUILayout.Slider("Envelope gamma", settings.gamma, .2f, 2);
        settings.minimumAmplitude = EditorGUILayout.IntSlider("Minimum amplitude", settings.minimumAmplitude, 1, 254); settings.quantizationLevels = EditorGUILayout.IntSlider("Amplitude levels", settings.quantizationLevels, 2, 32);
        settings.attackMs = EditorGUILayout.IntSlider("Attack (ms)", settings.attackMs, 0, 200); settings.releaseMs = EditorGUILayout.IntSlider("Release (ms)", settings.releaseMs, 0, 500); offsetMs = EditorGUILayout.IntField("Haptic offset (ms)", offsetMs);
        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("Contact Curve / Style Preset", EditorStyles.boldLabel);
        styleSettings.contactAttackMs = EditorGUILayout.IntSlider("Contact attack (ms)", styleSettings.contactAttackMs, 20, 1000);
        styleSettings.contactReleaseMs = EditorGUILayout.IntSlider("Contact release (ms)", styleSettings.contactReleaseMs, 20, 1500);
        styleSettings.curveKeyIntervalMs = EditorGUILayout.IntSlider("Curve key interval (ms)", styleSettings.curveKeyIntervalMs, 25, 1000);
        styleSettings.curveTolerance = EditorGUILayout.Slider("Curve simplification", styleSettings.curveTolerance, 0.001f, 0.1f);
        styleSettings.balancedDetail = EditorGUILayout.Slider("Balanced detail", styleSettings.balancedDetail, 0f, 1f);
        styleSettings.texturedDetail = EditorGUILayout.Slider("Textured detail", styleSettings.texturedDetail, 1f, 2f);
        if (sourceAudio != null) EditorGUILayout.LabelField($"{sourceAudio.length:0.###} s / {sourceAudio.channels} ch / {sourceAudio.frequency} Hz");
        if (GUILayout.Button("Analyze Audio")) Analyze();
        if (result != null) { int chunks = HapticWaveformChunker.Create(result.TimingsMs, result.Amplitudes).Count; EditorGUILayout.HelpBox($"{result.SourceWindowCount} windows → {result.Amplitudes.Length} segments / {result.DurationMs / 1000f:0.###} s / {chunks} chunk(s)", MessageType.None); DrawEnvelope(result.Amplitudes); }
        using (new EditorGUI.DisabledScope(scenarioProfile == null))
            if (GUILayout.Button("Generate Contact Curves + 3 Style Profiles")) GenerateScenarioStyles();
        if (styleResult != null)
        {
            EditorGUILayout.LabelField("Smooth"); DrawEnvelope(styleResult.smooth.Amplitudes);
            EditorGUILayout.LabelField("Balanced"); DrawEnvelope(styleResult.balanced.Amplitudes);
            EditorGUILayout.LabelField("Textured"); DrawEnvelope(styleResult.textured.Amplitudes);
        }
        if (generatedStyleSet != null)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Generated Style Assets", EditorStyles.boldLabel);
            DrawGeneratedAsset("Profile A / Smooth", generatedStyleSet.smooth);
            DrawGeneratedAsset("Profile B / Balanced", generatedStyleSet.balanced);
            DrawGeneratedAsset("Profile C / Textured", generatedStyleSet.textured);
        }
        using (new EditorGUILayout.HorizontalScope()) { if (GUILayout.Button("Save Profiles")) SaveProfiles(); if (GUILayout.Button("ADB Forward")) Run(HapticTransportClient.ForwardAdb); }
        using (new EditorGUILayout.HorizontalScope()) { if (GUILayout.Button("Play on Android")) Play(); using (new EditorGUI.DisabledScope(hapticProfile == null)) if (GUILayout.Button("Open in Manual Correction")) ManualHapticGeneratorWindow.EditProfile(hapticProfile); }
        EditorGUILayout.HelpBox(status, MessageType.None); EditorGUILayout.EndScrollView();
    }
    private void Analyze() { try { result = AudioHapticConverter.Convert(sourceAudio, settings); status = $"Analyzed {result.DurationMs / 1000f:0.###} seconds."; } catch (Exception ex) { result = null; status = ex.Message; } }
    private void GenerateScenarioStyles()
    {
        try
        {
            if (scenarioProfile == null) throw new InvalidOperationException("Select a ScenarioProfile.");
            if (scenarioProfile.sound == null || scenarioProfile.sound.clip == null)
                throw new InvalidOperationException("The ScenarioProfile has no usable sound clip.");
            sourceAudio = scenarioProfile.sound.clip;
            soundProfile = scenarioProfile.sound;
            styleResult = ScenarioHapticStyleGenerator.Generate(scenarioProfile, settings,
                styleSettings, true);
            result = styleResult.analysis;
            ScenarioHapticStyleSet set = ScenarioHapticStyleGenerator.SaveGeneratedAssets(
                scenarioProfile, settings, styleResult);
            generatedStyleSet = set;
            Selection.activeObject = set;
            status = "Updated v002 contact curves and saved Smooth/Balanced/Textured profiles. " +
                     "Researcher Haptics was not overwritten.";
        }
        catch (Exception ex)
        {
            styleResult = null;
            status = ex.Message;
            Debug.LogException(ex);
        }
    }
    private void SaveProfiles()
    {
        if (result == null || sourceAudio == null) { status = "Analyze the source audio first."; return; }
        ManualHapticGeneratorWindow.EnsureFolder(SoundDirectory); ManualHapticGeneratorWindow.EnsureFolder(HapticDirectory);
        if (soundProfile == null) { string path = EditorUtility.SaveFilePanelInProject("Save Sound Profile", sourceAudio.name + "_SoundProfile", "asset", "Save sound metadata.", SoundDirectory); if (string.IsNullOrEmpty(path)) return; soundProfile = CreateInstance<SoundProfile>(); soundProfile.clip = sourceAudio; soundProfile.SetIdentity(Path.GetFileNameWithoutExtension(path)); AssetDatabase.CreateAsset(soundProfile, path); } else soundProfile.clip = sourceAudio;
        if (hapticProfile == null) { string path = EditorUtility.SaveFilePanelInProject("Save Haptic Profile", sourceAudio.name + "_SoundBased_HapticProfile", "asset", "Save generated haptics.", HapticDirectory); if (string.IsNullOrEmpty(path)) return; hapticProfile = CreateInstance<HapticProfile>(); AssetDatabase.CreateAsset(hapticProfile, path); }
        string sourcePath = AssetDatabase.GetAssetPath(sourceAudio); string hash = string.IsNullOrEmpty(sourcePath) ? "" : AssetDatabase.GetAssetDependencyHash(sourcePath).ToString();
        soundProfile.sourceAssetHash = hash; hapticProfile.ApplyGenerated(soundProfile, settings, result, offsetMs, hash); EditorUtility.SetDirty(soundProfile); EditorUtility.SetDirty(hapticProfile); AssetDatabase.SaveAssets(); Selection.activeObject = hapticProfile;
        status = "Saved sound-based profile. Next: manual correction and researcher lock.";
    }
    private void Play() { if (result == null) { status = "Analyze first."; return; } Run(() => HapticTransportClient.Play("sound-preview", result.TimingsMs, result.Amplitudes)); }
    private static void DrawEnvelope(int[] amplitudes)
    {
        Rect rect = GUILayoutUtility.GetRect(10, 100, GUILayout.ExpandWidth(true)); EditorGUI.DrawRect(rect, new Color(.08f, .09f, .11f)); if (amplitudes == null || amplitudes.Length == 0) return;
        int columns = Mathf.Min(Mathf.RoundToInt(rect.width), amplitudes.Length); for (int x = 0; x < columns; x++) { int from = x * amplitudes.Length / columns, to = Mathf.Max(from + 1, (x + 1) * amplitudes.Length / columns), peak = 0; for (int i = from; i < to && i < amplitudes.Length; i++) peak = Mathf.Max(peak, amplitudes[i]); float h = rect.height * peak / 255f; EditorGUI.DrawRect(new Rect(rect.x + x, rect.yMax - h, 1, h), new Color(.25f, .78f, .95f)); }
    }
    private static void DrawGeneratedAsset(string label, HapticProfile profile)
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.ObjectField(label, profile, typeof(HapticProfile), false);
            string path = profile != null ? AssetDatabase.GetAssetPath(profile) : "Missing";
            EditorGUILayout.SelectableLabel(path, EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(profile == null))
                {
                    if (GUILayout.Button("Select")) Selection.activeObject = profile;
                    if (GUILayout.Button("Ping")) EditorGUIUtility.PingObject(profile);
                    if (GUILayout.Button("Copy Path")) EditorGUIUtility.systemCopyBuffer = path;
                    if (GUILayout.Button("Open Manual"))
                        ManualHapticGeneratorWindow.EditProfile(profile);
                }
            }
        }
    }
    private void Run(Func<string> action) { try { status = action(); } catch (Exception ex) { status = ex.Message; } }
}

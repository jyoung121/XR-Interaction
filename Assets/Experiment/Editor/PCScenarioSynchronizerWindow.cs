using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class PCScenarioSynchronizerWindow : EditorWindow
{
    private const string VariantRoot = "Assets/Profiles/Scenarios/Variants";
    private const int VariantPickerId = 872341;

    [SerializeField] private ScenarioProfile sourceProfile;
    [SerializeField] private ScenarioId defaultScenario;
    [SerializeField] private bool loopPreview;
    [SerializeField] private float repeatDelaySeconds;
    private ScenarioProfile workingCopy;
    private SerializedObject serializedWorkingCopy;
    private Vector2 scroll;
    private bool dirty;
    private string status = "Load a default or variant, edit a working copy, then save and preview.";
    private MessageType statusType = MessageType.Info;

    [MenuItem("Tools/Experiment/PC Scenario Synchronizer", false, 20)]
    private static void Open() => GetWindow<PCScenarioSynchronizerWindow>("Scenario Sync");

    private void OnEnable()
    {
        if (sourceProfile != null && workingCopy == null) CreateWorkingCopy(sourceProfile);
    }

    private void OnDestroy()
    {
        if (workingCopy != null) DestroyImmediate(workingCopy);
    }

    private void OnGUI()
    {
        HandleObjectPicker();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawProfileLoader();
        EditorGUILayout.Space(8f);
        DrawWorkingCopy();
        EditorGUILayout.Space(10f);
        DrawPreviewControls();
        EditorGUILayout.HelpBox(status, statusType);
        EditorGUILayout.EndScrollView();
    }

    private void DrawProfileLoader()
    {
        EditorGUILayout.LabelField("Scenario Profile Version", EditorStyles.boldLabel);
        defaultScenario = (ScenarioId)EditorGUILayout.EnumPopup("Default Scenario", defaultScenario);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Load Default")) LoadDefault();
            if (GUILayout.Button("Load Variant..."))
                EditorGUIUtility.ShowObjectPicker<ScenarioProfile>(sourceProfile, false, string.Empty,
                    VariantPickerId);
        }

        ScenarioProfile next = (ScenarioProfile)EditorGUILayout.ObjectField(
            "Loaded Profile", sourceProfile, typeof(ScenarioProfile), false);
        if (next != sourceProfile) TryLoad(next);
        if (sourceProfile != null)
        {
            string kind = IsDefaultProfile(sourceProfile) ? "DEFAULT · protected" : "VARIANT";
            EditorGUILayout.LabelField(kind + "  |  " + AssetDatabase.GetAssetPath(sourceProfile),
                EditorStyles.miniLabel);
        }
    }

    private void DrawWorkingCopy()
    {
        if (serializedWorkingCopy == null)
        {
            EditorGUILayout.HelpBox("Load a Scenario Profile to begin.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Working Copy" + (dirty ? "  * UNSAVED" : string.Empty),
            EditorStyles.boldLabel);
        serializedWorkingCopy.Update();
        EditorGUILayout.PropertyField(serializedWorkingCopy.FindProperty("sound"));
        EditorGUILayout.PropertyField(serializedWorkingCopy.FindProperty("animationDurationSeconds"));
        EditorGUILayout.PropertyField(serializedWorkingCopy.FindProperty("soundOffsetSeconds"));
        EditorGUILayout.PropertyField(serializedWorkingCopy.FindProperty("hapticOffsetSeconds"));
        EditorGUILayout.PropertyField(serializedWorkingCopy.FindProperty("animationBlocks"), true);
        if (serializedWorkingCopy.ApplyModifiedProperties())
        {
            dirty = true;
            SetStatus("Working copy changed. Save as a variant before previewing.", MessageType.Warning);
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(sourceProfile == null || IsDefaultProfile(sourceProfile)))
                if (GUILayout.Button("Save Variant")) SaveCurrentVariant();
            if (GUILayout.Button("Save As Variant...")) SaveAsVariant();
            if (GUILayout.Button("Revert")) RevertWorkingCopy();
        }
    }

    private void DrawPreviewControls()
    {
        EditorGUILayout.LabelField("PC Preview", EditorStyles.boldLabel);
        loopPreview = EditorGUILayout.Toggle("Loop Preview", loopPreview);
        using (new EditorGUI.DisabledScope(!loopPreview))
            repeatDelaySeconds = Mathf.Max(0f,
                EditorGUILayout.FloatField("Repeat Delay Seconds", repeatDelaySeconds));

        if (GUILayout.Button("Open PCScenarioTestScene")) OpenScene();
        using (new EditorGUI.DisabledScope(sourceProfile == null || EditorApplication.isPlaying))
            if (GUILayout.Button("Save and Start Preview")) SaveAndPreview();
    }

    private void LoadDefault()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            ExperimentProjectSetup.CatalogPath);
        if (catalog == null)
        {
            SetStatus("ProfileCatalog is missing. Run project setup first.", MessageType.Error);
            return;
        }
        TryLoad(catalog.FindScenario(defaultScenario));
    }

    private bool TryLoad(ScenarioProfile next)
    {
        if (next == sourceProfile) return true;
        if (dirty)
        {
            int choice = EditorUtility.DisplayDialogComplex("Unsaved Scenario Profile",
                "The working copy has unsaved changes.", "Save", "Cancel", "Discard");
            if (choice == 1) return false;
            if (choice == 0)
            {
                bool saved = sourceProfile != null && !IsDefaultProfile(sourceProfile)
                    ? SaveCurrentVariant()
                    : SaveAsVariant();
                if (!saved) return false;
            }
        }

        sourceProfile = next;
        CreateWorkingCopy(sourceProfile);
        SetStatus(sourceProfile == null ? "Profile cleared." : "Loaded " + sourceProfile.name,
            MessageType.Info);
        return true;
    }

    private void CreateWorkingCopy(ScenarioProfile source)
    {
        if (workingCopy != null) DestroyImmediate(workingCopy);
        workingCopy = source != null ? Instantiate(source) : null;
        if (workingCopy != null)
        {
            workingCopy.name = source.name + " (Working Copy)";
            workingCopy.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
            serializedWorkingCopy = new SerializedObject(workingCopy);
        }
        else
        {
            serializedWorkingCopy = null;
        }
        dirty = false;
    }

    private bool SaveCurrentVariant()
    {
        if (sourceProfile == null || workingCopy == null || IsDefaultProfile(sourceProfile))
            return false;
        Undo.RecordObject(sourceProfile, "Save Scenario Variant");
        string assetName = sourceProfile.name;
        EditorUtility.CopySerialized(workingCopy, sourceProfile);
        sourceProfile.name = assetName;
        sourceProfile.hideFlags = HideFlags.None;
        EditorUtility.SetDirty(sourceProfile);
        AssetDatabase.SaveAssetIfDirty(sourceProfile);
        CreateWorkingCopy(sourceProfile);
        SetStatus("Saved " + AssetDatabase.GetAssetPath(sourceProfile), MessageType.Info);
        return true;
    }

    private bool SaveAsVariant()
    {
        if (workingCopy == null) return false;
        string directory = VariantRoot + "/" + workingCopy.scenario;
        ManualHapticGeneratorWindow.EnsureFolder(directory);
        string suggested = sourceProfile != null
            ? sourceProfile.name + "_v" + (workingCopy.Version + 1).ToString("000")
            : workingCopy.scenario + "_ScenarioProfile_v001";
        string path = EditorUtility.SaveFilePanelInProject("Save Scenario Profile Variant",
            suggested, "asset", "Save a protected Scenario Profile variant.", directory);
        if (string.IsNullOrEmpty(path)) return false;

        var asset = CreateInstance<ScenarioProfile>();
        EditorUtility.CopySerialized(workingCopy, asset);
        string fileName = Path.GetFileNameWithoutExtension(path);
        asset.name = fileName;
        asset.hideFlags = HideFlags.None;
        asset.SetIdentity(fileName, workingCopy.Version + 1);
        AssetDatabase.CreateAsset(asset, path);
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssetIfDirty(asset);
        sourceProfile = asset;
        CreateWorkingCopy(asset);
        Selection.activeObject = asset;
        SetStatus("Saved new variant " + path, MessageType.Info);
        return true;
    }

    private void RevertWorkingCopy()
    {
        CreateWorkingCopy(sourceProfile);
        SetStatus("Working copy reverted to the saved asset.", MessageType.Info);
    }

    private void SaveAndPreview()
    {
        if (dirty)
        {
            if (IsDefaultProfile(sourceProfile))
            {
                if (!SaveAsVariant()) return;
            }
            else if (!SaveCurrentVariant())
            {
                return;
            }
        }

        if (sourceProfile == null) return;
        if (!sourceProfile.ValidateForPlayback(ExperimentCondition.Baseline, out string reason) ||
            !ScenarioAnimationTimeline.Validate(sourceProfile, out reason))
        {
            SetStatus(reason, MessageType.Error);
            Debug.LogError("[ScenarioSync] " + reason);
            return;
        }

        if (!OpenScene()) return;
        ScenarioPlaybackController player = Object.FindAnyObjectByType<ScenarioPlaybackController>();
        Camera viewerCamera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (player == null || viewerCamera == null)
        {
            SetStatus("PCScenarioTestScene requires a ScenarioPlaybackController and viewer camera.",
                MessageType.Error);
            return;
        }

        EnsurePreviewComponents(player, viewerCamera);
        PCScenarioPreviewController preview = player.GetComponent<PCScenarioPreviewController>();
        bool sceneWasDirty = player.gameObject.scene.isDirty;
        PCScenarioPreviewEditorSession.Prepare(sourceProfile, player, preview, sceneWasDirty,
            loopPreview, repeatDelaySeconds);

        player.playOnStart = false;
        preview.player = player;
        preview.profile = sourceProfile;
        preview.autoStart = true;
        preview.repeat = loopPreview;
        preview.repeatDelaySeconds = repeatDelaySeconds;
        EditorUtility.SetDirty(player);
        EditorUtility.SetDirty(preview);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

        SetStatus($"Previewing {sourceProfile.name}. RMB look, WASD/QE move, 1-6 presets, R reset.",
            MessageType.Info);
        Close();
        EditorApplication.isPlaying = true;
    }

    private static void EnsurePreviewComponents(ScenarioPlaybackController player, Camera camera)
    {
        bool persistentChange = false;
        MirrorScenarioAnimationDriver driver = player.GetComponent<MirrorScenarioAnimationDriver>();
        if (driver == null)
        {
            driver = Undo.AddComponent<MirrorScenarioAnimationDriver>(player.gameObject);
            persistentChange = true;
        }
        if (camera.GetComponent<PCScenarioCameraController>() == null)
        {
            Undo.AddComponent<PCScenarioCameraController>(camera.gameObject);
            persistentChange = true;
        }
        if (player.GetComponent<PCScenarioPreviewController>() == null)
        {
            Undo.AddComponent<PCScenarioPreviewController>(player.gameObject);
            persistentChange = true;
        }
        if (driver.viewer != camera.transform)
        {
            driver.viewer = camera.transform;
            EditorUtility.SetDirty(driver);
            persistentChange = true;
        }
        if (player.animationDriver != driver)
        {
            player.animationDriver = driver;
            EditorUtility.SetDirty(player);
            persistentChange = true;
        }
        if (persistentChange) EditorSceneManager.SaveScene(player.gameObject.scene);
    }

    private bool OpenScene()
    {
        if (!File.Exists(ExperimentProjectSetup.PcScenarioTestScenePath))
        {
            SetStatus("Run Create Profiles and Scenes first.", MessageType.Error);
            return false;
        }
        EditorSceneManager.OpenScene(ExperimentProjectSetup.PcScenarioTestScenePath);
        SetStatus("PC test scene opened.", MessageType.Info);
        return true;
    }

    private static bool IsDefaultProfile(ScenarioProfile candidate)
    {
        if (candidate == null) return false;
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            ExperimentProjectSetup.CatalogPath);
        return catalog != null && catalog.scenarios != null && catalog.scenarios.Contains(candidate);
    }

    private void HandleObjectPicker()
    {
        Event current = Event.current;
        if (current == null || current.commandName != "ObjectSelectorClosed" ||
            EditorGUIUtility.GetObjectPickerControlID() != VariantPickerId) return;
        TryLoad(EditorGUIUtility.GetObjectPickerObject() as ScenarioProfile);
        current.Use();
    }

    internal void RestoreAfterPreview(string profilePath, bool repeat, float delay)
    {
        loopPreview = repeat;
        repeatDelaySeconds = delay;
        ScenarioProfile restored = AssetDatabase.LoadAssetAtPath<ScenarioProfile>(profilePath);
        sourceProfile = restored;
        CreateWorkingCopy(restored);
        SetStatus("Preview ended. Restored " + (restored != null ? restored.name : "profile"),
            MessageType.Info);
    }

    private void SetStatus(string value, MessageType type)
    {
        status = value;
        statusType = type;
        Repaint();
    }
}

[InitializeOnLoad]
internal static class PCScenarioPreviewEditorSession
{
    private const string Prefix = "Experiment.PCPreview.";
    private const string PendingKey = Prefix + "Pending";
    private const string ProfilePathKey = Prefix + "ProfilePath";
    private const string PreviousPreviewProfilePathKey = Prefix + "PreviousPreviewProfilePath";
    private const string PreviousPlayerProfilePathKey = Prefix + "PreviousPlayerProfilePath";
    private const string PreviousPlayerAutoKey = Prefix + "PreviousPlayerAuto";
    private const string PreviousPreviewAutoKey = Prefix + "PreviousPreviewAuto";
    private const string PreviousRepeatKey = Prefix + "PreviousRepeat";
    private const string PreviousDelayKey = Prefix + "PreviousDelay";
    private const string SceneWasDirtyKey = Prefix + "SceneWasDirty";
    private const string RequestedRepeatKey = Prefix + "RequestedRepeat";
    private const string RequestedDelayKey = Prefix + "RequestedDelay";

    static PCScenarioPreviewEditorSession()
    {
        EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
    }

    internal static void Prepare(ScenarioProfile selected, ScenarioPlaybackController player,
        PCScenarioPreviewController preview, bool sceneWasDirty, bool requestedRepeat,
        float requestedDelay)
    {
        SessionState.SetBool(PendingKey, true);
        SessionState.SetString(ProfilePathKey, AssetDatabase.GetAssetPath(selected));
        SessionState.SetString(PreviousPlayerProfilePathKey,
            AssetDatabase.GetAssetPath(player.profile));
        SessionState.SetString(PreviousPreviewProfilePathKey,
            AssetDatabase.GetAssetPath(preview.profile));
        SessionState.SetBool(PreviousPlayerAutoKey, player.playOnStart);
        SessionState.SetBool(PreviousPreviewAutoKey, preview.autoStart);
        SessionState.SetBool(PreviousRepeatKey, preview.repeat);
        SessionState.SetFloat(PreviousDelayKey, preview.repeatDelaySeconds);
        SessionState.SetBool(SceneWasDirtyKey, sceneWasDirty);
        SessionState.SetBool(RequestedRepeatKey, requestedRepeat);
        SessionState.SetFloat(RequestedDelayKey, requestedDelay);
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(PendingKey, false)) return;
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
            EditorApplication.delayCall += () =>
                EditorApplication.ExecuteMenuItem("Window/General/Game");
            return;
        }
        if (state != PlayModeStateChange.EnteredEditMode) return;

        SessionState.SetBool(PendingKey, false);
        Scene scene = SceneManager.GetSceneByPath(ExperimentProjectSetup.PcScenarioTestScenePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(ExperimentProjectSetup.PcScenarioTestScenePath);

        ScenarioPlaybackController player = Object.FindAnyObjectByType<ScenarioPlaybackController>();
        PCScenarioPreviewController preview = player != null
            ? player.GetComponent<PCScenarioPreviewController>()
            : null;
        if (player != null && preview != null)
        {
            player.profile = AssetDatabase.LoadAssetAtPath<ScenarioProfile>(
                SessionState.GetString(PreviousPlayerProfilePathKey, string.Empty));
            player.playOnStart = SessionState.GetBool(PreviousPlayerAutoKey, false);
            preview.profile = AssetDatabase.LoadAssetAtPath<ScenarioProfile>(
                SessionState.GetString(PreviousPreviewProfilePathKey, string.Empty));
            preview.autoStart = SessionState.GetBool(PreviousPreviewAutoKey, false);
            preview.repeat = SessionState.GetBool(PreviousRepeatKey, false);
            preview.repeatDelaySeconds = SessionState.GetFloat(PreviousDelayKey, 0f);
            EditorUtility.SetDirty(player);
            EditorUtility.SetDirty(preview);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!SessionState.GetBool(SceneWasDirtyKey, false))
                EditorSceneManager.SaveScene(scene);
        }

        string profilePath = SessionState.GetString(ProfilePathKey, string.Empty);
        bool repeat = SessionState.GetBool(RequestedRepeatKey, false);
        float delay = SessionState.GetFloat(RequestedDelayKey, 0f);
        EditorApplication.delayCall += () =>
        {
            PCScenarioSynchronizerWindow window =
                EditorWindow.GetWindow<PCScenarioSynchronizerWindow>("Scenario Sync");
            window.RestoreAfterPreview(profilePath, repeat, delay);
            window.Focus();
        };
    }
}

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public sealed class PCScenarioSynchronizerWindow : EditorWindow
{
    [SerializeField] private ScenarioProfile profile; private SerializedObject serializedProfile; private Vector2 scroll; private string status = "Edit shared blocks and audio offset, then preview without XR.";
    [MenuItem("Tools/Experiment/PC Scenario Synchronizer")] private static void Open() => GetWindow<PCScenarioSynchronizerWindow>("Scenario Sync");
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll); var next = (ScenarioProfile)EditorGUILayout.ObjectField("Scenario Profile", profile, typeof(ScenarioProfile), false); if (next != profile) { profile = next; serializedProfile = profile != null ? new SerializedObject(profile) : null; }
        if (serializedProfile != null) { serializedProfile.Update(); EditorGUILayout.PropertyField(serializedProfile.FindProperty("sound")); EditorGUILayout.PropertyField(serializedProfile.FindProperty("animationDurationSeconds")); EditorGUILayout.PropertyField(serializedProfile.FindProperty("soundOffsetSeconds")); EditorGUILayout.PropertyField(serializedProfile.FindProperty("hapticOffsetSeconds")); EditorGUILayout.PropertyField(serializedProfile.FindProperty("animationBlocks"), true); if (serializedProfile.ApplyModifiedProperties()) { EditorUtility.SetDirty(profile); status = "Updated. Every condition uses these same audio/animation values."; } }
        if (GUILayout.Button("Open PCScenarioTestScene")) OpenScene();
        using (new EditorGUI.DisabledScope(profile == null || EditorApplication.isPlaying))
            if (GUILayout.Button("Assign Profile and Enter Play Mode")) AssignAndPlay();
        EditorGUILayout.HelpBox(status, MessageType.Info); EditorGUILayout.EndScrollView();
    }

    private void AssignAndPlay()
    {
        // Commit values that are still being edited in this window before opening the
        // preview scene. The runtime must read the same asset values shown in the UI.
        GUI.FocusControl(null);
        if (serializedProfile != null)
        {
            serializedProfile.ApplyModifiedProperties();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssetIfDirty(profile);
        }

        if (!profile.ValidateForPlayback(ExperimentCondition.Baseline, out string reason))
        {
            status = reason;
            Debug.LogError("[ScenarioSync] " + status);
            return;
        }
        if (!ScenarioAnimationTimeline.Validate(profile, out reason))
        {
            status = reason;
            Debug.LogError("[ScenarioSync] " + status);
            return;
        }

        if (!OpenScene()) return;

        ScenarioPlaybackController player = Object.FindAnyObjectByType<ScenarioPlaybackController>();
        if (player == null)
        {
            status = "PCScenarioTestScene has no ScenarioPlaybackController.";
            Debug.LogError("[ScenarioSync] " + status);
            return;
        }

        Camera viewerCamera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();
        if (viewerCamera == null)
        {
            status = "PCScenarioTestScene has no viewer camera.";
            Debug.LogError("[ScenarioSync] " + status);
            return;
        }

        MirrorScenarioAnimationDriver driver = player.GetComponent<MirrorScenarioAnimationDriver>();
        if (driver == null)
            driver = Undo.AddComponent<MirrorScenarioAnimationDriver>(player.gameObject);
        if (viewerCamera.GetComponent<PCScenarioCameraController>() == null)
            Undo.AddComponent<PCScenarioCameraController>(viewerCamera.gameObject);

        Undo.RecordObjects(new Object[] { player, driver }, "Configure PC Scenario Preview");
        driver.viewer = viewerCamera.transform;
        player.animationDriver = driver;
        player.profile = profile;
        player.condition = ExperimentCondition.Baseline;
        player.playOnStart = true;

        EditorUtility.SetDirty(driver);
        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();

        status = $"Previewing {profile.name}. RMB look, WASD/QE move, 1-6 presets, R reset.";
        EditorApplication.isPlaying = true;
    }

    private bool OpenScene()
    {
        if (!System.IO.File.Exists(ExperimentProjectSetup.PcScenarioTestScenePath))
        {
            status = "Run Tools/Experiment/Create Profiles and Scenes first.";
            return false;
        }

        EditorSceneManager.OpenScene(ExperimentProjectSetup.PcScenarioTestScenePath);
        status = "PC test scene opened.";
        return true;
    }
}

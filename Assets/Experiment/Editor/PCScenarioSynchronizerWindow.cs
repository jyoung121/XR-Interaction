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
        using (new EditorGUI.DisabledScope(profile == null)) if (GUILayout.Button("Assign Profile and Enter Play Mode")) { OpenScene(); var player = Object.FindAnyObjectByType<ScenarioPlaybackController>(); if (player != null) { Undo.RecordObject(player, "Assign Scenario Profile"); player.profile = profile; player.condition = ExperimentCondition.Baseline; EditorUtility.SetDirty(player); EditorSceneManager.MarkSceneDirty(player.gameObject.scene); EditorSceneManager.SaveOpenScenes(); EditorApplication.isPlaying = true; } }
        EditorGUILayout.HelpBox(status, MessageType.Info); EditorGUILayout.EndScrollView();
    }
    private void OpenScene() { if (!System.IO.File.Exists(ExperimentProjectSetup.PcScenarioTestScenePath)) { status = "Run Tools/Experiment/Create Profiles and Scenes first."; return; } EditorSceneManager.OpenScene(ExperimentProjectSetup.PcScenarioTestScenePath); status = "PC test scene opened."; }
}

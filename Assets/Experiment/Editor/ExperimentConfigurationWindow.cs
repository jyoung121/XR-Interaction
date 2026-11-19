using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed class ExperimentConfigurationWindow : EditorWindow
{
    private static readonly ExperimentCondition[] Conditions =
    {
        ExperimentCondition.Baseline,
        ExperimentCondition.ResearcherHaptic,
        ExperimentCondition.ParticipantHaptic
    };

    private static readonly ScenarioId[] Scenarios =
    {
        ScenarioId.DentalDrill,
        ScenarioId.Toothbrush,
        ScenarioId.Apple
    };

    private ExperimentProtocol protocol;
    private Vector2 scroll;
    private string status = "Assign one Scenario Profile to every enabled condition/scenario slot.";
    private MessageType statusType = MessageType.Info;

    [MenuItem("Tools/Experiment/Experiment Configuration", false, 10)]
    private static void Open() => GetWindow<ExperimentConfigurationWindow>("Experiment Config");

    private void OnEnable() => LoadProtocol();

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Android Experiment Configuration", EditorStyles.boldLabel);
        EditorGUILayout.Space();
        ExperimentProtocol selected = (ExperimentProtocol)EditorGUILayout.ObjectField(
            "Experiment Protocol", protocol, typeof(ExperimentProtocol), false);
        if (selected != protocol)
        {
            protocol = selected;
            EnsurePlans();
        }

        if (protocol == null)
        {
            EditorGUILayout.HelpBox("AndroidExperimentProtocol is missing. Run Create Profiles and Scenes once.",
                MessageType.Error);
            if (GUILayout.Button("Reload Default Protocol")) LoadProtocol();
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawCommonSettings();
        EditorGUILayout.Space(8f);
        DrawAssignmentGrid();
        EditorGUILayout.Space(12f);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Use Catalog Defaults")) UseCatalogDefaults();
            if (GUILayout.Button("Validate Configuration")) ValidateConfiguration();
            if (GUILayout.Button("Revert")) Revert();
            if (GUILayout.Button("Save")) Save();
        }
        EditorGUILayout.HelpBox(status, statusType);
        EditorGUILayout.EndScrollView();
    }

    private void DrawCommonSettings()
    {
        EditorGUILayout.LabelField("Session Settings", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        bool randomize = EditorGUILayout.Toggle("Randomize Scenario Order", protocol.randomizeOrder);
        int seed = EditorGUILayout.IntField("Random Seed", protocol.randomSeed);
        if (!EditorGUI.EndChangeCheck()) return;
        Undo.RecordObject(protocol, "Edit Experiment Session Settings");
        protocol.randomizeOrder = randomize;
        protocol.randomSeed = seed;
        EditorUtility.SetDirty(protocol);
    }

    private void DrawAssignmentGrid()
    {
        EditorGUILayout.LabelField("Condition × Scenario Profile", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Variants may be assigned directly. They stay outside ProfileCatalog until explicitly promoted.",
            MessageType.None);

        foreach (ExperimentCondition condition in Conditions)
        {
            ExperimentConditionPlan plan = protocol.GetPlan(condition);
            if (plan == null) continue;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUI.BeginChangeCheck();
            bool enabled = EditorGUILayout.ToggleLeft(
                ExperimentProtocol.DisplayName(condition), plan.enabled, EditorStyles.boldLabel);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(protocol, "Toggle Experiment Condition");
                plan.enabled = enabled;
                EditorUtility.SetDirty(protocol);
            }

            using (new EditorGUI.DisabledScope(!plan.enabled))
            {
                foreach (ScenarioId scenario in Scenarios)
                {
                    ConditionScenarioAssignment assignment = plan.Find(scenario);
                    if (assignment == null) continue;
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(ExperimentProtocol.DisplayName(scenario),
                            GUILayout.Width(120f));
                        ScenarioProfile next = (ScenarioProfile)EditorGUILayout.ObjectField(
                            assignment.profile, typeof(ScenarioProfile), false);
                        int repetitions = EditorGUILayout.IntField(assignment.repetitions,
                            GUILayout.Width(42f));
                        if (next != assignment.profile || repetitions != assignment.repetitions)
                        {
                            Undo.RecordObject(protocol, "Assign Scenario Profile");
                            assignment.profile = next;
                            assignment.repetitions = Mathf.Max(1, repetitions);
                            EditorUtility.SetDirty(protocol);
                        }
                    }

                    if (assignment.profile != null && assignment.profile.scenario != scenario)
                        EditorGUILayout.HelpBox(
                            $"{assignment.profile.name} is a {assignment.profile.scenario} profile.",
                            MessageType.Error);
                }
            }
            EditorGUILayout.EndVertical();
        }
    }

    private void UseCatalogDefaults()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            ExperimentProjectSetup.CatalogPath);
        if (catalog == null)
        {
            SetStatus("ProfileCatalog is missing.", MessageType.Error);
            return;
        }

        Undo.RecordObject(protocol, "Use Catalog Scenario Defaults");
        foreach (ExperimentCondition condition in Conditions)
        {
            ExperimentConditionPlan plan = protocol.GetPlan(condition);
            if (plan == null) continue;
            foreach (ScenarioId scenario in Scenarios)
            {
                ConditionScenarioAssignment assignment = plan.Find(scenario);
                if (assignment != null) assignment.profile = catalog.FindScenario(scenario);
            }
        }
        EditorUtility.SetDirty(protocol);
        SetStatus("Catalog defaults assigned. User Haptics remains unavailable until participant haptics are assigned.",
            MessageType.Info);
    }

    private void ValidateConfiguration()
    {
        var errors = new List<string>();
        int enabledCount = 0;
        foreach (ExperimentCondition condition in Conditions)
        {
            ExperimentConditionPlan plan = protocol.GetPlan(condition);
            if (plan == null)
            {
                errors.Add($"{ExperimentProtocol.DisplayName(condition)} plan is missing.");
                continue;
            }
            if (!plan.enabled) continue;
            enabledCount++;
            if (!protocol.TryValidatePlan(condition, out string reason)) errors.Add(reason);
        }
        if (enabledCount == 0) errors.Add("At least one condition must be enabled.");
        if (!protocol.TryValidateSharedStimuli(out string consistencyReason))
            errors.Add(consistencyReason);

        if (errors.Count == 0)
            SetStatus("Configuration is valid. Enabled conditions can be started on Android.",
                MessageType.Info);
        else
            SetStatus(string.Join("\n", errors), MessageType.Error);
    }

    private void Save()
    {
        EditorUtility.SetDirty(protocol);
        AssetDatabase.SaveAssetIfDirty(protocol);
        ValidateConfiguration();
        if (statusType != MessageType.Error)
            SetStatus("Saved " + AssetDatabase.GetAssetPath(protocol), MessageType.Info);
    }

    private void Revert()
    {
        string path = AssetDatabase.GetAssetPath(protocol);
        if (string.IsNullOrEmpty(path)) return;
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(path);
        EnsurePlans();
        SetStatus("Reverted unsaved protocol changes.", MessageType.Info);
    }

    private void LoadProtocol()
    {
        protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(
            ExperimentProjectSetup.ProtocolPath);
        EnsurePlans();
    }

    private void EnsurePlans()
    {
        if (protocol == null) return;
        if (!protocol.EnsureConditionPlans()) return;
        EditorUtility.SetDirty(protocol);
        SetStatus("Legacy protocol assignments were migrated in memory. Save to keep them.",
            MessageType.Warning);
    }

    private void SetStatus(string value, MessageType type)
    {
        status = value;
        statusType = type;
        Repaint();
    }
}

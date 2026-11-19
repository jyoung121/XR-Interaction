using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentBuild
{
    private static readonly ScenarioId[] RequiredScenarios =
    {
        ScenarioId.DentalDrill,
        ScenarioId.Toothbrush,
        ScenarioId.Apple
    };

    [MenuItem("Tools/Experiment/Validate Final Experiment", false, 40)]
    public static void ValidateFinalExperiment()
    {
        var errors = new List<string>();
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            ExperimentProjectSetup.CatalogPath);
        ExperimentProtocol protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(
            ExperimentProjectSetup.ProtocolPath);

        ValidateCatalog(catalog, errors);
        ValidateProtocol(protocol, catalog, errors);
        ValidateSceneReferences(protocol, catalog, errors);

        if (errors.Count == 0)
            ValidateAnimationRuntime(catalog, errors);

        if (errors.Count > 0)
            throw new BuildFailedException(
                "Final experiment validation failed:\n- " + string.Join("\n- ", errors));

        Debug.Log("[ExperimentBuild] Final experiment validation passed.");
    }

    // Compatibility entry point for existing batch commands.
    public static void ValidateExperimentProfiles() => ValidateFinalExperiment();

    [MenuItem("Tools/Experiment/Build/Experiment Android", false, 50)]
    public static void BuildExperimentAndroid()
    {
        ValidateFinalExperiment();
        BuildAndroid(ExperimentProjectSetup.ExperimentScenePath,
            "Builds/ExperimentAndroid/Experiment.apk");
    }

    [MenuItem("Tools/Experiment/Build/Mobile Test Android", false, 51)]
    public static void BuildMobileTestAndroid()
    {
        var errors = new List<string>();
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            ExperimentProjectSetup.CatalogPath);
        ValidateMobileTestScene(catalog, errors);
        ThrowIfInvalid("Mobile test scene validation failed", errors);
        BuildAndroid(ExperimentProjectSetup.MobileTestScenePath,
            "Builds/MobileTestAndroid/MobileTest.apk");
    }

    private static void ValidateCatalog(ProfileCatalog catalog, List<string> errors)
    {
        if (catalog == null)
        {
            errors.Add("ProfileCatalog is missing. Run Experiment/Setup first.");
            return;
        }

        if (catalog.scenarios == null || catalog.scenarios.Count == 0)
        {
            errors.Add("ProfileCatalog has no scenarios.");
            return;
        }

        var scenarioIds = new HashSet<ScenarioId>();
        foreach (ScenarioProfile scenario in catalog.scenarios)
        {
            if (scenario == null)
            {
                errors.Add("ProfileCatalog contains a null scenario.");
                continue;
            }

            if (!scenarioIds.Add(scenario.scenario))
                errors.Add($"Scenario '{scenario.scenario}' is duplicated in ProfileCatalog.");

            ValidateScenario(scenario, errors);
        }

        foreach (ScenarioId required in RequiredScenarios)
            if (!scenarioIds.Contains(required))
                errors.Add($"Required scenario '{required}' is missing from ProfileCatalog.");
    }

    private static void ValidateScenario(ScenarioProfile scenario, List<string> errors)
    {
        if (scenario.sound == null)
        {
            errors.Add($"{scenario.name}: SoundProfile is missing.");
        }
        else
        {
            if (!scenario.sound.IsUsable(out string soundReason))
                errors.Add($"{scenario.name}: {soundReason}");
            if (scenario.sound.placeholder)
                errors.Add($"{scenario.name}: sound is still marked as a placeholder.");
        }

        if (scenario.researcherHaptic == null)
        {
            errors.Add($"{scenario.name}: researcher HapticProfile is missing.");
        }
        else
        {
            if (scenario.researcherHaptic.Origin != HapticProfileOrigin.ResearcherCalibrated)
                errors.Add($"{scenario.name}: researcher profile is not manually calibrated/locked.");
            if (!scenario.researcherHaptic.IsPlayable(out string hapticReason))
                errors.Add($"{scenario.name}: researcher profile is not playable: {hapticReason}");
        }

        ValidateAnimationTimeline(scenario, errors);
    }

    private static void ValidateAnimationTimeline(ScenarioProfile scenario, List<string> errors)
    {
        if (scenario.animationDurationSeconds <= 0f)
            errors.Add($"{scenario.name}: animation duration must be greater than zero.");

        if (scenario.animationBlocks == null || scenario.animationBlocks.Count == 0)
        {
            errors.Add($"{scenario.name}: no animation blocks are configured.");
            return;
        }

        var ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var blocks = new List<ScenarioAnimationBlock>();
        var kinds = new HashSet<AnimationBlockKind>();
        for (int i = 0; i < scenario.animationBlocks.Count; i++)
        {
            ScenarioAnimationBlock block = scenario.animationBlocks[i];
            if (block == null)
            {
                errors.Add($"{scenario.name}: animation block {i} is null.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(block.blockId))
                errors.Add($"{scenario.name}: animation block {i} has no ID.");
            else if (!ids.Add(block.blockId))
                errors.Add($"{scenario.name}: animation block ID '{block.blockId}' is duplicated.");

            if (block.startSeconds < 0f || block.durationSeconds <= 0f)
                errors.Add($"{scenario.name}/{block.blockId}: time range is invalid.");
            else if (block.startSeconds + block.durationSeconds >
                     scenario.animationDurationSeconds + 0.0001f)
                errors.Add($"{scenario.name}/{block.blockId}: block exceeds the scenario duration.");

            blocks.Add(block);
            kinds.Add(block.kind);
        }

        blocks.Sort((a, b) => a.startSeconds.CompareTo(b.startSeconds));
        for (int i = 1; i < blocks.Count; i++)
        {
            ScenarioAnimationBlock previous = blocks[i - 1];
            ScenarioAnimationBlock current = blocks[i];
            if (current.startSeconds < previous.startSeconds + previous.durationSeconds - 0.0001f)
                errors.Add($"{scenario.name}: animation blocks '{previous.blockId}' and " +
                           $"'{current.blockId}' overlap.");
        }

        RequireAnimationKind(scenario, kinds, AnimationBlockKind.Approach, errors);
        RequireAnimationKind(scenario, kinds, AnimationBlockKind.Contact, errors);
        if (scenario.scenario != ScenarioId.Apple)
            RequireAnimationKind(scenario, kinds, AnimationBlockKind.Retreat, errors);
    }

    private static void RequireAnimationKind(ScenarioProfile scenario,
        HashSet<AnimationBlockKind> kinds, AnimationBlockKind required, List<string> errors)
    {
        if (!kinds.Contains(required))
            errors.Add($"{scenario.name}: required '{required}' animation block is missing.");
    }

    private static void ValidateProtocol(ExperimentProtocol protocol, ProfileCatalog catalog,
        List<string> errors)
    {
        if (protocol == null)
        {
            errors.Add("Android experiment protocol is missing. Run Experiment/Setup first.");
            return;
        }

        if (protocol.targetPlatform != RuntimePlatform.Android)
            errors.Add("Final experiment protocol must target Android.");
        if (protocol.participantConditionEnabled)
            errors.Add("Participant haptic condition must remain disabled until its flow is defined.");

        var conditions = new HashSet<ExperimentCondition>();
        if (protocol.conditions == null || protocol.conditions.Count == 0)
        {
            errors.Add("Experiment protocol has no conditions.");
        }
        else
        {
            foreach (ExperimentCondition condition in protocol.conditions)
                if (!conditions.Add(condition))
                    errors.Add($"Experiment condition '{condition}' is duplicated.");

            if (!conditions.Contains(ExperimentCondition.Baseline))
                errors.Add("Experiment protocol is missing the Baseline condition.");
            if (!conditions.Contains(ExperimentCondition.ResearcherHaptic))
                errors.Add("Experiment protocol is missing the ResearcherHaptic condition.");
        }

        var protocolScenarios = new HashSet<ScenarioId>();
        if (protocol.scenarios == null || protocol.scenarios.Count == 0)
        {
            errors.Add("Experiment protocol has no scenarios.");
        }
        else
        {
            for (int i = 0; i < protocol.scenarios.Count; i++)
            {
                ProtocolScenario entry = protocol.scenarios[i];
                if (entry == null || entry.scenario == null)
                {
                    errors.Add($"Experiment protocol scenario {i} is missing.");
                    continue;
                }

                if (entry.repetitions < 1)
                    errors.Add($"{entry.scenario.name}: repetitions must be at least one.");
                if (!protocolScenarios.Add(entry.scenario.scenario))
                    errors.Add($"Protocol scenario '{entry.scenario.scenario}' is duplicated.");
                if (catalog != null && (catalog.scenarios == null ||
                    !catalog.scenarios.Contains(entry.scenario)))
                    errors.Add($"{entry.scenario.name}: protocol scenario is not in ProfileCatalog.");
            }
        }

        foreach (ScenarioId required in RequiredScenarios)
            if (!protocolScenarios.Contains(required))
                errors.Add($"Required scenario '{required}' is missing from the protocol.");

        if (catalog != null && (catalog.protocols == null || !catalog.protocols.Contains(protocol)))
            errors.Add("Android experiment protocol is not registered in ProfileCatalog.");
    }

    private static void ValidateSceneReferences(ExperimentProtocol protocol, ProfileCatalog catalog,
        List<string> errors)
    {
        ValidateExperimentScene(protocol, errors);
        ValidateMobileTestScene(catalog, errors);
        ValidatePcScenarioScene(errors);
    }

    private static void ValidateExperimentScene(ExperimentProtocol protocol, List<string> errors)
    {
        WithScene(ExperimentProjectSetup.ExperimentScenePath, "ExperimentScene", errors, scene =>
        {
            ExperimentSessionController session = FindInScene<ExperimentSessionController>(scene);
            ScenarioPlaybackController player = FindInScene<ScenarioPlaybackController>(scene);
            if (session == null) errors.Add("ExperimentScene: ExperimentSessionController is missing.");
            if (player == null) errors.Add("ExperimentScene: ScenarioPlaybackController is missing.");
            if (session != null && session.protocol != protocol)
                errors.Add("ExperimentScene: final Android protocol is not assigned.");
            if (session != null && session.player != player)
                errors.Add("ExperimentScene: session player reference is missing or incorrect.");
            ValidatePlayer("ExperimentScene", player, errors);
        });
    }

    private static void ValidateMobileTestScene(ProfileCatalog catalog, List<string> errors)
    {
        WithScene(ExperimentProjectSetup.MobileTestScenePath, "MobileTestScene", errors, scene =>
        {
            MobileTestController controller = FindInScene<MobileTestController>(scene);
            ScenarioPlaybackController player = FindInScene<ScenarioPlaybackController>(scene);
            LongHapticPlayer hapticPlayer = FindInScene<LongHapticPlayer>(scene);
            HapticLinkReceiver receiver = FindInScene<HapticLinkReceiver>(scene);
            if (controller == null) errors.Add("MobileTestScene: MobileTestController is missing.");
            if (controller != null && controller.catalog != catalog)
                errors.Add("MobileTestScene: ProfileCatalog is not assigned.");
            if (controller != null && controller.player != player)
                errors.Add("MobileTestScene: scenario player reference is missing or incorrect.");
            if (controller != null && controller.hapticPlayer != hapticPlayer)
                errors.Add("MobileTestScene: haptic player reference is missing or incorrect.");
            if (controller != null && controller.linkReceiver != receiver)
                errors.Add("MobileTestScene: PC haptic link receiver is missing or incorrect.");
            ValidatePlayer("MobileTestScene", player, errors);
        });
    }

    private static void ValidatePcScenarioScene(List<string> errors)
    {
        WithScene(ExperimentProjectSetup.PcScenarioTestScenePath, "PCScenarioTestScene", errors,
            scene => ValidatePlayer("PCScenarioTestScene",
                FindInScene<ScenarioPlaybackController>(scene), errors));
    }

    private static void ValidatePlayer(string sceneName, ScenarioPlaybackController player,
        List<string> errors)
    {
        if (player == null)
        {
            errors.Add($"{sceneName}: ScenarioPlaybackController is missing.");
            return;
        }

        if (player.animationDriver == null)
            errors.Add($"{sceneName}: animation driver is not assigned.");
        if (player.GetComponent<AudioSource>() == null)
            errors.Add($"{sceneName}: AudioSource is missing from the scenario player.");
        if (player.GetComponent<LongHapticPlayer>() == null)
            errors.Add($"{sceneName}: LongHapticPlayer is missing from the scenario player.");
    }

    private static void ValidateAnimationRuntime(ProfileCatalog catalog, List<string> errors)
    {
        if (catalog == null || catalog.scenarios == null) return;

        GameObject viewer = new GameObject("Animation Validation Viewer");
        viewer.transform.position = new Vector3(0f, 1.65f, 0f);
        MirrorRig rig = null;
        try
        {
            foreach (ScenarioProfile scenario in catalog.scenarios)
            {
                if (scenario == null) continue;
                rig = MirrorRig.Create(viewer.transform);
                if (rig == null)
                {
                    errors.Add($"{scenario.name}: MirrorRig could not be created.");
                    continue;
                }

                if (scenario.scenario == ScenarioId.Apple)
                    rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Apple);
                else if (scenario.scenario == ScenarioId.DentalDrill)
                    rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Drill);
                else
                    rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Brush);

                foreach (ScenarioAnimationBlock block in scenario.animationBlocks)
                    if (block != null)
                        rig.StepForPreview(block.startSeconds + block.durationSeconds * 0.5f);

                rig.Dispose();
                rig = null;
            }
        }
        catch (System.Exception exception)
        {
            errors.Add("Animation runtime check failed: " + exception.Message);
        }
        finally
        {
            if (rig != null) rig.Dispose();
            Object.DestroyImmediate(viewer);
        }
    }

    private static void WithScene(string path, string label, List<string> errors,
        System.Action<Scene> validate)
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            errors.Add($"{label}: scene asset is missing at '{path}'.");
            return;
        }

        Scene scene = SceneManager.GetSceneByPath(path);
        bool openedForValidation = !scene.IsValid() || !scene.isLoaded;
        try
        {
            if (openedForValidation)
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            validate(scene);
        }
        catch (System.Exception exception)
        {
            errors.Add($"{label}: could not inspect scene: {exception.Message}");
        }
        finally
        {
            if (openedForValidation && scene.IsValid() && scene.isLoaded)
                EditorSceneManager.CloseScene(scene, true);
        }
    }

    private static T FindInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T component = root.GetComponentInChildren<T>(true);
            if (component != null) return component;
        }
        return null;
    }

    private static void ThrowIfInvalid(string title, List<string> errors)
    {
        if (errors.Count > 0)
            throw new BuildFailedException(title + ":\n- " + string.Join("\n- ", errors));
    }

    private static void BuildAndroid(string scene, string output)
    {
        var options = new BuildPlayerOptions
        {
            scenes = new[] { scene },
            locationPathName = output,
            target = BuildTarget.Android
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new BuildFailedException(report.summary.result.ToString());
    }
}

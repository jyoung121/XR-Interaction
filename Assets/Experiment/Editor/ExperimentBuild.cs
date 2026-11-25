using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Management;

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
        ProfileCatalog catalog = LoadCatalog();
        ExperimentProtocol protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(
            ExperimentProjectSetup.ProtocolPath);

        if (protocol != null && protocol.research.enabled)
        {
            ResearchExperimentSetup.ValidateResearch();
            ValidateExperimentScene(protocol, errors);
            ValidateMobileTestScene(catalog, errors);
            ThrowIfInvalid("Research scene validation failed", errors);
            return;
        }

        ValidateCatalog(catalog, errors);
        ValidateProtocol(protocol, catalog, errors);
        ValidateSceneReferences(protocol, catalog, errors);

        if (errors.Count == 0)
            ValidateAnimationRuntime(catalog, protocol, errors);

        ThrowIfInvalid("Final experiment validation failed", errors);
        Debug.Log("[ExperimentBuild] Final experiment validation passed.");
    }

    // Compatibility entry point for existing batch commands.
    public static void ValidateExperimentProfiles() => ValidateFinalExperiment();

    // Developer/batch entry points remain callable without adding normal Tools menu items.
    public static void ValidateAnimationTimelines()
    {
        var errors = new List<string>();
        ValidateAnimationTimelines(LoadCatalog(), errors);
        ThrowIfInvalid("Animation timeline validation failed", errors);
        Debug.Log("[ExperimentBuild] Animation timeline validation passed.");
    }

    public static void ValidateAnimationRuntime()
    {
        var errors = new List<string>();
        ProfileCatalog catalog = LoadCatalog();
        ValidateAnimationTimelines(catalog, errors);
        if (errors.Count == 0)
            ValidateAnimationRuntime(catalog,
                AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(ExperimentProjectSetup.ProtocolPath),
                errors);
        ThrowIfInvalid("Animation runtime validation failed", errors);
        Debug.Log("[ExperimentBuild] Animation runtime validation passed.");
    }

    [MenuItem("Tools/Experiment/Build/Unified Android", false, 50)]
    public static void BuildUnifiedAndroid()
    {
        ValidateFinalExperiment();
        BuildUnified("Builds/UnifiedAndroid/UnifiedExperiment.apk");
    }

    [MenuItem("Tools/Experiment/Build/Unified Android (Development)", false, 51)]
    public static void BuildUnifiedAndroidDevelopment()
    {
        ValidateUnifiedRuntime();
        BuildUnified("Builds/UnifiedAndroid/UnifiedExperiment-Development.apk");
    }

    [MenuItem("Tools/Experiment/Build/Experiment Android", false, 52)]
    public static void BuildExperimentAndroid()
    {
        ValidateFinalExperiment();
        if (ResearchExperimentSetup.Protocol?.research.enabled == true)
        {
            BuildUnified("Builds/ExperimentAndroid/Experiment.apk");
            return;
        }
        BuildAndroid(ExperimentProjectSetup.ExperimentScenePath,
            "Builds/ExperimentAndroid/Experiment.apk");
    }

    public static void BuildMobileTestAndroid()
    {
        ValidateUnifiedRuntime();
        BuildUnified("Builds/MobileTestAndroid/MobileTest.apk");
    }

    private static void BuildUnified(string output) => BuildAndroid(new[]
    {
        ExperimentProjectSetup.MobileTestScenePath,
        ExperimentProjectSetup.ExperimentScenePath
    }, output, false);

    private static void ValidateUnifiedRuntime()
    {
        if (ResearchExperimentSetup.Protocol?.research.enabled == true)
        {
            ValidateFinalExperiment();
            return;
        }
        var errors = new List<string>();
        ProfileCatalog catalog = LoadCatalog();
        ExperimentProtocol protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(
            ExperimentProjectSetup.ProtocolPath);
        if (catalog == null) errors.Add("ProfileCatalog is missing. Run Experiment/Setup first.");
        if (protocol == null)
        {
            errors.Add("Experiment Protocol is missing. Run Experiment/Setup first.");
        }
        else
        {
            foreach (ExperimentCondition condition in new[]
                     { ExperimentCondition.Baseline, ExperimentCondition.ResearcherHaptic,
                       ExperimentCondition.ParticipantHaptic })
                if (!protocol.TryValidatePlan(condition, out string reason))
                    errors.Add(reason);
        }
        ValidateExperimentScene(protocol, errors);
        ValidateMobileTestScene(catalog, errors);
        ThrowIfInvalid("Unified Android development validation failed", errors);
        Debug.Log("[ExperimentBuild] Unified Android development validation passed.");
    }

    private static ProfileCatalog LoadCatalog() =>
        AssetDatabase.LoadAssetAtPath<ProfileCatalog>(ExperimentProjectSetup.CatalogPath);

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
        if (!ScenarioAnimationTimeline.Validate(scenario, out string timelineReason))
            errors.Add($"{scenario.name}: {timelineReason}");
        if (!scenario.ValidateInteraction(out string interactionReason))
            errors.Add($"{scenario.name}: {interactionReason}");
        bool shouldBeInteractive = scenario.scenario == ScenarioId.Toothbrush ||
                                   scenario.scenario == ScenarioId.Apple;
        if (scenario.RequiresUserStart != shouldBeInteractive)
            errors.Add($"{scenario.name}: final interaction mode must be " +
                       (shouldBeInteractive ? "UserStartStop." : "Automatic."));

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
    }

    private static void ValidateAnimationTimelines(ProfileCatalog catalog, List<string> errors)
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

        foreach (ScenarioProfile scenario in catalog.scenarios)
        {
            if (scenario == null)
            {
                errors.Add("ProfileCatalog contains a null scenario.");
                continue;
            }
            if (!ScenarioAnimationTimeline.Validate(scenario, out string reason))
                errors.Add($"{scenario.name}: {reason}");
        }
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
        if (protocol.conditionPlans == null || protocol.conditionPlans.Count == 0)
        {
            errors.Add("Experiment protocol has not been migrated to condition-specific assignments. Open Experiment Configuration and save it.");
        }
        else
        {
            var conditions = new HashSet<ExperimentCondition>();
            foreach (ExperimentConditionPlan plan in protocol.conditionPlans)
            {
                if (plan == null)
                {
                    errors.Add("Experiment protocol contains an empty condition plan.");
                    continue;
                }
                if (!conditions.Add(plan.condition))
                    errors.Add($"Experiment condition '{plan.condition}' is duplicated.");
                if (!plan.enabled) continue;
                if (!protocol.TryValidatePlan(plan.condition, out string reason))
                {
                    errors.Add(reason);
                    continue;
                }
                foreach (ConditionScenarioAssignment assignment in plan.scenarios)
                    ValidateScenarioForCondition(assignment, plan.condition, errors);
            }

            foreach (ExperimentCondition required in new[]
            {
                ExperimentCondition.Baseline,
                ExperimentCondition.ResearcherHaptic,
                ExperimentCondition.ParticipantHaptic
            })
                if (!conditions.Contains(required))
                    errors.Add($"Experiment protocol is missing the {ExperimentProtocol.DisplayName(required)} plan.");

            if (!protocol.TryValidateSharedStimuli(out string consistencyReason))
                errors.Add(consistencyReason);
        }

        if (catalog != null && (catalog.protocols == null || !catalog.protocols.Contains(protocol)))
            errors.Add("Android experiment protocol is not registered in ProfileCatalog.");
    }

    private static void ValidateScenarioForCondition(ConditionScenarioAssignment assignment,
        ExperimentCondition condition, List<string> errors)
    {
        ScenarioProfile scenario = assignment != null ? assignment.profile : null;
        if (scenario == null) return;
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

        if (condition == ExperimentCondition.Baseline) return;
        if (condition == ExperimentCondition.ParticipantHaptic)
        {
            foreach (HapticProfile option in new[]
            {
                assignment.profileA, assignment.profileB, assignment.profileC
            })
                if (option != null && !option.IsPlayable(out string optionReason))
                    errors.Add($"{scenario.name}: User Haptics option is not playable: {optionReason}");
            return;
        }
        HapticProfile haptic = assignment.hapticProfile;
        if (haptic == null) return;
        if (condition == ExperimentCondition.ResearcherHaptic &&
            haptic.Origin != HapticProfileOrigin.ResearcherCalibrated)
            errors.Add($"{scenario.name}: researcher profile is not manually calibrated/locked.");
        if (!haptic.IsPlayable(out string hapticReason))
            errors.Add($"{scenario.name}: {ExperimentProtocol.DisplayName(condition)} profile is not playable: {hapticReason}");
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
            if (session != null && session.playOnStart)
                errors.Add("ExperimentScene: session must wait for condition selection instead of playing on start.");
            if (session != null && session.GetComponent<ExperimentConditionSelectionUI>() == null)
                errors.Add("ExperimentScene: condition selection UI is missing.");
            if (session != null && session.GetComponent<ExperimentControllerInput>() == null)
                errors.Add("ExperimentScene: Bluetooth/controller input is missing.");
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
            scene =>
            {
                ValidatePlayer("PCScenarioTestScene",
                    FindInScene<ScenarioPlaybackController>(scene), errors);
                if (FindInScene<PCScenarioCameraController>(scene) == null)
                    errors.Add("PCScenarioTestScene: PCScenarioCameraController is missing.");
                if (FindInScene<PCScenarioPreviewController>(scene) == null)
                    errors.Add("PCScenarioTestScene: PCScenarioPreviewController is missing.");
            });
    }

    private static void ValidatePlayer(string sceneName, ScenarioPlaybackController player,
        List<string> errors)
    {
        if (player == null)
        {
            errors.Add($"{sceneName}: ScenarioPlaybackController is missing.");
            return;
        }

        // Runtime resolves a co-located driver in Awake, so an explicit serialized
        // reference is optional as long as the scene contains the component.
        ScenarioAnimationDriver driver = player.animationDriver != null
            ? player.animationDriver
            : player.GetComponent<ScenarioAnimationDriver>();
        if (driver == null)
            errors.Add($"{sceneName}: animation driver component is missing.");
        if (player.GetComponent<AudioSource>() == null)
            errors.Add($"{sceneName}: AudioSource is missing from the scenario player.");
        if (player.GetComponent<LongHapticPlayer>() == null)
            errors.Add($"{sceneName}: LongHapticPlayer is missing from the scenario player.");
    }

    private static void ValidateAnimationRuntime(ProfileCatalog catalog, ExperimentProtocol protocol,
        List<string> errors)
    {
        if (catalog == null || catalog.scenarios == null) return;

        var scenarios = new List<(string name, ScenarioId scenario,
            List<(ScenarioAnimationAction action, float source, float normalized, int iteration)> samples)>();

        try
        {
            var profiles = new List<ScenarioProfile>();
            var seen = new HashSet<ScenarioProfile>();
            foreach (ScenarioProfile profile in catalog.scenarios)
                if (profile != null && seen.Add(profile)) profiles.Add(profile);
            if (protocol?.conditionPlans != null)
                foreach (ExperimentConditionPlan plan in protocol.conditionPlans)
                    if (plan?.scenarios != null)
                        foreach (ConditionScenarioAssignment assignment in plan.scenarios)
                            if (assignment?.profile != null && seen.Add(assignment.profile))
                                profiles.Add(assignment.profile);

            foreach (ScenarioProfile profile in profiles)
            {
                if (profile == null) continue;
                var samples = new List<(ScenarioAnimationAction, float, float, int)>();
                foreach (ScenarioAnimationBlock block in profile.animationBlocks)
                {
                    float sampleTime = block.startSeconds + block.durationSeconds * 0.5f;
                    if (!ScenarioAnimationTimeline.TrySample(profile, sampleTime,
                        out ScenarioBlockSample sample))
                    {
                        errors.Add($"{profile.name}/{block.blockId}: animation sampling failed.");
                        continue;
                    }
                    samples.Add((sample.action, sample.sourceSeconds,
                        sample.normalizedTime, sample.block.iteration));
                }
                scenarios.Add((profile.name, profile.scenario, samples));
            }
        }
        catch (System.Exception exception)
        {
            errors.Add("Animation sampling failed: " + exception.Message);
            return;
        }

        GameObject viewer = new GameObject("Animation Validation Viewer");
        viewer.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, 0f), Quaternion.identity);
        MirrorRig activeRig = null;
        try
        {
            foreach (var scenario in scenarios)
            {
                activeRig = MirrorRig.Create(viewer.transform);
                if (activeRig == null)
                {
                    errors.Add($"{scenario.name}: MirrorRig creation failed.");
                    continue;
                }
                activeRig.UseExternalAnimationTimeline(true);

                if (scenario.scenario == ScenarioId.Apple)
                    activeRig.ShowScenario(MirrorRig.Scenario.Food,
                        MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
                else if (scenario.scenario == ScenarioId.DentalDrill)
                    activeRig.ShowScenario(MirrorRig.Scenario.Dental,
                        MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
                else
                    activeRig.ShowScenario(MirrorRig.Scenario.Dental,
                        MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Brush);

                foreach (var sample in scenario.samples)
                    activeRig.EvaluateAnimationBlock(sample.action, sample.source,
                        sample.normalized, sample.iteration);

                activeRig.Dispose();
                activeRig = null;
            }
        }
        catch (System.Exception exception)
        {
            errors.Add("Animation runtime check failed: " + exception.Message);
        }
        finally
        {
            if (activeRig != null) activeRig.Dispose();
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
        BuildAndroid(new[] { scene }, output, true);
    }

    private static void BuildAndroid(string[] scenes, string output, bool initializeXrOnStartup)
    {
        var options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = output,
            target = BuildTarget.Android
        };

        XRGeneralSettings settings = null;
        bool previousInitOnStart = true;
        XRGeneralSettingsPerBuildTarget perTarget;
        if (EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.settingsKey, out perTarget) &&
            perTarget != null)
        {
            settings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            if (settings != null)
            {
                previousInitOnStart = settings.InitManagerOnStart;
                settings.InitManagerOnStart = initializeXrOnStartup;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }

        try
        {
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new BuildFailedException(report.summary.result.ToString());
        }
        finally
        {
            if (settings != null)
            {
                settings.InitManagerOnStart = previousInitOnStart;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }
        }
    }
}

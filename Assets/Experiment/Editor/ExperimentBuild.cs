using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ExperimentBuild
{
    [MenuItem("Tools/Experiment/Build/Experiment Android")] public static void BuildExperimentAndroid() { ValidateExperimentProfiles(); BuildAndroid(ExperimentProjectSetup.ExperimentScenePath, "Builds/ExperimentAndroid/Experiment.apk"); }
    [MenuItem("Tools/Experiment/Build/Mobile Test Android")] public static void BuildMobileTestAndroid() => BuildAndroid(ExperimentProjectSetup.MobileTestScenePath, "Builds/MobileTestAndroid/MobileTest.apk");
    [MenuItem("Tools/Experiment/Validate Animation Timelines")]
    public static void ValidateAnimationTimelines()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(ExperimentProjectSetup.CatalogPath);
        if (catalog == null) throw new BuildFailedException("ProfileCatalog is missing. Run setup.");

        var errors = new List<string>();
        foreach (ScenarioProfile scenario in catalog.scenarios)
        {
            if (scenario == null) { errors.Add("Null scenario."); continue; }
            if (!ScenarioAnimationTimeline.Validate(scenario, out string reason))
                errors.Add($"{scenario.name}: {reason}");
        }

        if (errors.Count > 0)
            throw new BuildFailedException("Animation timeline validation failed:\n- " + string.Join("\n- ", errors));
        Debug.Log("[ExperimentBuild] Animation timeline validation passed.");
    }

    [MenuItem("Tools/Experiment/Validate Animation Runtime")]
    public static void ValidateAnimationRuntime()
    {
        ValidateAnimationTimelines();
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(ExperimentProjectSetup.CatalogPath);
        var scenarios = new List<(string name, ScenarioId scenario,
            List<(ScenarioAnimationAction action, float source, float normalized, int iteration)> samples)>();

        // Copy values before replacing the active scene. Batch-mode asset unloading may
        // invalidate ScriptableObject references while the procedurally built rig is sampled.
        foreach (ScenarioProfile profile in catalog.scenarios)
        {
            var samples = new List<(ScenarioAnimationAction, float, float, int)>();
            foreach (ScenarioAnimationBlock block in profile.animationBlocks)
            {
                float sampleTime = block.startSeconds + block.durationSeconds * .5f;
                if (!ScenarioAnimationTimeline.TrySample(profile, sampleTime, out ScenarioBlockSample sample))
                    throw new BuildFailedException($"{profile.name}/{block.blockId}: sampling failed.");
                samples.Add((sample.action, sample.sourceSeconds,
                    sample.normalizedTime, sample.block.iteration));
            }
            scenarios.Add((profile.name, profile.scenario, samples));
        }

        var viewer = new GameObject("Animation Validation Viewer");
        viewer.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, 0f), Quaternion.identity);
        MirrorRig activeRig = null;

        try
        {
            foreach (var scenario in scenarios)
            {
                activeRig = MirrorRig.Create(viewer.transform);
                if (activeRig == null) throw new BuildFailedException($"{scenario.name}: MirrorRig creation failed.");
                activeRig.UseExternalAnimationTimeline(true);

                if (scenario.scenario == ScenarioId.Apple)
                    activeRig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
                else if (scenario.scenario == ScenarioId.DentalDrill)
                    activeRig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
                else
                    activeRig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Brush);

                foreach (var sample in scenario.samples)
                    activeRig.EvaluateAnimationBlock(sample.action, sample.source,
                        sample.normalized, sample.iteration);

                activeRig.Dispose();
                activeRig = null;
            }
        }
        finally
        {
            if (activeRig != null) activeRig.Dispose();
            Object.DestroyImmediate(viewer);
        }

        Debug.Log("[ExperimentBuild] Animation runtime validation passed.");
    }

    [MenuItem("Tools/Experiment/Validate Final Profiles")]
    public static void ValidateExperimentProfiles()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(ExperimentProjectSetup.CatalogPath); if (catalog == null) throw new BuildFailedException("ProfileCatalog is missing. Run setup.");
        var errors = new List<string>(); foreach (ScenarioProfile s in catalog.scenarios) { if (s == null) { errors.Add("Null scenario."); continue; } if (!ScenarioAnimationTimeline.Validate(s, out string timelineReason)) errors.Add($"{s.name}: {timelineReason}"); if (s.sound == null || s.sound.clip == null) errors.Add($"{s.name}: final sound is missing."); else if (s.sound.placeholder) errors.Add($"{s.name}: sound is still a placeholder."); if (s.researcherHaptic == null) errors.Add($"{s.name}: researcher profile is missing."); else if (s.researcherHaptic.Origin != HapticProfileOrigin.ResearcherCalibrated) errors.Add($"{s.name}: researcher profile is not manually calibrated/locked."); }
        if (errors.Count > 0) throw new BuildFailedException("Experiment build is not final:\n- " + string.Join("\n- ", errors)); Debug.Log("[ExperimentBuild] Final profile validation passed.");
    }
    private static void BuildAndroid(string scene, string output) { var options = new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = output, target = BuildTarget.Android }; BuildReport report = BuildPipeline.BuildPlayer(options); if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString()); }
}

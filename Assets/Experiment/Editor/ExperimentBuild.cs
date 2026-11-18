using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ExperimentBuild
{
    [MenuItem("Tools/Experiment/Build/Experiment Android")] public static void BuildExperimentAndroid() { ValidateExperimentProfiles(); BuildAndroid(ExperimentProjectSetup.ExperimentScenePath, "Builds/ExperimentAndroid/Experiment.apk"); }
    [MenuItem("Tools/Experiment/Build/Mobile Test Android")] public static void BuildMobileTestAndroid() => BuildAndroid(ExperimentProjectSetup.MobileTestScenePath, "Builds/MobileTestAndroid/MobileTest.apk");
    [MenuItem("Tools/Experiment/Validate Final Profiles")]
    public static void ValidateExperimentProfiles()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(ExperimentProjectSetup.CatalogPath); if (catalog == null) throw new BuildFailedException("ProfileCatalog is missing. Run setup.");
        var errors = new List<string>(); foreach (ScenarioProfile s in catalog.scenarios) { if (s == null) { errors.Add("Null scenario."); continue; } if (s.sound == null || s.sound.clip == null) errors.Add($"{s.name}: final sound is missing."); else if (s.sound.placeholder) errors.Add($"{s.name}: sound is still a placeholder."); if (s.researcherHaptic == null) errors.Add($"{s.name}: researcher profile is missing."); else if (s.researcherHaptic.Origin != HapticProfileOrigin.ResearcherCalibrated) errors.Add($"{s.name}: researcher profile is not manually calibrated/locked."); }
        if (errors.Count > 0) throw new BuildFailedException("Experiment build is not final:\n- " + string.Join("\n- ", errors)); Debug.Log("[ExperimentBuild] Final profile validation passed.");
    }
    private static void BuildAndroid(string scene, string output) { var options = new BuildPlayerOptions { scenes = new[] { scene }, locationPathName = output, target = BuildTarget.Android }; BuildReport report = BuildPipeline.BuildPlayer(options); if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString()); }
}

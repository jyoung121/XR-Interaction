using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.XR.Management;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;

/// <summary>
/// 명령줄에서 부르는 빌드/점검 진입점.
///
///   -executeMethod CIBuild.RegisterCardboardLoaders   XR 로더를 Android/iOS 양쪽에 등록
///   -executeMethod CIBuild.ReportStatus               현재 설정 덤프
///   -executeMethod CIBuild.BuildIOS                   iOS Xcode 프로젝트 생성
///   -executeMethod CIBuild.BuildAndroid               APK 생성
/// </summary>
public static class CIBuild
{
    private const string CardboardLoader = "Google.XR.Cardboard.XRLoader";

    private static string[] Scenes =>
        EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();

    // ───────────────────────── XR 로더 등록 ─────────────────────────

    public static void RegisterCardboardLoaders()
    {
        AssignFor(BuildTargetGroup.Android);
        AssignFor(BuildTargetGroup.iOS);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ReportStatus();
    }

    private static void AssignFor(BuildTargetGroup group)
    {
        XRGeneralSettingsPerBuildTarget perTarget;
        if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget) || perTarget == null)
        {
            perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
            const string dir = "Assets/XR";
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            AssetDatabase.CreateAsset(perTarget, dir + "/XRGeneralSettings.asset");
            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            Debug.Log("[CIBuild] XRGeneralSettings 를 새로 만들었다");
        }

        perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
        XRGeneralSettings settings = perTarget.SettingsForBuildTarget(group);
        if (settings == null || settings.Manager == null)
        {
            Debug.LogError("[CIBuild] " + group + " 설정 객체를 만들지 못했다");
            return;
        }

        bool already = settings.Manager.activeLoaders.Any(l => l != null && l.GetType().FullName == CardboardLoader);
        if (already)
        {
            Debug.Log("[CIBuild] " + group + " : 이미 등록됨");
            return;
        }

        bool ok = XRPackageMetadataStore.AssignLoader(settings.Manager, CardboardLoader, group);
        Debug.Log("[CIBuild] " + group + " : Cardboard 로더 등록 " + (ok ? "성공" : "실패"));
        EditorUtility.SetDirty(settings.Manager);
        EditorUtility.SetDirty(settings);
        EditorUtility.SetDirty(perTarget);
    }

    // ───────────────────────── 상태 보고 ─────────────────────────

    public static void ReportStatus()
    {
        Debug.Log("[CIBuild] ===== 상태 =====");
        Debug.Log("[CIBuild] Unity " + Application.unityVersion);
        Debug.Log("[CIBuild] 씬: " + string.Join(", ", Scenes));

        XRGeneralSettingsPerBuildTarget perTarget;
        EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out perTarget);
        foreach (BuildTargetGroup g in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
        {
            string names = "(없음)";
            if (perTarget != null)
            {
                XRGeneralSettings s = perTarget.SettingsForBuildTarget(g);
                if (s != null && s.Manager != null)
                    names = s.Manager.activeLoaders.Count == 0
                        ? "(비어 있음)"
                        : string.Join(", ", s.Manager.activeLoaders.Select(l => l == null ? "null" : l.GetType().Name));
            }
            Debug.Log("[CIBuild] XR 로더 [" + g + "] = " + names);
        }

        Debug.Log("[CIBuild] iOS bundle = " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.iOS));
        Debug.Log("[CIBuild] iOS 최소버전 = " + PlayerSettings.iOS.targetOSVersionString);
        Debug.Log("[CIBuild] iOS 카메라 설명 = " + PlayerSettings.iOS.cameraUsageDescription);
        Debug.Log("[CIBuild] Android bundle = " + PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android));
        Debug.Log("[CIBuild] ================");
    }

    // ───────────────────────── 빌드 ─────────────────────────

    public static void BuildIOS()
    {
        RegisterCardboardLoaders();

        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "iOSBuild");
        if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        PlayerSettings.SetScriptingBackend(NamedBuildTarget.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.iOS.targetOSVersionString = "13.0";
        PlayerSettings.iOS.appleEnableAutomaticSigning = true;

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = outDir,
            target = BuildTarget.iOS,
            targetGroup = BuildTargetGroup.iOS,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        Summarize(report, outDir);
    }

    public static void BuildAndroid()
    {
        RegisterCardboardLoaders();

        string outDir = Path.Combine(Directory.GetCurrentDirectory(), "Builds");
        Directory.CreateDirectory(outDir);
        string apk = Path.Combine(outDir, "CardboardHapticSearch.apk");

        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        EditorUserBuildSettings.buildAppBundle = false;

        var opts = new BuildPlayerOptions
        {
            scenes = Scenes,
            locationPathName = apk,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(opts);
        Summarize(report, apk);
    }

    private static void Summarize(BuildReport report, string outPath)
    {
        BuildSummary s = report.summary;
        Debug.Log("[CIBuild] 결과 = " + s.result + " / 오류 " + s.totalErrors + " / 경고 " + s.totalWarnings);
        Debug.Log("[CIBuild] 출력 = " + outPath);
        Debug.Log("[CIBuild] 소요 = " + s.totalTime);

        if (s.result != BuildResult.Succeeded)
        {
            foreach (BuildStep step in report.steps)
                foreach (BuildStepMessage m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Exception)
                        Debug.LogError("[CIBuild] " + step.name + " :: " + m.content);
            EditorApplication.Exit(1);
        }
        EditorApplication.Exit(0);
    }
}

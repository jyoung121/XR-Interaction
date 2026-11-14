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
///
/// **iOS 는 반드시 `-buildTarget iOS` 를 함께 준다.**
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath <proj>
///             -executeMethod CIBuild.BuildIOS -logFile <log>
///
/// 안 주면 조용히 반쪽짜리가 나온다. IOSBuildPostProcess 가 `#if UNITY_IOS`
/// 로 감싸여 있는데, 이 정의는 **스크립트를 컴파일하는 시점의 활성 빌드
/// 타깃**으로 정해진다. 활성 타깃이 iOS 가 아니면 그 클래스는 통째로
/// 컴파일에서 빠지고, BuildPlayer 가 타깃을 iOS 로 바꿔도 이미 없는
/// 콜백은 불릴 수 없다. 그러면 CoreHaptics 가 안 붙은 Xcode 프로젝트가
/// 나오고 **Unity 쪽은 exit 0** 이라 멀쩡해 보인다. 맥에서 링크할 때에야
/// exit 65 로 터진다 (11/14 에 이렇게 한 바퀴 버렸다 — iOS 모듈이 없는
/// 에디터로 컴파일 확인을 돌린 것만으로 활성 타깃이 밀려났다).
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

        // 없을 때만 만든다. 조건 없이 부르면 호출마다 XRManagerSettings 객체가
        // 새로 생겨 XRGeneralSettings.asset 에 쌓인다(빌드 한 번에 한 쌍).
        // 쌓인 것들은 참조되지 않아 동작에는 영향이 없지만 파일이 계속 커진다.
        XRGeneralSettings settings = perTarget.SettingsForBuildTarget(group);
        if (settings == null || settings.Manager == null)
        {
            perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
            settings = perTarget.SettingsForBuildTarget(group);
        }

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

        // **후처리가 돌았는지 여기서 센다.** 위 주석의 사고는 Unity 가
        // 성공으로 끝나서 걸러지지 않았다. 빌드가 스스로 확인하지 않으면
        // 다음 관문은 8 분짜리 맥 러너다 — 거기까지 가서 알 일이 아니다.
        string pbx = Path.Combine(outDir, "Unity-iPhone.xcodeproj", "project.pbxproj");
        if (File.Exists(pbx) && !File.ReadAllText(pbx).Contains("CoreHaptics"))
        {
            Debug.LogError("[CIBuild] pbxproj 에 CoreHaptics 가 없다. "
                + "IOSBuildPostProcess 가 안 돌았다는 뜻이고, 이대로 맥에 "
                + "넘기면 링커가 exit 65 로 떨어진다. "
                + "-buildTarget iOS 를 주고 다시 빌드할 것.");
            EditorApplication.Exit(1);
            return;
        }

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

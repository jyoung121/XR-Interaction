#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;
using System.IO;

/// <summary>
/// iOS 빌드 후처리.
///  - CoreHaptics / AudioToolbox 프레임워크 연결 (CardboardHaptics.mm 가 필요로 함)
///  - Cardboard QR 스캔용 카메라 권한 문구 추가
/// 맥에서 Build 를 누르면 자동으로 돈다. 수동으로 Xcode 를 만질 필요 없다.
/// </summary>
public static class IOSBuildPostProcess
{
    [PostProcessBuild(100)]
    public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
    {
        if (target != BuildTarget.iOS) return;

        // 1) 프레임워크 연결
        string projPath = PBXProject.GetPBXProjectPath(pathToBuiltProject);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

        string guid = proj.GetUnityFrameworkTargetGuid();
        proj.AddFrameworkToProject(guid, "CoreHaptics.framework", true);    // weak = 구형 기기 대응
        proj.AddFrameworkToProject(guid, "AudioToolbox.framework", false);

        string mainGuid = proj.GetUnityMainTargetGuid();
        proj.AddFrameworkToProject(mainGuid, "CoreHaptics.framework", true);

        proj.WriteToFile(projPath);

        // 2) Info.plist — 카메라 권한 문구가 없으면 QR 스캔 시 앱이 죽는다
        string plistPath = Path.Combine(pathToBuiltProject, "Info.plist");
        var plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        plist.root.SetString("NSCameraUsageDescription",
            "Cardboard 뷰어의 QR 코드를 읽어 렌즈 값을 설정하기 위해 카메라를 사용합니다.");
        plist.WriteToFile(plistPath);

        Debug.Log("[iOS PostProcess] CoreHaptics 연결 + 카메라 권한 문구 추가 완료");
    }
}
#endif

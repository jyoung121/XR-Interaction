using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 거울 속 사람의 **얼굴만** 가까이서, 여러 각도로 찍는다.
///
/// 11/15 에 폰에서 얼굴이 처음으로 제대로 깎여 나왔는데, 입술 위쪽이
/// 부리처럼 앞으로 길게 튀어나와 있었다. 여태 찍어 온 그림은 전부
/// 참가자 눈(1 m 밖)에서 찍은 정면 한 장이라, 앞으로 **얼마나** 나와
/// 있는지가 보이지 않는다. 튀어나온 양은 옆에서 봐야 보인다.
///
/// 그래서 얼굴 한 점을 가운데 두고 둘레를 돌며 찍는다. 정면·45·옆·
/// 뒤쪽 45 — 어느 조각이 어디로 밀렸는지 각도 두 장이면 잡힌다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod FaceShot.Dump
/// </summary>
public static class FaceShot
{
    private const string OutDir = @"C:\UnityProjects\preview";
    private const int Width = 800;
    private const int Height = 800;

    /// <summary>얼굴 둘레를 도는 각도 (도). 0 이 정면.</summary>
    private static readonly float[] Angles = { 0f, 40f, 90f, -40f };

    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);
        Directory.CreateDirectory(OutDir);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[FaceShot] 리그 없음"); return; }

        // 아무 시나리오도 안 켠 맨 얼굴. "모든 상황에서" 나온다고 했으니
        // 여기서 이미 보여야 한다. 보이면 시나리오 코드는 범인이 아니다.
        rig.StepForPreview(0f);
        Round(rig, "idle");

        // 음식 — 접시 위(입 다묾), 베어 무는 순간, 씹는 중.
        rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Candy);
        float bite = MirrorRig.BiteMomentOf(MirrorRig.FoodKind.Candy);
        Shot(rig, "candy_plate", 0.5f);
        Shot(rig, "candy_bite", bite);
        Shot(rig, "candy_chew", bite + 0.8f);

        Debug.Log("[FaceShot] 끝 → " + OutDir);
    }

    /// <summary>
    /// 얼굴 둘레 40 cm 안에 들어온 렌더러를 **이름으로** 적는다.
    ///
    /// 그림만 보고 "저 막대가 무엇이다" 를 맞히려 들면 또 빗나간다.
    /// 11/14 에 두 번 그랬다. 물건에 제 이름을 말하게 한다.
    /// </summary>
    public static void Parts()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[FaceShot] 리그 없음"); return; }
        rig.StepForPreview(0f);

        Vector3 faceW = rig.transform.TransformPoint(rig.FaceAt(-0.025f, 0f));
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("[FaceShot.Parts] 얼굴점 ").Append(faceW.ToString("F3")).Append('\n');

        foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(false))
        {
            float d = Vector3.Distance(r.bounds.ClosestPoint(faceW), faceW);
            if (d > 0.40f) continue;
            sb.Append("  ").Append(d.ToString("F3")).Append(" m  ")
              .Append(PathOf(r.transform))
              .Append("  크기 ").Append(r.bounds.size.ToString("F3"))
              .Append("  가운데 ").Append((r.bounds.center - faceW).ToString("F3"))
              .Append('\n');
        }

        Debug.Log(sb.ToString());
    }

    private static string PathOf(Transform t)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    private static void Shot(MirrorRig rig, string tag, float seconds)
    {
        rig.StepForPreview(seconds);
        Round(rig, tag);
    }

    /// <summary>얼굴 한 점을 가운데 두고 둘레를 돌며 찍는다.</summary>
    private static void Round(MirrorRig rig, string tag)
    {
        // 눈과 입 사이. 코와 입술이 한 화면에 들어오는 자리다.
        Vector3 centerW = rig.transform.TransformPoint(rig.FaceAt(-0.025f, 0f));
        Vector3 frontW = rig.transform.TransformPoint(rig.FaceAt(-0.025f, 1f));
        Vector3 dir = (frontW - centerW).normalized;

        foreach (float a in Angles)
        {
            Vector3 d = Quaternion.AngleAxis(a, Vector3.up) * dir;
            Shoot(centerW + d * 0.26f, centerW,
                  "face_" + tag + "_" + Mathf.RoundToInt(a) + ".png");
        }
    }

    private static void Shoot(Vector3 at, Vector3 lookAt, string fileName)
    {
        GameObject camGo = new GameObject("FaceCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.10f, 0.11f, 0.13f);
        cam.fieldOfView = 42f;
        cam.nearClipPlane = 0.01f;
        cam.farClipPlane = 40f;
        camGo.transform.position = at;
        camGo.transform.LookAt(lookAt);

        RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        File.WriteAllBytes(Path.Combine(OutDir, fileName), tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Debug.Log("[FaceShot] " + fileName);
    }
}

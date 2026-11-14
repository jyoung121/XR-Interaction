using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 고개를 돌리면서 **화면 밝기를 잰다.**
///
/// 폰에서 "고개 돌리면 조명이 번쩍거린다" 고 했다. 눈으로 본 것을
/// 눈으로 쫓으면 또 빗나가므로 숫자로 잡는다. 2 도씩 돌리며 한 장씩
/// 찍어 평균 밝기를 적고, **이웃한 두 각 사이에서 몇 % 튀는지**를 본다.
/// 매끄럽게 변하면 1 % 아래고, 등이 꺼졌다 켜지면 거기서 뚝 튄다.
///
/// 이 방의 등은 전부 점광이다 = URP 의 "추가 광원". 모바일 파이프라인
/// (Settings/Mobile_RPAsset.asset)에 한 물체가 받을 수 있는 추가 광원이
/// 네 개로 묶여 있고(AdditionalLightsPerObjectLimit), 켜지는 방식도
/// 꼭짓점 단위다(AdditionalLightsRenderingMode 1 = PerVertex). 둘 다
/// 카메라가 어디를 보느냐에 따라 **고르는 등이 바뀌는** 쪽이라,
/// 의심은 여기다. 다만 의심은 의심이고, 먼저 잰다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod LightSweep.Dump
/// </summary>
public static class LightSweep
{
    private const int Width = 320;
    private const int Height = 220;

    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[LightSweep] 리그 없음"); return; }

        // **폰이 쓰는 파이프라인으로 재야 한다.**
        //
        // 에디터는 PC_RPAsset 으로 그린다. 폰은 Mobile_RPAsset 이고, 두
        // 에셋은 추가 광원을 다루는 방식이 다르다 — 번쩍임을 의심하는
        // 자리가 바로 거기라, PC 로 재면 아무것도 안 나오거나 엉뚱한
        // 숫자가 나온다.
        var keep = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        var mobile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(
                         "Assets/Settings/Mobile_RPAsset.asset");
        if (mobile == null) { Debug.LogError("[LightSweep] Mobile_RPAsset 없음"); return; }

        try
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = mobile;
            Debug.Log("[LightSweep] pipeline = " + mobile.name);

            Sweep(rig, viewerGo.transform, "FOOD mobile-as-is", MirrorRig.Scenario.Food);
            Sweep(rig, viewerGo.transform, "DENTAL mobile-as-is", MirrorRig.Scenario.Dental);

            // 같은 것을 **추가 광원을 픽셀 단위로, 여덟 개까지** 켜고
            // 다시 잰다. 지금 모바일 에셋은 꼭짓점 단위에 네 개까지다
            // (PerVertex / PerObjectLimit 4). 둘 다 카메라가 움직이면
            // 고르는 등이 바뀔 수 있는 설정이라, 바꿔서 숫자가 가라앉으면
            // 그게 범인이고 아니면 아니다.
            var alt = Object.Instantiate(mobile);
            alt.name = "Mobile_PerPixel8";
            SerializedObject so = new SerializedObject(alt);
            so.FindProperty("m_AdditionalLightsRenderingMode").intValue = 2;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 8;
            so.ApplyModifiedPropertiesWithoutUndo();

            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = alt;
            Debug.Log("[LightSweep] pipeline = " + alt.name);
            Sweep(rig, viewerGo.transform, "FOOD perpixel8", MirrorRig.Scenario.Food);
            Sweep(rig, viewerGo.transform, "DENTAL perpixel8", MirrorRig.Scenario.Dental);
        }
        finally
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = keep;
        }
    }

    private static void Sweep(MirrorRig rig, Transform viewer, string tag,
                              MirrorRig.Scenario s)
    {
        rig.ShowScenario(s);
        rig.StepForPreview(1.0f);

        StringBuilder sb = new StringBuilder();
        sb.Append("[LightSweep] ").Append(tag).Append(" — 2도씩 돌리며 평균 밝기\n");

        // 한 장 미리 찍어 버린다. 첫 렌더는 늘 어둡게 나와서 (텍스처·
        // 셰이더가 아직 안 올라왔다) 첫 칸이 80 % 튄 것처럼 보인다.
        viewer.rotation = Quaternion.identity;
        Luma(viewer);

        float[] prev = null;
        float worst = 0f;
        float worstAt = 0f;
        int worstCell = -1;
        for (float yaw = -70f; yaw <= 70.01f; yaw += 2f)
        {
            // **목 모형.** 카드보드는 고개를 돌릴 때 눈이 제자리에서
            // 돌지 않는다 — 목뼈를 중심으로 호를 그린다. 눈은 목에서
            // 위로 7.5 cm, 앞으로 8 cm 쯤이다. 리그 원점이 매 프레임
            // 카메라를 따라붙으므로(MirrorRig.FollowViewer) 방과 등이
            // 통째로 그 호를 따라 움직인다. 고개만 돌려서는 안 보이던
            // 것이 여기서 보일 수 있다.
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            viewer.SetPositionAndRotation(rot * new Vector3(0f, 0.075f, 0.080f), rot);
            rig.StepForPreview(1.0f);
            float[] lum = Luma(viewer);
            if (prev != null)
            {
                // **칸마다** 본다. 너무 어두운 칸은 건너뛴다 — 0.01 이
                // 0.02 가 되면 100 % 지만 눈에는 아무 일도 안 일어난다.
                float here = 0f; int hereCell = -1;
                for (int k = 0; k < lum.Length; k++)
                {
                    if (prev[k] < 0.05f) continue;
                    float j = Mathf.Abs(lum[k] - prev[k]) / prev[k] * 100f;
                    if (j > here) { here = j; hereCell = k; }
                }
                if (here > worst) { worst = here; worstAt = yaw; worstCell = hereCell; }
                sb.Append("  yaw ").Append(yaw.ToString("F0").PadLeft(4))
                  .Append("  cell ").Append(hereCell.ToString().PadLeft(2))
                  .Append("  jump ").Append(here.ToString("F1")).Append(" %\n");
            }
            prev = lum;
        }
        sb.Append("  WORST JUMP ").Append(worst.ToString("F1"))
          .Append(" % (yaw ").Append(worstAt.ToString("F0"))
          .Append(", cell ").Append(worstCell).Append(" of ")
          .Append(GX * GY).Append(")\n");
        viewer.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// 번쩍인다는 각도들을 **그림으로** 남긴다.
    ///
    /// 숫자로는 세 번 쟀는데 조명 설정은 범인이 아니었다 (픽셀 단위로
    /// 바꿔 나란히 재니 소수점까지 같았다). 칸별로 27 % 튀는 것도
    /// 밝은 물건이 칸 경계를 넘는 시차로 보인다. 숫자가 못 가리키면
    /// 눈으로 본다 — 집 방에서, 목 모형을 넣고, 2 도씩.
    /// </summary>
    public static void Shots()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);
        System.IO.Directory.CreateDirectory(ShotDir);

        GameObject viewerGo = new GameObject("Viewer");
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[LightSweep] 리그 없음"); return; }

        var keep = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
        var mobile = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.RenderPipelineAsset>(
                         "Assets/Settings/Mobile_RPAsset.asset");
        if (mobile == null) { Debug.LogError("[LightSweep] Mobile_RPAsset 없음"); return; }

        StringBuilder sb2 = new StringBuilder();
        sb2.Append("[LightSweep] ceiling luma by yaw\n");

        try
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = mobile;
            rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Candy);

            // 첫 렌더는 늘 어둡게 나온다 — 버린다.
            viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            rig.StepForPreview(1.0f);
            Shot(viewerGo.transform, "warmup.png");

            for (float yaw = -20f; yaw <= 20.01f; yaw += 2f)
            {
                Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
                viewerGo.transform.SetPositionAndRotation(
                    rot * new Vector3(0f, 0.075f, 0.080f), rot);
                rig.StepForPreview(1.0f);
                float ceil2 = Shot(viewerGo.transform,
                     "flick_" + (yaw < 0f ? "m" : "p")
                     + Mathf.Abs(yaw).ToString("00") + ".png");
                // 천장만 따로 잰다. 천장은 큰 평면 한 장이라, 꼭짓점
                // 단위 추가 광원이 네 모서리에서만 계산되고 그 사이를
                // 당겨 쓴다. 카메라가 움직이면 그 네 값이 한꺼번에
                // 흔들려서 **천장 전체가 같이** 밝아졌다 어두워진다.
                sb2.Append("  yaw ").Append(yaw.ToString("F0").PadLeft(4))
                   .Append("  ceiling ").Append(ceil2.ToString("F4"))
                   .Append("\n");
            }
        }
        finally
        {
            UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline = keep;
        }
        Debug.Log(sb2.ToString());
        Debug.Log("[LightSweep] shots -> " + ShotDir);
    }

    private const string ShotDir = "C:/UnityProjects/preview/flicker";

    /// <summary>한 장 찍어 저장하고, **천장 쪽(위 1/4)** 평균 밝기를 돌려준다.</summary>
    private static float Shot(Transform from, string name)
    {
        GameObject camGo = new GameObject("ShotCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        camGo.transform.SetPositionAndRotation(from.position, from.rotation);

        RenderTexture rt = new RenderTexture(560, 380, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 4;
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(560, 380, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, 560, 380), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(System.IO.Path.Combine(ShotDir, name), tex.EncodeToPNG());

        Color[] px = tex.GetPixels();
        float sum = 0f; int n2 = 0;
        for (int y = 380 * 3 / 4; y < 380; y++)       // ReadPixels 는 아래가 0 이다
            for (int x = 0; x < 560; x++)
            {
                Color c = px[y * 560 + x];
                sum += c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                n2++;
            }

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        return n2 > 0 ? sum / n2 : 0f;
    }

    private const int GX = 8;   // 화면을 가로 8 × 세로 6 칸으로 나눈다
    private const int GY = 6;

    /// <summary>
    /// 한 장 찍어 **칸마다** 평균 밝기를 돌려준다.
    ///
    /// 처음엔 화면 전체 평균 하나만 쟀다. 그러면 벽 한 장이 번쩍여도
    /// 나머지 마흔일곱 칸에 묻혀 2 % 로 보인다 — 실제로 그렇게 나와서
    /// "번쩍임이 없다" 고 읽을 뻔했다. 번쩍이는 건 대개 한 면이다.
    /// </summary>
    private static float[] Luma(Transform from)
    {
        GameObject camGo = new GameObject("SweepCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        camGo.transform.SetPositionAndRotation(from.position, from.rotation);

        RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        cam.targetTexture = rt;
        cam.Render();

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;

        Color[] px = tex.GetPixels();
        float[] cell = new float[GX * GY];
        int[] cnt = new int[GX * GY];
        for (int y = 0; y < Height; y++)
            for (int x = 0; x < Width; x++)
            {
                Color c = px[y * Width + x];
                int k = Mathf.Min(GY - 1, y * GY / Height) * GX
                      + Mathf.Min(GX - 1, x * GX / Width);
                cell[k] += c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
                cnt[k]++;
            }
        for (int k = 0; k < cell.Length; k++) if (cnt[k] > 0) cell[k] /= cnt[k];

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);

        return cell;
    }
}

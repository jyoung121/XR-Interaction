using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// **폰에서 실제로 보이는 그림**을 찍는다.
///
/// 11/14 에 앱을 폰에 올렸더니 시뻘건 덩어리가 시야를 덮었다. 그런데
/// 여태 찍어 온 미리보기에는 그런 게 한 번도 없었다. 이유는 카메라
/// 자리였다 — ToolPreview 의 음식·도구 컷은 전부 `rig.EyeCam`,
/// 즉 **거울 속 사람 얼굴 앞 20 cm** 에서 찍는다. 참가자 자신의 눈에서
/// 찍는 컷은 `mirror_idle.png` 하나뿐이고 그건 시나리오를 켜기 전이다.
/// 그래서 시나리오를 켠 뒤의 참가자 시야는 **아무도 본 적이 없었다.**
///
/// 여기서는 ExperimentController 가 폰에서 하는 것과 똑같이 한다:
/// 원점·정면에 선 참가자, Create 로 리그를 세우고, 일곱 블록을 차례로
/// 켜서 **참가자 눈에서** 찍는다. 카메라를 덮고 있는 물건이 있으면
/// 이름과 거리까지 적는다.
///
///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod DeviceView.Dump
/// </summary>
public static class DeviceView
{
    private const string OutDir = @"C:\UnityProjects\preview";
    private const int Width = 900;
    private const int Height = 600;

    private static readonly MirrorRig.Scenario[] Order =
    {
        MirrorRig.Scenario.Food, MirrorRig.Scenario.Food, MirrorRig.Scenario.Food,
        MirrorRig.Scenario.Food, MirrorRig.Scenario.Food,
        MirrorRig.Scenario.Dental, MirrorRig.Scenario.Dental
    };
    private static readonly MirrorRig.FoodKind[] Foods =
    {
        MirrorRig.FoodKind.Candy, MirrorRig.FoodKind.Apple, MirrorRig.FoodKind.Meat,
        MirrorRig.FoodKind.Jelly, MirrorRig.FoodKind.Pudding,
        MirrorRig.FoodKind.Apple, MirrorRig.FoodKind.Apple
    };
    private static readonly MirrorRig.DentalKind[] Tools =
    {
        MirrorRig.DentalKind.Drill, MirrorRig.DentalKind.Drill, MirrorRig.DentalKind.Drill,
        MirrorRig.DentalKind.Drill, MirrorRig.DentalKind.Drill,
        MirrorRig.DentalKind.Drill, MirrorRig.DentalKind.Brush
    };

    /// <summary>참가자가 고개를 둘 만한 방향 (pitch, yaw).</summary>
    private static readonly System.Collections.Generic.KeyValuePair<string, Vector2>[] Looks =
    {
        new System.Collections.Generic.KeyValuePair<string, Vector2>("front", new Vector2(  0f,   0f)),
        new System.Collections.Generic.KeyValuePair<string, Vector2>("up",    new Vector2(-30f,   0f)),
        new System.Collections.Generic.KeyValuePair<string, Vector2>("down",  new Vector2( 30f,   0f)),
        new System.Collections.Generic.KeyValuePair<string, Vector2>("left",  new Vector2(  0f, -55f)),
        new System.Collections.Generic.KeyValuePair<string, Vector2>("right", new Vector2(  0f,  55f)),
        new System.Collections.Generic.KeyValuePair<string, Vector2>("back",  new Vector2(  0f, 180f))
    };

    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);
        Directory.CreateDirectory(OutDir);

        // 폰과 같은 출발점: 원점, 정면. ExperimentController 는 XR 을 켠
        // 바로 그 프레임에 Create 를 부르므로 카메라가 아직 씬에 적힌
        // 자리(0,0,0 / 회전 없음) 그대로다.
        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[DeviceView] 리그 없음"); return; }

        for (int i = 0; i < Order.Length; i++)
        {
            rig.ShowScenario(Order[i], Foods[i], Tools[i]);
            string tag = i + "_" + (Order[i] == MirrorRig.Scenario.Dental
                                    ? Tools[i].ToString() : Foods[i].ToString());

            // 고개를 돌린 쪽도 찍는다. 폰에서는 카메라가 가만히 있지
            // 않는데, 여태 미리보기는 늘 정면 한 장이었다. 시야를 덮는
            // 물건은 대개 정면이 아니라 **조금 돌린 쪽**에 있다.
            foreach (float t in new[] { 0f, 3.0f })
            {
                rig.StepForPreview(t);
                foreach (var a in Looks)
                {
                    viewerGo.transform.rotation = Quaternion.Euler(a.Value.x, a.Value.y, 0f);
                    Shoot(viewerGo.transform,
                          "dev_" + tag + "_t" + t.ToString("0.0") + "_" + a.Key + ".png");
                }
                viewerGo.transform.rotation = Quaternion.identity;
            }

            rig.StepForPreview(0f);
            Blockers(rig, viewerGo.transform.position, tag);
        }

        Debug.Log("[DeviceView] 끝");
    }

    /// <summary>
    /// 거울 속 입 **안쪽**에서 찍는다. 폰 사진과 같은 그림이 나오면
    /// 카메라가 거기 들어가 있다는 뜻이고, 그 자리가 리그 원점에서
    /// 얼마나 떨어져 있는지가 곧 "카메라가 얼마나 밀렸나" 다.
    /// </summary>
    public static void Mouth()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);
        Directory.CreateDirectory(OutDir);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[DeviceView] 리그 없음"); return; }

        rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Drill);
        rig.StepForPreview(2.0f);

        Transform jaw = null, head = null;
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == "JawUp") jaw = t;
            if (t.name == "HeadPivot") head = t;
        }
        if (jaw == null) { Debug.LogError("[DeviceView] JawUp 없음"); return; }

        Renderer jr = jaw.GetComponent<Renderer>();
        Vector3 c = jr != null ? jr.bounds.center : jaw.position;
        Vector3 cl = rig.transform.InverseTransformPoint(c);
        Debug.Log("[DeviceView] 입 중심 리그좌표=" + cl.ToString("0.000")
                  + "  원점에서 " + cl.magnitude.ToString("0.000") + " m"
                  + "  HeadPivot=" + (head != null
                      ? rig.transform.InverseTransformPoint(head.position).ToString("0.000") : "?")
                  + "  거울면 z=" + (2f * 0.45f).ToString("0.00"));

        // 입 안에서 바깥(참가자 쪽, 리그 -Z)을 본다.
        foreach (var a in Looks)
        {
            Quaternion r = rig.transform.rotation * Quaternion.Euler(a.Value.x, a.Value.y + 180f, 0f);
            GameObject g = new GameObject("MouthCam");
            g.transform.SetPositionAndRotation(c, r);
            Shoot(g.transform, "mouth_" + a.Key + ".png");
            Object.DestroyImmediate(g);
        }

        // 원점에서 입 쪽으로 가는 길에 몇 걸음 찍는다. 어느 지점부터
        // 폰 사진처럼 되는지가 곧 **밀린 거리**다.
        foreach (float k in new[] { 0.25f, 0.5f, 0.75f })
        {
            Vector3 pos = rig.transform.TransformPoint(cl * k);
            GameObject g = new GameObject("StepCam");
            g.transform.SetPositionAndRotation(pos, rig.transform.rotation);
            Shoot(g.transform, "step_" + k.ToString("0.00") + ".png");
            Object.DestroyImmediate(g);
        }

        Debug.Log("[DeviceView] 입 끝");
    }

    /// <summary>
    /// 리그를 세운 **뒤에 카메라가 딴 데로 간** 경우를 흉내 낸다.
    ///
    /// ExperimentController 는 XR 을 켠 바로 그 프레임에 MirrorRig.Create 를
    /// 부른다. 그 순간 카메라는 아직 씬에 적힌 자리(0,0,0)라, 리그가 거기에
    /// 박힌다. 그런데 다음 프레임에 Cardboard 가 카메라에 자세를 넣으면
    /// 카메라만 움직이고 리그는 그대로다. 그만큼 참가자가 방 안에서
    /// 엉뚱한 데 서 있게 된다.
    ///
    /// 전에는 이 문제가 안 보였다. 패턴 설정 화면을 몇 초 거치고 나서야
    /// 거울 방에 들어갔으니, 그때쯤 카메라 자세가 이미 자리를 잡았기
    /// 때문이다. 화면을 끄면서 **그 유예 시간까지 같이 끈 것**이다.
    /// </summary>
    public static void Offsets()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);
        Directory.CreateDirectory(OutDir);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[DeviceView] 리그 없음"); return; }

        rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Candy);

        foreach (float h in new[] { 0.4f, 0.8f, 1.2f, 1.6f })
        {
            // 카메라만 위로 간다. 리그는 원점에 박혀 있다.
            viewerGo.transform.position = new Vector3(0f, h, 0f);
            rig.StepForPreview(1.0f);
            foreach (var a in Looks)
            {
                viewerGo.transform.rotation = Quaternion.Euler(a.Value.x, a.Value.y, 0f);
                Shoot(viewerGo.transform, "off_" + h.ToString("0.0") + "_" + a.Key + ".png");
            }
            viewerGo.transform.rotation = Quaternion.identity;
        }

        Debug.Log("[DeviceView] 오프셋 끝");
    }

    /// <summary>카메라를 덮고 있거나 코앞에 있는 물건을 센다.</summary>
    private static void Blockers(MirrorRig rig, Vector3 eye, string tag)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("[DeviceView] ").Append(tag).Append(" — 눈앞 물건");

        int n = 0;
        foreach (Renderer r in rig.GetComponentsInChildren<Renderer>(false))
        {
            Bounds b = r.bounds;
            float d = Vector3.Distance(b.ClosestPoint(eye), eye);
            if (d > 0.30f) continue;
            n++;
            sb.Append("\n  ").Append(b.Contains(eye) ? "안에 있음 " : "d=" + d.ToString("0.000") + " ")
              .Append(Path(r.transform, rig.transform))
              .Append("  size=").Append(b.size.ToString("0.00"))
              .Append("  center=").Append(rig.transform.InverseTransformPoint(b.center).ToString("0.00"));
        }
        if (n == 0) sb.Append("\n  없음");
        Debug.Log(sb.ToString());
    }

    private static string Path(Transform t, Transform stop)
    {
        string s = t.name;
        for (Transform p = t.parent; p != null && p != stop; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    private static void Shoot(Transform from, string fileName)
    {
        GameObject camGo = new GameObject("DeviceCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 75f;          // Cardboard 한쪽 눈과 비슷하게
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        camGo.transform.SetPositionAndRotation(from.position, from.rotation);

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

        File.WriteAllBytes(System.IO.Path.Combine(OutDir, fileName), tex.EncodeToPNG());

        cam.targetTexture = null;
        Object.DestroyImmediate(camGo);
        rt.Release();
        Object.DestroyImmediate(rt);
        Object.DestroyImmediate(tex);
        Debug.Log("[DeviceView] " + fileName);
    }
}

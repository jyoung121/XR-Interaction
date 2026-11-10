using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// ToolModels 가 만든 도형을 PNG 로 찍어 본다. 빌드해서 폰에 올리지 않고도
/// 비율과 실루엣을 확인하려고 만든 프리뷰 전용 도구다. 실험과는 무관하다.
///
/// 배치모드 실행:
///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod ToolPreview.Render
/// -nographics 를 붙이면 안 된다. 렌더가 필요하다.
/// </summary>
public static class ToolPreview
{
    private const string OutDir = @"C:\UnityProjects\preview";
    private const int Width = 1000;
    private const int Height = 700;

    /// <summary>사람 머리 높이. Head.obj 의 원래 단위를 여기에 맞춘다.</summary>
    private const float HeadHeight = 0.22f;

    public static void Render()
    {
        Directory.CreateDirectory(OutDir);

        // 열려 있는 씬의 오브젝트가 끼어들지 않게 빈 씬에서 찍는다.
        // 저장은 하지 않는다 — SampleScene 을 건드리면 안 된다.
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SetupEnvironment();

        GameObject jelly = ToolModels.BuildJelly();
        Shoot(jelly, "jelly.png", new Vector3(0.75f, 0.45f, 0.95f));
        Object.DestroyImmediate(jelly);

        GameObject piece = ToolModels.BuildHandpiece();
        Shoot(piece, "handpiece.png", new Vector3(0.75f, 0.40f, 0.95f));
        Object.DestroyImmediate(piece);

        GameObject brush = ToolModels.BuildToothbrush();
        Shoot(brush, "toothbrush.png", new Vector3(0.75f, 0.40f, 0.95f));
        Object.DestroyImmediate(brush);

        MirrorShots();

        Debug.Log("[ToolPreview] 완료 → " + OutDir);
    }

    private static void SetupEnvironment()
    {
        GameObject sun = new GameObject("Sun");
        Light l = sun.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = 1.1f;
        l.color = new Color(1f, 0.98f, 0.94f);
        sun.transform.rotation = Quaternion.Euler(38f, -25f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.42f, 0.44f, 0.48f);

        // 금속은 주변 반사가 없으면 새까맣게 나온다. 기본 스카이박스를 붙여 준다.
        Material sky = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Skybox.mat");
        if (sky != null)
        {
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
        }
    }

    private static Bounds BoundsOf(GameObject root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(root.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in rs)
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>dir 방향에서 대상을 꽉 차게 잡아 PNG 로 저장한다.</summary>
    private static void Shoot(GameObject target, string fileName, Vector3 dir)
    {
        Bounds b = BoundsOf(target);

        GameObject camGo = new GameObject("PreviewCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.17f, 0.18f, 0.21f);
        cam.fieldOfView = 30f;
        // 기본 근평면은 0.3m 다. 대상이 15cm 라 그대로 두면 통째로 잘린다.
        cam.nearClipPlane = 0.001f;
        cam.farClipPlane = 50f;

        float dist = b.size.magnitude /
                     (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.25f;
        camGo.transform.position = b.center + dir.normalized * dist;
        camGo.transform.LookAt(b.center);

        RenderTexture rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        rt.antiAliasing = 8;
        cam.targetTexture = rt;
        // **두 번 그린다.** 재질을 막 만든 직후의 첫 렌더는 조각이 전부 첫
        // 재질 색으로 나온다 — 젤리 곰의 그라데이션이 통째로 한 색이 돼서
        // 한참 헤맸다. 같은 재질이 장면 안(MirrorShots)에서는 멀쩡히 나오는데,
        // 거기는 만든 뒤로 이미 몇 번 그려진 뒤라 그렇다. 버리는 한 장이
        // 그 차이를 메운다.
        cam.Render();
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

        Debug.Log(string.Format("[ToolPreview] {0}  크기 {1:0.000} x {2:0.000} x {3:0.000} m",
                                fileName, b.size.x, b.size.y, b.size.z));
    }

    /// <summary>지정한 위치·방향에서 그대로 찍는다. 참가자 눈에서 본 화면을 보려고 쓴다.</summary>
    private static void ShootFrom(Vector3 pos, Quaternion rot, string fileName, float fov)
    {
        GameObject camGo = new GameObject("PreviewCam");
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = fov;
        cam.nearClipPlane = 0.02f;
        cam.farClipPlane = 60f;
        camGo.transform.SetPositionAndRotation(pos, rot);

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

        Debug.Log("[ToolPreview] " + fileName);
    }

    /// <summary>리그 좌표로 주어진 자리에서 대상을 본다. 등장인물 자세 확인용이다.</summary>
    private static void CastShot(MirrorRig rig, Vector3 from, Vector3 at, string fileName)
    {
        // 리그 좌표로 받는다. 치과에서는 리그가 통째로 35° 젌혀 있어서
        // 월드 좌표로 주면 카메라가 기구 쌉반 속에 묻힌다.
        Transform t = rig.transform;
        Vector3 p = t.TransformPoint(from);
        ShootFrom(p, Quaternion.LookRotation(t.TransformPoint(at) - p, t.up), fileName, 50f);
    }

    /// <summary>거울 방을 세우고 참가자 눈높이에서 시나리오별로 찍는다.</summary>
    private static void MirrorShots()
    {
        // 리그가 자기 조명을 들고 있다. 프리뷰용 해를 끄고 찍어야 앱에서 보일 모습과 같다.
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 0f), Quaternion.identity);

        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { if (sun != null) sun.SetActive(true); return; }

        Vector3 eye = viewerGo.transform.position;
        Quaternion look = viewerGo.transform.rotation;

        rig.StepForPreview(0f);
        ShootFrom(eye, look, "mirror_idle.png", 75f);

        // 무른 음식. 단단한 쪽과 나란히 놓고 봐야 두 조건이 눈으로 구분되는지 알 수 있다.
        rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Soft);
        viewerGo.transform.rotation = look;
        rig.StepForPreview(1.6f);                 // 젤리를 집어 올리는 중
        ShootFrom(eye, look, "mirror_food_soft.png", 75f);

        // 음식이 아직 접시에 놓여 있는 순간. 책상에서 집어 먹는 그림이
        // 성립하려면 이 구간이 제대로 보여야 한다.
        rig.StepForPreview(0.4f);
        ShootFrom(eye, look, "mirror_food_plate.png", 75f);

        // 치과 거울은 머리 위에 있다. 참가자가 올려다보는 각도로 맞춰 찍어야
        // 실제로 보일 그림이 나온다.
        Quaternion upward = Quaternion.Euler(-35f, 0f, 0f);
        rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Drill);
        viewerGo.transform.rotation = upward;
        rig.StepForPreview(1.8f);                 // 드릴이 입에 닿은 직후
        ShootFrom(eye, upward, "mirror_dental.png", 75f);
        CastShot(rig, new Vector3(0.62f, 0.46f, 0.52f), new Vector3(-0.30f, 0.12f, 0.88f),
                 "cast_dentist.png");
        // 입만 바싹 당겨서. 거울 너머가 아니라 거울 속 머리 바로 앞에서 본다 —
        // 이가 낱개로 읽히는지는 이렇게 봐야 판단이 된다.
        CastShot(rig, new Vector3(0f, 0.02f, 0.58f), new Vector3(0f, -0.07f, 0.90f),
                 "mouth_closeup.png");

        // 전동칫솔 쪽. 드릴과 나란히 놓고 봐야 두 수준이 구분되는지 알 수 있다.
        rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Brush);
        viewerGo.transform.rotation = upward;
        rig.StepForPreview(2.4f);                 // 문지르는 중
        ShootFrom(eye, upward, "mirror_dental_brush.png", 75f);

        rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Drill);
        viewerGo.transform.rotation = upward;
        rig.StepForPreview(1.8f);

        // 치과에서 턱을 당겼을 때. 이 방은 35도 젖혀져 있어서 조금만 내려다봐도
        // 시선이 바닥·끝벽으로 떨어지는데, 전에는 거기가 민무늬 회색 면이었다.
        Quaternion chinDown = Quaternion.Euler(15f, 0f, 0f);
        viewerGo.transform.rotation = chinDown;
        rig.StepForPreview(2.0f);
        ShootFrom(eye, chinDown, "mirror_dental_down.png", 75f);

        rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Hard);
        viewerGo.transform.rotation = look;
        // 한 입 주기는 5.9 초고, 깨무는 순간은 주기 시작에서 3.1 초다.
        // StepForPreview 는 시각을 **박아 넣는** 것이라 세 입째 모양을
        // 보려면 깨무는 순간을 하나하나 밟아 줄여야 한다 — 한 번에
        // 건너뛰면 한 입만 깨문 것으로 나온다.
        const float BiteCycle = 5.9f;
        const float BiteAt = 3.1f + 0.5f;
        rig.StepForPreview(BiteAt);
        ShootFrom(eye, look, "mirror_food_hard.png", 75f);
        rig.StepForPreview(BiteCycle + BiteAt);
        rig.StepForPreview(BiteCycle * 2f + BiteAt);
        ShootFrom(eye, look, "mirror_food_bitten.png", 75f);
        rig.StepForPreview(BiteCycle * 2f + BiteAt + 1.2f);
        ShootFrom(eye, look, "mirror_food_chew.png", 75f);

        // 고개를 숙이면 거울에 제 몸이 보이는지. 집 거울이 전신인 이유가 이거다.
        Quaternion downward = Quaternion.Euler(27f, 0f, 0f);
        viewerGo.transform.rotation = downward;
        rig.StepForPreview(2.6f);                 // 사과를 내린 뒤
        ShootFrom(eye, downward, "mirror_lookdown.png", 75f);

        // 머리를 돌렸을 때 거울 속 머리가 따라오는지
        viewerGo.transform.rotation = Quaternion.Euler(8f, 22f, 0f);
        rig.ShowScenario(MirrorRig.Scenario.Dental);
        rig.StepForPreview(3.0f);
        ShootFrom(eye, viewerGo.transform.rotation, "mirror_turned.png", 75f);

        rig.Dispose();
        Object.DestroyImmediate(viewerGo);
        if (sun != null) sun.SetActive(true);
    }
}

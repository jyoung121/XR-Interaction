using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 다섯 음식을 접시와 손 두 각도에서 찍는다.
///
/// 크기·눕힘·모델 교체는 숫자로는 맞는지 알 수 없다. 접시에 반쯤
/// 잠긴 젤리나, 받침을 자르다 바닥이 뚫린 푸딩은 **봐야** 안다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod FoodShot.Dump
/// </summary>
public static class FoodShot
{
    private const string Out = "C:/UnityProjects/preview/food";

    public static void Dump()
    {
        System.IO.Directory.CreateDirectory(Out);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[FoodShot] 리그 없음"); return; }

        GameObject camGo = new GameObject("Shot");
        Camera cam = camGo.AddComponent<Camera>();
        cam.fieldOfView = 46f;
        cam.nearClipPlane = 0.02f;

        RenderTexture rt = new RenderTexture(900, 700, 24, RenderTextureFormat.ARGB32);
        Texture2D tex = new Texture2D(900, 700, TextureFormat.RGB24, false);
        cam.targetTexture = rt;

        StringBuilder sb = new StringBuilder("[FoodShot] 접시 알 치수\n");

        foreach (MirrorRig.FoodKind k in System.Enum.GetValues(typeof(MirrorRig.FoodKind)))
        {
            rig.ShowScenario(MirrorRig.Scenario.Food, k);

            // 접시를 내려다본다. 자리는 **찾아서** 쓴다 — 좌표를 손으로
            // 적었더니 사람 등짝이 찍혔다.
            rig.StepForPreview(0.2f);
            Transform plate = Find(rig, "M_Plate");
            if (plate == null) plate = Find(rig, "R_Plate");
            if (plate != null) AimAt(cam, plate.position, 0.46f, 38f);
            Shot(cam, rt, tex, Out + "/" + k + "_plate.png");

            // 손에 든 순간 — 입 앞으로 올라온 음식을 본다.
            rig.StepForPreview(MirrorRig.BiteMomentOf(k));
            Transform held = Held(rig);
            if (held != null) AimAt(cam, held.position, 0.30f, 6f);
            Shot(cam, rt, tex, Out + "/" + k + "_hand.png");

            sb.Append("  ").Append(k).Append(": ").Append(Sizes(rig, k)).Append('\n');
        }

        Debug.Log(sb.ToString());
        Debug.Log("[FoodShot] -> " + Out);
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
    }

    /// <summary>월드 점 at 을 pitch 만큼 내려다보며 dist 떨어져 본다.</summary>
    private static void AimAt(Camera cam, Vector3 at, float dist, float pitch)
    {
        Vector3 dir = at;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        Quaternion r = Quaternion.LookRotation(dir.normalized, Vector3.up)
                     * Quaternion.Euler(pitch, 0f, 0f);
        cam.transform.SetPositionAndRotation(at - (r * Vector3.forward) * dist, r);
    }

    private static Transform Find(MirrorRig rig, string name)
    {
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            if (t.gameObject.activeInHierarchy && t.name == name) return t;
        return null;
    }

    /// <summary>손에 든 음식 — 거울 쪽(z 가 가장 먼 쪽) 것을 고른다.</summary>
    private static Transform Held(MirrorRig rig)
    {
        Transform best = null;
        float hi = float.NegativeInfinity;
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
        {
            if (!t.gameObject.activeInHierarchy) continue;
            if (t.name.IndexOf("Food", System.StringComparison.Ordinal) < 0) continue;
            if (t.GetComponentsInChildren<Renderer>(true).Length == 0) continue;
            if (t.position.z > hi) { hi = t.position.z; best = t; }
        }
        return best;
    }

    private static void Shot(Camera cam, RenderTexture rt, Texture2D tex, string path)
    {
        cam.Render();
        RenderTexture keep = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = keep;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
    }

    /// <summary>접시에 놓인 알과 손에 든 알의 실제 크기를 적는다.</summary>
    private static string Sizes(MirrorRig rig, MirrorRig.FoodKind k)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
        {
            if (!t.gameObject.activeInHierarchy) continue;
            if (!t.name.StartsWith("R_")) continue;
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            string n = t.name.Substring(2);
            if (n.Contains("Shadow")) continue;
            // 접시 알 이름은 "<모델이름><번호>" 다 (BuildHome 의 접시 고리).
            string mdl = Model(k);
            if (!n.StartsWith(mdl, System.StringComparison.Ordinal)) continue;
            if (n.Length <= mdl.Length || !char.IsDigit(n[n.Length - 1])) continue;
            Bounds b = rs[0].bounds;
            for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
            sb.Append(n).Append(' ').Append(b.size.ToString("F3"))
              .Append(" 바닥y ").Append(b.min.y.ToString("F4")).Append("  ");
            break;   // 한 알만 적는다
        }
        if (sb.Length == 0) sb.Append("(접시 알 없음)");
        return sb.ToString();
    }

    private static string Model(MirrorRig.FoodKind k)
    {
        switch (k)
        {
            case MirrorRig.FoodKind.Apple:   return "Apple";
            case MirrorRig.FoodKind.Jelly:   return "GummyBear";
            case MirrorRig.FoodKind.Meat:    return "Steak";
            case MirrorRig.FoodKind.Pudding: return "FlanOnly";
            default:                         return "Candy";
        }
    }
}

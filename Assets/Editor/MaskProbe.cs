using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 머리 메시의 서브메시마다 재질 색·삼각형 수·차지하는 자리를 적는다.
/// 마스크를 뜰 면을 "가장 밝은 재질" 하나로 골랐더니 코와 입이 빠졌고,
/// 다섯 개를 다 쓰니 입 속까지 딸려 나왔다. 어느 게 뭔지 보고 고른다.
///
/// 길이는 머리뼈 기준, headUnit(머리뼈~정수리)으로 나눠 적는다.
/// </summary>
public static class MaskProbe
{
    [MenuItem("Tools/머리 서브메시 뜯어보기")]
    public static void Dump()
    {
        GameObject prefab = Resources.Load<GameObject>("Models/Person");
        if (prefab == null) { Debug.LogError("[MaskProbe] 모델 없음"); return; }

        GameObject go = Object.Instantiate(prefab);
        Transform head = Find(go.transform, "Head");
        Transform tip = Find(go.transform, "Head_end");
        if (head == null || tip == null) { Debug.LogError("[MaskProbe] 머리뼈 없음"); return; }
        float unit = Vector3.Distance(head.position, tip.position);

        SkinnedMeshRenderer skin = null;
        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0)
            { skin = r; break; }
        if (skin == null) { Debug.LogError("[MaskProbe] 머리 메시 없음"); return; }

        Mesh src = skin.sharedMesh;
        Mesh baked = new Mesh();
        skin.BakeMesh(baked, true);          // true 라야 임포트 배율이 들어간다
        Vector3[] bv = baked.vertices;
        Transform rt = skin.transform;

        Vector3[] P = new Vector3[bv.Length];
        for (int i = 0; i < bv.Length; i++)
            P[i] = head.InverseTransformPoint(rt.TransformPoint(bv[i]));

        Material[] mats = skin.sharedMaterials;
        StringBuilder sb = new StringBuilder();
        sb.Append("[MaskProbe] headUnit=").Append(unit.ToString("F4"))
          .Append(" m, 꼭짓점 ").Append(bv.Length).Append('\n');

        for (int sm = 0; sm < src.subMeshCount; sm++)
        {
            int[] tri = src.GetTriangles(sm);
            Vector3 lo = Vector3.one * 1e9f, hi = -Vector3.one * 1e9f;
            foreach (int t in tri) { lo = Vector3.Min(lo, P[t]); hi = Vector3.Max(hi, P[t]); }
            Color c = (sm < mats.Length && mats[sm] != null) ? mats[sm].color : Color.magenta;

            sb.Append("  sub ").Append(sm)
              .Append("  tri ").Append((tri.Length / 3).ToString().PadLeft(5))
              .Append("  색 ").Append(c.r.ToString("F2")).Append('/')
              .Append(c.g.ToString("F2")).Append('/').Append(c.b.ToString("F2"))
              .Append("  이름 ").Append(sm < mats.Length && mats[sm] != null ? mats[sm].name : "-")
              .Append("\n          x ").Append((lo.x / unit).ToString("F2")).Append("~").Append((hi.x / unit).ToString("F2"))
              .Append("   y ").Append((lo.y / unit).ToString("F2")).Append("~").Append((hi.y / unit).ToString("F2"))
              .Append("   z ").Append((lo.z / unit).ToString("F2")).Append("~").Append((hi.z / unit).ToString("F2"))
              .Append('\n');
        }

        Debug.Log(sb.ToString());
        Object.DestroyImmediate(baked);
        Object.DestroyImmediate(go);
    }

    private static Transform Find(Transform t, string name)
    {
        if (t.name == name) return t;
        for (int i = 0; i < t.childCount; i++)
        {
            Transform f = Find(t.GetChild(i), name);
            if (f != null) return f;
        }
        return null;
    }
}

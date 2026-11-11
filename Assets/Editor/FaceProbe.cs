using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Person.fbx 의 얼굴을 자로 잰다.
///
/// 눈·눈썹이 **따로 선 서브메시**라는 것까지는 알았다. 그러면 떼어 내서
/// 움직일 수 있는데, 그러려면 그게 머리뼈 기준 어디에 얼마만 한 크기로
/// 붙어 있는지를 알아야 한다. 눈대중으로 넣고 렌더해서 고치는 것보다
/// 한 번 재고 들어가는 쪽이 빠르다 — 입을 뚫을 때 그렇게 했다.
///
/// 좌표계는 MirrorRig.headPivot 과 같게 잡는다 (머리뼈 자리가 원점,
/// 프리팹은 회전이 없으니 월드축 그대로). 길이는 목관절~정수리(headUnit)로
/// 나눠 적는다 — MirrorRig 가 얼굴 위치를 그 비율로 들고 있다.
/// </summary>
public static class FaceProbe
{
    [MenuItem("Tools/얼굴 뜯어보기")]
    public static void Dump()
    {
        GameObject prefab = Resources.Load<GameObject>("Models/Person");
        if (prefab == null) { Debug.LogError("[FaceProbe] 모델 없음"); return; }

        GameObject go = Object.Instantiate(prefab);
        Transform head = Find(go.transform, "Head");
        Transform tip = Find(go.transform, "Head_end");
        if (head == null || tip == null) { Debug.LogError("[FaceProbe] 머리뼈 없음"); return; }
        float unit = Vector3.Distance(head.position, tip.position);

        StringBuilder sb = new StringBuilder();
        sb.Append("[FaceProbe] headUnit = ").Append(unit.ToString("F4")).Append(" m\n");

        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            Mesh src = r.sharedMesh;
            Mesh baked = new Mesh();
            r.BakeMesh(baked, true);
            Vector3[] bv = baked.vertices;
            if (bv.Length != src.vertexCount) { Debug.LogError("[FaceProbe] 정점 수 불일치"); return; }

            // MirrorRig.CarveMouth 와 같은 변환
            Vector3[] P = new Vector3[bv.Length];
            for (int i = 0; i < bv.Length; i++)
                P[i] = r.transform.TransformPoint(bv[i]) - head.position;

            for (int s = 0; s < src.subMeshCount; s++)
            {
                Material mt = s < r.sharedMaterials.Length ? r.sharedMaterials[s] : null;
                int[] tri = src.GetTriangles(s);
                sb.Append("── 서브메시 ").Append(s).Append(" ")
                  .Append(mt != null ? mt.name : "?")
                  .Append("  삼각형 ").Append(tri.Length / 3)
                  .Append("  색 ").Append(mt != null ? mt.color.ToString("F2") : "?").Append('\n');

                // 왼쪽 / 오른쪽으로 갈라서 잰다
                Side(sb, "  x<0", P, tri, unit, true);
                Side(sb, "  x>0", P, tri, unit, false);
            }
        }

        Debug.Log(sb.ToString());
        Object.DestroyImmediate(go);
    }

    private static void Side(StringBuilder sb, string tag, Vector3[] P, int[] tri,
                             float unit, bool negative)
    {
        HashSet<int> used = new HashSet<int>();
        foreach (int i in tri)
            if ((P[i].x < 0f) == negative) used.Add(i);
        if (used.Count == 0) { sb.Append(tag).Append(" (없음)\n"); return; }

        Vector3 lo = Vector3.one * 1e9f, hi = -Vector3.one * 1e9f, mid = Vector3.zero;
        foreach (int i in used) { lo = Vector3.Min(lo, P[i]); hi = Vector3.Max(hi, P[i]); mid += P[i]; }
        mid /= used.Count;

        sb.Append(tag).Append(" 정점 ").Append(used.Count)
          .Append("  가운데 (").Append((mid.x / unit).ToString("F3")).Append(", ")
          .Append((mid.y / unit).ToString("F3")).Append(", ")
          .Append((mid.z / unit).ToString("F3")).Append(")")
          .Append("  크기 ").Append(((hi.x - lo.x) * 100f).ToString("F1")).Append("×")
          .Append(((hi.y - lo.y) * 100f).ToString("F1")).Append("×")
          .Append(((hi.z - lo.z) * 100f).ToString("F1")).Append(" cm\n");
    }

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }
}

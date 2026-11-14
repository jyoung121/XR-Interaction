using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// JawUp 조각을 **꼭짓점 단위로** 뜯어본다.
///
/// 얼굴에서 막대 하나가 18.5 cm 앞으로 뻗어 나와 있다. 경계 상자로는
/// 거기까지만 알 수 있고, **어느 꼭짓점이 어디로 날아갔는지**는 구워 봐야
/// 안다. 스킨 메시라 원본 자리와 구운 자리가 다르므로 둘 다 적는다 —
/// 원본은 멀쩡한데 구운 쪽만 날아갔으면 범인은 뼈가중치다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod JawProbe.Dump
/// </summary>
public static class JawProbe
{
    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[JawProbe] 리그 없음"); return; }
        rig.StepForPreview(0f);

        foreach (SkinnedMeshRenderer r in rig.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (r.name != "JawUp") continue;
            Report(r);
            return;
        }

        // 스키닝에 실패하면 고정 메시로 달린다. 그쪽도 본다.
        foreach (MeshFilter mf in rig.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.name != "JawUp") continue;
            Debug.Log("[JawProbe] JawUp 이 **고정 메시**다 (스키닝 실패 경로)");
            Plain(mf.sharedMesh);
            return;
        }

        Debug.LogError("[JawProbe] JawUp 을 못 찾았다");
    }

    private static void Report(SkinnedMeshRenderer r)
    {
        Mesh src = r.sharedMesh;
        Mesh baked = new Mesh();
        r.BakeMesh(baked, true);

        Vector3[] raw = src.vertices;
        Vector3[] bak = baked.vertices;
        BoneWeight[] bw = src.boneWeights;
        Transform[] bones = r.bones;

        StringBuilder sb = new StringBuilder();
        sb.Append("[JawProbe] JawUp 꼭짓점 ").Append(raw.Length)
          .Append("  뼈 ").Append(bones == null ? 0 : bones.Length)
          .Append("  가중치 ").Append(bw == null ? 0 : bw.Length).Append('\n');
        sb.Append("  원본 경계 ").Append(src.bounds.ToString("F4")).Append('\n');
        sb.Append("  구운 경계 ").Append(baked.bounds.ToString("F4")).Append('\n');

        // 구운 뒤 한가운데에서 가장 먼 꼭짓점 열둘.
        Vector3 mid = baked.bounds.center;
        int n = Mathf.Min(bak.Length, raw.Length);
        int[] order = new int[n];
        float[] dist = new float[n];
        for (int i = 0; i < n; i++) { order[i] = i; dist[i] = (bak[i] - mid).magnitude; }
        System.Array.Sort(dist, order);

        sb.Append("  ── 구운 뒤 가장 멀리 간 꼭짓점 ──\n");
        for (int k = 0; k < Mathf.Min(12, n); k++)
        {
            int i = order[n - 1 - k];
            sb.Append("   #").Append(i)
              .Append("  거리 ").Append((bak[i] - mid).magnitude.ToString("F4"))
              .Append("  원본 ").Append(raw[i].ToString("F4"))
              .Append("  구운 ").Append(bak[i].ToString("F4"));
            if (bw != null && i < bw.Length)
            {
                BoneWeight w = bw[i];
                sb.Append("  뼈 ").Append(Name(bones, w.boneIndex0)).Append(':')
                  .Append(w.weight0.ToString("F2"))
                  .Append(' ').Append(Name(bones, w.boneIndex1)).Append(':')
                  .Append(w.weight1.ToString("F2"))
                  .Append("  합 ").Append((w.weight0 + w.weight1 + w.weight2 + w.weight3)
                                           .ToString("F3"));
            }
            sb.Append('\n');
        }

        // 가중치 합이 0 인 꼭짓점이 몇이나 되는지. 그게 범인이면 숫자로 나온다.
        int zero = 0;
        if (bw != null)
            for (int i = 0; i < bw.Length; i++)
                if (bw[i].weight0 + bw[i].weight1 + bw[i].weight2 + bw[i].weight3 < 0.001f)
                    zero++;
        sb.Append("  가중치 합이 0 인 꼭짓점 ").Append(zero).Append('\n');

        Debug.Log(sb.ToString());
        Object.DestroyImmediate(baked);
    }

    private static void Plain(Mesh m)
    {
        if (m == null) { Debug.LogError("[JawProbe] 메시 없음"); return; }
        Vector3[] v = m.vertices;
        Vector3 mid = m.bounds.center;
        StringBuilder sb = new StringBuilder();
        sb.Append("[JawProbe] 꼭짓점 ").Append(v.Length)
          .Append("  경계 ").Append(m.bounds.ToString("F4")).Append('\n');
        for (int i = 0; i < v.Length; i++)
            if ((v[i] - mid).magnitude > 0.04f)
                sb.Append("   #").Append(i).Append(' ').Append(v[i].ToString("F4")).Append('\n');
        Debug.Log(sb.ToString());
    }

    private static string Name(Transform[] bones, int i)
    {
        if (bones == null || i < 0 || i >= bones.Length) return "?" + i;
        return bones[i] == null ? "null" + i : bones[i].name;
    }
}

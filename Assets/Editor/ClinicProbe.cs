using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor.SceneManagement;

/// <summary>
/// 치과 방이 **화면의 몇 %를 뭘로 채우고 있는지** 잰다. 눈으로 보고
/// "휑하다" 고 말하기는 쉬운데, 그러면 어디에 뭘 놓아야 하는지가 안 나온다.
/// 참가자 눈에서 격자로 광선을 쏴서 맞은 물건 이름과 거리를 모은다.
///
/// 배치모드 실행:
///   Unity.exe -batchmode -quit -projectPath &lt;proj&gt; -executeMethod ClinicProbe.Dump
/// </summary>
public static class ClinicProbe
{
    private const int GX = 60;
    private const int GY = 40;
    private const float Fov = 75f;

    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects,
                                    NewSceneMode.Single);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 0f),
                                                  Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[ClinicProbe] 리그 없음"); return; }

        rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Drill);
        rig.StepForPreview(1.8f);

        var temps = new List<GameObject>();
        Collide(rig.transform, temps);

        Shot(rig, "올려다봄 (거울)", new Vector3(-35f, 0f, 0f));
        Shot(rig, "정면",            new Vector3(  0f, 0f, 0f));
        Shot(rig, "턱 당김",         new Vector3( 15f, 0f, 0f));
        Shot(rig, "왼쪽 (치과의)",   new Vector3( -8f, -42f, 0f));
        Shot(rig, "오른쪽",          new Vector3( -8f,  42f, 0f));
        Shot(rig, "오른쪽 끝",       new Vector3(  0f,  70f, 0f));

        foreach (GameObject g in temps) Object.DestroyImmediate(g);
        Debug.Log("[ClinicProbe] 끝");
    }

    /// <summary>리그 안의 모든 메시에 충돌체를 붙인다. 스킨 메시는 구워서 쓴다.</summary>
    private static void Collide(Transform root, List<GameObject> temps)
    {
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) continue;
            MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }
        foreach (SkinnedMeshRenderer sm in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (sm.sharedMesh == null) continue;
            Mesh baked = new Mesh();
            sm.BakeMesh(baked, true);
            GameObject g = new GameObject("PROBE_" + sm.transform.root.name
                                          + "/" + sm.name);
            g.transform.SetPositionAndRotation(sm.transform.position,
                                               sm.transform.rotation);
            MeshCollider mc = g.AddComponent<MeshCollider>();
            mc.sharedMesh = baked;
            temps.Add(g);
        }
    }

    /// <summary>한 방향으로 격자 광선을 쏘고 맞은 것을 모아 찍는다.</summary>
    private static void Shot(MirrorRig rig, string title, Vector3 euler)
    {
        Vector3 eye = new Vector3(0f, 1.15f, 0f);
        Quaternion rot = Quaternion.Euler(euler);
        float ty = Mathf.Tan(Fov * 0.5f * Mathf.Deg2Rad);
        float tx = ty * 1000f / 700f;

        var area = new Dictionary<string, int>();
        var dist = new Dictionary<string, float>();
        int miss = 0, total = 0;
        float near = 99f, far = 0f, sum = 0f;

        for (int iy = 0; iy < GY; iy++)
        for (int ix = 0; ix < GX; ix++)
        {
            float u = (ix + 0.5f) / GX * 2f - 1f;
            float v = (iy + 0.5f) / GY * 2f - 1f;
            Vector3 dir = rot * new Vector3(u * tx, v * ty, 1f).normalized;
            total++;
            RaycastHit h;
            if (!Physics.Raycast(eye, dir, out h, 60f)) { miss++; continue; }
            string n = Name(h.collider.transform);
            if (!area.ContainsKey(n)) { area[n] = 0; dist[n] = 0f; }
            area[n]++;
            dist[n] += h.distance;
            sum += h.distance;
            if (h.distance < near) near = h.distance;
            if (h.distance > far) far = h.distance;
        }

        var keys = new List<string>(area.Keys);
        keys.Sort((a, b) => area[b].CompareTo(area[a]));

        var sb = new StringBuilder();
        sb.AppendLine("[ClinicProbe] ── " + title + " ──");
        sb.AppendLine(string.Format("  빈 화면 {0:0.0}%   평균 거리 {1:0.00} m   {2:0.00}~{3:0.00} m",
                                    100f * miss / total,
                                    sum / Mathf.Max(1, total - miss), near, far));
        int shown = 0;
        foreach (string k in keys)
        {
            if (shown++ >= 14) break;
            sb.AppendLine(string.Format("  {0,-22} {1,5:0.0}%  {2:0.00} m",
                                        k, 100f * area[k] / total, dist[k] / area[k]));
        }
        // 어디에 있는지도 찍는다. "옆벽이 43 %" 만으로는 그게 화면
        // 어느 쪽인지 몰라서, 뭘 어디다 놓아야 하는지가 안 나온다.
        var mark = new Dictionary<string, char>();
        const string Alpha = "123456789abcdefghijklmnopqrstuvwxyz";
        int ai = 0;
        foreach (string k in keys)
            if (ai < Alpha.Length) mark[k] = Alpha[ai++];
        var map = new StringBuilder();
        map.AppendLine("[ClinicProbe] 자리 — " + title);
        for (int iy = GY - 1; iy >= 0; iy -= 2)
        {
            map.Append("  ");
            for (int ix = 0; ix < GX; ix++)
            {
                float u = (ix + 0.5f) / GX * 2f - 1f;
                float v = (iy + 0.5f) / GY * 2f - 1f;
                Vector3 d2 = rot * new Vector3(u * tx, v * ty, 1f).normalized;
                RaycastHit h2;
                if (!Physics.Raycast(eye, d2, out h2, 60f)) { map.Append('.'); continue; }
                string n2 = Name(h2.collider.transform);
                map.Append(mark.ContainsKey(n2) ? mark[n2] : '?');
            }
            map.AppendLine();
        }
        int si = 0;
        foreach (string k in keys)
        {
            if (si >= 20) break;
            map.Append(string.Format("  {0}={1}", Alpha[si], k));
            if (++si % 4 == 0) map.AppendLine();
        }
        Debug.Log(sb.ToString());
        Debug.Log(map.ToString());
    }

    /// <summary>이름을 쓸 만하게 줄인다. 모델 내부 조각은 뿌리 이름으로 묶는다.</summary>
    private static string Name(Transform t)
    {
        Transform cur = t;
        for (int i = 0; i < 6 && cur != null; i++)
        {
            string n = cur.name;
            if (n.StartsWith("PROBE_")) return n.Substring(6);
            if (n.StartsWith("Room_") || n == "MirrorRig") break;
            if (cur.parent != null && (cur.parent.name.StartsWith("Room_")
                                       || cur.parent.name == "MirrorRig"
                                       || cur.parent.name == "CeilPlane"))
                return n;
            cur = cur.parent;
        }
        return t.name;
    }
}

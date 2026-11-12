using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
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

        Collide(rig.transform);

        Shot(rig, "올려다봄 (거울)", new Vector3(-35f, 0f, 0f));
        Shot(rig, "정면",            new Vector3(  0f, 0f, 0f));
        Shot(rig, "턱 당김",         new Vector3( 15f, 0f, 0f));
        Shot(rig, "왼쪽 (치과의)",   new Vector3( -8f, -42f, 0f));
        Shot(rig, "오른쪽",          new Vector3( -8f,  42f, 0f));
        Shot(rig, "오른쪽 끝",       new Vector3(  0f,  70f, 0f));
        Shot(rig, "왼쪽 끝",         new Vector3(  0f, -70f, 0f));

        Debug.Log("[ClinicProbe] 끝");
    }

    /// <summary>
    /// 리그 안의 **굳은 메시에만** 충돌체를 붙인다. 사람은 빼고 센다.
    ///
    /// 처음에는 사람도 구워서(BakeMesh) 넣었다. 그런데 렌더에서는 옆에
    /// 선 치과의가 화면 절반을 덮는데 프로브는 사람을 거의 안 잡았다.
    /// 둘 중 하나가 틀렸으니 자를 먼저 의심했고, 구운 메시의 경계를
    /// 찍어 보니 꼭짓점 4894 개가 전부 원점에 몰린 **1 cm 짜리 점**이었다.
    /// 오류도 경고도 없다. 이 방을 재는 내내 사람 셋은 없는 것으로
    /// 세고 있었다.
    ///
    /// 원인을 여섯 번 쫓았다. FBX 의 Read/Write 를 켜도, 뼈(62)·
    /// 바인드포즈(62)·뿌리뼈를 확인해도, updateWhenOffscreen 을 켜도
    /// 그대로였다 — 이 배치모드에서 BakeMesh 는 작동하지 않는다.
    ///
    /// 그래서 **사람을 아예 뺀다.** 벽을 어떻게 채울지 정하는 데에는
    /// 이쪽이 오히려 맞는 자다: 벽이 비었느냐는 한 동작에서 누가 그
    /// 앞에 섰느냐와 상관없는 질문이다. 다만 여기서 나오는 벽 비율은
    /// **사람이 가리기 전의 값**이고, 실제 화면에서는 그만큼 덜 보인다.
    /// </summary>
    private static void Collide(Transform root)
    {
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            if (mf.GetComponent<Collider>() != null) continue;
            MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
        }
        int skin = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length;
        Debug.Log("[ClinicProbe] 사람은 안 센다 — 스킨 메시 " + skin + " 개를 뺐다."
                  + " 아래 벽 비율은 사람이 가리기 전의 값이다.");
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
        // 맞은 자리를 **방 좌표로** 모은다. 글자 지도에서 "왼쪽쯤" 을
        // 읽어 내는 것도 결국 짐작이다. 두 번 엉뚱한 데를 채우고 나서
        // 넣었다 — 어디를 채워야 하는지는 x, y 로 나와야 한다.
        var lo = new Dictionary<string, Vector3>();
        var hi = new Dictionary<string, Vector3>();
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
            Vector3 lp = RoomLocal(h.collider.transform, h.point);
            if (!area.ContainsKey(n))
            {
                area[n] = 0; dist[n] = 0f;
                lo[n] = lp; hi[n] = lp;
            }
            lo[n] = Vector3.Min(lo[n], lp);
            hi[n] = Vector3.Max(hi[n], lp);
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
            sb.AppendLine(string.Format(
                "  {0,-22} {1,5:0.0}%  {2:0.00} m   방 x {3,5:0.00}~{4,5:0.00}  y {5,5:0.00}~{6,5:0.00}  z {7,5:0.00}~{8,5:0.00}",
                k, 100f * area[k] / total, dist[k] / area[k],
                lo[k].x, hi[k].x, lo[k].y, hi[k].y, lo[k].z, hi[k].z));
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

    /// <summary>맞은 자리를 그 물건이 속한 방의 좌표로 옮긴다.</summary>
    private static Vector3 RoomLocal(Transform t, Vector3 world)
    {
        Transform cur = t;
        while (cur != null && !cur.name.StartsWith("Room_")) cur = cur.parent;
        return cur == null ? world : cur.InverseTransformPoint(world);
    }

    /// <summary>이름을 쓸 만하게 줄인다. 모델 내부 조각은 뿌리 이름으로 묶는다.</summary>
    private static string Name(Transform t)
    {
        Transform cur = t;
        for (int i = 0; i < 6 && cur != null; i++)
        {
            string n = cur.name;
            if (n.StartsWith("Room_") || n == "MirrorRig") break;
            // 거울 벽은 **묶지 않는다.** 위·아래·양옆 네 장이 한 이름으로
            // 찍히니 "거울 벽 28 %" 가 어느 장인지 알 수가 없었다.
            if (cur.parent != null && cur.parent.name == "MirrorWall") return n;
            if (cur.parent != null && (cur.parent.name.StartsWith("Room_")
                                       || cur.parent.name == "MirrorRig"
                                       || cur.parent.name == "CeilPlane"))
                return n;
            cur = cur.parent;
        }
        return t.name;
    }
}

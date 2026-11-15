using System.Text;
using UnityEngine;

/// <summary>
/// 음식 메시가 **폰에서 읽히는지** 확인한다.
///
/// Mesh.isReadable 이 false 면 에디터에서는 멀쩡히 읽히고 폰에서만 빈
/// 배열이 온다. 사과가 사라졌던 이유이자, 그 전에 얼굴이 깎이다 만
/// 이유다. 두 번 당했으니 이제는 빌드 전에 센다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod ReadableProbe.Dump
/// </summary>
public static class ReadableProbe
{
    public static void Dump()
    {
        string[] names = { "Apple", "GummyBear", "Steak", "Pudding", "Candy", "Plate" };
        StringBuilder sb = new StringBuilder("[Readable] 모델 메시\n");

        foreach (string n in names)
        {
            GameObject go = Resources.Load<GameObject>("Models/" + n);
            if (go == null) { sb.Append("  ").Append(n).Append("  <없음>\n"); continue; }

            foreach (MeshFilter f in go.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = f.sharedMesh;
                if (m == null) continue;
                sb.Append("  ").Append(n).Append('/').Append(f.name)
                  .Append("  정점 ").Append(m.vertexCount)
                  .Append("  isReadable ").Append(m.isReadable)
                  .Append(m.isReadable ? "" : "   <-- 폰에서 빈 배열")
                  .Append('\n');
            }
        }

        // 사과는 깎으므로 반드시 읽혀야 한다.
        GameObject ap = Resources.Load<GameObject>("Models/Apple");
        bool ok = false;
        if (ap != null)
            foreach (MeshFilter f in ap.GetComponentsInChildren<MeshFilter>(true))
                if (f.sharedMesh != null && f.sharedMesh.isReadable) ok = true;
        sb.Append(ok ? "  => 사과 OK (깎을 수 있다)"
                     : "  => **사과를 못 읽는다 — 폰에서 또 사라진다**");

        Debug.Log(sb.ToString());
    }
}

using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Person.fbx 의 손뼈 이름을 그대로 찍는다. MakeLimb 이 찾는 이름과
/// 맞는지 보려는 것이다 — 안 맞으면 Curl 이 아무것도 안 한다.</summary>
public static class HandProbe
{
    [MenuItem("Tools/손뼈 뜯어보기")]
    public static void Dump()
    {
        GameObject prefab = Resources.Load<GameObject>("Models/Person");
        if (prefab == null) { Debug.LogError("[HandProbe] 모델 없음"); return; }
        GameObject go = Object.Instantiate(prefab);

        StringBuilder sb = new StringBuilder("[HandProbe] 손 언저리 뼈 이름\n");
        foreach (Transform t in go.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (n.Contains("hand") || n.Contains("index") || n.Contains("middle")
                || n.Contains("ring") || n.Contains("pinky") || n.Contains("thumb")
                || n.Contains("finger") || n.Contains("wrist") || n.Contains("palm"))
                sb.Append("  ").Append(t.name)
                  .Append("   parent=").Append(t.parent != null ? t.parent.name : "-")
                  .Append('\n');
        }
        Debug.Log(sb.ToString());
        Object.DestroyImmediate(go);
    }
}

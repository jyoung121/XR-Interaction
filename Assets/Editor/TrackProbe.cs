using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 거울 속 사람이 **참가자 자리를 따라오는지** 숫자로 잰다.
///
/// FollowViewer 가 있던 동안 TrackViewer 가 보는 p 는 늘 (0,0,0) 이었다.
/// 참가자가 옆으로 기울여도 반사가 가만히 있었다는 뜻이다. 빼고 나서
/// 정말 따라오는지는 "그럴 것이다" 가 아니라 재서 확인해야 한다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod TrackProbe.Dump
/// </summary>
public static class TrackProbe
{
    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[TrackProbe] 리그 없음"); return; }

        Transform head = FindHead(rig);
        if (head == null) { Debug.LogError("[TrackProbe] 머리뼈 없음"); return; }

        rig.StepForPreview(0f);
        Vector3 a = head.position;
        Vector3 rigAt = rig.transform.position;

        // 참가자가 오른쪽으로 10 cm, 위로 5 cm, 거울 쪽으로 8 cm 간다.
        Vector3 move = new Vector3(0.10f, 0.05f, 0.08f);
        viewerGo.transform.position = move;
        rig.StepForPreview(0f);
        Vector3 b = head.position;

        Vector3 got = b - a;
        Vector3 want = new Vector3(move.x, move.y, -move.z);

        Debug.Log("[TrackProbe] 리그 원점 " + rigAt.ToString("F4")
                  + "  (움직이면 안 된다: " + (rig.transform.position - rigAt).magnitude.ToString("F5") + ")"
                  + "\n  참가자 이동 " + move.ToString("F4")
                  + "\n  거울 머리 이동 " + got.ToString("F4")
                  + "\n  기대값       " + want.ToString("F4")
                  + "\n  오차 " + (got - want).magnitude.ToString("F5")
                  + (got.magnitude < 0.001f
                     ? "\n  ** 안 따라온다 — 반사가 굳어 있다 **"
                     : ((got - want).magnitude < 0.01f
                        ? "\n  OK 따라온다"
                        : "\n  ** 따라오긴 하는데 값이 어긋난다 **")));
    }

    private static Transform FindHead(MirrorRig rig)
    {
        foreach (Transform t in rig.GetComponentsInChildren<Transform>(true))
            if (t.name == "Head" || t.name == "head" || t.name == "HeadBone") return t;
        return null;
    }
}

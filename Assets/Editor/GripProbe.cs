using UnityEditor;
using UnityEngine;

/// <summary>옆에 선 치과의가 드릴을 어떻게 쥐고 있는지 자로 잰다.
/// 손가락 고리의 한가운데를 눈대중으로 두 번 틀렸다 — 재고 들어간다.</summary>
public static class GripProbe
{
    [MenuItem("Tools/쥔 손 뜯어보기")]
    public static void Dump()
    {
        GameObject sun = GameObject.Find("Sun");
        if (sun != null) sun.SetActive(false);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(new Vector3(0f, 1.15f, 0f), Quaternion.identity);

        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[GripProbe] 리그 없음"); return; }

        rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.DentalKind.Drill);
        rig.StepForPreview(1.8f);
        Debug.Log(rig.DumpGrip());

        if (sun != null) sun.SetActive(true);
    }
}

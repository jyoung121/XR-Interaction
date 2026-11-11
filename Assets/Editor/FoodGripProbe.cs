using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 다섯 음식마다 집게손이 음식을 실제로 쥐고 있는지 잰다.
/// HandProbe·GripProbe 와 같은 자리에 둔다.
/// </summary>
public static class FoodGripProbe
{
    [MenuItem("Tools/집게손 뜯어보기")]
    public static void Dump()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.position = new Vector3(0f, 1.15f, 0f);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[FoodGrip] 리그 없음"); return; }

        foreach (MirrorRig.FoodKind k in System.Enum.GetValues(typeof(MirrorRig.FoodKind)))
        {
            rig.ShowScenario(MirrorRig.Scenario.Food, k);
            // 무는 순간을 리그에서 받는다. 0.4 초로 뒀더니 손이 아직
            // 접시 옆에 있어서 손끝이 음식에서 34 cm 떨어져 나왔다.
            rig.StepForPreview(MirrorRig.BiteMomentOf(k));
            Debug.Log(rig.DumpFoodGrip());
        }
    }
}

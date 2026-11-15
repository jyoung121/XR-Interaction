using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// 진동 설정창을 참가자 눈으로 찍는다.
///
/// 두 장이 필요하다. **앞을 볼 때** 창이 화면 가장자리에 걸치는지(가려
/// 놓으면 실험이 안 되고, 아예 안 보이면 있는 줄을 모른다), 그리고
/// **그쪽으로 고개를 돌렸을 때** 칸과 그림이 읽히는지. 글자를 못 쓰므로
/// 그림만으로 무슨 손잡이인지 알아볼 수 있어야 한다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod PanelShot.Dump
/// </summary>
public static class PanelShot
{
    private const string Out = "C:/UnityProjects/preview/panel";

    public static void Dump()
    {
        System.IO.Directory.CreateDirectory(Out);
        EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        GameObject viewerGo = new GameObject("Viewer");
        viewerGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        MirrorRig rig = MirrorRig.Create(viewerGo.transform);
        if (rig == null) { Debug.LogError("[PanelShot] 리그 없음"); return; }
        rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Apple);
        rig.StepForPreview(0.3f);

        GameObject camGo = new GameObject("Shot");
        Camera cam = camGo.AddComponent<Camera>();
        // 카드보드 한쪽 눈에 가까운 화각. 좁게 찍으면 가장자리에 걸치는지를
        // 못 본다 — 그게 이 사진의 요점이다.
        cam.fieldOfView = 80f;
        cam.nearClipPlane = 0.02f;
        cam.transform.position = viewerGo.transform.position;

        HapticPanel.Create(rig.transform, cam.transform);

        RenderTexture rt = new RenderTexture(900, 700, 24, RenderTextureFormat.ARGB32);
        Texture2D tex = new Texture2D(900, 700, TextureFormat.RGB24, false);
        cam.targetTexture = rt;

        // (1) 앞을 본다 — 창이 오른쪽 가장자리에 걸쳐야 한다.
        Look(cam, 0f);
        HapticPanel.Step(false, true);
        Shot(cam, rt, tex, Out + "/ahead.png");

        // (2) 창 쪽으로 돌아본다 — 겨눈 줄이 밝아져야 한다.
        Look(cam, 40f);
        HapticPanel.Step(false, true);
        Shot(cam, rt, tex, Out + "/aimed_row0.png");

        // (3) 탭 세 번 — 세기 칸이 따라 움직여야 한다.
        for (int i = 0; i < 3; i++) HapticPanel.Step(true, true);
        Shot(cam, rt, tex, Out + "/tapped.png");

        // (4) 아래 줄을 겨눈다. 치과처럼 주기가 안 먹는 때는 흐려야 한다.
        Look(cam, 40f, 6f);
        HapticPanel.Step(false, false);
        Shot(cam, rt, tex, Out + "/row1_dim.png");

        GameObject pan = GameObject.Find("HapticPanel");
        if (pan == null) Debug.LogError("[PanelShot] 창이 씬에 없다");
        else
        {
            Vector3 d = pan.transform.position - cam.transform.position;
            Debug.Log("[PanelShot] 창 " + pan.transform.position.ToString("F3")
                + "  배율 " + pan.transform.lossyScale.ToString("F3")
                + "  눈에서 " + d.magnitude.ToString("F3") + " m"
                + "  눈 기준 각 " + Vector3.Angle(cam.transform.forward, d).ToString("F1")
                + "도  자식 " + pan.transform.childCount);
            Debug.Log("[PanelShot] 리그뿌리 배율 "
                + rig.transform.lossyScale.ToString("F3")
                + "  자리 " + rig.transform.position.ToString("F3")
                + "  눈 " + cam.transform.position.ToString("F3"));
        }
        Debug.Log("[PanelShot] " + HapticPanel.Info() + " -> " + Out);
        cam.targetTexture = null;
        Object.DestroyImmediate(rt);
    }

    private static void Look(Camera cam, float yaw, float pitch = 0f)
    {
        cam.transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    private static void Shot(Camera cam, RenderTexture rt, Texture2D tex, string path)
    {
        cam.Render();
        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = prev;
        System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
    }
}

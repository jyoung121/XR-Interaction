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

        // 줄이 셋이 되면서 **어느 줄을 겨누는지가 수로 정해진다.**
        // 창은 눈보다 Drop(=5cm) 아래에 있고 줄은 0.060 간격이며 배율은
        // Dist/DesignDist 다. 그러니 각도를 손으로 적는 대신 계산해서
        // 겨누고, 창이 내놓는 AimedRow 가 그 줄과 **같은지 확인한다** —
        // 사진만 보고는 1 도 차이로 이웃 줄에 걸린 것을 알 수 없다.
        for (int row = 0; row < 3; row++)
        {
            float y = (0.060f - row * 0.060f) * (0.38f / 0.60f) - 0.05f;
            float pitch = -Mathf.Atan2(y, 0.38f) * Mathf.Rad2Deg;
            Look(cam, 42f, pitch);
            // 그 줄이 먹는 상황으로 놓고 찍는다 — 0·1 은 음식,
            // 2 는 치과의 **드릴**이다 (칫솔은 아래 따로).
            HapticPanel.Step(false, row != 2, row == 2);
            Debug.Log("[PanelShot] " + row + " 번 줄을 겨눔 (고개 "
                + pitch.ToString("F1") + "도)  -> 창이 고른 줄 "
                + HapticPanel.AimedRow
                + (HapticPanel.AimedRow == row ? "  맞다" : "  **어긋났다**"));
            Shot(cam, rt, tex, Out + "/aim" + row + ".png");
        }

        // (1) 앞을 본다 — 창이 오른쪽 가장자리에 걸쳐야 한다.
        Look(cam, 0f);
        HapticPanel.Step(false, true, false);
        Debug.Log("[PanelShot] 앞을 볼 때 고른 줄 " + HapticPanel.AimedRow
                  + " (-1 이라야 한다)");
        Shot(cam, rt, tex, Out + "/ahead.png");

        // (2) 부하 깊이 줄에서 탭 두 번. 1.00 -> 1.40 -> 0.00 이라야 한다.
        {
            float y = (0.060f - 2 * 0.060f) * (0.38f / 0.60f) - 0.05f;
            Look(cam, 42f, -Mathf.Atan2(y, 0.38f) * Mathf.Rad2Deg);
            for (int i = 0; i < 2; i++) HapticPanel.Step(true, false, true);
            Debug.Log("[PanelShot] 부하 줄 탭 두 번 -> " + HapticPanel.Info());
            Shot(cam, rt, tex, Out + "/tapped_load.png");
        }

        // (3) 안 먹는 줄을 눌러 본다. **값이 안 바뀌어야** 한다.
        {
            string before = HapticPanel.Info();
            float y = (0.060f - 1 * 0.060f) * (0.38f / 0.60f) - 0.05f;
            Look(cam, 42f, -Mathf.Atan2(y, 0.38f) * Mathf.Rad2Deg);
            bool used = HapticPanel.Step(true, false, true);   // 드릴인데 주기 줄
            Debug.Log("[PanelShot] 흐린 줄 탭: 탭 먹음 " + used
                + " (true 라야 한다 — 안 그러면 블록이 넘어간다)"
                + System.Environment.NewLine + "   전 " + before
                + System.Environment.NewLine + "   후 " + HapticPanel.Info()
                + (before == HapticPanel.Info() ? "   안 바뀌었다 맞다"
                                                : "   **몰래 바뀌었다**"));
            Shot(cam, rt, tex, Out + "/row1_dim.png");
        }

        // (4) 칫솔. **세기 말고는 다 흐려야 한다** — 주기는 음식 것이고
        // 부하 깊이는 조원이 맡은 칫솔 자극에 아직 안 붙였다. 줄을
        // 켜 놓고 눌러도 아무 일이 없으면 그게 고장으로 읽힌다.
        {
            string before = HapticPanel.Info();
            float y = (0.060f - 2 * 0.060f) * (0.38f / 0.60f) - 0.05f;
            Look(cam, 42f, -Mathf.Atan2(y, 0.38f) * Mathf.Rad2Deg);
            HapticPanel.Step(true, false, false);     // 칫솔에서 부하 줄 탭
            Debug.Log("[PanelShot] 칫솔에서 부하 줄 탭"
                + System.Environment.NewLine + "   전 " + before
                + System.Environment.NewLine + "   후 " + HapticPanel.Info()
                + (before == HapticPanel.Info() ? "   안 바뀌었다 맞다"
                                                : "   **몰래 바뀌었다**"));
            Shot(cam, rt, tex, Out + "/brush_dim.png");
        }

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

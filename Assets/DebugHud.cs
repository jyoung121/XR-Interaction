using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// VR 화면 안에 떠 있는 진단 표시판.
///
/// iOS 에서는 Debug.Log 를 보려면 맥(Xcode/Console)이 필요하다.
/// 윈도우만 있는 환경에서도 상태를 확인할 수 있게, 카메라 앞에 글자를 띄운다.
///
/// 씬을 고치지 않고 런타임에 전부 만들어 붙이므로 Inspector 연결이 필요 없다.
/// </summary>
public class DebugHud : MonoBehaviour
{
    private static DebugHud instance;
    private Text text;

    public static DebugHud Create(Transform parent)
    {
        if (instance != null) return instance;

        GameObject root = new GameObject("DebugHud");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, -0.35f, 2.0f);
        root.transform.localRotation = Quaternion.identity;

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1200f, 500f);
        rt.localScale = Vector3.one * 0.0012f;   // 월드 기준 약 1.4m 폭

        GameObject tgo = new GameObject("Text");
        tgo.transform.SetParent(root.transform, false);
        Text t = tgo.AddComponent<Text>();
        t.font = LoadFont();
        t.fontSize = 34;
        t.color = Color.white;
        t.alignment = TextAnchor.UpperLeft;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        // 내장 글꼴에 한글 글리프가 없다. 여기 들어가는 문자열은 전부 ASCII 여야 한다.
        t.text = "HUD";

        RectTransform trt = t.GetComponent<RectTransform>();
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = new Vector2(20f, 20f);
        trt.offsetMax = new Vector2(-20f, -20f);

        // 글자가 배경에 묻히지 않게 외곽선
        Outline o = tgo.AddComponent<Outline>();
        o.effectColor = Color.black;
        o.effectDistance = new Vector2(2f, -2f);

        instance = root.AddComponent<DebugHud>();
        instance.text = t;
        return instance;
    }

    private static Font LoadFont()
    {
        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Helvetica", 34);
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 34);
        return f;
    }

    public static void Set(string s)
    {
        if (instance != null && instance.text != null) instance.text.text = s;
    }

    public static void Show(bool on)
    {
        if (instance != null) instance.gameObject.SetActive(on);
    }
}

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
        // 2 m 앞에 두었더니 **코앞을 덮은 물건 뒤로 들어가** 안 보였다.
        // 진단판이 사고를 당하면 진단을 못 한다. 가까이 당기고,
        // 아래에서 깊이 검사를 꺼서 무조건 맨 앞에 그린다.
        root.transform.localPosition = new Vector3(0f, -0.22f, 0.6f);
        root.transform.localRotation = Quaternion.identity;

        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        RectTransform rt = canvas.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1200f, 500f);
        rt.localScale = Vector3.one * 0.00036f;  // 0.6 m 앞에서 약 0.43 m 폭

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

        // **깊이 검사를 끈다.** 무엇에 파묻혀 있든 글자는 보여야 한다.
        Shader sh = Shader.Find("UI/Default");
        if (sh != null)
        {
            Material m = new Material(sh);
            m.SetInt("unity_GUIZTestMode",
                     (int)UnityEngine.Rendering.CompareFunction.Always);
            t.material = m;
        }

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

    /// <summary>
    /// **예외를 화면에 띄운다.**
    ///
    /// 11/14 에 HUD 가 "starting XR..." 에서 멈춰 있었다. 거울 방에 들어가기
    /// 직전에 한 번 찍는 글자였으니, 그 뒤 코루틴이 죽었다는 뜻이다.
    /// 죽은 이유는 화면 어디에도 안 나왔다 — iOS 에서 Debug.Log 를 보려면
    /// 맥이 필요하고, 우리는 윈도우뿐이다.
    ///
    /// 그래서 Unity 가 내는 오류·예외를 가로채 **마지막 한 건을 화면에
    /// 남긴다.** 어느 줄에서 터졌는지가 폰 화면에 바로 적힌다.
    /// 이게 없어서 같은 자리를 세 번 돌았다.
    /// </summary>
    private static string lastError = "";
    private static string status = "";

    private void OnEnable()  { Application.logMessageReceived += OnLog; }
    private void OnDisable() { Application.logMessageReceived -= OnLog; }

    private static void OnLog(string condition, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception
            && type != LogType.Assert) return;

        // 스택 첫 줄이 터진 자리다. 화면이 좁으니 거기까지만.
        string head = stack ?? "";
        int nlpos = head.IndexOf(CHR_LF);
        if (nlpos > 0) head = head.Substring(0, nlpos);

        lastError = "ERR " + Trim(condition, 90) + " @ " + Trim(head, 90);
        Paint();
    }

    private const char CHR_LF = (char)10;

    private static string Trim(string s, int n)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= n ? s : s.Substring(0, n);
    }

    private static void Paint()
    {
        if (instance == null || instance.text == null) return;
        instance.text.text = lastError.Length > 0
            ? status + CHR_LF + lastError
            : status;
    }

    public static void Set(string s)
    {
        status = s;
        Paint();
    }

    public static void Show(bool on)
    {
        if (instance != null) instance.gameObject.SetActive(on);
    }
}

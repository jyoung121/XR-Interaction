using UnityEngine;

/// <summary>
/// 거울 방 화면 **아래쪽에 뜨는 숫자 하나** (1·2·3).
///
/// 사과 씬에 지금 어느 진동이 걸려 있는지를 알려 준다 — 설정 화면의
/// "3. APPLY TO SCENE" 에서 고른 번호다 (AppleSync.OptionNumber).
///   1 = 소리의 잔 파열마다 탭,  2 = Crunch,  3 = 고른 패턴
///
/// **글자를 안 쓴다.** 이 앱에서 기본 글꼴로 찍은 글자는 아이폰에서 깨져 나왔다
/// (HapticPanel 주석 (가)). 그래서 전자시계처럼 **막대 일곱 개**로 숫자를 그린다.
///
/// 머리에 붙인다. 설정창(HapticPanel)과 달리 이건 쳐다볼 물건이 아니라 늘 시야
/// 아래에 걸려 있어야 하는 표시라, 고개를 돌려도 따라와야 한다.
/// </summary>
public static class OptionBadge
{
    // 눈에서 38 cm 앞, 12 cm 아래 — 정면에서 17도쯤 아래다. 거울(45 cm)보다 가까워야
    // 거울 틀에 안 가린다.
    private const float Dist = 0.38f;
    private const float Drop = -0.12f;

    // 숫자 한 자의 크기 (m). 높이 2 cm 면 38 cm 앞에서 3도쯤이다.
    private const float W = 0.010f, H = 0.020f, T = 0.0026f;

    private static Transform root;
    private static readonly GameObject[] segment = new GameObject[7];
    private static int shown = -1;

    // 막대 일곱 개: 0 위, 1 오른쪽 위, 2 오른쪽 아래, 3 아래, 4 왼쪽 아래, 5 왼쪽 위, 6 가운데
    private static readonly bool[][] Digits =
    {
        new[] { false, false, false, false, false, false, false },   // 0 = 안 보임
        new[] { false, true,  true,  false, false, false, false },   // 1
        new[] { true,  true,  false, true,  true,  false, true  },   // 2
        new[] { true,  true,  true,  true,  false, false, true  },   // 3
    };

    public static void Create(Transform head)
    {
        Dispose();
        if (head == null) return;

        GameObject go = new GameObject("OptionBadge");
        root = go.transform;
        root.SetParent(head, false);
        root.localPosition = new Vector3(0f, Drop, Dist);
        root.localRotation = Quaternion.identity;

        Material back = ToolModels.MakeGlow(new Color(0.09f, 0.10f, 0.13f));
        Material lit = ToolModels.MakeGlow(new Color(1.00f, 0.72f, 0.25f));

        // 바탕판. 숫자보다 2 mm 뒤(눈에서 먼 쪽)에 둔다.
        Bar(back, new Vector3(0f, 0f, 0.002f),
            new Vector3(W + 0.014f, H + 0.014f, 0.001f), "Back");

        float x = W * 0.5f, y = H * 0.5f, q = H * 0.25f;
        segment[0] = Bar(lit, new Vector3(0f,  y, 0f), new Vector3(W + T, T, 0.001f), "Top");
        segment[1] = Bar(lit, new Vector3( x,  q, 0f), new Vector3(T, y + T, 0.001f), "TopRight");
        segment[2] = Bar(lit, new Vector3( x, -q, 0f), new Vector3(T, y + T, 0.001f), "BottomRight");
        segment[3] = Bar(lit, new Vector3(0f, -y, 0f), new Vector3(W + T, T, 0.001f), "Bottom");
        segment[4] = Bar(lit, new Vector3(-x, -q, 0f), new Vector3(T, y + T, 0.001f), "BottomLeft");
        segment[5] = Bar(lit, new Vector3(-x,  q, 0f), new Vector3(T, y + T, 0.001f), "TopLeft");
        segment[6] = Bar(lit, new Vector3(0f, 0f, 0f), new Vector3(W + T, T, 0.001f), "Middle");

        shown = -1;
        Show(0);
    }

    /// <summary>숫자를 바꾼다. 1~3 만 그리고, 그 밖의 값이면 통째로 감춘다.</summary>
    public static void Show(int number)
    {
        if (root == null) return;
        if (number < 1 || number > 3) number = 0;
        if (number == shown) return;
        shown = number;

        root.gameObject.SetActive(number != 0);
        bool[] on = Digits[number];
        for (int i = 0; i < segment.Length; i++)
            if (segment[i] != null) segment[i].SetActive(on[i]);
    }

    public static void Dispose()
    {
        if (root != null) Kill(root.gameObject);
        root = null;
        shown = -1;
        for (int i = 0; i < segment.Length; i++) segment[i] = null;
    }

    private static GameObject Bar(Material mat, Vector3 pos, Vector3 size, string name)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Kill(go.GetComponent<Collider>());     // 물리를 안 쓴다
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    private static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}

using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 바리깡과 치과 핸드피스를 Unity 기본 도형으로 조립한다.
///
/// 왜 모델을 안 받고 만드나 — 무료·무로그인으로 받을 수 있는 CC0 바리깡/치과 드릴이 없다.
/// poly.pizza 에는 아예 없고, Sketchfab·CGTrader 는 무료 건도 로그인을 요구한다.
/// 그리고 이 둘은 거울 속에서 가까이 보이므로 텍스처가 아니라 실루엣이 전부다.
/// 직접 만들면 실루엣을 정확히 맞출 수 있고 라이선스도 걸리지 않는다.
///
/// 좌표 약속 — 둘 다 원점이 "닿는 지점" 이고 +Z 가 닿는 방향이다.
/// 그래서 머리에 붙일 때 위치만 잡고 LookAt 하면 끝난다. 오프셋 보정이 필요 없다.
///
/// 런타임과 에디터 양쪽에서 쓸 수 있다 (ToolPreview 가 에디터에서 렌더해 본다).
/// </summary>
public static class ToolModels
{
    private static Shader cachedShader;

    /// <summary>
    /// 플레이 중이 아닐 때 Object.Destroy 를 부르면 에러만 나고 실제로 안 지워진다.
    /// 프리뷰를 에디터에서 돌리므로 둘을 갈라 준다.
    /// </summary>
    private static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }

    /// <summary>
    /// 런타임에 만드는 도형이 쓸 셰이더.
    ///
    /// 처음엔 CreatePrimitive 가 붙여 주는 머티리얼의 셰이더를 그대로 썼다. 에디터에서는
    /// URP/Lit 이 나와서 멀쩡했는데 **아이폰 빌드에서는 화면이 통째로 분홍색**이었다.
    /// 이 프로젝트는 URP 를 QualitySettings 의 품질 단계에만 걸어 두고 Graphics 의
    /// 기본 파이프라인은 비워 뒀는데, 그러면 플레이어에서 CreatePrimitive 가
    /// **내장 Standard** 머티리얼을 돌려준다. Standard 는 URP 가 그릴 줄 모르니
    /// 오류 셰이더(분홍색)로 대체된다. 에디터는 품질 단계를 보고 URP 를 넘겨줘서
    /// 이 차이가 안 드러난다.
    ///
    /// 그래서 Resources 에 URP/Lit 머티리얼을 하나 넣어 두고 그 셰이더를 쓴다.
    /// Resources 밑에 있으면 아무도 안 쓰더라도 빌드에서 빠지지 않는다.
    /// </summary>
    private static Shader BaseShader()
    {
        if (cachedShader != null) return cachedShader;

        Material baseMat = Resources.Load<Material>("Materials/RuntimeBase");
        if (baseMat != null && baseMat.shader != null) cachedShader = baseMat.shader;

        if (cachedShader == null) cachedShader = Shader.Find("Universal Render Pipeline/Lit");
        if (cachedShader == null) cachedShader = Shader.Find("Standard");

        if (cachedShader == null)
        {
            // 마지막 수단. 에디터에서는 이걸로도 맞는 셰이더가 나온다.
            GameObject probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            probe.SetActive(false);        // Destroy 는 프레임 끝에 처리된다
            cachedShader = probe.GetComponent<Renderer>().sharedMaterial.shader;
            Kill(probe);
        }
        return cachedShader;
    }

    /// <summary>지금 잡힌 셰이더 이름. 폰에서 또 분홍색이 나면 이걸 화면에 찍어 본다.</summary>
    public static string BaseShaderName
    {
        get { Shader sh = BaseShader(); return sh != null ? sh.name : "(none)"; }
    }

    /// <summary>다른 스크립트도 같은 셰이더로 머티리얼을 만들 수 있게 열어 둔다.</summary>
    public static Material MakeMaterial(Color color, float metallic, float smoothness)
    {
        return Mat(color, metallic, smoothness);
    }

    private static Material Mat(Color color, float metallic, float smoothness)
    {
        Material m = new Material(BaseShader());
        // 색과 반질거림의 프로퍼티 이름이 파이프라인마다 다르다. URP/Lit 은
        // _BaseColor 와 _Smoothness, 내장 Standard 는 _Color 와 _Glossiness 다.
        // 어느 쪽이 잡히든 맞게 양쪽을 다 넣는다.
        m.color = color;
        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color"))      m.SetColor("_Color", color);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    private static Transform Part(Transform parent, PrimitiveType type, Material mat,
                                  Vector3 pos, Quaternion rot, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(type);

        // 콜라이더는 떼어 낸다. 물리를 안 쓰는데 도형마다 하나씩 붙으면 낭비다.
        Collider col = go.GetComponent<Collider>();
        Kill(col);

        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    /// <summary>
    /// Unity 기본 Cylinder 는 반지름 0.5, 높이 2, 축이 Y 다.
    /// 반지름 r / 길이 L 로 만들려면 스케일이 (2r, L/2, 2r) 이어야 한다.
    /// </summary>
    private static Vector3 CylScale(float radius, float length)
    {
        return new Vector3(radius * 2f, length * 0.5f, radius * 2f);
    }

    /// <summary>
    /// 바리깡. 원점은 날 면, +Z 가 머리에 닿는 방향이다. 전체 길이 약 15cm.
    ///
    /// 처음엔 직육면체 하나에 빗살을 붙였는데 벽돌에 서랍 달린 모양이 나왔다. 두 군데 고쳤다.
    ///   ① 몸통을 Capsule 두 개로 겹친다. 앞으로 갈수록 가늘어지고 실루엣이 둥글어진다.
    ///   ② 날은 원점에 수직으로 두고 몸통만 15도 눕힌다. 실제로 바리깡을 머리에 댈 때
    ///      손잡이가 비스듬히 뻗는 모양이고, 덕분에 씬에서 위치만 잡아도 날이 살에 붙는다.
    ///
    /// 빗살은 그대로 둔다 — 이게 없으면 그냥 검은 막대라 뭔지 알아볼 수 없다.
    /// </summary>
    public static GameObject BuildClipper(string name = "Clipper")
    {
        GameObject root = new GameObject(name);
        Transform t = root.transform;

        // 몸통은 플라스틱이다. metallic 을 0 으로 둬야 주변 반사에 허옇게 뜨지 않는다.
        Material body   = Mat(new Color(0.15f, 0.16f, 0.18f), 0.00f, 0.30f);
        Material steel  = Mat(new Color(0.74f, 0.76f, 0.80f), 0.85f, 0.70f);
        Material accent = Mat(new Color(0.42f, 0.44f, 0.48f), 0.10f, 0.45f);

        Quaternion none = Quaternion.identity;

        // --- 날 쪽. 원점에 수직으로 붙는다 ---

        const int teeth = 11;
        const float combWidth = 0.044f;
        float pitch = combWidth / teeth;
        for (int i = 0; i < teeth; i++)
        {
            float x = -combWidth * 0.5f + pitch * (i + 0.5f);
            Part(t, PrimitiveType.Cube, steel,
                 new Vector3(x, 0f, -0.008f), none,
                 new Vector3(pitch * 0.55f, 0.0035f, 0.016f));
        }

        // 날 블록 — 몸통 앞머리보다 넓다. 실제 바리깡도 날이 제일 넓은 부분이다.
        Part(t, PrimitiveType.Cube, steel,
             new Vector3(0f, 0f, -0.026f), none,
             new Vector3(0.046f, 0.018f, 0.024f));

        // --- 몸통. 날 뒤에서 15도 눕혀 뻗는다 ---

        const float bodyTilt = 15f;
        Vector3 dir  = Quaternion.Euler(bodyTilt, 0f, 0f) * Vector3.back;   // 뒤쪽 위로
        Vector3 side = Quaternion.Euler(bodyTilt, 0f, 0f) * Vector3.up;     // dir 에 수직
        Quaternion rot = Quaternion.FromToRotation(Vector3.up, dir);

        Vector3 neck = new Vector3(0f, 0f, -0.038f);        // 날 블록 뒷면

        // Capsule 은 로컬 높이가 2 다. 길이 L 을 내려면 scale.y 가 L/2 여야 한다.
        const float frontLen = 0.052f;
        Part(t, PrimitiveType.Capsule, body,
             neck + dir * (frontLen * 0.5f), rot,
             new Vector3(0.046f, frontLen * 0.5f, 0.037f));

        // 두 캡슐은 지름이 거의 같아야 한 덩어리로 보인다. 차이가 크면 뒤쪽 캡슐의
        // 둥근 앞머리가 앞쪽 캡슐 옆면을 뚫고 나와 테두리가 생긴다.
        // 겹침도 캡슐 끝의 둥근 부분보다 넉넉히 길어야 한다.
        const float rearLen = 0.092f;
        const float overlap = 0.042f;
        Part(t, PrimitiveType.Capsule, body,
             neck + dir * (frontLen - overlap + rearLen * 0.5f), rot,
             new Vector3(0.049f, rearLen * 0.5f, 0.040f));

        // 슬라이드 스위치 — 전동 공구로 읽히게 하는 단서
        Part(t, PrimitiveType.Capsule, accent,
             neck + dir * 0.055f + side * 0.020f, rot,
             new Vector3(0.013f, 0.011f, 0.009f));

        return root;
    }

    /// <summary>
    /// 치과 핸드피스(콘트라앵글). 원점은 버(bur) 끝, +Z 가 입에 닿는 방향이다.
    ///
    /// 전동 드릴과 구분되는 건 두 가지다 — 꺾인 목, 그리고 머리에서 튀어나온 아주 얇은 버.
    /// 이게 없으면 그냥 공구용 드릴로 보인다.
    /// </summary>
    public static GameObject BuildHandpiece(string name = "Handpiece")
    {
        GameObject root = new GameObject(name);
        Transform t = root.transform;

        Material steel = Mat(new Color(0.80f, 0.82f, 0.85f), 0.75f, 0.75f);
        Material dark  = Mat(new Color(0.45f, 0.47f, 0.50f), 0.90f, 0.60f);
        Material grip  = Mat(new Color(0.30f, 0.32f, 0.35f), 0.20f, 0.40f);

        // 기본 Cylinder 의 축은 Y 다. 눕혀야 한다.
        Quaternion alongZ = Quaternion.FromToRotation(Vector3.up, Vector3.forward);

        // 버 — 지름 1.8mm. 얇을수록 치과처럼 보인다
        Part(t, PrimitiveType.Cylinder, dark,
             new Vector3(0f, 0f, -0.0065f), alongZ, CylScale(0.0009f, 0.013f));

        // 머리 — 버가 박혀 있는 작은 드럼
        Part(t, PrimitiveType.Cylinder, steel,
             new Vector3(0f, 0f, -0.019f), alongZ, CylScale(0.0055f, 0.013f));

        // 목과 몸통은 뒤쪽 위로 20도 꺾인다
        const float neckAngle = 20f;
        Vector3 shaftDir = Quaternion.Euler(neckAngle, 0f, 0f) * Vector3.back;
        Quaternion alongShaft = Quaternion.FromToRotation(Vector3.up, shaftDir);

        Vector3 neckStart = new Vector3(0f, 0f, -0.025f);
        const float neckLen = 0.028f;
        Part(t, PrimitiveType.Cylinder, steel,
             neckStart + shaftDir * (neckLen * 0.5f), alongShaft, CylScale(0.0045f, neckLen));

        Vector3 gripStart = neckStart + shaftDir * neckLen;
        const float gripLen = 0.085f;
        Part(t, PrimitiveType.Cylinder, steel,
             gripStart + shaftDir * (gripLen * 0.5f), alongShaft, CylScale(0.0075f, gripLen));

        // 손잡이 홈 세 줄 — 매끈한 원통만 있으면 펜처럼 보인다
        for (int i = 0; i < 3; i++)
        {
            Part(t, PrimitiveType.Cylinder, grip,
                 gripStart + shaftDir * (0.015f + 0.010f * i), alongShaft,
                 CylScale(0.0079f, 0.002f));
        }

        // 호스가 붙는 뒤쪽 커넥터
        Part(t, PrimitiveType.Cylinder, dark,
             gripStart + shaftDir * (gripLen + 0.006f), alongShaft, CylScale(0.0080f, 0.012f));

        return root;
    }
}

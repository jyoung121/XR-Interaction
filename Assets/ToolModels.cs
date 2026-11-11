using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 실험에 쓰는 소품을 Unity 기본 도형으로 조립한다 —
/// 치과 핸드피스 / 전동칫솔 / 젤리.
///
/// 왜 모델을 안 받고 만드나 — 무료·무로그인으로 받을 수 있는 CC0 치과 드릴이 없다.
/// poly.pizza 에는 아예 없고, Sketchfab·CGTrader 는 무료 건도 로그인을 요구한다.
/// 그리고 이것들은 거울 속에서 가까이 보이므로 텍스처가 아니라 실루엣이 전부다.
/// 직접 만들면 실루엣을 정확히 맞출 수 있고 라이선스도 걸리지 않는다.
///
/// 좌표 약속 — 입에 닿는 도구는 원점이 "닿는 지점" 이고 +Z 가 닿는 방향이다.
/// 그래서 머리에 붙일 때 위치만 잡고 LookAt 하면 끝난다. 오프셋 보정이 필요 없다.
/// 음식은 손에 들려 입으로 가는 것이라 원점이 한가운데다.
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
        return Mat(color, metallic, smoothness, null);
    }

    /// <summary>
    /// 무늬가 있는 모델용. 받아온 음식 모델 몇 개는 색이 .mtl 의 Kd 가 아니라
    /// **텍스처**에 들어 있다 — 박하사탕은 Kd 가 검정이라 텍스처를 안 넘기면
    /// 새까만 돌이 된다.
    /// </summary>
    public static Material MakeMaterial(Color color, float metallic,
                                        float smoothness, Texture tex)
    {
        return Mat(color, metallic, smoothness, tex);
    }

    private static Material Mat(Color color, float metallic, float smoothness,
                                Texture tex = null)
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
        // 무늬 이름도 파이프라인마다 다르다. URP/Lit 은 _BaseMap,
        // 내장 Standard 는 _MainTex 다.
        if (tex != null)
        {
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        }
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
    /// 무른 음식 — 젤리 한 알. 원점은 한가운데다.
    ///
    /// 음식 시나리오의 두 수준이 사과(단단) / 젤리(무름) 다. 쓸 만한 무료 모델이 없어
    /// 기본 도형으로 만든다(핸드피스와 같은 사정).
    ///
    /// 사과와 **눈으로 바로** 구분돼야 조건이 조건 노릇을 한다. 세 가지를 달리했다 —
    ///   ① 곰 모양. 사과는 구라서 실루엣부터 갈린다 — 그리고 "젤리"라고 하면
    ///      누구나 떠올리는 게 이 모양이라, 보자마자 무른 것으로 읽힌다.
    ///   ② 주황. 사과의 빨강과 색으로도 갈린다.
    ///   ③ smoothness 를 아주 높게. 젤리는 단단한 반들거림이 아니라 물기 있는
    ///      윤기로 읽혀야 해서, 빛을 넓게 머금는 쪽으로 민다.
    ///
    /// 한때 빵으로 만들었다가 젤리로 바꿨다. 빵은 베어 무는 것이지 씹어 뭉개는 것이
    /// 아니라서, 무른 쪽 자극으로 쓰기에는 사과와 먹는 동작이 너무 비슷했다.
    /// </summary>
    public static GameObject BuildJelly(string name = "Jelly")
    {
        GameObject root = new GameObject(name);
        Transform t = root.transform;

        // 육면체 → 눌린 구 → **곰** 순으로 왔다. 덩어리는 아무리 윤을 내도
        // "무슨 음식인지" 가 안 읽혔다. 곰 모양이면 설명이 필요 없다.
        //
        // 그라데이션 — URP/Lit 은 정점 색을 안 읽고, 셰이더를 따로 쓰자고
        // 이것 하나 때문에 파이프라인을 건드릴 수는 없다. 그래서 **조각마다
        // 제 높이에 맞는 색**을 준다. 조각이 열 몇 개라 이음매가 안 보이고,
        // 발끝 짙은 적갈색에서 귀 끝 밝은 호박색까지 이어진 것으로 읽힌다.
        // 통통하게 잡는다. 실제 하리보 곰 비율(홀쭉함)로 만들었더니 손에
        // 쥐었을 때 손가락 사이에 묻혀 거울에서 점으로만 보였다.
        const float h = 0.034f;          // 발끝 ~ 귀끝
        const float w = 0.030f;          // 몸통 폭

        // 윤기. 젤리의 젖은 느낌은 거의 이것이 만든다. 다만 0.9 까지 올렸더니
        // 표면이 주변(어두운 하늘)만 비춰서 **조각마다 준 색이 거의 안 보였다**
        // — 그라데이션이 통째로 죽었다. 젖은 느낌이 남는 선까지만 내린다.
        const float gloss = 0.68f;

        Part(t, PrimitiveType.Sphere, GummyMat(0.00f, gloss),     // 다리
             new Vector3(-w * 0.30f, -h * 0.34f, 0f), Quaternion.identity,
             new Vector3(w * 0.42f, h * 0.26f, w * 0.46f));
        Part(t, PrimitiveType.Sphere, GummyMat(0.00f, gloss),
             new Vector3(w * 0.30f, -h * 0.34f, 0f), Quaternion.identity,
             new Vector3(w * 0.42f, h * 0.26f, w * 0.46f));

        Part(t, PrimitiveType.Sphere, GummyMat(0.22f, gloss),     // 몸통
             new Vector3(0f, -h * 0.10f, 0f), Quaternion.identity,
             new Vector3(w * 0.84f, h * 0.44f, w * 0.68f));

        Part(t, PrimitiveType.Sphere, GummyMat(0.32f, gloss),     // 팔
             new Vector3(-w * 0.50f, -h * 0.02f, 0f), Quaternion.identity,
             new Vector3(w * 0.34f, h * 0.22f, w * 0.40f));
        Part(t, PrimitiveType.Sphere, GummyMat(0.32f, gloss),
             new Vector3(w * 0.50f, -h * 0.02f, 0f), Quaternion.identity,
             new Vector3(w * 0.34f, h * 0.22f, w * 0.40f));

        Part(t, PrimitiveType.Sphere, GummyMat(0.78f, gloss),     // 머리
             new Vector3(0f, h * 0.24f, 0f), Quaternion.identity,
             new Vector3(w * 0.78f, h * 0.36f, w * 0.72f));

        // 주둥이. 머리보다 조금 앞으로 나와야 곰 얼굴이 된다.
        Part(t, PrimitiveType.Sphere, GummyMat(0.70f, gloss),
             new Vector3(0f, h * 0.16f, w * 0.30f), Quaternion.identity,
             new Vector3(w * 0.36f, h * 0.16f, w * 0.30f));

        // 귀는 머리에 **파묻히게** 붙인다. 띄우면 귀가 아니라 머리 옆에 붙은
        // 방울 두 개다 — 실제로 그렇게 났었다.
        Part(t, PrimitiveType.Sphere, GummyMat(1.00f, gloss),
             new Vector3(-w * 0.33f, h * 0.38f, 0f), Quaternion.identity,
             new Vector3(w * 0.27f, h * 0.15f, w * 0.26f));
        Part(t, PrimitiveType.Sphere, GummyMat(1.00f, gloss),
             new Vector3(w * 0.33f, h * 0.38f, 0f), Quaternion.identity,
             new Vector3(w * 0.27f, h * 0.15f, w * 0.26f));

        // 눈·코. 하리보 곰은 이 세 점이 눌려 찍혀 있다. 색만 짙게 준다.
        Material press = Mat(new Color(0.42f, 0.09f, 0.04f), 0f, 0.60f);
        // 머리에 반쯤 파묻는다. 띄우면 눌러 찍은 자국이 아니라 붙여 놓은
        // 구슬이 된다.
        Part(t, PrimitiveType.Sphere, press,
             new Vector3(-w * 0.20f, h * 0.27f, w * 0.26f), Quaternion.identity,
             Vector3.one * (w * 0.10f));
        Part(t, PrimitiveType.Sphere, press,
             new Vector3(w * 0.20f, h * 0.27f, w * 0.26f), Quaternion.identity,
             Vector3.one * (w * 0.10f));
        Part(t, PrimitiveType.Sphere, press,
             new Vector3(0f, h * 0.18f, w * 0.38f), Quaternion.identity,
             new Vector3(w * 0.13f, w * 0.09f, w * 0.08f));

        return root;
    }

    /// <summary>
    /// 젤리 곰의 그라데이션 한 칸. k = 0 이 발끝(짙은 적갈색),
    /// 1 이 귀끝(밝은 호박색)이다.
    /// </summary>
    private static Material GummyMat(float k, float smoothness)
    {
        // 양 끝을 멀리 벌려 놓는다. 가까운 두 색으로는 조각 열 몇 개에
        // 나눠 놓는 순간 그라데이션이 안 보였다 — 그냥 한 가지 빨강이었다.
        Color low  = new Color(0.88f, 0.07f, 0.02f);   // 발끝 — 짙은 적색
        Color high = new Color(1.00f, 0.90f, 0.32f);   // 귀끝 — 밝은 호박색
        return Mat(Color.Lerp(low, high, Mathf.Clamp01(k)), 0f, smoothness);
    }

    /// <summary>
    /// 칫솔 — **지금은 받아온 모델을 못 찾았을 때의 대비책이다.**
    /// 평소에 쓰는 건 `MirrorRig.BuildBrush` 가 띄우는 `Models/Toothbrush`.
    ///
    /// 여기서 쌓은 쪽은 솔이 정육면체 아홉 개라 거울에서 지우개처럼 보였다.
    /// 머리를 동그랗게 하고 색 띠와 전원 버튼을 달아 전동칫솔로 읽히게 해
    /// 놨는데, 솔이 **앞을 보고 있어서** 이를 닦는 게 아니라 쿡 찌르는
    /// 모양이 된다. 받아온 모델은 솔이 손잡이와 직각이라 그 문제가 없다.
    ///
    /// 원점은 솔 끝, +Z 가 입에 닿는 방향이다 (핸드피스와 같은 약속).
    ///
    /// 치과 시나리오의 두 수준이 드릴 / 칫솔이다. 둘 다 입에 닿는 공구지만
    /// 사람이 기대하는 느낌이 정반대라서 — 드릴은 날카롭고 무섭고, 칫솔은
    /// 둔하고 익숙하다 — 같은 진동을 줘도 현실감 평정이 갈릴 것이라 보고 고른 짝이다.
    /// </summary>
    public static GameObject BuildToothbrush(string name = "Toothbrush")
    {
        GameObject root = new GameObject(name);
        Transform t = root.transform;

        Material handle  = Mat(new Color(0.93f, 0.94f, 0.96f), 0f, 0.45f);
        Material accent  = Mat(new Color(0.22f, 0.60f, 0.78f), 0f, 0.40f);
        Material bristle = Mat(new Color(0.97f, 0.97f, 0.93f), 0f, 0.20f);

        Quaternion alongZ = Quaternion.FromToRotation(Vector3.up, Vector3.forward);

        // 솔. 다발 하나로 두면 고무 지우개처럼 보인다 — 여러 개를 심어야 솔이다.
        for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j++)
                Part(t, PrimitiveType.Cube, bristle,
                     new Vector3(i * 0.0046f, j * 0.0046f, -0.0035f), Quaternion.identity,
                     new Vector3(0.0036f, 0.0036f, 0.0072f));

        // 동그란 헤드
        Part(t, PrimitiveType.Cylinder, handle,
             new Vector3(0f, 0f, -0.0115f), alongZ, CylScale(0.0088f, 0.010f));

        // 목 — 헤드보다 확연히 가늘어야 목으로 읽힌다
        Part(t, PrimitiveType.Cylinder, handle,
             new Vector3(0f, 0f, -0.027f), alongZ, CylScale(0.0046f, 0.023f));

        // 몸통
        const float bodyLen = 0.080f;
        Part(t, PrimitiveType.Cylinder, handle,
             new Vector3(0f, 0f, -0.0385f - bodyLen * 0.5f), alongZ,
             CylScale(0.0106f, bodyLen));

        // 색 띠와 전원 버튼 — 전동이라는 단서
        Part(t, PrimitiveType.Cylinder, accent,
             new Vector3(0f, 0f, -0.055f), alongZ, CylScale(0.0109f, 0.009f));
        Part(t, PrimitiveType.Sphere, accent,
             new Vector3(0f, 0.0098f, -0.070f), Quaternion.identity,
             new Vector3(0.0095f, 0.0042f, 0.0095f));

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

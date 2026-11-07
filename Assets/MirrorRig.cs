using UnityEngine;

/// <summary>
/// 거울 방 하나를 런타임에 조립한다.
///
/// 참가자는 1인칭으로 앉아 있고, 앞의 거울에 비친 자기 머리에 물체가 닿는 걸 본다.
/// 교수님이 제기한 "머리에 닿는 것 vs 입에 닿는 것" 을 재려면 접촉 지점이 보여야 하는데,
/// 순수 1인칭에서는 둘 다 시야 밖이라 거울이 필요하다.
///
/// RenderTexture 거울을 쓰지 않는다. Cardboard 는 양안이라 반사 카메라를 눈마다 따로
/// 만들어야 하고, 그래도 입체감이 어긋나기 쉽다. 대신 거울 뒤에 방을 한 벌 더 짓고
/// 머리를 실제로 놓은 뒤, 참가자 머리 움직임을 거울면 기준으로 반사시켜 따라가게 한다.
/// 진짜 지오메트리라 입체감이 저절로 맞는다.
///
/// 좌표 — 리그 자신이 참가자 눈에 놓이고 +Z 가 거울 쪽이다. 덕분에 거울면은 z = D 이고
/// 반사는 z → 2D - z 한 줄로 끝난다.
///
/// 씬 파일을 건드리지 않는다. 조원이 Assets 를 통째로 덮어도 충돌할 것이 없다.
/// </summary>
public class MirrorRig : MonoBehaviour
{
    public enum Scenario { None, Clipper, Food, Dental }

    // ───────────────────────── 치수 (m) ─────────────────────────

    // 거울까지의 거리가 이 설계에서 제일 민감한 값이다. 거울이 멀면 반사는 그 두 배
    // 거리라서 머리가 급격히 작아지고 접촉 지점이 안 보인다. 0.7m 로 지었더니 머리가
    // 9도밖에 안 돼서 못 쓸 수준이었다. 0.45m 면 반사가 0.9m 라 14도쯤 되고,
    // VR 에서 눈이 편한 거리(0.5m 이상)도 지킨다. 세면대 거울 정도의 거리다.
    private const float D           = 0.45f;   // 눈에서 거울면까지
    private const float CeilUp      = 0.85f;   // 눈 위 천장까지
    private const float RoomHalfW   = 1.60f;
    private const float RoomDepth   = 2.40f;
    private const float NeckDrop    = 0.10f;   // 눈에서 목 관절까지. 고개는 여기를 축으로 돈다

    // 치과 거울을 머리 위로 올리는 각. 누운 환자 위에 치료등이 달려 있는 모습이다.
    // 크게 줄수록 "누워 있다" 가 분명해지지만 목을 그만큼 젖히고 있어야 한다.
    private const float DentalPitch = 35f;

    // 바닥 높이는 상수로 둘 수 없다 — 거울 속 사람의 발이 바닥에 닿아야 하는데
    // 그 키는 모델이 정한다. 전신 거울을 쓰면 발까지 보이므로 어긋나면 바로 드러난다.
    // BuildHead 에서 뼈로 재서 넣는다.
    private float eyeHeight = 1.60f;

    private const float ApproachTime = 1.2f;
    private const float HandScale    = 0.62f;   // 손이 커서 줄인다
    private const float HandGrip     = 0f;      // 고리 한가운데에서 더 띄울 여유   // 물체가 들어와 닿기까지

    // ───────────────────────── 상태 ─────────────────────────

    private Transform viewer;
    private Transform charRoot;       // 거울 속 사람. 통째로 민다
    private Transform headBone;       // 목 위. 고개는 이 뼈만 돈다
    private Quaternion restHeadRot;   // 리그 기준 머리뼈의 기본 자세
    private Transform headPivot;      // 머리뼈에 매달린 축. 도구 좌표의 기준
    private Transform toolRoot;
    private GameObject clipper;
    private GameObject handpiece;
    private GameObject food;
    private TextMesh label;
    private float labelUntil;

    private Scenario current = Scenario.None;
    private float elapsed;

    private UnityEngine.Rendering.AmbientMode prevAmbientMode;
    private Color prevAmbientLight;
    private Material prevSkybox;
    private bool envSaved;

    private Vector3 templeLocal;      // headPivot 로컬 — 관자놀이
    private Vector3 mouthLocal;       // headPivot 로컬 — 입
    private Vector3 headCenterLocal;
    private float headUnit;          // 목관절~정수리. 머리 크기의 기준자

    private Transform mouthLips;     // 입술 테두리
    private Transform mouthHole;     // 벌어진 틈
    private Transform mouthTeeth;    // 윗니

    private Limb selfArm;            // 참가자 팔 — 사과는 제 손으로 든다
    private Actor barber;            // 미용사
    private Actor dentist;           // 치과의
    private Transform collar;        // 목에 두른 천의 깃
    private Color shirtColor = Color.grey;   // 참가자 상의 원래 색

    private Transform[] rooms;       // 시나리오별 방. 켜고 끈다
    private Quaternion baseRot;      // 리그를 만들 때의 방향. 치과에서 여기서 젖힌다
    private MirrorSpec spec;         // 지금 켜져 있는 거울

    /// <summary>
    /// 지금 도구가 살에 닿아 있는 정도 0~1. 진동 세기를 여기에 물린다.
    ///
    /// 거리가 아니라 접촉으로 움직인다는 게 중요하다. 모터가 얼굴에 하나뿐이라
    /// "멀면 약하게" 는 거리로 안 느껴지고 그냥 세기가 오르내리는 걸로만 느껴진다.
    /// 닿기 전에는 0 이다 — 실제로도 닿기 전엔 아무것도 안 느껴지고,
    /// 이 연구가 현실감을 재는 거라 거기서 어긋나면 안 된다.
    /// </summary>
    public float Contact { get; private set; }

    public Scenario Current { get { return current; } }
    public bool IsPlaying { get; private set; } = true;

    // ───────────────────────── 생성 ─────────────────────────

    public static MirrorRig Create(Transform viewer)
    {
        if (viewer == null) { Debug.LogError("[MirrorRig] viewer 가 없다"); return null; }

        GameObject go = new GameObject("MirrorRig");

        // 참가자가 지금 보고 있는 쪽으로 방을 짓는다. Cardboard 는 폰을 어느 방향으로
        // 들고 시작했는지에 따라 forward 가 제각각이라, 만드는 순간을 기준으로 잡는다.
        Vector3 fwd = Vector3.ProjectOnPlane(viewer.forward, Vector3.up);
        if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;

        go.transform.position = viewer.position;
        go.transform.rotation = Quaternion.LookRotation(fwd.normalized, Vector3.up);

        MirrorRig rig = go.AddComponent<MirrorRig>();
        rig.viewer = viewer;
        rig.Build();
        return rig;
    }

    public void Dispose()
    {
        RestoreEnvironment();
        Kill(gameObject);
    }

    /// <summary>
    /// 이 리그가 화면 전체를 책임지므로 주변광도 직접 정한다. 씬에 뭐가 설정돼 있든
    /// 같은 밝기로 나와야 파일럿에서 본 것과 본 실험에서 보는 것이 같다.
    /// 끝낼 때 원래대로 돌려놓는다 — 기존 탐색 실험 화면을 건드리면 안 된다.
    /// </summary>
    private void TakeOverEnvironment()
    {
        if (envSaved) return;
        prevAmbientMode = RenderSettings.ambientMode;
        prevAmbientLight = RenderSettings.ambientLight;
        prevSkybox = RenderSettings.skybox;
        envSaved = true;

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.34f, 0.34f, 0.36f);
        RenderSettings.skybox = null;      // 방 안이다. 하늘이 비치면 안 된다.
    }

    private void RestoreEnvironment()
    {
        if (!envSaved) return;
        RenderSettings.ambientMode = prevAmbientMode;
        RenderSettings.ambientLight = prevAmbientLight;
        RenderSettings.skybox = prevSkybox;
        envSaved = false;
    }

    private void Build()
    {
        TakeOverEnvironment();
        baseRot = transform.rotation;

        // 사람을 먼저 짓는다. 바닥 높이가 이 모델의 키에서 나오기 때문이다 —
        // 전신 거울을 쓰면 발까지 보이므로 바닥이 어긋나면 바로 드러난다.
        BuildHead();

        // 방은 상황마다 통째로 다르다. 셋을 미리 지어 두고 켜고 끈다.
        // 넘어갈 때마다 새로 지으면 그 순간 화면이 멎는다.
        rooms = new Transform[4];
        rooms[(int)Scenario.Clipper] = BuildBarberShop();
        rooms[(int)Scenario.Food]    = BuildHome();
        rooms[(int)Scenario.Dental]  = BuildClinic();
        for (int i = 0; i < rooms.Length; i++)
            if (rooms[i] != null) rooms[i].gameObject.SetActive(false);

        BuildTools();
        BuildCast();
        BuildLabel();
    }

    // ───────────────────────── 방 ─────────────────────────

    /// <summary>거울면 기준으로 z 를 뒤집는다. 거울 뒤쪽 방을 짓는 데 쓴다.</summary>
    private static float Z(float z, bool mirrored)
    {
        return mirrored ? 2f * D - z : z;
    }

    /// <summary>
    /// 거울 한 장의 모양. 세 상황이 저마다 다른 거울을 쓴다 —
    /// 미용실은 넓은 벽거울, 집은 전신 거울, 치과는 머리 위 둥근 거울이다.
    /// </summary>
    private struct MirrorSpec
    {
        public float w, h;        // 네모 거울
        public float radius;      // 0 보다 크면 둥근 거울 (w, h 는 쓰지 않는다)
        public float centerY;     // 눈높이에서 거울 한가운데까지
        public float pitch;       // 거울을 머리 위로 올리는 각

        public bool Round { get { return radius > 0f; } }
        public float Width  { get { return Round ? radius * 2f : w; } }
        public float Height { get { return Round ? radius * 2f : h; } }
    }

    private static MirrorSpec SpecFor(Scenario s)
    {
        MirrorSpec m = new MirrorSpec();
        switch (s)
        {
            case Scenario.Food:
                // 집 — 전신 거울. 세로로 길고 낮게 걸려서 고개를 숙이면 몸이 보인다.
                m.w = 0.40f; m.h = 1.02f; m.centerY = -0.18f;
                break;
            case Scenario.Dental:
                // 치과 — 치료등에 달린 둥근 거울. 누운 환자 머리 위에 있다.
                m.radius = 0.24f; m.pitch = DentalPitch;
                break;
            default:
                // 미용실 — 넓은 벽거울. 허리까지 넉넉히 들어온다.
                m.w = 0.52f; m.h = 0.74f; m.centerY = -0.12f;
                break;
        }
        return m;
    }

    private Transform Prim(Transform parent, PrimitiveType type, Material mat,
                           Vector3 pos, Quaternion rot, Vector3 size, string name)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;

        // 콜라이더는 떼어 낸다. 물리를 안 쓰는데 방 전체에 하나씩 붙으면 낭비다.
        Kill(go.GetComponent<Collider>());

        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = rot;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    private Transform Box(Transform root, Material mat, Vector3 center, Vector3 size, string name)
    {
        return Prim(root, PrimitiveType.Cube, mat, center, Quaternion.identity, size, name);
    }

    /// <summary>
    /// 소품 하나를 이쪽 방과 거울 속 방에 한 번씩 놓는다.
    ///
    /// 거울에 비치는 건 **참가자 뒤에 있는 것들**이다. 방 앞뒤가 똑같으면 거울이
    /// 그냥 벽에 파인 상자로 보여서, 뒤쪽 소품이 거울을 거울로 만든다.
    /// 거울상은 z 가 뒤집히므로 기울기의 x·y 성분도 같이 뒤집어야 한다.
    /// </summary>
    private void Both(Transform root, PrimitiveType type, Material mat,
                      Vector3 pos, Vector3 euler, Vector3 size, string name)
    {
        for (int i = 0; i < 2; i++)
        {
            bool m = i == 1;
            Prim(root, type, mat,
                 new Vector3(pos.x, pos.y, Z(pos.z, m)),
                 Quaternion.Euler(m ? -euler.x : euler.x, m ? -euler.y : euler.y, euler.z),
                 size, (m ? "M_" : "R_") + name);
        }
    }

    private void BothBox(Transform root, Material mat, Vector3 pos, Vector3 size, string name)
    {
        Both(root, PrimitiveType.Cube, mat, pos, Vector3.zero, size, name);
    }

    /// <summary>빈 방 껍데기. 거울면 양쪽에 한 벌씩 짓는다.</summary>
    private void RoomShell(Transform root, Material wall, Material floorMat, Material ceilMat)
    {
        float midY  = (CeilUp - eyeHeight) * 0.5f;
        float wallH = CeilUp + eyeHeight;
        float midZ  = D - RoomDepth * 0.5f;
        float farZ  = D - RoomDepth;

        for (int i = 0; i < 2; i++)
        {
            bool m = i == 1;
            string tag = m ? "M_" : "R_";

            Box(root, floorMat, new Vector3(0f, -eyeHeight - 0.05f, Z(midZ, m)),
                new Vector3(RoomHalfW * 2f, 0.1f, RoomDepth), tag + "Floor");

            Box(root, ceilMat, new Vector3(0f, CeilUp + 0.05f, Z(midZ, m)),
                new Vector3(RoomHalfW * 2f, 0.1f, RoomDepth), tag + "Ceiling");

            for (int s = -1; s <= 1; s += 2)
                Box(root, wall, new Vector3(s * (RoomHalfW + 0.05f), midY, Z(midZ, m)),
                    new Vector3(0.1f, wallH, RoomDepth), tag + "Side");

            Box(root, wall, new Vector3(0f, midY, Z(farZ - 0.05f, m)),
                new Vector3(RoomHalfW * 2f, wallH, 0.1f), tag + "End");
        }
    }

    /// <summary>방 뒤쪽 벽 안쪽 면의 z. 소품을 여기에 붙이면 거울에 비친다.</summary>
    private static float BackWall { get { return D - RoomDepth + 0.02f; } }

    /// <summary>
    /// 거울이 걸린 벽. 가운데를 뚫어야 하므로 판 하나가 아니라 위·아래·양옆 네 장이다.
    /// 그 구멍으로 거울 뒤에 지어 둔 방과 머리가 보인다.
    /// 둥근 거울이면 네모로 뚫은 구멍의 네 귀퉁이를 고리로 덮어 동그랗게 만든다.
    /// </summary>
    private void BuildMirrorWall(Transform root, Material wall, Material frame, MirrorSpec m)
    {
        float halfW = m.Width * 0.5f;
        float halfH = m.Height * 0.5f;
        float cy = m.centerY;
        // 벽이 두꺼우면 구멍 안쪽 면이 드러나 거울이 아니라 깊은 상자로 보인다.
        const float t = 0.02f;

        float aboveH = CeilUp - (cy + halfH);
        Box(root, wall, new Vector3(0f, cy + halfH + aboveH * 0.5f, D),
            new Vector3(RoomHalfW * 2f, aboveH, t), "WallTop");

        float belowH = eyeHeight + (cy - halfH);
        Box(root, wall, new Vector3(0f, cy - halfH - belowH * 0.5f, D),
            new Vector3(RoomHalfW * 2f, belowH, t), "WallBottom");

        float sideW = RoomHalfW - halfW;
        for (int s = -1; s <= 1; s += 2)
            Box(root, wall, new Vector3(s * (halfW + sideW * 0.5f), cy, D),
                new Vector3(sideW, m.Height, t), "WallSide");

        if (m.Round) RoundBezel(root, wall, frame, m);
        else
        {
            // 테두리 — 벽보다 조금 앞으로 나온다. 이게 있어야 창문이 아니라 거울로 보인다.
            const float fw = 0.035f;
            Box(root, frame, new Vector3(0f, cy + halfH + fw * 0.5f, D - 0.03f),
                new Vector3(m.w + fw * 2f, fw, 0.05f), "FrameTop");
            Box(root, frame, new Vector3(0f, cy - halfH - fw * 0.5f, D - 0.03f),
                new Vector3(m.w + fw * 2f, fw, 0.05f), "FrameBottom");
            for (int s = -1; s <= 1; s += 2)
                Box(root, frame, new Vector3(s * (halfW + fw * 0.5f), cy, D - 0.03f),
                    new Vector3(fw, m.h, 0.05f), "FrameSide");
        }
    }

    /// <summary>
    /// 둥근 거울. 기본 도형에는 고리가 없어서, 네모 구멍의 네 귀퉁이를 작은 판자
    /// 스물네 개로 둘러 덮는다. 안쪽 가장자리는 24각형이지만 이 거리에서는 원으로 보인다.
    /// 구멍 반지름이 r 이면 네 귀퉁이는 r√2 까지 뻗어 있으므로 그만큼은 덮어야 한다.
    /// </summary>
    private void RoundBezel(Transform root, Material wall, Material frame, MirrorSpec m)
    {
        const int seg = 24;
        float rIn = m.radius;
        float rOut = m.radius * 1.50f;
        float rMid = (rIn + rOut) * 0.5f;
        float tang = 2f * rOut * Mathf.Tan(Mathf.PI / seg) * 1.25f;   // 조금 겹치게

        for (int i = 0; i < seg; i++)
        {
            float a = 360f / seg * i;
            float rad = a * Mathf.Deg2Rad;
            Vector3 p = new Vector3(rMid * Mathf.Cos(rad), m.centerY + rMid * Mathf.Sin(rad), D);
            Prim(root, PrimitiveType.Cube, wall, p, Quaternion.Euler(0f, 0f, a),
                 new Vector3(rOut - rIn, tang, 0.02f), "Hole" + i);

            // 금속 테. 구멍 가장자리에 바짝 붙인다 — 앞으로 많이 빼면 갓과 따로
            // 놀아서 고리가 세 겹으로 보인다.
            Vector3 q = new Vector3((rIn + 0.016f) * Mathf.Cos(rad),
                                    m.centerY + (rIn + 0.016f) * Mathf.Sin(rad), D - 0.012f);
            Prim(root, PrimitiveType.Cube, frame, q, Quaternion.Euler(0f, 0f, a),
                 new Vector3(0.034f, tang * 0.62f, 0.028f), "Bezel" + i);
        }
    }

    /// <summary>
    /// 치료등 갓. 둥근 거울 테 바깥을 두르는 고리를 판자 스무 개로 만든다.
    /// 가운데가 비어 있어야 거울이 보인다.
    /// </summary>
    private void LampShade(Transform root, Material mat, MirrorSpec m, float z)
    {
        const int seg = 20;
        // 구멍 귀퉁이를 덮는 판이 r*1.50 까지 와 있다. 갓을 거기서 바로 이어
        // 붙여야 테·판·갓이 한 덩어리로 보인다.
        float rIn  = m.radius * 1.46f;
        float rOut = m.radius * 2.05f;
        float rMid = (rIn + rOut) * 0.5f;
        float tang = 2f * rOut * Mathf.Tan(Mathf.PI / seg) * 1.25f;

        for (int i = 0; i < seg; i++)
        {
            float a = 360f / seg * i;
            float rad = a * Mathf.Deg2Rad;
            Both(root, PrimitiveType.Cube, mat,
                 new Vector3(rMid * Mathf.Cos(rad), m.centerY + rMid * Mathf.Sin(rad), z),
                 new Vector3(0f, 0f, a),
                 new Vector3(rOut - rIn, tang, 0.09f), "Shade" + i);
        }
    }

    /// <summary>
    /// 거울이 거울로 보이려면 비친 쪽 조명이 이쪽 조명의 거울상이어야 한다.
    /// 방향광은 위치가 없어서 반사시킬 수가 없다 — 하나로 양쪽을 비추면
    /// 거울 속 얼굴만 뒤에서 빛을 받아 어두워진다. 그래서 점광만 쓰고,
    /// 같은 등을 거울면 기준으로 뒤집어 한 번 더 놓는다.
    /// </summary>
    private void Lamp(Transform root, Vector3 pos, float range, float intensity, Color color)
    {
        for (int i = 0; i < 2; i++)
        {
            bool mirrored = i == 1;
            GameObject go = new GameObject(mirrored ? "Lamp_m" : "Lamp");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(pos.x, pos.y, Z(pos.z, mirrored));
            Light l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = range;
            l.intensity = intensity;
            l.color = color;
            // 그림자는 끄다. 폰에서 점광 그림자는 비싸고, 가까운 등이
            // 치료등 갓의 그림자를 방 전체에 크게 드리워 얼룩처럼 보였다.
            l.shadows = LightShadows.None;
        }
    }

    /// <summary>
    /// 짝 없는 등. 거울 속에만 있는 빛이다 — 치료등처럼 한쪽만 비추는 조명이
    /// 그렇다. 이쪽 방을 비추는 데 쓰면 거울이 깨져 보이므로 z 는 D 보다 뒤여야 한다.
    /// </summary>
    private void OneLamp(Transform root, Vector3 pos, float range, float intensity, Color color)
    {
        GameObject go = new GameObject("Lamp_one");
        go.transform.SetParent(root, false);
        go.transform.localPosition = pos;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = range;
        l.intensity = intensity;
        l.color = color;
        l.shadows = LightShadows.None;
    }

    private Transform NewRoom(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    // ───────────────────────── 미용실 ─────────────────────────

    private Transform BuildBarberShop()
    {
        Transform r = NewRoom("Room_Barber");
        MirrorSpec m = SpecFor(Scenario.Clipper);

        Material wall  = ToolModels.MakeMaterial(new Color(0.30f, 0.34f, 0.35f), 0f, 0.10f);
        Material floor = ToolModels.MakeMaterial(new Color(0.23f, 0.22f, 0.21f), 0f, 0.18f);
        Material ceil  = ToolModels.MakeMaterial(new Color(0.42f, 0.42f, 0.42f), 0f, 0.06f);
        Material wood  = ToolModels.MakeMaterial(new Color(0.34f, 0.23f, 0.15f), 0f, 0.22f);
        Material chrome = ToolModels.MakeMaterial(new Color(0.72f, 0.73f, 0.75f), 0.8f, 0.72f);
        Material black = ToolModels.MakeMaterial(new Color(0.10f, 0.10f, 0.11f), 0f, 0.26f);

        RoomShell(r, wall, floor, ceil);
        BuildMirrorWall(r, wall, black, m);

        // 거울 밑 작업대. 미용실이라고 알아보게 하는 건 결국 이것과 병들이다.
        float deskY = m.centerY - m.h * 0.5f - 0.10f;
        BothBox(r, wood, new Vector3(0f, deskY, D - 0.13f),
                new Vector3(1.30f, 0.05f, 0.26f), "Desk");
        Color[] bottles = {
            new Color(0.78f, 0.74f, 0.34f), new Color(0.26f, 0.44f, 0.30f),
            new Color(0.70f, 0.31f, 0.26f), new Color(0.32f, 0.36f, 0.48f) };
        for (int i = 0; i < bottles.Length; i++)
            Both(r, PrimitiveType.Cylinder, ToolModels.MakeMaterial(bottles[i], 0f, 0.42f),
                 new Vector3(-0.44f + i * 0.17f, deskY + 0.08f, D - 0.14f), Vector3.zero,
                 new Vector3(0.055f, 0.055f, 0.055f), "Bottle" + i);

        // 이발 의자 등받이. 참가자 **뒤**에 둬야 거울에 비쳐 뒤로 보인다.
        BothBox(r, black, new Vector3(0f, -0.62f, -0.30f),
                new Vector3(0.50f, 0.58f, 0.11f), "ChairBack");
        Both(r, PrimitiveType.Cylinder, chrome, new Vector3(0f, -1.05f, -0.30f), Vector3.zero,
             new Vector3(0.07f, 0.22f, 0.07f), "ChairPost");

        // 뒷벽 — 거울에 비치는 배경. 삼색 등과 거울 하나, 수건 선반.
        Material red  = ToolModels.MakeMaterial(new Color(0.72f, 0.20f, 0.18f), 0f, 0.30f);
        Material white = ToolModels.MakeMaterial(new Color(0.88f, 0.87f, 0.84f), 0f, 0.30f);
        Material blue = ToolModels.MakeMaterial(new Color(0.20f, 0.33f, 0.62f), 0f, 0.30f);
        Material[] pole = { red, white, blue, red, white, blue };
        for (int i = 0; i < pole.Length; i++)
            Both(r, PrimitiveType.Cylinder, pole[i],
                 new Vector3(-1.08f, 0.42f - i * 0.11f, BackWall + 0.05f), new Vector3(0f, 0f, 18f),
                 new Vector3(0.10f, 0.055f, 0.10f), "Pole" + i);

        BothBox(r, black, new Vector3(0.52f, 0.16f, BackWall),
                new Vector3(0.62f, 0.78f, 0.03f), "BackMirrorFrame");
        BothBox(r, ToolModels.MakeMaterial(new Color(0.46f, 0.48f, 0.49f), 0.3f, 0.55f),
                new Vector3(0.52f, 0.16f, BackWall + 0.02f),
                new Vector3(0.54f, 0.70f, 0.02f), "BackMirrorGlass");

        BothBox(r, wood, new Vector3(-0.30f, -0.30f, BackWall + 0.09f),
                new Vector3(0.72f, 0.04f, 0.20f), "Shelf");
        for (int i = 0; i < 3; i++)
            BothBox(r, white, new Vector3(-0.52f + i * 0.21f, -0.24f, BackWall + 0.09f),
                    new Vector3(0.17f, 0.10f, 0.17f), "Towel" + i);

        Lamp(r, new Vector3(-0.55f, CeilUp - 0.25f, -0.70f), 4.2f, 1.55f,
             new Color(1f, 0.96f, 0.88f));
        Lamp(r, new Vector3(0.25f, 0.20f, -0.45f), 2.8f, 0.85f,
             new Color(1f, 0.95f, 0.88f));
        return r;
    }

    // ───────────────────────── 집 ─────────────────────────

    private Transform BuildHome()
    {
        Transform r = NewRoom("Room_Home");
        MirrorSpec m = SpecFor(Scenario.Food);

        Material wall  = ToolModels.MakeMaterial(new Color(0.76f, 0.71f, 0.63f), 0f, 0.06f);
        Material floor = ToolModels.MakeMaterial(new Color(0.47f, 0.33f, 0.21f), 0f, 0.20f);
        Material ceil  = ToolModels.MakeMaterial(new Color(0.84f, 0.82f, 0.78f), 0f, 0.04f);
        Material wood  = ToolModels.MakeMaterial(new Color(0.42f, 0.28f, 0.17f), 0f, 0.24f);
        Material cloth = ToolModels.MakeMaterial(new Color(0.55f, 0.34f, 0.30f), 0f, 0.08f);

        RoomShell(r, wall, floor, ceil);
        // 집 거울은 나무 테두리에 세로로 길다. 현관이나 방에 세워 두는 그 거울이다.
        BuildMirrorWall(r, wall, wood, m);

        // 거울 옆 서랍장 — 거울 앞을 막지 않게 옆으로 비켜 놓는다.
        BothBox(r, wood, new Vector3(0.92f, -eyeHeight + 0.38f, D - 0.22f),
                new Vector3(0.62f, 0.76f, 0.40f), "Dresser");
        BothBox(r, ToolModels.MakeMaterial(new Color(0.30f, 0.21f, 0.13f), 0f, 0.30f),
                new Vector3(0.92f, -eyeHeight + 0.52f, D - 0.43f),
                new Vector3(0.52f, 0.02f, 0.02f), "DresserHandle");

        // 뒷벽 — 거울에 비치는 집 배경. 액자 둘, 문틀, 화분.
        BothBox(r, wood, new Vector3(0.58f, 0.26f, BackWall),
                new Vector3(0.44f, 0.56f, 0.03f), "PictureFrame");
        BothBox(r, ToolModels.MakeMaterial(new Color(0.62f, 0.70f, 0.72f), 0f, 0.10f),
                new Vector3(0.58f, 0.26f, BackWall + 0.02f),
                new Vector3(0.37f, 0.48f, 0.02f), "Picture");
        BothBox(r, wood, new Vector3(0.58f, -0.34f, BackWall),
                new Vector3(0.30f, 0.34f, 0.03f), "PictureFrame2");
        BothBox(r, ToolModels.MakeMaterial(new Color(0.74f, 0.66f, 0.48f), 0f, 0.10f),
                new Vector3(0.58f, -0.34f, BackWall + 0.02f),
                new Vector3(0.24f, 0.27f, 0.02f), "Picture2");

        // 문. 집이라는 걸 한눈에 알려 주는 건 사실 이게 제일 크다.
        BothBox(r, ToolModels.MakeMaterial(new Color(0.86f, 0.84f, 0.80f), 0f, 0.12f),
                new Vector3(-0.95f, -eyeHeight + 1.02f, BackWall + 0.01f),
                new Vector3(0.82f, 2.04f, 0.04f), "Door");
        BothBox(r, wood, new Vector3(-0.95f, -eyeHeight + 1.02f, BackWall),
                new Vector3(0.92f, 2.14f, 0.03f), "DoorFrame");
        Both(r, PrimitiveType.Sphere, ToolModels.MakeMaterial(new Color(0.70f, 0.62f, 0.32f), 0.7f, 0.6f),
             new Vector3(-0.60f, -eyeHeight + 0.98f, BackWall + 0.04f), Vector3.zero,
             new Vector3(0.07f, 0.07f, 0.07f), "Knob");

        // 화분
        Both(r, PrimitiveType.Cylinder, ToolModels.MakeMaterial(new Color(0.55f, 0.33f, 0.24f), 0f, 0.18f),
             new Vector3(1.22f, -eyeHeight + 0.16f, BackWall + 0.26f), Vector3.zero,
             new Vector3(0.24f, 0.16f, 0.24f), "Pot");
        for (int i = 0; i < 3; i++)
            Both(r, PrimitiveType.Sphere, ToolModels.MakeMaterial(new Color(0.24f, 0.42f, 0.24f), 0f, 0.10f),
                 new Vector3(1.22f + (i - 1) * 0.10f, -eyeHeight + 0.44f + i * 0.08f, BackWall + 0.26f),
                 Vector3.zero, new Vector3(0.30f, 0.26f, 0.30f), "Leaf" + i);

        // 집은 등이 하나뿐이고 따뜻하다. 미용실·치과보다 어둑해야 집처럼 보인다.
        Lamp(r, new Vector3(-0.40f, CeilUp - 0.20f, -0.55f), 4.4f, 1.25f,
             new Color(1f, 0.92f, 0.78f));
        Lamp(r, new Vector3(0.95f, -0.25f, D - 0.40f), 1.9f, 0.70f,
             new Color(1f, 0.88f, 0.70f));
        return r;
    }

    // ───────────────────────── 치과 ─────────────────────────

    private Transform BuildClinic()
    {
        Transform r = NewRoom("Room_Clinic");
        MirrorSpec m = SpecFor(Scenario.Dental);

        // 이 방은 통째로 뒤로 젖혀진다(환자가 누워 있으므로). 그래서 바닥·천장이
        // 기울어 보이는데, 밋밋하고 밝은 상자로 두면 그 기울기가 눈에 안 띈다.
        // 대신 거울 둘레의 치료등과 머리맡 소품으로 치과라는 걸 알린다.
        Material wall  = ToolModels.MakeMaterial(new Color(0.80f, 0.83f, 0.84f), 0f, 0.05f);
        Material floor = ToolModels.MakeMaterial(new Color(0.70f, 0.72f, 0.73f), 0f, 0.10f);
        Material ceil  = ToolModels.MakeMaterial(new Color(0.90f, 0.92f, 0.93f), 0f, 0.03f);
        Material shell = ToolModels.MakeMaterial(new Color(0.92f, 0.93f, 0.94f), 0.1f, 0.42f);
        Material teal  = ToolModels.MakeMaterial(new Color(0.42f, 0.66f, 0.70f), 0f, 0.12f);
        Material steel = ToolModels.MakeMaterial(new Color(0.74f, 0.76f, 0.78f), 0.85f, 0.70f);

        RoomShell(r, wall, floor, ceil);
        // 거울 판은 shell(0.92) 이 아니라 wall 을 쓴다. 눈앞 20 cm 라 흰색을
        // 그대로 쓰면 통째로 날아가서 치료등 갓과 구별이 안 된다.
        BuildMirrorWall(r, wall, steel, m);

        // 치료등 갓 — 거울 테 바깥을 두르는 고리다. 원판으로 두면 거울을 통째로
        // 덮어 버린다(실제로 덮었다). 그리고 z 는 반드시 D 보다 **앞**이어야 한다.
        // Both 는 pos.z 를 그대로 한 번, 2D-z 로 한 번 놓으므로 D 보다 뒤에 두면
        // 진짜 갓이 거울 속으로 들어가고 거울상이 눈앞을 가린다.
        LampShade(r, shell, m, D - 0.055f);
        // 갓을 매단 팔. 거울 위로만 지나가게 띄운다.
        BothBox(r, steel, new Vector3(0f, m.radius * 2.45f, D - 0.03f),
                new Vector3(0.07f, 0.60f, 0.07f), "LampArm");

        // 머리맡 받침. 거울 속에서 참가자 머리 뒤에 보인다.
        BothBox(r, teal, new Vector3(0f, -0.26f, -0.26f),
                new Vector3(0.34f, 0.26f, 0.13f), "HeadRest");
        BothBox(r, teal, new Vector3(0f, -0.72f, -0.20f),
                new Vector3(0.52f, 0.70f, 0.16f), "ChairBack");

        // 기구 쟁반 — 환자 왼쪽. 거울 속에서 치과의 뒤로 보인다.
        BothBox(r, steel, new Vector3(-0.72f, -0.30f, 0.10f),
                new Vector3(0.40f, 0.02f, 0.30f), "Tray");
        for (int i = 0; i < 3; i++)
            Both(r, PrimitiveType.Cylinder, steel,
                 new Vector3(-0.84f + i * 0.09f, -0.28f, 0.10f), new Vector3(90f, 0f, 0f),
                 new Vector3(0.015f, 0.09f, 0.015f), "Probe" + i);

        // 뒷벽 — 수납장과 세면대. 기울어 보여도 치과 색이면 읽힌다.
        BothBox(r, shell, new Vector3(-0.70f, -eyeHeight + 0.45f, BackWall + 0.22f),
                new Vector3(1.20f, 0.90f, 0.44f), "Cabinet");
        BothBox(r, steel, new Vector3(-0.70f, -eyeHeight + 0.92f, BackWall + 0.22f),
                new Vector3(1.24f, 0.04f, 0.48f), "CabinetTop");
        BothBox(r, teal, new Vector3(0.75f, 0.10f, BackWall),
                new Vector3(0.70f, 0.50f, 0.03f), "XrayPanel");

        // 얼굴을 비추는 등은 짝을 짓지 않는다.
        //
        // Lamp 는 거울면 기준으로 같은 등을 두 개 놓는다. 그런데 거울 속 얼굴을
        // 가깝게 비추려면 짝이 거울면 바로 앞에 놓이고, 그러면 눈앞 20 cm 의 벽이
        // 통째로 하얗게 탄다. 둘 다 만족하는 자리가 없다.
        //
        // 치료등은 원래 환자를 비추지 이쪽을 비추지 않는다. 그러니 거울 속에만
        // 등이 있는 게 오히려 맞다. 짝 없는 등 하나를 거울 속 얼굴 앞에 둔다.
        OneLamp(r, new Vector3(0.16f, 0.26f, 0.60f), 1.6f, 0.95f, new Color(0.95f, 0.98f, 1f));
        Lamp(r, new Vector3(-0.60f, CeilUp - 0.20f, -0.60f), 4.4f, 1.30f,
             new Color(0.92f, 0.96f, 1f));
        return r;
    }

    // ───────────────────────── 거울 속 사람 ─────────────────────────

    /// <summary>
    /// 거울 속 사람. 머리만 떠 있으면 "내 몸"으로 안 읽혀서 전신 모델을 쓴다.
    /// 가슴 아래는 거울 테두리에 잘려 안 보이지만, 목·어깨·팔이 이어져 있어야
    /// 보이는 부분이 사람으로 읽힌다.
    /// </summary>
    private void BuildHead()
    {
        GameObject person = SpawnPerson("Person");
        if (person == null) return;

        // 모델은 제 +Z 를 본다. 거울 속 사람은 참가자 쪽(-Z)을 봐야 한다.
        person.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        person.transform.localPosition = new Vector3(0f, 0f, 2f * D);
        charRoot = person.transform;

        headBone = FindBone(person.transform, "Head");
        if (headBone == null)
        {
            Debug.LogError("[MirrorRig] Head 뼈를 못 찾았다");
            return;
        }
        restHeadRot = Quaternion.Inverse(transform.rotation) * headBone.rotation;

        // 도구 좌표의 기준축. 머리뼈를 따라 돌되 +Z 가 얼굴 방향, +Y 가 위라서
        // 뼈의 축 방향이 어떻게 생겼든 신경 쓸 필요가 없다.
        GameObject pivot = new GameObject("HeadPivot");
        pivot.transform.SetParent(transform, false);
        pivot.transform.position = headBone.position;
        pivot.transform.rotation = charRoot.rotation;
        pivot.transform.SetParent(headBone, true);
        headPivot = pivot.transform;

        // 관자놀이와 입을 머리뼈 기준으로 잡는다.
        // 메시를 재는 쪽은 믿을 게 못 됐다 — SkinnedMeshRenderer.bounds 는
        // 애니메이션 여유까지 포함해 부풀어 있고, BakeMesh 는 임포트 배율이
        // 빠진 원본 단위(cm)로 나온다. 뼈 사이 거리는 둘 다 겪지 않는다.
        Transform tip = FindBone(person.transform, "Head_end");
        float hh = tip != null ? Vector3.Distance(headBone.position, tip.position) : 0.25f;
        headUnit = hh;

        // 바닥을 이 사람의 발밑에 맞춘다. 거울이 전신이라 발이 바닥을 뚫고 들어가 있으면
        // 바로 보인다. 발뼈는 발목이라 밑창만큼(7.5 cm) 더 내린다.
        Transform foot = FindBone(person.transform, "Foot.L");
        if (foot == null) foot = FindBone(person.transform, "Foot.R");
        float stand = foot != null ? headBone.position.y - foot.position.y + 0.075f : 1.45f;
        eyeHeight = stand + NeckDrop;
        Debug.Log("[MirrorRig] 목관절~정수리 = " + hh.ToString("F3")
                  + " m / 눈높이 = " + eyeHeight.ToString("F3") + " m");

        // hh 를 1 로 본 머리 비율. 이 모델에서 재 보니 반너비 0.44, 얼굴 앞 0.57,
        // 뒤통수 -0.80 이었다. 다른 사람 모델로 바꿔도 대충 들어맞는 값이다.
        headCenterLocal = new Vector3(0f,          hh * 0.41f, -hh * 0.12f);
        templeLocal     = new Vector3(-hh * 0.41f, hh * 0.45f,  hh * 0.08f);
        mouthLocal      = new Vector3(0f,          hh * 0.08f,  hh * 0.51f);

        BuildMouth();
    }

    /// <summary>
    /// 입. 이 리그에는 얼굴 뼈가 없고 (Root/Hips/Abdomen/Torso/Chest/Neck/Head 가 전부다)
    /// 블렌드셰이프도 없어서, 메시는 건드리지 않고 입술 자리에 작은 조각을 얹어
    /// 크기로 여닫는다.
    ///
    /// 어두운 타원 하나만 놓으면 입으로 안 보인다 — 그냥 얼굴에 난 얼룩이다.
    /// 입술 테두리(조금 짙은 살색) 위에 틈(검정)을 겹치고, 벌어지면 윗니가 드러나게
    /// 세 겹으로 만들어야 입으로 읽힌다.
    /// </summary>
    private void BuildMouth()
    {
        mouthLips  = MouthPart("Lips",  new Color(0.64f, 0.46f, 0.40f), 0.10f);
        mouthHole  = MouthPart("Hole",  new Color(0.07f, 0.04f, 0.04f), 0.02f);
        mouthTeeth = MouthPart("Teeth", new Color(0.93f, 0.91f, 0.86f), 0.25f);
        SetMouth(0f);
    }

    private Transform MouthPart(string name, Color color, float smoothness)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        Kill(go.GetComponent<Collider>());
        go.transform.SetParent(headPivot, false);
        go.GetComponent<Renderer>().sharedMaterial =
            ToolModels.MakeMaterial(color, 0f, smoothness);
        return go.transform;
    }

    /// <summary>0 이면 다문 입, 1 이면 활짝 벌린 입.</summary>
    private void SetMouth(float open)
    {
        if (mouthHole == null) return;
        open = Mathf.Clamp01(open);

        float gap = Mathf.Lerp(0.0035f, 0.026f, open);     // 벌어진 높이
        float w   = Mathf.Lerp(0.036f, 0.043f, open);      // 벌리면 가로도 조금 늘어난다

        // 얼굴 표면에 걸치게 둔다. 앞으로 빼면 혹처럼 튀어나오고,
        // 뒤로 넣으면 불투명한 머리에 가려 아예 안 보인다.
        // 입술이 제일 뒤, 틈이 그 앞, 이가 제일 앞이라야 겹치는 순서가 맞는다.
        mouthLips.localPosition = mouthLocal + Vector3.forward * -0.0015f;
        mouthLips.localScale = new Vector3(w + 0.016f, gap + 0.015f, 0.012f);

        mouthHole.localPosition = mouthLocal + Vector3.forward * 0.0015f;
        mouthHole.localScale = new Vector3(w, gap, 0.011f);

        // 윗니는 윗입술 바로 밑에 걸린다. 다물었을 때는 보이면 안 된다.
        bool show = open > 0.25f;
        mouthTeeth.gameObject.SetActive(show);
        if (show)
        {
            float th = Mathf.Min(0.0055f, gap * 0.42f);
            mouthTeeth.localPosition = mouthLocal
                + Vector3.forward * 0.0030f
                + Vector3.up * (gap * 0.5f - th * 0.75f);
            mouthTeeth.localScale = new Vector3(w * 0.74f, th, 0.010f);
        }
    }

    // ───────────────────────── 사람 다루기 ─────────────────────────

    /// <summary>팔 하나. IK 로 손 위치를 맞추고, 안 쓸 때는 기본 자세로 되돌린다.</summary>
    private class Limb
    {
        public Transform shoulder, upper, lower, wrist, palm, tip;
        public Quaternion restShoulder, restUpper, restLower, restWrist;
        public Transform[] fingers;
        public Quaternion[] restFingers;
        public Vector3 curlAxis;            // 손목 로컬 — 손가락이 접히는 축
        public float curlSign = 1f;

        public bool Valid { get { return upper != null && lower != null && wrist != null; } }

        public void Rest()
        {
            if (shoulder != null) shoulder.localRotation = restShoulder;
            if (upper != null) upper.localRotation = restUpper;
            if (lower != null) lower.localRotation = restLower;
            if (wrist != null) wrist.localRotation = restWrist;
            if (fingers == null) return;
            for (int i = 0; i < fingers.Length; i++)
                if (fingers[i] != null) fingers[i].localRotation = restFingers[i];
        }
    }

    /// <summary>거울 속에 서 있는 사람 하나.</summary>
    private class Actor
    {
        public GameObject go;
        public Transform root, head;
        public Quaternion facing;           // 상체를 숙인 뒤의 실제 얼굴 방향
        public Limb armL, armR, reach;      // reach = 참가자 쪽에 가까운 팔
    }

    private static Limb MakeLimb(Transform root, string side)
    {
        Limb l = new Limb();
        l.shoulder = FindBone(root, "Shoulder." + side);
        l.upper = FindBone(root, "UpperArm." + side);
        l.lower = FindBone(root, "LowerArm." + side);
        l.wrist = FindBone(root, "Wrist." + side);
        l.palm  = FindBone(root, "Middle1." + side);   // 손가락 밑동
        l.tip   = FindBone(root, "Middle3." + side);   // 손끝
        // 이 모델의 손은 19 cm 로 제 아래팔(23.5 cm)의 80% 다. 저대로 두면
        // 사과를 입에 가져갈 때 손 하나가 얼굴을 통째로 덮어 버린다.
        // 손목뼈를 줄이면 거기 매달린 손 메시가 통째로 줄어든다.
        if (l.wrist != null) l.wrist.localScale = l.wrist.localScale * HandScale;

        if (l.shoulder != null) l.restShoulder = l.shoulder.localRotation;
        if (l.upper != null) l.restUpper = l.upper.localRotation;
        if (l.lower != null) l.restLower = l.lower.localRotation;
        if (l.wrist != null) l.restWrist = l.wrist.localRotation;

        // 엄지도 같이 접어야 주먹이 닫힌다. 네 손가락만 말면 도구가 그 사이로
        // 들여다보여서 쥔 게 아니라 걸쳐 놓은 것처럼 보인다.
        string[] names = { "Index1", "Index2", "Index3", "Middle1", "Middle2", "Middle3",
                           "Ring1", "Ring2", "Ring3", "Pinky1", "Pinky2", "Pinky3",
                           "Thumb1", "Thumb2", "Thumb3" };
        l.fingers = new Transform[names.Length];
        l.restFingers = new Quaternion[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            l.fingers[i] = FindBone(root, names[i] + "." + side);
            if (l.fingers[i] != null) l.restFingers[i] = l.fingers[i].localRotation;
        }

        // 손가락이 접히는 축. 이 리그는 네 손가락 밑마디가 전부 한 점에 겹쳐 있어서
        // (검지와 새끼의 좌표가 소수점 넷째 자리까지 같다) 손바닥을 가로지르는 선을
        // 두 밑마디 사이에서 잴 수가 없다. 대신 손가락이 뻗은 방향과 엄지 쪽 방향의
        // 외적을 쓴다 — 그 축으로 돌리면 손끝이 엄지 쪽, 곧 손바닥 안쪽으로 말린다.
        Transform knuckle = FindBone(root, "Middle1." + side);
        Transform tip     = FindBone(root, "Middle3." + side);
        Transform thumb   = FindBone(root, "Thumb2." + side);
        if (knuckle != null && tip != null && thumb != null && l.wrist != null)
        {
            Vector3 axis = Vector3.Cross(tip.position - knuckle.position,
                                         thumb.position - knuckle.position);
            if (axis.sqrMagnitude > 1e-12f)
                l.curlAxis = Quaternion.Inverse(l.wrist.rotation) * axis.normalized;
        }
        return l;
    }

    /// <summary>
    /// 이 팔이 몸의 어느 쪽에 붙어 있는지. +1 이면 body.right 쪽이다.
    /// 이름(.L/.R)으로 고르면 모델마다 좌우 규칙이 달라 반대가 나온다.
    /// </summary>
    private static float SideOf(Limb l, Transform body)
    {
        if (l == null || l.upper == null || body == null) return 1f;
        return body.InverseTransformPoint(l.upper.position).x >= 0f ? 1f : -1f;
    }

    /// <summary>
    /// 안 쓰는 팔을 몸 옆으로 내린다.
    ///
    /// 그냥 두면 FBX 의 기본 자세 그대로라 손가락을 편 채 허공에 떠 있고,
    /// 상체를 숙이는 각까지 얹혀서 제 턱 앞으로 손이 올라온다. 거울에 비치는 게
    /// 가슴 위쪽뿐이라 이 팔도 화면 한가운데에 들어온다 — 그냥 둘 수가 없다.
    /// </summary>
    private void RelaxArm(Limb l, Transform body)
    {
        if (l == null || !l.Valid || body == null) return;
        if (l.shoulder != null) l.shoulder.localRotation = l.restShoulder;

        float side = SideOf(l, body);
        Vector3 down = -body.up;
        Vector3 out_ = body.right * side;

        // 위팔은 거의 수직으로, 몸에서 살짝 벌어지게. 완전히 붙이면 팔이 몸에 묻힌다.
        PointBone(l.upper, l.lower, (down + out_ * 0.17f - body.forward * 0.05f).normalized);
        // 아래팔은 약간 앞으로. 팔꿈치가 조금 굽어 있어야 산 사람처럼 보인다.
        PointBone(l.lower, l.wrist, (down * 0.92f + out_ * 0.08f + body.forward * 0.30f).normalized);
        if (l.wrist != null) l.wrist.localRotation = l.restWrist;
        Curl(l, 16f);
    }

    /// <summary>
    /// 뼈가 제 자식 쪽으로 뻗은 방향을 목표 방향으로 돌린다.
    /// 지금 방향에서 목표 방향으로의 차이만 얹기 때문에, 뼈의 로컬 축이
    /// 어디를 보고 있든 상관없다 — FBX 마다 제각각이라 이게 중요하다.
    /// </summary>
    private static void PointBone(Transform bone, Transform child, Vector3 worldDir)
    {
        if (bone == null || child == null) return;
        Vector3 cur = child.position - bone.position;
        if (cur.sqrMagnitude < 1e-10f || worldDir.sqrMagnitude < 1e-10f) return;
        bone.rotation = Quaternion.FromToRotation(cur.normalized, worldDir.normalized) * bone.rotation;
    }

    /// <summary>
    /// 두 마디 팔을 목표 지점으로 뻗는다. 위팔·아래팔 길이와 목표까지의 거리를 알면
    /// 삼각형 하나로 팔꿈치 각이 나온다. pole 은 팔꿈치가 꺾일 방향이다.
    /// </summary>
    private static void AimArm(Limb l, Vector3 target, Vector3 pole)
    {
        if (l == null || !l.Valid) return;

        float a = Vector3.Distance(l.upper.position, l.lower.position);
        float b = Vector3.Distance(l.lower.position, l.wrist.position);

        // 어깨는 **닿아야 할 때만** 따라 나간다.
        //
        // 처음엔 늘 0.62 만큼 돌렸는데, 가까운 목표에도 어깨가 끌려 나가서
        // 삼각근이 목까지 딸려 올라가고 팔이 지느러미처럼 넓적해졌다.
        // 사람은 팔이 닿는 거리면 어깨를 가만 두고 팔꿈치만 편다.
        // 팔 길이의 92% 안쪽이면 0, 그 밖이면 모자란 만큼 비례해서 돌린다.
        if (l.shoulder != null)
        {
            Vector3 arm = l.upper.position - l.shoulder.position;
            Vector3 want = target - l.shoulder.position;
            float slack = Vector3.Distance(l.upper.position, target) - (a + b) * 0.92f;
            float t = Mathf.Clamp01(slack / 0.12f) * 0.62f;
            if (t > 0.001f && arm.sqrMagnitude > 1e-10f && want.sqrMagnitude > 1e-10f)
            {
                l.shoulder.rotation =
                    Quaternion.Slerp(Quaternion.identity,
                                     Quaternion.FromToRotation(arm.normalized, want.normalized),
                                     t) * l.shoulder.rotation;
                a = Vector3.Distance(l.upper.position, l.lower.position);
                b = Vector3.Distance(l.lower.position, l.wrist.position);
            }
        }
        Vector3 to = target - l.upper.position;
        if (to.sqrMagnitude < 1e-8f || a < 1e-5f) return;

        // 팔이 닿는 범위로 잘라낸다. 안 자르면 acos 인자가 범위를 벗어나 팔이 뒤집힌다.
        float d = Mathf.Clamp(to.magnitude, Mathf.Abs(a - b) + 1e-3f, a + b - 1e-3f);
        Vector3 dir = to.normalized;

        float cos = Mathf.Clamp((a * a + d * d - b * b) / (2f * a * d), -1f, 1f);
        float ang = Mathf.Acos(cos) * Mathf.Rad2Deg;

        Vector3 axis = Vector3.Cross(dir, pole);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(dir, Vector3.up);
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();

        PointBone(l.upper, l.lower, Quaternion.AngleAxis(-ang, axis) * dir);
        // 위팔을 돌리면 아래팔도 따라 움직인 뒤다. 여기서 다시 재야 손목이 제자리에 온다.
        PointBone(l.lower, l.wrist, target - l.lower.position);
    }

    /// <summary>
    /// 무언가를 감싸 쥔다. <paramref name="axis"/> 는 감싸는 축 — 막대라면 막대가 누운
    /// 방향, 공이라면 손가락이 넘어가는 축이다.
    ///
    /// 손목을 목표에 놓고 손끝만 목표 쪽으로 돌려서는 쥔 모양이 안 나온다.
    /// 그렇게 하면 손목의 '비틀림'이 아무렇게나 남아서, 손가락이 도구와 상관없는
    /// 엉뚱한 면에서 접힌다 — 바리깡이 손등을 뚫고 지나가고 손가락은 그 아래에서
    /// 따로 흔들리는 모양이 그래서 나왔다.
    ///
    /// 손가락이 도구를 감싸려면 **손가락이 접히는 축이 도구와 나란해야** 한다.
    /// 손목 회전을 통째로 지어서 그 조건을 박아 넣는다.
    /// </summary>
    private void GripAround(Limb l, Vector3 point, Vector3 axis, Vector3 forward,
                            float back, float curl, Vector3 pole)
    {
        if (l == null || !l.Valid || l.palm == null) return;
        if (axis.sqrMagnitude < 1e-10f) return;
        axis = axis.normalized;

        // 손이 향할 방향. 따로 주지 않으면 팔이 오는 쪽에서 목표로 다가간다.
        // 감싸는 축 방향 성분은 빼야 손이 도구를 따라 미끄러지지 않는다.
        Vector3 approach = forward.sqrMagnitude > 1e-10f
            ? forward
            : Vector3.ProjectOnPlane(point - l.upper.position, axis);
        approach = Vector3.ProjectOnPlane(approach, axis);
        if (approach.sqrMagnitude < 1e-8f) approach = Vector3.ProjectOnPlane(-pole, axis);
        if (approach.sqrMagnitude < 1e-8f) return;
        Vector3 fwd = approach.normalized;

        // 손가락이 접히고 나면 밑동과 손끝 사이에 고리가 하나 생긴다.
        // 도구가 놓여야 할 자리는 그 고리의 한가운데다 — 손목도 아니고 밑동도 아니다.
        // 고리 크기는 손 길이와 접는 각에 달려 있어서 미리 계산할 수가 없다.
        // 그래서 한 번 풀어 접어 보고, 고리가 목표에서 얼마나 빗나갔는지 재서
        // 그만큼 옮겨 다시 푼다. 모델이 바뀌어도 그대로 맞는다.
        Vector3 kLocal = Quaternion.Inverse(l.wrist.rotation)
                       * (l.palm.position - l.wrist.position).normalized;
        if (kLocal.sqrMagnitude < 1e-8f || l.curlAxis.sqrMagnitude < 1e-8f) return;

        // 로컬에서 (손가락 방향, 접는 축) 이던 짝을 월드에서 (들어오는 방향, 도구 축) 으로 보낸다.
        Quaternion hand = Quaternion.LookRotation(fwd, axis)
                        * Quaternion.Inverse(Quaternion.LookRotation(kLocal, l.curlAxis));

        Vector3 target = point - fwd * back;
        for (int pass = 0; pass < 2; pass++)
        {
            AimArm(l, target, pole);
            l.wrist.rotation = hand;
            Curl(l, curl);

            if (pass == 0 && l.tip != null && l.palm != null)
                target += point - (l.palm.position + l.tip.position) * 0.5f;
        }
    }

    /// <summary>
    /// 바리깡처럼 몸통을 움켜쥔다. <paramref name="axisLocal"/> 은 도구가 누운 방향 —
    /// 손가락이 그 둘레를 감는다.
    /// </summary>
    private void HoldTool(Limb l, Transform tool, Vector3 gripLocal, Vector3 axisLocal, Vector3 pole)
    {
        if (l == null || !l.Valid || tool == null) return;
        GripAround(l, tool.TransformPoint(gripLocal), tool.TransformDirection(axisLocal),
                   Vector3.zero, HandGrip, 50f, pole);
    }

    /// <summary>
    /// 손이 도구로 들어오는 방향까지 정해서 쥔다. 팔이 오는 쪽에 맡겨 두면
    /// 팔뚝이 도구를 가로질러 지나가 버리는 자세가 나올 때 쓴다.
    /// </summary>
    private void HoldTool(Limb l, Transform tool, Vector3 gripLocal, Vector3 axisLocal,
                          Vector3 forward, Vector3 pole)
    {
        if (l == null || !l.Valid || tool == null) return;
        GripAround(l, tool.TransformPoint(gripLocal), tool.TransformDirection(axisLocal),
                   forward, HandGrip, 50f, pole);
    }

    /// <summary>
    /// 손가락을 쥔 모양으로 접는다. 마디마다 같은 각을 월드 축으로 얹으면
    /// 부모가 먼저 돌고 자식이 따라가므로 누적돼서 자연스럽게 말린다.
    /// 매 프레임 불리므로 먼저 기본 자세로 되돌려야 각이 쌓이지 않는다.
    /// </summary>
    private static void Curl(Limb l, float degrees)
    {
        if (l == null || l.fingers == null || l.wrist == null) return;

        for (int i = 0; i < l.fingers.Length; i++)
            if (l.fingers[i] != null) l.fingers[i].localRotation = l.restFingers[i];

        Vector3 axis = l.wrist.rotation * l.curlAxis;
        if (axis.sqrMagnitude < 1e-8f) return;
        Quaternion step = Quaternion.AngleAxis(degrees * l.curlSign, axis.normalized);

        for (int i = 0; i < l.fingers.Length; i++)
            if (l.fingers[i] != null)
                l.fingers[i].rotation = step * l.fingers[i].rotation;
    }

    private GameObject SpawnPerson(string name)
    {
        GameObject prefab = Resources.Load<GameObject>("Models/Person");
        if (prefab == null)
        {
            Debug.LogError("[MirrorRig] Resources/Models/Person 을 못 찾았다");
            return null;
        }

        GameObject go = Instantiate(prefab);
        go.name = name;
        go.transform.SetParent(transform, false);

        // FBX 에 딸려 온 애니메이터가 살아 있으면 내가 돌린 뼈를 덮어쓴다.
        foreach (Animator a in go.GetComponentsInChildren<Animator>(true)) Kill(a);

        // 스크립트로 뼈를 돌린 뒤 바로 렌더하면 스키닝이 직전 프레임 자세로 남는다.
        // 참가자 팔이 분명히 움직였는데(뼈 좌표로 확인) 화면에는 안 나오던 원인이다.
        // 범위 밖 판정도 갱신하게 둔다 — 팔을 뻗으면 원래 범위를 벗어난다.
        foreach (SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            r.forceMatrixRecalculationPerRender = true;
            r.updateWhenOffscreen = true;
        }

        Standardize(go);
        return go;
    }

    /// <summary>
    /// 상황마다 등장인물이 다르다. 미용실은 뒤에서 깎아 주는 사람이, 치과는 옆에서
    /// 들여다보는 의사가 있어야 한다. 식사는 제 손으로 먹으니 아무도 없다.
    /// 셋이 전부 정면으로 혼자 서 있기만 하면 무슨 상황인지 안 읽힌다.
    /// </summary>
    private void BuildCast()
    {
        if (charRoot == null) return;

        // 바리깡은 참가자의 리그 +X 쪽 관자놀이에 닿는다. 미용사는 그쪽 뒤에 선다.
        barber = MakeActor("Barber", new Vector3(0.22f, 0.27f, 2f * D + 0.16f), 196f, 17f,
                           new Color(0.17f, 0.18f, 0.22f), new Color(0.06f, 0.05f, 0.05f));
        // 핸드피스는 -X 쪽에서 들어온다. 의사는 그쪽 옆에서 몸을 기울인다.
        dentist = MakeActor("Dentist", new Vector3(-0.35f, 0.21f, 2f * D - 0.05f), 98f, 15f,
                            new Color(0.87f, 0.90f, 0.93f), new Color(0.20f, 0.16f, 0.13f));
        if (dentist != null) BuildMask(dentist);

        collar = BuildCollar();

        // 상의 원래 색을 기억해 둔다. 미용실에서만 검게 바꿨다가 되돌린다.
        foreach (Renderer r in charRoot.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null && m.name.ToLowerInvariant().Contains("lightbrown"))
                    shirtColor = m.color;

        // 사과는 리그 -X 쪽에서 올라온다. 그쪽 팔을 쓴다.
        selfArm = NearerInX(MakeLimb(charRoot, "L"), MakeLimb(charRoot, "R"));
    }

    /// <summary>등장인물이 도구를 쥐지 않은 쪽 팔을 내린다.</summary>
    private void RelaxOther(Actor a)
    {
        if (a == null || a.root == null) return;
        if (a.armL != null && a.armL != a.reach) RelaxArm(a.armL, a.root);
        if (a.armR != null && a.armR != a.reach) RelaxArm(a.armR, a.root);
    }

    /// <summary>
    /// 도구를 쥔 팔의 팔꿈치가 꺾일 방향.
    ///
    /// AimArm 은 pole 을 **팔꿈치의 반대쪽**으로 쓴다(위팔을 dir 에서 pole 의 반대로
    /// 젖힌다). 팔꿈치는 아래·바깥으로 가야 사람 같으므로 pole 은 위·안쪽이다.
    /// 리그의 위쪽이 아니라 **그 사람의 위쪽**을 써야 한다 — 치과에서는 리그가
    /// 통째로 35° 젖혀져 있어서 둘이 다르다.
    /// </summary>
    private Vector3 ElbowPole(Actor a)
    {
        if (a == null || a.root == null || a.reach == null) return -transform.up;
        float side = SideOf(a.reach, a.root);
        return (a.root.up * 0.85f - a.root.right * side * 0.52f).normalized;
    }

    private Limb NearerInX(Limb a, Limb b)
    {
        if (a == null || !a.Valid) return b;
        if (b == null || !b.Valid) return a;
        return transform.InverseTransformPoint(a.wrist.position).x <
               transform.InverseTransformPoint(b.wrist.position).x ? a : b;
    }

    private Actor MakeActor(string name, Vector3 headAt, float yaw, float lean,
                            Color shirt, Color hair)
    {
        GameObject go = SpawnPerson(name);
        if (go == null) return null;

        Actor a = new Actor();
        a.go = go;
        a.root = go.transform;
        a.root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        a.head = FindBone(a.root, "Head");
        a.armL = MakeLimb(a.root, "L");
        a.armR = MakeLimb(a.root, "R");

        // 상체를 숙인다. 뻣뻣하게 선 채로는 남을 만지는 사람으로 안 보이고,
        // 키가 큰 채로 서 있으면 머리가 거울 위 테두리 밖으로 나가 얼굴이 잘린다.
        a.facing = Quaternion.AngleAxis(lean, a.root.right) * a.root.rotation;
        Transform torso = FindBone(a.root, "Torso");
        if (torso != null) torso.rotation = Quaternion.AngleAxis(lean, a.root.right) * torso.rotation;

        // 머리뼈가 원하는 자리에 오도록 통째로 민다 (참가자와 같은 방식).
        if (a.head != null)
            a.root.localPosition += headAt - transform.InverseTransformPoint(a.head.position);

        Tint(go, "lightbrown", shirt);
        Tint(go, "hair", hair);

        // 참가자 쪽에 가까운 팔을 쓴다. 이름(.L/.R)으로 고르면 모델마다
        // 좌우 규칙이 달라 엉뚱한 팔이 뻗는다.
        Vector3 t = headBone != null ? headBone.position : transform.position;
        a.reach = a.armL.Valid && a.armR.Valid
            ? (Vector3.Distance(a.armL.wrist.position, t) <=
               Vector3.Distance(a.armR.wrist.position, t) ? a.armL : a.armR)
            : (a.armL.Valid ? a.armL : a.armR);

        go.SetActive(false);
        return a;
    }

    /// <summary>재질 이름으로 골라 색만 바꾼다. 같은 모델을 다른 사람으로 보이게 하는 데 쓴다.</summary>
    private static void Tint(GameObject root, string key, Color c)
    {
        key = key.ToLowerInvariant();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null && m.name.ToLowerInvariant().Contains(key)) m.color = c;
    }

    /// <summary>
    /// 미용 보자기 깃. 보자기 자체는 천을 따로 만들지 않고 참가자 상의 색을 검게 바꾼다 —
    /// 구로 만들면 알처럼, 원기둥으로 만들면 드럼통처럼 보여서 어느 쪽도 천이 안 된다.
    /// 거울에 비치는 건 어차피 가슴 위쪽뿐이라, 색만 바뀌어도 천으로 덮인 걸로 읽힌다.
    /// 목에 밝은 깃이 하나 있으면 "옷"이 아니라 "두른 것"으로 보인다.
    /// </summary>
    private Transform BuildCollar()
    {
        Transform neck = FindBone(charRoot, "Neck");
        if (neck == null) return null;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = "Collar";
        Kill(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial =
            ToolModels.MakeMaterial(new Color(0.82f, 0.82f, 0.80f), 0f, 0.08f);

        // 뼈에 붙이기 전에 월드 크기를 정한다. 뼈에 배율이 걸려 있으면
        // 붙인 뒤에 localScale 을 만져서는 원하는 크기가 안 나온다.
        go.transform.position = neck.position - transform.up * 0.018f;
        go.transform.rotation = charRoot.rotation;
        go.transform.localScale = new Vector3(0.168f, 0.016f, 0.158f);
        go.transform.SetParent(neck, true);

        go.SetActive(false);
        return go.transform;
    }

    /// <summary>치과의 마스크. 흰 가운만으로는 의사로 안 읽힌다.</summary>
    private void BuildMask(Actor a)
    {
        if (a == null || a.head == null) return;

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Mask";
        Kill(go.GetComponent<Collider>());
        go.GetComponent<Renderer>().sharedMaterial =
            ToolModels.MakeMaterial(new Color(0.60f, 0.79f, 0.84f), 0f, 0.08f);

        // 머리뼈의 로컬 축이 어디를 보는지 모르니, 얼굴 방향을 아는 축을 하나 만들어
        // 거기에 매단다. 참가자 입을 놓을 때와 같은 방식이다.
        GameObject pivot = new GameObject("FacePivot");
        pivot.transform.position = a.head.position;
        pivot.transform.rotation = a.facing;
        pivot.transform.SetParent(a.head, true);

        go.transform.SetParent(pivot.transform, false);
        go.transform.localPosition = new Vector3(0f, headUnit * 0.07f, headUnit * 0.50f);
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = new Vector3(headUnit * 0.40f, headUnit * 0.26f, headUnit * 0.20f);
    }

    private static Transform FindBone(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    private static Renderer FindHeadRenderer(GameObject root)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            if (r.name.ToLowerInvariant().Contains("head")) return r;
        return null;
    }

    /// <summary>
    /// 임포트된 재질이 URP 셰이더다. 이 프로젝트는 빌트인이라 그대로 두면 색이 깨진다.
    /// 색만 _BaseColor 에서 뽑아 Standard 로 옮겨 끼운다. 공유 재질을 고치면
    /// 프로젝트 파일이 더러워지므로 새로 만들어 인스턴스에만 꽂는다.
    /// </summary>
    private static void Standardize(GameObject root)
    {
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] src = r.sharedMaterials;
            Material[] dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                Color c = Color.grey;
                string n = "mat";
                if (src[i] != null)
                {
                    n = src[i].name;
                    if (src[i].HasProperty("_BaseColor")) c = src[i].GetColor("_BaseColor");
                    else if (src[i].HasProperty("_Color")) c = src[i].color;
                }
                string key = n.ToLowerInvariant();
                float smooth = 0.10f;                       // 옷도 피부도 거의 무광이다
                if (key.Contains("eye") && !key.Contains("brow")) smooth = 0.55f;
                dst[i] = ToolModels.MakeMaterial(c, 0f, smooth);
                dst[i].name = n + "_std";
            }
            r.materials = dst;
        }
    }

    private static Bounds WorldBounds(GameObject root)
    {
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        Bounds b = new Bounds(root.transform.position, Vector3.zero);
        bool first = true;
        foreach (Renderer r in rs)
        {
            if (first) { b = r.bounds; first = false; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    // ───────────────────────── 도구 ─────────────────────────

    private void BuildTools()
    {
        if (headPivot == null) return;

        GameObject root = new GameObject("Tools");
        root.transform.SetParent(headPivot, false);
        toolRoot = root.transform;

        clipper = ToolModels.BuildClipper();
        clipper.transform.SetParent(toolRoot, false);
        clipper.SetActive(false);

        handpiece = ToolModels.BuildHandpiece();
        handpiece.transform.SetParent(toolRoot, false);
        handpiece.SetActive(false);

        GameObject applePrefab = Resources.Load<GameObject>("Models/Apple");
        if (applePrefab == null)
        {
            Debug.LogWarning("[MirrorRig] Resources/Models/Apple 을 못 찾았다");
            return;
        }

        // 사과의 원점이 한가운데가 아니라서, 빈 오브젝트로 한 번 감싸 중심을 맞춘다.
        // 그래야 입 위치에 놓았을 때 입 한가운데로 온다.
        GameObject holder = new GameObject("Food");
        holder.transform.SetParent(toolRoot, false);

        GameObject apple = Instantiate(applePrefab);
        apple.name = "Apple";
        apple.transform.SetParent(holder.transform, false);

        // 사과 하나는 입에 비해 크다. 한 입 크기로 줄인다.
        Bounds fb = WorldBounds(apple);
        apple.transform.localScale = Vector3.one * (0.055f / Mathf.Max(fb.size.y, 1e-5f));
        apple.transform.localPosition -=
            holder.transform.InverseTransformPoint(WorldBounds(apple).center);

        food = holder;
        food.SetActive(false);
    }

    // ───────────────────────── 진행 ─────────────────────────

    public void ShowScenario(Scenario s)
    {
        current = s;
        elapsed = 0f;
        Contact = 0f;
        IsPlaying = true;

        // 방을 통째로 갈아 끼운다. 미용실·집·치과는 벽도 바닥도 거울도 다르다.
        spec = SpecFor(s);
        if (rooms != null)
            for (int i = 0; i < rooms.Length; i++)
                if (rooms[i] != null) rooms[i].gameObject.SetActive(i == (int)s);

        // 치과에서는 환자가 누워 있고 거울이 머리 위에 있다. 리그를 통째로 젖히면
        // 거울도, 거울 속 사람의 자세도, 치과의가 들어오는 방향도 한꺼번에 따라온다.
        // 반사 계산은 전부 리그 좌표라서 젖혀도 그대로 성립한다.
        transform.rotation = baseRot * Quaternion.Euler(-spec.pitch, 0f, 0f);

        if (label != null)
            label.transform.localPosition =
                new Vector3(0f, spec.centerY - (spec.Height * 0.5f + 0.085f), D - 0.02f);

        if (clipper != null) clipper.SetActive(s == Scenario.Clipper);
        if (handpiece != null) handpiece.SetActive(s == Scenario.Dental);
        if (food != null) food.SetActive(s == Scenario.Food);
        SetMouth(0f);          // 치과를 보다 나가면 입이 벌어진 채로 남는다

        // 등장인물과 소품은 상황마다 다르다.
        if (barber != null)  barber.go.SetActive(s == Scenario.Clipper);
        if (dentist != null) dentist.go.SetActive(s == Scenario.Dental);
        // 미용실 보자기도 치과 에이프런도 목에 두른 천이다. 깃은 둘 다 쓴다.
        if (collar != null) collar.gameObject.SetActive(s != Scenario.Food);

        // 천은 따로 만들지 않고 상의 색을 바꾼다. 가슴 앞에 판을 세워 보니
        // 몸에 묻히거나 가슴에서 떠 보이거나 둘 중 하나였다 — 몸 두께가 자세마다
        // 달라서 맞는 거리가 하나로 정해지지 않는다. 거울에 비치는 건 가슴 위쪽뿐이라
        // 색만 바뀌어도 천으로 덮인 걸로 읽힌다.
        if (charRoot != null)
        {
            Color drape = shirtColor;
            if (s == Scenario.Clipper)     drape = new Color(0.10f, 0.10f, 0.12f);
            else if (s == Scenario.Dental) drape = new Color(0.56f, 0.78f, 0.84f);
            Tint(charRoot.gameObject, "lightbrown", drape);
        }

        if (selfArm != null) selfArm.Rest();
        if (barber != null)  barber.reach.Rest();
        if (dentist != null) dentist.reach.Rest();

        ShowLabel(s);
    }

    public void SetPlaying(bool value)
    {
        IsPlaying = value;
        if (!value) Contact = 0f;
    }

    private void LateUpdate()
    {
        TrackViewer();

        if (label != null && label.gameObject.activeSelf && Time.time > labelUntil)
            label.gameObject.SetActive(false);

        if (current == Scenario.None || !IsPlaying) return;
        elapsed += Time.deltaTime;
        Animate();
    }

    private void Animate()
    {
        switch (current)
        {
            case Scenario.Clipper: AnimateClipper(); break;
            case Scenario.Dental:  AnimateDental();  break;
            case Scenario.Food:    AnimateFood();    break;
        }
    }

    /// <summary>
    /// 에디터 프리뷰용 — 시간을 지정해 한 프레임만 돌린다.
    /// 에디터에서는 LateUpdate 가 안 돌아서 원하는 순간의 그림을 찍을 수 없다.
    /// </summary>
    public void StepForPreview(float seconds)
    {
        TrackViewer();
        if (current == Scenario.None) return;
        elapsed = seconds;
        Animate();
    }

    /// <summary>참가자 머리를 거울면 기준으로 반사시켜 거울 속 사람에게 옮긴다.</summary>
    private void TrackViewer()
    {
        if (viewer == null || charRoot == null || headBone == null) return;

        Vector3 p = transform.InverseTransformPoint(viewer.position);
        Vector3 f = transform.InverseTransformDirection(viewer.forward);
        Vector3 u = transform.InverseTransformDirection(viewer.up);

        // 거울면이 z = D 이고 법선이 Z 라서, 반사는 z 성분만 뒤집으면 끝난다.
        // 좌우가 뒤집히는 것(글자가 거꾸로 보이는 것)은 몸이 거의 좌우대칭이라 안 보인다.
        Quaternion mirrored = Quaternion.LookRotation(new Vector3(f.x, f.y, -f.z),
                                                      new Vector3(u.x, u.y, -u.z));

        // 몸은 가만히 있고 목 위만 돈다. 정면(Y 180도)을 기준으로 한 차이만 뽑아
        // 머리뼈에 얹고, 사람 목이 실제로 돌아가는 범위로 잘라낸다.
        // 안 자르면 참가자가 뒤를 볼 때 거울 속 목이 꺾인다.
        Quaternion delta = mirrored * Quaternion.Inverse(Quaternion.Euler(0f, 180f, 0f));
        Vector3 e = delta.eulerAngles;
        delta = Quaternion.Euler(Mathf.Clamp(Mathf.DeltaAngle(0f, e.x), -38f, 38f),
                                 Mathf.Clamp(Mathf.DeltaAngle(0f, e.y), -62f, 62f),
                                 Mathf.Clamp(Mathf.DeltaAngle(0f, e.z), -22f, 22f));
        headBone.rotation = transform.rotation * (delta * restHeadRot);

        // 머리뼈가 반사된 목 관절에 오도록 사람을 통째로 민다.
        // 뼈 자신의 회전은 제 위치를 바꾸지 않으므로 회전 뒤에 재도 값이 안 흔들린다.
        Vector3 now  = transform.InverseTransformPoint(headBone.position);
        Vector3 want = new Vector3(p.x, p.y - NeckDrop, 2f * D - p.z);
        charRoot.localPosition += want - now;
    }

    private static float Ease(float k)
    {
        k = Mathf.Clamp01(k);
        return k * k * (3f - 2f * k);
    }

    private void AnimateClipper()
    {
        if (clipper == null) return;
        SetMouth(0f);

        Vector3 away = new Vector3(-0.22f, 0.06f, 0f);     // 옆에서 들어온다
        float k = elapsed / ApproachTime;
        Vector3 pos = Vector3.Lerp(templeLocal + away, templeLocal, Ease(k));

        if (k >= 1f)
        {
            // 닿은 뒤에는 관자놀이에서 천천히 짧게 오르내린다. 세기는 건드리지 않는다 —
            // 실제 바리깡도 머리 어디에 있든 세기가 비슷하고, 모터가 하나라
            // 위치를 세기로 표현할 수도 없다.
            pos += Vector3.up * (Mathf.Sin((elapsed - ApproachTime) * 2.6f) * 0.013f);
        }

        clipper.transform.localPosition = pos;

        // 머리 중심보다 조금 위를 겨눈다. 정확히 중심을 보게 하면 몸통이 옆으로
        // 수평하게 뻗어서 손에 들린 게 아니라 머리에 꽂힌 것처럼 보인다.
        Vector3 aim = headCenterLocal + Vector3.up * (headUnit * 0.22f);
        clipper.transform.localRotation =
            Quaternion.LookRotation((aim - pos).normalized, Vector3.up);

        // 미용사가 쥐고 있다. 바리깡은 원점이 날이고 몸통이 -Z 쪽으로 15도 들린 채
        // 뻗어 있어서, 손은 그 몸통 중간을 잡는다.
        if (barber != null)
        {
            RelaxOther(barber);
            // 팔꿈치는 아래·바깥으로 꺾인다. pole 은 팔꿈치의 **반대쪽**을 가리킨다.
            HoldTool(barber.reach, clipper.transform,
                     new Vector3(0f, 0.022f, -0.095f),
                     new Vector3(0f, 0.26f, -0.97f), ElbowPole(barber));
        }

        Contact = k >= 1f ? 1f : 0f;
    }

    private void AnimateDental()
    {
        if (handpiece == null) return;

        // 치과의가 위에서 몸을 숙이고 있으므로 드릴도 위에서 내려와야 한다.
        // 아래에서 올리면 팔뚝이 드릴을 가로질러 지나가 쥔 게 아닌 것처럼 보인다.
        // 너무 높이 올리면 손이 눈을 가린다. 광대뼈 옆 정도에서 들어온다.
        Vector3 away = new Vector3(0.22f, 0.07f, 0.13f);   // 위 옆에서 비스듬히
        float k = elapsed / ApproachTime;
        Vector3 pos = Vector3.Lerp(mouthLocal + away, mouthLocal, Ease(k));
        bool touching = k >= 1f;

        if (touching)
        {
            // 치과의는 계속 대고 있지 않고 댔다 뗐다 한다. 뗀 동안은 진동도 꺼진다.
            float cycle = Mathf.Repeat(elapsed - ApproachTime, 3.2f);
            if (cycle > 2.1f)
            {
                float lift = Mathf.Sin((cycle - 2.1f) / 1.1f * Mathf.PI);
                pos += away.normalized * (lift * 0.025f);
                touching = lift < 0.25f;
            }
        }

        handpiece.transform.localPosition = pos;
        handpiece.transform.localRotation = Quaternion.LookRotation(-away.normalized, Vector3.up);

        // 치과는 처음부터 끝까지 벌리고 있는다. 드릴이 들어오기 전에 미리 벌린다.
        SetMouth(Mathf.Max(Ease(elapsed / (ApproachTime * 0.6f)), 0.95f * Mathf.Clamp01(k)));

        if (dentist != null)
        {
            RelaxOther(dentist);
            HoldTool(dentist.reach, handpiece.transform,
                     new Vector3(0f, 0.008f, -0.090f),
                     new Vector3(0f, 0.10f, -0.99f), ElbowPole(dentist));
        }

        Contact = touching ? 1f : 0f;
    }

    private void AnimateFood()
    {
        if (food == null) return;

        const float bite = 3.0f;                            // 한 입 주기
        const float hold = 0.28f;                           // 입술에 닿아 있는 순간
        const float chew = 1.0f;                            // 씹는 시간
        float t = Mathf.Repeat(elapsed, bite);
        Vector3 away = new Vector3(0.05f, -0.26f, 0.14f);   // 아래에서 올라온다

        // 사과의 원점은 한가운데라, 입술 지점에 그대로 놓으면 절반이 얼굴에 박힌다.
        // 반지름만큼 앞으로 빼서 입술에 닿게 한다 (0.065 m 짜리로 줄여 뒀다).
        // 손이 아래에서 올라오므로 사과를 입술 한가운데에 두면 손이 코까지 올라온다.
        Vector3 rest = mouthLocal + Vector3.forward * 0.040f - Vector3.up * 0.008f;

        if (t < ApproachTime)
        {
            food.SetActive(true);
            food.transform.localPosition =
                Vector3.Lerp(rest + away, rest, Ease(t / ApproachTime));
            SetMouth(Ease(t / ApproachTime) * 0.9f);      // 올라오는 동안 벌린다
            Contact = 0f;
        }
        else if (t < ApproachTime + hold)
        {
            // 입술에 닿아 있는 순간. 여기를 건너뛰면 정작 베어 무는 장면이 안 보인다 —
            // 사과가 다 올라오자마자 사라져 버린다.
            food.SetActive(true);
            food.transform.localPosition = rest;
            SetMouth(Mathf.Lerp(0.90f, 0.35f, (t - ApproachTime) / hold));   // 물어 내린다
            Contact = 1f;
        }
        else
        {
            // 베어 물었다. 사과는 사라지고, 씹는 동안 진동이 난다.
            food.SetActive(false);
            float c = t - ApproachTime - hold;
            bool chewing = c < chew;
            // 씹는 동안 입이 오물거린다. 진동이 나는 구간과 눈으로도 맞아떨어진다.
            SetMouth(chewing ? 0.30f + 0.22f * Mathf.Sin(c * 14f) : 0f);
            Contact = chewing ? 1f : 0f;
        }

        food.transform.localRotation = Quaternion.Euler(0f, elapsed * 40f, 0f);

        // 남이 먹여 주는 게 아니라 제 손으로 먹는다. 사과가 보이는 동안만 팔을 올린다.
        if (selfArm != null)
        {
            if (food.activeSelf)
            {
                // 사과는 제자리에서 돌고 있어서, 사과 좌표로 손 자리를 잡으면 손도 같이 돈다.
                // 머리 축(toolRoot) 기준으로 사과의 아래·바깥쪽을 잡아야 자리가 고정된다.
                // 손목을 입술 높이에 두면 손바닥이 얼굴을 통째로 가린다 — 턱 아래로 내린다.
                // 감싸는 축은 앞뒤로 둔다. 가로축으로 두면 손가락이 위아래로 말려서
                // 손바닥이 거울을 정면으로 보고, 넓적한 손이 얼굴을 가려 버린다.
                // 앞뒤축이면 손이 옆에서 들어와 모로 보이므로 얼굴이 살아 있다.
                // 사과가 제자리에서 돌고 있으므로 축은 사과가 아니라 머리 축에서 잡는다.
                // 팔꿈치는 아래로. pole 은 팔꿈치의 반대쪽이라 위를 가리킨다.
                // -up 을 주면 팔꿈치가 들려서 아래팔이 수평으로 가로지른다 —
                // 사과를 옆에서 들이미는 모양이 돼 버린다. 이 팔은 리그 -X 쪽에
                // 있으므로 팔꿈치가 바깥(-X)으로 빠지게 pole 을 +X 로 기울인다.
                GripAround(selfArm, food.transform.position,
                           toolRoot.forward, Vector3.zero, 0f, 44f,
                           (transform.up * 0.86f + transform.right * 0.45f).normalized);
            }
            else selfArm.Rest();
        }
    }

    // ───────────────────────── 안내 글자 ─────────────────────────

    /// <summary>
    /// 한글 대신 영어를 쓴다. 내장 폰트에 한글 글리프가 없어 기기에서 네모로 나올 수 있는데,
    /// 그걸 빌드해서 폰에 올려 보기 전에는 알 수 없다.
    /// </summary>
    private void BuildLabel()
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(transform, false);
        // 거울 아래 벽에 붙인다. 거울 안에 두면 머리를 가리고, 틀에 걸쳐 두면 잘린다.
        // 거울 크기가 상황마다 달라서 자리는 ShowScenario 가 다시 잡는다.
        go.transform.localPosition = new Vector3(0f, -0.40f, D - 0.02f);
        go.transform.localScale = Vector3.one;

        label = go.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        // TextMesh 의 글자 높이는 대략 fontSize * characterSize / 10 월드 단위다.
        // 100 x 0.004 → 4cm, 0.43m 거리에서 5도쯤 되어 읽힌다.
        label.fontSize = 100;
        label.characterSize = 0.0032f;
        label.color = Color.white;

        Font f = null;
        try { f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { }
        if (f == null) { try { f = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { } }

        if (f != null)
        {
            label.font = f;
            go.GetComponent<MeshRenderer>().sharedMaterial = f.material;
        }
        else Debug.LogWarning("[MirrorRig] 기본 폰트를 못 찾아 안내 글자가 안 보인다");

        go.SetActive(false);
    }

    private void ShowLabel(Scenario s)
    {
        if (label == null) return;

        string text;
        switch (s)
        {
            case Scenario.Clipper: text = "HAIR CLIPPER"; break;
            case Scenario.Food:    text = "EATING";       break;
            case Scenario.Dental:  text = "DENTAL DRILL"; break;
            default:               text = "";             break;
        }

        // 한때 여기에 ToolModels.BaseShaderName 을 같이 찍었다. 폰에서 화면이
        // 통째로 분홍색으로 나왔을 때 어느 셰이더가 잡혔는지 보려던 것인데,
        // 11/5 빌드에서 세 장면 다 정상으로 확인돼서 뺀다. 또 분홍색이 나오면
        // 그 프로퍼티를 다시 여기에 붙여 보면 된다.
        label.text = text;
        label.gameObject.SetActive(text.Length > 0);
        labelUntil = Time.time + 2f;
    }

    /// <summary>에디터 프리뷰에서도 쓰므로 Destroy 를 그대로 부르면 안 된다.</summary>
    private static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}

using System.Collections.Generic;
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
    public enum Scenario { None, Food, Dental }

    /// <summary>
    /// 음식 종류. 11/11 에 두 수준(단단한 것 / 무른 것)에서 다섯 가지로 늘렸다.
    ///
    /// 두 수준으로는 "단단하면 세게, 무르면 약하게" 한 줄밖에 못 묻는다.
    /// 다섯 가지는 씹는 느낌이 **한 축이 아니라는 것**을 보여 준다 — 사과는
    /// 아삭 부서지고, 고기는 질겨서 찢기고, 사탕은 유리처럼 쩍 깨지고,
    /// 젤리는 쫀득 눌리고, 푸딩은 아예 씹히지 않는다. 단단함 한 축으로
    /// 줄여 버리면 사과와 사탕이 같은 칸에 들어가고 젤리와 푸딩도 같은
    /// 칸에 들어가는데, 입에 넣어 보면 넷이 다 다른 느낌이다.
    ///
    /// 시나리오는 그래도 하나다. 방도 거울도 팔 동작도 같고 달라지는 건
    /// 음식 모델과 씹는 값뿐이라, 다른 점은 전부 FoodSpecs 표에 있다.
    /// </summary>
    public enum FoodKind { Apple, Jelly, Meat, Pudding, Candy }

    /// <summary>
    /// 음식 하나의 생김새와 씹는 느낌. 조건마다 다른 값을 **한 군데** 모은다.
    ///
    /// 전에는 `soft ? a : b` 가 AnimateFood·BuildHome·ShowLabel·
    /// ApplyContactHaptics 네 군데에 흩어져 있었다. 두 가지일 때는 읽혔지만
    /// 다섯 가지가 되면 어디를 안 고쳤는지 알 수가 없다.
    /// </summary>
    private class FoodSpec
    {
        public string model;     // Resources/Models 아래 이름
        public string label;     // 거울 밑 안내 글자 (내장 폰트에 한글이 없다)
        public float size;       // 손에 드는 것의 가장 긴 변 (m)
        public int bites;        // 1 = 한 입에 통째로, 2 이상 = 그 횟수에 걸쳐
        public bool carve;       // 깨문 자리를 파낼지 (안 파면 작아지기만 한다)
        public float hold;       // 입에 닿아 있는 시간 (s)
        public float chew;       // 씹는 시간 (s)
        public float rate;       // 오물거리는 속도
        public float depth;      // 오물거리는 폭
        public float open;       // 입을 벌리는 정도 (0~1)
        public float spin;       // 손에 들고 돌리는 속도 (도/s)
        public float tilt;       // 손에 들 때 세우는 각 (도, X 축)
        public float smooth;     // 재질 반질거림 (Reskin 이 쓴다)
        public float grip;       // 쥐는 자리를 음식 밑으로 내리는 양 (m)
                                 // 0 = 한가운데를 쥔다
        public float curl;       // 쥐는 손가락 각 (GripAround)
        public float thumb;
        public float fold;
        public int frags;        // 진동 조각 수 (ScenarioHaptics.BuildCrunch)
        public float peak;       // 진동 세기 배율 (0~1)
        public float bright;     // 깨지는 소리의 밝기 (0 둔한 퍽 ~ 1 유리 쨍)
        public float pSize;      // 접시에 담은 알의 크기 (m)
        public int pCount;       // 접시에 담은 알 수
        public float pRing;      // 알들을 놓는 원의 반지름 (m)
        public float pYaw;       // 알을 돌려 놓는 각 (도)
    }

    /// <summary>
    /// 다섯 가지의 값. 순서가 FoodKind 와 같아야 한다 — 표를 번호로 찾는다.
    ///
    /// 씹는 시간·속도·폭은 실제로 재서 맞춘 값이 아니라 **순서**를 맞춘
    /// 값이다. 중요한 건 절대값이 아니라 다섯이 서로 다르고, 그 차이가
    /// 진동(frags·peak)과 같은 방향이라는 것이다 — 보이는 것과 느껴지는
    /// 것이 어긋나면 이 연구가 재려는 현실감이 바로 무너진다.
    ///
    /// 접시에 담는 알 수는 겹치지 않게 고른다. 반지름 R 인 원에 n 알을
    /// 두면 이웃 사이가 2·R·sin(π/n) 이라 그게 알 지름보다 커야 하고,
    /// 접시가 지름 20 cm 니까 R + 알 반지름이 10 cm 를 넘으면 접시 밖으로
    /// 나간다. 손에 드는 것보다 접시 알을 조금 작게 둔 건 그래서다.
    /// </summary>
    private static readonly FoodSpec[] FoodSpecs =
    {
        // 사과 — 아삭 부서진다. 네 입에 걸쳐 먹고 깨문 자리가 패인다.
        new FoodSpec { model = "Apple", label = "APPLE",
                       size = 0.075f, bites = 4, carve = true,
                       hold = 0.50f, chew = 1.90f, rate = 12f, depth = 0.20f,
                       open = 0.55f, spin = 26f, tilt = 0f, smooth = 0.58f,
                       grip = 0f, curl = 42f, thumb = 46f, fold = 78f,
                       frags = 3, peak = 1.00f, bright = 0.70f,
                       pSize = 0.075f, pCount = 3, pRing = 0.050f,
                       pYaw = 40f },

        // 젤리 — 쫀득 눌린다. 한 알이니 통째로 들어간다. 부서지는 게 아니라
        // 이에 붙었다 떨어지는 느낌이라 진동은 조각이 적고 약하다.
        //
        // 8.5 cm 는 곰 젤리치고 컸다 — 사과보다 큰 젤리였다. 7.2 cm 로
        // 내린다. 알이 작아진 만큼 손가락을 더 접어야 한다(curl/thumb/fold):
        // 고리 크기는 접는 각이 정하는데, 각을 그대로 두면 작아진 알이
        // 고리 안에 묻혀 손가락 뒤로 사라진다.
        new FoodSpec { model = "GummyBear", label = "JELLY",
                       size = 0.072f, bites = 1, carve = false,
                       hold = 0.70f, chew = 2.40f, rate = 8f, depth = 0.14f,
                       open = 0.45f, spin = 22f, tilt = 0f, smooth = 0.74f,
                       grip = 0f, curl = 63f, thumb = 67f, fold = 101f,
                       frags = 2, peak = 0.55f, bright = 0.25f,
                       pSize = 0.058f, pCount = 5, pRing = 0.042f,
                       pYaw = 150f },

        // 고기 — 세워서 든다. 납작한 조각이라 눕혀 들면 두께 1 cm 짜리
        // 선으로만 보이고, 그 선마저 주먹 안에 들어가 아예 안 보였다.
        // 질겨서 오래 씹는다. 다섯 중 씹는 시간이 가장 길고 속도는
        // 가장 느리다. 부서지는 게 아니라 찢기는 것이라 진동은 조각이
        // 적으면서 둔하다. 세 입에 걸쳐 먹는데 **깨문 자리는 안 판다** —
        // 납작한 조각이라 구를 파면 구멍이 뚫린다(PrepareBites 주석).
        new FoodSpec { model = "Steak", label = "MEAT",
                       size = 0.085f, bites = 3, carve = false,
                       hold = 0.60f, chew = 3.40f, rate = 6f, depth = 0.17f,
                       open = 0.60f, spin = 18f, tilt = 90f, smooth = 0.26f,
                       grip = 0.023f, curl = 40f, thumb = 44f, fold = 74f,
                       frags = 2, peak = 0.70f, bright = 0.18f,
                       pSize = 0.080f, pCount = 3, pRing = 0.052f,
                       pYaw = 90f },

        // 푸딩 — 거의 안 씹힌다. 혀로 누르면 사라지니 씹는 구간을 가장
        // 짧게, 오물거림도 가장 얕게 둔다. 진동은 한 번, 아주 약하게 —
        // 다섯 중 아래 끝을 잡아 주는 조건이다.
        new FoodSpec { model = "Flan", label = "PUDDING",
                       size = 0.070f, bites = 1, carve = false,
                       hold = 0.80f, chew = 1.10f, rate = 5f, depth = 0.09f,
                       open = 0.45f, spin = 14f, tilt = 0f, smooth = 0.55f,
                       grip = 0.024f, curl = 46f, thumb = 50f, fold = 80f,
                       frags = 1, peak = 0.30f, bright = 0.08f,
                       pSize = 0.065f, pCount = 4, pRing = 0.048f,
                       pYaw = 0f },

        // 사탕 — 납작한 알이라 고기와 같이 세워서 든다. 세워야 거울에서
        // 빨간 소용돌이 무늬가 보이고, 그게 "사탕" 으로 읽히는 유일한 단서다.
        // 유리처럼 쩍 깨진다. 알이 작아 입을 거의 안 벌리는데
        // 깨지는 순간은 다섯 중 가장 날카롭다. 조각이 가장 많고 세기도
        // 최대다 — 위 끝을 잡아 주는 조건이다.
        new FoodSpec { model = "Candy", label = "CANDY",
                       size = 0.055f, bites = 1, carve = false,
                       // 입을 0.32 밖에 안 벌리니 씹는 폭도 작아야 한다. 씹는
                       // 동안의 기준 벌림은 open × ChewBase = 0.13 인데, 여기에
                       // 0.22 를 더했다 빼면 주기의 절반이 0 으로 잘려 나가
                       // (SetMouth 가 0~1 로 묶는다) 턱이 다물린 채 떨리는 것
                       // 처럼 보인다. 날카로움은 폭이 아니라 rate 가 맡는다.
                       hold = 0.35f, chew = 1.50f, rate = 14f, depth = 0.12f,
                       open = 0.32f, spin = 40f, tilt = 90f, smooth = 0.86f,
                       grip = 0.026f, curl = 44f, thumb = 48f, fold = 78f,
                       frags = 4, peak = 1.00f, bright = 1.00f,
                       pSize = 0.052f, pCount = 5, pRing = 0.045f,
                       pYaw = 0f },
    };

    private static FoodSpec SpecOf(FoodKind k)
    {
        return FoodSpecs[Mathf.Clamp((int)k, 0, FoodSpecs.Length - 1)];
    }


    /// <summary>
    /// 치과에서 입에 닿는 도구. 두 수준을 **파라미터가 아니라 물건**으로 가른다.
    ///
    /// 드릴과 전동칫솔은 둘 다 입에 닿는 전동 공구인데 사람이 기대하는 느낌이
    /// 정반대다 — 드릴은 날카롭고 무섭고, 칫솔은 둔하고 익숙하다.
    /// 같은 진동을 줘도 "이게 맞는 느낌인가" 가 갈릴 것이라 보고 고른 짝이다.
    /// </summary>
    public enum DentalKind { Drill, Brush }

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

    /// <summary>
    /// 치과에서 거울 속 장면을 통째로 키우는 배율.
    ///
    /// 치과에서 봐야 하는 건 입 안이다 — 이가 몇 개인지 보일 정도로. 그런데
    /// 반사는 거울의 두 배 거리(0.9 m)에 맺혀서, 사람 크기 그대로는 입이
    /// 눈에 3도쯤으로 들어온다. 거울을 키우든 줄이든 이 각도는 안 변한다
    /// (거울은 창문이지 돋보기가 아니다). 커져야 하는 건 거울 속 장면이다.
    ///
    /// **환자와 치과의를 같은 배율로, 같은 축(입)을 중심으로** 키운다. 둘 중
    /// 하나만 키우면 도구를 쥔 손과 도구의 크기가 어긋난다 — 도구는 환자
    /// 머리뼈에 매달려 있어서 환자를 따라 커지기 때문이다.
    ///
    /// 위로 더 못 올리는 이유: 얼굴이 입을 축으로 앞으로도 자라서, 너무
    /// 키우면 **얼굴 앞면이 거울면(z = D)을 뚫고 참가자 방으로 나온다.**
    /// 그러면 거울 테두리 바깥에 살색 덩어리가 떠 있는 게 보인다 — 6배에서
    /// 실제로 그랬다. 입에서 얼굴 앞면까지가 5.5 cm 쯤이라 5배면 입
    /// (z ≈ 0.77)에서 0.275 앞, 즉 z ≈ 0.50 으로 0.45 까지 5 cm 남는다.
    /// </summary>
    private const float DentalZoom = 5.0f;

    // 사실적인 사람 머리 모델(poly.pizza "HeadRef")을 머리뼈에 덧씌워
    // 봤다가 **뺐다.** 해부학적으로는 맞는데 이 저폴리 몸·방에 섞어 놓으니
    // 얼굴만 따로 노는 데다 눈꺼풀뿐인 모델이라 표정이 죽어서, 원래 얼굴이
    // 낫다는 판단이다. 다시 해 볼 거면 입 자리도 같이 옮겨야 한다 —
    // 그 모델은 코끝이 z +0.476, 그 아래 입술이 y -0.27 / z +0.39 라
    // (키를 1 로 본 값) 지금 mouthLocal 로는 얼굴 안쪽에 묻힌다.

    /// <summary>
    /// 집 장면에서 눈높이가 내려가는 양. 서 있을 때 기준에서 뺀다.
    ///
    /// 집은 **앉은 자세**다 — 식탁에 앉아 접시에서 집어 먹기로 했다. 서 있는
    /// 채로 상만 놓았더니 상판이 거울 속 사람의 배를 가로질렀다. 상을 낮추면
    /// 이번엔 무릎 높이가 되고, 서 있는 사람에게 맞는 상 높이란 게 없다.
    /// 그래서 사람을 앉힌다 — 바닥이 이만큼 올라오고(PoseLegs 가 다리를 접는다)
    /// y = -0.45 에 놓인 상판이 바닥에서 0.75 m, 보통 식탁 높이가 된다.
    /// </summary>
    private const float SitDrop = 0.50f;

    // 바닥 높이는 상수로 둘 수 없다 — 거울 속 사람의 발이 바닥에 닿아야 하는데
    // 그 키는 모델이 정한다. 전신 거울을 쓰면 발까지 보이므로 어긋나면 바로 드러난다.
    // BuildHead 에서 뼈로 재서 넣는다.
    private float eyeHeight = 1.60f;

    private const float ApproachTime = 1.2f;

    // 씹는 동안 입이 머무는 벌림. 벌린 정도(FoodSpec.open)에 대한 비율이다 —
    // 사과에서 0.55 를 벌렸다가 0.22 로 물어 내렸으니 그 비율을 그대로 쓴다.
    private const float ChewBase = 0.40f;

    // 눈꺼풀을 눈보다 이만큼 앞에 띄운다. 덜 띄우면 두 면이 같은 자리를
    // 다퉈 얼룩덜룩해지고(z-fighting), 더 띄우면 눈이 쑥 들어가 보인다.
    private const float LidLift = 0.0015f;
    // 눈보다 이만큼 크게 떠서 눈 가장자리가 삐져나오지 않게 한다.
    private const float LidGrow = 1.14f;
    // 뜬 눈에 남겨 두는 눈꺼풀. 0 으로 두면 눈 위가 허전한데, 조금
    // 남기면 쌍꺼풀 선으로 읽힌다.
    private const float LidOpen = 0.07f;
    // 한 번 깜빡이는 데 걸리는 시간 (s)
    private const float BlinkDur = 0.22f;

    // 눈썹을 찡그릴 때 내려오는 거리 (m). 사람 눈썹은 5 mm 쯤 움직인다.
    private const float BrowDrop = 0.005f;
    // 볼이 부푸는 거리 (m). 4 mm 로 넓게 폈더니 볼이 부푼 게 아니라
    // **머리가 커 보였다.** 좁게 모으고 조금 키운다.
    private const float CheekPuff = 0.006f;

    // 한 입 주기에서 음식에 상관없이 같은 두 구간.
    private const float OnPlate = 1.4f;    // 접시에 놓인 채 기다리는 동안
    private const float AfterBite = 0.9f;  // 삼키고 잠시 쉬는 동안
    private const float HandScale    = 0.62f;   // 손이 커서 줄인다
    private const float HandGrip     = 0f;      // 고리 한가운데에서 더 띄울 여유   // 물체가 들어와 닿기까지

    // ───────────────────────── 상태 ─────────────────────────

    private Transform viewer;
    private Transform charRoot;       // 거울 속 사람. 통째로 민다
    private Transform headBone;       // 목 위. 고개는 이 뼈만 돈다
    private Quaternion restHeadRot;   // 리그 기준 머리뼈의 기본 자세
    private Transform headPivot;      // 머리뼈에 매달린 축. 도구 좌표의 기준
    private Transform toolRoot;
    // 참가자 바로 옆에 선 치과의 (BuildCast). 거울 속 치과의와 같은
    // 사람의 **실물 쪽**이다.
    private Actor nearDentist;
    private GameObject nearDrill;     // 그 사람이 쥔 진짜 드릴
    private GameObject nearBrush;     // 전동칫솔 조건에서 쥐는 것
    private GameObject handpiece;     // 치과 드릴 (거울 속)
    private GameObject toothbrush;    // 칫솔 (조건 이름은 전동칫솔)
    /// <summary>
    /// 음식 한 가지의 실물. FoodKind 순으로 다섯 벌 지어 두고 하나만 켠다.
    ///
    /// 접시에 담아 둔 것도 여기 같이 들고 있다. **조건마다 접시에 담긴
    /// 것이 달라야 한다** — 사과를 집어 먹으면서 접시에는 젤리가 담겨
    /// 있으면 그 조건이 무너진다.
    /// </summary>
    private class FoodItem
    {
        public GameObject go;        // 손에 드는 것
        public Transform plate;      // 접시에 담아 둔 같은 음식
        public Mesh[] bites;         // 깨문 단계별 메시 (안 파는 음식은 null)
        public MeshFilter[] body;    // 갈아 끼울 메시
        public int stage;            // 지금 몇 입 깨문 상태인가
        public Vector3 half;         // 실제 반 크기 (접시에 올려놓을 때 쓴다)
    }
    private FoodItem[] foodItems;    // FoodKind 순
    private FoodItem foodNow;        // 지금 켜져 있는 쪽
    // 입술 — 다문 선에서 피부로 그러데이션으로 건너간다 (JawPiece).
    private Material lipMatUp, lipMatLow;
    private TextMesh[] labelEdge;    // 안내 글자의 어두운 외곽선
    private const bool FakeContactShadows = false;  // 가짜 원반 그림자
    // 깨무는 순간을 한 번만 세려고 둔다. 몇 입에 걸쳐 먹는지는 음식마다
    // 다르므로(FoodSpec.bites) 단계 수는 여기 두지 않는다.
    private int biteCycle = -1;
    private float mouthOpen;

    // 눈꺼풀 두 짝 (0 = 왼쪽, x<0). 모델에 눈 뼈도 블렌드셰이프도 없어서
    // 눈 앞면을 떠다 살색으로 덮어 만들었다 — BuildEyes 참고.
    private readonly Transform[] eyeLid = new Transform[2];
    // 깜빡임 시계. elapsed 와 따로 가는 이유는, 아무 상황도 안 켠 거울
    // 앞에서도 사람은 눈을 깜빡여야 하기 때문이다. elapsed 는 상황이
    // 켜져 있는 동안만 흐른다.
    private float faceTime;

    // 눈썹과 볼은 얼굴 살이라 떼어 낼 수가 없다. 머리 메시를 한 장
    // 복사해 두고 그 꼭짓점을 직접 민다 (BuildFlex).
    private Mesh flexMesh;              // 갈아 끼운 머리 메시
    private Vector3[] flexRest;         // 밀기 전 꼭짓점
    private Vector3[] flexNow;          // 밀어 넣을 꼭짓점
    private int[] flexIdx;              // 움직이는 꼭짓점만
    private Vector3[] flexDir;          // 그 꼭짓점이 갈 방향 (메시 좌표)
    private float[] flexBrow;           // 눈썹다움 0~1
    private float[] flexCheek;          // 볼다움 0~1
    private float flexLastBrow = -9f, flexLastCheek = -9f;          // 지금 벌어진 정도. 소리 쪽이 본다
    private const float LipBulge = 0.0018f;  // 입술을 앞으로 내미는 양
    private Quaternion pivToMesh = Quaternion.identity;  // headPivot → 원본 메시 좌표
    private bool pivToMeshOK;
    private const float LipBand = 0.0095f;   // 다문 선에서 입술 끝까지
    private GameObject food;          // 둘 중 지금 켜져 있는 쪽
    private float tableTopY;          // 집 식탁 상판 높이. 음식이 여기 놓인다
    private Material jellyMat;        // 집어 든 젤리의 재질 — 한 입마다 색이 바뀐다
    private int jellyHue = -1;        // 지금 칠해 둔 무지개 칸 (-1 = 아직)

    /// <summary>
    /// 젤리 색. 한 봉지에서 집어 먹는 것이니 알마다 색이 달라야 한다 —
    /// 빨강 하나로 두면 사과와 같은 색이라 거울에서 두 조건이 구분되지 않았다.
    /// 접시 위에는 이 색이 한 알씩 놓여 있고, 집어 드는 알은 한 입마다
    /// 이 차례를 돌아가며 칠한다.
    /// </summary>
    private static readonly Color[] JellyColors =
    {
        new Color(0.88f, 0.12f, 0.10f),   // 빨강
        new Color(0.95f, 0.47f, 0.06f),   // 주황
        new Color(0.95f, 0.82f, 0.10f),   // 노랑
        new Color(0.26f, 0.72f, 0.26f),   // 초록
        new Color(0.16f, 0.42f, 0.86f),   // 파랑
        new Color(0.58f, 0.26f, 0.78f),   // 보라
    };

    private TextMesh label;
    private float labelUntil;

    private Scenario current = Scenario.None;
    private FoodKind foodKind = FoodKind.Apple;
    private DentalKind dentalKind = DentalKind.Drill;
    private float elapsed;

    private UnityEngine.Rendering.AmbientMode prevAmbientMode;
    private Color prevAmbientLight;
    private Material prevSkybox;
    private bool envSaved;

    private Vector3 templeLocal;      // headPivot 로컬 — 관자놀이
    private Vector3 mouthLocal;       // headPivot 로컬 — 입
    private Vector3 headCenterLocal;
    private float headUnit;          // 목관절~정수리. 머리 크기의 기준자

    // 입은 **얼굴에 뚫은 진짜 구멍**이다 (CarveMouth 참고). 입 자리의
    // 얼굴 삼각형을 잘라 내 위·아래 두 조각으로 떼어 놓고, 그 뒤에 구강을
    // 지었다. 두 조각이 벌어지면 그 사이로 구강이 들여다보인다.
    private Transform mouthJawUp;    // 잘라 낸 얼굴 윗조각 (윗입술까지)
    private Transform mouthJawLow;   // 잘라 낸 얼굴 아랫조각 (턱)
    private Transform mouthCavity;   // 구강 안쪽 벽
    private Transform mouthTeeth;    // 윗니 한 줄
    private Transform mouthTeethLow; // 아랫니 한 줄
    private Transform mouthTongue;   // 혀
    private float holeHalfW, holeHalfH;   // 뚫어 둔 구멍의 반너비·반높이
    private float mouthSkinZ;        // 구멍 자리 얼굴 표면의 z (headPivot 로컬)
    private float holeCenterY;       // 구멍의 세로 한가운데. 입술선보다 아래다
    private System.Collections.Generic.List<Vector3> cavityRing;  // 구멍 테두리 변
    private Vector3 jawHinge;        // 턱이 도는 축 (headPivot 로컬)
    // 제일 크게 벌렸을 때의 틈. 입술을 얇게 하면서 구멍을 6 mm 낮췄더니
    // 보이는 틈도 그만큼 줄어 아랫니가 아랫입술에 가렸다. 그만큼 더 연다.
    private const float JawOpenMax = 0.020f;

    // 다리뼈. 집에서는 접어 앉히고 치과에서는 편다.
    private Transform[] legUpper, legLower, legFoot;
    private Quaternion[] legRestU, legRestL, legRestF;

    private Limb selfArm;            // 참가자 팔 — 음식은 제 손으로 든다
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

    /// <summary>지금 보여 주고 있는 음식의 단단함. 진동 쪽에서 조각 수를 여기에 맞춘다.</summary>
    public FoodKind CurrentFood { get { return foodKind; } }

    /// <summary>
    /// 지금 음식의 진동 조각 수. ScenarioHaptics.BuildCrunch 에 그대로 넘긴다.
    ///
    /// 진동 값을 음식 표(FoodSpecs)에 같이 적어 둔 이유가 여기 있다. 전에는
    /// 실험 러너가 "단단하면 3 조각, 무르면 2 조각" 을 **제 쪽에서** 정했다.
    /// 다섯 가지가 되면 보이는 씹기와 느껴지는 씹기가 두 파일에서 따로
    /// 굴러가는데, 이 연구가 재려는 건 바로 그 둘이 맞는지다.
    /// </summary>
    public int FoodFragments { get { return FragmentsOf(foodKind); } }

    /// <summary>음식 표를 종류로 바로 묻는다. 미리보기(AudioPreview)가 쓴다.</summary>
    public static int FragmentsOf(FoodKind k) { return SpecOf(k).frags; }
    public static float PeakScaleOf(FoodKind k) { return SpecOf(k).peak; }
    public static float BrightnessOf(FoodKind k) { return SpecOf(k).bright; }

    /// <summary>지금 음식의 진동 세기 배율 (0~1). 세기의 윗값에 곱한다.</summary>
    public float FoodPeakScale { get { return PeakScaleOf(foodKind); } }

    /// <summary>
    /// 지금 음식이 깨지는 소리의 밝기 (0~1). ScenarioAudio.PlayCrunch 에 넘긴다.
    ///
    /// 진동 값과 같은 줄에 둔 이유는 같다 — 사탕이 유리처럼 쨍 하고 푸딩이
    /// 둔하게 퍽 하는 건 조각 수·세기와 **같은 축**의 이야기라, 한 군데서
    /// 같이 움직여야 소리와 진동이 어긋나지 않는다.
    /// </summary>
    public float FoodBrightness { get { return BrightnessOf(foodKind); } }

    /// <summary>
    /// 지금 시나리오에 들어온 뒤 흐른 시간 (s). 소리 쪽이 보이는 움직임과
    /// 위상을 맞추는 데 쓴다 — 칫솔 소리는 솔이 좌우로 쓸리는 박자에 맞춰
    /// 커졌다 작아져야 한다 (AnimateDental 의 2.2 Hz).
    /// </summary>
    public float Seconds { get { return elapsed; } }

    /// <summary>
    /// 지금 입이 얼마나 벌어져 있는가 (0 다문 입 ~ 1 활짝).
    ///
    /// 소리 쪽이 쓴다. 씹는 소리는 턱이 **닫히는 순간**에 나야 하는데,
    /// 그 박자는 음식마다 다르고(FoodSpec.rate) 구간마다도 다르다.
    /// 소리 쪽에서 같은 식을 다시 세우는 대신 턱이 지금 어디 있는지를
    /// 넘겨주면, 보이는 턱과 들리는 소리가 어긋날 길이 없다.
    /// </summary>
    public float MouthOpen { get { return mouthOpen; } }

    /// <summary>
    /// 거울 속 사람의 두 눈 한가운데 (리그 좌표).
    ///
    /// 미리보기가 눈을 가까이서 찍을 때 쓴다. 숫자를 저쪽에 적어 두면
    /// 안 된다 — 치과에서는 리그가 통째로 젖혀지고 사람이 눕기 때문에,
    /// 세워 놓고 잰 좌표가 거기서는 턱 속을 가리킨다. 실제로 그렇게
    /// 찍어서 얼굴이 아니라 턱 안쪽이 나왔다.
    ///
    /// 비율은 Editor/FaceProbe 로 잰 값이다 (눈 한가운데가 목관절에서
    /// 위로 0.365, 앞으로 0.459 — 머리 길이를 1 로 봤을 때).
    /// </summary>
    public Vector3 EyeSpot { get { return FaceAt(0f, 0f); } }

    /// <summary>눈을 정면 20 cm 앞에서 볼 카메라 자리 (리그 좌표).</summary>
    public Vector3 EyeCam { get { return FaceAt(0f, 0.20f); } }

    /// <summary>얼굴 위의 한 점 (리그 좌표).</summary>
    /// <param name="up">눈에서 위로 (m). 음수면 입 쪽</param>
    /// <param name="ahead">얼굴에서 앞으로 (m)</param>
    public Vector3 FaceAt(float up, float ahead)
    {
        if (headPivot == null) return Vector3.zero;
        return transform.InverseTransformPoint(
            headPivot.TransformPoint(
                new Vector3(0f, headUnit * 0.365f + up, headUnit * 0.459f + ahead)));
    }

    /// <summary>
    /// 씹기 시작한 뒤 <paramref name="c"/> 초에 턱이 얼마나 벌어져 있는가.
    ///
    /// 돌아가는 중에는 쓰지 않는다 — AnimateFood 가 쓰고, 소리를 미리
    /// 들어 보는 AudioPreview 가 쓴다. 미리듣기가 제 나름의 박자를 꾸며
    /// 쓰면 WAV 는 그럴듯한데 폰에서는 다르게 들리므로, 거울이 턱을
    /// 움직이는 바로 그 식을 빌려 쓰게 한다.
    /// </summary>
    public static float ChewJawOf(FoodKind k, float c)
    {
        FoodSpec fs = SpecOf(k);
        return Mathf.Clamp01(fs.open * ChewBase + fs.depth * Mathf.Sin(c * fs.rate));
    }

    /// <summary>오물거리는 시간 (s). 푸딩 1.1 초 ~ 고기 3.4 초.</summary>
    public static float ChewSecondsOf(FoodKind k) { return SpecOf(k).chew; }

    /// <summary>
    /// 한 입 주기의 길이 (s). 음식마다 다르다 — 질긴 고기가 7.5 초로 가장
    /// 길고 사탕이 5.4 초로 가장 짧다.
    ///
    /// 밖에 내놓는 이유는 미리보기(ToolPreview)다. 음식마다 "입에 닿는
    /// 순간" 을 찍어야 조건이 서로 구분되는지 볼 수 있는데, 그 시각을
    /// 저쪽에 숫자로 적어 두면 표를 고칠 때 엉뚱한 데를 찍는다.
    /// </summary>
    public static float BiteCycleOf(FoodKind k)
    {
        FoodSpec fs = SpecOf(k);
        return OnPlate + ApproachTime + fs.hold + fs.chew + AfterBite;
    }

    /// <summary>한 입 주기에서 입에 닿아 있는 순간 (s). 그림으로 찍을 자리다.</summary>
    public static float BiteMomentOf(FoodKind k)
    {
        return OnPlate + ApproachTime + SpecOf(k).hold * 0.5f;
    }

    /// <summary>지금 입에 닿고 있는 치과 도구. 진동 쪽에서 날카로움을 여기에 맞춘다.</summary>
    public DentalKind CurrentDental { get { return dentalKind; } }

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

        // 위·옆·아래를 다르게 준다. 로우폴리는 면이 넓고 평평해서
        // 주변광이 한 색이면 붙어 다닌 종이처럼 보인다. 천장을 밝게,
        // 바닥을 어둡게 두면 그리지 않은 그림자처럼 아래가 가락진다.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor     = new Color(0.46f, 0.47f, 0.50f);
        RenderSettings.ambientEquatorColor = new Color(0.33f, 0.33f, 0.35f);
        RenderSettings.ambientGroundColor  = new Color(0.17f, 0.16f, 0.16f);
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

        // 방은 상황마다 통째로 다르다. 둘을 미리 지어 두고 켜고 끈다.
        // 넘어갈 때마다 새로 지으면 그 순간 화면이 멎는다.
        rooms = new Transform[3];
        rooms[(int)Scenario.None]   = BuildLobby();
        rooms[(int)Scenario.Food]   = BuildHome();
        rooms[(int)Scenario.Dental] = BuildClinic();
        for (int i = 0; i < rooms.Length; i++)
            if (rooms[i] != null) rooms[i].gameObject.SetActive(false);
        if (rooms[(int)Scenario.None] != null)
            rooms[(int)Scenario.None].gameObject.SetActive(true);

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
    /// 거울 한 장의 모양. 상황마다 다른 거울을 쓴다 —
    /// 집은 전신 거울, 치과는 머리 위 둥근 거울이다.
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
            case Scenario.Dental:
                // 치과 — 치료등에 달린 둥근 거울. 누운 환자 머리 위에 있다.
                //
                // **입만 보이는 크기**다. 거울을 키워 봐야 입이 커지지는
                // 않는다 — 거울은 창문이라, 키우면 입 둘레가 더 보일 뿐이다.
                // 확대는 DentalZoom 이 거울 속 장면을 통째로 키워서 하고,
                // 거울은 그 중 입 둘레가 담길 만큼만 잘라 낸다. 5배로 키운
                // 입이 눈에 약 18도, 이 거울이 약 45도다. 한때 30도까지
                // 좁혔다가 되돌렸다 — 입만 남기는 데는 성공했지만 거울 자체가
                // 시야에서 너무 작은 동전이 됐다. 지금은 입과 입술·턱이
                // 같이 들어오면서도 거울이 시야의 절반을 차지한다.
                //
                // centerY 는 눈높이에서 거울 한가운데까지다. 눈에서 거울 속
                // 입(z ≈ 0.77, y ≈ -0.08)을 잇는 선이 거울면(z = D)을 지나는
                // 높이 — -0.08 × 0.45 / 0.77 ≈ -0.047 — 에 맞춘다.
                m.radius = 0.185f; m.centerY = -0.047f; m.pitch = DentalPitch;
                break;
            default:
                // 집 — 전신 거울. 앉은 눈높이 기준이라 한가운데가 꽤 아래다
                // (SitDrop 참고). 넓게 잡아야 상 위의 손까지 한눈에 들어온다.
                m.w = 0.72f; m.h = 1.34f; m.centerY = -0.28f;
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

    /// <summary>
    /// 받아온 모델 하나를 이쪽 방과 거울 속 방에 한 번씩 놓는다.
    /// Both 와 같은 일인데 기본 도형 대신 Resources 의 모델을 쓴다.
    ///
    /// 거울상을 **음수 스케일로 뒤집지 않는다.** 그러면 면의 감는 방향이
    /// 뒤집혀서 안쪽이 보이고 그림자가 깨진다. 대신 Y 로 180도에서 yaw 를
    /// 뺀 각으로 돌린다 — z 를 뒤집은 것과 방향이 같고 뒤집히지 않는다.
    /// (상자는 180도 돌려도 제 모양이라 Both 의 -yaw 로도 같았지만,
    ///  앞뒤가 있는 모델은 그러면 반대로 돌아 앉는다.)
    ///
    /// size 는 "이 모델을 몇 미터로 쓸지"다. 모델은 미리 기준 축 길이가
    /// 1.0 이 되게 맞춰 뒀다(normalize_obj.py).
    /// </summary>
    private void BothModel(Transform root, string resource, Material mat,
                           Vector3 pos, Vector3 euler, Vector3 size, string name)
    {
        for (int i = 0; i < 2; i++)
        {
            bool m = i == 1;
            GameObject go = AddModel(root, resource, mat);
            if (go == null) return;
            go.name = (m ? "M_" : "R_") + name;
            go.transform.localPosition = new Vector3(pos.x, pos.y, Z(pos.z, m));
            go.transform.localRotation = m
                ? Quaternion.Euler(-euler.x, 180f - euler.y, euler.z)
                : Quaternion.Euler(euler);
            go.transform.localScale = size;
        }
    }

    private void BothModel(Transform root, string resource, Material mat,
                           Vector3 pos, float yaw, Vector3 size, string name)
    {
        BothModel(root, resource, mat, pos, new Vector3(0f, yaw, 0f), size, name);
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

        // 방의 껍데기는 **그림자를 지지 않는다.**
        //
        // 키라이트는 위에서 내리쫬는 방향광인데 이 방은 천장이 닫혀
        // 있다. 그대로 두면 천장이 빛을 통째로 막아서 방 안이 전부
        // 그림자가 된다 — 그러면 그림자 「무늘」이 없어져 켜나 마나다.
        // 껍데기만 캐스팅에서 빼면 빛은 천장을 지나 내려오고, 사람·
        // 의자·접시 같은 안의 물건만 바닥에 그림자를 드리운다.
        // 물리적으로는 말이 안 되지만, 보고 싶은 건 천장등 아래의
        // 접촉 그림자다.
        foreach (Renderer rr in root.GetComponentsInChildren<Renderer>(true))
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    /// <summary>방 뒤쪽 벽 안쪽 면의 z. 소품을 여기에 붙이면 거울에 비친다.</summary>
    private static float BackWall { get { return D - RoomDepth + 0.02f; } }

    /// <summary>
    /// 거울이 걸린 벽. 가운데를 뚫어야 하므로 판 하나가 아니라 위·아래·양옆 네 장이다.
    /// 그 구멍으로 거울 뒤에 지어 둔 방과 머리가 보인다.
    /// 둥근 거울이면 네모로 뚫은 구멍의 네 귀퉁이를 고리로 덮어 동그랗게 만든다.
    /// </summary>
    /// <summary>
    /// 누운 참가자가 **올려다보는 면**을 채운다.
    ///
    /// 거울이 박힌 벽(z = D)이 환자에게는 천장이다. 치료등 고리 바깥이
    /// 통째로 민무늬 회색 판이라, 고개를 조금만 돌려도 화면이 비었다
    /// (mirror_turned.png / mirror_dental_down.png 에서 화면의 2/3).
    /// 식사 장면은 문·액자·화분·식탁으로 방이 꽉 차 있는데 치과만 그래서,
    /// 두 상황의 presence 차이가 진동이 아니라 여기서 날 수 있었다.
    ///
    /// **거울 각도는 안 건드린다.** 거울은 입만 담도록 일부러 좁게
    /// 잡아 둔 것이고(SpecFor / DentalZoom 5배), 각을 돌려 얼굴이
    /// 들어오게 하면 정작 봐야 할 "드릴이 이를 가는 그림"이 사라진다.
    /// 비어 있던 것은 거울이 아니라 그 **둘레**다.
    ///
    /// **Both 를 쓰면 안 된다.** 거울면 바로 뒤(2D-z)에 쌍둥이가 생겨
    /// 거울 구멍을 뒤에서 막는다. 거울 벽 자체도 같은 이유로 한 벌만
    /// 세운다 (BuildMirrorWall).
    /// </summary>
    private void CeilingPanels(Transform root, MirrorSpec m,
                               Material shell, Material steel)
    {
        // 벽보다 뚜렷하게 어두워야 한다. 0.72 로 뒀더니 벽(0.80)과 차이가
        // 10 % 뿐이라 45 cm 앞에서 7 mm 짜리 줄이 아예 안 보였다.
        Material seam = ToolModels.MakeMaterial(new Color(0.60f, 0.63f, 0.65f), 0f, 0.05f);

        // 타일 한 변 20 cm 는 **보이는 각**으로 맞춘 값이다. 진짜 치과
        // 천장은 60 cm 타일이 1.5 m 위에 있어 0.4 rad 쯤인데, 이 벽은
        // 45 cm 앞이라 같은 각이 되려면 0.4 × 0.45 ≈ 0.18 m 다.
        const float Tile = 0.20f;
        const float Clear = 0.40f;     // 치료등과 손잡이가 차지하는 반지름
        const float Half = 1.00f;      // 눈금을 깔 범위. 더 넓혀도 안 보인다
        // **벽은 두께가 2 cm 다** (BuildMirrorWall 의 t). Box 는 가운데로
        // 자리를 잡으므로 벽 앞면은 z = D - 0.01 에 있다. D - 0.004 에
        // 뒀더니 눈금이 통째로 벽 **속에** 박혀서 하나도 안 보였다.
        float z = D - 0.018f;
        float yLo = Mathf.Max(-eyeHeight + 0.05f, m.centerY - Half);
        float yHi = Mathf.Min(CeilUp - 0.05f, m.centerY + Half);

        // 줄을 칸칸이 끊지 않고 **한 줄로 긋되 치료등 자리만 비운다.**
        // 칸마다 상자를 놓으면 250 개가 넘는데, 폰에서는 그게 그대로
        // 드로우콜이고 스테레오라 두 배다.
        for (float y = Mathf.Ceil(yLo / Tile) * Tile; y <= yHi; y += Tile)
        {
            float d = Mathf.Abs(y - m.centerY);
            float w = d < Clear ? Mathf.Sqrt(Clear * Clear - d * d) : 0f;
            Span(root, seam, true, y, -Half, -w, z);
            Span(root, seam, true, y, w, Half, z);
        }
        for (float x = -Half; x <= Half + 1e-3f; x += Tile)
        {
            float d = Mathf.Abs(x);
            float h = d < Clear ? Mathf.Sqrt(Clear * Clear - d * d) : 0f;
            Span(root, seam, false, x, yLo, m.centerY - h, z);
            Span(root, seam, false, x, m.centerY + h, yHi, z);
        }

        // 형광등 한 짝. 치과 천장에서 제일 먼저 눈에 들어오는 물건이고,
        // 빈 회색 면에 **밝기 차이**를 주는 것이 눈금보다 크게 먹는다.
        // 빛을 내는 재질이라 (MakeGlow) 그늘에서도 하얗게 남는다.
        Material tube = ToolModels.MakeGlow(new Color(0.97f, 0.98f, 1f));
        for (int sx = -1; sx <= 1; sx += 2)
            Box(root, tube, new Vector3(sx * 0.56f, m.centerY + 0.34f, z - 0.006f),
                new Vector3(0.52f, 0.14f, 0.010f), "CeilTube" + sx);

        // 환기구. 날 네 장이면 격자로 읽힌다.
        Vector3 vent = new Vector3(0.62f, m.centerY - 0.52f, z - 0.006f);
        Box(root, shell, vent, new Vector3(0.26f, 0.20f, 0.010f), "Vent");
        for (int i = 0; i < 4; i++)
            Box(root, steel,
                vent + new Vector3(0f, 0.070f - i * 0.045f, -0.006f),
                new Vector3(0.23f, 0.012f, 0.004f), "VentFin" + i);
    }

    /// <summary>천장 눈금 한 토막. 비어 있는 구간은 그냥 넘긴다.</summary>
    private void Span(Transform root, Material mat, bool horizontal,
                      float at, float from, float to, float z)
    {
        float len = to - from;
        if (len <= 0.02f) return;
        float mid = (from + to) * 0.5f;
        Box(root, mat,
            horizontal ? new Vector3(mid, at, z) : new Vector3(at, mid, z),
            horizontal ? new Vector3(len, 0.007f, 0.004f)
                       : new Vector3(0.007f, len, 0.004f),
            horizontal ? "CeilSeamH" : "CeilSeamV");
    }

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
    /// 방 하나에 하나만 두는 **그림자 지는 빛.**
    ///
    /// 지금까지 그림자가 아예 없었다. 등이 전부 점광 = URP 의 "추가
    /// 광원" 인데 모바일 파이프라인에 추가 광원 그림자가 꺼져
    /// 있어서다. 그림자를 받으려면 **메인 광원 = 방향광**이어야 한다.
    ///
    /// 그런데 방향광은 자리가 없어서 거울상을 만들 수가 없다 — 이 리그의
    /// 거울은 비치는 게 아니라 방을 한 벌 더 지어 둔 것이라, 빛도 거울면
    /// 기준으로 뒤집힌 짝이 있어야 한다. 점광은 위치를 뒤집어 한 번 더
    /// 놓으면 되지만 방향광은 그렇게 못 한다.
    ///
    /// 해결은 **z 성분이 0 인 방향**만 쓰는 것이다. 위에서 옆(x)으로만
    /// 기울인 빛은 z = D 로 뒤집어도 **자기 자신**이라, 이 방과 거울 속
    /// 방이 띄같은 방향으로 빛을 받는다. 거울이 안 깨진다.
    ///
    /// 그림자는 하드다 (모바일 에셋에 soft 가 꺼져 있다). 대신 그림자
    /// 거리를 9 m 로 좁혀 텍셀을 춘한히 넣고, 진하기를 0.55 로 낮춰
    /// 가장자리 계단이 덜 눈에 띄게 한다.
    /// </summary>
    private void KeyLight(Transform root, float intensity, Color color)
    {
        GameObject go = new GameObject("KeyLight");
        go.transform.SetParent(root, false);
        go.transform.localRotation =
            Quaternion.LookRotation(new Vector3(0.38f, -1f, 0f).normalized);
        Light l = go.AddComponent<Light>();
        l.type = LightType.Directional;
        l.intensity = intensity;
        l.color = color;
        l.shadows = LightShadows.Hard;
        l.shadowStrength = 0.55f;
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

    /// <summary>
    /// 물건이 바닥에 닿은 자리에 까는 **가짜 그림자.** 어두운 원반 한 장이다.
    ///
    /// 진짜 그림자를 켜지 않는 이유는 성능이 아까워서가 아니라 아예 못 켜서다.
    /// 이 리그의 등은 전부 점광 = URP 의 "추가 광원"인데, 모바일 파이프라인
    /// (Settings/Mobile_RPAsset.asset)에 `AdditionalLightShadowsSupported: 0`
    /// 으로 꺼져 있다. Light 쪽에서 아무리 켜도 안 나온다. 켜려면 점광마다
    /// 큐브맵 그림자를 구워야 하는데, 방마다 등이 둘씩 짝지어 있고
    /// (Lamp 참고) Cardboard 는 그걸 양쪽 눈에 두 번 그린다.
    ///
    /// 바닥색을 받아서 그보다 어둡게 칠한다. 고정된 회색으로 두면 나무 바닥
    /// 위에서는 푸르딩딩한 얼룩이 된다.
    /// </summary>
    private void ContactShadow(Transform root, Vector3 pos, float wx, float wz,
                               Color surface, string name)
    {
        // 진짜 그림자가 생긴 뒤로는 **안 깔긴다.**
        //
        // 키라이트(방향광)는 비스듬히 내리쫬니 그림자가 한쪽으로
        // 누워진다. 그 위에 물건 밑마다 동근 원반을 깔아 두면 빛 방향과
        // 안 맞아서 그림자가 아니라 때처럼 보인다.
        //
        // 남겨 두는 이유는 이 방법이 언제든 다시 필요해질 수 있어서다 —
        // 폰에서 그림자 값이 모자라면 이 스위치만 다시 켜면 된다.
        if (!FakeContactShadows) return;

        Material m = ToolModels.MakeMaterial(
            new Color(surface.r * 0.52f, surface.g * 0.50f, surface.b * 0.50f), 0f, 0f);
        Both(root, PrimitiveType.Cylinder, m, pos, Vector3.zero,
             new Vector3(wx, 0.0006f, wz), name);
    }

    /// <summary>
    /// 접시에 담는 것들을 묶어 두는 빈 통. 원점에 두므로 거울 뒤쪽 복제
    /// 좌표 계산(Z)은 그대로 맞는다. 조건이 바뀌면 통째로 켜고 끈다.
    /// </summary>
    private Transform PlateGroup(Transform room, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(room, false);
        return go.transform;
    }

    private Transform NewRoom(string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    // ───────────────────────── 집 ─────────────────────────

    /// <summary>
    /// 아무 상황도 고르지 않았을 때 서 있는 방.
    ///
    /// 전에는 이 방이 **없었다.** 방은 상황별로 하나씩만 지어 두고
    /// `ShowScenario` 가 켜고 끄는데, `Scenario.None` 칸이 비어 있어서
    /// 아무 방도 안 켜졌다. 등도 방에 달려 있으니 같이 꺼져서, 참가자가
    /// 헤드셋을 쓰고 **맨 처음 보는 화면**이 캄캄한 허공에 어둑한 사람
    /// 하나였다. 실험 시작 전 인상이 여기서 결정되는데 그게 암흑이었다.
    ///
    /// 그래서 빈 방 하나를 더 짓는다. 소품은 거의 없다 — 여기는 기다리는
    /// 곳이지 보여 줄 곳이 아니고, 집도 치과도 아닌 중립이어야 다음에 켜질
    /// 방이 새 장면으로 읽힌다.
    /// </summary>
    private Transform BuildLobby()
    {
        Transform r = NewRoom("Room_Lobby");
        MirrorSpec m = SpecFor(Scenario.None);

        // 여기서는 서 있다. eyeHeight 를 건드리지 않는다 (집은 앉은 자세라
        // 방을 세우는 동안만 낮춰 쓴다 — BuildHome 참고).
        Material wall  = ToolModels.MakeMaterial(new Color(0.72f, 0.73f, 0.76f), 0f, 0.07f);
        Material floor = ToolModels.MakeMaterial(new Color(0.40f, 0.36f, 0.33f), 0f, 0.18f);
        Material ceil  = ToolModels.MakeMaterial(new Color(0.88f, 0.88f, 0.89f), 0f, 0.04f);
        Material frame = ToolModels.MakeMaterial(new Color(0.34f, 0.33f, 0.32f), 0f, 0.22f);

        RoomShell(r, wall, floor, ceil);
        BuildMirrorWall(r, wall, frame, m);

        // 걸레받이 — 벽과 바닥이 맞닿는 선이 하나 있어야 방에 깊이가 생긴다.
        BothBox(r, frame, new Vector3(0f, -eyeHeight + 0.06f, BackWall + 0.01f),
                new Vector3(RoomHalfW * 2f, 0.12f, 0.02f), "Base");

        // 발밑 그림자. 거울 속 사람은 z = 2D 에 서 있고, 그건 이쪽 z = 0 의
        // 거울상이다 — Both 가 양쪽에 한 장씩 깔아 준다. 이게 없으면 사람이
        // 바닥에 **서 있는** 게 아니라 떠 있는 것으로 보인다.
        ContactShadow(r, new Vector3(0f, -eyeHeight + 0.002f, 0f),
                      0.78f, 0.56f, floor.color, "BodyShadow");

        // 천장등 하나. 짝을 지어야 거울 속도 같이 밝다 (Lamp 참고).
        // 등은 **하나뿐이다.** 둘을 달았더니 각자의 거울상이 거울 속 천장
        // 거의 같은 자리에 겹쳐서, 둘이 더해진 그 한 점만 하얗게 탔다.
        // 짝은 Lamp 가 알아서 놓는다 — 이 한 줄이 방 양쪽을 다 밝힌다.
        // 천장에 바싹 붙이면 그 자리만 타는 것도 같다 (BuildHome 의 등 참고).
        //
        // 자리는 집과 같다. 가운데(z 0.10)에 달았더니 거울상이 거울 속 사람
        // 머리 바로 위에 떨어져서 정수리가 하얗게 떴다 — 이 모델은 머리
        // 꼭대기가 머리카락이 아니라 **살색**이라 위에서 때리면 다 드러난다.
        // 비스듬히 뒤에서 비춰야 정수리가 그늘에 들어간다.
        KeyLight(r, 0.95f, new Color(1f, 0.98f, 0.94f));
        Lamp(r, new Vector3(-0.40f, CeilUp - 0.50f, -0.55f), 5.6f, 0.58f,
             new Color(0.98f, 0.97f, 0.94f));
        return r;
    }

    private Transform BuildHome()
    {
        Transform r = NewRoom("Room_Home");
        MirrorSpec m = SpecFor(Scenario.Food);

        // 집은 앉은 자세다 (SitDrop 참고). 이 방을 세우는 동안만 눈높이를
        // 앉은 값으로 바꿔 둔다 — 바닥·걸레받이·서랍장이 전부 이 값에 매달려
        // 있어서, 하나씩 고치는 것보다 기준을 바꾸는 쪽이 어긋날 데가 없다.
        // 치과는 누운 자세라 선 눈높이 그대로 써야 하므로 나갈 때 되돌린다.
        float standEye = eyeHeight;
        eyeHeight -= SitDrop;

        Material wall  = ToolModels.MakeMaterial(new Color(0.76f, 0.71f, 0.63f), 0f, 0.06f);
        Material floor = ToolModels.MakeMaterial(new Color(0.47f, 0.33f, 0.21f), 0f, 0.20f);
        Material ceil  = ToolModels.MakeMaterial(new Color(0.84f, 0.82f, 0.78f), 0f, 0.04f);
        Material wood  = ToolModels.MakeMaterial(new Color(0.42f, 0.28f, 0.17f), 0f, 0.24f);
        Material cloth = ToolModels.MakeMaterial(new Color(0.55f, 0.34f, 0.30f), 0f, 0.08f);

        RoomShell(r, wall, floor, ceil);
        // 집 거울은 나무 테두리에 세로로 길다. 현관이나 방에 세워 두는 그 거울이다.
        BuildMirrorWall(r, wall, wood, m);

        // 거울 옆 서랍장 — 거울 앞을 막지 않게 옆으로 비켜 놓는다.
        ContactShadow(r, new Vector3(0.92f, -eyeHeight + 0.002f, D - 0.20f),
                      0.80f, 0.56f, floor.color, "DresserShadow");
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
        ContactShadow(r, new Vector3(1.22f, -eyeHeight + 0.002f, BackWall + 0.29f),
                      0.34f, 0.30f, floor.color, "PotShadow");
        Both(r, PrimitiveType.Cylinder, ToolModels.MakeMaterial(new Color(0.55f, 0.33f, 0.24f), 0f, 0.18f),
             new Vector3(1.22f, -eyeHeight + 0.16f, BackWall + 0.26f), Vector3.zero,
             new Vector3(0.24f, 0.16f, 0.24f), "Pot");
        for (int i = 0; i < 3; i++)
            Both(r, PrimitiveType.Sphere, ToolModels.MakeMaterial(new Color(0.24f, 0.42f, 0.24f), 0f, 0.10f),
                 new Vector3(1.22f + (i - 1) * 0.10f, -eyeHeight + 0.44f + i * 0.08f, BackWall + 0.26f),
                 Vector3.zero, new Vector3(0.30f, 0.26f, 0.30f), "Leaf" + i);

        // 의자. 참가자는 z = 0 에 앉아 있으므로 거울상은 거울 속 사람 밑에 온다.
        // BothModel 이 z 를 2D-z 로 한 번 더 놓아 주니 z = 0 하나면 둘 다 생긴다.
        // 모델은 높이가 1.0 으로 맞춰져 있고 바닥이 y = 0 이다.
        // yaw 180 — 이 모델은 제 -Z 를 본다. 0 으로 두면 등받이가 거울 쪽으로
        // 와서, 거울 속에서 앉은 사람 **앞**에 등받이가 서 있었다.
        BothModel(r, "Models/WoodChair", wood,
                  new Vector3(0f, -eyeHeight, 0.02f), 180f,
                  Vector3.one * 0.955f, "Chair");

        // 식탁. "책상에 앉아 책상 위의 음식을 집어 먹는" 그림으로 정했다.
        //
        // 깊이가 이것밖에 안 되는 이유가 있다. 상은 참가자(z=0)와 거울면(z=D)
        // **사이**에만 있을 수 있다 — 거울을 넘기면 상이 거울을 뚫는다. 그리고
        // 거울상은 2D-z 로 뒤집혀 놓이므로, 상을 깊게 잡으면 거울 속 상의
        // 먼 끝이 z = 2D 에 선 사람의 배를 지난다. 실제로 그렇게 났었다.
        // 그래서 0.12 ~ 0.44 로 자른다 — 거울상은 0.46 ~ 0.78 이 되어
        // 거울 속 사람(0.90) 앞에서 깔끔히 끝난다.
        //
        // 받아온 식탁 모델은 **낮고 넓은** 비율이라(높이가 폭의 0.377) 그대로
        // 쓰면 좌탁이 된다. 위의 깊이 제약까지 있어서 축마다 다르게 늘린다.
        // 식탁은 네모난 물건이라 늘려도 티가 덜 난다.
        // 가로·세로는 **같은 배율로** 늘린다. 가로만 1.10 으로 묶고 세로를
        // 2배로 늘렸더니 다리가 판자가 돼서 식탁이 아니라 궤짝이 됐다.
        // 그래서 폭은 높이에 딸려 나온다 — 2 m 짜리 긴 식탁이 된다.
        // 깊이만은 위의 제약 때문에 따로 눌러야 한다. 앞에서 보는 그림이라
        // 눌린 건 다리가 얇아지는 정도로만 드러난다.
        const float tableH = 0.765f, tableD = 0.32f;
        float tableK = tableH / 0.377f;
        BothModel(r, "Models/Table", wood,
                  new Vector3(0f, -eyeHeight, D - 0.17f), 0f,
                  new Vector3(tableK, tableK, tableD / 0.566f), "Table");
        float tableTop = -eyeHeight + tableH;

        // 음식이 놓이는 접시. AnimateFood 의 집어 드는 자리와 같은 좌표여야 한다.
        tableTopY = tableTop;
        Color plateColor = new Color(0.90f, 0.89f, 0.86f);
        // 접시보다 아주 조금만 크게, 그리고 한쪽으로 밀어 둔다. 사방으로
        // 고르게 삐져나오면 그림자가 아니라 접시 테두리로 읽힌다.
        ContactShadow(r, new Vector3(-0.118f, tableTop + 0.0015f, D - 0.262f),
                      0.212f, 0.212f, wood.color, "PlateShadow");
        BothModel(r, "Models/Plate",
                  ToolModels.MakeMaterial(plateColor, 0f, 0.35f),
                  new Vector3(-0.13f, tableTop, D - 0.25f), 0f,
                  Vector3.one * 0.20f, "Plate");

        // 접시에 담긴 음식. 한가운데는 비워 둔다 — 집어 드는 알이 거기서
        // 뜬다(AnimateFood). 모델 원점이 바닥이라 접시 높이에 그대로 둔다.
        //
        // **조건마다 담는 것이 다르다.** 사과를 집어 먹으면서 접시에는
        // 젤리가 담겨 있으면 그 조건이 무너진다. 다섯 벌을 다 지어 두고
        // ShowScenario 에서 한 벌만 켠다.
        //
        // 알 수와 원 반지름은 표에 있다 (FoodSpecs 주석의 겹침 계산).
        for (int k = 0; k < FoodSpecs.Length; k++)
        {
            FoodSpec fs = FoodSpecs[k];
            Transform g = PlateGroup(r, "Plate" + fs.model);
            Item((FoodKind)k).plate = g;

            for (int i = 0; i < fs.pCount; i++)
            {
                float a = i * (Mathf.PI * 2f / fs.pCount) + 0.4f;
                Vector3 at = new Vector3(-0.13f + Mathf.Cos(a) * fs.pRing,
                                         tableTop + 0.010f,
                                         D - 0.25f + Mathf.Sin(a) * fs.pRing);
                // 젤리만 알마다 색이 다르다 — 한 봉지에서 집어 먹는 것이라
                // 무지개로 담아 둔다. 나머지는 재질을 null 로 줘서 모델이
                // 달고 온 색과 무늬를 그대로 쓴다 (AddModel 참고). 고기·
                // 푸딩·사탕은 색이 Kd 가 아니라 **텍스처**에 들어 있어서,
                // 한 색으로 덮으면 사탕이 새까매진다 (Reskin 참고).
                Material mat = (FoodKind)k == FoodKind.Jelly
                    ? ToolModels.MakeMaterial(
                          JellyColors[(i + 1) % JellyColors.Length], 0f, fs.smooth)
                    : null;
                BothModel(g, "Models/" + fs.model, mat,
                          at, a * Mathf.Rad2Deg + fs.pYaw,
                          Vector3.one * fs.pSize, fs.model + i);
                ContactShadow(g, at + new Vector3(0f, 0.0005f, 0f),
                              fs.pSize * 0.9f, fs.pSize * 0.9f,
                              plateColor, fs.model + "Shadow" + i);
            }
        }

        // 상 위의 컵 — 상이 비어 있으면 식탁으로 안 읽힌다.
        //
        // y 는 -0.385 였다. 그런데 상판이 -0.335 라 컵이 상에 **10 cm 박혀**
        // 있었다. 거울에서 굽 낮은 통으로 보이던 게 그래서다. 원기둥의
        // scale.y 는 반높이라, 바닥을 상판에 놓으려면 상판 + 반높이다.
        ContactShadow(r, new Vector3(0.22f, tableTop + 0.0015f, D - 0.295f),
                      0.105f, 0.105f, wood.color, "CupShadow");
        Both(r, PrimitiveType.Cylinder, cloth,
             new Vector3(0.22f, tableTop + 0.055f, D - 0.30f), Vector3.zero,
             new Vector3(0.075f, 0.055f, 0.075f), "Cup");

        // 집은 등이 하나뿐이고 따뜻하다. 치과보다 어둑해야 집처럼 보인다.
        //
        // 천장에서 20 cm 밑에 세기 1.25 로 매달려 있었다. 점광은 거리 제곱으로
        // 떨어지니 바로 위 천장만 **하얗게 타서** 흰 웅덩이가 됐다. 등을 더
        // 내려 달고 세기를 낮추되 사거리는 늘린다 — 사거리를 늘려야 감쇠가
        // 완만해져서 방이 어두워지지 않는다.
        KeyLight(r, 0.95f, new Color(1f, 0.98f, 0.94f));
        Lamp(r, new Vector3(-0.40f, CeilUp - 0.50f, -0.55f), 5.6f, 0.58f,
             new Color(1f, 0.92f, 0.78f));
        Lamp(r, new Vector3(0.95f, -0.25f, D - 0.40f), 1.9f, 0.70f,
             new Color(1f, 0.88f, 0.70f));

        eyeHeight = standEye;
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
        // 갓을 매단 팔. 거울이 입만 담을 만큼 좁아지면서(SpecFor 참고) 이 팔의
        // **거울상**이 입 앞을 세로로 가로질렀다 — 팔은 거울 앞(z = D-0.03)에
        // 있으니 거울상은 D+0.03 에, 즉 거울과 얼굴 사이에 맺힌다. 물리적으로는
        // 맞지만 정작 봐야 할 것을 가린다. 아래 끝이 갓 바깥에서 끝나도록
        // 통째로 올린다.
        float armLen = 0.60f;
        BothBox(r, steel,
                new Vector3(0f, m.centerY + m.radius * 2.05f + armLen * 0.5f, D - 0.03f),
                new Vector3(0.07f, armLen, 0.07f), "LampArm");

        // 치료등 손잡이. 거울 테두리 좌우에 하나씩, 환자 쪽으로 나와 있다.
        // 거울 둘레가 민무늬 회색 고리뿐이라 "조명"으로도 안 읽혔는데,
        // 이 손잡이가 붙으면 바로 치과 치료등이 된다 — 치과의가 등을
        // 끌어당길 때 잡는 그 막대다.
        float grip = m.radius * 1.75f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            BothBox(r, steel,
                    new Vector3(sx * grip, m.centerY, D - 0.115f),
                    new Vector3(0.035f, m.radius * 1.30f, 0.035f), "LampGrip" + sx);
            BothBox(r, steel,
                    new Vector3(sx * grip, m.centerY + m.radius * 0.62f, D - 0.085f),
                    new Vector3(0.030f, 0.030f, 0.075f), "LampGripArmU" + sx);
            BothBox(r, steel,
                    new Vector3(sx * grip, m.centerY - m.radius * 0.62f, D - 0.085f),
                    new Vector3(0.030f, 0.030f, 0.075f), "LampGripArmD" + sx);
        }

        CeilingPanels(r, m, shell, steel);

        // 머리맡 받침. 거울 속에서 참가자 머리 뒤에 보인다.
        BothBox(r, teal, new Vector3(0f, -0.26f, -0.26f),
                new Vector3(0.34f, 0.26f, 0.13f), "HeadRest");
        BothBox(r, teal, new Vector3(0f, -0.72f, -0.20f),
                new Vector3(0.52f, 0.70f, 0.16f), "ChairBack");

        // 기구 쟁반 — 환자 왼쪽. 거울 속에서 치과의 뒤로 보인다.
        //
        // 전에는 쟁반 위에 작은 원통 세 개를 세워 뒀다. 그게 뭔지 알아볼
        // 수가 없어서 "치과"로 안 읽혔다. 실제 기구 모델을 눕혀 놓는다 —
        // 치경·메스·주사기는 생김새만으로 치과가 되는 물건이다.
        BothBox(r, steel, new Vector3(-0.72f, -0.30f, 0.10f),
                new Vector3(0.40f, 0.02f, 0.30f), "Tray");
        BothModel(r, "Models/DentalMirror", steel,
                  new Vector3(-0.72f, -0.283f, 0.02f), 90f,
                  Vector3.one * 0.17f, "TrayMirror");
        BothModel(r, "Models/Scalpel", steel,
                  new Vector3(-0.72f, -0.283f, 0.09f), 90f,
                  Vector3.one * 0.15f, "TrayScalpel");
        BothModel(r, "Models/Syringe", steel,
                  new Vector3(-0.72f, -0.280f, 0.17f), new Vector3(90f, 90f, 0f),
                  Vector3.one * 0.15f, "TraySyringe");

        // 뒷벽 — 수납장과 세면대. 기울어 보여도 치과 색이면 읽힌다.
        // 아래는 받아온 진료실 수납장, 위는 벽장(모델이 없어 상자 그대로).
        BothModel(r, "Models/LabDesk", shell,
                  new Vector3(-0.70f, -eyeHeight, BackWall + 0.22f), 0f,
                  Vector3.one * 1.45f, "Cabinet");
        BothBox(r, shell, new Vector3(-0.70f, -eyeHeight + 1.30f, BackWall + 0.14f),
                new Vector3(1.30f, 0.52f, 0.28f), "WallCabinet");
        BothBox(r, teal, new Vector3(0.75f, 0.10f, BackWall),
                new Vector3(0.70f, 0.50f, 0.03f), "XrayPanel");

        // ── 고개를 숙이면 민무늬 회색 면만 보이던 문제 ──
        //
        // 이 방은 통째로 35도 젖혀져 있어서, 참가자가 턱을 당기면 시선이 바닥과
        // 끝벽으로 떨어진다. 둘 다 아무 무늬 없는 밝은 회색 판이라 화면이 비어 보였다.
        // 면을 끊어 주는 선을 깔고, 누워 있으면 당연히 보여야 할 것들을 몸 위에 놓는다.

        Material line = ToolModels.MakeMaterial(new Color(0.58f, 0.61f, 0.63f), 0f, 0.06f);

        // 바닥 줄눈. 선이 깔리면 기울어진 것도 깊이도 눈에 들어온다.
        for (int i = 0; i < 5; i++)
            BothBox(r, line,
                    new Vector3(0f, -eyeHeight + 0.005f, D - 0.35f - i * 0.45f),
                    new Vector3(RoomHalfW * 2f, 0.004f, 0.012f), "FloorLine" + i);

        // 걸레받이 — 바닥과 벽이 만나는 자리를 끊어 준다.
        for (int s = -1; s <= 1; s += 2)
            BothBox(r, teal,
                    new Vector3(s * RoomHalfW, -eyeHeight + 0.06f, D - RoomDepth * 0.5f),
                    new Vector3(0.02f, 0.12f, RoomDepth), "Base" + s);
        BothBox(r, teal, new Vector3(0f, -eyeHeight + 0.06f, BackWall),
                new Vector3(RoomHalfW * 2f, 0.12f, 0.02f), "BaseEnd");

        // 시술대와 에이프런은 **이쪽 방에만** 놓는다.
        //
        // 거울 속에는 이미 등받이와 에이프런 색이 들어가 있다. 모자랐던 건 1인칭으로
        // 내려다보는 쪽이다 — 누워 있는데 제 몸도 의자도 없고 바닥만 보였다.
        // Both 가 아니라 Box 를 쓰는 이유가 이것이다. 짝을 지으면 거울 속 사람 앞에
        // 판이 하나 더 생겨 얼굴을 가린다.
        // 종이 에이프런. 시술대와 색이 달라야 "내 가슴 위에 덮인 것" 으로 읽힌다.
        //
        // 처음에 0.40 x 0.30 을 눈 아래 0.32 m 에 뒀더니, 가까워서 화면 아래쪽을
        // 통째로 덮은 데다 치료등을 받아 하얗게 타 버렸다 — 회색 벽이 흰 벽이 됐을 뿐이다.
        // 더 내리고 줄이고 색을 죽여서, 빕 → 시술대 → 팔걸이 → 바닥 줄눈 순으로
        // 겹쳐 보이게 한다. 한 겹이 아니라 여러 겹이라야 면이 아니라 공간으로 읽힌다.
        Material bibMat = ToolModels.MakeMaterial(new Color(0.78f, 0.85f, 0.86f), 0f, 0.05f);
        Box(r, bibMat, new Vector3(0f, -0.40f, 0.20f),
            new Vector3(0.34f, 0.02f, 0.24f), "Bib");

        for (int s = -1; s <= 1; s += 2)
            Box(r, shell, new Vector3(s * 0.33f, -0.44f, 0f),
                new Vector3(0.09f, 0.07f, 0.52f), "ArmRest" + s);

        // 시술대. 밝은 청록 한 판으로 두면 회색 벽이 청록 벽으로 바뀔 뿐이다.
        // 색을 한 단계 죽이고 쿠션 이음매를 넣어 면을 끊는다.
        Material couchMat = ToolModels.MakeMaterial(new Color(0.33f, 0.55f, 0.60f), 0f, 0.10f);
        Material seam     = ToolModels.MakeMaterial(new Color(0.24f, 0.42f, 0.47f), 0f, 0.06f);
        Box(r, couchMat, new Vector3(0f, -0.50f, -0.30f),
            new Vector3(0.52f, 0.06f, 0.80f), "Couch");
        for (int i = 0; i < 3; i++)
            Box(r, seam, new Vector3(0f, -0.468f, -0.05f - i * 0.26f),
                new Vector3(0.52f, 0.004f, 0.012f), "CouchSeam" + i);

        // 얼굴을 비추는 등은 짝을 짓지 않는다.
        //
        // Lamp 는 거울면 기준으로 같은 등을 두 개 놓는다. 그런데 거울 속 얼굴을
        // 가깝게 비추려면 짝이 거울면 바로 앞에 놓이고, 그러면 눈앞 20 cm 의 벽이
        // 통째로 하얗게 탄다. 둘 다 만족하는 자리가 없다.
        //
        // 치료등은 원래 환자를 비추지 이쪽을 비추지 않는다. 그러니 거울 속에만
        // 등이 있는 게 오히려 맞다. 짝 없는 등 하나를 거울 속 얼굴 앞에 둔다.
        //
        // 세기는 0.95 에서 **0.40 으로 낮추고 사거리는 1.6 → 3.0 으로 늘렸다.**
        // 거울 속 장면이 5배(DentalZoom)라 얼굴이 이 등 코앞까지 와 있어서,
        // 이마가 순백으로 타고 이에는 하얀 판때기 같은 반사가 얹혀 있었다.
        // 자리도 0.60 → 0.52 로 **물렸다.** 세기만 낮추면 코끝만 밝고 턱은
        // 묻힌다 — 코와 볼의 거리 차가 그대로라서다. 등을 멀리 두고 사거리를
        // 늘려야 그 차가 줄어 얼굴 전체가 고르게 받는다.
        OneLamp(r, new Vector3(0.16f, 0.26f, 0.52f), 3.0f, 0.40f, new Color(0.95f, 0.98f, 1f));
        KeyLight(r, 1.00f, new Color(1f, 0.99f, 0.97f));
        Lamp(r, new Vector3(-0.60f, CeilUp - 0.50f, -0.60f), 5.6f, 0.62f,
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
        // 입의 z 는 0.51 이었다. 그건 **코끝** 깊이다. 입 자리에 광선을
        // 쏴서 재 보니 얼굴 표면은 0.465 였다 — 입이 얼굴 앞에 1.3 cm 떠
        // 있었다는 뜻이다. 구멍을 뚫으려면 이게 표면에 정확히 붙어야 한다.
        mouthLocal      = new Vector3(0f,          hh * 0.08f,  hh * 0.465f);

        CollectLegs();
        BuildMouth(person);
    }

    /// <summary>
    /// 다리뼈와 그 기본 방향을 모아 둔다. 리그가 치과에서 통째로 젖혀지므로
    /// 기본 방향은 월드가 아니라 **리그 기준**으로 저장한다.
    /// </summary>
    private void CollectLegs()
    {
        string[] side = { "L", "R" };
        legUpper = new Transform[2]; legLower = new Transform[2]; legFoot = new Transform[2];
        legRestU = new Quaternion[2]; legRestL = new Quaternion[2]; legRestF = new Quaternion[2];

        Quaternion inv = Quaternion.Inverse(transform.rotation);
        for (int i = 0; i < 2; i++)
        {
            legUpper[i] = FindBone(charRoot, "UpperLeg." + side[i]);
            legLower[i] = FindBone(charRoot, "LowerLeg." + side[i]);
            legFoot[i]  = FindBone(charRoot, "Foot." + side[i]);
            if (legUpper[i] != null) legRestU[i] = inv * legUpper[i].rotation;
            if (legLower[i] != null) legRestL[i] = inv * legLower[i].rotation;
            if (legFoot[i]  != null) legRestF[i] = inv * legFoot[i].rotation;
        }
        if (legUpper[0] == null)
            Debug.LogWarning("[MirrorRig] 다리뼈(UpperLeg.L)를 못 찾았다 — 앉은 자세를 못 만든다");
    }

    /// <summary>
    /// 앉히거나 편다. 허벅지만 앞으로 눕히고 정강이·발은 제 방향 그대로 둔다.
    ///
    /// 뼈의 로컬 축이 어느 쪽을 향하는지는 모델마다 다르다. 그래서 로컬 회전을
    /// 건드리지 않고 **리그 기준 회전을 직접 박는다** — 허벅지만 돌리고 자식은
    /// 쉴 때 방향으로 되돌리면, 축이 어떻게 생겼든 "허벅지 수평, 정강이 수직"이
    /// 된다. 부모를 먼저 넣어야 자식 값이 덮이지 않는다.
    ///
    /// 머리는 TrackViewer 가 참가자 머리에 붙여 놓으므로, 다리를 접으면 몸이
    /// 올라가는 게 아니라 발이 올라온다. 그래서 집 바닥도 SitDrop 만큼 올렸다.
    /// </summary>
    private void PoseLegs(bool seated)
    {
        if (legUpper == null) return;

        // 거울 속 사람은 참가자를 보고 서 있다(리그 -Z). 무릎이 그쪽으로 나와야
        // 앉은 것이다. 아래(-Y)를 -Z 로 돌리는 축은 리그의 +X 다.
        Quaternion bend = Quaternion.AngleAxis(seated ? 88f : 0f, transform.right);
        for (int i = 0; i < 2; i++)
        {
            if (legUpper[i] != null) legUpper[i].rotation = bend * (transform.rotation * legRestU[i]);
            if (legLower[i] != null) legLower[i].rotation = transform.rotation * legRestL[i];
            if (legFoot[i]  != null) legFoot[i].rotation  = transform.rotation * legRestF[i];
        }
    }

    /// <summary>
    /// 입. **얼굴에 구멍을 뚫어서** 만든다.
    ///
    /// 전에는 메시를 안 건드리고 얼굴 앞면에 몇 mm 두께의 켜를 얹었다.
    /// 그래서 이를 제 깊이로 둘 수가 없었다 — 아치를 세우면 어금니가
    /// 불투명한 머리 속으로 들어가 사라지니까, 앞뒤로 0.5 cm 까지 눌러
    /// 부조로 만들어야 했다. 치과 장면이 5배 확대라 그게 **종잇장 같은
    /// 이**로 드러났다.
    ///
    /// 지금은 입 자리의 얼굴 삼각형을 잘라 내고(CarveMouth), 잘라 낸 것을
    /// 위·아래 두 조각으로 나눠 그대로 되돌려 놓는다. 다문 입에서는 두
    /// 조각이 원래 자리에 있어 **이음매가 없다** — 같은 메시, 같은 재질,
    /// 같은 UV 니까. 벌리면 두 조각이 갈라지고 그 사이로 구강이 보인다.
    /// 입술도 따로 안 만든다. 이 모델은 입술이 텍스처에 그려져 있어서
    /// 잘라 낸 조각이 그걸 그대로 들고 간다.
    /// </summary>
    private void BuildMouth(GameObject person)
    {
        // 구멍 크기. 한 번 뚫으면 못 줄이므로 **제일 크게 벌렸을 때**에
        // 맞춘다 (SetMouth 의 gap·w 최대값). 다문 입은 두 조각이 도로
        // 덮으니 커도 손해가 없다.
        // 가로는 사람 입만 하게(4.8 cm), 세로는 제일 크게 벌렸을 때(3.6 cm).
        //
        // 더 키우고 싶어도 못 키운다. 위로 1 cm 만 더 가면 **코**에 닿고,
        // 옆으로 벌리면 얼굴이 둥글어서 구멍 가장자리와 한가운데의 깊이
        // 차가 커진다 — 구강 벽을 어디에 맞춰 세워도 한쪽이 볼을 뚫고
        // 나온다. 광선을 쏴 재 보니 ±2.4 cm 안에서는 그 차가 7 mm 라
        // 벽 하나로 덮인다.
        holeHalfW = 0.022f;
        // **얇게 만들려고 여기를 줄이면 안 된다.** 입을 벌릴 때 보이는 틈은
        // 턱이 얼마나 열리느냐가 아니라 이 구멍이 얼마나 크냐로 정해진다 —
        // 아래턱 조각은 통째로 구멍 밑으로 내려가 버리기 때문이다. 1.4 cm 로
        // 줄였더니 입술은 얇아졌지만 **아랫니가 안 보였다.** 구멍은 크게
        // 두고, 입술은 다문 선 둘레만 색으로 칠한다 (JawPiece).
        holeHalfH = 0.013f;

        // 구멍의 한가운데는 입술선보다 **8 mm 아래**다. 입술선에 맞췄더니
        // 위쪽 모서리가 **코를 베어 먹었다** — 떼어 낸 윗조각의 앞뒤 두께가
        // 2.8 cm 로 나와서 알았다. 납작한 얼굴 조각이 그렇게 두꺼울 리 없다.
        // 턱은 어차피 아래로 열리니 구멍도 아래로 치우치는 게 맞다.
        holeCenterY = mouthLocal.y - 0.005f;

        CarveMouth(person);
        if (mouthJawLow != null)
        {
            // 턱은 **경첩으로 돈다.** 아래로 밀기만 하면 조각이 턱 앞에
            // 떠서 판자처럼 보인다 — 얼굴이 아래로 갈수록 뒤로 말려
            // 들어가기 때문이다. 귀 아래쯤(위로 2 cm, 뒤로 7.5 cm)에 축을
            // 두면 내려가면서 저절로 뒤로도 물러난다. 사람 턱이 그렇다.
            jawHinge = new Vector3(0f, mouthLocal.y + 0.020f, mouthSkinZ - 0.075f);
            mouthJawLow.localPosition = jawHinge;
            if (mouthJawLow.childCount > 0)
                mouthJawLow.GetChild(0).localPosition = -jawHinge;
        }
        BuildCavity();

        mouthTongue   = MouthPart("Tongue", new Color(0.70f, 0.30f, 0.32f), 0.35f);
        // 이와 잇몸은 받아온 틀니 모델이다 (ToothRow 참고).
        mouthTeeth    = ToothRow("TeethUp",  true);
        mouthTeethLow = ToothRow("TeethLow", false);
        BuildEyes(person);
        SetMouth(0f);
    }

    /// <summary>
    /// 눈꺼풀. **눈의 앞면을 그대로 떠서** 살색으로 덮는다.
    ///
    /// 이 모델에는 눈을 움직일 뼈도 블렌드셰이프도 없다 (Editor/FaceProbe
    /// 로 찍어 확인했다). 대신 눈과 눈썹이 각각 **따로 선 서브메시**라
    /// 그 삼각형만 집어낼 수 있다. 그래서 눈 모양을 베껴 1.5 mm 앞에
    /// 살색으로 띄운다 — 눈꺼풀의 윤곽이 곧 눈의 윤곽이라 어긋날 수가
    /// 없고, 이 모델은 텍스처 없이 재질 색 하나로 칠해져 있어서
    /// (FaceProbe: "텍스처 (없음)") 살색만 가져오면 이음매도 안 보인다.
    ///
    /// 감는 것은 **위 모서리를 축으로 세로로 눌러서** 한다. 눈꺼풀이
    /// 위에서 내려오는 것이니 축도 위에 있어야 한다. 아래에 두면 눈이
    /// 밑에서 차오른다.
    ///
    /// 재질은 **이름으로 못 찾는다.** Standardize 가 받아온 재질을 전부
    /// 갈아 끼우면서 이름이 날아가기 때문이다. 그래서 원본 프리팹에서
    /// 몇 번째 서브메시가 눈인지만 읽어 오고, 색은 살아 있는 쪽에서
    /// 가져온다 — Reskin 이 색은 그대로 들고 간다.
    /// </summary>
    private void BuildEyes(GameObject person)
    {
        SkinnedMeshRenderer head = HeadSkin(person);
        if (head == null || head.sharedMesh == null || headPivot == null) return;

        int eyeSub = SubMeshNamed("Eye");
        if (eyeSub < 0 || eyeSub >= head.sharedMesh.subMeshCount)
        {
            Debug.LogWarning("[MirrorRig] 눈 서브메시를 못 찾아 눈꺼풀을 못 만들었다");
            return;
        }

        // 살색. 머리 재질 가운데 **가장 밝은 것**이 피부다 (살 0.81 >
        // 머리카락 0.29 > 눈썹 0.24 > 눈 0.19). 이름이 날아가도 이건 남는다.
        Material[] mats = head.sharedMaterials;
        Color skin = new Color(0.81f, 0.68f, 0.53f);
        float best = -1f;
        foreach (Material m in mats)
        {
            if (m == null) continue;
            float lum = m.color.r + m.color.g + m.color.b;
            if (lum > best) { best = lum; skin = m.color; }
        }

        // CarveMouth 와 같은 변환 — 구운 자세를 headPivot 좌표로 옮긴다.
        Mesh src = head.sharedMesh;
        Mesh baked = new Mesh();
        head.BakeMesh(baked, true);
        Vector3[] bv = baked.vertices;
        if (bv.Length != src.vertexCount) { Destroy(baked); return; }

        Transform rt = head.transform;
        Vector3[] P = new Vector3[bv.Length];
        for (int i = 0; i < bv.Length; i++)
            P[i] = headPivot.InverseTransformPoint(rt.TransformPoint(bv[i]));

        int[] tri = src.GetTriangles(eyeSub);
        Material lidMat = ToolModels.MakeMaterial(skin, 0f, 0.12f);
        Material glintMat = ToolModels.MakeGlow(new Color(0.95f, 0.97f, 1f));

        for (int side = 0; side < 2; side++)
            eyeLid[side] = OneLid(side == 0, P, tri, lidMat, glintMat);

        BuildFlex(head, P, SubMeshNamed("Eyebrows"));

        // 지은 눈꺼풀은 아직 localScale 이 1 이라 **감은 눈**이다.
        // 한 번 불러서 떠 둔다.
        UpdateFace();
        Kill(baked);
    }

    /// <summary>한쪽 눈꺼풀. 앞을 보는 삼각형만 쓴다.</summary>
    private Transform OneLid(bool left, Vector3[] P, int[] tri,
                             Material lidMat, Material glintMat)
    {
        var keep = new System.Collections.Generic.List<int>();
        for (int i = 0; i < tri.Length; i += 3)
        {
            Vector3 a = P[tri[i]], b = P[tri[i + 1]], c = P[tri[i + 2]];
            Vector3 mid = (a + b + c) / 3f;
            if ((mid.x < 0f) != left) continue;

            // 뒤통수 쪽을 보는 면은 버린다. 눈알은 공이라 절반이 뒤를
            // 보는데, 그것까지 떠다 덮으면 눈꺼풀이 머리 속으로 들어간다.
            if (Vector3.Cross(b - a, c - a).z <= 0f) continue;
            keep.Add(i);
        }
        if (keep.Count == 0) return null;

        // 쓸 꼭짓점의 한가운데와 맨 위. 축을 맨 위에 둬야 위에서 내려온다.
        var map = new System.Collections.Generic.Dictionary<int, int>();
        Vector3 c0 = Vector3.zero;
        float topY = -1e9f, frontZ = -1e9f;
        int n = 0;
        foreach (int i in keep)
            for (int k = 0; k < 3; k++)
            {
                Vector3 v = P[tri[i + k]];
                c0 += v; n++;
                if (v.y > topY) topY = v.y;
                if (v.z > frontZ) frontZ = v.z;
            }
        c0 /= n;

        Vector3 hinge = new Vector3(c0.x, topY, c0.z);
        var vs = new System.Collections.Generic.List<Vector3>();
        var ts = new System.Collections.Generic.List<int>();
        foreach (int i in keep)
            for (int k = 0; k < 3; k++)
            {
                int srcIdx = tri[i + k], dst;
                if (!map.TryGetValue(srcIdx, out dst))
                {
                    dst = vs.Count;
                    map[srcIdx] = dst;
                    // 눈 한가운데를 기준으로 좌우·위아래만 키우고, 앞으로
                    // 1.5 mm 민다. 깊이는 안 키운다 — 키우면 뒤쪽이 머리
                    // 밖으로 빠져나온다.
                    Vector3 v = P[srcIdx];
                    v.x = c0.x + (v.x - c0.x) * LidGrow;
                    v.y = c0.y + (v.y - c0.y) * LidGrow;
                    v.z += LidLift;
                    vs.Add(v - hinge);
                }
                ts.Add(dst);
            }

        Mesh m = new Mesh();
        m.name = left ? "EyeLid.L" : "EyeLid.R";
        m.SetVertices(vs);
        m.SetTriangles(ts, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();

        GameObject pivot = new GameObject(m.name);
        pivot.transform.SetParent(headPivot, false);
        pivot.transform.localPosition = hinge;

        GameObject go = new GameObject(m.name + "_Mesh");
        go.transform.SetParent(pivot.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterial = lidMat;

        // 눈에 빛점 하나. 이게 없으면 까만 구슬이라 눈이 죽어 보인다.
        // 눈꺼풀보다 **뒤에** 둬야 감았을 때 같이 가려진다.
        // 공으로 만든다. 쿼드로 했더니 아무것도 안 보였는데, 유니티
        // 기본 Quad 는 제 -Z 를 보고 있어서 얼굴 쪽으로 돌아앉아 있었다.
        // 공은 어느 쪽을 봐도 같아서 그 실수를 할 수가 없다.
        Prim(headPivot, PrimitiveType.Sphere, glintMat,
             new Vector3(c0.x - Mathf.Sign(c0.x) * 0.003f, c0.y + 0.003f, frontZ),
             Quaternion.identity, Vector3.one * 0.0045f,
             left ? "EyeGlint.L" : "EyeGlint.R");

        return pivot.transform;
    }

    /// <summary>
    /// 눈썹과 볼을 밀 준비. 머리 메시를 한 장 복사해 두고, 어느 꼭짓점이
    /// 얼마나 따라 움직일지를 미리 재 둔다.
    ///
    /// 눈썹은 **제 서브메시**라 어느 꼭짓점인지가 분명하다. 볼은 그냥
    /// 살이라 자리로 고른다 — 입꼬리 바깥 위쪽에 공 하나를 놓고 그 안을
    /// 부드럽게 집는다.
    ///
    /// 스킨드 메시라 꼭짓점이 바인드포즈 좌표에 있다. headPivot 기준으로
    /// 밀고 싶은 방향을 그 좌표로 옮기는 행렬을 한 번 구해 둔다 — 머리가
    /// 돌아가도 둘이 같이 도니 상수다.
    /// </summary>
    private void BuildFlex(SkinnedMeshRenderer head, Vector3[] P, int browSub)
    {
        Mesh src = head.sharedMesh;
        if (src == null || browSub < 0) return;

        Transform[] bones = head.bones;
        int headIdx = -1;
        for (int i = 0; i < bones.Length; i++)
            if (bones[i] == headBone) { headIdx = i; break; }
        if (headIdx < 0)
        {
            Debug.LogWarning("[MirrorRig] 머리뼈가 스킨에 없다 — 눈썹·볼을 못 민다");
            return;
        }

        // 메시 좌표 → headPivot 좌표로 가는 변환. **회전만 떼어 쓰면
        // 안 된다.** 처음에 Quaternion 으로 받았더니 얼굴이 접시처럼
        // 터졌다 — 이 FBX 는 메시 꼭짓점이 월드와 다른 단위라, 바인드
        // 행렬에 배율이 같이 들어 있고 회전만 뽑으면 그게 날아간다.
        // 4 mm 를 밀었는데 5 cm 가 나간 이유가 그것이었다.
        Matrix4x4 meshToPiv = headPivot.worldToLocalMatrix
                            * headBone.localToWorldMatrix * src.bindposes[headIdx];
        Matrix4x4 pivToMesh = meshToPiv.inverse;

        // 맞는 변환인지 **확인하고** 쓴다. 이게 틀리면 얼굴이 조용히
        // 망가지는 게 아니라 터지므로, 터뜨리기 전에 잡는다.
        Vector3 chk = meshToPiv.MultiplyPoint3x4(src.vertices[0]) - P[0];
        if (chk.magnitude > 0.002f)
        {
            Debug.LogWarning("[MirrorRig] 메시↔머리 변환이 안 맞는다 ("
                             + (chk.magnitude * 1000f).ToString("F1")
                             + " mm) — 눈썹·볼을 안 움직인다");
            return;
        }

        flexMesh = Object.Instantiate(src);
        flexMesh.name = src.name + "_Flex";
        head.sharedMesh = flexMesh;
        flexRest = flexMesh.vertices;
        flexNow = (Vector3[])flexRest.Clone();

        // 눈썹 꼭짓점
        var brow = new System.Collections.Generic.HashSet<int>();
        foreach (int i in src.GetTriangles(browSub)) brow.Add(i);

        // 볼은 입 양옆 위쪽. 입이 (0, mouthLocal.y, mouthSkinZ) 근처에
        // 있으니 거기서 좌우로 벌리고 조금 올린다.
        float cx = headUnit * 0.26f;
        Vector3 cheekL = new Vector3(-cx, mouthLocal.y + headUnit * 0.07f, mouthSkinZ * 0.90f);
        float reach = headUnit * 0.13f;

        var idx = new System.Collections.Generic.List<int>();
        var dir = new System.Collections.Generic.List<Vector3>();
        var wb = new System.Collections.Generic.List<float>();
        var wc = new System.Collections.Generic.List<float>();

        for (int i = 0; i < flexRest.Length; i++)
        {
            float b = brow.Contains(i) ? 1f : 0f;

            Vector3 p = P[i];
            Vector3 c = new Vector3(cheekL.x * (p.x < 0f ? 1f : -1f), cheekL.y, cheekL.z);
            float d = Vector3.Distance(p, c);
            float ch = Mathf.SmoothStep(1f, 0f, Mathf.Clamp01(d / reach));
            // 얼굴 앞쪽만. 뒤통수가 같이 부풀면 머리가 커진다.
            if (p.z < headUnit * 0.15f) ch = 0f;

            if (b <= 0f && ch <= 0.01f) continue;

            idx.Add(i);
            wb.Add(b);
            wc.Add(ch);
            // 눈썹은 아래로, 볼은 밖으로(옆·앞). 둘 다 headPivot 기준이라
            // 메시 좌표로 돌려서 적어 둔다.
            Vector3 want = b > 0f
                         ? Vector3.down
                         : new Vector3(Mathf.Sign(p.x) * 0.72f, 0.12f, 0.68f).normalized;
            // 길이까지 같이 옮긴다. 여기서 정규화하면 배율이 도로 날아간다.
            dir.Add(pivToMesh.MultiplyVector(want));
        }

        flexIdx = idx.ToArray();
        flexDir = dir.ToArray();
        flexBrow = wb.ToArray();
        flexCheek = wc.ToArray();
        Debug.Log("[MirrorRig] 움직이는 얼굴 꼭짓점 " + flexIdx.Length
                  + " (눈썹 " + brow.Count + ")");
    }

    /// <summary>
    /// 눈썹과 볼을 민다. brow 1 이면 잔뜩 찡그린 얼굴, cheek 1 이면
    /// 볼이 가득 부푼 얼굴.
    ///
    /// 값이 거의 안 바뀌었으면 넘긴다. 꼭짓점 3282 개를 매 프레임
    /// 올려 보내면 폰에서 초당 2 MB 가 그냥 나간다.
    /// </summary>
    private void SetFlex(float brow, float cheek)
    {
        if (flexMesh == null || flexIdx == null) return;
        if (Mathf.Abs(brow - flexLastBrow) < 0.01f
            && Mathf.Abs(cheek - flexLastCheek) < 0.01f) return;
        flexLastBrow = brow;
        flexLastCheek = cheek;

        for (int k = 0; k < flexIdx.Length; k++)
        {
            int i = flexIdx[k];
            float amt = flexBrow[k] * brow * BrowDrop + flexCheek[k] * cheek * CheekPuff;
            flexNow[i] = flexRest[i] + flexDir[k] * amt;
        }
        flexMesh.vertices = flexNow;
    }

    /// <summary>머리 스킨 렌더러.</summary>
    private static SkinnedMeshRenderer HeadSkin(GameObject person)
    {
        foreach (SkinnedMeshRenderer r in person.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return r;
        return null;
    }

    /// <summary>
    /// 원본 프리팹에서 그 이름의 재질이 몇 번째 서브메시인지 읽는다.
    /// 살아 있는 쪽은 Standardize 가 재질을 갈아 끼워 이름이 없다.
    /// </summary>
    private static int SubMeshNamed(string want)
    {
        GameObject prefab = Resources.Load<GameObject>("Models/Person");
        if (prefab == null) return -1;
        SkinnedMeshRenderer head = HeadSkin(prefab);
        if (head == null) return -1;

        Material[] mats = head.sharedMaterials;
        for (int i = 0; i < mats.Length; i++)
        {
            // "Eye" 를 찾을 때 "Eyebrows" 가 걸리면 안 되므로 꼭 맞을 때만.
            if (mats[i] != null
                && string.Equals(mats[i].name, want,
                                 System.StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 눈을 깜빡인다. 0 이면 뜬 눈, 1 이면 감은 눈.
    ///
    /// 시각을 넣으면 답이 나오는 식으로 짰다 — 다음 깜빡일 때를 들고
    /// 있지 않는다. 미리보기가 시각을 건너뛰며 찍기 때문에
    /// (StepForPreview), 상태를 들고 있으면 찍을 때마다 다른 그림이 나온다.
    /// </summary>
    private static float BlinkAt(float t, bool tense)
    {
        // 사람은 보통 3~4 초에 한 번 깜빡이고, 긴장하면 잦아진다.
        // 치과에서 더 자주 깜빡이는 건 세 진동 패턴에서 **똑같다** —
        // 패턴을 알려 주는 단서가 되면 안 된다.
        float period = tense ? 2.3f : 4.2f;
        int k = Mathf.FloorToInt(t / period);
        float at = k * period + Hash01(k) * (period - BlinkDur);
        float u = (t - at) / BlinkDur;
        if (u < 0f || u > 1f) return 0f;

        // 감는 게 뜨는 것보다 빠르다. 사람 눈이 그렇다.
        return u < 0.35f ? Ease(u / 0.35f) : Ease(1f - (u - 0.35f) / 0.65f);
    }

    /// <summary>k 번째 깜빡임을 0~1 사이 아무 값으로. 돌릴 때마다 같다.</summary>
    private static float Hash01(int k)
    {
        float v = Mathf.Sin(k * 12.9898f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    /// <summary>얼굴에서 매 프레임 움직이는 것들.</summary>
    private void UpdateFace()
    {
        float y = Mathf.Lerp(LidOpen, 1f,
                             BlinkAt(faceTime, current == Scenario.Dental));
        for (int i = 0; i < 2; i++)
            if (eyeLid[i] != null) eyeLid[i].localScale = new Vector3(1f, y, 1f);

        // 눈썹은 **도구가 닿아 있는 동안** 찡그린다. 기계가 이를 갈고
        // 있는데 표정이 그대로면 그 얼굴이 내 얼굴로 안 읽힌다.
        //
        // Contact 는 세 진동 패턴에서 **똑같다.** 그래야 한다 — 눈썹이
        // 패턴마다 다르게 움직이면, "진동이 진짜 같았나" 를 묻는 자리에서
        // 사람들이 눈썹을 보고 답하게 된다. 소리에 걸어 둔 규칙과 같다.
        float brow = current == Scenario.Dental ? Contact : Contact * 0.35f;

        // 볼은 씹을 때 부푼다. 턱이 다물릴수록 가득 찬다 — 입을 벌린
        // 순간에 볼이 불룩하면 음식이 밖에 나와 있는 꼴이다.
        float cheek = current == Scenario.Food
                    ? Contact * (1f - Mathf.Clamp01(mouthOpen / 0.35f))
                    : 0f;
        SetFlex(brow, cheek);
    }
    /// <summary>
    /// 구강 안쪽 벽. **뚫은 구멍의 테두리를 그대로 뒤로 늘여서** 만든다.
    ///
    /// 막지 않으면 구멍으로 머리 속이 들여다보인다. 머리는 껍데기 하나라
    /// 안쪽 면은 뒷면 컬링으로 안 그려지고, 결국 뒤통수 너머 방이 비친다.
    ///
    /// 처음엔 상자 다섯 장으로 둘러 세웠다가 **틈이 생겼다** — 얼굴이
    /// 둥글어서 구멍 가장자리가 한가운데보다 1.3 cm 뒤에 있는데 상자는
    /// 평평하다. 아래에서 올려다보면 시선이 입천장 앞모서리를 넘어 머리
    /// 속으로 빠져서 새까맣게 보였다. 테두리에서 뽑으면 그 틈이 생길 수가
    /// 없다 — 관의 입구가 곧 구멍이니까.
    /// </summary>
    private void BuildCavity()
    {
        GameObject root = new GameObject("MouthCavity");
        root.transform.SetParent(headPivot, false);
        mouthCavity = root.transform;
        if (cavityRing == null || cavityRing.Count < 6) return;

        // 어둡되 살색이 남은 적갈색. 새까맣게 두면 구멍이지 입이 아니다.
        Material inner = ToolModels.MakeMaterial(new Color(0.30f, 0.13f, 0.13f), 0f, 0.10f);

        const float depth = 0.034f;   // 입술에서 목구멍까지
        const float taper = 0.45f;    // 뒤로 갈수록 좁아진다

        Vector3 c = new Vector3(mouthLocal.x, holeCenterY, 0f);
        var vs = new System.Collections.Generic.List<Vector3>();
        var ts = new System.Collections.Generic.List<int>();

        Vector3 back = Vector3.zero;
        int n = cavityRing.Count / 2;
        for (int i = 0; i < n; i++)
        {
            Vector3 a = cavityRing[i * 2], b2 = cavityRing[i * 2 + 1];
            Vector3 ab = Taper(a, c, taper, depth, holeHalfH), bb = Taper(b2, c, taper, depth, holeHalfH);
            back += (ab + bb) * 0.5f;
            Quad(vs, ts, a, b2, bb, ab, c);
        }
        back /= Mathf.Max(n, 1);

        // 입술 안쪽 **깃**. 턱이 돌면 아랫입술 조각이 구멍 테두리에서
        // 떨어져 나가 입꼬리에 쐐기 모양 틈이 벌어진다 — 거기로 치과
        // 의자의 파란 천이 비쳤다. 구강 입구를 한 겹 더 바깥·뒤로 둘러
        // 두면 그 틈이 깃에 막힌다. 얼굴 표면보다 뒤에 있어 정면에서는
        // 안 보인다.
        for (int i = 0; i < n; i++)
        {
            Vector3 a = cavityRing[i * 2], b2 = cavityRing[i * 2 + 1];
            // **두 겹**으로 두른다. 얼굴은 입에서 멀어질수록 빠르게 뒤로
            // 말려서, 한 번에 멀리 보내면 깃이 볼을 뚫고 나온다. 그렇다고
            // 가까이만 두면 테두리에서 조금 먼 곳이 안 받쳐진다. 두 번에
            // 나눠 보내면 얼굴 뒤를 고르게 받친다.
            Vector3 a1 = Flange(a, c, 1.12f, 0.0018f), b1 = Flange(b2, c, 1.12f, 0.0018f);
            Vector3 a2 = Flange(a, c, 1.45f, 0.0075f), b2f = Flange(b2, c, 1.45f, 0.0075f);
            Quad(vs, ts, a1, b1, b2, a, c);
            Quad(vs, ts, a2, b2f, b1, a1, c);
        }

        // 목구멍 — 뒤를 막는다. 안 막으면 뒤통수 너머가 비친다.
        int cen = vs.Count;
        vs.Add(back);
        for (int i = 0; i < n; i++)
        {
            Vector3 ab = Taper(cavityRing[i * 2], c, taper, depth, holeHalfH);
            Vector3 bb = Taper(cavityRing[i * 2 + 1], c, taper, depth, holeHalfH);
            int ia = vs.Count; vs.Add(ab);
            int ib = vs.Count; vs.Add(bb);
            // 목구멍은 입 쪽(+Z)을 봐야 한다.
            if (Vector3.Cross(ab - back, bb - back).z >= 0f)
            { ts.Add(cen); ts.Add(ia); ts.Add(ib); }
            else
            { ts.Add(cen); ts.Add(ib); ts.Add(ia); }
        }

        Mesh m = new Mesh();
        m.name = "MouthCavity";
        m.SetVertices(vs);
        m.SetTriangles(ts, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();

        GameObject go = new GameObject("CavityMesh");
        go.transform.SetParent(mouthCavity, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterial = inner;
    }

    /// <summary>
    /// 0.1 mm 격자로 끊은 자리를 long 하나에 담는다. 자리가 같은 꼭짓점을
    /// 하나로 묶을 때 쓴다.
    ///
    /// 처음엔 흔한 방식대로 축마다 소수를 곱해 XOR 했다. 그랬더니 서로
    /// **다른 자리가 같은 값**으로 겹쳤고, 겹친 자리의 테두리 변이 두 번
    /// 세어져 구강 벽에서 한 장이 빠졌다 — 입꼬리에 실금 같은 구멍이
    /// 나서 그 너머 방이 비쳤다. 좌표를 21 비트씩 **쌓아** 두면 겹칠 수가
    /// 없다 (±100 m 까지).
    /// </summary>
    private static long PosKey(Vector3 v)
    {
        long x = Mathf.Clamp(Mathf.RoundToInt(v.x * 10000f), -1048575, 1048575) + 1048576L;
        long y = Mathf.Clamp(Mathf.RoundToInt(v.y * 10000f), -1048575, 1048575) + 1048576L;
        long z = Mathf.Clamp(Mathf.RoundToInt(v.z * 10000f), -1048575, 1048575) + 1048576L;
        return (x << 42) | (y << 21) | z;
    }

    /// <summary>테두리 한 점을 바깥·뒤로 보낸다 (입술 안쪽 깃).</summary>
    private static Vector3 Flange(Vector3 p, Vector3 c, float outward, float back)
    {
        return new Vector3(c.x + (p.x - c.x) * outward,
                           c.y + (p.y - c.y) * outward,
                           p.z - back);
    }

    /// <summary>
    /// 테두리 한 점을 구강 안쪽 끝으로 보낸다.
    ///
    /// 고르게 좁히면 안 된다. 입천장은 뒤로 말려 들어가지만 **바닥은
    /// 오히려 더 내려가야** 한다 — 턱이 열리면 아랫니가 구멍 아래 모서리
    /// 보다 더 내려가는데, 바닥을 구멍에 맞춰 두면 아랫니가 그 밑으로
    /// 빠져 벽에 가려 사라진다. 실제로 그렇게 사라졌다.
    /// 좌우는 그대로 둔다. 좁히면 어금니가 볼 벽을 뚫고 나온다.
    ///
    /// 다만 위아래가 **dy = 0 에서 이어져야** 한다. 바닥을 통째로 2 cm
    /// 내렸더니 입꼬리(dy ≈ 0)에서 위벽과 아래벽이 2 cm 어긋나 벽이
    /// 세로로 찢어졌고, 그 틈으로 치과 의자의 파란 천이 비쳤다.
    /// 내리는 양을 dy 에 비례시키면 틈이 생길 자리가 없다.
    /// </summary>
    private static Vector3 Taper(Vector3 p, Vector3 c, float k, float depth, float halfH)
    {
        float dy = p.y - c.y;
        float y;
        if (dy > 0f) y = c.y + dy * k;                   // 입천장
        else                                             // 바닥
        {
            // 내리는 양은 **턱이 벌어지는 양에 묶어 둔다.** 전에는 2 cm 로
            // 적어 뒀는데, 나중에 벌림을 2.8 cm 로 키우자 아랫니가 바닥
            // 아래로 빠져 또 사라졌다. 같은 실수를 두 번 했다.
            float t = Mathf.Clamp01(-dy / Mathf.Max(halfH, 1e-4f));
            y = c.y + dy * 1.8f - (JawOpenMax + 0.008f) * t;
        }
        return new Vector3(p.x, y, p.z - depth);
    }

    /// <summary>
    /// 구강 벽 한 장. **한 겹**으로 깔되 감는 방향은 계산해서 정한다.
    ///
    /// 테두리 변의 방향을 모르니 처음엔 앞뒤 두 장을 겹쳐 깔았다. 그랬더니
    /// RecalculateNormals 가 마주 보는 두 노멀을 평균 내서 0 에 가깝게
    /// 만들었고, 구강이 줄무늬 부채처럼 보였다. 면의 노멀이 구강 축
    /// 쪽을 보는지 내적으로 보고, 아니면 뒤집는다.
    /// </summary>
    private static void Quad(System.Collections.Generic.List<Vector3> vs,
                             System.Collections.Generic.List<int> ts,
                             Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 axis)
    {
        Quad(vs, ts, a, b, c, d, axis, true);
    }

    /// <summary>
    /// inward 가 거짓이면 반대로 — 축에서 **멀어지는** 쪽을 보게 깐다.
    /// 구강 벽은 안쪽을 보지만 입술은 바깥을 봐야 한다.
    /// </summary>
    private static void Quad(System.Collections.Generic.List<Vector3> vs,
                             System.Collections.Generic.List<int> ts,
                             Vector3 a, Vector3 b, Vector3 c, Vector3 d,
                             Vector3 axis, bool inward)
    {
        Vector3 mid = (a + b + c + d) * 0.25f;
        Vector3 toAxis = new Vector3(axis.x, axis.y, mid.z) - mid;
        bool flip = Vector3.Dot(Vector3.Cross(b - a, c - a), toAxis) < 0f;
        if (!inward) flip = !flip;

        int i = vs.Count;
        vs.Add(a); vs.Add(b); vs.Add(c); vs.Add(d);
        if (!flip)
        {
            ts.Add(i); ts.Add(i + 1); ts.Add(i + 2);
            ts.Add(i); ts.Add(i + 2); ts.Add(i + 3);
        }
        else
        {
            ts.Add(i); ts.Add(i + 2); ts.Add(i + 1);
            ts.Add(i); ts.Add(i + 3); ts.Add(i + 2);
        }
    }

    /// <summary>
    /// 입 자리의 얼굴 삼각형을 잘라 내고, 잘라 낸 것을 위·아래 두 조각으로
    /// 돌려준다.
    ///
    /// 그냥 지울 수는 없었다. 이 얼굴은 입 자리가 **삼각형 몇 장**으로 덮여
    /// 있고 그 한 장의 변이 6 cm 다 — 입이 5 cm 니까, 한 장만 지워도 턱에서
    /// 볼까지 뚫린다. 그래서 겹치는 삼각형만 3 mm 격자로 **잘게 쪼갠 뒤**
    /// 구멍 안에 드는 조각만 떼어 낸다. 새로 생기는 꼭짓점은 원래 세
    /// 꼭짓점에서 무게좌표로 뽑으므로 위치·노멀·UV·뼈가중치가 다 따라온다.
    ///
    /// 떼어 낸 조각은 버리지 않고 **같은 자리에 그대로** 다시 놓는다.
    /// 다문 입에서 이음매가 안 보이는 건 그래서다 — 같은 메시, 같은 재질,
    /// 같은 UV 다. 살색을 따로 칠해 덮었다면 얼굴에 네모가 하나 떴을 것이다.
    ///
    /// 조각은 스킨드가 아니라 headPivot 에 매단 고정 메시다. 얼굴 메시는
    /// 머리뼈 하나에만 매달려 있어서 머리를 돌려도 모양이 안 바뀐다.
    /// </summary>
    private void CarveMouth(GameObject person)
    {
        SkinnedMeshRenderer head = null;
        foreach (SkinnedMeshRenderer r in person.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r.name.IndexOf("Head", System.StringComparison.OrdinalIgnoreCase) >= 0)
            { head = r; break; }
        if (head == null || head.sharedMesh == null)
        {
            Debug.LogWarning("[MirrorRig] 머리 메시를 못 찾아 입을 못 뚫었다");
            return;
        }

        Mesh src = head.sharedMesh;
        Mesh baked = new Mesh();
        head.BakeMesh(baked, true);
        Vector3[] bv = baked.vertices;
        if (bv.Length != src.vertexCount)
        {
            Debug.LogWarning("[MirrorRig] 구운 메시와 원본의 꼭짓점 수가 다르다");
            return;
        }

        // 자를지 말지는 **구운 자세**로 판단하고, 잘라 낸 결과는 원본
        // 좌표로 쓴다. 스키닝이 선형이라 무게좌표 보간이 양쪽에서 같다.
        Transform rt = head.transform;
        Vector3[] P = new Vector3[bv.Length];
        for (int i = 0; i < bv.Length; i++)
            P[i] = headPivot.InverseTransformPoint(rt.TransformPoint(bv[i]));

        // 노멀도 **같은 좌표계로** 옮겨 둬야 한다. 처음엔 떼어 낸 조각에
        // 원본 메시 좌표의 노멀을 그대로 넣었더니, 자리는 맞는데 빛을
        // 엉뚱하게 받아서 다문 입이 새까만 구멍으로 보였다.
        Vector3[] bn = baked.normals;
        if (bn == null || bn.Length != bv.Length) bn = src.normals;
        Vector3[] PN = new Vector3[bv.Length];
        if (bn != null && bn.Length == bv.Length)
            for (int i = 0; i < bn.Length; i++)
                PN[i] = headPivot.InverseTransformDirection(rt.TransformDirection(bn[i])).normalized;
        else
            for (int i = 0; i < PN.Length; i++) PN[i] = Vector3.forward;

        MakeLipShades(head);

        Vector3[] V = src.vertices;
        Vector3[] N = src.normals;
        Vector2[] UV = src.uv;
        Vector4[] TAN = src.tangents;
        BoneWeight[] BW = src.boneWeights;
        bool hasN = N != null && N.Length == V.Length;
        bool hasUV = UV != null && UV.Length == V.Length;
        bool hasTan = TAN != null && TAN.Length == V.Length;
        bool hasBW = BW != null && BW.Length == V.Length;

        var fv = new System.Collections.Generic.List<Vector3>(V);
        var fn = new System.Collections.Generic.List<Vector3>(hasN ? N : new Vector3[0]);
        var fuv = new System.Collections.Generic.List<Vector2>(hasUV ? UV : new Vector2[0]);
        var ftan = new System.Collections.Generic.List<Vector4>(hasTan ? TAN : new Vector4[0]);
        var fbw = new System.Collections.Generic.List<BoneWeight>(hasBW ? BW : new BoneWeight[0]);
        var fp = new System.Collections.Generic.List<Vector3>(P);
        var fpn = new System.Collections.Generic.List<Vector3>(PN);

        // 나중에 구멍 테두리를 타원 위로 끌어다 붙일 때 쓰는 출신 삼각형.
        // 원본 삼각형의 **안쪽**에서 나온 꼭짓점만 적는다 — 변을
        // 공유하는 꼭짓점은 이웃 삼각형에도 똑같이 있어서, 한쪽만
        // 밀면 둘이 벌어져 틈이 생긴다. 그 틈은 톱니보다 더 눈에 띄다.
        var oA = new System.Collections.Generic.List<int>();
        var oB = new System.Collections.Generic.List<int>();
        var oC = new System.Collections.Generic.List<int>();
        for (int i = 0; i < V.Length; i++) { oA.Add(-1); oB.Add(-1); oC.Add(-1); }

        int subs = src.subMeshCount;
        var faceTris = new System.Collections.Generic.List<int>[subs];
        var cutTris = new System.Collections.Generic.List<int>[subs];
        for (int s = 0; s < subs; s++)
        {
            faceTris[s] = new System.Collections.Generic.List<int>();
            cutTris[s] = new System.Collections.Generic.List<int>();
        }

        float mx = mouthLocal.x, my = holeCenterY;
        float minZ = 1e9f;
        int cut = 0;

        for (int s = 0; s < subs; s++)
        {
            int[] T = src.GetTriangles(s);
            for (int i = 0; i < T.Length; i += 3)
            {
                int ia = T[i], ib = T[i + 1], ic = T[i + 2];
                Vector3 pa = P[ia], pb = P[ib], pc = P[ic];

                // 구멍과 겹치지도 않는 삼각형은 손대지 않는다.
                float loX = Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x));
                float hiX = Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x));
                float loY = Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y));
                float hiY = Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y));
                if (hiX < mx - holeHalfW || loX > mx + holeHalfW ||
                    hiY < my - holeHalfH || loY > my + holeHalfH)
                {
                    faceTris[s].Add(ia); faceTris[s].Add(ib); faceTris[s].Add(ic);
                    continue;
                }

                // 뒤통수 쪽 면은 자르면 안 된다. 구멍은 **얼굴 앞면**에만 난다.
                //
                // 처음엔 꼭짓점 차의 외적으로 앞뒷면을 갈랐다가 **뒤통수를
                // 뚫었다.** 유니티는 시계방향이 앞면이라 그 외적이 면의
                // 반대쪽을 가리킨다. 머리 한가운데(z = 0)보다 앞인지로
                // 가르는 쪽이 감기는 방향에 안 휘둘린다.
                if ((pa.z + pb.z + pc.z) / 3f <= 0f)
                {
                    faceTris[s].Add(ia); faceTris[s].Add(ib); faceTris[s].Add(ic);
                    continue;
                }

                float edge = Mathf.Max(Vector3.Distance(pa, pb),
                             Mathf.Max(Vector3.Distance(pb, pc), Vector3.Distance(pc, pa)));
                int n = Mathf.Clamp(Mathf.RoundToInt(edge / 0.002f), 4, 32);

                // (i, j) 격자점마다 꼭짓점을 한 번만 만든다.
                int[] grid = new int[(n + 1) * (n + 1)];
                for (int gi = 0; gi <= n; gi++)
                    for (int gj = 0; gj <= n - gi; gj++)
                    {
                        float wb = (float)gi / n, wc = (float)gj / n, wa = 1f - wb - wc;
                        grid[gi * (n + 1) + gj] = fv.Count;
                        bool inner = gi > 0 && gj > 0 && gi + gj < n;
                        oA.Add(inner ? ia : -1); oB.Add(ib); oC.Add(ic);
                        fv.Add(V[ia] * wa + V[ib] * wb + V[ic] * wc);
                        fp.Add(pa * wa + pb * wb + pc * wc);
                        fpn.Add((PN[ia] * wa + PN[ib] * wb + PN[ic] * wc).normalized);
                        if (hasN) fn.Add((N[ia] * wa + N[ib] * wb + N[ic] * wc).normalized);
                        if (hasUV) fuv.Add(UV[ia] * wa + UV[ib] * wb + UV[ic] * wc);
                        if (hasTan) ftan.Add(TAN[ia] * wa + TAN[ib] * wb + TAN[ic] * wc);
                        // 뼈가중치는 섞지 않고 제일 가까운 꼭짓점 것을 쓴다.
                        // 얼굴은 머리뼈 하나에만 매달려 있어 어차피 셋이 같다.
                        if (hasBW) fbw.Add(wa >= wb && wa >= wc ? BW[ia] : (wb >= wc ? BW[ib] : BW[ic]));
                    }

                for (int gi = 0; gi < n; gi++)
                    for (int gj = 0; gj < n - gi; gj++)
                    {
                        AddSub(faceTris[s], cutTris[s], fp, ref cut, ref minZ, mx, my,
                               grid[gi * (n + 1) + gj],
                               grid[(gi + 1) * (n + 1) + gj],
                               grid[gi * (n + 1) + gj + 1]);
                        if (gj < n - gi - 1)
                            AddSub(faceTris[s], cutTris[s], fp, ref cut, ref minZ, mx, my,
                                   grid[(gi + 1) * (n + 1) + gj],
                                   grid[(gi + 1) * (n + 1) + gj + 1],
                                   grid[gi * (n + 1) + gj + 1]);
                    }
            }
        }

        if (cut == 0)
        {
            Debug.LogWarning("[MirrorRig] 입 자리에서 잘라 낼 면이 없었다");
            return;
        }
        // ---- 구멍 테두리를 타원 위로 끌어다 붙인다 ----
        //
        // 자를지 말지는 쪼갠 조각의 **무게중심**으로 갈랐다. 그래서 테두리가
        // 2 mm 격자를 따라 톱니로 남는다. 치과 거울은 장면을 5 배로 키워
        // 보여 주므로 그 톱니가 1 cm 짜리로 보인다 — 격자를 더 잘게 쪼개도
        // 톱니는 작아질 뿐 없어지지 않는다.
        //
        // 그래서 경계에 걸친 꼭짓점만 타원 위로 민다. 민 자리의 무게좌표를
        // **원본 삼각형에서 다시 풀면** z·노멀·UV 까지 같이 따라오므로
        // 꼭짓점이 얼굴 표면에서 떨어지지 않는다. 자리만 옮기면 입술선이
        // 표면에서 떠서 얼굴에 금이 간 것처럼 보인다.
        {
            System.Func<Vector3, long> Key = PosKey;

            // 자리마다 "구멍 쪽 삼각형이 썼나(1) / 얼굴 쪽이 썼나(2)".
            // 둘 다면 그 자리가 테두리다.
            var touch = new System.Collections.Generic.Dictionary<long, int>();
            for (int s = 0; s < subs; s++)
            {
                for (int i = 0; i < cutTris[s].Count; i++)
                {
                    long k = Key(fp[cutTris[s][i]]); int m;
                    touch.TryGetValue(k, out m); touch[k] = m | 1;
                }
                for (int i = 0; i < faceTris[s].Count; i++)
                {
                    long k = Key(fp[faceTris[s][i]]); int m;
                    touch.TryGetValue(k, out m); touch[k] = m | 2;
                }
            }

            int moved = 0;
            for (int i = V.Length; i < fp.Count; i++)
            {
                int ia2 = oA[i];
                if (ia2 < 0) continue;                       // 변 위의 꼭짓점은 손대지 않는다
                Vector3 q = fp[i];
                if (q.z <= mouthLocal.z - 0.030f || q.z >= mouthLocal.z + 0.012f) continue;
                int mk;
                if (!touch.TryGetValue(Key(q), out mk) || mk != 3) continue;

                float ex = (q.x - mx) / holeHalfW, ey = (q.y - my) / holeHalfH;
                float r = Mathf.Sqrt(ex * ex + ey * ey);
                if (r < 0.3f || r > 2.5f) continue;
                float tx = mx + (q.x - mx) / r, ty = my + (q.y - my) / r;

                int ib2 = oB[i], ic2 = oC[i];
                Vector3 pa2 = P[ia2], pb2 = P[ib2], pc2 = P[ic2];
                float d = (pb2.y - pc2.y) * (pa2.x - pc2.x) + (pc2.x - pb2.x) * (pa2.y - pc2.y);
                if (Mathf.Abs(d) < 1e-9f) continue;          // xy 에 눕혀 보면 선이 된 삼각형
                float wa = ((pb2.y - pc2.y) * (tx - pc2.x) + (pc2.x - pb2.x) * (ty - pc2.y)) / d;
                float wb = ((pc2.y - pa2.y) * (tx - pc2.x) + (pa2.x - pc2.x) * (ty - pc2.y)) / d;
                float wc = 1f - wa - wb;
                // 타원 위의 그 점이 이 삼각형 밖이면 건드리지 않는다.
                // 억지로 끌어오면 이웃 삼각형을 뚫고 나간다.
                if (wa < -0.02f || wb < -0.02f || wc < -0.02f) continue;

                fv[i] = V[ia2] * wa + V[ib2] * wb + V[ic2] * wc;
                fp[i] = pa2 * wa + pb2 * wb + pc2 * wc;
                fpn[i] = (PN[ia2] * wa + PN[ib2] * wb + PN[ic2] * wc).normalized;
                if (hasN) fn[i] = (N[ia2] * wa + N[ib2] * wb + N[ic2] * wc).normalized;
                if (hasUV) fuv[i] = UV[ia2] * wa + UV[ib2] * wb + UV[ic2] * wc;
                if (hasTan) ftan[i] = TAN[ia2] * wa + TAN[ib2] * wb + TAN[ic2] * wc;
                moved++;
            }
            Debug.Log("[MirrorRig] 입술선 다듬기 — 꼭짓점 " + moved + "개를 타원 위로 옮겼다");
        }

        // ---- 입 주변 음영을 **다시 짓는다** ----
        //
        // 사용자가 "입 위에 선 같은 게 보인다" 고 한 그 선이다. 재어 보니
        // 선 양쪽이 **같은 살색의 밝기만 다른 두 단**이었다 (135,113,87 →
        // 125,104,81). 틈도, 구강이 비친 것도 아니고, 떼어 낸 조각의
        // 경계도 아니다 — 조각을 초록색으로 물들여 렌더해 보니 조각은
        // 선보다 아래에서 시작했다. 순전히 얼굴 메시 안의 음영 단이다.
        //
        // 원인은 원본 머리 모델이 들고 있는 **노멀**이다. 입에서 3 cm 안에
        // 원본 꼭짓점이 스물넷뿐인데 이웃끼리 평균 68.7°(최대 170°)나
        // 꺾여 있고, 자리가 똑같은 꼭짓점을 두 벌 두고 서로 다른 노멀을
        // 준 "각진 모서리" 가 56 군데다. 그 값을 6 cm 짜리 삼각형에 걸쳐
        // 직선으로 펴면 면 안에서는 기울기가 일정하고 **변에서만 꺾인다.**
        // 눈은 그 꺾임을 선으로 읽는다. 치과 거울이 5 배로 키우니 더 또렷.
        //
        // 그 노멀을 손보려 두 번 헛짚었다.
        //  · 이웃끼리 섞기 — 단이 137→125 에서 138→132 로 옅어질 뿐이었다.
        //    각진 모서리의 두 벌은 서로를 "등진 노멀" 로 보고 피해서,
        //    하필 그 선만 살아남는다.
        //  · 자리가 같은 것끼리 합치기 — 더 나빠졌다. 입 안쪽으로 접혀
        //    들어간 꼭짓점(뒤를 보는 노멀)까지 피부에 섞여서 입 위에
        //    87 까지 어두워지는 쐐기가 생겼다.
        //
        // 그래서 들고 있는 노멀을 **믿지 않고 버린다.** 자리만 보고 다시
        // 짓는다 — 꼭짓점마다 1.4 cm 안의 이웃에 평면을 맞춰(최소제곱)
        // 그 평면의 법선을 쓴다. 가중치가 거리에 따라 매끄럽게 줄어드니
        // 법선도 자리에 따라 매끄럽게 변한다. 꺾일 변이 아예 없다.
        //
        // 입 안쪽으로 접힌 꼭짓점은 평면을 기울인다. z 로 걸러 내되
        // 뚝 끊지 않고 **부드럽게** 가중치를 0 으로 내린다 — 뚝 끊으면
        // 끊기는 자리가 또 선이 된다.
        {
            const float REG = 0.042f;    // 다시 지을 범위
            const float EDGE = 0.014f;   // 범위 끝에서 원래 값으로 돌아가는 폭
            const float R = 0.013f;      // 평면을 맞출 이웃 거리

            var idx = new System.Collections.Generic.List<int>();
            for (int i = 0; i < fp.Count; i++)
                if ((fp[i] - mouthLocal).sqrMagnitude < REG * REG) idx.Add(i);

            // headPivot 좌표의 노멀을 원본 메시 좌표로 되돌리는 회전.
            // 입 주변은 전부 머리 뼈 하나에 매달려 있어서 **회전 하나**로
            // 끝난다. 두 쌍(N → PN)이 있으면 그 회전이 정해진다. 정말로
            // 하나인지는 아래에서 잔차를 재서 로그로 남긴다.
            Quaternion back = Quaternion.identity;
            bool haveBack = false;
            if (hasN)
            {
                int ia3 = -1, ib3 = -1;
                float bestCross = 0f;
                var cand = new System.Collections.Generic.List<int>();
                for (int i = 0; i < V.Length && cand.Count < 60; i++)
                    if ((P[i] - mouthLocal).sqrMagnitude < 0.060f * 0.060f) cand.Add(i);
                for (int a = 0; a < cand.Count; a++)
                    for (int b = a + 1; b < cand.Count; b++)
                    {
                        float c2 = Vector3.Cross(N[cand[a]], N[cand[b]]).magnitude;
                        if (c2 > bestCross) { bestCross = c2; ia3 = cand[a]; ib3 = cand[b]; }
                    }
                if (bestCross > 0.25f)
                {
                    Quaternion fromRest = Quaternion.LookRotation(N[ia3], N[ib3]);
                    Quaternion fromPiv = Quaternion.LookRotation(PN[ia3], PN[ib3]);
                    back = fromRest * Quaternion.Inverse(fromPiv);
                    haveBack = true;
                    pivToMesh = back;   // back 이 곧 headPivot → 메시 다
                    pivToMeshOK = true;

                    float worst = 0f;
                    for (int t = 0; t < cand.Count; t++)
                        worst = Mathf.Max(worst, Vector3.Angle(back * PN[cand[t]], N[cand[t]]));
                    Debug.Log("[MirrorRig] 노멀 좌표 되돌리기 — 꼭짓점 " + cand.Count
                        + "개로 잰 잔차 최대 " + worst.ToString("F1") + "°");
                    if (worst > 8f) { haveBack = false; pivToMeshOK = false; }   // 뼈가 하나가 아니다 — 손대지 않는다
                }
            }

            // 1.4 cm 칸으로 묶어 이웃을 찾는다. 안 묶으면 쌍마다 거리를
            // 재서 꼭짓점 수의 제곱만큼 걸린다.
            var cell = new System.Collections.Generic.Dictionary<long,
                       System.Collections.Generic.List<int>>();
            System.Func<Vector3, long> CK = v =>
                ((long)Mathf.FloorToInt(v.x / R) + 32768L) * 4294967296L
              + ((long)Mathf.FloorToInt(v.y / R) + 32768L) * 65536L
              + ((long)Mathf.FloorToInt(v.z / R) + 32768L);
            foreach (int i in idx)
            {
                long k = CK(fp[i]);
                System.Collections.Generic.List<int> list;
                if (!cell.TryGetValue(k, out list))
                { list = new System.Collections.Generic.List<int>(); cell[k] = list; }
                list.Add(i);
            }

            var fit = new Vector3[idx.Count];
            var ok = new bool[idx.Count];
            int given = 0;
            for (int q = 0; q < idx.Count; q++)
            {
                int i = idx[q];
                Vector3 here = fp[i];
                // z = a + b·dx + c·dy 를 최소제곱으로 맞춘다. 얼굴은 입
                // 주변에서 +Z 를 보고 있어 이렇게 높이로 적을 수 있다.
                double s00 = 0, s01 = 0, s02 = 0, s11 = 0, s12 = 0, s22 = 0;
                double t0 = 0, t1 = 0, t2 = 0;
                int cnt = 0;
                for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    System.Collections.Generic.List<int> list;
                    if (!cell.TryGetValue(CK(here + new Vector3(dx, dy, dz) * R), out list))
                        continue;
                    for (int t = 0; t < list.Count; t++)
                    {
                        Vector3 e = fp[list[t]] - here;
                        float d = e.magnitude;
                        if (d >= R) continue;
                        float w = 1f - d / R;
                        w *= w;
                        // 입 안쪽으로 접힌 자리는 서서히 뺀다
                        w *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((e.z + 0.007f) / 0.004f));
                        if (w <= 0f) continue;
                        s00 += w;        s01 += w * e.x;         s02 += w * e.y;
                        s11 += w * e.x * e.x;                    s12 += w * e.x * e.y;
                        s22 += w * e.y * e.y;
                        t0 += w * e.z;   t1 += w * e.x * e.z;    t2 += w * e.y * e.z;
                        cnt++;
                    }
                }
                // 3×3 대칭 연립방정식을 크라메르로 푼다
                double det = s00 * (s11 * s22 - s12 * s12)
                           - s01 * (s01 * s22 - s12 * s02)
                           + s02 * (s01 * s12 - s11 * s02);
                if (cnt < 4 || System.Math.Abs(det) < 1e-18) { fit[q] = fpn[i]; continue; }
                double b = (s00 * (t1 * s22 - s12 * t2)
                          - t0  * (s01 * s22 - s12 * s02)
                          + s02 * (s01 * t2 - t1 * s02)) / det;
                double c = (s00 * (s11 * t2 - t1 * s12)
                          - s01 * (s01 * t2 - t1 * s02)
                          + t0  * (s01 * s12 - s11 * s02)) / det;
                Vector3 nf2 = new Vector3((float)(-b), (float)(-c), 1f).normalized;
                // 높이로 적는 맞춤은 면이 정면을 볼 때만 맞는다. 볼 옆처럼
                // 70° 넘게 누운 자리에서는 믿지 말고 원래 노멀을 둔다.
                if (nf2.z < 0.34f) { fit[q] = fpn[i]; continue; }
                if (Vector3.Dot(nf2, fpn[i]) < 0f) nf2 = -nf2;
                fit[q] = nf2;
                ok[q] = true;
                given++;
            }

            // 평면이 안 맞은 몇 군데를 이웃에게서 받아 온다.
            //
            // 이웃이 넷도 안 되면 연립방정식이 안 풀려서 원본의 엉뚱한
            // 노멀이 그대로 남는다. 그런 자리는 잘 맞은 이웃의 평균을 쓴다.
            //
            // 지금 이 머리에서는 안 맞는 자리가 여섯인데 **한 군데도 못
            // 받아 온다** — 1.4 cm 안에 성한 이웃이 아예 없는, 쪼개지지
            // 않은 성긴 원본 꼭짓점들이라서다. 범위 끝이라 어차피 섞는
            // 양이 0 으로 줄어 화면에는 안 나타난다. 한때 이 여섯이
            // 입 위의 점선이라고 짚었는데, 개수만 우연히 맞았을 뿐
            // 아니었다 (점선의 정체는 JawUpSkinned 에 적어 뒀다).
            // 다른 머리 모델로 갈면 쓸모가 생기므로 남겨 둔다.
            {
                int fixedUp = 0;
                var where = new System.Collections.Generic.Dictionary<int, int>();
                for (int q = 0; q < idx.Count; q++) where[idx[q]] = q;
                var patched = new Vector3[idx.Count];
                for (int q = 0; q < idx.Count; q++)
                {
                    if (ok[q]) { patched[q] = fit[q]; continue; }
                    patched[q] = fit[q];
                    Vector3 here = fp[idx[q]], sum = Vector3.zero;
                    float wsum = 0f;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        System.Collections.Generic.List<int> list;
                        if (!cell.TryGetValue(CK(here + new Vector3(dx, dy, dz) * R), out list))
                            continue;
                        for (int t = 0; t < list.Count; t++)
                        {
                            int j = list[t];
                            int qj = where.ContainsKey(j) ? where[j] : -1;
                            if (qj < 0 || !ok[qj]) continue;
                            float d = (fp[j] - here).magnitude;
                            if (d >= R) continue;
                            float w = 1f - d / R;
                            sum += fit[qj] * w;
                            wsum += w;
                        }
                    }
                    if (wsum > 0f && sum.sqrMagnitude > 1e-12f)
                    { patched[q] = sum.normalized; fixedUp++; }
                }
                for (int q = 0; q < idx.Count; q++) fit[q] = patched[q];
                if (idx.Count - given > 0)
                    Debug.Log("[MirrorRig] 평면이 안 맞은 " + (idx.Count - given)
                        + "군데 중 " + fixedUp + "군데를 이웃에게서 받아 왔다");
            }

            for (int q = 0; q < idx.Count; q++)
            {
                int i = idx[q];
                float dist = (fp[i] - mouthLocal).magnitude;
                // 범위는 **입 둘레로 좌한다.** 얼굴 전체(6 cm)로 넓혔더니
                // 코가 납작한 널빤지가 됐다. 이 모델은 꼭짓점이 3282개뿐이라
                // **노멀이 형태를 대신 지고 있다** — 매끄럽게 펌면 형태까지
                // 같이 사라진다. 도려낸 입 주변만 고치는 게 맞다.
                float w2 = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((REG - dist) / EDGE));
                Vector3 nn = Vector3.Slerp(fpn[i], fit[q], w2).normalized;
                fpn[i] = nn;
                if (hasN && haveBack) fn[i] = (back * nn).normalized;
            }
            Debug.Log("[MirrorRig] 음영 다시 짓기 — 꼭짓점 " + idx.Count + "개 중 "
                + given + "개에 평면을 맞췄다");
        }

        mouthSkinZ = minZ;

        // 얼굴 메시를 갈아 끼운다. 원본은 Resources 의 에셋이라 **반드시**
        // 복제해야 한다 — 그대로 고치면 프로젝트 파일이 바뀐다.
        Mesh face = Object.Instantiate(src);
        face.name = src.name + "_Carved";
        face.triangles = new int[0];
        face.SetVertices(fv);
        if (hasN) face.SetNormals(fn);
        if (hasUV) face.SetUVs(0, fuv);
        if (hasTan) face.SetTangents(ftan);
        if (hasBW) face.boneWeights = fbw.ToArray();
        face.subMeshCount = subs;
        for (int s = 0; s < subs; s++) face.SetTriangles(faceTris[s], s);
        face.RecalculateBounds();
        head.sharedMesh = face;

        // 구멍 테두리. 한 삼각형만 쓰는 변이 테두리다. 구강은 이걸 뒤로
        // 늘여서 만든다 (BuildCavity). 자리가 같은 꼭짓점은 번호가 달라도
        // 하나로 쳐야 한다 — 안 그러면 쪼갠 삼각형들 사이 이음매까지
        // 테두리로 세어서 구강 안에 칸막이가 생긴다.
        cavityRing = BoundaryRing(fp, cutTris, subs);

        // 떼어 낸 조각. 위는 고정, 아래는 턱이라 돌아간다 (SetMouth).
        // 가르는 선은 구멍 한가운데가 아니라 **입술선**이다.
        mouthJawUp = JawUpSkinned(head, fv, fn, fp, fpn, fbw, cutTris, subs, mouthLocal.y);
        mouthJawLow = JawPiece("JawLow", head, fp, fpn, fuv, cutTris, subs, false, mouthLocal.y);

        Object.DestroyImmediate(baked);
        Debug.Log(string.Format("[MirrorRig] 입을 뚫었다 — 떼어 낸 면 {0}장, 구멍 자리 표면 z {1:0.0000}",
                                cut, mouthSkinZ));
    }

    /// <summary>
    /// 뚫은 구멍의 테두리 변을 모은다. 두 점씩 짝지어 돌려준다.
    /// </summary>
    private System.Collections.Generic.List<Vector3> BoundaryRing(
        System.Collections.Generic.List<Vector3> p,
        System.Collections.Generic.List<int>[] tris, int subs)
    {
        // 자리로 꼭짓점을 합친다 (0.1 mm 격자).
        var weld = new System.Collections.Generic.Dictionary<long, int>();
        var id = new System.Collections.Generic.Dictionary<int, int>();
        var pos = new System.Collections.Generic.List<Vector3>();
        var count = new System.Collections.Generic.Dictionary<long, int>();

        System.Func<int, int> W = i =>
        {
            int w;
            if (id.TryGetValue(i, out w)) return w;
            Vector3 v = p[i];
            long key = PosKey(v);
            if (!weld.TryGetValue(key, out w)) { w = pos.Count; weld[key] = w; pos.Add(v); }
            id[i] = w;
            return w;
        };

        for (int s = 0; s < subs; s++)
        {
            var t = tris[s];
            for (int i = 0; i < t.Count; i += 3)
            {
                int a = W(t[i]), b = W(t[i + 1]), c = W(t[i + 2]);
                for (int e = 0; e < 3; e++)
                {
                    int u = e == 0 ? a : (e == 1 ? b : c);
                    int v = e == 0 ? b : (e == 1 ? c : a);
                    long key = (long)Mathf.Min(u, v) * 1000003L + Mathf.Max(u, v);
                    int n;
                    count.TryGetValue(key, out n);
                    count[key] = n + 1;
                }
            }
        }

        var ring = new System.Collections.Generic.List<Vector3>();
        var deg = new System.Collections.Generic.Dictionary<int, int>();
        int odd = 0;
        foreach (var kv in count)
        {
            if (kv.Value > 2) odd++;
            // 한 번만 쓰인 변이 테두리다 — 다만 **홀수 번**으로 세야 한다.
            // 얼굴 메시에는 UV 이음매가 있어서 같은 자리에 꼭짓점이 둘씩
            // 있다. 자리로 묶고 나면 그 이음매의 변 하나가 세 번 쓰인 것으로
            // 나오는데, 그걸 버리면 구강 벽에서 한 장이 빠져 입꼬리에
            // 실금 같은 구멍이 난다.
            if ((kv.Value & 1) == 0) continue;
            int u = (int)(kv.Key / 1000003L), v = (int)(kv.Key % 1000003L);
            ring.Add(pos[u]); ring.Add(pos[v]);
            int d;
            deg.TryGetValue(u, out d); deg[u] = d + 1;
            deg.TryGetValue(v, out d); deg[v] = d + 1;
        }

        // 테두리는 **닫힌 고리**라야 한다. 한 점에 변이 두 개씩 붙지
        // 않으면 그 자리에서 구강 벽이 한 장 빠지고, 실금 같은 구멍으로
        // 머리 너머가 비친다 — 치과 거울에서 그 실금으로 의자의 파란
        // 천이 비쳤다.
        //
        // 받아온 얼굴 메시는 깨끗하지 않다. UV 이음매에 겹친 꼭짓점이
        // 있어서 자리로 묶고 나면 변 하나가 세 번 쓰인 것으로 나온다.
        // 그런 자리를 규칙으로 다 걸러 내려 하기보다, **끊긴 끝끼리
        // 이어 붙인다.** 끝은 가까이 붙어 있으니 가장 가까운 짝을 잇는다.
        var ends = new System.Collections.Generic.List<int>();
        foreach (var kv in deg) if ((kv.Value & 1) == 1) ends.Add(kv.Key);
        int sewn = 0;
        while (ends.Count >= 2)
        {
            int a0 = ends[0];
            ends.RemoveAt(0);
            int best = 0;
            float bd = float.MaxValue;
            for (int i = 0; i < ends.Count; i++)
            {
                float d = (pos[ends[i]] - pos[a0]).sqrMagnitude;
                if (d < bd) { bd = d; best = i; }
            }
            int b0 = ends[best];
            ends.RemoveAt(best);
            ring.Add(pos[a0]); ring.Add(pos[b0]);
            sewn++;
        }

        if (sewn > 0 || odd > 0)
            Debug.Log(string.Format(
                "[MirrorRig] 구멍 테두리 — 변 {0}개 (끊긴 자리 {1}군데를 이었다, 겹친 변 {2}개)",
                ring.Count / 2, sewn, odd));
        else
            Debug.Log("[MirrorRig] 구멍 테두리 닫힘 — 변 " + (ring.Count / 2) + "개");
        return ring;
    }

    /// <summary>쪼갠 작은 삼각형 하나를 얼굴 쪽/구멍 쪽으로 보낸다.</summary>
    private void AddSub(System.Collections.Generic.List<int> keep,
                        System.Collections.Generic.List<int> drop,
                        System.Collections.Generic.List<Vector3> p,
                        ref int cut, ref float minZ, float mx, float my,
                        int a, int b, int c)
    {
        // z 로도 잘라야 한다. x·y 만 보면 같은 자리의 **턱 밑과 목**까지
        // 같이 뜯긴다 — 떼어 낸 아랫조각의 앞뒤 두께가 5.2 cm 로 나와서
        // 알았다. 얼굴 앞면만 남기는 띠를 둔다.
        //
        // 네모가 아니라 **타원**으로 뚫는다. 입술을 따로 안 만들었으니
        // 이 테두리가 곧 입술선이다 — 네모로 뚫으면 입이 아니라 창문이 된다.
        Vector3 mid = (p[a] + p[b] + p[c]) / 3f;
        float ex = (mid.x - mx) / holeHalfW, ey = (mid.y - my) / holeHalfH;
        if (ex * ex + ey * ey < 1f
            && mid.z > mouthLocal.z - 0.030f && mid.z < mouthLocal.z + 0.012f)
        {
            drop.Add(a); drop.Add(b); drop.Add(c);
            cut++;
            if (mid.z < minZ) minZ = mid.z;
        }
        else { keep.Add(a); keep.Add(b); keep.Add(c); }
    }

    /// <summary>
    /// 윗조각은 **얼굴과 똑같이 스키닝해서** 단다.
    ///
    /// 치과 화면에서 윗입술을 가로질러 흰 점선 호가 그어졌다. 구강을
    /// 자홍색으로 물들여 보니 그 선은 자홍색이 아니었다 — 입 속이 비친 게
    /// 아니라 **머리 너머 천장등**이 비쳤다는 뜻이다. 재어 보니 입 주변
    /// 꼭짓점 24개 중 8개가 뼈를 둘 이상 섞어 쓰고 있었다 (주 가중치가
    /// 최저 0.921). 얼굴 메시는 그 가중치대로 휘는데 윗조각은 headPivot 에
    /// 그냥 매달린 고정 메시였다. 그래서 고개를 젖히는 치과 장면에서만
    /// 둘이 어긋나 테두리가 벌어졌고, 그 틈으로 밝은 방이 보였다.
    ///
    /// 윗조각은 어차피 **움직이지 않는다** (턱을 따라 도는 건 아랫조각뿐).
    /// 그러니 얼굴과 같은 뼈·같은 가중치로 스키닝해 주면 얼굴이 어떻게
    /// 휘든 꼭짓점이 똑같이 따라가서 틈이 생길 수가 없다. 좌표도 그래서
    /// headPivot 이 아니라 **원본 메시 좌표**(fv·fn)로 넣는다.
    /// </summary>
    private Transform JawUpSkinned(SkinnedMeshRenderer head,
                                   System.Collections.Generic.List<Vector3> v,
                                   System.Collections.Generic.List<Vector3> n,
                                   System.Collections.Generic.List<Vector3> piv,
                                   System.Collections.Generic.List<Vector3> pn,
                                   System.Collections.Generic.List<BoneWeight> bw,
                                   System.Collections.Generic.List<int>[] tris,
                                   int subs, float my)
    {
        var map = new System.Collections.Generic.Dictionary<int, int>();
        var vs = new System.Collections.Generic.List<Vector3>();
        var ns = new System.Collections.Generic.List<Vector3>();
        var us = new System.Collections.Generic.List<Vector2>();
        var ws = new System.Collections.Generic.List<BoneWeight>();
        var vp = new System.Collections.Generic.List<Vector3>();   // 뼈를 못 쓸 때용
        var np = new System.Collections.Generic.List<Vector3>();
        var outTris = new System.Collections.Generic.List<int>();
        int total = 0;

        for (int s = 0; s < subs; s++)
        {
            var t = tris[s];
            for (int i = 0; i < t.Count; i += 3)
            {
                float midY = (piv[t[i]].y + piv[t[i + 1]].y + piv[t[i + 2]].y) / 3f;
                if (midY < my) continue;                 // 아랫조각은 턱이 가져간다
                for (int k = 0; k < 3; k++)
                {
                    int srcI = t[i + k], dst;
                    if (!map.TryGetValue(srcI, out dst))
                    {
                        dst = vs.Count;
                        map[srcI] = dst;
                        float lip = LipAmount(piv[srcI], my);
                        Vector3 p = v[srcI];
                        // 입술을 앞으로 내미는 건 headPivot 좌표의 +Z 다.
                        // 메시 좌표로는 방향이 달라서 회전을 태워 보낸다.
                        if (pivToMeshOK)
                            p += pivToMesh * new Vector3(0f, 0f, LipBulge * lip);
                        vs.Add(p);
                        vp.Add(piv[srcI] + new Vector3(0f, 0f, LipBulge * lip));
                        us.Add(new Vector2(0.5f, 1f - lip));
                        if (n.Count > srcI) ns.Add(n[srcI]);
                        if (pn != null && pn.Count > srcI) np.Add(pn[srcI]);
                        if (bw != null && bw.Count > srcI) ws.Add(bw[srcI]);
                    }
                    outTris.Add(dst);
                }
                total++;
            }
        }
        if (total == 0) return null;

        Mesh m = new Mesh();
        m.name = "JawUp";
        m.SetVertices(vs);
        if (ns.Count == vs.Count) m.SetNormals(ns);
        m.SetUVs(0, us);
        m.subMeshCount = 1;
        m.SetTriangles(outTris, 0);
        bool skinned = ws.Count == vs.Count && head.sharedMesh != null
                    && head.sharedMesh.bindposes != null
                    && head.sharedMesh.bindposes.Length > 0;
        if (skinned)
        {
            m.boneWeights = ws.ToArray();
            m.bindposes = head.sharedMesh.bindposes;
        }
        m.RecalculateBounds();
        if (ns.Count != vs.Count) m.RecalculateNormals();

        GameObject go = new GameObject("JawUp");
        if (skinned)
        {
            // 얼굴 렌더러와 **같은 자리**에 둔다. 스키닝은 뼈가 하니
            // 자리 자체는 거의 쓰이지 않지만, 경계 상자 계산이 어긋나면
            // 화면 밖이라고 판단해 통째로 안 그려 버린다.
            // 스키닝은 뼈가 하니 이 객체의 자리 자체는 안 쓰인다.
            // 그래도 **예전과 같은 자리**(headPivot 아래)에 둔다 —
            // 얼굴 렌더러의 부모 밑에 달았더니 치과 장면에서만
            // 사라졌다. 장면마다 그 위 계층이 다르기 때문이다.
            go.transform.SetParent(headPivot, false);

            SkinnedMeshRenderer smr = go.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = m;
            smr.bones = head.bones;
            smr.rootBone = head.rootBone;
            smr.sharedMaterial = lipMatUp;
            smr.updateWhenOffscreen = true;
            smr.localBounds = new Bounds(m.bounds.center,
                                        m.bounds.size + Vector3.one * 0.10f);
            smr.quality = SkinQuality.Bone4;
        }
        else
        {
            // 뼈를 못 구했다 — 예전처럼 headPivot 에 고정으로 단다.
            // 좌표계가 다르니 headPivot 좌표를 쓴다.
            Debug.LogWarning("[MirrorRig] 윗조각을 스키닝하지 못했다 — 고정 메시로 단다");
            go.transform.SetParent(headPivot, false);
            m.SetVertices(vp);
            if (np.Count == vp.Count) m.SetNormals(np);
            m.RecalculateBounds();
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterial = lipMatUp;
        }
        return go.transform;
    }

    /// <summary>
    /// 떼어 낸 면 중 위(또는 아래) 절반을 고정 메시로 만들어 headPivot 에 단다.
    /// 좌표는 headPivot 로컬이라 머리를 돌려도 같이 돈다.
    /// </summary>
    private Transform JawPiece(string name, SkinnedMeshRenderer head,
                               System.Collections.Generic.List<Vector3> p,
                               System.Collections.Generic.List<Vector3> n,
                               System.Collections.Generic.List<Vector2> uv,
                               System.Collections.Generic.List<int>[] tris,
                               int subs, bool upper, float my)
    {
        var map = new System.Collections.Generic.Dictionary<int, int>();
        var vs = new System.Collections.Generic.List<Vector3>();
        var ns = new System.Collections.Generic.List<Vector3>();
        var us = new System.Collections.Generic.List<Vector2>();

        // 조각 전체를 **한 장**으로 그린다. 색은 재질이 아니라 그러데이션
        // 그림이 낸다 — 삼각형 단위로 재질을 갈랐더니 입술과 피부의
        // 경계가 2 mm 짜리 톱니 계단으로 드러났다. 치과 거울은 장면을
        // 5 배로 키우므로 그 계단이 그대로 보인다.
        //
        // 꼭짓점마다 "입술다움"을 재서 UV 로 적어 둔다. 띠 바깥에서는
        // 그림의 색이 **얼굴 재질 색과 똑같아서** 이음매가 아예 없다.
        var outTris = new System.Collections.Generic.List<int>();
        int total = 0;

        for (int s = 0; s < subs; s++)
        {
            var t = tris[s];
            for (int i = 0; i < t.Count; i += 3)
            {
                float midY = (p[t[i]].y + p[t[i + 1]].y + p[t[i + 2]].y) / 3f;
                if ((midY >= my) != upper) continue;

                for (int k = 0; k < 3; k++)
                {
                    int src = t[i + k], dst;
                    if (!map.TryGetValue(src, out dst))
                    {
                        dst = vs.Count;
                        map[src] = dst;
                        float lip = LipAmount(p[src], my);
                        // 입술 한가운데를 앞으로 살짝 내민다. 타원
                        // 테두리에서는 0 이라 입꼬리에 틈이 안 생긴다.
                        Vector3 v = p[src];
                        // 윗조각과 **같은 만큼만** 내민다. 한쪽만 내밀면
                        // 다문 입에 1.8 mm 짜리 턱이 진다.
                        if (pivToMeshOK) v.z += LipBulge * lip;
                        vs.Add(v);
                        us.Add(new Vector2(0.5f, 1f - lip));
                        if (n.Count > src) ns.Add(n[src]);
                    }
                    outTris.Add(dst);
                }
                total++;
            }
        }
        if (total == 0) return null;

        Mesh m = new Mesh();
        m.name = name;
        m.SetVertices(vs);
        if (ns.Count == vs.Count) m.SetNormals(ns);
        m.SetUVs(0, us);
        m.subMeshCount = 1;
        m.SetTriangles(outTris, 0);
        m.RecalculateBounds();
        if (ns.Count != vs.Count) m.RecalculateNormals();

        GameObject pivot = new GameObject(name);
        pivot.transform.SetParent(headPivot, false);
        GameObject go = new GameObject(name + "_Mesh");
        go.transform.SetParent(pivot.transform, false);
        go.AddComponent<MeshFilter>().sharedMesh = m;
        go.AddComponent<MeshRenderer>().sharedMaterial = upper ? lipMatUp : lipMatLow;
        return pivot.transform;
    }

    /// <summary>
    /// 이 자리가 얼마나 입술인가. 1 이면 입술 한가운데, 0 이면 그냥 피부.
    ///
    /// 다문 선에서 멀어질수록, 그리고 뚫은 타원의 테두리에 가까워질수록
    /// 0 으로 간다. 테두리에서 0 이라야 입꼬리가 피부로 자연스럽게
    /// 사라지고, 앞으로 미는 양도 거기서 0 이 되어 틈이 안 생긴다.
    /// </summary>
    private float LipAmount(Vector3 v, float my)
    {
        float near = Mathf.Clamp01(1f - Mathf.Abs(v.y - my) / LipBand);
        float ex = (v.x - mouthLocal.x) / holeHalfW;
        float ey = (v.y - holeCenterY) / holeHalfH;
        float inside = Mathf.Clamp01((1f - Mathf.Sqrt(ex * ex + ey * ey)) / 0.30f);
        // near 에는 SmoothStep 을 걸지 않는다. 걸면 입술색이 꽉 차는
        // 자리가 실선처럼 좁아져 입술이 아니라 빨간 줄이 된다.
        return near * Mathf.SmoothStep(0f, 1f, inside);
    }

    /// <summary>
    /// 입술 재질을 **얼굴 재질에서** 만든다. 색을 따로 적어 두면 피부와
    /// 미묘하게 어긋나 입술이 덧붙인 스티커처럼 보인다. 얼굴 재질을
    /// 복제하면 셰이더·거칠기까지 그대로라 빛을 똑같이 받는다.
    ///
    /// 색은 세로 그러데이션 한 장이 낸다 — 위쪽 끝이 피부색, 아래쪽 끝이
    /// 입술색. 꼭짓점이 적어 둔 UV 가 그 사이를 고르게 메운다.
    /// </summary>
    private void MakeLipShades(SkinnedMeshRenderer head)
    {
        Material[] face = head != null ? head.sharedMaterials : null;
        Material src = face != null && face.Length > 0 ? face[0] : null;
        Color skin = src != null ? src.color : new Color(0.808f, 0.678f, 0.525f);
        if (skin.r > 0.93f && skin.g > 0.93f && skin.b > 0.93f)
            skin = new Color(0.808f, 0.678f, 0.525f);
        Debug.Log("[MirrorRig] 얼굴 재질 " + skin + " / "
                  + (src != null ? src.shader.name : "(없음)")
                  + " / 텍스처 " + (src != null && src.mainTexture != null ? "있음" : "없음"));

        // 살색에서 붉은 쪽으로만 당긴다. 초록·파랑을 내리면 탁해지지
        // 않으면서 혈색이 돈다.
        // 입술 색을 조금만 더 넣는다. 빨강을 올리는 게 아니라
        // 초록·파랑을 내려야 피부와 같은 계열로 짙어진다.
        Color deep = new Color(skin.r * 0.98f, skin.g * 0.74f, skin.b * 0.72f);
        lipMatUp  = LipMaterial(src, skin, deep * 0.94f, "LipUp");   // 윗입술은 그늘진다
        lipMatLow = LipMaterial(src, skin, deep, "LipLow");
    }

    private Material LipMaterial(Material src, Color skin, Color deep, string name)
    {
        const int N = 32;
        Texture2D tex = new Texture2D(1, N, TextureFormat.RGBA32, false);
        tex.name = name + "_Ramp";
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        for (int i = 0; i < N; i++)
        {
            // v = 0 이 입술 한가운데, v = 1 이 피부. LipAmount 가 1 - lip 를
            // UV 로 넣으므로 방향이 이렇게 맞는다.
            float t = i / (float)(N - 1);
            tex.SetPixel(0, i, Color.Lerp(deep, skin, Mathf.SmoothStep(0f, 1f, t)));
        }
        tex.Apply();

        Material m = src != null
            ? new Material(src)
            : ToolModels.MakeMaterial(Color.white, 0f, 0.25f);
        m.name = name;
        m.color = Color.white;                      // 색은 그림이 낸다
        m.mainTexture = tex;
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        return m;
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

    /// <summary>
    /// 이 한 줄 — 받아온 틀니 모델에서 가져온다.
    /// (poly.pizza "Teeth", Poly by Google, CC-BY)
    ///
    /// 전에는 상자를 가로로 늘어놓아 만들었다. 상자로는 이 모양이 안
    /// 나온다 — 폭·길이를 자리마다 바꾸고 잇몸 유두까지 넣어 봤지만
    /// 가까이서 보면 결국 흰 판에 톱니 자국이다. 이건 상자를 더 잘
    /// 쌓아서 풀 문제가 아니라 모델이 있어야 하는 문제였다.
    ///
    /// 모델은 미리 맞춰 뒀다(scratchpad/tools/normalize_obj.py):
    ///   - 좌우 폭 1.0, 씹는 면이 y = 0, +Z 가 앞(앞니)
    ///   - 윗니는 y 양수, 아랫니는 y 음수
    ///   - 이와 잇몸이 **다른 파일**이라 재질을 따로 준다. 한 파일로 두면
    ///     Unity 의 OBJ 가져오기가 그룹을 하나로 합쳐서 이와 잇몸을
    ///     갈라낼 수 없다.
    /// 그래서 여기서는 localScale 에 입 크기만 넣으면 된다.
    /// </summary>
    private Transform ToothRow(string name, bool upper)
    {
        GameObject row = new GameObject(name);
        // 아랫니는 턱에 매단다. 입을 벌리면 턱과 같이 돌아 내려가야 한다 —
        // headPivot 에 매달아 두고 따로 내리면 치열이 턱을 뚫고 지나간다.
        row.transform.SetParent(!upper && mouthJawLow != null ? mouthJawLow : headPivot, false);

        string side = upper ? "Upper" : "Lower";
        // 번들거림(0.72)을 0.46 으로 낮췄다. 치료등이 코앞이라 이 하나하나가
        // 아니라 하얀 판 하나로 뭉개져 보였다 — 낱개로 읽히려면 반사보다
        // 이 사이의 그늘이 살아야 한다.
        Material enamel = ToolModels.MakeMaterial(new Color(0.94f, 0.93f, 0.89f), 0f, 0.62f);
        Material gum    = ToolModels.MakeMaterial(new Color(0.66f, 0.38f, 0.38f), 0f, 0.28f);
        AddModel(row.transform, "Models/Teeth" + side + "Enamel", enamel);
        AddModel(row.transform, "Models/Teeth" + side + "Gum",    gum);
        return row.transform;
    }

    /// <summary>
    /// Resources 의 모델을 parent 밑에 그대로 붙이고 재질을 덮어쓴다.
    ///
    /// 재질을 덮어쓰는 이유: OBJ 와 같이 온 .mtl 은 Standard 셰이더로
    /// 들어오는데, 이 프로젝트는 URP 를 품질 단계에만 걸어 둬서
    /// 에디터에서는 멀쩡해 보이다가 **아이폰 빌드에서 분홍색 오류
    /// 셰이더로 나온다.** 런타임 재질(RuntimeBase)로 통일해야 안전하다.
    /// </summary>
    private GameObject AddModel(Transform parent, string resource, Material mat)
    {
        GameObject src = Resources.Load<GameObject>(resource);
        if (src == null)
        {
            Debug.LogWarning("모델을 못 찾았다: " + resource);
            return null;
        }
        GameObject go = Instantiate(src, parent, false);
        go.name = resource.Substring(resource.LastIndexOf('/') + 1);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Kill(c);
        if (mat != null)
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                r.sharedMaterial = mat;
        else
            Reskin(go);   // 색은 그대로 두고 셰이더만 바꿔 끼운다
        return go;
    }

    /// <summary>
    /// 받아온 모델의 재질을 런타임 재질로 바꿔 끼운다. 색은 그대로 가져온다.
    ///
    /// OBJ 와 함께 오는 .mtl 재질은 Standard 셰이더다. 이 프로젝트는 URP 를
    /// 품질 단계에만 걸어 둬서 **에디터에서는 멀쩡해 보이는데** 아이폰
    /// 빌드에서는 전부 분홍색 오류 셰이더로 나온다. 한 가지 색으로 덮어
    /// 버리면 사과 꼭지 같은 두 번째 색이 날아가므로, 재질마다 색을
    /// 읽어서 같은 색의 런타임 재질을 만든다.
    /// </summary>
    private static void Reskin(GameObject go)
    {
        // 불러온 모델을 전부 같은 0.35 로 덮어쓰면 사과든 빵이든
        // 같은 플라스틱으로 보인다. 이름으로 갈라 반질거림만 달리 준다.
        string n = go.name.ToLowerInvariant();
        float smooth = 0.30f;
        if (n.Contains("bread")) smooth = 0.05f;      // 빵은 빛을 거의 안 튀긴다
        else if (n.Contains("brush")) smooth = 0.34f;
        // 음식은 표에 값이 있다. 같은 값을 두 군데 적어 두면 한쪽만 고치게 된다.
        // 찍어 낸 것은 이름이 "Apple(Clone)" 이라 이름이 같은지가 아니라
        // 표의 이름을 품고 있는지를 본다.
        foreach (FoodSpec fs in FoodSpecs)
            if (n.Contains(fs.model.ToLowerInvariant())) smooth = fs.smooth;

        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
        {
            Material[] src = r.sharedMaterials;
            if (src == null) continue;
            Material[] dst = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                // 무늬가 있으면 색은 흰색으로 둔다. 런타임 재질은 색과 무늬를
                // 곱하는데, 받아온 모델의 Kd 는 무늬가 있을 때 아무 값이나
                // 들어 있다 — 박하사탕은 검정이라 곱하면 새까매진다.
                Texture tex = src[i] != null ? src[i].mainTexture : null;
                Color col = src[i] != null ? src[i].color : Color.gray;
                dst[i] = ToolModels.MakeMaterial(
                    tex != null ? Color.white : col, 0f, smooth, tex);
            }
            r.sharedMaterials = dst;
        }
    }

    /// <summary>0 이면 다문 입, 1 이면 활짝 벌린 입.</summary>
    private void SetMouth(float open)
    {
        if (mouthJawLow == null) return;
        open = Mathf.Clamp01(open);
        mouthOpen = open;

        // 다문 입은 **딱 붙여야** 한다. 0.3 mm 만 띄워도 턱이 0.22° 돌아가
        // 위아래 조각이 갈라지고, 그 실금으로 방이 비쳐 입술에 하얀
        // 점선이 그어진다. 1.5 mm 였을 때는 아예 이까지 보였다.
        float gap = Mathf.Lerp(0f, JawOpenMax, open);        // 위아래 입술 사이

        // 턱을 경첩으로 돌린다. 축에서 입까지의 거리를 알면 "틈 gap 을
        // 만들려면 몇 도 돌려야 하는가" 가 나온다.
        Vector3 arm = mouthLocal - jawHinge;
        float r = Mathf.Max(arm.magnitude, 1e-4f);
        float deg = Mathf.Asin(Mathf.Clamp(gap / r, -1f, 1f)) * Mathf.Rad2Deg;
        mouthJawLow.localRotation = Quaternion.Euler(deg, 0f, 0f);

        // 구강은 **건드리지 않는다.** 꼭짓점이 이미 headPivot 절대 좌표라
        // (구멍 테두리에서 그대로 뽑았다 — BuildCavity) 루트는 원점에
        // 있어야 한다. 여기서 입 자리로 옮겼더니 구강이 통째로 앞으로
        // 10.7 cm 밀려나 구멍을 안 덮었고, 입 아래로 턱받이가 비쳤다.

        bool show = open > 0.04f;
        mouthTeeth.gameObject.SetActive(true);
        mouthTeethLow.gameObject.SetActive(true);
        mouthTongue.gameObject.SetActive(show);

        // 이제 이는 **제 깊이**로 선다. 전에는 앞뒤로 0.5 cm 까지 눌러
        // 부조로 만들어야 했다 — 얼굴에 구멍이 없어서 아치를 세우면
        // 어금니가 머리 속으로 들어가 사라졌기 때문이다. 구강이 생겼으니
        // 틀니를 통째로 집어넣으면 된다.
        //
        // 모델은 좌우 폭 1.0, 씹는 면 y = 0, +Z 가 앞(앞니)이다.
        // x 와 z 는 같은 배율이라야 아치가 안 찌그러진다.
        const float archW = 0.042f;     // 치열 너비
        const float archH = 0.030f;     // 잇몸까지의 높이
        Vector3 rowSize = new Vector3(archW, archH, archW);

        // 앞니가 입술 바로 안쪽에 오게 민다. 모델의 z 원점이 아치 한가운데라
        // 깊이의 절반만큼 뒤로 물려야 앞니가 제자리에 선다.
        // 4 mm 였다. 그러면 앞니 끝이 입술 평면까지 나와서 다문 입에
        // 하얀 실선이 그어졌다. 입술 두께만큼 더 물린다.
        float frontZ = mouthSkinZ - 0.009f - archW * 0.40f;

        // 윗니는 위턱이라 **안 움직인다.** 윗입술 조각도 같이 고정이라,
        // 씹는 면을 입술선에 딱 맞추면 입을 벌려도 윗니가 그 조각 뒤에
        // 가려서 아랫니만 보인다. 그래서 4 mm 내려
        // 앞니 끝이 입술 아래로 나오게 한다 — 다문 입에서는 아랫입술
        // 조각이 그 끝을 가려 준다.
        mouthTeeth.localPosition = new Vector3(mouthLocal.x, mouthLocal.y - 0.004f, frontZ);
        mouthTeeth.localScale = rowSize;

        // 아랫니는 턱의 자식이다. 턱이 돌면 같이 돈다 — 여기서는 다문
        // 자세의 자리만 잡아 주면 된다. 축 기준 좌표라 축을 빼 준다.
        mouthTeethLow.localPosition =
            new Vector3(mouthLocal.x, mouthLocal.y, frontZ) - jawHinge;
        mouthTeethLow.localScale = rowSize;

        if (show)
        {
            // 혀도 턱에 얹혀 내려간다. 아랫니보다 안쪽, 구강 바닥에 깔린다.
            mouthTongue.SetParent(mouthJawLow, false);
            // 아랫니 안쪽에 **들어앉게** 둔다. 전에는 혀 앞끝이 앞니를
            // 넘어서서 이 사이로 핑크색이 샜다. 치열 안쪽 반지름보다
            // 작게, 그리고 씹는 면보다 낮게.
            mouthTongue.localPosition =
                new Vector3(mouthLocal.x, mouthLocal.y - 0.011f, mouthSkinZ - 0.031f)
                - jawHinge;
            mouthTongue.localScale = new Vector3(archW * 0.56f, 0.006f, archW * 0.58f);
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
        public Vector3 headAt;              // 배율 1 일 때 머리뼈가 놓이는 자리
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
        GripAround(l, point, axis, forward, back, curl, 0f, -1f, pole);
    }

    /// <summary>
    /// 집게손으로 쥔다. <paramref name="fold"/> 가 0 이상이면 검지를
    /// <paramref name="curl"/>, 엄지를 <paramref name="thumb"/> 로 모으고
    /// 나머지 셋은 <paramref name="fold"/> 만큼 손바닥에 접는다. 음수면
    /// 다섯 손가락을 같은 각으로 마는 주먹 — 도구를 잡을 때 쓰는 그 동작이다.
    /// </summary>
    private void GripAround(Limb l, Vector3 point, Vector3 axis, Vector3 forward,
                            float back, float curl, float thumb, float fold, Vector3 pole)
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
            if (fold >= 0f) Pinch(l, curl, thumb, fold);
            else Curl(l, curl);

            if (pass == 0)
                target += point - HoldPoint(l, fold >= 0f);
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
    /// 집게손. 검지와 엄지만 모으고 나머지 셋은 손바닥에 접는다.
    ///
    /// Curl 은 다섯 손가락을 같은 각으로 말아 주먹을 만든다. 그 고리
    /// 한가운데에 음식을 놓으니 손등 위에 **얹어 놓은** 것처럼 보였다 —
    /// 젤리 한 알을 주먹으로 쥐는 사람은 없다. 집는 건 검지와 엄지다.
    /// </summary>
    private static void Pinch(Limb l, float nip, float thumb, float fold)
    {
        if (l == null || l.fingers == null || l.wrist == null) return;

        for (int i = 0; i < l.fingers.Length; i++)
            if (l.fingers[i] != null) l.fingers[i].localRotation = l.restFingers[i];

        Vector3 axis = l.wrist.rotation * l.curlAxis;
        if (axis.sqrMagnitude < 1e-8f) return;
        axis = axis.normalized;

        // 뼈 차례는 MakeLimb 에서 정한 그대로다 —
        // 0~2 검지, 3~11 중지·약지·새끼, 12~14 엄지.
        for (int i = 0; i < l.fingers.Length; i++)
        {
            if (l.fingers[i] == null) continue;
            float deg = i < 3 ? nip : (i >= 12 ? thumb : fold);
            l.fingers[i].rotation =
                Quaternion.AngleAxis(deg * l.curlSign, axis) * l.fingers[i].rotation;
        }
    }

    /// <summary>
    /// 쥔 물건이 놓여야 할 자리. 주먹이면 손가락 고리의 한가운데,
    /// 집게손이면 검지와 엄지 사이다.
    /// </summary>
    private static Vector3 HoldPoint(Limb l, bool pinch)
    {
        if (pinch && l.fingers != null && l.fingers.Length > 14
            && l.fingers[2] != null && l.fingers[14] != null)
            return (l.fingers[2].position + l.fingers[14].position) * 0.5f;
        if (l.tip != null && l.palm != null)
            return (l.palm.position + l.tip.position) * 0.5f;
        return l.wrist != null ? l.wrist.position : Vector3.zero;
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
    /// 상황마다 등장인물이 다르다. 치과는 옆에서 들여다보는 의사가 있어야 하고,
    /// 식사는 제 손으로 먹으니 아무도 없다.
    /// 정면으로 혼자 서 있기만 하면 무슨 상황인지 안 읽힌다.
    /// </summary>
    private void BuildCast()
    {
        if (charRoot == null) return;

        // 핸드피스는 -X 쪽에서 들어온다. 의사는 그쪽 옆에서 몸을 기울인다.
        dentist = MakeActor("Dentist", new Vector3(-0.35f, 0.21f, 2f * D - 0.05f), 98f, 15f,
                            new Color(0.87f, 0.90f, 0.93f), new Color(0.20f, 0.16f, 0.13f));
        if (dentist != null) BuildMask(dentist);

        // 참가자 **바로 옆**에 한 사람 더 세운다. 위 dentist 는 거울 속
        // 사람이라 참가자가 한 번도 못 본다 — 거울 속 장면이 5배라
        // (DentalZoom) 거인이 되어 꺼 뒀기 때문이다. 그래서 거울에는
        // 떠 있는 드릴만 보이고, 고개를 돌리면 아무도 없었다.
        //
        // 자리는 거울 속 치과의를 거울면(z = D)에 비춘 자리다 —
        // z = 2D - (2D - 0.05) = 0.05. 거기서 조금 물러나 세운다.
        // 35 cm 는 눈에서 너무 가까워 몸이 카메라에 박힌다. 좌우로
        // 돌아앉는 각도 거울상이라 180 - 98 = 82 도다.
        nearDentist = MakeActor("DentistNear", new Vector3(-0.52f, 0.27f, 0.015f),
                                82f, 16f,
                                new Color(0.87f, 0.90f, 0.93f),
                                new Color(0.20f, 0.16f, 0.13f), 22f);
        if (nearDentist != null) BuildMask(nearDentist);

        collar = BuildCollar();

        // 상의 원래 색을 기억해 둔다. 치과에서만 에이프런 색으로 바꿨다가 되돌린다.
        foreach (Renderer r in charRoot.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
                if (m != null && m.name.ToLowerInvariant().Contains("lightbrown"))
                    shirtColor = m.color;

        // 음식은 리그 -X 쪽에서 올라온다. 그쪽 팔을 쓴다.
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
                            Color shirt, Color hair, float headDown = 0f)
    {
        GameObject go = SpawnPerson(name);
        if (go == null) return null;

        Actor a = new Actor();
        a.go = go;
        a.root = go.transform;
        a.headAt = headAt;
        a.root.localRotation = Quaternion.Euler(0f, yaw, 0f);
        a.head = FindBone(a.root, "Head");
        a.armL = MakeLimb(a.root, "L");
        a.armR = MakeLimb(a.root, "R");

        // 상체를 숙인다. 뻣뻣하게 선 채로는 남을 만지는 사람으로 안 보이고,
        // 키가 큰 채로 서 있으면 머리가 거울 위 테두리 밖으로 나가 얼굴이 잘린다.
        a.facing = Quaternion.AngleAxis(lean, a.root.right) * a.root.rotation;
        Transform torso = FindBone(a.root, "Torso");
        if (torso != null) torso.rotation = Quaternion.AngleAxis(lean, a.root.right) * torso.rotation;

        // 고개를 따로 더 숙인다. 몸만 숙이면 눈이 정면을 본 채라
        // "남의 입을 들여다보는 사람"이 아니라 딴 데 보는 사람이 된다.
        // 머리뼈는 아무도 매 프레임 건드리지 않으므로 한 번만 돌려 둔다
        // (팔만 RelaxOther·GripAround 가 다시 쓴다).
        if (a.head != null && Mathf.Abs(headDown) > 0.01f)
        {
            a.head.rotation = Quaternion.AngleAxis(headDown, a.root.right) * a.head.rotation;

            // facing 도 같이 돌려야 한다. 마스크는 이 축에 매달리는데
            // (BuildMask), 고개만 돌리고 축을 그대로 두면 마스크가
            // 제자리에 남아 **눈을 덮는다**. 실제로 그렇게 났다.
            a.facing = Quaternion.AngleAxis(headDown, a.root.right) * a.facing;
        }

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

        handpiece = ToolModels.BuildHandpiece();
        handpiece.transform.SetParent(toolRoot, false);
        handpiece.SetActive(false);

        // 옆에 선 치과의가 쥘 드릴. 위 handpiece 는 toolRoot(거울 속 머리)
        // 에 달려 있어 거울 안에만 있다. 이쪽은 리그에 직접 달아 참가자
        // 제 입가에 둔다 — 손에 뭔가 쥐여 줘야 팔이 늘어진 게 아니라
        // 일하는 것으로 보인다.
        nearDrill = ToolModels.BuildHandpiece();
        nearDrill.transform.SetParent(transform, false);
        nearDrill.SetActive(false);

        // 칫솔 조건에도 하나 있어야 한다. 드릴만 쥐여 줬더니 칫솔
        // 조건에서는 같은 손이 **빈손으로** 입가에 떠 있었다.
        nearBrush = BuildBrush();
        nearBrush.transform.SetParent(transform, false);
        nearBrush.SetActive(false);

        toothbrush = BuildBrush();
        toothbrush.transform.SetParent(toolRoot, false);
        toothbrush.SetActive(false);

        // 음식은 다섯 가지를 미리 지어 두고 켜고 끈다. 값은 모두 표에
        // 있다(FoodSpecs). 다 짓고 켜 두지 않는 쪽이 매번 짓는 것보다
        // 싸다 — 한 입에 한 번씩 모델을 찍어 내면 거울 속 상까지 두 배다.
        //
        // 크기는 실물보다 크다. 젤리를 3.2 cm 로 뒀을 때 손가락 사이에
        // 묻혀 거울에서 점으로밖에 안 보였다. 여기서 중요한 건 치수가
        // 아니라 "무엇을 먹는지 보이는 것" 이다.
        //
        // 젤리만 재질을 덮어쓴다. 한 입마다 색을 갈아 끼워야 해서 재질을
        // 하나 만들어 들고 있는다 — 그때그때 새로 만들면 재질이 계속
        // 쌓인다(AnimateFood 참고).
        jellyMat = ToolModels.MakeMaterial(JellyColors[0], 0f,
                                           SpecOf(FoodKind.Jelly).smooth);
        for (int k = 0; k < FoodSpecs.Length; k++)
        {
            FoodSpec fs = FoodSpecs[k];
            FoodItem it = Item((FoodKind)k);
            GameObject src = Resources.Load<GameObject>("Models/" + fs.model);
            if ((FoodKind)k == FoodKind.Jelly && src != null)
            {
                src = Instantiate(src);
                foreach (Renderer rr in src.GetComponentsInChildren<Renderer>(true))
                    rr.sharedMaterial = jellyMat;
            }
            it.go = BuildFoodItem("Food" + fs.model, src, fs.size, out it.half);
            // 크기를 맞춘 뒤에 깨문 단계별 메시를 만든다.
            if (fs.carve) PrepareBites(it, fs);
        }
        foodNow = Item(FoodKind.Apple);
        food = foodNow.go;
    }

    /// <summary>
    /// 음식 한 벌을 꺼낸다. 접시 쪽(BuildHome)과 손에 드는 쪽(BuildTools)이
    /// 서로 다른 때에 채우므로, 먼저 부르는 쪽이 자리를 만든다.
    /// </summary>
    private FoodItem Item(FoodKind k)
    {
        if (foodItems == null)
        {
            foodItems = new FoodItem[FoodSpecs.Length];
            for (int i = 0; i < foodItems.Length; i++) foodItems[i] = new FoodItem();
        }
        return foodItems[Mathf.Clamp((int)k, 0, foodItems.Length - 1)];
    }

    /// <summary>
    /// 칫솔. 받아온 모델을 쓰고, 없으면 기본 도형으로 쌓은 것으로 돌아간다.
    ///
    /// 쌓아 만든 칫솔은 솔이 정육면체 아홉 개라 거울에서 지우개처럼 보였다.
    /// 받아온 모델은 솔이 줄줄이 심겨 있고 머리도 둥글다.
    ///
    /// `Toothbrush.obj` 는 이미 도구 규약으로 정규화해 뒀다 — 원점이 솔
    /// 끝(닿는 지점), +Z 가 입으로 들어가는 쪽, 솔은 +Y, 전체 길이 1.
    /// 그래서 여기서는 길이만 미터로 준다. 솔이 손잡이와 **직각**이라
    /// 옆에서 들어와 윗니를 닦는 자세가 된다 — 쌓아 만든 쪽은 솔이 앞을
    /// 보고 있어서 이를 쿡 찌르는 모양이었다.
    /// </summary>
    private GameObject BuildBrush()
    {
        // 19 cm. 입이 가로 4.4 cm 니까(holeHalfW) 이 세상은 거의 실물
        // 크기다 — 칫솔도 실물 길이로 둔다. 쌓아 만든 것은 11.8 cm 에
        // 머리가 1.8 cm 라 비율이 만화였다.
        const float BrushLen = 0.19f;

        GameObject src = Resources.Load<GameObject>("Models/Toothbrush");
        if (src == null)
        {
            Debug.LogWarning("[MirrorRig] 칫솔 모델을 못 찾았다 — 기본 도형으로 짓는다");
            return ToolModels.BuildToothbrush();
        }

        GameObject holder = new GameObject("Toothbrush");
        GameObject item = Instantiate(src);
        item.name = "Toothbrush_Model";
        item.transform.SetParent(holder.transform, false);
        item.transform.localPosition = Vector3.zero;
        item.transform.localRotation = Quaternion.identity;
        item.transform.localScale = Vector3.one * BrushLen;
        foreach (Collider c in item.GetComponentsInChildren<Collider>(true)) Kill(c);
        Reskin(item);
        return holder;
    }

    /// <summary>
    /// 음식 하나를 입 한가운데에 놓을 수 있는 모양으로 감싼다.
    ///
    /// 모델의 원점이 한가운데라는 보장이 없어서(사과가 그렇다) 빈 오브젝트로 한 번
    /// 감싸 중심을 맞춘다. 그래야 입 위치에 그대로 놓을 수 있다.
    /// </summary>
    /// <param name="size">한 입 크기로 맞출 **가장 긴 변**의 길이 (m)</param>
    private GameObject BuildFoodItem(string name, GameObject source, float size,
                                     out Vector3 half)
    {
        half = Vector3.one * (size * 0.5f);
        if (source == null)
        {
            Debug.LogWarning("[MirrorRig] 음식 모델을 못 찾았다 — " + name);
            return null;
        }

        GameObject holder = new GameObject(name);
        holder.transform.SetParent(toolRoot, false);

        // Resources 에서 온 것은 프리팹이라 찍어 내야 하고, ToolModels 가 만든 것은
        // 이미 씬에 있는 실물이라 그대로 쓴다. scene 이 유효한지로 구분된다.
        bool fromResources = !source.scene.IsValid();
        GameObject item = fromResources ? Instantiate(source) : source;
        // Resources 에서 온 것은 .mtl 재질을 달고 온다. 아이폰에서 분홍색이
        // 되지 않게 런타임 재질로 바꿔 끼운다 (Reskin 주석 참고).
        if (fromResources) Reskin(item);
        item.name = name + "_Model";
        item.transform.SetParent(holder.transform, false);

        // 통째로는 입에 비해 크다. 한 입 크기로 줄인다.
        //
        // 세로(y)로 재서는 안 된다. 사과와 젤리는 세로로 서 있으니 맞았지만,
        // 고기와 사탕은 **납작한 조각**이라 세로가 모델에서 가장 짧은 변이다.
        // 그 1 cm 를 8.5 cm 로 늘리면 고기가 가로 44 cm 짜리 판이 된다.
        // 가장 긴 변으로 재면 사과·젤리도 전과 같은 값이 나온다(세로가
        // 가장 긴 변이다) — 사과만 1.3 % 작아진다.
        Bounds b = WorldBounds(item);
        float longest = Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z));
        item.transform.localScale = Vector3.one * (size / Mathf.Max(longest, 1e-5f));
        item.transform.localPosition -=
            holder.transform.InverseTransformPoint(WorldBounds(item).center);

        // 접시에 올려놓을 때 쓸 반 크기. **가장 긴 변의 절반이 아니다** —
        // 납작한 고기는 길이가 8.5 cm 여도 두께는 1 cm 뿐이라, 절반인
        // 4.25 cm 를 쓰면 접시 위 3.7 cm 허공에 뜬다. 세워 드는 음식은
        // 세운 뒤의 높이가 또 다르므로(SitHeight) 세 축을 다 들고 있는다.
        half = WorldBounds(item).size * 0.5f;

        holder.SetActive(false);
        return holder;
    }

    /// <summary>
    /// 사과를 깨문 단계별 메시를 미리 만들어 둔다.
    ///
    /// 한 입에 통째로 사라지면 먹는 게 아니라 지워지는 것으로 보인다.
    /// 깨물 때마다 한 조각씩 패이고 네 입째에 없어지게 한다.
    ///
    /// 받아 온 사과는 169 정점이라 한 입 자리를 패기엔 너무 거칠다 —
    /// 베어 문 자리가 육각형 구멍이 돼 버린다. 삼각형을 두 번 쪼개
    /// 2768 면으로 올린 다음 파낸다.
    ///
    /// 파내는 방법은 **구와의 교차를 안쪽 면으로 미는 것**이다. 삼각형을
    /// 지우는 쪽이 먼저 떠오르지만 그러면 구멍이 뚫려서 속살을 또 막아
    /// 줘야 한다. 미는 쪽은 메시가 닫힌 채로 남아서 막을 것이 없다.
    /// 민 정점으로만 이뤄진 삼각형은 과육 재질로 돌려서 하얗게 보이게
    /// 한다 — 패인 자리가 껍질과 같은 빨강이면 패인 게 안 보인다.
    /// </summary>
    private void PrepareBites(FoodItem it, FoodSpec fs)
    {
        GameObject holder = it.go;
        if (holder == null) return;

        // 사과 몸통은 정점이 가장 많은 메시다. 꼭지·잎도 같은 메시에
        // 서브메시로 들어 있다.
        MeshFilter[] all = holder.GetComponentsInChildren<MeshFilter>(true);
        int most = 0;
        foreach (MeshFilter f in all)
            if (f.sharedMesh != null && f.sharedMesh.vertexCount > most)
                most = f.sharedMesh.vertexCount;
        if (most == 0) return;

        List<MeshFilter> body = new List<MeshFilter>();
        foreach (MeshFilter f in all)
            if (f.sharedMesh != null && f.sharedMesh.vertexCount == most) body.Add(f);
        it.body = body.ToArray();

        Mesh src = it.body[0].sharedMesh;
        int subs = src.subMeshCount;

        List<Vector3> v = new List<Vector3>();
        src.GetVertices(v);
        List<int> tri = new List<int>();
        List<int> sub = new List<int>();
        for (int sm = 0; sm < subs; sm++)
        {
            int[] t = src.GetTriangles(sm);
            for (int i = 0; i + 2 < t.Length; i += 3)
            {
                tri.Add(t[i]); tri.Add(t[i + 1]); tri.Add(t[i + 2]);
                sub.Add(sm);
            }
        }
        SplitTris(v, tri, sub);
        SplitTris(v, tri, sub);

        Bounds b = src.bounds;
        // 꼭지와 잎은 위에 달려 있다. 한 입 자리를 허리에 둬서 건드리지 않는다.
        Vector3 mid = b.center - Vector3.up * (b.size.y * 0.05f);
        float rad = Mathf.Max(b.size.x, b.size.z) * 0.5f;

        it.bites = new Mesh[fs.bites];
        for (int k = 0; k < fs.bites; k++)
            it.bites[k] = BiteMesh(v, tri, sub, subs, mid, rad, k, fs.bites);

        // 과육 재질을 서브메시 한 칸 뒤에 붙인다.
        Material flesh =
            ToolModels.MakeMaterial(new Color(0.95f, 0.91f, 0.74f), 0f, 0.18f);
        foreach (MeshFilter f in it.body)
        {
            MeshRenderer mr = f.GetComponent<MeshRenderer>();
            if (mr == null) continue;
            Material[] old = mr.sharedMaterials;
            Material[] now = new Material[subs + 1];
            for (int sm = 0; sm < subs; sm++)
                now[sm] = sm < old.Length ? old[sm] : null;
            now[subs] = flesh;
            mr.sharedMaterials = now;
        }

        it.stage = 0;
        SetBiteStage(it, 0);

        // 과육 면이 몇 장 나왔는지는 눈으로 가늠이 안 된다. 처음에
        // 작은 자국 넷을 냈을 때 5280 면 중 32 면뿐이었는데, 그게
        // 화면에서 안 보이는 이유였다 — 세어 봐야 알 수 있다.
        Debug.Log("[MirrorRig] " + fs.model + " 깨물기 — 쪼갠 뒤 " + v.Count
                  + "꼭짓점 " + sub.Count + "면 / 과육면 "
                  + it.bites[1].GetTriangles(subs).Length / 3
                  + "→" + it.bites[fs.bites - 1].GetTriangles(subs).Length / 3);
    }

    /// <summary>삼각형을 네 쪽으로 쪼갠다. 변 가운데 점은 한 번만 만든다.</summary>
    private static void SplitTris(List<Vector3> v, List<int> tri, List<int> sub)
    {
        Dictionary<long, int> seen = new Dictionary<long, int>();
        List<int> nt = new List<int>(tri.Count * 4);
        List<int> ns = new List<int>(sub.Count * 4);
        for (int f = 0; f < sub.Count; f++)
        {
            int a = tri[f * 3], b = tri[f * 3 + 1], c = tri[f * 3 + 2];
            int ab = EdgeMid(v, seen, a, b);
            int bc = EdgeMid(v, seen, b, c);
            int ca = EdgeMid(v, seen, c, a);
            nt.Add(a);  nt.Add(ab); nt.Add(ca);
            nt.Add(ab); nt.Add(b);  nt.Add(bc);
            nt.Add(ca); nt.Add(bc); nt.Add(c);
            nt.Add(ab); nt.Add(bc); nt.Add(ca);
            for (int i = 0; i < 4; i++) ns.Add(sub[f]);
        }
        tri.Clear(); tri.AddRange(nt);
        sub.Clear(); sub.AddRange(ns);
    }

    private static int EdgeMid(List<Vector3> v, Dictionary<long, int> seen, int a, int b)
    {
        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        int got;
        if (seen.TryGetValue(key, out got)) return got;
        v.Add((v[a] + v[b]) * 0.5f);
        got = v.Count - 1;
        seen[key] = got;
        return got;
    }

    /// <summary>
    /// 몇 입째 깨문 자리가 메시 좌표에서 어느 쪽을 보는가 (도, Y 축 둘레).
    /// 메시를 파는 쪽(`BiteApple`)과 사과를 돌려 놓는 쪽(`FoodSpin`)이
    /// **같은 값**을 봐야 패인 자리가 뜻한 데를 향한다.
    /// </summary>
    private static float BiteAngle(int bites)
    {
        return -20f + 16f * bites;
    }

    /// <summary>사과를 <paramref name="bites"/> 번 깨문 메시를 만든다.</summary>
    private Mesh BiteMesh(List<Vector3> srcV, List<int> tri, List<int> sub, int subs,
                          Vector3 mid, float rad, int bites, int stages)
    {
        List<Vector3> v = new List<Vector3>(srcV);
        bool[] gone = new bool[v.Count];

        if (bites > 0)
        {
            // **한 자리를 점점 크게** 베어 문다.
            //
            // 처음엔 작은 자국을 네 군데 돌아가며 냈다. 그런데 사과는 거울
            // 속에서 가로 35 픽셀쯤이고, 그 자국 하나는 전체 5280 면 중
            // 32 면 — 화면에서 3 픽셀짜리 점이라 아예 안 보였다. 과육을
            // 자홍색으로 칠해 놓고 찍어 봐도 한 점도 안 나왔다.
            //
            // 사람이 사과를 먹는 모양도 실제로 이쪽이다. 한 자리를 계속
            // 베어 물어서 허연 단면이 넓어지고, 사과는 초승달이 된다.
            // 단면이 넓으니 작은 화면에서도 "먹고 있다" 가 읽힌다.
            //
            // 구를 **단계마다 하나만** 쓰는 것도 중요하다. 구를 여러 개
            // 겹쳐 쓰면 앞 구에 끌려 들어간 정점을 뒤 구가 또 끌어당겨
            // 이웃을 지나치고, 면이 뒤집혀 사과가 빨간 가시처럼 찢겨
            // 보였다. 한 개면 정점마다 제 광선 위에서 한 번만 움직인다.
            int k = bites;
            float ang = BiteAngle(k) * Mathf.Deg2Rad;
            // 깊이가 아니라 **너비**를 키운다. 전엔 반지름을 0.42 → 0.93
            // 으로 늘렸더니 세 입째에 패인 바닥이 중심에서 0.17 까지
            // 들어가, 남은 껍질이 얇아지고 서로 뚫고 나와 사과가 빨간
            // 파편처럼 보였다. 바닥은 0.5 쯤에 붙들어 두고 입만 넓힌다
            // — 한 입이 50°, 세 입이 72° 쯤 된다.
            Vector3 c = mid
                      + new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang))
                        * (rad * (0.93f + 0.05f * k))
                      // 허리 높이에 파면 쥔 손가락이 그 앞을 가린다.
                      // 조금 위에 파야 주먹 위로 단면이 드러난다.
                      + Vector3.up * (rad * 0.18f);
            float r = rad * (0.32f + 0.11f * k);

            for (int i = 0; i < v.Count; i++)
            {
                if ((v[i] - c).sqrMagnitude >= r * r) continue;

                // 패인 바닥은 **구의 안쪽 면**이다. 정점을 c 에서 바로 밀어내면
                // 바깥 면으로 가서 혹이 솟는다. 사과 중심에서 정점으로 가는
                // 선이 구를 처음 뚫는 자리로 당겨야 오목해진다.
                Vector3 p = v[i] - mid;
                Vector3 q = c - mid;
                float pp = p.sqrMagnitude;
                if (pp < 1e-12f) continue;
                float pq = Vector3.Dot(p, q);
                float disc = pq * pq - pp * (q.sqrMagnitude - r * r);
                if (disc < 0f) continue;
                float t = (pq - Mathf.Sqrt(disc)) / pp;
                if (t <= 0f || t >= 1f) continue;

                // 테두리에서는 **덜** 당긴다. 안팎을 딱 잘라 당기면 구에
                // 아슬아슬하게 걸친 정점은 광선이 거의 스치듯 들어가
                // 한참 안쪽으로 끌려가고, 안 끌린 이웃과 사이가 길게
                // 늘어나 가장자리가 찢긴 것처럼 보인다. 구 안쪽으로
                // 얼마나 들어왔는지에 따라 섞으면 테두리가 둥글게 돈다.
                float deep = (r - (v[i] - c).magnitude) / (r * 0.35f);
                float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(deep));
                v[i] = Vector3.Lerp(v[i], mid + p * t, w);
                // 과육 판정은 **헐겁게**. 0.75 로 잡았더니 과육이 한 입에
                // 28 면만 남아 하얀 단면이 아예 안 나왔다. 조금이라도
                // 끌려 들어왔으면 패인 자리로 친다.
                gone[i] = w > 0.25f;
            }
        }

        // 파인 자리만으로는 부족하다. 사과는 거울 속에서 엄지만 하게
        // 보이는데, 20° 짜리 홈은 그 크기에서 몇 픽셀이라 안 읽힌다.
        // 한 입마다 전체를 8.5 % 씩 줄여야 줄어드는 게 눈에 보인다.
        if (bites > 0)
        {
            float k2 = 1f - BiteShrink(stages) * bites;
            for (int i = 0; i < v.Count; i++) v[i] = mid + (v[i] - mid) * k2;
        }

        Mesh m = new Mesh();
        m.name = "Apple_bite" + bites;
        if (v.Count > 65000)
            m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        m.SetVertices(v);
        m.subMeshCount = subs + 1;

        List<int>[] outT = new List<int>[subs + 1];
        for (int sm = 0; sm <= subs; sm++) outT[sm] = new List<int>();
        for (int f = 0; f < sub.Count; f++)
        {
            int a = tri[f * 3], b = tri[f * 3 + 1], c2 = tri[f * 3 + 2];
            // 세 꼭지 다 밀린 삼각형만 과육이다. 테두리 삼각형을 껍질로
            // 남겨 두면 하얀 자리 둘레에 빨간 테가 얇게 돌아서 베어 문
            // 자리처럼 보인다.
            bool flesh = gone[a] && gone[b] && gone[c2];
            List<int> dst = outT[flesh ? subs : sub[f]];
            dst.Add(a); dst.Add(b); dst.Add(c2);
        }
        for (int sm = 0; sm <= subs; sm++) m.SetTriangles(outT[sm], sm);

        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>
    /// 음식을 몇 입 깨문 모양으로 갈아 끼운다.
    ///
    /// 깨문 자리를 파 둔 음식(FoodSpec.carve)은 단계별 메시를 갈아 끼우고,
    /// 안 판 음식은 **작아지기만** 한다. 납작한 조각(고기)에 구를 파면
    /// 구멍이 뚫리는데, 어차피 거울에서 사과는 가로 35 픽셀쯤이라 읽히는
    /// 단서는 패인 자리가 아니라 작아지는 쪽이다(BiteMesh 주석).
    /// </summary>
    private void SetBiteStage(FoodItem it, int stage)
    {
        if (it == null || it.go == null) return;
        stage = Mathf.Max(stage, 0);

        if (it.bites != null && it.body != null)
        {
            Mesh m = it.bites[Mathf.Min(stage, it.bites.Length - 1)];
            foreach (MeshFilter f in it.body)
                if (f != null) f.sharedMesh = m;
            return;
        }

        // 메시를 안 파는 음식. 한 입마다 같은 비율로 줄인다 — 파는 쪽의
        // 줄어드는 폭(BiteMesh)과 같은 식을 쓴다.
        int stages = SpecOf(KindOf(it)).bites;
        float k = stages > 1 ? 1f - BiteShrink(stages) * Mathf.Min(stage, stages - 1) : 1f;
        it.go.transform.localScale = Vector3.one * k;
    }

    /// <summary>
    /// 접시에 올려놓을 때의 반높이. 세워 드는 음식(FoodSpec.tilt)은 눕혀
    /// 쟀을 때의 반높이를 쓰면 접시에 박힌다 — 눕힌 고기는 반두께가
    /// 0.5 cm 인데, 세우고 나면 4.3 cm 가 필요하다.
    /// </summary>
    private static float SitHeight(FoodItem it, FoodSpec fs)
    {
        float r = fs.tilt * Mathf.Deg2Rad;
        return Mathf.Abs(Mathf.Cos(r)) * it.half.y + Mathf.Abs(Mathf.Sin(r)) * it.half.z;
    }

    /// <summary>이 음식이 FoodKind 몇 번인가.</summary>
    private FoodKind KindOf(FoodItem it)
    {
        if (foodItems != null)
            for (int i = 0; i < foodItems.Length; i++)
                if (foodItems[i] == it) return (FoodKind)i;
        return FoodKind.Apple;
    }

    /// <summary>
    /// 한 입마다 줄어드는 비율. 네 입에 걸쳐 먹는 사과에서 8.5 % 로 맞춰
    /// 뒀으니, 횟수가 달라져도 다 먹을 때까지 줄어드는 총량은 같게 둔다.
    /// </summary>
    private static float BiteShrink(int stages)
    {
        return 0.085f * 4f / Mathf.Max(stages, 1);
    }

    // ───────────────────────── 진행 ─────────────────────────

    /// <summary>수준은 직전에 쓰던 것을 그대로 둔다.</summary>
    public void ShowScenario(Scenario s)
    {
        ShowScenario(s, foodKind, dentalKind);
    }

    /// <summary>음식 블록 — 사과·젤리·고기·푸딩·사탕 중 하나.</summary>
    public void ShowScenario(Scenario s, FoodKind kind)
    {
        ShowScenario(s, kind, dentalKind);
    }

    /// <summary>치과 블록 — 드릴이냐 칫솔이냐.</summary>
    public void ShowScenario(Scenario s, DentalKind kind)
    {
        ShowScenario(s, foodKind, kind);
    }

    public void ShowScenario(Scenario s, FoodKind kind, DentalKind dental)
    {
        current = s;
        foodKind = kind;
        dentalKind = dental;
        elapsed = 0f;
        Contact = 0f;

        // 방을 통째로 갈아 끼운다. 집과 치과는 벽도 바닥도 거울도 다르다.
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

        // 치과 도구도 둘 중 하나만 든다.
        bool atDentist = s == Scenario.Dental;
        if (handpiece != null)  handpiece.SetActive(atDentist && dental == DentalKind.Drill);
        if (toothbrush != null) toothbrush.SetActive(atDentist && dental == DentalKind.Brush);

        // 접시에 담아 두는 것도, 손에 드는 것도 조건을 따라간다. 다섯 벌을
        // 다 지어 뒀으니 한 벌만 켜면 된다. 손에 든 쪽을 켜는 건 AnimateFood
        // 가 한 입 주기에 맞춰 하므로 여기서는 전부 끈다.
        //
        // 새로 들어가는 장면은 온전한 것으로 시작한다.
        biteCycle = -1;
        for (int k = 0; k < FoodSpecs.Length; k++)
        {
            FoodItem it = Item((FoodKind)k);
            bool on = (FoodKind)k == kind;
            if (it.plate != null) it.plate.gameObject.SetActive(on);
            it.stage = 0;
            SetBiteStage(it, 0);
            if (it.go != null) it.go.SetActive(false);
        }
        foodNow = Item(kind);
        food = foodNow.go;

        SetMouth(0f);          // 치과를 보다 나가면 입이 벌어진 채로 남는다

        // 거울 속 장면 확대 (DentalZoom 참고). 환자와 치과의를 **같은 배율로,
        // 같은 축(= 입)을 중심으로** 키운다.
        //
        // charRoot 에 배율을 걸어도 위치 계산은 안 깨진다 — 자기 localScale 은
        // 자기 localPosition 이 부모 좌표로 가는 길에 끼어들지 않는다. 축을
        // 머리뼈에서 입으로 옮기는 일은 TrackViewer 가 매 프레임 한다.
        float zoom = atDentist ? DentalZoom : 1f;
        if (charRoot != null) charRoot.localScale = Vector3.one * zoom;
        if (dentist != null && dentist.head != null && zoom <= 1.001f)
        {
            dentist.root.localScale = Vector3.one * zoom;
            // 치과의는 머리 위치로 자리를 잡는다(MakeActor 와 같은 방식).
            // 입을 축으로 키운 자리 = 입 + (원래 머리자리 - 입) × 배율.
            Vector3 pivot = MouthPivot();
            Vector3 want  = pivot + (dentist.headAt - pivot) * zoom;
            dentist.root.localPosition +=
                want - transform.InverseTransformPoint(dentist.head.position);
        }

        // 등장인물과 소품은 상황마다 다르다.
        //
        // 확대가 걸리면 치과의는 끈다. 거울이 입만 담을 만큼 좁아서 이 화각에는
        // 애초에 안 들어오는데, 몸이 5배가 되면서 쉬고 있는 반대쪽 팔이
        // 거울면을 넘어 참가자 방까지 뻗어 나왔다 — 거울 테두리 밖에 거대한
        // 손이 떠 있었다. 도구는 환자 머리뼈에 매달려 있어 치과의가 없어도
        // 그대로 움직이고 닿는다. 확대를 끄면(배율 1) 다시 나온다.
        if (dentist != null)
            dentist.go.SetActive(s == Scenario.Dental && DentalZoom <= 1.001f);
        // 이쪽은 실물이라 배율과 상관없다. 늘 제 크기로 선다.
        if (nearDentist != null) nearDentist.go.SetActive(s == Scenario.Dental);
        if (nearDrill != null)
            nearDrill.SetActive(atDentist && dental == DentalKind.Drill);
        if (nearBrush != null)
            nearBrush.SetActive(atDentist && dental == DentalKind.Brush);
        // 치과 에이프런. 목에 두른 천이라 깃이 같이 보인다.
        if (collar != null) collar.gameObject.SetActive(s == Scenario.Dental);

        // 천은 따로 만들지 않고 상의 색을 바꾼다. 가슴 앞에 판을 세워 보니
        // 몸에 묻히거나 가슴에서 떠 보이거나 둘 중 하나였다 — 몸 두께가 자세마다
        // 달라서 맞는 거리가 하나로 정해지지 않는다. 거울에 비치는 건 가슴 위쪽뿐이라
        // 색만 바뀌어도 천으로 덮인 걸로 읽힌다.
        if (charRoot != null)
        {
            Color drape = s == Scenario.Dental
                ? new Color(0.56f, 0.78f, 0.84f)
                : shirtColor;
            Tint(charRoot.gameObject, "lightbrown", drape);
        }

        if (selfArm != null) selfArm.Rest();
        if (dentist != null) dentist.reach.Rest();
        if (nearDentist != null) nearDentist.reach.Rest();

        ShowLabel(s);
    }

    private void LateUpdate()
    {
        TrackViewer();

        if (label != null && label.gameObject.activeSelf && Time.time > labelUntil)
            label.gameObject.SetActive(false);

        // 상황을 안 켠 거울 앞에서도 사람은 눈을 깜빡인다. elapsed 는
        // 상황이 켜진 동안만 흐르므로 얼굴은 제 시계로 돈다.
        faceTime += Time.deltaTime;
        UpdateFace();

        if (current == Scenario.None) return;
        elapsed += Time.deltaTime;
        Animate();
    }

    private void Animate()
    {
        // 다리는 매 프레임 다시 박는다. TrackViewer 가 사람을 통째로 밀고
        // 머리뼈를 돌려 놓기 때문에, 한 번만 앉혀 두면 다음 프레임에 풀린다.
        // 치과만 누운 자세다. 아무것도 안 켠 상태에서는 집 방이 보이므로
        // 그때도 앉혀 둬야 발이 바닥에서 뜨지 않는다.
        PoseLegs(current != Scenario.Dental);

        switch (current)
        {
            case Scenario.Dental: AnimateDental(); break;
            case Scenario.Food:   AnimateFood();   break;
        }
    }

    /// <summary>
    /// 에디터 프리뷰용 — 시간을 지정해 한 프레임만 돌린다.
    /// 에디터에서는 LateUpdate 가 안 돌아서 원하는 순간의 그림을 찍을 수 없다.
    /// </summary>
    public void StepForPreview(float seconds)
    {
        TrackViewer();
        faceTime = seconds;
        UpdateFace();
        if (current == Scenario.None) { PoseLegs(true); return; }
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

        // 확대가 걸려 있으면 얼굴이 머리뼈를 축으로 커져서 입이 앞으로 튀어나온다.
        // 심하면 거울면을 뚫고 참가자 쪽으로 나온다. 축을 입으로 옮긴다 —
        // 머리뼈에서 입까지가 배율만큼 늘어난 그만큼 사람을 뒤로 민다.
        float z = charRoot.localScale.x;
        if (z > 1.0001f && headPivot != null)
        {
            Vector3 o = transform.InverseTransformVector(
                            headPivot.TransformVector(mouthLocal)) / z;
            charRoot.localPosition -= o * (z - 1f);
        }
    }

    /// <summary>
    /// 배율 1 일 때 거울 속 입이 놓이는 자리 (리그 좌표).
    /// 거울 속 사람은 참가자를 마주 보므로 머리뼈 로컬 +Z(얼굴 쪽)가 리그 -Z 다.
    /// </summary>
    private Vector3 MouthPivot()
    {
        return new Vector3(0f, -NeckDrop + mouthLocal.y, 2f * D - mouthLocal.z);
    }

    private static float Ease(float k)
    {
        k = Mathf.Clamp01(k);
        return k * k * (3f - 2f * k);
    }

    private void AnimateDental()
    {
        bool brush = dentalKind == DentalKind.Brush;
        GameObject tool = brush ? toothbrush : handpiece;
        if (tool == null) return;

        // 도구가 물러나는 방향. 치과의가 옆에서 몸을 숙이고 있으므로 옆 위에서
        // 비스듬히 들어온다. 아래에서 올리면 팔뚝이 도구를 가로질러 지나가
        // 쥔 게 아닌 것처럼 보인다.
        //
        // 두 가지가 이 값을 묶는다. (1) 거울 속 장면이 DentalZoom 배로 커져
        // 있으므로, 로컬 거리를 그만큼 나눠야 실제로 움직이는 거리가 사람
        // 크기에 맞는다. (2) z 성분(참가자 쪽)을 크게 주면 안 된다 — 4배로
        // 커진 도구가 뒤로 빠지면서 거울면(z = D)을 뚫고 참가자 방 쪽으로
        // 튀어나온다. 그래서 거의 옆으로 빠지게 눕혔다.
        Vector3 away = new Vector3(0.30f, 0.10f, 0.05f) / DentalZoom;
        float k = elapsed / ApproachTime;
        Vector3 pos = Vector3.Lerp(mouthLocal + away, mouthLocal, Ease(k));
        bool touching = k >= 1f;

        if (touching)
        {
            float s = elapsed - ApproachTime;

            if (brush)
            {
                // 칫솔은 떼지 않고 문지른다. 드릴처럼 댔다 뗐다 하면 칫솔질이 아니라
                // 쿡쿡 찌르는 걸로 보인다. 좌우로 크게, 위아래로 잘게 — 실제로
                // 전동칫솔을 치열을 따라 옮길 때 나오는 움직임이다.
                pos += Vector3.right * (Mathf.Sin(s * 2.2f) * 0.018f)
                     + Vector3.up    * (Mathf.Sin(s * 13f)  * 0.0035f);
            }
            else
            {
                // 치과의는 계속 대고 있지 않고 댔다 뗐다 한다. 뗀 동안은 진동도 꺼진다.
                float cycle = Mathf.Repeat(s, 3.2f);
                if (cycle > 2.1f)
                {
                    float lift = Mathf.Sin((cycle - 2.1f) / 1.1f * Mathf.PI);
                    pos += away.normalized * (lift * 0.025f);
                    touching = lift < 0.25f;
                }
            }
        }

        // 칫솔은 솔이 손잡이와 직각이라 머리가 **옆으로 눕는다**. 닿는
        // 지점을 입 한가운데(mouthLocal)에 그대로 두면 머리가 입술 앞에
        // 떠서 아랫입술을 덮는다 — mouthLocal 은 살갗보다 9 mm 앞이다.
        // 솔 끝이 윗니에 닿도록 조금 올리고 안으로 민다.
        if (brush) pos += new Vector3(0f, 0.005f, -0.006f);

        tool.transform.localPosition = pos;
        tool.transform.localRotation = Quaternion.LookRotation(-away.normalized, Vector3.up);

        // 치과는 처음부터 끝까지 벌리고 있는다. 도구가 들어오기 전에 미리 벌린다.
        // 칫솔은 드릴만큼 크게 벌릴 일이 없다 — 반쯤 벌리고 문지른다.
        float open = Mathf.Max(Ease(elapsed / (ApproachTime * 0.6f)),
                               0.95f * Mathf.Clamp01(k));
        SetMouth(brush ? open * 0.62f : open);

        if (dentist != null)
        {
            RelaxOther(dentist);
            // 칫솔은 몸통이 곧고 길어서 더 뒤를 잡아야 손이 입을 가리지
            // 않는다. 받아온 모델로 바꾸며 11.8 → 19 cm 가 됐으므로
            // 잡는 자리도 같은 비율(손잡이의 60 % 쯤)로 물려 뒀다.
            HoldTool(dentist.reach, tool.transform,
                     brush ? new Vector3(0f, -0.008f, -0.115f)
                           : new Vector3(0f, 0.008f, -0.090f),
                     brush ? new Vector3(0f, 0f, -1f)
                           : new Vector3(0f, 0.10f, -0.99f),
                     ElbowPole(dentist));
        }

        if (nearDentist != null)
        {
            RelaxOther(nearDentist);

            // 손은 참가자 입 왼쪽 아래. **거기 드릴은 없다** — 진짜
            // 드릴은 참가자 제 입 안이라 제 눈에는 안 보인다. 보여야
            // 하는 건 위에서 내려오는 팔과 몸이고, 손은 시야 아래
            // 모서리에 걸친다.
            //
            // 입 자리는 눈에서 아래로 NeckDrop - 머리단위×0.08,
            // 앞으로 머리단위×0.465 다 (BuildHead 의 mouthLocal).
            Vector3 at = new Vector3(0f, -NeckDrop + headUnit * 0.08f,
                                     headUnit * 0.465f);

            // 가만히 서 있으면 마네킹이다. 숨 쉬는 만큼만 흔든다.
            at.y += Mathf.Sin(elapsed * 0.85f) * 0.005f;

            GameObject near = nearDrill != null && nearDrill.activeSelf
                            ? nearDrill
                            : (nearBrush != null && nearBrush.activeSelf
                               ? nearBrush : null);
            if (near != null)
            {
                // 자루가 들어오는 방향. **옆에서** 비스듬히여야 한다.
                //
                // 위에서 가파르게(-0.46, 0.82, -0.34) 내려오게 해 봤더니
                // 자루가 화면 한복판을 세로로 갈라 거울 구멍을 통째로
                // 덮었다. 참가자가 보는 건 거울 속 입 하나뿐이라 그걸
                // 가리면 실험이 없어진다. 옆으로 눕히면 자루가 거울
                // 테두리 바깥으로 빠져 구멍이 열린 채로 남는다.
                Vector3 fromDoc = new Vector3(-0.72f, 0.42f, -0.55f).normalized;

                // 도구의 +Z 가 **끝 방향**이고 원점이 끝이다 (위 거울 쪽
                // 코드의 LookRotation(-away) 와 같은 규칙). 자루는 -Z 로
                // 뻗으므로 치과의 쪽인 fromDoc 의 반대를 봐야 한다.
                near.transform.localPosition = at;
                near.transform.localRotation =
                    Quaternion.LookRotation(-fromDoc, Vector3.up);

                // 드릴의 자루 한가운데는 BuildHandpiece 를 따라가면
                // (0, 0.024, -0.091) 이다. 거울 쪽과 같은 y = 0.008 을
                // 썼더니 손이 자루 **밑** 16 mm 에 떠서 손가락이 허공을
                // 쥐었다. 거울 속 치과의는 꺼져 있어(DentalZoom) 아무도
                // 그걸 못 봤을 뿐이다.
                bool br = near == nearBrush;
                HoldTool(nearDentist.reach, near.transform,
                         br ? new Vector3(0f, -0.008f, -0.115f)
                            : new Vector3(0f, 0.024f, -0.091f),
                         br ? new Vector3(0f, 0f, -1f)
                            : new Vector3(0f, 0.34f, -0.94f),
                         ElbowPole(nearDentist));
            }
            else
            {
                GripAround(nearDentist.reach, transform.TransformPoint(at),
                           transform.TransformDirection(
                               new Vector3(-0.72f, 0.42f, -0.55f).normalized),
                           Vector3.zero, HandGrip, 46f, ElbowPole(nearDentist));
            }
        }

        Contact = touching ? 1f : 0f;
    }

    /// <summary>
    /// 음식이 놓일 자세.
    ///
    /// 아직 온전한 것은 돌려서 보여 준다. 무른 것은 더 천천히 — 젤리가
    /// 사과처럼 핑핑 돌면 단단해 보인다.
    ///
    /// 깨문 사과는 패인 쪽이 <paramref name="toMouth"/> 를 따라 돈다.
    /// 1 이면 입 쪽(-Z), 0 이면 거울 쪽(+Z).
    ///
    /// **베어 무는 쪽은 입 쪽이어야 한다.** 한동안 패인 자리를 늘 거울로
    /// 돌려 놨는데, 그러면 사과 뒤통수를 물면서 앞쪽이 패이는 셈이라
    /// 말이 안 된다. 그렇다고 입 쪽에 고정해 두면 패인 자리가 영영 안
    /// 보인다 — 애써 파 놓고 한 점도 안 보였던 적이 실제로 있다.
    ///
    /// 그래서 **돌린다**. 입으로 가져가는 동안 패인 쪽을 입으로 돌리고,
    /// 씹으려고 손을 내리는 동안 도로 거울 쪽으로 돌린다. 사람이 사과를
    /// 먹을 때 실제로 하는 짓이기도 하다 — 한 입 베고 손을 내리면서
    /// 베어 문 자리를 들여다본다.
    /// </summary>
    private Quaternion FoodSpin(FoodSpec fs, float toMouth)
    {
        // 세우기는 **돌리기 바깥**에 건다. Euler(tilt,0,0) * Euler(0,yaw,0)
        // 은 모델 좌표에서 먼저 돌리고 나서 세우는 것이라, 90° 세운 음식은
        // 제 앞면을 거울에 둔 채 그 자리에서 뱅글뱅글 돈다. 순서를 뒤집으면
        // 세운 것이 Y 축으로 돌아 반 바퀴마다 두께만 보인다.
        Quaternion stand = Quaternion.Euler(fs.tilt, 0f, 0f);

        // 깨문 자리를 파지 않는 음식은 돌려 봐야 보여 줄 자리가 없다.
        if (!fs.carve || foodNow == null || foodNow.stage <= 0)
            return stand * Quaternion.Euler(0f, elapsed * fs.spin, 0f);

        float out0 = -BiteAngle(foodNow.stage);        // 패인 쪽이 거울을 본다
        float in1  = 180f - BiteAngle(foodNow.stage);  // 패인 쪽이 입을 본다
        return stand * Quaternion.Euler(
            0f, Mathf.Lerp(out0, in1, Mathf.Clamp01(toMouth))
                + Mathf.Sin(elapsed * 1.1f) * 5f, 0f);
    }

    private void AnimateFood()
    {
        if (food == null) return;

        FoodSpec fs = SpecOf(foodKind);
        if (foodNow == null) foodNow = Item(foodKind);

        // 음식마다 다른 값은 전부 표에 있다(FoodSpecs). 씹는 시간·속도·폭이
        // 진동 쪽 조각 수(ScenarioHaptics.BuildCrunch)와 짝이 맞아야 보이는
        // 것과 느껴지는 것이 어긋나지 않는다 — 표에 나란히 적어 둔 이유다.
        //
        // 사람은 이렇게 못 먹는다. 전엔 한 입이 4 초여서, 집어 들자마자
        // 삼키고 곧바로 다시 집는 것으로 보였다. 구간마다 늘려 한 입을
        // 6 초 안팎으로 뒀다. 진동은 깨무는 순간에 한 번 터지고 끝나는
        // 짧은 다발이라, 보이는 쪽을 늘려도 느껴지는 쪽과 어긋나지 않는다.
        float bite = BiteCycleOf(foodKind);

        float t = Mathf.Repeat(elapsed, bite);

        // 집어 드는 젤리는 한 입마다 다음 색이다. 접시에 무지개로 놓여
        // 있는데 집어 드는 것만 늘 빨강이면 같은 알을 계속 집는 것으로 보인다.
        if (foodKind == FoodKind.Jelly && jellyMat != null)
        {
            int hue = Mathf.FloorToInt(elapsed / bite) % JellyColors.Length;
            if (hue != jellyHue)
            {
                jellyHue = hue;
                jellyMat.color = JellyColors[hue];
            }
        }

        // 음식이 놓인 접시 자리. **거울 속** 상이므로 z 를 뒤집어 잡는다.
        //
        // 이 점은 방에 붙어 있어야 한다. 도구처럼 머리에 매달린 좌표로 두면
        // 고개를 돌릴 때 접시 위의 음식이 같이 따라 돌아 버린다. 그래서 리그
        // 좌표에서 월드로 올렸다가 toolRoot 로 다시 내린다.
        // BuildHome 의 Plate 와 같은 x·z 를 써야 음식이 접시 위에 놓인다.
        //
        // y 는 -0.40 으로 박아 뒀었다. 상판은 -0.335 라 음식이 접시를 뚫고
        // 상 **속에** 6 cm 들어가 있었다 — 접시 위에서 기다리는 구간이 통째로
        // 안 보였다는 뜻이다. 음식의 원점은 한가운데니까(BuildFoodItem),
        // 접시 바닥에 올려놓으려면 거기에 반높이를 더한다.
        float plateFloor = tableTopY + 0.010f;
        Vector3 plateRig = new Vector3(
            -0.13f, plateFloor + SitHeight(foodNow, fs), 2f * D - (D - 0.25f));
        Vector3 plateLocal = toolRoot.InverseTransformPoint(transform.TransformPoint(plateRig));

        // 음식의 원점은 한가운데라, 입술 지점에 그대로 놓으면 절반이 얼굴에 박힌다.
        // 반지름만큼 앞으로 빼서 입술에 닿게 한다.
        // 손이 아래에서 올라오므로 음식을 입술 한가운데에 두면 손이 코까지 올라온다.
        // 앞으로 빼는 양은 반지름을 따라간다. 사과(7.5 cm)에서 5 cm 가
        // 맞았으니 그 비율(0.60)을 그대로 쓴다 — 사탕에 5 cm 를 주면
        // 입술에서 한참 떨어진 허공에 떠 있다.
        Vector3 rest = mouthLocal
                     + Vector3.forward * (fs.size * 0.60f)
                     - Vector3.up * 0.008f;

        if (t < OnPlate)
        {
            // 아직 접시 위에 있다. 손은 내려와 있고 입도 다물려 있다.
            // 이 구간이 있어야 "책상 위의 것을 집어서" 가 눈에 보인다 —
            // 없으면 음식이 허공에서 생겨나 입으로 날아가는 것으로 보인다.
            // 다 먹었으면 접시에서 새 사과를 집는다. 먹던 사과는 깨문
            // 채로 접시 위에서 기다린다 — 쉬는 동안 저 혼자 온전해지면
            // 같은 사과가 아니라 다른 사과를 집은 것으로 보인다.
            if (foodNow.stage >= fs.bites)
            {
                foodNow.stage = 0;
                SetBiteStage(foodNow, 0);
            }
            food.SetActive(true);
            food.transform.localPosition = plateLocal;
            // 접시에서 기다릴 땐 패인 쪽을 거울로 둔다. 어차피 아무
            // 쪽이나 될 자리고, 몇 입 먹었는지 눈에 보이는 쪽이 낫다.
            food.transform.localRotation = FoodSpin(fs, 0f);
            SetMouth(0f);
            Contact = 0f;
            if (selfArm != null) selfArm.Rest();
            return;
        }

        float u = t - OnPlate;

        // 패인 쪽이 입을 보는 정도. 입으로 올리는 동안 돌려서 대고,
        // 씹으려고 내리는 동안 도로 거울 쪽으로 돌린다 (FoodSpin).
        float toMouth;

        if (u < ApproachTime)
        {
            // 집어서 입으로 가져간다.
            food.SetActive(true);
            food.transform.localPosition =
                Vector3.Lerp(plateLocal, rest, Ease(u / ApproachTime));
            toMouth = Ease(u / ApproachTime);
            // 치과에 맞춰 최대 벌림을 키웠더니(SetMouth 참고) 음식 쪽은 밥 먹는
            // 입이 아니라 비명 지르는 입이 됐다. 음식은 그 절반쯤이면 된다.
            // 얼마나 벌리는지도 음식마다 다르다 — 알사탕에 입을 크게
            // 벌리면 사탕이 아니라 주먹을 넣는 것으로 보인다.
            SetMouth(Ease(u / ApproachTime) * fs.open);   // 올라오는 동안 벌린다
            Contact = 0f;
        }
        else if (u < ApproachTime + fs.hold)
        {
            // 입술에 닿아 있는 순간. 여기를 건너뛰면 정작 베어 무는 장면이 안 보인다 —
            // 음식이 다 올라오자마자 사라져 버린다.
            food.SetActive(true);
            food.transform.localPosition = rest;
            // 벌린 입을 물어 내린다. 씹는 동안의 기준 벌림도 이 값이다.
            SetMouth(Mathf.Lerp(fs.open, fs.open * ChewBase,
                                (u - ApproachTime) / fs.hold));
            Contact = 1f;
            toMouth = 1f;   // 패인 쪽이 입에 닿아 있다
        }
        else
        {
            // 베어 물었다.
            float c = u - ApproachTime - fs.hold;
            bool chewing = c < fs.chew;

            // 깨무는 순간은 한 번만 센다. 사과는 여기서 한 조각 패이고,
            // 네 입째에 사라진다. 젤리는 한 알이니 통째로 들어간다.
            int cyc = Mathf.FloorToInt(elapsed / bite);
            if (cyc != biteCycle)
            {
                biteCycle = cyc;
                if (fs.bites > 1)
                {
                    foodNow.stage++;
                    SetBiteStage(foodNow, foodNow.stage);
                }
            }

            // 깨문 사과는 여전히 손에 있다. 입에서 조금 내려서 들고
            // 씹는다. 예전처럼 한 입에 통째로 감추면 먹는 게 아니라
            // 지워지는 것으로 보이고, 입에 붙여 둔 채로 두면 아직
            // 무는 중인지 씹는 중인지 분간이 안 된다.
            // 여러 입에 걸쳐 먹는 것은 깨문 채로 손에 남는다. 한 입에
            // 통째로 들어가는 것(젤리·푸딩·사탕)은 여기서 사라진다.
            bool inHand = fs.bites > 1 && foodNow.stage < fs.bites;
            food.SetActive(inHand);
            // 아래로만 내리면 모자란다. 손을 재 보니 손목이 입보다 2 cm
            // **위**에 있었다 (입 y 0.02, 손목 y 0.04). 가운데에 둔 채로
            // 내리면 주먹이 입을 따라 내려올 뿐이라 얼굴을 계속 덮는다.
            // 어깨가 x +0.15 에 있으니 그쪽으로 빼면 손이 얼굴 밖으로
            // 나가고, 덤으로 팔을 덜 뻗게 돼 어깨보다 18 cm 앞으로
            // 튀어나와 있던 팔꿈치도 몸쪽으로 들어온다.
            if (inHand)
                food.transform.localPosition = Vector3.Lerp(rest,
                    rest + Vector3.right * 0.13f
                         - Vector3.up * 0.18f
                         + Vector3.forward * 0.03f,
                    Ease(Mathf.Clamp01(c / Mathf.Max(fs.chew, 1e-3f))));

            // 손을 내리면서 베어 문 자리를 들여다본다 — 패인 쪽이
            // 입에서 거울 쪽으로 돌아 나온다.
            toMouth = 1f - Ease(Mathf.Clamp01(c / Mathf.Max(fs.chew, 1e-3f)));

            // 씹는 동안 입이 오물거린다. 진동이 나는 구간과 맞아떨어진다.
            SetMouth(chewing ? ChewJawOf(foodKind, c) : 0f);
            Contact = chewing ? 1f : 0f;
        }

        food.transform.localRotation = FoodSpin(fs, toMouth);

        // 남이 먹여 주는 게 아니라 제 손으로 먹는다. 음식이 보이는 동안만 팔을 올린다.
        if (selfArm != null)
        {
            if (food.activeSelf)
            {
                // 사과는 제자리에서 돌고 있어서, 사과 좌표로 손 자리를 잡으면 손도 같이 돈다.
                // 머리 축(toolRoot) 기준으로 사과의 아래·바깥쪽을 잡아야 자리가 고정된다.
                // 손목을 입술 높이에 두면 손바닥이 얼굴을 통째로 가린다 — 턱 아래로 내린다.
                // 자리는 눈으로 맞추지 말고 재서 맞춘다. 머리 축 좌표로 찍어 보면
                // 입이 (0, 0.02, 0.12), 어깨가 (0.15, -0.14, -0.01) 이라 손목이
                // 어디쯤 있어야 얼굴 밖인지가 바로 나온다 (AnimateFood 의 씹는 자리).
                // 감싸는 축은 앞뒤로 둔다. 가로축으로 두면 손가락이 위아래로 말려서
                // 손바닥이 거울을 정면으로 보고, 넓적한 손이 얼굴을 가려 버린다.
                // 앞뒤축이면 손이 옆에서 들어와 모로 보이므로 얼굴이 살아 있다.
                // 사과가 제자리에서 돌고 있으므로 축은 사과가 아니라 머리 축에서 잡는다.
                // 팔꿈치는 아래로. pole 은 팔꿈치의 반대쪽이라 위를 가리킨다.
                // -up 을 주면 팔꿈치가 들려서 아래팔이 수평으로 가로지른다 —
                // 사과를 옆에서 들이미는 모양이 돼 버린다. 이 팔은 리그 -X 쪽에
                // 있으므로 팔꿈치가 바깥(-X)으로 빠지게 pole 을 +X 로 기울인다.
                // 주먹이 아니라 **집게손**이다. 다섯 손가락을 같은 각으로 말면
                // 음식이 그 고리 한가운데 들어가서, 거울에서는 손등 위에 얹어
                // 놓은 것처럼 보였다. 젤리 한 알은 검지와 엄지로 집는다.
                // 손이 **아래에서** 들어오게 방향을 박아 준다. 비워 두면
                // 어깨에서 사과로 가는 선을 쓰는데, 그러면 손이 사과보다
                // 위에 서서 손가락이 입 위로 늘어진다.
                // 손가락 각은 쥐는 것의 굵기를 따라간다. 접는 각이 고리
                // 크기를 정하는데, 사과를 7.5 cm 로 키웠더니 5.5 cm 에
                // 맞춰 둔 각으로는 손가락이 사과를 뚫고 지나갔다.
                // 젤리 한 알은 그대로 바싹 집는다.
                // 쥐는 자리를 음식 **밑으로** 내린다.
                //
                // GripAround 의 `back` 으로는 안 된다. 그 함수는 손가락
                // 고리를 한 번 접어 보고 고리 한가운데가 목표에서 빗나간
                // 만큼 손을 옮겨 다시 접는데, 그 보정이 back 을 도로
                // 지운다 — 고리는 언제나 넘겨준 점을 한가운데 둔다.
                // 그러니 넘겨주는 점 자체를 내려야 한다.
                //
                // 사과와 젤리는 0 이다. 둥글고 굵어서 주먹 위로 솟는다.
                // 납작한 고기·푸딩·사탕은 한가운데를 쥐면 조각이 통째로
                // 고리 안에 들어가 손가락 뒤로 사라진다 — 실제로도 이런
                // 건 아래쪽 끝을 집어 든다.
                //
                // 내리는 양은 **미터로 박는다.** 처음엔 음식 반높이에
                // 대한 비율로 뒀는데, 넓고 납작한 푸딩은 반높이가 8 mm
                // 뿐이라 0.6 을 줘도 5 mm 밖에 안 내려가 그대로 묻혔다.
                // 고리 크기는 음식이 아니라 **손**이 정하니 손 치수로
                // 적는 게 맞다.
                Vector3 hold = food.transform.position - toolRoot.up * fs.grip;
                GripAround(selfArm, hold,
                           toolRoot.forward, toolRoot.up, 0f,
                           fs.curl, fs.thumb, fs.fold,
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

        // 글자에 **외곽선**을 두른다.
        //
        // 흰 글자만 띄우면 배경이 밝을 때 사라진다. 치과 화면은 아래가 흰
        // 기기 패널이라 「DENTAL DRILL」이 반쪽만 보였고, 조명을 밝힌
        // 뒤로 더 심해졌다.
        //
        // 처음엔 뒤에 어두운 판을 깔았다. 그런데 TextMesh 는 한 번 그려
        // 보기 전에는 경계 상자를 안 알려 줘서 판 크기를 글자 수로
        // 짐작해야 했고, 세로로 계속 어긋났다. 같은 글자를 어두운 색으로
        // 네 방향에 격자 넣어 깔면 크기를 재지 않고도 **글자 모양에**
        // 정확히 붙는다. 자막이 밝은 화면 위에서도 읽히는 원리다.
        float em = label.fontSize * label.characterSize / 10f;
        float off = em * 0.075f;
        Vector2[] dirs = {
            new Vector2(-1f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, -1f), new Vector2(0f, 1f),
        };
        labelEdge = new TextMesh[dirs.Length];
        for (int i = 0; i < dirs.Length; i++)
        {
            GameObject eg = new GameObject("LabelEdge" + i);
            eg.transform.SetParent(go.transform, false);
            // 살짝 뒤로 밀어야 흰 글자가 위에 그려진다.
            eg.transform.localPosition =
                new Vector3(dirs[i].x * off, dirs[i].y * off, 0.0006f);
            TextMesh t = eg.AddComponent<TextMesh>();
            t.anchor = label.anchor;
            t.alignment = label.alignment;
            t.fontSize = label.fontSize;
            t.characterSize = label.characterSize;
            t.color = new Color(0.03f, 0.03f, 0.05f);
            if (f != null)
            {
                t.font = f;
                eg.GetComponent<MeshRenderer>().sharedMaterial = f.material;
            }
            eg.GetComponent<MeshRenderer>().shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            labelEdge[i] = t;
        }

        go.SetActive(false);
    }

    private void ShowLabel(Scenario s)
    {
        if (label == null) return;

        string text;
        switch (s)
        {
            case Scenario.Food:
                text = "EATING  (" + SpecOf(foodKind).label + ")";
                break;
            case Scenario.Dental:
                text = dentalKind == DentalKind.Brush ? "TOOTHBRUSH" : "DENTAL DRILL";
                break;
            default:
                text = "";
                break;
        }

        // 한때 여기에 ToolModels.BaseShaderName 을 같이 찍었다. 폰에서 화면이
        // 통째로 분홍색으로 나왔을 때 어느 셰이더가 잡혔는지 보려던 것인데,
        // 11/5 빌드에서 세 장면 다 정상으로 확인돼서 뺀다. 또 분홍색이 나오면
        // 그 프로퍼티를 다시 여기에 붙여 보면 된다.
        label.text = text;
        label.gameObject.SetActive(text.Length > 0);
        if (labelEdge != null)
            foreach (TextMesh t in labelEdge)
                if (t != null) t.text = text;
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

using UnityEngine;

/// <summary>
/// 거울 방 **옆에 떠 있는 진동 설정창**. 보고 + 탭으로 만진다.
///
/// 왜 이렇게 생겼나 —
///
/// (가) **글자를 안 쓴다.** 이 앱에서 기본 글꼴(LegacyRuntime.ttf)로 찍은
///      글자는 아이폰에서 깨져 나왔다. 거울 속 안내 글자도, 아래쪽 HUD 도
///      그래서 지워야 했다. 그 글꼴을 다시 쓰면 같은 일이 난다. 그러니
///      그림과 **칸 막대**로만 적는다.
///
/// (나) **머리에 붙이지 않는다.** 「화면 옆에」 라고 해서 머리에 달면
///      설정창이 눈을 따라다녀서 **영영 쳐다볼 수가 없다** — 고개를
///      돌려도 창이 같이 돌아간다. 그래서 몸 기준으로 오른쪽 40° 에
///      세워 둔다. 앞을 보면 화면 가장자리에 걸치고, 고개를 그쪽으로
///      돌리면 한가운데로 들어와 고를 수 있다.
///
/// (다) **입력은 탭 하나뿐이다.** Cardboard 버튼이 전부다(Api.IsTriggerPressed).
///      그래서 겨눈 줄에만 탭이 먹는다. 설정창을 안 보고 있을 때의 탭은
///      원래대로 다음 블록으로 넘어간다 — <see cref="Step"/> 가 탭을
///      썼는지 알려 주므로 부르는 쪽이 가린다.
/// </summary>
public static class HapticPanel
{
    // ───────────────────────── 값 ─────────────────────────

    /// <summary>진동 세기 배수. 진동을 내보내는 모든 자리에 곱한다.</summary>
    public static float AmpScale { get { return AmpSteps[ampIdx]; } }

    /// <summary>
    /// 한 입 안에서 **탁과 탁 사이**를 늘리고 줄이는 배수 (= 주기).
    ///
    /// ScenarioHaptics.BuildCrunch 의 조각 사이 침묵에 곱한다. 씹는
    /// 소리도 **같은 timings 배열**에서 나오므로(ScenarioAudio.CrunchClip)
    /// 소리와 진동이 저절로 같이 늘어난다 — 따로 곱하면 어긋난다.
    /// </summary>
    public static float GapScale { get { return GapSteps[gapIdx]; } }

    /// <summary>
    /// 치과 드릴 진동이 **녹음을 얼마나 따라갈지** (= 부하 깊이).
    ///
    /// 0 이면 내내 같은 세기로 운다 — 11/15 까지의 드릴이 이것이었다.
    /// 1 이면 ScenarioAudio.DrillLoad 를 그대로 쓴다. 날이 이를 파고들며
    /// 소리가 커질 때 손도 같이 세지고, 공회전으로 돌아오면 같이
    /// 잦아든다. 1 을 넘기면 **녹음보다 더** 꿀렁인다.
    ///
    /// **드릴에서만 먹는다.** 같은 치과 블록의 칫솔은 조원이 맡은
    /// 자극이라 손대지 않았다. 그래서 칫솔 블록에서는 이 줄이 흐리고,
    /// 켜진 손잡이가 세기 하나뿐이다. 칫솔에서 무엇을 조절하게 할지는
    /// 그쪽에서 정하면 여기에 들어온다.
    ///
    /// 1 이 기본이다. 「음성에 맞게」 가 글자 그대로 1 이고, 손잡이는
    /// 거기서 양쪽으로 열려 있다 — 0 쪽은 베이스라인에 가깝고 1.4 쪽은
    /// 실제보다 부풀린 쪽이다. 과장이 현장감을 올리는지 내리는지가
    /// 이 연구가 물을 수 있는 것 중 하나다.
    /// </summary>
    public static float LoadDepth { get { return LoadSteps[loadIdx]; } }

    private static readonly float[] AmpSteps  = { 0.30f, 0.60f, 1.00f, 1.40f, 1.80f };
    private static readonly float[] GapSteps  = { 0.40f, 0.70f, 1.00f, 1.50f, 2.20f };
    private static readonly float[] LoadSteps = { 0.00f, 0.35f, 0.70f, 1.00f, 1.40f };

    private static int ampIdx = 2;      // 가운데가 지금까지 쓰던 값이다
    private static int gapIdx = 2;
    private static int loadIdx = 3;     // 1.00 = 녹음 그대로

    // ───────────────────────── 생김새 ─────────────────────────

    private const float Yaw   = 42f;    // 몸 앞에서 오른쪽으로 (도)

    /// <summary>
    /// 눈에서 창까지 (m). **거울면보다 가까워야 한다** (MirrorRig.D = 0.45).
    ///
    /// 처음에 0.60 으로 뒀다가 창이 거울 **뒤**로 들어가서 거울 틀이
    /// 창을 세로로 갈랐다. 찍어 놓고도 한동안 "창이 작고 멀다" 로 잘못
    /// 읽어 엉뚱한 데(부모·배율)를 두 번 고쳤다 — 창은 내내 0.603 m 에
    /// 배율 1 로 제대로 있었고, 틀린 건 이 수 하나였다. 이 방은 눈에서
    /// 거울까지가 45 cm 밖에 안 된다.
    /// </summary>
    private const float Dist  = 0.38f;

    /// <summary>
    /// 칸 치수를 적어 둔 기준 거리. Dist 를 바꿔도 창이 눈에 **같은
    /// 크기**로 보이도록 이 비로 배율을 준다 — 안 그러면 거리를 고칠
    /// 때마다 칸 치수를 전부 다시 적어야 한다.
    /// </summary>
    private const float DesignDist = 0.60f;

    private const float Drop  = -0.05f; // 눈높이보다 살짝 아래
    private const int   Cells = 5;

    /// <summary>
    /// 손잡이 수. 셋이지만 **한 시나리오에서 먹는 것은 둘뿐이다** —
    /// 세기는 늘, 주기는 음식에서만, 부하 깊이는 치과에서만.
    /// </summary>
    private const int Rows = 3;

    private const float CellW = 0.030f, CellH = 0.020f, CellGap = 0.007f;

    /// 맨 윗줄의 y 와 줄 간격. 둘이던 때는 ±0.042 였고, 셋이 되면서
    /// 판을 0.056 → 0.050 으로 낮추고 간격을 0.060 으로 잡았다 —
    /// 판 사이에 0.010 이 남아 줄이 붙어 보이지 않는다.
    private const float Row0Y = 0.060f, RowPitch = 0.060f;
    private const float PlateH = 0.050f;

    private static readonly Color Back = new Color(0.09f, 0.10f, 0.13f);
    private static readonly Color Idle = new Color(0.22f, 0.24f, 0.29f);
    private static readonly Color Lit  = new Color(1.00f, 0.72f, 0.25f);
    private static readonly Color Aim  = new Color(0.30f, 0.80f, 0.95f);
    private static readonly Color Dull = new Color(0.16f, 0.17f, 0.20f);

    private static Transform root;
    private static Transform head;
    private static float bodyYaw;       // 창을 세워 둔 **몸의** 방향 (도)
    private static readonly Transform[] rowPlate = new Transform[Rows];
    private static readonly Transform[,] cell = new Transform[Rows, Cells];
    private static Material matBack, matIdle, matLit, matAim, matDull, matIcon;
    private static int aimedRow = -1;

    /// <summary>창이 서 있나.</summary>
    public static bool Alive { get { return root != null; } }

    /// <summary>지금 겨눈 줄 (-1 = 안 겨눔). 재 보려고 (Editor/PanelShot).</summary>
    public static int AimedRow { get { return aimedRow; } }

    /// <summary>
    /// 창을 세운다. <paramref name="body"/> 는 **머리가 아닌** 것이라야
    /// 한다 (리그 뿌리). 머리에 달면 쳐다볼 수가 없다 — 위 (나) 참고.
    /// </summary>
    public static void Create(Transform body, Transform headCam)
    {
        Dispose();
        if (body == null) return;
        head = headCam;

        matBack = ToolModels.MakeGlow(Back);
        matIdle = ToolModels.MakeGlow(Idle);
        matLit  = ToolModels.MakeGlow(Lit);
        matAim  = ToolModels.MakeGlow(Aim);
        matDull = ToolModels.MakeGlow(Dull);
        matIcon = ToolModels.MakeGlow(new Color(0.78f, 0.82f, 0.88f));

        GameObject go = new GameObject("HapticPanel");
        root = go.transform;
        // 리그 **밑에 넣지 않는다.** 처음엔 리그 뿌리에 달고 거기 기준으로
        // 자리를 적었는데, 리그 뿌리는 눈이 있는 데가 아니라 방 원점이라
        // 창이 몇 미터 밖 벽 속에 박혔다 (찍어 보고 알았다 — 화각 80° 에서
        // 손톱만 하게 나왔고 40° 로 돌아봐도 한가운데 안 왔다).
        //
        // 자리는 **눈에서** 잰다. 방향만 몸을 따른다 — 고개를 돌려도
        // 창은 제자리에 있어야 쳐다볼 수 있다.
        // **아무 데도 안 붙인다.** 리그는 방 전체를 들고 있고 배율이나
        // 자리가 언제 바뀔지 모르는데, 창은 눈 옆 고정 거리에 있어야
        // 한다. 붙여 두면 그 변화를 고스란히 물려받는다. 치울 때는
        // Dispose 가 통째로 지운다.
        root.SetParent(null);
        bodyYaw = body.eulerAngles.y;
        Place();

        Quad(root, matBack, Vector3.zero,
             new Vector3(0.30f, 0.20f, 0.008f), "Back");

        for (int row = 0; row < Rows; row++)
        {
            float y = Row0Y - row * RowPitch;
            // x 는 **왼쪽이 양수**다. 창을 참가자 쪽으로 180° 돌려
            // 세우므로 로컬 +X 가 보는 사람의 왼쪽이 된다. 처음에 그냥
            // 적었더니 그림이 오른쪽에 붙고 눈금이 오른쪽부터 찼다.
            rowPlate[row] = Quad(root, matDull, new Vector3(-0.012f, y, 0.005f),
                                 new Vector3(0.268f, PlateH, 0.004f),
                                 "Row" + row);

            // 왼쪽 그림. 글자를 못 쓰니 이것이 이름표다. 셋이 서로
            // **다른 생김새**라야 한다 — 세기는 올라가는 계단,
            // 주기는 간격이 벌어지는 막대, 부하는 부풀었다 꺼지는 산.
            if (row == 0)      IconAmp(root, y);
            else if (row == 1) IconGap(root, y);
            else               IconLoad(root, y);

            for (int i = 0; i < Cells; i++)
                cell[row, i] = Quad(root, matIdle,
                    new Vector3(0.062f - i * (CellW + CellGap), y, 0.009f),
                    new Vector3(CellW, CellH, 0.004f),
                    "R" + row + "C" + i);
        }
        Paint(true, true);
    }

    /// <summary>
    /// 매 프레임 부른다. 겨눈 줄을 밝히고, 탭을 썼으면 true 를 준다.
    /// </summary>
    /// <param name="tapped">이 프레임에 탭이 시작됐나.</param>
    /// <param name="gapUseful">
    /// 주기 줄이 지금 먹나. 조각 사이 침묵이란 것은 음식에만 있다.
    /// </param>
    /// <param name="loadUseful">
    /// 부하 깊이 줄이 지금 먹나. 지금은 **치과의 드릴에서만**이다.
    ///
    /// 둘을 따로 받는 이유. 처음엔 "음식인가" 하나로 갈라서 음식이
    /// 아니면 부하 줄을 켰는데, 치과 블록에는 드릴과 **칫솔**이 둘 다
    /// 있다. 칫솔에서는 이 값이 쓰이는 데가 없으므로 그 줄은 켜 놓고
    /// 눌러도 **아무 일이 없는 값**이 된다 — 흐린 줄을 눌러도 값이
    /// 몰래 바뀌던 것과 같은 허물이다. 한동안 칫솔 쪽에도 이 값을
    /// 먹여 두었다가, 조원이 맡은 자극이라 뺐다.
    ///
    /// 안 먹는 줄은 흐리게 해서 **안 먹는다는 것을 보여 준다.** 안
    /// 그러면 눌러 보고 아무 일도 안 일어나는 것을 고장으로 읽는다.
    /// </param>
    public static bool Step(bool tapped, bool gapUseful, bool loadUseful)
    {
        if (root == null || head == null) return false;

        Place();
        aimedRow = Aimed();

        bool used = false;
        if (tapped && aimedRow >= 0)
        {
            // 끝 칸에서 한 번 더 누르면 처음으로 돌아간다. 탭이 하나뿐이라
            // 올리고 내리기를 둘 다 둘 수가 없다.
            //
            // **안 먹는 줄은 값을 안 바꾸되 탭은 먹는다.** 전에는 흐린
            // 줄을 눌러도 숨은 값(gapIdx)이 올라가고 탭도 사라졌다 —
            // 보이는 것은 아무 일 없는데 값이 몰래 바뀌는 셈이었다.
            // 그렇다고 탭을 흘려보내면 창을 겨누고 누른 것이 **다음
            // 블록으로 넘어가** 버린다. 실험을 건너뛰는 쪽이 훨씬 나쁘다.
            if (Live(aimedRow, gapUseful, loadUseful))
            {
                if (aimedRow == 0)      ampIdx  = (ampIdx  + 1) % Cells;
                else if (aimedRow == 1) gapIdx  = (gapIdx  + 1) % Cells;
                else                    loadIdx = (loadIdx + 1) % Cells;
            }
            used = true;
        }
        Paint(gapUseful, loadUseful);
        return used;
    }

    /// <summary>이 줄이 지금 시나리오에서 먹나. 세기는 늘 먹는다.</summary>
    private static bool Live(int row, bool gapUseful, bool loadUseful)
    {
        return row == 0 || (row == 1 ? gapUseful : loadUseful);
    }

    /// <summary>
    /// 겨눈 줄. 눈길을 **창 평면에 꽂아** 어느 띠에 떨어지는지로 가린다.
    ///
    /// 전에는 줄 가운데에서 몇 도 안쪽이냐(9° 원뿔)로 봤다. 줄이 둘일
    /// 때는 8° 씩 떨어져 있어 그걸로 됐다. 셋이 되면서 간격이 5.7° 로
    /// 좁아져 원뿔이 이웃 줄까지 덮는다 — 가까운 쪽이 이기므로 줄
    /// 사이를 겨누면 **1 도도 안 되는 차이로 승자가 바뀐다.** 평면에
    /// 꽂으면 띠 경계가 그대로 경계라 그런 흔들림이 없다.
    /// </summary>
    private static int Aimed()
    {
        // 앞면(+Z)이 참가자를 보고 있으므로 법선은 root.forward 다.
        Plane face = new Plane(root.forward, root.position);
        Ray ray = new Ray(head.position, head.forward);
        float hit;
        if (!face.Raycast(ray, out hit)) return -1;

        // InverseTransformPoint 가 배율까지 나눠 주므로, 아래 수들은
        // 칸 치수를 적어 둔 것과 같은 단위다 (DesignDist 참고).
        Vector3 at = root.InverseTransformPoint(ray.GetPoint(hit));
        if (Mathf.Abs(at.x) > 0.15f || Mathf.Abs(at.y) > 0.10f) return -1;

        int row = Mathf.RoundToInt((Row0Y - at.y) / RowPitch);
        return row >= 0 && row < Rows ? row : -1;
    }

    public static void Dispose()
    {
        if (root != null) Kill(root.gameObject);
        root = null; head = null; aimedRow = -1;
        for (int r = 0; r < Rows; r++)
        {
            rowPlate[r] = null;
            for (int i = 0; i < Cells; i++) cell[r, i] = null;
        }
    }

    /// <summary>지금 값을 한 줄로. 로그에만 쓴다 (화면에는 글자가 없다).</summary>
    public static string Info()
    {
        return "amp x" + AmpScale.ToString("F2")
             + "  gap x" + GapScale.ToString("F2")
             + "  load x" + LoadDepth.ToString("F2");
    }

    // ───────────────────────── 속 ─────────────────────────

    /// <summary>
    /// 눈 옆 제자리에 둔다. 머리가 움직이면 따라가되 **고개 방향은 안
    /// 따른다** — 창이 눈을 따라다니면 영영 못 쳐다본다.
    /// </summary>
    private static void Place()
    {
        if (root == null || head == null) return;
        Quaternion body = Quaternion.Euler(0f, bodyYaw + Yaw, 0f);
        root.position = head.position + body * (Vector3.forward * Dist)
                      + Vector3.up * Drop;
        // 앞면(+Z)이 참가자를 보게 돌린다.
        root.rotation = Quaternion.Euler(0f, bodyYaw + Yaw + 180f, 0f);
        root.localScale = Vector3.one * (Dist / DesignDist);
    }

    private static void Paint(bool gapUseful, bool loadUseful)
    {
        for (int row = 0; row < Rows; row++)
        {
            bool live = Live(row, gapUseful, loadUseful);
            int lit = row == 0 ? ampIdx : row == 1 ? gapIdx : loadIdx;
            if (rowPlate[row] != null)
                Set(rowPlate[row], aimedRow == row ? matAim : matDull);
            for (int i = 0; i < Cells; i++)
            {
                if (cell[row, i] == null) continue;
                // 켜진 칸까지 **채운다.** 점 하나만 옮기는 것보다 눈금이
                // 몇 칸인지가 한눈에 들어온다.
                Set(cell[row, i], i <= lit && live ? matLit : matIdle);
            }
        }
    }

    /// <summary>세기 — 점점 높아지는 막대 셋.</summary>
    private static void IconAmp(Transform p, float y)
    {
        float[] h = { 0.010f, 0.018f, 0.026f };
        for (int i = 0; i < 3; i++)
            Quad(p, matIcon, new Vector3(0.122f - i * 0.010f, y, 0.009f),
                 new Vector3(0.006f, h[i], 0.004f), "IconAmp" + i);
    }

    /// <summary>주기 — 사이가 점점 벌어지는 막대 넷.</summary>
    private static void IconGap(Transform p, float y)
    {
        float[] x = { 0.130f, 0.122f, 0.111f, 0.097f };
        for (int i = 0; i < 4; i++)
            Quad(p, matIcon, new Vector3(x[i], y, 0.009f),
                 new Vector3(0.004f, 0.020f, 0.004f), "IconGap" + i);
    }

    /// <summary>
    /// 부하 깊이 — **부풀었다 꺼지는 산.** 가운데가 솟은 막대 다섯이다.
    ///
    /// 세기 그림(올라가는 계단)과 헷갈리면 안 되니 대칭으로 둔다.
    /// 계단은 "끝이 더 세다", 산은 "오르내린다" 로 읽힌다.
    /// </summary>
    private static void IconLoad(Transform p, float y)
    {
        float[] h = { 0.008f, 0.017f, 0.026f, 0.017f, 0.008f };
        for (int i = 0; i < 5; i++)
            Quad(p, matIcon, new Vector3(0.130f - i * 0.009f, y, 0.009f),
                 new Vector3(0.005f, h[i], 0.004f), "IconLoad" + i);
    }

    private static Transform Quad(Transform parent, Material mat,
                                  Vector3 pos, Vector3 size, string name)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Kill(go.GetComponent<Collider>());     // 물리를 안 쓴다
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go.transform;
    }

    private static void Set(Transform t, Material m)
    {
        Renderer r = t.GetComponent<Renderer>();
        if (r != null && r.sharedMaterial != m) r.sharedMaterial = m;
    }

    private static void Kill(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Object.Destroy(o);
        else Object.DestroyImmediate(o);
    }
}

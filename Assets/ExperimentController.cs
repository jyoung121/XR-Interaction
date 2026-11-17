using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using Google.XR.Cardboard;

public enum ExperimentCase
{
    Case1_VisualOnly,            // Case 1: 시각 화살표만 (베이스라인)
    Case2_Visual_PulseInterval,  // Case 2: 시각 + 진동 주기 변조 (가이거 방식)
    Case3_Visual_Intensity       // Case 3: 시각 + 진동 세기 변조
}

/// <summary>
/// Cardboard 360° 탐색 실험 러너.
///
/// 원본 대비 바뀐 점
///  1) 여러 시행을 자동 진행. 조건(블록)도 자동 전환되므로 조건마다 재빌드할 필요가 없다.
///  2) 타겟 이심각(45/105/165°)과 좌/우를 통제한다. 원본은 구면에 뿌린 뒤 무작위로 정답을 골라
///     시작 각도가 통제되지 않았고, 그래서 "멀수록 햅틱 이득이 커진다"는 상호작용을 볼 수 없었다.
///  3) 타겟을 수평 띠 안에만 둔다(머리 위/아래 제외). 좌식에서 편하고 방위각·고도가 섞이지 않는다.
///  4) 시행별 CSV 저장(탐색시간, 총시간, 첫 회전 방향, 반전 횟수, 궤적 효율, 오클릭).
///  5) 안내 차단 각도를 화살표와 진동에 똑같이 적용 → 조건 간 정보량이 어긋나지 않는다.
///  6) 확정 수단을 트리거 하나로 통일(dwellSeconds=0 기본). 섞이면 시간 측정이 더러워진다.
///
/// Inspector 연결(headCamera / visualArrowUI / targetPrefab)은 기존 필드명을 유지해 그대로 살아 있다.
/// </summary>
public class ExperimentController : MonoBehaviour
{
    // ───────────────────────── 기존 필드 (씬 연결 유지를 위해 이름 그대로) ─────────────────────────

    [Header("실험 조건")]
    [Tooltip("단일 블록 모드(runAllBlocks=false)에서만 사용")]
    public ExperimentCase currentCase = ExperimentCase.Case1_VisualOnly;

    [Header("참조 컴포넌트")]
    public Transform headCamera;
    public RectTransform visualArrowUI;
    public GameObject targetPrefab;

    [Header("타겟 배치")]
    public int totalDistractorCount = 11;
    public float sphereRadius = 5.0f;
    [Tooltip("이 각도 안에서 트리거를 누르면 '찾음'으로 인정")]
    public float selectAngleDeg = 8f;
    [Tooltip("응시 유지 확정(초). 0 = 트리거로만 확정. 측정을 깨끗하게 하려면 0 권장")]
    public float dwellSeconds = 0f;

    [Header("햅틱 파라미터")]
    public float minIntervalMs = 80f;
    public float maxIntervalMs = 900f;
    public long pulseDurationMs = 45;
    [Tooltip("Case 3 에서 정면(0도)일 때 세기. 0~255, 안드로이드 스케일과 같다")]
    public float intensityNear = 255f;
    [Tooltip("Case 3 에서 정반대(180도)일 때 세기. 가이드 기본값 30 은 CoreHaptics 0.118 에 해당하며, " +
             "폰을 Cardboard 상자에 넣으면 느껴지지 않을 수 있다")]
    public float intensityFar = 30f;
    [Tooltip("켜면 각도 비율을 제곱해, 가까워질 때 더 급격하게 변한다 (가이드 2-3(A))")]
    public bool squareCurve = false;
    [Tooltip("Case 3 세기를 몇 단계로 끊을지. 0 = 연속 (가이드 2-3(B))")]
    public int intensitySteps = 0;

    // ───────────────────────── 새로 추가된 필드 ─────────────────────────

    [Header("참가자 / 블록")]
    [Tooltip("참가자 번호. 라틴방격 블록 순서를 결정한다 (6명이면 0~5가 모든 순서를 한 번씩)")]
    public int participantId = 0;
    [Tooltip("체크하면 3개 조건을 라틴방격 순서로 전부 진행. 해제하면 currentCase 하나만")]
    public bool runAllBlocks = true;

    [Header("시행 구성")]
    [Tooltip("통제할 이심각(도). 시행 시작 시점의 머리 방향 기준")]
    public float[] eccentricitiesDeg = { 45f, 105f, 165f };
    [Tooltip("이심각 × 좌우 조합당 반복 수. 3×2×6 = 블록당 36시행")]
    public int repetitionsPerCell = 6;
    [Tooltip("블록 시작 시 연습 시행 수 (분석 제외)")]
    public int practiceTrials = 6;
    [Tooltip("시행 간 간격(초)")]
    public float interTrialSeconds = 1.5f;
    [Tooltip("한 시행 제한 시간(초). 넘으면 timeout 으로 기록하고 다음 시행")]
    public float trialTimeoutSeconds = 20f;

    [Header("타겟 배치 세부")]
    [Tooltip("수평 기준 위아래 흔들기(도). 0이면 완전 수평")]
    public float elevationJitterDeg = 10f;
    [Tooltip("방해 물체끼리, 그리고 정답과 최소로 떨어질 각도(도)")]
    public float minSeparationDeg = 18f;
    [Tooltip("켜면 물체를 위아래까지 전 구면에 뿌린다. 끄면 아래 elevationJitterDeg 만큼의 " +
             "수평 띠에만 놓는다(이전 동작)")]
    public bool fullSphere = true;
    [Tooltip("전 구면 배치에서, 정답이 똑바로 위/아래에서 최소로 떨어질 각도. " +
             "0 이면 정답이 천정이나 바닥에 올 수 있어 좌우(side) 구분이 흐려진다")]
    public float sideRollMarginDeg = 15f;

    [Header("안내 차단")]
    [Tooltip("타겟까지 각도가 이 값보다 작아지면 화살표와 진동을 '함께' 끈다. " +
             "0이면 확정 직전까지 계속 안내. 원본 동작(화살표만 37°에서 사라짐)은 조건 간 정보량이 어긋났다.")]
    public float guidanceCutoffAngleDeg = 0f;

    [Header("색 / 드웰 표시")]
    [Tooltip("평소 색. 모든 물체가 같은 색이라 정답을 눈으로 바로 가려낼 수 없다")]
    public Color idleColor = Color.white;
    [Tooltip("드웰해서 가려냈을 때의 색")]
    public Color revealColor = Color.red;
    [Tooltip("찾은 뒤 빨개진 정답을 몇 초 보여 주고 치울지. " +
             "interTrialSeconds 보다 길면 그 값으로 잘린다")]
    public float foundFeedbackSeconds = 0.6f;

    [Header("진단 / 테스트")]
    [Tooltip("켜면 시행 수를 줄여 빠르게 한 바퀴 돌린다. 본 실험에서는 꺼야 한다")]
    public bool testMode = false;
    [Tooltip("앱 시작 시 참가자 번호를 화면에서 고른다. 끄면 위 participantId 값을 그대로 쓴다. " +
             "**꺼 두었다** — 폰에 올려 눈으로 볼 때 거칠 게 없도록. " +
             "본 실험에서는 반드시 다시 켤 것: 꺼 두면 매번 P00 에 같은 블록 순서라 " +
             "라틴방격이 무너지고 CSV 도 같은 번호로 쌓인다")]
    public bool askParticipantId = false;
    [Tooltip("앱 시작 시 햅틱을 점검한다. 시스템 햅틱이 꺼진 폰을 걸러낸다. " +
             "**꺼 두었다** — 켜면 시작하자마자 진동부터 울린다. " +
             "폰에서 아무것도 안 느껴지면 이것부터 다시 켜 볼 것")]
    public bool runHapticSelfTest = false;
    [Tooltip("켜면 시작할 때 진동 프리셋을 비교하는 화면을 띄운다. 값을 정하는 용도이므로 " +
             "본 실험에서는 반드시 꺼야 한다")]
    public bool runHapticTuning = false;
    [Tooltip("켜면 시작할 때 진동 패턴 10가지를 하나씩 확인하는 화면을 띄운다. " +
             "아이폰에서 패턴이 되는지 보는 용도이므로 본 실험에서는 꺼야 한다")]
    public bool runPatternTest = true;

    /// <summary>
    /// 이 빌드를 알아보는 표시. **빌드할 때마다 손으로 올린다.**
    ///
    /// 11/14 에 고친 걸 올리고도 폰 화면이 그대로여서, 고친 게 안 먹은
    /// 건지 **옛 앱이 그대로 깔려 있는 건지** 구분할 수가 없었다.
    /// 화면에 적어 두면 한 번에 끝난다.
    /// </summary>
    public const string BuildStamp = "1116-A";

    [Tooltip("앱을 켜면 **곧장 거울 방으로** 간다 — 설정창을 건너뛴다. " +
             "꺼 두면 설정창(runPatternTest)이 먼저 나오고, 거기서 " +
             "거울 방으로 들어간다. 지금은 설정창을 첫 화면으로 쓴다")]
    public bool goStraightToMirror = false;

    [Tooltip("VR 화면 안에 상태를 표시한다 (iOS 는 로그를 볼 수 없으므로 필요)")]
    public bool showHud = false;

    [Header("진동 패턴 (패턴 확인 모드에서만 쓴다)")]
    // 조원 원본 ExperimentController 의 기본값과 같은 값이다. 본 실험(Case 1/2/3)은
    // 이 값을 쓰지 않는다 — 패턴이 아이폰에서 되는지 보는 확인 모드 전용이다.
    public long waveStepMs = 20;
    public long waveGapMs = 20;
    public int waveHighAmplitude = 255;
    public int waveLowAmplitude = 70;
    public int envelopeDurationMs = 200;
    public int envelopeSampleMs = 20;
    public float envelopeModulationHz = 5f;
    [Tooltip("확인 모드에서 같은 패턴을 몇 ms 마다 되풀이할지")]
    public float patternRepeatMs = 700f;
    [Tooltip("설정 화면에서 Preview 로 들어 볼 패턴")]
    public VibrationPattern previewPattern = VibrationPattern.SinglePulse;

    [Header("기록")]
    [Tooltip("비워 두면 persistentDataPath 에 저장")]
    public string csvFileName = "";
    [Tooltip("선택 사항. 연결하면 진행 상황을 화면에 표시")]
    public Text statusText;

    // ───────────────────────── 내부 상태 ─────────────────────────

    private enum Phase { Ready, Trial, InterTrial, BlockRest, Finished }

    /// <summary>시행이 어떻게 끝났는지. 확정 수단이 섞이면 total_time 의 의미가 달라지므로 기록한다.</summary>
    private enum EndReason { Trigger, Dwell, Timeout }

    private struct TrialSpec
    {
        public float eccentricity;
        public int side;        // -1 = 왼쪽, +1 = 오른쪽
        public bool isPractice;
    }

    private static readonly int[][] LatinSquare =
    {
        new[] { 0, 1, 2 }, new[] { 0, 2, 1 },
        new[] { 1, 0, 2 }, new[] { 1, 2, 0 },
        new[] { 2, 0, 1 }, new[] { 2, 1, 0 }
    };

    private Phase phase = Phase.Ready;
    private List<ExperimentCase> blockOrder = new List<ExperimentCase>();
    private int blockIndex = 0;
    private List<TrialSpec> trials = new List<TrialSpec>();
    private int trialIndex = 0;

    private readonly List<GameObject> pool = new List<GameObject>();
    // 매 프레임 GetComponent 를 부르지 않도록 미리 받아 둔다
    private readonly List<Renderer> poolRenderers = new List<Renderer>();
    private bool showingFoundFeedback;   // 찾은 정답을 보여 주는 중인가
    private PatternSettingsMenu settingsMenu;
    private bool xrOk;                       // 마지막 StartXR 이 성공했나
    private bool triggerLatched;             // 직전 프레임에 손가락이 닿아 있었나
    private bool triggerPressedThisFrame;    // 이 프레임에 탭이 '시작'됐나
    private Transform correctTarget;

    private float hapticTimer, dwellTimer, phaseTimer;
    private float trialStartTime;
    private TrialSpec currentTrial;

    // 측정값
    private float searchTime;            // 타겟에 처음 들어간 시각
    private bool entered;
    private float enterHoldTimer;
    private float signedYawAtOnset;      // + = 오른쪽이 최단
    private float prevYaw;
    private float accumulatedYaw;        // Σ|Δyaw|
    private float netYawSinceOnset;
    private int firstTurnSign;           // 0 = 아직, ±1
    private int reversals;
    private int lastMoveSign;
    // 좌우만 세면 타겟이 위아래에 있을 때 탐색량이 과소측정된다.
    // 시선 방향이 실제로 움직인 각도를 따로 누적해 좌우·위아래를 함께 담는다.
    private Vector3 prevForward;
    private float prevPitch;             // 고도각(도)
    private float accumulatedPath;       // Σ 시선이 움직인 각도 (좌우+위아래)
    private float accumulatedPitch;      // Σ|Δ고도각|
    private float shortestPathDeg;       // 시행 시작 시점의 타겟까지 각도 (3D 최단)
    private int errorClicks;

    private string csvPath;
    private StringBuilder line = new StringBuilder();

    // ───────────────────────── 수명주기 ─────────────────────────

    private void Start()
    {
        if (headCamera == null && Camera.main != null) headCamera = Camera.main.transform;

        Screen.sleepTimeout = SleepTimeout.NeverSleep;
        Screen.brightness = 1.0f;
        if (!Api.HasDeviceParams()) Api.ScanDeviceParams();

        HapticManager.Init();

        settingsMenu = gameObject.AddComponent<PatternSettingsMenu>();
        settingsMenu.Initialize(this);

        if (testMode) { repetitionsPerCell = 1; practiceTrials = 1; }
        if (showHud && headCamera != null) DebugHud.Create(headCamera);

        BuildPool();
        blockIndex = 0;

        // 참가자 번호를 앱 안에서 고른 뒤라야 블록 순서와 CSV 파일명이 정해진다.
        StartCoroutine(Startup());
    }

    /// <summary>
    /// 켜자마자 거울 방으로 간다 (goStraightToMirror).
    ///
    /// **시작 화면을 셋 다 꺼 두었다** — 진동 자가진단, 진동 패턴 확인,
    /// 참가자 번호 선택. 폰에 올려 한 번 보려 할 때마다 셋을 거쳐야 했다.
    ///
    /// 그런데 셋을 끄자 **거울 방까지 같이 사라졌다.** 거울 방으로 가는
    /// 길이 패턴 설정 화면 안에만 있었던 탓이다. 11/14 에 폰에 올려 보고
    /// 알았다 — 빨간 네모 탐색 실험이 떴다. 그래서 위에 직통 문을 냈다.
    ///
    /// 남길 교훈: **곁가지 화면을 끄는 일이 본 화면을 끄는 일이 되면
    /// 안 된다.** 보고 싶은 것에는 자기 문이 있어야 한다.
    ///
    /// **본 실험 전에 셋 중 둘은 도로 켜야 한다.**
    ///
    /// - `askParticipantId` — 꺼 두면 participantId 가 0 에 머문다.
    ///   라틴방격이 참가자마다 같은 순서가 되고(LatinSquare[0]),
    ///   CSV 가 전부 `results_P00_…` 으로 쌓인다. 반드시 켠다.
    /// - `runHapticSelfTest` — 폰의 시스템 햅틱이 꺼져 있어도 이제
    ///   아무 경고가 없다. 아무것도 안 느껴질 때 이것부터 켜서
    ///   폰 설정 탓인지 가려낸다.
    ///
    /// `runPatternTest` 하나만 원래부터 본 실험에서 꺼야 하는 것이다.
    /// </summary>
    private IEnumerator Startup()
    {
        // ── 거울 방으로 **곧장** ──
        //
        // 여기 통로가 없어서 한 번 크게 헛돌았다. 거울 방으로 가는 길이
        // 패턴 설정 화면 안에만 있었다 (PatternSettings → RunMirrorScene).
        // 시작 화면들이 번거롭다고 runPatternTest 를 끄자 **그 문까지 같이
        // 닫혀서**, 폰에 올린 앱이 곧장 StartBlock() 으로 — 옛 빨간 네모
        // 탐색 실험으로 가 버렸다. 거울 방을 몇 주 손봐 놓고 정작 폰에서는
        // 한 번도 안 보이는 상태였다.
        //
        // 부수적인 화면을 끄는 일이 본 화면을 끄는 일이 되면 안 된다.
        // 그래서 거울 방에는 **자기 문**을 준다.
        if (goStraightToMirror)
        {
            yield return StartCoroutine(StartXR());
            if (!xrOk) yield break;      // ShowError 가 화면을 띄워 뒀다

            // 마지막 블록 다음에 트리거를 누르면 처음부터 다시 돈다.
            // MirrorScene 은 끝에서 rig.Dispose() 로 치우므로 다시 들어가도
            // 쌓이지 않는다. 눈으로 보는 단계에서는 빈 화면에 남는 것보다
            // 처음으로 돌아가는 편이 쓸모 있다.
            while (true) yield return StartCoroutine(MirrorScene());
        }

        // 튜닝 모드에서는 자가진단을 먼저 돌린다. 아무것도 안 느껴질 때
        // 프리셋 탓인지 폰의 시스템 햅틱 설정 탓인지 헷갈리지 않게 하려고.
        if (runHapticTuning)
        {
            if (runHapticSelfTest) yield return StartCoroutine(HapticSelfTest());
            yield return StartCoroutine(HapticTuning());
        }

        // 패턴 확인 모드도 자가진단을 알에 둔다 — 아무것도 안 나라면
        // 패턴 탓인지 시스템 햅틱 설정 탓인지 가려지 지 않아서다.
        if (runPatternTest)
        {
            if (runHapticSelfTest && !runHapticTuning)
                yield return StartCoroutine(HapticSelfTest());
            yield return StartCoroutine(PatternSettings());
        }

        if (askParticipantId) yield return StartCoroutine(SelectParticipant());

        BuildBlockOrder();
        OpenCsv();

        if (!runHapticTuning && !runPatternTest && runHapticSelfTest)
            yield return StartCoroutine(HapticSelfTest());

        StartBlock();
    }

    /// <summary>
    /// 참가자 번호를 앱 안에서 고른다.
    ///
    /// 라틴방격은 참가자마다 다른 블록 순서를 요구하는데, 번호가 Inspector 값이면
    /// 참가자 6명에 6번 다시 빌드·사이드로드해야 한다. 입력 수단이 트리거 하나뿐이라
    /// 탭으로 번호를 올리고 잠시 두면 확정되는 방식으로 했다.
    /// </summary>
    private IEnumerator SelectParticipant()
    {
        phase = Phase.Ready;
        HideAll();

        int id = Mathf.Abs(participantId) % LatinSquare.Length;
        float idle = 0f;
        const float confirmAfter = 3f;

        while (idle < confirmAfter)
        {
            if (triggerPressedThisFrame)
            {
                id = (id + 1) % LatinSquare.Length;
                idle = 0f;
            }
            else
            {
                idle += Time.deltaTime;
            }

            DebugHud.Set("PARTICIPANT\n\n"
                       + "      P" + id + "\n\n"
                       + "tap to change\n"
                       + "hold still " + Mathf.CeilToInt(confirmAfter - idle) + "s to confirm\n\n"
                       + "order: " + BlockOrderText(id));
            yield return null;
        }

        participantId = id;
        DebugHud.Set("PARTICIPANT P" + id + " confirmed\n\norder: " + BlockOrderText(id));
        yield return new WaitForSeconds(1.2f);
    }

    // ───────────────────────── 햅틱 튜닝 ─────────────────────────

    /// <summary>
    /// 가이드 2-3절이 제안하는 변형들을 프리셋으로 담아 둔 것.
    ///
    /// 아이폰은 빌드 한 번에 30~40분이 걸려(안드로이드는 Build And Run 2분) 값을 하나씩
    /// 바꿔 가며 느껴 볼 수가 없다. 그래서 후보를 미리 넣어 두고 앱 안에서 비교한다.
    /// </summary>
    private struct HapticPreset
    {
        public string label;
        public float minMs, maxMs;
        public long pulseMs;
        public float near, far;
        public bool square;
        public int steps;
    }

    private static readonly HapticPreset[] Presets =
    {
        // 가이드 기본값. 비교 기준이므로 반드시 첫 번째에 둔다.
        new HapticPreset { label = "guide default",   minMs =  80f, maxMs =  900f, pulseMs = 45, near = 255f, far = 30f, square = false, steps = 0 },
        // 간격을 좁혀 더 촘촘하게
        new HapticPreset { label = "narrow interval", minMs =  60f, maxMs =  700f, pulseMs = 30, near = 255f, far = 30f, square = false, steps = 0 },
        // 간격을 넓혀, 가까운 쪽에서도 펄스가 개별로 구분되게
        new HapticPreset { label = "wide interval",   minMs = 120f, maxMs = 1200f, pulseMs = 55, near = 255f, far = 30f, square = false, steps = 0 },
        // 가이드 2-3(A) 의 제곱 곡선
        new HapticPreset { label = "square curve",    minMs =  80f, maxMs =  900f, pulseMs = 45, near = 255f, far = 30f, square = true,  steps = 0 },
        // Case 3 의 먼 쪽 세기를 0.118 -> 0.25 로 올린 것. 상자 안에서 안 느껴질 때의 대안
        new HapticPreset { label = "strong far",      minMs =  80f, maxMs =  900f, pulseMs = 45, near = 255f, far = 64f, square = false, steps = 0 },
        // 가이드 2-3(B) 의 3단계 양자화
        new HapticPreset { label = "3 steps",         minMs =  80f, maxMs =  900f, pulseMs = 45, near = 255f, far = 50f, square = false, steps = 3 }
    };

    /// <summary>
    /// 프리셋을 넘겨 가며 진동을 직접 느껴 본다.
    ///
    /// 타겟을 찾게 하는 대신 각도를 180도 → 0도 → 180도로 쓸어 준다. 어디를 보고 있든
    /// 같은 범위를 느끼므로 프리셋끼리 비교가 된다. Case 2 구간과 Case 3 구간을 번갈아 보여 주므로
    /// 한 프리셋에서 두 조건을 다 판단할 수 있다.
    ///
    /// 가장 중요한 확인 항목은 Case 3 의 먼 쪽 세기다. 그게 안 느껴지면 큰 각도에서
    /// Case 3 이 아무 안내를 못 하게 되고, 안내가 가장 필요한 구간이 바로 거기다.
    /// </summary>
    private IEnumerator HapticTuning()
    {
        phase = Phase.Ready;
        HideAll();
        DebugHud.Show(true);

        ExperimentCase savedCase = currentCase;

        int idx = 0;
        float idle = 0f;
        bool prevCase2 = true;
        const float confirmAfter = 3f;
        const float sweepSeconds = 3f;      // 한 방향을 쓸는 데 걸리는 시간

        while (idle < confirmAfter)
        {
            if (triggerPressedThisFrame)
            {
                idx = (idx + 1) % Presets.Length;
                idle = 0f;
                HapticManager.StopContinuous();
                HapticManager.Cancel();
                hapticTimer = 0f;
            }
            else
            {
                idle += Time.deltaTime;
            }

            ApplyPreset(Presets[idx]);

            // 네 구간을 돈다: Case2 로 한 번 왕복, Case3 으로 한 번 왕복.
            float u = (Time.time / sweepSeconds) % 4f;
            bool demoCase2 = u < 2f;
            float leg = u % 2f;                                   // 0 ~ 2
            float angle = (leg < 1f ? 1f - leg : leg - 1f) * 180f; // 180 → 0 → 180

            if (demoCase2 != prevCase2)
            {
                HapticManager.StopContinuous();
                hapticTimer = 0f;
                prevCase2 = demoCase2;
            }

            currentCase = demoCase2 ? ExperimentCase.Case2_Visual_PulseInterval
                                    : ExperimentCase.Case3_Visual_Intensity;
            UpdateHaptics(angle, true);

            HapticPreset p = Presets[idx];
            DebugHud.Set("HAPTIC TUNING   [" + (idx + 1) + "/" + Presets.Length + "]\n\n"
                       + "  " + p.label + "\n\n"
                       + "Case2  interval " + (int)p.minMs + " - " + (int)p.maxMs + " ms\n"
                       + "       pulse    " + p.pulseMs + " ms"
                       + (p.square ? "   (square)" : "") + "\n"
                       + "Case3  strength " + (int)p.near + " - " + (int)p.far
                       + (p.steps >= 2 ? "   (" + p.steps + " steps)" : "") + "\n\n"
                       + "now  " + (demoCase2 ? "CASE 2 pulse  " : "CASE 3 strength")
                       + "   angle " + Mathf.RoundToInt(angle) + "\n\n"
                       + "tap to change preset\n"
                       + "hold still " + Mathf.CeilToInt(confirmAfter - idle) + "s to go on");
            yield return null;
        }

        HapticManager.StopContinuous();
        HapticManager.Cancel();
        hapticTimer = 0f;
        currentCase = savedCase;
        ApplyPreset(Presets[idx]);

        DebugHud.Set("HAPTIC SET\n\n  " + Presets[idx].label + "\n\n" + HapticProfileText());
        Debug.Log("[Experiment] haptic preset = " + Presets[idx].label + " / " + HapticProfileText());
        yield return new WaitForSeconds(1.8f);
    }

    /// <summary>
    /// 진동 패턴 10가지를 하나씩 재생해, 아이폰에서 모양이 구별되는지 확인한다.
    ///
    /// 조원이 안드로이드에 넣은 패턴은 VibrationEffect.createWaveform 으로
    /// 구간별 세기를 지정하는 방식인데, CoreHaptics 에는 대응 API 가 없다.
    /// HapticManager.PlayPattern 이 같은 timings/amplitudes 를 받아 구간마다
    /// continuous 이벤트를 놓는 식으로 옮기는데, 그게 실제로 구별되게 들리는지는
    /// 폰에서 만져 봐야만 알 수 있다. 빌드 한 번으로 열 개를 다 보기 위한 화면이다.
    ///
    /// 탭하면 다음 패턴. 열 개를 다 지나간 뒤 5초 가만히 두면 넘어간다.
    /// </summary>
    /// <summary>
    /// 진동 값을 폰에서 고치는 화면을 띄운다.
    ///
    /// 화면이 떠 있는 동안에는 Cardboard XR 을 끈다. XR 모드에서는 화면이 둘로 갈라져
    /// 글씨를 읽기 어렵고 터치·키보드가 제대로 먹지 않는다.
    ///
    /// XR 을 다시 켜는 데 실패하면 본 실험으로 넘어가지 않고 화면에 남는다.
    /// Api 트리거는 XR 이 꺼져 있으면 항상 false 라(XRLoader._isStarted 검사),
    /// 그냥 넘어가면 탭이 하나도 안 먹어 아무것도 못 하게 된다.
    /// 이 화면은 IMGUI 버튼만 쓰므로 그 상태에서도 다시 시도할 수 있다.
    /// </summary>
    private IEnumerator PatternSettings()
    {
        DebugHud.Show(false);
        yield return StartCoroutine(StopXR());

        settingsMenu.Begin();
        while (true)
        {
            while (!settingsMenu.Done) yield return null;
            settingsMenu.Visible = false;

            yield return StartCoroutine(StartXR());
            if (!xrOk) continue;          // ShowError 가 화면을 다시 띄워 뒀다

            if (settingsMenu.RunMirrorScene)
            {
                yield return StartCoroutine(MirrorScene());
                yield return StartCoroutine(StopXR());
                settingsMenu.Begin();
                continue;
            }

            if (!settingsMenu.RunPatternTest) break;

            yield return StartCoroutine(PatternTest());

            // 비교가 끝나면 값을 다시 만질 수 있게 화면으로 돌아온다
            yield return StartCoroutine(StopXR());
            settingsMenu.Begin();
        }

        settingsMenu.Visible = false;
        DebugHud.Show(true);
    }

    // ───────────────────── 거울 씬 (새 주제) ─────────────────────

    /// <summary>
    /// 칫솔 진동의 바탕 세기 (드릴 대비). **전동칫솔이 아니다.**
    ///
    /// 11/15 까지 드릴과 같은 세기로 울려서 "진동칫솔" 로 느껴졌다.
    /// 솔이 이를 쓸고 지나가는 "샤샥" 은 세기가 아니라 **박자**가
    /// 만든다 — 세기를 낮추고 보이는 솔질에 맞춰 오르내리게 한다.
    /// </summary>
    private const float BrushAmp = 0.18f;

    private bool scenarioBuzzing;
    private int lastChewBeat;        // 지난 프레임의 MirrorRig.ChewBeat
    private int crunchSeed;
    private float prevMouth;      // 지난 프레임의 입 벌림 — 닫히는 속도를 잰다
    private float chewLevel;      // 씹는 소리의 세기. 빨리 오르고 천천히 꺼진다

    /// <summary>
    /// 거울 방을 띄우고 세 시나리오를 차례로 보여 준다.
    /// 트리거를 누르면 다음으로 넘어가고, 마지막 다음에 누르면 설정 화면으로 돌아온다.
    ///
    /// 아직 파일럿용이다 — 실험자가 눈으로 확인하고 진동 값을 맞추는 단계다.
    /// 참가자가 직접 값을 올리내리는 화면은 블루투스 컨트롤러가 와야 붙는다.
    /// </summary>
    private IEnumerator MirrorScene()
    {
        phase = Phase.Ready;
        HideAll();
        DebugHud.Show(showHud);
        DebugHud.Set("BUILD " + BuildStamp + "  starting XR...");

        // 방을 **참가자가 보고 있는 쪽으로** 짓는다. 그러려면 카메라
        // 자세가 들어온 뒤라야 한다. 자세가 들어오기 전에는 카메라가
        // 씬에 적힌 그대로(회전 없음)라, 그 상태로 지으면 방이 엉뚱한
        // 쪽을 향한다 — 들어가자마자 옆벽을 보게 된다.
        yield return StartCoroutine(WaitForHeadPose());

        MirrorRig rig = MirrorRig.Create(headCamera);
        if (rig == null)
        {
            settingsMenu.ShowError("Mirror rig could not be created.");
            yield break;
        }

        // 소리는 진동과 **같은 자리에서** 켜고 끈다(ApplyContactHaptics).
        // 따로 돌리면 드릴 소리가 나는데 진동은 안 오는 순간이 생기고,
        // 참가자는 그 어긋남을 "진동이 가짜 같다" 로 적는다.
        SafeCall("ScenarioAudio.Attach", delegate { ScenarioAudio.Attach(headCamera); });

        // 바리깡을 빼고 치과와 음식 두 상황으로 간다. 음식은 11/11 에 두
        // 수준에서 다섯 가지로 늘렸으므로(MirrorRig.FoodKind) 블록은 일곱이다.
        //
        // 순서는 섞지 않고 **단단한 쪽에서 무른 쪽으로** 내려간다. 파일럿에서
        // 실험자가 값을 맞추는 단계라, 느낌이 한 방향으로 움직이는 게 눈에
        // 보여야 "이 조건이 과했다" 를 바로 안다. 참가자를 받을 때는 순서
        // 효과를 지워야 하니 돌려 가며 내야 한다 — 아직 안 했다.
        MirrorRig.Scenario[] order =
        {
            MirrorRig.Scenario.Food,
            MirrorRig.Scenario.Food,
            MirrorRig.Scenario.Food,
            MirrorRig.Scenario.Food,
            MirrorRig.Scenario.Food,
            MirrorRig.Scenario.Dental,
            MirrorRig.Scenario.Dental
        };
        MirrorRig.FoodKind[] foods =
        {
            MirrorRig.FoodKind.Candy,    // 유리처럼 쩍
            MirrorRig.FoodKind.Apple,    // 아삭
            MirrorRig.FoodKind.Meat,     // 질겨서 길게
            MirrorRig.FoodKind.Jelly,    // 쫀득
            MirrorRig.FoodKind.Pudding,  // 거의 안 씹힌다
            MirrorRig.FoodKind.Apple,    // 치과에서는 쓰이지 않는다
            MirrorRig.FoodKind.Apple
        };
        MirrorRig.DentalKind[] tools =
        {
            MirrorRig.DentalKind.Drill,  // 음식에서는 쓰이지 않는다
            MirrorRig.DentalKind.Drill,
            MirrorRig.DentalKind.Drill,
            MirrorRig.DentalKind.Drill,
            MirrorRig.DentalKind.Drill,
            MirrorRig.DentalKind.Drill,
            MirrorRig.DentalKind.Brush
        };

        int idx = 0;
        SafeCall("EnterScenario", delegate { EnterScenario(rig, order[0], foods[0], tools[0]); });

        // 진동 설정창. **리그 뿌리**에 단다 — 머리에 달면 눈을 따라다녀서
        // 쳐다볼 수가 없다 (HapticPanel 주석 (나)).
        SafeCall("HapticPanel", delegate {
            HapticPanel.Create(rig.transform, headCamera); });

        while (true)
        {
            // 탭은 하나뿐이라 **겨눈 쪽이 가져간다.** 설정창을 보고
            // 있으면 설정창이 먹고, 아니면 원래대로 다음 블록으로 넘어간다.
            bool onPanel = false;
            SafeCall("HapticPanel.Step", delegate {
                onPanel = HapticPanel.Step(
                    triggerPressedThisFrame,
                    // 주기 — 조각 사이 침묵이라 음식에만 있다
                    order[idx] == MirrorRig.Scenario.Food,
                    // 부하 깊이 — 지금은 **드릴에만** 있다. 칫솔은
                    // 조원 몫이라 안 건드린다 (ApplyContactHaptics 참고).
                    order[idx] == MirrorRig.Scenario.Dental
                        && tools[idx] == MirrorRig.DentalKind.Drill); });

            if (triggerPressedThisFrame && !onPanel)
            {
                idx++;
                if (idx >= order.Length) break;
                int i = idx;
                SafeCall("EnterScenario", delegate { EnterScenario(rig, order[i], foods[i], tools[i]); });
            }

            SafeCall("ApplyContactHaptics", delegate { ApplyContactHaptics(rig); });

            // 폰이 스스로 상태를 말하게 한다. 에디터에서 폰 화면을
            // 재현하려다 두 번 빗나간 뒤에 둔 것이다 (MirrorRig.NearInfo).
            if (showHud)
            {
                DebugHud.Set("BUILD " + BuildStamp + "  " + HapticPanel.Info()
                    + "  blk " + idx + "/" + (order.Length - 1)
                    + " " + order[idx]
                    + (order[idx] == MirrorRig.Scenario.Dental
                        ? " " + tools[idx] : " " + foods[idx])
                    + "\n" + rig.NearInfo());
            }

            yield return null;
        }

        HapticManager.StopContinuous();
        HapticManager.StopWaveform();
        ScenarioAudio.Detach();
        HapticPanel.Dispose();
        scenarioBuzzing = false;
        rig.Dispose();
    }

    private void EnterScenario(MirrorRig rig, MirrorRig.Scenario s,
                               MirrorRig.FoodKind food, MirrorRig.DentalKind tool)
    {
        // SetContinuous 는 첫 호출에만 sharpness 를 넘긴다. 시나리오를 바꿀 때
        // 멈춰 두지 않으면 앞 시나리오의 날카로움이 그대로 남는다.
        HapticManager.StopContinuous();
        HapticManager.StopWaveform();
        ScenarioAudio.SetLoop(ScenarioAudio.Loop.None, 0f);
        scenarioBuzzing = false;
        prevMouth = 0f;
        chewLevel = 0f;

        // 파일럿에서 맞출 값의 출발점. 치과 드릴은 날카롭고, 전동칫솔은 같은 전동
        // 공구여도 훨씬 둔하다 — 이 차이가 두 수준을 가르는 지점이라 값도 갈라 둔다.
        if (s == MirrorRig.Scenario.Dental)
            HapticManager.Sharpness = tool == MirrorRig.DentalKind.Brush ? 0.45f : 0.95f;
        else
            HapticManager.Sharpness = 0.35f;

        rig.ShowScenario(s, food, tool);
    }

    /// <summary>
    /// 진동을 접촉에 물린다. 거리가 아니라 닿았는지로 움직이는 게 이 설계의 핵심이다 —
    /// 모터가 얼굴에 하나뿐이라 "멀면 약하게" 는 거리로 안 느껴지고 그냥 세기가
    /// 오르내리는 걸로만 느껴진다. 닿기 전에는 아무것도 내보내지 않는다.
    /// </summary>
    private void ApplyContactHaptics(MirrorRig rig)
    {
        bool touching = rig.Contact > 0.5f;

        if (rig.Current == MirrorRig.Scenario.Food)
        {
            // 씹기는 지속 진동이 아니라 짧은 탁이 여러 번이다.
            //
            // **닿는 순간 한 번**만 쏘던 것을 고쳤다. 4~11 ms 짜리 탁
            // 몇 개는 한 번 스치면 끝이라, 폰에서는 "먹을 때 진동이
            // 없다" 로 느껴졌다. 이제 깨무는 순간에 한 번 크게 울리고,
            // 그 뒤로 **이가 맞물릴 때마다** (MirrorRig.ChewBeat) 다시
            // 울린다. 보이는 턱과 박자가 같다.
            bool firstBite = touching && !scenarioBuzzing;
            bool beat = touching && rig.ChewBeat != lastChewBeat;
            lastChewBeat = rig.ChewBeat;

            if (firstBite || beat)
            {
                crunchSeed++;

                // 조각 수와 세기는 **리그가 쥐고 있다**(MirrorRig.FoodFragments).
                // 여기서 음식 종류를 보고 정하면 보이는 씹기와 느껴지는 씹기가
                // 두 파일에서 따로 굴러간다 — 다섯 가지가 되면서 값을 음식
                // 표 한 군데로 모았다. BuildCrunch 주석의 기준(과자 4~6,
                // 사과 2~3, 얼음 1~2)은 그 표에 그대로 들어가 있다.
                // 처음 깨물 때가 가장 세고, 오물거릴수록 잦아든다.
                // 음식이 줄어드는 것을 세기로 낸다 (MirrorRig.FoodLeft) —
                // 입에 가득할 때와 거의 다 먹었을 때가 같게 느껴지면
                // 먹는 게 아니라 모터가 도는 것이다.
                float strength = firstBite ? 1f : 0.28f + 0.72f * rig.FoodLeft;

                // 설정창의 **세기** 손잡이를 여기서 곱한다
                // (HapticPanel.AmpScale). 진동이 나가는 자리는 음식과
                // 치과 둘뿐이라 두 군데만 보면 된다.
                int peak = Mathf.Clamp(Mathf.RoundToInt(
                    waveHighAmplitude * rig.FoodPeakScale * strength
                    * HapticPanel.AmpScale), 1, 255);

                // 조각 수도 같이 준다. 다 부서진 뒤에 처음처럼 여러
                // 조각이 깨지면 입 안에 없는 것이 깨지는 셈이다.
                int frags = firstBite
                          ? rig.FoodFragments
                          : Mathf.Max(1, Mathf.RoundToInt(
                                rig.FoodFragments * (0.35f + 0.65f * rig.FoodLeft)));

                ScenarioHaptics.PlayCrunch(frags, peak, crunchSeed);
                // **같은 세 값**을 소리 쪽에도 넘긴다. 그래야 조각이 터지는
                // 시각과 세기가 글자 그대로 같아진다 (ScenarioAudio 주석).
                ScenarioAudio.PlayCrunch(frags, peak, crunchSeed,
                                         rig.FoodBrightness);
            }
            scenarioBuzzing = touching;

            // 씹는 소리. 보이는 턱이 닫히는 속도가 세기가 된다 —
            // 식은 ScenarioAudio.ChewLevel 한 군데에만 있다.
            //
            // 입에 닿아 있지 않은 동안(접시에 놓인 채, 손으로 가져가는
            // 중)은 0 을 먹여 꺼지게 한다. 클립을 Stop 하지는 않는다 —
            // 한 주기에 네 번씩 켜고 끄면 그때마다 처음으로 되감겨
            // 이음매에서 틱 소리가 난다.
            float mouth = touching ? rig.MouthOpen : 0f;
            chewLevel = touching
                      ? ScenarioAudio.ChewLevel(chewLevel, mouth, prevMouth,
                                                Time.deltaTime)
                      : 0f;
            prevMouth = mouth;
            ScenarioAudio.SetLoop(ScenarioAudio.Loop.Chew, chewLevel,
                                  rig.FoodBrightness);
            return;
        }

        // 치과는 지속 진동. 닿아 있는 동안만 켠다.
        bool isBrush = rig.CurrentDental == MirrorRig.DentalKind.Brush;
        if (touching)
        {
            // 둘 다 한 세기로 가지 않는다.
            //
            // 드릴은 11/15 까지 amp = 1 로 **내내 똑같이** 울렸다.
            // 근거는 "실제로 그렇다" 였는데 틀렸다. 받아온 녹음을 재
            // 보니 공회전과 부하가 4.25 배 차이로 오간다
            // (ScenarioAudio.DrillFrom 주석). 날이 법랑질을 파고들면
            // 소리가 커지고 손도 같이 세진다 — 치과에서 사람이
            // 기억하는 것이 바로 그 오르내림이다.
            //
            // 칫솔은 솔이 좌우로 쓸리는 박자(AnimateDental 의 2.2)에
            // 맞춰 세기가 오르내리고, 그 위에 솔이 이를 넘는 잔 떨림
            // (같은 곳의 13)을 얹는다. 보이는 움직임과 **같은 두 수**를
            // 쓰므로 눈과 손이 어긋날 수가 없다.
            float amp = 1f;
            if (isBrush)
            {
                // **양치할 때 손에 오는 진동이다.** 전동칫솔이 아니다.
                //
                // 전동칫솔은 한 가지 세기로 계속 운다. 손으로 닦을 때
                // 손에 오는 건 솔이 이를 긁을 때마다 올라왔다 잦아드는
                // **마찰**이다. 그래서 세기를 왕복에 묶는다 — 쓱 할 때
                // 올라오고 방향을 바꿀 때 가라앉는다.
                //
                // 박자는 소리와 **같은 수**를 쓴다(ScenarioAudio.StrokeHz).
                // 귀로는 쓱쓱 하는데 손은 딴 박자로 떨면 그 어긋남이
                // 바로 "진동이 가짜 같다" 로 적힌다.
                float t2 = rig.Seconds;
                float stroke = Mathf.Abs(
                    Mathf.Sin(Mathf.PI * ScenarioAudio.StrokeHz * t2));
                // 솔이 법랑질 위에서 튀는 잔결. 세기를 바꾸는 게 아니라
                // 매끄러운 산을 **까끌하게** 만드는 것이라 폭이 좁다.
                float grain = 0.88f + 0.12f * Mathf.Sin(t2 * 41f);

                // **여기는 손대지 않는다.** 칫솔 시나리오는 조원이
                // 맡았다. 한 번 설정창의 부하 깊이를 여기까지 먹였다가
                // 뺐다 — 기본값에서는 식이 같았지만, 손잡이를 돌리면
                // 남의 자극이 바뀐다. 칫솔에서 무엇을 조절하게 할지는
                // 그쪽에서 정할 몫이다. 그때까지 부하 줄은 칫솔 블록
                // 에서 흐리다 (HapticPanel.Step 의 loadUseful).
                amp = BrushAmp * (0.22f + 0.78f * stroke) * grain;
            }
            else
            {
                // **지금 귀에 들리는 그 크기**를 그대로 쓴다
                // (ScenarioAudio.DrillLoad). 소리는 클립에서 나오고
                // 이 수는 바로 그 클립을 재서 만든 포락선에서 나오므로,
                // 들리는 것과 느껴지는 것이 어긋날 자리가 없다 —
                // 씹기에서 timings 배열 하나를 양쪽이 읽는 것과 같다.
                //
                // 얼마나 따라갈지는 설정창의 **부하 깊이**가 쥔다
                // (HapticPanel.LoadDepth). 0 이면 11/15 까지의 평평한
                // 진동이고 1 이면 녹음 그대로다. 1 을 넘기면 녹음보다
                // 더 꿀렁이므로 1 아래로 내려갈 때는 음수가 될 수 있다 —
                // 0.08 로 받친다. 모터가 접촉 중에 아예 멎으면 고장으로
                // 느껴진다.
                amp = Mathf.Max(0.08f,
                    Mathf.LerpUnclamped(1f, ScenarioAudio.DrillLoad,
                                        HapticPanel.LoadDepth));
            }
            HapticManager.SetContinuous(Mathf.Clamp(
                Mathf.RoundToInt(waveHighAmplitude * amp
                                 * HapticPanel.AmpScale), 1, 255));
            scenarioBuzzing = true;
        }
        else if (scenarioBuzzing)
        {
            HapticManager.StopContinuous();
            scenarioBuzzing = false;
        }

        // 소리도 닿아 있는 동안만 **들린다.** 다만 클립을 **멈추지는
        // 않는다** — 세기만 0 으로 내린다. 씹는 소리가 이미 같은
        // 수법을 쓰고 있다 (위쪽 Loop.Chew 주석).
        //
        // 멈추면 다음 접촉에서 Play() 가 처음으로 되감는다. 그러면
        // 접촉마다 **글자 그대로 같은 2.1 초**가 반복된다 —
        // AnimateDental 의 주기가 3.2 초이고 그중 2.1 초가 접촉이라,
        // 한 블록에 아홉 번쯤 된다. 아홉 번 똑같으면 귀가 그걸
        // 녹음으로 알아챈다. 돌려 둔 채로 두면 접촉마다 녹음의 다른
        // 자리를 긁어 와서 매번 조금씩 다르다. 실제 터빈도 의사가
        // 자리를 옮기는 동안 계속 돌고 있다.
        //
        // 값을 치르는 쪽도 있다. 참가자마다 어느 자리에서 시작하는지가
        // 달라서 자극이 조건마다 글자 그대로 같지 않다. 다만 그 어긋남은
        // **한쪽으로 쏠리지 않으므로** 잡음만 늘고 편향은 안 생긴다.
        // 반면 아홉 번 똑같은 소리는 "녹음이구나" 를 한 방향으로 심는다.
        // 되감는 쪽으로 돌리려면 아래에서 !touching 일 때 Loop.None 을
        // 주면 된다.
        bool brush = isBrush;
        float level = 0f;
        if (touching)
        {
            // 칫솔은 솔이 좌우로 쓸리는 박자에 맞춰 커졌다 작아진다.
            // 2.2 Hz 는 보이는 움직임과 같은 값이다(MirrorRig.AnimateDental).
            // 일정한 세기로 두면 모터만 돌고 솔은 안 움직이는 소리가 된다.
            level = brush
                ? 0.55f + 0.20f * Mathf.Sin(rig.Seconds * 2.2f)
                : 0.80f;
        }
        ScenarioAudio.SetLoop(
            brush ? ScenarioAudio.Loop.Brush : ScenarioAudio.Loop.Drill,
            level);
    }

    private IEnumerator StopXR()
    {
        XRManagerSettings manager = XRGeneralSettings.Instance != null
            ? XRGeneralSettings.Instance.Manager : null;

        if (manager != null && manager.isInitializationComplete)
        {
            manager.StopSubsystems();
            manager.DeinitializeLoader();
        }

        // XR 이 꺼져도 카메라는 스테레오 설정을 들고 있어 화면이 찌그러진다
        Camera cam = headCamera != null ? headCamera.GetComponent<Camera>() : Camera.main;
        if (cam != null)
        {
            cam.ResetAspect();
            cam.fieldOfView = 60f;
        }

        yield return null;
    }

    /// <summary>
    /// Cardboard 가 카메라에 자세를 넣을 때까지 기다린다 (최대 1 초).
    ///
    /// StartXR 은 서브시스템을 켜고 **그 프레임에** 돌아온다. 카메라에
    /// 머리 자세가 실리는 건 그다음 프레임부터다. 전에는 이 사이에
    /// 패턴 설정 화면이 있어서 저절로 몇 초가 지났는데, 그 화면을 끄면서
    /// 유예 시간도 같이 사라졌다.
    ///
    /// 자리(위치)가 어긋나는 쪽은 MirrorRig.FollowViewer 가 원점을 카메라에
    /// 붙여 두는 것으로 아예 막았다. 여기서 기다리는 건 **방향** 때문이다 —
    /// 방의 방위는 지을 때 한 번만 정해지므로 그 한 번을 제대로 재야 한다.
    ///
    /// 자이로는 가만히 들고 있어도 미세하게 흔들린다. 1 도만 움직이면
    /// 자세가 들어온 것으로 본다. 끝내 안 움직이면 1 초 뒤 그냥 간다 —
    /// 기다리다 멈춰 있는 것보다 방위가 조금 틀린 편이 낫다.
    /// </summary>
    /// <summary>
    /// 터져도 코루틴을 죽이지 않고, **어디서 터졌는지 화면에 남긴다.**
    ///
    /// 코루틴 안에서 예외가 나면 Unity 는 그 코루틴을 조용히 멈춘다.
    /// 11/14 에 거울 방이 그렇게 멈췄다 — 방은 세워졌는데 매 프레임 도는
    /// 몸통이 첫 바퀴에 죽어서, HUD 가 들어가기 직전 글자("starting XR...")에
    /// 멈춘 채로 남았다. iOS 라 로그를 볼 맥도 없어서 사흘치 추측을 했다.
    ///
    /// 그래서 네이티브(오디오·햅틱)를 건드리는 호출은 전부 여기를 거친다.
    /// Debug.LogError 가 DebugHud 의 가로채기로 들어가므로 예외 종류와
    /// 메시지가 폰 화면에 그대로 뜬다.
    ///
    /// 삼키기만 하면 안 된다 — 진동이 조용히 안 오는 실험이 되면 그게 더
    /// 나쁘다. 그래서 **반드시 화면에 남긴다.**
    /// </summary>
    private void SafeCall(string where, System.Action body)
    {
        try { body(); }
        catch (System.Exception e)
        {
            Debug.LogError("[" + where + "] " + e.GetType().Name + ": " + e.Message);
        }
    }

    private IEnumerator WaitForHeadPose()
    {
        if (headCamera == null) yield break;

        Quaternion start = headCamera.rotation;
        for (float t = 0f; t < 1.0f; t += Time.deltaTime)
        {
            if (Quaternion.Angle(start, headCamera.rotation) > 1f) yield break;
            yield return null;
        }
    }

    private IEnumerator StartXR()
    {
        xrOk = false;

        XRManagerSettings manager = XRGeneralSettings.Instance != null
            ? XRGeneralSettings.Instance.Manager : null;
        if (manager == null)
        {
            settingsMenu.ShowError("XR manager is missing.");
            yield break;
        }

        if (!manager.isInitializationComplete) yield return manager.InitializeLoader();

        if (manager.activeLoader == null)
        {
            manager.DeinitializeLoader();
            settingsMenu.ShowError("Cardboard XR did not start. Tap a Start button again.");
            yield break;
        }

        manager.StartSubsystems();
        if (!Api.HasDeviceParams()) Api.ScanDeviceParams();
        if (Api.HasNewDeviceParams()) Api.ReloadDeviceParams();
        xrOk = true;
    }

    private IEnumerator PatternTest()
    {
        phase = Phase.Ready;
        HideAll();
        DebugHud.Show(true);

        VibrationPattern[] list =
            (VibrationPattern[])System.Enum.GetValues(typeof(VibrationPattern));
        bool[] seen = new bool[list.Length];

        int idx = 0;
        float idle = 0f;
        float timer = 0f;
        const float exitAfter = 5f;

        while (true)
        {
            if (triggerPressedThisFrame)
            {
                idx = (idx + 1) % list.Length;
                idle = 0f;
                timer = 0f;
                HapticManager.StopWaveform();
            }
            else
            {
                idle += Time.deltaTime;
            }

            seen[idx] = true;

            timer += Time.deltaTime * 1000f;
            if (timer >= Mathf.Max(100f, patternRepeatMs))
            {
                HapticManager.PlayPattern(list[idx], pulseDurationMs, waveStepMs, waveGapMs,
                    waveHighAmplitude, waveLowAmplitude, 255,
                    envelopeDurationMs, envelopeSampleMs, envelopeModulationHz);
                timer = 0f;
            }

            int left = 0;
            for (int i = 0; i < seen.Length; i++) if (!seen[i]) left++;

            string shape, bars;
            PatternShape(list[idx], out shape, out bars);

            DebugHud.Set("PATTERN TEST   [" + (idx + 1) + "/" + list.Length + "]\n\n"
                       + "  " + list[idx] + "\n\n"
                       + shape + "\n"
                       + "|" + bars + "|\n\n"
                       + (left > 0
                            ? "tap to change pattern\n" + left + " left to try"
                            : "tap to change pattern\nhold still "
                              + Mathf.CeilToInt(exitAfter - idle) + "s to go on") + "\n\n"
                       + HapticManager.Diagnostics);

            if (left == 0 && idle >= exitAfter) break;
            yield return null;
        }

        HapticManager.StopWaveform();
        HapticManager.Cancel();
        hapticTimer = 0f;

        DebugHud.Set("PATTERN TEST done\n\n" + HapticManager.Diagnostics);
        Debug.Log("[Experiment] pattern test done / " + HapticManager.Diagnostics);
        yield return new WaitForSeconds(1.8f);
    }

    /// <summary>
    /// 패턴이 실제로 어떤 파형으로 나가는지 글자로 보여 준다.
    /// 아무것도 안 느껴질 때 "파형이 비었는지" 와 "폰이 안 울리는지" 를 갈라 준다.
    /// </summary>
    private void PatternShape(VibrationPattern pattern, out string summary, out string bars)
    {
        long[] timings;
        int[] amplitudes;
        long totalMs;

        if (pattern == VibrationPattern.SinglePulse)
        {
            timings = new long[] { System.Math.Max(1L, pulseDurationMs) };
            amplitudes = new int[] { Mathf.Clamp(waveHighAmplitude, 1, 255) };
            totalMs = timings[0];
        }
        else if (AndroidVibrationManager.IsMathPattern(pattern))
        {
            totalMs = AndroidVibrationManager.BuildMathWaveform(pattern, envelopeDurationMs,
                envelopeSampleMs, envelopeModulationHz, waveHighAmplitude,
                out timings, out amplitudes);
        }
        else
        {
            totalMs = AndroidVibrationManager.BuildWaveform(pattern, waveStepMs, waveGapMs,
                waveHighAmplitude, waveLowAmplitude, out timings, out amplitudes);
        }

        summary = timings.Length + " seg   " + totalMs + " ms   peak " + waveHighAmplitude;

        // 세기를 글자 높이로 그린다. 폰 화면이 좁으니 40칸에서 끊는다.
        const string ramp = " .:-=+*#";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < amplitudes.Length && i < 40; i++)
            sb.Append(ramp[Mathf.Clamp(amplitudes[i] * (ramp.Length - 1) / 255, 0, ramp.Length - 1)]);
        bars = sb.ToString();
    }

    private void ApplyPreset(HapticPreset p)
    {
        minIntervalMs   = p.minMs;
        maxIntervalMs   = p.maxMs;
        pulseDurationMs = p.pulseMs;
        intensityNear   = p.near;
        intensityFar    = p.far;
        squareCurve     = p.square;
        intensitySteps  = p.steps;
    }

    /// <summary>
    /// CSV 한 칸에 들어가는 햅틱 설정 요약. 어떤 값으로 받은 데이터인지 행마다 남기려고.
    /// 쉼표를 쓰지 않는다.
    /// </summary>
    private string HapticProfileText()
    {
        var ci = CultureInfo.InvariantCulture;
        return string.Format(ci, "{0}-{1}-{2}-{3}-{4}-{5}-{6}",
            (int)minIntervalMs, (int)maxIntervalMs, pulseDurationMs,
            (int)intensityNear, (int)intensityFar,
            squareCurve ? "sq" : "lin",
            intensitySteps >= 2 ? intensitySteps.ToString(ci) : "cont");
    }

    private static string BlockOrderText(int id)
    {
        int[] o = LatinSquare[Mathf.Abs(id) % LatinSquare.Length];
        return CaseShort(o[0]) + " > " + CaseShort(o[1]) + " > " + CaseShort(o[2]);
    }

    private static string CaseShort(int i)
    {
        if (i == 0) return "C1 visual";
        if (i == 1) return "C2 pulse";
        return "C3 intensity";
    }

    /// <summary>
    /// 앱을 켜자마자 진동이 되는지 손으로 바로 확인시킨다.
    ///
    /// 진동 경로가 세 단계이므로 하나씩 끊어서 돌린다. 어디까지 느껴지는지가
    /// 곧 원인이다 — 맥이 없어 로그를 볼 수 없으니 이 방식으로 범위를 좁힌다.
    ///   1단계도 안 느껴짐 → 기기/설정 문제 (설정 > 사운드 및 햅틱 > 시스템 햅틱)
    ///   1단계만 느껴짐    → CoreHaptics 와 UIImpactFeedback 둘 다 실패
    ///   2단계까지         → CoreHaptics 만 실패. Case 2 는 되고 Case 3 는 3단계로 뭉개짐
    ///   4단계까지         → 전부 정상
    /// </summary>
    private IEnumerator HapticSelfTest()
    {
        phase = Phase.Ready;

        // 내장 글꼴(LegacyRuntime.ttf)에 한글 글리프가 없어 한글은 빈 칸으로 보인다.
        // HUD 문자열은 전부 ASCII 로 쓴다.
        string head = "HAPTIC CHECK  (" + Application.platform + ")\n\n";

        DebugHud.Set(head + "hold the phone in your hand\nstarting in 2s");
        yield return new WaitForSeconds(2f);

        // ── 1단계 : 기기 기본 진동 ─────────────────────────────
        DebugHud.Set(head + "[1/4] system vibrate\n3 long buzzes");
        for (int k = 0; k < 3; k++)
        {
            HapticManager.SystemVibrate();
            yield return new WaitForSeconds(1.0f);
        }
        yield return new WaitForSeconds(0.6f);

        // ── 2단계 : UIImpactFeedback (세기 3단계) ───────────────
        int[] amps = { 70, 150, 255 };
        string[] names = { "weak", "medium", "strong" };

        DebugHud.Set(head + "[2/4] 3-step taps\nweak - medium - strong");
        yield return new WaitForSeconds(1.0f);
        for (int i = 0; i < amps.Length; i++)
        {
            DebugHud.Set(head + "[2/4] 3-step taps - " + names[i]);
            for (int k = 0; k < 4; k++)
            {
                HapticManager.ImpactOnly(amps[i]);
                yield return new WaitForSeconds(0.3f);
            }
            yield return new WaitForSeconds(0.7f);
        }

        // ── 3단계 : CoreHaptics 펄스 (Case 2 가 쓰는 경로) ──────
        HapticManager.Refresh();
        DebugHud.Set(head + "[3/4] Case 2 pulses\nweak - medium - strong");
        yield return new WaitForSeconds(1.0f);
        for (int i = 0; i < amps.Length; i++)
        {
            DebugHud.Set(head + "[3/4] Case 2 pulses - " + names[i]);
            for (int k = 0; k < 5; k++)
            {
                HapticManager.Pulse(60, amps[i]);
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(0.7f);
        }

        // ── 4단계 : CoreHaptics 연속 진동 (Case 3 가 쓰는 경로) ──
        DebugHud.Set(head + "[4/4] Case 3 continuous\nintensity ramps up");
        yield return new WaitForSeconds(1.0f);
        for (int i = 0; i <= 20; i++)
        {
            HapticManager.SetContinuous(30 + i * 11);
            yield return new WaitForSeconds(0.12f);
        }
        HapticManager.StopContinuous();
        HapticManager.Cancel();
        yield return new WaitForSeconds(0.5f);

        HapticManager.Refresh();
        DebugHud.Set(head
            + "check done\n\n"
            + "CoreHaptics : " + (HapticManager.HasAmplitudeControl ? "OK" : "FAILED") + "\n"
            + HapticManager.Diagnostics + "\n\n"
            + "if you felt nothing, turn on\nSettings > Sounds & Haptics\n> System Haptics");
        yield return new WaitForSeconds(5f);
    }

    private void BuildPool()
    {
        int n = Mathf.Max(1, totalDistractorCount + 1);
        for (int i = 0; i < n; i++)
        {
            GameObject go = Instantiate(targetPrefab, Vector3.zero, Quaternion.identity);
            go.name = "PooledTarget_" + i;
            go.SetActive(false);
            pool.Add(go);
            poolRenderers.Add(go.GetComponent<Renderer>());
        }
    }

    private void BuildBlockOrder()
    {
        blockOrder.Clear();
        if (!runAllBlocks)
        {
            blockOrder.Add(currentCase);
            return;
        }
        int[] order = LatinSquare[Mathf.Abs(participantId) % LatinSquare.Length];
        foreach (int i in order) blockOrder.Add((ExperimentCase)i);
    }

    private void OpenCsv()
    {
        string name = string.IsNullOrEmpty(csvFileName)
            ? string.Format("results_P{0:00}_{1}.csv", participantId, System.DateTime.Now.ToString("yyyyMMdd_HHmmss"))
            : csvFileName;
        csvPath = Path.Combine(Application.persistentDataPath, name);
        try
        {
            File.WriteAllText(csvPath,
                "participant,block_index,case,trial_index,is_practice,eccentricity_deg,side," +
                "search_time_s,total_time_s,first_turn_correct,reversals,path_efficiency," +
                "accum_path_deg,accum_pitch_deg,path_efficiency_3d," +
                "error_clicks,timeout,end_by,dwell_setting_s,haptic_profile\n", Encoding.UTF8);
            Debug.Log("[Experiment] CSV: " + csvPath);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[Experiment] CSV 생성 실패: " + e.Message);
        }
    }

    // ───────────────────────── 블록 / 시행 구성 ─────────────────────────

    private void StartBlock()
    {
        if (blockIndex >= blockOrder.Count)
        {
            phase = Phase.Finished;
            HideAll();
            HapticManager.Cancel();
            DebugHud.Show(true);
            SetStatus("ALL BLOCKS DONE\nthank you\n\nP" + participantId + "\n" + csvPath);
            Debug.Log("[Experiment] all blocks done. csv=" + csvPath);
            return;
        }

        currentCase = blockOrder[blockIndex];
        BuildTrialList();
        trialIndex = 0;

        phase = Phase.BlockRest;
        phaseTimer = 0f;
        HideAll();
        HapticManager.Cancel();
        HapticManager.Refresh();
        DebugHud.Show(true);
        SetStatus(string.Format("P{0}    BLOCK {1}/{2}\n{3}\n\n{4} trials ({5} practice)\n\n{6}\n\nTAP TO START",
            participantId, blockIndex + 1, blockOrder.Count, CaseShort((int)currentCase),
            trials.Count, practiceTrials, HapticManager.Diagnostics));
        Debug.Log(string.Format("[Experiment] block {0}/{1} = {2}", blockIndex + 1, blockOrder.Count, currentCase));
    }

    private void BuildTrialList()
    {
        trials.Clear();

        // 본 시행
        var main = new List<TrialSpec>();
        foreach (float ecc in eccentricitiesDeg)
            for (int s = 0; s < 2; s++)
                for (int r = 0; r < repetitionsPerCell; r++)
                    main.Add(new TrialSpec { eccentricity = ecc, side = (s == 0 ? -1 : 1), isPractice = false });
        Shuffle(main);

        // 연습 시행 — 이심각을 고르게 섞어서 앞에 붙인다
        for (int i = 0; i < practiceTrials; i++)
        {
            trials.Add(new TrialSpec
            {
                eccentricity = eccentricitiesDeg[i % eccentricitiesDeg.Length],
                side = (i % 2 == 0) ? -1 : 1,
                isPractice = true
            });
        }
        trials.AddRange(main);
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            T t = list[i]; list[i] = list[j]; list[j] = t;
        }
    }

    private void StartTrial()
    {
        currentTrial = trials[trialIndex];
        PlaceTargets(currentTrial);

        hapticTimer = 0f;
        dwellTimer = 0f;
        enterHoldTimer = 0f;
        entered = false;
        searchTime = -1f;
        accumulatedYaw = 0f;
        netYawSinceOnset = 0f;
        accumulatedPath = 0f;
        accumulatedPitch = 0f;
        firstTurnSign = 0;
        reversals = 0;
        lastMoveSign = 0;
        errorClicks = 0;

        prevYaw = headCamera.eulerAngles.y;
        prevForward = headCamera.forward;
        prevPitch = PitchOf(headCamera.forward);

        Vector3 dirToTarget = (correctTarget.position - headCamera.position).normalized;
        signedYawAtOnset = SignedYawTo(dirToTarget);
        shortestPathDeg = Vector3.Angle(headCamera.forward, dirToTarget);

        trialStartTime = Time.time;
        phase = Phase.Trial;

        // 시행 중에는 HUD 를 숨긴다. 화살표 말고 다른 글자가 시야에 떠 있을 이유가 없다.
        DebugHud.Show(false);
        if (statusText != null)
            statusText.text = string.Format("{0}  {1}/{2}{3}",
                CaseShort((int)currentCase), trialIndex + 1, trials.Count,
                currentTrial.isPractice ? "  PRACTICE" : "");
    }

    /// <summary>시행 시작 시점의 머리 방향을 기준으로, 지정한 이심각·좌우에 정답을 둔다.</summary>
    private void PlaceTargets(TrialSpec spec)
    {
        Vector3 center = headCamera.position;
        Vector3 fwd = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
        if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.forward;

        var dirs = new List<Vector3>();

        // 정답
        // 이심각은 그대로 지키고, 시선축을 돌아가는 회전각만 자유롭게 둔다.
        // 오른쪽 반구에서 뽑아 왼쪽은 거울로 뒤집으므로 side 는 그대로 살아 있다.
        Vector3 targetDir;
        if (fullSphere)
        {
            float margin = Mathf.Clamp(sideRollMarginDeg, 0f, 80f);
            float a = Random.Range(-90f + margin, 90f - margin);
            targetDir = SphereDir(fwd, spec.eccentricity, spec.side >= 0 ? a : 180f - a);
        }
        else
        {
            targetDir = YawPitch(fwd, spec.side * spec.eccentricity,
                                 Random.Range(-elevationJitterDeg, elevationJitterDeg));
        }
        dirs.Add(targetDir);

        // 방해 물체 — 수평 띠 안에서, 서로 그리고 정답과 최소 간격을 지키도록 기각 표집
        int want = Mathf.Max(0, totalDistractorCount);
        int guard = 0;
        while (dirs.Count < want + 1 && guard < want * 200 + 500)
        {
            guard++;
            Vector3 cand = fullSphere
                ? Random.onUnitSphere
                : YawPitch(fwd, Random.Range(0f, 360f),
                           Random.Range(-elevationJitterDeg, elevationJitterDeg));
            bool ok = true;
            for (int i = 0; i < dirs.Count; i++)
            {
                if (Vector3.Angle(cand, dirs[i]) < minSeparationDeg) { ok = false; break; }
            }
            if (ok) dirs.Add(cand);
        }
        // 간격을 못 맞추면 남은 건 그냥 채운다(무한 루프 방지)
        if (dirs.Count < want + 1)
            Debug.LogWarning("[Experiment] 간격 " + minSeparationDeg + "도를 지키며 "
                + (want + 1) + "개를 다 놓지 못했다 - 남은 "
                + (want + 1 - dirs.Count) + "개는 간격 없이 채운다. "
                + "minSeparationDeg 를 내리거나 totalDistractorCount 를 줄여야 한다");
        while (dirs.Count < want + 1)
            dirs.Add(fullSphere
                ? Random.onUnitSphere
                : YawPitch(fwd, Random.Range(0f, 360f), Random.Range(-elevationJitterDeg, elevationJitterDeg)));

        for (int i = 0; i < pool.Count; i++)
        {
            if (i >= dirs.Count) { pool[i].SetActive(false); continue; }

            GameObject go = pool[i];
            go.SetActive(true);
            go.transform.position = center + dirs[i] * sphereRadius;
            go.transform.LookAt(center);

            // 모두 같은 색으로 둔다. 정답은 드웰해야 가려진다.
            SetTargetColor(i, idleColor);
            if (i == 0) correctTarget = go.transform;
        }

        showingFoundFeedback = false;
    }

    private void SetTargetColor(int index, Color c)
    {
        if (index < 0 || index >= poolRenderers.Count) return;
        Renderer rend = poolRenderers[index];
        if (rend != null) rend.material.color = c;
    }

    /// <summary>
    /// 시선축을 기준으로, 이심각 ecc 만큼 벗어난 방향 하나.
    /// roll 은 시선축을 돌아가는 각도다 — 0 오른쪽, 90 위, 180 왼쪽, 270 아래.
    ///
    /// 이렇게 두면 "왼쪽 45도"가 왼쪽 수평뿐 아니라 왼쪽 위·왼쪽 아래도 될 수 있다.
    /// 이심각(baseFwd 와의 각도)은 roll 과 무관하게 정확히 보존되므로, 통제 변수는
    /// 그대로 살아 있다.
    /// </summary>
    private static Vector3 SphereDir(Vector3 baseFwd, float eccDeg, float rollDeg)
    {
        Vector3 f = baseFwd.normalized;
        Vector3 r = Vector3.Cross(Vector3.up, f).normalized;
        Vector3 u = Vector3.Cross(f, r).normalized;

        float e = eccDeg * Mathf.Deg2Rad;
        float p = rollDeg * Mathf.Deg2Rad;

        return (Mathf.Cos(e) * f
                + Mathf.Sin(e) * (Mathf.Cos(p) * r + Mathf.Sin(p) * u)).normalized;
    }

    private static Vector3 YawPitch(Vector3 baseFwd, float yawDeg, float pitchDeg)
    {
        Vector3 d = Quaternion.AngleAxis(yawDeg, Vector3.up) * baseFwd;
        Vector3 right = Vector3.Cross(Vector3.up, d).normalized;
        return (Quaternion.AngleAxis(pitchDeg, right) * d).normalized;
    }

    /// <summary>
    /// 고도각(도). -90 아래, 0 수평, +90 위.
    /// eulerAngles.x 는 0~360 으로 돌아 들어와 뺄셈이 깨지므로 쓰지 않는다.
    /// </summary>
    private static float PitchOf(Vector3 dir)
    {
        return Mathf.Asin(Mathf.Clamp(dir.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    private float SignedYawTo(Vector3 dir)
    {
        Vector3 a = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
        Vector3 b = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
        return Vector3.SignedAngle(a, b, Vector3.up);   // + = 오른쪽
    }

    // ───────────────────────── 메인 루프 ─────────────────────────

    /// <summary>
    /// 탭 한 번을 한 번으로 센다.
    ///
    /// Api.IsTriggerPressed 는 touch.phase == Began 인지만 본다(Api.cs:121).
    /// 그런데 iOS 는 손가락이 가만히 있으면 새 터치 이벤트를 보내지 않아서,
    /// phase 가 Began 에 머문 채 손가락을 대고 있는 몇 프레임 동안 계속 true 가
    /// 된다. 탭 한 번에 패턴이 다섯 칸 넘어간 원인이고, 시행 중에는 손가락을
    /// 한 번 대는 것만으로 error_clicks 가 수십 번 올라간다.
    ///
    /// 닿기 시작한 프레임만 통과시킨다. Update 에서 프레임당 한 번만 갱신하므로,
    /// 코루틴(참가자 선택 / 튜닝 / 패턴 확인)과 Update(블록 대기 / 시행)가
    /// 같은 값을 보고 서로 어긋나지 않는다.
    /// </summary>
    private void UpdateTriggerEdge()
    {
        bool down = Api.IsTriggerPressed;
        triggerPressedThisFrame = down && !triggerLatched;
        triggerLatched = down;
    }

    private void Update()
    {
        UpdateTriggerEdge();

        // Cardboard 공통 처리
        if (Api.IsGearButtonPressed) Api.ScanDeviceParams();
        if (Api.IsTriggerHeldPressed) Api.Recenter();
        if (Api.HasNewDeviceParams()) Api.ReloadDeviceParams();
        Api.UpdateScreenParams();

        switch (phase)
        {
            case Phase.BlockRest:
                if (triggerPressedThisFrame) StartTrial();
                break;

            case Phase.InterTrial:
                phaseTimer += Time.deltaTime;
                if (showingFoundFeedback &&
                    phaseTimer >= Mathf.Min(foundFeedbackSeconds, interTrialSeconds))
                    HideAll();
                if (phaseTimer >= interTrialSeconds) AdvanceTrial();
                break;

            case Phase.Trial:
                TickTrial();
                break;
        }
    }

    private void TickTrial()
    {
        if (correctTarget == null) return;

        Vector3 dirToTarget = (correctTarget.position - headCamera.position).normalized;
        float angle = Vector3.Angle(headCamera.forward, dirToTarget);

        TrackHeadMotion();

        bool guidanceOn = angle > guidanceCutoffAngleDeg;
        UpdateVisualArrow(dirToTarget, guidanceOn);
        UpdateHaptics(angle, guidanceOn);

        // 첫 진입 시각
        if (!entered)
        {
            if (angle <= selectAngleDeg)
            {
                enterHoldTimer += Time.deltaTime;
                if (enterHoldTimer >= 0.1f)         // 100ms 디바운스 — 스치는 건 제외
                {
                    entered = true;
                    searchTime = Time.time - trialStartTime - 0.1f;
                }
            }
            else enterHoldTimer = 0f;
        }

        // 확정
        bool aimed = angle <= selectAngleDeg;
        if (triggerPressedThisFrame)
        {
            if (aimed) { EndTrial(EndReason.Trigger); return; }
            errorClicks++;
        }
        if (dwellSeconds > 0f)
        {
            dwellTimer = aimed ? dwellTimer + Time.deltaTime : 0f;
            if (dwellTimer >= dwellSeconds) { EndTrial(EndReason.Dwell); return; }
        }

        if (Time.time - trialStartTime >= trialTimeoutSeconds) EndTrial(EndReason.Timeout);
    }

    private void TrackHeadMotion()
    {
        float yawNow = headCamera.eulerAngles.y;
        float d = Mathf.DeltaAngle(prevYaw, yawNow);
        prevYaw = yawNow;

        accumulatedYaw += Mathf.Abs(d);
        netYawSinceOnset += d;

        // 좌우·위아래를 합친 실제 이동 각도. 타겟이 머리 위에 있으면 yaw 는
        // 거의 안 움직이는데 이 값은 제대로 쌓인다.
        Vector3 fwdNow = headCamera.forward;
        accumulatedPath += Vector3.Angle(prevForward, fwdNow);
        prevForward = fwdNow;

        float pitchNow = PitchOf(fwdNow);
        accumulatedPitch += Mathf.Abs(pitchNow - prevPitch);
        prevPitch = pitchNow;

        // 첫 회전 방향 — 누적 5° 넘어선 순간의 부호
        if (firstTurnSign == 0 && Mathf.Abs(netYawSinceOnset) >= 5f)
            firstTurnSign = netYawSinceOnset > 0 ? 1 : -1;

        // 반전 횟수 — 첫 진입 전까지만, 작은 흔들림은 무시
        if (!entered && Mathf.Abs(d) > 0.15f)
        {
            int s = d > 0 ? 1 : -1;
            if (lastMoveSign != 0 && s != lastMoveSign) reversals++;
            lastMoveSign = s;
        }
    }

    private void UpdateVisualArrow(Vector3 dirToTarget, bool guidanceOn)
    {
        if (visualArrowUI == null) return;

        if (!guidanceOn)
        {
            visualArrowUI.gameObject.SetActive(false);
            return;
        }

        Vector3 localDir = headCamera.InverseTransformDirection(dirToTarget);
        visualArrowUI.gameObject.SetActive(true);
        float screenAngle = Mathf.Atan2(localDir.y, localDir.x) * Mathf.Rad2Deg;
        visualArrowUI.localEulerAngles = new Vector3(0, 0, screenAngle - 90f);
    }

    private void UpdateHaptics(float angle, bool guidanceOn)
    {
        if (!guidanceOn) return;

        switch (currentCase)
        {
            case ExperimentCase.Case1_VisualOnly:
                break;

            case ExperimentCase.Case2_Visual_PulseInterval:
                float intervalMs = Mathf.Lerp(minIntervalMs, maxIntervalMs, HapticRatio(angle));
                hapticTimer += Time.deltaTime * 1000f;
                if (hapticTimer >= intervalMs)
                {
                    HapticManager.Pulse(pulseDurationMs, 255);
                    hapticTimer = 0f;
                }
                break;

            case ExperimentCase.Case3_Visual_Intensity:
                hapticTimer += Time.deltaTime * 1000f;
                if (hapticTimer >= 100f)
                {
                    HapticManager.SetContinuous(IntensityAt(angle));
                    hapticTimer = 0f;
                }
                break;
        }
    }

    /// <summary>
    /// 각도(0~180도)를 0~1 비율로 바꾼다. squareCurve 를 켜면 제곱해서,
    /// 가까워질 때 변화가 더 급해진다 (가이드 2-3(A)).
    /// </summary>
    private float HapticRatio(float angle)
    {
        float t = Mathf.Clamp01(angle / 180f);
        return squareCurve ? t * t : t;
    }

    /// <summary>
    /// Case 3 의 세기(1~255). intensitySteps 가 2 이상이면 그 수만큼 단계로 끊는다 (가이드 2-3(B)).
    /// </summary>
    private int IntensityAt(float angle)
    {
        float t = HapticRatio(angle);

        if (intensitySteps >= 2)
        {
            float step = Mathf.Min(Mathf.Floor(t * intensitySteps), intensitySteps - 1);
            t = (step + 0.5f) / intensitySteps;     // 각 단계의 중앙값을 쓴다
        }

        return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(intensityNear, intensityFar, t)), 1, 255);
    }

    // ───────────────────────── 시행 종료 / 기록 ─────────────────────────

    private void EndTrial(EndReason reason)
    {
        bool timedOut = reason == EndReason.Timeout;
        float total = Time.time - trialStartTime;
        if (searchTime < 0f) searchTime = total;

        int firstTurnCorrect = -1;
        if (firstTurnSign != 0)
        {
            int shortest = signedYawAtOnset >= 0f ? 1 : -1;
            firstTurnCorrect = (firstTurnSign == shortest) ? 1 : 0;
        }

        float shortestAbs = Mathf.Abs(signedYawAtOnset);
        float efficiency = accumulatedYaw > 0.01f ? Mathf.Clamp01(shortestAbs / accumulatedYaw) : 0f;

        // 좌우만 보는 efficiency 는 타겟이 위아래에 있을 때 최단 경로를 짧게 잡아
        // 효율을 과대평가한다. 3D 쪽을 같이 남겨 분석에서 고를 수 있게 한다.
        float efficiency3d = accumulatedPath > 0.01f
            ? Mathf.Clamp01(shortestPathDeg / accumulatedPath) : 0f;

        if (!currentTrial.isPractice)
            WriteRow(total, firstTurnCorrect, efficiency, efficiency3d, timedOut, reason);

        Debug.Log(string.Format(
            "[Experiment] case={0} ecc={1} side={2} search={3:F2}s total={4:F2}s firstTurn={5} rev={6} " +
            "eff={7:F2} eff3d={8:F2} path={9:F0} pitch={10:F0} err={11}{12}",
            currentCase, currentTrial.eccentricity, currentTrial.side, searchTime, total,
            firstTurnCorrect, reversals, efficiency, efficiency3d,
            accumulatedPath, accumulatedPitch, errorClicks, timedOut ? " TIMEOUT" : ""));

        HapticManager.StopContinuous();
        HapticManager.Cancel();

        // 색은 찾은 순간에만 바뀐다. 탐색 중에 바뀌면 색 자체가 안내가 되어,
        // 화살표와 진동의 몫을 재는 실험에서 교란이 된다.
        // 시간을 다 쓴 시행은 찾은 것이 아니므로 아무것도 보여 주지 않는다.
        if (reason == EndReason.Timeout)
        {
            HideAll();
        }
        else
        {
            SetTargetColor(0, revealColor);      // 정답은 언제나 pool[0]
            showingFoundFeedback = true;
        }

        phase = Phase.InterTrial;
        phaseTimer = 0f;
    }

    private void WriteRow(float total, int firstTurnCorrect, float efficiency, float efficiency3d,
        bool timedOut, EndReason reason)
    {
        var ci = CultureInfo.InvariantCulture;
        line.Length = 0;
        line.Append(participantId).Append(',')
            .Append(blockIndex).Append(',')
            .Append(currentCase).Append(',')
            .Append(trialIndex).Append(',')
            .Append(currentTrial.isPractice ? 1 : 0).Append(',')
            .Append(currentTrial.eccentricity.ToString("F1", ci)).Append(',')
            .Append(currentTrial.side).Append(',')
            .Append(searchTime.ToString("F3", ci)).Append(',')
            .Append(total.ToString("F3", ci)).Append(',')
            .Append(firstTurnCorrect).Append(',')
            .Append(reversals).Append(',')
            .Append(efficiency.ToString("F4", ci)).Append(',')
            .Append(accumulatedPath.ToString("F1", ci)).Append(',')
            .Append(accumulatedPitch.ToString("F1", ci)).Append(',')
            .Append(efficiency3d.ToString("F4", ci)).Append(',')
            .Append(errorClicks).Append(',')
            .Append(timedOut ? 1 : 0).Append(',')
            .Append(reason).Append(',')
            .Append(dwellSeconds.ToString("F2", ci)).Append(',')
            .Append(HapticProfileText()).Append('\n');

        try { File.AppendAllText(csvPath, line.ToString(), Encoding.UTF8); }
        catch (System.Exception e) { Debug.LogWarning("[Experiment] CSV 기록 실패: " + e.Message); }
    }

    private void AdvanceTrial()
    {
        trialIndex++;
        if (trialIndex < trials.Count) StartTrial();
        else { blockIndex++; StartBlock(); }
    }

    // ───────────────────────── 보조 ─────────────────────────

    private void HideAll()
    {
        foreach (var go in pool) if (go != null) go.SetActive(false);
        if (visualArrowUI != null) visualArrowUI.gameObject.SetActive(false);
        showingFoundFeedback = false;
    }

    private void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
        DebugHud.Set(s);
    }

    private void OnDisable()
    {
        HapticManager.Cancel();
    }
}

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
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

    [Header("안내 차단")]
    [Tooltip("타겟까지 각도가 이 값보다 작아지면 화살표와 진동을 '함께' 끈다. " +
             "0이면 확정 직전까지 계속 안내. 원본 동작(화살표만 37°에서 사라짐)은 조건 간 정보량이 어긋났다.")]
    public float guidanceCutoffAngleDeg = 0f;

    [Header("진단 / 테스트")]
    [Tooltip("켜면 시행 수를 줄여 빠르게 한 바퀴 돌린다. 본 실험에서는 꺼야 한다")]
    public bool testMode = false;
    [Tooltip("앱 시작 시 참가자 번호를 화면에서 고른다. 끄면 위 participantId 값을 그대로 쓴다")]
    public bool askParticipantId = true;
    [Tooltip("앱 시작 시 햅틱을 점검한다. 시스템 햅틱이 꺼진 폰을 걸러내므로 켜 두는 것을 권한다")]
    public bool runHapticSelfTest = true;
    [Tooltip("켜면 시작할 때 진동 프리셋을 비교하는 화면을 띄운다. 값을 정하는 용도이므로 " +
             "본 실험에서는 반드시 꺼야 한다")]
    public bool runHapticTuning = false;
    [Tooltip("켜면 시작할 때 진동 패턴 10가지를 하나씩 확인하는 화면을 띄운다. " +
             "아이폰에서 패턴이 되는지 보는 용도이므로 본 실험에서는 꺼야 한다")]
    public bool runPatternTest = true;
    [Tooltip("VR 화면 안에 상태를 표시한다 (iOS 는 로그를 볼 수 없으므로 필요)")]
    public bool showHud = true;

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

        if (testMode) { repetitionsPerCell = 1; practiceTrials = 1; }
        if (showHud && headCamera != null) DebugHud.Create(headCamera);

        BuildPool();
        blockIndex = 0;

        // 참가자 번호를 앱 안에서 고른 뒤라야 블록 순서와 CSV 파일명이 정해진다.
        StartCoroutine(Startup());
    }

    /// <summary>참가자 번호 선택 → 햅틱 점검 → 첫 블록.</summary>
    private IEnumerator Startup()
    {
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
            yield return StartCoroutine(PatternTest());
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
            if (Api.IsTriggerPressed)
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
            if (Api.IsTriggerPressed)
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
            if (Api.IsTriggerPressed)
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
        firstTurnSign = 0;
        reversals = 0;
        lastMoveSign = 0;
        errorClicks = 0;

        prevYaw = headCamera.eulerAngles.y;

        Vector3 dirToTarget = (correctTarget.position - headCamera.position).normalized;
        signedYawAtOnset = SignedYawTo(dirToTarget);

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
        Vector3 targetDir = YawPitch(fwd, spec.side * spec.eccentricity,
                                     Random.Range(-elevationJitterDeg, elevationJitterDeg));
        dirs.Add(targetDir);

        // 방해 물체 — 수평 띠 안에서, 서로 그리고 정답과 최소 간격을 지키도록 기각 표집
        int want = Mathf.Max(0, totalDistractorCount);
        int guard = 0;
        while (dirs.Count < want + 1 && guard < want * 200 + 500)
        {
            guard++;
            Vector3 cand = YawPitch(fwd, Random.Range(0f, 360f),
                                    Random.Range(-elevationJitterDeg, elevationJitterDeg));
            bool ok = true;
            for (int i = 0; i < dirs.Count; i++)
            {
                if (Vector3.Angle(cand, dirs[i]) < minSeparationDeg) { ok = false; break; }
            }
            if (ok) dirs.Add(cand);
        }
        // 간격을 못 맞추면 남은 건 그냥 채운다(무한 루프 방지)
        while (dirs.Count < want + 1)
            dirs.Add(YawPitch(fwd, Random.Range(0f, 360f), Random.Range(-elevationJitterDeg, elevationJitterDeg)));

        for (int i = 0; i < pool.Count; i++)
        {
            if (i >= dirs.Count) { pool[i].SetActive(false); continue; }

            GameObject go = pool[i];
            go.SetActive(true);
            go.transform.position = center + dirs[i] * sphereRadius;
            go.transform.LookAt(center);

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null) rend.material.color = (i == 0) ? Color.red : Color.white;
            if (i == 0) correctTarget = go.transform;
        }
    }

    private static Vector3 YawPitch(Vector3 baseFwd, float yawDeg, float pitchDeg)
    {
        Vector3 d = Quaternion.AngleAxis(yawDeg, Vector3.up) * baseFwd;
        Vector3 right = Vector3.Cross(Vector3.up, d).normalized;
        return (Quaternion.AngleAxis(pitchDeg, right) * d).normalized;
    }

    private float SignedYawTo(Vector3 dir)
    {
        Vector3 a = Vector3.ProjectOnPlane(headCamera.forward, Vector3.up).normalized;
        Vector3 b = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
        return Vector3.SignedAngle(a, b, Vector3.up);   // + = 오른쪽
    }

    // ───────────────────────── 메인 루프 ─────────────────────────

    private void Update()
    {
        // Cardboard 공통 처리
        if (Api.IsGearButtonPressed) Api.ScanDeviceParams();
        if (Api.IsTriggerHeldPressed) Api.Recenter();
        if (Api.HasNewDeviceParams()) Api.ReloadDeviceParams();
        Api.UpdateScreenParams();

        switch (phase)
        {
            case Phase.BlockRest:
                if (Api.IsTriggerPressed) StartTrial();
                break;

            case Phase.InterTrial:
                phaseTimer += Time.deltaTime;
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
        if (Api.IsTriggerPressed)
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

        if (!currentTrial.isPractice) WriteRow(total, firstTurnCorrect, efficiency, timedOut, reason);

        Debug.Log(string.Format(
            "[Experiment] case={0} ecc={1} side={2} search={3:F2}s total={4:F2}s firstTurn={5} rev={6} eff={7:F2} err={8}{9}",
            currentCase, currentTrial.eccentricity, currentTrial.side, searchTime, total,
            firstTurnCorrect, reversals, efficiency, errorClicks, timedOut ? " TIMEOUT" : ""));

        HapticManager.StopContinuous();
        HapticManager.Cancel();
        HideAll();

        phase = Phase.InterTrial;
        phaseTimer = 0f;
    }

    private void WriteRow(float total, int firstTurnCorrect, float efficiency, bool timedOut, EndReason reason)
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

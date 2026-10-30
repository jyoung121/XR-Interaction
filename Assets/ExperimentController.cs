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
    public bool testMode = true;
    [Tooltip("앱 시작 시 약-중-강 펄스와 연속 진동으로 햅틱을 자가진단한다")]
    public bool runHapticSelfTest = true;
    [Tooltip("VR 화면 안에 상태를 표시한다 (iOS 는 로그를 볼 수 없으므로 필요)")]
    public bool showHud = true;

    [Header("기록")]
    [Tooltip("비워 두면 persistentDataPath 에 저장")]
    public string csvFileName = "";
    [Tooltip("선택 사항. 연결하면 진행 상황을 화면에 표시")]
    public Text statusText;

    // ───────────────────────── 내부 상태 ─────────────────────────

    private enum Phase { Ready, Trial, InterTrial, BlockRest, Finished }

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
        BuildBlockOrder();
        OpenCsv();

        blockIndex = 0;
        if (runHapticSelfTest) StartCoroutine(HapticSelfTest());
        else StartBlock();
    }

    /// <summary>앱을 켜자마자 햅틱이 되는지 눈과 손으로 바로 확인시킨다.</summary>
    private IEnumerator HapticSelfTest()
    {
        phase = Phase.Ready;

        string head = "햅틱 자가진단\n\n"
                    + "플랫폼       : " + Application.platform + "\n"
                    + "진동 가능    : " + HapticManager.HasVibrator + "\n"
                    + "세기 조절    : " + (HapticManager.HasAmplitudeControl ? "지원함" : "지원 안 함") + "\n\n";

        DebugHud.Set(head + "2초 뒤 약 - 중 - 강 순서로 울립니다.");
        yield return new WaitForSeconds(2f);

        int[] amps = { 70, 150, 255 };
        string[] names = { "약", "중", "강" };
        for (int i = 0; i < amps.Length; i++)
        {
            DebugHud.Set(head + "펄스 " + names[i] + "  (세기 " + amps[i] + " / 255)");
            for (int k = 0; k < 5; k++)
            {
                HapticManager.Pulse(60, amps[i]);
                yield return new WaitForSeconds(0.25f);
            }
            yield return new WaitForSeconds(0.9f);
        }

        DebugHud.Set(head + "연속 진동 - 세기가 서서히 올라갑니다.");
        for (int i = 0; i <= 20; i++)
        {
            HapticManager.SetContinuous(30 + i * 11);
            yield return new WaitForSeconds(0.1f);
        }
        HapticManager.StopContinuous();
        HapticManager.Cancel();

        DebugHud.Set(head
            + "자가진단 끝.\n"
            + "약-중-강이 다르게 느껴졌다면 세기 조절이 동작하는 것입니다.\n\n"
            + "트리거(화면 탭)를 누르면 실험이 시작됩니다.");
        yield return new WaitForSeconds(2.5f);

        StartBlock();
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
                "error_clicks,timeout\n", Encoding.UTF8);
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
            SetStatus("실험 종료. 수고하셨습니다.\n" + csvPath);
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
        SetStatus(string.Format("블록 {0}/{1} — {2}\n트리거를 눌러 시작",
            blockIndex + 1, blockOrder.Count, currentCase));
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

        SetStatus(string.Format("{0}  {1}/{2}{3}",
            currentCase, trialIndex + 1, trials.Count, currentTrial.isPractice ? "  (연습)" : ""));
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
            if (aimed) { EndTrial(false); return; }
            errorClicks++;
        }
        if (dwellSeconds > 0f)
        {
            dwellTimer = aimed ? dwellTimer + Time.deltaTime : 0f;
            if (dwellTimer >= dwellSeconds) { EndTrial(false); return; }
        }

        if (Time.time - trialStartTime >= trialTimeoutSeconds) EndTrial(true);
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

        float normalizedAngle = Mathf.Clamp01(angle / 180f);

        switch (currentCase)
        {
            case ExperimentCase.Case1_VisualOnly:
                break;

            case ExperimentCase.Case2_Visual_PulseInterval:
                float intervalMs = Mathf.Lerp(minIntervalMs, maxIntervalMs, normalizedAngle);
                hapticTimer += Time.deltaTime * 1000f;
                if (hapticTimer >= intervalMs)
                {
                    HapticManager.Pulse(pulseDurationMs, 255);
                    hapticTimer = 0f;
                }
                break;

            case ExperimentCase.Case3_Visual_Intensity:
                int amplitude = Mathf.RoundToInt(Mathf.Lerp(255f, 30f, normalizedAngle));
                hapticTimer += Time.deltaTime * 1000f;
                if (hapticTimer >= 100f)
                {
                    HapticManager.SetContinuous(amplitude);
                    hapticTimer = 0f;
                }
                break;
        }
    }

    // ───────────────────────── 시행 종료 / 기록 ─────────────────────────

    private void EndTrial(bool timedOut)
    {
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

        if (!currentTrial.isPractice) WriteRow(total, firstTurnCorrect, efficiency, timedOut);

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

    private void WriteRow(float total, int firstTurnCorrect, float efficiency, bool timedOut)
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
            .Append(timedOut ? 1 : 0).Append('\n');

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

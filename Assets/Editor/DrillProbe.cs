using UnityEngine;
using UnityEditor;

/// <summary>
/// 받아온 드릴 음원이 쓸 만한지 **재서** 가린다.
///
/// 파형 그림만 보고 고르면 안 된다. 우리가 필요한 건 "드릴 소리처럼
/// 들리는가" 가 아니라 두 가지다.
///
///  (가) **이음매 없이 떼어 낼 수 있는 구간**이 있는가.
///       닿아 있는 동안 계속 돌려야 하므로, 고리의 꼬리와 머리가
///       비슷한 크기여야 1 바퀴마다 틱 소리가 안 난다.
///
///  (나) 날이 이에 **닿았다 떨어지는** 순간이 들어 있는가.
///       드릴의 느낌은 공회전이 아니라 그 전환에 있다. 진동 쪽
///       부하 변조 깊이를 그 차이에서 뽑는다.
///
/// 이 둘은 **서로 당긴다.** 고르기만 한 구간은 전환이 없고, 전환이
/// 많은 구간은 꼬리와 머리가 안 맞는다. Loop() 가 그 둘을 같이 재서
/// 타협점을 찾는다.
///
///   Unity.exe -batchmode -quit -buildTarget iOS -projectPath &lt;proj&gt;
///             -executeMethod DrillProbe.Dump
///             -executeMethod DrillProbe.Loop
/// </summary>
public static class DrillProbe
{
    private const string Path = "Assets/Resources/Audio/DentalTurbine.mp3";

    /// <summary>10 ms 마다 한 칸. 진동이 따라갈 수 있는 빠르기다.</summary>
    private const float Hop = 0.010f;

    /// <summary>한 칸을 잴 때 보는 폭. 겹쳐 봐야 들쭉날쭉하지 않다.</summary>
    private const float Win = 0.025f;

    // ───────────────────────── 읽기 ─────────────────────────

    private static float[] Read(out int rate, out float seconds)
    {
        rate = 0; seconds = 0f;

        // 샘플을 읽으려면 통째로 풀어 두게 해야 한다. 기본값은 압축을
        // 품은 채라 GetData 가 0 을 돌려준다.
        AudioImporter imp = AssetImporter.GetAtPath(Path) as AudioImporter;
        if (imp == null) { Debug.LogError("[DrillProbe] 음원을 못 찾는다: " + Path); return null; }
        AudioImporterSampleSettings ss = imp.defaultSampleSettings;
        ss.loadType = AudioClipLoadType.DecompressOnLoad;
        ss.preloadAudioData = true;
        imp.defaultSampleSettings = ss;
        imp.forceToMono = true;          // 진동은 한 줄이다. 섞어서 본다
        imp.SaveAndReimport();

        AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>(Path);
        if (c == null) { Debug.LogError("[DrillProbe] 클립 로드 실패"); return null; }

        float[] s = new float[c.samples * c.channels];
        if (!c.GetData(s, 0) || s.Length == 0)
        { Debug.LogError("[DrillProbe] 샘플을 못 읽는다"); return null; }

        rate = c.frequency;
        seconds = c.length;
        if (c.channels > 1)
        {
            float[] m = new float[c.samples];
            for (int i = 0; i < c.samples; i++) m[i] = s[i * c.channels];
            s = m;
        }
        Debug.Log(string.Format(
            "[DrillProbe] {0}  {1} Hz  ch {2}  {3:F2} 초  샘플 {4}",
            c.name, rate, c.channels, c.length, c.samples));
        return s;
    }

    /// <summary>
    /// 크기의 **움직임**. 이 배열 하나가 소리의 크기이면서 진동의
    /// 세기가 된다 — 두 군데서 따로 재면 어긋날 자리가 생긴다.
    /// </summary>
    private static float[] Envelope(float[] s, int rate)
    {
        int hop = Mathf.Max(1, (int)(rate * Hop));
        int win = Mathf.Max(hop, (int)(rate * Win));
        int n = (s.Length - win) / hop;
        if (n < 2) return new float[0];

        float[] e = new float[n];
        for (int k = 0; k < n; k++)
        {
            double acc = 0;
            int at = k * hop;
            for (int i = 0; i < win; i++) { float v = s[at + i]; acc += v * v; }
            e[k] = Mathf.Sqrt((float)(acc / win));
        }
        return e;
    }

    // ───────────────────────── (1) 뭐가 들어 있나 ─────────────────────────

    public static void Dump()
    {
        int rate; float secs;
        float[] s = Read(out rate, out secs);
        if (s == null) return;

        float[] e = Envelope(s, rate);
        float lo = float.MaxValue, hi = 0f;
        foreach (float r in e) { if (r < lo) lo = r; if (r > hi) hi = r; }
        Debug.Log(string.Format("[DrillProbe] 10ms 칸 {0} 개   최소 {1:F4}  최대 {2:F4}",
                                e.Length, lo, hi));

        var sb = new System.Text.StringBuilder("[DrillProbe] 1초마다 평균 크기");
        int per = (int)(1f / Hop);
        for (int t = 0; t + per <= e.Length; t += per)
        {
            float a = 0f;
            for (int k = 0; k < per; k++) a += e[t + k];
            a /= per;
            int bars = Mathf.RoundToInt(a / Mathf.Max(hi, 1e-6f) * 40f);
            sb.Append(string.Format("{0}  {1,3}s {2:F4} {3}",
                System.Environment.NewLine, t / per, a, new string('#', bars)));
        }
        Debug.Log(sb.ToString());

        float[] sorted = (float[])e.Clone();
        System.Array.Sort(sorted);
        float q1 = sorted[sorted.Length / 4];
        float q3 = sorted[sorted.Length * 3 / 4];
        Debug.Log(string.Format(
            "[DrillProbe] 아래 4분위 {0:F4}  위 4분위 {1:F4}  비 {2:F2} 배"
            + "   (1.5 배 넘으면 닿는 순간이 들어 있다)",
            q1, q3, q3 / Mathf.Max(q1, 1e-6f)));
    }

    // ───────────────────────── (2) 어디를 떼어 낼까 ─────────────────────────

    /// <summary>
    /// 고리로 쓸 구간을 찾는다.
    ///
    /// **처음에 엉뚱한 것을 재서 틀린 구간을 골랐다.** 구간 안의
    /// 위4분위÷아래4분위("전환")를 가장 크게 하는 데를 찾았는데, 그게
    /// 가장 큰 자리는 **드릴을 입에서 빼는** 구간이었다. 3.95~15.95 초가
    /// 뽑혔고 0.5 초 눈금으로 보니 1.5 초에서 0.11 까지 떨어졌다.
    ///
    /// 그게 왜 쓸 수 없나. 이 앱은 보이는 접촉으로 소리와 진동을 이미
    /// 켜고 끈다 (MirrorRig.AnimateDental 의 3.2 초 주기 중 2.1 초가
    /// 접촉이다). 녹음이 제 사정으로 또 조용해지면, **보이는 드릴은
    /// 갈고 있는데 소리와 진동이 사라진다.** 이 연구가 재려는 그
    /// 어긋남을 자극 안에 넣어 버리는 셈이다.
    ///
    /// 그래서 기준을 둘로 나눴다.
    ///
    ///  받침 : 구간이 **내내 갈고 있어야** 한다. 아래 5 % 가 가운데값의
    ///         45 % 아래로 내려가면 버린다.
    ///  전환 : 그 안에서 날이 먹히는 깊이가 오르내려야 한다. 받침을
    ///         지킨 것들 중에서 이게 가장 큰 데를 고른다.
    ///
    /// 이음매는 0.10 으로 조였다. 접촉마다 되감지 않고 **돌려 둔 채로**
    /// 쓰기로 했으니(ExperimentController 참고) 고리가 들리는 중에
    /// 넘어간다 — 전보다 깨끗해야 한다.
    ///
    ///   -executeMethod DrillProbe.Loop
    /// </summary>
    public static void Loop()
    {
        int rate; float secs;
        float[] s = Read(out rate, out secs);
        if (s == null) return;

        float[] e = Envelope(s, rate);
        if (e.Length < 100) { Debug.LogError("[DrillProbe] 너무 짧다"); return; }

        int per = (int)(1f / Hop);            // 1 초에 몇 칸
        int edge = (int)(0.2f / Hop);         // 이음매를 볼 폭 (0.2 초)

        // 접촉 한 번이 2.1 초다. 그보다 짧은 고리는 한 번 닿아 있는
        // 동안에 한 바퀴를 다 도므로 쓸 수 없다.
        foreach (int lenSec in new int[] { 3, 4, 6, 8, 10, 12 })
        {
            int len = lenSec * per;
            if (len + edge >= e.Length) continue;

            int bestAt = -1;
            float bestScore = 0f, bestSeam = 0f, bestFloor = 0f, bestMid = 0f;

            for (int t = 0; t + len < e.Length; t += 5)   // 50 ms 씩 밀며
            {
                float head = 0f, tail = 0f;
                for (int k = 0; k < edge; k++)
                {
                    head += e[t + k];
                    tail += e[t + len - edge + k];
                }
                head /= edge; tail /= edge;
                float seam = Mathf.Abs(head - tail) / Mathf.Max(head, 1e-6f);
                if (seam > 0.10f) continue;

                float[] w = new float[len];
                System.Array.Copy(e, t, w, 0, len);
                System.Array.Sort(w);
                float p05 = w[len / 20], q1 = w[len / 4];
                float mid = w[len / 2], q3 = w[(len * 3) / 4];

                // 받침 — 내내 갈고 있나
                if (p05 < mid * 0.45f) continue;
                // 거의 무음인 구간은 애초에 쓸 수 없다
                if (mid < 0.08f) continue;

                float contrast = q3 / Mathf.Max(q1, 1e-6f);
                if (contrast > bestScore)
                {
                    bestScore = contrast; bestAt = t;
                    bestSeam = seam; bestFloor = p05 / mid; bestMid = mid;
                }
            }

            if (bestAt < 0)
                Debug.Log("[DrillProbe] " + lenSec + "초: 내내 갈고 있는 구간 없음");
            else
                Debug.Log(string.Format(
                    "[DrillProbe] {0,2}초 고리  {1:F2}~{2:F2} 초   전환 {3:F2} 배"
                    + "   받침 {4:F2} (가운데값의 몫, 0.45 넘어야)"
                    + "   가운데 {5:F4}   이음매 {6:F1} %",
                    lenSec, bestAt * Hop, (bestAt + len) * Hop,
                    bestScore, bestFloor, bestMid, bestSeam * 100f));
        }
    }

    // ─────────────── (3) 실제로 연결됐나 ───────────────

    /// <summary>
    /// ScenarioAudio 가 **녹음을 쓰고 있는지**, 그 포락선이 모터 세기로
    /// 얼마나 벌어지는지 잰다.
    ///
    /// 이걸 따로 재는 이유. BuildDrill 은 녹음을 못 읽으면 조용히 합성으로
    /// 떨어진다 — 소리는 그대로 나니 **고장난 줄을 모른다.** 그 길로
    /// 떨어지면 DrillLoad 가 내내 1 이라 진동이 11/15 와 똑같아지고,
    /// "부하 깊이를 올렸는데 아무 차이가 없다" 가 된다.
    ///
    ///   -executeMethod DrillProbe.Check
    /// </summary>
    public static void Check()
    {
        AudioClip c = ScenarioAudio.Drill;
        if (c == null) { Debug.LogError("[DrillProbe] 드릴 클립이 없다"); return; }

        Debug.Log(string.Format(
            "[DrillProbe] 쓰는 클립 {0}  {1:F2} 초  {2} Hz   녹음인가 {3}",
            c.name, c.length, c.frequency,
            ScenarioAudio.DrillIsRecorded ? "그렇다" : "**아니다 (합성으로 떨어졌다)**"));

        if (!ScenarioAudio.DrillIsRecorded)
        {
            Debug.LogError("[DrillProbe] 합성으로 떨어졌다 — 진동이 꿀렁이지 "
                + "않는다. Resources/Audio 와 .meta 를 봐라");
            return;
        }

        // 고리의 이음매.
        //
        // **처음에 틀린 것을 쟀다.** 꼬리에서 머리로 건너뛰는 폭을 그
        // 자리 RMS 와 견주어 "1.44 배" 를 얻고 이음매가 티 난다고 읽었다.
        // 틀렸다. 24 kHz 에 9 kHz 성분이 있으면 **한 주기가 2.7 샘플**
        // 이라, 붙어 있는 두 샘플끼리도 그만큼 벌어진다. 게다가 녹임
        // 때문에 b[0] 은 원본에서 b[n-1] 다음 샘플이라 **애초에 이어져
        // 있다.** 그러니 건너뛰는 폭은 안쪽의 보통 걸음과 견줘야 한다 —
        // 1 쯤이면 이음매가 없는 것이다.
        float[] b = new float[c.samples];
        if (c.GetData(b, 0))
        {
            int w = c.frequency / 100;            // 10 ms
            float head = 0f, tail = 0f;
            for (int i = 0; i < w; i++)
            {
                head += b[i] * b[i];
                tail += b[b.Length - 1 - i] * b[b.Length - 1 - i];
            }
            head = Mathf.Sqrt(head / w); tail = Mathf.Sqrt(tail / w);

            double step = 0;
            for (int i = 1; i < b.Length; i++) step += Mathf.Abs(b[i] - b[i - 1]);
            step /= b.Length - 1;
            float jump = Mathf.Abs(b[0] - b[b.Length - 1]);
            Debug.Log(string.Format(
                "[DrillProbe] 이음매: 머리 10ms {0:F4}  꼬리 10ms {1:F4}"
                + "   넘어가는 걸음 {2:F4}  안쪽 보통 걸음 {3:F4}"
                + "   몫 {4:F2} (1 쯤이면 이음매 없음)",
                head, tail, jump, (float)step, jump / Mathf.Max((float)step, 1e-9f)));
        }

        float[] e = ScenarioAudio.DrillEnvelope;
        if (e == null || e.Length == 0)
        { Debug.LogError("[DrillProbe] 포락선이 비었다"); return; }

        float lo = float.MaxValue, hi = 0f, mean = 0f;
        foreach (float v in e) { if (v < lo) lo = v; if (v > hi) hi = v; mean += v; }
        mean /= e.Length;
        float[] q = (float[])e.Clone();
        System.Array.Sort(q);
        Debug.Log(string.Format(
            "[DrillProbe] 포락선 {0} 칸 ({1:F1} 초)   최소 {2:F2}  아래4분위 {3:F2}"
            + "  가운데 {4:F2}  위4분위 {5:F2}  최대 {6:F2}  평균 {7:F2}",
            e.Length, e.Length * Hop, lo, q[q.Length / 4], q[q.Length / 2],
            q[(q.Length * 3) / 4], hi, mean));

        var sb = new System.Text.StringBuilder(
            "[DrillProbe] 0.5초마다 포락선 (| = 1.0 자리)");
        int per = (int)(0.5f / Hop);
        for (int t = 0; t + per <= e.Length; t += per)
        {
            float a = 0f;
            for (int k = 0; k < per; k++) a += e[t + k];
            a /= per;
            int bars = Mathf.Clamp(Mathf.RoundToInt(a * 30f), 0, 44);
            string bar = new string('#', bars);
            if (bars < 30) bar = bar.PadRight(30) + "|";
            sb.Append(string.Format("{0}  {1,5:F1}s {2:F2} {3}",
                System.Environment.NewLine, t * Hop, a, bar));
        }
        Debug.Log(sb.ToString());

        // **모터가 실제로 쓸 수 있는 폭.** 설정창 다섯 칸을 그대로 돌려
        // 본다. 0 칸이 11/15 까지의 평평한 진동과 같아야 하고, 칸을
        // 올릴수록 폭이 벌어져야 한다.
        float[] depths = { 0.00f, 0.35f, 0.70f, 1.00f, 1.40f };
        var sb2 = new System.Text.StringBuilder(
            "[DrillProbe] 부하 깊이 칸마다 모터 세기 (1~255)");
        foreach (float d in depths)
        {
            int mn = 255, mx = 1; float av = 0f;
            foreach (float v in e)
            {
                float amp = Mathf.Max(0.08f, Mathf.LerpUnclamped(1f, v, d));
                int motor = Mathf.Clamp(Mathf.RoundToInt(255f * amp), 1, 255);
                if (motor < mn) mn = motor;
                if (motor > mx) mx = motor;
                av += motor;
            }
            sb2.Append(string.Format("{0}   깊이 {1:F2}  {2,3} ~ {3,3}"
                + "  평균 {4,3:F0}   폭 {5,3}",
                System.Environment.NewLine, d, mn, mx, av / e.Length, mx - mn));
        }
        Debug.Log(sb2.ToString());
    }
}

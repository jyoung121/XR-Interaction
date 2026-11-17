using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 사과 씬을 **녹음한 씹는 소리**에 맞춘다 — 소리, 거울 속 턱, 진동이 한 시간표를 쓴다.
///
/// 시간표는 AppleCrunchData 에 있다. 소리 파일(Resources/Audio/AppleCrunch.wav)을 재서
/// 뽑은 것이고, 한 입 주기 15.7 초가 세 번 이어져 사과 하나(세 입)가 된다.
///
///  소리   ScenarioAudio.Loop.Apple 이 그 파일을 돌린다. 파일의 0 초가 곧 장면의 0 초라
///         따로 예약할 것이 없다 — 어긋나면 SyncLoop 가 다시 맞춘다.
///  턱     MirrorRig.ChewJawOf 가 같은 표(ChewStart/ChewEnd)를 읽어 씹는 소리마다 다문다.
///  진동   셋 중 하나를 고른다 (HapticMode). **셋 다 진동이 나는 시각은 소리가 정한다** —
///         간격을 손으로 고치는 칸은 없다. 고칠 수 있는 것은 세기와 모양뿐이다.
///
/// 씹는 덩어리(이벤트)마다 파형 하나를 그 덩어리가 시작되는 순간에
/// HapticManager.PlayWaveform 으로 넘긴다. 덩어리 안의 잔 타이밍은 OS 가 맞춰 주므로
/// 프레임 시간에 흔들리지 않는다. 한 주기를 통째로 한 파형으로 내지 않는 이유는 iOS 쪽이
/// 파형 하나에 이벤트를 96개까지만 받기 때문이다 (CardboardHaptics.mm 의 kMaxWaveEvents).
/// </summary>
public static class AppleSync
{
    /// <summary>
    /// 사과 씬에 쓸 진동. 설정 화면의 "3. APPLY TO SCENE" 에서 고르고, 거울 방 아래쪽에
    /// 그 번호(1·2·3)가 뜬다 (OptionBadge).
    ///
    ///  Taps    (1) 소리의 잔 파열 하나하나에 탭. 세기도 그 파열의 크기를 따른다 — 기본값
    ///  Crunch  (2) 씹는 덩어리마다 Crunch. 탭의 **시각은 그 덩어리의 가장 큰 파열들**이고,
    ///              세기는 Crunch 식(첫 탭이 가장 세고 점점 약해진다)을 따른다
    ///  Pattern (3) 씹는 덩어리마다 고른 패턴 하나. 패턴을 **그 소리의 길이에 맞춰** 늘이거나
    ///              줄인다 (150~735 ms)
    /// </summary>
    public enum HapticMode { Taps, Crunch, Pattern }

    /// <summary>설정 화면(PatternSettingsMenu)에서 고치는 값. 앱이 떠 있는 동안 남는다.</summary>
    public class SettingsData
    {
        /// 녹음한 소리를 쓸지. 끄면 예전처럼 합성한 깨무는 소리 + Crunch 진동으로 간다.
        public bool useRecordedSound = true;
        public HapticMode haptics = HapticMode.Taps;

        // ── 탭(1 번)에만 쓰는 값 ──
        /// 크게 베어 무는 첫 소리의 세기 배율 (0~2)
        public float biteGain = 1.0f;
        /// 잘게 씹는 소리들의 세기 배율 (0~4). 소리로는 첫 입보다 20~35 dB 작아서
        /// 그대로 옮기면 거의 안 느껴진다. 그래서 1 보다 키워 둔다.
        public float chewGain = 1.5f;
        /// 탭 하나의 길이 (ms). 탭 간격보다 길면 옆 탭과 이어져 연속 진동처럼 된다.
        public int tapMs = 10;
        /// 이보다 붙은 탭은 하나로 합친다 (ms). 탭의 시각을 옮기지는 않는다 — 너무
        /// 촘촘한 것을 솎을 뿐이다. 0 이면 전부 낸다.
        public int minSpacingMs = 12;
        /// 이 세기(1~255)보다 약한 탭은 버린다. 0 이면 전부 낸다.
        public int minAmplitude = 10;
        /// iOS 전용. 이 길이(ms) 이하인 탭은 짧은 탁(transient)으로 낸다.
        public int transientMaxMs = 12;

        // ── 셋 다에 쓰는 값 ──
        /// 진동을 소리보다 얼마나 늦게 낼지 (ms). 음수면 앞당긴다. 간격을 바꾸는 것이
        /// 아니라 진동 전체를 통째로 민다. 기기마다 소리가 나오기까지의 지연이 달라서 필요하다.
        public int hapticOffsetMs = 0;
        /// 소리 크기 0~1. 0 이면 진동만 느껴 볼 수 있다.
        public float volume = 1f;
        /// iOS 전용. 사과 씬 진동의 날카로움 0~1 (0 둥글다 ~ 1 또렷하다).
        /// 거울 방에 들어갈 때와 미리듣기에서 HapticManager.Sharpness 에 넣는다.
        /// 0.35 는 음식 씬에 지금까지 박혀 있던 값이다.
        public float sharpness = 0.35f;

        public void CopyFrom(SettingsData o)
        {
            useRecordedSound = o.useRecordedSound;
            haptics = o.haptics;
            biteGain = o.biteGain;
            chewGain = o.chewGain;
            tapMs = o.tapMs;
            minSpacingMs = o.minSpacingMs;
            minAmplitude = o.minAmplitude;
            transientMaxMs = o.transientMaxMs;
            hapticOffsetMs = o.hapticOffsetMs;
            volume = o.volume;
            sharpness = o.sharpness;
        }
    }

    public static readonly SettingsData Settings = new SettingsData();

    /// <summary>지금 사과 씬이 녹음한 소리로 돌아가나 (설정이 켜져 있고 파일도 있다).</summary>
    public static bool Active
    {
        get { return Settings.useRecordedSound && ScenarioAudio.HasApple; }
    }

    /// <summary>
    /// 지금 사과 씬에 실제로 쓰이는 진동의 번호 (1 탭, 2 Crunch, 3 패턴).
    /// 녹음한 소리가 꺼져 있거나 파일이 없으면 소리에 맞출 수가 없어, 예전의
    /// 합성 소리 + Crunch 로 떨어진다. 그때는 2 다.
    /// </summary>
    public static int OptionNumber
    {
        get
        {
            if (!Active) return 2;
            HapticMode m = Settings.haptics;
            return m == HapticMode.Taps ? 1 : m == HapticMode.Crunch ? 2 : 3;
        }
    }

    // ───────────────────────── 시계 ─────────────────────────

    private static int cycleAt = -1;         // 지난 프레임의 주기 번호
    private static int nextStart;            // 다음에 쏠 이벤트
    private static int crunchCount;          // Crunch 를 쏜 횟수 — seed 를 여기서 올린다

    /// <summary>시계를 처음으로 돌린다. 장면에 새로 들어갈 때 부른다.</summary>
    public static void Reset()
    {
        cycleAt = -1;
        nextStart = 0;
        crunchCount = 0;
    }

    /// <summary>
    /// 씹는 덩어리(이벤트)가 **시작되는 순간**을 하나씩 알려 준다.
    ///
    /// 매 프레임 `while (NextEvent(t, out i)) PlayEvent(...)` 로 부른다. 장면 시계가
    /// 이벤트 시작(+ hapticOffsetMs)을 지나면 그 번호를 한 번만 돌려준다.
    /// </summary>
    /// <param name="sceneSeconds">장면에 들어온 뒤 흐른 시간 (MirrorRig.Seconds)</param>
    public static bool NextEvent(float sceneSeconds, out int index)
    {
        index = -1;
        float period = AppleCrunchData.SegmentSeconds;
        if (sceneSeconds < 0f) sceneSeconds = 0f;

        int cycle = Mathf.FloorToInt(sceneSeconds / period);
        double localMs = (sceneSeconds - cycle * period) * 1000.0;
        int[] starts = AppleCrunchData.EventStartMs;

        if (cycle != cycleAt)
        {
            // 새 주기. 시계가 뒤로 갔을 때(장면을 다시 켰다)도 여기로 온다.
            cycleAt = cycle;
            nextStart = 0;
            // 주기 한가운데로 뛰어들었으면 이미 지난 것은 건너뛴다 —
            // 한꺼번에 몰아서 쏘면 안 된다.
            while (nextStart < starts.Length &&
                   starts[nextStart] + Settings.hapticOffsetMs < localMs - 60.0)
                nextStart++;
        }

        if (nextStart >= starts.Length) return false;
        if (localMs < starts[nextStart] + Settings.hapticOffsetMs) return false;

        index = nextStart;
        nextStart++;
        return true;
    }

    /// <summary>
    /// 이벤트 하나의 진동을 쏜다. 거울 방과 설정 화면의 미리듣기가 같이 쓴다 —
    /// 미리 들은 것과 방에서 느끼는 것이 같아야 한다.
    /// </summary>
    /// <param name="ampScale">세기 배율 (거울 방의 세기 손잡이. 미리듣기는 1)</param>
    public static void PlayEvent(HapticMode mode, int index, float ampScale,
                                 ExperimentController c)
    {
        if (c == null) return;

        CrunchSettings cs = ScenarioHaptics.Crunch;
        int seed = cs.seed;
        if (mode == HapticMode.Crunch)
        {
            if (cs.autoIncrementSeed) seed = cs.seed + crunchCount;
            crunchCount++;
        }

        long[] timings;
        int[] amplitudes;
        BuildModeEvent(mode, index, ampScale, Settings, cs, seed,
            c.previewPattern, c.waveHighAmplitude, c.waveLowAmplitude,
            out timings, out amplitudes);
        if (timings.Length == 0) return;

        int transient = mode == HapticMode.Taps ? Settings.transientMaxMs
                      : mode == HapticMode.Crunch ? cs.transientMaxMs
                      : 0;
        HapticManager.StopWaveform();
        HapticManager.PlayWaveform(timings, amplitudes, Mathf.Clamp(transient, 0, 200));
    }

    // ───────────────────────── 파형 만들기 ─────────────────────────

    /// <summary>
    /// 이벤트 하나의 파형을 만든다. **파형의 0 은 그 이벤트가 시작되는 순간**이다 —
    /// 첫 진동이 그보다 늦으면 앞에 세기 0 인 구간이 들어간다. 낼 것이 없으면
    /// 길이 0 인 배열을 돌려준다.
    ///
    /// 설정 화면이 아직 저장 안 한 값으로 그림을 그릴 수 있도록, 값을 전부 인자로 받는다.
    /// </summary>
    /// <param name="ampScale">세기 배율 (거울 방의 세기 손잡이)</param>
    /// <param name="crunchSeed">Crunch 의 세기 흔들림에 쓸 seed</param>
    /// <param name="high">Peak strength 1~255</param>
    /// <param name="low">Low strength 1~255 (Ramp·Bounce·Wobble 에만 쓰인다)</param>
    public static void BuildModeEvent(HapticMode mode, int index, float ampScale,
        SettingsData taps, CrunchSettings crunch, int crunchSeed,
        VibrationPattern pattern, int high, int low,
        out long[] timings, out int[] amplitudes)
    {
        if (taps == null) taps = Settings;
        if (crunch == null) crunch = ScenarioHaptics.Crunch;
        ampScale = Mathf.Max(0f, ampScale);
        high = Mathf.Clamp(high, 1, 255);

        // 크게 베어 무는 첫 이벤트가 가장 세고, 씹는 소리는 뒤로 갈수록 잦아든다
        // (1.0 → 0.28). 탭은 이걸 안 쓴다 — 소리에서 잰 크기가 이미 그렇게 줄어든다.
        int n = AppleCrunchData.EventStartMs.Length;
        bool first = index < AppleCrunchData.BiteEvents;
        float left = n > 1 ? 1f - index / (float)(n - 1) : 1f;
        float strength = first ? 1f : 0.28f + 0.72f * left;

        List<long> t = new List<long>();
        List<int> a = new List<int>();

        if (mode == HapticMode.Taps)
        {
            int lead;
            long[] tt;
            int[] aa;
            BuildTaps(index, taps,
                Mathf.Clamp(Mathf.RoundToInt(high * ampScale), 1, 255),
                out tt, out aa, out lead);
            if (tt.Length > 0 && lead > 0) { t.Add(lead); a.Add(0); }
            for (int i = 0; i < tt.Length; i++) { t.Add(tt[i]); a.Add(aa[i]); }
        }
        else if (mode == HapticMode.Crunch)
        {
            int peak = Mathf.Clamp(Mathf.RoundToInt(high * strength * ampScale), 1, 255);
            int baseFrags = Mathf.Clamp(crunch.fragments, 1, 12);
            int frags = first ? baseFrags
                      : Mathf.Max(1, Mathf.RoundToInt(baseFrags * (0.35f + 0.65f * left)));
            BuildSyncedCrunch(index, crunch, frags, peak, crunchSeed, t, a);
        }
        else
        {
            int scale = Mathf.Clamp(Mathf.RoundToInt(255f * strength * ampScale), 1, 255);
            int peak = Mathf.Clamp(Mathf.RoundToInt(high * scale / 255f), 1, 255);
            int floor = Mathf.Clamp(Mathf.RoundToInt(
                Mathf.Clamp(low, 1, 255) * scale / 255f), 1, peak);
            BuildSyncedPattern(index, pattern, peak, floor, t, a);
        }

        timings = t.ToArray();
        amplitudes = a.ToArray();
    }

    /// <summary>
    /// 2 번 — 소리에 맞춘 Crunch.
    ///
    /// 탭이 나는 **시각은 이 덩어리에서 가장 큰 파열 frags 개**다. 조각 사이의 침묵을
    /// 난수로 뽑던 것(ScenarioHaptics.BuildCrunch 의 15~65 ms)을 소리가 대신한다.
    /// 세기는 Crunch 식 그대로다 — 첫 탭이 가장 세고, 탭마다 흔들리며 약해진다.
    /// </summary>
    private static void BuildSyncedCrunch(int index, CrunchSettings s, int frags, int peak,
                                          int seed, List<long> t, List<int> a)
    {
        int[] tapMs = AppleCrunchData.TapMs[index];
        int[] tapLevel = AppleCrunchData.TapLevel[index];
        if (tapMs.Length == 0) return;
        frags = Mathf.Clamp(frags, 1, tapMs.Length);

        // 가장 큰 파열 frags 개를 고른다. 이미 고른 것과 20 ms 안쪽으로 붙은 파열은
        // 뒤로 미룬다 — 큰 파열은 둘씩 붙어 나오는 일이 많아서, 그대로 고르면 탭 둘이
        // 한 번처럼 느껴지고 정작 다른 자리는 빈다. 떨어진 것이 더 없을 때만 붙은 것을 쓴다.
        bool[] use = new bool[tapMs.Length];
        for (int k = 0; k < frags; k++)
        {
            int best = -1;
            for (int pass = 0; pass < 2 && best < 0; pass++)
            {
                for (int i = 0; i < tapMs.Length; i++)
                {
                    if (use[i]) continue;
                    if (pass == 0 && NearChosen(tapMs, use, i, 20)) continue;
                    if (best < 0 || tapLevel[i] > tapLevel[best]) best = i;
                }
            }
            if (best < 0) break;
            use[best] = true;
        }

        int onMin = Mathf.Clamp(s.fragmentMinMs, 1, 200);
        int onMax = Mathf.Clamp(s.fragmentMaxMs, onMin, 200);
        float jitterFloor = Mathf.Clamp01(s.jitterFloor);
        float decayMin = Mathf.Clamp(s.decayMin, 0.05f, 1f);
        float decayMax = Mathf.Clamp(s.decayMax, decayMin, 1f);

        System.Random rng = new System.Random(seed);
        float level = 1f;
        int at = 0;                 // 지금까지 채운 길이 (ms, 이벤트 시작부터)
        for (int i = 0; i < tapMs.Length; i++)
        {
            if (!use[i]) continue;

            int length = onMin + rng.Next(0, onMax - onMin + 1);
            float jitter = jitterFloor + (float)rng.NextDouble() * (1f - jitterFloor);
            int amp = Mathf.Clamp(Mathf.RoundToInt(peak * level * jitter), 1, 255);
            level *= decayMin + (float)rng.NextDouble() * (decayMax - decayMin);

            int start = Mathf.Max(tapMs[i], at);     // 앞 탭이 길어 겹치면 바로 뒤에 붙인다
            if (start > at) { t.Add(start - at); a.Add(0); }
            t.Add(length);
            a.Add(amp);
            at = start + length;
        }
    }

    /// <summary>i 번 파열이 이미 고른 파열과 withinMs 안쪽으로 붙어 있나.</summary>
    private static bool NearChosen(int[] tapMs, bool[] use, int i, int withinMs)
    {
        for (int j = 0; j < tapMs.Length; j++)
            if (use[j] && Mathf.Abs(tapMs[j] - tapMs[i]) < withinMs) return true;
        return false;
    }

    /// <summary>
    /// 3 번 — 소리에 맞춘 패턴.
    ///
    /// 패턴 하나를 **이 덩어리의 소리 길이**에 맞춰 늘이거나 줄인다. 그래서 길이·간격·
    /// 변조 주파수를 손으로 넣는 칸이 없다 — 소리가 150 ms 면 패턴도 150 ms 다.
    ///
    ///  Pulse                  소리 길이만큼 한 번
    ///  Ramp / Bounce / Wobble 구간 5·5·7 개를 소리 길이에 고르게 나눈다
    ///  Sine … Damped          포락선 한 주기를 소리 길이에 편다 (10 ms 단위로 그린다)
    /// </summary>
    private static void BuildSyncedPattern(int index, VibrationPattern pattern,
                                           int peak, int low, List<long> t, List<int> a)
    {
        int length = AppleCrunchData.EventEndMs[index] - AppleCrunchData.EventStartMs[index];
        length = Mathf.Clamp(length, 60, 2000);

        long[] timings;
        int[] amplitudes;

        if (pattern == VibrationPattern.SinglePulse)
        {
            timings = new long[] { length };
            amplitudes = new int[] { peak };
        }
        else if (AndroidVibrationManager.IsMathPattern(pattern))
        {
            AndroidVibrationManager.BuildMathWaveform(pattern, length, 10,
                1000f / length, peak, out timings, out amplitudes);
        }
        else
        {
            int parts = pattern == VibrationPattern.Wobble ? 7 : 5;
            long step = Mathf.Max(1, length / parts);
            AndroidVibrationManager.BuildWaveform(pattern, step, step, peak, low,
                out timings, out amplitudes);
        }

        for (int i = 0; i < timings.Length; i++) { t.Add(timings[i]); a.Add(amplitudes[i]); }
    }

    /// <summary>
    /// 1 번 — 잔 파열마다 탭. leadMs 는 덩어리 시작에서 첫 탭까지의 시간이고,
    /// 파형은 첫 탭에서 시작한다. 남는 탭이 없으면 길이 0 인 배열을 돌려준다.
    /// </summary>
    private static void BuildTaps(int index, SettingsData s, int peak,
        out long[] timings, out int[] amplitudes, out int leadMs)
    {
        int[] tapMs = AppleCrunchData.TapMs[index];
        int[] tapLevel = AppleCrunchData.TapLevel[index];
        float gain = index < AppleCrunchData.BiteEvents ? s.biteGain : s.chewGain;
        gain = Mathf.Clamp(gain, 0f, 4f);
        peak = Mathf.Clamp(peak, 1, 255);
        int tapLength = Mathf.Clamp(s.tapMs, 1, 100);
        int spacing = Mathf.Clamp(s.minSpacingMs, 0, 200);
        int weakest = Mathf.Clamp(s.minAmplitude, 0, 255);

        // 1) 세기를 매기고, 너무 약하거나 너무 붙은 탭을 걸러 낸다
        List<int> keptMs = new List<int>();
        List<int> keptAmp = new List<int>();
        for (int i = 0; i < tapMs.Length; i++)
        {
            int amp = Mathf.Clamp(Mathf.RoundToInt(peak * (tapLevel[i] / 255f) * gain), 0, 255);
            if (amp < 1 || amp < weakest) continue;

            int last = keptMs.Count - 1;
            if (last >= 0 && tapMs[i] - keptMs[last] < spacing)
            {
                // 붙어 있는 탭은 하나로 — 센 쪽의 세기를 남긴다
                if (amp > keptAmp[last]) keptAmp[last] = amp;
                continue;
            }

            keptMs.Add(tapMs[i]);
            keptAmp.Add(amp);
        }

        if (keptMs.Count == 0)
        {
            timings = new long[0];
            amplitudes = new int[0];
            leadMs = 0;
            return;
        }

        // 2) 탭 → 켜짐/꺼짐 구간
        List<long> t = new List<long>();
        List<int> a = new List<int>();
        for (int i = 0; i < keptMs.Count; i++)
        {
            int length = tapLength;
            if (i + 1 < keptMs.Count)
            {
                int untilNext = keptMs[i + 1] - keptMs[i];
                if (untilNext < 1) untilNext = 1;
                if (length > untilNext) length = untilNext;

                t.Add(length);
                a.Add(keptAmp[i]);
                if (untilNext > length)
                {
                    t.Add(untilNext - length);
                    a.Add(0);
                }
            }
            else
            {
                t.Add(length);
                a.Add(keptAmp[i]);
            }
        }

        timings = t.ToArray();
        amplitudes = a.ToArray();
        leadMs = keptMs[0];
    }
}

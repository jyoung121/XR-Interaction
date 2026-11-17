using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 시나리오별 진동을 만든다 — 치과치료 / 음식먹기.
/// (바리깡은 11/10 에 빠졌다. 머리에 닿는 시나리오가 없어져서 두 상황 다 입이다.)
///
/// 조원 코드(AndroidVibrationManager)에는 손대지 않는다. VibrationPattern enum 과
/// BuildWaveform / BuildMathWaveform 이 거기 있어서, 그 파일을 고치면 조원이 Assets 를
/// 통째로 덮을 때 또 충돌한다. 그래서 새 패턴은 전부 이 파일에서 만들고,
/// 재생은 이미 양쪽 플랫폼이 공유하는 HapticManager.PlayWaveform 으로 넘긴다.
///
/// 두 시나리오는 성격이 갈린다.
///
///  치과     : 지속 진동. 파형이 아니라 HapticManager.SetContinuous 로 돌린다.
///             두 수준(프리퀀시 높음/낮음)은 **변조 주파수**로 낸다 — 모터의 실제
///             진동 주파수는 LRA 공진이라 못 바꾼다 (AndroidVibrationManager 참고).
///             세기를 주기적으로 꿀렁이게 하는 ClipperLoad() 가 그 자리에 쓸 수 있다.
///
///  음식먹기 : 불규칙한 짧은 탁이 여러 번. 파형으로 돌린다 → BuildCrunch().
///             주기가 일정하면 바로 기계처럼 들리므로 간격·세기를 흔든다.
///             두 수준은 조각 수와 세기로 가른다 — 사과는 적고 세게,
///             무른 것은 더 적고 약하게 (ExperimentController.ApplyContactHaptics).
/// </summary>
/// <summary>
/// 한 입(Crunch) 파형을 정하는 값 전부. 설정 화면(PatternSettingsMenu)의 Crunch 칸에서
/// 고친다. 기본값은 지금까지 BuildCrunch 안에 박혀 있던 수 그대로다.
/// </summary>
public class CrunchSettings
{
    /// 깨지는 조각(탁) 수. 과자 4~6, 사과 2~3, 얼음 1~2 정도.
    /// 사과 씬이 이 값을 쓴다 (음식 표의 frags 3 과 같은 값에서 출발한다).
    public int fragments = 3;
    /// 난수 씨앗. 같은 값이면 같은 파형.
    public int seed = 1;
    /// 켜면 재생할 때마다 seed 를 1 씩 올려 매번 다른 파형을 낸다.
    public bool autoIncrementSeed = true;

    /// 조각 하나의 길이 범위 (ms)
    public int fragmentMinMs = 4;
    public int fragmentMaxMs = 11;
    /// 조각 사이 침묵 범위 (ms). 설정창의 주기 손잡이(HapticPanel.GapScale)가 곱해진다.
    public int gapMinMs = 15;
    public int gapMaxMs = 65;
    /// 조각 세기 흔들림의 바닥 (0~1). 각 조각은 [이 값, 1] 사이 배율을 무작위로 받는다.
    public float jitterFloor = 0.65f;
    /// 조각이 하나 지날 때마다 세기에 곱하는 배율 범위 (0.05~1). 작을수록 빨리 약해진다.
    public float decayMin = 0.70f;
    public float decayMax = 0.92f;
    /// iOS 전용. 이 길이(ms) 이하인 조각은 짧은 탁(transient)으로 낸다.
    public int transientMaxMs = 12;

    public void CopyFrom(CrunchSettings o)
    {
        fragments = o.fragments;
        seed = o.seed;
        autoIncrementSeed = o.autoIncrementSeed;
        fragmentMinMs = o.fragmentMinMs;
        fragmentMaxMs = o.fragmentMaxMs;
        gapMinMs = o.gapMinMs;
        gapMaxMs = o.gapMaxMs;
        jitterFloor = o.jitterFloor;
        decayMin = o.decayMin;
        decayMax = o.decayMax;
        transientMaxMs = o.transientMaxMs;
    }
}

public static class ScenarioHaptics
{
    /// <summary>
    /// 지금 쓰는 Crunch 값. BuildCrunch 가 읽는다 — 진동도, 그 timings 로 그리는
    /// 합성 소리(ScenarioAudio.CrunchClip)도 여기 한 군데를 본다.
    /// </summary>
    public static readonly CrunchSettings Crunch = new CrunchSettings();

    /// <summary>
    /// 한 입 베어 무는 느낌의 파형을 만든다.
    ///
    /// 조각 하나하나는 아주 짧은 탁이고, 사이 침묵과 세기가 들쭉날쭉하다.
    /// 전체적으로는 점점 약해진다 — 처음 깨질 때가 가장 세다.
    ///
    /// seed 를 적어 두면 같은 자극을 나중에 다시 만들 수 있다. 파일럿에서 고른 자극을
    /// 본 실험에서 그대로 재현해야 하므로, 고정 난수를 쓰고 seed 를 기록한다.
    /// </summary>
    /// <param name="fragments">깨지는 조각 수. 과자 4~6, 사과 2~3, 얼음 1~2 정도</param>
    /// <param name="peak">첫 조각의 세기 1~255</param>
    /// <param name="seed">난수 씨앗. 같은 값이면 같은 파형</param>
    public static void BuildCrunch(int fragments, int peak, int seed,
                                   out long[] timings, out int[] amplitudes)
    {
        fragments = Mathf.Clamp(fragments, 1, 12);
        peak = Mathf.Clamp(peak, 1, 255);

        // 조각 길이·침묵·흔들림·감쇠는 설정에서 온다 (Crunch). 기본값이면 예전에
        // 여기 박혀 있던 수와 같고, 난수를 뽑는 순서도 그대로라 같은 seed 는 같은 파형이다.
        CrunchSettings s = Crunch;
        int onMin = Mathf.Clamp(s.fragmentMinMs, 1, 200);
        int onMax = Mathf.Clamp(s.fragmentMaxMs, onMin, 200);
        int gapMin = Mathf.Clamp(s.gapMinMs, 1, 1000);
        int gapMax = Mathf.Clamp(s.gapMaxMs, gapMin, 1000);
        float jitterFloor = Mathf.Clamp01(s.jitterFloor);
        float decayMin = Mathf.Clamp(s.decayMin, 0.05f, 1f);
        float decayMax = Mathf.Clamp(s.decayMax, decayMin, 1f);
        // 기본값(0.70~0.92)에서는 예전 식의 0.22 를 글자 그대로 쓴다. 0.92f - 0.70f 는
        // 부동소수점에서 0.22f 와 끝자리가 달라, 적어 둔 seed 의 파형이 1 단계 어긋날 수 있다.
        float decaySpan = (decayMin == 0.70f && decayMax == 0.92f)
                        ? 0.22f : decayMax - decayMin;
        float jitterSpan = jitterFloor == 0.65f ? 0.35f : 1f - jitterFloor;   // 같은 이유

        System.Random rng = new System.Random(seed);
        List<long> t = new List<long>();
        List<int> a = new List<int>();

        float level = 1f;
        for (int i = 0; i < fragments; i++)
        {
            // 조각 하나 — 4~11ms. 이 길이는 iOS 에서 transient(짧은 탁) 로 나간다.
            // continuous 로 내면 Taptic Engine 이 뜨는 시간 때문에 뭉개진다.
            long on = onMin + rng.Next(0, onMax - onMin + 1);
            float jitter = jitterFloor + (float)rng.NextDouble() * jitterSpan;
            int amp = Mathf.Clamp(Mathf.RoundToInt(peak * level * jitter), 1, 255);

            t.Add(on);
            a.Add(amp);

            if (i < fragments - 1)
            {
                // 조각 사이 침묵 — 15~65ms. 여기가 일정하면 리듬이 되어 씹는 느낌이 죽는다.
                //
                // 설정창의 **주기** 손잡이가 이 침묵을 늘이고 줄인다
                // (HapticPanel.GapScale). 씹는 소리도 이 timings 를 그대로
                // 읽어 만들므로(ScenarioAudio.CrunchClip) 소리와 진동이
                // 같이 늘어난다 — 여기 한 군데만 건드려야 하는 이유다.
                long gap = (long)Mathf.Round(
                    (gapMin + rng.Next(0, gapMax - gapMin + 1))
                    * Mathf.Max(0.1f, HapticPanel.GapScale));
                t.Add(System.Math.Max(5L, gap));
                a.Add(0);
            }

            level *= decayMin + (float)rng.NextDouble() * decaySpan;   // 약해지되 단조롭지 않게
        }

        timings = t.ToArray();
        amplitudes = a.ToArray();
    }

    /// <summary>
    /// 한 입을 재생한다. 짧은 조각이 살아나도록 transient 한계를 12ms 로 넘긴다.
    /// (기존 패턴 10가지는 이 값을 안 넘기므로 동작이 그대로다 — 조원이 평가해 둔 자극을
    ///  바꾸지 않기 위해서다.)
    /// </summary>
    public static void PlayCrunch(int fragments, int peak, int seed)
    {
        long[] timings;
        int[] amplitudes;
        BuildCrunch(fragments, peak, seed, out timings, out amplitudes);
        HapticManager.StopWaveform();
        HapticManager.PlayWaveform(timings, amplitudes,
            Mathf.Clamp(Crunch.transientMaxMs, 0, 200));
    }

    /// <summary>
    /// 바리깡 부하 딥 — 시간에 따른 세기 배율(0~1)을 돌려준다.
    ///
    /// 실제 바리깡은 머리카락이 두꺼운 데 걸리면 모터가 "웅—" 하고 처진다.
    /// 파라미터를 아무리 맞춰도 이 처짐이 없으면 그냥 전동칫솔처럼 느껴진다.
    ///
    /// 지속 진동을 돌리는 쪽에서 매 프레임 이 값을 곱해 SetContinuous 에 넣는다.
    /// </summary>
    /// <param name="seconds">버즈를 켠 뒤 흐른 시간</param>
    /// <param name="everySeconds">딥이 돌아오는 주기</param>
    /// <param name="dipSeconds">딥 한 번의 길이</param>
    /// <param name="depth">가장 깊을 때 남는 비율. 0.65 면 65% 까지 떨어진다</param>
    public static float ClipperLoad(float seconds, float everySeconds = 1.6f,
                                    float dipSeconds = 0.30f, float depth = 0.65f)
    {
        if (everySeconds <= 0f || dipSeconds <= 0f) return 1f;

        float phase = Mathf.Repeat(seconds, everySeconds);
        if (phase > dipSeconds) return 1f;

        // 딥 안에서 부드럽게 내려갔다 올라온다 — 갑자기 끊기면 고장난 느낌이 된다
        float u = phase / dipSeconds;                       // 0 → 1
        float shape = Mathf.Sin(u * Mathf.PI);              // 0 → 1 → 0
        return Mathf.Lerp(1f, Mathf.Clamp01(depth), shape);
    }
}

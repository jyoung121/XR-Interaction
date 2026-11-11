using UnityEngine;

/// <summary>
/// 시나리오별 소리를 만든다 — 치과 드릴 / 칫솔 모터 / 깨무는 소리.
///
/// **파일을 받아오지 않고 코드로 합성한다.** 이 저장소에는 오디오 파일이
/// 한 개도 없었고, 그대로 두는 편이 낫다. 방·사람·도구는 받아온 모델이지만
/// 소리는 사정이 다르다 — 쓸 만한 치과 드릴 녹음은 거의 다 출처·라이선스가
/// 불분명하고, 발표 자료에 출처를 적어야 하는 건 모델과 똑같다. 합성하면
/// 출처가 이 파일이고, 빌드도 안 무거워지고, 무엇보다 **값을 돌릴 수 있다.**
///
/// 진동 쪽(ScenarioHaptics)과 짝을 맞추는 게 이 파일의 핵심이다.
///
///  치과     : 닿아 있는 동안 이어지는 소리. 진동도 지속 진동이라 짝이 맞는다.
///             드릴은 5 kHz 쯤의 날카로운 휘파람, 칫솔은 260 Hz 쯤의 둔한
///             웅웅거림 — 둘 다 "무엇인지 아는" 소리라야 두 수준이 갈린다.
///
///  음식먹기 : 깨무는 순간 한 번. **진동과 같은 조각 묘사를 쓴다** —
///             ScenarioHaptics.BuildCrunch 가 뱉은 timings/amplitudes 를
///             그대로 받아 소리로 그린다. 조각이 터지는 시각과 세기가
///             글자 그대로 같은 값이라, 들리는 것과 느껴지는 것이 어긋날
///             수가 없다. 이 연구가 재려는 게 바로 그 어긋남이다.
///
/// **씹는 동안에도 소리가 난다.** 깨무는 순간의 터짐 뒤로, 오물거리는
/// 1~3 초 동안 턱이 닫힐 때마다 둔한 소리가 따라붙는다. 세기는
/// MirrorRig.MouthOpen 이 **닫히는 속도**에서 그대로 나온다 — 보이는
/// 턱이 빨리 닫힐수록 크다. 음식마다 오물거리는 속도와 폭이 다르니
/// (FoodSpec.rate/depth) 소리의 박자와 크기도 저절로 갈린다.
///
/// 한 번 조용하게 뒀다가 되돌린 자리다. 씹는 소리는 진동이 짝지을 것이
/// 없는 단서라 — 그 1~3 초 동안 진동은 안 울린다 — "진동이 맞았는가" 를
/// 묻는 자리에 "소리가 그럴듯했는가" 가 섞여 들 수 있다. 다만 소리가
/// 세 패턴에서 **똑같으므로** 패턴 사이의 비교는 여전히 진동 차이만
/// 남는다. 섞이는 건 조건 안의 절대 점수지 패턴 간 차이가 아니다.
/// 무음으로 되돌리려면 SetLoop 에 Chew 를 안 주면 된다.
///
/// 세 패턴(베이스라인 / 설계 / 맞춤화) 사이에서 소리는 **똑같다.** 소리는
/// 상황과 음식만 보고 정해지고 진동 패턴은 안 본다 — 그래야 세 패턴의
/// 차이가 진동의 차이로 남는다.
/// </summary>
public static class ScenarioAudio
{
    /// <summary>지금 이어서 낼 소리.</summary>
    public enum Loop { None, Drill, Brush, Chew }

    /// <summary>무음 조건을 만들 때 끈다.</summary>
    public static bool Enabled = true;

    // 24 kHz. 드릴에서 가장 높은 성분이 9 kHz 라 나이퀴스트가 12 kHz 면 넉넉하다.
    // 48 kHz 로 올려 봐야 들리는 건 같고 만드는 시간과 메모리만 두 배다.
    private const int Rate = 24000;

    // 이어지는 소리는 1 초를 만들어 돌려 쓴다.
    private const int LoopLen = Rate;

    private static AudioSource loopSrc;   // 드릴·칫솔
    private static AudioSource shotSrc;   // 깨무는 소리
    private static AudioClip drillClip, brushClip;
    private static Loop now = Loop.None;

    // 씹는 소리는 음식마다 다르다. 다섯 가지뿐이라 밝기로 적바림해 둔다.
    private static readonly System.Collections.Generic.Dictionary<float, AudioClip>
        chewClips = new System.Collections.Generic.Dictionary<float, AudioClip>();
    private static float nowBright = -1f;

    // ───────────────────────── 붙이고 떼기 ─────────────────────────

    /// <summary>
    /// 머리에 스피커를 붙인다. AudioListener 는 Unity 가 카메라에 기본으로
    /// 달아 주므로 따로 만들지 않는다.
    ///
    /// 소리는 **방향 없이** 낸다(spatialBlend 0). 드릴은 내 입 안에 있고
    /// 씹는 소리는 내 턱뼈를 타고 오는 것이라, 어느 쪽에서 들리는지가
    /// 없는 게 맞다. 3D 로 두면 고개를 돌릴 때마다 좌우가 바뀌어서
    /// 소리가 방 어딘가에 놓인 물건처럼 들린다.
    /// </summary>
    public static void Attach(Transform head)
    {
        if (loopSrc != null) return;

        GameObject go = new GameObject("ScenarioAudio");
        if (head != null) go.transform.SetParent(head, false);

        loopSrc = go.AddComponent<AudioSource>();
        loopSrc.playOnAwake = false;
        loopSrc.loop = true;
        loopSrc.spatialBlend = 0f;
        loopSrc.volume = 0f;

        shotSrc = go.AddComponent<AudioSource>();
        shotSrc.playOnAwake = false;
        shotSrc.loop = false;
        shotSrc.spatialBlend = 0f;
    }

    public static void Detach()
    {
        now = Loop.None;
        nowBright = -1f;
        if (loopSrc != null) Object.Destroy(loopSrc.gameObject);
        loopSrc = null;
        shotSrc = null;
    }

    // ───────────────────────── 재생 ─────────────────────────

    /// <summary>
    /// 이어지는 소리를 켜고 끈다. 매 프레임 불러도 된다 — 같은 소리를 다시
    /// 주면 세기만 바꾸고 재생은 안 끊는다.
    /// </summary>
    /// <param name="bright">Chew 일 때만 쓴다 — 음식 표의 FoodSpec.bright</param>
    public static void SetLoop(Loop kind, float volume, float bright = 0f)
    {
        if (loopSrc == null) return;
        if (!Enabled) kind = Loop.None;

        // 씹는 소리는 음식마다 클립이 다르므로 밝기가 바뀌어도 갈아 끼운다.
        bool swap = kind != now
                 || (kind == Loop.Chew && !Mathf.Approximately(bright, nowBright));
        if (swap)
        {
            now = kind;
            nowBright = bright;
            if (kind == Loop.None)
            {
                loopSrc.Stop();
            }
            else
            {
                loopSrc.clip = kind == Loop.Drill ? Drill
                             : kind == Loop.Brush ? Brush
                             : ChewBed(bright);
                loopSrc.Play();
            }
        }
        loopSrc.volume = kind == Loop.None ? 0f : Mathf.Clamp01(volume);
    }

    /// <summary>
    /// 한 입 깨무는 소리. 진동과 **같은 조각 묘사**로 그린다.
    ///
    /// fragments/peak/seed 를 ScenarioHaptics.PlayCrunch 와 똑같이 넘기면
    /// 터지는 시각과 세기가 글자 그대로 같은 값이 된다.
    /// </summary>
    /// <param name="bright">0 이면 둔한 퍽, 1 이면 유리 깨지는 쨍 (MirrorRig 의 음식 표)</param>
    public static void PlayCrunch(int fragments, int peak, int seed, float bright)
    {
        if (!Enabled || shotSrc == null) return;
        AudioClip c = CrunchClip(fragments, peak, seed, bright);
        if (c != null) shotSrc.PlayOneShot(c);
    }

    // ───────────────────────── 만들기 ─────────────────────────

    public static AudioClip Drill
    {
        get
        {
            if (drillClip == null) drillClip = BuildDrill();
            return drillClip;
        }
    }

    public static AudioClip Brush
    {
        get
        {
            if (brushClip == null) brushClip = BuildBrush();
            return brushClip;
        }
    }

    /// <summary>
    /// 치과 드릴 — 에어터빈이 내는 날카로운 휘파람 + 공기 새는 쉭.
    ///
    /// 휘파람만 넣으면 전자음이 되고 쉭만 넣으면 수도꼭지가 된다. 둘이
    /// 같이 있어야 드릴로 들린다.
    /// </summary>
    private static AudioClip BuildDrill()
    {
        float[] b = new float[LoopLen];
        System.Random rng = new System.Random(4700);

        // 휘파람. 4700 Hz 를 기둥으로 두고 배음과 아래음을 얹는다.
        // 아래음(2350)이 없으면 소리가 가늘어서 "삐—" 하는 경보음이 된다.
        AddSine(b, 4700, 0.50f, 0f);
        AddSine(b, 9400, 0.16f, 0.3f);
        AddSine(b, 2350, 0.13f, 0.7f);

        // 모터는 완벽하게 고르지 않다. 7 Hz 로 아주 조금 흔들어야 기계가
        // **돌고 있는** 것으로 들린다. 1 초에 7 바퀴라 이음매에서도 맞는다.
        AddSine(b, 4707, 0.10f, 0.2f);

        // 공기 새는 쉭. 2.5~9 kHz.
        AddNoise(b, 2500, 9000, 150, 0.40f, rng);

        return Clip("DentalDrill", b);
    }

    /// <summary>
    /// 칫솔 — 둔한 모터 웅웅거림 + 솔이 이를 긁는 소리.
    ///
    /// 드릴과 **같은 축에서 반대쪽**이어야 두 수준이 갈린다. 드릴이
    /// 높고 날카로우면 칫솔은 낮고 둔하다.
    /// </summary>
    private static AudioClip BuildBrush()
    {
        float[] b = new float[LoopLen];
        System.Random rng = new System.Random(260);

        // 260 Hz 모터. 배음을 1/n 로 쌓으면 사인보다 거칠어져서 플라스틱
        // 통 안에서 도는 모터처럼 들린다.
        for (int h = 1; h <= 6; h++)
            AddSine(b, 260 * h, 0.42f / h, h * 0.17f);

        // 솔. 긁는 소리는 드릴의 쉭보다 낮고 성글다.
        AddNoise(b, 800, 5000, 120, 0.26f, rng);

        return Clip("Toothbrush", b);
    }

    /// <summary>
    /// 씹는 소리의 세기를 턱이 **닫히는 속도**에서 뽑는다.
    ///
    /// 세기만 여기서 정하고 박자는 안 정한다. 오물거리는 속도는 음식마다
    /// 다른데(FoodSpec.rate 5~14), 같은 식을 두 군데 적으면 한쪽만 고쳤을
    /// 때 입은 다물리는데 소리는 딴 데서 난다. 보이는 턱이 지금 어디
    /// 있는지만 받아 오면 어긋날 길이 없다.
    ///
    /// 입이 **열리는** 동안은 조용하다. 씹을 때 소리가 나는 건 무는 쪽이지
    /// 벌리는 쪽이 아니다. 올라갈 땐 곧바로 올리고 내려올 땐 천천히
    /// 떨어뜨린다 — 프레임마다 날값을 그대로 쓰면 세기가 톱니처럼 튀어서
    /// 지직거린다.
    /// </summary>
    /// <param name="level">지난 프레임의 세기</param>
    /// <param name="open">지금 입 벌림 (MirrorRig.MouthOpen)</param>
    /// <param name="prevOpen">지난 프레임의 입 벌림</param>
    public static float ChewLevel(float level, float open, float prevOpen, float dt)
    {
        dt = Mathf.Max(dt, 1e-4f);

        // 다섯 중 사과가 가장 빨리 닫힌다 — 초당 2.4 쯤(depth 0.20 ×
        // rate 12). 거기가 0.42 가 되게 맞췄다. 푸딩은 0.45 라 0.08 로,
        // 거의 안 들린다 — 혀로 뭉개는 것이니 그게 맞다.
        float closing = Mathf.Max(0f, (prevOpen - open) / dt);
        float want = Mathf.Clamp01(closing * 0.35f) * 0.5f;

        return Mathf.Max(want, level * Mathf.Exp(-25f * dt));
    }

    /// <summary>씹는 바탕 소리를 밖에서 들어 보려고 (AudioPreview).</summary>
    public static AudioClip ChewBedOf(float bright) { return ChewBed(bright); }

    /// <summary>
    /// 씹는 바탕 소리. 턱이 닫힐 때마다 세기를 올려 쓸 **고른 소리**다.
    ///
    /// 터짐을 미리 박아 두지 않는 게 요점이다. 오물거리는 박자는 음식마다
    /// 다르고(FoodSpec.rate 5~14) 클립에 박아 두면 보이는 턱과 어긋난다.
    /// 고른 바탕을 깔아 두고 **보이는 턱이 닫히는 속도로 세기를 주면**
    /// 박자가 저절로 맞는다 (ExperimentController.ApplyContactHaptics).
    ///
    /// 소리는 입 안에서 나는 것이라 둔하다. 깨무는 터짐이 2.6 kHz 위를
    /// 쓰는 데 비해 여기는 거의 다 1.5 kHz 아래다 — 볼과 턱뼈를 거쳐
    /// 들리는 소리에 높은 쪽이 남아 있으면 입 밖에서 나는 소리가 된다.
    /// </summary>
    private static AudioClip ChewBed(float bright)
    {
        AudioClip got;
        if (chewClips.TryGetValue(bright, out got) && got != null) return got;

        float[] b = new float[LoopLen];
        System.Random rng = new System.Random(911 + Mathf.RoundToInt(bright * 100f));

        // 살이 눌리는 둔한 소리. 밝은 음식일수록 바스락거리는 쪽이 는다.
        AddNoise(b, 120, 700, 90, 0.40f, rng);
        AddNoise(b, 700, 2200, 90, 0.14f + 0.26f * Mathf.Clamp01(bright), rng);

        // 턱뼈를 타고 오는 낮은 울림. 이게 없으면 종이 구기는 소리가 된다.
        AddSine(b, 95, 0.10f, 0f);
        AddSine(b, 143, 0.06f, 0.4f);

        got = Clip("Chew", b);
        chewClips[bright] = got;
        return got;
    }

    /// <summary>
    /// 한 입 깨무는 소리를 조각 묘사에서 그린다.
    ///
    /// 조각 하나는 아주 짧은 잡음 터짐이다. 빠르게 올라갔다 지수로 꺼진다 —
    /// 천천히 올라가면 깨지는 게 아니라 긁히는 소리가 된다.
    /// </summary>
    public static AudioClip CrunchClip(int fragments, int peak, int seed, float bright)
    {
        long[] timings;
        int[] amps;
        ScenarioHaptics.BuildCrunch(fragments, peak, seed, out timings, out amps);
        if (timings == null || timings.Length == 0) return null;

        long totalMs = 0;
        foreach (long t in timings) totalMs += t;
        // 마지막 조각의 꼬리가 잘리지 않게 뒤를 조금 남긴다.
        int n = Mathf.Max(1, (int)((totalMs + 120) * Rate / 1000));
        float[] b = new float[n];

        bright = Mathf.Clamp01(bright);
        System.Random rng = new System.Random(seed * 31 + 7);

        // 한 극 필터 두 개로 낮은 쪽과 높은 쪽을 갈라 섞는다. 이 소리는
        // 돌려 쓰지 않으니 이음매를 걱정할 필요가 없어, 정수 사인을 쌓는
        // 대신 흰 잡음을 걸러 쓰는 쪽이 싸다.
        //
        // 무른 것일수록 낮은 쪽만 남는다 — 푸딩은 퍽, 사탕은 쨍.
        float kLow = Coef(900f);
        float kHi = Coef(2600f);

        long atMs = 0;
        for (int i = 0; i < timings.Length; i++)
        {
            if (amps[i] > 0)
            {
                int from = (int)(atMs * Rate / 1000);
                // 터짐 하나의 길이 — 조각 길이에 꼬리를 붙인다. 밝을수록
                // 짧게 꺼진다. 유리가 깨지는 소리는 금방 끝난다.
                float tau = Mathf.Lerp(0.030f, 0.009f, bright);
                int len = Mathf.Min((int)((timings[i] + 90) * Rate / 1000), n - from);
                float gain = amps[i] / 255f;

                float lo = 0f, hi = 0f;
                for (int s = 0; s < len; s++)
                {
                    float x = (float)(rng.NextDouble() * 2.0 - 1.0);
                    lo += (x - lo) * kLow;              // 저역
                    hi += (x - hi) * kHi;
                    float band = x - hi;                 // 고역
                    float v = Mathf.Lerp(lo * 1.8f, band, bright);

                    float t = s / (float)Rate;
                    // 0.8 ms 만에 올라가고 지수로 꺼진다.
                    float up = Mathf.Min(1f, t / 0.0008f);
                    b[from + s] += v * gain * up * Mathf.Exp(-t / tau);
                }
            }
            atMs += timings[i];
        }

        return Clip("Crunch", b);
    }

    // ───────────────────────── 거드는 것들 ─────────────────────────

    private static void AddSine(float[] b, int hz, float gain, float phase)
    {
        float w = 2f * Mathf.PI * hz / Rate;
        float p = phase * 2f * Mathf.PI;
        for (int i = 0; i < b.Length; i++) b[i] += Mathf.Sin(i * w + p) * gain;
    }

    /// <summary>
    /// 이음매 없는 잡음.
    ///
    /// 흰 잡음을 1 초로 잘라 돌리면 이음매에서 파형이 뚝 끊겨 **1 초마다
    /// "틱" 소리**가 난다. 드릴처럼 몇 초씩 이어지는 소리에서는 바로
    /// 들린다. 그래서 잡음을 정수 Hz 사인의 더미로 만든다 — 정수 Hz 는
    /// 1 초에 딱 떨어지므로 꼬리와 머리의 위상이 저절로 맞는다.
    /// 위상만 제각각으로 흩어 주면 귀에는 잡음으로 들린다.
    /// </summary>
    private static void AddNoise(float[] b, int lo, int hi, int lines, float gain,
                                 System.Random rng)
    {
        float g = gain / Mathf.Sqrt(lines);
        for (int k = 0; k < lines; k++)
        {
            int hz = lo + rng.Next(0, hi - lo + 1);
            AddSine(b, hz, g, (float)rng.NextDouble());
        }
    }

    /// <summary>한 극 저역통과의 계수. 자를 주파수를 넣는다.</summary>
    private static float Coef(float hz)
    {
        return 1f - Mathf.Exp(-2f * Mathf.PI * hz / Rate);
    }

    /// <summary>
    /// 버퍼를 클립으로 굽는다. 넘치지 않게 가장 큰 값을 0.9 로 맞춘다 —
    /// 1 을 넘으면 Unity 가 잘라 버려서 지직거린다.
    /// </summary>
    private static AudioClip Clip(string name, float[] b)
    {
        float max = 0f;
        for (int i = 0; i < b.Length; i++) max = Mathf.Max(max, Mathf.Abs(b[i]));
        if (max > 1e-6f)
        {
            float k = 0.9f / max;
            for (int i = 0; i < b.Length; i++) b[i] *= k;
        }

        AudioClip c = AudioClip.Create(name, b.Length, 1, Rate, false);
        c.SetData(b, 0);
        return c;
    }
}

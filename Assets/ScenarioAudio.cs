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

    /// <summary>
    /// 칫솔질 왕복 횟수 (초당). 소리와 진동이 **같은 박자**로 가도록
    /// 양쪽이 이 값을 쓴다. 1 초 고리에 정수 번 들어가야 이어 붙인
    /// 자리에서 박자가 안 튄다.
    /// </summary>
    public const float StrokeHz = 3f;

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

        // 휘파람. 4700 Hz 를 기둥으로 두되 **정수배로만 쌓지 않는다.**
        // 배음만 올리면 터빈이 아니라 악기가 된다. 터빈은 날이 여럿이라
        // 딱 떨어지지 않는 성분이 같이 돈다.
        AddSine(b, 4700, 0.42f, 0f);
        AddSine(b, 7050, 0.12f, 0.3f);   // 1.5 배 — 배음이 아닌 성분
        AddSine(b, 9400, 0.10f, 0.6f);
        AddSine(b, 2350, 0.10f, 0.7f);   // 없으면 "삐—" 하는 경보음이 된다

        // 모터는 완벽하게 고르지 않다. 7 Hz 로 아주 조금 흔들어야 기계가
        // **돌고 있는** 것으로 들린다. 1 초에 7 바퀴라 이음매에서도 맞는다.
        AddSine(b, 4707, 0.09f, 0.2f);

        // 공기 새는 쉭. **여기가 주인공이다** — 휘파람만 크면 경보음이고,
        // 쉭이 같이 커야 드릴로 들린다.
        AddNoise(b, 2500, 9000, 0.58f, rng);
        AddNoise(b, 300, 1200, 0.12f, rng);   // 손잡이에서 도는 모터 몸통

        return Clip("DentalDrill", b);
    }

    /// <summary>
    /// 칫솔 — **쓱쓱**. 솔이 이를 왕복으로 쓸고 지나가는 소리.
    ///
    /// 전에는 230 Hz 모터 배음을 깔았다. 그게 전동칫솔 소리였다 —
    /// 모터를 작게 줄여도 음정이 있는 소리가 깔려 있으면 귀는 그걸
    /// 기계로 듣는다. 손으로 닦는 칫솔에는 **음정이 없다.** 그래서
    /// 사인을 한 줄도 안 쓴다. 전부 잡음이다.
    ///
    /// "쓱쓱" 을 만드는 건 음색이 아니라 **왕복**이다. 솔이 지나갈 때
    /// 커졌다가 방향을 바꾸며 잦아든다. 그 리듬을 |sin| 으로 씌운다 —
    /// 한 번의 |sin| 산이 한 번의 쓱이다.
    ///
    /// 초당 StrokeHz 번 쓴다. 1 초 고리에 정수 번 들어가므로 이어
    /// 붙여도 박자가 안 어긋난다. 진동도 같은 값을 쓴다
    /// (ExperimentController) — 소리는 쓱쓱인데 손은 딴 박자로 떨면
    /// 그 어긋남이 바로 "가짜" 로 읽힌다.
    /// </summary>
    private static AudioClip BuildBrush()
    {
        // 후보 여섯을 뽑아 놓고 고른 값이다 (BrushPicker).
        //
        //   왕복 3/s — 손으로 닦을 때 초당 2~3 번이고 3 이 보통 속도다.
        //              2 는 느긋해서 닦는 것보다 문지르는 것 같고,
        //              4 는 급해서 다시 기계로 들린다.
        //   1400~6000 — 1800 위만 남기면 "쓱" 이 아니라 "쉭" 이 된다.
        //              1200 까지 내리면 묵직한 대신 솔끝이 죽는다.
        //   바닥 0.10 — 지나갈 때와 돌아설 때 차이를 지금(0.16)보다
        //              벌려 왕복을 또렷하게 한다. 0.05 까지 내리면
        //              쓱쓱이 아니라 뚝뚝 끊어진다.
        return BrushVariant(StrokeHz, 1400, 6000, 0.62f, 0.27f, 0.10f);
    }

    /// <summary>
    /// 칫솔 소리를 값으로 뽑는다. **고르려고 열어 둔 문이다** — 쓱쓱은
    /// 말로 주고받기 어려워서, 몇 가지를 뽑아 들려주고 고른 값을
    /// BuildBrush 에 박는 식으로 맞춘다.
    /// </summary>
    /// <param name="strokeHz">초당 왕복 횟수. 1 초 고리에 정수 번이라야 한다.</param>
    /// <param name="lo">솔끝 사각거림의 아래 끝 (Hz). 높일수록 가늘고 날카롭다.</param>
    /// <param name="hi">그 위 끝 (Hz).</param>
    /// <param name="gain">사각거림의 크기.</param>
    /// <param name="body">잇몸에 눌리는 둔한 몸통(250~900 Hz)의 크기.</param>
    /// <param name="floor">
    /// 방향을 바꾸는 순간에 남는 소리. 0 으로 두면 쓱쓱이 아니라
    /// 뚝뚝 끊기는 소리가 된다 — 솔은 돌아설 때도 이에 닿아 있다.
    /// </param>
    public static AudioClip BrushVariant(float strokeHz, int lo, int hi,
                                         float gain, float body, float floor)
    {
        float[] b = new float[LoopLen];
        System.Random rng = new System.Random(260);

        AddNoise(b, lo, hi, gain, rng);        // 솔끝의 사각거림
        AddNoise(b, 250, 900, body, rng);      // 잇몸에 눌리는 둔한 몸통

        for (int i = 0; i < b.Length; i++)
        {
            float t = (float)i / Rate;
            float stroke = Mathf.Abs(Mathf.Sin(Mathf.PI * strokeHz * t));
            b[i] *= floor + (1f - floor) * stroke;
        }

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

        // 살이 눌리는 둔한 소리. **다섯 가지가 서로 다르게 들려야 한다** —
        // 밝기로 세게 갈라 둔다. 푸딩(0.08)은 낮은 쪽만 남아 퍽퍽하고,
        // 사탕(1.00)은 2 kHz 위까지 바스락거린다. 사과(0.70)·젤리(0.25)·
        // 고기(0.18)가 그 사이를 메운다.
        float br = Mathf.Clamp01(bright);
        AddNoise(b, 110, 600, 0.46f - 0.20f * br, rng);
        AddNoise(b, 600, 2000, 0.08f + 0.40f * br, rng);
        AddNoise(b, 2000, 6000, 0.26f * br * br, rng);   // 사탕만 쨍하다

        // 턱뼈를 타고 오는 낮은 울림. 이게 없으면 종이 구기는 소리가 된다.
        // 무른 것일수록 이게 주인공이다.
        AddSine(b, 95, 0.10f + 0.08f * (1f - br), 0f);
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
    /// 전에는 정수 Hz 사인 90~150 개를 쌓아 잡음을 **흉내 냈다.** 1 초에
    /// 딱 떨어지는 사인만 쓰면 꼬리와 머리의 위상이 저절로 맞아 이음매가
    /// 안 들린다는 게 근거였고, 그 근거 자체는 맞다. 문제는 사인 백 개가
    /// 잡음이 아니라 **빽빽한 화음**이라는 것이다. 드릴도 칫솔도 씹는
    /// 소리도 쇳소리가 섞여 전자음처럼 들렸다 — 11/15 에 폰에서 "소리가
    /// 다 이상하다" 고 한 게 이것이다.
    ///
    /// 이제 진짜 잡음을 만들고, 이음매는 **꼬리를 머리에 겹쳐 녹여서**
    /// 없앤다. 겹치는 구간은 서로 다른 잡음 둘이 섞이는 것인데, 잡음끼리
    /// 섞이면 귀에는 그냥 잡음이라 이음매가 안 생긴다.
    ///
    /// 통과 대역은 한 극 필터 둘로 만든다. 가파르지는 않지만 깨무는
    /// 소리가 이미 같은 방식을 쓰고 있고(CrunchClip), 이 소리들은 대역
    /// 모서리가 날카로울 이유가 없다.
    /// </summary>
    private static void AddNoise(float[] b, int lo, int hi, float gain,
                                 System.Random rng)
    {
        int n = b.Length;
        if (n < 16 || gain <= 0f) return;

        int fade = Mathf.Clamp(n / 8, 1, n / 2);
        int len = n + fade;

        float kHi = Coef(hi);
        float kLo = Coef(lo);
        float[] t = new float[len];
        float a = 0f, c = 0f;
        for (int i = 0; i < len; i++)
        {
            float x = (float)(rng.NextDouble() * 2.0 - 1.0);
            a += (x - a) * kHi;      // hi 아래만 남는다
            c += (a - c) * kLo;      // 그중 lo 아래를 덜어 낸다
            t[i] = a - c;
        }

        // 꼬리를 머리에 녹인다. 이걸 빼면 1 초마다 틱 소리가 난다.
        for (int i = 0; i < fade; i++)
        {
            float w = (float)i / fade;
            t[i] = t[i] * w + t[n + i] * (1f - w);
        }

        // 세기를 맞춘다. 한 극을 둘 거치면 진폭이 크게 줄어드는데 얼마나
        // 줄지는 lo·hi 에 달려 있어 미리 적어 둘 수가 없다. 재서 맞춘다.
        float rms = 0f;
        for (int i = 0; i < n; i++) rms += t[i] * t[i];
        rms = Mathf.Sqrt(rms / n);
        if (rms < 1e-9f) return;

        float k = gain / (rms * 3f);   // 잡음은 RMS 의 세 배쯤이 꼭대기다
        for (int i = 0; i < n; i++) b[i] += t[i] * k;
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

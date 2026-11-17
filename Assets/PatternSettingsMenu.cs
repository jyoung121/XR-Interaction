using System.Globalization;
using UnityEngine;

/// <summary>
/// 진동 값을 폰에서 직접 고치는 평면 화면.
///
/// 아이폰은 빌드 한 번에 30~40분이 걸린다. 값을 하나 바꿔 보려고 그 시간을 쓰는 대신,
/// 앱 안에서 숫자를 쳐 넣고 Preview 로 바로 느껴 보게 한다.
/// 조원이 안드로이드에 만들어 둔 ExperimentSettingsMenu 와 같은 생각이고,
/// 필드 이름만 이쪽 ExperimentController 에 맞췄다.
///
/// 이 화면이 떠 있는 동안에는 컨트롤러가 Cardboard XR 을 꺼 둔다. XR 모드에서는
/// 화면이 둘로 갈라져 글씨를 읽기 어렵고 터치·키보드가 제대로 먹지 않는다.
/// 그래서 여기서는 Api 트리거를 쓰지 않고 IMGUI 버튼만 쓴다 — XR 이 꺼져 있어도,
/// 다시 켜는 데 실패해도 버튼은 눌린다.
/// </summary>
public class PatternSettingsMenu : MonoBehaviour
{
    public bool Visible { get; set; }
    public bool Done { get; private set; }
    public bool RunPatternTest { get; private set; }
    public bool RunMirrorScene { get; private set; }

    private ExperimentController controller;
    private VibrationPattern selected = VibrationPattern.SinglePulse;
    private Vector2 scroll;
    private string error = "";

    private string envDuration, envSample, envModulation;
    private string stepMs, gapMs, highAmp, lowAmp, pulseMs, repeatMs;
    private string minInterval, maxInterval, near, far;
    private float sharpness;             // 0~1. iOS 전용 — 안드로이드는 무시한다

    // 시나리오 확인용 (파일럿). 바리깡·치과는 지속 진동이라 단발 Preview 로는 들을 수 없다.
    private bool buzzing;
    private bool loadDip;
    private float buzzSeconds;
    private float buzzSharpness;         // 버즈를 시작할 때 쓴 sharpness
    // Crunch (한 입 파형) 의 값 전부 — ScenarioHaptics.Crunch 를 고친다
    private string crFragments, crSeed, crOnMin, crOnMax, crGapMin, crGapMax;
    private string crJitter, crDecayMin, crDecayMax, crTransient;
    private bool crAutoSeed;

    // 사과 씬 (녹음한 소리에 맞춘 진동) — AppleSync.Settings 를 고친다
    private string apBite, apChew, apTap, apSpacing, apMinAmp, apOffset, apTransient, apVolume;
    private string apSharp;

    // 화면이 둘이다. 1 = 원래 화면(10개 패턴, 버즈, 탐색 실험), 0 = 사과 화면(사과 씬 + Crunch).
    // 한 화면에 다 있을 때는 사과에 쓰는 칸이 어디서 어디까지인지 알 수가 없었다.
    // 사과 화면은 원래 화면 아래쪽의 "Apple settings" 버튼으로 들어가고 "Back" 으로 나온다.
    private int tab = 1;
    private bool apUseSound;
    private int apMode;                  // 씬에 쓸 진동: 0 탭(기본), 1 Crunch, 2 패턴
    private AppleSync.HapticMode previewMode;   // 지금 미리듣는 쪽

    // 사과 소리 미리듣기. 거울 방 없이 이 화면에서 한 입 분량을 틀어 본다.
    private bool applePreview;
    private float appleClock;            // 한 입 주기 안의 시각 (s)
    private bool attachedAudio;          // 이 화면이 스피커를 붙였나 (나갈 때 뗀다)

    public void Initialize(ExperimentController experiment)
    {
        controller = experiment;
        RefreshFromController();
    }

    /// <summary>화면을 띄운다. 컨트롤러가 XR 을 끈 뒤에 부른다.</summary>
    public void Begin()
    {
        StopBuzz();
        StopApplePreview();
        RefreshFromController();
        Done = false;
        RunPatternTest = false;
        RunMirrorScene = false;
        Visible = true;
    }

    public void ShowError(string message)
    {
        error = message;
        Done = false;
        Visible = true;
    }

    public void RefreshFromController()
    {
        var ci = CultureInfo.InvariantCulture;
        selected = controller.previewPattern;
        envDuration = controller.envelopeDurationMs.ToString(ci);
        envSample = controller.envelopeSampleMs.ToString(ci);
        envModulation = controller.envelopeModulationHz.ToString(ci);
        stepMs = controller.waveStepMs.ToString(ci);
        gapMs = controller.waveGapMs.ToString(ci);
        highAmp = controller.waveHighAmplitude.ToString(ci);
        lowAmp = controller.waveLowAmplitude.ToString(ci);
        pulseMs = controller.pulseDurationMs.ToString(ci);
        repeatMs = ((int)controller.patternRepeatMs).ToString(ci);
        minInterval = ((int)controller.minIntervalMs).ToString(ci);
        maxInterval = ((int)controller.maxIntervalMs).ToString(ci);
        near = ((int)controller.intensityNear).ToString(ci);
        far = ((int)controller.intensityFar).ToString(ci);
        sharpness = Mathf.Clamp01(HapticManager.Sharpness);

        CrunchSettings c = ScenarioHaptics.Crunch;
        crFragments = c.fragments.ToString(ci);
        crSeed = c.seed.ToString(ci);
        crOnMin = c.fragmentMinMs.ToString(ci);
        crOnMax = c.fragmentMaxMs.ToString(ci);
        crGapMin = c.gapMinMs.ToString(ci);
        crGapMax = c.gapMaxMs.ToString(ci);
        crJitter = c.jitterFloor.ToString("0.###", ci);
        crDecayMin = c.decayMin.ToString("0.###", ci);
        crDecayMax = c.decayMax.ToString("0.###", ci);
        crTransient = c.transientMaxMs.ToString(ci);
        crAutoSeed = c.autoIncrementSeed;

        AppleSync.SettingsData ap = AppleSync.Settings;
        apBite = ap.biteGain.ToString("0.###", ci);
        apChew = ap.chewGain.ToString("0.###", ci);
        apTap = ap.tapMs.ToString(ci);
        apSpacing = ap.minSpacingMs.ToString(ci);
        apMinAmp = ap.minAmplitude.ToString(ci);
        apOffset = ap.hapticOffsetMs.ToString(ci);
        apTransient = ap.transientMaxMs.ToString(ci);
        apVolume = ap.volume.ToString("0.###", ci);
        apSharp = ap.sharpness.ToString("0.###", ci);
        apUseSound = ap.useRecordedSound;
        apMode = (int)ap.haptics;
        error = "";
    }

    /// <summary>
    /// 지속 버즈를 돌린다. 바리깡·치과는 끊기지 않는 진동이라 단발 Preview 로는
    /// 들어볼 수 없어서, 파일럿에서 기준 패턴을 정하려면 이게 있어야 한다.
    ///
    /// SetContinuous 는 첫 호출에만 sharpness 를 넘기고 그 뒤에는 세기만 갱신한다.
    /// 그래서 돌아가는 중에 sharpness 를 바꿔도 안 먹는다. 바리깡과 치과를 가르는 축이
    /// 바로 sharpness 이므로, 값이 바뀌면 재생기를 다시 띄워 준다.
    /// </summary>
    private void Update()
    {
        StepApplePreview();

        if (!buzzing) return;

        if (!Visible || controller == null) { StopBuzz(); return; }

        if (!Mathf.Approximately(buzzSharpness, sharpness))
        {
            HapticManager.StopContinuous();
            HapticManager.Sharpness = Mathf.Clamp01(sharpness);
            buzzSharpness = sharpness;
        }

        buzzSeconds += Time.deltaTime;

        int peak;
        if (!Read(highAmp, 1, 255, out peak)) peak = 200;

        float load = loadDip ? ScenarioHaptics.ClipperLoad(buzzSeconds) : 1f;
        HapticManager.SetContinuous(Mathf.Clamp(Mathf.RoundToInt(peak * load), 1, 255));
    }

    private void StartBuzz()
    {
        HapticManager.StopWaveform();
        HapticManager.Sharpness = Mathf.Clamp01(sharpness);
        buzzSharpness = sharpness;
        buzzSeconds = 0f;
        buzzing = true;
    }

    private void StopBuzz()
    {
        if (!buzzing) return;
        buzzing = false;
        buzzSeconds = 0f;
        HapticManager.StopContinuous();
    }

    private void OnDisable()
    {
        StopBuzz();
        StopApplePreview();
    }

    // ───────────────────── 사과 소리 미리듣기 ─────────────────────

    /// <summary>
    /// 사과 소리 한 입 분량(베어 물기 1번 + 씹기 11번, 약 13 초)을 진동과 같이 튼다.
    /// 진동은 mode 가 고른다 — 탭 / Crunch / 패턴.
    ///
    /// 거울 방에서는 장면 시계가 소리와 진동을 끌고 가는데, 여기에는 장면이 없으니
    /// 이 화면이 시계를 돌린다. 나머지는 거울 방과 같은 길이다 — 같은 소리 파일,
    /// 같은 AppleSync.NextEvent / PlayEvent.
    /// </summary>
    private void StartApplePreview(AppleSync.HapticMode mode)
    {
        StopBuzz();
        HapticManager.StopWaveform();
        HapticManager.Sharpness = Mathf.Clamp01(AppleSync.Settings.sharpness);
        previewMode = mode;

        // 스피커는 거울 방이 머리에 붙여 쓴다. 이 화면에서는 아무 데나 붙여도 된다 —
        // 방향 없는 소리라 어디에 있든 똑같이 들린다. 나갈 때 도로 뗀다.
        ScenarioAudio.Attach(null);
        attachedAudio = true;

        // 주기의 앞 2.4 초는 조용하다(접시에서 집어 오는 동안). 닿기 0.3 초 전부터 튼다.
        appleClock = Mathf.Max(0f, AppleCrunchData.TouchMs / 1000f - 0.3f);
        AppleSync.Reset();

        ScenarioAudio.SetLoop(ScenarioAudio.Loop.None, 0f);     // 처음부터 다시 틀게
        ScenarioAudio.SetLoop(ScenarioAudio.Loop.Apple, AppleSync.Settings.volume);
        ScenarioAudio.SyncLoop(appleClock);
        applePreview = true;
    }

    private void StopApplePreview()
    {
        if (applePreview)
        {
            applePreview = false;
            HapticManager.StopWaveform();
            ScenarioAudio.SetLoop(ScenarioAudio.Loop.None, 0f);
        }
        if (attachedAudio)
        {
            attachedAudio = false;
            ScenarioAudio.Detach();
        }
    }

    private void StepApplePreview()
    {
        if (!applePreview) return;
        if (!Visible || controller == null) { StopApplePreview(); return; }

        appleClock += Time.unscaledDeltaTime;
        if (appleClock >= AppleCrunchData.SegmentSeconds) { StopApplePreview(); return; }

        ScenarioAudio.SetLoop(ScenarioAudio.Loop.Apple, AppleSync.Settings.volume);
        ScenarioAudio.SyncLoop(appleClock);

        int ev;
        while (AppleSync.NextEvent(appleClock, out ev))
            AppleSync.PlayEvent(previewMode, ev, 1f, controller);
    }

    /// <summary>"소리에 맞춰 틀기" 버튼. 틀고 있으면 멈추는 버튼이 된다.</summary>
    private void PlayWithSoundButton(string label, AppleSync.HapticMode mode)
    {
        bool mine = applePreview && previewMode == mode;
        if (GUILayout.Button(mine ? "Stop" : label, GUILayout.Height(46)))
        {
            if (mine) StopApplePreview();
            else if (Apply()) StartApplePreview(mode);
        }
    }

    private void OnGUI()
    {
        if (!Visible || controller == null) return;

        Matrix4x4 oldMatrix = GUI.matrix;
        int oldLabel = GUI.skin.label.fontSize;
        int oldButton = GUI.skin.button.fontSize;
        int oldField = GUI.skin.textField.fontSize;

        float scale = Mathf.Max(0.5f, Mathf.Min(Screen.width / 960f, Screen.height / 540f));
        float width = Screen.width / scale;
        float height = Screen.height / scale;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.skin.label.fontSize = 17;
        GUI.skin.button.fontSize = 17;
        GUI.skin.textField.fontSize = 17;

        GUI.Box(new Rect(0, 0, width, height), "");
        GUILayout.BeginArea(new Rect(24, 14, width - 48, height - 28));

        // 어느 화면을 그릴지. 버튼을 누른 그 이벤트 안에서 그릴 칸 수가 달라지면
        // IMGUI 가 예외를 내므로, 이번 패스는 들어올 때의 값(showTab)으로만 그린다.
        int showTab = tab;
        GUILayout.Label(showTab == 0 ? "Apple settings  (apple scene + crunch)"
                                     : "Haptic pattern settings");

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));

        if (showTab == 0)
        {
            DrawAppleTab();
        }
        else
        {
        GUILayout.Label("Change a number, tap Preview, feel it. No rebuild needed.");
        GUILayout.Space(10);
        GUILayout.Label("Pattern to preview");
        GUILayout.BeginHorizontal();
        Choice("Pulse", VibrationPattern.SinglePulse);
        Choice("Ramp up/down", VibrationPattern.RampUpDown);
        Choice("Bounce", VibrationPattern.Bounce);
        Choice("Wobble", VibrationPattern.Wobble);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Choice("Sine", VibrationPattern.SineEnvelope);
        Choice("Triangle", VibrationPattern.TriangleEnvelope);
        Choice("Sawtooth", VibrationPattern.SawtoothEnvelope);
        Choice("Square", VibrationPattern.SquareEnvelope);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Choice("Exp. decay", VibrationPattern.ExponentialDecay);
        Choice("Damped oscillation", VibrationPattern.DampedOscillation);
        GUILayout.EndHorizontal();

        GUILayout.Space(12);
        GUILayout.Label("Envelope shapes (sine / triangle / sawtooth / square / decay / damped)");
        Field("Envelope duration (ms)  20-2000", ref envDuration);
        Field("Sample step (ms)  10-100", ref envSample);
        Field("Modulation (Hz)  0.5-50", ref envModulation);
        GUILayout.Label("Usable ceiling is 1000 / (2 x sample step): 25Hz at 20ms, 50Hz at 10ms.");
        GUILayout.Label("duration x Hz / 1000 = cycles.  200ms x 5Hz = 1 cycle (one swell).");
        GUILayout.Label("600ms x 15Hz = 9 cycles, which feels like a rhythm instead.");

        GUILayout.Space(12);
        GUILayout.Label("Step shapes (ramp / bounce / wobble)");
        Field("Wave segment (ms)  1-2000", ref stepMs);
        Field("Silent gap (ms)  1-2000", ref gapMs);

        GUILayout.Space(12);
        GUILayout.Label("Strength and timing");
        Field("Peak strength  1-255", ref highAmp);
        Field("Low strength  1-255", ref lowAmp);
        Field("Pulse duration (ms)  1-2000", ref pulseMs);
        Field("Preview repeat (ms)  100-5000", ref repeatMs);

        GUILayout.Space(12);
        GUILayout.Label("Sharpness  (iPhone only - Android ignores it)");
        GUILayout.BeginHorizontal();
        GUILayout.Label("Sharpness  " + sharpness.ToString("0.00"), GUILayout.Width(170));
        if (GUILayout.Button("-", GUILayout.Width(48), GUILayout.Height(34)))
            sharpness = Mathf.Clamp01(Mathf.Round((sharpness - 0.05f) * 100f) / 100f);
        sharpness = GUILayout.HorizontalSlider(sharpness, 0f, 1f, GUILayout.Width(180));
        if (GUILayout.Button("+", GUILayout.Width(48), GUILayout.Height(34)))
            sharpness = Mathf.Clamp01(Mathf.Round((sharpness + 0.05f) * 100f) / 100f);
        GUILayout.EndHorizontal();
        GUILayout.Label("0 = round and organic, 1 = crisp and precise. Apple does NOT call this");
        GUILayout.Label("frequency, and it also changes how strong the buzz feels - so it is not");
        GUILayout.Label("independent of the strength boxes above. Peak output sits near 0.7, not 1.");
        GUILayout.Label("Report the number itself, not a Hz value.");

        GUILayout.Space(12);
        GUILayout.Label("Scenario check  (pilot)");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(buzzing ? "Stop buzz" : "Hold buzz  (clipper / dental)",
                             GUILayout.Height(44)))
        {
            if (buzzing) StopBuzz();
            else if (Apply()) { StopApplePreview(); StartBuzz(); }
        }
        if (GUILayout.Button(loadDip ? "[x] load dip" : "[ ] load dip",
                             GUILayout.Width(160), GUILayout.Height(44)))
            loadDip = !loadDip;
        GUILayout.EndHorizontal();

        GUILayout.Label("Buzz uses Peak strength + Sharpness, and keeps running until you stop it.");
        GUILayout.Label("Clipper: sharpness 0.10-0.25, load dip ON.   Dental: sharpness 1.0, dip OFF.");

        GUILayout.Space(12);
        GUILayout.Label("Experiment (case 2 interval, case 3 strength)");
        Field("Minimum interval (ms)  1-10000", ref minInterval);
        Field("Maximum interval (ms)  1-10000", ref maxInterval);
        Field("Strength near target  1-255", ref near);
        Field("Strength far from target  1-255", ref far);

        GUILayout.Space(12);
        GUILayout.Label("Haptics: " + HapticManager.Diagnostics);
        }   // showTab == 1

        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(error)) GUILayout.Label(error);

        if (showTab == 1 &&
            GUILayout.Button("Preview this pattern", GUILayout.Height(44))) Preview();

        // 사과 화면으로 들어가는 버튼 / 거기서 돌아오는 버튼.
        // 돌아올 때는 값을 저장한다 — 틀린 값이 있으면 그 화면에 남아 고치게 한다.
        if (showTab == 1)
        {
            if (GUILayout.Button("Apple settings  (apple scene + crunch)", GUILayout.Height(48)))
            {
                StopBuzz();
                tab = 0;
                scroll = Vector2.zero;
            }
        }
        else
        {
            if (GUILayout.Button("Back", GUILayout.Height(48)))
            {
                if (Apply())
                {
                    StopApplePreview();
                    tab = 1;
                    scroll = Vector2.zero;
                }
            }
        }

        if (GUILayout.Button("Mirror scene  (apple / dental)", GUILayout.Height(48)))
        {
            if (Apply())
            {
                StopBuzz();
                StopApplePreview();
                RunMirrorScene = true;
                RunPatternTest = false;
                Done = true;
            }
        }

        // "Start experiment" 는 11/15 에 뺐다. 그 버튼은 옛 빨간 네모
        // 탐색 과제로 가는 길이었는데, 본 실험은 거울 방에서의 진동
        // 경험이다. 남겨 두면 실수로 눌러 엉뚱한 과제가 돌아간다.
        if (showTab == 1 && GUILayout.Button("Compare all 10", GUILayout.Height(48)))
        {
            if (Apply())
            {
                StopBuzz();
                StopApplePreview();
                RunPatternTest = true; RunMirrorScene = false; Done = true;
            }
        }

        GUILayout.EndArea();

        GUI.skin.label.fontSize = oldLabel;
        GUI.skin.button.fontSize = oldButton;
        GUI.skin.textField.fontSize = oldField;
        GUI.matrix = oldMatrix;
    }

    // ───────────────────────── 사과 화면 ─────────────────────────

    /// <summary>
    /// 사과에 쓰는 칸만 모은 화면. 굵은 띠로 갈린 네 구역이다.
    ///
    ///   COMMON             전체 세기, 소리 크기, 날카로움
    ///   1. PATTERN         10개 패턴 중 하나를 골라 값을 맞추고, 사과 소리에 맞춰 들어 본다
    ///   2. CRUNCH          Crunch 파형의 값을 맞추고, 사과 소리에 맞춰 들어 본다
    ///   3. APPLY TO SCENE  거울 방에 쓸 진동을 셋 중에서 고른다
    /// </summary>
    private void DrawAppleTab()
    {
        // 고른 패턴에 따라 아래에 그릴 칸이 달라진다. 버튼을 누른 그 이벤트에서
        // 칸 수가 바뀌면 IMGUI 가 예외를 내므로 들어올 때의 값으로만 그린다.
        VibrationPattern shown = selected;

        Header("COMMON  -  applies to everything on this screen");
        Field("Peak strength  1-255", ref highAmp);
        Field("Sound volume  0-1", ref apVolume);
        Field("Sharpness (iOS)  0-1", ref apSharp);
        GUILayout.Label("Sharpness: 0 = round and soft, 1 = crisp. It also changes how strong it feels.");
        GUILayout.Label(ScenarioAudio.HasApple
            ? "Apple sound file: loaded"
            : "Apple sound file: NOT FOUND - nothing on this screen can play with sound");

        // ── 1 ──
        Header("1. PATTERN  -  pick a pattern, set its values, hear it with the apple sound");
        GUILayout.BeginHorizontal();
        Choice("Pulse", VibrationPattern.SinglePulse);
        Choice("Ramp up/down", VibrationPattern.RampUpDown);
        Choice("Bounce", VibrationPattern.Bounce);
        Choice("Wobble", VibrationPattern.Wobble);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Choice("Sine", VibrationPattern.SineEnvelope);
        Choice("Triangle", VibrationPattern.TriangleEnvelope);
        Choice("Sawtooth", VibrationPattern.SawtoothEnvelope);
        Choice("Square", VibrationPattern.SquareEnvelope);
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Choice("Exp. decay", VibrationPattern.ExponentialDecay);
        Choice("Damped oscillation", VibrationPattern.DampedOscillation);
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        GUILayout.Label("Timing is fixed to the sound: the pattern is stretched over each chew sound");
        GUILayout.Label("(150-735 ms), so there is no duration, gap or Hz to set here.");
        // 고른 패턴에 쓰이는 값만. Ramp·Bounce·Wobble 만 낮은 세기를 쓴다.
        if (shown == VibrationPattern.RampUpDown ||
            shown == VibrationPattern.Bounce ||
            shown == VibrationPattern.Wobble)
        {
            Field("Low strength  1-255", ref lowAmp);
        }
        else
        {
            GUILayout.Label("This pattern has no value of its own. Its strength is Peak strength (COMMON).");
        }
        DrawTimeline(AppleSync.HapticMode.Pattern);
        PlayWithSoundButton("Play this pattern with the apple sound  (one bite, about 13 s)",
                            AppleSync.HapticMode.Pattern);
        GUILayout.Label("One pattern on the big bite and one on each of the 11 chews,");
        GUILayout.Label("strongest on the bite and fading toward the last chew.");

        // ── 2 ──
        Header("2. CRUNCH  -  set the bite waveform, hear it with the apple sound");
        DrawCrunchSection();

        // ── 3 ──
        Header("3. APPLY TO SCENE  -  which haptics the mirror scene uses");
        if (GUILayout.Button((apMode == 0 ? "[x]  " : "[  ]  ") +
                "1   Default (recommended): a tap on every crack in the sound",
                GUILayout.Height(46)))
            apMode = 0;
        if (GUILayout.Button((apMode == 1 ? "[x]  " : "[  ]  ") +
                "2   Crunch, as set in section 2",
                GUILayout.Height(46)))
            apMode = 1;
        if (GUILayout.Button((apMode == 2 ? "[x]  " : "[  ]  ") +
                "3   Pattern, as set in section 1:  " + shown,
                GUILayout.Height(46)))
            apMode = 2;
        GUILayout.Label("The chosen number (1, 2 or 3) is shown at the bottom of the mirror scene.");
        apUseSound = Toggle("Use the recorded apple sound in the mirror scene", apUseSound);
        GUILayout.Label("With the sound off, the scene falls back to the old synthetic crunch (shown as 2).");

        DrawOptionOneSection();

        GUILayout.Space(16);
        GUILayout.Label("Haptics: " + HapticManager.Diagnostics);
    }

    /// <summary>구역을 가르는 굵은 띠.</summary>
    private static void Header(string text)
    {
        GUILayout.Space(18);
        GUIStyle style = new GUIStyle(GUI.skin.box);
        style.fontSize = 19;
        style.fontStyle = FontStyle.Bold;
        style.alignment = TextAnchor.MiddleLeft;
        style.normal.textColor = new Color(1f, 0.82f, 0.35f);
        GUILayout.Box("  " + text, style, GUILayout.ExpandWidth(true), GUILayout.Height(40));
        GUILayout.Space(4);
    }

    // ───────────────────────── Crunch ─────────────────────────

    /// <summary>
    /// Crunch 의 값과 한 입 분량의 그림, 소리에 맞춰 들어 보는 버튼.
    ///
    /// **조각 사이 간격을 넣는 칸이 없다.** 탭이 나는 시각은 소리의 큰 파열 자리로
    /// 정해져 있다 (AppleSync.BuildSyncedCrunch). 고칠 수 있는 것은 몇 개를 낼지,
    /// 하나가 얼마나 긴지, 세기가 어떻게 흔들리고 약해지는지다.
    /// </summary>
    private void DrawCrunchSection()
    {
        GUILayout.Label("Timing is fixed to the sound: taps land on the loudest cracks of each chew,");
        GUILayout.Label("so there is no gap to set here. Strength follows the crunch shape below.");
        Field("Fragments (taps per chew)  1-12", ref crFragments);
        Field("Fragment min (ms)  1-200", ref crOnMin);
        Field("Fragment max (ms)  1-200", ref crOnMax);
        Field("Strength jitter floor  0-1", ref crJitter);
        Field("Decay per fragment min  0.05-1", ref crDecayMin);
        Field("Decay per fragment max  0.05-1", ref crDecayMax);
        Field("Seed  0-999999", ref crSeed);
        crAutoSeed = Toggle("Auto-increment seed on every chew", crAutoSeed);
        Field("Transient limit (ms, iOS)  0-200", ref crTransient);

        DrawTimeline(AppleSync.HapticMode.Crunch);
        PlayWithSoundButton("Play crunch with the apple sound  (one bite, about 13 s)",
                            AppleSync.HapticMode.Crunch);
        GUILayout.Label("Fragments = how many taps on the big bite. Chews get fewer as they fade.");
        GUILayout.Label("Jitter floor 1 = every tap at full level.  Decay 1 = no fade.");
        GUILayout.Label("Seed only changes the random strength wobble, never the timing.");
    }

    /// <summary>Crunch 입력칸을 읽어 into 에 채운다. 하나라도 범위를 벗어나면 false.</summary>
    private bool TryReadCrunch(CrunchSettings into)
    {
        int fragments, seed, onMin, onMax, gapMin, gapMax, transient;
        float jitter, decayMin, decayMax;

        if (!Read(crFragments, 1, 12, out fragments) ||
            !Read(crSeed, 0, 999999, out seed) ||
            !Read(crOnMin, 1, 200, out onMin) ||
            !Read(crOnMax, 1, 200, out onMax) || onMax < onMin ||
            !Read(crGapMin, 1, 1000, out gapMin) ||
            !Read(crGapMax, 1, 1000, out gapMax) || gapMax < gapMin ||
            !Read(crJitter, 0f, 1f, out jitter) ||
            !Read(crDecayMin, 0.05f, 1f, out decayMin) ||
            !Read(crDecayMax, 0.05f, 1f, out decayMax) || decayMax < decayMin ||
            !Read(crTransient, 0, 200, out transient))
            return false;

        into.fragments = fragments;
        into.seed = seed;
        into.autoIncrementSeed = crAutoSeed;
        into.fragmentMinMs = onMin;
        into.fragmentMaxMs = onMax;
        into.gapMinMs = gapMin;
        into.gapMaxMs = gapMax;
        into.jitterFloor = jitter;
        into.decayMin = decayMin;
        into.decayMax = decayMax;
        into.transientMaxMs = transient;
        return true;
    }

    // ───────────────────────── 옵션 1 (탭) ─────────────────────────

    /// <summary>
    /// 옵션 1 — 소리의 잔 파열마다 탭을 내는 쪽 — 의 세부 값과 시간표 그림.
    /// 기본값으로도 쓸 수 있게 맞춰 뒀으므로 3 번 구역 맨 아래에 둔다.
    /// </summary>
    private void DrawOptionOneSection()
    {
        GUILayout.Space(14);
        GUILayout.Label("Fine tuning for option 1  (the defaults already work)");
        Field("Big bite gain  0-2", ref apBite);
        Field("Small chews gain  0-4", ref apChew);
        Field("Tap length (ms)  1-100", ref apTap);
        Field("Merge taps closer than (ms)  0-200", ref apSpacing);
        Field("Drop taps weaker than  0-255", ref apMinAmp);
        Field("Transient limit (ms, iOS)  0-200", ref apTransient);
        Field("Haptic offset (ms)  -500..500", ref apOffset);

        DrawTimeline(AppleSync.HapticMode.Taps);

        PlayWithSoundButton("Play option 1 with the apple sound  (one bite, about 13 s)",
                            AppleSync.HapticMode.Taps);
        GUILayout.Label("Haptic offset moves ALL three options as a whole against the sound");
        GUILayout.Label("(+ later, - earlier). It does not change the spacing between vibrations.");
        GUILayout.Label("Merging only thins out taps that are too close; it never moves a tap.");
    }

    /// <summary>
    /// 한 입 주기(15.7 초)에 걸쳐 진동이 언제 얼마나 세게 나는지 막대로 그린다.
    /// 세 방식(탭 / Crunch / 패턴)이 같이 쓴다. 앞쪽 빈 자리는 사과를 집어 오는
    /// 동안이고, 첫 큰 덩어리가 베어 무는 소리, 나머지가 잘게 씹는 소리다.
    ///
    /// 아직 저장 안 한 입력칸의 값으로 그린다. 값이 맞든 틀리든 **칸 수는 같게** 낸다
    /// (제목 1 + 그림 1 + 설명 1) — 글자를 치는 이벤트 도중에 칸 수가 바뀌면 IMGUI 가
    /// 예외를 낸다.
    /// </summary>
    private void DrawTimeline(AppleSync.HapticMode mode)
    {
        AppleSync.SettingsData s = new AppleSync.SettingsData();
        CrunchSettings cr = new CrunchSettings();
        int peak = 0, low = 0;
        bool ok = TryReadApple(s) && TryReadCrunch(cr)
               && Read(highAmp, 1, 255, out peak) && Read(lowAmp, 1, 255, out low);

        GUILayout.Label(ok
            ? "One bite, 0 - " + AppleCrunchData.SegmentSeconds.ToString("0.0") +
              " s.  Bars = vibration (width = time, height = strength)"
            : "Timeline: fix the values on this screen to see it.");

        Rect area = GUILayoutUtility.GetRect(10f, 90f, GUILayout.ExpandWidth(true));
        if (ok && Event.current.type == EventType.Repaint)
        {
            Color oldColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.9f, 0.55f, 1f);

            float totalMs = AppleCrunchData.SegmentSeconds * 1000f;
            for (int e = 0; e < AppleCrunchData.EventStartMs.Length; e++)
            {
                long[] timings;
                int[] amplitudes;
                // Crunch 의 seed 는 미리듣기와 같은 순서로 준다 (이벤트마다 +1)
                AppleSync.BuildModeEvent(mode, e, 1f, s, cr,
                    cr.autoIncrementSeed ? cr.seed + e : cr.seed,
                    selected, peak, low, out timings, out amplitudes);

                float ms = AppleCrunchData.EventStartMs[e];
                for (int i = 0; i < timings.Length; i++)
                {
                    if (amplitudes[i] > 0)
                    {
                        float x = area.x + area.width * ms / totalMs;
                        float w = Mathf.Max(area.width * timings[i] / totalMs, 1.5f);
                        float h = area.height * amplitudes[i] / 255f;
                        GUI.DrawTexture(new Rect(x, area.yMax - h, w, h), Texture2D.whiteTexture);
                    }
                    ms += timings[i];
                }
            }
            GUI.color = oldColor;
        }

        GUILayout.Label("Empty space on the left = reaching for the apple. Big block = the bite.");
    }

    /// <summary>사과 입력칸을 읽어 into 에 채운다. 하나라도 범위를 벗어나면 false.</summary>
    private bool TryReadApple(AppleSync.SettingsData into)
    {
        float bite, chew, volume, sharp;
        int tap, spacing, minAmp, offset, transient;

        if (!Read(apBite, 0f, 2f, out bite) ||
            !Read(apChew, 0f, 4f, out chew) ||
            !Read(apTap, 1, 100, out tap) ||
            !Read(apSpacing, 0, 200, out spacing) ||
            !Read(apMinAmp, 0, 255, out minAmp) ||
            !Read(apOffset, -500, 500, out offset) ||
            !Read(apTransient, 0, 200, out transient) ||
            !Read(apVolume, 0f, 1f, out volume) ||
            !Read(apSharp, 0f, 1f, out sharp))
            return false;

        into.useRecordedSound = apUseSound;
        into.haptics = apMode == 2 ? AppleSync.HapticMode.Pattern
                     : apMode == 1 ? AppleSync.HapticMode.Crunch
                     : AppleSync.HapticMode.Taps;
        into.biteGain = bite;
        into.chewGain = chew;
        into.tapMs = tap;
        into.minSpacingMs = spacing;
        into.minAmplitude = minAmp;
        into.hapticOffsetMs = offset;
        into.transientMaxMs = transient;
        into.volume = volume;
        into.sharpness = sharp;
        return true;
    }

    private static bool Toggle(string label, bool value)
    {
        if (GUILayout.Button((value ? "[x] " : "[ ] ") + label,
                GUILayout.Width(640), GUILayout.Height(38)))
            return !value;
        return value;
    }

    private void Choice(string label, VibrationPattern value)
    {
        if (GUILayout.Button((selected == value ? "[x] " : "[ ] ") + label, GUILayout.Height(42)))
            selected = value;
    }

    private static void Field(string label, ref string value)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(330));
        value = GUILayout.TextField(value, GUILayout.Width(150), GUILayout.Height(34));
        GUILayout.EndHorizontal();
    }

    /// <summary>입력한 값을 컨트롤러에 쓴다. 하나라도 범위를 벗어나면 아무것도 쓰지 않는다.</summary>
    private bool Apply()
    {
        int duration, sample, step, gap, high, low, pulse, repeat, lo, hi, n, f;
        float modulation;

        if (!Read(envDuration, 20, 2000, out duration) ||
            !Read(envSample, 10, 100, out sample) ||
            !Read(envModulation, 0.5f, 50f, out modulation) ||
            !Read(stepMs, 1, 2000, out step) ||
            !Read(gapMs, 1, 2000, out gap) ||
            !Read(highAmp, 1, 255, out high) ||
            !Read(lowAmp, 1, 255, out low) || low > high ||
            !Read(pulseMs, 1, 2000, out pulse) ||
            !Read(repeatMs, 100, 5000, out repeat) ||
            !Read(minInterval, 1, 10000, out lo) ||
            !Read(maxInterval, 1, 10000, out hi) || hi < lo ||
            !Read(near, 1, 255, out n) ||
            !Read(far, 1, 255, out f) || f > n)
        {
            error = "Check the ranges printed next to each box. " +
                    "Also: low <= peak, min <= max, far <= near.";
            return false;
        }

        CrunchSettings crunchValues = new CrunchSettings();
        if (!TryReadCrunch(crunchValues))
        {
            error = "Check the Crunch boxes. Each min must be <= its max.";
            return false;
        }

        AppleSync.SettingsData appleValues = new AppleSync.SettingsData();
        if (!TryReadApple(appleValues))
        {
            error = "Check the Apple scene boxes against the ranges printed next to them.";
            return false;
        }

        error = "";
        ScenarioHaptics.Crunch.CopyFrom(crunchValues);
        AppleSync.Settings.CopyFrom(appleValues);
        controller.previewPattern = selected;
        controller.envelopeDurationMs = duration;
        controller.envelopeSampleMs = sample;
        controller.envelopeModulationHz = modulation;
        controller.waveStepMs = step;
        controller.waveGapMs = gap;
        controller.waveHighAmplitude = high;
        controller.waveLowAmplitude = low;
        controller.pulseDurationMs = pulse;
        controller.patternRepeatMs = repeat;
        controller.minIntervalMs = lo;
        controller.maxIntervalMs = hi;
        controller.intensityNear = n;
        controller.intensityFar = f;

        // 슬라이더라 범위를 벗어날 수 없다. 패턴 재생 때 HapticManager 가 이 값을 읽는다
        // (ch_playWaveform / ch_transient / ch_beginContinuous 로 그대로 넘어간다).
        HapticManager.Sharpness = Mathf.Clamp01(sharpness);
        return true;
    }

    private void Preview()
    {
        if (!Apply()) return;

        StopApplePreview();
        HapticManager.StopWaveform();
        HapticManager.PlayPattern(selected, controller.pulseDurationMs,
            controller.waveStepMs, controller.waveGapMs,
            controller.waveHighAmplitude, controller.waveLowAmplitude, 255,
            controller.envelopeDurationMs, controller.envelopeSampleMs,
            controller.envelopeModulationHz);
    }

    private static bool Read(string value, int minimum, int maximum, out int number)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            && number >= minimum && number <= maximum;
    }

    private static bool Read(string value, float minimum, float maximum, out float number)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            && !float.IsNaN(number) && number >= minimum && number <= maximum;
    }
}

using System;
using UnityEngine;

public enum ScenarioPlaybackState
{
    Idle,
    Prepared,
    Scheduled,
    Playing,
    Finishing
}

[RequireComponent(typeof(AudioSource), typeof(LongHapticPlayer))]
public sealed class ScenarioPlaybackController : MonoBehaviour
{
    public event Action PlaybackCompleted;
    public event Action<ScenarioPlaybackState> PlaybackStateChanged;
    public event Action<string, string> ResearchEvent;
    private bool researchPlayback;
    private bool researchScenePrepared;
    private double researchEndSeconds = double.PositiveInfinity;
    public double ClockStartDsp => clockStartDsp;
    public bool ResearchFinishing { get; private set; }

    public bool BeginResearch(ScenarioProfile next, HapticProfile haptic)
    {
        var nextCondition = haptic == null ? ExperimentCondition.Baseline : ExperimentCondition.ResearcherHaptic;
        if (!researchScenePrepared || !IsPrepared || profile != next)
        {
            if (!PrepareResearch(next, nextCondition, haptic)) return false;
        }
        else if (!Configure(next, nextCondition, haptic)) return false;
        researchPlayback = true;
        researchEndSeconds = double.PositiveInfinity;
        ResearchFinishing = false;
        return StartPrepared();
    }

    // Apple continues the original synchronized timeline through the current iteration's rest.
    // Brush uses the original retreat animation and stops contact media immediately.
    public bool FinishResearch()
    {
        if (!researchPlayback || !IsPlaying || ResearchFinishing) return false;
        ResearchFinishing = true;
        ResearchEvent?.Invoke("finish_requested", ScenarioSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        if (scheduled && ScenarioSeconds < 0d)
        {
            ResearchEvent?.Invoke("start_cancelled", "before_animation_start");
            CompletePlayback();
            return true;
        }
        if (profile.scenario == ScenarioId.Apple)
        {
            researchEndSeconds = ResearchTimelineRules.AppleFinishSeconds(profile, Mathf.Max(0f, (float)ScenarioSeconds));
            return true;
        }
        StopMediaOnly();
        lastScenarioSeconds = Math.Max(0d, ScenarioSeconds);
        scheduled = false;
        finishBlock = profile.animationBlocks.Find(b => b.kind == AnimationBlockKind.Retreat);
        if (finishBlock == null) { CompletePlayback(); return true; }
        finishBlockOffsetSeconds = ScenarioAnimationTimeline.TrySample(profile, (float)lastScenarioSeconds, out var sample) &&
            ReferenceEquals(sample.block, finishBlock) ? sample.localSeconds : 0f;
        finishTransitionSeconds = Mathf.Max(profile.finishBlendSeconds, finishBlock.durationSeconds - finishBlockOffsetSeconds);
        finishStartDsp = AudioSettings.dspTime;
        finishBlockStarted = false;
        // Capture the currently displayed tool/mouth pose before advancing the retreat.
        if (!ReferenceEquals(sample.block, finishBlock)) animationDriver?.BeginRetreatTransition(profile);
        animationDriver?.EvaluateTimeline(profile, finishBlock.startSeconds + finishBlockOffsetSeconds);
        SetState(ScenarioPlaybackState.Finishing);
        return true;
    }

    public ScenarioProfile profile;
    public ExperimentCondition condition = ExperimentCondition.Baseline;
    public ScenarioAnimationDriver animationDriver;
    public bool playOnStart;
    [Min(0.05f)] public float scheduleLeadSeconds = 0.25f;
    [Header("Mobile test calibration (runtime only)")]
    public float runtimeHapticOffsetSeconds;
    [Range(0f, 2f)] public float runtimeAmplitudeScale = 1f;

    private AudioSource audioSource;
    private LongHapticPlayer hapticPlayer;
    private HapticProfile activeHapticOverride;
    private double clockStartDsp;
    private double finishStartDsp;
    private double lastScenarioSeconds;
    private bool scheduled;
    private bool animationStarted;
    private bool finishBlockStarted;
    private float finishBlockOffsetSeconds;
    private float finishTransitionSeconds;
    private ScenarioAnimationBlock finishBlock;
    private bool[] blockStarted = Array.Empty<bool>();
    private bool[] blockEnded = Array.Empty<bool>();
    private ScenarioPlaybackState playbackState;

    public bool IsPlaying => scheduled || playbackState == ScenarioPlaybackState.Finishing;
    public bool IsPrepared => playbackState == ScenarioPlaybackState.Prepared;
    public ScenarioPlaybackState State => playbackState;
    public double ScenarioSeconds => scheduled
        ? AudioSettings.dspTime - clockStartDsp
        : lastScenarioSeconds;
    public bool CanAcceptFinishInput => CanFinishAtCurrentTime(out _);

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();
        hapticPlayer = GetComponent<LongHapticPlayer>();
        if (animationDriver == null) animationDriver = GetComponent<ScenarioAnimationDriver>();
        audioSource.playOnAwake = false;
    }

    private void Start()
    {
        if (playOnStart && profile != null) Play(profile, condition);
    }

    public bool Play(ScenarioProfile nextProfile, ExperimentCondition nextCondition)
        => Play(nextProfile, nextCondition, null);

    public bool Play(ScenarioProfile nextProfile, ExperimentCondition nextCondition,
                     HapticProfile hapticOverride)
    {
        Stop();
        if (!Configure(nextProfile, nextCondition, hapticOverride)) return false;
        return SchedulePlayback();
    }

    public bool Prepare(ScenarioProfile nextProfile, ExperimentCondition nextCondition,
                        HapticProfile hapticOverride = null)
    {
        Stop();
        if (!Configure(nextProfile, nextCondition, hapticOverride)) return false;

        InitializeBlockState();
        animationDriver.BeginScenario(profile);
        animationDriver.EvaluateTimeline(profile, 0f);
        animationStarted = true;
        lastScenarioSeconds = 0d;
        SetState(ScenarioPlaybackState.Prepared);
        Debug.Log($"[ScenarioPlayer] Prepared interactive scenario {profile.ProfileId}.");
        return true;
    }

    // A research trial owns one world anchor and lighting setup across all ON/OFF episodes.
    public bool PrepareResearch(ScenarioProfile nextProfile, ExperimentCondition nextCondition,
                                HapticProfile hapticOverride = null)
    {
        bool reuse = researchScenePrepared && profile == nextProfile && animationStarted;
        if (!reuse) Stop();
        else
        {
            StopMediaOnly();
            researchPlayback = false;
            ResearchFinishing = false;
            researchEndSeconds = double.PositiveInfinity;
            scheduled = false;
            finishBlock = null;
            finishBlockStarted = false;
        }
        if (!Configure(nextProfile, nextCondition, hapticOverride)) return false;
        InitializeBlockState();
        animationDriver.BeginScenario(profile);
        animationDriver.EvaluateTimeline(profile, 0f);
        animationStarted = true;
        researchScenePrepared = true;
        lastScenarioSeconds = 0d;
        SetState(ScenarioPlaybackState.Prepared);
        return true;
    }

    public bool StartPrepared()
    {
        if (playbackState != ScenarioPlaybackState.Prepared || profile == null)
        {
            Debug.LogWarning("[ScenarioPlayer] No interactive scenario is prepared.");
            return false;
        }
        return SchedulePlayback();
    }

    public bool RequestFinish()
    {
        if (!CanFinishAtCurrentTime(out ScenarioBlockSample sample)) return false;

        lastScenarioSeconds = ScenarioSeconds;
        StopMediaOnly();
        scheduled = false;
        animationDriver?.EndBlock(sample.block);

        finishBlock = ResolveFinishBlock(sample);
        if (profile.finishMode == ScenarioFinishMode.StopImmediately || finishBlock == null)
        {
            CompletePlayback();
            return true;
        }

        finishBlockOffsetSeconds = ReferenceEquals(sample.block, finishBlock)
            ? Mathf.Clamp(sample.localSeconds, 0f, finishBlock.durationSeconds)
            : 0f;
        finishTransitionSeconds = Mathf.Max(profile.finishBlendSeconds, finishBlock.durationSeconds - finishBlockOffsetSeconds);
        finishStartDsp = AudioSettings.dspTime;
        finishBlockStarted = false;
        if (finishBlock.kind == AnimationBlockKind.Retreat && !ReferenceEquals(sample.block, finishBlock))
            animationDriver?.BeginRetreatTransition(profile);
        animationDriver?.EvaluateTimeline(profile, finishBlock.startSeconds + finishBlockOffsetSeconds);
        SetState(ScenarioPlaybackState.Finishing);
        Debug.Log($"[ScenarioPlayer] Finish requested at {lastScenarioSeconds:F3}s; " +
                  $"transition={finishBlock.blockId}.");
        return true;
    }

    public void Stop()
    {
        researchScenePrepared = false;
        researchPlayback = false;
        ResearchFinishing = false;
        researchEndSeconds = double.PositiveInfinity;
        scheduled = false;
        animationStarted = false;
        finishBlock = null;
        finishBlockStarted = false;
        lastScenarioSeconds = 0d;
        StopMediaOnly();
        if (animationDriver != null) animationDriver.StopScenario();
        SetState(ScenarioPlaybackState.Idle);
    }

    private bool Configure(ScenarioProfile nextProfile, ExperimentCondition nextCondition,
                           HapticProfile hapticOverride)
    {
        profile = nextProfile;
        condition = nextCondition;
        activeHapticOverride = nextCondition == ExperimentCondition.Baseline
            ? null : hapticOverride;

        if (profile == null)
        {
            Debug.LogError("[ScenarioPlayer] ScenarioProfile is missing.");
            return false;
        }
        if (animationDriver == null) animationDriver = GetComponent<ScenarioAnimationDriver>();
        if (animationDriver == null)
        {
            Debug.LogError("[ScenarioPlayer] ScenarioAnimationDriver is missing.");
            return false;
        }
        if (!profile.ValidateForPlayback(condition, activeHapticOverride, out string reason))
        {
            Debug.LogError("[ScenarioPlayer] " + reason);
            return false;
        }
        if (!ScenarioAnimationTimeline.Validate(profile, out reason))
        {
            Debug.LogError("[ScenarioPlayer] " + reason);
            return false;
        }
        if (!profile.ValidateInteraction(out reason))
        {
            Debug.LogError("[ScenarioPlayer] " + reason);
            return false;
        }
        return true;
    }

    private bool SchedulePlayback()
    {
        double earliest = Math.Min(0d, profile.soundOffsetSeconds);
        HapticProfile haptic = profile.ResolveHaptic(condition, activeHapticOverride);
        if (haptic != null)
        {
            earliest = Math.Min(earliest, profile.hapticOffsetSeconds +
                runtimeHapticOffsetSeconds + haptic.hapticOffsetMs / 1000d);
        }

        clockStartDsp = AudioSettings.dspTime + scheduleLeadSeconds - earliest;
        SoundProfile sound = profile.sound;
        audioSource.clip = sound.clip;
        audioSource.volume = sound.volume;
        audioSource.loop = sound.loop;
        audioSource.time = Mathf.Clamp(sound.trimStartSeconds, 0f,
            Mathf.Max(0f, sound.clip.length - 0.001f));
        audioSource.PlayScheduled(clockStartDsp + profile.soundOffsetSeconds);
        ResearchEvent?.Invoke("audio_scheduled", (clockStartDsp + profile.soundOffsetSeconds).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        if (haptic != null)
        {
            hapticPlayer.PlayProfile(haptic,
                clockStartDsp + profile.hapticOffsetSeconds + runtimeHapticOffsetSeconds,
                runtimeAmplitudeScale);
        }

        InitializeBlockState();
        lastScenarioSeconds = 0d;
        scheduled = true;
        SetState(ScenarioPlaybackState.Scheduled);
        return true;
    }

    private void InitializeBlockState()
    {
        int count = profile != null ? profile.animationBlocks?.Count ?? 0 : 0;
        blockStarted = new bool[count];
        blockEnded = new bool[count];
    }

    private void Update()
    {
        if (playbackState == ScenarioPlaybackState.Finishing)
        {
            UpdateFinishing();
            return;
        }
        if (!scheduled || profile == null) return;

        double seconds = ScenarioSeconds;
        lastScenarioSeconds = seconds;
        if (seconds < 0d) return; // Keep the prepared world visible during DSP scheduling.
        if (!animationStarted && seconds >= 0d)
        {
            animationStarted = true;
            animationDriver?.BeginScenario(profile);
        }
        if (!animationStarted) return;

        if (seconds >= 0d && playbackState == ScenarioPlaybackState.Scheduled)
            SetState(ScenarioPlaybackState.Playing);

        for (int i = 0; i < blockStarted.Length; i++)
        {
            var block = profile.animationBlocks[i];
            if (!blockStarted[i] && block.kind == AnimationBlockKind.Retreat && seconds >= block.startSeconds &&
                seconds < block.startSeconds + block.durationSeconds)
                animationDriver?.BeginRetreatTransition(profile);
        }
        animationDriver?.EvaluateTimeline(profile, (float)seconds);
        for (int i = 0; i < blockStarted.Length; i++)
        {
            ScenarioAnimationBlock block = profile.animationBlocks[i];
            if (!blockStarted[i] && seconds >= block.startSeconds)
            {
                blockStarted[i] = true;
                ResearchEvent?.Invoke("block_begin", block.blockId);
                animationDriver?.BeginBlock(block);
            }
            if (!blockEnded[i] && seconds >= block.startSeconds + block.durationSeconds)
            {
                blockEnded[i] = true;
                ResearchEvent?.Invoke("block_end", block.blockId);
                animationDriver?.EndBlock(block);
            }
        }

        if (researchPlayback && seconds >= researchEndSeconds)
        {
            CompletePlayback();
            return;
        }
        if (seconds >= profile.animationDurationSeconds)
        {
            if (researchPlayback) { CompletePlayback(); return; }
            bool repeat = profile.loop && !researchPlayback;
            ScenarioProfile completedProfile = profile;
            ExperimentCondition completedCondition = condition;
            HapticProfile completedHapticOverride = activeHapticOverride;
            Stop();
            if (repeat) Play(completedProfile, completedCondition, completedHapticOverride);
            else PlaybackCompleted?.Invoke();
        }
    }

    private void UpdateFinishing()
    {
        if (finishBlock == null)
        {
            CompletePlayback();
            return;
        }
        if (!finishBlockStarted)
        {
            finishBlockStarted = true;
            animationDriver?.BeginBlock(finishBlock);
        }

        float elapsed = (float)(AudioSettings.dspTime - finishStartDsp);
        float remaining = Mathf.Max(0f, finishBlock.durationSeconds - finishBlockOffsetSeconds);
        float local = Mathf.Min(finishBlockOffsetSeconds +
            remaining * Mathf.Clamp01(elapsed / Mathf.Max(.0001f, finishTransitionSeconds)),
            Mathf.Max(0f, finishBlock.durationSeconds - 0.0001f));
        animationDriver?.EvaluateTimeline(profile, finishBlock.startSeconds + local);
        if (researchPlayback) lastScenarioSeconds = finishBlock.startSeconds + local;
        if (elapsed < finishTransitionSeconds) return;

        animationDriver?.EndBlock(finishBlock);
        CompletePlayback();
    }

    private bool CanFinishAtCurrentTime(out ScenarioBlockSample sample)
    {
        sample = default;
        if (profile == null || !profile.RequiresUserStart ||
            playbackState != ScenarioPlaybackState.Playing)
            return false;
        if (!ScenarioAnimationTimeline.TrySample(profile, (float)ScenarioSeconds, out sample))
            return false;

        float firstContact = float.MaxValue;
        if (profile.animationBlocks != null)
        {
            foreach (ScenarioAnimationBlock block in profile.animationBlocks)
            {
                if (block != null && block.kind == AnimationBlockKind.Contact)
                    firstContact = Mathf.Min(firstContact, block.startSeconds);
            }
        }
        if (firstContact < float.MaxValue &&
            ScenarioSeconds < firstContact + profile.minimumActiveSeconds)
            return false;

        if (profile.ignoreStopDuringApproach &&
            (sample.block.kind == AnimationBlockKind.Setup ||
             sample.block.kind == AnimationBlockKind.Approach))
            return false;

        switch (profile.finishMode)
        {
            case ScenarioFinishMode.PlayRetreat:
                return sample.block.kind == AnimationBlockKind.Contact;
            case ScenarioFinishMode.PlayCurrentIterationRest:
                return sample.block.kind == AnimationBlockKind.Contact ||
                       sample.action == ScenarioAnimationAction.AppleRest;
            default:
                return true;
        }
    }

    private ScenarioAnimationBlock ResolveFinishBlock(ScenarioBlockSample sample)
    {
        if (profile?.animationBlocks == null) return null;
        foreach (ScenarioAnimationBlock block in profile.animationBlocks)
        {
            if (block == null) continue;
            if (profile.finishMode == ScenarioFinishMode.PlayRetreat &&
                block.kind == AnimationBlockKind.Retreat)
                return block;
            if (profile.finishMode == ScenarioFinishMode.PlayCurrentIterationRest &&
                block.action == ScenarioAnimationAction.AppleRest &&
                block.iteration == sample.block.iteration)
                return block;
        }
        return null;
    }

    private void CompletePlayback()
    {
        ResearchEvent?.Invoke("episode_completed", "");
        researchPlayback = false;
        ResearchFinishing = false;
        scheduled = false;
        animationStarted = researchScenePrepared;
        finishBlock = null;
        finishBlockStarted = false;
        StopMediaOnly();
        if (!researchScenePrepared) animationDriver?.StopScenario();
        SetState(ScenarioPlaybackState.Idle);
        PlaybackCompleted?.Invoke();
    }

    private void StopMediaOnly()
    {
        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (hapticPlayer == null) hapticPlayer = GetComponent<LongHapticPlayer>();
        audioSource.Stop();
        hapticPlayer.Stop();
    }

    private void SetState(ScenarioPlaybackState next)
    {
        if (playbackState == next) return;
        playbackState = next;
        PlaybackStateChanged?.Invoke(next);
    }

    private void OnDisable() => Stop();
}

using System;
using UnityEngine;

[RequireComponent(typeof(AudioSource), typeof(LongHapticPlayer))]
public sealed class ScenarioPlaybackController : MonoBehaviour
{
    public ScenarioProfile profile;
    public ExperimentCondition condition = ExperimentCondition.Baseline;
    public ScenarioAnimationDriver animationDriver;
    public bool playOnStart;
    [Min(0.05f)] public float scheduleLeadSeconds = 0.25f;
    [Header("Mobile test calibration (runtime only)")]
    public float runtimeHapticOffsetSeconds;
    [Range(0f, 2f)] public float runtimeAmplitudeScale = 1f;
    private AudioSource audioSource; private LongHapticPlayer hapticPlayer;
    private double clockStartDsp; private bool scheduled; private bool animationStarted;
    private bool[] blockStarted = Array.Empty<bool>(); private bool[] blockEnded = Array.Empty<bool>();
    public bool IsPlaying => scheduled;
    public double ScenarioSeconds => scheduled ? AudioSettings.dspTime - clockStartDsp : 0d;

    private void Awake() { audioSource = GetComponent<AudioSource>(); hapticPlayer = GetComponent<LongHapticPlayer>(); audioSource.playOnAwake = false; }
    private void Start() { if (playOnStart && profile != null) Play(profile, condition); }
    public bool Play(ScenarioProfile nextProfile, ExperimentCondition nextCondition)
    {
        Stop(); profile = nextProfile; condition = nextCondition;
        if (profile == null) { Debug.LogError("[ScenarioPlayer] ScenarioProfile is missing."); return false; }
        if (!profile.ValidateForPlayback(condition, out string reason)) { Debug.LogError("[ScenarioPlayer] " + reason); return false; }
        double earliest = Math.Min(0d, profile.soundOffsetSeconds); HapticProfile haptic = profile.ResolveHaptic(condition);
        if (haptic != null) earliest = Math.Min(earliest, profile.hapticOffsetSeconds +
            runtimeHapticOffsetSeconds + haptic.hapticOffsetMs / 1000d);
        clockStartDsp = AudioSettings.dspTime + scheduleLeadSeconds - earliest;
        SoundProfile sound = profile.sound; audioSource.clip = sound.clip; audioSource.volume = sound.volume;
        audioSource.loop = sound.loop || profile.loop; audioSource.time = Mathf.Clamp(sound.trimStartSeconds, 0f, Mathf.Max(0f, sound.clip.length - 0.001f));
        audioSource.PlayScheduled(clockStartDsp + profile.soundOffsetSeconds);
        if (haptic != null) hapticPlayer.PlayProfile(haptic,
            clockStartDsp + profile.hapticOffsetSeconds + runtimeHapticOffsetSeconds,
            runtimeAmplitudeScale);
        int count = profile.animationBlocks?.Count ?? 0; blockStarted = new bool[count]; blockEnded = new bool[count]; scheduled = true; return true;
    }
    public void Stop()
    {
        scheduled = false; animationStarted = false;
        if (audioSource == null) audioSource = GetComponent<AudioSource>(); if (hapticPlayer == null) hapticPlayer = GetComponent<LongHapticPlayer>();
        audioSource.Stop(); hapticPlayer.Stop(); if (animationDriver != null) animationDriver.StopScenario();
    }
    private void Update()
    {
        if (!scheduled || profile == null) return; double seconds = ScenarioSeconds;
        if (!animationStarted && seconds >= 0d) { animationStarted = true; if (animationDriver != null) animationDriver.BeginScenario(profile); }
        if (!animationStarted) return;
        for (int i = 0; i < blockStarted.Length; i++)
        {
            var b = profile.animationBlocks[i];
            if (!blockStarted[i] && seconds >= b.startSeconds) { blockStarted[i] = true; animationDriver?.BeginBlock(b); }
            if (!blockEnded[i] && seconds >= b.startSeconds + b.durationSeconds) { blockEnded[i] = true; animationDriver?.EndBlock(b); }
        }
        if (!profile.loop && seconds >= profile.animationDurationSeconds) Stop();
    }
    private void OnDisable() => Stop();
}

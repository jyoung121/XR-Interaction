using System;
using UnityEngine;

public enum HapticProfileOrigin { Manual, SoundBased, ResearcherCalibrated, Imported }

[CreateAssetMenu(fileName = "HapticProfile", menuName = "Experiment/Haptic Profile")]
public class HapticProfile : ScriptableObject
{
    [SerializeField] private string profileId = "haptic";
    [SerializeField] private int version = 1;
    [SerializeField] private HapticProfileOrigin origin;
    [SerializeField] private int[] durationsMs = Array.Empty<int>();
    [SerializeField] private int[] amplitudes = Array.Empty<int>();
    [Header("Synchronization")]
    public int hapticOffsetMs;
    [Range(0f, 2f)] public float defaultAmplitudeScale = 1f;
    public bool loop;
    [Header("Provenance")]
    public SoundProfile sourceSound;
    public string sourceAssetHash = string.Empty;
    public int converterVersion;
    [TextArea] public string calibrationNote;
    public AudioHapticConversionSettings conversionSettings;
    [Header("Source Summary")]
    public float sourceDurationSeconds;
    public int sourceSampleRate;
    public int sourceChannels;
    public int effectiveWindowMs;
    public int sourceWindowCount;
    public float silenceRatio;
    public float sourcePeak;
    public float sourceRms;

    public string ProfileId => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
    public int Version => Mathf.Max(1, version);
    public HapticProfileOrigin Origin => origin;
    public int[] DurationsMs => durationsMs;
    public int[] Amplitudes => amplitudes;
    public int SegmentCount => Mathf.Min(durationsMs?.Length ?? 0, amplitudes?.Length ?? 0);
    public long DurationMs { get { long total = 0; for (int i = 0; i < SegmentCount; i++) total += Mathf.Max(1, durationsMs[i]); return total; } }

    public long[] CreateTimings()
    {
        long[] result = new long[SegmentCount];
        for (int i = 0; i < result.Length; i++) result[i] = Mathf.Max(1, durationsMs[i]);
        return result;
    }

    public int[] CreateScaledAmplitudes(float additionalScale = 1f)
    {
        float scale = Mathf.Max(0f, defaultAmplitudeScale * additionalScale);
        int[] result = new int[SegmentCount];
        for (int i = 0; i < result.Length; i++) result[i] = Mathf.Clamp(Mathf.RoundToInt(amplitudes[i] * scale), 0, 255);
        return result;
    }

    public bool IsPlayable(out string reason)
    {
        if (SegmentCount <= 0 || durationsMs.Length != amplitudes.Length)
        { reason = "Timing/amplitude arrays are empty or mismatched."; return false; }
        for (int i = 0; i < SegmentCount; i++)
            if (durationsMs[i] < 1 || durationsMs[i] > 2000 || amplitudes[i] < 0 || amplitudes[i] > 255)
            { reason = $"Segment {i} is invalid."; return false; }
        reason = string.Empty;
        return true;
    }

    public void SetWaveform(int[] durations, int[] levels, HapticProfileOrigin nextOrigin,
                            string id = null, int nextVersion = 1)
    {
        if (durations == null || levels == null || durations.Length != levels.Length)
            throw new ArgumentException("Durations and amplitudes must have equal lengths.");
        durationsMs = new int[durations.Length];
        amplitudes = new int[levels.Length];
        for (int i = 0; i < durations.Length; i++)
        { durationsMs[i] = Mathf.Clamp(durations[i], 1, 2000); amplitudes[i] = Mathf.Clamp(levels[i], 0, 255); }
        origin = nextOrigin;
        if (!string.IsNullOrWhiteSpace(id)) profileId = id.Trim();
        version = Mathf.Max(1, nextVersion);
    }

    public void ApplyGenerated(SoundProfile sound, AudioHapticConversionSettings settings,
                               AudioHapticConversionResult result, int offsetMs, string assetHash)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        int[] durations = new int[result.TimingsMs.Length];
        for (int i = 0; i < durations.Length; i++) durations[i] = (int)Math.Max(1L, Math.Min(2000L, result.TimingsMs[i]));
        SetWaveform(durations, result.Amplitudes, HapticProfileOrigin.SoundBased, profileId, version);
        sourceSound = sound; sourceAssetHash = assetHash ?? string.Empty;
        converterVersion = AudioHapticConverter.ConverterVersion; hapticOffsetMs = offsetMs;
        conversionSettings = settings.Sanitized();
        AudioClip clip = sound != null ? sound.clip : null;
        sourceDurationSeconds = clip != null ? clip.length : 0f;
        sourceSampleRate = clip != null ? clip.frequency : 0; sourceChannels = clip != null ? clip.channels : 0;
        effectiveWindowMs = result.EffectiveWindowMs; sourceWindowCount = result.SourceWindowCount;
        silenceRatio = result.SilenceRatio; sourcePeak = result.SourcePeak; sourceRms = result.SourceRms;
    }
}

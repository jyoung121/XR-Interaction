using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct AudioHapticConversionSettings
{
    [Range(10, 200)] public int windowMs;
    [Tooltip("Retained for profile compatibility. Long profiles no longer expand the window to fit a transport cap.")]
    public bool autoFitWindow;
    [Range(0f, 1f)] public float rmsWeight;
    [Range(-80f, -10f)] public float gateDb;
    [Range(0.2f, 2f)] public float gamma;
    [Range(1, 254)] public int minimumAmplitude;
    [Range(2, 32)] public int quantizationLevels;
    [Range(0, 200)] public int attackMs;
    [Range(0, 500)] public int releaseMs;

    public static AudioHapticConversionSettings Default => new AudioHapticConversionSettings
    {
        windowMs = 25,
        autoFitWindow = false,
        rmsWeight = 0.8f,
        gateDb = -45f,
        gamma = 0.65f,
        minimumAmplitude = 40,
        quantizationLevels = 12,
        attackMs = 15,
        releaseMs = 60
    };

    public AudioHapticConversionSettings Sanitized()
    {
        AudioHapticConversionSettings value = this;
        value.windowMs = Mathf.Clamp(value.windowMs, 10, 200);
        value.rmsWeight = Mathf.Clamp01(value.rmsWeight);
        value.gateDb = Mathf.Clamp(value.gateDb, -80f, -10f);
        value.gamma = Mathf.Clamp(value.gamma, 0.2f, 2f);
        value.minimumAmplitude = Mathf.Clamp(value.minimumAmplitude, 1, 254);
        value.quantizationLevels = Mathf.Clamp(value.quantizationLevels, 2, 32);
        value.attackMs = Mathf.Clamp(value.attackMs, 0, 200);
        value.releaseMs = Mathf.Clamp(value.releaseMs, 0, 500);
        return value;
    }
}

public sealed class AudioHapticConversionResult
{
    public long[] TimingsMs { get; internal set; } = Array.Empty<long>();
    public int[] Amplitudes { get; internal set; } = Array.Empty<int>();
    public int EffectiveWindowMs { get; internal set; }
    public int SourceWindowCount { get; internal set; }
    public float SilenceRatio { get; internal set; }
    public float SourcePeak { get; internal set; }
    public float SourceRms { get; internal set; }
    public float NormalizationLevel { get; internal set; }
    public long DurationMs { get; internal set; }
    public long[] WindowDurationsMs { get; internal set; } = Array.Empty<long>();
    public float[] RawEnvelope { get; internal set; } = Array.Empty<float>();
    public float[] NormalizedEnvelope { get; internal set; } = Array.Empty<float>();
}

/// <summary>
/// Converts audio loudness to a quantized haptic envelope. AudioClip conversion reads one
/// analysis window at a time, so memory use is bounded and long scenario recordings are allowed.
/// </summary>
public static class AudioHapticConverter
{
    public const int ConverterVersion = 3;
    public const int MaxSegmentDurationMs = 2000;

    public static AudioHapticConversionResult Convert(
        AudioClip clip, AudioHapticConversionSettings settings)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        if (clip.samples <= 0 || clip.channels <= 0 || clip.frequency <= 0)
            throw new InvalidOperationException("The AudioClip has no readable sample data.");
        if (clip.loadState != AudioDataLoadState.Loaded && !clip.LoadAudioData())
            throw new InvalidOperationException("Unity could not load the AudioClip data.");

        settings = settings.Sanitized();
        int framesPerWindow = Mathf.Max(1,
            Mathf.RoundToInt(clip.frequency * settings.windowMs / 1000f));
        int windowCount = (clip.samples + framesPerWindow - 1) / framesPerWindow;
        float[] energies = new float[windowCount];
        long[] durations = new long[windowCount];
        double allSquares = 0d;
        long allValues = 0;
        float sourcePeak = 0f;

        for (int window = 0; window < windowCount; window++)
        {
            int startFrame = window * framesPerWindow;
            int frameCount = Mathf.Min(framesPerWindow, clip.samples - startFrame);
            float[] buffer = new float[frameCount * clip.channels];
            if (!clip.GetData(buffer, startFrame))
                throw new InvalidOperationException(
                    "AudioClip.GetData failed. Set Load Type to Decompress On Load and enable preload.");

            MeasureWindow(buffer, settings, out float energy, out float peak, out double squares);
            energies[window] = energy;
            sourcePeak = Mathf.Max(sourcePeak, peak);
            allSquares += squares;
            allValues += buffer.Length;
            long startMs = (long)Math.Round(startFrame * 1000d / clip.frequency);
            long endMs = (long)Math.Round((startFrame + frameCount) * 1000d / clip.frequency);
            durations[window] = Math.Max(1L, endMs - startMs);
        }

        return BuildResult(energies, durations, settings, sourcePeak, allSquares, allValues);
    }

    public static AudioHapticConversionResult ConvertSamples(
        float[] interleaved, int sampleFrames, int channels, int sampleRate,
        AudioHapticConversionSettings settings)
    {
        if (interleaved == null) throw new ArgumentNullException(nameof(interleaved));
        if (sampleFrames <= 0 || channels <= 0 || sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleFrames));
        if ((long)interleaved.Length < (long)sampleFrames * channels)
            throw new ArgumentException("The sample buffer is shorter than the declared clip shape.");

        settings = settings.Sanitized();
        int framesPerWindow = Mathf.Max(1,
            Mathf.RoundToInt(sampleRate * settings.windowMs / 1000f));
        int windowCount = (sampleFrames + framesPerWindow - 1) / framesPerWindow;
        float[] energies = new float[windowCount];
        long[] durations = new long[windowCount];
        double allSquares = 0d;
        long allValues = 0;
        float sourcePeak = 0f;

        for (int window = 0; window < windowCount; window++)
        {
            int startFrame = window * framesPerWindow;
            int endFrame = Mathf.Min(sampleFrames, startFrame + framesPerWindow);
            double squares = 0d;
            float peak = 0f;
            int values = 0;
            for (int frame = startFrame; frame < endFrame; frame++)
            {
                int baseIndex = frame * channels;
                for (int channel = 0; channel < channels; channel++)
                {
                    float sample = Mathf.Clamp(interleaved[baseIndex + channel], -1f, 1f);
                    squares += sample * sample;
                    peak = Mathf.Max(peak, Mathf.Abs(sample));
                    values++;
                }
            }
            float rms = values > 0 ? Mathf.Sqrt((float)(squares / values)) : 0f;
            energies[window] = settings.rmsWeight * rms + (1f - settings.rmsWeight) * peak;
            sourcePeak = Mathf.Max(sourcePeak, peak);
            allSquares += squares;
            allValues += values;
            long startMs = (long)Math.Round(startFrame * 1000d / sampleRate);
            long endMs = (long)Math.Round(endFrame * 1000d / sampleRate);
            durations[window] = Math.Max(1L, endMs - startMs);
        }

        return BuildResult(energies, durations, settings, sourcePeak, allSquares, allValues);
    }

    public static AudioHapticConversionResult BuildFromNormalizedEnvelope(
        AudioHapticConversionResult source, float[] normalizedEnvelope,
        AudioHapticConversionSettings settings)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (normalizedEnvelope == null) throw new ArgumentNullException(nameof(normalizedEnvelope));
        if (source.WindowDurationsMs == null ||
            source.WindowDurationsMs.Length != normalizedEnvelope.Length)
            throw new ArgumentException("Envelope and source window durations must have equal lengths.");

        settings = settings.Sanitized();
        BuildQuantized(normalizedEnvelope, source.WindowDurationsMs, settings,
            out long[] timings, out int[] amplitudes, out long total);
        int silent = 0;
        for (int i = 0; i < normalizedEnvelope.Length; i++)
            if (normalizedEnvelope[i] <= 0f) silent++;

        return new AudioHapticConversionResult
        {
            TimingsMs = timings,
            Amplitudes = amplitudes,
            EffectiveWindowMs = source.EffectiveWindowMs,
            SourceWindowCount = normalizedEnvelope.Length,
            SilenceRatio = normalizedEnvelope.Length > 0
                ? silent / (float)normalizedEnvelope.Length : 0f,
            SourcePeak = source.SourcePeak,
            SourceRms = source.SourceRms,
            NormalizationLevel = source.NormalizationLevel,
            DurationMs = total,
            WindowDurationsMs = (long[])source.WindowDurationsMs.Clone(),
            RawEnvelope = (float[])source.RawEnvelope.Clone(),
            NormalizedEnvelope = (float[])normalizedEnvelope.Clone()
        };
    }

    public static AudioHapticConversionResult CreateAnalysisSlice(
        AudioHapticConversionResult source, long[] durations, float[] raw,
        float[] normalized)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (durations == null || raw == null || normalized == null ||
            durations.Length != raw.Length || durations.Length != normalized.Length)
            throw new ArgumentException("Slice durations and envelopes must have equal lengths.");
        long total = 0;
        for (int i = 0; i < durations.Length; i++) total += Math.Max(1L, durations[i]);
        return new AudioHapticConversionResult
        {
            EffectiveWindowMs = source.EffectiveWindowMs,
            SourceWindowCount = durations.Length,
            SourcePeak = source.SourcePeak,
            SourceRms = source.SourceRms,
            NormalizationLevel = source.NormalizationLevel,
            DurationMs = total,
            WindowDurationsMs = (long[])durations.Clone(),
            RawEnvelope = (float[])raw.Clone(),
            NormalizedEnvelope = (float[])normalized.Clone()
        };
    }

    private static void MeasureWindow(float[] buffer, AudioHapticConversionSettings settings,
        out float energy, out float peak, out double squares)
    {
        squares = 0d;
        peak = 0f;
        for (int i = 0; i < buffer.Length; i++)
        {
            float sample = Mathf.Clamp(buffer[i], -1f, 1f);
            squares += sample * sample;
            peak = Mathf.Max(peak, Mathf.Abs(sample));
        }
        float rms = buffer.Length > 0 ? Mathf.Sqrt((float)(squares / buffer.Length)) : 0f;
        energy = settings.rmsWeight * rms + (1f - settings.rmsWeight) * peak;
    }

    private static AudioHapticConversionResult BuildResult(
        float[] energies, long[] windowDurations, AudioHapticConversionSettings settings,
        float sourcePeak, double allSquares, long allValues)
    {
        float gateLinear = Mathf.Pow(10f, settings.gateDb / 20f);
        List<float> active = new List<float>();
        int silent = 0;
        for (int i = 0; i < energies.Length; i++)
        {
            if (energies[i] > gateLinear) active.Add(energies[i]);
            else silent++;
        }
        if (active.Count == 0)
            throw new InvalidOperationException("No signal rose above the noise gate.");

        active.Sort();
        float normalization = active[Mathf.Clamp(
            Mathf.RoundToInt((active.Count - 1) * 0.95f), 0, active.Count - 1)];
        if (normalization <= gateLinear + 1e-7f)
            throw new InvalidOperationException("The audio has insufficient dynamic range.");

        float[] normalized = new float[energies.Length];
        for (int i = 0; i < energies.Length; i++)
        {
            float target = energies[i] <= gateLinear
                ? 0f
                : Mathf.Clamp01((energies[i] - gateLinear) / (normalization - gateLinear));
            normalized[i] = Mathf.Pow(target, settings.gamma);
        }

        BuildQuantized(normalized, windowDurations, settings,
            out long[] timings, out int[] amplitudes, out long total);
        return new AudioHapticConversionResult
        {
            TimingsMs = timings,
            Amplitudes = amplitudes,
            EffectiveWindowMs = settings.windowMs,
            SourceWindowCount = energies.Length,
            SilenceRatio = energies.Length > 0 ? silent / (float)energies.Length : 0f,
            SourcePeak = sourcePeak,
            SourceRms = allValues > 0 ? Mathf.Sqrt((float)(allSquares / allValues)) : 0f,
            NormalizationLevel = normalization,
            DurationMs = total,
            WindowDurationsMs = (long[])windowDurations.Clone(),
            RawEnvelope = (float[])energies.Clone(),
            NormalizedEnvelope = normalized
        };
    }

    private static void BuildQuantized(float[] normalized, long[] windowDurations,
        AudioHapticConversionSettings settings, out long[] timings, out int[] amplitudes,
        out long total)
    {
        var timingList = new List<long>();
        var amplitudeList = new List<int>();
        float smoothed = 0f;
        for (int i = 0; i < normalized.Length; i++)
        {
            float target = Mathf.Clamp01(normalized[i]);
            float tauMs = target > smoothed ? settings.attackMs : settings.releaseMs;
            float alpha = tauMs <= 0f ? 1f : 1f - Mathf.Exp(-windowDurations[i] / tauMs);
            smoothed += (target - smoothed) * alpha;
            if (target <= 0f && smoothed < 0.005f) smoothed = 0f;
            AppendSegment(timingList, amplitudeList, windowDurations[i],
                Quantize(smoothed, settings.minimumAmplitude, settings.quantizationLevels));
        }

        total = 0;
        for (int i = 0; i < timingList.Count; i++) total += timingList[i];
        timings = timingList.ToArray();
        amplitudes = amplitudeList.ToArray();
    }

    private static int Quantize(float normalized, int minimumAmplitude, int levels)
    {
        normalized = Mathf.Clamp01(normalized);
        if (normalized <= 0f) return 0;
        int step = Mathf.RoundToInt(normalized * (levels - 1));
        float quantized = step / (float)(levels - 1);
        return Mathf.Clamp(Mathf.RoundToInt(
            Mathf.Lerp(minimumAmplitude, 255f, quantized)), minimumAmplitude, 255);
    }

    private static void AppendSegment(
        List<long> timings, List<int> amplitudes, long durationMs, int amplitude)
    {
        long remaining = Math.Max(1L, durationMs);
        while (remaining > 0)
        {
            if (timings.Count > 0 && amplitudes[amplitudes.Count - 1] == amplitude &&
                timings[timings.Count - 1] < MaxSegmentDurationMs)
            {
                long add = Math.Min(MaxSegmentDurationMs - timings[timings.Count - 1], remaining);
                timings[timings.Count - 1] += add;
                remaining -= add;
                if (remaining <= 0) break;
            }
            long piece = Math.Min(MaxSegmentDurationMs, remaining);
            timings.Add(piece);
            amplitudes.Add(Mathf.Clamp(amplitude, 0, 255));
            remaining -= piece;
        }
    }
}

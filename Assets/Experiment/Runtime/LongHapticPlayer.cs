using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public readonly struct HapticChunk
{
    public readonly long[] durations; public readonly int[] amplitudes; public readonly long durationMs;
    public HapticChunk(long[] d, int[] a, long ms) { durations = d; amplitudes = a; durationMs = ms; }
}

public static class HapticWaveformChunker
{
    public const int DefaultMaxSegments = 256;
    public const int DefaultMaxDurationMs = 8000;
    public static List<HapticChunk> Create(long[] durations, int[] amplitudes,
        int maxSegments = DefaultMaxSegments, int maxDurationMs = DefaultMaxDurationMs)
    {
        if (durations == null || amplitudes == null || durations.Length != amplitudes.Length)
            throw new ArgumentException("Durations and amplitudes must have equal lengths.");
        maxSegments = Mathf.Max(1, maxSegments); maxDurationMs = Mathf.Max(100, maxDurationMs);
        var result = new List<HapticChunk>(); var d = new List<long>(); var a = new List<int>(); long total = 0;
        for (int i = 0; i < durations.Length; i++)
        {
            long remaining = Math.Max(1L, durations[i]); int amp = Mathf.Clamp(amplitudes[i], 0, 255);
            while (remaining > 0)
            {
                long room = maxDurationMs - total;
                if (d.Count >= maxSegments || room <= 0) { Flush(result, d, a, ref total); room = maxDurationMs; }
                long piece = Math.Min(remaining, Math.Min(2000L, room));
                d.Add(piece); a.Add(amp); total += piece; remaining -= piece;
                if (d.Count >= maxSegments || total >= maxDurationMs) Flush(result, d, a, ref total);
            }
        }
        Flush(result, d, a, ref total); return result;
    }
    private static void Flush(List<HapticChunk> output, List<long> d, List<int> a, ref long total)
    { if (d.Count == 0) return; output.Add(new HapticChunk(d.ToArray(), a.ToArray(), total)); d.Clear(); a.Clear(); total = 0; }
}

public sealed class LongHapticPlayer : MonoBehaviour
{
    [Min(16)] public int maxSegmentsPerChunk = HapticWaveformChunker.DefaultMaxSegments;
    [Range(500, 10000)] public int maxChunkDurationMs = HapticWaveformChunker.DefaultMaxDurationMs;
    private Coroutine playback;
    public bool IsPlaying => playback != null;

    public void PlayProfile(HapticProfile profile, double startDspTime, float amplitudeScale = 1f)
    {
        if (profile == null) { Debug.LogWarning("[LongHaptic] Profile is missing."); return; }
        if (!profile.IsPlayable(out string reason)) { Debug.LogWarning("[LongHaptic] " + reason); return; }
        PlayWaveform(profile.CreateTimings(), profile.CreateScaledAmplitudes(amplitudeScale),
            startDspTime + profile.hapticOffsetMs / 1000d, profile.loop);
    }
    public void PlayWaveform(long[] durations, int[] amplitudes, double startDspTime, bool loop = false)
    {
        Stop(); var chunks = HapticWaveformChunker.Create(durations, amplitudes, maxSegmentsPerChunk, maxChunkDurationMs);
        if (chunks.Count > 0) playback = StartCoroutine(PlayRoutine(chunks, startDspTime, loop));
    }
    public void Stop()
    { if (playback != null) { StopCoroutine(playback); playback = null; } HapticManager.StopWaveform(); HapticManager.StopContinuous(); }
    private IEnumerator PlayRoutine(List<HapticChunk> chunks, double start, bool loop)
    {
        do
        {
            double at = start;
            for (int i = 0; i < chunks.Count; i++)
            { while (AudioSettings.dspTime < at) yield return null; var c = chunks[i]; HapticManager.PlayWaveform(c.durations, c.amplitudes); at += c.durationMs / 1000d; }
            start = at;
        } while (loop);
        playback = null;
    }
    private void OnDisable() => Stop();
}

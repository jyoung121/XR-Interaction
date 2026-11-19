using System;
using UnityEngine;

public static class AudioHapticConverterChecks
{
    // Developer regression check. This intentionally has no Tools menu entry;
    // invoke RunBatch from automated editor checks when the converter changes.
    public static void RunBatch()
    {
        const int rate = 48000;
        AudioHapticConversionSettings settings = AudioHapticConversionSettings.Default;
        settings.windowMs = 20;

        float[] mono = new float[rate];
        for (int i = rate / 2; i < mono.Length; i++)
        {
            float t = i / (float)rate;
            float envelope = 0.15f + 0.75f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4f));
            mono[i] = envelope * Mathf.Sin(t * Mathf.PI * 2f * 180f);
        }
        AudioHapticConversionResult result = AudioHapticConverter.ConvertSamples(
            mono, rate, 1, rate, settings);
        Require(result.TimingsMs.Length == result.Amplitudes.Length, "Array lengths differ.");
        Require(Math.Abs(result.DurationMs - 1000L) <= 2L, "Duration does not match source.");
        Require(Max(result.Amplitudes) > 0, "Active audio produced no haptics.");
        Require(result.Amplitudes[0] == 0, "Initial silence was not preserved.");

        // 60 seconds verifies that conversion is no longer capped at ten seconds.
        float[] longClip = new float[rate * 60];
        for (int i = 0; i < longClip.Length; i++)
            longClip[i] = 0.4f * Mathf.Sin(i / (float)rate * Mathf.PI * 2f * 160f);
        AudioHapticConversionResult longResult = AudioHapticConverter.ConvertSamples(
            longClip, longClip.Length, 1, rate, settings);
        Require(Math.Abs(longResult.DurationMs - 60000L) <= 2L,
            "Long profile duration was truncated.");
        var chunks = HapticWaveformChunker.Create(longResult.TimingsMs, longResult.Amplitudes);
        Require(chunks.Count > 1, "Long profile was not chunked for transport.");
        foreach (HapticChunk chunk in chunks)
        {
            Require(chunk.durations.Length <= HapticWaveformChunker.DefaultMaxSegments,
                "Chunk segment cap was exceeded.");
            Require(chunk.durationMs <= HapticWaveformChunker.DefaultMaxDurationMs,
                "Chunk duration cap was exceeded.");
        }

        bool silenceRejected = false;
        try { AudioHapticConverter.ConvertSamples(new float[rate], rate, 1, rate, settings); }
        catch (InvalidOperationException) { silenceRejected = true; }
        Require(silenceRejected, "Silent audio should be rejected.");
        Debug.Log("[AudioHapticChecks] All converter checks passed.");
    }

    private static int Max(int[] values)
    {
        int maximum = 0;
        for (int i = 0; i < values.Length; i++) maximum = Mathf.Max(maximum, values[i]);
        return maximum;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

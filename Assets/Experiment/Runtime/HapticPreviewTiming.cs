using System;

public static class HapticPreviewTiming
{
    // Same lead-in rule as the scenario scheduler: a negative offset shifts the
    // reference clock forward, rather than discarding the beginning of the waveform.
    public static double StartDsp(double now, int offsetMs) => now + .25d + Math.Max(0d, offsetMs / 1000d);
}

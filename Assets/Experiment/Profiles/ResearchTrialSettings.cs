using System;
using System.Collections.Generic;
using UnityEngine;

// Separate values avoid reinterpreting serialized legacy ParticipantHaptic slots.
public enum ResearchMode { Active, Passive }
public enum ResearchHaptic { None, Simple, SoundBased }
public enum ResearchRecordingPurpose { Experiment, ScenarioPreview, DirectHapticTest, SensorTest }

[Serializable]
public sealed class ResearchScenarioSettings
{
    public ScenarioProfile scenario;
    public HapticProfile soundBased;
    public HapticProfile simple;
    [Min(1f)] public float observationSeconds = 60f;
    [Range(1, 255)] public int simpleAmplitude = 100;
    [Range(0.01f, 1f)] public float pulseThreshold = 0.15f;
    [Min(10)] public int minimumPulseMs = 50;
    [Min(0)] public int mergeGapMs = 60;
    [Min(20)] public int maximumPulseMs = 200;
    [Min(10)] public int interPulseMs = 80;
    public string frozenSoundBasedHash;

    public HapticProfile Resolve(ResearchHaptic condition) => condition == ResearchHaptic.None
        ? null : condition == ResearchHaptic.Simple ? simple : soundBased;

    public bool Validate(out string reason)
    {
        reason = "";
        if (scenario == null || (scenario.scenario != ScenarioId.Apple && scenario.scenario != ScenarioId.Toothbrush))
        { reason = "Assign an Apple or Toothbrush scenario."; return false; }
        if (!scenario.ValidateForPlayback(ExperimentCondition.Baseline, out reason) ||
            !ScenarioAnimationTimeline.Validate(scenario, out reason) ||
            !scenario.ValidateInteraction(out reason)) return false;
        if (float.IsNaN(observationSeconds) || float.IsInfinity(observationSeconds) || observationSeconds < 1f)
        { reason = "Observation duration must be at least one second."; return false; }
        if (soundBased == null || !soundBased.IsPlayable(out reason))
        { reason = "Sound-based profile is missing or invalid. " + reason; return false; }
        if (simple == null || !simple.IsPlayable(out reason))
        { reason = "Generate and assign a Simple profile. " + reason; return false; }
        int? amplitude = null;
        for (int i = 0; i < simple.SegmentCount; i++)
        {
            int value = simple.Amplitudes[i];
            if (value == 0) continue;
            if (amplitude.HasValue && amplitude.Value != value)
            { reason = "Simple pulses must all have one amplitude."; return false; }
            amplitude = value;
        }
        if (!amplitude.HasValue) { reason = "Simple profile contains no pulses."; return false; }
        if (simple == soundBased || simple.loop || simple.hapticOffsetMs != 0 || Mathf.Abs(simple.defaultAmplitudeScale - 1f) > .0001f)
        { reason = "Simple must be a separate non-looping, unscaled profile with zero offset."; return false; }
        return true;
    }
}

[Serializable]
public sealed class ResearchExperimentSettings
{
    public int schemaVersion = 2;
    public bool enabled;
    public bool requireController = true;
    [Range(20, 200)] public int requestedImuHz = 100;
    public List<ResearchScenarioSettings> scenarios = new List<ResearchScenarioSettings>();
    public ResearchScenarioSettings Find(ScenarioId id) => scenarios?.Find(s => s?.scenario != null && s.scenario.scenario == id);

    public bool Validate(out string reason)
    {
        foreach (ScenarioId id in new[] { ScenarioId.Toothbrush, ScenarioId.Apple })
        {
            var settings = Find(id);
            if (settings == null) { reason = "Missing research scenario: " + id; return false; }
            if (!settings.Validate(out reason)) return false;
        }
        if (scenarios.Count != 2) { reason = "Research protocol must have exactly two scenarios."; return false; }
        reason = "";
        return true;
    }
}

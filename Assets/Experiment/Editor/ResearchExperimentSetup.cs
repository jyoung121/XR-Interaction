using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class ResearchExperimentSetup
{
    public const string SimpleFolder = "Assets/Profiles/Haptics/Simple";
    public static ExperimentProtocol Protocol => AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(ExperimentProjectSetup.ProtocolPath);

    [MenuItem("Tools/Experiment/Setup/Initialize Research Redesign", false, 91)]
    public static void InitializeResearch()
    {
        var protocol = Protocol;
        if (protocol == null) throw new BuildFailedException("Experiment Protocol missing");
        if (protocol.research.enabled)
        {
            // One-time upgrade from the initial implementation's session-local JSON hash.
            if (protocol.research.schemaVersion < 2)
            {
                foreach (var settings in protocol.research.scenarios)
                    settings.frozenSoundBasedHash = ResearchLogRecorder.HapticHash(settings.soundBased);
                protocol.research.schemaVersion = 2;
                EditorUtility.SetDirty(protocol); AssetDatabase.SaveAssets();
            }
            ValidateResearch(); return;
        }
        Undo.RecordObject(protocol, "Initialize research redesign");
        protocol.research = new ResearchExperimentSettings();
        foreach (ScenarioId id in new[] { ScenarioId.Toothbrush, ScenarioId.Apple })
        {
            var assignment = protocol.GetPlan(ExperimentCondition.ResearcherHaptic)?.Find(id);
            if (assignment?.profile == null || assignment.hapticProfile == null)
                throw new BuildFailedException("Missing existing source assignment for " + id);
            var settings = new ResearchScenarioSettings {
                scenario = assignment.profile, soundBased = assignment.hapticProfile,
                frozenSoundBasedHash = ResearchLogRecorder.HapticHash(assignment.hapticProfile)
            };
            GenerateSimple(settings);
            protocol.research.scenarios.Add(settings);
        }
        protocol.research.enabled = true;
        EditorUtility.SetDirty(protocol);
        AssetDatabase.SaveAssets();
        ValidateResearch();
        Debug.Log("[Research] Initialized 12 conditions. Existing Sound-based assets were not changed. 60 s, pulse 200/80 ms and amplitude 100 are editable PILOT defaults, not finalized stimuli.");
    }

    public static void GenerateSimple(ResearchScenarioSettings settings, string newAssetPath = null)
    {
        if (settings?.scenario?.sound?.clip == null) throw new ArgumentException("Assign a source scenario and sound.");
        if (settings.minimumPulseMs < 10 || settings.mergeGapMs < 0 || settings.maximumPulseMs < 20 ||
            settings.interPulseMs < 10 || settings.pulseThreshold <= 0 || settings.pulseThreshold > 1)
            throw new ArgumentException("Pulse threshold must be (0,1], min span >=10 ms, merge gap >=0, max pulse >=20 ms, silence >=10 ms.");
        ScenarioProfile scenario = settings.scenario;
        string before = ResearchLogRecorder.HapticHash(settings.soundBased);
        var conversion = settings.soundBased != null ? settings.soundBased.conversionSettings : AudioHapticConversionSettings.Default;
        if (conversion.windowMs <= 0) conversion = AudioHapticConversionSettings.Default;
        var analysis = AudioHapticConverter.Convert(scenario.sound.clip, conversion);
        int step = analysis.EffectiveWindowMs;
        int count = Mathf.CeilToInt(scenario.animationDurationSeconds * 1000f / step);
        var raw = new bool[count]; var allowed = new bool[count];
        var sourceCenters = new double[analysis.WindowDurationsMs.Length];
        long sourceMs = 0;
        for (int i = 0; i < sourceCenters.Length; i++)
        { sourceCenters[i] = sourceMs + analysis.WindowDurationsMs[i] * .5; sourceMs += analysis.WindowDurationsMs[i]; }
        int index = 0;
        for (int i = 0; i < count; i++)
        {
            float time = (i + .5f) * step / 1000f;
            double clipMs = (time - scenario.soundOffsetSeconds + scenario.sound.trimStartSeconds) * 1000d;
            while (index + 1 < sourceCenters.Length && sourceCenters[index + 1] <= clipMs) index++;
            allowed[i] = time >= scenario.soundOffsetSeconds && clipMs >= scenario.sound.trimStartSeconds * 1000d &&
                clipMs < (scenario.sound.clip.length - scenario.sound.trimEndSeconds) * 1000d &&
                ScenarioAnimationTimeline.TrySample(scenario, time, out var sample) && sample.block.kind == AnimationBlockKind.Contact;
            raw[i] = allowed[i] && analysis.NormalizedEnvelope[index] >= settings.pulseThreshold;
        }
        int mergeSteps = Mathf.Max(0, settings.mergeGapMs / step);
        for (int start = 0; start < count;)
        {
            if (raw[start]) { start++; continue; }
            int end = start; bool contact = true;
            while (end < count && !raw[end]) { contact &= allowed[end]; end++; }
            if (start > 0 && end < count && contact && end - start <= mergeSteps)
                for (int j = start; j < end; j++) raw[j] = true;
            start = end;
        }
        for (int start = 0; start < count;)
        {
            if (!raw[start]) { start++; continue; }
            int end = start; while (end < count && raw[end]) end++;
            if ((end - start) * step < settings.minimumPulseMs)
                for (int j = start; j < end; j++) raw[j] = false;
            start = end;
        }
        var durations = new List<int>(); var amplitudes = new List<int>();
        int runMs = 0, gapMs = 0, pulseCount = 0;
        bool previousOn = false;
        for (int i = 0; i < count; i++)
        {
            int duration = Math.Min(step, Mathf.RoundToInt(scenario.animationDurationSeconds * 1000f) - i * step);
            bool on = raw[i] && gapMs <= 0;
            if (on && runMs >= settings.maximumPulseMs) { gapMs = settings.interPulseMs; on = false; }
            if (on) { runMs += duration; if (!previousOn) pulseCount++; }
            else { runMs = 0; gapMs = Math.Max(0, gapMs - duration); }
            if (!raw[i]) { runMs = 0; gapMs = 0; }
            Append(durations, amplitudes, duration, on ? Mathf.Clamp(settings.simpleAmplitude, 1, 255) : 0);
            previousOn = on;
        }
        if (pulseCount == 0) throw new BuildFailedException("No Simple pulses detected. Lower threshold or review contact blocks.");
        Directory.CreateDirectory(SimpleFolder);
        AssetDatabase.Refresh();
        string path = newAssetPath ?? SimpleFolder + "/" + scenario.scenario + "_SimplePulse.asset";
        if (newAssetPath != null && (AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(path)))
            throw new ArgumentException("Choose a new asset path. Existing profiles are never overwritten by the standalone generator.");
        var asset = newAssetPath == null ? settings.simple : null;
        if (asset != null && asset == settings.soundBased) throw new BuildFailedException("Simple must not overwrite the Sound-based profile.");
        if (asset == null && newAssetPath == null) asset = AssetDatabase.LoadAssetAtPath<HapticProfile>(path);
        if (asset == null) { asset = ScriptableObject.CreateInstance<HapticProfile>(); AssetDatabase.CreateAsset(asset, path); }
        Undo.RecordObject(asset, "Generate fixed amplitude pulses");
        asset.SetWaveform(durations.ToArray(), amplitudes.ToArray(), HapticProfileOrigin.Manual,
            scenario.scenario + "-simple-pulse", asset.Version);
        asset.defaultAmplitudeScale = 1f; asset.hapticOffsetMs = 0; asset.loop = false;
        asset.sourceSound = scenario.sound; asset.sourceScenario = scenario; asset.isStyleGenerated = false;
        asset.sourceAssetHash = AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(scenario.sound.clip)).ToString();
        asset.contactCurveHash = ResearchLogRecorder.Hash(string.Join(",", analysis.NormalizedEnvelope));
        asset.scenarioHash = ResearchLogRecorder.Hash(JsonUtility.ToJson(scenario));
        asset.converterVersion = AudioHapticConverter.ConverterVersion;
        asset.conversionSettings = conversion;
        asset.calibrationNote = "Research Simple generator v1. All nonzero amplitudes equal. " +
            "threshold=" + settings.pulseThreshold.ToString("R", CultureInfo.InvariantCulture) +
            ";minPulseMs=" + settings.minimumPulseMs + ";mergeGapMs=" + settings.mergeGapMs +
            ";maxPulseMs=" + settings.maximumPulseMs + ";interPulseMs=" + settings.interPulseMs +
            ";amplitude=" + settings.simpleAmplitude + ";pulseCount=" + pulseCount +
            ";windowMs=" + step + ". PILOT settings require physical review; timing resolution is one analysis window.";
        settings.simple = asset;
        EditorUtility.SetDirty(asset);
        if (settings.soundBased != null && ResearchLogRecorder.HapticHash(settings.soundBased) != before)
            throw new BuildFailedException("Sound-based profile unexpectedly changed.");
    }
    private static void Append(List<int> durations, List<int> amplitudes, int duration, int amplitude)
    {
        while (duration > 0)
        {
            int last = durations.Count - 1;
            if (last >= 0 && amplitudes[last] == amplitude && durations[last] < 2000)
            { int add = Math.Min(duration, 2000 - durations[last]); durations[last] += add; duration -= add; }
            else { int add = Math.Min(duration, 2000); durations.Add(add); amplitudes.Add(amplitude); duration -= add; }
        }
    }
    public static void ValidateResearch()
    {
        var protocol = Protocol;
        ValidateProtocol(protocol);
    }
    public static void ValidateProtocol(ExperimentProtocol protocol)
    {
        if (protocol == null || !protocol.research.enabled) throw new BuildFailedException("Initialize Research Redesign first.");
        if (!protocol.research.Validate(out string reason)) throw new BuildFailedException(reason);
        foreach (var settings in protocol.research.scenarios)
        {
            string hash = ResearchLogRecorder.HapticHash(settings.soundBased);
            if (string.IsNullOrEmpty(settings.frozenSoundBasedHash) || hash != settings.frozenSoundBasedHash)
                throw new BuildFailedException("Frozen Sound-based profile changed: " + settings.soundBased.name);
            if (settings.simple == settings.soundBased) throw new BuildFailedException("Simple and Sound-based must be different assets.");
            // The selected asset is authoritative. Legacy generator seed parameters in
            // ResearchScenarioSettings must not restrict assigning a separately generated profile.
        }
        var scenes = EditorBuildSettings.scenes;
        var enabledScenes = new List<string>();
        foreach (var scene in scenes) if (scene.enabled) enabledScenes.Add(scene.path);
        if (enabledScenes.Count != 2 || enabledScenes[0] != ExperimentProjectSetup.MobileTestScenePath ||
            enabledScenes[1] != ExperimentProjectSetup.ExperimentScenePath)
            throw new BuildFailedException("Build scenes must be MobileTestScene, then ExperimentScene.");
        Debug.Log("[Research] All 12 combinations validated; Sound-based hashes unchanged; Simple amplitudes constant.");
    }
}

public sealed class ResearchBuildValidation : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform == BuildTarget.Android && ResearchExperimentSetup.Protocol?.research.enabled == true)
            ResearchExperimentSetup.ValidateResearch();
    }
}


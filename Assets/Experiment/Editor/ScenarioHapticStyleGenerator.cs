using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

[Serializable]
public struct HapticStyleGenerationSettings
{
    [Range(20, 1000)] public int contactAttackMs;
    [Range(20, 1500)] public int contactReleaseMs;
    [Range(25, 1000)] public int curveKeyIntervalMs;
    [Range(0.001f, 0.1f)] public float curveTolerance;
    [Range(0f, 1f)] public float balancedDetail;
    [Range(1f, 2f)] public float texturedDetail;

    public static HapticStyleGenerationSettings ForScenario(ScenarioId scenario)
    {
        if (scenario == ScenarioId.Apple)
            return Create(45, 120, 50, 0.02f, 0.55f, 1.35f);
        if (scenario == ScenarioId.Toothbrush)
            return Create(160, 350, 200, 0.015f, 0.55f, 1.25f);
        return Create(240, 500, 250, 0.01f, 0.5f, 1.2f);
    }

    private static HapticStyleGenerationSettings Create(int attack, int release,
        int keyInterval, float tolerance, float balanced, float textured)
    {
        return new HapticStyleGenerationSettings
        {
            contactAttackMs = attack,
            contactReleaseMs = release,
            curveKeyIntervalMs = keyInterval,
            curveTolerance = tolerance,
            balancedDetail = balanced,
            texturedDetail = textured
        };
    }

    public HapticStyleGenerationSettings Sanitized()
    {
        HapticStyleGenerationSettings value = this;
        value.contactAttackMs = Mathf.Clamp(value.contactAttackMs, 20, 1000);
        value.contactReleaseMs = Mathf.Clamp(value.contactReleaseMs, 20, 1500);
        value.curveKeyIntervalMs = Mathf.Clamp(value.curveKeyIntervalMs, 25, 1000);
        value.curveTolerance = Mathf.Clamp(value.curveTolerance, 0.001f, 0.1f);
        value.balancedDetail = Mathf.Clamp01(value.balancedDetail);
        value.texturedDetail = Mathf.Clamp(value.texturedDetail, 1f, 2f);
        return value;
    }
}

public sealed class ScenarioHapticStyleGenerationResult
{
    public AudioHapticConversionResult analysis;
    public AudioHapticConversionResult smooth;
    public AudioHapticConversionResult balanced;
    public AudioHapticConversionResult textured;
    public string sourceSoundHash;
    public string scenarioHash;
    public string contactCurveHash;
}

public static class ScenarioHapticStyleGenerator
{
    public const string GeneratedRoot = "Assets/Profiles/Haptics/Generated";

    public static ScenarioHapticStyleGenerationResult Generate(ScenarioProfile scenario,
        AudioHapticConversionSettings conversionSettings,
        HapticStyleGenerationSettings styleSettings, bool writeContactCurves)
    {
        if (scenario == null) throw new ArgumentNullException(nameof(scenario));
        if (scenario.sound == null || scenario.sound.clip == null)
            throw new InvalidOperationException("ScenarioProfile requires a SoundProfile with an AudioClip.");

        conversionSettings = conversionSettings.Sanitized();
        styleSettings = styleSettings.Sanitized();
        AudioHapticConversionResult analysis = AudioHapticConverter.Convert(
            scenario.sound.clip, conversionSettings);
        float[] slow = Smooth(analysis.NormalizedEnvelope, analysis.WindowDurationsMs,
            styleSettings.contactAttackMs, styleSettings.contactReleaseMs);
        double[] centers = WindowCenters(analysis.WindowDurationsMs);

        if (writeContactCurves)
        {
            Undo.RecordObject(scenario, "Generate Contact Load Curves");
            ApplyContactCurves(scenario, slow, centers, styleSettings.curveKeyIntervalMs,
                styleSettings.curveTolerance);
            EditorUtility.SetDirty(scenario);
        }

        BuildStyleEnvelopes(scenario, analysis, slow, centers, conversionSettings, styleSettings,
            out AudioHapticConversionResult smoothResult,
            out AudioHapticConversionResult balancedResult,
            out AudioHapticConversionResult texturedResult);

        string soundPath = AssetDatabase.GetAssetPath(scenario.sound.clip);
        string soundHash = string.IsNullOrEmpty(soundPath) ? string.Empty
            : AssetDatabase.GetAssetDependencyHash(soundPath).ToString();
        return new ScenarioHapticStyleGenerationResult
        {
            analysis = analysis,
            smooth = smoothResult,
            balanced = balancedResult,
            textured = texturedResult,
            sourceSoundHash = soundHash,
            scenarioHash = HashText(EditorJsonUtility.ToJson(scenario)),
            contactCurveHash = ContactCurveHash(scenario)
        };
    }

    public static ScenarioHapticStyleSet SaveGeneratedAssets(ScenarioProfile scenario,
        AudioHapticConversionSettings conversionSettings,
        ScenarioHapticStyleGenerationResult generated)
    {
        if (scenario == null || generated == null)
            throw new ArgumentNullException(scenario == null ? nameof(scenario) : nameof(generated));

        string folder = GeneratedRoot + "/" + scenario.scenario;
        ManualHapticGeneratorWindow.EnsureFolder(GeneratedRoot);
        ManualHapticGeneratorWindow.EnsureFolder(folder);
        string stem = scenario.scenario + "_v" + scenario.Version.ToString("000");

        HapticProfile smooth = GetOrCreate<HapticProfile>(folder + "/" + stem + "_Smooth.asset");
        HapticProfile balanced = GetOrCreate<HapticProfile>(folder + "/" + stem + "_Balanced.asset");
        HapticProfile textured = GetOrCreate<HapticProfile>(folder + "/" + stem + "_Textured.asset");
        Apply(smooth, scenario, HapticStyle.Smooth,
            StyleSettings(conversionSettings, HapticStyle.Smooth), generated.smooth, generated);
        Apply(balanced, scenario, HapticStyle.Balanced,
            StyleSettings(conversionSettings, HapticStyle.Balanced), generated.balanced, generated);
        Apply(textured, scenario, HapticStyle.Textured,
            StyleSettings(conversionSettings, HapticStyle.Textured), generated.textured, generated);

        ScenarioHapticStyleSet set = GetOrCreate<ScenarioHapticStyleSet>(
            folder + "/" + stem + "_StyleSet.asset");
        set.scenario = scenario.scenario;
        set.sourceScenario = scenario;
        set.smooth = smooth;
        set.balanced = balanced;
        set.textured = textured;
        set.generatorVersion = ScenarioHapticStyleSet.CurrentGeneratorVersion;
        set.sourceSoundHash = generated.sourceSoundHash;
        set.scenarioHash = generated.scenarioHash;
        set.contactCurveHash = generated.contactCurveHash;
        scenario.participantStyleSet = set;

        EditorUtility.SetDirty(smooth);
        EditorUtility.SetDirty(balanced);
        EditorUtility.SetDirty(textured);
        EditorUtility.SetDirty(set);
        EditorUtility.SetDirty(scenario);
        AssetDatabase.SaveAssets();
        return set;
    }

    private static void Apply(HapticProfile destination, ScenarioProfile scenario,
        HapticStyle style, AudioHapticConversionSettings settings,
        AudioHapticConversionResult waveform, ScenarioHapticStyleGenerationResult generated)
    {
        destination.SetIdentity(destination.name, scenario.Version);
        destination.ApplyStyledGenerated(scenario, style, scenario.sound, settings, waveform,
            generated.sourceSoundHash, generated.scenarioHash, generated.contactCurveHash);
    }

    private static T GetOrCreate<T>(string path) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null) return asset;
        asset = ScriptableObject.CreateInstance<T>();
        asset.name = System.IO.Path.GetFileNameWithoutExtension(path);
        AssetDatabase.CreateAsset(asset, path);
        return asset;
    }

    private static void ApplyContactCurves(ScenarioProfile scenario, float[] slow,
        double[] centers, int keyIntervalMs, float curveTolerance)
    {
        if (scenario.animationBlocks == null) return;
        for (int i = 0; i < scenario.animationBlocks.Count; i++)
        {
            ScenarioAnimationBlock block = scenario.animationBlocks[i];
            if (block == null || block.kind != AnimationBlockKind.Contact) continue;
            int steps = Mathf.Max(1, Mathf.CeilToInt(
                block.durationSeconds * 1000f / Mathf.Max(25, keyIntervalMs)));
            var keys = new Keyframe[steps + 1];
            for (int key = 0; key <= steps; key++)
            {
                float progress = key / (float)steps;
                double scenarioSeconds = block.startSeconds + block.durationSeconds * progress;
                double clipSeconds = scenarioSeconds - scenario.soundOffsetSeconds +
                    scenario.sound.trimStartSeconds;
                keys[key] = new Keyframe(progress,
                    SampleEnvelope(slow, centers, clipSeconds));
            }

            block.useContactLoadCurve = true;
            block.contactLoadCurve = new AnimationCurve(SimplifyKeys(keys, curveTolerance));
            for (int key = 0; key < block.contactLoadCurve.length; key++)
            {
                AnimationUtility.SetKeyLeftTangentMode(block.contactLoadCurve, key,
                    AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(block.contactLoadCurve, key,
                    AnimationUtility.TangentMode.ClampedAuto);
            }
            if (scenario.scenario == ScenarioId.DentalDrill)
            { block.minimumContactDepth = 0.82f; block.maximumContactDepth = 1f; }
            else if (scenario.scenario == ScenarioId.Toothbrush)
            { block.minimumContactDepth = 0.86f; block.maximumContactDepth = 1f; }
            else
            { block.minimumContactDepth = 0f; block.maximumContactDepth = 1f; }
        }
    }

    private static Keyframe[] SimplifyKeys(Keyframe[] keys, float tolerance)
    {
        if (keys == null || keys.Length <= 2) return keys ?? Array.Empty<Keyframe>();
        bool[] keep = new bool[keys.Length];
        keep[0] = true;
        keep[keys.Length - 1] = true;
        MarkImportant(keys, 0, keys.Length - 1, Mathf.Max(0.0001f, tolerance), keep);
        var result = new List<Keyframe>();
        for (int i = 0; i < keys.Length; i++) if (keep[i]) result.Add(keys[i]);
        return result.ToArray();
    }

    private static void MarkImportant(Keyframe[] keys, int from, int to,
        float tolerance, bool[] keep)
    {
        if (to <= from + 1) return;
        float span = Mathf.Max(0.000001f, keys[to].time - keys[from].time);
        float greatest = 0f;
        int greatestIndex = -1;
        for (int i = from + 1; i < to; i++)
        {
            float t = (keys[i].time - keys[from].time) / span;
            float expected = Mathf.Lerp(keys[from].value, keys[to].value, t);
            float error = Mathf.Abs(keys[i].value - expected);
            if (error <= greatest) continue;
            greatest = error;
            greatestIndex = i;
        }
        if (greatestIndex < 0 || greatest <= tolerance) return;
        keep[greatestIndex] = true;
        MarkImportant(keys, from, greatestIndex, tolerance, keep);
        MarkImportant(keys, greatestIndex, to, tolerance, keep);
    }

    private static void BuildStyleEnvelopes(ScenarioProfile scenario,
        AudioHapticConversionResult analysis, float[] slow, double[] centers,
        AudioHapticConversionSettings conversionSettings,
        HapticStyleGenerationSettings styleSettings,
        out AudioHapticConversionResult smoothResult,
        out AudioHapticConversionResult balancedResult,
        out AudioHapticConversionResult texturedResult)
    {
        var durations = new List<long>();
        var raw = new List<float>();
        var smoothValues = new List<float>();
        var balancedValues = new List<float>();
        var texturedValues = new List<float>();
        double trimStart = scenario.sound.trimStartSeconds;
        double trimEnd = scenario.sound.clip.length - scenario.sound.trimEndSeconds;

        for (int i = 0; i < analysis.WindowDurationsMs.Length; i++)
        {
            double clipSeconds = centers[i];
            if (clipSeconds < trimStart || clipSeconds >= trimEnd) continue;
            double scenarioSeconds = scenario.soundOffsetSeconds + clipSeconds - trimStart;
            ScenarioAnimationBlock contact = FindContactBlock(scenario, scenarioSeconds);
            float mask = contact == null ? 0f : 1f;
            float progress = contact == null ? 0f : Mathf.Clamp01(
                ((float)scenarioSeconds - contact.startSeconds) /
                Mathf.Max(0.0001f, contact.durationSeconds));
            float load = contact == null ? 0f : contact.EvaluateContactLoad(progress);
            float detail = analysis.NormalizedEnvelope[i];

            durations.Add(analysis.WindowDurationsMs[i]);
            raw.Add(analysis.RawEnvelope[i]);
            smoothValues.Add(mask * load);
            balancedValues.Add(mask * Mathf.Clamp01(
                Mathf.Lerp(load, detail, styleSettings.balancedDetail)));
            texturedValues.Add(mask * Mathf.Clamp01(
                load + (detail - load) * styleSettings.texturedDetail));
        }

        long[] durationArray = durations.ToArray();
        float[] smoothArray = smoothValues.ToArray();
        float[] balancedArray = balancedValues.ToArray();
        float[] texturedArray = texturedValues.ToArray();
        NormalizeCommonEnergy(smoothArray, balancedArray, texturedArray, durationArray);

        AudioHapticConversionResult playableSource = SliceSource(
            analysis, durationArray, raw.ToArray(), balancedArray);
        AudioHapticConversionSettings smoothSettings = StyleSettings(
            conversionSettings, HapticStyle.Smooth);
        AudioHapticConversionSettings balancedSettings = StyleSettings(
            conversionSettings, HapticStyle.Balanced);
        AudioHapticConversionSettings texturedSettings = StyleSettings(
            conversionSettings, HapticStyle.Textured);
        smoothResult = AudioHapticConverter.BuildFromNormalizedEnvelope(
            playableSource, smoothArray, smoothSettings);
        balancedResult = AudioHapticConverter.BuildFromNormalizedEnvelope(
            playableSource, balancedArray, balancedSettings);
        texturedResult = AudioHapticConverter.BuildFromNormalizedEnvelope(
            playableSource, texturedArray, texturedSettings);
        NormalizeQuantizedEnergy(smoothResult, balancedResult, texturedResult);
    }

    private static AudioHapticConversionSettings StyleSettings(
        AudioHapticConversionSettings source, HapticStyle style)
    {
        AudioHapticConversionSettings result = source.Sanitized();
        if (style == HapticStyle.Smooth)
        { result.attackMs = 80; result.releaseMs = 180; result.quantizationLevels = 8; }
        else if (style == HapticStyle.Textured)
        { result.attackMs = 5; result.releaseMs = 35; result.quantizationLevels = 16; }
        else
        { result.attackMs = 20; result.releaseMs = 70; result.quantizationLevels = 12; }
        return result;
    }

    private static AudioHapticConversionResult SliceSource(AudioHapticConversionResult source,
        long[] durations, float[] raw, float[] normalized)
    {
        return AudioHapticConverter.CreateAnalysisSlice(source, durations, raw, normalized);
    }

    private static ScenarioAnimationBlock FindContactBlock(ScenarioProfile scenario,
        double scenarioSeconds)
    {
        if (scenario.animationBlocks == null) return null;
        for (int i = 0; i < scenario.animationBlocks.Count; i++)
        {
            ScenarioAnimationBlock block = scenario.animationBlocks[i];
            if (block == null || block.kind != AnimationBlockKind.Contact) continue;
            if (scenarioSeconds >= block.startSeconds &&
                scenarioSeconds < block.startSeconds + block.durationSeconds) return block;
        }
        return null;
    }

    private static float[] Smooth(float[] input, long[] durations, int attackMs, int releaseMs)
    {
        float[] output = new float[input.Length];
        float current = 0f;
        for (int i = 0; i < input.Length; i++)
        {
            float target = Mathf.Clamp01(input[i]);
            float tau = target > current ? attackMs : releaseMs;
            float alpha = tau <= 0f ? 1f : 1f - Mathf.Exp(-durations[i] / tau);
            current += (target - current) * alpha;
            if (target <= 0f && current < 0.0025f) current = 0f;
            output[i] = Mathf.Clamp01(current);
        }
        return output;
    }

    private static double[] WindowCenters(long[] durations)
    {
        double[] result = new double[durations.Length];
        long elapsed = 0;
        for (int i = 0; i < durations.Length; i++)
        {
            result[i] = (elapsed + durations[i] * 0.5d) / 1000d;
            elapsed += durations[i];
        }
        return result;
    }

    private static float SampleEnvelope(float[] values, double[] centers, double seconds)
    {
        if (values.Length == 0 || seconds < 0d) return 0f;
        int index = Array.BinarySearch(centers, seconds);
        if (index >= 0) return values[index];
        index = ~index;
        if (index <= 0) return values[0];
        if (index >= values.Length) return 0f;
        double span = Math.Max(0.000001d, centers[index] - centers[index - 1]);
        float t = (float)((seconds - centers[index - 1]) / span);
        return Mathf.Lerp(values[index - 1], values[index], t);
    }

    private static void NormalizeCommonEnergy(float[] smooth, float[] balanced,
        float[] textured, long[] durations)
    {
        double smoothRms = EnvelopeRms(smooth, durations);
        double balancedRms = EnvelopeRms(balanced, durations);
        double texturedRms = EnvelopeRms(textured, durations);
        double target = Math.Min(smoothRms, Math.Min(balancedRms, texturedRms));
        if (target <= 1e-9d) return;
        ScaleEnvelope(smooth, target / smoothRms);
        ScaleEnvelope(balanced, target / balancedRms);
        ScaleEnvelope(textured, target / texturedRms);
    }

    private static double EnvelopeRms(float[] values, long[] durations)
    {
        double energy = 0d;
        long total = 0;
        for (int i = 0; i < values.Length; i++)
        {
            energy += values[i] * values[i] * durations[i];
            total += durations[i];
        }
        return total > 0 ? Math.Sqrt(energy / total) : 0d;
    }

    private static void ScaleEnvelope(float[] values, double scale)
    {
        for (int i = 0; i < values.Length; i++)
            values[i] = Mathf.Clamp01(values[i] * (float)scale);
    }

    private static void NormalizeQuantizedEnergy(AudioHapticConversionResult smooth,
        AudioHapticConversionResult balanced, AudioHapticConversionResult textured)
    {
        double smoothRms = ResultRms(smooth);
        double balancedRms = ResultRms(balanced);
        double texturedRms = ResultRms(textured);
        double target = Math.Min(smoothRms, Math.Min(balancedRms, texturedRms));
        if (target <= 1e-9d) return;
        ScaleResult(smooth, target / smoothRms);
        ScaleResult(balanced, target / balancedRms);
        ScaleResult(textured, target / texturedRms);
    }

    private static double ResultRms(AudioHapticConversionResult result)
    {
        double energy = 0d;
        long total = 0;
        for (int i = 0; i < result.Amplitudes.Length; i++)
        {
            double amplitude = result.Amplitudes[i] / 255d;
            energy += amplitude * amplitude * result.TimingsMs[i];
            total += result.TimingsMs[i];
        }
        return total > 0 ? Math.Sqrt(energy / total) : 0d;
    }

    private static void ScaleResult(AudioHapticConversionResult result, double scale)
    {
        for (int i = 0; i < result.Amplitudes.Length; i++)
        {
            if (result.Amplitudes[i] <= 0) continue;
            result.Amplitudes[i] = Mathf.Clamp(
                Mathf.RoundToInt(result.Amplitudes[i] * (float)scale), 1, 255);
        }
    }

    private static string ContactCurveHash(ScenarioProfile scenario)
    {
        var text = new StringBuilder();
        if (scenario.animationBlocks != null)
        {
            for (int i = 0; i < scenario.animationBlocks.Count; i++)
            {
                ScenarioAnimationBlock block = scenario.animationBlocks[i];
                if (block == null || !block.useContactLoadCurve ||
                    block.contactLoadCurve == null) continue;
                text.Append(block.blockId).Append('|')
                    .Append(block.minimumContactDepth.ToString("R", CultureInfo.InvariantCulture))
                    .Append('|')
                    .Append(block.maximumContactDepth.ToString("R", CultureInfo.InvariantCulture));
                Keyframe[] keys = block.contactLoadCurve.keys;
                for (int key = 0; key < keys.Length; key++)
                    text.Append('|').Append(keys[key].time.ToString("R", CultureInfo.InvariantCulture))
                        .Append(':').Append(keys[key].value.ToString("R", CultureInfo.InvariantCulture));
            }
        }
        return HashText(text.ToString());
    }

    private static string HashText(string value) => Hash128.Compute(value ?? string.Empty).ToString();
}

public static class ScenarioHapticStyleBatch
{
    private static readonly string[] ScenarioPaths =
    {
        "Assets/Profiles/Scenarios/Variants/DentalDrill/DentalDrill_ScenarioProfile_v002.asset",
        "Assets/Profiles/Scenarios/Variants/Toothbrush/Toothbrush_ScenarioProfile_v002.asset",
        "Assets/Profiles/Scenarios/Variants/Apple/Apple_ScenarioProfile_v002.asset"
    };

    [MenuItem("Tools/Haptics/Generate v002 Contact Curves and Styles", false, 30)]
    public static void GenerateAllV002()
    {
        AudioHapticConversionSettings conversion = AudioHapticConversionSettings.Default;
        var scenarios = new List<ScenarioProfile>();
        var generatedProfiles = new List<HapticProfile>();
        for (int i = 0; i < ScenarioPaths.Length; i++)
        {
            ScenarioProfile scenario = AssetDatabase.LoadAssetAtPath<ScenarioProfile>(ScenarioPaths[i]);
            if (scenario == null)
                throw new InvalidOperationException("Missing v002 scenario: " + ScenarioPaths[i]);
            HapticStyleGenerationSettings style =
                HapticStyleGenerationSettings.ForScenario(scenario.scenario);
            ScenarioHapticStyleGenerationResult result = ScenarioHapticStyleGenerator.Generate(
                scenario, conversion, style, true);
            ScenarioHapticStyleSet set = ScenarioHapticStyleGenerator.SaveGeneratedAssets(
                scenario, conversion, result);
            scenarios.Add(scenario);
            generatedProfiles.Add(set.smooth);
            generatedProfiles.Add(set.balanced);
            generatedProfiles.Add(set.textured);
        }

        ActivateV002(scenarios, generatedProfiles);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[HapticStyles] Generated 9 profiles and configured mobile Profiles A/B/C " +
                  "for the three v002 scenarios.");
        if (!Application.isBatchMode && scenarios.Count > 0)
        {
            Selection.activeObject = scenarios[0].participantStyleSet;
            EditorGUIUtility.PingObject(scenarios[0].participantStyleSet);
            EditorUtility.DisplayDialog("Haptic Styles Updated",
                "Updated 9 independent HapticProfile assets under:\n" +
                ScenarioHapticStyleGenerator.GeneratedRoot +
                "\n\nMobile mapping: A = Smooth, B = Balanced, C = Textured.", "OK");
        }
    }

    [MenuItem("Tools/Haptics/Validate v002 Contact Curves and Styles", false, 31)]
    public static void ValidateAllV002()
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            "Assets/Profiles/ProfileCatalog.asset");
        ExperimentProtocol protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(
            "Assets/Profiles/Experiments/AndroidExperimentProtocol.asset");
        if (catalog == null || protocol == null)
            throw new InvalidOperationException("ProfileCatalog or AndroidExperimentProtocol is missing.");
        if (catalog.scenarios == null || catalog.scenarios.Count != ScenarioPaths.Length)
            throw new InvalidOperationException("ProfileCatalog must contain the three v002 scenarios.");

        foreach (string path in ScenarioPaths)
        {
            ScenarioProfile scenario = AssetDatabase.LoadAssetAtPath<ScenarioProfile>(path);
            if (scenario == null) throw new InvalidOperationException("Missing scenario: " + path);
            if (!ScenarioAnimationTimeline.Validate(scenario, out string reason))
                throw new InvalidOperationException(scenario.name + ": " + reason);
            if (scenario.participantStyleSet == null ||
                !scenario.participantStyleSet.IsComplete(out reason))
                throw new InvalidOperationException(scenario.name + " style set: " + reason);
            string soundPath = AssetDatabase.GetAssetPath(scenario.sound.clip);
            string currentSoundHash = string.IsNullOrEmpty(soundPath) ? string.Empty :
                AssetDatabase.GetAssetDependencyHash(soundPath).ToString();
            if (scenario.participantStyleSet.sourceSoundHash != currentSoundHash)
                throw new InvalidOperationException(scenario.name +
                    " style profiles are stale. Regenerate them from the current sound.");
            ValidateContactCurves(scenario);
            ValidateStyleDurations(scenario, scenario.participantStyleSet);
        }

        foreach (ExperimentConditionPlan plan in protocol.conditionPlans)
        {
            if (plan == null) continue;
            if (!protocol.TryValidatePlan(plan.condition, out string reason))
                throw new InvalidOperationException(reason);
            if (plan.condition != ExperimentCondition.ParticipantHaptic) continue;
            foreach (ConditionScenarioAssignment assignment in plan.scenarios)
            {
                foreach (ParticipantHapticSlot slot in new[]
                {
                    ParticipantHapticSlot.ProfileA,
                    ParticipantHapticSlot.ProfileB,
                    ParticipantHapticSlot.ProfileC
                })
                {
                    if (!protocol.TryResolveTrial(plan.condition, assignment.scenario, slot,
                            out ScenarioProfile resolvedScenario, out HapticProfile resolvedHaptic,
                            out reason))
                        throw new InvalidOperationException(reason);
                    if (resolvedScenario != assignment.profile ||
                        resolvedHaptic != assignment.ResolveParticipantProfile(slot))
                        throw new InvalidOperationException(
                            $"{assignment.scenario} / {slot} resolved the wrong asset.");
                }
            }
        }
        Debug.Log("[HapticStyles] v002 validation passed for curves, 9 style profiles, " +
                  "catalog references, and enabled experiment conditions.");
    }

    private static void ActivateV002(List<ScenarioProfile> scenarios,
        List<HapticProfile> generatedProfiles)
    {
        ProfileCatalog catalog = AssetDatabase.LoadAssetAtPath<ProfileCatalog>(
            "Assets/Profiles/ProfileCatalog.asset");
        ExperimentProtocol protocol = AssetDatabase.LoadAssetAtPath<ExperimentProtocol>(
            "Assets/Profiles/Experiments/AndroidExperimentProtocol.asset");
        if (catalog == null || protocol == null)
            throw new InvalidOperationException("ProfileCatalog or AndroidExperimentProtocol is missing.");

        catalog.scenarios = new List<ScenarioProfile>(scenarios);
        catalog.haptics = new List<HapticProfile>(generatedProfiles);
        protocol.EnsureConditionPlans();
        for (int planIndex = 0; planIndex < protocol.conditionPlans.Count; planIndex++)
        {
            ExperimentConditionPlan plan = protocol.conditionPlans[planIndex];
            if (plan == null) continue;
            plan.enabled = true;
            for (int assignmentIndex = 0; assignmentIndex < plan.scenarios.Count; assignmentIndex++)
            {
                ConditionScenarioAssignment assignment = plan.scenarios[assignmentIndex];
                if (assignment == null) continue;
                assignment.profile = scenarios.Find(p => p != null &&
                    p.scenario == assignment.scenario);
                if (plan.condition == ExperimentCondition.Baseline)
                {
                    assignment.hapticProfile = null;
                }
                else if (plan.condition == ExperimentCondition.ResearcherHaptic)
                {
                    assignment.hapticProfile = assignment.profile != null
                        ? assignment.profile.researcherHaptic : null;
                }
                else
                {
                    ScenarioHapticStyleSet set = assignment.profile != null
                        ? assignment.profile.participantStyleSet : null;
                    assignment.profileA = set != null ? set.smooth : null;
                    assignment.profileB = set != null ? set.balanced : null;
                    assignment.profileC = set != null ? set.textured : null;
                }
            }
        }
        protocol.participantConditionEnabled = true;
        protocol.scenarios = new List<ProtocolScenario>();
        for (int i = 0; i < scenarios.Count; i++)
            protocol.scenarios.Add(new ProtocolScenario { scenario = scenarios[i], repetitions = 1 });

        EditorUtility.SetDirty(catalog);
        EditorUtility.SetDirty(protocol);
    }

    private static void ValidateContactCurves(ScenarioProfile scenario)
    {
        int contactCount = 0;
        foreach (ScenarioAnimationBlock block in scenario.animationBlocks)
        {
            if (block == null || block.kind != AnimationBlockKind.Contact) continue;
            contactCount++;
            if (!block.useContactLoadCurve || block.contactLoadCurve == null ||
                block.contactLoadCurve.length < 2)
                throw new InvalidOperationException(scenario.name + "/" + block.blockId +
                    " has no generated Contact Load Curve.");
        }
        if (contactCount == 0)
            throw new InvalidOperationException(scenario.name + " has no contact blocks.");
    }

    private static void ValidateStyleDurations(ScenarioProfile scenario,
        ScenarioHapticStyleSet set)
    {
        long expected = (long)Math.Round(scenario.sound.PlayableDurationSeconds * 1000d);
        long smooth = set.smooth.DurationMs;
        long balanced = set.balanced.DurationMs;
        long textured = set.textured.DurationMs;
        if (Math.Abs(smooth - expected) > 100 || Math.Abs(balanced - expected) > 100 ||
            Math.Abs(textured - expected) > 100)
            throw new InvalidOperationException(scenario.name +
                " generated profile duration does not match its playable sound.");
        if (Math.Abs(smooth - balanced) > 2 || Math.Abs(textured - balanced) > 2)
            throw new InvalidOperationException(scenario.name +
                " styles do not share one synchronized duration.");

        double reference = ProfileRms(set.balanced);
        double smoothRms = ProfileRms(set.smooth);
        double texturedRms = ProfileRms(set.textured);
        if (reference <= 0d || smoothRms <= 0d || texturedRms <= 0d)
            throw new InvalidOperationException(scenario.name + " contains a silent style profile.");
        double minimum = Math.Min(reference, Math.Min(smoothRms, texturedRms));
        double maximum = Math.Max(reference, Math.Max(smoothRms, texturedRms));
        if (maximum - minimum > 0.01d)
            throw new InvalidOperationException(scenario.name +
                " style intensity normalization differs by more than 0.01 RMS.");
        Debug.Log($"[HapticStyles] {scenario.scenario}: {expected} ms, segments " +
                  $"{set.smooth.SegmentCount}/{set.balanced.SegmentCount}/{set.textured.SegmentCount}, " +
                  $"RMS {smoothRms:0.000}/{reference:0.000}/{texturedRms:0.000}");
    }

    private static double ProfileRms(HapticProfile profile)
    {
        double squares = 0d;
        long total = 0;
        int count = profile.SegmentCount;
        for (int i = 0; i < count; i++)
        {
            long duration = Math.Max(1, profile.DurationsMs[i]);
            double amplitude = profile.Amplitudes[i] / 255d;
            squares += amplitude * amplitude * duration;
            total += duration;
        }
        return total > 0 ? Math.Sqrt(squares / total) : 0d;
    }
}

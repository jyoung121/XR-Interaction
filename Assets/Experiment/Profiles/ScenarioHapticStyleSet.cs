using UnityEngine;

public enum HapticStyle
{
    Smooth,
    Balanced,
    Textured
}

[CreateAssetMenu(fileName = "ScenarioHapticStyleSet",
    menuName = "Experiment/Scenario Haptic Style Set")]
public sealed class ScenarioHapticStyleSet : ScriptableObject
{
    public const int CurrentGeneratorVersion = 1;

    public ScenarioId scenario;
    public ScenarioProfile sourceScenario;
    public HapticProfile smooth;
    public HapticProfile balanced;
    public HapticProfile textured;

    [Header("Provenance")]
    public int generatorVersion = CurrentGeneratorVersion;
    public string sourceSoundHash = string.Empty;
    public string scenarioHash = string.Empty;
    public string contactCurveHash = string.Empty;

    public HapticProfile Resolve(HapticStyle style)
    {
        if (style == HapticStyle.Smooth) return smooth;
        if (style == HapticStyle.Textured) return textured;
        return balanced;
    }

    public bool IsComplete(out string reason)
    {
        if (sourceScenario == null)
        { reason = "Source ScenarioProfile is missing."; return false; }
        if (smooth == null || balanced == null || textured == null)
        { reason = "Smooth, Balanced, and Textured profiles are all required."; return false; }
        if (smooth == balanced || smooth == textured || balanced == textured)
        { reason = "Smooth, Balanced, and Textured must be independent assets."; return false; }
        if (smooth.isStyleGenerated && smooth.generatedStyle != HapticStyle.Smooth)
        { reason = "Smooth references the wrong generated style."; return false; }
        if (balanced.isStyleGenerated && balanced.generatedStyle != HapticStyle.Balanced)
        { reason = "Balanced references the wrong generated style."; return false; }
        if (textured.isStyleGenerated && textured.generatedStyle != HapticStyle.Textured)
        { reason = "Textured references the wrong generated style."; return false; }
        if (!smooth.IsPlayable(out reason) || !balanced.IsPlayable(out reason) ||
            !textured.IsPlayable(out reason)) return false;
        reason = string.Empty;
        return true;
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ProtocolScenario
{
    public ScenarioProfile scenario;
    [Min(1)] public int repetitions = 1;
}

[Serializable]
public sealed class ConditionScenarioAssignment
{
    public ScenarioId scenario;
    public ScenarioProfile profile;
    [Tooltip("Fixed profile used by Researcher Haptics. Baseline must leave this empty.")]
    public HapticProfile hapticProfile;
    [Header("User Haptics (mobile labels A / B / C)")]
    public HapticProfile profileA;
    public HapticProfile profileB;
    public HapticProfile profileC;
    [Min(1)] public int repetitions = 1;

    public HapticProfile ResolveParticipantProfile(ParticipantHapticSlot slot)
    {
        switch (slot)
        {
            case ParticipantHapticSlot.ProfileA: return profileA;
            case ParticipantHapticSlot.ProfileB: return profileB;
            case ParticipantHapticSlot.ProfileC: return profileC;
            default: return null;
        }
    }
}

public enum ParticipantHapticSlot
{
    None,
    ProfileA,
    ProfileB,
    ProfileC
}

[Serializable]
public sealed class ExperimentConditionPlan
{
    public ExperimentCondition condition;
    public bool enabled = true;
    public List<ConditionScenarioAssignment> scenarios = new List<ConditionScenarioAssignment>();

    public ConditionScenarioAssignment Find(ScenarioId id)
    {
        if (scenarios == null) return null;
        return scenarios.Find(entry => entry != null && entry.scenario == id);
    }
}

[CreateAssetMenu(fileName = "ExperimentProtocol", menuName = "Experiment/Protocol")]
public sealed class ExperimentProtocol : ScriptableObject
{
    public const int CurrentVersion = 2;
    private static readonly ScenarioId[] RequiredScenarios =
    {
        ScenarioId.DentalDrill,
        ScenarioId.Toothbrush,
        ScenarioId.Apple
    };

    private static readonly ExperimentCondition[] RequiredConditions =
    {
        ExperimentCondition.Baseline,
        ExperimentCondition.ResearcherHaptic,
        ExperimentCondition.ParticipantHaptic
    };

    [SerializeField] private string protocolId = "android-final";
    [SerializeField] private int version = 1;
    public RuntimePlatform targetPlatform = RuntimePlatform.Android;
    public ResearchExperimentSettings research = new ResearchExperimentSettings();

    [Header("Condition-specific scenario assignments")]
    public List<ExperimentConditionPlan> conditionPlans = new List<ExperimentConditionPlan>();
    public bool randomizeOrder;
    public int randomSeed = 1;

    // Kept serialized so existing protocol assets can be migrated without losing data.
    [HideInInspector] public List<ProtocolScenario> scenarios = new List<ProtocolScenario>();
    [HideInInspector] public List<ExperimentCondition> conditions = new List<ExperimentCondition>
    { ExperimentCondition.Baseline, ExperimentCondition.ResearcherHaptic };
    [HideInInspector] public bool participantConditionEnabled;

    public string ProtocolId => string.IsNullOrWhiteSpace(protocolId) ? name : protocolId;
    public int Version => Mathf.Max(1, version);
    public IReadOnlyList<ScenarioId> ScenarioOrder => RequiredScenarios;

    public ExperimentConditionPlan GetPlan(ExperimentCondition condition)
    {
        if (conditionPlans == null) return null;
        return conditionPlans.Find(plan => plan != null && plan.condition == condition);
    }

    public bool TryResolveTrial(ExperimentCondition condition, ScenarioId scenario,
                                out ScenarioProfile profile, out string reason)
    {
        profile = null;
        ExperimentConditionPlan plan = GetPlan(condition);
        if (plan == null)
        {
            reason = $"{DisplayName(condition)} is not configured.";
            return false;
        }
        if (!plan.enabled)
        {
            reason = $"{DisplayName(condition)} is not ready.";
            return false;
        }

        ConditionScenarioAssignment assignment = plan.Find(scenario);
        if (assignment?.profile == null)
        {
            reason = $"{DisplayName(condition)} / {DisplayName(scenario)} has no Scenario Profile.";
            return false;
        }
        if (assignment.profile.scenario != scenario)
        {
            reason = $"{assignment.profile.name} is assigned to the wrong scenario slot ({scenario}).";
            return false;
        }
        if (!ValidateAssignment(condition, assignment, false, ParticipantHapticSlot.None,
                out _, out reason)) return false;

        profile = assignment.profile;
        reason = string.Empty;
        return true;
    }

    public bool TryResolveTrial(ExperimentCondition condition, ScenarioId scenario,
                                ParticipantHapticSlot participantSlot,
                                out ScenarioProfile profile, out HapticProfile haptic,
                                out string reason)
    {
        profile = null;
        haptic = null;
        ExperimentConditionPlan plan = GetPlan(condition);
        if (plan == null || !plan.enabled)
        {
            reason = plan == null ? $"{DisplayName(condition)} is not configured." :
                $"{DisplayName(condition)} is not ready.";
            return false;
        }
        ConditionScenarioAssignment assignment = plan.Find(scenario);
        if (assignment?.profile == null)
        {
            reason = $"{DisplayName(condition)} / {DisplayName(scenario)} has no Scenario Profile.";
            return false;
        }
        if (!ValidateAssignment(condition, assignment, true, participantSlot,
                out haptic, out reason)) return false;
        profile = assignment.profile;
        return true;
    }

    public IEnumerable<ExperimentConditionPlan> EnabledPlans()
    {
        if (conditionPlans == null) yield break;
        for (int i = 0; i < conditionPlans.Count; i++)
        {
            ExperimentConditionPlan plan = conditionPlans[i];
            if (plan != null && plan.enabled) yield return plan;
        }
    }

    public bool EnsureConditionPlans()
    {
        bool changed = false;
        if (version < CurrentVersion)
        {
            version = CurrentVersion;
            changed = true;
        }
        if (conditionPlans == null)
        {
            conditionPlans = new List<ExperimentConditionPlan>();
            changed = true;
        }

        foreach (ExperimentCondition condition in RequiredConditions)
        {
            ExperimentConditionPlan plan = GetPlan(condition);
            if (plan == null)
            {
                plan = new ExperimentConditionPlan
                {
                    condition = condition,
                    enabled = condition != ExperimentCondition.ParticipantHaptic
                };
                conditionPlans.Add(plan);
                changed = true;
            }

            if (plan.scenarios == null)
            {
                plan.scenarios = new List<ConditionScenarioAssignment>();
                changed = true;
            }

            foreach (ScenarioId id in RequiredScenarios)
            {
                if (plan.Find(id) != null) continue;
                plan.scenarios.Add(new ConditionScenarioAssignment
                {
                    scenario = id,
                    profile = FindLegacyScenario(id),
                    repetitions = 1
                });
                changed = true;
            }

            foreach (ConditionScenarioAssignment assignment in plan.scenarios)
                if (ApplyHapticDefaults(plan.condition, assignment)) changed = true;
        }

        return changed;
    }

    public bool TryValidatePlan(ExperimentCondition condition, out string reason)
    {
        ExperimentConditionPlan plan = GetPlan(condition);
        if (plan == null)
        {
            reason = $"{DisplayName(condition)} is not configured.";
            return false;
        }
        if (!plan.enabled)
        {
            reason = $"{DisplayName(condition)} is disabled in the experiment protocol.";
            return false;
        }

        var ids = new HashSet<ScenarioId>();
        if (plan.scenarios == null)
        {
            reason = $"{DisplayName(condition)} has no scenario assignments.";
            return false;
        }

        foreach (ConditionScenarioAssignment assignment in plan.scenarios)
        {
            if (assignment == null)
            {
                reason = $"{DisplayName(condition)} contains an empty scenario assignment.";
                return false;
            }
            if (!ids.Add(assignment.scenario))
            {
                reason = $"{DisplayName(condition)} contains duplicate {assignment.scenario} assignments.";
                return false;
            }
            if (assignment.profile == null)
            {
                reason = $"{DisplayName(condition)} / {DisplayName(assignment.scenario)} has no Scenario Profile.";
                return false;
            }
            if (assignment.profile.scenario != assignment.scenario)
            {
                reason = $"{assignment.profile.name} is assigned to the wrong scenario slot ({assignment.scenario}).";
                return false;
            }
            if (assignment.repetitions < 1)
            {
                reason = $"{assignment.profile.name} repetitions must be at least one.";
                return false;
            }
            if (!ValidateAssignment(condition, assignment, false,
                    ParticipantHapticSlot.None, out _, out reason)) return false;
        }

        foreach (ScenarioId required in RequiredScenarios)
        {
            if (ids.Contains(required)) continue;
            reason = $"{DisplayName(condition)} is missing {DisplayName(required)}.";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    public bool TryValidateSharedStimuli(out string reason)
    {
        foreach (ScenarioId id in RequiredScenarios)
        {
            ScenarioProfile reference = null;
            foreach (ExperimentConditionPlan plan in EnabledPlans())
            {
                ConditionScenarioAssignment assignment = plan.Find(id);
                if (assignment?.profile == null) continue;
                if (reference == null)
                {
                    reference = assignment.profile;
                    continue;
                }
                if (!HaveSameStimulus(reference, assignment.profile, out string difference))
                {
                    reason = $"{DisplayName(id)} differs between conditions: {difference}";
                    return false;
                }
            }
        }

        reason = string.Empty;
        return true;
    }

    public static string DisplayName(ExperimentCondition condition)
    {
        switch (condition)
        {
            case ExperimentCondition.Baseline: return "Baseline";
            case ExperimentCondition.ResearcherHaptic: return "Researcher Haptics";
            case ExperimentCondition.ParticipantHaptic: return "User Haptics";
            default: return condition.ToString();
        }
    }

    public static string DisplayName(ScenarioId scenario)
    {
        switch (scenario)
        {
            case ScenarioId.DentalDrill: return "Dental Drill";
            case ScenarioId.Toothbrush: return "Toothbrush";
            case ScenarioId.Apple: return "Food / Apple";
            default: return scenario.ToString();
        }
    }

    public static string DisplayName(ParticipantHapticSlot slot)
    {
        switch (slot)
        {
            case ParticipantHapticSlot.ProfileA: return "Profile A";
            case ParticipantHapticSlot.ProfileB: return "Profile B";
            case ParticipantHapticSlot.ProfileC: return "Profile C";
            default: return "None";
        }
    }

    private static bool ApplyHapticDefaults(ExperimentCondition condition,
        ConditionScenarioAssignment assignment)
    {
        if (assignment?.profile == null) return false;
        bool changed = false;
        if (condition == ExperimentCondition.ResearcherHaptic && assignment.hapticProfile == null &&
            assignment.profile.researcherHaptic != null)
        {
            assignment.hapticProfile = assignment.profile.researcherHaptic;
            changed = true;
        }
        if (condition == ExperimentCondition.ParticipantHaptic &&
            assignment.profile.participantStyleSet != null)
        {
            ScenarioHapticStyleSet set = assignment.profile.participantStyleSet;
            if (assignment.profileA == null && set.smooth != null)
            { assignment.profileA = set.smooth; changed = true; }
            if (assignment.profileB == null && set.balanced != null)
            { assignment.profileB = set.balanced; changed = true; }
            if (assignment.profileC == null && set.textured != null)
            { assignment.profileC = set.textured; changed = true; }
        }
        return changed;
    }

    private static bool ValidateAssignment(ExperimentCondition condition,
        ConditionScenarioAssignment assignment, bool requireParticipantSelection,
        ParticipantHapticSlot participantSlot, out HapticProfile resolvedHaptic,
        out string reason)
    {
        resolvedHaptic = null;
        ScenarioProfile scenario = assignment.profile;
        string prefix = $"{DisplayName(condition)} / {DisplayName(assignment.scenario)}";
        if (scenario == null)
        { reason = prefix + " has no Scenario Profile."; return false; }
        if (scenario.scenario != assignment.scenario)
        { reason = $"{scenario.name} is assigned to the wrong scenario slot."; return false; }
        if (!scenario.ValidateForPlayback(ExperimentCondition.Baseline, out reason))
        { reason = $"{prefix} / {scenario.name}: {reason}"; return false; }
        if (!ScenarioAnimationTimeline.Validate(scenario, out reason))
        { reason = $"{prefix} / {scenario.name}: {reason}"; return false; }

        if (condition == ExperimentCondition.Baseline)
        {
            if (assignment.hapticProfile != null)
            { reason = prefix + " must not assign a HapticProfile."; return false; }
            reason = string.Empty;
            return true;
        }

        if (condition == ExperimentCondition.ResearcherHaptic)
        {
            resolvedHaptic = assignment.hapticProfile;
            if (resolvedHaptic == null)
            { reason = prefix + " has no researcher HapticProfile."; return false; }
            if (!resolvedHaptic.IsPlayable(out reason))
            { reason = prefix + ": " + reason; return false; }
            reason = string.Empty;
            return true;
        }

        HapticProfile[] options = { assignment.profileA, assignment.profileB, assignment.profileC };
        var unique = new HashSet<HapticProfile>();
        for (int i = 0; i < options.Length; i++)
        {
            HapticProfile option = options[i];
            if (option == null)
            { reason = $"{prefix} is missing Profile {(char)('A' + i)}."; return false; }
            if (!unique.Add(option))
            { reason = prefix + " must use three different HapticProfile assets."; return false; }
            if (!option.IsPlayable(out reason))
            { reason = $"{prefix} / Profile {(char)('A' + i)}: {reason}"; return false; }
            if (Mathf.Abs(option.defaultAmplitudeScale - 1f) > 0.0001f)
            { reason = $"{prefix} / Profile {(char)('A' + i)} must use intensity scale 1.0."; return false; }
            if (option.sourceScenario != null && option.sourceScenario.scenario != assignment.scenario)
            { reason = $"{prefix} / Profile {(char)('A' + i)} was generated for another scenario."; return false; }
            HapticStyle expectedStyle = i == 0 ? HapticStyle.Smooth :
                i == 1 ? HapticStyle.Balanced : HapticStyle.Textured;
            if (option.isStyleGenerated && option.generatedStyle != expectedStyle)
            { reason = $"{prefix} / Profile {(char)('A' + i)} has the wrong generated style."; return false; }
        }
        if (requireParticipantSelection)
        {
            resolvedHaptic = assignment.ResolveParticipantProfile(participantSlot);
            if (resolvedHaptic == null)
            { reason = "Select Profile A, B, or C before continuing."; return false; }
        }
        reason = string.Empty;
        return true;
    }

    private ScenarioProfile FindLegacyScenario(ScenarioId id)
    {
        if (scenarios == null) return null;
        for (int i = 0; i < scenarios.Count; i++)
        {
            ProtocolScenario entry = scenarios[i];
            if (entry?.scenario != null && entry.scenario.scenario == id) return entry.scenario;
        }
        return null;
    }

    private static bool HaveSameStimulus(ScenarioProfile a, ScenarioProfile b, out string difference)
    {
        const float tolerance = 0.0001f;
        if (a.sound != b.sound)
        {
            difference = $"Sound Profile is not shared ({a.name} vs {b.name}).";
            return false;
        }
        if (Mathf.Abs(a.animationDurationSeconds - b.animationDurationSeconds) > tolerance ||
            Mathf.Abs(a.soundOffsetSeconds - b.soundOffsetSeconds) > tolerance)
        {
            difference = $"animation duration or sound offset differs ({a.name} vs {b.name}).";
            return false;
        }
        int aCount = a.animationBlocks?.Count ?? 0;
        int bCount = b.animationBlocks?.Count ?? 0;
        if (aCount != bCount)
        {
            difference = $"animation block count differs ({a.name} vs {b.name}).";
            return false;
        }
        for (int i = 0; i < aCount; i++)
        {
            ScenarioAnimationBlock left = a.animationBlocks[i];
            ScenarioAnimationBlock right = b.animationBlocks[i];
            if (left == null || right == null)
            {
                if (left == right) continue;
                difference = $"animation block {i} differs ({a.name} vs {b.name}).";
                return false;
            }
            if (left.blockId != right.blockId || left.kind != right.kind ||
                left.action != right.action || left.track != right.track ||
                Mathf.Abs(left.startSeconds - right.startSeconds) > tolerance ||
                Mathf.Abs(left.durationSeconds - right.durationSeconds) > tolerance ||
                left.timeMode != right.timeMode ||
                Mathf.Abs(left.playbackSpeed - right.playbackSpeed) > tolerance ||
                left.iteration != right.iteration ||
                left.useContactLoadCurve != right.useContactLoadCurve ||
                Mathf.Abs(left.minimumContactDepth - right.minimumContactDepth) > tolerance ||
                Mathf.Abs(left.maximumContactDepth - right.maximumContactDepth) > tolerance ||
                !HaveSameCurve(left.contactLoadCurve, right.contactLoadCurve, tolerance))
            {
                difference = $"animation block '{left.blockId}' differs ({a.name} vs {b.name}).";
                return false;
            }
        }

        difference = string.Empty;
        return true;
    }

    private static bool HaveSameCurve(AnimationCurve a, AnimationCurve b, float tolerance)
    {
        int aCount = a?.length ?? 0;
        int bCount = b?.length ?? 0;
        if (aCount != bCount) return false;
        if (aCount == 0) return true;
        for (int i = 0; i < aCount; i++)
        {
            Keyframe left = a.keys[i];
            Keyframe right = b.keys[i];
            if (Mathf.Abs(left.time - right.time) > tolerance ||
                Mathf.Abs(left.value - right.value) > tolerance ||
                Mathf.Abs(left.inTangent - right.inTangent) > tolerance ||
                Mathf.Abs(left.outTangent - right.outTangent) > tolerance)
                return false;
        }
        return true;
    }
}

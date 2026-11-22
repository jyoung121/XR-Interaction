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
    [Min(1)] public int repetitions = 1;
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
        if (!assignment.profile.ValidateForPlayback(condition, out reason))
        {
            reason = $"{DisplayName(condition)} / {assignment.profile.name}: {reason}";
            return false;
        }
        if (!ScenarioAnimationTimeline.Validate(assignment.profile, out reason))
        {
            reason = $"{DisplayName(condition)} / {assignment.profile.name}: {reason}";
            return false;
        }

        profile = assignment.profile;
        reason = string.Empty;
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
            if (!assignment.profile.ValidateForPlayback(condition, out reason))
            {
                reason = $"{DisplayName(condition)} / {assignment.profile.name}: {reason}";
                return false;
            }
            if (!ScenarioAnimationTimeline.Validate(assignment.profile, out reason))
            {
                reason = $"{DisplayName(condition)} / {assignment.profile.name}: {reason}";
                return false;
            }
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
                left.iteration != right.iteration)
            {
                difference = $"animation block '{left.blockId}' differs ({a.name} vs {b.name}).";
                return false;
            }
        }

        difference = string.Empty;
        return true;
    }
}

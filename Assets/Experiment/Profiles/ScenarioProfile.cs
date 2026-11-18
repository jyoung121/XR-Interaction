using System;
using System.Collections.Generic;
using UnityEngine;

public enum ScenarioId { Apple, DentalDrill, Toothbrush }
public enum ExperimentCondition { Baseline, ResearcherHaptic, ParticipantHaptic }
public enum AnimationBlockKind { Setup, Approach, Contact, Retreat, Finish, Custom }

[Serializable]
public sealed class ScenarioAnimationBlock
{
    public string blockId = "block";
    public AnimationBlockKind kind;
    [Min(0f)] public float startSeconds;
    [Min(0f)] public float durationSeconds = 1f;
    [TextArea] public string note;
}

[CreateAssetMenu(fileName = "ScenarioProfile", menuName = "Experiment/Scenario Profile")]
public sealed class ScenarioProfile : ScriptableObject
{
    [SerializeField] private string profileId = "scenario";
    [SerializeField] private int version = 1;
    public ScenarioId scenario;
    public SoundProfile sound;
    public HapticProfile researcherHaptic;
    [Tooltip("Reserved until the participant-tuning protocol is decided.")]
    public HapticProfile participantHaptic;
    [Header("Shared timeline (identical in every condition)")]
    [Min(0f)] public float animationDurationSeconds = 10f;
    public float soundOffsetSeconds;
    public float hapticOffsetSeconds;
    public bool loop;
    public List<ScenarioAnimationBlock> animationBlocks = new List<ScenarioAnimationBlock>();
    public string ProfileId => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
    public int Version => Mathf.Max(1, version);

    public HapticProfile ResolveHaptic(ExperimentCondition condition)
    {
        if (condition == ExperimentCondition.ResearcherHaptic) return researcherHaptic;
        if (condition == ExperimentCondition.ParticipantHaptic) return participantHaptic;
        return null;
    }

    public bool ValidateForPlayback(ExperimentCondition condition, out string reason)
    {
        if (sound == null) { reason = $"Scenario profile '{name}' has no SoundProfile."; return false; }
        if (!sound.IsUsable(out reason)) return false;
        if (condition == ExperimentCondition.Baseline) { reason = string.Empty; return true; }
        HapticProfile haptic = ResolveHaptic(condition);
        if (haptic == null) { reason = $"{condition} has no HapticProfile in '{name}'."; return false; }
        return haptic.IsPlayable(out reason);
    }

#if UNITY_EDITOR
    public void SetIdentity(string id, int nextVersion = 1)
    { profileId = string.IsNullOrWhiteSpace(id) ? name : id.Trim(); version = Mathf.Max(1, nextVersion); }
#endif
}

using System;
using System.Collections.Generic;
using UnityEngine;

public enum ScenarioId { Apple, DentalDrill, Toothbrush }
public enum ExperimentCondition { Baseline, ResearcherHaptic, ParticipantHaptic }
public enum AnimationBlockKind { Setup, Approach, Contact, Retreat, Finish, Custom }
public enum AnimationTrack { Primary, Face, Prop }
public enum BlockTimeMode { Stretch, Loop, Once, Hold }
public enum ScenarioInteractionMode { Automatic, UserStartStop }
public enum ScenarioFinishMode { StopImmediately, PlayRetreat, PlayCurrentIterationRest }

public enum ScenarioAnimationAction
{
    Auto = 0,
    ApplePlateWait = 1,
    AppleApproach = 2,
    AppleBite = 3,
    AppleChew = 4,
    AppleRest = 5,
    DrillSetup = 10,
    DrillApproach = 11,
    DrillContact = 12,
    DrillRetreat = 13,
    DrillFinish = 14,
    BrushSetup = 20,
    BrushApproach = 21,
    BrushContact = 22,
    BrushRetreat = 23,
    BrushFinish = 24
}

[Serializable]
public sealed class ScenarioAnimationBlock
{
    public string blockId = "block";
    public AnimationBlockKind kind;
    [Tooltip("Concrete animation evaluated by this block. Use an explicit action in final profiles.")]
    public ScenarioAnimationAction action;
    public AnimationTrack track;
    [Min(0f)] public float startSeconds;
    [Min(0f)] public float durationSeconds = 1f;
    public BlockTimeMode timeMode = BlockTimeMode.Stretch;
    [Min(0.01f)] public float playbackSpeed = 1f;
    [Min(0)] public int iteration;
    [Header("Contact load")]
    [Tooltip("Evaluate a shared 0-1 interaction load over the full block, independent of animation looping.")]
    public bool useContactLoadCurve;
    public AnimationCurve contactLoadCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
    [Range(0f, 1f)] public float minimumContactDepth = 0.82f;
    [Range(0f, 1f)] public float maximumContactDepth = 1f;
    [TextArea] public string note;

    public float EvaluateContactLoad(float blockProgress01)
    {
        if (!useContactLoadCurve || contactLoadCurve == null || contactLoadCurve.length == 0)
            return kind == AnimationBlockKind.Contact ? 1f : 0f;
        return Mathf.Clamp01(contactLoadCurve.Evaluate(Mathf.Clamp01(blockProgress01)));
    }
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
    [Tooltip("Generated Smooth/Balanced/Textured options. The participant condition remains disabled until its protocol is approved.")]
    public ScenarioHapticStyleSet participantStyleSet;
    [Header("Shared timeline (identical in every condition)")]
    [Min(0f)] public float animationDurationSeconds = 10f;
    public float soundOffsetSeconds;
    public float hapticOffsetSeconds;
    public bool loop;
    [Header("Participant interaction")]
    [Tooltip("Automatic preserves the fixed timeline. UserStartStop waits for a participant input before playback and accepts a second input to finish.")]
    public ScenarioInteractionMode interactionMode;
    [Tooltip("Animation used after a participant requests a normal finish.")]
    public ScenarioFinishMode finishMode;
    [Tooltip("Ignore the finish input until the first contact block is active.")]
    public bool ignoreStopDuringApproach = true;
    [Min(0f)] public float minimumActiveSeconds = 1f;
    [Min(0f)] public float finishBlendSeconds = 0.2f;
    public List<ScenarioAnimationBlock> animationBlocks = new List<ScenarioAnimationBlock>();
    public string ProfileId => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
    public int Version => Mathf.Max(1, version);
    public bool RequiresUserStart => interactionMode == ScenarioInteractionMode.UserStartStop;

    public HapticProfile ResolveHaptic(ExperimentCondition condition)
    {
        if (condition == ExperimentCondition.ResearcherHaptic) return researcherHaptic;
        if (condition == ExperimentCondition.ParticipantHaptic) return participantHaptic;
        return null;
    }

    public HapticProfile ResolveHaptic(ExperimentCondition condition, HapticProfile overrideProfile)
    {
        if (condition == ExperimentCondition.Baseline) return null;
        return overrideProfile != null ? overrideProfile : ResolveHaptic(condition);
    }

    public bool ValidateForPlayback(ExperimentCondition condition, out string reason)
        => ValidateForPlayback(condition, null, out reason);

    public bool ValidateForPlayback(ExperimentCondition condition,
                                    HapticProfile overrideProfile, out string reason)
    {
        if (sound == null) { reason = $"Scenario profile '{name}' has no SoundProfile."; return false; }
        if (!sound.IsUsable(out reason)) return false;
        if (condition == ExperimentCondition.Baseline) { reason = string.Empty; return true; }
        HapticProfile haptic = ResolveHaptic(condition, overrideProfile);
        if (haptic == null) { reason = $"{condition} has no HapticProfile in '{name}'."; return false; }
        return haptic.IsPlayable(out reason);
    }

    public bool ValidateInteraction(out string reason)
    {
        if (!RequiresUserStart)
        {
            reason = string.Empty;
            return true;
        }

        bool hasApproach = false;
        bool hasContact = false;
        bool hasRetreat = false;
        var contactIterations = new HashSet<int>();
        var restIterations = new HashSet<int>();

        if (animationBlocks != null)
        {
            foreach (ScenarioAnimationBlock block in animationBlocks)
            {
                if (block == null) continue;
                hasApproach |= block.kind == AnimationBlockKind.Approach;
                hasContact |= block.kind == AnimationBlockKind.Contact;
                hasRetreat |= block.kind == AnimationBlockKind.Retreat;
                if (block.kind == AnimationBlockKind.Contact)
                    contactIterations.Add(block.iteration);
                if (block.action == ScenarioAnimationAction.AppleRest)
                    restIterations.Add(block.iteration);
            }
        }

        if (!hasApproach || !hasContact)
        {
            reason = $"Interactive scenario '{name}' requires Approach and Contact blocks.";
            return false;
        }
        if (finishMode == ScenarioFinishMode.PlayRetreat && !hasRetreat)
        {
            reason = $"Interactive scenario '{name}' requires a Retreat block.";
            return false;
        }
        if (finishMode == ScenarioFinishMode.PlayCurrentIterationRest)
        {
            foreach (int iteration in contactIterations)
            {
                if (restIterations.Contains(iteration)) continue;
                reason = $"Interactive scenario '{name}' has no AppleRest block for iteration {iteration}.";
                return false;
            }
        }

        reason = string.Empty;
        return true;
    }

#if UNITY_EDITOR
    public void SetIdentity(string id, int nextVersion = 1)
    { profileId = string.IsNullOrWhiteSpace(id) ? name : id.Trim(); version = Mathf.Max(1, nextVersion); }
#endif
}

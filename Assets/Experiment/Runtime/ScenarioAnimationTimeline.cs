using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct ScenarioBlockSample
{
    public readonly ScenarioAnimationBlock block;
    public readonly ScenarioAnimationAction action;
    public readonly float localSeconds;
    public readonly float normalizedTime;
    public readonly float blockProgress01;
    public readonly float sourceSeconds;
    public readonly float contactLoad;
    public readonly float minimumContactDepth;
    public readonly float maximumContactDepth;

    public ScenarioBlockSample(ScenarioAnimationBlock block, ScenarioAnimationAction action,
        float localSeconds, float normalizedTime, float blockProgress01, float sourceSeconds,
        float contactLoad)
    {
        this.block = block;
        this.action = action;
        this.localSeconds = localSeconds;
        this.normalizedTime = normalizedTime;
        this.blockProgress01 = blockProgress01;
        this.sourceSeconds = sourceSeconds;
        this.contactLoad = Mathf.Clamp01(contactLoad);
        minimumContactDepth = block != null ? Mathf.Clamp01(block.minimumContactDepth) : 0f;
        maximumContactDepth = block != null
            ? Mathf.Max(minimumContactDepth, Mathf.Clamp01(block.maximumContactDepth)) : 1f;
    }
}

public static class ScenarioAnimationTimeline
{
    private const float Epsilon = 0.0001f;

    public static bool TrySample(ScenarioProfile profile, float scenarioSeconds,
        out ScenarioBlockSample sample)
    {
        sample = default;
        if (profile == null || profile.animationBlocks == null || profile.animationBlocks.Count == 0)
            return false;

        ScenarioAnimationBlock active = null;
        ScenarioAnimationBlock previous = null;
        float time = Mathf.Max(0f, scenarioSeconds);

        for (int i = 0; i < profile.animationBlocks.Count; i++)
        {
            ScenarioAnimationBlock candidate = profile.animationBlocks[i];
            if (candidate == null || candidate.track != AnimationTrack.Primary) continue;

            float start = Mathf.Max(0f, candidate.startSeconds);
            float end = start + Mathf.Max(0f, candidate.durationSeconds);
            // Decimal block boundaries can overlap by a few float ULPs. Prefer the
            // block which starts here over the preceding block's rounded-up end.
            if (time >= start && time < end && (active == null || start >= active.startSeconds))
                active = candidate;

            if (end <= time && (previous == null || start >= previous.startSeconds))
                previous = candidate;
        }

        // Gaps intentionally hold the last block pose. Before the first block, use its start pose.
        bool holdingPrevious = active == null && previous != null;
        if (active == null)
        {
            active = previous ?? FirstPrimary(profile.animationBlocks);
            if (active == null) return false;
        }

        float duration = Mathf.Max(Epsilon, active.durationSeconds);
        float local = holdingPrevious ? duration : Mathf.Clamp(time - active.startSeconds, 0f, duration);
        float blockProgress = Mathf.Clamp01(local / duration);
        float normalized = blockProgress;
        ScenarioAnimationAction action = ResolveAction(profile.scenario, active);
        float nativeDuration = Mathf.Max(Epsilon, MirrorRig.AnimationSourceDuration(action));
        float speed = Mathf.Max(0.01f, active.playbackSpeed);
        float source;

        switch (active.timeMode)
        {
            case BlockTimeMode.Loop:
                source = Mathf.Repeat(local * speed, nativeDuration);
                normalized = source / nativeDuration;
                break;
            case BlockTimeMode.Once:
                source = Mathf.Min(local * speed, nativeDuration);
                normalized = source / nativeDuration;
                break;
            case BlockTimeMode.Hold:
                source = holdingPrevious ? nativeDuration : Mathf.Min(local * speed, nativeDuration);
                normalized = source / nativeDuration;
                break;
            default:
                source = normalized * nativeDuration;
                break;
        }

        sample = new ScenarioBlockSample(active, action, local, normalized, blockProgress,
            source, active.EvaluateContactLoad(blockProgress));
        return true;
    }

    public static ScenarioAnimationAction ResolveAction(ScenarioId scenario, ScenarioAnimationBlock block)
    {
        if (block == null) return ScenarioAnimationAction.Auto;
        if (block.action != ScenarioAnimationAction.Auto) return block.action;

        switch (scenario)
        {
            case ScenarioId.Apple:
                if (block.kind == AnimationBlockKind.Setup) return ScenarioAnimationAction.ApplePlateWait;
                if (block.kind == AnimationBlockKind.Approach) return ScenarioAnimationAction.AppleApproach;
                if (block.kind == AnimationBlockKind.Contact) return ScenarioAnimationAction.AppleBite;
                return ScenarioAnimationAction.AppleRest;
            case ScenarioId.DentalDrill:
                if (block.kind == AnimationBlockKind.Setup) return ScenarioAnimationAction.DrillSetup;
                if (block.kind == AnimationBlockKind.Approach) return ScenarioAnimationAction.DrillApproach;
                if (block.kind == AnimationBlockKind.Contact) return ScenarioAnimationAction.DrillContact;
                if (block.kind == AnimationBlockKind.Retreat) return ScenarioAnimationAction.DrillRetreat;
                return ScenarioAnimationAction.DrillFinish;
            case ScenarioId.Toothbrush:
                if (block.kind == AnimationBlockKind.Setup) return ScenarioAnimationAction.BrushSetup;
                if (block.kind == AnimationBlockKind.Approach) return ScenarioAnimationAction.BrushApproach;
                if (block.kind == AnimationBlockKind.Contact) return ScenarioAnimationAction.BrushContact;
                if (block.kind == AnimationBlockKind.Retreat) return ScenarioAnimationAction.BrushRetreat;
                return ScenarioAnimationAction.BrushFinish;
            default:
                return ScenarioAnimationAction.Auto;
        }
    }

    public static bool Validate(ScenarioProfile profile, out string reason)
    {
        if (profile == null) { reason = "ScenarioProfile is missing."; return false; }
        if (profile.animationBlocks == null || profile.animationBlocks.Count == 0)
        { reason = $"Scenario profile '{profile.name}' has no animation blocks."; return false; }

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tracks = new Dictionary<AnimationTrack, List<ScenarioAnimationBlock>>();
        for (int i = 0; i < profile.animationBlocks.Count; i++)
        {
            ScenarioAnimationBlock block = profile.animationBlocks[i];
            if (block == null) { reason = $"Animation block {i} is null."; return false; }
            if (string.IsNullOrWhiteSpace(block.blockId)) { reason = $"Animation block {i} has no id."; return false; }
            if (!ids.Add(block.blockId)) { reason = $"Animation block id '{block.blockId}' is duplicated."; return false; }
            if (block.startSeconds < 0f || block.durationSeconds <= 0f)
            { reason = $"Animation block '{block.blockId}' has an invalid time range."; return false; }
            if (block.startSeconds + block.durationSeconds > profile.animationDurationSeconds + Epsilon)
            { reason = $"Animation block '{block.blockId}' exceeds the scenario duration."; return false; }
            if (block.track != AnimationTrack.Primary)
            { reason = $"Animation track '{block.track}' is reserved but not implemented yet."; return false; }
            if (block.action == ScenarioAnimationAction.Auto)
            { reason = $"Animation block '{block.blockId}' must use an explicit action."; return false; }
            if (block.minimumContactDepth > block.maximumContactDepth)
            { reason = $"Animation block '{block.blockId}' has an inverted contact depth range."; return false; }

            ScenarioAnimationAction action = ResolveAction(profile.scenario, block);
            if (!IsCompatible(profile.scenario, action))
            { reason = $"Animation action '{action}' is not valid for {profile.scenario}."; return false; }

            if (!tracks.TryGetValue(block.track, out List<ScenarioAnimationBlock> list))
            { list = new List<ScenarioAnimationBlock>(); tracks.Add(block.track, list); }
            list.Add(block);
        }

        foreach (KeyValuePair<AnimationTrack, List<ScenarioAnimationBlock>> pair in tracks)
        {
            pair.Value.Sort((a, b) => a.startSeconds.CompareTo(b.startSeconds));
            for (int i = 1; i < pair.Value.Count; i++)
            {
                ScenarioAnimationBlock previous = pair.Value[i - 1];
                ScenarioAnimationBlock current = pair.Value[i];
                if (current.startSeconds < previous.startSeconds + previous.durationSeconds - Epsilon)
                { reason = $"Animation blocks '{previous.blockId}' and '{current.blockId}' overlap on {pair.Key}."; return false; }
            }
        }

        if (!HasRequiredActions(profile, out reason)) return false;

        reason = string.Empty;
        return true;
    }

    public static bool IsCompatible(ScenarioId scenario, ScenarioAnimationAction action)
    {
        if (action == ScenarioAnimationAction.Auto) return false;
        string name = action.ToString();
        if (scenario == ScenarioId.Apple) return name.StartsWith("Apple", StringComparison.Ordinal);
        if (scenario == ScenarioId.DentalDrill) return name.StartsWith("Drill", StringComparison.Ordinal);
        if (scenario == ScenarioId.Toothbrush) return name.StartsWith("Brush", StringComparison.Ordinal);
        return false;
    }

    private static ScenarioAnimationBlock FirstPrimary(List<ScenarioAnimationBlock> blocks)
    {
        ScenarioAnimationBlock result = null;
        for (int i = 0; i < blocks.Count; i++)
        {
            ScenarioAnimationBlock candidate = blocks[i];
            if (candidate == null || candidate.track != AnimationTrack.Primary) continue;
            if (result == null || candidate.startSeconds < result.startSeconds) result = candidate;
        }
        return result;
    }

    private static bool HasRequiredActions(ScenarioProfile profile, out string reason)
    {
        if (profile.scenario == ScenarioId.Apple)
        {
            for (int iteration = 0; iteration < AppleCrunchData.Segments; iteration++)
            {
                if (!Has(profile, ScenarioAnimationAction.ApplePlateWait, iteration) ||
                    !Has(profile, ScenarioAnimationAction.AppleApproach, iteration) ||
                    !Has(profile, ScenarioAnimationAction.AppleBite, iteration) ||
                    !Has(profile, ScenarioAnimationAction.AppleChew, iteration) ||
                    !Has(profile, ScenarioAnimationAction.AppleRest, iteration))
                {
                    reason = $"Apple bite {iteration + 1} is missing a plate, approach, bite, chew, or rest block.";
                    return false;
                }
            }
        }
        else if (profile.scenario == ScenarioId.DentalDrill)
        {
            if (!Has(profile, ScenarioAnimationAction.DrillApproach) ||
                !Has(profile, ScenarioAnimationAction.DrillContact) ||
                !Has(profile, ScenarioAnimationAction.DrillRetreat))
            { reason = "Dental Drill requires independent approach, contact, and retreat blocks."; return false; }
        }
        else if (profile.scenario == ScenarioId.Toothbrush)
        {
            if (!Has(profile, ScenarioAnimationAction.BrushApproach) ||
                !Has(profile, ScenarioAnimationAction.BrushContact) ||
                !Has(profile, ScenarioAnimationAction.BrushRetreat))
            { reason = "Toothbrush requires independent approach, contact, and retreat blocks."; return false; }
        }

        reason = string.Empty;
        return true;
    }

    private static bool Has(ScenarioProfile profile, ScenarioAnimationAction action, int iteration = -1)
    {
        for (int i = 0; i < profile.animationBlocks.Count; i++)
        {
            ScenarioAnimationBlock block = profile.animationBlocks[i];
            if (block == null || block.action != action) continue;
            if (iteration < 0 || block.iteration == iteration) return true;
        }
        return false;
    }
}

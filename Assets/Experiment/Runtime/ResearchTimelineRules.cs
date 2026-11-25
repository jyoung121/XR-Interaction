public static class ResearchTimelineRules
{
    public static double AppleFinishSeconds(ScenarioProfile profile, float scenarioSeconds)
    {
        ScenarioAnimationBlock rest = null;
        foreach (var block in profile.animationBlocks)
            if (block.action == ScenarioAnimationAction.AppleRest &&
                block.startSeconds + block.durationSeconds > scenarioSeconds &&
                (rest == null || block.startSeconds < rest.startSeconds)) rest = block;
        return rest == null ? profile.animationDurationSeconds : rest.startSeconds + rest.durationSeconds;
    }
}

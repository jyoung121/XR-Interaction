using UnityEngine;

public sealed class MirrorScenarioAnimationDriver : ScenarioAnimationDriver
{
    public Transform viewer;
    private MirrorRig rig;
    private ScenarioProfile activeProfile;

    public override void BeginScenario(ScenarioProfile profile)
    {
        StopScenario();
        if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
        if (viewer == null)
        {
            Debug.LogError("[ScenarioAnimation] A viewer camera is required.");
            return;
        }

        rig = MirrorRig.Create(viewer);
        if (rig == null) return;
        activeProfile = profile;
        rig.UseExternalAnimationTimeline(true);

        switch (profile.scenario)
        {
            case ScenarioId.Apple:
                rig.ShowScenario(MirrorRig.Scenario.Food,
                    MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
                break;
            case ScenarioId.DentalDrill:
                rig.ShowScenario(MirrorRig.Scenario.Dental,
                    MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
                break;
            case ScenarioId.Toothbrush:
                rig.ShowScenario(MirrorRig.Scenario.Dental,
                    MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Brush);
                break;
            default:
                Debug.LogError($"[ScenarioAnimation] Unsupported scenario: {profile.scenario}");
                StopScenario();
                break;
        }
    }

    public override void EvaluateTimeline(ScenarioProfile profile, float scenarioSeconds)
    {
        if (rig == null || profile == null) return;
        if (activeProfile != profile)
        {
            BeginScenario(profile);
            if (rig == null) return;
        }

        if (ScenarioAnimationTimeline.TrySample(profile, scenarioSeconds,
            out ScenarioBlockSample sample))
            rig.EvaluateAnimationBlock(sample.action, sample.sourceSeconds,
                sample.normalizedTime, sample.block.iteration, sample.contactLoad,
                sample.minimumContactDepth, sample.maximumContactDepth,
                sample.block.useContactLoadCurve);
    }

    public override void StopScenario()
    {
        activeProfile = null;
        if (rig != null)
        {
            rig.Dispose();
            rig = null;
        }
    }

    private void OnDisable() => StopScenario();
}

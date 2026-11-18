using UnityEngine;

public abstract class ScenarioAnimationDriver : MonoBehaviour
{
    public abstract void BeginScenario(ScenarioProfile profile);
    public virtual void BeginBlock(ScenarioAnimationBlock block) { }
    public virtual void EndBlock(ScenarioAnimationBlock block) { }
    public abstract void StopScenario();
}

public sealed class MirrorScenarioAnimationDriver : ScenarioAnimationDriver
{
    public Transform viewer; private MirrorRig rig;
    public override void BeginScenario(ScenarioProfile profile)
    {
        StopScenario(); if (viewer == null && Camera.main != null) viewer = Camera.main.transform;
        if (viewer == null) { Debug.LogError("[ScenarioAnimation] A viewer camera is required."); return; }
        rig = MirrorRig.Create(viewer); if (rig == null) return;
        if (profile.scenario == ScenarioId.Apple)
            rig.ShowScenario(MirrorRig.Scenario.Food, MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
        else if (profile.scenario == ScenarioId.DentalDrill)
            rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Drill);
        else
            rig.ShowScenario(MirrorRig.Scenario.Dental, MirrorRig.FoodKind.Apple, MirrorRig.DentalKind.Brush);
    }
    public override void StopScenario() { if (rig != null) { rig.Dispose(); rig = null; } }
    private void OnDisable() => StopScenario();
}

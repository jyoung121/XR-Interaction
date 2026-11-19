using UnityEngine;

public abstract class ScenarioAnimationDriver : MonoBehaviour
{
    public abstract void BeginScenario(ScenarioProfile profile);
    public virtual void BeginBlock(ScenarioAnimationBlock block) { }
    public virtual void EndBlock(ScenarioAnimationBlock block) { }
    public abstract void EvaluateTimeline(ScenarioProfile profile, float scenarioSeconds);
    public abstract void StopScenario();
}

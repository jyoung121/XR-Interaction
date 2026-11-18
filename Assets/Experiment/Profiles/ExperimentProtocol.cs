using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public sealed class ProtocolScenario { public ScenarioProfile scenario; [Min(1)] public int repetitions = 1; }

[CreateAssetMenu(fileName = "ExperimentProtocol", menuName = "Experiment/Protocol")]
public sealed class ExperimentProtocol : ScriptableObject
{
    [SerializeField] private string protocolId = "android-final";
    [SerializeField] private int version = 1;
    public RuntimePlatform targetPlatform = RuntimePlatform.Android;
    public List<ProtocolScenario> scenarios = new List<ProtocolScenario>();
    public List<ExperimentCondition> conditions = new List<ExperimentCondition>
    { ExperimentCondition.Baseline, ExperimentCondition.ResearcherHaptic };
    public bool randomizeOrder;
    public int randomSeed = 1;
    [Tooltip("Kept false until the participant-tuning flow is specified.")]
    public bool participantConditionEnabled;
    public string ProtocolId => string.IsNullOrWhiteSpace(protocolId) ? name : protocolId;
    public int Version => Mathf.Max(1, version);

    public IEnumerable<ExperimentCondition> ActiveConditions()
    {
        for (int i = 0; i < conditions.Count; i++)
        { var c = conditions[i]; if (c != ExperimentCondition.ParticipantHaptic || participantConditionEnabled) yield return c; }
    }
}

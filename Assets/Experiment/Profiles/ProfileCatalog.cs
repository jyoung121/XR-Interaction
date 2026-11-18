using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "ProfileCatalog", menuName = "Experiment/Profile Catalog")]
public sealed class ProfileCatalog : ScriptableObject
{
    public List<SoundProfile> sounds = new List<SoundProfile>();
    public List<HapticProfile> haptics = new List<HapticProfile>();
    public List<ScenarioProfile> scenarios = new List<ScenarioProfile>();
    public List<ExperimentProtocol> protocols = new List<ExperimentProtocol>();
    public ScenarioProfile FindScenario(ScenarioId id) => scenarios.Find(p => p != null && p.scenario == id);
}

using System.Collections.Generic;
using UnityEngine;

public sealed class ExperimentSessionController : MonoBehaviour
{
    public ExperimentProtocol protocol; public ScenarioPlaybackController player; public bool playOnStart = true;
    private readonly List<(ScenarioProfile scenario, ExperimentCondition condition)> trials = new List<(ScenarioProfile, ExperimentCondition)>();
    private int trialIndex = -1;
    private void Start() { if (player == null) player = FindAnyObjectByType<ScenarioPlaybackController>(); BuildTrials(); if (playOnStart) PlayNext(); }
    public void BuildTrials()
    {
        trials.Clear(); if (protocol == null) return;
        foreach (var condition in protocol.ActiveConditions())
            for (int i = 0; i < protocol.scenarios.Count; i++)
            { var e = protocol.scenarios[i]; if (e?.scenario == null) continue; for (int r = 0; r < Mathf.Max(1, e.repetitions); r++) trials.Add((e.scenario, condition)); }
        if (protocol.randomizeOrder)
        { var rng = new System.Random(protocol.randomSeed); for (int i = trials.Count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = trials[i]; trials[i] = trials[j]; trials[j] = t; } }
        trialIndex = -1;
    }
    public void PlayNext()
    {
        if (player == null || trials.Count == 0) return; trialIndex++;
        if (trialIndex >= trials.Count) { player.Stop(); Debug.Log("[Experiment] Session complete."); return; }
        var t = trials[trialIndex]; player.Play(t.scenario, t.condition);
        Debug.Log($"[Experiment] Trial {trialIndex + 1}/{trials.Count}: {t.scenario.scenario}, {t.condition}");
    }
    private void Update()
    {
        bool advance = Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        if (!advance && Input.touchCount > 0) advance = Input.GetTouch(0).phase == TouchPhase.Began;
        if (advance && (player == null || !player.IsPlaying)) PlayNext();
    }
}

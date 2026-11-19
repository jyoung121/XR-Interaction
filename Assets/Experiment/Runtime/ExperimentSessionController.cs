using System;
using System.Collections.Generic;
using UnityEngine;

public enum ExperimentSessionState
{
    ConditionSelection,
    Running,
    BetweenTrials,
    Completed,
    Error
}

public sealed class ExperimentSessionController : MonoBehaviour
{
    public event Action<ExperimentSessionState> StateChanged;
    public event Action<int, int, ScenarioProfile> TrialChanged;

    public ExperimentProtocol protocol;
    public ScenarioPlaybackController player;
    [HideInInspector] public bool playOnStart;

    private readonly List<(ScenarioProfile scenario, ExperimentCondition condition)> trials =
        new List<(ScenarioProfile, ExperimentCondition)>();
    private int trialIndex = -1;
    private ExperimentCondition selectedCondition = ExperimentCondition.Baseline;
    private ExperimentSessionState state = ExperimentSessionState.ConditionSelection;
    private string lastError = string.Empty;

    public ExperimentSessionState State => state;
    public ExperimentCondition SelectedCondition => selectedCondition;
    public int TrialIndex => trialIndex;
    public int TrialCount => trials.Count;
    public string LastError => lastError;

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<ScenarioPlaybackController>();
        if (player != null) player.PlaybackCompleted += HandlePlaybackCompleted;
    }

    private void Start()
    {
        player?.Stop();
        SetState(ExperimentSessionState.ConditionSelection);
        if (GetComponent<ExperimentConditionSelectionUI>() == null)
            gameObject.AddComponent<ExperimentConditionSelectionUI>();
    }

    private void OnDestroy()
    {
        if (player != null) player.PlaybackCompleted -= HandlePlaybackCompleted;
    }

    public bool CanStartCondition(ExperimentCondition condition, out string reason)
    {
        if (protocol == null)
        {
            reason = "Experiment Protocol is missing.";
            return false;
        }
        return protocol.TryValidatePlan(condition, out reason);
    }

    public bool StartCondition(ExperimentCondition condition)
    {
        if (!CanStartCondition(condition, out string reason))
        {
            Fail(reason);
            return false;
        }

        selectedCondition = condition;
        BuildTrials(condition);
        if (trials.Count == 0)
        {
            Fail($"{ExperimentProtocol.DisplayName(condition)} has no trials.");
            return false;
        }

        trialIndex = -1;
        lastError = string.Empty;
        SetState(ExperimentSessionState.Running);
        return PlayNext();
    }

    public bool RepeatSelectedCondition() => StartCondition(selectedCondition);

    public void ReturnToConditionSelection()
    {
        player?.Stop();
        trials.Clear();
        trialIndex = -1;
        lastError = string.Empty;
        SetState(ExperimentSessionState.ConditionSelection);
    }

    public void BuildTrials(ExperimentCondition condition)
    {
        trials.Clear();
        ExperimentConditionPlan plan = protocol != null ? protocol.GetPlan(condition) : null;
        if (plan?.scenarios == null) return;

        IReadOnlyList<ScenarioId> order = protocol.ScenarioOrder;
        for (int scenarioIndex = 0; scenarioIndex < order.Count; scenarioIndex++)
        {
            ConditionScenarioAssignment assignment = plan.Find(order[scenarioIndex]);
            if (assignment?.profile == null) continue;
            for (int repetition = 0; repetition < Mathf.Max(1, assignment.repetitions); repetition++)
                trials.Add((assignment.profile, condition));
        }

        if (protocol.randomizeOrder)
        {
            var rng = new System.Random(protocol.randomSeed);
            for (int i = trials.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (ScenarioProfile scenario, ExperimentCondition condition) swap = trials[i];
                trials[i] = trials[j];
                trials[j] = swap;
            }
        }
    }

    public bool PlayNext()
    {
        if (player == null || trials.Count == 0) return false;
        trialIndex++;
        if (trialIndex >= trials.Count)
        {
            player.Stop();
            SetState(ExperimentSessionState.Completed);
            Debug.Log($"[Experiment] {ExperimentProtocol.DisplayName(selectedCondition)} session complete.");
            return true;
        }

        (ScenarioProfile scenario, ExperimentCondition condition) trial = trials[trialIndex];
        if (!player.Play(trial.scenario, trial.condition))
        {
            Fail($"Could not play {trial.scenario.name}.");
            return false;
        }

        SetState(ExperimentSessionState.Running);
        TrialChanged?.Invoke(trialIndex + 1, trials.Count, trial.scenario);
        Debug.Log($"[Experiment] Trial {trialIndex + 1}/{trials.Count}: " +
                  $"{trial.scenario.scenario}, {trial.condition}, " +
                  $"profile={trial.scenario.ProfileId}, version={trial.scenario.Version}");
        return true;
    }

    private void HandlePlaybackCompleted()
    {
        if (state == ExperimentSessionState.Running)
            SetState(ExperimentSessionState.BetweenTrials);
    }

    private void Update()
    {
        if (state != ExperimentSessionState.BetweenTrials) return;
        bool advance = Input.GetKeyDown(KeyCode.Space) || Input.GetMouseButtonDown(0);
        if (!advance && Input.touchCount > 0)
            advance = Input.GetTouch(0).phase == TouchPhase.Began;
        if (advance) PlayNext();
    }

    private void Fail(string reason)
    {
        lastError = reason;
        player?.Stop();
        SetState(ExperimentSessionState.Error);
        Debug.LogError("[Experiment] " + reason);
    }

    private void SetState(ExperimentSessionState next)
    {
        if (state == next && next != ExperimentSessionState.ConditionSelection) return;
        state = next;
        StateChanged?.Invoke(state);
    }
}

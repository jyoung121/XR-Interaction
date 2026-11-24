using System;
using System.Collections.Generic;
using UnityEngine;

public enum ExperimentSessionState
{
    ConditionSelection,
    ReadyToStart,
    Running,
    Finishing,
    BetweenTrials,
    Completed,
    Aborted,
    Error
}

public sealed class ExperimentSessionController : MonoBehaviour
{
    public event Action<ExperimentSessionState> StateChanged;
    public event Action<int, int, ScenarioProfile> TrialChanged;

    public ExperimentProtocol protocol;
    public ScenarioPlaybackController player;
    [HideInInspector] public bool playOnStart;

    private readonly List<(ScenarioProfile scenario, ExperimentCondition condition,
        HapticProfile haptic)> trials =
        new List<(ScenarioProfile, ExperimentCondition, HapticProfile)>();
    private int trialIndex = -1;
    private ExperimentCondition selectedCondition = ExperimentCondition.Baseline;
    private ExperimentSessionState state = ExperimentSessionState.ConditionSelection;
    private string lastError = string.Empty;
    private bool returnToLauncherWhenComplete;
    private ScenarioProfile currentScenario;
    private HapticProfile currentHaptic;
    private double readyRealtime;
    private double startRealtime;

    public ExperimentSessionState State => state;
    public ExperimentCondition SelectedCondition => selectedCondition;
    public int TrialIndex => trialIndex;
    public int TrialCount => trials.Count;
    public string LastError => lastError;
    public ScenarioProfile CurrentScenario => currentScenario;
    public bool IsInteractiveTrial => currentScenario != null && currentScenario.RequiresUserStart;
    public bool CanFinishCurrentTrial => state == ExperimentSessionState.Running &&
        player != null && player.CanAcceptFinishInput;

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<ScenarioPlaybackController>();
        if (player != null) player.PlaybackCompleted += HandlePlaybackCompleted;
    }

    private void Start()
    {
        player?.Stop();
        MobileAppFlowController flow = MobileAppFlowController.Instance;
        if (flow != null && flow.TryGetSelectedTrial(out ScenarioProfile selectedProfile,
                out ExperimentCondition condition, out _, out HapticProfile selectedHaptic,
                out _, out float runtimeOffset, out float runtimeAmplitude))
        {
            ExperimentConditionSelectionUI existingUi = GetComponent<ExperimentConditionSelectionUI>();
            if (existingUi != null) existingUi.SetSuppressed(true);
            player.runtimeHapticOffsetSeconds = runtimeOffset;
            player.runtimeAmplitudeScale = runtimeAmplitude;
            StartTrial(selectedProfile, condition, selectedHaptic, true);
            return;
        }
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
        if (condition == ExperimentCondition.ParticipantHaptic)
        {
            reason = "Choose Profile A, B, or C in the mobile Experiment Setup.";
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

    public bool StartTrial(ScenarioProfile scenario, ExperimentCondition condition,
                           bool returnToLauncher = false)
        => StartTrial(scenario, condition, null, returnToLauncher);

    public bool StartTrial(ScenarioProfile scenario, ExperimentCondition condition,
                           HapticProfile hapticOverride, bool returnToLauncher = false)
    {
        returnToLauncherWhenComplete = returnToLauncher;
        selectedCondition = condition;
        trials.Clear();
        trialIndex = 0;
        lastError = string.Empty;

        if (scenario == null)
        {
            Fail("Selected Scenario Profile is missing.");
            return false;
        }
        currentScenario = scenario;
        currentHaptic = hapticOverride;
        bool started = scenario.RequiresUserStart
            ? player != null && player.Prepare(scenario, condition, hapticOverride)
            : player != null && player.Play(scenario, condition, hapticOverride);
        if (!started)
        {
            Fail($"Could not play {scenario.name}.");
            return false;
        }

        if (scenario.RequiresUserStart)
        {
            readyRealtime = Time.realtimeSinceStartupAsDouble;
            SetState(ExperimentSessionState.ReadyToStart);
            Debug.Log($"[Experiment] Interactive trial ready: {scenario.ProfileId}, " +
                      $"condition={condition}.");
        }
        else
        {
            startRealtime = Time.realtimeSinceStartupAsDouble;
            SetState(ExperimentSessionState.Running);
        }
        TrialChanged?.Invoke(1, 1, scenario);
        Debug.Log($"[Experiment] Manual trial: {scenario.scenario}, {condition}, " +
                  $"profile={scenario.ProfileId}, version={scenario.Version}, " +
                  $"haptic={(hapticOverride != null ? hapticOverride.ProfileId : "none")}");
        return true;
    }

    public void ReturnToConditionSelection()
    {
        player?.Stop();
        trials.Clear();
        trialIndex = -1;
        currentScenario = null;
        currentHaptic = null;
        lastError = string.Empty;
        SetState(ExperimentSessionState.ConditionSelection);
    }

    public bool StartPreparedTrial()
    {
        if (state != ExperimentSessionState.ReadyToStart || player == null ||
            !player.StartPrepared())
            return false;

        startRealtime = Time.realtimeSinceStartupAsDouble;
        SetState(ExperimentSessionState.Running);
        Debug.Log($"[Experiment] Interactive start: scenario={currentScenario?.ProfileId}, " +
                  $"readyWait={startRealtime - readyRealtime:F3}s.");
        return true;
    }

    public bool RequestFinishCurrentTrial()
    {
        if (!CanFinishCurrentTrial || !player.RequestFinish()) return false;
        double now = Time.realtimeSinceStartupAsDouble;
        SetState(ExperimentSessionState.Finishing);
        Debug.Log($"[Experiment] Interactive finish requested: " +
                  $"scenario={currentScenario?.ProfileId}, active={now - startRealtime:F3}s.");
        return true;
    }

    public void AbortCurrentTrial(string reason = "Controller abort")
    {
        if (state != ExperimentSessionState.ReadyToStart &&
            state != ExperimentSessionState.Running &&
            state != ExperimentSessionState.Finishing)
            return;

        player?.Stop();
        lastError = reason;
        SetState(ExperimentSessionState.Aborted);
        Debug.LogWarning($"[Experiment] Trial aborted: {reason}");
        if (returnToLauncherWhenComplete && MobileAppFlowController.Instance != null)
            MobileAppFlowController.Instance.ReturnToLauncher(reason);
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
            HapticProfile haptic = condition == ExperimentCondition.ResearcherHaptic
                ? assignment.hapticProfile : null;
            for (int repetition = 0; repetition < Mathf.Max(1, assignment.repetitions); repetition++)
                trials.Add((assignment.profile, condition, haptic));
        }

        if (protocol.randomizeOrder)
        {
            var rng = new System.Random(protocol.randomSeed);
            for (int i = trials.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (ScenarioProfile scenario, ExperimentCondition condition, HapticProfile haptic) swap = trials[i];
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

        (ScenarioProfile scenario, ExperimentCondition condition, HapticProfile haptic) trial = trials[trialIndex];
        currentScenario = trial.scenario;
        currentHaptic = trial.haptic;
        bool started = trial.scenario.RequiresUserStart
            ? player.Prepare(trial.scenario, trial.condition, trial.haptic)
            : player.Play(trial.scenario, trial.condition, trial.haptic);
        if (!started)
        {
            Fail($"Could not play {trial.scenario.name}.");
            return false;
        }

        if (trial.scenario.RequiresUserStart)
        {
            readyRealtime = Time.realtimeSinceStartupAsDouble;
            SetState(ExperimentSessionState.ReadyToStart);
        }
        else
        {
            startRealtime = Time.realtimeSinceStartupAsDouble;
            SetState(ExperimentSessionState.Running);
        }
        TrialChanged?.Invoke(trialIndex + 1, trials.Count, trial.scenario);
        Debug.Log($"[Experiment] Trial {trialIndex + 1}/{trials.Count}: " +
                  $"{trial.scenario.scenario}, {trial.condition}, " +
                  $"profile={trial.scenario.ProfileId}, version={trial.scenario.Version}");
        return true;
    }

    private void HandlePlaybackCompleted()
    {
        if (state != ExperimentSessionState.Running &&
            state != ExperimentSessionState.Finishing) return;
        if (returnToLauncherWhenComplete)
        {
            player?.Stop();
            MobileAppFlowController flow = MobileAppFlowController.Instance;
            if (flow != null) flow.ReturnToLauncher();
            else SetState(ExperimentSessionState.Completed);
            return;
        }
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
        if (returnToLauncherWhenComplete && MobileAppFlowController.Instance != null)
            MobileAppFlowController.Instance.ReturnToLauncher(reason);
    }

    private void SetState(ExperimentSessionState next)
    {
        if (state == next && next != ExperimentSessionState.ConditionSelection) return;
        state = next;
        StateChanged?.Invoke(state);
    }
}

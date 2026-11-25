using System;
using UnityEngine;

public enum ResearchTrialPhase { Preparing, Idle, Acting, Finishing, Ending, Complete, Aborted }

/// <summary>Observation deadline is independent of episodes. Passive issues exactly one ON.</summary>
public sealed class ResearchTrialController : MonoBehaviour
{
    public ResearchTrialPhase Phase { get; private set; }
    public ResearchLogRecorder Recorder { get; private set; }
    public ResearchMode Mode { get; private set; }
    public ResearchHaptic Haptic { get; private set; }
    public double Remaining => Math.Max(0, deadline - Time.realtimeSinceStartupAsDouble);
    private ResearchScenarioSettings settings;
    private ResearchExperimentSettings protocol;
    private ScenarioPlaybackController player;
    private double readyAt, deadline, abortHeldAt = -1;
    private bool initialized, ending, finished;
    private string lastPrompt;
    public ResearchRecordingPurpose Purpose { get; private set; }
    public bool RecordingRequested { get; private set; }

    public void Initialize(ResearchScenarioSettings scenario, ResearchMode mode, ResearchHaptic haptic,
        ResearchExperimentSettings experiment, ScenarioPlaybackController playback,
        ResearchRecordingPurpose purpose = ResearchRecordingPurpose.Experiment, bool recordPreview = false)
    {
        Purpose = purpose;
        RecordingRequested = purpose == ResearchRecordingPurpose.Experiment || recordPreview;
        settings = scenario; Mode = mode; Haptic = haptic; protocol = experiment; player = playback;
        Recorder = gameObject.AddComponent<ResearchLogRecorder>();
        Recorder.player = player;
        Recorder.Failed += OnLogFailure;
        player.PlaybackCompleted += OnEpisodeComplete;
        player.ResearchEvent += OnPlaybackEvent;
        if (settings == null || !settings.Validate(out string reason))
        { Abort("Invalid scenario settings"); return; }
        if (protocol.requireController && !ResearchController.Ready)
        { Abort("Controller buttons must be checked in Launcher"); return; }
        if (!string.IsNullOrEmpty(settings.frozenSoundBasedHash) &&
            ResearchLogRecorder.HapticHash(settings.soundBased) != settings.frozenSoundBasedHash)
        { Abort("Frozen Sound-based profile changed. Review it in Experiment Configuration."); return; }
        if (RecordingRequested && !Recorder.Begin(settings, mode, haptic, protocol.requestedImuHz,
                purpose == ResearchRecordingPurpose.ScenarioPreview ? "scenario_preview" : "trial", purpose))
        { Abort("Logging could not start: " + Recorder.Error); return; }
        if (!Prepare()) { Abort("Could not prepare scenario"); return; }
        if (Camera.main != null) DebugHud.Create(Camera.main.transform);
        SetPhase(ResearchTrialPhase.Preparing);
        readyAt = Time.realtimeSinceStartupAsDouble + 1d;
        initialized = true;
    }
    private bool Prepare() => player.PrepareResearch(settings.scenario,
        Haptic == ResearchHaptic.None ? ExperimentCondition.Baseline : ExperimentCondition.ResearcherHaptic,
        settings.Resolve(Haptic));
    private void Update()
    {
        if (!initialized || finished) return;
        ResearchController.Refresh();
        if (protocol.requireController && !ResearchController.Ready)
        { Abort("controller_disconnected_or_reconfigured"); return; }
        if (ResearchController.AbortHeld)
        {
            if (abortHeldAt < 0) abortHeldAt = Time.realtimeSinceStartupAsDouble;
            if (Time.realtimeSinceStartupAsDouble - abortHeldAt >= 1d) { Abort("controller_abort"); return; }
        }
        else abortHeldAt = -1;

        double now = Time.realtimeSinceStartupAsDouble;
        if (Phase == ResearchTrialPhase.Preparing)
        {
            if (now < readyAt) { Prompt("PREPARING..."); return; }
            deadline = now + settings.observationSeconds;
            Recorder.Event("observation_start", settings.observationSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetPhase(ResearchTrialPhase.Idle);
            if (Mode == ResearchMode.Passive) Activate("automatic_once");
        }
        if (!ending && now >= deadline)
        {
            ending = true;
            Recorder.Event("nominal_end");
            if (Phase == ResearchTrialPhase.Idle) { Complete(); return; }
            if (Phase == ResearchTrialPhase.Acting) Deactivate("deadline");
            if (finished) return;
            SetPhase(ResearchTrialPhase.Ending);
        }
        if (!ending && Mode == ResearchMode.Active && ResearchController.PrimaryPressed)
        {
            if (Phase == ResearchTrialPhase.Idle) Activate("button_a");
            else if (Phase == ResearchTrialPhase.Acting) Deactivate("button_a");
            else Recorder.Event("input_ignored", "A during finishing");
        }
        Prompt((Purpose == ResearchRecordingPurpose.ScenarioPreview ? "PREVIEW\n" : "") +
            (ending ? "FINISHING..." : Mode == ResearchMode.Passive ? "PASSIVE" :
            Phase == ResearchTrialPhase.Idle ? "PRESS A TO ACT" :
            Phase == ResearchTrialPhase.Acting ? "PRESS A TO STOP" : "FINISHING...") +
            "\n" + Remaining.ToString("F0") + " s");
    }
    private void Activate(string source)
    {
        Recorder.Episode++;
        Recorder.Event("on_request", source);
        if (finished) return;
        player.runtimeHapticOffsetSeconds = 0;
        player.runtimeAmplitudeScale = 1f;
        if (!player.BeginResearch(settings.scenario, settings.Resolve(Haptic)))
        { Abort("Playback failed"); return; }
        Recorder.Event("episode_scheduled", "clock_start_dsp=" + player.ClockStartDsp.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        SetPhase(ResearchTrialPhase.Acting);
    }
    private void Deactivate(string source)
    {
        Recorder.Event("off_request", source);
        if (finished) return;
        if (!player.FinishResearch()) { Abort("Could not finish current episode"); return; }
        Recorder.Event("off_accepted", source);
        // Cancellation may complete synchronously and restore Idle (or end the trial).
        if (!finished && player.IsPlaying) SetPhase(ResearchTrialPhase.Finishing);
    }
    private void OnEpisodeComplete()
    {
        if (finished) return;
        Recorder.Event("episode_inactive");
        if (ending) { Complete(); return; }
        if (!Prepare()) { Abort("Could not restore idle pose"); return; }
        SetPhase(ResearchTrialPhase.Idle);
    }
    private void OnPlaybackEvent(string kind, string value) => Recorder?.Event(kind, value);
    private void OnLogFailure(string error) => Abort("logger_error: " + error);
    private void SetPhase(ResearchTrialPhase next)
    {
        Phase = next;
        if (Recorder != null) { Recorder.Phase = next.ToString(); Recorder.Event("trial_state", next.ToString()); }
    }
    private void Prompt(string text)
    {
        if (lastPrompt == text) return;
        lastPrompt = text; DebugHud.Set(text); DebugHud.Show(true);
    }
    private void Complete()
    {
        if (finished) return;
        finished = true;
        SetPhase(ResearchTrialPhase.Complete);
        player.Stop();
        Recorder.End("completed", true);
        Return(!string.IsNullOrEmpty(Recorder.Error) ? "LOG ERROR: " + Recorder.Error :
            Purpose == ResearchRecordingPurpose.ScenarioPreview ? "Preview completed" + (RecordingRequested ? "; preview log saved" : "; no log recorded") :
            "Trial completed; recording saved");
    }
    public void Abort(string reason)
    {
        if (finished) return;
        finished = true;
        SetPhase(ResearchTrialPhase.Aborted);
        player?.Stop();
        Recorder?.End(reason, false);
        Return("Trial aborted: " + reason);
    }
    private void Return(string message)
    {
        DebugHud.Show(false);
        if (MobileAppFlowController.Instance != null) MobileAppFlowController.Instance.ReturnToLauncher(message);
    }
    private void OnApplicationPause(bool pause) { if (pause && initialized && !finished) Abort("app_pause"); }
    private void OnDestroy()
    {
        if (player != null) { player.PlaybackCompleted -= OnEpisodeComplete; player.ResearchEvent -= OnPlaybackEvent; }
        if (Recorder != null) { Recorder.Failed -= OnLogFailure; if (Recorder.Recording) Recorder.End("scene_unloaded", false); }
    }
}

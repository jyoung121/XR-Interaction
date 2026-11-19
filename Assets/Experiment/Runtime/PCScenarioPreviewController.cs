using System.Collections;
using UnityEngine;

/// <summary>PC-only preview harness. Repeats complete synchronized scenarios, not individual clips.</summary>
public sealed class PCScenarioPreviewController : MonoBehaviour
{
    public ScenarioPlaybackController player;
    public ScenarioProfile profile;
    public bool autoStart = true;
    public bool repeat;
    [Min(0f)] public float repeatDelaySeconds;

    private Coroutine repeatRoutine;

    private void Awake()
    {
        if (player == null) player = GetComponent<ScenarioPlaybackController>();
        if (player != null) player.PlaybackCompleted += HandlePlaybackCompleted;
    }

    private void Start()
    {
        if (autoStart) PlayOnce();
    }

    private void OnDestroy()
    {
        if (player != null) player.PlaybackCompleted -= HandlePlaybackCompleted;
    }

    public bool PlayOnce()
    {
        if (repeatRoutine != null)
        {
            StopCoroutine(repeatRoutine);
            repeatRoutine = null;
        }
        if (player == null || profile == null) return false;
        return player.Play(profile, ExperimentCondition.Baseline);
    }

    public void StopPreview()
    {
        if (repeatRoutine != null)
        {
            StopCoroutine(repeatRoutine);
            repeatRoutine = null;
        }
        player?.Stop();
    }

    private void HandlePlaybackCompleted()
    {
        if (repeat && isActiveAndEnabled)
            repeatRoutine = StartCoroutine(RepeatAfterDelay());
    }

    private IEnumerator RepeatAfterDelay()
    {
        if (repeatDelaySeconds > 0f)
            yield return new WaitForSecondsRealtime(repeatDelaySeconds);
        repeatRoutine = null;
        PlayOnce();
    }

    private void OnDisable() => StopPreview();
}

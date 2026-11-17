using System.Collections;
using UnityEngine;

/// <summary>
/// Plays a generated profile with a manually calibrated audio/haptic offset.
/// Positive offset means haptics start after audio; negative means haptics start first.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public sealed class AudioHapticPlayer : MonoBehaviour
{
    public HapticProfile profile;
    public bool playOnStart;

    private AudioSource source;
    private Coroutine playback;

    private void Awake()
    {
        source = GetComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
    }

    private void Start()
    {
        if (playOnStart) Play();
    }

    public void Play()
    {
        Stop();
        if (profile == null)
        {
            Debug.LogWarning("[AudioHaptic] Cannot play: Profile is missing.");
            return;
        }
        if (!profile.IsPlayable(out string reason))
        {
            Debug.LogWarning("[AudioHaptic] Cannot play: " + reason);
            return;
        }
        playback = StartCoroutine(PlayRoutine());
    }

    public void Stop()
    {
        if (playback != null)
        {
            StopCoroutine(playback);
            playback = null;
        }
        if (source == null) source = GetComponent<AudioSource>();
        if (source != null) source.Stop();
        HapticManager.StopWaveform();
    }

    private IEnumerator PlayRoutine()
    {
        source.clip = profile.sourceSound != null ? profile.sourceSound.clip : null;
        if (source.clip == null)
        {
            Debug.LogWarning("[AudioHaptic] Cannot play: source SoundProfile is missing.");
            playback = null;
            yield break;
        }
        int offset = profile.hapticOffsetMs;

        if (offset < 0)
        {
            PlayHaptics();
            yield return new WaitForSecondsRealtime(-offset / 1000f);
            source.Play();
        }
        else
        {
            source.Play();
            if (offset > 0) yield return new WaitForSecondsRealtime(offset / 1000f);
            PlayHaptics();
        }

        playback = null;
    }

    private void PlayHaptics()
    {
        HapticManager.PlayWaveform(profile.CreateTimings(), profile.CreateScaledAmplitudes());
    }

    private void OnDisable()
    {
        Stop();
    }
}

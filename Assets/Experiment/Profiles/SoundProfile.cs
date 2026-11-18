using UnityEngine;

[CreateAssetMenu(fileName = "SoundProfile", menuName = "Experiment/Sound Profile")]
public sealed class SoundProfile : ScriptableObject
{
    [SerializeField] private string profileId = "sound";
    [SerializeField] private int version = 1;
    public AudioClip clip;
    [Range(0f, 1f)] public float volume = 1f;
    [Min(0f)] public float trimStartSeconds;
    [Min(0f)] public float trimEndSeconds;
    [Min(0f)] public float fadeInSeconds;
    [Min(0f)] public float fadeOutSeconds;
    public bool loop;
    public bool placeholder;
    [TextArea] public string sourceNote;
    [HideInInspector] public string sourceAssetHash;

    public string ProfileId => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
    public int Version => Mathf.Max(1, version);
    public double PlayableDurationSeconds => clip == null ? 0d :
        System.Math.Max(0d, clip.length - trimStartSeconds - trimEndSeconds);

    public bool IsUsable(out string reason)
    {
        if (clip == null) { reason = $"Sound profile '{name}' has no AudioClip."; return false; }
        if (PlayableDurationSeconds <= 0d) { reason = $"Sound profile '{name}' is completely trimmed."; return false; }
        reason = string.Empty;
        return true;
    }

#if UNITY_EDITOR
    public void SetIdentity(string id, int nextVersion = 1)
    {
        profileId = string.IsNullOrWhiteSpace(id) ? name : id.Trim();
        version = Mathf.Max(1, nextVersion);
    }
#endif
}

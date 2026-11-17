using System;
using UnityEngine;

/// <summary>
/// Backward-compatible asset type for profiles created by the first converter prototype.
/// New generators save HapticProfile assets.
/// </summary>
[Obsolete("Use HapticProfile. This type is kept so existing assets continue to deserialize.")]
[CreateAssetMenu(fileName = "LegacyAudioHapticProfile", menuName = "Haptics/Legacy Audio Haptic Profile")]
public sealed class AudioHapticProfile : HapticProfile
{
    public AudioClip sourceAudio;
}

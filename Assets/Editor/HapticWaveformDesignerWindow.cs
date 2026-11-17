using UnityEditor;
using UnityEngine;

/// <summary>
/// Compatibility entry point for the former all-in-one designer.
/// New work is intentionally split between the manual and sound-based generators.
/// </summary>
public sealed class HapticWaveformDesignerWindow : EditorWindow
{
    [MenuItem("Tools/Haptics/Legacy Waveform Designer")]
    private static void Open()
    {
        GetWindow<HapticWaveformDesignerWindow>("Haptic Generators");
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "The waveform designer was split into two focused tools. Existing profiles remain compatible.",
            MessageType.Info);

        if (GUILayout.Button("Open Manual Haptic Generator", GUILayout.Height(36)))
            ManualHapticGeneratorWindow.OpenWindow();

        if (GUILayout.Button("Open Sound-based Haptic Generator", GUILayout.Height(36)))
            SoundBasedHapticGeneratorWindow.OpenWindow();
    }
}

using System;
using UnityEditor;
using UnityEngine;

/// <summary>Saved-asset playback reuses the existing ADB waveform transport.</summary>
public sealed class HapticProfilePlayerWindow : EditorWindow
{
    [SerializeField] private HapticProfile profile;
    private string status = "Open PC Haptic Receiver on the phone, then ADB Forward.";
    private Vector2 scroll;
    [MenuItem("Tools/Haptics/Haptic Profile Player", false, 20)]
    public static void OpenWindow() => GetWindow<HapticProfilePlayerWindow>("Haptic Player");
    public static void OpenProfile(HapticProfile selected)
    {
        var window = GetWindow<HapticProfilePlayerWindow>("Haptic Player");
        window.profile = selected; window.Show();
    }
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("Saved HapticProfile -> Android", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("No Unity Play Mode required. This sends the selected saved waveform to the phone; it never changes the APK's experiment assignments.", MessageType.Info);
        profile = (HapticProfile)EditorGUILayout.ObjectField("Haptic Profile", profile, typeof(HapticProfile), false);
        string reason = "Select a saved HapticProfile.";
        bool valid = profile != null && AssetDatabase.Contains(profile) && profile.IsPlayable(out reason);
        if (profile != null)
        {
            EditorGUILayout.SelectableLabel(AssetDatabase.GetAssetPath(profile), GUILayout.Height(20));
            EditorGUILayout.LabelField(profile.ProfileId + " / v" + profile.Version + " / " + profile.DurationMs + " ms");
            EditorGUILayout.LabelField("Scale " + profile.defaultAmplitudeScale + " / offset " + profile.hapticOffsetMs + " ms / loop " + profile.loop);
            EditorGUILayout.SelectableLabel("SHA256 " + ResearchLogRecorder.HapticHash(profile), GUILayout.Height(20));
            if (EditorUtility.IsDirty(profile)) { valid = false; reason = "Asset has unsaved changes. Save it before saved-profile playback; draft buttons remain available in the generators."; }
            if (GUILayout.Button("Select Asset")) Selection.activeObject = profile;
            DrawWaveform(profile);
        }
        if (!valid) EditorGUILayout.HelpBox(reason, MessageType.Warning);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("ADB Forward")) Run(HapticTransportClient.ForwardAdb);
            if (GUILayout.Button("Check Receiver")) Run(HapticTransportClient.CheckReceiver);
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!valid))
                if (GUILayout.Button("Play on Android")) Run(() => HapticTransportClient.Play(profile));
            if (GUILayout.Button("Stop")) Run(HapticTransportClient.Stop);
        }
        EditorGUILayout.HelpBox(status, MessageType.Info);
        EditorGUILayout.EndScrollView();
    }
    private void Run(Func<string> action) { try { status = action(); } catch (Exception exception) { status = exception.Message; } }
    private static void DrawWaveform(HapticProfile value)
    {
        if (value.DurationMs <= 0) return;
        Rect rect = GUILayoutUtility.GetRect(100, 90, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.1f, .1f, .1f));
        double time = 0;
        for (int i = 0; i < value.SegmentCount; i++)
        {
            float x = rect.x + (float)(time / value.DurationMs) * rect.width;
            float width = (float)((double)value.DurationsMs[i] / value.DurationMs) * rect.width;
            float height = Mathf.Clamp01(value.Amplitudes[i] * value.defaultAmplitudeScale / 255f) * rect.height;
            EditorGUI.DrawRect(new Rect(x, rect.yMax - height, Mathf.Max(.5f, width), height), Color.cyan);
            time += value.DurationsMs[i];
        }
    }
}

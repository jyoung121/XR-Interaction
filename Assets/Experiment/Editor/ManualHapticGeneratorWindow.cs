using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class ManualHapticGeneratorWindow : EditorWindow
{
    private const string Directory = "Assets/Profiles/Haptics/Manual";
    private const string VariantDirectory = "Assets/Profiles/Haptics/Variants";
    [SerializeField] private HapticProfile profile; [SerializeField] private string durations = "80, 40, 120, 40, 200"; [SerializeField] private string amplitudes = "255, 0, 180, 0, 255"; [SerializeField] private bool researcherCalibrated;
    private Vector2 scroll; private string status = "Enter arrays directly or load a generated profile for manual correction.";
    [MenuItem("Tools/Haptics/Manual Haptic Generator")] public static void OpenWindow() => GetWindow<ManualHapticGeneratorWindow>("Manual Haptic");
    public static void EditProfile(HapticProfile target) { var w = GetWindow<ManualHapticGeneratorWindow>("Manual Haptic"); w.profile = target; w.LoadProfile(); w.Show(); }
    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll); EditorGUILayout.LabelField("Manual Haptic Generator", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Direct array editing. Use this after sound-based generation to lock the researcher profile.", MessageType.Info);
        var next = (HapticProfile)EditorGUILayout.ObjectField("Profile", profile, typeof(HapticProfile), false); if (next != profile) { profile = next; LoadProfile(); }
        if (profile != null)
        {
            string path = AssetDatabase.GetAssetPath(profile);
            EditorGUILayout.SelectableLabel(path, EditorStyles.miniLabel,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Ping")) EditorGUIUtility.PingObject(profile);
                if (GUILayout.Button("Copy Path")) EditorGUIUtility.systemCopyBuffer = path;
                if (GUILayout.Button("Create Tuned Copy...")) CreateTunedCopy();
            }
            if (profile.isStyleGenerated)
                EditorGUILayout.HelpBox(
                    "This generated style is overwritten when styles are regenerated. " +
                    "Create a tuned copy before saving manual changes.", MessageType.Warning);
        }
        researcherCalibrated = EditorGUILayout.Toggle("Researcher calibrated", researcherCalibrated);
        durations = EditorGUILayout.TextField("Durations (ms)", durations); amplitudes = EditorGUILayout.TextField("Amplitudes", amplitudes);
        if (TryParse(out int[] d, out int[] a, out string error)) { long total = d.Aggregate<int, long>(0, (s, v) => s + v); int chunks = HapticWaveformChunker.Create(d.Select(v => (long)v).ToArray(), a).Count; EditorGUILayout.HelpBox($"{d.Length} segments / {total / 1000f:0.###} s / {chunks} chunk(s)", MessageType.None); } else EditorGUILayout.HelpBox(error, MessageType.Warning);
        using (new EditorGUILayout.HorizontalScope()) { if (GUILayout.Button("Create / Save Profile")) SaveProfile(); if (GUILayout.Button("ADB Forward")) Run(HapticTransportClient.ForwardAdb); }
        using (new EditorGUILayout.HorizontalScope()) { if (GUILayout.Button("Play on Android")) Play(); if (GUILayout.Button("Stop")) Run(HapticTransportClient.Stop); }
        EditorGUILayout.HelpBox(status, MessageType.None); EditorGUILayout.EndScrollView();
    }
    private void LoadProfile() { if (profile == null) return; durations = string.Join(", ", profile.DurationsMs); amplitudes = string.Join(", ", profile.Amplitudes); researcherCalibrated = profile.Origin == HapticProfileOrigin.ResearcherCalibrated; }
    private void SaveProfile()
    {
        if (!TryParse(out int[] d, out int[] a, out string error)) { status = error; return; }
        if (profile == null) { EnsureFolder(Directory); string path = EditorUtility.SaveFilePanelInProject("Save Haptic Profile", "ManualHapticProfile", "asset", "Save manually authored profile.", Directory); if (string.IsNullOrEmpty(path)) return; profile = CreateInstance<HapticProfile>(); AssetDatabase.CreateAsset(profile, path); }
        profile.SetWaveform(d, a, researcherCalibrated ? HapticProfileOrigin.ResearcherCalibrated : HapticProfileOrigin.Manual, profile.ProfileId, profile.Version); EditorUtility.SetDirty(profile); AssetDatabase.SaveAssets(); Selection.activeObject = profile; status = "Saved " + AssetDatabase.GetAssetPath(profile);
    }
    private void Play() { if (!TryParse(out int[] d, out int[] a, out string e)) { status = e; return; } Run(() => HapticTransportClient.Play("manual-preview", d.Select(v => (long)v).ToArray(), a)); }
    private void CreateTunedCopy()
    {
        if (profile == null) return;
        EnsureFolder(VariantDirectory);
        string source = AssetDatabase.GetAssetPath(profile);
        string suggested = profile.name + "_Tuned";
        string destination = EditorUtility.SaveFilePanelInProject("Create Tuned Haptic Copy",
            suggested, "asset", "Save a copy that style regeneration will not overwrite.",
            VariantDirectory);
        if (string.IsNullOrEmpty(destination)) return;
        if (!AssetDatabase.CopyAsset(source, destination))
        { status = "Could not copy " + source; return; }
        AssetDatabase.ImportAsset(destination);
        HapticProfile copy = AssetDatabase.LoadAssetAtPath<HapticProfile>(destination);
        if (copy == null) { status = "Copied asset could not be loaded."; return; }
        copy.MarkAsManualVariant(System.IO.Path.GetFileNameWithoutExtension(destination));
        EditorUtility.SetDirty(copy);
        AssetDatabase.SaveAssetIfDirty(copy);
        profile = copy;
        LoadProfile();
        Selection.activeObject = copy;
        status = "Created tuned copy: " + destination;
    }
    private bool TryParse(out int[] d, out int[] a, out string error)
    {
        try { d = durations.Split(',').Select(v => int.Parse(v.Trim())).ToArray(); a = amplitudes.Split(',').Select(v => int.Parse(v.Trim())).ToArray(); if (d.Length == 0 || d.Length != a.Length) throw new FormatException("Arrays must have equal non-zero lengths."); if (d.Any(v => v < 1 || v > 2000)) throw new FormatException("Durations must be 1-2000 ms."); if (a.Any(v => v < 0 || v > 255)) throw new FormatException("Amplitudes must be 0-255."); error = ""; return true; } catch (Exception ex) { d = Array.Empty<int>(); a = Array.Empty<int>(); error = ex.Message; return false; }
    }
    private void Run(Func<string> action) { try { status = action(); } catch (Exception ex) { status = ex.Message; } }
    internal static void EnsureFolder(string path) { string[] parts = path.Split('/'); string current = parts[0]; for (int i = 1; i < parts.Length; i++) { string next = current + "/" + parts[i]; if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]); current = next; } }
}

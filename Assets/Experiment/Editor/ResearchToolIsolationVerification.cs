using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>Verify generation/assignment separation without changing experiment assets.</summary>
public static class ResearchToolIsolationVerification
{
    public static void Run()
    {
        var protocol = ResearchExperimentSetup.Protocol;
        ResearchExperimentSetup.ValidateResearch();
        string protocolBefore = JsonUtility.ToJson(protocol);
        var assigned = protocol.research.scenarios[0];
        var originalSimple = assigned.simple;
        string simpleBefore = ResearchLogRecorder.HapticHash(originalSimple);
        string soundBefore = ResearchLogRecorder.HapticHash(assigned.soundBased);
        string unique = Guid.NewGuid().ToString("N");
        string path = ResearchExperimentSetup.SimpleFolder + "/_IsolationCheck_" + unique + ".asset";
        string defaultPath = ResearchExperimentSetup.SimpleFolder + "/_DefaultAnalysisCheck_" + unique + ".asset";
        HapticProfile result = null;
        ExperimentProtocol clone = null;
        try
        {
            var draft = SimplePulseDraft.FromAssignment(assigned);
            draft.amplitude = assigned.simpleAmplitude == 117 ? 118 : 117;
            Require(assigned.simpleAmplitude != draft.amplitude, "Draft modified assignment parameters");
            result = SimplePulseGeneratorWindow.GenerateNew(draft, path);
            Require(result != originalSimple && result != assigned.soundBased, "Generation overwrote an assigned profile");
            Require(AssetDatabase.GetAssetPath(result) == path, "New output path was not respected");
            Require(result.ProfileId == Path.GetFileNameWithoutExtension(path), "Output identity does not match selected asset name");
            Require(assigned.simple == originalSimple, "Generation automatically reassigned Simple");
            Require(JsonUtility.ToJson(protocol) == protocolBefore, "Generation modified protocol data");
            Require(ResearchLogRecorder.HapticHash(originalSimple) == simpleBefore, "Assigned Simple was modified");
            Require(ResearchLogRecorder.HapticHash(assigned.soundBased) == soundBefore, "Fixed Sound-based was modified");
            foreach (int value in result.Amplitudes) Require(value == 0 || value == draft.amplitude, "New pulse levels are not fixed");

            // Explicit assignment is a separate operation; old generator seeds must not reject it.
            clone = UnityEngine.Object.Instantiate(protocol);
            clone.research.scenarios[0].simple = result;
            ResearchExperimentSetup.ValidateProtocol(clone);
            Require(protocol.research.scenarios[0].simple == originalSimple, "Assignment clone changed original protocol");

            bool blocked = false;
            try { SimplePulseGeneratorWindow.GenerateNew(draft, path); }
            catch (ArgumentException) { blocked = true; }
            Require(blocked, "Generator allowed overwriting an existing asset");
            draft.analysisTemplate = null;
            var defaultResult = SimplePulseGeneratorWindow.GenerateNew(draft, defaultPath);
            Require(defaultResult != null, "Default analysis without a reference profile failed");
            Require(JsonUtility.ToJson(protocol) == protocolBefore, "Default analysis modified protocol data");
            Debug.Log("[ToolIsolation] PASS: independent draft, new-asset generation, unchanged experiment/profile bindings, explicit alternate Simple assignment, overwrite protection.");
        }
        finally
        {
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            // Only the uniquely named test artifact created above is removed.
            if (AssetDatabase.LoadMainAssetAtPath(path) != null) AssetDatabase.DeleteAsset(path);
            if (AssetDatabase.LoadMainAssetAtPath(defaultPath) != null) AssetDatabase.DeleteAsset(defaultPath);
        }
        Require(JsonUtility.ToJson(protocol) == protocolBefore, "Final protocol changed");
        ResearchExperimentSetup.ValidateResearch();
    }
    public static void OpenWindowsForReview()
    {
        EditorApplication.delayCall += () => {
            EditorApplication.ExecuteMenuItem("Tools/Experiment/Experiment Configuration");
            SimplePulseGeneratorWindow.OpenFor(ResearchExperimentSetup.Protocol.research.scenarios[0]);
        };
    }
    private static void Require(bool valid, string message) { if (!valid) throw new Exception(message); }
}

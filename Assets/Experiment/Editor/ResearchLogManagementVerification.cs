using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Deletion checks operate only on a newly created, uniquely named temp fixture.</summary>
public static class ResearchLogManagementVerification
{
    public static void Run()
    {
        string parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "XRLogManagementVerification"));
        string fixture = Path.GetFullPath(Path.Combine(parent, Guid.NewGuid().ToString("N")));
        Require(fixture.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Unsafe fixture path");
        string previousRoot = ResearchLogRecorder.TestRoot;
        ResearchLogRecorder.TestRoot = Path.Combine(fixture, "ResearchLogs");
        var firstOwner = new GameObject("First verification recorder");
        var secondOwner = new GameObject("Second verification recorder");
        var first = firstOwner.AddComponent<ResearchLogRecorder>();
        var second = secondOwner.AddComponent<ResearchLogRecorder>();
        try
        {
            Require(first.Begin(null, ResearchMode.Passive, ResearchHaptic.None, 100, "first", ResearchRecordingPurpose.SensorTest), "First recording failed");
            string firstPath = ResearchLogRecorder.LastDirectory;
            Require(second.Begin(null, ResearchMode.Passive, ResearchHaptic.None, 100, "second", ResearchRecordingPurpose.DirectHapticTest), "Second recording failed");
            string secondPath = ResearchLogRecorder.LastDirectory;
            var active = ResearchLogLibrary.Read();
            Require(active.Count == 2 && active.All(e => e.Active && !e.Manageable), "Active recording discovery/protection failed");
            Blocked(() => ResearchLogLibrary.Delete(firstPath));
            Blocked(() => ResearchLogRecorder.Export(firstPath));
            first.SendMessage("LateUpdate"); second.SendMessage("LateUpdate");
            first.End("sensor_test_complete", true);
            Require(File.Exists(Path.Combine(firstPath, "summary.json")) && !File.Exists(Path.Combine(secondPath, "summary.json")), "Recorder wrote to another recording's directory");
            Require(!ResearchLogRecorder.IsDirectoryActive(firstPath) && ResearchLogRecorder.IsDirectoryActive(secondPath), "Closing one recorder unlocked another");
            Require(ResearchLogRecorder.Export(firstPath).StartsWith("ZIP:"), "Saved export failed");
            Require(ResearchLogRecorder.Export(firstPath).StartsWith("ZIP:"), "Repeated export failed");
            string[] archives = Directory.GetFiles(Path.GetDirectoryName(firstPath), "*.zip");
            Require(archives.Length == 2, "ZIP fixtures missing");
            string unrelated = firstPath + "_other.zip"; File.WriteAllText(unrelated, "keep unrelated archive");
            string downloads = Path.Combine(fixture, "Downloads", "XRExperiment"); Directory.CreateDirectory(downloads);
            string exportedCopy = Path.Combine(downloads, "exported.zip"); File.Copy(archives[0], exportedCopy);
            string outside = Path.Combine(fixture, "outside"); Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "metadata.json"), "{}");
            Blocked(() => ResearchLogLibrary.Delete(outside));
            Blocked(() => ResearchLogLibrary.Delete(ResearchLogLibrary.Root));
            Blocked(() => ResearchLogLibrary.Delete(Path.GetDirectoryName(firstPath)));
            Blocked(() => ResearchLogLibrary.Delete(Path.Combine(ResearchLogLibrary.Root, "SensorTest", "..", "..", "outside")));

            var saved = ResearchLogLibrary.Read().Single(e => e.Path == firstPath);
            Require(saved.Manageable && saved.Status == "SAVED" && saved.Bytes > 0 && saved.Purpose == "SensorTest", "Saved listing metadata/status/size invalid");
            Require(ResearchLogLibrary.Delete(firstPath) > 0, "Deletion size missing");
            Require(!Directory.Exists(firstPath) && archives.All(p => !File.Exists(p)), "Recording or its ZIP caches survived deletion");
            Require(File.Exists(unrelated) && File.Exists(exportedCopy) && Directory.Exists(secondPath) && Directory.Exists(outside), "Deletion affected unrelated data");
            Blocked(() => ResearchLogLibrary.Delete(secondPath));
            second.End("aborted_test", false);
            Require(ResearchLogLibrary.Read().Single(e => e.Path == secondPath).Status == "ABORTED", "Aborted status lost");

            string partial = Path.Combine(ResearchLogLibrary.Root, "ScenarioPreview", "partial");
            Directory.CreateDirectory(partial); File.WriteAllText(Path.Combine(partial, "imu.csv"), "raw data without metadata");
            Require(ResearchLogLibrary.Read().Any(e => e.Path == partial && e.Status == "INCOMPLETE" && e.Purpose == "ScenarioPreview"), "Recoverable partial recording hidden or misclassified");
            ResearchLogLibrary.Delete(partial);
            string legacy = Path.Combine(ResearchLogLibrary.Root, "legacy-trial");
            Directory.CreateDirectory(legacy); File.WriteAllText(Path.Combine(legacy, "metadata.json"), "{broken json");
            Require(ResearchLogLibrary.Read().Any(e => e.Path == legacy && e.Manageable), "Legacy/corrupt recording cannot be managed");
            ResearchLogLibrary.Delete(legacy);
            Require(!Directory.Exists(partial) && !Directory.Exists(legacy), "Partial/legacy deletion failed");
            Debug.Log("[LogManagement] PASS: active protection, per-recorder directories, saved/repeated exports, scoped deletion, ZIP cleanup, Downloads/unrelated preservation, incomplete/aborted/legacy discovery.");
        }
        finally
        {
            first.End("verification_cleanup", false); second.End("verification_cleanup", false);
            UnityEngine.Object.DestroyImmediate(firstOwner); UnityEngine.Object.DestroyImmediate(secondOwner);
            ResearchLogRecorder.TestRoot = previousRoot;
            // Only the GUID fixture allocated above is removed; the real app log directory is never used.
            if (Directory.Exists(fixture)) Directory.Delete(fixture, true);
        }
        ResearchExperimentSetup.ValidateResearch();
    }
    private static void Blocked(Action action)
    {
        bool rejected = false;
        try { action(); } catch (IOException) { rejected = true; }
        Require(rejected, "Unsafe or active recording operation was allowed");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}

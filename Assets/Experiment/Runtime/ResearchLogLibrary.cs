using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public sealed class ResearchLogEntry
{
    public string Path, RelativePath, Purpose, Scenario, Mode, Condition, Status, Error;
    public DateTime StartedUtc;
    public long Bytes;
    public bool Active;
    public bool Manageable => !Active && string.IsNullOrEmpty(Error);
    public string Description => string.Join(" / ", new[] { Scenario, Mode, Condition });
}

/// <summary>Only recording directories beneath the app's log root may be exported or deleted.</summary>
public static class ResearchLogLibrary
{
    [Serializable] private sealed class Header
    {
        public string utcStart, purpose, scenario, mode, condition;
    }
    [Serializable] private sealed class Result { public bool completed; public string endReason; }
    private static StringComparison Comparison => Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    public static string Root => Path.GetFullPath(ResearchLogRecorder.Root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    public static List<ResearchLogEntry> Read()
    {
        var result = new List<ResearchLogEntry>();
        if (!Directory.Exists(Root)) return result;
        RejectLink(Root);
        foreach (string folder in Directory.GetDirectories(Root))
        {
            if (IsLink(folder)) continue;
            if (IsRecording(folder)) Add(result, folder);
            else foreach (string candidate in Directory.GetDirectories(folder))
                if (!IsLink(candidate) && IsRecording(candidate)) Add(result, candidate);
        }
        return result;
    }
    private static bool IsRecording(string path) => File.Exists(Path.Combine(path, "metadata.json")) ||
        File.Exists(Path.Combine(path, "head.csv")) || File.Exists(Path.Combine(path, "imu.csv")) ||
        File.Exists(Path.Combine(path, "events.csv")) || File.Exists(Path.Combine(path, "summary.json")) ||
        File.Exists(Path.Combine(path, "failure.txt"));

    private static void Add(List<ResearchLogEntry> result, string path)
    {
        var entry = new ResearchLogEntry { Path = Path.GetFullPath(path), RelativePath = path.Substring(Root.Length + 1),
            Purpose = "Legacy", Scenario = "Unknown", Mode = "Unknown", Condition = "Unknown", Status = "INCOMPLETE" };
        string category = Path.GetFileName(Path.GetDirectoryName(path));
        if (IsPurpose(category)) entry.Purpose = category;
        try
        {
            ValidateEntryPath(path);
            entry.Active = ResearchLogRecorder.IsDirectoryActive(path);
            entry.StartedUtc = Directory.GetLastWriteTimeUtc(path);
            entry.Bytes = TreeBytes(path);
            foreach (string archive in Archives(path)) entry.Bytes += new FileInfo(archive).Length;
            string metadata = Path.Combine(path, "metadata.json");
            if (File.Exists(metadata))
            {
                try
                {
                    var header = JsonUtility.FromJson<Header>(File.ReadAllText(metadata));
                    if (header != null)
                    {
                        entry.Purpose = string.IsNullOrEmpty(header.purpose) ? "Legacy" : header.purpose;
                        entry.Scenario = header.scenario ?? "Unknown"; entry.Mode = header.mode ?? "Unknown";
                        entry.Condition = header.condition ?? "Unknown";
                        if (DateTime.TryParse(header.utcStart, null, System.Globalization.DateTimeStyles.RoundtripKind, out var date))
                            entry.StartedUtc = date.ToUniversalTime();
                    }
                }
                catch (Exception) { entry.Status = "UNREADABLE METADATA"; }
            }
            string summary = Path.Combine(path, "summary.json");
            if (File.Exists(summary))
            {
                try { var saved = JsonUtility.FromJson<Result>(File.ReadAllText(summary)); entry.Status = saved != null && saved.completed ? "SAVED" : "ABORTED"; }
                catch (Exception) { entry.Status = "UNREADABLE SUMMARY"; }
            }
            if (entry.Active) entry.Status = "RECORDING - LOCKED";
        }
        catch (Exception exception) { entry.Error = exception.Message; entry.Status = "UNAVAILABLE"; }
        result.Add(entry);
    }

    public static void RequireManageable(string directory)
    {
        ValidateEntryPath(directory);
        if (!Directory.Exists(directory) || !IsRecording(directory)) throw new IOException("Recording no longer exists.");
        if (ResearchLogRecorder.IsDirectoryActive(directory)) throw new IOException("Recording is active. Stop and save before managing it.");
        TreeBytes(directory); // Reject any nested symbolic link before ZIP traversal or recursive deletion.
    }

    public static long Delete(string directory)
    {
        string path = ValidateEntryPath(directory);
        RequireManageable(path);
        var archives = Archives(path);
        long bytes = TreeBytes(path);
        // Root and every descendant were checked above; never delete a purpose folder or a supplied arbitrary path.
        Directory.Delete(path, true);
        foreach (string archive in archives) { bytes += new FileInfo(archive).Length; File.Delete(archive); }
        return bytes;
    }

    private static string ValidateEntryPath(string directory)
    {
        if (string.IsNullOrEmpty(directory)) throw new ArgumentException("Choose a recording.");
        string root = Root;
        string path = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, Comparison)) throw new IOException("Only app recording directories can be managed.");
        string relative = path.Substring(root.Length + 1);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length < 1 || parts.Length > 2) throw new IOException("Invalid recording directory depth.");
        if (parts.Length == 1 && IsPurpose(parts[0])) throw new IOException("A recording category cannot be deleted.");
        if (parts.Length == 2 && !IsPurpose(parts[0])) throw new IOException("Unknown recording category.");
        RejectLink(root);
        if (parts.Length == 2) RejectLink(Path.GetDirectoryName(path));
        RejectLink(path);
        return path;
    }
    private static bool IsPurpose(string name) => name == "Experiment" || name == "ScenarioPreview" || name == "DirectHapticTest" || name == "SensorTest";
    private static bool IsLink(string path) => (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
    private static void RejectLink(string path) { if (IsLink(path)) throw new IOException("Linked directories/files cannot be managed."); }
    private static long TreeBytes(string path)
    {
        RejectLink(path);
        long bytes = 0;
        foreach (string file in Directory.GetFiles(path)) { RejectLink(file); bytes += new FileInfo(file).Length; }
        foreach (string child in Directory.GetDirectories(path)) bytes += TreeBytes(child);
        return bytes;
    }
    private static List<string> Archives(string directory)
    {
        var paths = new List<string>();
        string parent = Path.GetDirectoryName(directory), name = Path.GetFileName(directory);
        foreach (string file in Directory.GetFiles(parent, "*.zip"))
        {
            string stem = Path.GetFileNameWithoutExtension(file);
            bool match = string.Equals(stem, name, Comparison);
            if (!match && stem.StartsWith(name + "_", Comparison))
            {
                string suffix = stem.Substring(name.Length + 1);
                match = suffix.Length == 9;
                foreach (char c in suffix) if (c < '0' || c > '9') match = false;
            }
            if (!match) continue;
            RejectLink(file); paths.Add(file);
        }
        return paths;
    }
    public static string Size(long bytes) => bytes >= 1024 * 1024 ? (bytes / (1024d * 1024d)).ToString("F1") + " MB" :
        bytes >= 1024 ? (bytes / 1024d).ToString("F1") + " KB" : bytes + " B";
}

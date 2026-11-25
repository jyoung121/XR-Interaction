using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Cached, paged landscape UI. Disk scans occur on entry, refresh or a completed action.</summary>
public sealed class ResearchLogBrowser
{
    private const int PageSize = 4;
    private readonly HashSet<string> selected = new HashSet<string>();
    private readonly string[] filters = { "All", "Experiment", "ScenarioPreview", "DirectHapticTest", "SensorTest", "Legacy" };
    private List<ResearchLogEntry> entries = new List<ResearchLogEntry>();
    private List<ResearchLogEntry> pending;
    private int filter, page;
    private bool newest = true;
    private string error;
    private GUIStyle label, button, box;

    public void Refresh()
    {
        try
        {
            entries = ResearchLogLibrary.Read(); error = null;
            selected.RemoveWhere(path => !entries.Any(e => e.Path == path && e.Manageable));
        }
        catch (Exception exception) { entries.Clear(); selected.Clear(); error = exception.Message; }
    }
    public void CancelConfirmation() => pending = null;
    private bool Button(string title, params GUILayoutOption[] options) =>
        GUILayout.Button(title, button, new[] { GUILayout.MinHeight(65) }.Concat(options).ToArray());
    private void Label(string value) => GUILayout.Label(value, label);
    private List<ResearchLogEntry> Filtered()
    {
        var view = entries.Where(e => filter == 0 || e.Purpose == filters[filter]);
        return (newest ? view.OrderByDescending(e => e.StartedUtc) : view.OrderBy(e => e.StartedUtc)).ThenBy(e => e.RelativePath).ToList();
    }
    public void Draw(GUIStyle textStyle, GUIStyle buttonStyle, GUIStyle boxStyle, Action<string> notify)
    {
        label = textStyle; button = buttonStyle; box = boxStyle;
        if (pending != null) { Confirmation(notify); return; }
        if (!string.IsNullOrEmpty(error)) Label("Could not read logs: " + error);
        GUILayout.BeginHorizontal();
        if (Button("TYPE: " + filters[filter])) { filter = (filter + 1) % filters.Length; page = 0; selected.Clear(); }
        if (Button(newest ? "NEWEST FIRST" : "OLDEST FIRST")) { newest = !newest; page = 0; }
        if (Button("REFRESH")) Refresh();
        GUILayout.EndHorizontal();
        var view = Filtered();
        int pages = Math.Max(1, (view.Count + PageSize - 1) / PageSize);
        page = Math.Min(page, pages - 1);
        Label(view.Count + " logs in this filter / " + ResearchLogLibrary.Size(view.Sum(e => e.Bytes)) +
            "   |   Selected " + selected.Count + " across all pages");
        GUILayout.BeginHorizontal();
        if (Button("SELECT FILTERED")) foreach (var entry in view) if (entry.Manageable) selected.Add(entry.Path);
        if (Button("CLEAR SELECTION")) selected.Clear();
        using (new Enabled(selected.Count > 0))
            if (Button("DELETE SELECTED (" + selected.Count + ")"))
                pending = entries.Where(e => selected.Contains(e.Path)).ToList();
        GUILayout.EndHorizontal();

        foreach (var entry in view.Skip(page * PageSize).Take(PageSize))
        {
            GUILayout.BeginVertical(box);
            GUILayout.BeginHorizontal();
            using (new Enabled(entry.Manageable))
            {
                string title = (selected.Contains(entry.Path) ? "[SELECTED] " : "") +
                    entry.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " | " + entry.Purpose +
                    "\n" + entry.Description + " | " + entry.Status + " | " + ResearchLogLibrary.Size(entry.Bytes);
                if (Button(title)) { if (!selected.Add(entry.Path)) selected.Remove(entry.Path); }
                if (Button("EXPORT ZIP", GUILayout.Width(175)))
                {
                    try { notify(ResearchLogRecorder.Export(entry.Path)); }
                    catch (Exception exception) { notify("Export failed: " + exception.Message); }
                    Refresh();
                }
                if (Button("DELETE", GUILayout.Width(135))) pending = new List<ResearchLogEntry> { entry };
            }
            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(entry.Error)) Label(entry.Error);
            GUILayout.EndVertical();
        }
        if (view.Count == 0) Label("No recordings in this filter.");
        GUILayout.BeginHorizontal();
        using (new Enabled(page > 0)) if (Button("PREVIOUS")) page--;
        Label("Page " + (page + 1) + " / " + pages);
        using (new Enabled(page + 1 < pages)) if (Button("NEXT")) page++;
        GUILayout.EndHorizontal();
        Label("Delete removes app recordings and their internal ZIP caches. Copies in Downloads/XRExperiment are kept.");
    }
    private void Confirmation(Action<string> notify)
    {
        Label("DELETE " + pending.Count + " RECORDING(S)?  " + ResearchLogLibrary.Size(pending.Sum(e => e.Bytes)));
        Label("Raw CSV/JSON files and internal ZIP caches will be permanently removed. Exported Downloads copies are kept.");
        foreach (var entry in pending.Take(4)) Label(entry.RelativePath + "\n" + entry.Description);
        if (pending.Count > 4) Label("... and " + (pending.Count - 4) + " more selected recordings.");
        GUILayout.BeginHorizontal();
        if (Button("CANCEL")) { pending = null; GUILayout.EndHorizontal(); return; }
        if (Button("DELETE " + pending.Count + " RECORDING(S)"))
        {
            int deleted = 0, failed = 0; long bytes = 0; string failure = "";
            foreach (var entry in pending)
            {
                try { bytes += ResearchLogLibrary.Delete(entry.Path); selected.Remove(entry.Path); deleted++; }
                catch (Exception exception) { failed++; if (failure.Length == 0) failure = exception.Message; }
            }
            pending = null; Refresh();
            notify("Deleted " + deleted + " recording(s), " + ResearchLogLibrary.Size(bytes) + "." +
                (failed > 0 ? " Could not fully remove " + failed + ": " + failure : ""));
        }
        GUILayout.EndHorizontal();
    }
    private readonly struct Enabled : IDisposable
    {
        private readonly bool previous;
        public Enabled(bool enabled) { previous = GUI.enabled; GUI.enabled = previous && enabled; }
        public void Dispose() { GUI.enabled = previous; }
    }
}

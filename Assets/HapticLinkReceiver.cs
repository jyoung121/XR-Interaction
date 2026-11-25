using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public sealed class HapticLinkReceiver : MonoBehaviour
{
    public const int Port = 48700, MaxSegmentsPerChunk = 256, MaxChunkDurationMs = 10000, MaxProfileChunks = 4096;
    [Serializable] private sealed class Command
    { public string command = "", profileId = "", profileName = "", profileHash = ""; public int chunkIndex, chunkCount, profileVersion, offsetMs; public long[] durations = Array.Empty<long>(); public int[] amplitudes = Array.Empty<int>(); public bool loop; }
    [Serializable] private sealed class Response { public bool ok; public string status = ""; }
    private sealed class Pending
    { public readonly string json; public readonly ManualResetEventSlim done = new ManualResetEventSlim(false); public string response; public Pending(string value) { json = value; } }
    private sealed class Uploaded
    {
        public readonly HapticChunk[] chunks; public bool loop; public Uploaded(int count) { chunks = new HapticChunk[count]; }
        public bool Complete { get { for (int i = 0; i < chunks.Length; i++) if (chunks[i].durations == null) return false; return true; } }
    }
    private readonly ConcurrentQueue<Pending> commands = new ConcurrentQueue<Pending>();
    private readonly Dictionary<string, Uploaded> profiles = new Dictionary<string, Uploaded>();
    private readonly object connectionLock = new object();
    private TcpListener listener; private TcpClient activeClient; private Thread worker;
    private volatile bool running, connected; private volatile string status = "Stopped";
    private bool requested, paused; private LongHapticPlayer player;
    public bool Running => running; public bool Connected => connected; public string Status => status;
    public string LastProfile { get; private set; } = "No profile played";
    public bool Playing => player != null && player.IsPlaying;
    public event Action<string, string> PreviewEvent;
    private void Awake() => player = GetComponent<LongHapticPlayer>() ?? gameObject.AddComponent<LongHapticPlayer>();
    public void StartListening() { requested = true; if (!paused) Open(); }
    public void StopListening() { requested = false; Close("Stopped"); }
    private void Open()
    {
        if (running || !requested) return;
        try { listener = new TcpListener(IPAddress.Loopback, Port); listener.Start(1); running = true; status = $"Listening on device port {Port}"; worker = new Thread(Listen) { IsBackground = true, Name = "HapticLinkReceiver" }; worker.Start(); }
        catch (Exception ex) { running = false; status = "Could not listen: " + ex.Message; Debug.LogError("[HapticLink] " + status); }
    }
    private void Close(string next)
    {
        PreviewEvent?.Invoke("receiver_closed", next);
        running = connected = false; lock (connectionLock) { try { activeClient?.Close(); } catch { } activeClient = null; }
        try { listener?.Stop(); } catch { } listener = null;
        while (commands.TryDequeue(out Pending p)) { p.response = Make(false, next); p.done.Set(); }
        if (worker != null && worker.IsAlive) worker.Join(1000); worker = null; status = next; if (player != null) player.Stop();
    }
    private void Listen()
    {
        while (running)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient(); lock (connectionLock) activeClient = client; connected = true; status = "PC connected";
                using (client) using (NetworkStream stream = client.GetStream()) using (StreamReader reader = new StreamReader(stream)) using (StreamWriter writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    string line; while (running && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 65536) { writer.WriteLine(Make(false, "Command exceeds 64 KB")); continue; }
                        Pending p = new Pending(line); commands.Enqueue(p); while (running && !p.done.Wait(50)) { }
                        if (p.done.IsSet) writer.WriteLine(p.response); p.done.Dispose();
                    }
                }
            }
            catch (SocketException) { if (running) status = "Connection interrupted"; }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { if (running) status = "Receiver error: " + ex.Message; }
            finally { connected = false; lock (connectionLock) activeClient = null; if (running) status = $"Listening on device port {Port}"; }
        }
    }
    private void Update() { while (commands.TryDequeue(out Pending p)) { p.response = Execute(p.json); p.done.Set(); } }
    private string Execute(string json)
    {
        Command m; try { m = JsonUtility.FromJson<Command>(json); } catch (Exception ex) { return Fail("Invalid JSON: " + ex.Message); }
        if (m == null || string.IsNullOrWhiteSpace(m.command)) return Fail("Invalid command");
        switch (m.command.ToLowerInvariant())
        {
            case "ping": return Make(true, "Receiver ready; protocol=2; playing=" + Playing + "; " + LastProfile);
            case "stop": player.Stop(); PreviewEvent?.Invoke("pc_stop", ""); return Success("Playback stopped");
            case "clear": profiles.Clear(); player.Stop(); return Success("Uploaded profiles cleared");
            case "play": if (!Validate(m, out string e)) return Fail(e); LastProfile = "Legacy draft / " + m.profileId; PreviewEvent?.Invoke("pc_legacy_play", json); player.PlayWaveform(m.durations, m.amplitudes, AudioSettings.dspTime + .05d, m.loop); return Success($"Playing {m.durations.Length} segments");
            case "load-chunk": return Load(m);
            case "play-profile": return PlayUploaded(m);
            default: return Fail("Unknown command: " + m.command);
        }
    }
    private string Load(Command m)
    {
        if (string.IsNullOrWhiteSpace(m.profileId)) return Fail("profileId is required");
        if (m.chunkCount < 1 || m.chunkCount > MaxProfileChunks || m.chunkIndex < 0 || m.chunkIndex >= m.chunkCount) return Fail("Invalid chunk index/count");
        if (!Validate(m, out string error)) return Fail(error);
        // Only the current upload is retained; playback owns its own waveform copy.
        if (m.chunkIndex == 0) profiles.Clear();
        if (!profiles.TryGetValue(m.profileId, out Uploaded p) || p.chunks.Length != m.chunkCount) { p = new Uploaded(m.chunkCount); profiles[m.profileId] = p; }
        long total = 0; for (int i = 0; i < m.durations.Length; i++) total += m.durations[i];
        p.chunks[m.chunkIndex] = new HapticChunk((long[])m.durations.Clone(), (int[])m.amplitudes.Clone(), total); p.loop = m.loop;
        PreviewEvent?.Invoke("pc_chunk_loaded", JsonUtility.ToJson(m));
        return Success($"Loaded chunk {m.chunkIndex + 1}/{m.chunkCount}");
    }
    private string PlayUploaded(Command command)
    {
        string id = command.profileId;
        if (string.IsNullOrWhiteSpace(id) || !profiles.TryGetValue(id, out Uploaded p)) return Fail("Profile not uploaded: " + id);
        if (!p.Complete) return Fail("Profile upload is incomplete");
        var d = new List<long>(); var a = new List<int>(); for (int i = 0; i < p.chunks.Length; i++) { d.AddRange(p.chunks[i].durations); a.AddRange(p.chunks[i].amplitudes); }
        double at = HapticPreviewTiming.StartDsp(AudioSettings.dspTime, command.offsetMs);
        LastProfile = (string.IsNullOrEmpty(command.profileName) ? id + " [draft]" :
            command.profileName + " / v" + command.profileVersion + " / SHA256 " + command.profileHash) +
            " / offset " + command.offsetMs + " ms / loop " + p.loop;
        PreviewEvent?.Invoke("pc_profile", JsonUtility.ToJson(command));
        PreviewEvent?.Invoke("pc_profile_scheduled", "start_dsp=" + at.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
        player.PlayWaveform(d.ToArray(), a.ToArray(), at, p.loop);
        return Success($"Playing profile {id}: {d.Count} segments");
    }
    private static bool Validate(Command m, out string error)
    {
        error = ""; if (m.durations == null || m.amplitudes == null || m.durations.Length == 0 || m.durations.Length != m.amplitudes.Length) { error = "Arrays must have equal non-zero lengths"; return false; }
        if (m.durations.Length > MaxSegmentsPerChunk) { error = $"Chunk exceeds {MaxSegmentsPerChunk} segments"; return false; }
        long total = 0; for (int i = 0; i < m.durations.Length; i++) { if (m.durations[i] < 1 || m.durations[i] > 2000 || m.amplitudes[i] < 0 || m.amplitudes[i] > 255) { error = $"Invalid segment {i}"; return false; } total += m.durations[i]; }
        if (total > MaxChunkDurationMs) { error = $"Chunk exceeds {MaxChunkDurationMs} ms"; return false; } return true;
    }
    private string Success(string m) { status = m; return Make(true, m); }
    private string Fail(string m) { status = m; return Make(false, m); }
    private static string Make(bool ok, string m) => JsonUtility.ToJson(new Response { ok = ok, status = m });
    private void OnEnable() { if (requested && !paused) Open(); }
    private void OnDisable() => Close("Disabled; waiting to resume");
    private void OnDestroy() { requested = false; Close("Stopped"); }
    private void OnApplicationPause(bool value) { paused = value; if (paused) Close("Paused; will resume automatically"); else if (requested) Open(); }
}

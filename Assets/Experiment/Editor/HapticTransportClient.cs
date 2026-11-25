using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class HapticTransportClient
{
    [Serializable] private sealed class Command
    { public string command, profileId, profileName, profileHash; public int chunkIndex, chunkCount, profileVersion, offsetMs; public long[] durations; public int[] amplitudes; public bool loop; }
    [Serializable] private sealed class Response { public bool ok; public string status; }
    public static string ForwardAdb()
    {
        string editor = Path.GetDirectoryName(EditorApplication.applicationPath);
        string bundled = Path.Combine(editor, "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
        var info = new ProcessStartInfo { FileName = File.Exists(bundled) ? bundled : "adb", Arguments = $"forward tcp:{HapticLinkReceiver.Port} tcp:{HapticLinkReceiver.Port}", UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using (Process p = Process.Start(info)) { if (!p.WaitForExit(5000)) { try { p.Kill(); } catch { } throw new TimeoutException("ADB forward timed out."); } string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); if (p.ExitCode != 0) throw new IOException(output.Trim()); }
        return $"ADB forwarded localhost:{HapticLinkReceiver.Port}";
    }
    public static string Play(HapticProfile profile)
    {
        if (profile == null) throw new InvalidOperationException("Profile is missing.");
        if (!profile.IsPlayable(out string reason)) throw new InvalidOperationException(reason);
        if (!CheckReceiver().Contains("protocol=2"))
            throw new InvalidOperationException("Update the mobile APK for saved-profile offset/metadata support.");
        string hash = ResearchLogRecorder.HapticHash(profile);
        string id = profile.ProfileId + "-" + profile.Version + "-" + hash.Substring(0, 12);
        Upload(id, profile.CreateTimings(), profile.CreateScaledAmplitudes(), profile.loop);
        return Send(new Command { command = "play-profile", profileId = id, profileName = profile.ProfileId,
            profileVersion = profile.Version, profileHash = hash, offsetMs = profile.hapticOffsetMs }).status;
    }
    public static string Play(string id, long[] d, int[] a, bool loop = false)
    {
        Upload(id, d, a, loop);
        return Send(new Command { command = "play-profile", profileId = id }).status;
    }
    private static void Upload(string id, long[] d, int[] a, bool loop)
    {
        var chunks = HapticWaveformChunker.Create(d, a, HapticLinkReceiver.MaxSegmentsPerChunk, HapticLinkReceiver.MaxChunkDurationMs);
        if (chunks.Count == 0) throw new InvalidOperationException("Waveform is empty.");
        for (int i = 0; i < chunks.Count; i++) Send(new Command { command = "load-chunk", profileId = id, chunkIndex = i, chunkCount = chunks.Count, durations = chunks[i].durations, amplitudes = chunks[i].amplitudes, loop = loop });
    }
    public static string CheckReceiver() => Send(new Command { command = "ping" }).status;
    public static string Stop() => Send(new Command { command = "stop" }).status;
    private static Response Send(Command command)
    {
        using (TcpClient client = new TcpClient())
        {
            IAsyncResult pending = client.BeginConnect("127.0.0.1", HapticLinkReceiver.Port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(2500)) throw new IOException("Phone did not accept the connection. Open MobileTestScene and run ADB Forward.");
            client.EndConnect(pending); client.ReceiveTimeout = client.SendTimeout = 5000;
            using (NetworkStream stream = client.GetStream()) using (StreamReader reader = new StreamReader(stream))
            { byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(command) + "\n"); stream.Write(data, 0, data.Length); stream.Flush(); string json = reader.ReadLine(); if (string.IsNullOrEmpty(json)) throw new IOException("Phone returned no ACK."); Response r = JsonUtility.FromJson<Response>(json); if (r == null || !r.ok) throw new IOException(r?.status ?? "Invalid phone response."); return r; }
        }
    }
}

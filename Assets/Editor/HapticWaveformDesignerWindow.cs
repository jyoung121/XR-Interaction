using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using UnityEditor;
using UnityEngine;

public sealed class HapticWaveformDesignerWindow : EditorWindow
{
    [Serializable]
    private sealed class HapticCommand
    {
        public string command;
        public long[] durations;
        public int[] amplitudes;
        public int transientMaxMs;
    }

    [Serializable]
    private sealed class HapticResponse
    {
        public bool ok = false;
        public string status = "";
    }

    private string durations = "80, 40, 120, 40, 200";
    private string amplitudes = "255, 0, 180, 0, 255";
    private int transientMaxMs;
    private string status = "Connect the phone, open PC Haptic Link in the app, then forward the port.";

    [MenuItem("Tools/Haptic Waveform Designer")]
    private static void Open() => GetWindow<HapticWaveformDesignerWindow>("Haptic Designer");

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Phone Waveform Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each duration is paired with one amplitude. Duration: 1-2000 ms, amplitude: 0-255.", MessageType.Info);
        durations = EditorGUILayout.TextField("Durations (ms)", durations);
        amplitudes = EditorGUILayout.TextField("Amplitudes", amplitudes);
        transientMaxMs = EditorGUILayout.IntSlider("iOS transient max (ms)", transientMaxMs, 0, 200);

        EditorGUILayout.Space();
        if (GUILayout.Button("ADB Forward", GUILayout.Height(30))) ForwardPort();
        if (GUILayout.Button("Play on Phone", GUILayout.Height(36))) SendPlay();
        if (GUILayout.Button("Stop Phone Haptics", GUILayout.Height(30))) SendStop();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(status, MessageType.None);
    }

    private void ForwardPort()
    {
        try
        {
            string adb = FindAdb();
            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = adb,
                Arguments = $"forward tcp:{HapticLinkReceiver.Port} tcp:{HapticLinkReceiver.Port}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process process = Process.Start(info))
            {
                if (!process.WaitForExit(5000))
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("ADB forward timed out.");
                }
                string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
                status = process.ExitCode == 0 ? $"ADB forwarded localhost:{HapticLinkReceiver.Port}" : output.Trim();
            }
        }
        catch (Exception ex) { status = "ADB failed: " + ex.Message; }
    }

    private void SendPlay()
    {
        try
        {
            long[] d = ParseLongs(durations);
            int[] a = ParseInts(amplitudes);
            if (d.Length == 0 || d.Length != a.Length) throw new FormatException("The two lists must have the same non-zero length.");
            if (d.Any(v => v < 1 || v > 2000)) throw new FormatException("Durations must be 1-2000 ms.");
            if (a.Any(v => v < 0 || v > 255)) throw new FormatException("Amplitudes must be 0-255.");
            if (d.Length > 256 || d.Sum() > 10000) throw new FormatException("Maximum: 256 segments and 10 seconds.");

            HapticResponse response = Send(new HapticCommand
            {
                command = "play",
                durations = d,
                amplitudes = a,
                transientMaxMs = transientMaxMs
            });
            if (!response.ok) throw new IOException(response.status);
            status = "Phone ACK: " + response.status;
        }
        catch (Exception ex) { status = "Play failed: " + ex.Message; }
    }

    private void SendStop()
    {
        try
        {
            HapticResponse response = Send(new HapticCommand { command = "stop" });
            if (!response.ok) throw new IOException(response.status);
            status = "Phone ACK: " + response.status;
        }
        catch (Exception ex) { status = "Stop failed: " + ex.Message; }
    }

    private static HapticResponse Send(HapticCommand command)
    {
        using (TcpClient client = new TcpClient())
        {
            IAsyncResult pending = client.BeginConnect("127.0.0.1", HapticLinkReceiver.Port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(2000)) throw new IOException("Phone did not accept the connection. Open PC Haptic Link and run ADB Forward.");
            client.EndConnect(pending);
            client.ReceiveTimeout = 4000;
            client.SendTimeout = 4000;
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream))
            {
                byte[] data = Encoding.UTF8.GetBytes(JsonUtility.ToJson(command) + "\n");
                stream.Write(data, 0, data.Length);
                stream.Flush();

                string json = reader.ReadLine();
                if (string.IsNullOrEmpty(json))
                    throw new IOException("Phone closed the connection without an ACK. Reopen PC Haptic Link in the app.");
                HapticResponse response = JsonUtility.FromJson<HapticResponse>(json);
                if (response == null) throw new IOException("Phone returned an invalid ACK.");
                return response;
            }
        }
    }

    private static long[] ParseLongs(string text) =>
        text.Split(',').Select(v => long.Parse(v.Trim())).ToArray();

    private static int[] ParseInts(string text) =>
        text.Split(',').Select(v => int.Parse(v.Trim())).ToArray();

    private static string FindAdb()
    {
        string editorDirectory = Path.GetDirectoryName(EditorApplication.applicationPath);
        string bundled = Path.Combine(editorDirectory, "Data", "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe");
        return File.Exists(bundled) ? bundled : "adb";
    }
}

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
        public float sharpness;
    }

    [Serializable]
    private sealed class HapticResponse
    {
        public bool ok = false;
        public string status = "";
    }

    /// <summary>CoreHaptics 쪽 상한. CardboardHaptics.mm 의 kMaxWaveEvents 와 같은 값이어야 한다.</summary>
    private const int IosMaxEvents = 96;

    private const string HostKey = "HapticDesigner.Host";
    private const string AndroidHost = "127.0.0.1";
    private const string IPhoneHotspotHost = "172.20.10.1";

    private string host = AndroidHost;
    private string durations = "80, 40, 120, 40, 200";
    private string amplitudes = "255, 0, 180, 0, 255";
    private int transientMaxMs;
    private bool overrideSharpness;
    private float sharpness = 0.5f;
    private string status = "Open PC Haptic Link in the app. Android: ADB Forward. iPhone: set Host to the address the app shows.";

    [MenuItem("Tools/Haptic Waveform Designer")]
    private static void Open() => GetWindow<HapticWaveformDesignerWindow>("Haptic Designer");

    private void OnEnable() => host = EditorPrefs.GetString(HostKey, AndroidHost);

    private void OnDisable() => EditorPrefs.SetString(HostKey, host);

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Phone Waveform Preview", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Each duration is paired with one amplitude. Duration: 1-2000 ms, amplitude: 0-255.", MessageType.Info);

        // 전송 경로. 안드로이드는 adb 가 기기 루프백으로 밀어 주지만, iOS 에는 대응
        // 도구가 없어 같은 와이파이에서 폰 주소로 직접 붙는다.
        EditorGUILayout.LabelField("Transport", EditorStyles.boldLabel);
        host = EditorGUILayout.TextField("Phone host", host);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Android (adb, 127.0.0.1)")) host = AndroidHost;
            if (GUILayout.Button("iPhone hotspot (172.20.10.1)")) host = IPhoneHotspotHost;
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Waveform", EditorStyles.boldLabel);
        durations = EditorGUILayout.TextField("Durations (ms)", durations);
        amplitudes = EditorGUILayout.TextField("Amplitudes", amplitudes);
        transientMaxMs = EditorGUILayout.IntSlider("iOS transient max (ms)", transientMaxMs, 0, 200);

        overrideSharpness = EditorGUILayout.Toggle("Set iOS sharpness", overrideSharpness);
        using (new EditorGUI.DisabledScope(!overrideSharpness))
            sharpness = EditorGUILayout.Slider("Sharpness (iOS only)", sharpness, 0f, 1f);

        DrawIosEventEstimate();

        EditorGUILayout.Space();
        if (GUILayout.Button("ADB Forward (Android only)", GUILayout.Height(30))) ForwardPort();
        if (GUILayout.Button("Play on Phone", GUILayout.Height(36))) SendPlay();
        if (GUILayout.Button("Stop Phone Haptics", GUILayout.Height(30))) SendStop();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(status, MessageType.None);
    }

    /// <summary>
    /// iOS 는 구간을 그대로 재생하지 않는다. CoreHaptics 에 createWaveform 대응 API 가
    /// 없어서 CardboardHaptics.mm 이 구간을 이벤트로 바꿔 놓는데,
    ///  - 세기 0 구간은 이벤트를 만들지 않고 침묵이 되고
    ///  - 같은 세기가 이어지면 하나로 합치고
    ///  - 96개를 넘으면 초과분을 조용히 버린다.
    /// 그래서 폰에서 안 들리는 뒷부분이 생길 수 있다. 보내기 전에 같은 셈을 해서 보여 준다.
    /// </summary>
    private void DrawIosEventEstimate()
    {
        long[] d;
        int[] a;
        try
        {
            d = ParseLongs(durations);
            a = ParseInts(amplitudes);
            if (d.Length == 0 || d.Length != a.Length) return;
        }
        catch (Exception) { return; }

        int events = 0;
        int i = 0;
        while (i < a.Length)
        {
            int level = Mathf.Clamp(a[i], 0, 255);
            long span = 0;
            int j = i;
            while (j < a.Length && Mathf.Clamp(a[j], 0, 255) == level)
            {
                if (d[j] > 0) span += d[j];
                j++;
            }
            if (level > 0 && span > 0) events++;
            i = j;
        }

        string text = $"iOS: {a.Length} segments merge into {events} CoreHaptics events (cap {IosMaxEvents}).";
        if (events > IosMaxEvents)
            EditorGUILayout.HelpBox(text + $" The last {events - IosMaxEvents} would be dropped on iPhone - the tail will not play.", MessageType.Warning);
        else
            EditorGUILayout.HelpBox(text, MessageType.None);
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
                transientMaxMs = transientMaxMs,
                sharpness = overrideSharpness ? sharpness : -1f
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
            HapticResponse response = Send(new HapticCommand { command = "stop", sharpness = -1f });
            if (!response.ok) throw new IOException(response.status);
            status = "Phone ACK: " + response.status;
        }
        catch (Exception ex) { status = "Stop failed: " + ex.Message; }
    }

    private HapticResponse Send(HapticCommand command)
    {
        string target = string.IsNullOrWhiteSpace(host) ? AndroidHost : host.Trim();
        using (TcpClient client = new TcpClient())
        {
            IAsyncResult pending = client.BeginConnect(target, HapticLinkReceiver.Port, null, null);
            if (!pending.AsyncWaitHandle.WaitOne(2000))
                throw new IOException($"{target} did not accept the connection. Open PC Haptic Link in the app; " +
                    "on Android run ADB Forward, on iPhone check that the PC is on the phone's hotspot and Host matches the address the app shows.");
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

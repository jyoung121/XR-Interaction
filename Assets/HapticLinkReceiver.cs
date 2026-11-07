using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

/// <summary>
/// Receives newline-delimited JSON waveforms forwarded by ADB from the Unity editor.
/// The listener is deliberately active only while the PC Haptic Link settings page is open.
/// </summary>
public sealed class HapticLinkReceiver : MonoBehaviour
{
    public const int Port = 48700;

    [Serializable]
    private sealed class HapticCommand
    {
        public string command = "";
        public long[] durations = Array.Empty<long>();
        public int[] amplitudes = Array.Empty<int>();
        public int transientMaxMs = 0;
    }

    [Serializable]
    private sealed class HapticResponse
    {
        public bool ok;
        public string status = "";
    }

    private sealed class PendingCommand
    {
        public readonly string json;
        public readonly ManualResetEventSlim completed = new ManualResetEventSlim(false);
        public string response;

        public PendingCommand(string value) { json = value; }
    }

    private readonly ConcurrentQueue<PendingCommand> commands = new ConcurrentQueue<PendingCommand>();
    private readonly object connectionLock = new object();
    private TcpListener listener;
    private TcpClient activeClient;
    private Thread worker;
    private volatile bool running;
    private volatile bool connected;
    private volatile string status = "Stopped";
    private bool listenerRequested;
    private bool applicationPaused;

    public bool Running => running;
    public bool Connected => connected;
    public string Status => status;

    public void StartListening()
    {
        listenerRequested = true;
        if (!applicationPaused) OpenListener();
    }

    private void OpenListener()
    {
        if (running || !listenerRequested) return;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start(1);
            running = true;
            status = $"Listening on device port {Port}";
            worker = new Thread(ListenLoop) { IsBackground = true, Name = "HapticLinkReceiver" };
            worker.Start();
        }
        catch (Exception ex)
        {
            running = false;
            status = "Could not listen: " + ex.Message;
            Debug.LogError("[HapticLink] " + status);
        }
    }

    public void StopListening()
    {
        listenerRequested = false;
        CloseListener("Stopped");
    }

    private void CloseListener(string nextStatus)
    {
        running = false;
        connected = false;
        lock (connectionLock)
        {
            try { activeClient?.Close(); } catch { }
            activeClient = null;
        }
        try { listener?.Stop(); } catch { }
        listener = null;
        while (commands.TryDequeue(out PendingCommand pending))
        {
            pending.response = MakeResponse(false, nextStatus);
            pending.completed.Set();
        }
        if (worker != null && worker.IsAlive) worker.Join(1000);
        worker = null;
        status = nextStatus;
        HapticManager.StopWaveform();
        HapticManager.StopContinuous();
    }

    private void ListenLoop()
    {
        while (running)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();
                lock (connectionLock) activeClient = client;
                connected = true;
                status = "PC connected";

                using (client)
                using (NetworkStream stream = client.GetStream())
                using (StreamReader reader = new StreamReader(stream))
                using (StreamWriter writer = new StreamWriter(stream) { AutoFlush = true })
                {
                    string line;
                    while (running && (line = reader.ReadLine()) != null)
                    {
                        if (line.Length > 32768)
                        {
                            writer.WriteLine(MakeResponse(false, "Command exceeds 32 KB"));
                            continue;
                        }

                        PendingCommand pending = new PendingCommand(line);
                        commands.Enqueue(pending);
                        while (running && !pending.completed.Wait(50)) { }

                        if (pending.completed.IsSet)
                            writer.WriteLine(pending.response);
                        pending.completed.Dispose();
                        if (!running) break;
                    }
                }
            }
            catch (SocketException) { if (running) status = "Connection interrupted"; }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { if (running) status = "Receiver error: " + ex.Message; }
            finally
            {
                connected = false;
                lock (connectionLock) activeClient = null;
                if (running && status == "PC connected")
                    status = $"Listening on device port {Port}";
            }
        }
    }

    private void Update()
    {
        while (commands.TryDequeue(out PendingCommand pending))
        {
            pending.response = Execute(pending.json);
            pending.completed.Set();
        }
    }

    private string Execute(string json)
    {
        HapticCommand message;
        try { message = JsonUtility.FromJson<HapticCommand>(json); }
        catch (Exception ex)
        {
            status = "Invalid JSON: " + ex.Message;
            return MakeResponse(false, status);
        }

        if (message == null || string.IsNullOrEmpty(message.command))
        {
            status = "Invalid command";
            return MakeResponse(false, status);
        }
        if (string.Equals(message.command, "stop", StringComparison.OrdinalIgnoreCase))
        {
            HapticManager.StopWaveform();
            HapticManager.StopContinuous();
            status = "Waveform stopped";
            return MakeResponse(true, status);
        }
        string validationError = null;
        if (!string.Equals(message.command, "play", StringComparison.OrdinalIgnoreCase) ||
            !Validate(message, out validationError))
        {
            status = validationError ?? "Unknown command";
            return MakeResponse(false, status);
        }

        HapticManager.StopWaveform();
        HapticManager.PlayWaveform(message.durations, message.amplitudes,
            Mathf.Clamp(message.transientMaxMs, 0, 2000));
        status = $"Played {message.durations.Length} segments";
        return MakeResponse(true, status);
    }

    private static string MakeResponse(bool ok, string message)
    {
        return JsonUtility.ToJson(new HapticResponse { ok = ok, status = message });
    }

    private static bool Validate(HapticCommand message, out string error)
    {
        error = null;
        if (message.durations == null || message.amplitudes == null ||
            message.durations.Length == 0 || message.durations.Length != message.amplitudes.Length)
        {
            error = "Durations/amplitudes must have the same non-zero length";
            return false;
        }
        if (message.durations.Length > 256)
        {
            error = "Waveform exceeds 256 segments";
            return false;
        }

        long total = 0;
        for (int i = 0; i < message.durations.Length; i++)
        {
            if (message.durations[i] < 1 || message.durations[i] > 2000 ||
                message.amplitudes[i] < 0 || message.amplitudes[i] > 255)
            {
                error = $"Segment {i}: duration 1-2000 ms, amplitude 0-255";
                return false;
            }
            total += message.durations[i];
        }
        if (total > 10000)
        {
            error = "Waveform exceeds 10 seconds";
            return false;
        }
        return true;
    }

    private void OnEnable()
    {
        if (listenerRequested && !applicationPaused) OpenListener();
    }

    private void OnDisable() { CloseListener("Disabled; waiting to resume"); }

    private void OnDestroy()
    {
        listenerRequested = false;
        CloseListener("Stopped");
    }

    private void OnApplicationPause(bool paused)
    {
        applicationPaused = paused;
        if (paused) CloseListener("Paused; will resume automatically");
        else if (listenerRequested) OpenListener();
    }
}

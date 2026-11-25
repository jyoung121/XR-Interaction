using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>Raw data is immutable; filtering and behavioral interpretation happen offline.</summary>
public sealed class ResearchLogRecorder : MonoBehaviour
{
#if UNITY_EDITOR
    public static string TestRoot;
#endif
    public static string Root
    {
        get
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(TestRoot)) return TestRoot;
#endif
            return Path.Combine(Application.persistentDataPath, "ResearchLogs");
        }
    }
    public static string LastDirectory { get; private set; }
    private static readonly HashSet<string> activeDirectories = new HashSet<string>(
        Path.DirectorySeparatorChar == '\\' ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string directory;
    public static bool IsDirectoryActive(string path) => !string.IsNullOrEmpty(path) &&
        activeDirectories.Contains(Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    public bool Recording { get; private set; }
    public string Status { get; private set; } = "Not recording";
    public string Error { get; private set; }
    public int HeadSamples { get; private set; }
    public long ImuSamples { get; private set; }
    public double GyroHz => SensorHz(gyroCount, gyroFirst, gyroLast);
    public double AccelHz => SensorHz(accelCount, accelFirst, accelLast);
    public double Elapsed => Recording ? Time.realtimeSinceStartupAsDouble - started : 0;
    public ScenarioPlaybackController player;
    public int Episode { get; set; }
    public string Phase { get; set; } = "Idle";
    public event Action<string> Failed;
    private StreamWriter head, imu, events;
    private double started, nextFlush;
    private double observationStarted = -1;
    private long gyroCount, accelCount, gyroFirst, gyroLast, accelFirst, accelLast;
    private double contactSeconds, lastHeadTime;
    private string lastBlock = "";
    private int onCount, offCount;
    private float nominalSeconds;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
#if UNITY_ANDROID && !UNITY_EDITOR
    private AndroidJavaObject sensor;
#endif
    [Serializable] private sealed class Metadata
    {
        public int schemaVersion = 2;
        public string purpose;
        public string trialId, utcStart, applicationVersion, device, os, unityVersion;
        public string scenario, mode, condition, profileId, scenarioJson, soundProfileJson, hapticJson, hapticHash;
        public string sensorInfo, appBuild, sensorCoordinates = "Android native device axes, accel m/s^2 including gravity, gyro rad/s";
        public string headCoordinates = "Unity camera local rotation relative to its parent; quaternion xyzw; no translation metric";
        public string timestampMeaning = "IMU ns since boot; clock_anchor events map boot ns to Unity monotonic/DSP clocks; commands are not physical onset measurements";
        public float observationSeconds;
        public int requestedImuHz;
    }
    [Serializable] private sealed class Summary
    {
        public string endReason, error;
        public bool completed;
        public double actualSeconds, contactSeconds, finishingSeconds;
        public double recordingSeconds, observedGyroHz, observedAccelHz;
        public float nominalSeconds;
        public int headSamples, onRequests, offRequests;
        public long imuSamples, nativeDropped;
    }
    private double finishingAt = -1;

    public static string Hash(string data)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(data))).Replace("-", "").ToLowerInvariant();
    }
    // JsonUtility encodes Unity asset references as session-local instance IDs. Never use
    // those IDs as persistent fingerprints across Editor reloads or Android builds.
    public static string HapticHash(HapticProfile profile)
    {
        if (profile == null) return "none";
        var data = new StringBuilder();
        data.Append(profile.ProfileId).Append('|').Append(profile.Version).Append('|').Append((int)profile.Origin)
            .Append('|').Append(profile.hapticOffsetMs).Append('|').Append(Number(profile.defaultAmplitudeScale))
            .Append('|').Append(profile.loop).Append('|').Append(profile.sourceAssetHash)
            .Append('|').Append(profile.converterVersion).Append('|').Append(JsonUtility.ToJson(profile.conversionSettings));
        for (int i = 0; i < profile.SegmentCount; i++) data.Append('|').Append(profile.DurationsMs[i]).Append(':').Append(profile.Amplitudes[i]);
        return Hash(data.ToString());
    }

    public bool Begin(ResearchScenarioSettings settings, ResearchMode mode, ResearchHaptic haptic,
        int requestedHz, string label = "trial", ResearchRecordingPurpose purpose = ResearchRecordingPurpose.Experiment)
    {
        if (Recording) { Error = "A recording is already active."; return false; }
        try
        {
            string id = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", Inv) + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            directory = Path.GetFullPath(Path.Combine(Root, purpose.ToString(), id));
            LastDirectory = directory;
            Directory.CreateDirectory(directory);
            activeDirectories.Add(directory);
            head = Writer("head.csv", "sample,monotonic_s,dsp_s,recording_s,observation_s,scenario_s,episode,phase,block,qx,qy,qz,qw,tracked,frame");
            imu = Writer("imu.csv", "sequence,sensor_boot_ns,type,x,y,z,accuracy");
            events = Writer("events.csv", "monotonic_s,dsp_s,recording_s,observation_s,episode,event,value");
            started = Time.realtimeSinceStartupAsDouble;
            lastHeadTime = started;
            nextFlush = started + .5;
            HeadSamples = 0; ImuSamples = 0; Error = null;
            contactSeconds = 0; onCount = 0; offCount = 0; finishingAt = -1;
            observationStarted = -1;
            gyroCount = accelCount = gyroFirst = gyroLast = accelFirst = accelLast = 0;
            nominalSeconds = settings?.observationSeconds ?? 0;
            string sensorInfo = "Editor simulated pose only; no native IMU";
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                sensor = new AndroidJavaObject("com.xrteam.research.ResearchImu", unity.GetStatic<AndroidJavaObject>("currentActivity"));
            sensorInfo = sensor.Call<string>("start", requestedHz);
            if (sensorInfo.StartsWith("ERROR:")) throw new IOException(sensorInfo);
#endif
            var selected = settings?.Resolve(haptic);
            var metadata = new Metadata {
                purpose = purpose.ToString(),
                trialId = id, utcStart = DateTime.UtcNow.ToString("O", Inv),
                applicationVersion = Application.version, unityVersion = Application.unityVersion,
                device = SystemInfo.deviceModel, os = SystemInfo.operatingSystem,
                scenario = settings?.scenario != null ? settings.scenario.scenario.ToString() : label,
                profileId = settings?.scenario != null ? settings.scenario.ProfileId : label,
                scenarioJson = settings?.scenario != null ? JsonUtility.ToJson(settings.scenario) : "",
                soundProfileJson = settings?.scenario?.sound != null ? JsonUtility.ToJson(settings.scenario.sound) : "",
                hapticJson = selected != null ? JsonUtility.ToJson(selected) : "",
                hapticHash = HapticHash(selected),
                mode = settings == null ? "NotApplicable" : mode.ToString(),
                condition = purpose == ResearchRecordingPurpose.DirectHapticTest ? "PCStream" : haptic.ToString(), sensorInfo = sensorInfo,
                requestedImuHz = requestedHz, observationSeconds = nominalSeconds,
                appBuild = Application.buildGUID
            };
            File.WriteAllText(Path.Combine(directory, "metadata.json"), JsonUtility.ToJson(metadata, true));
            Recording = true;
            Status = "Recording " + id;
            Event("recording_start", label);
            Anchor();
            LongHapticPlayer.Command += HapticCommand;
            return true;
        }
        catch (Exception exception) { Fail(exception); return false; }
    }

    private StreamWriter Writer(string name, string header)
    {
        var writer = new StreamWriter(Path.Combine(directory, name), false, new UTF8Encoding(false));
        writer.WriteLine(header); return writer;
    }
    private static string Number(double value) => value.ToString("R", Inv);
    private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
    public void Event(string kind, string value = "")
    {
        if (!Recording) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            if (kind == "observation_start") observationStarted = now;
            events.WriteLine(Number(now) + "," + Number(AudioSettings.dspTime) + "," + Number(now - started) + "," + Number(observationStarted < 0 ? -1 : now - observationStarted) + "," + Episode + "," + Csv(kind) + "," + Csv(value));
            if (kind == "on_request") onCount++;
            if (kind == "off_request") offCount++;
            if (kind == "nominal_end") finishingAt = now;
        }
        catch (Exception exception) { Fail(exception); }
    }
    private void HapticCommand(string kind, string value) => Event(kind, value);
    private void Anchor()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        double before = Time.realtimeSinceStartupAsDouble;
        long boot = sensor.Call<long>("clockNanos");
        double after = Time.realtimeSinceStartupAsDouble;
        Event("clock_anchor", "boot_ns=" + boot + ";unity_mid_s=" + Number((before + after) * .5) + ";roundtrip_s=" + Number(after - before));
#else
        Event("clock_anchor", "editor_no_boot_clock");
#endif
    }
    private void LateUpdate()
    {
        if (!Recording) return;
        try
        {
            double now = Time.realtimeSinceStartupAsDouble;
            double scenarioTime = player != null ? player.ScenarioSeconds : 0;
            string blockId = "";
            bool contact = player != null && player.IsPlaying && !player.IsPrepared && scenarioTime >= 0 &&
                ScenarioAnimationTimeline.TrySample(player.profile, (float)scenarioTime, out var sample) &&
                sample.block.kind == AnimationBlockKind.Contact;
            if (player != null && player.IsPlaying && ScenarioAnimationTimeline.TrySample(player.profile, (float)scenarioTime, out var current))
                blockId = current.block.blockId;
            if (blockId != lastBlock) { Event("sampled_block", blockId); lastBlock = blockId; }
            if (contact) contactSeconds += Math.Max(0, now - lastHeadTime);
            lastHeadTime = now;
            Camera camera = Camera.main;
            bool tracked = camera != null;
#if !UNITY_EDITOR
            var device = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head);
            tracked = device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool valid) && valid;
#endif
            Quaternion q = camera != null ? camera.transform.localRotation : Quaternion.identity;
            head.WriteLine(HeadSamples + "," + Number(now) + "," + Number(AudioSettings.dspTime) + "," + Number(now - started) + "," + Number(observationStarted < 0 ? -1 : now - observationStarted) + "," + Number(scenarioTime) + "," + Episode + "," + Csv(Phase) + "," + Csv(blockId) + "," + Number(q.x) + "," + Number(q.y) + "," + Number(q.z) + "," + Number(q.w) + "," + (tracked ? "1" : "0") + "," + Time.frameCount);
            HeadSamples++;
            if (now >= nextFlush)
            {
                Drain(); Anchor(); head.Flush(); imu.Flush(); events.Flush(); nextFlush = now + .5;
            }
        }
        catch (Exception exception) { Fail(exception); }
    }
    private void Drain()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (sensor == null) return;
        string data = sensor.Call<string>("drain");
        imu.Write(data);
        foreach (string row in data.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            ImuSamples++;
            var fields = row.Split(',');
            if (fields.Length != 7 || !long.TryParse(fields[1], NumberStyles.Integer, Inv, out long stamp)) continue;
            if (fields[2] == "gyro") { if (gyroCount++ == 0) gyroFirst = stamp; gyroLast = stamp; }
            if (fields[2] == "accel") { if (accelCount++ == 0) accelFirst = stamp; accelLast = stamp; }
        }
#endif
    }
    private static double SensorHz(long count, long first, long last) => count < 2 || last <= first ? 0 : (count - 1) * 1e9 / (last - first);
    public void End(string reason, bool completed)
    {
        if (!Recording) { Close(); return; }
        try
        {
            Event("recording_end", reason);
            long dropped = 0;
#if UNITY_ANDROID && !UNITY_EDITOR
            sensor?.Call("stop");
            if (sensor != null) dropped = sensor.Call<long>("droppedCount");
#endif
            Drain();
            // A summary must not claim success while buffered data has not reached disk.
            head.Flush(); imu.Flush(); events.Flush();
            double now = Time.realtimeSinceStartupAsDouble;
            var summary = new Summary {
                endReason = reason, completed = completed, error = Error,
                actualSeconds = observationStarted < 0 ? 0 : now - observationStarted, nominalSeconds = nominalSeconds,
                recordingSeconds = now - started, observedGyroHz = GyroHz, observedAccelHz = AccelHz,
                finishingSeconds = finishingAt < 0 ? 0 : now - finishingAt,
                contactSeconds = contactSeconds, headSamples = HeadSamples, imuSamples = ImuSamples,
                nativeDropped = dropped, onRequests = onCount, offRequests = offCount
            };
            File.WriteAllText(Path.Combine(directory, "summary.json"), JsonUtility.ToJson(summary, true));
            Status = completed ? "Saved " + directory : "Incomplete: " + reason;
        }
        catch (Exception exception)
        {
            Error = exception.Message; Status = "LOG ERROR: " + Error; Debug.LogException(exception);
            try { File.WriteAllText(Path.Combine(directory, "failure.txt"), Error); } catch (Exception) { }
        }
        finally { Recording = false; Close(); }
    }
    private void Fail(Exception exception)
    {
        Error = exception.Message; Status = "LOG ERROR: " + Error;
        Recording = false; Close();
        try { if (!string.IsNullOrEmpty(directory)) File.WriteAllText(Path.Combine(directory, "failure.txt"), DateTime.UtcNow.ToString("O") + " " + Error); }
        catch (Exception) { }
        Debug.LogException(exception); Failed?.Invoke(Error);
    }
    private void Close()
    {
        LongHapticPlayer.Command -= HapticCommand;
#if UNITY_ANDROID && !UNITY_EDITOR
        try { sensor?.Call("stop"); } catch (Exception) { }
        sensor?.Dispose(); sensor = null;
#endif
        foreach (var writer in new[] { head, imu, events })
        { try { writer?.Dispose(); } catch (Exception exception) { Error = exception.Message; } }
        head = imu = events = null;
        if (!string.IsNullOrEmpty(directory)) activeDirectories.Remove(directory);
    }
    public static string Export(string directory)
    {
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return "No saved recording selected.";
        ResearchLogLibrary.RequireManageable(directory);
        string zip = directory.TrimEnd(Path.DirectorySeparatorChar) + ".zip";
        if (File.Exists(zip)) zip = directory + "_" + DateTime.UtcNow.ToString("HHmmssfff", Inv) + ".zip";
        ZipFile.CreateFromDirectory(directory, zip, System.IO.Compression.CompressionLevel.Fastest, false);
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var bridge = new AndroidJavaClass("com.xrteam.research.ResearchImu"))
            return bridge.CallStatic<string>("exportZip", unity.GetStatic<AndroidJavaObject>("currentActivity"), zip);
#else
        return "ZIP: " + zip;
#endif
    }
    private void OnApplicationPause(bool paused) { if (paused && Recording) { Event("app_pause"); End("app_pause", false); Failed?.Invoke("App paused"); } }
    private void OnApplicationFocus(bool focused) { if (Recording) Event("app_focus", focused.ToString()); }
    private void OnDisable() { if (Recording) End("recorder_disabled", false); else Close(); }
}

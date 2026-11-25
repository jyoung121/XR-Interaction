package com.xrteam.research;

import android.app.Activity;
import android.content.ContentValues;
import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.provider.MediaStore;
import android.net.Uri;
import java.io.*;
import java.util.ArrayDeque;

/** Sensor callbacks retain native timestamps; no Unity frame resampling or filtering. */
public final class ResearchImu implements SensorEventListener {
    private final SensorManager manager;
    private final ArrayDeque<String> rows = new ArrayDeque<>();
    private HandlerThread thread;
    private long sequence, dropped;
    private volatile boolean running;
    public ResearchImu(Activity activity) { manager = (SensorManager) activity.getSystemService(Context.SENSOR_SERVICE); }
    public String start(int hz) {
        stop();
        synchronized(rows) { rows.clear(); sequence = 0; dropped = 0; }
        Sensor gyro = manager.getDefaultSensor(Sensor.TYPE_GYROSCOPE);
        Sensor accel = manager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
        if (gyro == null || accel == null) return "ERROR: accelerometer and gyroscope are required";
        thread = new HandlerThread("ResearchImu"); thread.start();
        Handler handler = new Handler(thread.getLooper());
        running = true;
        int periodUs = 1000000 / Math.max(20, Math.min(200, hz));
        boolean a = manager.registerListener(this, gyro, periodUs, handler);
        boolean b = manager.registerListener(this, accel, periodUs, handler);
        if (!a || !b) { stop(); return "ERROR: sensor registration failed"; }
        return "gyro=" + gyro.getName() + ";accel=" + accel.getName() + ";requested_hz=" + hz;
    }
    public long clockNanos() { return SystemClock.elapsedRealtimeNanos(); }
    public long droppedCount() { synchronized(rows) { return dropped; } }
    public void stop() {
        running = false;
        manager.unregisterListener(this);
        if (thread != null) { thread.quitSafely(); thread = null; }
    }
    @Override public void onSensorChanged(SensorEvent event) {
        synchronized(rows) {
            if (!running) return;
            if (rows.size() >= 8192) { rows.removeFirst(); dropped++; }
            String type = event.sensor.getType() == Sensor.TYPE_GYROSCOPE ? "gyro" : "accel";
            rows.addLast((sequence++) + "," + event.timestamp + "," + type + "," +
                Float.toString(event.values[0]) + "," + Float.toString(event.values[1]) + "," +
                Float.toString(event.values[2]) + "," + event.accuracy + "\n");
        }
    }
    @Override public void onAccuracyChanged(Sensor sensor, int accuracy) { }
    public String drain() {
        StringBuilder result = new StringBuilder();
        synchronized(rows) { while(!rows.isEmpty()) result.append(rows.removeFirst()); }
        return result.toString();
    }
    public static String exportZip(Activity activity, String path) throws Exception {
        if (Build.VERSION.SDK_INT < 29) return "Export requires Android 10+. ZIP saved at: " + path;
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, new File(path).getName());
        values.put(MediaStore.MediaColumns.MIME_TYPE, "application/zip");
        values.put(MediaStore.MediaColumns.RELATIVE_PATH, "Download/XRExperiment");
        values.put(MediaStore.MediaColumns.IS_PENDING, 1);
        Uri uri = activity.getContentResolver().insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
        if (uri == null) throw new IOException("Cannot create Downloads entry");
        try {
            try (InputStream input = new FileInputStream(path);
                 OutputStream output = activity.getContentResolver().openOutputStream(uri)) {
                if (output == null) throw new IOException("Cannot open export output");
                byte[] buffer = new byte[32768]; int length;
                while ((length = input.read(buffer)) != -1) output.write(buffer, 0, length);
            }
            values.clear(); values.put(MediaStore.MediaColumns.IS_PENDING, 0);
            activity.getContentResolver().update(uri, values, null, null);
        } catch (Exception failure) {
            activity.getContentResolver().delete(uri, null, null);
            throw failure;
        }
        return "Exported to Downloads/XRExperiment/" + new File(path).getName();
    }
}

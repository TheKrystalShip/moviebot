package com.thekrystalship.cinema.phone;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.ConnectivityManager;
import android.net.Network;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;

/**
 * The downloads, run with no page: a foreground service of the data transfer kind, which Android
 * keeps running and on the network through Doze and with the app out of view, as it does not for a
 * browser's page. It runs {@link Downloads#work} on a thread of its own, holds a partial wake lock
 * and a Wi-Fi lock while it does, and shows one notification with the save being fetched, how far
 * it has got and how many wait behind it. A network coming back wakes a transfer waiting between
 * attempts. When nothing is left it says so and stops itself.
 *
 * Android stops a data transfer service once it has run six hours in a day ({@link #onTimeout});
 * what is not fetched by then waits, and carries on the next time the app is opened.
 */
public final class DownloadService extends Service {
    private static final String CHANNEL = "downloads";
    private static final int ONGOING = 1;
    private static final int FINISHED = 2;
    private static final long DRAW_MS = 1000;

    private Thread worker;
    private PowerManager.WakeLock cpu;
    private WifiManager.WifiLock wifi;
    private long drawn;
    private ConnectivityManager.NetworkCallback network;

    /** Starts the service if anything is left to fetch; the activity calls it, while it is in view. */
    static void start(Context context) {
        if (!Downloads.of(context).pending()) return;
        Intent intent = new Intent(context, DownloadService.class);
        if (Build.VERSION.SDK_INT >= 26) context.startForegroundService(intent);
        else context.startService(intent);
    }

    @Override
    public void onCreate() {
        super.onCreate();
        NotificationManager notifications = getSystemService(NotificationManager.class);
        notifications.createNotificationChannel(
            new NotificationChannel(CHANNEL, getString(R.string.downloads_channel), NotificationManager.IMPORTANCE_LOW));
        PowerManager power = getSystemService(PowerManager.class);
        cpu = power.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "KrystalCinema:downloads");
        WifiManager radio = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
        //noinspection deprecation
        wifi = radio.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "KrystalCinema:downloads");
        final Downloads downloads = Downloads.of(this);
        network = new ConnectivityManager.NetworkCallback() {
            @Override
            public void onAvailable(Network available) {
                downloads.networkBack();
            }
        };
        getSystemService(ConnectivityManager.class).registerDefaultNetworkCallback(network);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        Notification ongoing = ongoing(Downloads.of(this).progress());
        if (Build.VERSION.SDK_INT >= 29) startForeground(ONGOING, ongoing, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
        else startForeground(ONGOING, ongoing);
        if (worker == null || !worker.isAlive()) {
            final Downloads downloads = Downloads.of(this);
            downloads.onChanged(new Runnable() {
                @Override
                public void run() {
                    draw();
                }
            });
            worker = new Thread(new Runnable() {
                @Override
                public void run() {
                    cpu.acquire();
                    wifi.acquire();
                    try {
                        downloads.work(Thread.currentThread());
                    } finally {
                        if (wifi.isHeld()) wifi.release();
                        if (cpu.isHeld()) cpu.release();
                        finished();
                    }
                }
            }, "downloads");
            worker.start();
        }
        return START_NOT_STICKY;
    }

    /** Android has run the service as long as it allows in a day: what is left waits for the app. */
    @Override
    public void onTimeout(int startId, int type) {
        if (worker != null) worker.interrupt();
        stopSelf();
    }

    @Override
    public void onDestroy() {
        if (worker != null) worker.interrupt();
        getSystemService(ConnectivityManager.class).unregisterNetworkCallback(network);
        Downloads.of(this).onChanged(null);
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }

    /** Draws the notification again, at most once a second. */
    private void draw() {
        long now = System.currentTimeMillis();
        if (now - drawn < DRAW_MS) return;
        drawn = now;
        getSystemService(NotificationManager.class).notify(ONGOING, ongoing(Downloads.of(this).progress()));
    }

    private void finished() {
        stopForeground(STOP_FOREGROUND_REMOVE);
        stopSelf();
        if (Downloads.of(this).pending()) return;
        Notification done = builder()
            .setContentTitle("Downloads finished")
            .setAutoCancel(true)
            .build();
        getSystemService(NotificationManager.class).notify(FINISHED, done);
    }

    private Notification ongoing(Downloads.Progress p) {
        Notification.Builder b = builder().setOngoing(true).setOnlyAlertOnce(true);
        if (p.name == null) return b.setContentTitle("Downloading").build();
        int percent = p.bytes > 0 ? (int) (100 * p.received / p.bytes) : 0;
        String text = (p.waiting ? "Waiting for a connection" : percent + "%") + (p.more > 0 ? " · " + p.more + " more" : "");
        return b.setContentTitle(p.name).setContentText(text).setProgress(100, percent, p.waiting).build();
    }

    private Notification.Builder builder() {
        Intent open = new Intent(this, CinemaActivity.class).setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent tap = PendingIntent.getActivity(this, 0, open, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
        Notification.Builder b = new Notification.Builder(this, CHANNEL)
            .setSmallIcon(R.drawable.notification)
            .setContentIntent(tap);
        if (Build.VERSION.SDK_INT >= 31) b.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE);
        return b;
    }
}

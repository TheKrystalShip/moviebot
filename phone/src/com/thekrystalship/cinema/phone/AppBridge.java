package com.thekrystalship.cinema.phone;

import android.Manifest;
import android.content.pm.PackageManager;
import android.os.Build;
import android.webkit.JavascriptInterface;

import org.json.JSONException;
import org.json.JSONObject;

/**
 * What the page reaches of the app, as {@code window.KrystalCinemaApp}: the downloads. The page hands
 * a save's tracks over with the save's grant and reads back how far each has got; the app fetches
 * them with no page running ({@link DownloadService}) and serves them back to the page once whole
 * ({@link LocalTracks}). Every answer is a JSON string, since that is what a bridge carries.
 *
 * Every call arrives on the WebView's bridge thread, never the page's. A WebView puts the bridge on
 * every page it loads, so each call answers only while the site's own page is showing
 * ({@link CinemaActivity#onSite}): the sign-in's pages, on other origins, reach nothing.
 */
final class AppBridge {
    private final CinemaActivity activity;
    private final Server server;
    private final Downloads downloads;

    AppBridge(CinemaActivity activity, Server server, Downloads downloads) {
        this.activity = activity;
        this.server = server;
        this.downloads = downloads;
    }

    /** The app's version, which is also how a page knows it runs inside the app. */
    @JavascriptInterface
    public String version() {
        return Built.VERSION;
    }

    /**
     * Takes a save over: {@code {saveId, titleId, name, grant: {header, value, expiresAt}, tracks:
     * [{rendition, url, bytes, etag}]}}, the URLs on the site. Answers {@code "ok"}, or why not.
     */
    @JavascriptInterface
    public String download(String json) {
        if (!activity.onSite()) return "This page cannot download.";
        try {
            JSONObject save = new JSONObject(json);
            save.put("origin", server.origin());
            downloads.add(save);
        } catch (JSONException refused) {
            return refused.getMessage();
        }
        askToNotify();
        DownloadService.start(activity);
        return "ok";
    }

    /** One save's status as JSON, or {@code "null"} when the app holds none by that id. */
    @JavascriptInterface
    public String status(String saveId) {
        if (!activity.onSite()) return "null";
        JSONObject status = downloads.status(saveId);
        return status != null ? status.toString() : "null";
    }

    /** Every save's status, by save id. */
    @JavascriptInterface
    public String statuses() {
        if (!activity.onSite()) return "{}";
        return downloads.statuses().toString();
    }

    @JavascriptInterface
    public void pause(String saveId) {
        if (activity.onSite()) downloads.pause(saveId);
    }

    @JavascriptInterface
    public void resume(String saveId) {
        if (!activity.onSite()) return;
        downloads.resume(saveId);
        DownloadService.start(activity);
    }

    @JavascriptInterface
    public void remove(String saveId) {
        if (activity.onSite()) downloads.remove(saveId);
    }

    /** Where the page reads a whole track of a save: on the site's origin, answered by the app. */
    @JavascriptInterface
    public String trackUrl(String saveId, String rendition) {
        return server.origin() + LocalTracks.PREFIX + saveId + "/" + rendition + ".mp4";
    }

    /** The download notification needs leave to show from Android 13; asked once, with the first download. */
    private void askToNotify() {
        if (Build.VERSION.SDK_INT < 33) return;
        if (activity.checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                activity.requestPermissions(new String[] {Manifest.permission.POST_NOTIFICATIONS}, 1);
            }
        });
    }
}

package com.thekrystalship.cinema.phone;

import android.content.Context;
import android.util.Log;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.RandomAccessFile;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Map;

/**
 * The tracks of every save this phone is downloading or holds, kept under the app's own files:
 * {@code saves/<saveId>/save.json}, what the page handed over and how far it has got, beside one
 * {@code <rendition>.mp4} per track.
 *
 * The page hands a save over ({@link #add}) with the save's grant, which opens its tracks with no
 * access token for as long as the save holds its title, so a transfer carries on for hours with no
 * page running. One save is fetched at a time, one track after another, by {@link #work}, which the
 * foreground service runs. A track resumes from its length on disk, by {@code Range} with
 * {@code If-Range} naming the validator the bytes on disk arrived under; a whole {@code 200} to a
 * resume means the track changed, and it starts over. A connection that breaks or goes quiet for
 * {@link #READ_TIMEOUT_MS} is tried again, after a pause that grows to a minute and ends early when a
 * network comes back ({@link #networkBack}), for as long as the save is not paused: a phone loses its
 * connection all the time and gets it back. A {@code 401} or
 * {@code 409} is the grant or the save gone, which only the page can renew, so the save fails with
 * that said. A save whose every track is whole is told to the API on its grant.
 *
 * Everything here is one lock: the page's calls arrive on the WebView's bridge thread and the
 * transfer runs on the service's.
 */
final class Downloads {
    private static final String TAG = "Downloads";
    static final int CONNECT_TIMEOUT_MS = 15000;
    static final int READ_TIMEOUT_MS = 30000;
    private static final long[] BACKOFF_MS = {2000, 5000, 15000, 30000, 60000};
    private static final int BUFFER = 256 * 1024;
    private static final long PROGRESS_MS = 500;

    private static Downloads instance;

    private final File root;
    private final Map<String, JSONObject> saves = new LinkedHashMap<>();
    /** What each save is doing now, by id: not kept, rebuilt from the files when the process starts. */
    private final Map<String, String> states = new LinkedHashMap<>();
    private final Map<String, String> messages = new LinkedHashMap<>();
    private String running;
    private HttpURLConnection connection;
    private static final Runnable NOTHING = new Runnable() {
        @Override
        public void run() {
        }
    };

    private Runnable changed = NOTHING;
    private Finished finished = null;
    /** What a pause between attempts waits on, so a network coming back ends it at once. */
    private final Object nap = new Object();

    private Downloads(Context context) {
        root = new File(context.getFilesDir(), "saves");
        File[] dirs = root.listFiles();
        if (dirs == null) return;
        for (File dir : dirs) {
            JSONObject save = read(new File(dir, "save.json"));
            if (save == null) continue;
            String id = save.optString("saveId");
            saves.put(id, save);
            states.put(id, save.has("completedAt") ? "done" : save.optBoolean("paused") ? "paused" : "queued");
            if (save.has("failed")) {
                states.put(id, "failed");
                messages.put(id, save.optString("failed"));
            }
        }
    }

    static synchronized Downloads of(Context context) {
        if (instance == null) instance = new Downloads(context.getApplicationContext());
        return instance;
    }

    /** What hears that a save is whole: its id and the name the page gave it. */
    interface Finished {
        void finished(String saveId, String name);
    }

    /** Called with each save that becomes whole, on the transfer's thread. */
    synchronized void onFinished(Finished listener) {
        finished = listener;
    }

    /** Called after every change a person could see, from whichever thread made it. */
    synchronized void onChanged(Runnable listener) {
        changed = listener != null ? listener : NOTHING;
    }

    /**
     * Takes a save over from the page: {@code {saveId, titleId, name, origin, grant: {header, value,
     * expiresAt}, tracks: [{rendition, url, bytes, etag}]}}. A save already here keeps the bytes it
     * holds of each track, which the server judges by their validator, and takes the new grant.
     */
    synchronized void add(JSONObject handed) throws JSONException {
        String id = handed.getString("saveId");
        File dir = dirOf(id);
        if (!dir.isDirectory() && !dir.mkdirs()) throw new JSONException("No room for " + id);
        JSONObject before = saves.get(id);
        JSONArray tracks = handed.getJSONArray("tracks");
        for (int i = 0; i < tracks.length(); i++) {
            JSONObject track = tracks.getJSONObject(i);
            JSONObject held = before != null ? trackOf(before, track.getString("rendition")) : null;
            if (held != null && held.has("etag")) track.put("etag", held.getString("etag"));
        }
        handed.remove("paused");
        handed.remove("failed");
        handed.remove("completedAt");
        handed.remove("told");
        saves.put(id, handed);
        write(id);
        states.put(id, "queued");
        messages.remove(id);
        changed.run();
    }

    synchronized void pause(String id) {
        JSONObject save = saves.get(id);
        if (save == null || save.has("completedAt")) return;
        put(save, "paused", true);
        write(id);
        states.put(id, "paused");
        if (id.equals(running)) cut();
        changed.run();
    }

    synchronized void resume(String id) {
        JSONObject save = saves.get(id);
        if (save == null || save.has("completedAt")) return;
        save.remove("paused");
        save.remove("failed");
        write(id);
        states.put(id, "queued");
        messages.remove(id);
        changed.run();
    }

    synchronized void remove(String id) {
        saves.remove(id);
        states.remove(id);
        messages.remove(id);
        if (id.equals(running)) cut();
        delete(dirOf(id));
        changed.run();
    }

    /** One save as the page reads it, or null: {@code {saveId, titleId, state, received, bytes, message, tracks}}. */
    synchronized JSONObject status(String id) {
        JSONObject save = saves.get(id);
        if (save == null) return null;
        JSONObject out = new JSONObject();
        long received = 0;
        long bytes = 0;
        JSONArray tracks = new JSONArray();
        JSONArray listed = save.optJSONArray("tracks");
        for (int i = 0; listed != null && i < listed.length(); i++) {
            JSONObject track = listed.optJSONObject(i);
            long length = fileOf(id, track.optString("rendition")).length();
            long want = track.optLong("bytes");
            received += Math.min(length, want);
            bytes += want;
            JSONObject t = new JSONObject();
            put(t, "rendition", track.optString("rendition"));
            put(t, "received", Math.min(length, want));
            put(t, "bytes", want);
            tracks.put(t);
        }
        put(out, "saveId", id);
        put(out, "titleId", save.optString("titleId"));
        put(out, "state", states.get(id));
        put(out, "received", received);
        put(out, "bytes", bytes);
        put(out, "tracks", tracks);
        if (messages.containsKey(id)) put(out, "message", messages.get(id));
        return out;
    }

    /** Every save's status, by save id. */
    synchronized JSONObject statuses() {
        JSONObject out = new JSONObject();
        for (String id : saves.keySet()) put(out, id, status(id));
        return out;
    }

    /** Whether anything is left to fetch: a save neither done, paused nor failed. */
    synchronized boolean pending() {
        for (String state : states.values()) if ("queued".equals(state) || "downloading".equals(state) || "waiting".equals(state)) return true;
        return false;
    }

    /** What the notification says: the save being fetched, how far, and how many wait behind it. */
    synchronized Progress progress() {
        String id = running;
        int more = 0;
        for (Map.Entry<String, String> e : states.entrySet()) {
            if (e.getKey().equals(id)) continue;
            if ("queued".equals(e.getValue()) || "waiting".equals(e.getValue())) more++;
        }
        if (id == null || !saves.containsKey(id)) return new Progress(null, 0, 0, more, false);
        JSONObject status = status(id);
        return new Progress(saves.get(id).optString("name", saves.get(id).optString("titleId")),
            status.optLong("received"), status.optLong("bytes"), more, "waiting".equals(states.get(id)));
    }

    /** A track's file, for the page to read once whole: under the save's directory, by rendition. */
    File fileOf(String id, String rendition) {
        return new File(dirOf(id), rendition.replaceAll("[^A-Za-z0-9_-]", "_") + ".mp4");
    }

    /** Whether a save's track is whole on disk, which is when the page may read it. */
    synchronized boolean whole(String id, String rendition) {
        JSONObject save = saves.get(id);
        JSONObject track = save != null ? trackOf(save, rendition) : null;
        return track != null && fileOf(id, rendition).length() == track.optLong("bytes");
    }

    // ---- The transfer, on the service's thread ----

    /** Fetches every save that is due, one after another, until none is left or the service is stopped. */
    void work(Thread self) {
        while (!self.isInterrupted()) {
            String id = next();
            if (id == null) return;
            try {
                fetch(id, self);
            } catch (InterruptedException stopped) {
                return;
            } finally {
                synchronized (this) {
                    running = null;
                    connection = null;
                }
            }
        }
    }

    private synchronized String next() {
        for (Map.Entry<String, String> e : states.entrySet()) {
            if ("queued".equals(e.getValue()) || "waiting".equals(e.getValue())) {
                running = e.getKey();
                states.put(running, "downloading");
                changed.run();
                return running;
            }
        }
        return null;
    }

    private void fetch(String id, Thread self) throws InterruptedException {
        JSONObject save;
        synchronized (this) {
            save = saves.get(id);
        }
        if (save == null) return;
        JSONArray tracks = save.optJSONArray("tracks");
        for (int i = 0; tracks != null && i < tracks.length(); i++) {
            if (!stillMine(id)) return;
            Outcome outcome = track(id, save, tracks.optJSONObject(i), self);
            if (outcome != Outcome.WHOLE) return;
        }
        tell(id, save);
    }

    private enum Outcome { WHOLE, STOPPED, FAILED }

    private Outcome track(String id, JSONObject save, JSONObject track, Thread self) throws InterruptedException {
        String rendition = track.optString("rendition");
        long bytes = track.optLong("bytes");
        File file = fileOf(id, rendition);
        int attempt = 0;
        while (stillMine(id)) {
            long at = file.length();
            if (at > bytes) {
                truncate(file);
                at = 0;
            }
            if (at == bytes) return Outcome.WHOLE;
            HttpURLConnection http = null;
            try {
                http = (HttpURLConnection) new URL(save.getString("origin") + track.getString("url")).openConnection();
                JSONObject grant = save.getJSONObject("grant");
                http.setRequestProperty(grant.getString("header"), grant.getString("value"));
                http.setConnectTimeout(CONNECT_TIMEOUT_MS);
                http.setReadTimeout(READ_TIMEOUT_MS);
                http.setUseCaches(false);
                if (at > 0) {
                    http.setRequestProperty("Range", "bytes=" + at + "-");
                    if (track.has("etag")) http.setRequestProperty("If-Range", track.getString("etag"));
                }
                synchronized (this) {
                    if (!id.equals(running)) return Outcome.STOPPED;
                    connection = http;
                }
                int code = http.getResponseCode();
                if (code == 401 || code == 403 || code == 409 || code == 404) {
                    fail(id, code == 409 || code == 404
                        ? "This download is no longer on the server. Open Krystal Cinema to download it again."
                        : "Open Krystal Cinema to carry on with this download.");
                    return Outcome.FAILED;
                }
                if (code == 429) {
                    waiting(id);
                    rest(retryAfter(http) * 1000L);
                    continue;
                }
                if (code == 200 && at > 0) {
                    // The track changed under the bytes on disk: they go, and this answer is the track.
                    truncate(file);
                    at = 0;
                }
                if (code == 206 && !String.valueOf(http.getHeaderField("Content-Range")).startsWith("bytes " + at + "-")) {
                    truncate(file);
                    continue;
                }
                if (code != 200 && code != 206) throw new IOException("MovieBot answered " + code);
                String etag = http.getHeaderField("ETag");
                if (etag != null && !etag.equals(track.optString("etag", null))) {
                    synchronized (this) {
                        put(track, "etag", etag);
                        write(id);
                    }
                }
                downloading(id);
                copy(id, http.getInputStream(), file, at, bytes);
                attempt = 0;
            } catch (IOException | JSONException broken) {
                if (!stillMine(id)) return Outcome.STOPPED;
                Log.i(TAG, id + " " + rendition + " broke at " + file.length() + ": " + broken.getMessage());
                waiting(id);
                rest(BACKOFF_MS[Math.min(attempt++, BACKOFF_MS.length - 1)]);
            } finally {
                if (http != null) http.disconnect();
                synchronized (this) {
                    connection = null;
                }
            }
            if (self.isInterrupted()) throw new InterruptedException();
        }
        return Outcome.STOPPED;
    }

    private void copy(String id, InputStream in, File file, long at, long bytes) throws IOException {
        byte[] buffer = new byte[BUFFER];
        long last = 0;
        try (RandomAccessFile out = new RandomAccessFile(file, "rw"); InputStream body = in) {
            out.seek(at);
            long done = at;
            while (done < bytes) {
                int n = body.read(buffer, 0, (int) Math.min(buffer.length, bytes - done));
                if (n < 0) break;
                out.write(buffer, 0, n);
                done += n;
                long now = System.currentTimeMillis();
                if (now - last >= PROGRESS_MS) {
                    last = now;
                    changed();
                }
            }
            out.getFD().sync();
        }
        changed();
    }

    /** Tells the API the save is whole, on its grant; the page tells it again for one this could not. */
    private void tell(String id, JSONObject save) {
        synchronized (this) {
            if (!id.equals(running)) return;
            put(save, "completedAt", System.currentTimeMillis());
            write(id);
            states.put(id, "done");
            if (finished != null) finished.finished(id, save.optString("name", save.optString("titleId")));
        }
        changed();
        HttpURLConnection http = null;
        try {
            http = (HttpURLConnection) new URL(save.getString("origin") + "/api/me/offline/" + id + "/complete").openConnection();
            JSONObject grant = save.getJSONObject("grant");
            http.setRequestMethod("POST");
            http.setRequestProperty(grant.getString("header"), grant.getString("value"));
            http.setConnectTimeout(CONNECT_TIMEOUT_MS);
            http.setReadTimeout(READ_TIMEOUT_MS);
            http.setDoOutput(true);
            http.setFixedLengthStreamingMode(0);
            http.getOutputStream().close();
            if (http.getResponseCode() / 100 == 2) {
                synchronized (this) {
                    put(save, "told", true);
                    write(id);
                }
            }
        } catch (IOException | JSONException unreached) {
            Log.i(TAG, id + " finished, and the API could not be told: " + unreached.getMessage());
        } finally {
            if (http != null) http.disconnect();
        }
    }

    /** Whether the transfer of a save is still wanted: a pause or a removal lets go of it. */
    /** Waits between attempts, until the time is up or a network comes back. */
    private void rest(long ms) throws InterruptedException {
        synchronized (nap) {
            nap.wait(ms);
        }
    }

    /** A network is there again: a transfer waiting between attempts tries at once. */
    void networkBack() {
        synchronized (nap) {
            nap.notifyAll();
        }
    }

    private synchronized boolean stillMine(String id) {
        return id.equals(running) && saves.containsKey(id);
    }

    private synchronized void downloading(String id) {
        if (!id.equals(running)) return;
        states.put(id, "downloading");
        changed.run();
    }

    private synchronized void waiting(String id) {
        if (!id.equals(running)) return;
        states.put(id, "waiting");
        changed.run();
    }

    private synchronized void fail(String id, String message) {
        JSONObject save = saves.get(id);
        if (save == null) return;
        put(save, "failed", message);
        write(id);
        states.put(id, "failed");
        messages.put(id, message);
        changed.run();
    }

    private synchronized void changed() {
        changed.run();
    }

    /** Stops the transfer under way at once: its connection is closed under the reading thread. */
    private void cut() {
        running = null;
        final HttpURLConnection open = connection;
        connection = null;
        if (open == null) return;
        new Thread(new Runnable() {
            @Override
            public void run() {
                open.disconnect();
            }
        }).start();
    }

    private static int retryAfter(HttpURLConnection http) {
        try {
            return Math.max(1, Integer.parseInt(String.valueOf(http.getHeaderField("Retry-After")).trim()));
        } catch (NumberFormatException none) {
            return 10;
        }
    }

    // ---- The files ----

    private File dirOf(String id) {
        return new File(root, id.replaceAll("[^A-Za-z0-9_-]", "_"));
    }

    private static JSONObject trackOf(JSONObject save, String rendition) {
        JSONArray tracks = save.optJSONArray("tracks");
        for (int i = 0; tracks != null && i < tracks.length(); i++) {
            JSONObject track = tracks.optJSONObject(i);
            if (track != null && rendition.equals(track.optString("rendition"))) return track;
        }
        return null;
    }

    /** Writes a save's record whole to a file beside it, then puts it in place, so a reader never meets half of one. */
    private void write(String id) {
        JSONObject save = saves.get(id);
        if (save == null) return;
        File dir = dirOf(id);
        File temp = new File(dir, "save.json.tmp");
        try (OutputStream out = new FileOutputStream(temp)) {
            out.write(save.toString().getBytes(StandardCharsets.UTF_8));
            out.flush();
        } catch (IOException failed) {
            Log.w(TAG, "Could not write " + id + ": " + failed.getMessage());
            return;
        }
        if (!temp.renameTo(new File(dir, "save.json"))) Log.w(TAG, "Could not put " + id + "'s record in place");
    }

    private static JSONObject read(File file) {
        try (InputStream in = new java.io.FileInputStream(file)) {
            byte[] all = new byte[(int) file.length()];
            int at = 0;
            while (at < all.length) {
                int n = in.read(all, at, all.length - at);
                if (n < 0) break;
                at += n;
            }
            return new JSONObject(new String(all, 0, at, StandardCharsets.UTF_8));
        } catch (IOException | JSONException unreadable) {
            return null;
        }
    }

    private static void truncate(File file) {
        try (RandomAccessFile f = new RandomAccessFile(file, "rw")) {
            f.setLength(0);
        } catch (IOException ignored) {
            // A file that cannot be opened is started again from nothing by the next request.
        }
    }

    private static void delete(File file) {
        File[] children = file.listFiles();
        if (children != null) for (File child : children) delete(child);
        //noinspection ResultOfMethodCallIgnored
        file.delete();
    }

    private static void put(JSONObject object, String key, Object value) {
        try {
            object.put(key, value);
        } catch (JSONException impossible) {
            throw new IllegalStateException(impossible);
        }
    }

    /** What the download notification shows. */
    static final class Progress {
        final String name;
        final long received;
        final long bytes;
        final int more;
        final boolean waiting;

        Progress(String name, long received, long bytes, int more, boolean waiting) {
            this.name = name;
            this.received = received;
            this.bytes = bytes;
            this.more = more;
            this.waiting = waiting;
        }
    }
}

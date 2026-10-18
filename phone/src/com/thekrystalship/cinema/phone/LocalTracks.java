package com.thekrystalship.cinema.phone;

import android.net.Uri;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;

import java.io.File;
import java.io.FileInputStream;
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/**
 * The tracks this phone downloaded, served to the page on the site's own origin under
 * {@link #PREFIX}{@code <saveId>/<rendition>.mp4}, so the player reads them as it reads a file:
 * a byte range at a time, named as {@code ?start=&end=} (end exclusive) and answered whole. The range
 * is never a {@code Range} header, because WebView applies one itself to the stream answered here,
 * which would skip what this already skipped. A track is served only once it is whole, since the
 * player reads a track as the film it is and a short one ends early. A request for one is answered
 * here and never reaches the network; any other request is the network's.
 */
final class LocalTracks {
    static final String PREFIX = "/__app/saves/";

    private final Server server;
    private final Downloads downloads;

    LocalTracks(Server server, Downloads downloads) {
        this.server = server;
        this.downloads = downloads;
    }

    /** The answer to a request for a downloaded track, or null for any other request. */
    WebResourceResponse answer(WebResourceRequest request) {
        Uri url = request.getUrl();
        if (!server.owns(url) || url.getPath() == null || !url.getPath().startsWith(PREFIX)) return null;
        List<String> parts = url.getPathSegments();
        if (parts.size() != 4 || !parts.get(3).endsWith(".mp4")) return status(404);
        String save = parts.get(2);
        String rendition = parts.get(3).substring(0, parts.get(3).length() - 4);
        if (!downloads.whole(save, rendition)) return status(404);

        File file = downloads.fileOf(save, rendition);
        long length = file.length();
        long start;
        long end;
        try {
            start = url.getQueryParameter("start") != null ? Long.parseLong(url.getQueryParameter("start")) : 0;
            end = url.getQueryParameter("end") != null ? Long.parseLong(url.getQueryParameter("end")) : length;
        } catch (NumberFormatException malformed) {
            return status(416);
        }
        if (start < 0 || end > length || start >= end) return status(416);

        Map<String, String> headers = new HashMap<>();
        headers.put("Cache-Control", "no-store");
        try {
            InputStream body = new Bounded(new FileInputStream(file), start, end - start);
            return new WebResourceResponse("video/mp4", null, 200, "OK", headers, body);
        } catch (IOException unreadable) {
            return status(404);
        }
    }

    private static WebResourceResponse status(int code) {
        Map<String, String> headers = new HashMap<>();
        headers.put("Cache-Control", "no-store");
        return new WebResourceResponse("text/plain", "utf-8", code, code == 404 ? "Not Found" : "Range Not Satisfiable", headers,
            new java.io.ByteArrayInputStream(new byte[0]));
    }

    /** A file from an offset, for a length, and no further. */
    private static final class Bounded extends FilterInputStream {
        private long left;

        Bounded(FileInputStream in, long skip, long length) throws IOException {
            super(in);
            in.getChannel().position(skip);
            left = length;
        }

        @Override
        public int read() throws IOException {
            if (left <= 0) return -1;
            int b = super.read();
            if (b >= 0) left--;
            return b;
        }

        @Override
        public int read(byte[] buffer, int offset, int count) throws IOException {
            if (left <= 0) return -1;
            int n = super.read(buffer, offset, (int) Math.min(count, left));
            if (n > 0) left -= n;
            return n;
        }

        @Override
        public int available() throws IOException {
            return (int) Math.min(super.available(), left);
        }
    }
}

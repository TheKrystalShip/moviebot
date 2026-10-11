package com.thekrystalship.cinema;

import android.app.Activity;
import android.net.Uri;
import android.os.Bundle;
import android.view.KeyEvent;
import android.webkit.ValueCallback;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

/**
 * Krystal Cinema on a television: the site in a full-screen WebView. The one thing of the app's own
 * the page can reach is {@code window.KrystalCinemaTV} ({@link Television}), which answers the TV's
 * name and nothing else.
 *
 * The app tells the site it is on a television by its mark in the user agent, and hands it the two
 * keys a page never receives, Back and Menu, as {@code window.krystalTv.back()} and
 * {@code window.krystalTv.menu()}; a Back the page did not use closes the app. Everything else is the
 * site's: the remote's arrows and OK reach the page as keys, a playing film keeps the screen on by
 * itself, and the session lives in the page's storage, kept across launches.
 *
 * The site's origin is the app's whole world. A link anywhere else is refused: a page off the site
 * cannot be driven by the remote here, and a TV's own browser, where it has one, is driven by an
 * on-screen cursor instead. A site that cannot
 * be reached is shown with a way to try again rather than the WebView's own error page.
 */
public final class CinemaActivity extends Activity {
    private static final String UNREACHABLE = "file:///android_asset/unreachable.html";
    private static final int BACKGROUND = 0xFF0B0F14;

    private WebView web;
    private Server server;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        server = Server.of(getIntent());
        WebView.setWebContentsDebuggingEnabled(Built.DEBUG);

        web = new WebView(this);
        web.setBackgroundColor(BACKGROUND);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setUserAgentString(settings.getUserAgentString() + " KrystalCinemaTV/" + Built.VERSION);
        web.setWebViewClient(new Client());
        web.addJavascriptInterface(new Television(getApplicationContext()), "KrystalCinemaTV");
        setContentView(web);
        web.loadUrl(server.start());
        web.requestFocus();
    }

    /**
     * Back and Menu go to the page when let go. Fire OS's keyboard takes Back for itself while it is
     * up, before it reaches here.
     */
    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        int code = event.getKeyCode();
        if (code != KeyEvent.KEYCODE_BACK && code != KeyEvent.KEYCODE_MENU) return super.dispatchKeyEvent(event);
        if (event.getAction() == KeyEvent.ACTION_UP && !event.isCanceled()) {
            final boolean back = code == KeyEvent.KEYCODE_BACK;
            String call = back ? "back" : "menu";
            web.evaluateJavascript("(window.krystalTv ? window.krystalTv." + call + "() === true : false)",
                new ValueCallback<String>() {
                    @Override
                    public void onReceiveValue(String used) {
                        if (back && !"true".equals(used)) finish();
                    }
                });
        }
        return true;
    }

    @Override
    protected void onResume() {
        super.onResume();
        web.onResume();
    }

    @Override
    protected void onPause() {
        web.onPause();
        super.onPause();
    }

    @Override
    protected void onDestroy() {
        web.destroy();
        super.onDestroy();
    }

    private final class Client extends WebViewClient {
        /** Anything off the site is refused; the site itself, and the page shown when it is down, load. */
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            return !server.owns(request.getUrl());
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (request.isForMainFrame()) unreachable(view, request.getUrl());
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            if (request.isForMainFrame() && response.getStatusCode() >= 500) unreachable(view, request.getUrl());
        }

        private void unreachable(WebView view, Uri failed) {
            String again = server.owns(failed) ? failed.toString() : server.start();
            view.loadUrl(UNREACHABLE + "?host=" + Uri.encode(server.host()) + "&to=" + Uri.encode(again));
        }
    }
}

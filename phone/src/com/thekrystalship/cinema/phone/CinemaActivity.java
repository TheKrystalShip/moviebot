package com.thekrystalship.cinema.phone;

import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Message;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.webkit.ServiceWorkerClient;
import android.webkit.ServiceWorkerController;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

/**
 * Krystal Cinema on a phone: the site in a WebView, and the downloads it starts run by the app
 * ({@link DownloadService}), which carries on with the screen off and the app out of view. The page
 * reaches the app as {@code window.KrystalCinemaApp} ({@link AppBridge}), and plays what the app
 * downloaded from the site's own origin ({@link LocalTracks}); the user agent carries
 * {@code KrystalCinemaApp/<version>}.
 *
 * Every page loads here, the sign-in's among them, so the session the site keeps in its storage is
 * the app's. A link that opens a new window goes to the phone's browser. A film the player puts full
 * screen fills the screen, with the system bars hidden, until it leaves. With no connection the site's
 * service worker answers its pages, the downloaded films among them; a site that cannot be reached
 * and has no such answer is shown with a way to try again. Back goes back a page, and leaves from the
 * first.
 */
public final class CinemaActivity extends Activity {
    private static final String UNREACHABLE = "file:///android_asset/unreachable.html";
    private static final int BACKGROUND = 0xFF0B0F14;
    private static final int QUIET_TRIES = 2;
    private static final int QUIET_AFTER_MS = 1500;

    private FrameLayout frame;
    private WebView web;
    private Server server;
    private LocalTracks tracks;
    private View fullScreen;
    private WebChromeClient.CustomViewCallback leaveFullScreen;
    /** The address of the page the WebView last began loading, read by the bridge on its own thread. */
    private volatile Uri showing;
    /** How many times in a row a page of the site has failed to load. */
    private int failures;

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        server = Server.of(getIntent());
        Downloads downloads = Downloads.of(this);
        tracks = new LocalTracks(server, downloads);
        WebView.setWebContentsDebuggingEnabled(Built.DEBUG);

        frame = new FrameLayout(this);
        frame.setBackgroundColor(BACKGROUND);
        web = new WebView(this);
        web.setBackgroundColor(BACKGROUND);
        WebSettings settings = web.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setMediaPlaybackRequiresUserGesture(false);
        settings.setSupportMultipleWindows(true);
        settings.setUserAgentString(settings.getUserAgentString() + " KrystalCinemaApp/" + Built.VERSION);
        web.setWebViewClient(new Client());
        web.setWebChromeClient(new Chrome());
        web.addJavascriptInterface(new AppBridge(this, server, downloads), "KrystalCinemaApp");
        // A request a service worker makes reaches the app here rather than through the page's client.
        ServiceWorkerController.getInstance().setServiceWorkerClient(new ServiceWorkerClient() {
            @Override
            public WebResourceResponse shouldInterceptRequest(WebResourceRequest request) {
                return tracks.answer(request);
            }
        });
        frame.addView(web, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        // Android draws the app under the system bars and the keyboard; the page is kept clear of them.
        frame.setOnApplyWindowInsetsListener(new View.OnApplyWindowInsetsListener() {
            @Override
            public WindowInsets onApplyWindowInsets(View view, WindowInsets insets) {
                if (Build.VERSION.SDK_INT >= 30) {
                    android.graphics.Insets bars = insets.getInsets(WindowInsets.Type.systemBars() | WindowInsets.Type.ime()
                        | WindowInsets.Type.displayCutout());
                    view.setPadding(bars.left, bars.top, bars.right, bars.bottom);
                }
                return WindowInsets.CONSUMED;
            }
        });
        setContentView(frame);
        web.loadUrl(server.start());
    }

    @Override
    public void onBackPressed() {
        if (fullScreen != null) {
            leaveFullScreen.onCustomViewHidden();
            return;
        }
        if (web.canGoBack()) web.goBack();
        else super.onBackPressed();
    }

    @Override
    protected void onResume() {
        super.onResume();
        web.onResume();
        // What a timed-out or stopped service left is taken up again whenever the app is in view.
        DownloadService.start(this);
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

    @SuppressWarnings("deprecation")
    private void bars(boolean shown) {
        if (Build.VERSION.SDK_INT < 30) {
            getWindow().getDecorView().setSystemUiVisibility(shown ? 0
                : View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
            return;
        }
        WindowInsetsController insets = getWindow().getInsetsController();
        if (insets == null) return;
        if (shown) {
            insets.show(WindowInsets.Type.systemBars());
        } else {
            insets.hide(WindowInsets.Type.systemBars());
            insets.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        }
    }

    private void openOutside(Uri address) {
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, address));
        } catch (ActivityNotFoundException nothing) {
            // No browser on the phone: the link stays unopened.
        }
    }

    /** Whether the page showing is the site's: the bridge answers nothing else, the sign-in's pages among them. */
    boolean onSite() {
        return server.owns(showing);
    }

    private final class Client extends WebViewClient {
        @Override
        public void onPageStarted(WebView view, String url, android.graphics.Bitmap favicon) {
            showing = Uri.parse(url);
        }

        @Override
        public void doUpdateVisitedHistory(WebView view, String url, boolean reload) {
            showing = Uri.parse(url);
        }

        /** Pages load here, the site's and its sign-in's; anything that is not a web page goes to the phone. */
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            String scheme = request.getUrl().getScheme();
            if ("https".equals(scheme) || server.owns(request.getUrl())) return false;
            openOutside(request.getUrl());
            return true;
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            return tracks.answer(request);
        }

        /**
         * A page of the site that fails is tried twice more, quietly, before the app says it cannot reach
         * the site: on a cold start with no connection, the first loads fail before the site's service
         * worker, which answers every page with no connection, is running, which takes a second or two.
         */
        @Override
        public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
            if (!request.isForMainFrame()) return;
            boolean quietly = server.owns(request.getUrl()) && failures < QUIET_TRIES;
            failures++;
            unreachable(view, request.getUrl(), quietly);
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            if (server.owns(Uri.parse(url))) failures = 0;
        }

        @Override
        public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
            if (request.isForMainFrame() && server.owns(request.getUrl()) && response.getStatusCode() >= 500) unreachable(view, request.getUrl(), false);
        }

        private void unreachable(WebView view, Uri failed, boolean quietly) {
            String again = server.owns(failed) ? failed.toString() : server.start();
            view.loadUrl(UNREACHABLE + "?host=" + Uri.encode(server.host()) + "&to=" + Uri.encode(again)
                + (quietly ? "&quiet=1&after=" + QUIET_AFTER_MS : ""));
        }
    }

    private final class Chrome extends WebChromeClient {
        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            if (fullScreen != null) {
                callback.onCustomViewHidden();
                return;
            }
            fullScreen = view;
            leaveFullScreen = callback;
            frame.addView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
            bars(false);
        }

        @Override
        public void onHideCustomView() {
            if (fullScreen == null) return;
            frame.removeView(fullScreen);
            fullScreen = null;
            leaveFullScreen = null;
            bars(true);
        }

        /** A link that asks for a new window opens in the phone's browser. */
        @Override
        public boolean onCreateWindow(WebView view, boolean dialog, boolean gesture, Message result) {
            WebView probe = new WebView(view.getContext());
            probe.setWebViewClient(new WebViewClient() {
                @Override
                public boolean shouldOverrideUrlLoading(WebView v, WebResourceRequest request) {
                    openOutside(request.getUrl());
                    probe.destroy();
                    return true;
                }
            });
            ((WebView.WebViewTransport) result.obj).setWebView(probe);
            result.sendToTarget();
            return true;
        }
    }
}

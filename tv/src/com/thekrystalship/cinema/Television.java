package com.thekrystalship.cinema;

import android.content.Context;
import android.provider.Settings;
import android.webkit.JavascriptInterface;

/**
 * What the page may ask of the television it runs on, as {@code window.KrystalCinemaTV}: its name,
 * and nothing else.
 *
 * The name is the one the TV's owner gave it in the system's settings (Fire TV's My Fire TV, Android
 * TV's About), which the site offers when it signs the TV in so a person can tell their TVs apart. It
 * is read each time it is asked for, so a rename on the TV is seen without restarting the app.
 */
final class Television {
    private final Context context;

    Television(Context context) {
        this.context = context;
    }

    /** The TV's name as its owner set it, or null when it has none. */
    @JavascriptInterface
    public String deviceName() {
        String name = Settings.Global.getString(context.getContentResolver(), Settings.Global.DEVICE_NAME);
        if (name == null) return null;
        name = name.trim();
        return name.isEmpty() ? null : name;
    }
}

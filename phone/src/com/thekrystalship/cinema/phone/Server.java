package com.thekrystalship.cinema.phone;

import android.content.Intent;
import android.net.Uri;

/**
 * The one site this app shows: its origin, which everything that needs it asks for here.
 *
 * A build carries its origin fixed ({@link Built#ORIGIN}), and then never asks for one and offers no
 * way to change it. A debug build also takes a whole address from the launch's {@code url} extra,
 * which is how it reaches a scratch site on the development machine.
 */
final class Server {
    private final Uri start;

    private Server(Uri start) {
        this.start = start;
    }

    /** The site this launch shows. */
    static Server of(Intent launch) {
        String asked = Built.DEBUG && launch != null ? launch.getStringExtra("url") : null;
        return new Server(Uri.parse(asked != null ? asked : Built.ORIGIN + "/"));
    }

    /** The address the app opens on. */
    String start() {
        return start.toString();
    }

    /** The site's origin: scheme, host and any port, with no path. */
    String origin() {
        return start.getScheme() + "://" + start.getEncodedAuthority();
    }

    /** The site's host, as a person reads it. */
    String host() {
        return start.getAuthority();
    }

    /** Whether an address is on this site: the same scheme, host and port. */
    boolean owns(Uri address) {
        return address != null
            && equal(start.getScheme(), address.getScheme())
            && equal(start.getHost(), address.getHost())
            && port(start) == port(address);
    }

    private static int port(Uri address) {
        if (address.getPort() >= 0) return address.getPort();
        return "https".equals(address.getScheme()) ? 443 : "http".equals(address.getScheme()) ? 80 : -1;
    }

    private static boolean equal(String a, String b) {
        return a != null && a.equalsIgnoreCase(b);
    }
}

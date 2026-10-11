# Krystal Cinema for Android TV and Fire TV

The site in an app a television launches from its home screen and drives with the remote. It is a
single Java activity holding a full-screen WebView (`src/com/thekrystalship/cinema/`), with no
dependencies. Everything a person sees is the site's TV design (`web/site/README.md`, "On a
television"); the app adds five things.

- **The TV mark.** `KrystalCinemaTV/<version>` is appended to the WebView's user agent, which is how
  the site knows to draw its TV design.
- **Back and Menu.** A page never receives either key, so the activity hands them over when they are
  let go, as `window.krystalTv.back()` and `window.krystalTv.menu()`; a Back the page did not use
  finishes the activity. Fire OS's keyboard takes Back for itself while it is up.
- **The TV's name.** `window.KrystalCinemaTV.deviceName()` answers the name the TV's owner gave it
  in the system's settings (`Settings.Global.DEVICE_NAME`; on a Fire TV, My Fire TV), trimmed, or
  `null` when it has none. It is read on every call, so a rename is seen at once. It is the one thing
  of the app's own the page can reach, and it asks nothing in return.
- **One origin.** The site's origin is the app's whole world: navigation to any other is refused,
  since a page off the site has no remote handling, and a TV's own browser, where it has one (Silk on
  Fire TV), is driven by an on-screen cursor rather than the remote's focus. `Server` holds the origin and everything that needs it
  asks there: what is loaded, the navigation rule, and the debug build's launch address. A site that
  cannot be reached, or answers a page load with a server error, is shown as `assets/unreachable.html`:
  its host and Retry, focused, which tries again on its own every fifteen seconds.
- **Storage.** DOM storage is on, so the session the site keeps in `localStorage` survives launches
  and updates of the app; uninstalling it or clearing its data is a new sign-in.

What the app does not do, because the WebView already does it: a playing film holds the screen on and
a paused one lets the screensaver come, and Home stops the film, since Fire OS stops a backgrounded
app's audio. Media plays without a gesture inside the app.

Nothing in the app is particular to one server. The client it signs in as, the provider and every
page come from the site its origin names; the app asks for `INTERNET` alone and never installs
anything. A build fixes its origin and offers no way to change it; nothing else in the app assumes
the origin is known before `Server` is asked for it, which is what a build that asks a person for its
server would change (`docs/tv-mode-plan.md`, section 6).

## Building

```bash
tv/build.sh                                   # release, for https://movies.thekrystalship.com
tv/build.sh --origin https://movies.example.com   # release, for another host
tv/build.sh --debug --install --open http://127.0.0.1:8299/   # debug build, opened on a scratch site
```

`build.sh` runs the SDK's own tools, `javac`, `d8`, `aapt2`, `zipalign` and `apksigner`, from
`JAVA_HOME` (`~/Android/jdk`) and `ANDROID_HOME` (`~/Android/Sdk`, with `build-tools;35.0.0`,
`platforms;android-35` and `platform-tools`); there is no Gradle. It writes the origin, the version and
whether the build is a debug one into a generated `Built` class, and the APK to `tv/out/`.

- **The version** is the newest heading in `tv/CHANGELOG.md`, tagged `tv-v<version>`, a clock of its
  own that `release.yml`, which fires on `v*`, never sees. `versionCode` is major×10000 + minor×100 +
  patch.
- **A release** takes an https origin, speaks https alone (`res/xml/network_security_config.xml`),
  and is signed with the key in `~/.config/krystal-cinema-tv/` (`release.keystore`, its password in
  `release.password`). That key is never in a repository and has to be kept: Android installs an
  update only under the key the app was installed with, so a TV whose app was signed with a lost key
  uninstalls it and signs in again. `build.sh` prints how to make one when there is none.
- **A debug build** is `com.thekrystalship.cinema.debug`, named Krystal Cinema Debug, installed beside
  a release one. It has remote debugging on, allows plain http to `127.0.0.1` and `localhost` (the
  `debug/res/` overlay), takes its start address from the launch's `url` extra, and is signed with a
  debug key made on first use. With `adb reverse` for the scratch site's ports, it opens that site on
  the TV and its pages are driven over the DevTools protocol (`webview_devtools_remote_<pid>`).

The launcher banner and icon (`res/drawable-*/banner.png`, `res/mipmap-*/icon.png`) are the TKS mark
and the wordmark on the canvas colour, at the xhdpi and xxxhdpi densities.

## Getting it onto a television

- **From this machine**: with the TV's developer options on and ADB debugging allowed,
  `adb connect <tv>:5555`, then `tv/build.sh --install`.
- **Anybody else**: the site serves the build made for it at `/tv.apk` (`docs/api-contract.md`). On a
  Fire TV, the Downloader app from the Amazon Appstore fetches it from that address, after Settings →
  My Fire TV → Developer options allows Downloader to install unknown apps. A TV does not update an
  app installed this way; fetching the same address again does.

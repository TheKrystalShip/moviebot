# Krystal Cinema for Android phones

The site in an app, for one thing a browser cannot do: download films that keep downloading with the
screen off and the app out of view. A browser runs a download only while it runs the page, and
Android freezes a page in the background within a minute or two; Chrome's Background Fetch stops
under Doze once Chrome is out of view, and loses what it fetched when a credential runs out. A
foreground service is what Android keeps on the network through Doze, so the app runs one.

It is a single Java activity holding a WebView (`src/com/thekrystalship/cinema/phone/`), with no
dependencies. Everything a person sees is the site; the app adds these.

- **Downloads the app runs** (`DownloadService`, `Downloads`). Download in the site hands a save's
  tracks to the app with the save's grant (`docs/api-contract.md`, Save offline), which opens them
  with no access token for as long as the save holds its title. A foreground service of the data
  transfer kind fetches them one save at a time, one track after another, into the app's own files
  (`saves/<saveId>/`), holding a partial wake lock and a Wi-Fi lock while it does. A track resumes
  from its length on disk with `Range` and `If-Range`; a connection that breaks or goes quiet for 30
  seconds is tried again after a pause growing to a minute, which a network coming back ends at
  once. A save whose every track is whole is told to the API on its grant, with no page running.
  One notification says which save is downloading, how far it has got and how many wait behind it,
  and each save that becomes whole gets one of its own, "Downloaded", that stays after the service
  goes.
  Android stops a data transfer service after six hours in a day; what is left carries on the next
  time the app is opened, as anything the service could not finish does.
- **The bridge** (`AppBridge`), `window.KrystalCinemaApp`: `download`, `status`, `statuses`,
  `pause`, `resume`, `remove` and `trackUrl`, each answering only while the site's own page is
  showing, since a WebView puts the bridge on every page it loads, the sign-in's among them. The
  site's half is `web/site/src/offline/app.js` (`web/site/src/offline/CLAUDE.md`).
- **The downloaded tracks, served to the page** (`LocalTracks`) at
  `<origin>/__app/saves/<saveId>/<rendition>.mp4?start=&end=`, once whole, a byte range at a time. The
  range is named in the query, never a `Range` header, because WebView applies a `Range` header
  itself to the stream an app answers with.
- **The site's icon**, Lucide's clapperboard on the PWA icon's ground (`web/site/public/icons/icon.svg`),
  as an adaptive vector icon with a themed variant.
- **The app mark.** `KrystalCinemaApp/<version>` is appended to the WebView's user agent, and the
  site names the device "Krystal Cinema on Android" on the API's records.
- **Every page loads in the app**, the sign-in's included, so the session the site keeps is the
  app's; a link that opens a new window goes to the phone's browser. A film put full screen fills
  the screen with the system bars hidden; otherwise the page is kept clear of the bars, the display
  cutout and the keyboard. Back goes back a page, and leaves from the first.
- **No connection.** The site's service worker answers every page, and the player plays what the
  app downloaded. A page of the site that fails to load is tried twice more, 1.5 seconds apart and
  drawing nothing, before `assets/unreachable.html` says the site cannot be reached and offers
  Retry: on a cold start, the service worker takes a second or two to come up.

## Building

```bash
phone/build.sh                                     # release, for https://movies.thekrystalship.com
phone/build.sh --origin https://movies.example.com # release, for another host
phone/build.sh --debug --install --open            # debug build, installed and opened on the phone adb reaches
```

`build.sh` runs the SDK's own tools from `JAVA_HOME` (`~/Android/jdk`) and `ANDROID_HOME`
(`~/Android/Sdk`, with `build-tools;35.0.0`, `platforms;android-35` and `platform-tools`), as
`tv/build.sh` does: the resources are linked first, writing `R.java`, then the code is compiled to
Java 8 bytecode against `android.jar` alone, which has no lambdas, and dexed. The APK lands in
`phone/out/`.

- **The version** is the newest heading in `phone/CHANGELOG.md`, tagged `phone-v<version>`, a clock
  of its own. `versionCode` is major×10000 + minor×100 + patch.
- **A release** takes an https origin, speaks https alone, and is signed with the key in
  `~/.config/krystal-cinema-phone/` (`release.keystore`, its password in `release.password`). That
  key is never in a repository and has to be kept: Android installs an update only under the key the
  app was installed with. `build.sh` prints how to make one when there is none.
- **A debug build** is `com.thekrystalship.cinema.phone.debug`, named Krystal Cinema Debug,
  installed beside a release one, with WebView debugging on (`webview_devtools_remote_<pid>`) and
  plain http allowed to `127.0.0.1` and `localhost`; it takes its start address from the launch's
  `url` extra. Its downloads are its own files, read with `adb shell run-as
  com.thekrystalship.cinema.phone.debug`.

## Getting it onto a phone

- **From this machine**: with USB debugging allowed, `phone/build.sh --install`.
- **Anybody else**: the site serves the build made for it at `/phone.apk` (`docs/api-contract.md`).
  Opening that address on the phone downloads it; installing asks once to allow the browser to
  install apps. A phone does not update an app installed this way; fetching the same address again
  does.

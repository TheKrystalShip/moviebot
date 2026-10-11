# TV mode for Krystal Cinema

A plan, not canon. What it settles moves into the docs beside the code it governs
(`web/site/README.md`, `web/site/src/player/CLAUDE.md`, the wrapper's own README under `tv/`) when
it lands, and its section here is struck. When the last step lands, this file is deleted.

Each step below is taken through a deep dive before it is built: the questions listed under it are
answered, by measurement where they can be, and the answers are written into the step before code is
written.

## 1. What this is for

Krystal Cinema works on an Android TV or Fire TV: driven entirely by the remote, readable from across
a room, signed in without typing a password, and playing films in the shared room like any other
viewer. Apple TV is out of scope.

The site runs inside a thin Android app, the wrapper, which shows it in a WebView. A TV's own browser
is not a target: Silk, the Fire TV browser, turns the D-pad into an on-screen cursor and never hands
the page the keys.

The reference device is the user's Xiaomi TV running Fire TV, reached over the LAN with `adb`
(`~/Android/Sdk/platform-tools/adb connect 192.168.1.23:5555`, already authorised for hotrod):

| | Measured |
|---|---|
| Model | Amazon `AFTBR92D74` (`ginzaeu`), Fire OS 8.1.8.2, Android 11 (API 30), leanback only |
| WebView | `com.amazon.webview.chromium` 148 |
| Screen | 3840×2160 panel; a page sees 960×540 at a pixel ratio of 4 |
| What a page is told | `(pointer: coarse)` and `(hover: hover)` both match, `(any-pointer: fine)` matches, no `navigator.mediaSession`, `document.fullscreenEnabled` false, the user agent says `Mobile Safari` and `wv` |
| Frame rate | The WebView paces `requestAnimationFrame` at about 50 a second on any page, a blank one included, on a 60 Hz display |
| Hardware | MediaTek `merak`, four Cortex-A55 cores, 32-bit userland; 1.8 GB of memory with about 500 MB available |
| Decoding | `c2.mtk.avc.decoder` in hardware for H.264; 1080p plays through hls.js with no frames dropped |
| Screensaver | On, after five minutes |

## 2. Decisions

| Decision | Outcome |
|---|---|
| Chromecast | Rejected. The Web Sender works only in Chrome on desktop and Android, and keeping the room's shared timeline needs a custom receiver |
| How the TV runs the site | A WebView wrapper app around the site, not a TV browser and not a Trusted Web Activity (Fire TV has no Chrome to host one) |
| Spatial navigation | Our own, in the site, reading geometry from the DOM. Norigin is rejected because it has to wrap every focusable element in its hooks, which ties krystal-ui to it |
| Holding Left or Right in the player | Scrubs with the preview, and moves the room once, when the key is let go |
| Sign-in | RFC 8628, served by tks-auth (`tks-auth/src/Auth.Anchor/CLAUDE.md`, "The device grant"): a code and a QR on the TV, approved on a phone; signing out is RFC 7009 revocation |
| One person per TV | A TV is signed in as one person. Changing person is signing out and in again; there is no profile picker |
| Typing on the TV | The system keyboard, which OK on a focused field in the WebView raises. Fire OS's keyboard carries its own QR code for typing from a phone and takes voice from the remote's microphone, at no cost to us |
| The TV's layout | A design of its own, the way the phone has its tab bar instead of the sidebar, drawn at the TV's page size on the canvas https://claude.ai/artifact/BeZQYibgdZx37EYSvFxz16. The desktop layout at 960×540 is not the base; the sidebar and the footer are redesigned for the TV, and how focus moves through them is settled with that design rather than tuned before it |
| Getting around on the TV | A top bar: the mark, then Home, Discover, Movies, TV Shows and Saved, then the bell and the avatar. It scrolls away with the page; Back, or Up from the top of the page, brings focus to it. A side rail is rejected: it costs width on a 960-pixel page and is a second area for focus to sit in |
| Home on the TV | The top of the page is a spotlight on whatever has focus: its artwork, name, facts and two lines of overview. It arrives on the newest film with Watch focused. Rows run off the right edge, the focused row keeps one line on screen, and cards carry only their artwork |
| A row's way to everything | A See all tile ends the row; a row's heading never takes focus |
| Places off the bar | About is in Profile; Notify me folds into Profile's Requests |
| Leaving soon | Not a page on any surface. The library clock runs internally and nobody sees it, since Discover reaches any film and it plays within minutes |
| The primary button | Dark text on the accent, which holds contrast from a sofa |
| How focus looks | A lift with a hairline: the focused card grows by about 15% and casts a deep shadow, a 1 px white edge sits on it with a faint glow of the accent, and its neighbours dim. A focused button or tab becomes a white pill with dark text, with the same shadow and glow. No outline and no underline |
| The phone as a remote | Its own plan, `docs/remote-mode-plan.md` |
| Where the wrapper lives | `moviebot/tv/`, its own version clock, tagged `tv-v<version>`. That prefix keeps it out of `release.yml`, which fires on `v*` |
| Building the wrapper | On hotrod: a JDK and Android's command-line tools under the user's home, no Android Studio and no Gradle. Installed on the TV with `adb` over the LAN once developer options are on, or fetched from the site with Downloader |

## 3. What stands

These exist and are not restated here:

| Piece | Authority |
|---|---|
| TV mode: detection, the navigator, Back and Menu as `window.krystalTv`, the keys a page receives, what is left out | `web/site/README.md` "On a television", `web/site/src/tv/` |
| The player on a remote: reveal first, gathered skips, the scrub bar taking focus, holding to scrub, Back and Menu | `web/site/src/player/CLAUDE.md`, `web/site/src/player/engine/media/remote.ts` |
| The TV's own pages: the bar, spotlit rows, a film's and a show's page, Discover, Movies and TV Shows by genre, Saved, Profile, the player's panel; the focus look | `web/site/README.md` "On a television", `web/site/src/tv/`; krystal-ui `TvBar`, `TvSpotlight`, `TvRow`, `TvCard` and `styles/tv.css`, its README "On a television" |
| Discover by person and by genre, on every surface | `docs/api-contract.md`, `web/site/README.md` |
| `npm run verify:tv` | `web/site/README.md` "Verifying" |
| The device grant: `/device_authorization`, polling `/token`, the `/device` approval page, device sessions surviving the phone's sign-out; `/revoke` ending a session | `tks-auth/src/Auth.Anchor/CLAUDE.md` |
| The site's session: `oidc-client-ts`, tokens in `localStorage` under `cinema.oidc.`, `restore`, `freshToken`, `signOut` | `web/site/src/session.js`, `web/site/README.md` "Signing in" |
| The TV signing in: the `cinema-tv` client named on `/api/config`, the code screen and its QR, each answer's handling, the code over a page whose session ended, signing out by revocation | `web/site/README.md` "Signing in", `web/site/src/tv/TvSignIn.jsx`, `docs/deploying.md` |
| The API accepting any token whose audience is the Cinema application, whatever client asked for it | `src/MovieBot.Api/Auth/CinemaAuthentication.cs` |
| Media: HLS fMP4, H.264 High at level 4.2 with stereo AAC-LC, WebVTT drawn by the page, `MediaSource.isTypeSupported` asked before anything is fetched | `web/site/README.md` "A viewer who cannot play the film" |
| The app: one activity on the site's origin, the TV mark, Back and Menu, other origins refused, the unreachable page, its build, signing, version clock and debug build; `/tv.apk` and Downloader | `tv/README.md`, `docs/api-contract.md`, `deploy/CLAUDE.md`, `docs/deploying.md` |

## 4. Steps

Every step is built (section 3): step 1, knowing it is a TV, moving focus, showing focus and the
remote's keys; step 2, the TV's own design; step 3, signing the TV in; step 4, the app. What step 2
leaves open is under it, and this plan stands until that is settled.

### Step 2: what the TV's design leaves open

The design is the canvas section 2 links, and the site draws it (section 3). Measured on the Xiaomi
through the app's debug build, from the page's long-animation-frame records:

- Moving along a row has no frame over 50 ms; moving between rows, 81 ms at worst.
- The two moves that change the whole screen, from Home's opening film into its rows and Back up into
  the bar, take a frame of 64 to 72 ms for the press, about 40 ms of it script with 23 ms of forced
  style and layout; the frame after it takes 70 to 100 ms with the main thread idle, the compositor
  rastering a page drawn at 3840 by 2160, so the next press is taken at once. Settled: cards change
  opacity without a transition and are skipped off the screen (krystal-ui 0.17.2). Skipping whole
  rows made the frame after slower, and the stylesheet's size costs nothing measurable.
- Profile draws the site's own panels inside the TV's frame. Requests is the desktop's cards rather
  than the rows the design draws.

The pages, as designed and built:

- **Home**: the top bar; a spotlight on whatever has focus, arriving on the newest film with Watch
  focused; Continue watching, Watching now, Watch later, Recently added, Movies and TV Shows, the
  last three ending in See all.
- **A film**: no top bar, Back returns. The artwork fills the page; Resume (Watch when unstarted),
  Restart and Save; under them the progress bar and `1:36:10 of 2:35:00 - (59m remaining)`. Below,
  the cast as focusable round photos with their characters, then the details and the TMDB credit.
- **A show**: the same header with Resume at the next episode, Follow and Save; season pills; the
  season's episodes as stills with number, name and length. With an episode focused, the spotlight
  is that episode: its season and number, name, length, air date, overview and time left.
- **Discover**: the search field focused on arrival, OK raising the system keyboard; popular films
  and shows below. With a query: the best match as a large card with Watch, More info and Save, then
  the rest of what the catalogue answered.
- **Movies and TV Shows**: Home's layout, one row per genre from the library, each ending in a
  Discover more tile that searches the catalogue for that genre.
- **Saved**: Watch later as a row, the focused title's name and facts under it; the lists as cards.
- **Profile**: no top bar; its sections down the left (History, Requests, Following, Playback,
  Subtitles, Appearance, Account, About) and the open one on the right. Requests holds what this
  person waits on (Notify me) with Stop waiting, and the downloads they asked for.
- **The player**: the title and who is watching at the top; the scrub bar with elapsed and
  remaining time; Play or Pause, back and forward ten seconds, then Subtitles, Audio and Quality
  with their values. Subtitles at 26 pixels. Menu opens the subtitles as a panel from the right,
  with Size and Background under the languages.
- Left out of TV mode beyond what already is: push notifications (and with them Profile's
  Notifications), installing the app, editing lists.

OK on a cast member opens Discover on everything they are credited on, and a genre row's Discover more
the catalogue's titles in that genre, on every surface, from the API's catalogue browse.

TV mode draws no footer. The TMDB credit, the one thing in it that has to stay, is on About and at the
foot of every film and show page.

The Xiaomi shows the whole picture: a pattern of 1% bands drawn to the page's edge is visible in full
on every side. The 5% margin stays regardless, for TVs that do crop and because a sofa reads a page
better with room around it.

Not drawn in TV mode, for cost: `backdrop-filter` anywhere, the page fade animations.

## 5. Testing

- **`npm run verify:tv`** in a desktop Chromium at 960×540 (`web/site/README.md` "Verifying").
- **The Xiaomi** for what only a device shows: the keys the remote really sends, the frame rate,
  decoding, the screensaver, and the QR scanned by a real phone.

The Xiaomi runs the app's debug build (`tv/README.md`) on the scratch site, through `adb reverse`, and
its pages are driven over the DevTools protocol by `~/Android/tvprobe/drive.mjs`, kept outside any
repository beside the toolchain (`~/Android/jdk`, a JDK 21, and `~/Android/Sdk` with `platform-tools`,
`build-tools;35.0.0` and `platforms;android-35`). Each launch of the app is a new process, and so a new
`webview_devtools_remote_<pid>` for the forward on :9333.

```bash
A=~/Android/Sdk/platform-tools/adb
web/site/verify/mobile/scratch.sh                        # the scratch site, with MOVIEBOT_VERIFY_TMDB_TOKEN_FILE
$A reverse tcp:8299 tcp:8299; $A reverse tcp:8300 tcp:8300
tv/build.sh --debug --install --open http://127.0.0.1:8299/
$A forward tcp:9333 localabstract:webview_devtools_remote_$($A shell pidof com.thekrystalship.cinema.debug)
node ~/Android/tvprobe/drive.mjs stage                   # stage the scratch site (keeps the staged rooms open while it runs)
node ~/Android/tvprobe/drive.mjs plant http://127.0.0.1:9333   # sign the TV in as the staged viewer
node ~/Android/tvprobe/drive.mjs walk http://127.0.0.1:9333 DPAD_DOWN DPAD_RIGHT   # press keys, log focus
$A exec-out screencap -p > shot.png                      # what the TV shows
```

What a page holds on the device is read from Android itself: `dumpsys power` for the screen lock a
playing film takes, `dumpsys dreams` for the screensaver, and `settings put system screen_off_timeout`
to bring the screensaver closer for a test, put back afterwards.

The scratch library is `moviebot/media/`, which holds every film's manifest, playlists, artwork and
subtitles copied from hotbox without their segments, so pages draw; `coherence-2013` also holds its
first five minutes, which the player checks play.

## 6. Follow-up, not in this plan

**The app in a TV's own store.** Decided against for now: the friends a cinema is shared with get the
same app from `/tv.apk` with Downloader, at the cost of a one-time setting per TV and updates fetched
by hand, which the thin app rarely needs. The door is kept open by the app (`tv/README.md`) holding
its origin as a value one class answers, and nothing in it being particular to one server. A store's
version is then the same source built without `--origin`. A store signs what it distributes with a
key of its own, so a sideloaded install and a store's do not update each other even under the same
app id: moving a TV between them is uninstalling, and signing in again. It needs:

- A first screen asking for the server's address, taken with the system keyboard, accepted only once
  `<origin>/api/config` answers as Krystal Cinema, and kept; and a way to change it.
- A demo server for the store's reviewers holding only films that may be shown, with an account the
  provider has approved, since a reviewer signs in.
- For the Amazon Appstore (Fire TV): a free account; it takes an APK, so the build chain stands. New
  Fire TV devices on Vega OS run no Android app, so a listing reaches the Fire OS ones.
- For Google Play (Android TV, Google TV): the one-off developer fee, a closed test with testers
  before production for a new personal account, an Android App Bundle (`bundletool` with `aapt2`'s
  proto output, no Gradle needed), a target SDK raised yearly, the TV quality review, a privacy policy
  and the data safety form.
- Both: the EU trader declaration, which publishes a trader's name and contact details.

These requirements are as understood when this was written and are checked against each store's
current rules before anything is submitted.

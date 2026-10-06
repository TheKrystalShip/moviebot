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
| Sign-in | RFC 8628, served by tks-auth 4.5.0 (`tks-auth/src/Auth.Anchor/CLAUDE.md`, "The device grant"): a code and a QR on the TV, approved on a phone |
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
| The phone channel | Out of scope; see section 6 |
| Where the wrapper lives | `moviebot/tv/`, its own version clock, tagged `tv-v<version>`. That prefix keeps it out of `release.yml`, which fires on `v*` |
| Building the wrapper | On hotrod: a JDK and Android's command-line tools under the user's home, no Android Studio. Installed on the TV with `adb` over the LAN once developer options are on |

## 3. What stands

These exist and are not restated here:

| Piece | Authority |
|---|---|
| TV mode: detection, the navigator, Back and Menu as `window.krystalTv`, the keys a page receives, what is left out | `web/site/README.md` "On a television", `web/site/src/tv/` |
| The player on a remote: reveal first, gathered skips, the scrub bar taking focus, holding to scrub, Back and Menu | `web/site/src/player/CLAUDE.md`, `web/site/src/player/engine/media/remote.ts` |
| The focus ring on a television and `Rail` without arrows | krystal-ui `styles/tv.css`, its README "On a television" |
| `npm run verify:tv` | `web/site/README.md` "Verifying" |
| The device grant: `/device_authorization`, polling `/token`, the `/device` approval page, device sessions surviving the phone's sign-out | `tks-auth/src/Auth.Anchor/CLAUDE.md` |
| The site's session: `oidc-client-ts`, tokens in `localStorage` under `cinema.oidc.`, `restore`, `freshToken`, `signOut` | `web/site/src/session.js`, `web/site/README.md` "Signing in" |
| The API accepting any token whose audience is the Cinema application, whatever client asked for it | `src/MovieBot.Api/Auth/CinemaAuthentication.cs` |
| Media: HLS fMP4, H.264 High at level 4.2 with stereo AAC-LC, WebVTT drawn by the page, `MediaSource.isTypeSupported` asked before anything is fetched | `web/site/README.md` "A viewer who cannot play the film" |

What does not exist: the TV's design, a client registered for the TV and its sign-in screen, and the
wrapper.

## 4. Steps

Step 1, knowing it is a TV, moving focus, showing focus and the remote's keys, is built (section 3).
The navigator works from whatever is on screen, so it carries over to the TV's design; what the design
changes is where things are, and `verify:tv` says whether every control is still reached.

### Step 2: the TV design, and building it

The TV gets its own design in the Krystal Cinema design project, beside the desktop and phone ones,
and the site draws it in TV mode the way it draws the phone's tab bar on a phone. The design is made
first and the build follows it.

What the design has to answer for, from what the Xiaomi showed:

- A 960×540 page seen from a sofa: the desktop layout there spends a quarter of the width on the
  sidebar, puts the first row of films below the fold under the featured film, and draws controls
  sized for a mouse.
- The footer's place, and how focus enters and leaves the top bar on every page that is not Home.
- One row to a line on every page, so Right at the end of a row has nothing to land on but the row's
  own See all tile.
- The focused element as the one thing the eye follows: the lift with a hairline (section 2) in
  place of the outline and halo `krystal-ui`'s `styles/tv.css` draws, and how a row moves under it.
  A focused card is scrolled to the start of its row.
- Every page that stays in TV mode, the player's controls among them, and subtitles sized for the
  distance.
- Left out of TV mode beyond what already is: push notifications, installing the app, editing lists.
- A margin for TVs that crop their edges, if the Xiaomi crops.

What the build has to keep, measured:

- The WebView paces frames at about 50 a second on any page. Scrolling a row of the current site
  holds that with its worst frame at 20 ms; scrolling the page and the idle Home page have frames of
  50 to 100 ms, which the build profiles and removes.
- Not drawn in TV mode, for cost: `backdrop-filter` (seven places across `site.css` and `player.css`),
  the blurred artwork behind a search hit, the page fade animations.
- Artwork is already sized for the screen (posters 500 to 600 wide, backdrops 1280, drawn at up to
  390 CSS pixels); poster backgrounds drawn through CSS load eagerly.
- At 960 wide the current Profile overflows sideways: its tab strip runs off the right edge and the
  column grows a horizontal scrollbar.

Done when: every page reads from a sofa and scrolls without stutter on the Xiaomi, and `verify:tv`
passes.

### Step 3: signing the TV in

- Register the TV's client: `tks-auth app client add <cinema application> --client-id cinema-tv
  --device`. `/api/config` names it beside the site's client id: `AuthOptions` gains it, declared
  with its default in `src/moviebot.settings.json`, and `packaging/` and `docs/deploying.md` name it,
  since a release carries it.
- On a TV with no session, the site shows the sign-in screen instead of going to the provider: the
  user code large, `verification_uri` as text, a QR code of `verification_uri_complete`, and the
  remaining time. It polls `/token` at the interval it was given, adding five seconds on `slow_down`,
  and starts over with a new code on `expired_token`.
- Approval hands the tokens to `session.js` as its session, so `freshToken`, the refresh grant and
  every caller work unchanged. `oidc-client-ts` has no device grant, so the request and the polling
  are the site's own.
- Signing out on the TV forgets the session here and revokes its refresh token.

Deep dive:

- How `session.js` takes a session it did not create: building an `oidc-client-ts` `User` and storing
  it, or a TV session store behind the same functions.
- Which QR library, its size in the bundle, and whether it is loaded only in TV mode.
- How long a device session lasts unused, under tks-auth's refresh window, and what the TV shows when
  it has run out.
- What the screen shows when the provider cannot be reached, and when the person refuses.

Done when: a fresh TV signs in by scanning the QR with a phone, survives the phone signing out and the
app being restarted, and renews on its own.

### Step 4: the wrapper app

`moviebot/tv/`: one activity holding a full-screen WebView on `https://movies.thekrystalship.com`.

- Leanback launcher entry and banner, `android.software.leanback` not required, no touchscreen
  required, so it installs and launches on Android TV and Fire TV alike.
- `KrystalCinemaTV/<version>` appended to the WebView's user agent, which turns TV mode on.
- Back handed to the page as `window.krystalTv.back()` from `onBackPressed`, and the app closed only
  when it answers false; Menu handed over as `window.krystalTv.menu()` from `dispatchKeyEvent`, since
  the WebView declines it. Fire OS's keyboard takes Back for itself while it is up.
- Media playback allowed without a gesture inside the app, and the screen kept on while a film plays,
  through a small bridge the player calls.
- Signed with a key kept outside the repository; built by a script on hotrod; installed with `adb`.

The probe app (section 5) already does the user agent, Back and Menu, and keeps the screen on while it
is open; the wrapper is that, with the bridge, the launcher entry and a release build.

Deep dive:

- The bridge between the page and the app: what it exposes (keeping the screen on while a film
  plays), and that only the site's own origin can reach it.
- What the app does with a link out of the site (the provider's pages, TMDB's credit).
- Whether the app is written in Kotlin with a Gradle build or kept to the plain `javac`, `d8`,
  `aapt2` and `apksigner` chain the probe uses, which needs nothing more on hotrod.
- How the friend's TV gets it: the Downloader app and an address to fetch it from.

Done when: the app installs from hotrod onto the Xiaomi, launches from the Fire TV home screen, and
steps 1 to 3 hold there.

## 5. Testing

- **`npm run verify:tv`** in a desktop Chromium at 960×540 (`web/site/README.md` "Verifying").
- **The Xiaomi** for what only a device shows: the keys the remote really sends, the frame rate,
  decoding, the screensaver, and the QR scanned by a real phone.

Until the wrapper exists, the Xiaomi is reached through a probe app kept outside any repository, in
`~/Android/tvprobe` beside the toolchain (`~/Android/jdk`, a JDK 21, and `~/Android/Sdk` with
`platform-tools`, `build-tools;35.0.0` and `platforms;android-35`). It is one Java activity holding a
full-screen WebView with remote debugging on, the TV mark in its user agent, Back and Menu handed to
the page, and the screen kept awake; it logs every key it sees, every console line and what Back and
Menu answered under the logcat tag `tvprobe`, and opens the address in its `url` extra.

```bash
web/site/verify/mobile/scratch.sh                        # the scratch site, with MOVIEBOT_VERIFY_TMDB_TOKEN_FILE
~/Android/tvprobe/build.sh [url]                         # build, install, open; reverses 8299, 8300, 8765; devtools on :9333
node ~/Android/tvprobe/drive.mjs stage                   # stage the scratch site (keeps the staged rooms open while it runs)
node ~/Android/tvprobe/drive.mjs plant http://127.0.0.1:9333   # sign the TV in as the staged viewer
node ~/Android/tvprobe/drive.mjs walk http://127.0.0.1:9333 DPAD_DOWN DPAD_RIGHT   # press keys, log focus
~/Android/Sdk/platform-tools/adb exec-out screencap -p > shot.png                   # what the TV shows
```

The scratch library is `moviebot/media/`, which holds every film's manifest, playlists, artwork and
subtitles copied from hotbox without their segments, so pages draw; `coherence-2013` also holds its
first five minutes, which the player checks play.

## 6. Follow-up, not in this plan

**The phone channel.** Every open copy of the site joins the API's hub under its person, and a TV
announces itself as a screen. A phone signed in as the same person then gets Play on TV for a title,
a keyboard for a focused field on the TV (sending the field's whole text rather than keystrokes), and
joining the TV's room as a remote that plays no video. The room's QR on the TV lets people on the
sofa join the same way. Only the same person's sessions see each other, checked by the server on every
message.
